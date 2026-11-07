from __future__ import annotations

import uuid
from datetime import UTC, datetime


def utcnow() -> datetime:
    return datetime.now(UTC)


def as_utc(value: datetime | None) -> datetime | None:
    """MongoDB returns naive datetimes unless tz_aware is set (mocks never set it)."""
    if value is None:
        return None
    return value.replace(tzinfo=UTC) if value.tzinfo is None else value.astimezone(UTC)


def new_id() -> str:
    return uuid.uuid4().hex


def month_key(dt: datetime | None = None) -> str:
    dt = dt or utcnow()
    return f"{dt.year:04d}-{dt.month:02d}"
