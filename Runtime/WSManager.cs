using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

// --- JSON Data Structures ---
[Serializable]
public class WSPlacementData
{
    public string placement_id;
    public string format;
    public string prominence;
    public string asset_url;
    public string contract_end_date;
}

[Serializable]
public class CacheManifestEntry
{
    public string fileHash;
    public long expirationTimestamp;
}

[Serializable]
public class CacheManifest
{
    public List<CacheManifestEntry> entries = new List<CacheManifestEntry>();
}

[Serializable]
public class WSPayload
{
    public bool success;
    public string timestamp;
    public List<WSPlacementData> placements;
}

public class WSManager : MonoBehaviour
{
    [Header("Configuration")]
    [Tooltip("The unique ID for this specific game, generated in the ASN Dashboard.")]
    public string gameId;

    [Tooltip("The live API endpoint for the ASN active contracts route. Example: https://<project-ref>.supabase.co/functions/v1/active-contracts")]
    public string apiBaseUrl = "";

    // --- Cache Paths ---
    private string CacheDirectory => Path.Combine(Application.persistentDataPath, "ASN_Cache");
    private string ManifestPath => Path.Combine(Application.persistentDataPath, "asn_cache_manifest.json");

    private CacheManifest _manifest;
    private Dictionary<string, AssetBundle> activeBundles = new Dictionary<string, AssetBundle>();

    public static WSManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // INITIALIZE AND PURGE CACHE ON BOOT
        InitializeCache();
        RunCacheEvictionSweep();
    }

    private void Start()
    {
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
            _manifest = JsonUtility.FromJson<CacheManifest>(json) ?? new CacheManifest();
        }
        else
        {
            _manifest = new CacheManifest();
        }
    }

    private void RunCacheEvictionSweep()
    {
        if (_manifest == null || _manifest.entries.Count == 0) return;

        long currentUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        List<CacheManifestEntry> validEntries = new List<CacheManifestEntry>();
        bool manifestChanged = false;

        foreach (var entry in _manifest.entries)
        {
            if (currentUnixTime > entry.expirationTimestamp)
            {
                // Contract expired. Nuke the file.
                string filePath = Path.Combine(CacheDirectory, entry.fileHash + ".assetbundle");
                if (File.Exists(filePath))
                {
                    try
                    {
                        File.Delete(filePath);
                        Debug.Log($"[WS Cache] Purged expired asset: {entry.fileHash}.assetbundle");
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[WS Cache] Failed to delete file {filePath}: {e.Message}");
                    }
                }
                manifestChanged = true;
            }
            else
            {
                validEntries.Add(entry);
            }
        }

        if (manifestChanged)
        {
            _manifest.entries = validEntries;
            SaveManifest();
        }
    }

    public void RegisterDownloadedAsset(string fileHash, long expirationTimestamp)
    {
        // Remove existing entry if updating
        _manifest.entries.RemoveAll(e => e.fileHash == fileHash);

        _manifest.entries.Add(new CacheManifestEntry
        {
            fileHash = fileHash,
            expirationTimestamp = expirationTimestamp
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

    private IEnumerator FetchActiveContracts()
    {
        string requestUrl = $"{apiBaseUrl}?game_id={gameId}";
        Debug.Log($"[WS] Fetching contracts from: {requestUrl}");

        using (UnityWebRequest webRequest = UnityWebRequest.Get(requestUrl))
        {
            yield return webRequest.SendWebRequest();

            if (webRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[WS] API Error: {webRequest.error}");
                yield break;
            }

            string json = webRequest.downloadHandler.text;
            WSPayload response = JsonUtility.FromJson<WSPayload>(json);

            if (response != null && response.success && response.placements != null)
            {
                Debug.Log($"[WS] Found {response.placements.Count} active contracts. Processing assets...");
                
                foreach (var placement in response.placements)
                {
                    StartCoroutine(LoadOrDownloadAssetBundle(placement));
                }
            }
            else
            {
                Debug.LogWarning("[WS] Payload parsed, but no active placements found or success flag was false.");
            }
        }
    }

    private IEnumerator LoadOrDownloadAssetBundle(WSPlacementData placementData)
    {
        string fileHash = placementData.asset_url.GetHashCode().ToString();
        string safeFileName = fileHash + ".assetbundle";
        string localFilePath = Path.Combine(CacheDirectory, safeFileName);

        AssetBundle bundleToApply = null;

        // Step 1: Check Disk Cache
        if (File.Exists(localFilePath))
        {
            Debug.Log($"[WS] Found cached asset bundle on disk for placement: {placementData.placement_id}");
            
            var request = AssetBundle.LoadFromFileAsync(localFilePath);
            yield return request;
            
            bundleToApply = request.assetBundle;
        }
        // Step 2: Download & Cache
        else
        {
            Debug.Log($"[WS] Downloading new asset bundle from network for placement: {placementData.placement_id}");
            
            using (UnityWebRequest uwr = UnityWebRequest.Get(placementData.asset_url))
            {
                yield return uwr.SendWebRequest();

                if (uwr.result == UnityWebRequest.Result.Success)
                {
                    // Save the bytes and register with the manifest!
                    File.WriteAllBytes(localFilePath, uwr.downloadHandler.data);
                    
                    long expirationTimestamp = 0;
                    if (DateTimeOffset.TryParse(placementData.contract_end_date, out DateTimeOffset endDate))
                    {
                        expirationTimestamp = endDate.ToUnixTimeSeconds();
                    }
                    
                    RegisterDownloadedAsset(fileHash, expirationTimestamp);
                    Debug.Log($"[WS] Successfully cached asset bundle to disk and registered in manifest: {localFilePath}");
                    
                    // Load the newly downloaded bundle
                    var request = AssetBundle.LoadFromFileAsync(localFilePath);
                    yield return request;
                    
                    bundleToApply = request.assetBundle;
                }
                else
                {
                    Debug.LogError($"[WS] Failed to download asset bundle: {uwr.error}");
                }
            }
        }

        // Step 3: Store in memory and notify placements
        if (bundleToApply != null)
        {
            activeBundles[placementData.placement_id] = bundleToApply;
            Debug.Log($"[WS] AssetBundle ready in memory for placement: {placementData.placement_id}");
        }
    }

    public AssetBundle GetBundleForPlacement(string placementId)
    {
        if (activeBundles.ContainsKey(placementId))
        {
            return activeBundles[placementId];
        }
        return null;
    }
}