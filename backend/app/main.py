"""FastAPI application factory."""

from __future__ import annotations

import asyncio
import contextlib

from contextlib import asynccontextmanager

from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware

from app.core.config import Settings, get_settings
from app.core.container import Container
from app.core.deps import ContainerDep
from app.core.errors import install_error_handlers
from app.core.logging import configure_logging, get_logger
from app.core.security import safety_identifier
from app.db.indexes import ensure_indexes
from app.db.mongo import Database, create_database
from app.modules.auth.router import router as auth_router
from app.modules.billing.router import router as billing_router
from app.modules.catalog import voice_preview
from app.modules.catalog.router import router as catalog_router
from app.modules.interviews.router import router as interviews_router
from app.modules.questions.service import seed_question_bank
from app.modules.realtime.registry import LiveSessionRegistry
from app.modules.realtime.router import router as realtime_router
from app.modules.realtime.upstream import UpstreamFactory, default_upstream_factory
from app.modules.reports.generator import ReportGenerator, default_report_generator
from app.modules.reports.service import ReportScheduler
from app.modules.resumes.router import router as resumes_router
from app.modules.users.router import router as users_router

log = get_logger(__name__)

API_DESCRIPTION = """
Backend for the AI Interviewer mobile app.

* Authenticate with `POST /v1/auth/login` and send `Authorization: Bearer <access_token>`.
* Live interviews use the WebSocket `/v1/interviews/{id}/live` (see docs/API.md for the protocol).
* All errors use the envelope `{"error": {"code", "message", "details"}}`.
"""


def create_app(settings: Settings | None = None, *, db: Database | None = None,
               upstream_factory: UpstreamFactory | None = None,
               report_generator: ReportGenerator | None = None) -> FastAPI:
    settings = settings or get_settings()
    settings.validate_for_startup()
    configure_logging(settings.log_level)

    container = Container(settings=settings, db=db or create_database(settings))
    container.upstream_factory = upstream_factory or default_upstream_factory(settings)
    container.report_generator = report_generator or default_report_generator(settings)
    container.live_registry = LiveSessionRegistry()
    container.report_scheduler = ReportScheduler(container)

    @asynccontextmanager
    async def lifespan(_: FastAPI):
        await ensure_indexes(container.db)
        seeded = await seed_question_bank(container.db)
        recovered = await container.report_scheduler.recover()
        log.info("API started", extra={"ctx": {"environment": settings.environment, "demo_mode": settings.demo_mode,
                                               "questions_seeded": seeded, "reports_requeued": recovered,
                                               "memory_db": container.db.is_memory}})
        if settings.demo_mode:
            log.warning("DEMO MODE: OPENAI_API_KEY is not set. Live interviews use the scripted demo interviewer.")
        warmup = asyncio.create_task(voice_preview.warm_up(container, safety_identifier("system:voice-previews")),
                                     name="voice-preview-warmup")
        yield
        warmup.cancel()
        with contextlib.suppress(asyncio.CancelledError, Exception):
            await warmup
        await container.live_registry.shutdown()
        await container.report_scheduler.shutdown()
        await container.db.close()

    app = FastAPI(
        title=settings.app_name,
        version="1.0.0",
        description=API_DESCRIPTION,
        lifespan=lifespan,
        docs_url=None if settings.is_production else "/docs",
        redoc_url=None if settings.is_production else "/redoc",
        openapi_url=None if settings.is_production else "/openapi.json",
    )
    app.state.container = container
    install_error_handlers(app)

    if settings.cors_origin_list:
        app.add_middleware(
            CORSMiddleware,
            allow_origins=settings.cors_origin_list,
            allow_credentials=False,
            allow_methods=["GET", "POST", "PATCH", "DELETE"],
            allow_headers=["Authorization", "Content-Type"],
        )

    @app.middleware("http")
    async def security_headers(request, call_next):
        response = await call_next(request)
        response.headers.setdefault("X-Content-Type-Options", "nosniff")
        response.headers.setdefault("Referrer-Policy", "no-referrer")
        response.headers.setdefault("Cache-Control", "no-store")
        if settings.is_production:
            response.headers.setdefault("Strict-Transport-Security", "max-age=31536000; includeSubDomains")
        return response

    @app.get("/health", tags=["health"])
    async def health() -> dict:
        return {"status": "ok"}

    @app.get("/health/ready", tags=["health"])
    async def ready(c: ContainerDep) -> dict:
        db_ok = await c.db.ping()
        return {"status": "ok" if db_ok else "degraded", "database": db_ok, "demo_mode": c.settings.demo_mode,
                "live_sessions": len(c.live_registry or [])}

    for r in (auth_router, users_router, catalog_router, billing_router, interviews_router, realtime_router,
              resumes_router):
        app.include_router(r)
    return app

