using System;
using System.Collections;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace WireSyndicate.SDK
{
    [System.Serializable]
    public class SpatialData
    {
        public float distance_to_camera;
        public float angle_of_incidence;
        public float on_screen_percentage;
        public float occlusion_percentage;
    }

    [System.Serializable]
    public class TelemetryPayload
    {
        public string impression_token;
        public string placementId;
        public string bid_id;
        public int durationMs;
        public float screenCoverage;
        public SpatialData spatial_data;
    }

    public class WSTelemetryDispatcher : MonoBehaviour
    {
        public static WSTelemetryDispatcher Instance { get; private set; }


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
            string baseUrl = WireSyndicateInitializer.Instance != null && !string.IsNullOrWhiteSpace(WireSyndicateInitializer.Instance.apiBaseUrl)
                ? WireSyndicateInitializer.Instance.apiBaseUrl.Trim()
                : "";

            if (string.IsNullOrEmpty(baseUrl) || !baseUrl.StartsWith("http"))
            {
                Debug.LogError("[WireSyndicate] Configuration Error: API Base URL is missing or malformed. Please configure the API URL on the WireSyndicate Initializer.");
                return false;
            }

            string handshakeUrl = baseUrl.TrimEnd('/') + "/api/v1/network/handshake";

            using (UnityWebRequest request = UnityWebRequest.Get(handshakeUrl))
            {
                request.SetRequestHeader("Authorization", $"Bearer {networkKey}");
                request.SetRequestHeader("Content-Type", "application/json");

                var operation = request.SendWebRequest();
                while (!operation.isDone) await Task.Yield();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    if (request.responseCode == 403)
                    {
                        Debug.LogError("[WireSyndicate] FATAL: Handshake rejected. Organization account is suspended or banned. Edge delivery halted.");
                    }
                    else
                    {
                        Debug.LogError($"[WireSyndicate] FATAL: Zero-Trust Handshake Failed. {request.error}");
                    }
                    return false;
                }

                var response = JsonUtility.FromJson<HandshakeResponse>(request.downloadHandler.text);
                
                if (response.success)
                {
                    _sessionToken = response.session_token;
                    _handshakeSecret = response.handshake_secret;
                    _isAuthenticated = true;
                    Debug.Log("[WireSyndicate] Zero-Trust Handshake successful. Edge network synced.");
                    return true;
                }

                return false;
            }
        }

        private System.Collections.Concurrent.ConcurrentQueue<TelemetryPayload> _dispatchQueue = new System.Collections.Concurrent.ConcurrentQueue<TelemetryPayload>();

        public void DispatchImpression(string impressionToken, string placementId, string bidId, float durationSec, float screenCoverage, SpatialData spatialData)
        {
            if (!_isAuthenticated) return;

            var payload = new TelemetryPayload
            {
                impression_token = impressionToken,
                placementId = placementId,
                bid_id = bidId,
                durationMs = Mathf.RoundToInt(durationSec * 1000f),
                screenCoverage = screenCoverage,
                spatial_data = spatialData
            };
            _dispatchQueue.Enqueue(payload);
        }

        private void Update()
        {
            // Dequeue on the main thread
            while (_dispatchQueue.TryDequeue(out var payload))
            {
                StartCoroutine(DispatchRoutine(payload));
            }
        }

        private IEnumerator DispatchRoutine(TelemetryPayload payload)
        {
            if (!_isAuthenticated)
            {
                Debug.LogError("[WireSyndicate] Cannot dispatch telemetry: SDK lacks a valid session token.");
                yield break;
            }

            string jsonPayload = JsonUtility.ToJson(payload);
            string signature = WSCryptography.GenerateHMAC(jsonPayload, _handshakeSecret);

            string baseUrl = WireSyndicateInitializer.Instance != null && !string.IsNullOrWhiteSpace(WireSyndicateInitializer.Instance.apiBaseUrl)
                ? WireSyndicateInitializer.Instance.apiBaseUrl.Trim()
                : "";
                
            string telemetryUrl = baseUrl.TrimEnd('/') + "/api/v1/telemetry/impressions";

            using (UnityWebRequest request = new UnityWebRequest(telemetryUrl, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", $"Bearer {_sessionToken}");
                request.SetRequestHeader("X-WS-Signature", signature);

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[WireSyndicate] Perimeter Rejected Telemetry: {request.error}");
                    yield break;
                }

                Debug.Log("[WireSyndicate] Signed Token burned. Financial clearing executed.");
            }
        }
    }
}
