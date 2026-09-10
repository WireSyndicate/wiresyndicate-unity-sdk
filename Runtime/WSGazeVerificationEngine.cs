using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace WireSyndicate.SDK
{
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
        
        [Tooltip("Toggle on to log the exact reason why a placement fails the verification matrix (e.g. angle, distance, occlusion).")]
        public bool enableDebugLogs = false;

        [Tooltip("Layers that can block line-of-sight to the advertisement.")]
        public LayerMask occlusionLayerMask;

        private Camera mainCamera;
        private List<WSPlacementNode> activeNodes = new List<WSPlacementNode>();
        
        private class GazeState
        {
            public float currentDwellTime;
            public float peakScreenCoverage;
            public bool hasSurpassedThreshold;
            public bool isCurrentlyVisible;
            public float lastDistance;
            public float lastAngle;
            public float lastOcclusionPercentage;
        }

        private Dictionary<WSPlacementNode, GazeState> nodeStates = new Dictionary<WSPlacementNode, GazeState>();

        // Cached array for the 8 corners of a bounding box
        private Vector3[] boundsCorners = new Vector3[8];

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                mainCamera = Camera.main;
                
                if (mainCamera == null)
                {
                    Debug.LogWarning("[WSGazeVerificationEngine] No Camera tagged 'MainCamera' found. Frustum tracking will be paused until one is assigned.");
                }

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
                float currentCoverage, currentDistance, currentAngle, currentOcclusion;
                bool isVerified = EvaluateNode(node, cameraPos, cameraForward, out currentCoverage, out currentDistance, out currentAngle, out currentOcclusion);

                if (isVerified != state.isCurrentlyVisible) {
                    state.isCurrentlyVisible = isVerified;
                    node.OnVisibilityChanged(isVerified);
                }

                if (isVerified)
                {
                    state.currentDwellTime += tickRate;
                    if (currentCoverage > state.peakScreenCoverage)
                    {
                        state.peakScreenCoverage = currentCoverage;
                        state.lastDistance = currentDistance;
                        state.lastAngle = currentAngle;
                        state.lastOcclusionPercentage = currentOcclusion;
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

        private bool EvaluateNode(WSPlacementNode node, Vector3 cameraPos, Vector3 cameraForward, out float screenCoverage, out float distanceToNode, out float angle, out float occlusionPercentage)
        {
            screenCoverage = 0f;
            distanceToNode = 0f;
            angle = 0f;
            occlusionPercentage = 1f;

            Bounds bounds = node.GetBounds();
            Vector3 nodeCenter = bounds.center;
            Vector3 dirToNode = nodeCenter - cameraPos;
            distanceToNode = dirToNode.magnitude;

            // 1. Frustum Culling (Is the center behind the camera?)
            Vector3 centerViewportPos = mainCamera.WorldToViewportPoint(nodeCenter);
            if (centerViewportPos.z <= 0)
            {
                if (enableDebugLogs) Debug.Log($"[WSGazeVerificationEngine] {node.placementId} failed: Frustum Culling (Behind Camera)");
                return false;
            }

            // 2. Angle of Incidence (Dot Product Gaze Match)
            dirToNode.Normalize();
            float dotProduct = Vector3.Dot(cameraForward, dirToNode);
            angle = Mathf.Acos(Mathf.Clamp(dotProduct, -1f, 1f)) * Mathf.Rad2Deg;

            if (angle > MAX_VIEWING_ANGLE)
            {
                if (enableDebugLogs) Debug.Log($"[WSGazeVerificationEngine] {node.placementId} failed: Viewing Angle ({angle}° > {MAX_VIEWING_ANGLE}°)");
                return false;
            }

            // Backface culling
            float facingDot = Vector3.Dot(node.GetForward(), -dirToNode);
            if (facingDot < 0) 
            {
                if (enableDebugLogs) Debug.Log($"[WSGazeVerificationEngine] {node.placementId} failed: Backface Culling");
                return false;
            }

            // 3. Pixel Density Projection (Must occupy >= 1.5% of screen)
            screenCoverage = CalculateViewportCoverage(bounds);
            if (screenCoverage < MIN_VIEWPORT_COVERAGE)
            {
                if (enableDebugLogs) Debug.Log($"[WSGazeVerificationEngine] {node.placementId} failed: Screen Coverage ({(screenCoverage * 100f):F2}% < {(MIN_VIEWPORT_COVERAGE * 100f):F2}%)");
                return false;
            }

            // 4. Occlusion Check (5-Point Sparse Raycast Matrix)
            int occlusionHits = 0;
            Vector3 extents = bounds.extents;
            
            // Generate 5 points on the front-facing plane of the bounds
            // Using a simple cross shape: Center, Top, Bottom, Left, Right relative to world axes
            // (A more advanced version would use camera-aligned axes, but this is fast and standard)
            Vector3[] rayTargets = new Vector3[5] {
                nodeCenter,
                nodeCenter + new Vector3(0, extents.y, 0),
                nodeCenter + new Vector3(0, -extents.y, 0),
                nodeCenter + new Vector3(extents.x, 0, 0),
                nodeCenter + new Vector3(-extents.x, 0, 0)
            };

            for (int i = 0; i < 5; i++)
            {
                Vector3 targetDir = rayTargets[i] - cameraPos;
                float targetDist = targetDir.magnitude;
                
                if (Physics.Raycast(cameraPos, targetDir, out RaycastHit hitInfo, targetDist - 0.01f, occlusionLayerMask))
                {
                    WSPlacementNode hitNode = hitInfo.collider.GetComponentInParent<WSPlacementNode>();
                    if (hitNode != node)
                    {
                        // Fallback for WSSharedMaterialNode which uses an external collider as a gaze target
                        bool isValidHit = false;
                        if (node is WSSharedMaterialNode sharedNode && sharedNode.primaryGazeTarget == hitInfo.collider)
                        {
                            isValidHit = true;
                        }
                        else if (node is WSGhostNode ghostNode && ghostNode.targetCollider == hitInfo.collider)
                        {
                            isValidHit = true;
                        }
                        
                        if (!isValidHit)
                        {
                            occlusionHits++;
                        }
                    }
                }
            }

            occlusionPercentage = occlusionHits / 5.0f;
            
            // If the center is blocked, or more than 3 points are blocked, consider it occluded
            if (occlusionHits >= 3)
            {
                if (enableDebugLogs) Debug.Log($"[WSGazeVerificationEngine] {node.placementId} failed: Highly Occluded ({occlusionPercentage * 100}%)");
                return false;
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
                // In a full implementation, campaignId and bidId would be retrieved from the active ad payload on the node
                string campaignId = ""; 
                string bidId = "";
                
                // playerOrigin is the camera position
                Vector3 playerOrigin = mainCamera != null ? mainCamera.transform.position : Vector3.zero;
                
                WSTelemetryDispatcher.Instance.DispatchImpression(node.placementId, campaignId, bidId, playerOrigin, boundsCorners);
            }
            else
            {
                Debug.LogWarning("[WSGazeVerificationEngine] WSTelemetryDispatcher not found. Impression not sent to Edge.");
            }
        }
    }
}
