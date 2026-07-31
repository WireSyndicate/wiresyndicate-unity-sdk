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

        [Tooltip("Additional colliders in the scene that share this material. The system will automatically spawn ghost nodes on these to track gaze verification for all of them without extra setup.")]
        public System.Collections.Generic.List<Collider> additionalGazeTargets = new System.Collections.Generic.List<Collider>();

        [Header("Atlas & Shader Overrides")]
        [Tooltip("Forcefully overrides the material's UV Scale/Offset to 1x1, neutralizing base texture atlases that could distort the ad.")]
        [SerializeField] private bool overrideUVScaleOffset = true;

        [Tooltip("Override specific shader properties (e.g. _Rows, _Tile, _Glow). This is critical if your material uses a custom shader graph for texture atlases.")]
        public System.Collections.Generic.List<ShaderFloatOverride> shaderPropertyOverrides = new System.Collections.Generic.List<ShaderFloatOverride>();

        // Keep track of the dynamically loaded texture so we can destroy it if the ad rotates, preventing VRAM leaks.
        private Texture2D _activeTexture;

        // Caching variables for MaterialPropertyBlock approach (non-destructive)
        private MaterialPropertyBlock _propBlock;

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

            _propBlock = new MaterialPropertyBlock();

            // Register this node for telemetry
            WSGazeVerificationEngine.Instance.RegisterNode(this);

            // Spawn Ghost Nodes for additional targets to track gaze without requiring extra setup
            if (additionalGazeTargets != null && additionalGazeTargets.Count > 0)
            {
                foreach (Collider target in additionalGazeTargets)
                {
                    if (target != null && target != primaryGazeTarget)
                    {
                        // Ensure we don't double up if they already have one
                        if (target.GetComponent<WSGhostNode>() == null)
                        {
                            WSGhostNode ghost = target.gameObject.AddComponent<WSGhostNode>();
                            ghost.placementId = this.placementId;
                            ghost.targetCollider = target;
                        }
                    }
                }
                Debug.Log($"[WSSharedMaterialNode] Auto-spawned {additionalGazeTargets.Count} ghost nodes for telemetry tracking.");
            }

            // Fetch the asset via the unified engine connection (preserves caching and batching)
            WireSyndicate.Core.WireSyndicateEngine.RequestAsset(placementId, ApplyTextureSafely);
        }

        public override Bounds GetBounds()
        {
            // The Gaze Engine will call this to calculate screen percentage and raycasts.
            // We feed it the primary anchor.
            return primaryGazeTarget != null ? primaryGazeTarget.bounds : base.GetBounds();
        }

        public override Vector3 GetForward()
        {
            return primaryGazeTarget != null ? primaryGazeTarget.transform.forward : base.GetForward();
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
                    
                    // THE ARCHITECT'S LESSON: Non-Destructive Global Material Swapping
                    // Modifying targetMaterial directly permanently alters the .mat asset in the Unity Editor.
                    // Instead, we find all active and inactive renderers using this material and apply a MaterialPropertyBlock.
                    Renderer[] allRenderers = FindObjectsOfType<Renderer>(true);
                    int matchCount = 0;

                    foreach (Renderer r in allRenderers)
                    {
                        Material[] sharedMats = r.sharedMaterials;
                        for (int i = 0; i < sharedMats.Length; i++)
                        {
                            if (sharedMats[i] == targetMaterial)
                            {
                                r.GetPropertyBlock(_propBlock, i);
                                _propBlock.SetTexture(texturePropertyName, texture);
                                
                                if (overrideUVScaleOffset)
                                {
                                    // Scale 1x1, Offset 0x0
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
                                
                                r.SetPropertyBlock(_propBlock, i);
                                matchCount++;
                            }
                        }
                    }
                    
                    Debug.Log($"[WireSyndicate] GLOBAL Texture swapped safely on {matchCount} renderer(s) for '{targetMaterial.name}' (Placement: {placementId}).");
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

        private void OnApplicationQuit()
        {
            // No cleanup necessary since MaterialPropertyBlocks are intrinsically non-destructive.
        }

#if UNITY_EDITOR
        [ContextMenu("Auto-Populate Gaze Targets")]
        public void AutoPopulateGazeTargets()
        {
            if (targetMaterial == null)
            {
                Debug.LogWarning("[WSSharedMaterialNode] Please assign a Global Material Configuration > Target Material first.");
                return;
            }

            // Keep the primary target, but clear the additional ones
            additionalGazeTargets.Clear();
            
            // Pass 'true' to include inactive GameObjects in the scene
            Renderer[] allRenderers = FindObjectsOfType<Renderer>(true);
            int addedCount = 0;

            foreach (Renderer r in allRenderers)
            {
                bool usesMaterial = false;
                if (r.sharedMaterials != null)
                {
                    foreach (Material mat in r.sharedMaterials)
                    {
                        if (mat == targetMaterial)
                        {
                            usesMaterial = true;
                            break;
                        }
                    }
                }

                if (usesMaterial)
                {
                    // The collider might be on the same object, nested on a child/parent, or a sibling
                    Collider c = r.GetComponent<Collider>();
                    if (c == null) c = r.GetComponentInChildren<Collider>(true);
                    if (c == null) c = r.GetComponentInParent<Collider>();
                    
                    // If we still haven't found it, search siblings (children of the parent)
                    if (c == null && r.transform.parent != null)
                    {
                        c = r.transform.parent.GetComponentInChildren<Collider>(true);
                    }

                    // If no collider exists in the entire hierarchy for this renderer, add one automatically!
                    if (c == null)
                    {
                        c = r.gameObject.AddComponent<BoxCollider>();
                        Debug.Log($"[WSSharedMaterialNode] Automatically added missing BoxCollider to '{r.gameObject.name}'.");
                    }

                    if (c != null)
                    {
                        if (primaryGazeTarget == null)
                        {
                            primaryGazeTarget = c;
                        }
                        else if (c != primaryGazeTarget && !additionalGazeTargets.Contains(c))
                        {
                            additionalGazeTargets.Add(c);
                            addedCount++;
                        }
                    }
                }
            }

            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log($"[WSSharedMaterialNode] Auto-populated {addedCount} additional gaze targets based on the '{targetMaterial.name}' material.");
        }
#endif
    }

    /// <summary>
    /// A lightweight tracking node automatically spawned by WSSharedMaterialNode 
    /// for additional gaze targets. It registers with the Gaze Verification Engine 
    /// but does not perform any texture downloads or material modifications.
    /// </summary>
    public class WSGhostNode : WSPlacementNode
    {
        public Collider targetCollider;

        protected override void Start()
        {
            if (string.IsNullOrEmpty(placementId)) return;
            if (WSGazeVerificationEngine.Instance != null)
            {
                WSGazeVerificationEngine.Instance.RegisterNode(this);
            }
        }

        public override Bounds GetBounds()
        {
            return targetCollider != null ? targetCollider.bounds : base.GetBounds();
        }

        public override Vector3 GetForward()
        {
            return targetCollider != null ? targetCollider.transform.forward : base.GetForward();
        }
    }
}
