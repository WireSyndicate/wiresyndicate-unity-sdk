using UnityEngine;
using WireSyndicate.Core;

[DisallowMultipleComponent]
public class WireSyndicateInitializer : MonoBehaviour
{
    public static WireSyndicateInitializer Instance { get; private set; }

    [Header("Authentication")]
    [Tooltip("Your Network Key (org_id) from the Developer Portal")]
    public string networkKey;
    [Tooltip("Your Game Key (game_id) from the Developer Portal")]
    public string gameId;

    [Header("Network Configuration")]
    [Tooltip("Target URL. E.g., https://api.wiresyndicate.com")]
    public string apiBaseUrl = "https://api.wiresyndicate.com";

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
            GameId = gameId,
            ApiBaseUrl = apiBaseUrl,
            EnableDebugLogging = false
        });

        Debug.Log("[WireSyndicate] Edge connection established.");
    }
}
