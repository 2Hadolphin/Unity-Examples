using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;
using UnityEditor;
using Sirenix.Utilities.Editor;
using Sirenix.Utilities;
using System.Text;
using System.Linq;
using System;
using Object = UnityEngine.Object;
using UnityEngine.Assertions;
using System.IO;

namespace Return.Editors
{

    public class ExportManager : DropAssetWindow
    {
        [MenuItem(MenuUtil.Assets+"ExportManager",priority = MenuUtil.Order_First)]
        static void OpenWindow()
        {
            var window = GetWindow<ExportManager>();

            window.position = GUIHelper.GetEditorWindowRect().AlignCenter(700, 700);
            window.onTargetAdded = window.InitPackageName;
            window.LoadConfigs();
        }

        #region Package Option

        [PropertySpace(3, 3)]
        [BoxGroup("Package Config")]
        public ExportPackageOptions packageOption = ExportPackageOptions.Interactive | ExportPackageOptions.Recurse | ExportPackageOptions.IncludeDependencies;// | ExportPackageOptions.IncludeLibraryAssets;

        [OnValueChanged(nameof(InitProjectOption))]
        [PropertySpace(3, 3)]
        [BoxGroup("Package Config")]
        [ShowInInspector]
        ExportProjectOption projectOption = ExportProjectOption.None;

        /// <summary>
        /// Setup project output option. **Tags | Layers | Inputs | Graphics | Packages | Physics
        /// </summary>
        void InitProjectOption()
        {
            if (projectOption.HasFlag(ExportProjectOption.Packages))
                LoadPackageInstaller();
        }

        /// <summary>
        /// 
        /// </summary>
        void LoadPackageInstaller()
        {
            PackageInstaller installer;

            // ??
            if (DropZoneObjects.Count < 100)
            {
                var cache = DropZoneObjects.FirstOrDefault(x => x is PackageInstaller);
                if (cache!=null && cache.Parse(out installer))
                    if (DropZoneObjects.Contains(installer.Mainfest))
                        return;
            }

            installer = PackageInstaller.BuildInstaller();
            
            var assets = EditorUtility.CollectDependencies(new[] { installer });

            foreach (var asset in assets)
                AddDropZoneTarget(asset);

            var depends = AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(installer), true);

            foreach (var depend in depends)
                AddDropZoneTarget(AssetDatabase.LoadAssetAtPath(depend,typeof(Object)));           

            AddDropZoneTarget(installer);
            AddDropZoneTarget(installer.Mainfest);
        }


        [PropertyOrder(3)]
        [BoxGroup("Package Config")]
        [ShowInInspector]
        FolderPathFinder FilePath = new(nameof(ExportManager));

        [PropertyOrder(4)]
        [PropertySpace(3, 3)]
        [BoxGroup("Package Config")]
        [SerializeField]
        string FileName;

        void InitPackageName(Object obj)
        {
            if (string.IsNullOrEmpty(FileName))
            {
                if (EditorPathUtility.IsValidFolder(obj))
                    FileName = obj.name;
                else
                    FileName = EditorPathUtility.GetAssetFolderName(obj);
            }
        }

        #region PrefabVariant

        [BoxGroup("Package Config")]
        [PropertyOrder(5)]
        [SerializeField]
        bool m_isCreateDummyPrefab;
        public bool IsCreateDummyPrefab { get => m_isCreateDummyPrefab; set => m_isCreateDummyPrefab = value; }

        [BoxGroup("Package Config")]
        [PropertyOrder(5)]
        [ShowIf(nameof(IsCreateDummyPrefab))]
        [SerializeField]
        bool m_bindingAsVariant;
        public bool BindingAsVariant { get => m_bindingAsVariant; set => m_bindingAsVariant = value; }

        [BoxGroup("Package Config")]
        [PropertyOrder(5)]
        [ShowIf(nameof(IsCreateDummyPrefab))]
        [SerializeField]
        [FolderPath]
        string m_variantFolderPath;
        public string VariantFolderPath { get => m_variantFolderPath; set => m_variantFolderPath = value; }

        #endregion

        #endregion


        #region Preference
        [HideInInspector]
        [SerializeField]
        List<ExportBundle> preferences;

        /// <summary>
        /// ??????
        /// </summary>
        [HideInInspector]
        [SerializeField]
        string[] config_buttons;

        int index;


        /// <summary>
        /// Get export preference path via this script asset.  
        /// </summary>
        string GetPrefPath()
        {
            var mono = MonoScript.FromScriptableObject(this);
            Assert.IsNotNull(mono);
            var path = EditorPathUtility.GetAssetFolderPath(mono);
            Assert.IsFalse(string.IsNullOrEmpty(path));

            return path;
        }

