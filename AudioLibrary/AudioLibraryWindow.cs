using System;
using System.Text.RegularExpressions;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;
using Object = UnityEngine.Object;
using UnityEditor;

using Return;

using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities.Editor;
using Sirenix.Utilities;
using Sirenix.OdinInspector.Editor.Drawers;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

namespace Return.Editors
{
    /// <summary>
    /// Read audio file at disk(out side project).
    /// </summary>
    public partial class AudioLibraryWindow : OdinEditorWindow
    {
        [MenuItem(MenuUtil.Tool_Asset_Audio + "AudioLibrary")]
        private static void OpenWindow()
        {
            var window = GetWindow<AudioLibraryWindow>();

            window.position = GUIHelper.GetEditorWindowRect().AlignCenter(700, 900);
            window.titleContent = new GUIContent("Audio Library", SdfIcons.CreateTransparentIconTexture(SdfIconType.Earbuds, Color.white, 20, 20, 5));
        }

        #region Routine

        protected override void Initialize()
        {
            ColorCache = ColorCache.GetGUICache(Color.green);

            SearchFolderPath = new FolderPathFinder($"{nameof(AudioLibraryWindow)}_searchPath", @"D:\SFX").Initialize();
            ArchiveFolderPath = new FolderPathFinder($"{nameof(AudioLibraryWindow)}_archivePath", EditorPathUtility.cst_TempArchive).Initialize();
        }

        protected virtual void Update()
        {
            if (Loading != null)
                Loading.MoveNext();
        }

        protected override void OnDestroy()
        {
            DisposeClip();

            base.OnDestroy();
        }

        #endregion


        #region GUI

        const string cst_tab = "tabs";
        const string cst_tab_search = cst_tab + "/Search/";
        const string cst_tab_preview = cst_tab + "/Preview/";
        const string cst_tab_archive = cst_tab + "/Archive/";

        protected ColorCache ColorCache;

        #endregion

        #region Search

        [PropertyOrder(-1)]
        [TabGroup(cst_tab, "Search")]
        [SerializeField]
        FolderPathFinder m_folderPath;//= new($"{nameof(AudioLibraryWindow)}_path", EditorPathUtility.cst_TempArchive);

        [TabGroup(cst_tab, "Search")]
        [SerializeField]
        SearchOption m_searchOption = SearchOption.AllDirectories;

        [TabGroup(cst_tab, "Search")]
        [SerializeField]
        string m_extension = ".mp3,.wav,.ogg";

        [TabGroup(cst_tab, "Search")]
        [SerializeField]
        string m_search;

        [TabGroup(cst_tab, "Search")]
        [Button(ButtonSizes.Medium, Icon = SdfIconType.Download)]
        public virtual void SearchFiles()
        {
            if (!Directory.Exists(SearchFolderPath))
            {
                Debug.LogException(new Exception($"Folder not exist [{SearchFolderPath}]."));
                return;
            }

            var pattern = "*.";

            // search pattern
            {
                if (string.IsNullOrEmpty(Extension) || Extension.Contains(','))
                    pattern += "*";
                else
                    pattern += Extension;
            }

            var filePaths = Directory.GetFiles(SearchFolderPath, pattern, SearchOption);

            // filter by name match
            if (!string.IsNullOrEmpty(Search))
            {
                filePaths = filePaths.
                   Where(path => path.Contains(Search, StringComparison.InvariantCultureIgnoreCase)).
                   ToArray();
            }

            // filter by multiple extension
            if (pattern.FastEndsWith(".*") && Extension.Contains(','))
            {
                var exts = Extension.Split(',');
                filePaths = filePaths.
                     Where(path => exts.Exist(ext=> path.FastEndsWith(ext))).
                     ToArray();
            }


            Debug.Log($"Match [{filePaths.Length}] files with [{pattern}].");

            // remove repeat data
            {
                var newDatas = filePaths.Select(x => new ExternalAudioInfo(x)).ToHashSet();

                foreach (var exist in Datas)
                {
                    if (newDatas.TryGetValue(exist, out var remove))
                        newDatas.Remove(remove);
                }

                Datas.AddRange(newDatas);
            }
        }

        public FolderPathFinder SearchFolderPath { get => m_folderPath; set => m_folderPath = value; }
        public SearchOption SearchOption { get => m_searchOption; set => m_searchOption = value; }
        public string Extension { get => m_extension; set => m_extension = value; }
        public string Search { get => m_search; set => m_search = value; }

