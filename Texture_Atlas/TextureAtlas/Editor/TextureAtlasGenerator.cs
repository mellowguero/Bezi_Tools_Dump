using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TextureAtlas.Editor
{
    /// <summary>
    /// Static utility for atlas texture generation and material creation.
    /// Does NOT handle UV remapping — see AtlasRemapGenerator for that.
    /// </summary>
    public static class TextureAtlasGenerator
    {
        private const string LOG_PREFIX = "[TextureAtlasGenerator]";

        private static readonly string[] MAP_PROPERTIES = new[]
        {
            "_BaseMap",
            "_BumpMap",
            "_MetallicGlossMap",
            "_OcclusionMap",
            "_EmissionMap"
        };

        /// <summary>
        /// Main entry point. Generates atlas textures, creates the combined material,
        /// persists the material-to-rect mapping, and optionally deletes old materials.
        /// </summary>
        public static void ProcessTask(TextureAtlasTask task)
        {
            if (task == null)
            {
                Debug.LogWarning($"{LOG_PREFIX} Task is null.");
                return;
            }

            var validMaterials = task.SourceMaterials?.Where(m => m != null).ToList();
            if (validMaterials == null || validMaterials.Count == 0)
            {
                Debug.LogWarning($"{LOG_PREFIX} No valid source materials found. Aborting.");
                return;
            }

            if (string.IsNullOrEmpty(task.OutputFolder))
            {
                Debug.LogWarning($"{LOG_PREFIX} Output folder is empty. Aborting.");
                return;
            }

            EnsureDirectoryExists(task.OutputFolder);

            // Step 1: Generate atlas textures
            AtlasResult atlasResult = GenerateAtlasTextures(
                validMaterials,
                task.CellSize,
                task.Padding,
                task.MaxAtlasSize,
                task.OutputFolder,
                task.AssetPrefix);

            if (atlasResult == null)
            {
                Debug.LogError($"{LOG_PREFIX} Atlas generation failed. Aborting.");
                return;
            }

            // Step 2: Create atlas material
            Material atlasMaterial = CreateAtlasMaterial(atlasResult, task.OutputFolder, task.AssetPrefix);
            if (atlasMaterial == null)
            {
                Debug.LogError($"{LOG_PREFIX} Failed to create atlas material. Aborting.");
                return;
            }

            // Step 3: Optionally delete old materials
            if (task.DeleteOriginalMaterials)
            {
                foreach (Material mat in validMaterials)
                {
                    string matPath = AssetDatabase.GetAssetPath(mat);
                    if (!string.IsNullOrEmpty(matPath))
                    {
                        AssetDatabase.DeleteAsset(matPath);
                        Debug.Log($"{LOG_PREFIX} Deleted original material: {matPath}");
                    }
                }
            }

            // Step 4: Persist results to the task
            Undo.RecordObject(task, "Process Texture Atlas Task");
            task.IsProcessed = true;
            task.GeneratedMaterial = atlasMaterial;

            task.AtlasMapping.Clear();
            foreach (var kvp in atlasResult.MaterialToIndex)
            {
                task.AtlasMapping.Add(new AtlasRectEntry(kvp.Key, atlasResult.Rects[kvp.Value]));
            }

            EditorUtility.SetDirty(task);
            AssetDatabase.SaveAssets();

            Debug.Log($"{LOG_PREFIX} Atlas generated successfully. Material: {AssetDatabase.GetAssetPath(atlasMaterial)}");
        }

        /// <summary>
        /// Generates atlas textures for each map property that has data.
        /// Returns an AtlasResult with the generated textures and mapping data.
        /// </summary>
        private static AtlasResult GenerateAtlasTextures(
            List<Material> materials,
            int cellSize,
            int padding,
            int maxAtlasSize,
            string outputFolder,
            string assetPrefix)
        {
            int count = materials.Count;
            int cellWithPadding = cellSize + padding;

            // Calculate grid dimensions
            int cols = Mathf.CeilToInt(Mathf.Sqrt(count));
            int rows = Mathf.CeilToInt((float)count / cols);

            int atlasWidth = cols * cellWithPadding + padding;
            int atlasHeight = rows * cellWithPadding + padding;

            if (atlasWidth > maxAtlasSize || atlasHeight > maxAtlasSize)
            {
                Debug.LogError($"{LOG_PREFIX} Atlas size ({atlasWidth}x{atlasHeight}) exceeds maximum ({maxAtlasSize}). " +
                               "Reduce cell size, padding, or material count.");
                return null;
            }

            // Build material-to-index mapping and compute rects
            var materialToIndex = new Dictionary<Material, int>();
            var rects = new Rect[count];

            for (int i = 0; i < count; i++)
            {
                materialToIndex[materials[i]] = i;

                int col = i % cols;
                int row = i / cols;

                float x = (col * cellWithPadding + padding) / (float)atlasWidth;
                float y = (row * cellWithPadding + padding) / (float)atlasHeight;
                float w = cellSize / (float)atlasWidth;
                float h = cellSize / (float)atlasHeight;

                rects[i] = new Rect(x, y, w, h);
            }

            // Generate atlas textures for each map property
            var atlasTextures = new Dictionary<string, Texture2D>();

            // Always generate base color map
            Texture2D baseColorAtlas = GenerateMapAtlas(
                materials, "_BaseMap", "_BaseColor",
                cellSize, padding, cols, rows, atlasWidth, atlasHeight, true);

            if (baseColorAtlas != null)
            {
                string baseMapPath = Path.Combine(outputFolder, $"{assetPrefix}_BaseMap.png");
                SaveTexture(baseColorAtlas, baseMapPath);
                atlasTextures["_BaseMap"] = AssetDatabase.LoadAssetAtPath<Texture2D>(baseMapPath);
            }

            // Generate other maps only if any material uses them
            string[] otherMaps = { "_BumpMap", "_MetallicGlossMap", "_OcclusionMap", "_EmissionMap" };
            string[] colorFallbacks = { null, null, null, "_EmissionColor" };

            for (int m = 0; m < otherMaps.Length; m++)
            {
                if (!AnyMaterialHasMap(materials, otherMaps[m])) continue;

                Texture2D mapAtlas = GenerateMapAtlas(
                    materials, otherMaps[m], colorFallbacks[m],
                    cellSize, padding, cols, rows, atlasWidth, atlasHeight, false);

                if (mapAtlas != null)
                {
                    string mapPath = Path.Combine(outputFolder, $"{assetPrefix}{otherMaps[m]}.png");
                    SaveTexture(mapAtlas, mapPath);
                    atlasTextures[otherMaps[m]] = AssetDatabase.LoadAssetAtPath<Texture2D>(mapPath);
                }
            }

            return new AtlasResult
            {
                MaterialToIndex = materialToIndex,
                Rects = rects,
                AtlasTextures = atlasTextures,
                AtlasWidth = atlasWidth,
                AtlasHeight = atlasHeight
            };
        }

        /// <summary>
        /// Generates a single atlas texture for a specific map property.
        /// </summary>
        private static Texture2D GenerateMapAtlas(
            List<Material> materials,
            string mapProperty,
            string colorFallbackProperty,
            int cellSize,
            int padding,
            int cols, int rows,
            int atlasWidth, int atlasHeight,
            bool useBaseColor)
        {
            Texture2D atlas = new Texture2D(atlasWidth, atlasHeight, TextureFormat.RGBA32, false);

            // Fill with default color
            Color defaultColor = useBaseColor ? Color.white : new Color(0.5f, 0.5f, 1f, 1f); // neutral normal
            Color[] fillPixels = new Color[atlasWidth * atlasHeight];
            for (int i = 0; i < fillPixels.Length; i++) fillPixels[i] = defaultColor;
            atlas.SetPixels(fillPixels);

            for (int i = 0; i < materials.Count; i++)
            {
                Material mat = materials[i];
                int col = i % cols;
                int row = i / cols;
                int startX = col * (cellSize + padding) + padding;
                int startY = row * (cellSize + padding) + padding;

                Texture2D sourceTex = null;
                if (mat.HasProperty(mapProperty))
                {
                    sourceTex = mat.GetTexture(mapProperty) as Texture2D;
                }

                if (sourceTex != null)
                {
                    // Read source texture (handle non-readable textures via RenderTexture)
                    Texture2D readableTex = MakeReadable(sourceTex);
                    Texture2D resized = ResizeTexture(readableTex, cellSize, cellSize);

                    atlas.SetPixels(startX, startY, cellSize, cellSize, resized.GetPixels());

                    if (readableTex != sourceTex) Object.DestroyImmediate(readableTex);
                    Object.DestroyImmediate(resized);
                }
                else
                {
                    // Fill cell with solid color
                    Color fallbackColor = defaultColor;

                    if (useBaseColor && mat.HasProperty("_BaseColor"))
                    {
                        fallbackColor = mat.GetColor("_BaseColor");
                    }
                    else if (!string.IsNullOrEmpty(colorFallbackProperty) && mat.HasProperty(colorFallbackProperty))
                    {
                        fallbackColor = mat.GetColor(colorFallbackProperty);
                    }

                    Color[] cellPixels = new Color[cellSize * cellSize];
                    for (int p = 0; p < cellPixels.Length; p++) cellPixels[p] = fallbackColor;
                    atlas.SetPixels(startX, startY, cellSize, cellSize, cellPixels);
                }
            }

            atlas.Apply();
            return atlas;
        }

        /// <summary>
        /// Creates a URP/Lit material using the generated atlas textures.
        /// </summary>
        private static Material CreateAtlasMaterial(AtlasResult result, string outputFolder, string assetPrefix)
        {
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (litShader == null)
            {
                Debug.LogError($"{LOG_PREFIX} Could not find URP/Lit shader.");
                return null;
            }

            Material mat = new Material(litShader);
            mat.name = $"{assetPrefix}_Material";

            if (result.AtlasTextures.TryGetValue("_BaseMap", out Texture2D baseMap))
            {
                mat.SetTexture("_BaseMap", baseMap);
                mat.SetColor("_BaseColor", Color.white);
            }

            if (result.AtlasTextures.TryGetValue("_BumpMap", out Texture2D bumpMap))
            {
                mat.SetTexture("_BumpMap", bumpMap);
                mat.EnableKeyword("_NORMALMAP");
            }

            if (result.AtlasTextures.TryGetValue("_MetallicGlossMap", out Texture2D metallicMap))
            {
                mat.SetTexture("_MetallicGlossMap", metallicMap);
                mat.EnableKeyword("_METALLICGLOSSMAP");
            }

            if (result.AtlasTextures.TryGetValue("_OcclusionMap", out Texture2D occlusionMap))
            {
                mat.SetTexture("_OcclusionMap", occlusionMap);
            }

            if (result.AtlasTextures.TryGetValue("_EmissionMap", out Texture2D emissionMap))
            {
                mat.SetTexture("_EmissionMap", emissionMap);
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", Color.white);
            }

            string matPath = Path.Combine(outputFolder, $"{assetPrefix}_Material.mat");
            matPath = AssetDatabase.GenerateUniqueAssetPath(matPath);
            AssetDatabase.CreateAsset(mat, matPath);
            AssetDatabase.SaveAssets();

            // Configure texture import settings
            foreach (var kvp in result.AtlasTextures)
            {
                ConfigureAtlasTextureImport(AssetDatabase.GetAssetPath(kvp.Value), kvp.Key);
            }

            return AssetDatabase.LoadAssetAtPath<Material>(matPath);
        }

        /// <summary>
        /// Configures import settings for atlas textures (sRGB, filter mode, etc.).
        /// </summary>
        private static void ConfigureAtlasTextureImport(string texturePath, string mapProperty)
        {
            if (string.IsNullOrEmpty(texturePath)) return;

            TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer == null) return;

            importer.textureType = mapProperty == "_BumpMap"
                ? TextureImporterType.NormalMap
                : TextureImporterType.Default;

            // Non-color data for metallic, occlusion, normal maps
            importer.sRGBTexture = mapProperty == "_BaseMap" || mapProperty == "_EmissionMap";
            importer.filterMode = FilterMode.Point; // Sharp pixel boundaries
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        /// <summary>
        /// Checks if any material in the list uses the specified map property.
        /// </summary>
        private static bool AnyMaterialHasMap(List<Material> materials, string mapProperty)
        {
            foreach (Material mat in materials)
            {
                if (mat != null && mat.HasProperty(mapProperty) && mat.GetTexture(mapProperty) != null)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Makes a texture readable by copying it through a RenderTexture.
        /// </summary>
        private static Texture2D MakeReadable(Texture2D source)
        {
            if (source.isReadable) return source;

            RenderTexture rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, rt);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;

            Texture2D readable = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            readable.Apply();

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);

            return readable;
        }

        /// <summary>
        /// Resizes a texture to the target dimensions using bilinear filtering.
        /// </summary>
        private static Texture2D ResizeTexture(Texture2D source, int targetWidth, int targetHeight)
        {
            RenderTexture rt = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32);
            rt.filterMode = FilterMode.Bilinear;
            Graphics.Blit(source, rt);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;

            Texture2D resized = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);
            resized.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
            resized.Apply();

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);

            return resized;
        }

        /// <summary>
        /// Saves a Texture2D to disk as PNG and imports it.
        /// </summary>
        private static void SaveTexture(Texture2D texture, string path)
        {
            byte[] pngData = texture.EncodeToPNG();
            File.WriteAllBytes(path, pngData);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            Object.DestroyImmediate(texture);
        }

        /// <summary>
        /// Ensures the directory at the given path exists.
        /// </summary>
        private static void EnsureDirectoryExists(string folderPath)
        {
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
                AssetDatabase.Refresh();
            }
        }
    }

    /// <summary>
    /// Intermediate result from atlas texture generation.
    /// </summary>
    public class AtlasResult
    {
        public Dictionary<Material, int> MaterialToIndex;
        public Rect[] Rects;
        public Dictionary<string, Texture2D> AtlasTextures;
        public int AtlasWidth;
        public int AtlasHeight;
    }
}
