"""Static interview configuration catalog: roles, types, levels, durations, voices, avatars."""

from __future__ import annotations

from enum import StrEnum


class InterviewType(StrEnum):
    TECHNICAL = "technical"
    HR = "hr"
    BEHAVIORAL = "behavioral"
    MIXED = "mixed"


class Difficulty(StrEnum):
    BEGINNER = "beginner"
    INTERMEDIATE = "intermediate"
    ADVANCED = "advanced"


class ExperienceLevel(StrEnum):
    ENTRY = "entry"
    JUNIOR = "junior"
    MID = "mid"
    SENIOR = "senior"


CUSTOM_ROLE_ID = "custom"

ROLES: list[dict] = [
    {
        "id": "software_engineer",
        "title": "Software Engineer",
        "description": "Data structures, algorithms, system design, code quality and collaboration.",
        "focus_areas": ["data structures & algorithms", "system design", "testing", "debugging", "code review"],
    },
    {
        "id": "ai_ml_engineer",
        "title": "AI/ML Engineer",
        "description": "Model development, MLOps, LLM applications, evaluation and deployment.",
        "focus_areas": ["ML fundamentals", "deep learning", "LLM systems", "evaluation", "MLOps"],
    },
    {
        "id": "data_scientist",
        "title": "Data Scientist",
        "description": "Statistics, experimentation, modelling, SQL and stakeholder communication.",
        "focus_areas": ["statistics", "A/B testing", "SQL", "feature engineering", "communicating insights"],
    },
    {
        "id": "frontend_developer",
        "title": "Frontend Developer",
        "description": "JavaScript/TypeScript, UI frameworks, performance, accessibility.",
        "focus_areas": ["JavaScript/TypeScript", "React or similar", "web performance", "accessibility", "CSS"],
    },
    {
        "id": "backend_developer",
        "title": "Backend Developer",
        "description": "APIs, databases, scalability, reliability and security.",
        "focus_areas": ["API design", "databases", "caching", "concurrency", "security"],
    },
    {
        "id": CUSTOM_ROLE_ID,
        "title": "Custom Role",
        "description": "Describe any role and the interviewer adapts its questions.",
        "focus_areas": [],
    },
]

ROLE_BY_ID = {r["id"]: r for r in ROLES}

INTERVIEW_TYPES = [
    {"id": InterviewType.TECHNICAL, "title": "Technical", "description": "Role-specific knowledge and problem solving."},
    {"id": InterviewType.HR, "title": "HR", "description": "Motivation, expectations, culture fit and logistics."},
    {"id": InterviewType.BEHAVIORAL, "title": "Behavioral", "description": "Past experiences using the STAR method."},
    {"id": InterviewType.MIXED, "title": "Mixed", "description": "A realistic blend of technical and behavioral."},
]

DIFFICULTIES = [
    {"id": Difficulty.BEGINNER, "title": "Beginner"},
    {"id": Difficulty.INTERMEDIATE, "title": "Intermediate"},
    {"id": Difficulty.ADVANCED, "title": "Advanced"},
]

EXPERIENCE_LEVELS = [
    {"id": ExperienceLevel.ENTRY, "title": "Entry-level"},
    {"id": ExperienceLevel.JUNIOR, "title": "Junior"},
    {"id": ExperienceLevel.MID, "title": "Mid-level"},
    {"id": ExperienceLevel.SENIOR, "title": "Senior"},
]

DURATIONS_MINUTES = [5, 10, 20, 30]

