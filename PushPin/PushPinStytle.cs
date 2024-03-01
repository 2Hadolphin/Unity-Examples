using UnityEngine;
using UnityEditor;
using Object = UnityEngine.Object;

namespace Return.Editors.Plugin.PushPin
{
    public static class PushPinStytle
    {
        internal static readonly int ITEM_SIZE = 32, PADDING = 1, ITEM_PADDED = 33;
        static GUIStyle styleItem, styleItemUnavailable, styleItemSelected, styleHint, styleItemHeightLight;

        public static GUIStyle StyleItemUnavailable
        {
            get
            {
                if (styleItemUnavailable == null)
                {
                    styleItemUnavailable = new GUIStyle(GUI.skin.button);
                    styleItemUnavailable.normal.textColor = Color.grey;
                    styleItemUnavailable.alignment = TextAnchor.MiddleLeft;
                    styleItemUnavailable.fontStyle = FontStyle.Italic;
                    styleItemUnavailable.fontSize = 14;

                }
                return styleItemUnavailable;
            }
        }

        public static GUIStyle StyleItemSelected
        {
            get
            {
                if (styleItemSelected == null)
                {
                    styleItemSelected = new GUIStyle(GUI.skin.button);
                    styleItemSelected.normal = styleItemSelected.active;
                    styleItemSelected.alignment = TextAnchor.MiddleLeft;
                    styleItemSelected.fontStyle = FontStyle.Bold;
                    styleItemSelected.fontSize = 16;
                    styleItemSelected.richText = true;

                }
                return styleItemSelected;
            }
        }


        public static GUIStyle StyleItemHeightlight
        {
            get
            {
                if (styleItemHeightLight == null)
                {
                    styleItemHeightLight = new GUIStyle(GUI.skin.button);
                    styleItemHeightLight.normal.textColor = Color.green;
                    styleItemHeightLight.alignment = TextAnchor.MiddleLeft;
                    styleItemHeightLight.fontStyle = FontStyle.Bold;
                    styleItemHeightLight.fontSize = 16;
                    styleItemHeightLight.richText = true;

                }
                return styleItemHeightLight;
            }
        }

        public static GUIStyle StyleHint
        {
            get
            {
                if (styleHint == null)
                {
                    styleHint = new GUIStyle(GUI.skin.label);
                    styleHint.normal.textColor = Color.grey;
                    styleHint.alignment = TextAnchor.MiddleCenter;
                    styleHint.fontSize = 12;
                }
                return styleHint;
            }
        }

        public static GUIStyle StyleItem
        {
            get
            {
                if (styleItem == null)
                {
                    styleItem = new GUIStyle(GUI.skin.button);
                    styleItem.alignment = TextAnchor.MiddleLeft;
                    styleItem.fontSize = 16;
                    styleItem.richText = true;
                }
                return styleItem;
            }
        }
    }


}