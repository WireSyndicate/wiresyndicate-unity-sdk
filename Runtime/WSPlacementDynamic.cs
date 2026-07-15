using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace AssetSyndicateNetwork.SDK
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

        [Header("API Configuration")]
        [Tooltip("The Edge API endpoint for fetching the placement manifest. Example: https://<project-ref>.supabase.co/functions/v1/active-contracts")]
        public string apiEndpoint = "";

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
                Debug.LogWarning($"[WS] The placement object '{gameObject.name}' is missing a Placement ID!");
                return;
            }

            StartCoroutine(FetchAndApplyTexture());
        }

        private IEnumerator FetchAndApplyTexture()
        {
            string url = $"{apiEndpoint}?id={placementId}";
            using (UnityWebRequest webRequest = UnityWebRequest.Get(url))
            {
                yield return webRequest.SendWebRequest();

                if (webRequest.result != UnityWebRequest.Result.Success)
                {
                    Debug.Log($"[WS] No active contract found or network error for {placementId}: {webRequest.error}. Native fallback visual retained.");
                    yield break;
                }

                string json = webRequest.downloadHandler.text;
                ContractManifest manifest = JsonUtility.FromJson<ContractManifest>(json);

                if (manifest != null && !string.IsNullOrEmpty(manifest.game_ready_manifest))
                {
                    yield return DownloadTexture(manifest.game_ready_manifest);
                }
                else
                {
                    Debug.LogWarning($"[WS] Parsed manifest was null or empty for {placementId}.");
                }
            }
        }

        private IEnumerator DownloadTexture(string textureUrl)
        {
            using (UnityWebRequest uwr = UnityWebRequestTexture.GetTexture(textureUrl))
            {
                yield return uwr.SendWebRequest();

                if (uwr.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[WS] Failed to download texture for placement {placementId}: {uwr.error}");
                    yield break;
                }

                Texture2D texture = DownloadHandlerTexture.GetContent(uwr);
                
                // THE ARCHITECT'S LESSON: Non-Destructive Texture Swapping
                // Using MaterialPropertyBlock prevents the creation of new material instances in memory,
                // avoiding memory leaks and keeping the base material untouched.
                _renderer.GetPropertyBlock(_propBlock);
                _propBlock.SetTexture(texturePropertyName, texture);
                _renderer.SetPropertyBlock(_propBlock);
                
                Debug.Log($"[WS] Texture swapped successfully for '{gameObject.name}' (Placement: {placementId}).");
            }
        }
    }
}