        [CustomValueDrawer(nameof(DrawData))]
        [PropertyOrder(1)]
        [TabGroup(cst_tab, "Search")]
        [ListDrawerSettings(ShowFoldout = false, NumberOfItemsPerPage = 10)]
        [SerializeField]
        List<ExternalAudioInfo> m_datas;

        public List<ExternalAudioInfo> Datas { get => m_datas; set => m_datas = value; }
        [SerializeField, HideInInspector]
        protected HashSet<int> SelectedDatas = new();

        /// <summary>
        /// Draw GUI for external audio info.
        /// </summary>
        protected virtual void DrawData(ExternalAudioInfo data)
        {
            if (data == null)
            {
                EditorGUILayout.HelpBox("Null", MessageType.Error);
                return;
            }

            {
                SirenixEditorGUI.BeginHorizontalPropertyLayout(GUIContent.none);

                GUILayout.Label(data.Name, SirenixGUIStyles.BoldLabel,GUILayout.Width(150));
                GUILayout.Label(data.Extension, SirenixGUIStyles.BoldLabel, GUILayout.Width(70));

                {
                    EditorGUI.BeginChangeCheck();

                    var newTarget = SirenixEditorFields.UnityObjectField(data.Reference.Target, typeof(AudioClip), true, GUILayout.Width(250));

                    if (EditorGUI.EndChangeCheck())
                    {
                        var reference = data.Reference;
                        reference.Target = newTarget as AudioClip;
                        data.Reference = reference;
                    }
                }

                if (data.Reference.Target == null)
                {
                    if (data.Reference.Id.identifierType != 0)
                    {
                        if (GUILayout.Button(data.Reference.Id.ToString(), Sirenix.Utilities.Editor.SirenixGUIStyles.ToolbarButton))
                        {
                            var reference = data.Reference;
                            reference.Target = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(reference.Id) as AudioClip;
                            data.Reference = reference;
                        }
                    }
                    else
                    {
                        GUILayout.Label(data.Reference.Id.ToString(), Sirenix.Utilities.Editor.SirenixGUIStyles.Label);
                    }
                }

                var hash = data.GetHashCode();
                var selected = SelectedDatas.Contains(hash);

                if (selected)
                    ColorCache.SetColor();

                if (GUILayout.Button("Select"))
                {
                    if (selected)
                        SelectedDatas.Remove(data.GetHashCode());
                    else
                        SelectedDatas.Add(data.GetHashCode());
                }

                if (selected)
                    ColorCache.Reset();

                if (GUILayout.Button("Preview"))
                {
                    InspectIndex = Datas.IndexOf(data);
                    Load();
                }

                SirenixEditorGUI.EndHorizontalPropertyLayout();
            }
        }

        #endregion

        #region Preview

        [TabGroup(cst_tab, "Preview")]
        [OnValueChanged(nameof(UpdatePreview))]
        [DelayedProperty]
        [SerializeField]
        int m_inspectIndex;

        [TabGroup(cst_tab, "Preview")]
        public bool previewNextLoad = true;

        protected virtual void UpdatePreview()
        {
            InspectIndex = Datas.Loop(InspectIndex);
            Load();
        }

        protected int LoadingHash;

        [TabGroup(cst_tab, "Preview")]
        [ProgressBar(0f, 1f)]
        [SerializeField]
        float Progress;

        [TabGroup(cst_tab, "Preview")]
        [SerializeField]
        AudioClip m_clip;

        public int InspectIndex { get => m_inspectIndex; set => m_inspectIndex = value; }
        public AudioClip Clip { get => m_clip; set => m_clip = value; }

        //[TabGroup(cst_tab, "Preview")]
        //[OnInspectorGUI]
        [ToDo("custom audio inspector")]
        protected virtual void DrawPreviewClip()
        {
            //SirenixEditorFields.UnityPreviewObjectField(GUIContent.none, Clip, typeof(AudioClip), true,45,Sirenix.Utilities.Editor.ObjectFieldAlignment.Center);
            //Sirenix.Utilities.Editor.GUIHelper.
            return;
            var rect = SirenixEditorGUI.BeginBox(GUILayout.ExpandWidth(true), GUILayout.Height(280));
            rect.height = 180;
            Clip = SirenixEditorFields.PreviewObjectField<AudioClip>(rect, Clip);
            SirenixEditorGUI.EndBox();
        }

