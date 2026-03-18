using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TextureAtlas.Editor
{
    /// <summary>
    /// Static utility for replacing materials and remapping UVs on prefabs
    /// using a processed TextureAtlasTask's atlas mapping.
    /// </summary>
    public static class AtlasRemapGenerator
    {
        private const string LOG_PREFIX = "[AtlasRemapGenerator]";

        /// <summary>
        /// Main entry point. Replaces materials and remaps UVs on all target prefabs.
        /// </summary>
        public static void ProcessTask(AtlasRemapTask task)
        {
            if (task == null)
            {
                Debug.LogWarning($"{LOG_PREFIX} Task is null.");
                return;
            }

            if (task.AtlasTask == null)
            {
                Debug.LogWarning($"{LOG_PREFIX} No atlas task assigned. Aborting.");
                return;
            }

            if (!task.AtlasTask.IsProcessed)
            {
                Debug.LogWarning($"{LOG_PREFIX} Atlas task has not been processed yet. Generate the atlas first.");
                return;
            }

            if (task.AtlasTask.GeneratedMaterial == null)
            {
                Debug.LogWarning($"{LOG_PREFIX} Atlas task has no generated material. Aborting.");
                return;
            }

            if (task.AtlasTask.AtlasMapping == null || task.AtlasTask.AtlasMapping.Count == 0)
            {
                Debug.LogWarning($"{LOG_PREFIX} Atlas task has no atlas mapping data. Aborting.");
                return;
            }

            var validPrefabs = task.TargetPrefabs?.Where(p => p != null).ToList();
            if (validPrefabs == null || validPrefabs.Count == 0)
            {
                Debug.LogWarning($"{LOG_PREFIX} No valid target prefabs found. Aborting.");
                return;
            }

            string outputFolder = task.GetEffectiveOutputFolder();
            EnsureDirectoryExists(outputFolder);

            // Build lookup from the serialized atlas mapping
            var materialToRect = new Dictionary<Material, Rect>();
            foreach (AtlasRectEntry entry in task.AtlasTask.AtlasMapping)
            {
                if (entry.Material != null)
                {
                    materialToRect[entry.Material] = entry.Rect;
                }
            }

            Material atlasMaterial = task.AtlasTask.GeneratedMaterial;
            int successCount = 0;
            int skipCount = 0;

            foreach (GameObject prefab in validPrefabs)
            {
                string prefabPath = AssetDatabase.GetAssetPath(prefab);
                if (string.IsNullOrEmpty(prefabPath))
                {
                    Debug.LogWarning($"{LOG_PREFIX} Could not resolve asset path for prefab '{prefab.name}'. Skipping.");
                    skipCount++;
                    continue;
                }

                bool success = RemapPrefabUVs(
                    prefabPath,
                    materialToRect,
                    atlasMaterial,
                    task.CreateMeshCopies,
                    outputFolder);

                if (success)
                    successCount++;
                else
                    skipCount++;
            }

            // Mark task as processed
            Undo.RecordObject(task, "Process Atlas Remap Task");
            task.IsProcessed = true;
            EditorUtility.SetDirty(task);
            AssetDatabase.SaveAssets();

            Debug.Log($"{LOG_PREFIX} Remap complete. {successCount} prefab(s) processed, {skipCount} skipped.");
        }

        /// <summary>
        /// Remaps UVs on a prefab's meshes so each sub-mesh points to its
        /// correct region in the atlas. Only replaces materials that are in the atlas mapping.
        /// </summary>
        public static bool RemapPrefabUVs(
            string prefabPath,
            Dictionary<Material, Rect> materialToRect,
            Material atlasMaterial,
            bool createMeshCopies,
            string outputFolder)
        {
            // PHASE 1: Ensure all meshes in the prefab are readable BEFORE loading contents.
            // SaveAndReimport invalidates loaded assets, so this must happen first.
            EnsurePrefabMeshesReadable(prefabPath);

            // PHASE 2: Now load prefab contents with readable meshes
            GameObject prefabContents = PrefabUtility.LoadPrefabContents(prefabPath);
            if (prefabContents == null)
            {
                Debug.LogWarning($"{LOG_PREFIX} Could not load prefab contents at '{prefabPath}'. Skipping.");
                return false;
            }

            bool anyProcessed = false;

            try
            {
                // Process MeshRenderer + MeshFilter pairs
                var meshFilters = prefabContents.GetComponentsInChildren<MeshFilter>(true);
                foreach (MeshFilter mf in meshFilters)
                {
                    MeshRenderer mr = mf.GetComponent<MeshRenderer>();
                    if (mr == null) continue;

                    Mesh mesh = mf.sharedMesh;
                    if (mesh == null) continue;

                    Mesh processedMesh = RemapMeshUVs(
                        mesh, mr.sharedMaterials, materialToRect,
                        createMeshCopies, outputFolder, prefabContents.name);

                    if (processedMesh != null)
                    {
                        mf.sharedMesh = processedMesh;
                        mr.sharedMaterials = BuildRemappedMaterialArray(mr.sharedMaterials, materialToRect, atlasMaterial);
                        anyProcessed = true;
                    }
                }

                // Process SkinnedMeshRenderers
                var skinnedRenderers = prefabContents.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                foreach (SkinnedMeshRenderer smr in skinnedRenderers)
                {
                    Mesh mesh = smr.sharedMesh;
                    if (mesh == null) continue;

                    Mesh processedMesh = RemapMeshUVs(
                        mesh, smr.sharedMaterials, materialToRect,
                        createMeshCopies, outputFolder, prefabContents.name);

                    if (processedMesh != null)
                    {
                        smr.sharedMesh = processedMesh;
                        smr.sharedMaterials = BuildRemappedMaterialArray(smr.sharedMaterials, materialToRect, atlasMaterial);
                        anyProcessed = true;
                    }
                }

                if (anyProcessed)
                {
                    PrefabUtility.SaveAsPrefabAsset(prefabContents, prefabPath);
                    Debug.Log($"{LOG_PREFIX} Remapped prefab: {prefabPath}");
                }
                else
                {
                    Debug.LogWarning($"{LOG_PREFIX} No renderers with matching materials found in '{prefabPath}'.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabContents);
            }

            return anyProcessed;
        }

        /// <summary>
        /// Pre-scans a prefab and enables Read/Write on any non-readable mesh imports.
        /// Must be called BEFORE PrefabUtility.LoadPrefabContents since reimport invalidates references.
        /// </summary>
        private static void EnsurePrefabMeshesReadable(string prefabPath)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) return;

            var meshAssetPaths = new HashSet<string>();

            // Collect all mesh asset paths from MeshFilters
            var meshFilters = prefab.GetComponentsInChildren<MeshFilter>(true);
            foreach (MeshFilter mf in meshFilters)
            {
                if (mf.sharedMesh != null)
                {
                    string meshPath = AssetDatabase.GetAssetPath(mf.sharedMesh);
                    if (!string.IsNullOrEmpty(meshPath))
                        meshAssetPaths.Add(meshPath);
                }
            }

            // Collect from SkinnedMeshRenderers
            var skinnedRenderers = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (SkinnedMeshRenderer smr in skinnedRenderers)
            {
                if (smr.sharedMesh != null)
                {
                    string meshPath = AssetDatabase.GetAssetPath(smr.sharedMesh);
                    if (!string.IsNullOrEmpty(meshPath))
                        meshAssetPaths.Add(meshPath);
                }
            }

            // Enable Read/Write on each model importer that needs it
            foreach (string meshPath in meshAssetPaths)
            {
                ModelImporter importer = AssetImporter.GetAtPath(meshPath) as ModelImporter;
                if (importer != null && !importer.isReadable)
                {
                    Debug.Log($"{LOG_PREFIX} Enabling Read/Write on '{meshPath}' for UV remapping.");
                    importer.isReadable = true;
                    importer.SaveAndReimport();
                }
            }
        }

        /// <summary>
        /// Builds a new material array where atlas-mapped materials are replaced
        /// with the atlas material, and non-atlas materials are kept as-is.
        /// </summary>
        private static Material[] BuildRemappedMaterialArray(
            Material[] originalMaterials,
            Dictionary<Material, Rect> materialToRect,
            Material atlasMaterial)
        {
            Material[] result = new Material[originalMaterials.Length];
            for (int i = 0; i < originalMaterials.Length; i++)
            {
                Material mat = originalMaterials[i];
                if (mat != null && materialToRect.ContainsKey(mat))
                {
                    result[i] = atlasMaterial;
                }
                else
                {
                    result[i] = mat; // Keep original (glass, transparent, etc.)
                }
            }
            return result;
        }

        /// <summary>
        /// Remaps UVs on a mesh based on material-to-rect mapping.
        /// Handles shared vertices between sub-meshes by duplicating them.
        /// </summary>
        private static Mesh RemapMeshUVs(
            Mesh sourceMesh,
            Material[] rendererMaterials,
            Dictionary<Material, Rect> materialToRect,
            bool createCopy,
            string outputFolder,
            string prefabName)
        {
            // Check if any renderer materials are in the atlas (cheap check first)
            bool hasMatchingMaterial = false;
            foreach (Material mat in rendererMaterials)
            {
                if (mat != null && materialToRect.ContainsKey(mat))
                {
                    hasMatchingMaterial = true;
                    break;
                }
            }
            if (!hasMatchingMaterial) return null;

            if (!sourceMesh.isReadable)
            {
                Debug.LogWarning($"{LOG_PREFIX} Mesh '{sourceMesh.name}' is still not readable after import fix. Skipping.");
                return null;
            }

            Mesh mesh;
            if (createCopy)
            {
                mesh = Object.Instantiate(sourceMesh);
                mesh.name = $"{sourceMesh.name}_Atlas";

                string meshPath = Path.Combine(outputFolder, $"{prefabName}_{mesh.name}.asset");
                EnsureDirectoryExists(outputFolder);
                meshPath = AssetDatabase.GenerateUniqueAssetPath(meshPath);
                AssetDatabase.CreateAsset(mesh, meshPath);
            }
            else
            {
                mesh = sourceMesh;
            }

            int subMeshCount = mesh.subMeshCount;
            Vector2[] uvs = mesh.uv;
            bool hadNoUVs = false;

            // If mesh has no UVs, generate default UVs (all at 0,0 — will be remapped below)
            if (uvs == null || uvs.Length == 0)
            {
                Debug.Log($"{LOG_PREFIX} Mesh '{mesh.name}' has no UVs. Generating atlas UVs for flat-color materials.");
                uvs = new Vector2[mesh.vertexCount];
                mesh.SetUVs(0, new List<Vector2>(uvs));
                hadNoUVs = true;
            }

            // Detect shared vertices between sub-meshes
            int[] vertexOwner = new int[mesh.vertexCount];
            for (int i = 0; i < vertexOwner.Length; i++) vertexOwner[i] = -1;

            bool hasSharedVertices = false;
            var subMeshVertexSets = new HashSet<int>[subMeshCount];

            for (int subIdx = 0; subIdx < subMeshCount; subIdx++)
            {
                int[] triangles = mesh.GetTriangles(subIdx);
                subMeshVertexSets[subIdx] = new HashSet<int>(triangles);

                foreach (int vertIdx in subMeshVertexSets[subIdx])
                {
                    if (vertexOwner[vertIdx] != -1 && vertexOwner[vertIdx] != subIdx)
                        hasSharedVertices = true;
                    vertexOwner[vertIdx] = subIdx;
                }
            }

            if (hasSharedVertices)
            {
                Debug.Log($"{LOG_PREFIX} Mesh '{mesh.name}' has shared vertices between sub-meshes. Duplicating to resolve UV conflicts.");
                mesh = DuplicateSharedVertices(mesh, subMeshVertexSets);
                uvs = mesh.uv;

                if (createCopy)
                {
                    EditorUtility.SetDirty(mesh);
                }
            }

            // Remap UVs per sub-mesh — only for sub-meshes with matching materials
            Vector2[] newUVs = new Vector2[uvs.Length];
            System.Array.Copy(uvs, newUVs, uvs.Length);

            for (int subIdx = 0; subIdx < mesh.subMeshCount; subIdx++)
            {
                Material subMeshMaterial = subIdx < rendererMaterials.Length ? rendererMaterials[subIdx] : null;

                if (subMeshMaterial == null || !materialToRect.TryGetValue(subMeshMaterial, out Rect atlasRect))
                {
                    // Not in atlas — leave UVs unchanged for this sub-mesh
                    continue;
                }

                int[] triangles = mesh.GetTriangles(subIdx);
                HashSet<int> uniqueVertices = new HashSet<int>(triangles);

                foreach (int vertIdx in uniqueVertices)
                {
                    if (vertIdx < newUVs.Length)
                    {
                        if (hadNoUVs)
                        {
                            // No original UVs — point to center of atlas cell (flat color)
                            newUVs[vertIdx] = new Vector2(
                                atlasRect.x + atlasRect.width * 0.5f,
                                atlasRect.y + atlasRect.height * 0.5f);
                        }
                        else
                        {
                            // Remap existing UVs into the atlas rect
                            Vector2 originalUV = uvs[vertIdx];
                            float wrappedU = originalUV.x - Mathf.Floor(originalUV.x);
                            float wrappedV = originalUV.y - Mathf.Floor(originalUV.y);
                            newUVs[vertIdx] = new Vector2(
                                atlasRect.x + wrappedU * atlasRect.width,
                                atlasRect.y + wrappedV * atlasRect.height);
                        }
                    }
                }
            }

            mesh.SetUVs(0, newUVs.ToList());

            if (createCopy)
            {
                EditorUtility.SetDirty(mesh);
                AssetDatabase.SaveAssets();
            }

            return mesh;
        }

        /// <summary>
        /// Duplicates vertices shared between multiple sub-meshes,
        /// so each sub-mesh has its own copy for independent UV remapping.
        /// </summary>
        private static Mesh DuplicateSharedVertices(Mesh mesh, HashSet<int>[] subMeshVertexSets)
        {
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector4[] tangents = mesh.tangents;
            Vector2[] uvs = mesh.uv;
            Color[] colors = mesh.colors;
            BoneWeight[] boneWeights = mesh.boneWeights;

            var newVertices = new List<Vector3>(vertices);
            var newNormals = new List<Vector3>(normals.Length > 0 ? normals : new Vector3[vertices.Length]);
            var newTangents = new List<Vector4>(tangents.Length > 0 ? tangents : new Vector4[vertices.Length]);
            var newUVs = new List<Vector2>(uvs.Length > 0 ? uvs : new Vector2[vertices.Length]);
            var newColors = new List<Color>(colors.Length > 0 ? colors : new Color[0]);
            var newBoneWeights = new List<BoneWeight>(boneWeights.Length > 0 ? boneWeights : new BoneWeight[0]);

            bool hasColors = colors.Length > 0;
            bool hasBoneWeights = boneWeights.Length > 0;

            int[] firstOwner = new int[vertices.Length];
            for (int i = 0; i < firstOwner.Length; i++) firstOwner[i] = -1;

            int subMeshCount = mesh.subMeshCount;
            int[][] subMeshTriangles = new int[subMeshCount][];
            for (int s = 0; s < subMeshCount; s++)
            {
                subMeshTriangles[s] = mesh.GetTriangles(s);
            }

            for (int s = 0; s < subMeshCount; s++)
            {
                var remapDict = new Dictionary<int, int>();

                for (int t = 0; t < subMeshTriangles[s].Length; t++)
                {
                    int vertIdx = subMeshTriangles[s][t];

                    if (firstOwner[vertIdx] == -1)
                    {
                        firstOwner[vertIdx] = s;
                    }
                    else if (firstOwner[vertIdx] != s)
                    {
                        if (!remapDict.TryGetValue(vertIdx, out int newIdx))
                        {
                            newIdx = newVertices.Count;
                            newVertices.Add(vertices[vertIdx]);
                            newNormals.Add(normals.Length > vertIdx ? normals[vertIdx] : Vector3.up);
                            newTangents.Add(tangents.Length > vertIdx ? tangents[vertIdx] : Vector4.zero);
                            newUVs.Add(uvs.Length > vertIdx ? uvs[vertIdx] : Vector2.zero);
                            if (hasColors) newColors.Add(colors[vertIdx]);
                            if (hasBoneWeights) newBoneWeights.Add(boneWeights[vertIdx]);
                            remapDict[vertIdx] = newIdx;
                        }

                        subMeshTriangles[s][t] = newIdx;
                    }
                }
            }

            mesh.SetVertices(newVertices);
            mesh.SetNormals(newNormals);
            mesh.SetTangents(newTangents);
            mesh.SetUVs(0, newUVs);
            if (hasColors) mesh.SetColors(newColors);
            if (hasBoneWeights) mesh.boneWeights = newBoneWeights.ToArray();

            for (int s = 0; s < subMeshCount; s++)
            {
                mesh.SetTriangles(subMeshTriangles[s], s);
            }

            mesh.RecalculateBounds();
            return mesh;
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
}
