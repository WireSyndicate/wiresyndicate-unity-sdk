using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace WireSyndicate.SDK.Telemetry
{
    public static class WSOfflineTelemetryCache
    {
        /// <summary>
        /// Wrapper class required because Unity's native JsonUtility cannot serialize top-level Lists or Arrays.
        /// </summary>
        [Serializable]
        private class CacheWrapper
        {
            public List<TelemetryPayload> payloads = new List<TelemetryPayload>();
        }

        // Cache written to the OS-approved persistent data directory to survive app closures
        private static string CacheFilePath => Path.Combine(Application.persistentDataPath, "ws_telemetry_dlq.json");
        private static readonly object _fileLock = new object();

        public static void CachePayload(TelemetryPayload payload)
        {
            lock (_fileLock)
            {
                try
                {
                    CacheWrapper wrapper = LoadCache();
                    wrapper.payloads.Add(payload);
                    SaveCache(wrapper);
                    Debug.Log($"[WireSyndicate] Payload {payload.placementId} safely cached to local offline queue.");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[WireSyndicate] CRITICAL: Failed to write telemetry to local disk. {e.Message}");
                }
            }
        }

        public static List<TelemetryPayload> GetAndClearCache()
        {
            lock (_fileLock)
            {
                try
                {
                    CacheWrapper wrapper = LoadCache();
                    if (wrapper.payloads.Count > 0)
                    {
                        // Atomically clear the file upon retrieval to prevent duplicate billing
                        SaveCache(new CacheWrapper());
                        return wrapper.payloads;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[WireSyndicate] Failed to read telemetry from local disk. {e.Message}");
                }
                return new List<TelemetryPayload>();
            }
        }

        private static CacheWrapper LoadCache()
        {
            if (File.Exists(CacheFilePath))
            {
                string json = File.ReadAllText(CacheFilePath);
                return JsonUtility.FromJson<CacheWrapper>(json) ?? new CacheWrapper();
            }
            return new CacheWrapper();
        }

        private static void SaveCache(CacheWrapper wrapper)
        {
            string json = JsonUtility.ToJson(wrapper, false);
            File.WriteAllText(CacheFilePath, json);
        }
    }
}
