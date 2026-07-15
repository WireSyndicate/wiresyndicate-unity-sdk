using System;

namespace WireSyndicate
{
    [Serializable]
    public class AssetResponse
    {
        public string asset_url;
        public string impression_token;
        public long expires_in_ms;
    }

    [Serializable]
    public class TelemetryPayload
    {
        public string impression_token;
        public int duration_ms;
        public float screen_coverage;
    }
}
