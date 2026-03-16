using System.Collections.Generic;
using UnityEngine;

namespace FBXImporter.Editor
{
    /// <summary>
    /// Persistent config for a single imported FBX, storing user decisions
    /// about how to split it into prefabs.
    /// </summary>
    public class FBXImportTask : ScriptableObject
    {
        /// <summary>Reference to the imported FBX asset inside Unity.</summary>
        public GameObject SourceFBX;

        /// <summary>Path where generated prefabs will be saved.</summary>
        public string PrefabOutputFolder;

        /// <summary>Path where extracted materials are stored.</summary>
        public string MaterialsFolder;

        /// <summary>Path where extracted textures are stored.</summary>
        public string TexturesFolder;

        /// <summary>Per-root-object decisions: split into individual prefab or keep grouped.</summary>
        public List<RootObjectEntry> RootObjects = new List<RootObjectEntry>();

        /// <summary>Detected duplicate groups based on shared mesh data.</summary>
        public List<DuplicateGroup> DuplicateGroups = new List<DuplicateGroup>();

        /// <summary>If true, all roots become one prefab. If false, use per-root decisions.</summary>
        public bool KeepAsSinglePrefab = true;

        /// <summary>Whether this task has already been processed.</summary>
        public bool IsProcessed;

        /// <summary>Paths to prefabs generated during the last processing run.</summary>
        public List<string> GeneratedPrefabPaths = new List<string>();
    }

    /// <summary>
    /// Represents a single root-level child inside an imported FBX hierarchy.
    /// </summary>
    [System.Serializable]
    public class RootObjectEntry
    {
        /// <summary>Name of the root GameObject inside the FBX.</summary>
        public string Name;

        /// <summary>Whether to export this root as its own individual prefab.</summary>
        public bool SplitAsIndividualPrefab;

        /// <summary>
        /// Identifier linking this entry to a DuplicateGroup.
        /// Empty string means no duplicates detected.
        /// </summary>
        public string DuplicateGroupId = string.Empty;
    }

    /// <summary>
    /// A group of root objects that share the same mesh geometry.
    /// When split, only one prefab is created and duplicates reference it.
    /// </summary>
    [System.Serializable]
    public class DuplicateGroup
    {
        /// <summary>Unique identifier for this group (based on the shared mesh name).</summary>
        public string GroupId;

        /// <summary>Names of all root objects in this group.</summary>
        public List<string> MemberNames = new List<string>();

        /// <summary>Vertex count of the shared mesh (for display).</summary>
        public int VertexCount;

        /// <summary>Name of the shared mesh asset.</summary>
        public string SharedMeshName;
    }
}
