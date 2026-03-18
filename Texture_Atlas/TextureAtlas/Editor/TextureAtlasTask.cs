using System;
using System.Collections.Generic;
using UnityEngine;

namespace TextureAtlas.Editor
{
    /// <summary>
    /// Persistent configuration for texture atlas generation.
    /// Stores source materials, atlas settings, and the generated atlas mapping.
    /// Does NOT handle prefab UV remapping — use AtlasRemapTask for that.
    /// </summary>
    [CreateAssetMenu(fileName = "NewAtlasTask", menuName = "Tools/Texture Atlas Task")]
    public class TextureAtlasTask : ScriptableObject
    {
        // --- Source Materials ---

        /// <summary>List of materials to merge into the atlas.</summary>
        public List<Material> SourceMaterials = new List<Material>();

        // --- Atlas Settings ---

        /// <summary>Maximum atlas texture size (power-of-2). Default 2048.</summary>
        public int MaxAtlasSize = 2048;

        /// <summary>Pixel padding between atlas cells. Default 2.</summary>
        public int Padding = 2;

        /// <summary>
        /// Size of each cell in the grid (pixels). Default 64.
        /// For flat-color materials, this is the swatch size.
        /// For textured materials, source textures are resized to fit.
        /// </summary>
        public int CellSize = 64;

        // --- Cleanup ---

        /// <summary>If true, delete original material assets after successful processing.</summary>
        public bool DeleteOriginalMaterials;

        // --- Output ---

        /// <summary>Folder where generated atlas textures and material are saved.</summary>
        public string OutputFolder = "Assets/Generated/Atlas";

        /// <summary>Name prefix for generated assets.</summary>
        public string AssetPrefix = "Atlas";

        // --- State (populated after processing) ---

        /// <summary>Whether this task has been processed.</summary>
        public bool IsProcessed;

        /// <summary>Reference to the generated atlas material.</summary>
        public Material GeneratedMaterial;

        /// <summary>Serialized atlas mapping — material-to-rect lookup for UV remapping.</summary>
        public List<AtlasRectEntry> AtlasMapping = new List<AtlasRectEntry>();

        /// <summary>
        /// Looks up the atlas rect for a given material.
        /// Returns true if found, false otherwise.
        /// </summary>
        public bool TryGetAtlasRect(Material material, out Rect rect)
        {
            for (int i = 0; i < AtlasMapping.Count; i++)
            {
                if (AtlasMapping[i].Material == material)
                {
                    rect = AtlasMapping[i].Rect;
                    return true;
                }
            }
            rect = Rect.zero;
            return false;
        }
    }

    /// <summary>
    /// Serializable entry mapping a material to its atlas rect.
    /// </summary>
    [Serializable]
    public class AtlasRectEntry
    {
        public Material Material;
        public Rect Rect;

        public AtlasRectEntry(Material material, Rect rect)
        {
            Material = material;
            Rect = rect;
        }
    }
}
