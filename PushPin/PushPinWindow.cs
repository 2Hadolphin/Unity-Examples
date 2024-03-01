using UnityEngine;
using Sirenix.OdinInspector;
using UnityEditor;
using Object = UnityEngine.Object;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities.Editor;
using Sirenix.Utilities;
using System;
using System.Linq;

namespace Return.Editors.Plugin.PushPin
{
    /// <summary>
    /// Base Pushpin window.
    /// </summary>
    public abstract class PushPinWindow : OdinEditorWindow,IHasCustomMenu
    {
        public void AddItemsToMenu(GenericMenu menu)
        {
            menu.AddItem(EditorGUIUtility.TrTextContent("Edit Preset"), false, ShowPreset);
        }

        protected virtual void ShowPreset()
        {
            if (Preset != null)
            {
                Preset.OpenAsset();
                Debug.Log($"Open [{Preset}].", Preset);
            }
        }

        #region Routine

        protected override void OnEnable()
        {
            hotKeyBinding_Assist = new(nameof(PushPinWindow), KeyCode.LeftControl);

            base.OnEnable();

            // load presistent data
            {
                var typeName = GetType().Name;
                var defaultDataPath = MonoScript.FromScriptableObject(this).GetAssetFolderPath();
                m_preset = new(typeName, $"{defaultDataPath}/Binding_{typeName}") { };

                if(Preset!=null)
                    UseScrollView = Preset.EnabledScrollView;
            }

            //if (isUseDrop && !DragAndDrop.HasHandler(DragAndDropWindowTarget.inspector, (DragAndDrop.InspectorDropHandler)OnInspectorDrop))
            //    DragAndDrop.AddDropHandler(OnInspectorDrop);
        }

        protected override void OnDisable()
        {
            base.OnDisable();

            DragAndDrop.RemoveDropHandler(OnInspectorDrop);
        }

        /// <summary>
        /// Custom focus prevent unity bug.
        /// </summary>
        bool isFocus;

        private void OnFocus()
        {
            isFocus = true;
        }

        private void OnLostFocus()
        {
            isFocus = false;
            this.Repaint();
        }

        protected override void Initialize()
        {

        }



        #endregion
        /// <summary>
        /// Whether use editor drag and drop.
        /// </summary>
        protected bool isUseDrop = true;

        /// <summary>
        /// Editor asset finder, used to cache preset.
        /// </summary>
        [SerializeField, HideInInspector]
        AssetReferenceFinder<PushPinPreset> m_preset;

        /// <summary>
        /// Refer to pushpin data.
        /// </summary>
        public virtual PushPinPreset Preset
        {
            get => m_preset.Target;
            set
            {
                if (value != m_preset.Target)
                    OnDataUpdate();

                if (m_preset.Target != null)
                    m_preset.Target.CustomMenuChanged -= OnDataUpdate;

                if (value != null)
                {
                    value.CustomMenuChanged -= OnDataUpdate;
                    value.CustomMenuChanged += OnDataUpdate;
                }

                m_preset.Target = value;
            }
        }

        /// <summary>
        /// Initialize preset.
        /// </summary>
        protected virtual void LoadPreset() { }

        /// <summary>
        /// Cache editor asset dragging state.
        /// </summary>
        protected bool isAssetDraggin;

        /// <summary>
        /// Cache assist operation.
        /// </summary>
        protected bool isAssist;
        /// <summary>
        /// Cache dragging menu index for operation.
        /// </summary>
        protected int draggingIndex = -1;
        /// <summary>
        /// Cache menu rects for interaction.
        /// </summary>
        protected Rect[] cache_rects;

        /// <summary>
        /// Cache drop binding icon.
        /// </summary>
        [NonSerialized]
        static GUIContent cache_dropLabel;

        [NonSerialized]
        static GUIStyle style_dropZone_image;

        [NonSerialized]
        static GUIStyle style_dropZone_text;

        HotKeyBinding hotKeyBinding_Assist;

