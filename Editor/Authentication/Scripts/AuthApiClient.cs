using Auth0.AuthenticationApi;
using Auth0.AuthenticationApi.Models;
using Auth0.Core.Exceptions;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Debug = UnityEngine.Debug;

namespace Auth0.Api
{
    /// <summary>
    /// Client for the Auth0 Authentication API.
    /// </summary>
    /// <remarks>
    /// Full documentation on the Auth0 Authentication API is available at https://auth0.com/docs/api/authentication
    /// </remarks>
    public class AuthApiClient : AuthenticationApiClient 
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AuthApiClient" /> class.
        /// </summary>
        /// <param name="domain">Your Auth0 domain name, e.g. tenant.auth0.com.</param>
        public AuthApiClient(string domain) : base(new Uri(String.Format("https://{0}", domain)))
        {
        }

        /// <summary>
        /// A helper methods to poll your Auth0 Authorization Server for an access token using the time period specified by retryInterval.
        /// The method continues polling until either the user completes the browser flow path, the user code expires or an unexpected error is returned by Auth0.
        /// </summary>
        /// <param name="clientId">Your Auth0 client id.</param>
        /// <param name="deviceCode">Device code to be exchanged.</param>
        /// <param name="retryInterval">The interval (in seconds) to poll the token URL to request a token.</param>
        /// <param name="cancellationToken">The cancellation token to cancel operation.</param>
        /// <returns><see cref="Task"/> representing the async operation containing a valid <see cref="AccessTokenResponse" /> with the requested tokens.</returns>
        public async Task<AccessTokenResponse> ExchangeDeviceCodeAsync(string clientId, string deviceCode, int retryInterval, CancellationToken cancellationToken = default)
        {
            AccessTokenResponse response = null;
            ErrorApiException apiError;

            var request = new DeviceCodeTokenRequest
            {
                ClientId = clientId,
                DeviceCode = deviceCode
            };

            var sw = Stopwatch.StartNew();
            int pollCount = 0;
            Debug.Log($"[Auth] ExchangeDeviceCode polling start, interval={retryInterval}s");

            do
            {
                await Task.Delay(TimeSpan.FromSeconds(retryInterval));
                apiError = null;
                pollCount++;

                try
                {
                    response = await this.GetTokenAsync(request, cancellationToken);
                    Debug.Log($"[Auth] ExchangeDeviceCode poll #{pollCount} — token received, totalMs={sw.ElapsedMilliseconds}");
                }
                catch (ErrorApiException ex)
                {
                    apiError = ex;
                    Debug.Log($"[Auth] ExchangeDeviceCode poll #{pollCount} — {ex.ApiError?.ErrorCode ?? "unknown"}, totalMs={sw.ElapsedMilliseconds}");
                }
            } while((apiError != null && apiError.ApiError.ErrorCode != "authorization_pending") || response == null);

            if (apiError != null)
            {
                Debug.Log($"[Auth] ExchangeDeviceCode failed after {pollCount} polls — totalMs={sw.ElapsedMilliseconds}, error={apiError.ApiError?.ErrorCode}");
                throw apiError;
            }

            Debug.Log($"[Auth] ExchangeDeviceCode success — totalMs={sw.ElapsedMilliseconds}, polls={pollCount}");
            return response;
        }
    }
}
