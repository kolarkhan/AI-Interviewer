from __future__ import annotations

import random

from app.db.mongo import Database
from app.modules.questions.seed import ANY_ROLE, build_seed_documents


async def seed_question_bank(db: Database) -> int:
    """Inserts seed questions that are missing (idempotent)."""
    inserted = 0
    for doc in build_seed_documents():
        result = await db.questions.update_one({"_id": doc["_id"]}, {"$setOnInsert": doc}, upsert=True)
        if result.upserted_id is not None:
            inserted += 1
    return inserted


async def pick_seed_questions(db: Database, *, role_id: str, interview_type: str, difficulty: str,
                              count: int, rng: random.Random | None = None) -> list[str]:
    """Picks a varied set of seed questions for the interview plan."""
    rng = rng or random.Random()
    tech_role = role_id if role_id != "custom" else None

    async def fetch(q: dict) -> list[str]:
        return [d["text"] for d in await db.questions.find(q).to_list(None)]

    technical = await fetch({"role_id": tech_role, "interview_type": "technical", "difficulty": difficulty}) \
        if tech_role else []
    behavioral = await fetch({"role_id": ANY_ROLE, "interview_type": "behavioral"})
    hr = await fetch({"role_id": ANY_ROLE, "interview_type": "hr"})
    for pool in (technical, behavioral, hr):
        rng.shuffle(pool)

    if interview_type == "technical":
        chosen = technical[:count] if technical else []
    elif interview_type == "behavioral":
        chosen = behavioral[:count]
    elif interview_type == "hr":
        chosen = hr[:count]
    else:  # mixed: one HR opener, then alternate technical / behavioral
        chosen = hr[:1]
        tech_iter, beh_iter = iter(technical), iter(behavioral)
        while len(chosen) < count:
            nxt = next(tech_iter, None) if len(chosen) % 2 == 1 else next(beh_iter, None)
            if nxt is None:
                nxt = next(beh_iter, None) or next(tech_iter, None)
            if nxt is None:
                break
            chosen.append(nxt)
    return chosen[:count]