# Spoken interview languages. "id" is the ISO 639-1 code (also passed to Realtime transcription).
# Technical terms may stay in English in every language, as in real interviews.
DEFAULT_LANGUAGE = "en"
LANGUAGES = [
    {"id": "en", "title": "English", "description": "Interview in English"},
    {"id": "ur", "title": "Urdu", "description": "Interview in Urdu (technical terms may stay in English)"},
    {"id": "hi", "title": "Hindi", "description": "Interview in Hindi (technical terms may stay in English)"},
    {"id": "ar", "title": "Arabic", "description": "Interview in Arabic"},
    {"id": "bn", "title": "Bengali", "description": "Interview in Bengali"},
    {"id": "es", "title": "Spanish", "description": "Interview in Spanish"},
    {"id": "fr", "title": "French", "description": "Interview in French"},
    {"id": "de", "title": "German", "description": "Interview in German"},
    {"id": "it", "title": "Italian", "description": "Interview in Italian"},
    {"id": "pt", "title": "Portuguese", "description": "Interview in Portuguese"},
    {"id": "tr", "title": "Turkish", "description": "Interview in Turkish"},
    {"id": "ru", "title": "Russian", "description": "Interview in Russian"},
    {"id": "id", "title": "Indonesian", "description": "Interview in Indonesian"},
    {"id": "zh", "title": "Chinese (Mandarin)", "description": "Interview in Mandarin Chinese"},
    {"id": "ja", "title": "Japanese", "description": "Interview in Japanese"},
    {"id": "ko", "title": "Korean", "description": "Interview in Korean"},
]
LANGUAGE_BY_ID = {lang["id"]: lang for lang in LANGUAGES}


def language_title(language_id: str | None) -> str:
    return LANGUAGE_BY_ID.get(language_id or DEFAULT_LANGUAGE, LANGUAGE_BY_ID[DEFAULT_LANGUAGE])["title"]

# GPT-Live voices (gpt-live-1), from OpenAI's GPT-Live voice table (2026-10). API ids are lowercase.
# "tag" is the short accent/language label shown in the app.
LIVE_VOICES = [
    {"id": "cedar", "title": "Cedar", "gender": "male", "description": "Open and upbeat", "language": "English", "tag": "US"},
    {"id": "meridian", "title": "Meridian", "gender": "male", "description": "Grounded and measured", "language": "English", "tag": "US"},
    {"id": "vesper", "title": "Vesper", "gender": "male", "description": "Clear and direct", "language": "English", "tag": "UK"},
    {"id": "ripple", "title": "Ripple", "gender": "male", "description": "Warm and conversational", "language": "English", "tag": "AU"},
    {"id": "stone", "title": "Stone", "gender": "male", "description": "Calm and precise", "language": "English", "tag": "IE"},
    {"id": "tempo", "title": "Tempo", "gender": "male", "description": "Grounded and measured", "language": "Portuguese", "tag": "Portuguese"},
    {"id": "flitz", "title": "Flitz", "gender": "male", "description": "Rich and playful", "language": "German", "tag": "German"},
    {"id": "sillage", "title": "Sillage", "gender": "male", "description": "Warm and grounded", "language": "French", "tag": "French"},
    {"id": "shida", "title": "Shida", "gender": "male", "description": "Quiet and grounded", "language": "Japanese", "tag": "Japanese"},
    {"id": "marin", "title": "Marin", "gender": "female", "description": "Animated and earnest", "language": "English", "tag": "US"},
    {"id": "gleam", "title": "Gleam", "gender": "female", "description": "Warm and conversational", "language": "English", "tag": "US"},
    {"id": "willow", "title": "Willow", "gender": "female", "description": "Expressive and bright", "language": "English", "tag": "IE"},
    {"id": "bossa", "title": "Bossa", "gender": "female", "description": "Clear and direct", "language": "Portuguese", "tag": "Portuguese"},
    {"id": "nuri", "title": "Nuri", "gender": "female", "description": "Warm and animated", "language": "German", "tag": "German"},
    {"id": "juni", "title": "Juni", "gender": "female", "description": "Bright and expressive", "language": "German", "tag": "German"},
    {"id": "brise", "title": "Brise", "gender": "female", "description": "Clear and conversational", "language": "French", "tag": "French"},
    {"id": "nira", "title": "Nira", "gender": "female", "description": "Soft and thoughtful", "language": "Hindi", "tag": "Hindi"},
    {"id": "harema", "title": "Harema", "gender": "female", "description": "Gentle and engaging", "language": "Japanese", "tag": "Japanese"},
    {"id": "noeul", "title": "Noeul", "gender": "female", "description": "Airy and expressive", "language": "Korean", "tag": "Korean"},
]

