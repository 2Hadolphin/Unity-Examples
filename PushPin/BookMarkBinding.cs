using UnityEngine;
using System;
using Object = UnityEngine.Object;
using Sirenix.OdinValidator;

namespace Return.Editors.Plugin.PushPin
{
    /// <summary>
    /// Storage asset reference and cache GUI elements.
    /// </summary>
    [Serializable]
    public class BookMarkBinding
    {
        [SerializeField]
        Object m_bindingAsset;

        /// <summary>
        /// Whether GUI elements been initialized.
        /// </summary>
        [NonSerialized]
        public bool initialized;
        [NonSerialized]
        public GUIContent icon;
        [NonSerialized]
        public GUIContent content;
        [NonSerialized]
        public GUIStyle style;

        /// <summary>
        /// Archived asset.
        /// </summary>
        public Object BindingAsset { get => m_bindingAsset; set => m_bindingAsset = value; }

        public override bool Equals(object obj)
        {
            if(obj is BookMarkBinding binding)
                return binding.BindingAsset == this.BindingAsset;

            if (obj is not Object asset)
                return false;

            return BindingAsset == asset;
        }

        public override int GetHashCode()
        {
            return BindingAsset.GetHashCode();
        }
    }


}