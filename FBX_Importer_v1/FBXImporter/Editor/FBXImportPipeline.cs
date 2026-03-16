using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FBXImporter.Editor
{
    /// <summary>
    /// Static utility class containing all FBX import and processing logic.
    /// Handles copying external FBX files into Unity, extracting materials/textures,
    /// and generating unpacked prefabs based on user decisions.
    /// </summary>
    public static class FBXImportPipeline
    {
        private const string LOG_PREFIX = "[FBXImportPipeline]";
        private const string FBX_EXTENSION = "*.fbx";
        private const string URP_LIT_SHADER_NAME = "Universal Render Pipeline/Lit";

        /// <summary>
        /// Copies all .fbx files from externalFolderPath into Unity at
        /// destinationFolder, extracts materials/textures, and creates
        /// FBXImportTask ScriptableObjects for each.
        /// </summary>
        public static List<FBXImportTask> ImportFromExternalFolder(
            string externalFolderPath,
            string destinationFolder,
            string prefabOutputFolder)
        {
            var createdTasks = new List<FBXImportTask>();

            if (!Directory.Exists(externalFolderPath))
            {
                Debug.LogWarning($"{LOG_PREFIX} External folder does not exist: {externalFolderPath}");
                return createdTasks;
            }

            if (string.IsNullOrEmpty(destinationFolder))
            {
                Debug.LogWarning($"{LOG_PREFIX} Destination folder is empty.");
                return createdTasks;
            }

            if (string.IsNullOrEmpty(prefabOutputFolder))
            {
                Debug.LogWarning($"{LOG_PREFIX} Prefab output folder is empty.");
                return createdTasks;
            }

            EnsureDirectoryExists(destinationFolder);
            EnsureDirectoryExists(prefabOutputFolder);

            string[] fbxFiles = Directory.GetFiles(externalFolderPath, FBX_EXTENSION, SearchOption.AllDirectories);

            if (fbxFiles.Length == 0)
            {
                Debug.LogWarning($"{LOG_PREFIX} No .fbx files found in: {externalFolderPath}");
                return createdTasks;
            }

            Debug.Log($"{LOG_PREFIX} Found {fbxFiles.Length} FBX file(s) to import.");

            foreach (string sourcePath in fbxFiles)
            {
                string fileName = Path.GetFileName(sourcePath);
                string destPath = Path.Combine(destinationFolder, fileName);
                string destAssetPath = destPath.Replace("\\", "/");

                // Check for duplicates
                if (File.Exists(destPath))
                {
                    Debug.LogWarning($"{LOG_PREFIX} File already exists at destination, skipping: {destAssetPath}");
                    continue;
                }

                // Copy FBX into Unity project
                File.Copy(sourcePath, destPath);
                AssetDatabase.Refresh();

                // Configure model importer for material/texture extraction
                var modelImporter = AssetImporter.GetAtPath(destAssetPath) as ModelImporter;
                if (modelImporter == null)
                {
                    Debug.LogWarning($"{LOG_PREFIX} Could not get ModelImporter for: {destAssetPath}");
                    continue;
                }

                // Extract textures into a dedicated folder
                string fbxNameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                string texturesFolder = $"{destinationFolder}/{fbxNameWithoutExt}_Textures";
                EnsureDirectoryExists(texturesFolder);
                modelImporter.ExtractTextures(texturesFolder);
                AssetDatabase.Refresh();

                // Extract materials by setting location to External
                string materialsFolder = $"{destinationFolder}/{fbxNameWithoutExt}_Materials";
                EnsureDirectoryExists(materialsFolder);
                modelImporter.materialLocation = ModelImporterMaterialLocation.External;
                AssetDatabase.ImportAsset(destAssetPath, ImportAssetOptions.ForceUpdate);
                AssetDatabase.Refresh();

                // Move extracted materials to the designated folder
                MoveExtractedMaterials(destinationFolder, fbxNameWithoutExt, materialsFolder);

                // Attempt to remap materials to URP/Lit shader
                RemapMaterialsToURP(materialsFolder);

                // Create FBXImportTask ScriptableObject
                FBXImportTask task = CreateImportTask(
                    destAssetPath, prefabOutputFolder, materialsFolder, texturesFolder);

                if (task != null)
                {
                    createdTasks.Add(task);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"{LOG_PREFIX} Import complete. Created {createdTasks.Count} task(s).");
            return createdTasks;
        }

        /// <summary>
        /// Processes a single FBXImportTask: instantiates the FBX, unpacks it
        /// completely, and saves prefabs based on user decisions.
        /// </summary>
        public static void ProcessTask(FBXImportTask task)
        {
            if (task == null)
            {
                Debug.LogWarning($"{LOG_PREFIX} Task is null.");
                return;
            }

            if (task.SourceFBX == null)
            {
                Debug.LogWarning($"{LOG_PREFIX} SourceFBX is null on task: {task.name}");
                return;
            }

            if (task.IsProcessed)
            {
                Debug.LogWarning($"{LOG_PREFIX} Task already processed: {task.name}");
                return;
            }

            EnsureDirectoryExists(task.PrefabOutputFolder);
            task.GeneratedPrefabPaths.Clear();

            if (task.KeepAsSinglePrefab)
            {
                ProcessAsSinglePrefab(task);
            }
            else
            {
                ProcessWithSplitDecisions(task);
            }

            task.IsProcessed = true;
            EditorUtility.SetDirty(task);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Ping the output folder so the user sees the results
            var outputFolder = AssetDatabase.LoadAssetAtPath<Object>(task.PrefabOutputFolder);
            if (outputFolder != null)
            {
                EditorGUIUtility.PingObject(outputFolder);
            }

            Debug.Log($"{LOG_PREFIX} Task processed: {task.name} — generated {task.GeneratedPrefabPaths.Count} prefab(s).");
        }

        /// <summary>
        /// Resets a processed task so it can be re-configured and processed again.
        /// </summary>
        public static void ResetTask(FBXImportTask task)
        {
            if (task == null)
            {
                Debug.LogWarning($"{LOG_PREFIX} Task is null.");
                return;
            }

            task.IsProcessed = false;
            task.GeneratedPrefabPaths.Clear();
            EditorUtility.SetDirty(task);
            AssetDatabase.SaveAssets();

            Debug.Log($"{LOG_PREFIX} Task reset: {task.name}");
        }

        /// <summary>
        /// Processes all unprocessed FBXImportTask assets in the project.
        /// </summary>
        public static void ProcessAllTasks()
        {
            string[] guids = AssetDatabase.FindAssets("t:FBXImportTask");
            int processedCount = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var task = AssetDatabase.LoadAssetAtPath<FBXImportTask>(path);

                if (task != null && !task.IsProcessed)
                {
                    ProcessTask(task);
                    processedCount++;
                }
            }

            Debug.Log($"{LOG_PREFIX} Batch processing complete. Processed {processedCount} task(s).");
        }

        /// <summary>
        /// Analyzes the FBX hierarchy and detects root objects that share
        /// the same mesh geometry (duplicates).
        /// </summary>
        public static void DetectDuplicates(FBXImportTask task)
        {
            if (task == null || task.SourceFBX == null)
                return;

            task.DuplicateGroups.Clear();

            // Build a mapping from mesh signature to child names
            var meshToChildren = new Dictionary<string, List<string>>();
            var meshInfoMap = new Dictionary<string, (string meshName, int vertexCount)>();

            for (int i = 0; i < task.SourceFBX.transform.childCount; i++)
            {
                Transform child = task.SourceFBX.transform.GetChild(i);
                string signature = GetMeshSignature(child.gameObject);

                if (string.IsNullOrEmpty(signature))
                    continue;

                if (!meshToChildren.ContainsKey(signature))
                {
                    meshToChildren[signature] = new List<string>();
                    var meshInfo = GetMeshInfo(child.gameObject);
                    meshInfoMap[signature] = meshInfo;
                }

                meshToChildren[signature].Add(child.name);
            }

            // Create DuplicateGroups for signatures with more than one member
            int groupIndex = 0;
            foreach (var kvp in meshToChildren)
            {
                if (kvp.Value.Count <= 1)
                    continue;

                var info = meshInfoMap[kvp.Key];
                string groupId = $"Group_{groupIndex}";

                var group = new DuplicateGroup
                {
                    GroupId = groupId,
                    SharedMeshName = info.meshName,
                    VertexCount = info.vertexCount,
                    MemberNames = new List<string>(kvp.Value)
                };

                task.DuplicateGroups.Add(group);

                // Tag each RootObjectEntry with its group
                foreach (var entry in task.RootObjects)
                {
                    if (kvp.Value.Contains(entry.Name))
                    {
                        entry.DuplicateGroupId = groupId;
                    }
                }

                groupIndex++;
            }

            EditorUtility.SetDirty(task);

            int totalDuplicates = task.DuplicateGroups.Sum(g => g.MemberNames.Count);
            Debug.Log(
                $"{LOG_PREFIX} Duplicate detection complete on '{task.name}': " +
                $"found {task.DuplicateGroups.Count} group(s) covering {totalDuplicates} object(s).");
        }

        /// <summary>
        /// Precision used when rounding bounds for geometry fingerprinting.
        /// </summary>
        private const float BOUNDS_ROUND_PRECISION = 100f;

        /// <summary>
        /// Repositions a GameObject so that the pivot (transform origin)
        /// sits at the bottom-center of the combined renderer bounds.
        /// If the root has no children but has a mesh directly, a wrapper
        /// parent is created and returned as the new root.
        /// </summary>
        /// <returns>The GameObject to save as a prefab (may be a new wrapper).</returns>
        private static GameObject RecenterPivotToBottom(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                Debug.LogWarning($"{LOG_PREFIX} No renderers found on '{root.name}', skipping pivot adjustment.");
                return root;
            }

            // Calculate combined world-space bounds of all renderers
            Bounds combinedBounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                combinedBounds.Encapsulate(renderers[i].bounds);
            }

            // The point we want as the new origin: bottom-center
            Vector3 bottomCenter = new Vector3(
                combinedBounds.center.x,
                combinedBounds.min.y,
                combinedBounds.center.z);

            // Offset to shift geometry so bottom-center maps to world origin
            Vector3 offset = root.transform.position - bottomCenter;

            if (root.transform.childCount > 0)
            {
                // Offset all direct children to compensate
                for (int i = 0; i < root.transform.childCount; i++)
                {
                    Transform child = root.transform.GetChild(i);
                    child.position += offset;
                }

                root.transform.position = Vector3.zero;
                return root;
            }

            // Root has no children but has a mesh directly on it.
            // Wrap it in a new parent so the pivot offset can be applied.
            string originalName = root.name;
            var wrapper = new GameObject(originalName);
            wrapper.transform.position = Vector3.zero;

            root.transform.SetParent(wrapper.transform, true);
            root.transform.localPosition += offset;

            return wrapper;
        }

        /// <summary>
        /// Generates a geometry-based fingerprint for a GameObject.
        /// Compares actual mesh data (vertex count, triangle counts, bounds, materials)
        /// instead of mesh asset names, so identical geometry exported as separate
        /// mesh assets (e.g., Cylinder.000, Cylinder.001) will still match.
        /// </summary>
        private static string GetMeshSignature(GameObject go)
        {
            var parts = new List<string>();
            CollectMeshSignatureParts(go, parts);

            if (parts.Count == 0)
                return null;

            parts.Sort();
            return string.Join("||", parts);
        }

        /// <summary>
        /// Recursively collects geometry fingerprint parts from a GameObject
        /// and all its descendants.
        /// </summary>
        private static void CollectMeshSignatureParts(GameObject go, List<string> parts)
        {
            var meshFilter = go.GetComponent<MeshFilter>();
            var renderer = go.GetComponent<MeshRenderer>();

            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                Mesh mesh = meshFilter.sharedMesh;

                // Build signature from geometry data, NOT mesh name
                string sig = $"v:{mesh.vertexCount}_sm:{mesh.subMeshCount}";

                // Add per-submesh triangle counts for more precision
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    sig += $"_t{s}:{mesh.GetSubMesh(s).indexCount}";
                }

                // Add rounded bounds to distinguish meshes with same vertex/tri
                // count but different shapes
                Vector3 size = mesh.bounds.size;
                float rx = Mathf.Round(size.x * BOUNDS_ROUND_PRECISION) / BOUNDS_ROUND_PRECISION;
                float ry = Mathf.Round(size.y * BOUNDS_ROUND_PRECISION) / BOUNDS_ROUND_PRECISION;
                float rz = Mathf.Round(size.z * BOUNDS_ROUND_PRECISION) / BOUNDS_ROUND_PRECISION;
                sig += $"_b:{rx}x{ry}x{rz}";

                // Include material names (these reference extracted materials, so
                // duplicates from the same FBX share the same material assets)
                if (renderer != null && renderer.sharedMaterials != null)
                {
                    string matNames = string.Join(
                        "|",
                        renderer.sharedMaterials
                            .Where(m => m != null)
                            .Select(m => m.name));
                    sig += $"_mats:{matNames}";
                }

                parts.Add(sig);
            }

            // Recurse into children so compound objects get a full fingerprint
            for (int i = 0; i < go.transform.childCount; i++)
            {
                CollectMeshSignatureParts(go.transform.GetChild(i).gameObject, parts);
            }
        }

        /// <summary>
        /// Gets display info about the mesh on a GameObject, searching
        /// children if the root has no MeshFilter.
        /// </summary>
        private static (string meshName, int vertexCount) GetMeshInfo(GameObject go)
        {
            var meshFilter = go.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                return (meshFilter.sharedMesh.name, meshFilter.sharedMesh.vertexCount);
            }

            // Search children for the first mesh
            var childFilter = go.GetComponentInChildren<MeshFilter>();
            if (childFilter != null && childFilter.sharedMesh != null)
            {
                return (childFilter.sharedMesh.name, childFilter.sharedMesh.vertexCount);
            }

            return ("No Mesh", 0);
        }

        /// <summary>
        /// Saves the entire FBX as a single unpacked prefab.
        /// </summary>
        private static void ProcessAsSinglePrefab(FBXImportTask task)
        {
            string outputPath = $"{task.PrefabOutputFolder}/{task.SourceFBX.name}.prefab";

            var instance = PrefabUtility.InstantiatePrefab(task.SourceFBX) as GameObject;
            if (instance == null)
            {
                Debug.LogWarning($"{LOG_PREFIX} Failed to instantiate FBX: {task.SourceFBX.name}");
                return;
            }

            PrefabUtility.UnpackPrefabInstance(
                instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            var pivotedInstance = RecenterPivotToBottom(instance);

            PrefabUtility.SaveAsPrefabAsset(pivotedInstance, outputPath);
            Object.DestroyImmediate(pivotedInstance);

            task.GeneratedPrefabPaths.Add(outputPath);
            Debug.Log($"{LOG_PREFIX} Saved single prefab: {outputPath}");
        }

        /// <summary>
        /// Processes the FBX by splitting selected roots into individual prefabs
        /// and grouping remaining roots into a combined prefab.
        /// Handles duplicates by creating one prefab per unique mesh group.
        /// </summary>
        private static void ProcessWithSplitDecisions(FBXImportTask task)
        {
            var splitEntries = new List<RootObjectEntry>();
            var groupedEntries = new List<RootObjectEntry>();

            foreach (var entry in task.RootObjects)
            {
                if (entry.SplitAsIndividualPrefab)
                    splitEntries.Add(entry);
                else
                    groupedEntries.Add(entry);
            }

            // Track which duplicate groups have already been saved as prefabs.
            // Key: DuplicateGroupId, Value: the output prefab path
            var savedDuplicateGroups = new Dictionary<string, string>();

            // Process individual split entries, deduplicating by group
            foreach (var entry in splitEntries)
            {
                bool isDuplicate = !string.IsNullOrEmpty(entry.DuplicateGroupId);

                if (isDuplicate && savedDuplicateGroups.ContainsKey(entry.DuplicateGroupId))
                {
                    // Already saved a prefab for this duplicate group — skip
                    string existingPath = savedDuplicateGroups[entry.DuplicateGroupId];
                    Debug.Log(
                        $"{LOG_PREFIX} Skipping duplicate '{entry.Name}' — " +
                        $"prefab already exists at: {existingPath}");
                    continue;
                }

                string prefabPath = SaveChildAsIndividualPrefab(task, entry.Name);

                if (isDuplicate && !string.IsNullOrEmpty(prefabPath))
                {
                    savedDuplicateGroups[entry.DuplicateGroupId] = prefabPath;
                }
            }

            // Process grouped entries as a combined prefab
            if (groupedEntries.Count > 0)
            {
                SaveGroupedPrefab(task, groupedEntries);
            }
        }

        /// <summary>
        /// Extracts a single child from the FBX and saves it as its own prefab.
        /// Returns the output path if successful, null otherwise.
        /// </summary>
        private static string SaveChildAsIndividualPrefab(FBXImportTask task, string childName)
        {
            var instance = PrefabUtility.InstantiatePrefab(task.SourceFBX) as GameObject;
            if (instance == null)
            {
                Debug.LogWarning($"{LOG_PREFIX} Failed to instantiate FBX for child extraction: {childName}");
                return null;
            }

            PrefabUtility.UnpackPrefabInstance(
                instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            Transform targetChild = null;
            var childrenToDestroy = new List<GameObject>();

            for (int i = 0; i < instance.transform.childCount; i++)
            {
                Transform child = instance.transform.GetChild(i);
                if (child.name == childName && targetChild == null)
                {
                    targetChild = child;
                }
                else
                {
                    childrenToDestroy.Add(child.gameObject);
                }
            }

            if (targetChild == null)
            {
                Debug.LogWarning($"{LOG_PREFIX} Child '{childName}' not found in FBX: {task.SourceFBX.name}");
                Object.DestroyImmediate(instance);
                return null;
            }

            // Destroy unwanted children
            foreach (var go in childrenToDestroy)
            {
                Object.DestroyImmediate(go);
            }

            // Reparent the target child to be the root
            targetChild.SetParent(null);
            Object.DestroyImmediate(instance);

            string outputPath = $"{task.PrefabOutputFolder}/{childName}.prefab";
            var pivotedChild = RecenterPivotToBottom(targetChild.gameObject);
            PrefabUtility.SaveAsPrefabAsset(pivotedChild, outputPath);
            Object.DestroyImmediate(pivotedChild);

            task.GeneratedPrefabPaths.Add(outputPath);
            Debug.Log($"{LOG_PREFIX} Saved individual prefab: {outputPath}");
            return outputPath;
        }

        /// <summary>
        /// Saves the grouped (non-split) children as a single combined prefab.
        /// </summary>
        private static void SaveGroupedPrefab(FBXImportTask task, List<RootObjectEntry> groupedEntries)
        {
            var instance = PrefabUtility.InstantiatePrefab(task.SourceFBX) as GameObject;
            if (instance == null)
            {
                Debug.LogWarning($"{LOG_PREFIX} Failed to instantiate FBX for grouped prefab.");
                return;
            }

            PrefabUtility.UnpackPrefabInstance(
                instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            var groupedNames = new HashSet<string>(groupedEntries.Select(e => e.Name));
            var childrenToDestroy = new List<GameObject>();

            for (int i = 0; i < instance.transform.childCount; i++)
            {
                Transform child = instance.transform.GetChild(i);
                if (!groupedNames.Contains(child.name))
                {
                    childrenToDestroy.Add(child.gameObject);
                }
            }

            foreach (var go in childrenToDestroy)
            {
                Object.DestroyImmediate(go);
            }

            string outputPath = $"{task.PrefabOutputFolder}/{task.SourceFBX.name}_Grouped.prefab";
            var pivotedGrouped = RecenterPivotToBottom(instance);
            PrefabUtility.SaveAsPrefabAsset(pivotedGrouped, outputPath);
            Object.DestroyImmediate(pivotedGrouped);

            task.GeneratedPrefabPaths.Add(outputPath);
            Debug.Log($"{LOG_PREFIX} Saved grouped prefab: {outputPath}");
        }

        /// <summary>
        /// Creates an FBXImportTask ScriptableObject for the given imported FBX.
        /// </summary>
        private static FBXImportTask CreateImportTask(
            string fbxAssetPath,
            string prefabOutputFolder,
            string materialsFolder,
            string texturesFolder)
        {
            var fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(fbxAssetPath);
            if (fbxAsset == null)
            {
                Debug.LogWarning($"{LOG_PREFIX} Could not load FBX asset at: {fbxAssetPath}");
                return null;
            }

            var task = ScriptableObject.CreateInstance<FBXImportTask>();
            task.SourceFBX = fbxAsset;
            task.PrefabOutputFolder = prefabOutputFolder;
            task.MaterialsFolder = materialsFolder;
            task.TexturesFolder = texturesFolder;
            task.KeepAsSinglePrefab = true;
            task.IsProcessed = false;

            // Populate root objects from the FBX hierarchy
            task.RootObjects = new List<RootObjectEntry>();
            for (int i = 0; i < fbxAsset.transform.childCount; i++)
            {
                Transform child = fbxAsset.transform.GetChild(i);
                task.RootObjects.Add(new RootObjectEntry
                {
                    Name = child.name,
                    SplitAsIndividualPrefab = false,
                    DuplicateGroupId = string.Empty
                });
            }

            // Run duplicate detection automatically
            DetectDuplicates(task);

            string fbxName = Path.GetFileNameWithoutExtension(fbxAssetPath);
            string taskFolder = Path.GetDirectoryName(fbxAssetPath)?.Replace("\\", "/");
            string taskPath = $"{taskFolder}/{fbxName}_ImportTask.asset";

            AssetDatabase.CreateAsset(task, taskPath);

            Debug.Log($"{LOG_PREFIX} Created import task: {taskPath} with {task.RootObjects.Count} root object(s).");
            return task;
        }

        /// <summary>
        /// Moves extracted materials from the default Materials subfolder
        /// to the designated materials folder.
        /// </summary>
        private static void MoveExtractedMaterials(
            string destinationFolder,
            string fbxNameWithoutExt,
            string targetMaterialsFolder)
        {
            // Unity creates a "Materials" subfolder next to the FBX by default
            string defaultMaterialsFolder = $"{destinationFolder}/Materials";

            if (!AssetDatabase.IsValidFolder(defaultMaterialsFolder))
                return;

            string[] materialGuids = AssetDatabase.FindAssets("t:Material", new[] { defaultMaterialsFolder });

            foreach (string guid in materialGuids)
            {
                string materialPath = AssetDatabase.GUIDToAssetPath(guid);
                string materialFileName = Path.GetFileName(materialPath);
                string targetPath = $"{targetMaterialsFolder}/{materialFileName}";

                if (AssetDatabase.LoadAssetAtPath<Object>(targetPath) != null)
                {
                    Debug.LogWarning(
                        $"{LOG_PREFIX} Material already exists at target, skipping move: {targetPath}");
                    continue;
                }

                string result = AssetDatabase.MoveAsset(materialPath, targetPath);
                if (!string.IsNullOrEmpty(result))
                {
                    Debug.LogWarning($"{LOG_PREFIX} Failed to move material: {result}");
                }
            }

            // Clean up empty default Materials folder
            string[] remaining = AssetDatabase.FindAssets("", new[] { defaultMaterialsFolder });
            if (remaining.Length == 0)
            {
                AssetDatabase.DeleteAsset(defaultMaterialsFolder);
            }

            AssetDatabase.Refresh();
        }

        /// <summary>
        /// Attempts to remap materials in the given folder to URP/Lit shader.
        /// </summary>
        private static void RemapMaterialsToURP(string materialsFolder)
        {
            Shader urpLitShader = Shader.Find(URP_LIT_SHADER_NAME);
            if (urpLitShader == null)
            {
                Debug.LogWarning($"{LOG_PREFIX} URP Lit shader not found. Skipping material remapping.");
                return;
            }

            string[] materialGuids = AssetDatabase.FindAssets("t:Material", new[] { materialsFolder });

            foreach (string guid in materialGuids)
            {
                string materialPath = AssetDatabase.GUIDToAssetPath(guid);
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);

                if (material != null && material.shader.name != URP_LIT_SHADER_NAME)
                {
                    material.shader = urpLitShader;
                    EditorUtility.SetDirty(material);
                }
            }

            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Ensures a directory exists at the given asset path, creating it if necessary.
        /// </summary>
        private static void EnsureDirectoryExists(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath))
                return;

            if (AssetDatabase.IsValidFolder(folderPath))
                return;

            // Build folder hierarchy using AssetDatabase.CreateFolder
            string[] parts = folderPath.Replace("\\", "/").Split('/');
            string currentPath = parts[0]; // "Assets"

            for (int i = 1; i < parts.Length; i++)
            {
                string nextPath = $"{currentPath}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(nextPath))
                {
                    AssetDatabase.CreateFolder(currentPath, parts[i]);
                }
                currentPath = nextPath;
            }
        }
    }
}
