from __future__ import annotations

import json

import httpx
import pytest
from fastapi.testclient import TestClient
from openai import AsyncOpenAI

from app.modules.billing.plans import ReportTier
from app.modules.reports.generator import (
    GeneratedReport,
    OpenAIReportGenerator,
    ReportContext,
    ReportGenerationError,
    ReportGenerator,
)
from app.modules.reports.schemas import BasicReportLLM, DetailedReportLLM, ReportValidationError, \
    validate_and_normalise
from tests.conftest import INTERVIEW_BODY, auth_headers, build_app, make_settings, register


def cat(score=70, assessed=True):
    return {"assessed": assessed, "score": score, "rationale": "Because."}


def basic_payload(**overrides) -> dict:
    p = {"summary": "Solid answers overall.", "technical_knowledge": cat(80), "communication_clarity": cat(60),
         "answer_relevance": cat(70), "problem_solving": cat(50), "strengths": ["Clear", " "],
         "improvements": ["Add metrics"], "recommended_topics": ["Hash maps"]}
    p.update(overrides)
    return p


def detailed_payload() -> dict:
    return {**basic_payload(),
            "confidence_indicators": [{"observation": "Frequent hedging", "evidence": "'I think maybe'",
                                       "suggestion": "State conclusions first"}],
            "answer_feedback": [{"question": "Q1", "answer_summary": "A1", "assessment": "strong",
                                 "feedback": "Good", "improved_answer": "S/T/A/R"}],
            "practice_plan": [{"title": "Drill", "activity": "Practice", "duration_minutes": 1000}]}


# ---- validation ---------------------------------------------------------------------

def test_overall_score_computed_server_side():
    out = validate_and_normalise(BasicReportLLM(**basic_payload()))
    # 80*.35 + 60*.25 + 70*.2 + 50*.2 = 67
    assert out["overall_score"] == 67
    assert out["strengths"] == ["Clear"]  # blank entries removed


def test_unassessed_category_excluded_and_reweighted():
    out = validate_and_normalise(BasicReportLLM(**basic_payload(technical_knowledge=cat(0, assessed=False))))
    assert out["categories"]["technical_knowledge"]["score"] is None
    # (60*.25 + 70*.2 + 50*.2) / .65 = 60
    assert out["overall_score"] == 60


@pytest.mark.parametrize("bad", [cat(101), cat(-1)])
def test_out_of_range_scores_rejected(bad):
    with pytest.raises(ReportValidationError):
        validate_and_normalise(BasicReportLLM(**basic_payload(problem_solving=bad)))


def test_nothing_assessed_rejected():
    payload = basic_payload(**{k: cat(0, False) for k in ("technical_knowledge", "communication_clarity",
                                                          "answer_relevance", "problem_solving")})
    with pytest.raises(ReportValidationError):
        validate_and_normalise(BasicReportLLM(**payload))


def test_detailed_sections_are_clamped():
    out = validate_and_normalise(DetailedReportLLM(**detailed_payload()))
    assert out["practice_plan"][0]["duration_minutes"] == 240
    assert out["answer_feedback"][0]["assessment"] == "strong"


def test_llm_schema_is_strict():
    schema = DetailedReportLLM.model_json_schema()
    assert schema["additionalProperties"] is False


# ---- OpenAI generator against a mocked Responses API -------------------------------

def _responses_api_body(payload: dict, status: str = "completed") -> dict:
    return {
        "id": "resp_test", "object": "response", "created_at": 1760000000, "model": "gpt-6.1-sol",
        "status": status, "error": None, "incomplete_details": None, "instructions": None, "metadata": {},
        "parallel_tool_calls": True, "temperature": 1.0, "top_p": 1.0, "tool_choice": "auto", "tools": [],
        "output": [{"type": "message", "id": "msg_1", "status": "completed", "role": "assistant",
                    "content": [{"type": "output_text", "text": json.dumps(payload), "annotations": []}]}],
        "usage": {"input_tokens": 1200, "output_tokens": 600, "total_tokens": 1800,
                  "input_tokens_details": {"cached_tokens": 0}, "output_tokens_details": {"reasoning_tokens": 0}},
    }


