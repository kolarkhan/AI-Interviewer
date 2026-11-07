using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AIInterviewer.Core.Realtime
{
    /// <summary>
    /// App &lt;-&gt; backend live interview protocol v1 (mirror of backend/app/modules/realtime/protocol.py).
    /// Audio travels as binary WebSocket frames of PCM16 mono little-endian at 24 kHz.
    /// </summary>
    public static class LiveProtocol
    {
        public const int Version = 1;
        public const int SampleRate = 24000;

        public static class ServerEvents
        {
            public const string SessionReady = "session.ready";
            public const string UserSpeechStarted = "user.speech_started";
            public const string UserSpeechStopped = "user.speech_stopped";
            public const string TranscriptUser = "transcript.user";
            public const string AiResponseStarted = "ai.response.started";
            public const string AiAudioStarted = "ai.audio.started";
            public const string AiAudioDone = "ai.audio.done";
            public const string TranscriptAiDelta = "transcript.ai.delta";
            public const string TranscriptAiDone = "transcript.ai.done";
            public const string AiResponseDone = "ai.response.done";
            public const string InterviewTime = "interview.time";
            public const string InterviewWrappingUp = "interview.wrapping_up";
            public const string InterviewCompleted = "interview.completed";
            public const string Error = "error";
            public const string Pong = "pong";
        }

        /// <summary>WebSocket close codes used by the backend.</summary>
        public static class CloseCodes
        {
            public const int Normal = 1000;
            public const int InternalError = 1011;
            public const int BadRequest = 4400;
            public const int Unauthorized = 4401;
            public const int PlanLimit = 4402;
            public const int Forbidden = 4403;
            public const int NotFound = 4404;
            public const int Conflict = 4409;
            public const int SessionEnded = 4410;
            public const int Replaced = 4411;
            public const int RateLimited = 4429;
            public const int UpstreamUnavailable = 4503;

            /// <summary>True when reconnecting can succeed (network issues, AI hiccups).</summary>
            public static bool IsRetryable(int code)
            {
                return code == InternalError || code == UpstreamUnavailable || code == 1001 || code == 1006 ||
                       code == RateLimited;
            }
        }

        public static string Mute(bool muted) => Serialize(new JObject { ["type"] = "input.mute", ["muted"] = muted });

        public static string Cancel() => Serialize(new JObject { ["type"] = "response.cancel" });

        public static string Truncate(string itemId, int audioEndMs) =>
            Serialize(new JObject { ["type"] = "playback.truncate", ["item_id"] = itemId, ["audio_end_ms"] = audioEndMs });

        public static string End() => Serialize(new JObject { ["type"] = "interview.end" });

        public static string Ping() => Serialize(new JObject { ["type"] = "ping" });

        public static LiveServerEvent Parse(string json)
        {
            var ev = JsonConvert.DeserializeObject<LiveServerEvent>(json);
            if (ev == null || string.IsNullOrEmpty(ev.Type))
            {
                throw new JsonException("Event without type");
            }
            return ev;
        }

        private static string Serialize(JObject o) => o.ToString(Formatting.None);
    }

    /// <summary>Flat view over all server event payloads (unused fields stay null).</summary>
    public sealed class LiveServerEvent
    {
        [JsonProperty("type")] public string Type;

        // session.ready
        [JsonProperty("protocol_version")] public int? ProtocolVersion;
        [JsonProperty("session_id")] public string SessionId;
        [JsonProperty("resumed")] public bool? Resumed;
        [JsonProperty("demo")] public bool? Demo;
        [JsonProperty("interviewer_name")] public string InterviewerName;
        [JsonProperty("live_transcript")] public bool? LiveTranscript;
        [JsonProperty("audio")] public LiveAudioFormat Audio;

        // timing
        [JsonProperty("elapsed_seconds")] public int? ElapsedSeconds;
        [JsonProperty("remaining_seconds")] public int? RemainingSeconds;

        // conversation
        [JsonProperty("item_id")] public string ItemId;
        [JsonProperty("response_id")] public string ResponseId;
        [JsonProperty("text")] public string Text;
        [JsonProperty("delta")] public string Delta;
        [JsonProperty("status")] public string Status;
        [JsonProperty("interrupted")] public bool? Interrupted;
        [JsonProperty("failed")] public bool? Failed;
        [JsonProperty("audio_start_ms")] public int? AudioStartMs;

        // completion / errors
        [JsonProperty("reason")] public string Reason;
        [JsonProperty("report_status")] public string ReportStatus;
        [JsonProperty("code")] public string Code;
        [JsonProperty("message")] public string Message;
        [JsonProperty("fatal")] public bool? Fatal;
    }

    public sealed class LiveAudioFormat
    {
        [JsonProperty("format")] public string Format;
        [JsonProperty("sample_rate")] public int SampleRate;
        [JsonProperty("channels")] public int Channels;
    }
}
