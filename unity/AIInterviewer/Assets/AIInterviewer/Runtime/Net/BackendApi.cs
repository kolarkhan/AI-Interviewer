using System.Collections.Generic;
using System.Threading.Tasks;
using AIInterviewer.Core.Api;

namespace AIInterviewer.Net
{
    /// <summary>Typed wrappers for the backend REST endpoints (see docs/API.md).</summary>
    public sealed class BackendApi
    {
        private readonly ApiClient _api;

        public BackendApi(ApiClient api)
        {
            _api = api;
        }

        public Task<UserProfile> GetMeAsync() => _api.GetAsync<UserProfile>("/v1/me");

        public Task<UserProfile> UpdateMeAsync(ProfileUpdate update) => _api.PatchAsync<UserProfile>("/v1/me", update);

        public Task<DataDeletionResult> DeleteMyDataAsync() => _api.DeleteAsync<DataDeletionResult>("/v1/me/data");

        public Task DeleteAccountAsync(string password) =>
            _api.PostAsync<object>("/v1/me/delete-account", new { password, confirm = true });

        public Task<Catalog> GetCatalogAsync() => _api.GetAsync<Catalog>("/v1/catalog");

        /// <summary>WAV sample of an interviewer voice. The first request for a voice may take up to ~40 s (generated once).</summary>
        public Task<byte[]> GetVoicePreviewAsync(string voiceId) =>
            _api.GetBytesAsync("/v1/catalog/voices/" + System.Uri.EscapeDataString(voiceId) + "/preview",
                "audio/wav, application/json", 60);

        public Task<PlanList> GetPlansAsync() => _api.GetAsync<PlanList>("/v1/billing/plans");

        public Task<SubscriptionResponse> GetSubscriptionAsync() =>
            _api.GetAsync<SubscriptionResponse>("/v1/billing/subscription");

        /// <summary>Development builds only: grants a test premium plan (endpoint is disabled in production).</summary>
        public Task<SubscriptionResponse> DevGrantPremiumAsync() =>
            _api.PostAsync<SubscriptionResponse>("/v1/billing/dev/grant-premium");

        public Task<SubscriptionResponse> DevResetFreeAsync() =>
            _api.PostAsync<SubscriptionResponse>("/v1/billing/dev/reset-free");

        public Task<InterviewSession> CreateInterviewAsync(InterviewConfig config) =>
            _api.PostAsync<InterviewSession>("/v1/interviews", config.CloneForRequest());

        public Task<InterviewList> ListInterviewsAsync(int limit = 20, int offset = 0) =>
            _api.GetAsync<InterviewList>($"/v1/interviews?limit={limit}&offset={offset}");

        public Task<InterviewSession> GetInterviewAsync(string id) => _api.GetAsync<InterviewSession>($"/v1/interviews/{id}");

        public Task DeleteInterviewAsync(string id) => _api.DeleteAsync($"/v1/interviews/{id}");

        public Task<InterviewSession> CompleteInterviewAsync(string id, string reason) =>
            _api.PostAsync<InterviewSession>($"/v1/interviews/{id}/complete", new { reason });

        public Task<ReportEnvelope> GetReportAsync(string id) => _api.GetAsync<ReportEnvelope>($"/v1/interviews/{id}/report");

        public Task<ReportEnvelope> RegenerateReportAsync(string id) =>
            _api.PostAsync<ReportEnvelope>($"/v1/interviews/{id}/report");

        public Task<TranscriptResponse> GetTranscriptAsync(string id) =>
            _api.GetAsync<TranscriptResponse>($"/v1/interviews/{id}/transcript");

        public Task<ProgressResponse> GetProgressAsync() => _api.GetAsync<ProgressResponse>("/v1/progress");

        public Task<ResumeDocumentList> ListResumesAsync() => _api.GetAsync<ResumeDocumentList>("/v1/resumes");

        /// <summary>Uploads one or more PDF/DOCX/TXT documents (all-or-nothing on the server).</summary>
        public Task<ResumeDocumentList> UploadResumeFilesAsync(IList<(string fileName, byte[] data)> files) =>
            _api.UploadFilesAsync<ResumeDocumentList>("/v1/resumes", "files", files,
                new Dictionary<string, string> { { "consent", "true" } });

        public Task<ResumeDocumentList> UploadResumeTextAsync(string text) =>
            UploadResumeFilesAsync(new List<(string, byte[])> { ("pasted-text.txt", System.Text.Encoding.UTF8.GetBytes(text)) });

        public Task DeleteResumeAsync(string documentId) => _api.DeleteAsync($"/v1/resumes/{documentId}");

        public Task DeleteAllResumesAsync() => _api.DeleteAsync("/v1/resumes");
    }
}
