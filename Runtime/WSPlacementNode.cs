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

            if (WSGazeVerificationEngine.Instance == null)
            {
                GameObject engineObj = new GameObject("[WireSyndicate_GazeEngine]");
                engineObj.AddComponent<WSGazeVerificationEngine>();
                // The engine's Awake method will automatically set the singleton Instance and apply DontDestroyOnLoad
                Debug.Log("[WSPlacementNode] Auto-bootstrapped missing WSGazeVerificationEngine.");
            }

            WSGazeVerificationEngine.Instance.RegisterNode(this);
        }

        public virtual Bounds GetBounds()
        {
            return targetRenderer != null ? targetRenderer.bounds : GetComponent<Collider>().bounds;
        }

        protected virtual void OnDestroy()
        {
            if (WSGazeVerificationEngine.Instance != null)
            {
                WSGazeVerificationEngine.Instance.UnregisterNode(this);
            }
        }
    }
}
