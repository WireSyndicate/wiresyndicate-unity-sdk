using System;
using System.Collections;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace WireSyndicate.SDK
{
    [System.Serializable]
    public class TelemetryPayload
    {
        public string placementId;
        public string gameId;
        public int durationMs;
        public float screenCoverage;
    }

    public class WSTelemetryDispatcher : MonoBehaviour
    {
        public static WSTelemetryDispatcher Instance { get; private set; }

        [Tooltip("Optional. Leave blank to use Production Edge URL.")]
        public string apiBaseUrl;

        [Tooltip("The UUID of this specific game, registered in the Developer Dashboard.")]
        public string gameId;

        private static string _sessionToken;
        private static string _handshakeSecret;
        private static bool _isAuthenticated = false;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        public static async Task<bool> AuthenticateAsync(string networkKey)
        {
            string baseUrl = Instance != null && !string.IsNullOrWhiteSpace(Instance.apiBaseUrl)
                ? Instance.apiBaseUrl.Trim()
                : "https://api.wiresyndicate.com/api/v1";

            if (!baseUrl.StartsWith("http://") && !baseUrl.StartsWith("https://"))
            {
                Debug.LogError("[WireSyndicate] Configuration Error: API Base URL is missing or malformed. Please configure the network settings in the WireSyndicate Manager.");
                return false;
            }

            string handshakeUrl = baseUrl.TrimEnd('/') + "/auth/handshake";

            HandshakeRequest requestData = new HandshakeRequest { network_key = networkKey };
            string jsonBody = JsonUtility.ToJson(requestData);

            using (UnityWebRequest request = new UnityWebRequest(handshakeUrl, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");

                var operation = request.SendWebRequest();
                while (!operation.isDone) await Task.Yield();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[WireSyndicate] FATAL: Zero-Trust Handshake Failed. {request.error}");
                    return false;
                }

                var response = JsonUtility.FromJson<HandshakeResponse>(request.downloadHandler.text);
                
                if (response.success)
                {
                    _sessionToken = response.session_token;
                    _handshakeSecret = response.handshake_secret;
                    _isAuthenticated = true;
                    Debug.Log("[WireSyndicate] Cryptographic Handshake Established.");
                    return true;
                }

                return false;
            }
        }

        public void DispatchImpression(string placementId, float durationSeconds, float screenCoverage)
        {
            if (string.IsNullOrEmpty(gameId))
            {
                Debug.LogWarning("[WSTelemetryDispatcher] GameId is not configured. Aborting telemetry dispatch.");
                return;
            }

            int durationMs = Mathf.RoundToInt(durationSeconds * 1000f);
            
            // Dispatch async without waiting in the synchronous method
            _ = DispatchImpressionAsync(placementId, durationMs, screenCoverage);
        }

        private async Task<bool> DispatchImpressionAsync(string placementId, int durationMs, float screenCoverage)
        {
            if (!_isAuthenticated)
            {
                Debug.LogError("[WireSyndicate] Cannot dispatch telemetry: SDK lacks a valid session token.");
                return false;
            }

            TelemetryPayload payload = new TelemetryPayload
            {
                placementId = placementId,
                gameId = gameId,
                durationMs = durationMs,
                screenCoverage = screenCoverage
            };
            string jsonPayload = JsonUtility.ToJson(payload);

            string signature = WSCryptography.GenerateHMAC(jsonPayload, _handshakeSecret);

            string baseUrl = Instance != null && !string.IsNullOrWhiteSpace(Instance.apiBaseUrl)
                ? Instance.apiBaseUrl.Trim()
                : "https://api.wiresyndicate.com/api/v1";
                
            string telemetryUrl = baseUrl.TrimEnd('/') + "/telemetry/impressions";

            using (UnityWebRequest request = new UnityWebRequest(telemetryUrl, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                
                request.SetRequestHeader("Content-Type", "application/json");
                
                request.SetRequestHeader("Authorization", $"Bearer {_sessionToken}");
                request.SetRequestHeader("X-WS-Signature", signature);

                var operation = request.SendWebRequest();
                while (!operation.isDone) await Task.Yield();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[WireSyndicate] Perimeter Rejected Telemetry: {request.error}");
                    return false;
                }

                Debug.Log("[WireSyndicate] Signed Token burned. Financial clearing executed.");
                return true;
            }
        }
    }
}
