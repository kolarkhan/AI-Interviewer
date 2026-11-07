"""MongoDB access.

Production uses PyMongo's native asyncio client (`AsyncMongoClient`). Motor is deprecated.
`memory://` uses `mongomock-motor` (development/demo/test only — data is not persisted).
Application code relies only on the async API subset both clients share:
`find_one`, `find(...).sort().skip().limit().to_list()`, `insert_one`, `update_one`,
`update_many`, `delete_one`, `delete_many`, `count_documents`, `find_one_and_update`,
`create_index`.
"""

from __future__ import annotations

from typing import Any

from app.core.config import Settings
from app.core.logging import get_logger

log = get_logger(__name__)


class Collections:
    USERS = "users"
    REFRESH_TOKENS = "refresh_tokens"
    INTERVIEW_SESSIONS = "interview_sessions"
    INTERVIEW_MESSAGES = "interview_messages"
    INTERVIEW_REPORTS = "interview_reports"
    SUBSCRIPTIONS = "subscriptions"
    USAGE_RECORDS = "usage_records"
    RESUMES = "resumes"
    QUESTION_BANK = "question_bank"
    VOICE_PREVIEWS = "voice_previews"


class Database:
    """Thin holder for the client and database handle."""

    def __init__(self, client: Any, name: str, *, is_memory: bool) -> None:
        self.client = client
        self.db = client[name]
        self.is_memory = is_memory

    def __getitem__(self, collection: str) -> Any:
        return self.db[collection]

    @property
    def users(self) -> Any:
        return self.db[Collections.USERS]

    @property
    def refresh_tokens(self) -> Any:
        return self.db[Collections.REFRESH_TOKENS]

    @property
    def sessions(self) -> Any:
        return self.db[Collections.INTERVIEW_SESSIONS]

    @property
    def messages(self) -> Any:
        return self.db[Collections.INTERVIEW_MESSAGES]

    @property
    def reports(self) -> Any:
        return self.db[Collections.INTERVIEW_REPORTS]

    @property
    def subscriptions(self) -> Any:
        return self.db[Collections.SUBSCRIPTIONS]

    @property
    def usage(self) -> Any:
        return self.db[Collections.USAGE_RECORDS]

    @property
    def resumes(self) -> Any:
        return self.db[Collections.RESUMES]

    @property
    def questions(self) -> Any:
        return self.db[Collections.QUESTION_BANK]

    @property
    def voice_previews(self) -> Any:
        return self.db[Collections.VOICE_PREVIEWS]

    async def ping(self) -> bool:
        if self.is_memory:
            return True
        try:
            await self.client.admin.command("ping")
            return True
        except Exception:  # noqa: BLE001 - health check must not raise
            log.warning("MongoDB ping failed")
            return False

    async def close(self) -> None:
        if not self.is_memory:
            await self.client.close()


def create_database(settings: Settings) -> Database:
    if settings.database_url.startswith("memory://"):
        from mongomock_motor import AsyncMongoMockClient

        log.warning("Using IN-MEMORY database (memory://). Data will be lost on restart. Never use in production.")
        return Database(AsyncMongoMockClient(), settings.database_name, is_memory=True)

    from pymongo import AsyncMongoClient

    client = AsyncMongoClient(
        settings.database_url,
        tz_aware=True,
        serverSelectionTimeoutMS=5000,
        appname=settings.app_name,
        uuidRepresentation="standard",
    )
    return Database(client, settings.database_name, is_memory=False)
