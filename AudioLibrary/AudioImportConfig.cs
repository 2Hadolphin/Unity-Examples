
using UnityEngine;
using Sirenix.OdinInspector;
using UnityEditor;

namespace Return.Editors
{
    /// <summary>
    /// Persistent audio import options.
    /// </summary>
    public class AudioImportConfig : SerializedScriptableObject
    {
        [SerializeField]
        string m_paltform;

        [SerializeField]
        AudioImporterSampleSettings m_setting;

        public AudioImporterSampleSettings Setting { get => m_setting; set => m_setting = value; }
        public string Paltform { get => m_paltform; set => m_paltform = value; }

        public override bool Equals(object other)
        {
            if(other is AudioImportConfig config)
                return Paltform == config.Paltform;

            return false;
        }

        public override int GetHashCode()
        {
            if (string.IsNullOrEmpty(m_paltform))
                return 0;

            return Paltform.GetHashCode();
        }

        public static implicit operator AudioImporterSampleSettings(AudioImportConfig config)
        {
            return config.Setting;
        }
    }
}
