using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace WireSyndicate.Core
{
    public enum WireEnvironment
    {
        Development,
        Staging,
        Production
    }

    public class WireSyndicateConfig
    {
        public string OrgId;
        public string GameId;
        public string ApiBaseUrl;
        public bool EnableDebugLogging = false;
    }

    [Serializable]
    public class WireSyndicateCreativeData
    {
        public string bid_id;
        public string asset_url;
        public string format;
    }

    [Serializable]
    public class WireSyndicatePayload
    {
        public string placement_id;
        public WireSyndicateCreativeData creative;
        public int ttl_seconds;
    }

    [Serializable]
    public class WireSyndicateCacheEntry 
    {
        public string urlHash;
        public string localFileName;
        public string contractEndDate; // ISO 8601 string
    }

    [Serializable]
    public class WireSyndicateCacheManifest 
    {
        public List<WireSyndicateCacheEntry> entries = new List<WireSyndicateCacheEntry>();
    }

    public class AssetDeliveryResult
    {
        public Texture2D Texture;
        public string VideoUrl;
        public string Format;
        public string BidId;
    }

    public static class WireSyndicateEngine
    {
        private static WireSyndicateCoreBehaviour _coreBehaviour;
        public static WireSyndicateConfig Config { get; private set; }

        public static void Initialize(WireSyndicateConfig config)
        {
            if (_coreBehaviour != null)
            {
                if (config.EnableDebugLogging)
                    Debug.LogWarning("[WireSyndicate] Engine is already initialized.");
                return;
            }

            Config = config;

            _ = WireSyndicate.SDK.WSTelemetryDispatcher.AuthenticateAsync(config.OrgId);

            if (WireSyndicate.SDK.WSTelemetryDispatcher.Instance == null)
            {
                GameObject telemetryObj = new GameObject("[WireSyndicate_Telemetry]");
                UnityEngine.Object.DontDestroyOnLoad(telemetryObj);
                var dispatcher = telemetryObj.AddComponent<WireSyndicate.SDK.WSTelemetryDispatcher>();
                dispatcher.gameId = config.GameId;
            }

            GameObject coreObj = new GameObject("[WireSyndicate_InternalEngine]");
            UnityEngine.Object.DontDestroyOnLoad(coreObj);
            _coreBehaviour = coreObj.AddComponent<WireSyndicateCoreBehaviour>();
            
            if (config.EnableDebugLogging)
                Debug.Log($"[WireSyndicate] Engine initialized with OrgId: {config.OrgId}");
        }

        public static void RequestAsset(string placementId, Action<AssetDeliveryResult> onAssetLoaded)
        {
            if (_coreBehaviour == null)
            {
                Debug.LogError("[WireSyndicate] Engine is not initialized! Cannot request asset.");
                return;
            }

            _coreBehaviour.RequestAsset(placementId, onAssetLoaded);
        }
    }

    public class WireSyndicateCoreBehaviour : MonoBehaviour
    {
        private string CacheDirectory => Path.Combine(Application.persistentDataPath, "WireSyndicate_Cache");
        private string ManifestPath => Path.Combine(CacheDirectory, "WireSyndicate_manifest.json");

        private WireSyndicateCacheManifest _manifest;
        private Dictionary<string, AssetDeliveryResult> _activeAssets = new Dictionary<string, AssetDeliveryResult>();
        private Dictionary<string, List<Action<AssetDeliveryResult>>> _pendingRequests = new Dictionary<string, List<Action<AssetDeliveryResult>>>();

        private void Awake()
        {
            InitializeCache();
            PurgeExpiredCache();
        }

        private void InitializeCache()
        {
            if (!Directory.Exists(CacheDirectory))
            {
                Directory.CreateDirectory(CacheDirectory);
            }

            if (File.Exists(ManifestPath))
            {
                string json = File.ReadAllText(ManifestPath);
                _manifest = JsonUtility.FromJson<WireSyndicateCacheManifest>(json) ?? new WireSyndicateCacheManifest();
            }
            else
            {
                _manifest = new WireSyndicateCacheManifest();
            }
        }

        private void PurgeExpiredCache()
        {
            if (_manifest == null || _manifest.entries.Count == 0) return;

            DateTime currentTime = DateTime.UtcNow;
            List<WireSyndicateCacheEntry> validEntries = new List<WireSyndicateCacheEntry>();
            bool manifestChanged = false;

            foreach (var entry in _manifest.entries)
            {
                if (DateTime.TryParse(entry.contractEndDate, out DateTime endDate))
                {
                    if (currentTime > endDate)
                    {
                        string filePath = Path.Combine(CacheDirectory, entry.localFileName);
                        if (File.Exists(filePath))
                        {
                            try
                            {
                                File.Delete(filePath);
                            }
                            catch (Exception) {}
                        }
                        manifestChanged = true;
                    }
                    else
                    {
                        validEntries.Add(entry);
                    }
                }
            }

            if (manifestChanged)
            {
                _manifest.entries = validEntries;
                SaveManifest();
            }
        }

        private void RegisterDownloadedAsset(string url, string localFileName, string endDateString)
        {
            string hash = url.GetHashCode().ToString();
            _manifest.entries.RemoveAll(e => e.urlHash == hash);

            _manifest.entries.Add(new WireSyndicateCacheEntry
            {
                urlHash = hash,
                localFileName = localFileName,
                contractEndDate = endDateString
            });

            SaveManifest();
        }

        private void SaveManifest()
        {
            string json = JsonUtility.ToJson(_manifest, true);
            File.WriteAllText(ManifestPath, json);
        }

        private string GetResolveUrl(string placementId)
        {
            string baseUrl = !string.IsNullOrEmpty(WireSyndicateEngine.Config.ApiBaseUrl)
                ? WireSyndicateEngine.Config.ApiBaseUrl.Trim().TrimEnd('/')
                : "https://api.wiresyndicate.com";

            return $"{baseUrl}/api/v1/delivery/resolve?placement_id={placementId}";
        }

        private IEnumerator ResolvePlacement(string placementId)
        {
            string url = GetResolveUrl(placementId);
            if (WireSyndicateEngine.Config.EnableDebugLogging)
                Debug.Log($"[WireSyndicateEngine] Resolving delivery for placement '{placementId}' at: {url}...");

            using (UnityWebRequest webRequest = UnityWebRequest.Get(url))
            {
                yield return webRequest.SendWebRequest();

                if (webRequest.responseCode == 204)
                {
                    Debug.LogWarning($"[WireSyndicateEngine] 204 No Content for {placementId}: No active campaigns won the waterfall. Awaiting fallback.");
                    FulfillPendingRequests(placementId, null);
                    yield break;
                }

                if (webRequest.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[WireSyndicateEngine] API Error ({webRequest.responseCode}): {webRequest.error}");
                    FulfillPendingRequests(placementId, null);
                    yield break;
                }

                string json = webRequest.downloadHandler.text;
                WireSyndicatePayload response = null;
                try {
                    response = JsonUtility.FromJson<WireSyndicatePayload>(json);
                } catch(Exception e) {
                    Debug.LogError($"[WireSyndicateEngine] JSON Parse Error: {e.Message}");
                }

                if (response != null && response.creative != null && !string.IsNullOrEmpty(response.creative.asset_url))
                {
                    if (WireSyndicateEngine.Config.EnableDebugLogging)
                        Debug.Log($"[WireSyndicateEngine] Delivery resolved for {placementId}. Extracting asset URL...");
                    
                    bool isVideo = response.creative.format != null && response.creative.format.ToLower().Contains("video");

                    if (isVideo) {
                        AssetDeliveryResult result = new AssetDeliveryResult {
                            VideoUrl = response.creative.asset_url,
                            Format = response.creative.format,
                            BidId = response.creative.bid_id
                        };
                        _activeAssets[response.placement_id] = result;
                        FulfillPendingRequests(response.placement_id, result);
                    } else {
                        StartCoroutine(LoadOrDownloadTexture(response));
                    }
                }
                else
                {
                    Debug.LogError($"[WireSyndicateEngine] Failed to parse delivery resolve response for {placementId}. Payload: {json}");
                    FulfillPendingRequests(placementId, null);
                }
            }
        }

        private async System.Threading.Tasks.Task<Texture2D> LoadTextureAsync(string filePath)
        {
            string uri = "file://" + filePath.Replace("\\", "/");

            using (UnityWebRequest uwr = UnityWebRequestTexture.GetTexture(uri))
            {
                var asyncOperation = uwr.SendWebRequest();

                while (!asyncOperation.isDone)
                {
                    await System.Threading.Tasks.Task.Yield();
                }

                if (uwr.result != UnityWebRequest.Result.Success)
                {
                    if (WireSyndicateEngine.Config.EnableDebugLogging)
                        Debug.LogError($"[WireSyndicate] Failed to load cached texture asynchronously: {uwr.error}");
                    return null;
                }

                return DownloadHandlerTexture.GetContent(uwr);
            }
        }

        private IEnumerator LoadOrDownloadTexture(WireSyndicatePayload payload)
        {
            string safeFileName = payload.creative.asset_url.GetHashCode().ToString() + ".png";
            string localFilePath = Path.Combine(CacheDirectory, safeFileName);
            string endDateString = DateTime.UtcNow.AddSeconds(payload.ttl_seconds).ToString("o");

            Texture2D textureToApply = null;

            if (File.Exists(localFilePath))
            {
                var loadTask = LoadTextureAsync(localFilePath);
                yield return new WaitUntil(() => loadTask.IsCompleted);
                textureToApply = loadTask.Result;
            }
            else
            {
                using (UnityWebRequest uwr = UnityWebRequestTexture.GetTexture(payload.creative.asset_url))
                {
                    yield return uwr.SendWebRequest();

                    if (uwr.result == UnityWebRequest.Result.Success)
                    {
                        textureToApply = DownloadHandlerTexture.GetContent(uwr);
                        File.WriteAllBytes(localFilePath, uwr.downloadHandler.data);
                        RegisterDownloadedAsset(payload.creative.asset_url, safeFileName, endDateString);
                    }
                    else
                    {
                        Debug.LogError($"[WireSyndicateEngine] Texture download failed for URL {payload.creative.asset_url}: {uwr.error}");
                    }
                }
            }

            if (textureToApply != null)
            {
                AssetDeliveryResult result = new AssetDeliveryResult {
                    Texture = textureToApply,
                    Format = payload.creative.format,
                    BidId = payload.creative.bid_id
                };
                _activeAssets[payload.placement_id] = result;
                FulfillPendingRequests(payload.placement_id, result);
            } else {
                FulfillPendingRequests(payload.placement_id, null);
            }
        }

        public void RequestAsset(string placementId, Action<AssetDeliveryResult> onAssetLoaded)
        {
            placementId = placementId != null ? placementId.Trim() : "";

            if (_activeAssets.ContainsKey(placementId))
            {
                onAssetLoaded?.Invoke(_activeAssets[placementId]);
            }
            else
            {
                bool isFirstRequest = !_pendingRequests.ContainsKey(placementId);
                
                if (isFirstRequest)
                {
                    _pendingRequests[placementId] = new List<Action<AssetDeliveryResult>>();
                }
                _pendingRequests[placementId].Add(onAssetLoaded);

                if (isFirstRequest)
                {
                    StartCoroutine(ResolvePlacement(placementId));
                }
            }
        }

        private void FulfillPendingRequests(string placementId, AssetDeliveryResult result)
        {
            if (_pendingRequests.ContainsKey(placementId))
            {
                foreach (var callback in _pendingRequests[placementId])
                {
                    callback?.Invoke(result);
                }
                _pendingRequests.Remove(placementId);
            }
        }
    }
}
