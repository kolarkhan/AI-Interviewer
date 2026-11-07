using System.Collections.Generic;
using Newtonsoft.Json;

namespace AIInterviewer.Core.Api
{
    // DTOs mirror the FastAPI responses (see docs/API.md). Field names use the backend's snake_case.

    public sealed class ApiErrorBody
    {
        [JsonProperty("error")] public ApiErrorDetail Error;
    }

    public sealed class ApiErrorDetail
    {
        [JsonProperty("code")] public string Code;
        [JsonProperty("message")] public string Message;
        [JsonProperty("details")] public Newtonsoft.Json.Linq.JToken Details;
    }

    public sealed class TokenPair
    {
        [JsonProperty("access_token")] public string AccessToken;
        [JsonProperty("access_token_expires_at")] public System.DateTime AccessTokenExpiresAt;
        [JsonProperty("refresh_token")] public string RefreshToken;
        [JsonProperty("refresh_token_expires_at")] public System.DateTime RefreshTokenExpiresAt;
    }

    public sealed class AuthResponse
    {
        [JsonProperty("user")] public UserProfile User;
        [JsonProperty("tokens")] public TokenPair Tokens;
    }

    public sealed class UserPreferences
    {
        [JsonProperty("target_role_id")] public string TargetRoleId;
        [JsonProperty("experience_level")] public string ExperienceLevel;
        [JsonProperty("voice")] public string Voice;
        [JsonProperty("avatar_id")] public string AvatarId;
        [JsonProperty("show_live_transcript")] public bool ShowLiveTranscript = true;
        [JsonProperty("retain_transcripts")] public bool RetainTranscripts;
    }

