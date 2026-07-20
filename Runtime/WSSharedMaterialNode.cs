using UnityEngine;

namespace WireSyndicate.SDK
{
    // We do NOT require a Collider natively because we rely on the serialized primaryGazeTarget.
    // However, because we inherit from WSPlacementNode (which has [RequireComponent(typeof(Collider))]),
    // Unity will auto-add a Collider to this object. The developer can just use it, or assign a different one.
    public class WSSharedMaterialNode : WSPlacementNode
    {
        [Header("Global Material Configuration")]
        [Tooltip("The global material asset to modify. ALL objects using this material will update.")]
        public Material targetMaterial;

        [Tooltip("Shader property name for the texture (e.g., _BaseMap or _MainTex).")]
        public string texturePropertyName = "_BaseMap";

        [Tooltip("A physical anchor in the scene required for the WSGazeVerificationEngine to raycast against.")]
        public Collider primaryGazeTarget;

        [Header("Atlas & Shader Overrides")]
        [Tooltip("Forcefully overrides the material's UV Scale/Offset to 1x1, neutralizing base texture atlases that could distort the ad.")]
        [SerializeField] private bool overrideUVScaleOffset = true;

        [Tooltip("Override specific shader properties (e.g. _Rows, _Tile, _Glow). This is critical if your material uses a custom shader graph for texture atlases.")]
        public System.Collections.Generic.List<ShaderFloatOverride> shaderPropertyOverrides = new System.Collections.Generic.List<ShaderFloatOverride>();

        // Keep track of the dynamically loaded texture so we can destroy it if the ad rotates, preventing VRAM leaks.
        private Texture2D _activeTexture;

        protected override void Start()
        {
            // Instead of doing the targetRenderer logic from the base class, we just validate our own.
            if (targetMaterial == null)
            {
                Debug.LogWarning($"[WSSharedMaterialNode] Initialization failed on {gameObject.name}: targetMaterial is missing.");
                return;
            }

            if (primaryGazeTarget == null)
            {
                Debug.LogWarning($"[WSSharedMaterialNode] Initialization failed on {gameObject.name}: primaryGazeTarget is missing.");
                return;
            }

            if (string.IsNullOrEmpty(placementId))
            {
                Debug.LogWarning($"[WSSharedMaterialNode] Initialization failed on {gameObject.name}: placementId is missing.");
                return;
            }

            // Bootstrap the Gaze Engine exactly as WSPlacementNode does
            if (WSGazeVerificationEngine.Instance == null)
            {
                GameObject engineObj = new GameObject("[WireSyndicate_GazeEngine]");
                engineObj.AddComponent<WSGazeVerificationEngine>();
                Debug.Log("[WSSharedMaterialNode] Auto-bootstrapped missing WSGazeVerificationEngine.");
            }

            // Register this node for telemetry
            WSGazeVerificationEngine.Instance.RegisterNode(this);

            // Fetch the asset via the unified engine connection (preserves caching and batching)
            WireSyndicate.Core.WireSyndicateEngine.RequestAsset(placementId, ApplyTextureSafely);
        }

        public override Bounds GetBounds()
        {
            // The Gaze Engine will call this to calculate screen percentage and raycasts.
            // We feed it the primary anchor.
            return primaryGazeTarget != null ? primaryGazeTarget.bounds : base.GetBounds();
        }

        private void ApplyTextureSafely(Texture2D texture)
        {
            if (texture != null)
            {
                Debug.Log($"[WSSharedMaterialNode] Texture downloaded successfully. Applying to GLOBAL Material '{targetMaterial.name}'...");
                try
                {
                    // Memory Management: Prevent VRAM leaks by destroying the old texture if the ad is rotating
                    if (_activeTexture != null && _activeTexture != texture)
                    {
                        Destroy(_activeTexture);
                    }

                    _activeTexture = texture;
                    targetMaterial.SetTexture(texturePropertyName, texture);
                    
                    if (overrideUVScaleOffset)
                    {
                        targetMaterial.SetTextureScale(texturePropertyName, new Vector2(1, 1));
                        targetMaterial.SetTextureOffset(texturePropertyName, new Vector2(0, 0));
                    }
                    
                    if (shaderPropertyOverrides != null)
                    {
                        foreach (var floatOverride in shaderPropertyOverrides)
                        {
                            if (!string.IsNullOrEmpty(floatOverride.propertyName))
                            {
                                targetMaterial.SetFloat(floatOverride.propertyName, floatOverride.value);
                            }
                        }
                    }
                    
                    Debug.Log($"[WireSyndicate] GLOBAL Texture swapped successfully for '{targetMaterial.name}' (Placement: {placementId}).");
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[WSSharedMaterialNode] FATAL: Failed to apply texture to global material. Exception: {ex.Message}");
                }
            }
            else
            {
                Debug.LogWarning($"[WSSharedMaterialNode] Failed to retrieve asset for placement {placementId}. Fallback visuals retained.");
            }
        }

        protected override void OnDestroy()
        {
            // First call base to unregister from the gaze engine
            base.OnDestroy();

            // Then clean up our texture to prevent VRAM leaks on scene unload
            if (_activeTexture != null)
            {
                Destroy(_activeTexture);
            }
        }
    }
}