# Realtime API voices (gpt-realtime-2.1), used when OPENAI_VOICE_ENGINE=realtime. OpenAI does not assign
# genders officially; "gender" reflects how each voice is commonly perceived.
REALTIME_VOICES = [
    {"id": "cedar", "title": "Cedar", "gender": "male", "description": "Calm, confident", "language": "English", "tag": "US"},
    {"id": "ash", "title": "Ash", "gender": "male", "description": "Direct, clear", "language": "English", "tag": "US"},
    {"id": "echo", "title": "Echo", "gender": "male", "description": "Smooth, steady", "language": "English", "tag": "US"},
    {"id": "ballad", "title": "Ballad", "gender": "male", "description": "Warm, articulate", "language": "English", "tag": "UK"},
    {"id": "verse", "title": "Verse", "gender": "male", "description": "Expressive", "language": "English", "tag": "US"},
    {"id": "marin", "title": "Marin", "gender": "female", "description": "Warm, professional", "language": "English", "tag": "US"},
    {"id": "coral", "title": "Coral", "gender": "female", "description": "Friendly, upbeat", "language": "English", "tag": "US"},
    {"id": "sage", "title": "Sage", "gender": "female", "description": "Measured, thoughtful", "language": "English", "tag": "US"},
    {"id": "shimmer", "title": "Shimmer", "gender": "female", "description": "Bright, clear", "language": "English", "tag": "US"},
]

FREE_VOICE_IDS = frozenset({"cedar", "marin", "meridian", "gleam"})


def voices_for_engine(engine: str) -> list[dict]:
    return LIVE_VOICES if engine == "live" else REALTIME_VOICES


VOICES = LIVE_VOICES + [v for v in REALTIME_VOICES if v["id"] not in {lv["id"] for lv in LIVE_VOICES}]
VOICE_IDS = {v["id"] for v in VOICES}
VOICE_BY_ID = {v["id"]: v for v in VOICES}

# Avatar ids map to Unity Resources/Avatars/{id}.asset (AvatarDefinition). Characters come from the
# Microsoft Rocketbox Avatar Library (MIT licence). If an avatar is missing from a build, the app falls back
# to its built-in placeholder.
AVATARS = [
    {"id": "alex", "title": "Alex", "gender": "male", "interviewer_name": "Alex", "default_voice": "cedar", "available": True,
     "note": "Professional male interviewer (Microsoft Rocketbox, MIT)."},
    {"id": "maya", "title": "Maya", "gender": "female", "interviewer_name": "Maya", "default_voice": "marin", "available": True,
     "note": "Professional female interviewer (Microsoft Rocketbox, MIT)."},
]
DEFAULT_AVATAR_ID = "alex"
LEGACY_AVATAR_IDS = {"alex_placeholder"}  # accepted for older clients/data
AVATAR_BY_ID = {a["id"]: a for a in AVATARS}
AVATAR_IDS = {a["id"] for a in AVATARS} | LEGACY_AVATAR_IDS


def avatar_for(avatar_id: str | None) -> dict:
    """Avatar entry; legacy/unknown ids map to the default interviewer."""
    return AVATAR_BY_ID.get(avatar_id or DEFAULT_AVATAR_ID, AVATAR_BY_ID[DEFAULT_AVATAR_ID])


def interviewer_name_for(avatar_id: str | None) -> str:
    return AVATAR_BY_ID.get(avatar_id or DEFAULT_AVATAR_ID, AVATAR_BY_ID[DEFAULT_AVATAR_ID])["interviewer_name"]
