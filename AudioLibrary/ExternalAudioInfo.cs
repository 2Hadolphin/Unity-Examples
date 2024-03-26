using System;

using UnityEngine;

namespace Return.Editors
{
    [Serializable]
    public class ExternalAudioInfo : ExternalAssetInfo<AudioClip>
    {
        public ExternalAudioInfo(string path) : base(path)
        {

        }
    }
}
