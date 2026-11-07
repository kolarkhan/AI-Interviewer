# MongoDB data model

All `_id`s are UUID4 hex strings. Datetimes are UTC BSON dates. Indexes are created idempotently at startup (`backend/app/db/indexes.py`). Raw audio is **never** stored.

| Collection | Key fields | Indexes |
|---|---|---|
| `users` | `email` (lowercased), `password_hash` (Argon2id), `display_name`, `status`, `token_version`, `onboarding_completed`, `preferences{target_role_id, experience_level, voice, avatar_id, show_live_transcript, retain_transcripts}`, `consents{terms_accepted_at, transcript_retention_at, resume_processing_at}`, `created_at`, `updated_at`, `last_login_at` | `uniq_email` (unique) |
| `refresh_tokens` | `user_id`, `family_id`, `token_hash` (SHA-256), `expires_at`, `revoked_at`, `replaced_by` | `uniq_token_hash` (unique), `by_user`, `by_family`, `ttl_expires` (TTL on `expires_at`) |
| `voice_previews` | `_id` = `{engine}:{voice}:v{version}`, `engine`, `voice`, `version`, `sample_rate`, `pcm` (binary PCM16), `created_at` — one cached sample per voice | — |
| `interview_sessions` | `user_id`, `config{role_id, custom_role_title, interview_type, difficulty, experience_level, duration_minutes, voice, avatar_id, language (ISO 639-1, default "en"), use_resume, live_transcript}`, `status` (created/live/interrupted/completed/abandoned/expired/failed), `is_demo`, `realtime_model`, `plan_at_creation`, `report_tier`, `seed_questions[]`, `resume_id`, `start_deadline`, `started_at`, `ended_at`, `end_reason`, `elapsed_seconds`, `billed_seconds`, `connection_count`, `message_seq`, `usage_counted`, `realtime_usage{input_audio_tokens, output_audio_tokens, input_text_tokens, output_text_tokens, cached_tokens}`, `report_status`, `report_id`, `overall_score`, `report_error` | `by_user_recent` (user_id, created_at desc), `by_user_status` |
| `interview_messages` | `session_id`, `user_id`, `seq`, `role` (interviewer/candidate), `item_id`, `text`, `truncated`, `created_at`, `purge_after` | `by_session_seq`, `by_user`, `ttl_purge` (TTL on `purge_after`) |
| `interview_reports` | `session_id`, `user_id`, `schema_version`, `tier` (basic/detailed), `is_demo`, `model`, `overall_score` (computed server-side), `categories{technical_knowledge, communication_clarity, answer_relevance, problem_solving: {assessed, score, rationale}}`, `summary`, `strengths[]`, `improvements[]`, `recommended_topics[]`, `confidence_indicators[]`, `answer_feedback[]`, `practice_plan[]`, `disclaimer`, `generation_ms`, `token_usage`, `role_id`, `interview_type`, `created_at` | `uniq_session` (unique), `by_user_recent` |
| `subscriptions` | `user_id`, `plan` (free/premium), `status`, `source` (none/google_play/app_store/manual_dev), `product_id`, `store_transaction_id`, `verified`, `current_period_end` | `uniq_user` (unique) |
| `usage_records` | `user_id`, `kind` (interview_started/realtime_seconds/report_generated), `quantity`, `session_id`, `period` (`YYYY-MM`), `created_at` | `by_user_period_kind` |
| `resumes` | `user_id`, `filename`, `kind`, `text` (≤ 12k chars, extracted), `char_count`, `consent_at`, `created_at` | `uniq_user` (unique) |
| `question_bank` | `role_id` (`*` = any), `interview_type`, `difficulty`, `text` | `by_role_type_difficulty` |

## Retention and privacy rules

| Data | Default retention | User control |
|---|---|---|
| Audio | Not stored (streamed only) | — |
| Transcript messages | Deleted right after the report is generated; TTL safety net of 7 days (e.g. failed reports) | "Keep transcripts" → up to `TRANSCRIPT_RETENTION_DAYS` (default 90) |
| Reports & sessions | Until deleted | Delete one interview, "Delete interview data", or delete account |
| Resume | Extracted text only, until deleted | Delete any time; consent recorded |
| Usage counters | Kept without content after data deletion (so limits cannot be reset by deleting data) | Removed on account deletion |
| Refresh tokens | TTL at expiry | Logout / logout-all |
