from __future__ import annotations

import re
from enum import StrEnum

from pydantic import BaseModel, ConfigDict, Field, field_validator, model_validator

from app.modules.catalog.data import (
    AVATAR_IDS,
    CUSTOM_ROLE_ID,
    DEFAULT_LANGUAGE,
    DURATIONS_MINUTES,
    LANGUAGE_BY_ID,
    ROLE_BY_ID,
    VOICE_BY_ID,
    VOICE_IDS,
    avatar_for,
    Difficulty,
    ExperienceLevel,
    InterviewType,
)

_SAFE_TITLE = re.compile(r"^[\w\s\-/&+.,()#']+$", re.UNICODE)


class SessionStatus(StrEnum):
    CREATED = "created"      # configured, not yet connected
    LIVE = "live"            # a realtime connection is (or was recently) attached
    INTERRUPTED = "interrupted"  # connection lost; can be resumed while time remains
    COMPLETED = "completed"
    ABANDONED = "abandoned"  # ended before any meaningful conversation
    EXPIRED = "expired"      # never started within the start window
    FAILED = "failed"


ACTIVE_STATUSES = {SessionStatus.CREATED, SessionStatus.LIVE, SessionStatus.INTERRUPTED}


class ReportStatus(StrEnum):
    NONE = "none"
    PENDING = "pending"
    GENERATING = "generating"
    READY = "ready"
    INSUFFICIENT_DATA = "insufficient_data"
    FAILED = "failed"


class InterviewCreate(BaseModel):
    model_config = ConfigDict(extra="forbid")

    role_id: str
    custom_role_title: str | None = Field(default=None, min_length=2, max_length=80)
    interview_type: InterviewType
    difficulty: Difficulty
    experience_level: ExperienceLevel
    duration_minutes: int
    voice: str | None = None  # defaults to the chosen interviewer's voice
    avatar_id: str = "alex"
    language: str = DEFAULT_LANGUAGE  # spoken interview language (ISO 639-1)
    use_resume: bool = False
    live_transcript: bool = True

    @field_validator("role_id")
    @classmethod
    def _role(cls, v: str) -> str:
        if v not in ROLE_BY_ID:
            raise ValueError("Unknown role")
        return v

    @field_validator("custom_role_title")
    @classmethod
    def _title(cls, v: str | None) -> str | None:
        if v is None:
            return v
        v = " ".join(v.split())
        if not _SAFE_TITLE.match(v):
            raise ValueError("Role title contains unsupported characters")
        return v

    @field_validator("duration_minutes")
    @classmethod
    def _duration(cls, v: int) -> int:
        if v not in DURATIONS_MINUTES:
            raise ValueError(f"Duration must be one of {DURATIONS_MINUTES}")
        return v

    @field_validator("voice")
    @classmethod
    def _voice(cls, v: str | None) -> str | None:
        if v is not None and v not in VOICE_IDS:
            raise ValueError("Unknown voice")
        return v

    @field_validator("language")
    @classmethod
    def _language(cls, v: str) -> str:
        if v not in LANGUAGE_BY_ID:
            raise ValueError("Unsupported interview language")
        return v

    @field_validator("avatar_id")
    @classmethod
    def _avatar(cls, v: str) -> str:
        if v not in AVATAR_IDS:
            raise ValueError("Unknown avatar")
        return v

    @model_validator(mode="after")
    def _custom_role_requires_title(self) -> InterviewCreate:
        if self.role_id == CUSTOM_ROLE_ID and not self.custom_role_title:
            raise ValueError("custom_role_title is required for a custom role")
        if self.role_id != CUSTOM_ROLE_ID:
            self.custom_role_title = None
        avatar = avatar_for(self.avatar_id)
        if self.voice is None:
            self.voice = avatar["default_voice"]
        elif VOICE_BY_ID[self.voice]["gender"] != avatar["gender"]:
            raise ValueError(f"The {self.voice} voice does not match {avatar['title']}; choose a {avatar['gender']} voice")
        return self


class ConfirmCompletion(BaseModel):
    reason: str = Field(default="user_ended", pattern=r"^[a-z_]{3,40}$")
