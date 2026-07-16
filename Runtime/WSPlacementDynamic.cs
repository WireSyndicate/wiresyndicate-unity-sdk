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
    // [RequireComponent] is excellent defensive programming. It ensures our 
    // GetPropertyBlock calls never throw NullReferenceExceptions.
    [RequireComponent(typeof(Renderer))]
    public class WSPlacementDynamic : MonoBehaviour
    {
        [Header("Placement Configuration")]
        [Tooltip("The unique placement_id from the Supabase dashboard.")]
        public string placementId;

        [Tooltip("Shader property name for the texture (e.g., _MainTex for Standard, _BaseColorMap for URP/HDRP).")]
        public string texturePropertyName = "_BaseColorMap";

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

            // Route asset fetching directly through the core engine to leverage disk caching and the unified connection.
            WireSyndicate.Core.WireSyndicateEngine.RequestAsset(placementId, ApplyTextureSafely);
        }

        private void ApplyTextureSafely(Texture2D texture)
        {
            if (texture != null)
            {
                // THE ARCHITECT'S LESSON: Non-Destructive Texture Swapping
                // Using MaterialPropertyBlock prevents the creation of new material instances in memory,
                // avoiding memory leaks and keeping the base material untouched.
                _renderer.GetPropertyBlock(_propBlock);
                _propBlock.SetTexture(texturePropertyName, texture);
                _renderer.SetPropertyBlock(_propBlock);
                
                Debug.Log($"[WireSyndicate] Texture swapped successfully for '{gameObject.name}' (Placement: {placementId}).");
            }
            else
            {
                Debug.LogWarning($"[WireSyndicate] Failed to retrieve asset for placement {placementId}. Fallback visuals retained.");
            }
        }
    }
}
