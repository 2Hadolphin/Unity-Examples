#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System;
using UnityEngine.Assertions;
using System.Linq;
using Object = UnityEngine.Object;
using System.IO;
using UnityEditor.Build.Content;
using System.Text.RegularExpressions;
using UnityEngine.Profiling;
using System.Reflection;

namespace Return.Editors
{
    /// <summary>
    /// Editor util bundle. **GUID  **Asset-Crate-Save-Duplicate-Cut&Paste-Replace-Extract
    /// </summary>
    public static class EditorAssetsUtility
    {
        public const string cst_rootPath = "Assets";
        public const string cst_ext_prefab = ".prefab";

        const string cst_shortcut_createAsset = MenuUtil.ShortCut_Shift + "_c";

        #region Dirty

        /// <summary>
        /// Equal to <see cref="EditorUtility.SetDirty"/> but lazier :)
        /// </summary>
        public static void Dirty(this Object obj)
        {
            if (EditorUtility.IsPersistent(obj))
                EditorUtility.SetDirty(obj);
        }

        /// <summary>
        /// Reimport asset.
        /// </summary>
        public static void ReImport(this Object obj)
        {
            AssetDatabase.ImportAsset(obj.GetAssetPath(), ImportAssetOptions.Default);
            AssetDatabase.Refresh();
        }

        #endregion


        #region Edit

        /// <summary>
        /// Open asset.
        /// </summary>
        /// <param name="obj"></param>
        public static void OpenAsset(this Object obj)
        {
            if(obj==null)
            {
                Debug.LogException(new NullReferenceException(), obj);
                return;
            }

            if (AssetDatabase.CanOpenAssetInEditor(obj.GetInstanceID()))
                AssetDatabase.OpenAsset(obj.GetInstanceID());
            else if (AssetDatabase.CanOpenForEdit(obj))
                AssetDatabase.OpenAsset(obj);
            else
                Debug.LogError($"Failure to open asset with {obj}", obj);
        }

        /// <summary>
        /// Rename obj to new ID;
        /// </summary>
        public static void Rename(this Object obj, string newID,bool refreshAsset=true)
        {
            if (obj.name == newID)
                return;

            try
            {
                string assetPath = AssetDatabase.GetAssetPath(obj);
                Debug.Log($"{obj}_{assetPath}");

                AssetDatabase.RenameAsset(assetPath, newID);

                if (refreshAsset)
                    AssetDatabase.SaveAssets();
            }
            catch (Exception e)
            {
                Debug.LogError($"Failure to rename file [{obj}] with new name [{newID}]");
                Debug.LogException(e);
            }
        }

        #endregion

        #region GUID

        public static readonly Regex Regex_GUID = new("guid:\\s?([a-fA-F0-9]+)");

        public static string GetGUID(int instanceID, AssetPathToGUIDOptions options = AssetPathToGUIDOptions.OnlyExistingAssets)
        {
            string guid;

            if (ObjectIdentifier.TryGetObjectIdentifier(instanceID, out var id))
            {
                guid = id.guid.ToString();
                Debug.Log($"Get guid from : {id.fileType}.");
            }
            else
            {
                Debug.LogError("Failure to get asset identifier.");
                var path = AssetDatabase.GetAssetPath(instanceID);
                guid = AssetDatabase.AssetPathToGUID(path, options);
            }

            return guid;
        }

        /// <summary>
        /// Get asset guid.
        /// </summary>
        public static string GetGUID<T>(this T asset, AssetPathToGUIDOptions options = AssetPathToGUIDOptions.OnlyExistingAssets) where T : Object
        {
            string guid;

            if (ObjectIdentifier.TryGetObjectIdentifier(asset, out var id))
            {
                guid = id.guid.ToString();
                //Debug.Log($"Get guid from : {id.fileType}.");
            }
            else
            {
                Debug.LogError("Failure to get asset identifier.");
                var path = AssetDatabase.GetAssetPath(asset);
                guid = AssetDatabase.AssetPathToGUID(path, options);
            }

            return guid;
        }

        /// <summary>
        /// Get asset guid.
        /// </summary>
        public static GUID AsGUID(this Object asset)
        {
            GUID guid;

            if (ObjectIdentifier.TryGetObjectIdentifier(asset, out var id))
            {
                guid = id.guid;

                Debug.Log($"{asset} [{id.fileType}]-[{id.guid}] [{id.localIdentifierInFile}] [{id.filePath}]",asset);

                switch (id.fileType)
                {
                    case FileType.NonAssetType:

                        break;
                    case FileType.DeprecatedCachedAssetType:
                        throw new InvalidOperationException($"{asset}-[{id.guid}] [{id.localIdentifierInFile}]");
                    case FileType.SerializedAssetType:
                        break;
                    case FileType.MetaAssetType:
                        // mono instance
                        break;
                }

                //Debug.Log($"Get guid from : {id.fileType}.");
            }
            else
            {
                Debug.LogError($"Failure to get asset identifier with [{asset}].");
                var path = AssetDatabase.GetAssetPath(asset);
                GUID.TryParse(AssetDatabase.AssetPathToGUID(path), out guid);
            }

            return guid;
        }

        /// <summary>
        /// File extension for **Prefab    **Scene **ScriptableObject.
        /// </summary>
        public static readonly string[] kSwapGUIDFileExtensions = new string[]
        {
            "*.prefab",
            "*.unity",
            "*.asset",
        };

        /// <summary>
        /// All extensions of editor assets which contains guid.
        /// </summary>
        public static readonly string[] kDefaultGUIDFileExtensions = new string[]
        {
            "*.meta",
            "*.mat",
            "*.anim",
            "*.prefab",
            "*.unity",
            "*.asset",
            "*.guiskin",
            "*.fontsettings",
            "*.controller",
        };

 

        /// <summary>
        /// Log asset content guids(not index).
        /// </summary>
        [MenuItem("Assets/LogGUIDs", priority = 1000)]
        public static void LogGUIDs()
        {
            var asset = Selection.activeObject;

            if (asset == null)
                return;

            var filePath = EditorPathUtility.GetIOPath(asset);

            string contents = File.ReadAllText(filePath);

            IEnumerable<string> guids = GetGuids(contents);

            Debug.Log(string.Join("\n", guids));

            var paths = guids.Select(x => AssetDatabase.GUIDToAssetPath(x));



            Debug.Log(string.Join("\n", paths));



            var objs = paths.Where(x => !string.IsNullOrEmpty(x)).Select(x => AssetDatabase.LoadAssetAtPath<Object>(x).name);

            Debug.Log(string.Join("\n", objs));
        }

        /// <summary>
        /// Get guid in asset context.(.meta)
        /// </summary>
        public static IEnumerable<string> GetGuids(string text)
        {
            const string guidStart = "guid: ";
            const int guidLength = 32;
            int textLength = text.Length;
            int guidStartLength = guidStart.Length;
            List<string> guids = new();

            int index = 0;
            while (index + guidStartLength + guidLength < textLength)
            {
                index = text.IndexOf(guidStart, index, StringComparison.Ordinal);
                if (index == -1)
                    break;

                index += guidStartLength;
                string guid = text.Substring(index, guidLength);
                index += guidLength;

                if (IsGuid(guid))
                {
                    guids.Add(guid);
                }
            }

            return guids;
        }