def _ctx(tier=ReportTier.DETAILED) -> ReportContext:
    return ReportContext(session_id="s1", user_safety_id="abc", tier=tier, role_title="Software Engineer",
                         interview_type="technical", difficulty="beginner", experience_level="junior",
                         duration_minutes=10, transcript=[{"role": "interviewer", "text": "Q?", "truncated": False},
                                                          {"role": "candidate", "text": "A.", "truncated": False}])


async def test_openai_generator_parses_structured_output():
    seen: dict = {}

    def handler(request: httpx.Request) -> httpx.Response:
        seen["path"] = request.url.path
        seen["body"] = json.loads(request.content)
        return httpx.Response(200, json=_responses_api_body(detailed_payload()))

    client = AsyncOpenAI(api_key="sk-test", http_client=httpx.AsyncClient(transport=httpx.MockTransport(handler)))
    gen = OpenAIReportGenerator(make_settings(openai_api_key="sk-test"), client=client)
    result = await gen.generate(_ctx())
    assert isinstance(result.content, DetailedReportLLM)
    assert result.model == "gpt-6.1-sol"
    assert result.usage == {"input_tokens": 1200, "output_tokens": 600}
    assert seen["path"].endswith("/responses")
    body = seen["body"]
    assert body["store"] is False
    assert body["safety_identifier"] == "abc"
    assert body["text"]["format"]["type"] == "json_schema"
    assert body["text"]["format"]["strict"] is True
    assert "<<<TRANSCRIPT_START>>>" in body["input"]


async def test_openai_generator_uses_cheaper_model_for_basic_tier():
    seen: dict = {}

    def handler(request: httpx.Request) -> httpx.Response:
        seen["body"] = json.loads(request.content)
        return httpx.Response(200, json=_responses_api_body(basic_payload()))

    client = AsyncOpenAI(api_key="sk-test", http_client=httpx.AsyncClient(transport=httpx.MockTransport(handler)))
    result = await OpenAIReportGenerator(make_settings(openai_api_key="sk-test"), client=client) \
        .generate(_ctx(ReportTier.BASIC))
    assert seen["body"]["model"] == "gpt-6-luna"
    assert isinstance(result.content, BasicReportLLM)


async def test_openai_generator_maps_server_errors_to_retryable():
    client = AsyncOpenAI(api_key="sk-test", max_retries=0, http_client=httpx.AsyncClient(
        transport=httpx.MockTransport(lambda r: httpx.Response(503, json={"error": {"message": "busy"}}))))
    with pytest.raises(ReportGenerationError) as exc:
        await OpenAIReportGenerator(make_settings(openai_api_key="sk-test"), client=client).generate(_ctx())
    assert exc.value.retryable is True


async def test_openai_generator_incomplete_response():
    client = AsyncOpenAI(api_key="sk-test", http_client=httpx.AsyncClient(transport=httpx.MockTransport(
        lambda r: httpx.Response(200, json=_responses_api_body(basic_payload(), status="incomplete")))))
    with pytest.raises(ReportGenerationError):
        await OpenAIReportGenerator(make_settings(openai_api_key="sk-test"), client=client)             .generate(_ctx(ReportTier.BASIC))


async def test_openai_generator_schema_mismatch_is_retryable():
    # Basic payload returned for a detailed request: missing required fields.
    client = AsyncOpenAI(api_key="sk-test", http_client=httpx.AsyncClient(transport=httpx.MockTransport(
        lambda r: httpx.Response(200, json=_responses_api_body(basic_payload())))))
    with pytest.raises(ReportGenerationError) as exc:
        await OpenAIReportGenerator(make_settings(openai_api_key="sk-test"), client=client).generate(_ctx())
    assert exc.value.retryable is True


# ---- pipeline through the API --------------------------------------------------------

class ScriptedGenerator(ReportGenerator):
    def __init__(self, outputs: list) -> None:
        self.outputs = outputs
        self.calls = 0

    async def generate(self, ctx: ReportContext) -> GeneratedReport:
        out = self.outputs[min(self.calls, len(self.outputs) - 1)]
        self.calls += 1
        if isinstance(out, Exception):
            raise out
        return GeneratedReport(content=out, model="scripted", generation_ms=5)