        /// <summary>
        /// Draw Main Inspector
        /// </summary>
        [OnInspectorGUI]
        protected virtual void DrawInspector()
        {
            // draw preset reference binding
            if (Preset == null)
            {
                EditorGUILayout.HelpBox($"Please select a pushpin preset.", MessageType.Info);
                Preset = EditorGUILayout.ObjectField(Preset, typeof(PushPinPreset), false) as PushPinPreset;
                return;
            }

            #region Control

            // record keyboard operation
            if (Event.current != null && Event.current.isKey)
            {
                if (Event.current.keyCode == KeyCode.LeftControl)
                    isAssist = Event.current.type == EventType.KeyDown;
            }
            // record mouse operation
            var clickIndex = -1;
            {
                // 1 => right click
                if (Event.current != null && Event.current.isMouse)
                    clickIndex = Event.current.button;

                // delete asset
                if (isAssist && clickIndex == 1)
                {
                    var selectedIndex = GetPointerMenuIndex();

                    if (clickIndex == 1 && Preset.Bindings.ValidSN(selectedIndex))
                    {
                        DeleteBinding(Preset.Bindings[selectedIndex]);
                        Event.current.Use();
                        EditorGUIUtility.ExitGUI();
                        OnDataUpdate();
                        return;
                    }
                }
            }
            
            // valid GUI dragging
            if (Event.current != null)
            {
                if (isAssist && Event.current.type == EventType.MouseDown)
                {
                    // clicked and none selected => record dragging index
                    if (clickIndex == 0 && draggingIndex < 0)
                    {
                        draggingIndex = GetPointerMenuIndex();
                        Event.current.Use();
                        //Debug.Log($"{EventType.MouseDown} : start dragging {draggingIndex}");
                    }
                }

                if (Event.current.type == EventType.MouseUp)
                {
                    // dragging => prcess drop
                    if (draggingIndex >= 0)
                    {
                        Event.current.Use();

                        if (Preset.Bindings.ValidSN(draggingIndex))
                        {
                            var targetIndex = GetPointerMenuIndex();

                            //Debug.Log($"{EventType.MouseUp} : end dragging, drop {draggingIndex} to {targetIndex}");

                            // out of bounds
                            if (targetIndex < 0)
                            {
                                var binding = Preset.Bindings[draggingIndex];
                                Preset.Bindings.RemoveAt(draggingIndex);

                                // check mouse direction of window
                                var overWindow = Event.current.mousePosition.y < position.center.y;

                                if (overWindow)
                                    Preset.Bindings.Insert(0, binding);
                                else
                                    Preset.Bindings.Add(binding);
                            }
                            // re-order preset list
                            else if (targetIndex != draggingIndex)
                            {
                                var rect = cache_rects[targetIndex];

                                var binding = Preset.Bindings[draggingIndex];
                                Preset.Bindings.RemoveAt(draggingIndex);

                                // check mouse direction of selected target
                                var overTarget = Event.current.mousePosition.y < rect.center.y;

                                // valid drop in front of threshold
                                if (!overTarget)
                                {
                                    var gap = Event.current.mousePosition.y - rect.center.y;
                                    if (gap * 0.5f < rect.height * 0.5f)
                                        overTarget = true;
                                }

                                var insertIndex = targetIndex;

                                // insert after target
                                if (!overTarget)
                                    insertIndex++;

                                // dragging is in front of target
                                if (draggingIndex < targetIndex)
                                    insertIndex--;

                                Preset.Bindings.Insert(insertIndex, binding);
                            }

                            Preset.Dirty();
                            OnDataUpdate();
                        }

                        draggingIndex = -1;

                        EditorGUIUtility.ExitGUI();
                        Repaint();
                        return;
                    }
                }
            }

            // sensor editor drag and drop asset
            isAssetDraggin = !DragAndDrop.objectReferences.IsNullOrEmpty();

            #endregion

            // GUI setup
            var height_element = Preset.RectHeight;
            var width_gap = ((position.height - EditorGUIUtility.singleLineHeight * 2) / height_element) - Preset.Bindings.Count > 0 ? 10 : 23;
            var width = EditorGUIUtil.GetCurrentWidth(width_gap, 1f);
            var option_button = GUILayout.Height(height_element);

            if (isAssetDraggin && isFocus)
            {
                // prepare GUI style
                {
                    if(style_dropZone_text == null)
                    {
                        style_dropZone_text = new GUIStyle(GUI.skin.label)
                        {
                            alignment = TextAnchor.LowerCenter,
                            imagePosition = ImagePosition.TextOnly,
                            fontSize = 30,
                            fontStyle = FontStyle.Bold
                        };
                    }

                    if (style_dropZone_image == null)
                    {
                        style_dropZone_image = new GUIStyle(GUI.skin.label)
                        {
                            alignment = TextAnchor.MiddleCenter,
                            imagePosition = ImagePosition.ImageOnly,
                        };
                    }

                    if (cache_dropLabel == null)
                    {
                        var icon = SdfIcons.CreateTransparentIconTexture(SdfIconType.BoxArrowInDown, Color.white, (int)width, (int)width, 5);
                        //Debug.Log(icon);
                        cache_dropLabel = new GUIContent("Drop Asset", icon, "Drop down assets to adding bookmarks.");
                    }
                }

                GUILayout.Label(GUIContent.none, GUILayout.Width(width), GUILayout.ExpandHeight(true));

                var rect = GUILayoutUtility.GetLastRect();
                var iconSize = Math.Min(width, rect.height) * 0.75f;
                var backgroundRect = rect.AlignCenter(iconSize, iconSize);

                // draw background
                {
                    GUI.Label(rect, GUIContent.none, GUI.skin.box);
                    backgroundRect.position -= new Vector2(0, Preset.RectHeight);
                    GUI.Label(backgroundRect, cache_dropLabel, style_dropZone_image);
                }

                // draw tooltip
                {
                    var textRect = new Rect(backgroundRect);
                    textRect = textRect.HorizontalPadding(-width*0.1f);
                    textRect.position += new Vector2(0, Preset.RectHeight * 1.3f);// +rect.height*0.05f);
                    GUI.Label(textRect, cache_dropLabel, style_dropZone_text);
                }

                var id = DragAndDropUtilities.GetDragAndDropId(rect);

                var drop = DragAndDropUtilities.DropZone<Object>(rect, null, false, id);

                if (drop != null)
                {
                    //Debug.Log($"Mouse Drop {drop}.");

                    Preset.Bindings.CheckAdd(CreateBinding(drop));
                    Preset.Dirty();
                    GUIUtility.ExitGUI();
                }

                return;
            }

            // draw empty
            if (LinqExtensions.IsNullOrEmpty(Preset.Bindings))
            {
                var style = new GUIStyle(GUI.skin.box);
                style.alignment = TextAnchor.MiddleCenter;
                GUILayout.Label("Empty", style, GUILayout.Width(width), GUILayout.ExpandHeight(true));
                return;
            }

            // initialize list
            {
                foreach (var binding in Preset.Bindings)
                {
                     if (binding.initialized)
                        continue;

                    SetupBinding(binding);
                }
            }


            // draw list
            {
                GUILayout.BeginVertical(GUILayout.Width(width));

                var iconSize = Preset.RectHeight * 0.95f;

                var option_label = GUILayout.Width(width);
                var offset = new RectOffset((int)iconSize + 5, 0, 0, 0);
                var length = Preset.Bindings.Count;

                if (cache_rects == null || cache_rects.Length != length)
                    cache_rects = new Rect[length];

                var gap = Preset.RectGap;

                for (int i = 0; i < length; i++)
                {
                    var binding = Preset.Bindings[i];

                    {
                        GUILayout.BeginHorizontal();

                        var rect = GUILayoutUtility.GetRect(width, Preset.RectHeight, option_label, option_button);
                        // cache for selection
                        if (Event.current.type == EventType.MouseMove || Event.current.type == EventType.Repaint)
                            cache_rects[i] = rect;

                        binding.style.padding = offset;

                        if (GUI.Button(rect, binding.content, binding.style))
                        {
                            OpenBinding(binding);
                            EditorGUIUtility.ExitGUI();
                            break;
                        }

                        var iconRect = new Rect(rect);
                        rect.width = iconSize;
                        GUI.Label(iconRect, binding.icon);

                        GUILayout.EndHorizontal();
                    }

                    if (length - i > 1)
                        GUILayout.Space(gap);
                }

                GUILayout.EndVertical();
            }

            // draw dragging target
            if (Event.current != null && (Event.current.type == EventType.MouseMove || Event.current.type == EventType.Repaint))
            {
                if (draggingIndex >= 0 && Preset.Bindings.ValidSN(draggingIndex) && cache_rects.ValidSN(draggingIndex))
                {
                    var mousePos = Event.current.mousePosition;
                    var rect = cache_rects[draggingIndex];
                    rect.position = mousePos - new Vector2(rect.height * 0.5f, rect.height * 0.5f);// + rect.size * 0.5f;

                    var binding = Preset.Bindings[draggingIndex];

                    GUI.Label(rect, binding.content, binding.style);
                    var iconRect = new Rect(rect);
                    rect.width = rect.height * 0.95f;
                    GUI.Label(iconRect, binding.icon);
                }
            }

        }

