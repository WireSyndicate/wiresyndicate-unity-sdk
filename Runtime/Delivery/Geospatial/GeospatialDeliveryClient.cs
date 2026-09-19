using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace WireSyndicate.Delivery.Geospatial
{
    /// <summary>
    /// Request payload for the spatial delivery RPC.
    /// </summary>
    [Serializable]
    public struct SpatialResolutionRequest
    {
        public float player_lng;
        public float player_lat;
    }

    /// <summary>
    /// DTO representing the JSON response from the resolve_spatial_campaigns PostgREST RPC.
    /// </summary>
    [Serializable]
    public class SpatialCampaignResolutionDTO
    {
        public string campaign_id;
        public string brand_id;
        public string asset_url;
    }

    /// <summary>
    /// Wrapper to allow Unity's native JsonUtility to deserialize top-level JSON arrays.
    /// </summary>
    [Serializable]
    private class SpatialResolutionResponseWrapper
    {
        public List<SpatialCampaignResolutionDTO> items;
    }

    /// <summary>
    /// Core interface for fetching spatially validated 3D campaigns from the WireSyndicate Edge.
    /// </summary>
    public interface IGeospatialDeliveryClient
    {
        Task<List<SpatialCampaignResolutionDTO>> ResolveSpatialCampaignsAsync(float playerLongitude, float playerLatitude);
    }

    /// <summary>
    /// HTTP Client implementation for resolving geospatial campaigns against the Supabase PostgREST endpoint.
    /// </summary>
    public class GeospatialDeliveryClient : IGeospatialDeliveryClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _endpointUrl;

        public GeospatialDeliveryClient(string supabaseUrl, string anonKey)
        {
            _httpClient = new HttpClient();
            
            // Standard API Key Header Management for Supabase REST API
            _httpClient.DefaultRequestHeaders.Add("apikey", anonKey);
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {anonKey}");
            
            // Target the PostgREST endpoint
            _endpointUrl = $"{supabaseUrl.TrimEnd('/')}/rest/v1/rpc/resolve_spatial_campaigns";
        }

        public async Task<List<SpatialCampaignResolutionDTO>> ResolveSpatialCampaignsAsync(float playerLongitude, float playerLatitude)
        {
            try
            {
                var requestPayload = new SpatialResolutionRequest
                {
                    player_lng = playerLongitude,
                    player_lat = playerLatitude
                };

                // Serialize payload natively
                string jsonPayload = JsonUtility.ToJson(requestPayload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                // Execute asynchronous HTTP request
                HttpResponseMessage response = await _httpClient.PostAsync(_endpointUrl, content);
                response.EnsureSuccessStatusCode();

                string jsonResponse = await response.Content.ReadAsStringAsync();

                // Wrap the PostgREST JSON array payload to comply with Unity's JsonUtility restrictions
                string wrappedJson = $"{{\"items\":{jsonResponse}}}";
                
                // Deserialize response natively
                var wrapper = JsonUtility.FromJson<SpatialResolutionResponseWrapper>(wrappedJson);
                return wrapper?.items ?? new List<SpatialCampaignResolutionDTO>();
            }
            catch (Exception ex)
            {
                // Standard exception handling for networking errors
                Debug.LogError($"[GeospatialDeliveryClient] Spatial Resolution Failed: {ex.Message}");
                return new List<SpatialCampaignResolutionDTO>();
            }
        }
    }
}