async def _seed_finished_session(client, app, *, words: int = 60, retain: bool = False) -> tuple[dict, str]:
    body = register(client)
    h = auth_headers(body)
    if retain:
        client.patch("/v1/me", headers=h, json={"preferences": {"retain_transcripts": True}})
    sid = client.post("/v1/interviews", headers=h, json=INTERVIEW_BODY).json()["id"]
    c = app.state.container
    from app.modules.interviews.service import InterviewService

    async def go():
        svc = InterviewService(c)
        await c.db.sessions.update_one({"_id": sid}, {"$set": {"is_demo": False}})  # behave like a real session
        session = await svc.mark_live(sid)
        await svc.append_message(session, role="interviewer", text="Tell me about hash maps.", item_id="i1",
                                 retain=retain)
        half = " ".join(["word"] * (words // 2))
        await svc.append_message(session, role="candidate", text=half, item_id="i2", retain=retain)
        await svc.append_message(session, role="candidate", text=half, item_id="i3", retain=retain)
        await svc.finish(sid, reason="user_ended")
        await c.report_scheduler.wait_idle()

    client.portal.call(go)
    return h, sid


@pytest.fixture
def scripted():
    return ScriptedGenerator([BasicReportLLM(**basic_payload())])


@pytest.fixture
def report_client(scripted):
    app = build_app(report_generator=scripted)
    with TestClient(app) as c:
        yield c, app


async def test_report_ready_and_transcript_purged(report_client):
    client, app = report_client
    h, sid = await _seed_finished_session(client, app)
    r = client.get(f"/v1/interviews/{sid}/report", headers=h).json()
    assert r["status"] == "ready"
    rep = r["report"]
    assert rep["overall_score"] == 67
    assert rep["tier"] == "basic"
    assert "not predictions of real hiring outcomes" in rep["disclaimer"]
    # Not opted in to retention -> transcript deleted after the report.
    assert client.get(f"/v1/interviews/{sid}/transcript", headers=h).json()["messages"] == []
    assert client.get(f"/v1/interviews/{sid}", headers=h).json()["overall_score"] == 67


async def test_transcript_kept_when_user_opted_in(report_client):
    client, app = report_client
    h, sid = await _seed_finished_session(client, app, retain=True)
    t = client.get(f"/v1/interviews/{sid}/transcript", headers=h).json()
    assert t["retained"] is True and len(t["messages"]) == 3


async def test_insufficient_data_skips_ai(report_client, scripted):
    client, app = report_client
    h, sid = await _seed_finished_session(client, app, words=10)
    assert client.get(f"/v1/interviews/{sid}/report", headers=h).json()["status"] == "insufficient_data"
    assert scripted.calls == 0


async def test_invalid_output_retried_then_failed_then_regenerated():
    gen = ScriptedGenerator([BasicReportLLM(**basic_payload(problem_solving=cat(150))),
                             ReportGenerationError("boom", retryable=True)])
    app = build_app(report_generator=gen)
    with TestClient(app) as client:
        h, sid = await _seed_finished_session(client, app)
        assert gen.calls == 2  # one retry
        r = client.get(f"/v1/interviews/{sid}/report", headers=h).json()
        assert r["status"] == "failed"
        # Transcript is kept so the user can retry.
        gen.outputs = [BasicReportLLM(**basic_payload())]
        gen.calls = 0
        assert client.post(f"/v1/interviews/{sid}/report", headers=h).status_code == 202
        client.portal.call(app.state.container.report_scheduler.wait_idle)
        assert client.get(f"/v1/interviews/{sid}/report", headers=h).json()["status"] == "ready"
        assert client.post(f"/v1/interviews/{sid}/report", headers=h).status_code == 409


async def test_progress_locked_for_free_and_full_for_premium(report_client):
    client, app = report_client
    h, sid = await _seed_finished_session(client, app)
    free = client.get("/v1/progress", headers=h).json()
    assert free["summary"]["progress_tracking_locked"] is True
    assert free["summary"]["reports"] == 1
    assert free["trend"] == []
    client.post("/v1/billing/dev/grant-premium", headers=h)
    full = client.get("/v1/progress", headers=h).json()
    assert full["summary"]["average_score"] == 67
    assert full["categories"]["technical_knowledge"]["latest"] == 80
    assert full["summary"]["focus_category"] == "problem_solving"
    assert len(full["trend"]) == 1
