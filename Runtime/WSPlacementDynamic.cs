using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace WireSyndicate.SDK
{
    [System.Serializable]
    public class ContractManifest
    {
        public string game_ready_manifest;
    }

    // THE ARCHITECT'S LESSON: 
    // Allowing flexible assignment of targetRenderer prevents structural breakage in LOD hierarchies.
    public class WSPlacementDynamic : MonoBehaviour
    {
        [Header("Placement Configuration")]
        [Tooltip("The unique placement_id from the Supabase dashboard.")]
        public string placementId;

        [Tooltip("The specific renderer to apply textures to. If left empty, it will automatically locate one in children.")]
        [SerializeField] private Renderer targetRenderer;

        [Tooltip("Shader property name for the texture (e.g., _MainTex for Standard, _BaseColorMap for URP/HDRP).")]
        public string texturePropertyName = "_BaseColorMap";

        [Tooltip("The index of the material array on the Renderer. Element 1 = Index 1.")]
        [SerializeField] private int materialIndex = 0;

        [Tooltip("Forcefully overrides the material's UV Scale/Offset to 1x1, neutralizing texture atlases that could distort the ad.")]
        [SerializeField] private bool overrideUVScaleOffset = true;

        // Internal references
        private MaterialPropertyBlock _propBlock;

        private void Awake()
        {
            if (targetRenderer == null)
            {
                targetRenderer = GetComponentInChildren<Renderer>();
            }
            _propBlock = new MaterialPropertyBlock();
        }

        private void Start()
        {
            if (string.IsNullOrEmpty(placementId))
            {
                Debug.LogWarning($"[WireSyndicate] The placement object '{gameObject.name}' is missing a Placement ID!");
                return;
            }

            // Route asset fetching directly through the core engine to leverage disk caching and the unified connection.
            WireSyndicate.Core.WireSyndicateEngine.RequestAsset(placementId, ApplyTextureSafely);
        }

        private void ApplyTextureSafely(Texture2D texture)
        {
            if (texture != null)
            {
                Debug.Log($"[WSPlacementDynamic] Texture downloaded successfully. Applying to MeshRenderer on '{gameObject.name}'...");
                try
                {
                    // THE ARCHITECT'S LESSON: Non-Destructive Texture Swapping
                    // Using MaterialPropertyBlock prevents the creation of new material instances in memory,
                    // avoiding memory leaks and keeping the base material untouched.
                    if (targetRenderer != null)
                    {
                        targetRenderer.GetPropertyBlock(_propBlock, materialIndex);
                        _propBlock.SetTexture(texturePropertyName, texture);
                        
                        if (overrideUVScaleOffset)
                        {
                            // Hijack the atlas math by forcing Scale 1x1 and Offset 0,0
                            _propBlock.SetVector(texturePropertyName + "_ST", new Vector4(1, 1, 0, 0));
                        }
                        
                        targetRenderer.SetPropertyBlock(_propBlock, materialIndex);
                    }
                    
                    Debug.Log($"[WireSyndicate] Texture swapped successfully for '{gameObject.name}' (Placement: {placementId}).");
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[WSPlacementDynamic] FATAL: Failed to apply texture to material. Exception: {ex.Message}");
                }
            }
            else
            {
                Debug.LogWarning($"[WireSyndicate] Failed to retrieve asset for placement {placementId}. Fallback visuals retained.");
            }
        }
    }
}
