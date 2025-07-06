using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Auth0;
using Auth0.AuthenticationApi.Models;
using System.Linq;
using System;
using System.Threading.Tasks;
using System.Security.Cryptography;
//using ImaginationOverflow.UniversalDeepLinking;
using Auth0.AuthenticationApi.Builders;
using Auth0.AuthenticationApi;
using System.Text;

public class PkceAuthenticationFlow
{
    public static void RequestAuthCodePkce()
    {
        var client = AuthManager.Instance.Auth0;

        RandomNumberGenerator rng = RandomNumberGenerator.Create();
        byte[] verifyCode = new byte[32];
        rng.GetBytes(verifyCode);
        string codeVerifier = Convert.ToBase64String(verifyCode).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        PlayerPrefs.SetString(PlayerPrefKeys.CODE_VERIFIER_KEY, codeVerifier);
        string redirectUri = "futurefest://auth0redirect";

        string codeChallenge = GenerateCodeChallenge(codeVerifier);

        AuthorizationUrlBuilder builder = AuthenticationApiClientExtensions.BuildAuthorizationUrl(client);
        builder.WithResponseType(AuthorizationResponseType.Code);
        builder.WithValue("code_challenge", codeChallenge);
        builder.WithValue("code_challenge_method", "S256");
        builder.WithClient(AuthManager.Instance.Settings.ClientId);
        builder.WithRedirectUrl(redirectUri);
        builder.WithScope(AuthManager.Instance.Settings.Scope);
        builder.WithAudience(AuthManager.Instance.Settings.Audience);
        builder.WithState("xyzABC123");

        Uri authUrl = builder.Build();

        Application.OpenURL(authUrl.AbsoluteUri);
        Application.Quit();
    }

    public static async Task GetAccessToken(string code, Action onSuccess, Action<string> onFailure) // second part
    {
        try
        {
            var auth0 = AuthManager.Instance.Auth0;
            var clientId = AuthManager.Instance.Settings.ClientId;
            var scope = AuthManager.Instance.Settings.Scope;
            var audience = AuthManager.Instance.Settings.Audience;
            string redirectUri = "futurefest://auth0redirect";

            string codeVerifier = PlayerPrefs.GetString(PlayerPrefKeys.CODE_VERIFIER_KEY);
            int retryInterval = 5; // retry in seconds

            AccessTokenResponse tokenResp = await auth0.GetAccessTokenPkceAsync(clientId, codeVerifier, code, redirectUri, retryInterval, onFailure: onFailure);
            AuthManager.Instance.Credentials.SaveCredentials(tokenResp, scope);

            if(!string.IsNullOrEmpty(tokenResp.AccessToken) && !string.IsNullOrEmpty(tokenResp.IdToken))
            {
                Debug.Log($"Auth success!");
                onSuccess?.Invoke();
            }
            else
            {
                Exception exception = new Exception("Failed to authenticate! Empty Tokens. Try relaunching. (ERROR=1)");
                Debug.LogException(exception);
                onFailure?.Invoke("Failed to authenticate! Empty Tokens. Try relaunching. (ERROR=1)");
            }
        }
        catch (Exception ex)
        {
            Exception exception = new Exception($"Failed to authenticate! Try relaunching. (ERROR=2). {ex}");
            Debug.LogException(exception);
            onFailure?.Invoke("Failed to authenticate! Try relaunching. (ERROR=2)");
        }
    }

    public static string GenerateCodeChallenge(string codeVerifier)
    {
        // Generate code_challenge from code_verifier using SHA256
        using (var sha256 = SHA256.Create())
        {
            var challengeBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(codeVerifier));
            var challenge = Base64UrlEncode(challengeBytes);
            return challenge;
        }
    }

    private static string Base64UrlEncode(byte[] input)
    {
        // Base64UrlEncode implementation
        var base64 = Convert.ToBase64String(input);
        var base64Url = base64.TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return base64Url;
    }
}
