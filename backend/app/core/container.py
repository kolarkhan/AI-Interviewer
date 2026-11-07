"""Application service container stored on `app.state.container`.

Keeps wiring explicit and lets tests swap implementations (fake realtime upstream,
fake report generator, in-memory DB) without monkeypatching modules.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import TYPE_CHECKING

from app.core.config import Settings
from app.core.rate_limit import InMemoryRateLimiter
from app.db.mongo import Database

if TYPE_CHECKING:
    from app.modules.realtime.registry import LiveSessionRegistry
    from app.modules.realtime.upstream import UpstreamFactory
    from app.modules.reports.generator import ReportGenerator
    from app.modules.reports.service import ReportScheduler


@dataclass
class Container:
    settings: Settings
    db: Database
    limiter: InMemoryRateLimiter = field(default_factory=InMemoryRateLimiter)
    upstream_factory: UpstreamFactory | None = None
    report_generator: ReportGenerator | None = None
    live_registry: LiveSessionRegistry | None = None
    report_scheduler: ReportScheduler | None = None