        /// <summary>
        /// Get menu index by pointer position.
        /// </summary>
        /// <returns>Invalid index = -1.</returns>
        protected virtual int GetPointerMenuIndex()
        {
            if (LinqExtensions.IsNullOrEmpty(cache_rects))
                return -1;

            var mouse = Event.current.mousePosition;
            for (int i = 0; i < cache_rects.Length; i++)
            {
                //Debug.Log($"{Event.current.type} Match mosue [{mouse}] with rect [{cache_rects[i]}].");
                if (cache_rects[i].Contains(mouse))
                {
                    return i;
                }
            }

            return -1;
        }


        /// <summary>
        /// Craete asset bookmark binding
        /// </summary>
        public BookMarkBinding CreateBinding(Object obj)
        {
            var binding = new BookMarkBinding()
            {
                BindingAsset = obj
            };

            SetupBinding(binding);

            return binding;
        }

        /// <summary>
        /// Setup GUI options of <see cref="BookMarkBinding"/>.
        /// </summary>
        /// <param name="binding"></param>
        public void SetupBinding(BookMarkBinding binding)
        {
            GUIStyle style = new(GUI.skin.button)
            {
                fontSize = Preset.FontSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,

            };

            var obj = binding.BindingAsset;

            if (obj == null)
            {
                binding.content = new GUIContent("Missing Reference");
                var hiehgt = (int)Preset.RectHeight;
                var icon = SdfIcons.CreateTransparentIconTexture(SdfIconType.Trash,Color.white, hiehgt, hiehgt, 0);
                binding.icon = new GUIContent(icon);
            }
            else
            {
                var content = EditorGUIUtil.GetAssetContent(obj);
                content.text = obj.name;
                content.tooltip = obj.GetAssetPath();

                binding.icon = new GUIContent(content.image, obj.GetType().ToString());
                content.image = null;

                binding.content = content;
            }

            binding.style = style;
            binding.initialized = true;
        }




