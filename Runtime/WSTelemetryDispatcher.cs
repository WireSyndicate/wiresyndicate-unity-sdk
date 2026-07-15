using System.Collections;
using System.Text;
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

        [Header("Configuration")]
        [Tooltip("The Edge network telemetry endpoint.")]
        public string telemetryEndpoint = "https://api.wiresyndicate.com/api/v1/telemetry/impressions";

        [Tooltip("The UUID of this specific game, registered in the Developer Dashboard.")]
        public string gameId;

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

        public void DispatchImpression(string placementId, float durationSeconds, float screenCoverage)
        {
            if (string.IsNullOrEmpty(gameId))
            {
                Debug.LogWarning("[WSTelemetryDispatcher] GameId is not configured. Aborting telemetry dispatch.");
                return;
            }

            TelemetryPayload payload = new TelemetryPayload
            {
                placementId = placementId,
                gameId = gameId,
                durationMs = Mathf.RoundToInt(durationSeconds * 1000f),
                screenCoverage = screenCoverage
            };

            string jsonPayload = JsonUtility.ToJson(payload);
            StartCoroutine(PostTelemetryRoutine(jsonPayload));
        }

        private IEnumerator PostTelemetryRoutine(string jsonPayload)
        {
            using (UnityWebRequest request = new UnityWebRequest(telemetryEndpoint, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");

                // Fire and forget logic - we yield until done, but don't block the main thread.
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[WSTelemetryDispatcher] Telemetry drop. Error: {request.error}");
                }
                else
                {
                    Debug.Log($"[WSTelemetryDispatcher] Impression dispatched successfully.");
                }
            }
        }
    }
}