        [TabGroup(cst_tab, "Preview")]
        [Button(ButtonSizes.Medium)]
        public virtual void Load()
        {
            if (Datas.IsNullOrEmpty() || Datas.Count <= InspectIndex || InspectIndex < 0)
                return;

            var data = Datas[InspectIndex];
            Loading = Load(data.FilePath);
        }

        [HorizontalGroup(cst_tab_preview + "Control")]
        [Button(ButtonSizes.Medium)]
        public virtual void Last()
        {
            InspectIndex = Datas.Loop(--InspectIndex);
            Load();
        }

        [HorizontalGroup(cst_tab_preview + "Control")]
        [Button(ButtonSizes.Medium)]
        public virtual void Preview()
        {
            if (Clip == null)
                return;

            ClipUtil.StopAllPreviewClips();
            ClipUtil.PlayPreviewClip(Clip);
        }

        [HorizontalGroup(cst_tab_preview + "Control")]
        [Button(ButtonSizes.Medium)]
        public virtual void Next()
        {
            InspectIndex = Datas.Loop(++InspectIndex);
            Load();
        }

        protected virtual Color GetSelectedColor => (Datas.IsNullOrEmpty() || Datas.Count <= InspectIndex || InspectIndex < 0) || SelectedDatas.IsNullOrEmpty() || !SelectedDatas.Contains(Datas[InspectIndex].GetHashCode()) ? Color.white : Color.green;

        [TabGroup(cst_tab, "Preview")]
        [GUIColor(nameof(GetSelectedColor))]
        [Button(ButtonSizes.Medium)]
        protected virtual void Select()
        {
            InspectIndex = Datas.Loop(InspectIndex);
            if (InspectIndex < 0)
                return;

            var hash = Datas[InspectIndex].GetHashCode();
            var selected = SelectedDatas.Contains(hash);

            if (selected)
                SelectedDatas.Remove(hash);
            else
                SelectedDatas.Add(hash);
        }

        protected IEnumerator Loading;

        protected virtual AudioType GetAudioType(string filePath)
        {
            var extension = Path.GetExtension(filePath);

            if (extension.FastEndsWith("mp3"))
                return AudioType.MPEG;

            if (extension.FastEndsWith("wav"))
                return AudioType.WAV;

            if (extension.FastEndsWith("ogg"))
                return AudioType.OGGVORBIS;

            throw new NotImplementedException($"Not support type [{extension}].");
        }

        protected virtual IEnumerator Load(string filePath)
        {
            var hash = filePath.GetHashCode();
            LoadingHash = hash;

            DisposeClip();

            var uri = new Uri(filePath);
            var www = UnityWebRequestMultimedia.GetAudioClip(uri, GetAudioType(filePath));

            var request = www.SendWebRequest();

            while (!request.isDone)
            {
                if (LoadingHash != hash)
                    request.webRequest.Abort();

                Progress = request.progress;
                yield return null;
            }

            if (www.error != null)
            {
                Debug.LogError("Error loading audio file: " + www.error);
            }
            else
            {
                Clip = DownloadHandlerAudioClip.GetContent(www);
                Clip.name = Path.GetFileNameWithoutExtension(filePath);
            }

            Loading = null;

            if (previewNextLoad)
                Preview();

            this.Repaint();
        }

        public virtual void DisposeClip()
        {
            if (Clip != null && !Clip.IsPersistentAsset())
            {
                var clip = Clip;
                Clip = null;
                clip.Destroy();
            }
        }

        #endregion

        #region Import

        [TabGroup(cst_tab, "Archive")]
        //[FolderPath(AbsolutePath = false, RequireExistingPath = true)]
        [SerializeField]
        FolderPathFinder m_archiveFolder = new($"{nameof(AudioLibraryWindow)}_archive", EditorPathUtility.cst_TempArchive);
        [TabGroup(cst_tab, "Archive")]
        [SerializeField]
        string m_dummyFolderPath;

        [TabGroup(cst_tab, "Archive")]
        [SerializeField]
        bool m_forceToMono = true;

        [BoxGroup(cst_tab_archive + "Config")]
        [Tooltip("Register import info to bundle.")]
        [SerializeField,HideInInspector]
        ExternalAudioBundle m_externalAudioBundle;