    public sealed class UserProfile
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("email")] public string Email;
        [JsonProperty("display_name")] public string DisplayName;
        [JsonProperty("onboarding_completed")] public bool OnboardingCompleted;
        [JsonProperty("preferences")] public UserPreferences Preferences = new UserPreferences();

        public string FirstName
        {
            get
            {
                if (string.IsNullOrWhiteSpace(DisplayName)) return "there";
                var parts = DisplayName.Trim().Split(' ');
                return parts[0];
            }
        }
    }

    public sealed class CatalogOption
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("title")] public string Title;
        [JsonProperty("description")] public string Description;
        [JsonProperty("locked")] public bool Locked;
        [JsonProperty("available")] public bool Available = true;
        [JsonProperty("note")] public string Note;
        [JsonProperty("interviewer_name")] public string InterviewerName;
        [JsonProperty("gender")] public string Gender;
        [JsonProperty("language")] public string Language;
        [JsonProperty("tag")] public string Tag;

        /// <summary>Chip label, e.g. "Vesper · UK" or "Flitz · German".</summary>
        public string LabelWithTag => string.IsNullOrEmpty(Tag) ? Title : Title + " · " + Tag;
        [JsonProperty("default_voice")] public string DefaultVoice;
    }

    public sealed class DurationOption
    {
        [JsonProperty("minutes")] public int Minutes;
        [JsonProperty("locked")] public bool Locked;
    }

    public sealed class LockFlag
    {
        [JsonProperty("locked")] public bool Locked;
    }

    public sealed class Catalog
    {
        [JsonProperty("plan")] public string Plan;
        [JsonProperty("roles")] public List<CatalogOption> Roles = new List<CatalogOption>();
        [JsonProperty("interview_types")] public List<CatalogOption> InterviewTypes = new List<CatalogOption>();
        [JsonProperty("difficulties")] public List<CatalogOption> Difficulties = new List<CatalogOption>();
        [JsonProperty("experience_levels")] public List<CatalogOption> ExperienceLevels = new List<CatalogOption>();
        [JsonProperty("languages")] public List<CatalogOption> Languages = new List<CatalogOption>();
        [JsonProperty("durations")] public List<DurationOption> Durations = new List<DurationOption>();
        [JsonProperty("voices")] public List<CatalogOption> Voices = new List<CatalogOption>();
        [JsonProperty("avatars")] public List<CatalogOption> Avatars = new List<CatalogOption>();
        [JsonProperty("resume_questions")] public LockFlag ResumeQuestions = new LockFlag();
        [JsonProperty("demo_mode")] public bool DemoMode;
        /// <summary>Changes when the server's voice samples change (device-saved samples are then dropped).</summary>
        [JsonProperty("voice_preview_version")] public string VoicePreviewVersion;
    }

    public sealed class InterviewConfig
    {
        [JsonProperty("role_id")] public string RoleId = "software_engineer";
        [JsonProperty("custom_role_title", NullValueHandling = NullValueHandling.Ignore)] public string CustomRoleTitle;
        [JsonProperty("interview_type")] public string InterviewType = "technical";
        [JsonProperty("difficulty")] public string Difficulty = "beginner";
        [JsonProperty("experience_level")] public string ExperienceLevel = "junior";
        [JsonProperty("duration_minutes")] public int DurationMinutes = 10;
        [JsonProperty("voice")] public string Voice = "cedar";
        [JsonProperty("avatar_id")] public string AvatarId = "alex";
        /// <summary>Spoken interview language (ISO 639-1, e.g. "en", "ur").</summary>
        [JsonProperty("language")] public string Language = "en";
        [JsonProperty("use_resume")] public bool UseResume;
        [JsonProperty("live_transcript")] public bool LiveTranscript = true;
        [JsonProperty("role_title", NullValueHandling = NullValueHandling.Ignore)] public string RoleTitle;

        public InterviewConfig CloneForRequest()
        {
            return new InterviewConfig
            {
                RoleId = RoleId, CustomRoleTitle = RoleId == "custom" ? CustomRoleTitle : null,
                InterviewType = InterviewType, Difficulty = Difficulty, ExperienceLevel = ExperienceLevel,
                DurationMinutes = DurationMinutes, Voice = Voice, AvatarId = AvatarId,
                Language = string.IsNullOrEmpty(Language) ? "en" : Language, UseResume = UseResume,
                LiveTranscript = LiveTranscript
            };
        }
    }

    public sealed class LiveInfo
    {
        [JsonProperty("websocket_path")] public string WebSocketPath;
        [JsonProperty("protocol_version")] public int ProtocolVersion;
    }

    public sealed class InterviewSession
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("status")] public string Status;
        [JsonProperty("is_demo")] public bool IsDemo;
        [JsonProperty("config")] public InterviewConfig Config;
        [JsonProperty("created_at")] public System.DateTime? CreatedAt;
        [JsonProperty("started_at")] public System.DateTime? StartedAt;
        [JsonProperty("ended_at")] public System.DateTime? EndedAt;
        [JsonProperty("end_reason")] public string EndReason;
        [JsonProperty("elapsed_seconds")] public int ElapsedSeconds;
        [JsonProperty("duration_seconds")] public int DurationSeconds;
        [JsonProperty("report_status")] public string ReportStatus;
        [JsonProperty("report_id")] public string ReportId;
        [JsonProperty("overall_score")] public int? OverallScore;
        [JsonProperty("live")] public LiveInfo Live;
    }

    public sealed class InterviewList
    {
        [JsonProperty("items")] public List<InterviewSession> Items = new List<InterviewSession>();
        [JsonProperty("total")] public int Total;
        [JsonProperty("offset")] public int Offset;
        [JsonProperty("history_limited")] public bool HistoryLimited;
    }

    public sealed class CategoryScore
    {
        [JsonProperty("assessed")] public bool Assessed;
        [JsonProperty("score")] public int? Score;
        [JsonProperty("rationale")] public string Rationale;
    }

    public sealed class ConfidenceIndicator
    {
        [JsonProperty("observation")] public string Observation;
        [JsonProperty("evidence")] public string Evidence;
        [JsonProperty("suggestion")] public string Suggestion;
    }

    public sealed class AnswerFeedback
    {
        [JsonProperty("question")] public string Question;
        [JsonProperty("answer_summary")] public string AnswerSummary;
        [JsonProperty("assessment")] public string Assessment;
        [JsonProperty("feedback")] public string Feedback;
        [JsonProperty("improved_answer")] public string ImprovedAnswer;
    }

    public sealed class PracticeStep
    {
        [JsonProperty("title")] public string Title;
        [JsonProperty("activity")] public string Activity;
        [JsonProperty("duration_minutes")] public int DurationMinutes;
    }

    public sealed class InterviewReport
    {
        public static readonly string[] CategoryKeys =
            { "technical_knowledge", "communication_clarity", "answer_relevance", "problem_solving" };

        [JsonProperty("id")] public string Id;
        [JsonProperty("session_id")] public string SessionId;
        [JsonProperty("tier")] public string Tier;
        [JsonProperty("is_demo")] public bool IsDemo;
        [JsonProperty("overall_score")] public int OverallScore;
        [JsonProperty("categories")] public Dictionary<string, CategoryScore> Categories = new Dictionary<string, CategoryScore>();
        [JsonProperty("summary")] public string Summary;
        [JsonProperty("strengths")] public List<string> Strengths = new List<string>();
        [JsonProperty("improvements")] public List<string> Improvements = new List<string>();
        [JsonProperty("recommended_topics")] public List<string> RecommendedTopics = new List<string>();
        [JsonProperty("confidence_indicators")] public List<ConfidenceIndicator> ConfidenceIndicators = new List<ConfidenceIndicator>();
        [JsonProperty("answer_feedback")] public List<AnswerFeedback> AnswerFeedback = new List<AnswerFeedback>();
        [JsonProperty("practice_plan")] public List<PracticeStep> PracticePlan = new List<PracticeStep>();
        [JsonProperty("disclaimer")] public string Disclaimer;
        [JsonProperty("created_at")] public System.DateTime? CreatedAt;

        public static string CategoryTitle(string key)
        {
            switch (key)
            {
                case "technical_knowledge": return "Technical knowledge";
                case "communication_clarity": return "Communication clarity";
                case "answer_relevance": return "Answer relevance";
                case "problem_solving": return "Problem solving";
                default: return key;
            }
        }
    }

    public sealed class ReportEnvelope
    {
        [JsonProperty("status")] public string Status;
        [JsonProperty("report")] public InterviewReport Report;
    }

    public sealed class TranscriptMessage
    {
        [JsonProperty("seq")] public int Seq;
        [JsonProperty("role")] public string Role;
        [JsonProperty("text")] public string Text;
        [JsonProperty("truncated")] public bool Truncated;
    }

    public sealed class TranscriptResponse
    {
        [JsonProperty("retained")] public bool Retained;
        [JsonProperty("messages")] public List<TranscriptMessage> Messages = new List<TranscriptMessage>();
    }

    public sealed class Plan
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("title")] public string Title;
        [JsonProperty("interviews_per_month")] public int InterviewsPerMonth;
        [JsonProperty("realtime_minutes_per_month")] public int RealtimeMinutesPerMonth;
        [JsonProperty("max_duration_minutes")] public int MaxDurationMinutes;
        [JsonProperty("report_tier")] public string ReportTier;
        [JsonProperty("progress_tracking")] public bool ProgressTracking;
        [JsonProperty("features")] public List<string> Features = new List<string>();
    }

    public sealed class PlanList
    {
        [JsonProperty("plans")] public List<Plan> Plans = new List<Plan>();
    }

    public sealed class Usage
    {
        [JsonProperty("period")] public string Period;
        [JsonProperty("interviews_used")] public int InterviewsUsed;
        [JsonProperty("interviews_limit")] public int InterviewsLimit;
        [JsonProperty("interviews_remaining")] public int InterviewsRemaining;
        [JsonProperty("realtime_minutes_used")] public float RealtimeMinutesUsed;
        [JsonProperty("realtime_minutes_limit")] public int RealtimeMinutesLimit;
        [JsonProperty("realtime_minutes_remaining")] public float RealtimeMinutesRemaining;
    }

    public sealed class SubscriptionInfo
    {
        [JsonProperty("plan")] public string Plan;
        [JsonProperty("status")] public string Status;
        [JsonProperty("source")] public string Source;
        [JsonProperty("verified")] public bool Verified;
        [JsonProperty("current_period_end")] public System.DateTime? CurrentPeriodEnd;
        [JsonProperty("is_test_grant")] public bool IsTestGrant;
    }

    public sealed class SubscriptionResponse
    {
        [JsonProperty("subscription")] public SubscriptionInfo Subscription;
        [JsonProperty("plan")] public Plan Plan;
        [JsonProperty("usage")] public Usage Usage;
    }

    public sealed class ProgressSummary
    {
        [JsonProperty("interviews_completed")] public int InterviewsCompleted;
        [JsonProperty("reports")] public int Reports;
        [JsonProperty("practice_minutes")] public float PracticeMinutes;
        [JsonProperty("average_score")] public int? AverageScore;
        [JsonProperty("latest_score")] public int? LatestScore;
        [JsonProperty("first_score")] public int? FirstScore;
        [JsonProperty("improvement")] public int? Improvement;
        [JsonProperty("strongest_category")] public string StrongestCategory;
        [JsonProperty("focus_category")] public string FocusCategory;
        [JsonProperty("progress_tracking_locked")] public bool ProgressTrackingLocked;
    }

    public sealed class TrendPoint
    {
        [JsonProperty("session_id")] public string SessionId;
        [JsonProperty("date")] public System.DateTime? Date;
        [JsonProperty("overall_score")] public int OverallScore;
        [JsonProperty("interview_type")] public string InterviewType;
    }

    public sealed class CategoryProgress
    {
        [JsonProperty("average")] public int Average;
        [JsonProperty("recent_average")] public int RecentAverage;
        [JsonProperty("first")] public int First;
        [JsonProperty("latest")] public int Latest;
        [JsonProperty("change")] public int Change;
    }

    public sealed class ProgressResponse
    {
        [JsonProperty("summary")] public ProgressSummary Summary;
        [JsonProperty("trend")] public List<TrendPoint> Trend = new List<TrendPoint>();
        [JsonProperty("categories")] public Dictionary<string, CategoryProgress> Categories = new Dictionary<string, CategoryProgress>();
    }

    public sealed class ResumeDocument
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("filename")] public string Filename;
        [JsonProperty("kind")] public string Kind;
        [JsonProperty("char_count")] public int CharCount;
        [JsonProperty("uploaded_at")] public System.DateTime? UploadedAt;
        [JsonProperty("preview")] public string Preview;
    }

    public sealed class ResumeDocumentList
    {
        [JsonProperty("documents")] public List<ResumeDocument> Documents = new List<ResumeDocument>();
        [JsonProperty("uploaded")] public List<ResumeDocument> Uploaded = new List<ResumeDocument>();
        [JsonProperty("max_documents")] public int MaxDocuments = 5;
    }

    public sealed class ResumeInfo
    {
        [JsonProperty("filename")] public string Filename;
        [JsonProperty("char_count")] public int CharCount;
        [JsonProperty("uploaded_at")] public System.DateTime? UploadedAt;
        [JsonProperty("preview")] public string Preview;
    }

    public sealed class DataDeletionResult
    {
        [JsonProperty("sessions_deleted")] public int SessionsDeleted;
        [JsonProperty("reports_deleted")] public int ReportsDeleted;
    }

    /// <summary>Partial profile update; null fields are omitted from the JSON.</summary>
    public sealed class ProfileUpdate
    {
        [JsonProperty("display_name", NullValueHandling = NullValueHandling.Ignore)] public string DisplayName;
        [JsonProperty("onboarding_completed", NullValueHandling = NullValueHandling.Ignore)] public bool? OnboardingCompleted;
        [JsonProperty("preferences", NullValueHandling = NullValueHandling.Ignore)] public PreferencesUpdate Preferences;
    }

    public sealed class PreferencesUpdate
    {
        [JsonProperty("target_role_id", NullValueHandling = NullValueHandling.Ignore)] public string TargetRoleId;
        [JsonProperty("experience_level", NullValueHandling = NullValueHandling.Ignore)] public string ExperienceLevel;
        [JsonProperty("voice", NullValueHandling = NullValueHandling.Ignore)] public string Voice;
        [JsonProperty("avatar_id", NullValueHandling = NullValueHandling.Ignore)] public string AvatarId;
        [JsonProperty("show_live_transcript", NullValueHandling = NullValueHandling.Ignore)] public bool? ShowLiveTranscript;
        [JsonProperty("retain_transcripts", NullValueHandling = NullValueHandling.Ignore)] public bool? RetainTranscripts;
    }
}
