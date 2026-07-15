using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

// --- JSON Data Structures ---
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

public class WireSyndicateManager : MonoBehaviour
{
    [Header("Configuration")]
    [Tooltip("The unique ID for this specific game, generated in the WireSyndicate Dashboard.")]
    public string gameId;

    [Tooltip("The live API endpoint for the WireSyndicate active contracts route.")]
    public string apiBaseUrl = "http://localhost:3000/api/v1/active-contracts";

    // --- Cache Paths ---
    private string CacheDirectory => Path.Combine(Application.persistentDataPath, "WireSyndicate_Cache");
    private string ManifestPath => Path.Combine(CacheDirectory, "WireSyndicate_manifest.json");

    private WireSyndicateCacheManifest _manifest;
    private Dictionary<string, Texture2D> activeTextures = new Dictionary<string, Texture2D>();

    public static WireSyndicateManager Instance { get; private set; }

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
        PurgeExpiredCache();
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
            _manifest = JsonUtility.FromJson<WireSyndicateCacheManifest>(json) ?? new WireSyndicateCacheManifest();
        }
        else
        {
            _manifest = new WireSyndicateCacheManifest();
        }
    }

    public void PurgeExpiredCache()
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
                    // Contract expired. Nuke the file.
                    string filePath = Path.Combine(CacheDirectory, entry.localFileName);
                    if (File.Exists(filePath))
                    {
                        try
                        {
                            File.Delete(filePath);
                            Debug.Log($"[WireSyndicate Cache] Purged expired asset: {entry.localFileName}");
                        }
                        catch (Exception e)
                        {
                            Debug.LogError($"[WireSyndicate Cache] Failed to delete file {filePath}: {e.Message}");
                        }
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

    public void RegisterDownloadedAsset(string url, string localFileName, string endDateString)
    {
        string hash = url.GetHashCode().ToString();

        // Remove existing entry if updating
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

    private IEnumerator FetchActiveContracts()
    {
        string requestUrl = $"{apiBaseUrl}?game_id={gameId}";
        Debug.Log($"[WireSyndicate] Fetching contracts from: {requestUrl}");

        using (UnityWebRequest webRequest = UnityWebRequest.Get(requestUrl))
        {
            yield return webRequest.SendWebRequest();

            if (webRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[WireSyndicate] API Error: {webRequest.error}");
                yield break;
            }

            string json = webRequest.downloadHandler.text;
            WireSyndicatePayload response = JsonUtility.FromJson<WireSyndicatePayload>(json);

            if (response != null && response.success && response.placements != null)
            {
                Debug.Log($"[WireSyndicate] Found {response.placements.Count} active contracts. Processing assets...");
                
                foreach (var placement in response.placements)
                {
                    StartCoroutine(LoadOrDownloadTexture(placement));
                }
            }
            else
            {
                Debug.LogWarning("[WireSyndicate] Payload parsed, but no active placements found or success flag was false.");
            }
        }
    }

    private IEnumerator LoadOrDownloadTexture(WireSyndicatePlacementData placementData)
    {
        string safeFileName = placementData.asset_url.GetHashCode().ToString() + ".png";
        string localFilePath = Path.Combine(CacheDirectory, safeFileName); // UPDATED to use CacheDirectory

        Texture2D textureToApply = null;

        // Step 1: Check Disk Cache
        if (File.Exists(localFilePath))
        {
            Debug.Log($"[WireSyndicate] Found cached texture on disk for placement: {placementData.placement_id}");
            byte[] fileData = File.ReadAllBytes(localFilePath);
            textureToApply = new Texture2D(2, 2);
            textureToApply.LoadImage(fileData); 
        }
        // Step 2: Download & Cache
        else
        {
            Debug.Log($"[WireSyndicate] Downloading new texture from network for placement: {placementData.placement_id}");
            
            using (UnityWebRequest uwr = UnityWebRequestTexture.GetTexture(placementData.asset_url))
            {
                yield return uwr.SendWebRequest();

                if (uwr.result == UnityWebRequest.Result.Success)
                {
                    textureToApply = DownloadHandlerTexture.GetContent(uwr);

                    // Save the bytes and register with the manifest!
                    File.WriteAllBytes(localFilePath, uwr.downloadHandler.data);
                    RegisterDownloadedAsset(placementData.asset_url, safeFileName, placementData.contract_end_date);
                    
                    Debug.Log($"[WireSyndicate] Successfully cached texture to disk and registered in manifest: {localFilePath}");
                }
                else
                {
                    Debug.LogError($"[WireSyndicate] Failed to download texture: {uwr.error}");
                }
            }
        }

        // Step 3: Store in memory and notify placements
        if (textureToApply != null)
        {
            activeTextures[placementData.placement_id] = textureToApply;
            Debug.Log($"[WireSyndicate] Texture ready in memory for placement: {placementData.placement_id}");
        }
    }

    public Texture2D GetTextureForPlacement(string placementId)
    {
        if (activeTextures.ContainsKey(placementId))
        {
            return activeTextures[placementId];
        }
        return null;
    }
}
