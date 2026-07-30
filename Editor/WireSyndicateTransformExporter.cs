using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WireSyndicate.Editor
{
    public class WireSyndicateTransformExporter : EditorWindow
    {
        private Material targetMaterial;

        [MenuItem("WireSyndicate/Export Shared Material Transforms")]
        public static void ShowWindow()
        {
            GetWindow<WireSyndicateTransformExporter>("WS Transform Exporter");
        }

        private void OnGUI()
        {
            GUILayout.Label("Export Material Transforms", EditorStyles.boldLabel);
            
            targetMaterial = (Material)EditorGUILayout.ObjectField("Target Material", targetMaterial, typeof(Material), false);

            if (GUILayout.Button("Scan and Export to JSON"))
            {
                if (targetMaterial == null)
                {
                    EditorUtility.DisplayDialog("Error", "Please select a target Material.", "OK");
                    return;
                }

                ExportTransforms();
            }
        }

        private void ExportTransforms()
        {
            var renderers = FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
            var matchingRenderers = renderers.Where(r => r.sharedMaterials.Contains(targetMaterial)).ToList();

            if (matchingRenderers.Count == 0)
            {
                EditorUtility.DisplayDialog("Result", "No objects found using the selected material.", "OK");
                return;
            }

            if (matchingRenderers.Count > 150)
            {
                Debug.LogWarning("Target material is assigned to more than 150 objects. Multi-node limit exceeded.");
                EditorUtility.DisplayDialog("Error", "Target material is assigned to more than 150 objects. Multi-node limit exceeded.", "OK");
                return;
            }

            var exportData = new List<TransformData>();

            foreach (var renderer in matchingRenderers)
            {
                var t = renderer.transform;
                exportData.Add(new TransformData
                {
                    position = new Vector3Data(t.position),
                    rotation = new Vector3Data(t.eulerAngles),
                    scale = new Vector3Data(t.lossyScale)
                });
            }

            string json = JsonUtility.ToJson(new TransformDataWrapper { data = exportData }, true);
            
            // Extract pure array from wrapper to match Zod schema
            int start = json.IndexOf('[');
            int end = json.LastIndexOf(']');
            if (start != -1 && end != -1)
            {
                json = json.Substring(start, end - start + 1);
            }
            
            string path = EditorUtility.SaveFilePanel("Save Transforms", "", "transforms.json", "json");
            if (!string.IsNullOrEmpty(path))
            {
                File.WriteAllText(path, json);
                EditorUtility.DisplayDialog("Success", $"Exported {exportData.Count} transforms to JSON.", "OK");
            }
        }

        [System.Serializable]
        private class TransformDataWrapper
        {
            public List<TransformData> data;
        }

        [System.Serializable]
        private class TransformData
        {
            public Vector3Data position;
            public Vector3Data rotation;
            public Vector3Data scale;
        }

        [System.Serializable]
        private class Vector3Data
        {
            public float x;
            public float y;
            public float z;

            public Vector3Data(Vector3 v)
            {
                x = (float)System.Math.Round(v.x, 4);
                y = (float)System.Math.Round(v.y, 4);
                z = (float)System.Math.Round(v.z, 4);
            }
        }
    }
}
