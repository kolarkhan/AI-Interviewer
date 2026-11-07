from __future__ import annotations

from pydantic import BaseModel, ConfigDict, Field, field_validator

from app.modules.catalog.data import AVATAR_IDS, ROLE_BY_ID, VOICE_IDS, ExperienceLevel


class PreferencesUpdate(BaseModel):
    model_config = ConfigDict(extra="forbid")

    target_role_id: str | None = None
    experience_level: ExperienceLevel | None = None
    voice: str | None = None
    avatar_id: str | None = None
    show_live_transcript: bool | None = None
    retain_transcripts: bool | None = None

    @field_validator("target_role_id")
    @classmethod
    def _role(cls, v: str | None) -> str | None:
        if v is not None and v not in ROLE_BY_ID:
            raise ValueError("Unknown role")
        return v

    @field_validator("voice")
    @classmethod
    def _voice(cls, v: str | None) -> str | None:
        if v is not None and v not in VOICE_IDS:
            raise ValueError("Unknown voice")
        return v

    @field_validator("avatar_id")
    @classmethod
    def _avatar(cls, v: str | None) -> str | None:
        if v is not None and v not in AVATAR_IDS:
            raise ValueError("Unknown avatar")
        return v


class ProfileUpdate(BaseModel):
    model_config = ConfigDict(extra="forbid")

    display_name: str | None = Field(default=None, min_length=1, max_length=60)
    onboarding_completed: bool | None = None
    preferences: PreferencesUpdate | None = None


class DeleteAccountRequest(BaseModel):
    password: str = Field(min_length=1, max_length=128)
    confirm: bool

    @field_validator("confirm")
    @classmethod
    def _confirm(cls, v: bool) -> bool:
        if not v:
            raise ValueError("Deletion must be confirmed")
        return v