        [BoxGroup(cst_tab_archive + "Config")]
        [OnInspectorGUI]
        protected virtual void DrawExernalBundleGUI()
        {
            {
                SirenixEditorGUI.BeginIndentedHorizontal();
                {
                    EditorGUI.BeginChangeCheck();
                    var newTarget = SirenixEditorFields.UnityObjectField("External Bundle", ExternalAudioBundle, typeof(ExternalAudioBundle), false);
                    if (EditorGUI.EndChangeCheck())
                        ExternalAudioBundle = newTarget as ExternalAudioBundle;
                }

                if(ExternalAudioBundle==null)
                    if(GUILayout.Button("Create",SirenixGUIStyles.ButtonRight))
                    {
                        ExternalAudioBundle = EditorAssetsUtility.CreateInstanceSO<ExternalAudioBundle>(true, ArchiveFolderPath);
                    }
                SirenixEditorGUI.EndIndentedHorizontal();
            }
        }

        [HideLabel]
        [BoxGroup(cst_tab_archive + "Config")]
        [SerializeField]
        HashSet<AudioImportConfig> m_configs = new();

        protected Dictionary<ExternalAudioInfo, AudioImportConfig> dic_customImportOption = new();
        
        public FolderPathFinder ArchiveFolderPath { get => m_archiveFolder; set => m_archiveFolder = value; }
        public string DummyFolderPath { get => m_dummyFolderPath; set => m_dummyFolderPath = value; }
        public bool ForceToMono { get => m_forceToMono; set => m_forceToMono = value; }
        /// <summary>
        /// Register import info to bundle.
        /// </summary>
        public ExternalAudioBundle ExternalAudioBundle { get => m_externalAudioBundle; set => m_externalAudioBundle = value; }
        public HashSet<AudioImportConfig> Configs { get => m_configs; set => m_configs = value; }

        [PropertyOrder(1)]
        [TabGroup(cst_tab, "Archive")]
        [Button(ButtonSizes.Medium)]
        public virtual void Archive()
        {
            return;

            if (Clip == null || Clip.IsPersistentAsset())
                return;

            var data = Datas[InspectIndex];
            var archivePath = Path.Combine(ArchiveFolderPath, data.Name);
            Clip = EditorAssetsUtility.Archive(Clip, Clip.name, false, ArchiveFolderPath);
        }