        /// <summary>
        /// Oepn folder via menu binding
        /// </summary>
        public virtual void OpenBinding(BookMarkBinding binding)
        {
            if (binding == null || binding.BindingAsset == null)
            {
                Debug.LogException(new NullReferenceException($"Missing binding reference {binding?.content?.text}."),Preset);
                return;
            }

            bool hasFocus = Selection.activeObject == binding.BindingAsset;

            if (hasFocus)
                binding.BindingAsset.OpenAsset();
            else
            {
                Selection.activeObject = binding.BindingAsset;
                EditorGUIUtility.PingObject(binding.BindingAsset);
            }

            GUIUtility.ExitGUI();
        }

        /// <summary>
        /// Delete folder menu.
        /// </summary>
        public virtual void DeleteBinding(BookMarkBinding binding)
        {
            if (binding == null)
            {
                Debug.LogException(new NullReferenceException($"Missing binding reference {binding?.content?.text}."));
                return;
            }
           
            if (Preset.DeleteConfirm)
                if (!EditorUtility.DisplayDialog("Confirm Operation", $"Delete [{binding?.content.text}] menu?", "Yes.", "No."))
                    return;
     
            Preset.Bindings.Remove(binding);
            Preset.Dirty();

            OnDataUpdate();
            GUIUtility.ExitGUI();
        }


        /// <summary>
        /// Invoke while persistent data update.
        /// </summary>
        protected virtual void OnDataUpdate()
        {
            if(Preset != null && !LinqExtensions.IsNullOrEmpty(Preset.Bindings))
                foreach (var binding in Preset.Bindings)
                {
                    if (binding.initialized)
                        continue;

                    SetupBinding(binding);
                }
        }

        protected virtual void OnSelectionChange()
        {
            if(isAssetDraggin && DragAndDrop.objectReferences.IsNullOrEmpty())
            {
                isAssetDraggin = false;
                UpdateEditors();
            }

        }

        protected virtual DragAndDropVisualMode OnInspectorDrop(Object[] targets, bool perform)
        {
            if (perform)
            {
                Debug.Log(string.Join("\n", targets.Select(x => x.name)));
                return DragAndDropVisualMode.Link;
            }

            return DragAndDropVisualMode.None;
        }
    }


}