        public static bool IsGuid(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (
                    !((c >= '0' && c <= '9') ||
                      (c >= 'a' && c <= 'z'))
                    )
                    return false;
            }

            return true;
        }

        /// <summary>
        /// **Alert!!! This operation will break all reference inside project which refer to old GUID.
        /// </summary>
        public static void ChangeFolderGUID(params KeyValuePair<string,string>[] path_GUID)
        {
            using var refreshLock = LockRefresh();

            try
            {
                //AssetDatabase.StartAssetEditing();

                foreach (var pair in path_GUID)
                {
                    ChangeAssetMeta(EditorPathUtility.GetMetaPath(pair.Key), pair.Value,Regex_GUID,false,true);
                }
            }
            catch (Exception e)
            {

                Debug.LogException(e);
            }
            finally
            {
                //AssetDatabase.StopAssetEditing();
                //AssetDatabase.Refresh();
            }

        }

        /// <summary>
        /// Set asset guid.
        /// </summary>
        public static void ChangeGUID(string assetMetaPath,string newGUID)
        {
            ChangeAssetMeta(assetMetaPath,newGUID,Regex_GUID,true,true);
        }

        /// <summary>
        /// Replace the meta string with require format.
        /// </summary>
        /// <param name="assetPath">Target path point to asset source or meta file.</param>
        /// <param name="newMeta">New meta string to replace</param>
        /// <param name="format">Regex rule to find the string.</param>
        /// <param name="refreshEditor">Refresh assetdatabase after file editing.</param>
        /// <param name="singleResult">Only change content with first match. **folder guid</param>
        public static void ChangeAssetMeta(string assetPath,string newMeta,Regex format,bool refreshEditor = true, bool singleResult=false)
        {
            using var refreshLock = LockRefresh();


            bool dirty = false;
            try
            {
                if(!File.Exists(assetPath))
                    assetPath = EditorPathUtility.GetIOPath(assetPath);

                var file = assetPath;
                var contents = File.ReadAllLines(file);
                
                for (int i = 0; i < contents.Length; i++)
                {
                    var line = contents[i];
                    var guidMatch = format.Match(line);

                    if (guidMatch.Success)
                    {
                        var lastMeta = guidMatch.Groups[1].Value;
                        contents[i] = line.Replace(lastMeta, newMeta);

                        dirty = true;

                        if(singleResult)
                            break;
                    }
                }

                if(dirty)
                    File.WriteAllLines(file, contents);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                //if (dirty && refreshEditor)
                //    AssetDatabase.Refresh();
            }
        }


        /// <summary>
        /// Remap guids to prefabs and scene targets.
        /// </summary>
        /// <param name="remapGUID">Old GUID - Old FileID - new GUID & FileID</param>
        static public void ReplaceGUIDs(Dictionary<string, Dictionary<string, ObjectIdentifier>> remapGUID, string folderPath)
        {
            if(!Directory.Exists(folderPath))
                folderPath = EditorPathUtility.GetIOPath(folderPath);

            List<string> filesPaths = new();

            foreach (string extension in EditorAssetsUtility.kSwapGUIDFileExtensions)
            {
                filesPaths.AddRange(
                    Directory.GetFiles(folderPath, extension, SearchOption.AllDirectories)
                    );
            }

            ReplaceGUIDs(remapGUID, filesPaths.ToArray());
        }

        /// <summary>
        /// Remap guids.
        /// </summary>
        /// <param name="remapGUID">Old GUID - Old FileID - new GUID & FileID</param>
        static public void ReplaceGUIDs(Dictionary<string, Dictionary<string, ObjectIdentifier>> remapGUID, params string[] filesPaths)
        {
            using var refreshLock = LockRefresh();


            Debug.Log($"GUID files count : {filesPaths.Length} \n {string.Join("\n", filesPaths)}");


            //Debug.Log($"Load {data}");

            var guidRegx = Regex_GUID;
            var fileRegx = new Regex("fileID:\\s?([0-9]+)");

            int modify = 0;

            try
            {
                //AssetDatabase.StartAssetEditing();

                var fileNum = filesPaths.Length;

                const string title = "Replace GUID.";

                for (int index = 0; index < fileNum; index++)
                {
                    try
                    {
                        var file = filesPaths[index];

                        var contents = File.ReadAllLines(file);

                        if (EditorUtility.DisplayCancelableProgressBar(title, $"Proccessing changing guid..{Path.GetFileName(file)}", (float)index / fileNum))
                        {
                            Debug.LogWarning("OldGuid swap has not finish, some reference might be broken.");
                            break;
                        }


                        bool edit = false;

                        var length = contents.Length;

                        for (int i = 0; i < length; i++)
                        {
                            var context = contents[i];

                            var guidMatch = guidRegx.Match(context);

                            if (!guidMatch.Success)
                                continue;

                            var fileMatch = fileRegx.Match(context);

                            if (!fileMatch.Success)
                                continue;

                            var oldGUID = guidMatch.Groups[1].Value;
                            var oldFileID = fileMatch.Groups[1].Value;


                            // valid guid qualify
                            if (!remapGUID.TryGetValue(oldGUID, out var dic))
                                continue;

                            // valid fileID qualify
                            if (!dic.TryGetValue(oldFileID, out var newID))
                                continue;

                            // replace guid and fileID
                            {
                                contents[i] = context.Replace(oldFileID, newID.localIdentifierInFile.ToString()).Replace(oldGUID, newID.guid.ToString());
                                edit = true;
                                modify++;
                                Debug.Log($"Match GUID FileID : {oldFileID} GUID : {oldGUID}");
                            }
                        }

                        // update file if context changed
                        if (edit)
                            File.WriteAllLines(file, contents);

                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                //AssetDatabase.StopAssetEditing();
                //EditorUtility.ClearProgressBar();
            }


            Debug.Log($"Edit {modify} guid references.");
        }

        [Obsolete]
        class ScriptMapping 
        {
            public ScriptMapping(string meta,string guid)
            {
                Meta = meta;
                OldGuid = guid;
            }

            public string Meta;
            public string OldGuid;
            public string NewGuid;
        }


        public static void RemapScriptGuid()
        {
            using var refreshLock = LockRefresh();

            var workingPath = Path.GetFullPath(Application.dataPath + "/App");

            var prefix = Path.GetFileNameWithoutExtension(EditorUtility.SaveFilePanel("Type prefix", string.Empty, "00", "hex")).ToLower();
            var prefixRegex = new Regex("[a-fA-F0-9]{1,15}");
            if (string.IsNullOrEmpty(prefix))
            {
                return;
            }
            else if (!prefixRegex.Match(prefix).Success)
            {
                EditorUtility.DisplayDialog("Remapping GUID", string.Format("Prefix '{0}' is not valid.", prefix), "OK");
                return;
            }


            // Get script meta
            var mappings = new List<ScriptMapping>();
            var scriptMetas = Directory.GetFiles(workingPath, "*.cs.meta", SearchOption.AllDirectories);
            var guidRegx = new Regex("guid:\\s?([a-fA-F0-9]+)");
            foreach (var scriptMeta in scriptMetas)
            {
                var metaContent = File.ReadAllText(scriptMeta);
                var match = guidRegx.Match(metaContent);
                if (match.Success)
                {
                    var guid = match.Groups[1].Value;
                    var mapping = new ScriptMapping(scriptMeta, guid);
                    mappings.Add(mapping);
                }
            }

            // Assign new Guids
            foreach (var mapping in mappings)
            {
                var newGuid = prefix + mapping.OldGuid.Substring(prefix.Length);
                if (newGuid == mapping.OldGuid)
                {
                    continue;
                }
                // CheckAdd new Guids is exist?
                if (!string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(newGuid)))
                {
                    while (true)
                    {
                        // Generate new
                        newGuid = System.Guid.NewGuid().ToString("N");
                        newGuid = prefix + newGuid.Substring(prefix.Length);
                        if (string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(newGuid)))
                        {
                            break;
                        }
                    }
                }
                mapping.NewGuid = newGuid;
            }

            // Find resources amd replace GUIDs
            // *.assets ScriptableObject
            // *unity Scene
            // *.prefab Model
            // *.controller Animator
            // *.cs.meta Scripts
            //AssetDatabase.StartAssetEditing();
            var assets = Directory.GetFiles(Application.dataPath, "*.*", SearchOption.AllDirectories).Where(s => s.EndsWith(".assets") || s.EndsWith(".unity") || s.EndsWith(".prefab") || s.EndsWith(".controller") || s.EndsWith(".cs.meta"));
            var assetsCount = assets.Count();
            var assetsIndex = 0;
            foreach (var asset in assets)
            {
                EditorUtility.DisplayProgressBar("Remapping", string.Format("Current: {0}", Path.GetFileName(asset)), Mathf.InverseLerp(0, assetsCount, assetsIndex));
                var content = File.ReadAllText(asset);
                var replaced = false;
                foreach (var mapping in mappings)
                {
                    if (mapping.NewGuid == null)
                    {
                        continue;
                    }
                    var oldGuid = string.Format("guid: {0}", mapping.OldGuid);
                    var newGuid = string.Format("guid: {0}", mapping.NewGuid);
                    if (oldGuid.IndexOf(oldGuid) >= 0)
                    {
                        content = content.Replace(oldGuid, newGuid);
                        replaced = true;
                    }
                }
                if (replaced)
                {
                    File.WriteAllText(asset, content);
                }
                assetsIndex += 1;
            }
            EditorUtility.ClearProgressBar();
            //AssetDatabase.StopAssetEditing();
            //AssetDatabase.Refresh(ImportAssetOptions.Default);
        }

