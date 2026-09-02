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
        public string NetworkKey;
        public string GameId;
        public string ApiBaseUrl;
        public bool EnableDebugLogging = false;
    }

    [Serializable]
    public class WireSyndicateDeliveryData
    {
        public string placement_id;
        public string game_id;
        public string format;
        public string prominence;
        public string asset_url;
        public string asset_hash;
        public string contract_end_date;
    }

    [Serializable]
    public class WireSyndicatePayload
    {
        public bool success;
        public WireSyndicateDeliveryData data;
        public string error;
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
        public string LocalFilePath;
        public bool IsCacheHit;
        public string AssetHash;
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

            _ = WireSyndicate.SDK.WSTelemetryDispatcher.AuthenticateAsync(config.NetworkKey);

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
                Debug.Log($"[WireSyndicate] Engine initialized with NetworkKey: {config.NetworkKey}");
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
            StartCoroutine(StartLocationService());
        }

        private IEnumerator StartLocationService()
        {
#if UNITY_ANDROID
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.FineLocation))
            {
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.FineLocation);
                // Wait for the user to respond to the dialog
                yield return new WaitForSeconds(1.0f);
            }
#endif
            if (!Input.location.isEnabledByUser)
            {
                if (WireSyndicateEngine.Config.EnableDebugLogging)
                    Debug.Log("[WireSyndicate] OS Location services are disabled by the user. Graceful degradation active.");
                yield break;
            }

            Input.location.Start(500f, 500f);

            int maxWait = 10;
            while (Input.location.status == LocationServiceStatus.Initializing && maxWait > 0)
            {
                yield return new WaitForSeconds(1);
                maxWait--;
            }

            if (maxWait <= 0 || Input.location.status == LocationServiceStatus.Failed)
            {
                Debug.LogWarning("[WireSyndicate] OS Location service initialization failed or timed out. Graceful degradation active.");
                yield break;
            }
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

            string url = $"{baseUrl}/api/v1/delivery/resolve?placement_id={placementId}";

            if (Input.location.isEnabledByUser && Input.location.status == LocationServiceStatus.Running)
            {
                url += $"&lat={Input.location.lastData.latitude}&lng={Input.location.lastData.longitude}";
            }

            return url;
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

                if (response != null && response.success && response.data != null && !string.IsNullOrEmpty(response.data.asset_url))
                {
                    if (WireSyndicateEngine.Config.EnableDebugLogging)
                        Debug.Log($"[WireSyndicateEngine] Delivery resolved for {placementId}. Extracting asset URL...");
                    
                    bool isVideo = response.data.format != null && response.data.format.ToLower().Contains("video");
                    bool is3D = response.data.format != null && (response.data.format.ToLower().Contains("3d") || response.data.asset_url.EndsWith(".glb") || response.data.asset_url.EndsWith(".assetbundle"));

                    if (isVideo) {
                        AssetDeliveryResult result = new AssetDeliveryResult {
                            VideoUrl = response.data.asset_url,
                            Format = response.data.format
                        };
                        _activeAssets[response.data.placement_id] = result;
                        FulfillPendingRequests(response.data.placement_id, result);
                    } else if (is3D) {
                        StartCoroutine(LoadOrDownloadGenericAsset(response.data));
                    } else {
                        StartCoroutine(LoadOrDownloadTexture(response.data));
                    }
                }
                else
                {
                    Debug.LogError($"[WireSyndicateEngine] Failed to parse delivery resolve response or unsuccessful for {placementId}. Payload: {json}");
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

        private IEnumerator LoadOrDownloadGenericAsset(WireSyndicateDeliveryData data)
        {
            string cachedPath = WSAssetCache.GetCachedAssetPath(data.asset_hash);
            
            if (!string.IsNullOrEmpty(cachedPath))
            {
                if (WireSyndicateEngine.Config.EnableDebugLogging)
                    Debug.Log($"[WireSyndicateEngine] Asset matched hash {data.asset_hash}, loading from cache: {cachedPath}");
                
                AssetDeliveryResult result = new AssetDeliveryResult {
                    LocalFilePath = cachedPath,
                    Format = data.format,
                    IsCacheHit = true,
                    AssetHash = data.asset_hash
                };
                _activeAssets[data.placement_id] = result;
                FulfillPendingRequests(data.placement_id, result);
                yield break;
            }

            if (WireSyndicateEngine.Config.EnableDebugLogging)
                Debug.Log($"[WireSyndicateEngine] Downloading generic asset for hash {data.asset_hash} from {data.asset_url}");

            using (UnityWebRequest uwr = UnityWebRequest.Get(data.asset_url))
            {
                yield return uwr.SendWebRequest();

                if (uwr.result == UnityWebRequest.Result.Success)
                {
                    string finalPath = WSAssetCache.CacheAsset(data.asset_hash, uwr.downloadHandler.data);
                    
                    AssetDeliveryResult result = new AssetDeliveryResult {
                        LocalFilePath = finalPath,
                        Format = data.format,
                        IsCacheHit = false,
                        AssetHash = data.asset_hash
                    };
                    _activeAssets[data.placement_id] = result;
                    FulfillPendingRequests(data.placement_id, result);
                }
                else
                {
                    Debug.LogError($"[WireSyndicateEngine] Generic asset download failed for URL {data.asset_url}: {uwr.error}");
                    FulfillPendingRequests(data.placement_id, null);
                }
            }
        }

        private IEnumerator LoadOrDownloadTexture(WireSyndicateDeliveryData payload)
        {
            string safeFileName = payload.asset_url.GetHashCode().ToString() + ".png";
            string localFilePath = Path.Combine(CacheDirectory, safeFileName);
            string endDateString = payload.contract_end_date;

            Texture2D textureToApply = null;
            bool isCacheHit = false;

            if (File.Exists(localFilePath))
            {
                isCacheHit = true;
                var loadTask = LoadTextureAsync(localFilePath);
                yield return new WaitUntil(() => loadTask.IsCompleted);
                textureToApply = loadTask.Result;
            }
            else
            {
                using (UnityWebRequest uwr = UnityWebRequestTexture.GetTexture(payload.asset_url))
                {
                    yield return uwr.SendWebRequest();

                    if (uwr.result == UnityWebRequest.Result.Success)
                    {
                        textureToApply = DownloadHandlerTexture.GetContent(uwr);
                        File.WriteAllBytes(localFilePath, uwr.downloadHandler.data);
                        RegisterDownloadedAsset(payload.asset_url, safeFileName, endDateString);
                    }
                    else
                    {
                        Debug.LogError($"[WireSyndicateEngine] Texture download failed for URL {payload.asset_url}: {uwr.error}");
                    }
                }
            }

            if (textureToApply != null)
            {
                AssetDeliveryResult result = new AssetDeliveryResult {
                    Texture = textureToApply,
                    Format = payload.format,
                    IsCacheHit = isCacheHit,
                    AssetHash = payload.asset_hash
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
