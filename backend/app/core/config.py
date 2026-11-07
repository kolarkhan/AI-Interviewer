"""Application settings loaded from environment variables (see `.env.example`)."""

from __future__ import annotations

from functools import lru_cache
from typing import Literal

from pydantic import Field, SecretStr, field_validator
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_file=".env", env_file_encoding="utf-8", extra="ignore")

    # --- General -----------------------------------------------------------
    environment: Literal["development", "test", "staging", "production"] = "development"
    app_name: str = "AI Interviewer API"
    log_level: str = "INFO"
    # Comma-separated list of allowed CORS origins. Mobile apps do not need CORS;
    # only list web dashboards/admin tools here. Empty = CORS disabled.
    cors_origins: str = ""

    # --- Database ----------------------------------------------------------
    # `mongodb://...` / `mongodb+srv://...` for real MongoDB.
    # `memory://` uses an in-process mock (development/demo only, data is lost on restart).
    database_url: str = "memory://"
    database_name: str = "ai_interviewer"

    # --- Auth --------------------------------------------------------------
    jwt_secret: SecretStr = SecretStr("dev-only-insecure-secret-change-me-0123456789")
    jwt_issuer: str = "ai-interviewer"
    access_token_ttl_minutes: int = 15
    refresh_token_ttl_days: int = 30

    # --- OpenAI ------------------------------------------------------------
    openai_api_key: SecretStr | None = None
    # Voice engine: "live" = GPT-Live (gpt-live-1, full duplex, natural voices such as Gleam/Meridian),
    # "realtime" = Realtime API (gpt-realtime-2.1). Both run through the same backend relay.
    openai_voice_engine: Literal["live", "realtime"] = "live"
    openai_live_model: str = "gpt-live-1"
    openai_live_url: str = "wss://api.openai.com/v1/live/sessions"
    # GPT-Live needs a Responses backend to reason and reply (verified with probe sessions).
    openai_live_delegation_model: str = "gpt-5.6-luna"
    openai_live_delegation_reasoning: Literal["none", "minimal", "low", "medium", "high"] = "low"
    # Generate missing voice preview samples in the background at startup (one-time, ~8 s of voice per voice).
    voice_preview_warmup: bool = True
    openai_realtime_model: str = "gpt-realtime-2.1"
    # Cheaper realtime model for the free plan (roughly a third of the audio token price).
    openai_realtime_model_free: str = "gpt-realtime-2.1-mini"
    openai_realtime_url: str = "wss://api.openai.com/v1/realtime"
    openai_transcription_model: str = "gpt-4o-mini-transcribe"
    openai_report_model_basic: str = "gpt-6-luna"
    openai_report_model_detailed: str = "gpt-6.1-sol"
    openai_request_timeout_seconds: float = 90.0
    # Direct client connections (ephemeral client secrets). Off by default: the
    # backend relay is the supported transport. See docs/ARCHITECTURE.md.
    enable_direct_realtime: bool = False
    direct_realtime_secret_ttl_seconds: int = 120

    # Demo mode: when no OpenAI key is configured the relay uses a scripted,
    # clearly-labeled demo interviewer. Set to false to make missing keys an error.
    allow_demo_mode: bool = True

    # --- Live interview limits ---------------------------------------------
    live_reconnect_grace_seconds: int = 30
    live_wrapup_grace_seconds: int = 45
    session_start_window_minutes: int = 30
    max_client_audio_frame_bytes: int = 64_000

    # --- Uploads -----------------------------------------------------------
    resume_max_bytes: int = 2 * 1024 * 1024
    resume_max_chars: int = 12_000

    # --- Privacy -----------------------------------------------------------
    # Transcripts are kept only until the report is generated unless the user
    # opted in to retention. This is the hard cap for opted-in retention.
    transcript_retention_days: int = 90

    @field_validator("jwt_secret")
    @classmethod
    def _secret_long_enough(cls, v: SecretStr) -> SecretStr:
        if len(v.get_secret_value()) < 32:
            raise ValueError("JWT_SECRET must be at least 32 characters")
        return v

    @property
    def is_production(self) -> bool:
        return self.environment == "production"

    def realtime_model_for_plan(self, plan_id: str) -> str:
        return self.openai_realtime_model if plan_id == "premium" else self.openai_realtime_model_free

    def voice_model_for_plan(self, plan_id: str) -> str:
        """Model used for the live interview with the configured voice engine."""
        if self.openai_voice_engine == "live":
            return self.openai_live_model
        return self.realtime_model_for_plan(plan_id)

    @property
    def demo_mode(self) -> bool:
        return self.openai_api_key is None or not self.openai_api_key.get_secret_value().strip()

    @property
    def cors_origin_list(self) -> list[str]:
        return [o.strip() for o in self.cors_origins.split(",") if o.strip()]

    def validate_for_startup(self) -> None:
        """Fail fast on unsafe production configuration."""
        if not self.is_production:
            return
        problems: list[str] = []
        if self.jwt_secret.get_secret_value().startswith("dev-only"):
            problems.append("JWT_SECRET must be set to a strong random value in production")
        if self.database_url.startswith("memory://"):
            problems.append("DATABASE_URL must point to a real MongoDB in production")
        if self.demo_mode and not self.allow_demo_mode:
            problems.append("OPENAI_API_KEY is required when ALLOW_DEMO_MODE=false")
        if "*" in self.cors_origin_list:
            problems.append("CORS_ORIGINS must not contain '*' in production")
        if problems:
            raise RuntimeError("Unsafe production configuration: " + "; ".join(problems))


@lru_cache
def get_settings() -> Settings:
    return Settings()


# Re-exported for typing convenience.
__all__ = ["Settings", "get_settings", "Field"]
