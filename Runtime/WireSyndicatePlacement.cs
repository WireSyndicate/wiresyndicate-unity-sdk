using System.Collections;
using UnityEngine;

// THE ARCHITECT'S LESSON: 
// [RequireComponent] is excellent defensive programming. It ensures our 
// GetPropertyBlock calls never throw NullReferenceExceptions.
// [WireSyndicate UPDATE]: 100% compatible with Persistent Disk Caching architecture.
[RequireComponent(typeof(Renderer))]
public class WireSyndicatePlacement : MonoBehaviour
{
    [Header("Placement Configuration")]
    [Tooltip("The unique placement_id from the Supabase dashboard.")]
    public string placementId;

    // ZERO-DRIFT FIX: Changed the default to URP's '_BaseMap'. 
    // If a dev uses a custom shader, they can still override this in the Unity Inspector.
    [Tooltip("The shader property name for the texture. Use '_BaseMap' for URP/HDRP or '_MainTex' for Standard/Built-in.")]
    public string texturePropertyName = "_BaseMap"; 

    // Internal references
    private Renderer _renderer;
    private MaterialPropertyBlock _propBlock;

    private void Awake()
    {
        _renderer = GetComponent<Renderer>();
        _propBlock = new MaterialPropertyBlock();
    }

    private void Start()
    {
        if (string.IsNullOrEmpty(placementId))
        {
            Debug.LogWarning($"[WireSyndicate] The placement object '{gameObject.name}' is missing a Placement ID!");
            return;
        }

        StartCoroutine(WaitForTexture());
    }

    /// <summary>
    /// Checks the WireSyndicateManager for the texture. Features a timeout to prevent infinite CPU polling 
    /// if the placement has no active contract.
    /// </summary>
    private IEnumerator WaitForTexture()
    {
        // Safety check: Wait until the Manager Singleton actually wakes up
        while (WireSyndicateManager.Instance == null)
        {
            yield return null;
        }

        Texture2D appliedTexture = null;
        
        // THE ARCHITECT'S LESSON: The Timeout Safety Valve
        // We will only poll for a maximum of 30 seconds (60 attempts at 0.5s each).
        // If the texture isn't ready by then, we assume the contract is dead or network failed,
        // and we let the Coroutine die so the default Game Texture remains visible.
        int maxAttempts = 60; 
        int currentAttempt = 0;

        while (appliedTexture == null && currentAttempt < maxAttempts)
        {
            appliedTexture = WireSyndicateManager.Instance.GetTextureForPlacement(placementId);
            
            if (appliedTexture == null)
            {
                currentAttempt++;
                yield return new WaitForSeconds(0.5f); 
            }
        }

        // Check if we successfully got the texture, or if we just timed out.
        if (appliedTexture != null)
        {
            ApplyTextureSafely(appliedTexture);
        }
        else
        {
            Debug.Log($"[WireSyndicate] Placement '{placementId}' timed out. Assuming no active contract. Default texture retained.");
        }
    }

    /// <summary>
    /// Swaps the visible texture strictly using the GPU override to prevent RAM memory leaks.
    /// </summary>
    private void ApplyTextureSafely(Texture2D texture)
    {
        _renderer.GetPropertyBlock(_propBlock);
        _propBlock.SetTexture(texturePropertyName, texture);
        _renderer.SetPropertyBlock(_propBlock);

        Debug.Log($"[WireSyndicate] Viewable texture applied securely to '{gameObject.name}' (Placement: {placementId}).");
    }
}
