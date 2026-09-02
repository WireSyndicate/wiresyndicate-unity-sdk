using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace WireSyndicate.Core
{
    public static class WSAssetCache
    {
        private const string CACHE_DIR_NAME = "WS_Asset_Cache";
        private const long MAX_CACHE_SIZE_BYTES = 50 * 1024 * 1024; // 50MB

        public static string GetCacheDirectory()
        {
            string path = Path.Combine(Application.persistentDataPath, CACHE_DIR_NAME);
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            return path;
        }

        public static string GetCachedAssetPath(string assetHash)
        {
            if (string.IsNullOrEmpty(assetHash)) return null;
            
            string filePath = Path.Combine(GetCacheDirectory(), $"{assetHash}.glb");
            if (File.Exists(filePath))
            {
                // Touch file to update last access time for LRU eviction
                File.SetLastAccessTimeUtc(filePath, DateTime.UtcNow);
                return filePath;
            }
            return null;
        }

        public static string CacheAsset(string assetHash, byte[] data)
        {
            if (string.IsNullOrEmpty(assetHash) || data == null || data.Length == 0) return null;

            string filePath = Path.Combine(GetCacheDirectory(), $"{assetHash}.glb");
            File.WriteAllBytes(filePath, data);
            
            RunGarbageCollection();
            
            return filePath;
        }

        public static void RunGarbageCollection()
        {
            try
            {
                DirectoryInfo dirInfo = new DirectoryInfo(GetCacheDirectory());
                FileInfo[] files = dirInfo.GetFiles("*.glb").OrderBy(f => f.LastAccessTimeUtc).ToArray();

                long currentSizeBytes = files.Sum(f => f.Length);

                foreach (FileInfo file in files)
                {
                    if (currentSizeBytes <= MAX_CACHE_SIZE_BYTES)
                    {
                        break; // Under limit
                    }

                    currentSizeBytes -= file.Length;
                    file.Delete();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[WireSyndicate] Cache GC failed: {e.Message}");
            }
        }
    }
}
