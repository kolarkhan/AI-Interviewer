from __future__ import annotations

from functools import lru_cache

from openai import AsyncOpenAI

from app.core.config import Settings
from app.core.errors import ServiceUnavailable


@lru_cache(maxsize=4)
def _client(api_key: str, timeout: float) -> AsyncOpenAI:
    return AsyncOpenAI(api_key=api_key, timeout=timeout, max_retries=2)


def get_openai(settings: Settings) -> AsyncOpenAI:
    if settings.demo_mode:
        raise ServiceUnavailable("AI service is not configured (demo mode).", code="ai_not_configured")
    assert settings.openai_api_key is not None
    return _client(settings.openai_api_key.get_secret_value(), settings.openai_request_timeout_seconds)
