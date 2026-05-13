using Auth0.AuthenticationApi.Models;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Threading.Tasks;
using Debug = UnityEngine.Debug;
using PlayerPrefs = UnityEngine.PlayerPrefs;

namespace Auth0.Api.Credentials
{
    /// <summary>
    /// An implementation of <see cref="BaseCredentialsManager"/> that uses <see cref="PlayerPrefs"/> as storage.
    /// </summary>
    public class PlayerPrefsCredentialsManager : BaseCredentialsManager
    {
        private const string KEY_ACCESS_TOKEN = "com.auth0.access_token";
        private const string KEY_REFRESH_TOKEN = "com.auth0.refresh_token";
        private const string KEY_ID_TOKEN = "com.auth0.id_token";
        private const string KEY_EXPIRES_AT = "com.auth0.expires_at";
        private const string KEY_SCOPE = "com.auth0.scope";
        
        private readonly AuthApiClient auth0;

        private readonly string clientId;

        /// <summary>
        /// Initializes a new instance of the <see cref="PlayerPrefsCredentialsManager" /> class.
        /// </summary>
        /// <param name="authApiClient">An instance of <see cref="AuthApiClient"/>.</param>
        /// <param name="clientId">Your Auth0 client id.</param>
        public PlayerPrefsCredentialsManager(AuthApiClient authApiClient, string clientId)
        {
            this.auth0 = authApiClient;
            this.clientId = clientId;
        }

        /// <inheritdoc />
        public override bool HasValidCredentials()
        {
            var sw = Stopwatch.StartNew();
            var accessToken = PlayerPrefs.GetString(KEY_ACCESS_TOKEN);
            var refreshToken = PlayerPrefs.GetString(KEY_REFRESH_TOKEN);
            var idToken = PlayerPrefs.GetString(KEY_ID_TOKEN);
            var expiresAt = PlayerPrefs.GetString(KEY_EXPIRES_AT);

            var emptyCredentials = string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(expiresAt);
            if (emptyCredentials)
            {
                Debug.Log($"[Auth] HasValidCredentials=false (empty) — {sw.ElapsedMilliseconds}ms, accessTokenPresent={!string.IsNullOrWhiteSpace(accessToken)}, expiresAtPresent={!string.IsNullOrWhiteSpace(expiresAt)}");
                return false;
            }

            var accessTokenExpired = DateTime.Parse(expiresAt, CultureInfo.InvariantCulture).CompareTo(DateTime.UtcNow) <= 0;
            if (accessTokenExpired && string.IsNullOrWhiteSpace(refreshToken))
            {
                Debug.Log($"[Auth] HasValidCredentials=false (expired, no refresh token) — {sw.ElapsedMilliseconds}ms, expiresAt={expiresAt}");
                return false;
            }

            Debug.Log($"[Auth] HasValidCredentials=true — {sw.ElapsedMilliseconds}ms, expiresAt={expiresAt}, expired={accessTokenExpired}, hasRefresh={!string.IsNullOrWhiteSpace(refreshToken)}");
            return true;
        }

        /// <inheritdoc />
        public override void ClearCredentials()
        {
            PlayerPrefs.DeleteKey(KEY_ACCESS_TOKEN);
            PlayerPrefs.DeleteKey(KEY_REFRESH_TOKEN);
            PlayerPrefs.DeleteKey(KEY_ID_TOKEN);
            PlayerPrefs.DeleteKey(KEY_EXPIRES_AT);
            PlayerPrefs.DeleteKey(KEY_SCOPE);
        }

        /// <inheritdoc />
        public override bool TryGetCachedCredentials(out Credentials credentials)
        {
            var accessToken = PlayerPrefs.GetString(KEY_ACCESS_TOKEN);
            var refreshToken = PlayerPrefs.GetString(KEY_REFRESH_TOKEN);
            var idToken = PlayerPrefs.GetString(KEY_ID_TOKEN);
            var expiresAtStr = PlayerPrefs.GetString(KEY_EXPIRES_AT);
            var scope = PlayerPrefs.GetString(KEY_SCOPE);

            if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(expiresAtStr))
            {
                credentials = null;
                return false;
            }

            if (!DateTime.TryParse(expiresAtStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiresAt))
            {
                credentials = null;
                return false;
            }

            if (expiresAt.CompareTo(DateTime.UtcNow) <= 0)
            {
                credentials = null;
                return false;
            }

            credentials = new Credentials(accessToken, expiresAt, refreshToken, idToken, scope);
            return true;
        }

