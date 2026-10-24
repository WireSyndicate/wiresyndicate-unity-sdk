using UnityEngine;

namespace WireSyndicate.SDK
{
    public class WSPlacementNode : MonoBehaviour
    {
        [Tooltip("The UUID of the placement registered in the WireSyndicate portal.")]
        public string placementId;

        [Tooltip("The specific renderer to analyze. If left empty, it will automatically locate one in children.")]
        [SerializeField] private Renderer targetRenderer;

        public WireSyndicate.Core.AssetDeliveryResult ActiveDeliveryResult { get; set; }

        protected virtual void Start()
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

            // Gaze tracking is deferred until delivery resolves to prevent running raycasts on unmonetized/house assets
        }

        public void EnableGazeTracking()
        {
            if (WSGazeVerificationEngine.Instance == null)
            {
                GameObject engineObj = new GameObject("[WireSyndicate_GazeEngine]");
                engineObj.AddComponent<WSGazeVerificationEngine>();
            }
            WSGazeVerificationEngine.Instance.RegisterNode(this);
        }

        public void DisableGazeTracking()
        {
            if (WSGazeVerificationEngine.Instance != null)
            {
                WSGazeVerificationEngine.Instance.UnregisterNode(this);
            }
        }

        public virtual Bounds GetBounds()
        {
            return targetRenderer != null ? targetRenderer.bounds : GetComponent<Collider>().bounds;
        }

        public virtual void OnVisibilityChanged(bool isVisible) { }

        public virtual Vector3 GetForward()
        {
            return targetRenderer != null ? targetRenderer.transform.forward : transform.forward;
        }

        protected virtual void OnDestroy()
        {
            DisableGazeTracking();
        }
    }
}
