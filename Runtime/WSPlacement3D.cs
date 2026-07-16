using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace WireSyndicate.SDK
{
    // The ContractManifest is shared, so we don't need to redefine it if it already exists in WSPlacementDynamic,
    // but to prevent compile errors if this script is used standalone, we can conditionally compile or just assume
    // it's in the same namespace. Since WSPlacementDynamic already defined ContractManifest, we don't define it again here.
    // If you plan to use this script without WSPlacementDynamic.cs, you must ensure ContractManifest is defined.

    // THE ARCHITECT'S LESSON: 
    // We optionally look for a Renderer. If one is found, we hide it when the 3D asset loads.
    // If you attach this to an empty GameObject (anchor), it will simply spawn the ad here.
    public class WSPlacement3D : MonoBehaviour
    {
        [Header("Placement Configuration")]
        [Tooltip("The unique placement_id from the Supabase dashboard.")]
        public string placementId;

        [Header("API Configuration")]
        [Tooltip("The Edge API endpoint for fetching the placement manifest. Example: https://<project-ref>.supabase.co/functions/v1/active-contracts")]
        public string apiEndpoint = "";

        // Internal references
        private Renderer _fallbackRenderer;
        private GameObject _spawnedInstance;

        private void Awake()
        {
            _fallbackRenderer = GetComponentInChildren<Renderer>();
        }

        private void Start()
        {
            if (string.IsNullOrEmpty(placementId))
            {
                Debug.LogWarning($"[WS] The 3D placement object '{gameObject.name}' is missing a Placement ID!");
                return;
            }

            StartCoroutine(FetchAndApplyAssetBundle());
        }

        [System.Serializable]
        private class ManifestPayload
        {
            public string game_ready_manifest;
        }

        private IEnumerator FetchAndApplyAssetBundle()
        {
            string url = $"{apiEndpoint}?id={placementId}";
            using (UnityWebRequest webRequest = UnityWebRequest.Get(url))
            {
                yield return webRequest.SendWebRequest();

                if (webRequest.result != UnityWebRequest.Result.Success)
                {
                    Debug.Log($"[WS] No active contract found or network error for {placementId}: {webRequest.error}. Native fallback mesh retained.");
                    yield break;
                }

                string json = webRequest.downloadHandler.text;
                
                // Try to parse the JSON manifest
                ManifestPayload manifest = null;
                try 
                {
                    manifest = JsonUtility.FromJson<ManifestPayload>(json);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[WS] Failed to parse JSON manifest for {placementId}: {e.Message}");
                    yield break;
                }

                if (manifest != null && !string.IsNullOrEmpty(manifest.game_ready_manifest))
                {
                    yield return DownloadAndSpawnBundle(manifest.game_ready_manifest);
                }
                else
                {
                    Debug.LogWarning($"[WS] Parsed manifest was null or empty for {placementId}.");
                }
            }
        }

        private IEnumerator DownloadAndSpawnBundle(string bundleUrl)
        {
            Debug.Log($"[WS] Downloading AssetBundle for placement {placementId}...");
            
            using (UnityWebRequest uwr = UnityWebRequestAssetBundle.GetAssetBundle(bundleUrl))
            {
                yield return uwr.SendWebRequest();

                if (uwr.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[WS] Failed to download AssetBundle for placement {placementId}: {uwr.error}");
                    yield break;
                }

                // Extract the bundle from RAM
                AssetBundle bundle = DownloadHandlerAssetBundle.GetContent(uwr);
                
                if (bundle == null)
                {
                    Debug.LogError($"[WS] Failed to extract AssetBundle from downloaded data for placement: {placementId}. Ensure the URL points to a valid compiled .assetbundle and NOT a .unitypackage.");
                    yield break;
                }

                // Load the primary GameObject prefab from the bundle
                var request = bundle.LoadAllAssetsAsync<GameObject>();
                yield return request;

                if (request.allAssets != null && request.allAssets.Length > 0)
                {
                    GameObject prefab = request.allAssets[0] as GameObject;
                    if (prefab != null)
                    {
                        // Destroy previous instance if it exists (for dynamic updating)
                        if (_spawnedInstance != null) Destroy(_spawnedInstance);

                        // Instantiate the prefab as a child of this anchor
                        _spawnedInstance = Instantiate(prefab, transform);
                        _spawnedInstance.transform.localPosition = Vector3.zero;
                        _spawnedInstance.transform.localRotation = Quaternion.identity;
                        
                        // Disable the native fallback mesh
                        if (_fallbackRenderer != null)
                        {
                            _fallbackRenderer.enabled = false;
                        }

                        Debug.Log($"[WS] 3D Asset instantiated successfully for '{gameObject.name}' (Placement: {placementId}).");
                    }
                    else
                    {
                        Debug.LogError($"[WS] Failed to extract GameObject prefab from bundle for placement: {placementId}");
                    }
                }
                else
                {
                    Debug.LogError($"[WS] AssetBundle for placement {placementId} does not contain any GameObjects.");
                }

                // Memory Management: Immediately unload the compressed bundle from memory, 
                // but keep the instantiated objects alive (false).
                bundle.Unload(false);
            }
        }
    }
}
