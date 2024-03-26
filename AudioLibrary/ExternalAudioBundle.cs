using System.Linq;
using System.IO;

using UnityEngine;
using UnityEditor;

using Sirenix.OdinInspector;

namespace Return.Editors
{
    /// <summary>
    /// Archive external audio file import infos.
    /// </summary>
    public class ExternalAudioBundle : ExternalAssetBundle<AudioClip>
    {
        [ListDrawerSettings(NumberOfItemsPerPage = 10)]
        [SerializeField]
        ExternalAudioInfo[] m_infos;

        public ExternalAudioInfo[] Infos { get => m_infos; set => m_infos = value; }

        public override void SearchFiles()
        {
            var folderPath = EditorPathUtility.GetAssetFolderPath(this);
            if (!EditorPathUtility.IsValidFolder(folderPath))
                return;

            var dic_infos = Infos.ToDictionary(x => Path.GetFileNameWithoutExtension(x.Name), x => x);
            var audios = EditorAssetsUtility.GetAllAssetsAtPath<AudioClip>(folderPath, true);

            foreach (var audio in audios)
            {
                if (dic_infos.TryGetValue(audio.name, out var info))
                {
                    var refer = info.Reference;
                    refer.Target = audio;
                    info.Reference = refer;
                }
            }

            this.Dirty();
        }

        public override void RegisterDiskMeta()
        {
            using (var assetLock = AssetEditingLock.Use())
            {
                foreach (var info in Infos)
                {
                    if (info.Reference.Target == null)
                        continue;

                    var metaPath = EditorPathUtility.GetMetaPath(info.Reference.Target);
                    var dstPath = Path.ChangeExtension(info.FilePath, ".meta");

                    if (File.Exists(dstPath))
                        continue;

                    FileUtil.CopyFileOrDirectory(metaPath, dstPath);
                }
            }
        }

        protected override void OnAfterDeserialize()
        {
            Debug.Log(nameof(OnAfterDeserialize));

            foreach (var info in Infos)
            {
                if (info == null)
                    continue;

                if (info.Reference.Id.assetGUID == default || info.Reference.Target != null)
                    continue;

                var refer = info.Reference;
                refer.TryLoadReference();
                info.Reference = refer;
            }
        }
    }

}
