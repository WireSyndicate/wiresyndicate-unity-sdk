using UnityEngine;
using WireSyndicate.Core;

[DisallowMultipleComponent]
public class WireSyndicateInitializer : MonoBehaviour
{
    public static WireSyndicateInitializer Instance { get; private set; }

    [Header("Authentication")]
    [Tooltip("Your Network Key (org_id) from the Developer Portal")]
    public string networkKey;

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
            Environment = WireEnvironment.Production,
            EnableDebugLogging = false
        });

        Debug.Log("[WireSyndicate] Edge connection established.");
    }
}
