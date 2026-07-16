using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace WireSyndicate.SDK
{
    [RequireComponent(typeof(Camera))]
    public class WSGazeVerificationEngine : MonoBehaviour
    {
        public static WSGazeVerificationEngine Instance { get; private set; }

        [Header("Optical Physics Constraints (IAB Standards)")]
        private const float MIN_VIEWPORT_COVERAGE = 0.015f; // 1.5% of total screen
        private const float MAX_VIEWING_ANGLE = 70.0f; // Degrees
        private const float REQUIRED_DWELL_TIME = 1.5f; // Seconds

        [Header("Engine Configuration")]
        [Tooltip("Validation matrix execution frequency (in seconds). 0.1 = 10Hz.")]
        public float tickRate = 0.1f;
        
        [Tooltip("Layers that can block line-of-sight to the advertisement.")]
        public LayerMask occlusionMask;

        private Camera mainCamera;
        private List<WSPlacementNode> activeNodes = new List<WSPlacementNode>();
        
        private class GazeState
        {
            public float currentDwellTime;
            public float peakScreenCoverage;
            public bool hasSurpassedThreshold;
        }

        private Dictionary<WSPlacementNode, GazeState> nodeStates = new Dictionary<WSPlacementNode, GazeState>();

        // Cached array for the 8 corners of a bounding box
        private Vector3[] boundsCorners = new Vector3[8];

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                mainCamera = GetComponent<Camera>();
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void Start()
        {
            if (mainCamera != null)
            {
                StartCoroutine(ValidationMatrixRoutine());
            }
        }

        public void RegisterNode(WSPlacementNode node)
        {
            if (!activeNodes.Contains(node))
            {
                activeNodes.Add(node);
                nodeStates[node] = new GazeState();
            }
        }

        public void UnregisterNode(WSPlacementNode node)
        {
            if (activeNodes.Contains(node))
            {
                // If a node is destroyed while being gazed at and surpassed the threshold, fire it!
                if (nodeStates[node].hasSurpassedThreshold)
                {
                    TriggerImpression(node, nodeStates[node]);
                }

                activeNodes.Remove(node);
                nodeStates.Remove(node);
            }
        }

        private IEnumerator ValidationMatrixRoutine()
        {
            WaitForSeconds wait = new WaitForSeconds(tickRate);

            while (true)
            {
                yield return wait;
                ProcessValidationMatrix();
            }
        }

        private void ProcessValidationMatrix()
        {
            if (mainCamera == null) return;

            Vector3 cameraPos = mainCamera.transform.position;
            Vector3 cameraForward = mainCamera.transform.forward;

            for (int i = activeNodes.Count - 1; i >= 0; i--)
            {
                WSPlacementNode node = activeNodes[i];
                if (node == null) continue;

                GazeState state = nodeStates[node];
                float currentCoverage;
                bool isVerified = EvaluateNode(node, cameraPos, cameraForward, out currentCoverage);

                if (isVerified)
                {
                    state.currentDwellTime += tickRate;
                    if (currentCoverage > state.peakScreenCoverage)
                    {
                        state.peakScreenCoverage = currentCoverage;
                    }

                    if (state.currentDwellTime >= REQUIRED_DWELL_TIME)
                    {
                        state.hasSurpassedThreshold = true;
                    }
                }
                else
                {
                    // The line of sight was broken.
                    if (state.hasSurpassedThreshold)
                    {
                        // True end of the impression. Dispatch it!
                        TriggerImpression(node, state);
                    }

                    // Reset state
                    state.currentDwellTime = 0f;
                    state.peakScreenCoverage = 0f;
                    state.hasSurpassedThreshold = false;
                }
            }
        }

        private bool EvaluateNode(WSPlacementNode node, Vector3 cameraPos, Vector3 cameraForward, out float screenCoverage)
        {
            screenCoverage = 0f;
            Bounds bounds = node.GetBounds();
            Vector3 nodeCenter = bounds.center;
            Vector3 dirToNode = nodeCenter - cameraPos;
            float distanceToNode = dirToNode.magnitude;

            // 1. Frustum Culling (Is the center behind the camera?)
            Vector3 centerViewportPos = mainCamera.WorldToViewportPoint(nodeCenter);
            if (centerViewportPos.z <= 0)
            {
                return false;
            }

            // 2. Angle of Incidence (Dot Product Gaze Match)
            dirToNode.Normalize();
            float dotProduct = Vector3.Dot(cameraForward, dirToNode);
            float angle = Mathf.Acos(Mathf.Clamp(dotProduct, -1f, 1f)) * Mathf.Rad2Deg;

            if (angle > MAX_VIEWING_ANGLE)
            {
                return false;
            }

            // Backface culling
            float facingDot = Vector3.Dot(node.transform.forward, -dirToNode);
            if (facingDot < 0) 
            {
                return false;
            }

            // 3. Pixel Density Projection (Must occupy >= 1.5% of screen)
            screenCoverage = CalculateViewportCoverage(bounds);
            if (screenCoverage < MIN_VIEWPORT_COVERAGE)
            {
                return false;
            }

            // 4. Occlusion Check (Physics.Raycast single-hit)
            if (Physics.Raycast(cameraPos, dirToNode, out RaycastHit hitInfo, distanceToNode - 0.01f, occlusionMask))
            {
                // If we hit anything on the occlusion mask before reaching the target, it is blocked.
                // We check if the hit collider is part of the same hierarchy as the target node.
                WSPlacementNode hitNode = hitInfo.collider.GetComponentInParent<WSPlacementNode>();
                
                if (hitNode != node)
                {
                    return false;
                }
            }

            return true;
        }

        private float CalculateViewportCoverage(Bounds bounds)
        {
            Vector3 extents = bounds.extents;
            Vector3 center = bounds.center;

            boundsCorners[0] = new Vector3(center.x + extents.x, center.y + extents.y, center.z + extents.z);
            boundsCorners[1] = new Vector3(center.x + extents.x, center.y + extents.y, center.z - extents.z);
            boundsCorners[2] = new Vector3(center.x + extents.x, center.y - extents.y, center.z + extents.z);
            boundsCorners[3] = new Vector3(center.x + extents.x, center.y - extents.y, center.z - extents.z);
            boundsCorners[4] = new Vector3(center.x - extents.x, center.y + extents.y, center.z + extents.z);
            boundsCorners[5] = new Vector3(center.x - extents.x, center.y + extents.y, center.z - extents.z);
            boundsCorners[6] = new Vector3(center.x - extents.x, center.y - extents.y, center.z + extents.z);
            boundsCorners[7] = new Vector3(center.x - extents.x, center.y - extents.y, center.z - extents.z);

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            for (int i = 0; i < 8; i++)
            {
                Vector3 viewportPos = mainCamera.WorldToViewportPoint(boundsCorners[i]);

                viewportPos.x = Mathf.Clamp01(viewportPos.x);
                viewportPos.y = Mathf.Clamp01(viewportPos.y);

                if (viewportPos.x < minX) minX = viewportPos.x;
                if (viewportPos.x > maxX) maxX = viewportPos.x;
                if (viewportPos.y < minY) minY = viewportPos.y;
                if (viewportPos.y > maxY) maxY = viewportPos.y;
            }

            return (maxX - minX) * (maxY - minY);
        }

        private void TriggerImpression(WSPlacementNode node, GazeState state)
        {
            Debug.Log($"[WSGazeVerificationEngine] Valid GVI captured! {node.placementId} | Duration: {state.currentDwellTime}s | Peak Coverage: {state.peakScreenCoverage * 100}%");
            
            if (WSTelemetryDispatcher.Instance != null)
            {
                WSTelemetryDispatcher.Instance.DispatchImpression(node.placementId, state.currentDwellTime, state.peakScreenCoverage);
            }
            else
            {
                Debug.LogWarning("[WSGazeVerificationEngine] WSTelemetryDispatcher not found. Impression not sent to Edge.");
            }
        }
    }
}
