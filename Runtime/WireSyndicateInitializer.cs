using UnityEngine;
using WireSyndicate.Core;

public class WireSyndicateInitializer : MonoBehaviour
{
    [Header("Authentication")]
    [Tooltip("Your Network Key (org_id) from the Developer Portal")]
    public string networkKey;

    private void Awake()
    {
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
