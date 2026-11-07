using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using AIInterviewer.Core.Api;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace AIInterviewer.Net
{
    /// <summary>
    /// Minimal async JSON client over UnityWebRequest (supports HTTP/2, proxies and platform TLS on mobile).
    /// Must be used from the main thread. Authentication and token refresh are handled by <see cref="AuthSession"/>
    /// through the <see cref="AccessTokenProvider"/> / <see cref="RefreshHandler"/> hooks.
    /// </summary>
    public sealed class ApiClient
    {
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Include,
            DateParseHandling = DateParseHandling.DateTime
        };

        private readonly string _baseUrl;

        public ApiClient(string baseUrl)
        {
            _baseUrl = baseUrl.TrimEnd('/');
        }

        public int TimeoutSeconds { get; set; } = 20;

        /// <summary>Returns the current access token (or null when signed out).</summary>
        public Func<string> AccessTokenProvider { get; set; }

        /// <summary>Called once on a 401 `token_expired`; returns true if the token was refreshed.</summary>
        public Func<Task<bool>> RefreshHandler { get; set; }

        public Task<T> GetAsync<T>(string path) => SendAsync<T>("GET", path, null);

        public Task<T> PostAsync<T>(string path, object body = null) => SendAsync<T>("POST", path, body);

        public Task<T> PatchAsync<T>(string path, object body) => SendAsync<T>("PATCH", path, body);

        public Task DeleteAsync(string path) => SendAsync<object>("DELETE", path, null);

        public Task<T> DeleteAsync<T>(string path) => SendAsync<T>("DELETE", path, null);

        /// <summary>Unauthenticated request (login, register, refresh).</summary>
        public Task<T> PostAnonymousAsync<T>(string path, object body) =>
            SendCoreAsync<T>("POST", path, Serialize(body), "application/json", authenticated: false, allowRefresh: false);

        public async Task<T> UploadAsync<T>(string path, string fieldName, string fileName, byte[] data,
            Dictionary<string, string> fields)
        {
            var form = new List<IMultipartFormSection>();
            foreach (var kv in fields) form.Add(new MultipartFormDataSection(kv.Key, kv.Value));
            form.Add(new MultipartFormFileSection(fieldName, data, fileName, "application/octet-stream"));
            byte[] boundary = UnityWebRequest.GenerateBoundary();
            byte[] body = UnityWebRequest.SerializeFormSections(form, boundary);
            string contentType = "multipart/form-data; boundary=" + Encoding.UTF8.GetString(boundary);
            return await SendCoreAsync<T>("POST", path, body, contentType, authenticated: true, allowRefresh: true);
        }

        /// <summary>Multipart upload of several files under the same field name.</summary>
        public async Task<T> UploadFilesAsync<T>(string path, string fieldName, IList<(string fileName, byte[] data)> files,
            Dictionary<string, string> fields)
        {
            var form = new List<IMultipartFormSection>();
            foreach (var kv in fields) form.Add(new MultipartFormDataSection(kv.Key, kv.Value));
            foreach (var (fileName, data) in files)
            {
                form.Add(new MultipartFormFileSection(fieldName, data, fileName, "application/octet-stream"));
            }
            byte[] boundary = UnityWebRequest.GenerateBoundary();
            byte[] body = UnityWebRequest.SerializeFormSections(form, boundary);
            string contentType = "multipart/form-data; boundary=" + Encoding.UTF8.GetString(boundary);
            return await SendCoreAsync<T>("POST", path, body, contentType, authenticated: true, allowRefresh: true);
        }

        private Task<T> SendAsync<T>(string method, string path, object body)
        {
            byte[] payload = body == null ? null : Serialize(body);
            return SendCoreAsync<T>(method, path, payload, "application/json", authenticated: true, allowRefresh: true);
        }

        private static byte[] Serialize(object body) =>
            body == null ? null : Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(body, JsonSettings));

        private async Task<T> SendCoreAsync<T>(string method, string path, byte[] payload, string contentType,
            bool authenticated, bool allowRefresh)
        {
            var (status, text, _, networkError) = await RawAsync(method, path, payload, contentType, authenticated);
            if (networkError != null) throw ApiException.Network(networkError);

            if (status == 401 && authenticated && allowRefresh && RefreshHandler != null)
            {
                var err = ApiException.FromResponse((int)status, text);
                if (err.Code == "token_expired" || err.Code == "token_missing")
                {
                    if (await RefreshHandler())
                    {
                        (status, text, _, networkError) = await RawAsync(method, path, payload, contentType, true);
                        if (networkError != null) throw ApiException.Network(networkError);
                    }
                }
            }

            if (status < 200 || status >= 300) throw ApiException.FromResponse((int)status, text);
            if (typeof(T) == typeof(object) || string.IsNullOrEmpty(text)) return default;
            try
            {
                return JsonConvert.DeserializeObject<T>(text, JsonSettings);
            }
            catch (JsonException ex)
            {
                Debug.LogError($"[Api] Could not parse response from {path}: {ex.Message}");
                throw new ApiException((int)status, "bad_response", "Unexpected response from server", ApiFailureKind.Other);
            }
        }

        /// <summary>Authenticated GET of a binary resource (e.g. audio). Errors use the usual JSON envelope.</summary>
        public async Task<byte[]> GetBytesAsync(string path, string accept, int timeoutSeconds)
        {
            var (status, text, data, networkError) = await RawAsync("GET", path, null, null, true, accept, timeoutSeconds);
            if (networkError != null) throw ApiException.Network(networkError);
            if (status == 401 && RefreshHandler != null)
            {
                var err = ApiException.FromResponse((int)status, text);
                if ((err.Code == "token_expired" || err.Code == "token_missing") && await RefreshHandler())
                {
                    (status, text, data, networkError) = await RawAsync("GET", path, null, null, true, accept, timeoutSeconds);
                    if (networkError != null) throw ApiException.Network(networkError);
                }
            }
            if (status < 200 || status >= 300) throw ApiException.FromResponse((int)status, text);
            return data;
        }

        private async Task<(long status, string text, byte[] data, string networkError)> RawAsync(string method,
            string path, byte[] payload, string contentType, bool authenticated, string accept = "application/json",
            int timeoutSeconds = 0)
        {
            using (var req = new UnityWebRequest(_baseUrl + path, method))
            {
                req.downloadHandler = new DownloadHandlerBuffer();
                if (payload != null)
                {
                    req.uploadHandler = new UploadHandlerRaw(payload) { contentType = contentType };
                    req.SetRequestHeader("Content-Type", contentType);
                }
                req.SetRequestHeader("Accept", accept);
                req.timeout = timeoutSeconds > 0 ? timeoutSeconds : TimeoutSeconds;
                if (authenticated)
                {
                    string token = AccessTokenProvider?.Invoke();
                    if (!string.IsNullOrEmpty(token)) req.SetRequestHeader("Authorization", "Bearer " + token);
                }

                var op = req.SendWebRequest();
                var tcs = new TaskCompletionSource<bool>();
                op.completed += _ => tcs.TrySetResult(true);
                await tcs.Task;

                if (req.result == UnityWebRequest.Result.ConnectionError)
                {
                    return (0, null, null, req.error);
                }
                return (req.responseCode, req.downloadHandler.text, req.downloadHandler.data, null);
            }
        }
    }
}
