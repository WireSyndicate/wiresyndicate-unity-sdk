using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace WireSyndicate
{
    public class WireSyndicateClient : MonoBehaviour
    {
        // TODO: Map this to your actual Next.js edge API URL in production
        private const string API_BASE_URL = "https://api.wiresyndicate.com/api/v1"; 
        
        private static WireSyndicateClient _instance;
        private string _developerApiKey;

        public static WireSyndicateClient Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject go = new GameObject("[WireSyndicate_EdgeClient]");
                    _instance = go.AddComponent<WireSyndicateClient>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        public void Initialize(string apiKey)
        {
            _developerApiKey = apiKey;
            Debug.Log("[WireSyndicate] Client Initialized.");
        }

        /// <summary>
        /// Fetches the highest bidding 3D asset and issues the cryptographic handshake token.
        /// </summary>
        public async Task<AssetResponse> FetchAssetAsync(string placementId)
        {
            if (string.IsNullOrEmpty(_developerApiKey))
            {
                Debug.LogError("[WireSyndicate] API Key is missing. Call Initialize() first.");
                return null;
            }

            string url = $"{API_BASE_URL}/placements/fill?placement_id={placementId}";
            
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("Authorization", $"Bearer {_developerApiKey}");
                
                var operation = request.SendWebRequest();
                
                // Yield execution back to Unity's main thread until the edge responds
                while (!operation.isDone)
                    await Task.Yield();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[WireSyndicate] Failed to fetch asset: {request.error}");
                    return null;
                }

                // Unity's native JsonUtility ensures zero dependency bloat
                return JsonUtility.FromJson<AssetResponse>(request.downloadHandler.text);
            }
        }

        /// <summary>
        /// Validates physical rendering constraints and burns the impression token.
        /// </summary>
        public async Task<bool> SendTelemetryAsync(string token, int durationMs, float screenCoverage)
        {
            string url = $"{API_BASE_URL}/telemetry/ingest";
            
            TelemetryPayload payload = new TelemetryPayload
            {
                impression_token = token,
                duration_ms = durationMs,
                screen_coverage = screenCoverage
            };

            string json = JsonUtility.ToJson(payload);
            
            using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", $"Bearer {_developerApiKey}");

                var operation = request.SendWebRequest();
                
                while (!operation.isDone)
                    await Task.Yield();

                // A 409 Conflict (Replay Attack) or 401 Unauthorized will trip this logic
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[WireSyndicate] Zero-Trust Perimeter Rejected Telemetry: {request.error}");
                    return false;
                }

                Debug.Log("[WireSyndicate] Token burned. Financial clearing executed.");
                return true;
            }
        }
    }
}