        /// <summary>
        /// Reload export configs.
        /// </summary>
        public void LoadConfigs()
        {
            // cache inspect target
            string current_inspect=null;
            if (preferences != null && index < preferences.Count)
                current_inspect = preferences[index].Name;

            var path = GetPrefPath();

            var configs = EditorAssetsUtility.GetAllAssetsAtPath<TextAsset>(path);

            var length = configs.Length;

            preferences = new(length);
            config_buttons = new string[length+1];
            var strCache = new Queue<string>(length + 1);
            strCache.Enqueue("Null");

            for (int i = 0; i < length; i++)
            {
                var txt = configs[i];

                // ignore mono
                if (txt is MonoScript)
                    continue;

                // load json as export config
                var pref = ExportBundle.LoadJson(txt.text);

                if (pref == null)
                    continue;

                preferences.Add(pref);
                strCache.Enqueue(pref.Name);
            }

            config_buttons = strCache.ToArray();

            Assert.IsTrue(config_buttons.Length-1 == preferences.Count);

            // reload inspect target
            if (!string.IsNullOrEmpty(current_inspect))
            {
                var pref = preferences.First(x => x.Name == current_inspect);
                LoadPref(pref);
            }

        }

        /// <summary>
        /// Add bundle preference via TextAsset.
        /// </summary>
        void AddPref(Object obj)
        {
            // ignore normal asset
            if (obj is not TextAsset txt)
            {
                Debug.LogError("Not textAsset.");
                return;
            }

            // ignore script asset
            if (txt is MonoScript)
                return;

            var pref = ExportBundle.LoadJson(txt.text);

            if (pref == null)
            {
                Debug.LogError("Data resolve null "+txt.text);
                return;
            }

            if(preferences.Exists(x=>x.Name==pref.Name))
            {
                Debug.LogError($"Repeat loading bundle config : [{pref.Name}].");
                return;
            }

            if (preferences.CheckAdd(pref))
            {
                config_buttons = config_buttons.Append(pref.Name).ToArray();
                index = config_buttons.IndexOf(pref.Name);

                LoadPref(pref);
            }
        }

        /// <summary>
        /// Load export bundle config to window.
        /// </summary>
        void LoadPref(ExportBundle bundle)
        {
            packageOption = bundle.PackageOption;
            projectOption = bundle.ProjectOption;

            FilePath.Path = bundle.FilePath;
            FileName = bundle.FileName;

            // reload drop zone
            {
                DropZoneObjects.Clear();

                var objs = bundle.GUIDs.Select(x => EditorAssetsUtility.GetAsset(x)).Where(x => x != null);

                foreach (var obj in objs)
                    AddDropZoneTarget(obj);
            }
        }

        /// <summary>
        /// Save export config as json file.
        /// </summary>
        TextAsset SavePref(params string[] additionalGUIDs)
        {

            var path = GetPrefPath();

            path = EditorUtility.SaveFilePanel(
                 "Save preference",
                 EditorPathUtility.GetIOPath(path),
                 $"PackagePreference_{FileName}",
                 "asset"
                 );
            

            path = EditorPathUtility.GetEditorPath(path);

            var name = EditorPathUtility.GetAssetName(path);
            path = EditorPathUtility.GetAssetFolderPath(path);

            var pref = new ExportBundle()
            {
                Name=name,
                PackageOption = packageOption,
                ProjectOption = projectOption,

                FilePath = FilePath,
                FileName = FileName,

                IsCreateDummyPrefab = IsCreateDummyPrefab,
                BindingAsVariant = BindingAsVariant,
                VariantFolderPath= VariantFolderPath,

                GUIDs = DropZoneObjects.Select(x => EditorAssetsUtility.GetGUID(x)).ToArray()
            };

            if (!additionalGUIDs.IsNullOrEmpty())
            {
                var newGUIDs = new List<string>(pref.GUIDs);
                pref.GUIDs = newGUIDs.Concat(additionalGUIDs).ToArray();
            }
                

            var json = EditorJsonUtility.ToJson(pref, true);

            var text = new TextAsset(ExportBundle.confirm+json);


            return EditorAssetsUtility.Archive(text, name,false,path,true);

            //return pref;
        }

        [PropertySpace(5,5)]
        [OnInspectorGUI]
        void DrawConfig()
        {
            GUILayout.BeginHorizontal();

            if (preferences == null)
            {
                GUILayout.Label("Null preset package bundle",GUILayout.ExpandWidth(true));
            }
            else
            {
                var selected = EditorGUILayout.Popup("Config", index, config_buttons,GUILayout.ExpandWidth(true));

                if (selected != index)
                {
                    index = selected;

                    if(selected!=0)
                    {
                        //load preference
                        var config = preferences[selected - 1];
                        LoadPref(config);
                    }
                }
            }

            if(GUILayout.Button("Add Preference", GUILayout.Width(120)))
            {
                try
                {
                    var mono = MonoScript.FromScriptableObject(this);
                    var path = EditorUtility.OpenFilePanel("Add Preference", EditorPathUtility.GetIOPath(mono), "*");

                    Debug.Log($"File path : {path}");

                    if (string.IsNullOrEmpty(path))
                        return;

                    path = EditorPathUtility.GetEditorPath(path);
                    var pref = AssetDatabase.LoadAssetAtPath(path, typeof(Object));

                    AddPref(pref);


                    Debug.Log($"File path : {path}");
                }
                finally
                {
                    GUIUtility.ExitGUI();
                }
            }

            if (GUILayout.Button("Save Preference", GUILayout.Width(120)))
            {
                try
                {
                    var addSelfRef = EditorUtility.DisplayDialog("Binding Option", "Whether adding self reference into config file?", "Yes.", "No");

                    var guid = GUID.Generate().ToString();

                    var prefAsset = SavePref(addSelfRef? guid : null);

                    // edit asset guid
                    if(addSelfRef)
                    {
                        var path = prefAsset.GetAssetPath();

                        EditorAssetsUtility.ChangeGUID(prefAsset.GetMetaPath(), guid);
                        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                        AssetDatabase.Refresh();

                        prefAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                    }

                    AddPref(prefAsset);
                }
                finally
                {
                    GUIUtility.ExitGUI();
                }
    
            }


            GUILayout.EndHorizontal();
        }

