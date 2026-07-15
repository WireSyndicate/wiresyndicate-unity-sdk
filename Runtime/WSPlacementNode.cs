using UnityEngine;

namespace WireSyndicate.SDK
{
    [RequireComponent(typeof(Renderer))]
    [RequireComponent(typeof(Collider))]
    public class WSPlacementNode : MonoBehaviour
    {
        [Tooltip("The UUID of the placement registered in the WireSyndicate portal.")]
        public string placementId;

        private Renderer nodeRenderer;

        private void Start()
        {
            nodeRenderer = GetComponent<Renderer>();

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
            return nodeRenderer != null ? nodeRenderer.bounds : GetComponent<Collider>().bounds;
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
