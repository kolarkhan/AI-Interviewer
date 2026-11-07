from __future__ import annotations

import io
import zipfile

from docx import Document

from app.modules.resumes.service import combine_documents
from tests.conftest import INTERVIEW_BODY, auth_headers, register

RESUME_TEXT = ("Jamie Candidate — Software Engineer. Built a payments API in Python and FastAPI serving 2M "
               "requests per day. Led migration from MySQL to PostgreSQL. Skills: Python, SQL, Docker, AWS.")
COVER_TEXT = ("Dear hiring team, I am excited to apply for the backend role. I enjoy designing reliable APIs, "
              "mentoring juniors and improving observability. Kind regards, Jamie.")


def premium(client) -> dict:
    h = auth_headers(register(client))
    client.post("/v1/billing/dev/grant-premium", headers=h)
    return h


def upload(client, h, name, data, consent="true"):
    return client.post("/v1/resumes", headers=h, files={"file": (name, data, "application/octet-stream")},
                       data={"consent": consent})


def upload_many(client, h, items, consent="true"):
    return client.post("/v1/resumes", headers=h,
                       files=[("files", (n, d, "application/octet-stream")) for n, d in items],
                       data={"consent": consent})


def make_docx(text: str) -> bytes:
    doc = Document()
    doc.add_paragraph(text)
    buf = io.BytesIO()
    doc.save(buf)
    return buf.getvalue()


def test_resume_is_premium_only(client):
    h = auth_headers(register(client))
    assert upload(client, h, "cv.txt", RESUME_TEXT.encode()).status_code == 402


def test_multiple_documents_in_one_request(client):
    h = premium(client)
    r = upload_many(client, h, [("cv.docx", make_docx(RESUME_TEXT)), ("cover.txt", COVER_TEXT.encode())])
    assert r.status_code == 201, r.text
    body = r.json()
    assert [d["filename"] for d in body["uploaded"]] == ["cv.docx", "cover.txt"]
    assert body["max_documents"] == 5
    listing = client.get("/v1/resumes", headers=h).json()
    assert [d["filename"] for d in listing["documents"]] == ["cv.docx", "cover.txt"]
    assert listing["documents"][0]["kind"] == "docx"
    # Later uploads are added, not replaced.
    assert upload(client, h, "linkedin.txt", RESUME_TEXT.encode()).status_code == 201
    assert len(client.get("/v1/resumes", headers=h).json()["documents"]) == 3
    # Older clients still get a single "current" document.
    assert client.get("/v1/resumes/current", headers=h).json()["filename"] == "cv.docx"


def test_upload_is_all_or_nothing(client):
    h = premium(client)
    r = upload_many(client, h, [("cv.txt", RESUME_TEXT.encode()), ("photo.exe", b"MZ\x90\x00")])
    assert r.status_code == 422
    assert "photo.exe" in r.json()["error"]["message"]
    assert client.get("/v1/resumes", headers=h).json()["documents"] == []


def test_document_limit(client):
    h = premium(client)
    assert upload_many(client, h, [(f"doc{i}.txt", RESUME_TEXT.encode()) for i in range(5)]).status_code == 201
    r = upload(client, h, "one-more.txt", RESUME_TEXT.encode())
    assert r.status_code == 409
    assert r.json()["error"]["code"] == "too_many_documents"


def test_delete_single_and_all(client):
    h = premium(client)
    docs = upload_many(client, h, [("a.txt", RESUME_TEXT.encode()), ("b.txt", COVER_TEXT.encode())]).json()["documents"]
    assert client.delete(f"/v1/resumes/{docs[0]['id']}", headers=h).status_code == 204
    left = client.get("/v1/resumes", headers=h).json()["documents"]
    assert [d["filename"] for d in left] == ["b.txt"]
    assert client.delete("/v1/resumes/does-not-exist", headers=h).status_code == 404
    assert client.delete("/v1/resumes", headers=h).status_code == 204
    assert client.get("/v1/resumes", headers=h).json()["documents"] == []
    assert client.get("/v1/resumes/current", headers=h).status_code == 404


def test_interview_uses_all_documents(client, app):
    h = premium(client)
    upload_many(client, h, [("cv.txt", RESUME_TEXT.encode()), ("cover.txt", COVER_TEXT.encode())])
    r = client.post("/v1/interviews", headers=h, json={**INTERVIEW_BODY, "use_resume": True})
    assert r.status_code == 201, r.text
    session = client.portal.call(lambda: app.state.container.db.sessions.find_one({"_id": r.json()["id"]}))
    assert len(session["resume_ids"]) == 2


def test_combined_text_shares_the_budget():
    docs = [{"filename": "cv.pdf", "text": "A" * 10_000}, {"filename": "cover.docx", "text": "B" * 10_000}]
    text = combine_documents(docs, max_chars=4_000)
    assert len(text) <= 4_000
    assert "Document 1: cv.pdf" in text and "Document 2: cover.docx" in text
    assert text.count("A") > 1_000 and text.count("B") > 1_000


def test_resume_required_for_resume_interview(client):
    h = premium(client)
    r = client.post("/v1/interviews", headers=h, json={**INTERVIEW_BODY, "use_resume": True})
    assert r.status_code == 409
    assert r.json()["error"]["code"] == "resume_missing"


def test_consent_required(client):
    h = premium(client)
    assert upload(client, h, "cv.txt", RESUME_TEXT.encode(), consent="false").status_code == 422


def test_rejects_bad_files(client):
    h = premium(client)
    assert upload(client, h, "cv.exe", b"MZ\x90\x00").status_code == 422
    assert upload(client, h, "cv.pdf", RESUME_TEXT.encode()).status_code == 422  # extension/content mismatch
    assert upload(client, h, "cv.txt", b"%PDF-1.7 fake").status_code == 422
    assert upload(client, h, "cv.txt", b"too short").status_code == 422
    assert upload(client, h, "cv.txt", b"").status_code == 422
    assert upload(client, h, "cv.txt", b"a" * (2 * 1024 * 1024 + 1)).status_code == 422
    assert upload(client, h, "cv.pdf", b"%PDF-1.7\nnot really a pdf").status_code == 422
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w") as zf:
        zf.writestr("hello.txt", "x")
    assert upload(client, h, "cv.docx", buf.getvalue()).status_code == 422
    assert client.post("/v1/resumes", headers=h, data={"consent": "true"}).status_code == 422  # no files