        #endregion

        #region Operate

        [PropertyOrder(5)]
        [PropertySpace(3, 3)]
        [BoxGroup("Package Config")]
        [Button("ExportPackage",ButtonHeight =41)]
        public void Export()
        {
            var assetNums = DropZoneObjects.Count;
            var options= FlagExtension.GetFlagsValue(projectOption);

            var exportedPackageAssetList = new List<string>(assetNums + options.Length + 1);

            // add project export configs
            foreach (ExportProjectOption option in options)
            {
                var binding = GetProjectBinding(option);

                //Debug.Log($"Project option {option} binding {binding}");

                if (string.IsNullOrEmpty(binding))
                    continue;

                exportedPackageAssetList.Add(binding);
            }

            Debug.Log("Project output options :\n "+string.Join("\n", exportedPackageAssetList));

            // mapping prefab variant into custom folder
            if(IsCreateDummyPrefab && DropZoneObjects.Exist(x=>x is GameObject go && go.IsPrefab()))
            {
                try
                {
                    if (!AssetDatabase.IsValidFolder(VariantFolderPath))
                        EditorPathUtility.SafeCreateFolder(VariantFolderPath);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Invalid variant bundle path [{VariantFolderPath}].");
                    Debug.LogException(e);
                    return;
                }

                for (int i = 0; i < assetNums; i++)
                {
                    var obj = DropZoneObjects[i];

                    if (obj is not GameObject go)
                        continue;

                    if (PrefabUtility.IsPartOfVariantPrefab(go))
                    {
                        if (obj.GetAssetFolderPath() == VariantFolderPath)
                            continue;
                    }

                    var instance = PrefabUtility.InstantiatePrefab(go) as GameObject;
                    var assetPath = AssetDatabase.GenerateUniqueAssetPath($"{VariantFolderPath}/{go.name}.prefab");
                    DropZoneObjects[i] = PrefabUtility.SaveAsPrefabAsset(instance, assetPath);
                    instance.Destroy();
                }
            }

            try
            {
                if (assetNums > 0)
                {
                    for (int i = 0; i < assetNums; i++)
                    {
                        EditorUtility.DisplayProgressBar("ExportPackage", string.Format("Packing {0}", DropZoneObjects[i].name), (float)i / assetNums);
                        var path = AssetDatabase.GetAssetPath(DropZoneObjects[i]);
                        exportedPackageAssetList.Add(path);
                    }
                }
                else // empty package or project setting only
                {
                    //var path = "Assets/Return.asset";//AssetDatabase.LoadAssetAtPath<DefaultAsset>().GetGUID();
                    var path = "Assets/Return/ProjectPreset/CustomScriptTemplate.cs";//AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/Return/ProjectPreset/CustomScriptTemplate.cs").GetGUID();
                    Debug.Log($"Add default asset {path}.");
                    exportedPackageAssetList.Add(path);
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.ExportPackage(
              exportedPackageAssetList.ToArray(),
              Path.Combine(FilePath, FileName + ".unitypackage"),
              packageOption
              );

            AssetDatabase.Refresh();
        }

        #endregion

        /// <summary>
        /// Return project output binding.
        /// </summary>
        public static string GetProjectBinding(ExportProjectOption option)
        {
            return option switch
            {
                ExportProjectOption.None => null,
                ExportProjectOption.All => null,
                ExportProjectOption.Tags => "ProjectSettings/TagManager.asset",
                ExportProjectOption.Layers => "ProjectSettings/ProjectSettings.asset",
                ExportProjectOption.Inputs => "ProjectSettings/InputManager.asset",
                ExportProjectOption.Graphics => "GraphicsSettings.asset",
                ExportProjectOption.Packages => "Packages/packages-lock.json",
                ExportProjectOption.Physics => "ProjectSettings/DynamicsManager.asset",//"DynamicsManager.asset",


                _ => throw new NotImplementedException(option.ToString()),
            };
        }


    }
}