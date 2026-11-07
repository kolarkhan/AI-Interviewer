"""In-process registry of live interview sessions.

Single-instance design: a reconnecting client must reach the same API process to reattach
to its upstream. With several instances, use sticky sessions keyed on the interview id
(or move to a Redis-coordinated design). If the client reaches another instance, the
interview still resumes — with a new upstream and transcript context instead of reattaching.
"""

from __future__ import annotations

import asyncio
from collections import defaultdict
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from app.modules.realtime.live_session import LiveSession


class LiveSessionRegistry:
    def __init__(self) -> None:
        self._sessions: dict[str, LiveSession] = {}
        self._locks: dict[str, asyncio.Lock] = defaultdict(asyncio.Lock)

    def lock_for(self, session_id: str) -> asyncio.Lock:
        return self._locks[session_id]

    def get(self, session_id: str) -> LiveSession | None:
        live = self._sessions.get(session_id)
        return live if live and not live.ended else None

    def add(self, live: LiveSession) -> None:
        self._sessions[live.session_id] = live

    def remove(self, live: LiveSession) -> None:
        if self._sessions.get(live.session_id) is live:
            del self._sessions[live.session_id]
            self._locks.pop(live.session_id, None)

    def for_user(self, user_id: str) -> list[LiveSession]:
        return [s for s in self._sessions.values() if s.user_id == user_id and not s.ended]

    def __len__(self) -> int:
        return len(self._sessions)

    async def terminate(self, session_id: str, *, reason: str) -> None:
        live = self.get(session_id)
        if live:
            await live.end(reason)

    async def terminate_user_sessions(self, user_id: str, *, reason: str) -> None:
        for live in self.for_user(user_id):
            await live.end(reason)

    async def shutdown(self) -> None:
        """Server shutdown: suspend (not complete) so clients can resume after a restart."""
        for live in list(self._sessions.values()):
            await live.suspend()