        [TabGroup(cst_tab, "Archive")]
        [Button(ButtonSizes.Medium)]
        public virtual void ArchiveSelected()
        {
            if (SelectedDatas.IsNullOrEmpty() || Datas.IsNullOrEmpty())
            {
                Debug.LogException(new ArgumentNullException($"Selected audio files is empty."));
                return;
            }

            var targets = Datas.Where(x => SelectedDatas.Contains(x.GetHashCode())).ToArray();
            var length = targets.Length;
            var assetPaths = new string[length];

            try
            {
                using (var freezeDatabase = EditorAssetsUtility.LockRefresh())
                {
                    var hasDummyPath = !string.IsNullOrEmpty(DummyFolderPath);
                    int dummyIndex = DummyFolderPath.Length+1;
                    var validDatas = new HashSet<ExternalAudioInfo>(length);

                    for (int i = 0; i < length; i++)
                    {
                        var data = targets[i];
                        try
                        {
                            EditorUtility.DisplayProgressBar("Import", $"Copy audio files [{data.Name}]..", ((float)i) / length);

                            // chose dst folder
                            var dummyFolders = hasDummyPath? Path.GetDirectoryName(data.FilePath)[dummyIndex..] : string.Empty;
                            //Debug.Log($"[{dummyFolders}]/[{DummyFolderPath}]");

                            // check folder
                            var dst_folderPath = Path.Combine(ArchiveFolderPath, dummyFolders);
                            if (!Directory.Exists(dst_folderPath))
                                Directory.CreateDirectory(dst_folderPath);

                            // check file
                            var archivePath = Path.Combine(dst_folderPath, data.Name);
                            if (File.Exists(EditorPathUtility.GetIOPath(archivePath)))
                            {
                                Debug.LogException(new FileLoadException($"Dst file already exist [{archivePath}], importing will ignore this file."), AssetDatabase.LoadAssetAtPath<Object>(archivePath));
                                continue;
                            }

                            assetPaths[i] = EditorPathUtility.GetAssetPath(archivePath);

                            // copy source file
                            FileUtil.CopyFileOrDirectory(data.FilePath, archivePath);
                            // copy meta file
                            var metaPath = Path.ChangeExtension(data.FilePath, ".meta");
                            if (File.Exists(metaPath))
                                FileUtil.CopyFileOrDirectory(metaPath, Path.ChangeExtension(archivePath, ".meta"));
                        }
                        catch (Exception e)
                        {
                            Debug.LogException(e);
                            continue;
                        }

                        validDatas.Add(data);
                    }

                    // register external bundle
                    if (ExternalAudioBundle != null)
                    {
                        ExternalAudioBundle.Infos = ExternalAudioBundle.Infos.Concat(validDatas).ToArray();
                    }
                }

                AssetDatabase.Refresh();


                //using (var freezeDatabase = EditorAssetsUtility.LockRefresh())
                {
                    for (int i = 0; i < length; i++)
                    {
                        if (!File.Exists(EditorPathUtility.GetIOPath(assetPaths[i])))
                        {
                            Debug.LogException(new FileNotFoundException($"Failure to find copy file at [{assetPaths[i]}]."));
                            continue;
                        }

                        var data = targets[i];
                        try
                        {
                            // import with config
                            AudioImporter importer = AudioImporter.GetAtPath(assetPaths[i]) as AudioImporter;

                            if (importer.importSettingsMissing)
                            {
                                Debug.LogError($"[{assetPaths[i]}]importSettingsMissing");
                            }

                            importer.forceToMono = ForceToMono;

                            if (dic_customImportOption.TryGetValue(data, out var customConfig))
                            {
                                if (customConfig.Paltform == "Default")
                                {
                                    importer.defaultSampleSettings = customConfig;
                                }
                                else
                                {
                                    importer.SetOverrideSampleSettings(customConfig.Paltform, customConfig);
                                }
                            }
                            else
                            {
                                foreach (var config in Configs)
                                {
                                    if (config.Paltform == "Default")
                                    {
                                        importer.defaultSampleSettings = config;
                                    }
                                    else
                                    {
                                        importer.SetOverrideSampleSettings(config.Paltform, config);
                                        //importer.SetOverrideSampleSettings("iPhone", config);
                                        //importer.SetOverrideSampleSettings("Android", config);
                                    }

                                    //config.quality = importer.defaultSampleSettings.quality;
                                    //config.sampleRateSetting = importer.defaultSampleSettings.sampleRateSetting;
                                    //config.sampleRateOverride = importer.defaultSampleSettings.sampleRateOverride;
                                    //config.conversionMode = importer.defaultSampleSettings.conversionMode;
                                }
                            }
                            

                            // import setting
                            {
                                EditorApplication.delayCall += () =>
                                {
                                    try
                                    {
                                        importer.SaveAndReimport();
                                        data.Reference = new AssetReference<AudioClip>(AssetDatabase.LoadAssetAtPath<AudioClip>(importer.assetPath));
                                        var metaPath = EditorPathUtility.GetMetaPath(importer.assetPath);
                                        var sourceMetaPath = Path.Combine(Path.GetDirectoryName(data.FilePath), Path.GetFileName(metaPath));
                                        if (!File.Exists(sourceMetaPath) && File.Exists(metaPath))
                                            FileUtil.CopyFileOrDirectory(metaPath, sourceMetaPath);
                                    }
                                    catch (Exception e)
                                    {
                                        Debug.LogException(e);
                                    }
                                };
                            }
                            EditorUtility.DisplayProgressBar("Import", $"Initailize audio clips [{data.Name}]..", ((float)i) / length);
                        }
                        catch (Exception e)
                        {
                            Debug.LogException(e);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.Refresh();
            }
        }

        [TabGroup(cst_tab, "Archive")]
        [Button(ButtonSizes.Medium)]
        public virtual void ArchiveBundle()
        {
            var bundle = ScriptableObject.CreateInstance<ExternalAudioBundle>();
            var targets = Datas.Where(x => SelectedDatas.Contains(x.GetHashCode())).ToArray();
            bundle.Infos = targets;
            EditorAssetsUtility.Archive(bundle, $"{nameof(ExternalAudioBundle)}_{EditorPathUtility.GetAssetName(ArchiveFolderPath)}",true,ArchiveFolderPath);
        }

        #endregion
    }



}
