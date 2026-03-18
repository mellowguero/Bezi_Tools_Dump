using System.Collections.Generic;
using UnityEngine;

namespace TextureAtlas.Editor
{
    /// <summary>
    /// Persistent configuration for applying an atlas material to prefabs.
    /// References a processed TextureAtlasTask for the material-to-rect mapping,
    /// then remaps UVs and replaces materials on target prefabs.
    /// </summary>
    [CreateAssetMenu(fileName = "NewRemapTask", menuName = "Tools/Atlas Remap Task")]
    public class AtlasRemapTask : ScriptableObject
    {
        /// <summary>The processed atlas task containing the atlas material and rect mapping.</summary>
        public TextureAtlasTask AtlasTask;

        /// <summary>Prefabs whose materials should be replaced with the atlas material.</summary>
        public List<GameObject> TargetPrefabs = new List<GameObject>();

        /// <summary>If true, create new mesh asset copies. If false, modify mesh UVs in-place.</summary>
        public bool CreateMeshCopies = true;

        /// <summary>Folder where mesh copies are saved. Falls back to atlas task output folder.</summary>
        public string MeshOutputFolder = "";

        /// <summary>Whether this remap task has been processed.</summary>
        public bool IsProcessed;

        /// <summary>
        /// Returns the effective output folder for mesh copies.
        /// Falls back to the atlas task's output folder if not explicitly set.
        /// </summary>
        public string GetEffectiveOutputFolder()
        {
            if (!string.IsNullOrEmpty(MeshOutputFolder))
                return MeshOutputFolder;

            if (AtlasTask != null && !string.IsNullOrEmpty(AtlasTask.OutputFolder))
                return AtlasTask.OutputFolder;

            return "Assets/Generated/Atlas";
        }
    }
}
