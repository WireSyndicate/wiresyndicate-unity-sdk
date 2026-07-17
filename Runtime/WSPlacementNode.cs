using UnityEngine;

namespace WireSyndicate.SDK
{
    [RequireComponent(typeof(Collider))]
    public class WSPlacementNode : MonoBehaviour
    {
        [Tooltip("The UUID of the placement registered in the WireSyndicate portal.")]
        public string placementId;

        [Tooltip("The specific renderer to analyze. If left empty, it will automatically locate one in children.")]
        [SerializeField] private Renderer targetRenderer;

        private void Start()
        {
            if (targetRenderer == null)
            {
                targetRenderer = GetComponentInChildren<Renderer>();
            }

            if (string.IsNullOrEmpty(placementId))
            {
                Debug.LogWarning($"[WSPlacementNode] Initialization failed on {gameObject.name}: placementId is missing.");
                return;
            }

            if (WSGazeVerificationEngine.Instance != null)
            {
                WSGazeVerificationEngine.Instance.RegisterNode(this);
            }
            else
            {
                Debug.LogError("[WSPlacementNode] WSGazeVerificationEngine instance not found in scene.");
            }
        }

        public Bounds GetBounds()
        {
            return targetRenderer != null ? targetRenderer.bounds : GetComponent<Collider>().bounds;
        }

        private void OnDestroy()
        {
            if (WSGazeVerificationEngine.Instance != null)
            {
                WSGazeVerificationEngine.Instance.UnregisterNode(this);
            }
        }
    }
}
