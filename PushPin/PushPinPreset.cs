using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;
using System;
using UnityEditor;
using Sirenix.Serialization;
using System.Linq;
using Object = UnityEngine.Object;
using Sirenix.Utilities.Editor;
using Sirenix.Utilities;

namespace Return.Editors.Plugin.PushPin
{
    /// <summary>
    /// Archive push pin contents. 
    /// </summary>
    public class PushPinPreset : SerializedScriptableObject
    {
        DropZone dropZone;

        private void OnEnable()
        {
            if (dropZone == null)
                dropZone = new DropZone(OnDropZoneAddAsset,true,true);
        }

        void OnDropZoneAddAsset(Object obj)
        {
            var bookMark = new BookMarkBinding()
            {
                BindingAsset = obj,
            };

            Bindings.CheckAdd(bookMark);
            OnBindingUpdate();
        }

        void OnBindingUpdate()
        {
            CustomMenuChanged?.Invoke();
            this.Dirty();
        }

        public event Action CustomMenuChanged;

        [SerializeField]
        bool m_enabledScrollView = true;

        [SerializeField]
        bool m_deleteConfirm = true;

        [SerializeField]
        int m_capacity = 10;

        [Tooltip("Menu Font Size")]
        [SerializeField]
        int m_fontSize = 23;

        [Tooltip("Menu Line Height")]
        [SerializeField]
        float m_rectHeight = 47;

        [Tooltip("Menu Line Gap")]
        [SerializeField]
        float m_rectGap = 2.3f;

        public int FontSize { get => m_fontSize; set => m_fontSize = value; }
        public float RectHeight { get => m_rectHeight; set => m_rectHeight = value; }


        [OnValueChanged(nameof(OnBindingUpdate))]
        [ListDrawerSettings(DraggableItems =true,ShowFoldout =false)]
        [SerializeField]
        List<BookMarkBinding> m_bindings;

        /// <summary>
        /// Archived asset bindings.
        /// </summary>
        public List<BookMarkBinding> Bindings 
        {
            get
            {
                if (m_bindings == null)
                    m_bindings = new(Capacity);

                return m_bindings;
            }
            set => m_bindings = value; 
        }

        /// <summary>
        /// Bookmark capacity.
        /// </summary>
        public int Capacity { get => m_capacity; set => m_capacity = value; }
        /// <summary>
        /// Bookmark inspect element gap.
        /// </summary>
        public float RectGap { get => m_rectGap; set => m_rectGap = value; }
        /// <summary>
        /// Whether delete bookmark before confirm.
        /// </summary>
        public bool DeleteConfirm { get => m_deleteConfirm; set => m_deleteConfirm = value; }
        /// <summary>
        /// Whether enable window scroll.
        /// </summary>
        public bool EnabledScrollView { get => m_enabledScrollView; set => m_enabledScrollView = value; }
    }


}