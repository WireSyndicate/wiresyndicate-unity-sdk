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

    [System.Serializable]
    public class ShaderFloatOverride
    {
        [Tooltip("The exact property name in the shader (e.g. Rows, Columns, _Glossiness)")]
        public string propertyName;
        public float value;
    }

    // THE ARCHITECT'S LESSON: 
    // Allowing flexible assignment of targetRenderer prevents structural breakage in LOD hierarchies.
    public class WSPlacementDynamic : WSPlacementNode
    {
        [Header("Placement Configuration (Dynamic)")]

        [Tooltip("The specific renderers to apply textures to (e.g. all LOD levels). If left empty, it will automatically locate all renderers in children.")]
        [SerializeField] private Renderer[] targetRenderers;

        [Tooltip("Shader property name for the texture (e.g., _MainTex for Standard, _BaseColorMap for URP/HDRP).")]
        public string texturePropertyName = "_BaseColorMap";

        [Tooltip("The index of the material array on the Renderer. Element 1 = Index 1.")]
        [SerializeField] private int materialIndex = 0;

        [Header("Atlas & Shader Overrides")]
        [Tooltip("Forcefully overrides the material's UV Scale/Offset to 1x1, neutralizing base texture atlases that could distort the ad.")]
        [SerializeField] private bool overrideUVScaleOffset = true;

        [Tooltip("Override specific shader properties (e.g. _Rows, _Tile, _Glow). This is critical if your material uses a custom shader graph for texture atlases.")]
        public System.Collections.Generic.List<ShaderFloatOverride> shaderPropertyOverrides = new System.Collections.Generic.List<ShaderFloatOverride>();

        // Internal references
        private MaterialPropertyBlock _propBlock;

        private void Awake()
        {
            if (targetRenderers == null || targetRenderers.Length == 0)
            {
                targetRenderers = GetComponentsInChildren<Renderer>();
            }
            _propBlock = new MaterialPropertyBlock();
        }

        protected override void Start()
        {
            // Call base to register with Gaze Verification Engine
            base.Start();

            if (string.IsNullOrEmpty(placementId))
            {
                Debug.LogWarning($"[WireSyndicate] The placement object '{gameObject.name}' is missing a Placement ID!");
                return;
            }

            // Route asset fetching directly through the core engine to leverage disk caching and the unified connection.
            WireSyndicate.Core.WireSyndicateEngine.RequestAsset(placementId, ApplyTextureSafely);
        }

        public override Bounds GetBounds()
        {
            if (targetRenderers != null && targetRenderers.Length > 0)
            {
                // Encapsulate all LOD renderers to get a unified bounding box
                bool initialized = false;
                Bounds combinedBounds = new Bounds();
                
                foreach (var r in targetRenderers)
                {
                    if (r != null)
                    {
                        if (!initialized)
                        {
                            combinedBounds = r.bounds;
                            initialized = true;
                        }
                        else
                        {
                            combinedBounds.Encapsulate(r.bounds);
                        }
                    }
                }
                
                if (initialized) return combinedBounds;
            }
            return base.GetBounds();
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
                    if (targetRenderers != null && targetRenderers.Length > 0)
                    {
                        foreach (var targetRenderer in targetRenderers)
                        {
                            if (targetRenderer == null) continue;

                            if (materialIndex < 0 || materialIndex >= targetRenderer.sharedMaterials.Length)
                            {
                                Debug.LogError($"[WSPlacementDynamic] Skipped renderer '{targetRenderer.name}' on '{gameObject.name}': Material Index ({materialIndex}) is out of bounds (only {targetRenderer.sharedMaterials.Length} materials).");
                                continue;
                            }

                            targetRenderer.GetPropertyBlock(_propBlock, materialIndex);
                            _propBlock.SetTexture(texturePropertyName, texture);
                            
                            if (overrideUVScaleOffset)
                            {
                                // Hijack the atlas math by forcing Scale 1x1 and Offset 0,0
                                _propBlock.SetVector(texturePropertyName + "_ST", new Vector4(1, 1, 0, 0));
                            }
                            
                            if (shaderPropertyOverrides != null)
                            {
                                foreach (var floatOverride in shaderPropertyOverrides)
                                {
                                    if (!string.IsNullOrEmpty(floatOverride.propertyName))
                                    {
                                        _propBlock.SetFloat(floatOverride.propertyName, floatOverride.value);
                                    }
                                }
                            }
                            
                            targetRenderer.SetPropertyBlock(_propBlock, materialIndex);
                        }
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
