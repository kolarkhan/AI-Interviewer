using System;
using System.Threading.Tasks;
using AIInterviewer.Core.Api;
using AIInterviewer.Platform;
using UnityEngine;

namespace AIInterviewer.Net
{
    /// <summary>
    /// Owns authentication state. The access token lives in memory only; the refresh token is kept in
    /// the platform keystore (Android Keystore / iOS Keychain). Refreshes are single-flight.
    /// </summary>
    public sealed class AuthSession
    {
        private const string RefreshKey = "refresh_token";

        private readonly ApiClient _api;
        private readonly ISecureStorage _storage;
        private string _accessToken;
        private DateTime _accessExpiresUtc;
        private Task<bool> _refreshInFlight;

        public AuthSession(ApiClient api, ISecureStorage storage)
        {
            _api = api;
            _storage = storage;
            _api.AccessTokenProvider = () => _accessToken;
            _api.RefreshHandler = RefreshAsync;
        }

        public UserProfile User { get; private set; }
        public bool IsSignedIn => User != null && !string.IsNullOrEmpty(_accessToken);
        public bool HasStoredSession => !string.IsNullOrEmpty(_storage.Get(RefreshKey));

        /// <summary>Raised when the session ends (logout, revoked or expired refresh token).</summary>
        public event Action SignedOut;

        public event Action<UserProfile> UserChanged;

        public async Task<UserProfile> RegisterAsync(string email, string password, string displayName)
        {
            var res = await _api.PostAnonymousAsync<AuthResponse>("/v1/auth/register",
                new { email, password, display_name = displayName, accept_terms = true });
            Apply(res);
            return User;
        }

        public async Task<UserProfile> LoginAsync(string email, string password)
        {
            var res = await _api.PostAnonymousAsync<AuthResponse>("/v1/auth/login", new { email, password });
            Apply(res);
            return User;
        }

        /// <summary>Restores a session from the stored refresh token. Returns false if the user must sign in.</summary>
        public async Task<bool> TryRestoreAsync()
        {
            if (!HasStoredSession) return false;
            if (!await RefreshAsync()) return false;
            try
            {
                SetUser(await _api.GetAsync<UserProfile>("/v1/me"));
                return true;
            }
            catch (ApiException ex) when (ex.Kind == ApiFailureKind.Unauthorized)
            {
                ClearLocal();
                return false;
            }
        }

        /// <summary>Returns a valid access token, refreshing if it expires within 60 seconds.</summary>
        public async Task<string> GetFreshAccessTokenAsync()
        {
            if (string.IsNullOrEmpty(_accessToken) || DateTime.UtcNow > _accessExpiresUtc.AddSeconds(-60))
            {
                await RefreshAsync();
            }
            return _accessToken;
        }

        public Task<bool> RefreshAsync()
        {
            if (_refreshInFlight != null && !_refreshInFlight.IsCompleted) return _refreshInFlight;
            _refreshInFlight = RefreshCoreAsync();
            return _refreshInFlight;
        }

        private async Task<bool> RefreshCoreAsync()
        {
            string refresh = _storage.Get(RefreshKey);
            if (string.IsNullOrEmpty(refresh)) return false;
            try
            {
                var pair = await _api.PostAnonymousAsync<TokenPair>("/v1/auth/refresh", new { refresh_token = refresh });
                StoreTokens(pair);
                return true;
            }
            catch (ApiException ex) when (ex.Kind == ApiFailureKind.Unauthorized || ex.Kind == ApiFailureKind.Validation)
            {
                Debug.Log("[Auth] Refresh rejected: " + ex.Code);
                ClearLocal();
                SignedOut?.Invoke();
                return false;
            }
        }

        public async Task LogoutAsync()
        {
            string refresh = _storage.Get(RefreshKey);
            if (!string.IsNullOrEmpty(refresh))
            {
                try
                {
                    await _api.PostAnonymousAsync<object>("/v1/auth/logout", new { refresh_token = refresh });
                }
                catch (ApiException)
                {
                    // Best effort: local sign-out happens regardless.
                }
            }
            ClearLocal();
            SignedOut?.Invoke();
        }

        /// <summary>Clears local credentials after account deletion.</summary>
        public void ForgetLocally()
        {
            ClearLocal();
            SignedOut?.Invoke();
        }

        public void SetUser(UserProfile user)
        {
            User = user;
            UserChanged?.Invoke(user);
        }

        private void Apply(AuthResponse res)
        {
            StoreTokens(res.Tokens);
            SetUser(res.User);
        }

        private void StoreTokens(TokenPair pair)
        {
            _accessToken = pair.AccessToken;
            _accessExpiresUtc = pair.AccessTokenExpiresAt.ToUniversalTime();
            _storage.Set(RefreshKey, pair.RefreshToken);
        }

        private void ClearLocal()
        {
            _accessToken = null;
            User = null;
            _storage.Delete(RefreshKey);
        }
    }
}
