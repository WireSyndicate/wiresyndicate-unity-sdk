using UnityEngine;
using WireSyndicate.Core;

[DisallowMultipleComponent]
public class WireSyndicateInitializer : MonoBehaviour
{
    public static WireSyndicateInitializer Instance { get; private set; }

    [Header("Authentication")]
    [Tooltip("Your Network Key (org_id) from the Developer Portal")]
    public string networkKey;

    [Header("Network Configuration")]
    [Tooltip("Target URL. E.g., http://localhost:3000 or https://api.wiresyndicate.com")]
    public string apiBaseUrl = "http://localhost:3000";

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[WireSyndicate] Duplicate Initializer detected and destroyed.");
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (string.IsNullOrEmpty(networkKey))
        {
            Debug.LogError("[WireSyndicate] FATAL: Network Key is missing. Initialization aborted.");
            return;
        }

        WireSyndicateEngine.Initialize(new WireSyndicateConfig
        {
            OrgId = networkKey,
            ApiBaseUrl = apiBaseUrl,
            EnableDebugLogging = false
        });

        Debug.Log("[WireSyndicate] Edge connection established.");
    }
}
