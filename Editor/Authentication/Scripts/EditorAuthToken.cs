using System;

namespace Auth0
{
    /// <summary>
    /// Single source of truth for the editor tools' OAuth access token. Every editor API client
    /// (AvatarPublisherApi, WorldPublisherApi, BuildUploaderApi) delegates its token surface here so
    /// the tools can never drift to different cached tokens.
    /// </summary>
    public static class EditorAuthToken
    {
        public static string AccessToken { get; private set; }

        public static DateTime AccessTokenExpiry { get; private set; }

        public static bool IsValid => !string.IsNullOrEmpty(AccessToken) && DateTime.UtcNow < AccessTokenExpiry;

        public static void Set(string token, DateTime expiry)
        {
            AccessToken = token;
            AccessTokenExpiry = expiry;
        }

        public static void Clear()
        {
            AccessToken = null;
            AccessTokenExpiry = DateTime.MinValue;
        }
    }
}
