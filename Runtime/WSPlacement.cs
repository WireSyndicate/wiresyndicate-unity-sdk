using System.Collections;
using UnityEngine;

// THE ARCHITECT'S LESSON: 
// [RequireComponent] is excellent defensive programming. It ensures our 
// GetPropertyBlock calls never throw NullReferenceExceptions.
// [WS UPDATE]: 100% compatible with Persistent Disk Caching architecture.
[RequireComponent(typeof(Renderer))]
public class WSPlacement : MonoBehaviour
{
    [Header("Placement Configuration")]
    [Tooltip("The unique placement_id from the Supabase dashboard.")]
    public string placementId;

    // Internal references
    private Renderer _renderer;

    private void Awake()
    {
        _renderer = GetComponent<Renderer>();
    }

    private void Start()
    {
        if (string.IsNullOrEmpty(placementId))
        {
            Debug.LogWarning($"[WS] The placement object '{gameObject.name}' is missing a Placement ID!");
            return;
        }

        StartCoroutine(WaitForTexture());
    }

    /// <summary>
    /// Checks the WSManager for the asset bundle. Features a timeout to prevent infinite CPU polling 
    /// if the placement has no active contract.
    /// </summary>
    private IEnumerator WaitForTexture()
    {
        // Safety check: Wait until the Manager Singleton actually wakes up
        while (WSManager.Instance == null)
        {
            yield return null;
        }

        AssetBundle appliedBundle = null;
        
        // THE ARCHITECT'S LESSON: The Timeout Safety Valve
        // We will only poll for a maximum of 30 seconds (60 attempts at 0.5s each).
        // If the bundle isn't ready by then, we assume the contract is dead or network failed,
        // and we let the Coroutine die so the default Game Texture remains visible.
        int maxAttempts = 60; 
        int currentAttempt = 0;

        while (appliedBundle == null && currentAttempt < maxAttempts)
        {
            appliedBundle = WSManager.Instance.GetBundleForPlacement(placementId);
            
            if (appliedBundle == null)
            {
                currentAttempt++;
                yield return new WaitForSeconds(0.5f); 
            }
        }

        // Check if we successfully got the bundle, or if we just timed out.
        if (appliedBundle != null)
        {
            yield return ApplyPrefabSafely(appliedBundle);
        }
        else
        {
            Debug.Log($"[WS] Placement '{placementId}' timed out. Assuming no active contract. Default visuals retained.");
        }
    }

    /// <summary>
    /// Swaps the visible object by instantiating the bundle's prefab and unloading the bundle.
    /// </summary>
    private IEnumerator ApplyPrefabSafely(AssetBundle bundle)
    {
        // Load the primary GameObject prefab from the bundle
        var request = bundle.LoadAllAssetsAsync<GameObject>();
        yield return request;

        if (request.allAssets != null && request.allAssets.Length > 0)
        {
            GameObject prefab = request.allAssets[0] as GameObject;
            if (prefab != null)
            {
                // Instantiate the prefab as a child of this anchor
                GameObject instance = Instantiate(prefab, transform);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                
                // Disable the fallback/placeholder mesh
                if (_renderer != null)
                {
                    _renderer.enabled = false;
                }

                Debug.Log($"[WS] Prefab instantiated successfully for '{gameObject.name}' (Placement: {placementId}).");
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

        // Memory Management: Immediately unload the bundle, but keep instantiated objects
        bundle.Unload(false);
        Debug.Log($"[WS] AssetBundle unloaded from RAM for placement: {placementId}");
    }
}