        #endregion

        #region Valid

        /// <summary>
        /// Whether asset is persistent archive in disk. **equal to <see cref="EditorUtility.IsPersistent"/>.
        /// </summary>
        public static bool IsPersistentAsset<T>(this T asset) where T : Object
        {
            // instance id will be negitive by generate but still keep this value after archive and before editor refresh.
            //var id = asset.GetInstanceID();
            return EditorUtility.IsPersistent(asset);
        }

        /// <summary>
        /// Whether asset exist at path, return true when target not null.
        /// </summary>
        public static bool IsAssetExist<T>(string assetPath) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            return asset != null;
        }

        /// <summary>
        /// Whether asset exist at path, return true when target not null.
        /// </summary>
        public static bool IsAssetExist<T>(string assetPath,out T asset) where T : Object
        {
            asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            return asset != null;
        }

        /// <summary>
        /// Whether asset path inside resource folder?
        /// </summary>
        public static bool IsResource(string assetPath)
        {
            return assetPath.Contains("Resources");
        }

        /// <summary>
        /// Whether asset path inside resource folder?
        /// </summary>
        public static bool IsResource(this Object asset)
        {
            var path = AssetDatabase.GetAssetPath(asset);
            
            return EditorAssetsUtility.IsResource(path);
        }

        /// <summary>
        /// Whether asset path inside resource folder
        /// </summary>
        /// <param name="resourcePath">Resource path(start with /Resources).</param>
        /// <param name="index">Subasset index.</param>
        public static bool ValidResource(this Object asset,out string resourcePath,out int index)
        {
            index = -1;
            resourcePath = null;

            var path = AssetDatabase.GetAssetPath(asset);

            if (!IsResource(path))
                return false;

            resourcePath= path.Remove(0, path.IndexOf("Resources") + 1);

            Debug.Log($"Resources path : {resourcePath}.");

            var assets = Resources.LoadAll<Object>(resourcePath);

            var length = assets.Length;

            for (int i = 0; i < length; i++)
            {
                if (assets[i] == asset)
                {
                    index = i;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Valid selection object script reference.
        /// </summary>
        [MenuItem(MenuUtil.Asset+"Valid")]
        static void ValidMissingScript()
        {
            if(Selection.activeObject is GameObject go)
            {
                if (!ValidMissingScript(go))
                    EditorGUIUtility.PingObject(go);
            }
        }

        /// <summary>
        /// Valid object script reference.
        /// </summary>
        public static bool ValidMissingScript(this GameObject go)
        {
            var comps = go.GetComponentsInChildren<MonoBehaviour>(true);

            foreach (var comp in comps)
            {
                var missingInfos = SerializationUtility.GetManagedReferencesWithMissingTypes(comp);
                foreach (var info in missingInfos)
                {
                    Debug.Log(info.className);
                }
            }
    
            return PrefabUtility.HasManagedReferencesWithMissingTypes(go);
        }

        /// <summary>
        /// Whether object is prefab but not a fbx asset.
        /// </summary>
        public static bool IsPrefab(this GameObject go)
        {
           return EditorPathUtility.GetExtension(go).Equals(cst_ext_prefab);
        }

        #endregion

        #region Log

        /// <summary>
        /// Debug log selection.
        /// </summary>
        [MenuItem("Assets/LogSelection", priority = 1000)]
        public static void LogSelection()
        {
            var log = LogAsset(Selection.activeObject);

            log = $"InstanceID : {Selection.activeInstanceID}\n{log}";

            Debug.Log(log);
        }

        public static void LogTarget(Object obj)
        {
            var log = LogAsset(obj);

            log = $"InstanceID : {obj.GetInstanceID()}\n{log}";

            Debug.Log(log);
        }

        /// <summary>
        /// Log asset infomation.
        /// </summary>
        public static string LogAsset(this Object obj)
        {
            var sb = new System.Text.StringBuilder(500);

            if (obj != null)
            {
                string guid;
                
                if (ObjectIdentifier.TryGetObjectIdentifier(obj, out var id))
                {
                    guid = id.guid.ToString();
                    
                }
                else
                {
                    Debug.LogError("Failure to get asset identifier.");
                    guid = obj.GetGUID();
                }

                sb.AppendLine($"Name : {obj.name}");
                sb.AppendLine($"GUID : {guid}");
                sb.AppendLine($"FilePath : {obj.GetAssetPath()}");
                sb.AppendLine($"AssetType : {id.fileType}");
                sb.AppendLine($"Index : {id.localIdentifierInFile}");
                sb.AppendLine($"Type : {obj.GetType()}");
                sb.AppendLine($"MainAsset : {AssetDatabase.IsMainAsset(obj)}");
                sb.AppendLine($"NativeAsset : {AssetDatabase.IsNativeAsset(obj)}");
                sb.AppendLine($"ForeignAsset : {AssetDatabase.IsForeignAsset(obj)}");
                sb.AppendLine($"CanOpen : {AssetDatabase.IsOpenForEdit(obj)}");
                sb.AppendLine($"Extension : {EditorPathUtility.GetExtension(obj)}");
   

                if (AssetDatabase.IsMainAsset(obj))
                {
                    sb.AppendLine($"Memory Size : {obj.LogAssetSize()} bytes");
                }

                // audio
                if (obj is AudioClip audio)
                {
                    sb.AppendLine($"Length : {audio.length} secs.");
                }

                // mono
                if (obj is MonoScript mono)
                {
                    sb.AppendLine($"MonoType : {mono.GetClass()}");
                }

                if(obj is AnimationClip clip)
                {
                    sb.AppendLine($"Clip length : {clip.length}");
                    sb.AppendLine($"Clip frameRate : {clip.frameRate}");
                    sb.AppendLine($"Clip frame count : {clip.length* clip.frameRate}");
                    sb.AppendLine($"Max frame time : {clip.GetEditorCurves().Max(x=>x.keys.Max(key=>key.time))}");
                    sb.AppendLine($"Clip binding count : {clip.GetEditorBindings().Length}");

                }

            }

            return sb.ToString();
        }

        /// <summary>
        /// Return memory size.
        /// </summary>
        public static long LogAssetSize(this Object obj)
        {
            long memorySize = Profiler.GetRuntimeMemorySizeLong(obj);
            //Debug.Log("Object Memory Size: " + memorySize + " bytes");
            return memorySize;
        }
 
        /// <summary>
        /// Get prefab icon texture.
        /// </summary>
        public static Texture2D GetPrefabPreview(this GameObject go,int width)
        {
            var editor = Editor.CreateEditor(go);
            Texture2D tex = editor.RenderStaticPreview(go.GetAssetPath(), null, width, width);
            EditorWindow.DestroyImmediate(editor);
            return tex;
        }

        #endregion

        #region Search

        /// <summary>
        /// Try to get asset at path.
        /// </summary>
        public static bool TryGetAsset<T>(string path,out T asset) where T : Object
        {
            asset = AssetDatabase.LoadAssetAtPath<T>(path);
            return asset != null;
        }

        /// <summary>
        /// Try to get asset at path.
        /// </summary>
        public static bool TryGetAsset<T>(out T asset) where T :Object
        {
            asset = null;

            var paths=AssetDatabase.FindAssets($"t:{typeof(T).Name}");

            //Debug.Log($"Cache {paths.Length} paths of {typeof(T).Name} via t:{typeof(T).Name}");

            foreach (var path in paths)
            {
                if (TryGetAsset(AssetDatabase.GUIDToAssetPath(path), out asset))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Get asset from guid.
        /// </summary>
        public static Object GetAsset(string guid)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);

            if (string.IsNullOrEmpty(path))
                return null;
            else
                return AssetDatabase.LoadAssetAtPath(path, typeof(Object));
        }

        /// <summary>
        /// Find asset which is type of <typeparamref name="T"/> in the project. **equal to <see cref="AssetDatabase.FindAssets"/>
        /// </summary>
        public static IEnumerable<T> GetAssets<T>() where T : Object
        {
            var paths = AssetDatabase.FindAssets($"t:{typeof(T).Name}");

            foreach (var path in paths)
            {
                if (TryGetAsset<T>(path, out var asset))
                    yield return asset;
            }

            yield break;
        }

        /// <summary>
        /// Get script asset assetsPath, return false if no mono script asset matchPackages.
        /// </summary>
        public static bool TryGetScriptAsset<T>(out MonoScript mono,bool matchName=false)
        {
            mono = null;

            var type = typeof(T);

            var paths=AssetDatabase.FindAssets("t:MonoScript");

            foreach (var path in paths)
            {
                var asset = AssetDatabase.LoadAssetAtPath<MonoScript>(path);

                if (asset == null)
                    continue;

                if (asset.GetClass() == type)
                    return true;

                if (matchName)
                    if (asset.name.Equals(typeof(T).Name, StringComparison.CurrentCultureIgnoreCase))
                        return true;
            }

            

            return false;
        }

        /// <summary>
        /// Load all assets inside folder.
        /// </summary>
        public static IEnumerable<T> GetAssetsAtPath<T>(string path, bool includeChildFolder = false) where T : Object
        {
            if (path.StartsWith("Assets"))
                path = EditorPathUtility.GetIOPath(path);

            SearchOption option = includeChildFolder ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            IEnumerable<T> SearchFile(string directoryPath)
            {
                var files = Directory.GetFiles(directoryPath, "*.*");
                //Debug.Log(directoryPath);
                foreach (var filePath in files)
                {
                    // ignore meta file
                    if (filePath.EndsWith(".meta"))
                        continue;

                    var assetPath = EditorPathUtility.GetAssetPath(filePath);
                    if (EditorAssetsUtility.IsAssetExist<T>(assetPath, out var target))
                       yield return target;
                }
            }

            foreach (var target in SearchFile(path))
                yield return target;

            var directies = Directory.GetDirectories(path,"*",option);

            foreach (var dir in directies)
            {
                foreach (var target in SearchFile(dir))
                    yield return target;
            }

            yield break;
        }

        /// <summary>
        /// Load all asset under folder path. ** Null asset will be ignore
        /// </summary>
        /// <param name="path">IO path.</param>
        public static T[] GetAllAssetsAtPath<T>(string path,string searchPattern,bool includeChildFolder=false) where T : Object
        {
            if (string.IsNullOrEmpty(searchPattern))
                searchPattern = "*.*";
            
            if (path.StartsWith("Assets"))
                path = EditorPathUtility.GetIOPath(path);

             SearchOption option = includeChildFolder ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            void SolvePaths(ArrayList list, string[] paths)
            {

                foreach (string filePath in paths)
                {
                    //Debug.Log(filePath);

                    if (filePath.Contains(".meta"))
                        continue;

                    var localPath = EditorPathUtility.GetAssetPath(filePath);

                    //Debug.Log(localPath);

                    var t = AssetDatabase.LoadAssetAtPath<T>(localPath);

                    if (t != null)
                        list.Add(t);
                    else
                        Debug.LogWarning($"Target asset is null, file will be ignore : {localPath}.");
                }
            }


            string[] fileEntries = Directory.GetFiles(path, searchPattern, option);
            Debug.Log($"{nameof(GetAllAssetsAtPath)} {path} found {fileEntries.Length} files.");

            // root folder will be ignore
            var folders = Directory.GetDirectories(path, searchPattern, option);
            Debug.Log($"{nameof(GetAllAssetsAtPath)} {path} found {folders.Length} folders.");

            var list = new ArrayList(fileEntries.Length+folders.Length);

            SolvePaths(list, fileEntries);
            SolvePaths(list, folders);

            T[] result = new T[list.Count];
            for (int i = 0; i < list.Count; i++)
                result[i] = (T)list[i];

            return result;
        }

        public static T[] GetAllAssetsAtPath<T>(string path, bool inculdeChildFolder = false) where T : Object
        {
            EditorUtility.DisplayProgressBar("Loading", "Loading " + typeof(T) + " files..", 0);
            var fileEntries = GetSubFiles(path, true);
            EditorUtility.ClearProgressBar();

            if (fileEntries == null)
                return new T[0];

            ArrayList al = new ArrayList();

            foreach (string filePath in fileEntries)
            {
                Object t = AssetDatabase.LoadAssetAtPath<T>(filePath);
                if (t != null)
                    al.Add(t);
            }
            T[] result = new T[al.Count];
            for (int i = 0; i < al.Count; i++)
                result[i] = (T)al[i];

            return result;
        }

        public static string[] GetSubFiles(string path, bool inculdeChildFolder)
        {
            path = path.Substring(path.IndexOf(cst_rootPath));

            if (path.Contains(cst_rootPath + "/"))
                path = path.Replace(cst_rootPath + "/", string.Empty);
            else
                path = path.Replace(cst_rootPath, string.Empty);


            path = Application.dataPath + "/" + path;

            if (!Directory.Exists(path))
                return null;

            var fileEntries = Directory.GetFiles(path).ToList();

            if (inculdeChildFolder)
            {
                var subFolders = Directory.GetDirectories(path);
                foreach (var folder in subFolders)
                {
                    fileEntries.AddRange(GetSubFiles(folder, inculdeChildFolder));
                }
            }

            var length = fileEntries.Count;
            for (var i = 0; i < length; i++)
            {
                var filePath = fileEntries[i];
                int assetPathIndex = filePath.IndexOf(cst_rootPath);
                fileEntries[i] = filePath.Substring(assetPathIndex);
            }

            return fileEntries.ToArray();
        }


        #endregion

        #region Creation

        [MenuItem(MenuUtil.Assets+"CreateAsset"+MenuUtil.ShortCut_Shift + "_c", true)]
        static bool CheckCreateSO()
        {
            if (Selection.activeObject is not MonoScript mono)
                return false;

            if (mono.GetClass() == null)
                return false;

            if (mono.GetClass().IsClass)
                return mono.IsChild(typeof(ScriptableObject), typeof(MonoBehaviour));
            else
                return false;
        }

        [MenuItem(MenuUtil.Assets + "CreateAsset" + MenuUtil.ShortCut_Shift + "_c", priority = 1000)]
        public static void Create()
        {
            var ob = Selection.activeObject;
            if (ob is MonoScript mono)
                Create(mono);
        }

        public static void Create(MonoScript mono)
        {
            var type = mono.GetClass();

            try
            {
                Object obj;

                var folderPath = mono.GetAssetFolderPath();

                if (mono.IsChild(typeof(ScriptableObject)))
                {
                    // open editor window
                    if (mono.IsChild(typeof(EditorWindow)))
                    {
                        if(type.GetMethod("OpenWindow", BindingFlags.Static | BindingFlags.NonPublic) is MethodInfo info)
                        {                        
                            // try static invoke with initialize funcs
                            info.Invoke(null, null);
                        }
                        else
                        {
                            var window = EditorWindow.GetWindow(type);
                            window.Show();
                        }
                        
                        return;
                    }
                    else if (mono.IsChild(typeof(Editor)))
                    {
                        return;
                    }
                    else
                    {
                        obj = EditorAssetsUtility.CreateInstanceSO(type, false, folderPath, mono.name);
                    }
                }
                else if (mono.IsChild(typeof(MonoBehaviour)))
                {
                    var go = new GameObject(type.Name, type);
                    obj = EditorAssetsUtility.Archive(go, type.Name, false, folderPath);
                    Object.DestroyImmediate(go);
                }
                else
                    throw new NotImplementedException(type.Name);

                EditorGUIUtility.PingObject(obj);
            }
            catch (Exception e)
            {
                Debug.LogError(string.Format("{0} is not a scriptable asset script. {1}", mono.name, e));
            }
        }



        public static ScriptableObject CreateInstanceSO(Type type, bool folderout = false, string path = null, string fileName = null)
        {
            Assert.IsNotNull(type);

            var so = ScriptableObject.CreateInstance(type);

            if (so == null)
            {
                EditorUtility.DisplayDialog("Error", type + " is not valide 0_<", "Got it !");
                throw new KeyNotFoundException();
            }
            else
            {
                return Archive(so, string.IsNullOrEmpty(fileName) ? so.GetType().Name : fileName, folderout, path) as ScriptableObject;
            }

        }

        public static ScriptableObject CreateInstanceSO(string name, bool folderout = false, string path = null, string fileName = null)
        {
            var so = ScriptableObject.CreateInstance(name);

            if (so == null)
            {
                EditorUtility.DisplayDialog("Error", name + " is not valide 0_<", "Got it !");
                throw new KeyNotFoundException();
            }
            else
            {
                return Archive(so, string.IsNullOrEmpty(fileName) ? so.GetType().Name : fileName, folderout, path) as ScriptableObject;
            }

        }

        public static T CreateInstanceSO<T>(bool folderout = false, string path = null, string fileName = null) where T : ScriptableObject
        {
            var so = ScriptableObject.CreateInstance<T>();
            return Archive(so, string.IsNullOrEmpty(fileName) ? so.GetType().Name : fileName, folderout, path) as T;
        }

        #endregion

        #region Save Asset
   
        /// <summary>
        /// Create prefab with type.
        /// </summary>
        public static T CreatePrefab<T>(T @object, string folderPath, string name = null) where T : Object
        {
            if (string.IsNullOrEmpty(name))
                name = @object.name;

            Assert.IsFalse(string.IsNullOrEmpty(name));

            var path = Path.Combine(folderPath, name + ".prefab");

            path = AssetDatabase.GenerateUniqueAssetPath(path);

            if (@object is Component component)
            {
                var go = PrefabUtility.SaveAsPrefabAsset(component.gameObject, path, out var success);
                Assert.IsTrue(success);
#pragma warning disable UNT0014 // Invalid type for call to GetComponent
                return go.GetComponent<T>();
#pragma warning restore UNT0014 // Invalid type for call to GetComponent
            }
            else if (@object is GameObject gameObject)
            {
                var go = PrefabUtility.SaveAsPrefabAsset(gameObject, path, out var success);
                Assert.IsTrue(success);
                return go as T;
            }
            else
                throw new NotImplementedException();
        }

        /// <summary>
        /// Save asset to database.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="folderout"></param>
        /// <param name="path"></param>
        /// <param name="overwrite"></param>
        /// <returns></returns>
        public static T Archive<T>(T asset, string name, bool folderout, string path,bool overwrite=false) where T : Object
        {
            if (string.IsNullOrEmpty(name))
                name = asset.name;

            if (string.IsNullOrEmpty(name))
                name = GUID.Generate().ToString();
                        
            // valid file extension
            var ext = EditorPathUtility.GetExtension(asset);

            if (string.IsNullOrEmpty(path) || folderout)
            {
                path = EditorHelper.SaveFilePanel(path,name, ext.Replace(".",string.Empty));
                name = EditorPathUtility.GetAssetName(path);
                path = EditorPathUtility.GetAssetFolderPath(path);

                Debug.Log($"Archive file config changed -> name : {name} path : {path}");
            }
            else
            {
                Debug.Log($"Archive file with name : {name} path : {path}");
            }

            return EditorHelper.WriteAsset(asset, name, path,overwrite);
        }

        /// <summary>
        /// Save asset next to <paramref name="existAsset"/>
        /// </summary>
        /// <param name="asset">Asset to save in assetDatabase.</param>
        /// <param name="existAsset">Provide forler and similar name.</param>
        /// <returns></returns>
        public static void QuickArchive<T,U>(T asset,U existAsset) where T : Object where U : Object
        {
            var path = AssetDatabase.GetAssetPath(existAsset);

            // use self name
            if(!string.IsNullOrEmpty(asset.name))
            {
                var folderPath = EditorPathUtility.GetAssetFolderPath(path);
                var ext = EditorPathUtility.GetExtension(asset);
                path = Path.Combine(folderPath, asset.name)+ ext;
            }

            Debug.Log(path);

            path = AssetDatabase.GenerateUniqueAssetPath(path);
            AssetDatabase.CreateAsset(asset, path);

            var saveAsset = AssetDatabase.LoadAssetAtPath<T>(path);
            Debug.Log(path,saveAsset);
        }


        #endregion

        #region Duplicate

        /// <summary>
        /// Duplicate asset.
        /// </summary>
        /// <returns>Return true if successfully copy asset.</returns>
        public static bool Duplicate<T>(this T asset, out T newAsset, GameObject go = null) where T : Object
        {
            GameObject gameObj_creation()
            {
                if (go == null)
                {
                    if (asset is Component component)
                        go = component.gameObject;
                }

                return go;
            }

            var valid = asset.GetType().Instantiate(out var obj, gameObj_creation);

            if (valid && obj is T copyAsset)
            {
                EditorUtility.CopySerialized(asset, copyAsset);
                newAsset = copyAsset;
                return true;
            }
            else
            {
                newAsset = null;
                Debug.LogError("Duplicate asset failure : " + (obj.IsNull() ? "Null" : obj));
                return false;
            }
        }

        #endregion

        #region Cut
        const string cutGUID = "CutGUID";

        [MenuItem("Assets/Cut", true)]
        static bool CheckCutAsset()
        {
            foreach (var selected in Selection.objects)
            {
                if (selected == null)
                    return false;

                if (!AssetDatabase.IsMainAsset(selected))
                    return false;
            }

            return true;
        }

        static Object[] sp_CutAssets;

        [MenuItem("Assets/Cut", priority = 1000)]
        static void CacheAsset()
        {
            var length = Selection.objects.Length;
            var str = new Queue<string>(length);

            foreach (var selected in Selection.objects)
            {
                var guid = selected.GetGUID();
                str.Enqueue(guid);
            }

            EditorGUIUtility.systemCopyBuffer = cutGUID + string.Join("$", str);
        }


        [MenuItem("Assets/Paste", true)]
        static bool CheckPasteAsset()
        {
            var str=EditorGUIUtility.systemCopyBuffer;
            if (!str.StartsWith(cutGUID))
                return false;

            var folder = Selection.activeObject;

            if (folder == null)
                return false;

            return true;
        }

        [MenuItem("Assets/Paste", priority = 1000)]
        static void PasteAsset()
        {
            using var refreshLock = LockRefresh();

            Debug.LogFormat("Paste target folder {0}", Selection.activeObject);

            var targetAssetPath = AssetDatabase.GetAssetPath(Selection.activeObject);

            var newFolder = 
                AssetDatabase.IsValidFolder(targetAssetPath)?
                Selection.activeObject.GetAssetPath():
                Selection.activeObject.GetAssetFolderPath();


            if (string.IsNullOrEmpty(newFolder))
                return;

            Debug.LogFormat("Move assets to new folder : {0}", newFolder);

            var str = EditorGUIUtility.systemCopyBuffer;
            if (!str.StartsWith(cutGUID))
                return;


            var paths = str.Remove(0, cutGUID.Length).
                Split("$").
                Select(x => AssetDatabase.GUIDToAssetPath(x));

            //AssetDatabase.StartAssetEditing();

            foreach (var path in paths)
            {
                try
                {
                    var asset = AssetDatabase.LoadAssetAtPath<Object>(path);

                    if (asset.IsNull())
                    {
                        Debug.LogErrorFormat("Copy assetsPath can't matchPackages asset to move : {0}.", path);
                        continue;
                    }

                    var extension = EditorPathUtility.GetExtension(asset);
                    var newPath = newFolder + "/" + asset.name;//.Replace(".asset",null);// + extension;

                    if (!AssetDatabase.IsValidFolder(path))
                        newPath += extension;


                    Debug.Log($"Move asset {asset.name} to new path : {newPath}");
                    AssetDatabase.MoveAsset(path, newPath);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            EditorGUIUtility.systemCopyBuffer=null;

            //AssetDatabase.StopAssetEditing();

            //AssetDatabase.SaveAssets();
            //AssetDatabase.Refresh();
        }

        #endregion

        #region Replace

        /// <summary>
        /// Replace asset from database.
        /// </summary>
        public static void Replace<T>(this T oldTextAsset, T newTextAsset) where T : Object
        {
            Assert.IsTrue(oldTextAsset.IsPersistentAsset());

            var name = oldTextAsset.name;
            var path = AssetDatabase.GetAssetPath(oldTextAsset);
            var folder = EditorPathUtility.GetAssetFolderPath(oldTextAsset);
            AssetDatabase.DeleteAsset(path);

            Archive(newTextAsset, name, false, folder);
        }

        #endregion

        #region Extract

        /// <summary>
        /// Remove missing script root and extract assets
        /// </summary>
        [MenuItem("Assets/ExtractNull", priority = 1000)]
        static void ExtractNull()
        {
            using var refreshLock = LockRefresh();

            var rootID = Selection.activeInstanceID;

            Debug.LogFormat("Selection instanceID {0}", rootID);

            var path = AssetDatabase.GetAssetPath(rootID);
            Debug.Log($"Path : {path}");

            var folder = EditorPathUtility.GetAssetFolderPath(path);

            var parent = EditorUtility.InstanceIDToObject(rootID);

            if (parent.NotNull())
            {
                Debug.LogFormat("Main asset {0}", parent);
                Debug.LogFormat("Main asset  type {0}", parent.GetType());
            }

            if (!AssetDatabase.IsValidFolder(folder + "/Convex"))
                folder = AssetDatabase.CreateFolder(folder, "Convex");
            else
                folder += "/Convex";

            ExtractSubAssets(path, null, folder);


            AssetDatabase.DeleteAsset(path);
        }



        /// <summary>
        /// Remove missing script root and extract assets
        /// </summary>
        [MenuItem("Assets/SubAsset/ExtractSubAsset", priority = 1000)]
        static void ExtractSubAsset()
        {
             var subAssetID = Selection.activeInstanceID;


            var path = AssetDatabase.GetAssetPath(subAssetID);


            var depends=AssetDatabase.GetDependencies(path);

            Debug.Log("Depend assets : \n"+string.Join("\n", depends));

            string guid= GetGUID(subAssetID);

            Object asset;

            if (ObjectIdentifier.TryGetObjectIdentifier(subAssetID, out var id))
            {
                asset = ObjectIdentifier.ToObject(id);
            }
            else
            {
                Debug.LogError("Failure to found asset");
                asset = AssetDatabase.LoadAssetAtPath<Object>(path);
            }


            Debug.Log($"Path : {path} AssetGUID : {AssetDatabase.GUIDFromAssetPath(path)} InstanceGUID : {guid}");


            AssetDatabase.RemoveObjectFromAsset(asset);

            path = AssetDatabase.GenerateUniqueAssetPath(path);
 
            AssetDatabase.CreateAsset(asset, path);
          
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(LogAsset(asset));

            var folder = EditorUtility.OpenFolderPanel("Select Folder", "Chose folder to overwrite guid of assets.", null);

            if (string.IsNullOrEmpty(folder))
                return;

            var valid =ObjectIdentifier.TryGetObjectIdentifier(subAssetID, out var newID) ;
            Assert.IsTrue(valid);


            Dictionary<string, Dictionary<string, ObjectIdentifier>> dic = new();
            var binding = new Dictionary<string, ObjectIdentifier>();
            binding.Add(id.localIdentifierInFile.ToString(), newID);

            dic.Add(id.guid.ToString(), binding);


            ReplaceGUIDs(dic, folder);
        }




        public static Object[] ExtractSubAssets(string assetsPath,Func<string,string> namingFunc = null, string targetFolder = null)
        {
            if (string.IsNullOrEmpty(targetFolder))
                targetFolder = EditorPathUtility.GetAssetFolderPath(assetsPath);

            Debug.Log($"Target folder : {targetFolder}");

            var name = EditorPathUtility.GetAssetName(assetsPath);
            
            if(namingFunc!=null)
                name = namingFunc(name);

            var assets = AssetDatabase.LoadAllAssetsAtPath(assetsPath);


            Debug.Log($"Load {assets.Length} assets.");


            var multi = assets.Length > 2;
            var sn = 0;

            foreach (var asset in assets)
            {
                //Debug.Log($"Asset {(asset == null ? "is" : "not")} null.");

                if (asset == null)
                    continue;

                //Debug.LogFormat("Subasset instanceID {0}", asset.GetInstanceID());

                //Debug.Log($"Sub asset assetsPath : {AssetDatabase.GetAssetPath(asset.GetInstanceID())}.");

                var sub = AssetDatabase.IsSubAsset(asset);

                Debug.Log($"{asset.name} is sub asset : {sub}");

                if (!sub)
                    continue;

                AssetDatabase.RemoveObjectFromAsset(asset);

                //Debug.Log(asset);

                var extension = EditorPathUtility.GetExtension(asset);

                Debug.Log(extension);

                var newPath = $"{targetFolder}/{name}";

                if (multi)
                    newPath += $"_{sn}{extension}";
                else
                    newPath += $"{extension}";

                newPath = AssetDatabase.GenerateUniqueAssetPath(newPath);
                //Debug.Log($"New assetsPath {targetAssetPath}.");
                AssetDatabase.CreateAsset(asset, newPath);

                //Debug.Log(asset);
                //Debug.Log(AssetDatabase.IsMainAsset(asset));

                sn++;
                continue;
            }

            return assets.Where(x => x != null).ToArray();
        }


        /// <summary>
        /// Extract sub assets. 
        /// </summary>
        /// <param name="assets">Target to extract.</param>
        /// <param name="targetFolder">Destination folder.</param>
        /// <param name="bindings">OldGuid bindings to replace.</param>
        /// <param name="naming">Rename sub asset function.</param>
        /// <returns></returns>
        public static void ExtractSubAssets(Object[] assets, string targetFolder, out IEnumerable<KeyValuePair<ObjectIdentifier, ObjectIdentifier>> bindings,Func<Object,string> naming=null)
        {
            bool customFolder = string.IsNullOrEmpty(targetFolder) && AssetDatabase.IsValidFolder(targetFolder);

            Debug.Log($"Extract sub assets to : {targetFolder}");

            var length = assets.Length;

            var assetbindings = new Queue<KeyValuePair<ObjectIdentifier, ObjectIdentifier>>(length);

            for (int i = 0; i < length; i++)
            {
                try
                {
                    var asset = assets[i];

                    if (asset == null)
                        continue;

                    //Debug.LogFormat("Subasset instanceID {0}", asset.GetInstanceID());

                    //Debug.Log($"Sub asset assetsPath : {AssetDatabase.GetAssetPath(asset.GetInstanceID())}.");

                    // valid
                    {
                        var sub = AssetDatabase.IsSubAsset(asset);

                        if (!sub)
                        {
                            Debug.LogError($"{asset.name} is not a sub asset : {asset}");
                            continue;
                        }
                    }

                    if (!ObjectIdentifier.TryGetObjectIdentifier(asset, out var oldID))
                    {
                        Debug.LogException(new InvalidDataException($"Failure to get identifier from {asset}"));
                        continue;
                    }



                    string targetAssetPath;

                    // 
                    {
                        var name = naming?.Invoke(asset);

                        if (string.IsNullOrEmpty(name))
                            name = EditorPathUtility.GetRootAssetName(asset);

                        if (Path.HasExtension(name))
                            name = Path.GetFileNameWithoutExtension(name);

                        name += EditorPathUtility.GetExtension(asset);

                        targetAssetPath = Path.Combine(targetFolder, name);// $"{targetFolder}/{name}";
                        targetAssetPath = AssetDatabase.GenerateUniqueAssetPath(targetAssetPath);

                        //Debug.Log($"New assetsPath {targetAssetPath}.");
                    }

                    // split asset
                    AssetDatabase.RemoveObjectFromAsset(asset);

                    AssetDatabase.CreateAsset(asset, targetAssetPath);

                    var valid = ObjectIdentifier.TryGetObjectIdentifier(asset, out var newID);

                    Assert.IsTrue(valid, "Sub asset should be able to identifi.");


                    assetbindings.Enqueue(new(oldID, newID));
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    continue;
                }
            }

            bindings = assetbindings;
        }


        #endregion

        #region Config

        /// <summary>
        /// ????? Set editor icon for object.
        /// </summary>
        public static void SetCustomIconOnGameObject<T>(T obj, Texture2D iconResource) where T : Object
        {
            //var go = new GameObject();
            //var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/MyIcon.png");

            EditorGUIUtility.SetIconForObject(obj, iconResource);
        }

        #endregion

        #region Sub Asset

        [MenuItem("Assets/SubAsset/ShowAllSubAsset")]
        static void ShowAllSubAsset()
        {
            var obj = Selection.activeObject;

            if (!EditorAssetsUtility.IsPersistentAsset(obj))
                return;

            var path = obj.GetAssetPath();
            var assets = AssetDatabase.LoadAllAssetsAtPath(path);

            Debug.Log($"Find [{assets.Length}] sub-assets :\n{string.Join("\n", assets.Select(x => $"{x.name}-{x.GetInstanceID()}"))}",obj);

            foreach (var asset in assets)
            {
                if (asset == obj)
                    continue;

                var flag = asset.hideFlags;

                if (flag.HasFlag(HideFlags.HideInInspector))
                    flag ^= HideFlags.HideInInspector;

                if (flag.HasFlag(HideFlags.HideInHierarchy))
                    flag ^= HideFlags.HideInHierarchy;

                asset.hideFlags = flag;
            }
        }

        [MenuItem("Assets/SubAsset/DeleteSubAsset", true)]
        static bool CheckDeleteSubAsset()
        {
            var ob = Selection.activeObject;

            Debug.Log(ob);

            if (ob.IsNull())
                return false;

            return AssetDatabase.IsSubAsset(ob);
        }

        [MenuItem("Assets/SubAsset/DeleteSubAsset", priority = 1001)]
        static void Delete()
        {
            using var refreshLock = LockRefresh();

            //var ob = Selection.activeObject;
            //DeleteSubAsset(ob);
            var obs = Selection.objects.ToArray();
            var parentPaths = obs.Select(x => x.GetAssetPath()).ToHashSet();

            foreach (var obj in obs)
            {
                if (AssetDatabase.IsSubAsset(obj))
                    DeleteSubAsset(obj,false);
            }

            foreach (var path in parentPaths)
            {
                AssetDatabase.ImportAsset(path);
            }
        }

        /// <summary>
        /// Delete sub asset.
        /// </summary>
        /// <param name="alert">Set true to display confirm info message before delete asset.</param>
        public static void DeleteSubAsset(Object obj, bool alert = true)
        {
            using var refreshLock = LockRefresh();

            if (obj.IsNull())
            {
                Debug.LogError("Value should not be null");
                return;
            }

            if (!AssetDatabase.IsSubAsset(obj))
            {
                Debug.LogWarningFormat("Delete target is not sub asset : {0}", obj.name);
                return;
            }

            if (alert)
            {
                if (!EditorUtility.DisplayDialog("Confirm", string.Format("Delete subAsset {0} ?", obj), "Yes", "No"))
                    return;
            }


            AssetDatabase.RemoveObjectFromAsset(obj);

            Object.DestroyImmediate(obj, true);
        }

        /// <summary>
        /// Whether mono script is child of target type.
        /// </summary>
        public static bool IsChild(this MonoScript mono, params Type[] types)
        {
            var monoType = mono.GetClass();

            foreach (var type in types)
            {
                if (monoType.IsSubclassOf(type))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Add asset under mian asset.
        /// </summary>
        /// <param name="asset">Main asset to contain sub-assets.</param>
        /// <param name="subAsset">Sub asset.</param>
        /// <param name="name">Name of sub asset.</param>
        /// <returns></returns>
        public static T AddSubAsset<T>(this Object asset, T subAsset, string name = null) where T : Object
        {
            Assert.IsTrue(AssetDatabase.IsMainAsset(asset), asset + " must be as root asset.");
            Assert.IsFalse(subAsset is Component || subAsset is GameObject);

            if (!string.IsNullOrEmpty(name))
            {
                if (AssetDatabase.IsMainAsset(subAsset))
                    AssetDatabase.RenameAsset(AssetDatabase.GetAssetPath(subAsset), name);
                else
                    subAsset.name = name;
            }

            AssetDatabase.AddObjectToAsset(subAsset, asset);

            return subAsset;
        }

        /// <summary>
        /// Try get sub asset with name.
        /// </summary>
        public static bool TryGetSubAsset<T>(this Object asset,out T subAsset, string name = null) where T : Object
        {
            subAsset = GetSubAsset<T>(asset, name);
            return subAsset != null;
        }

        /// <summary>
        /// Get sub asset with name.
        /// </summary>
        public static T GetSubAsset<T>(this Object asset,string name = null) where T: Object
        {
            Assert.IsTrue(AssetDatabase.IsMainAsset(asset), asset + " must be as root asset.");

            var subAssets = GetSubObjectsOfType<T>(asset);

            var validName = !string.IsNullOrEmpty(name);

            foreach (var subAsset in subAssets)
            {
                Consoles.Log($"Load sub asset [{subAsset}]");

                if(subAsset is T target)
                {
                    if (!validName)
                        return target;

                    if (target.name.Equals(name))
                        return target;
                    else
                        continue;
                }
            }

            return null;
        }

        /// <summary>
        /// Get all sub asset which is type of <typeparamref name="T"/> under <paramref name="mainAsset"/>. 
        /// </summary>
        public static IEnumerable<T> GetSubObjectsOfType<T>(this Object mainAsset) where T : Object
        {
            Object[] objs = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(mainAsset));

            foreach (Object o in objs)
                if (o is T value)
                    yield return value;

            yield break;
        }

        #endregion

        #region Util

        /// <summary>
        /// Get native asset.
        /// </summary>
        public static T LoadAssetFromUniqueAssetPath<T>(string assetPath) where T : Object
        {
            if (assetPath.Contains("::"))
            {
                string[] parts = assetPath.Split(new string[] { "::" }, StringSplitOptions.RemoveEmptyEntries);
                assetPath = parts[0];
                if (parts.Length > 1)
                {
                    string assetName = parts[1];
                    Type t = typeof(T);
                    var assets = AssetDatabase.LoadAllAssetsAtPath(assetPath)
                        .Where(i => t.IsAssignableFrom(i.GetType())).Cast<T>();
                    var obj = assets.Where(i => i.name == assetName).FirstOrDefault();
                    if (obj == null)
                    {
                        int id;
                        if (int.TryParse(parts[1], out id))
                            obj = assets.Where(i => i.GetInstanceID() == id).FirstOrDefault();
                    }
                    if (obj != null)
                        return obj;
                }
            }
            return AssetDatabase.LoadAssetAtPath<T>(assetPath);
        }

        /// <summary>
        /// ??
        /// </summary>
        public static string GetUniqueAssetPath(Object obj)
        {
            string path = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(obj.name))
                path += "::" + obj.name;
            else
                path += "::" + obj.GetInstanceID();
            return path;
        }

        #endregion

        /// <summary>
        /// Lock editor AssetDataBase refresh during batch editing, and auto refresh editor after counter is zero.
        /// </summary>
        /// <returns></returns>
        public static IDisposable LockRefresh()
        {
            return AssetEditingLock.Use();
        }
    }
}
#endif