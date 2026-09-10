using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace WireSyndicate.SDK
{
    [System.Serializable]
    public class TelemetryPayload
    {
        public string impression_id;
        public string placement_id;
        public string campaign_id;
        public string bid_id;
        public string session_id;
        public string rendered_at;
        public string player_origin_geom;
        public string camera_frustum_geom;
        public string client_signature;
    }

    public class WSTelemetryDispatcher : MonoBehaviour
    {
        public static WSTelemetryDispatcher Instance { get; private set; }

        [Tooltip("The UUID of this specific game, registered in the Developer Dashboard.")]
        public string gameId;

        private static string _sessionToken;
        private static string _handshakeSecret;
        private static bool _isAuthenticated = false;
        private static string _sessionId = System.Guid.NewGuid().ToString();

        public static string SessionId => _sessionId;
        public static string HandshakeSecret => _handshakeSecret;

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
        private List<TelemetryPayload> _batchList = new List<TelemetryPayload>();
        private float _batchTimer = 0f;

        public void DispatchImpression(string placementId, string campaignId, string bidId, Vector3 playerOrigin, Vector3[] frustumCorners)
        {
            if (!_isAuthenticated) return;
            if (string.IsNullOrEmpty(gameId))
            {
                Debug.LogWarning("[WSTelemetryDispatcher] GameId is not configured. Aborting telemetry dispatch.");
                return;
            }

            string impressionId = System.Guid.NewGuid().ToString();
            string renderedAt = System.DateTime.UtcNow.ToString("O"); // ISO 8601

            string playerOriginGeom = $"SRID=0;POINT Z({playerOrigin.x} {playerOrigin.y} {playerOrigin.z})";
            
            string frustumGeom = "SRID=0;POLYGON Z((";
            if (frustumCorners != null && frustumCorners.Length >= 3)
            {
                for (int i = 0; i < frustumCorners.Length; i++)
                {
                    frustumGeom += $"{frustumCorners[i].x} {frustumCorners[i].y} {frustumCorners[i].z}, ";
                }
                // Close the polygon
                frustumGeom += $"{frustumCorners[0].x} {frustumCorners[0].y} {frustumCorners[0].z}";
            }
            else
            {
                frustumGeom += "0 0 0, 1 0 0, 1 1 0, 0 1 0, 0 0 0"; // Fallback dummy
            }
            frustumGeom += "))";

            string payloadToSign = $"{impressionId}:{placementId}:{campaignId}:{bidId}:{renderedAt}";
            string clientSignature = WSCryptography.GenerateHMAC(payloadToSign, _handshakeSecret);

            var payload = new TelemetryPayload
            {
                impression_id = impressionId,
                placement_id = placementId,
                campaign_id = campaignId,
                bid_id = bidId,
                session_id = _sessionId,
                rendered_at = renderedAt,
                player_origin_geom = playerOriginGeom,
                camera_frustum_geom = frustumGeom,
                client_signature = clientSignature
            };
            
            _dispatchQueue.Enqueue(payload);
        }

        private int _activeRequests = 0;
        private const int MaxConcurrentRequests = 5;

        private void Start()
        {
            // Initialize the background worker on startup
            StartCoroutine(OfflineCacheDrainWorker());
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            // Immediately attempt re-hydration when the user returns to the game 
            // (e.g., pulling the app out of background state on mobile)
            if (hasFocus)
            {
                StartCoroutine(AttemptRehydration());
            }
        }

        private void Update()
        {
            _batchTimer += Time.deltaTime;

            while (_dispatchQueue.TryDequeue(out var payload))
            {
                _batchList.Add(payload);
            }

            if (_batchList.Count > 0 && (_batchTimer >= 5f || _batchList.Count >= 500))
            {
                if (_activeRequests < MaxConcurrentRequests)
                {
                    _activeRequests++;
                    var batchCopy = new List<TelemetryPayload>(_batchList);
                    _batchList.Clear();
                    _batchTimer = 0f;
                    StartCoroutine(DispatchRoutine(batchCopy));
                }
            }
        }

        private IEnumerator DispatchRoutine(List<TelemetryPayload> batch)
        {
            if (!_isAuthenticated)
            {
                Debug.LogError("[WireSyndicate] Cannot dispatch telemetry: SDK lacks a valid session token.");
                _activeRequests--;
                yield break;
            }

            List<string> jsonItems = new List<string>();
            foreach (var item in batch)
            {
                jsonItems.Add(JsonUtility.ToJson(item));
            }
            string jsonPayload = "[" + string.Join(",", jsonItems) + "]";
            
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

                if (request.responseCode == 202)
                {
                    Debug.Log($"[WireSyndicate] Telemetry queued successfully at Edge (202 Accepted). Batch Size: {batch.Count}");
                }
                else
                {
                    Debug.LogError($"[WireSyndicate] Edge Ingestion Failed ({request.responseCode}): {request.error}. Triggering local fallback queue.");
                    // Serialize the dropped payloads to disk for offline recovery
                    foreach (var payload in batch)
                    {
                        WireSyndicate.SDK.Telemetry.WSOfflineTelemetryCache.CachePayload(payload);
                    }
                }
            }

            // Release the concurrency semaphore
            _activeRequests--;
        }

        private IEnumerator OfflineCacheDrainWorker()
        {
            // Poll every 60 seconds
            WaitForSeconds waitInterval = new WaitForSeconds(60f);
            while (true)
            {
                yield return waitInterval;
                yield return AttemptRehydration();
            }
        }

        private IEnumerator AttemptRehydration()
        {
            // Abort instantly if no internet connection is detected by the OS
            if (Application.internetReachability == NetworkReachability.NotReachable)
            {
                yield break;
            }

            var cachedPayloads = WireSyndicate.SDK.Telemetry.WSOfflineTelemetryCache.GetAndClearCache();
            if (cachedPayloads.Count == 0) yield break;

            Debug.Log($"[WireSyndicate] Network restored. Draining {cachedPayloads.Count} recovered payloads from offline cache.");

            foreach (var payload in cachedPayloads)
            {
                // Route recovered payloads back through the standard Edge ingestion pipeline
                // The Update loop will batch them back up
                _dispatchQueue.Enqueue(payload);
            }
        }
    }
}
