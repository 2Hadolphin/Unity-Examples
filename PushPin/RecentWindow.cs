using UnityEngine;
using UnityEditor;
using Sirenix.Utilities.Editor;
using Sirenix.Utilities;
using Sirenix.OdinInspector.Editor;
using Sirenix.OdinInspector;
using UnityEngine.Assertions;

namespace Return.Editors.Plugin.PushPin
{
    /// <summary>
    /// Pushpin window but recording <see cref="Selection"/> operation.
    /// </summary>
    public class RecentWindow : PushPinWindow
    {
        [MenuItem(MenuUtil.Window_Plugin + "PushPin/Recent")]
        static void CreateWindow()
        {
            var window = EditorWindow.GetWindow<RecentWindow>();
            window.position = GUIHelper.GetEditorWindowRect().AlignCenter(220, 400);

        }

        protected override void OnEnable()
        {
            isUseDrop = false;

            base.OnEnable();

            var icon = SdfIcons.CreateTransparentIconTexture(SdfIconType.ClockHistory, Color.white, 25, 25, 0);
            titleContent = new GUIContent("Recent", icon);           
        }


        public override void OpenBinding(BookMarkBinding binding)
        {
            ignoreNextSelection = true;

            base.OpenBinding(binding);
        }

        /// <summary>
        /// Pass next selection changed after open binding.
        /// </summary>
        bool ignoreNextSelection;

        protected override void OnSelectionChange()
        {
            base.OnSelectionChange();

            if (ignoreNextSelection)
            {
                ignoreNextSelection = false;
                return;
            }

            if (Selection.activeObject == null)
                return;

            if (Preset == null)
                return;

            var obj = Selection.activeObject;

            if (Preset.Bindings.Exists(x => x.BindingAsset == obj))
            {
                if (Preset.Bindings[0].BindingAsset == obj)
                    return;

                var existBinding = Preset.Bindings.Find(x => x.BindingAsset == obj);
                Assert.IsTrue(Preset.Bindings.Remove(existBinding));
                Preset.Bindings.Insert(0, existBinding);
                Preset.Dirty();
                return;
            }

            // valid capacity
            var capacity = Preset.Capacity;

            // dynamic extend
            {
                var maxCapacity = Mathf.FloorToInt(position.height / (Preset.RectHeight + Preset.RectGap))-2;
                if (maxCapacity > capacity)
                    capacity = maxCapacity;
            }
   

            if (Preset.Bindings.Count > capacity + 1)
            {
                var deleteCount = Preset.Bindings.Count + 1 - capacity;
                var index = Preset.Bindings.Count - 1;

                for (int i = 0; i < deleteCount && index - i > 0; i++)
                    Preset.Bindings.RemoveAt(index - i);
            }

            // insert binding
            var binding = new BookMarkBinding()
            {
                BindingAsset = obj
            };

            Preset.Bindings.Insert(0, binding);
            Preset.Dirty();
        }

        protected override void DrawInspector()
        {
            base.DrawInspector();
        }



    }


}