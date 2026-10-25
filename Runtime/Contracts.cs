using System;
using System.Collections.Generic;

namespace WireSyndicate.SDK.Contracts
{
    /// <summary>
    /// Response payload for successful authentication
    /// </summary>
    [Serializable]
    public class AuthResponse
    {
        public string token;
        public float expires_in;
        public string user_id;
    }

    /// <summary>
    /// Request payload from a DSP for an ad placement
    /// </summary>
    [Serializable]
    public class DspBidRequest
    {
        public string placement_id;
        public float floor_price;
        public string device_type;
        public string user_agent;
        public Dictionary<string, string> context;
    }

    /// <summary>
    /// Response payload from a DSP with the winning bid
    /// </summary>
    [Serializable]
    public class DspBidResponse
    {
        public string bid_id;
        public float price;
        public string ad_markup;
        public string creative_id;
    }

    /// <summary>
    /// Cryptographic tracking tokens for impressions and clicks (null for house assets)
    /// </summary>
    [Serializable]
    public class SDKTrackingData
    {
        public string impression_token;
        public string click_token;
    }

    /// <summary>
    /// Edge delivery waterfall payload matching Section 14 of API_CONTRACTS.md
    /// </summary>
    [Serializable]
    public class SDKDeliveryResponse
    {
        public string placement_id;
        public string content_type;
        public string asset_url;
        public string click_url;
        public string promo_code;
        public string cta_overlay_text;
        public string format;
        public string prominence;
        public string monetization_mode;
        public SDKTrackingData tracking;
    }

}