        /// <inheritdoc />
        public override async Task<Credentials> GetCredentials()
        {
            var sw = Stopwatch.StartNew();
            Debug.Log("[Auth] GetCredentials start");

            var accessToken = PlayerPrefs.GetString(KEY_ACCESS_TOKEN);
            var refreshToken = PlayerPrefs.GetString(KEY_REFRESH_TOKEN);
            var idToken = PlayerPrefs.GetString(KEY_ID_TOKEN);
            var expiresAtStr = PlayerPrefs.GetString(KEY_EXPIRES_AT);
            var scope = PlayerPrefs.GetString(KEY_SCOPE);

            var emptyCredentials = string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(expiresAtStr);
            if (emptyCredentials)
            {
                Debug.Log($"[Auth] GetCredentials throwing — no credentials stored, {sw.ElapsedMilliseconds}ms");
                throw new InvalidOperationException("No Credentials were previously set.");
            }

            var expiresAt = DateTime.Parse(expiresAtStr, CultureInfo.InvariantCulture);
            var accessTokenExpired = expiresAt.CompareTo(DateTime.UtcNow) <= 0;
            if (!accessTokenExpired) {
                Debug.Log($"[Auth] GetCredentials returning cached credentials — {sw.ElapsedMilliseconds}ms, expiresAt={expiresAt:O}");
                return new Credentials(accessToken, expiresAt, refreshToken, idToken, scope);
            }

            // Access token is expired, use refresh token to request a new one.
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                Debug.Log($"[Auth] GetCredentials throwing — token expired and no refresh token, {sw.ElapsedMilliseconds}ms");
                throw new InvalidOperationException("Credentials need to be renewed but no Refresh Token is available to renew them.");
            }

            try
            {
                Debug.Log($"[Auth] GetCredentials refresh token request start — {sw.ElapsedMilliseconds}ms");
                var refreshSw = Stopwatch.StartNew();
                var tokenResp = await this.auth0.GetTokenAsync(new RefreshTokenRequest
                {
                    ClientId = this.clientId,
                    RefreshToken = refreshToken
                });
                Debug.Log($"[Auth] GetCredentials refresh token request done — {refreshSw.ElapsedMilliseconds}ms");
                var renewedCredentials = AccessTokenResponseToCredentials(tokenResp, scope);

                SaveCredentials(renewedCredentials);
                Debug.Log($"[Auth] GetCredentials renewed and saved — totalMs={sw.ElapsedMilliseconds}, newExpiresAt={renewedCredentials.ExpiresAt:O}");
                return renewedCredentials;
            }
            catch (Exception ex)
            {
                Debug.Log($"[Auth] GetCredentials refresh failed — {sw.ElapsedMilliseconds}ms, {ex.GetType().Name}: {ex.Message}");
                throw new ApplicationException("An error occurred while trying to use the Refresh Token to renew the Credentials.", ex);
            }
        }

        /// <inheritdoc />
        public override void SaveCredentials(AccessTokenResponse tokenResponse, string scope)
        {
            SaveCredentials(AccessTokenResponseToCredentials(tokenResponse, scope));
        }

        private static void SaveCredentials(Credentials credentials)
        {
            PlayerPrefs.SetString(KEY_ACCESS_TOKEN, credentials.AccessToken);
            PlayerPrefs.SetString(KEY_REFRESH_TOKEN, credentials.RefreshToken);
            PlayerPrefs.SetString(KEY_ID_TOKEN, credentials.IdToken);
            PlayerPrefs.SetString(KEY_EXPIRES_AT, credentials.ExpiresAt.ToString(CultureInfo.InvariantCulture));
            PlayerPrefs.SetString(KEY_SCOPE, credentials.Scope);
        }

        private static Credentials AccessTokenResponseToCredentials(AccessTokenResponse tokenResponse, string scope)
        {
            var jwtHandler = new JwtSecurityTokenHandler();

            var accessToken = jwtHandler.ReadJwtToken(tokenResponse.AccessToken);
            DateTime accessTokenExpiration = accessToken.ValidTo;

            var idToken = jwtHandler.ReadJwtToken(tokenResponse.IdToken);
            DateTime idTokenExpiration = idToken.ValidTo;

            DateTime soonerExpiration = DateTime.Compare(accessTokenExpiration, idTokenExpiration) < 0 ? accessTokenExpiration : idTokenExpiration;

            return new Credentials(
                tokenResponse.AccessToken,
                soonerExpiration,
                tokenResponse.RefreshToken,
                tokenResponse.IdToken,
                scope
            );
        }
    }
}
