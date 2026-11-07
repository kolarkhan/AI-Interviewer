"""In-process sliding-window rate limiter.

Suitable for a single API instance. For horizontal scaling replace `InMemoryRateLimiter`
with a Redis-backed implementation of the same `hit()` contract.
"""

from __future__ import annotations

import time
from collections import defaultdict, deque
from collections.abc import Callable
from dataclasses import dataclass

from fastapi import Request

from app.core.errors import RateLimited


@dataclass(frozen=True)
class Limit:
    count: int
    window_seconds: int


class InMemoryRateLimiter:
    def __init__(self, clock: Callable[[], float] = time.monotonic) -> None:
        self._hits: dict[str, deque[float]] = defaultdict(deque)
        self._clock = clock

    def hit(self, key: str, limit: Limit) -> float | None:
        """Record a hit. Returns `None` if allowed, otherwise seconds until retry."""
        now = self._clock()
        q = self._hits[key]
        cutoff = now - limit.window_seconds
        while q and q[0] <= cutoff:
            q.popleft()
        if len(q) >= limit.count:
            return max(0.0, q[0] + limit.window_seconds - now)
        q.append(now)
        return None

    def reset(self) -> None:
        self._hits.clear()


def client_ip(request: Request) -> str:
    # Behind a trusted reverse proxy configure uvicorn `--proxy-headers --forwarded-allow-ips`
    # so that `request.client.host` is the real client address. We do not trust raw headers here.
    return request.client.host if request.client else "unknown"


def enforce(limiter: InMemoryRateLimiter, key: str, limit: Limit) -> None:
    retry = limiter.hit(key, limit)
    if retry is not None:
        raise RateLimited(
            "Too many requests. Please try again shortly.",
            details={"retry_after_seconds": int(retry) + 1},
            headers={"Retry-After": str(int(retry) + 1)},
        )


# Named limits used across routers.
LOGIN_PER_IP = Limit(10, 60)
LOGIN_PER_EMAIL = Limit(5, 300)
REGISTER_PER_IP = Limit(5, 3600)
REFRESH_PER_IP = Limit(30, 60)
INTERVIEW_CREATE_PER_USER = Limit(20, 3600)
REPORT_REGENERATE_PER_USER = Limit(5, 3600)
RESUME_UPLOAD_PER_USER = Limit(10, 3600)
LIVE_CONNECT_PER_USER = Limit(30, 600)
VOICE_PREVIEW_PER_USER = Limit(60, 600)
