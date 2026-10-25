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
    public class SDKTrackingData
    {
        public string impression_token;
        public string click_token;
    }

    [Serializable]
    public class SDKDeliveryResponse
    {
        public string placement_id;
        public string content_type;
        public string asset_url;
        public string click_url;
        public string format;
        public string prominence;
        public string monetization_mode;
        public SDKTrackingData tracking;
    }

    [Serializable]
    public class WireSyndicateDeliveryData
    {
        public string placement_id;
        public string content_type;
        public string game_id;
        public string format;
        public string prominence;
        public string asset_url;
        public string click_url;
        public string promo_code;
        public string cta_overlay_text;
        public string monetization_mode;
        public string asset_hash;
        public string contract_end_date;
        public SDKTrackingData tracking;
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
        public string ContentType;
        public string ClickUrl;
        public string PromoCode;
        public string CtaOverlayText;
        public string MonetizationMode;
        public string ImpressionToken;
        public string ClickToken;
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

        public static void RequestAsset(WireSyndicate.SDK.WSPlacementNode placementNode, Action<AssetDeliveryResult> onAssetLoaded, System.Threading.CancellationToken ct = default)
        {
            if (_coreBehaviour == null)
            {
                Debug.LogError("[WireSyndicate] Engine is not initialized! Cannot request asset.");
                return;
            }

            _coreBehaviour.RequestAsset(placementNode, onAssetLoaded, ct);
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

        private IEnumerator ResolvePlacement(WireSyndicate.SDK.WSPlacementNode placementNode)
        {
            string placementId = placementNode.placementId;
            string baseUrl = !string.IsNullOrEmpty(WireSyndicateEngine.Config.ApiBaseUrl)
                ? WireSyndicateEngine.Config.ApiBaseUrl.Trim().TrimEnd('/')
                : "https://api.wiresyndicate.com";

            string url = $"{baseUrl}/api/v1/sdk/delivery?placement_id={UnityWebRequest.EscapeURL(placementId)}";
            
            if (WireSyndicateEngine.Config.EnableDebugLogging)
                Debug.Log($"[WireSyndicateEngine] Resolving Live-Ops delivery for placement '{placementId}' at: {url}...");

            using (UnityWebRequest webRequest = UnityWebRequest.Get(url))
            {
                string networkKey = WireSyndicateEngine.Config.NetworkKey ?? "";
                if (!string.IsNullOrEmpty(networkKey))
                {
                    webRequest.SetRequestHeader("Authorization", $"Bearer {networkKey}");
                }
                webRequest.SetRequestHeader("Accept", "application/json");

                yield return webRequest.SendWebRequest();

                if (webRequest.responseCode == 204)
                {
                    Debug.LogWarning($"[WireSyndicateEngine] 204 No Content for {placementId}: No active campaigns won the waterfall and no house asset configured.");
                    FulfillPendingRequests(placementId, null);
                    yield break;
                }

                if (webRequest.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[WireSyndicateEngine] Delivery API Error ({webRequest.responseCode}): {webRequest.error}");
                    FulfillPendingRequests(placementId, null);
                    yield break;
                }

                string json = webRequest.downloadHandler.text;
                SDKDeliveryResponse response = null;
                try {
                    response = JsonUtility.FromJson<SDKDeliveryResponse>(json);
                } catch(Exception e) {
                    Debug.LogError($"[WireSyndicateEngine] JSON Parse Error for {placementId}: {e.Message}");
                }

                if (response != null && !string.IsNullOrEmpty(response.asset_url))
                {
                    if (WireSyndicateEngine.Config.EnableDebugLogging)
                        Debug.Log($"[WireSyndicateEngine] Delivery resolved for {placementId}. ContentType: {response.content_type}, Format: {response.format}, URL: {response.asset_url}");
                    
                    bool isVideo = response.format != null && response.format.ToLower().Contains("video");
                    bool is3D = response.format != null && (response.format.ToLower().Contains("3d") || response.asset_url.EndsWith(".glb") || response.asset_url.EndsWith(".assetbundle"));

                    WireSyndicateDeliveryData deliveryData = new WireSyndicateDeliveryData
                    {
                        placement_id = response.placement_id,
                        content_type = response.content_type,
                        format = response.format,
                        prominence = response.prominence,
                        asset_url = response.asset_url,
                        click_url = response.click_url,
                        promo_code = response.promo_code,
                        cta_overlay_text = response.cta_overlay_text,
                        monetization_mode = response.monetization_mode,
                        asset_hash = response.asset_url.GetHashCode().ToString(),
                        tracking = response.tracking
                    };

                    if (isVideo) {
                        AssetDeliveryResult result = new AssetDeliveryResult {
                            VideoUrl = response.asset_url,
                            Format = response.format,
                            ContentType = response.content_type,
                            ClickUrl = response.click_url,
                            PromoCode = response.promo_code,
                            CtaOverlayText = response.cta_overlay_text,
                            MonetizationMode = response.monetization_mode,
                            ImpressionToken = response.tracking != null ? response.tracking.impression_token : null,
                            ClickToken = response.tracking != null ? response.tracking.click_token : null
                        };
                        _activeAssets[response.placement_id] = result;
                        FulfillPendingRequests(response.placement_id, result);
                    } else if (is3D) {
                        StartCoroutine(LoadOrDownloadGenericAsset(deliveryData));
                    } else {
                        StartCoroutine(LoadOrDownloadTexture(deliveryData));
                    }
                }
                else
                {
                    Debug.LogError($"[WireSyndicateEngine] Failed to parse delivery response or missing asset_url for {placementId}. Payload: {json}");
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
            string assetHash = !string.IsNullOrEmpty(data.asset_hash) ? data.asset_hash : data.asset_url.GetHashCode().ToString();
            string cachedPath = WSAssetCache.GetCachedAssetPath(assetHash);
            
            if (!string.IsNullOrEmpty(cachedPath))
            {
                if (WireSyndicateEngine.Config.EnableDebugLogging)
                    Debug.Log($"[WireSyndicateEngine] Asset matched hash {assetHash}, loading from cache: {cachedPath}");
                
                AssetDeliveryResult result = new AssetDeliveryResult {
                    LocalFilePath = cachedPath,
                    Format = data.format,
                    IsCacheHit = true,
                    AssetHash = assetHash,
                    ContentType = data.content_type,
                    ClickUrl = data.click_url,
                    PromoCode = data.promo_code,
                    CtaOverlayText = data.cta_overlay_text,
                    MonetizationMode = data.monetization_mode,
                    ImpressionToken = data.tracking != null ? data.tracking.impression_token : null,
                    ClickToken = data.tracking != null ? data.tracking.click_token : null
                };
                _activeAssets[data.placement_id] = result;
                FulfillPendingRequests(data.placement_id, result);
                yield break;
            }

            if (WireSyndicateEngine.Config.EnableDebugLogging)
                Debug.Log($"[WireSyndicateEngine] Downloading generic asset for hash {assetHash} from {data.asset_url}");

            using (UnityWebRequest uwr = UnityWebRequest.Get(data.asset_url))
            {
                yield return uwr.SendWebRequest();

                if (uwr.result == UnityWebRequest.Result.Success)
                {
                    string finalPath = WSAssetCache.CacheAsset(assetHash, uwr.downloadHandler.data);
                    
                    AssetDeliveryResult result = new AssetDeliveryResult {
                        LocalFilePath = finalPath,
                        Format = data.format,
                        IsCacheHit = false,
                        AssetHash = assetHash,
                        ContentType = data.content_type,
                        ClickUrl = data.click_url,
                        PromoCode = data.promo_code,
                        CtaOverlayText = data.cta_overlay_text,
                        MonetizationMode = data.monetization_mode,
                        ImpressionToken = data.tracking != null ? data.tracking.impression_token : null,
                        ClickToken = data.tracking != null ? data.tracking.click_token : null
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
                    AssetHash = !string.IsNullOrEmpty(payload.asset_hash) ? payload.asset_hash : payload.asset_url.GetHashCode().ToString(),
                    ContentType = payload.content_type,
                    ClickUrl = payload.click_url,
                    PromoCode = payload.promo_code,
                    CtaOverlayText = payload.cta_overlay_text,
                    MonetizationMode = payload.monetization_mode,
                    ImpressionToken = payload.tracking != null ? payload.tracking.impression_token : null,
                    ClickToken = payload.tracking != null ? payload.tracking.click_token : null
                };
                _activeAssets[payload.placement_id] = result;
                FulfillPendingRequests(payload.placement_id, result);
            } else {
                FulfillPendingRequests(payload.placement_id, null);
            }
        }

        public void RequestAsset(WireSyndicate.SDK.WSPlacementNode placementNode, Action<AssetDeliveryResult> onAssetLoaded, System.Threading.CancellationToken ct = default)
        {
            if (placementNode == null) return;
            string placementId = placementNode.placementId != null ? placementNode.placementId.Trim() : "";

            if (_activeAssets.ContainsKey(placementId))
            {
                if (!ct.IsCancellationRequested)
                    onAssetLoaded?.Invoke(_activeAssets[placementId]);
            }
            else
            {
                if (!_pendingRequests.ContainsKey(placementId))
                {
                    _pendingRequests[placementId] = new List<Action<AssetDeliveryResult>>();
                    StartCoroutine(ResolvePlacement(placementNode));
                }
                
                Action<AssetDeliveryResult> wrappedCallback = (result) => {
                    if (!ct.IsCancellationRequested)
                    {
                        onAssetLoaded?.Invoke(result);
                    }
                };

                _pendingRequests[placementId].Add(wrappedCallback);
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
