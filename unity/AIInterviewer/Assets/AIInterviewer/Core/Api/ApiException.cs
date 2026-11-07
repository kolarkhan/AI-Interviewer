using System;
using Newtonsoft.Json;

namespace AIInterviewer.Core.Api
{
    public enum ApiFailureKind
    {
        Network,        // no connection / DNS / timeout
        Unauthorized,   // 401 (session expired or revoked)
        PlanLimit,      // 402 (upgrade or wait)
        NotFound,
        Conflict,
        Validation,     // 422
        RateLimited,    // 429
        Server,         // 5xx
        Other
    }

    /// <summary>Error from the backend, parsed from the uniform error envelope.</summary>
    public sealed class ApiException : Exception
    {
        public ApiException(int statusCode, string code, string message, ApiFailureKind kind, string rawDetails = null)
            : base(message)
        {
            StatusCode = statusCode;
            Code = code;
            Kind = kind;
            RawDetails = rawDetails;
        }

        public int StatusCode { get; }
        public string Code { get; }
        public ApiFailureKind Kind { get; }
        public string RawDetails { get; }

        public bool IsRetryable => Kind == ApiFailureKind.Network || Kind == ApiFailureKind.Server ||
                                   Kind == ApiFailureKind.RateLimited;

        public static ApiException Network(string message) =>
            new ApiException(0, "network_error", message ?? "No connection", ApiFailureKind.Network);

        public static ApiException FromResponse(int status, string body)
        {
            string code = "http_" + status, message = DefaultMessage(status), details = null;
            if (!string.IsNullOrEmpty(body))
            {
                try
                {
                    var parsed = JsonConvert.DeserializeObject<ApiErrorBody>(body);
                    if (parsed?.Error != null)
                    {
                        code = parsed.Error.Code ?? code;
                        message = string.IsNullOrEmpty(parsed.Error.Message) ? message : parsed.Error.Message;
                        details = parsed.Error.Details?.ToString(Formatting.None);
                    }
                }
                catch (JsonException)
                {
                    // Non-JSON error page (e.g. proxy). Keep the default message.
                }
            }
            return new ApiException(status, code, message, KindFor(status), details);
        }

        public static ApiFailureKind KindFor(int status)
        {
            if (status == 0) return ApiFailureKind.Network;
            if (status == 401) return ApiFailureKind.Unauthorized;
            if (status == 402) return ApiFailureKind.PlanLimit;
            if (status == 404) return ApiFailureKind.NotFound;
            if (status == 409) return ApiFailureKind.Conflict;
            if (status == 422) return ApiFailureKind.Validation;
            if (status == 429) return ApiFailureKind.RateLimited;
            if (status >= 500) return ApiFailureKind.Server;
            return ApiFailureKind.Other;
        }

        private static string DefaultMessage(int status)
        {
            if (status >= 500) return "The server had a problem. Please try again.";
            if (status == 429) return "Too many requests. Please wait a moment.";
            return "Request failed (" + status + ")";
        }
    }
}
