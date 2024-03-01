using UnityEngine;
using UnityEditor;
using Sirenix.Utilities.Editor;
using Sirenix.Utilities;
using Sirenix.OdinInspector.Editor;
using Sirenix.OdinInspector;

namespace Return.Editors.Plugin.PushPin
{
    /// <summary>
    /// Pushpin window using to quick access user preference.
    /// </summary>
    public class PersistentWindow : PushPinWindow
    {
        [MenuItem(MenuUtil.Window_Plugin+ "PushPin/Persistent")]
        static void CreateWindow()
        {
            var window = EditorWindow.GetWindow<PersistentWindow>();
            window.position = GUIHelper.GetEditorWindowRect().AlignCenter(220, 400);
            
        }

        protected override void OnEnable()
        {
            base.OnEnable();

            var icon = SdfIcons.CreateTransparentIconTexture(SdfIconType.BookmarkStar, Color.white, 25, 25, 0);
            titleContent = new GUIContent("Persistent", icon);
        }
    }


}