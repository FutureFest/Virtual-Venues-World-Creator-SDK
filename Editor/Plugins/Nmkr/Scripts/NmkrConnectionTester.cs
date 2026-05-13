using System.Threading.Tasks;
using UnityEngine.Networking;
using VirtualVenues.Plugins.Nmkr;

namespace VirtualVenues.Plugins.Nmkr.Editor
{
    public static class NmkrConnectionTester
    {
        private const string PREPROD_URL = "https://studio-api.preprod.nmkr.io/v2";
        private const string MAINNET_URL = "https://studio-api.nmkr.io/v2";

        // Endpoint chosen because (a) it requires auth (Bearer apiKey),
        // (b) it returns user-scoped data so a valid response proves the key
        // is bound to the account, (c) it's already in the NMKR SDK so we
        // know it's stable across versions.
        private const string TEST_ENDPOINT = "/GetPayoutWallets";

        public struct TestResult
        {
            public bool Success;
            public string Message;
        }

        public static async Task<TestResult> TestAsync(int customerId, string apiKey, NmkrEnvironment env)
        {
            if (string.IsNullOrEmpty(apiKey)) { return new TestResult { Success = false, Message = "API Key is empty." }; }

            string baseUrl = env == NmkrEnvironment.Mainnet ? MAINNET_URL : PREPROD_URL;
            string url = baseUrl + TEST_ENDPOINT;

            using UnityWebRequest req = UnityWebRequest.Get(url);
            req.SetRequestHeader("Authorization", $"Bearer {apiKey}");
            req.SetRequestHeader("accept", "text/plain");

            UnityWebRequestAsyncOperation op = req.SendWebRequest();
            while (!op.isDone) { await Task.Yield(); }

            if (req.result == UnityWebRequest.Result.Success)
            {
                return new TestResult { Success = true, Message = $"Connection OK ({env}, customer={customerId})." };
            }

            long code = req.responseCode;
            if (code == 401) { return new TestResult { Success = false, Message = $"Unauthorized (401) — check API key for {env}." }; }
            if (code == 403) { return new TestResult { Success = false, Message = $"Forbidden (403) — API key lacks required permission." }; }
            if (code == 429) { return new TestResult { Success = false, Message = "Rate limited (429) — wait and retry." }; }

            string err = string.IsNullOrEmpty(req.error) ? "Unknown error" : req.error;
            return new TestResult { Success = false, Message = $"{err} (HTTP {code})." };
        }
    }
}
