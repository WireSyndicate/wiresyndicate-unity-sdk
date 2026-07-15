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
        public WireEnvironment Environment = WireEnvironment.Production;
        public bool EnableDebugLogging = false;
    }

    [Serializable]
    public class WireSyndicatePlacementData
    {
        public string placement_id;
        public string format;
        public string prominence;
        public string asset_url;
        public string contract_end_date;
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

    [Serializable]
    public class WireSyndicatePayload
    {
        public bool success;
        public string timestamp;
        public List<WireSyndicatePlacementData> placements;
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

            GameObject coreObj = new GameObject("[WireSyndicate_InternalEngine]");
            UnityEngine.Object.DontDestroyOnLoad(coreObj);
            _coreBehaviour = coreObj.AddComponent<WireSyndicateCoreBehaviour>();
            
            if (config.EnableDebugLogging)
                Debug.Log($"[WireSyndicate] Engine initialized with OrgId: {config.OrgId}");
        }

        public static void RequestAsset(string placementId, Action<Texture2D> onAssetLoaded)
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
        private Dictionary<string, Texture2D> _activeTextures = new Dictionary<string, Texture2D>();
        private Dictionary<string, List<Action<Texture2D>>> _pendingRequests = new Dictionary<string, List<Action<Texture2D>>>();

        private void Awake()
        {
            InitializeCache();
            PurgeExpiredCache();
            StartCoroutine(FetchActiveContracts());
        }

        // ==========================================
        // DISK CACHE MANAGEMENT
        // ==========================================

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

        // ==========================================
        // API & NETWORK LOGIC
        // ==========================================

        private string GetApiUrl()
        {
            string baseUrl = WireSyndicateEngine.Config.Environment == WireEnvironment.Production 
                ? "https://api.wiresyndicate.com/v1" 
                : "http://localhost:3000/api/v1";

            return $"{baseUrl}/active-contracts?org_id={WireSyndicateEngine.Config.OrgId}";
        }

        private IEnumerator FetchActiveContracts()
        {
            using (UnityWebRequest webRequest = UnityWebRequest.Get(GetApiUrl()))
            {
                yield return webRequest.SendWebRequest();

                if (webRequest.result != UnityWebRequest.Result.Success)
                {
                    if (WireSyndicateEngine.Config.EnableDebugLogging)
                        Debug.LogError($"[WireSyndicate] API Error: {webRequest.error}");
                    yield break;
                }

                string json = webRequest.downloadHandler.text;
                WireSyndicatePayload response = JsonUtility.FromJson<WireSyndicatePayload>(json);

                if (response != null && response.success && response.placements != null)
                {
                    foreach (var placement in response.placements)
                    {
                        StartCoroutine(LoadOrDownloadTexture(placement));
                    }
                }
            }
        }

        private IEnumerator LoadOrDownloadTexture(WireSyndicatePlacementData placementData)
        {
            string safeFileName = placementData.asset_url.GetHashCode().ToString() + ".png";
            string localFilePath = Path.Combine(CacheDirectory, safeFileName);

            Texture2D textureToApply = null;

            if (File.Exists(localFilePath))
            {
                byte[] fileData = File.ReadAllBytes(localFilePath);
                textureToApply = new Texture2D(2, 2);
                textureToApply.LoadImage(fileData); 
            }
            else
            {
                using (UnityWebRequest uwr = UnityWebRequestTexture.GetTexture(placementData.asset_url))
                {
                    yield return uwr.SendWebRequest();

                    if (uwr.result == UnityWebRequest.Result.Success)
                    {
                        textureToApply = DownloadHandlerTexture.GetContent(uwr);
                        File.WriteAllBytes(localFilePath, uwr.downloadHandler.data);
                        RegisterDownloadedAsset(placementData.asset_url, safeFileName, placementData.contract_end_date);
                    }
                }
            }

            if (textureToApply != null)
            {
                _activeTextures[placementData.placement_id] = textureToApply;
                FulfillPendingRequests(placementData.placement_id, textureToApply);
            }
        }

        public void RequestAsset(string placementId, Action<Texture2D> onAssetLoaded)
        {
            if (_activeTextures.ContainsKey(placementId))
            {
                onAssetLoaded?.Invoke(_activeTextures[placementId]);
            }
            else
            {
                if (!_pendingRequests.ContainsKey(placementId))
                {
                    _pendingRequests[placementId] = new List<Action<Texture2D>>();
                }
                _pendingRequests[placementId].Add(onAssetLoaded);
            }
        }

        private void FulfillPendingRequests(string placementId, Texture2D texture)
        {
            if (_pendingRequests.ContainsKey(placementId))
            {
                foreach (var callback in _pendingRequests[placementId])
                {
                    callback?.Invoke(texture);
                }
                _pendingRequests.Remove(placementId);
            }
        }
    }
}
