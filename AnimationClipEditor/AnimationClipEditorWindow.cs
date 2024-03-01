using Microsoft.Win32;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities.Editor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Unity.Collections;
using Unity.Profiling;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Assertions;
using UnityEngine.Playables;
using static UnityEditor.AnimationUtility;
using Object = UnityEngine.Object;

namespace Return.Editors
{
    [PropertyTooltip("[ToDo] Record Additve Clip")]
    /// <summary>
    /// Recording animation window.
    /// </summary>
    public class AnimationClipEditorWindow : OdinEditorWindow, IDisposable
    {
        [MenuItem(MenuUtil.Tool_Asset_Animation + "Clip Editor")]
        static void OpenWindow()
        {
            var window = GetWindow<AnimationClipEditorWindow>();
            window.position = GUIHelper.GetEditorWindowRect().AlignCenter(Mathf.Max(minLayoutWidth_Property + minLayoutWidth_Curve + 50, 1100), 500);
        }

        protected static List<AnimationClipEditorWindow> instances = new(1);
        public static IEnumerable<AnimationClipEditorWindow> Instances => instances;

        static readonly ProfilerMarker profilerMarker = new("My code");

        bool ShowDropZone => Animator == null;
        [ShowIf(nameof(ShowDropZone))]
        [SerializeField]
        DropZone DropZone;

        #region Routine

        protected override void OnEnable()
        {
            AnimationUtility.onCurveWasModified -= OnCurveWasModified;
            AnimationUtility.onCurveWasModified += OnCurveWasModified;

            base.OnEnable();

            DropZone = new(SetGraphController, true, true) { customText = "Drop Skeleton", allowSceneObj = true };

            if (SearchField_properties == null)
                SearchField_properties = new();

            if (SearchField_bindings == null)
                SearchField_bindings = new();

            try
            {
                InitializeClipData(); // enable
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            listener_mouseDown = new KeyBoardListener(KeyCode.Mouse0);
            listener_multiSelection = new KeyBoardListener(KeyCode.LeftControl);
            listener_crossSelection = new KeyBoardListener(KeyCode.LeftShift);
            listener_delete = new KeyBoardListener(KeyCode.Delete);
            listener_delete.onKeyDown += DeleteSelectedKeyFrames;

            instances.CheckAdd(this);
        }

        protected override void OnDisable()
        {
            instances.Remove(this);
            base.OnDisable();
        }

        public virtual void Dispose()
        {
            dic_bindingDatas = null;

            TreeView_Properties = null;
            state_property = null;

            TreeView_bindings_skeleton = null;
            state_binding_skeleton = null;

            //Animator = null;

            if (muscleHandles.IsCreated)
                muscleHandles.Dispose();

            if (muscleValues.IsCreated)
                muscleValues.Dispose();
        }

        protected override void Initialize()
        {
            if (TimeTrack == null)
            {
                if (DummyClip != null)
                    TimeTrack = new(DummyClip);
                else
                    TimeTrack = new FrameTrack(1f, 30);
            }

            if (EnablePreview)
            {
                StartPreviewTarget();
                PreviewTarget(TimeTrack.Timeline.Time);
            }
        }

        protected virtual void OnSelectionChange()
        {
            if (Selection.activeObject is GameObject go)
            {
                Debug.Log($"{nameof(AnimationClipEditorWindow)}_{Selection.activeObject}");

                if (go.TryGetComponent<GraphDirector>(out var director))
                    InitializePreviewTarget(director);
                else if (go.TryGetComponent<Animator>(out var animator))
                    InitializePreviewTarget(animator);
            }
            else if (Selection.activeContext is Animator anim)
            {
                Debug.Log(anim);
            }
        }

        /// <summary>
        /// Whether during repaint phase.
        /// </summary>
        bool isRepaint;

        protected override void OnImGUI()
        {
            if (!hasInitializedGUI)
                InitializeGUI();

            isRepaint = Events.IsType(EventType.Repaint);

            base.OnImGUI();

            GUILayout.Space(10);
        }


        protected virtual void OnInspectorUpdate()
        {
            if (isRequiredUpdateKeyframeBar)
                UpdateKeyframeBarCache();
        }

        /// <summary>
        /// Draw window main GUI.
        /// </summary>
        [PropertySpace]
        [OnInspectorGUI]
        void DrawClipEditor()
        {
            //EditorGUI.DrawRect(position.SetYMin(100).SetHeight(10).SetXMin(0), Color.red);

            // assign layout options
            {
                if (isRepaint)
                {
                    var lastRect = GUILayoutUtility.GetLastRect();

                    // content offset
                    rect_content = new Rect(lastRect.xMin + 5, lastRect.yMax + 5, position.width - 20, position.height - lastRect.yMax - 20);

                    var width_left_layout = Mathf.Max(minLayoutWidth_Property, Mathf.Min(r_panelWidth, position.width - minLayoutWidth_Curve));
                    var width_right_layout = rect_content.width - width_left_layout;

                    // main tool bar
                    {
                        var controlHeight = 45f + 50;
                        var trackHeight = 60f;

                        rect_toolBar_top = rect_content;
                        rect_toolBar_top.height = controlHeight + trackHeight; // buttons + time track

                        // left panel
                        {
                            // file operation
                            rect_toolBar_general = rect_toolBar_top.SetWidth(width_left_layout);

                            // clip info

                        }

                        // right panel
                        {
                            // dopesheet menu bar
                            {
                                rect_toolBar_buttons = rect_toolBar_top.SetHeight(controlHeight);
                                rect_toolBar_buttons.xMin = rect_toolBar_top.xMin + width_left_layout;
                                rect_toolBar_buttons.xMax = rect_toolBar_top.xMax;
                            }

                            var width_recordingControl = controlHeight;

                            // right
                            {
                                // recording control
                                rect_recording_buttons = rect_toolBar_buttons.AlignRight(width_recordingControl);

                                // time control
                                rect_timeControl = rect_toolBar_buttons.SubXMax(width_recordingControl);
                            }

                            // time track
                            rect_timeTrack.xMin = rect_toolBar_top.xMin + width_left_layout;
                            rect_timeTrack.xMax = rect_toolBar_top.xMax;
                            rect_timeTrack.y = rect_toolBar_top.yMin + controlHeight;
                            rect_timeTrack.yMax = rect_toolBar_top.yMax;
                        }
                    }

                    // database
                    {
                        // property
                        rect_property_vision.xMin = rect_content.xMin;
                        rect_property_vision.yMin = rect_toolBar_top.yMax + height_bar_offset;
                        rect_property_vision.width = width_left_layout;
                        rect_property_vision.height = rect_content.height - rect_toolBar_top.height - height_bar_offset - height_toolbar_bottom * 2 - 10;

                        rect_property_content = rect_property_vision.SetPosition(Vector2.zero).AlignCenterX(rect_property_vision.width - 4);

                        // clip curves 
                        rect_curve.xMin = rect_property_vision.xMax;
                        rect_curve.yMin = rect_property_vision.yMin - height_bar_offset;
                        rect_curve.width = width_right_layout;
                        rect_curve.height = rect_property_vision.height + height_bar_offset;
                    }

                    // buttom bar
                    {
                        // left - 0
                        rect_property_toolbar = rect_property_vision.AlignBottomOut(height_toolbar_bottom, 5);

                        // left - 1
                        rect_curve_toolbar = rect_property_toolbar.AlignBottomOut(height_toolbar_bottom, 5);

                        // right - dopesheet toolkit bar
                        {
                            rect_toolBar_toolkit = rect_curve.
                                AlignBottomOut(rect_property_toolbar.height, height_bar_offset + 5).
                                AddYMax(rect_curve_toolbar.height - 5);
                        }
                    }

                    // control
                    {
                        var height = rect_curve.height * 0.25f;
                        rect_curve_control_scroll_vertical = new Rect(rect_curve);//.SubYMax(height);
                        rect_curve_control_scroll_horizontal = new Rect(rect_curve).AddYMin(height).SubYMax(height);
                    }
                }
                else if (Events.IsType(EventType.ScrollWheel))
                {
                    if (Events.PointerInRect(rect_curve_control_scroll_vertical))
                    {
                        if (Events.PointerInRect(rect_curve_control_scroll_horizontal))
                        {
                            isScrollHorizontal = true;
                        }
                        else
                        {
                            isScrollHorizontal = false;
                        }
                    }
                }
            }

            // dev - ??
            if (isRepaint)
            {
                EditorGUI.DrawRect(rect_curve_control_scroll_vertical, Color.green.SetAlpha(0.1f));
                EditorGUI.DrawRect(rect_curve_control_scroll_horizontal, Color.blue.SetAlpha(0.1f));

                //EditorGUI.DrawRect(rect_content, Color.green.SetAlpha(0.1f));
                EditorGUI.DrawRect(rect_toolBar_general, Color.yellow.SetAlpha(0.1f));
            }
            else
            {
                // Handle UI Events
                if (listener_mouseDown.CheckUpdate())
                    return;
                if (listener_multiSelection.CheckUpdate())
                    return;
                if (listener_crossSelection.CheckUpdate())
                    return;
                if (listener_delete.CheckUpdate())
                    return;
            }

            this.refresh = rect_content.Contains(Event.current.mousePosition);

            // draw control bar
            {
                // left
                {
                    DrawGeneralInfo(rect_toolBar_general);
                }

                // right 
                {
                    // draw recording control **top
                    DrawRecordingControl(rect_recording_buttons);

                    // draw timeline control **top
                    DrawTimelineControl(rect_timeControl);

                    // draw timeline track **down
                    DrawTimelineTrack(rect_timeTrack);
                }

                // buttom
                {
                    DrawToolkitBar(rect_toolBar_toolkit);
                }
            }

            // layout area
            DrawnResizeControl();

            if (Events.Finish())
                return;

            // draw backgrounds
            if (isRepaint)
            {
                EditorGUI.DrawRect(rect_property_vision, color_curve_background);
                EditorGUI.DrawRect(rect_curve, color_curve_background);
            }

            // property area 
            DrawPropertyPanel();

            // keyframe area
            DrawKeyFramesPanel();

            // dev GUI
            if (isRepaint)
            {
                // resize control
                //EditorGUI.DrawRect(rect_curve.AlignLeft(2f), Color.white.SetAlpha(0.7f));

                // layout - curve panel
                //EditorGUI.DrawRect(rect_curve.SetWidth(1f), Color.yellow);
                //EditorGUI.DrawRect(rect_curve.SetWidth(1f).AddX(offset_curvePanel_content*2), Color.yellow);

                // scroll
                //EditorGUI.DrawRect(rect_curve_control_scroll_vertical, Color.yellow.SetAlpha(0.2f));
                //EditorGUI.DrawRect(rect_curve_control_scroll_horizontal, Color.green.SetAlpha(0.2f));
            }
        }

        #endregion

        #region Initialization

        void SetGraphController(Object obj)
        {
            if (obj is GameObject go)
            {
                if (go.TryGetComponent<Animator>(out var _animator))
                    InitializePreviewTarget(_animator);
            }
            else if (obj is Animator animator)
                InitializePreviewTarget(animator);
            else if (obj is GraphDirector graphDirector)
                InitializePreviewTarget(graphDirector);

            // subscribe hierarchy event
            {
                EditorApplication.hierarchyChanged -= EditorApplication_hierarchyChanged;
                if (obj != null)
                    EditorApplication.hierarchyChanged += EditorApplication_hierarchyChanged;
            }

            if (obj is IGraphControl control)
            {

            }
        }

        void OnCurveWasModified(AnimationClip clip, EditorCurveBinding binding, CurveModifiedType type)
        {
            Debug.Log($"[{type}] [{clip.name}] {binding.propertyName}");
        }

        /// <summary>
        /// Initialize database for clip editor.
        /// </summary>
        public virtual void InitializeClipData()
        {
            var clip = DummyClip;

            if (clip == null)
            {
                ClipName = null;
                ClipFrameRate = 0;
                ClipLength = 0;
                QualifyGap = 0.5f / 30;

                if (TreeView_Properties != null)
                    TreeView_Properties = null;

                return;
            }

            // cache clip info
            {
                ClipName = clip.name;
                ClipFrameRate = clip.frameRate;
                ClipLength = clip.length;
                ClipFrameCount = ClipLength * ClipFrameRate + 1;
                QualifyGap = 0.5f / ClipFrameRate;
            }

            // setup time track
            SetupTimeTrack(clip);

            // treeview
            Setup_Property(clip);

            // initialize binding data
            SetBindingDatas(TreeView_Properties.BindingData_ClipSource);

            // cache GUI parameters
            UpdateKeyframeBarCache();
        }

        /// <summary>
        /// Setup time track by clip
        /// </summary>
        protected virtual void SetupTimeTrack(AnimationClip clip)
        {
            if (TimeTrack != null)
            {
                TimeTrack.onTick -= ForceRepaint;
                TimeTrack.Dispose();
            }

            TimeTrack = new FrameTrack(clip);
            TimeTrack.onTick += ForceRepaint;
            ClipFrameCount = TimeTrack.FrameCount + 1;

            UpdateKeyframeBarCache();
        }

        /// <summary>
        /// Setup time track by desire duration and framerate.
        /// </summary>
        protected virtual void SetupTimeTrack(float length, int frameRate)
        {
            if (TimeTrack != null)
            {
                TimeTrack.onTick -= ForceRepaint;
                TimeTrack.Dispose();
            }

            TimeTrack = new FrameTrack(length, frameRate);
            TimeTrack.onTick += ForceRepaint;
            ClipFrameCount = TimeTrack.FrameCount;

            UpdateKeyframeBarCache();
        }

        #endregion

        #region Input Control

        /// <summary>
        /// Event listener for mouse click.
        /// </summary>
        KeyBoardListener listener_mouseDown;

        /// <summary>
        /// Event listener for mouse click.
        /// </summary>
        KeyBoardListener listener_multiSelection;

        /// <summary>
        /// Event listener for mouse click.
        /// </summary>
        KeyBoardListener listener_crossSelection;

        /// <summary>
        /// Event listener for keyboard delete.
        /// </summary>
        KeyBoardListener listener_delete;
        #endregion

        #region Animation Clip

        [SerializeField, HideInInspector]
        AnimationClip m_inputClip;

        [SerializeField, HideInInspector]
        AnimationClip m_dummyClip;

        [SerializeField, HideInInspector]
        public string ClipName;
        [SerializeField, HideInInspector]
        public float ClipLength;
        [SerializeField, HideInInspector]
        public float ClipFrameRate;
        [SerializeField, HideInInspector]
        public float ClipFrameCount;

        [SerializeField, HideInInspector]
        private float m_qualifyGap;

        /// <summary>
        /// Time between two frames *0.5f, whcih used to match keyframe.
        /// </summary>
        protected float QualifyGap { get => m_qualifyGap; set => m_qualifyGap = value; }


        /// <summary>
        /// Sample clip, .
        /// </summary>
        public AnimationClip SourceClip { get => m_inputClip; set => m_inputClip = value; }
        /// <summary>
        /// Output clip, default preview clip.
        /// </summary>
        public AnimationClip DummyClip { get => m_dummyClip; set => m_dummyClip = value; }

        /// <summary>
        /// Cache curve data with binding datas.
        /// </summary>
        [HideInInspector, NonSerialized]
        public Dictionary<CurveBindingData, CurveData> dic_bindingDatas;

        #endregion

        #region Keyframe Operation(Curve Editor)

        /// <summary>
        /// Cache exist key frame data for batch key bar operation.
        /// </summary>
        protected bool[] cache_existKeyframes;

        protected bool isRequiredUpdateKeyframeBar;

        /// <summary>
        /// Execute this function for updating keyframe bar database after timetrack(<see cref="SetupTimeTrack"/>) and clip binding(<see cref="SetBindingDatas"/>) been setup.
        /// </summary>
        protected virtual void UpdateKeyframeBarCache()
        {
            isRequiredUpdateKeyframeBar = false;

            // fast refer key bar GUI
            if (cache_existKeyframes == null || cache_existKeyframes.Length != TimeTrack.FrameCount + 1)
                cache_existKeyframes = new bool[TimeTrack.FrameCount + 1];

            if (dic_bindingDatas.IsNullOrEmpty())
                return;

            for (int i = 0; i < cache_existKeyframes.Length; i++)
            {
                cache_existKeyframes[i] = dic_bindingDatas.Exist(pair => pair.Value.Keyframes.Exist(data => data.KeyframeIndex == i));
            }

            SelectedKeyFrameIndexes.Clear();
            SelectedKeyFrameIndexes.AddRange(SelectedKeyFrames.Select(x => x.KeyframeIndex));
        }


        #region Selection

        /// <summary>
        /// Cache selected key frame index.
        /// </summary>
        protected HashSet<int> SelectedKeyFrameIndexes = new(10);

        protected HashSet<CurveBindingData> SelectedBindingData = new(20);

        /// <summary>
        /// Cache selected key frames for batch operation.
        /// </summary>
        protected HashSet<KeyFrameData> SelectedKeyFrames = new(20);

        public virtual void AddSelectedKeyFrame(KeyFrameData data)
        {
            if (SelectedKeyFrames.Add(data))
            {
                SelectedBindingData.Add(data.RelativeData.BindingData);
                SelectedKeyFrameIndexes.Add(data.KeyframeIndex);
            }
        }

        public virtual void RemoveSelectedKeyFrame(KeyFrameData data)
        {
            if (SelectedKeyFrames.Remove(data))
            {
                SelectedBindingData.Remove(data.RelativeData.BindingData);
                SelectedKeyFrameIndexes.Remove(data.KeyframeIndex);
            }
        }

        public virtual void ClearSelectedKeyFrame()
        {
            SelectedKeyFrames.Clear();
            SelectedBindingData.Clear();
            SelectedKeyFrameIndexes.Clear();
        }

        public virtual void ToggleKeyframeSelection(KeyFrameData data)
        {
            if (listener_multiSelection.isKey)
            {
                if (SelectedKeyFrames.Contains(data))
                    RemoveSelectedKeyFrame(data);
                else
                    AddSelectedKeyFrame(data);
            }
            else
            {
                var selected = SelectedKeyFrames.Contains(data);

                if (SelectedKeyFrames.Count == 1)
                {
                    ClearSelectedKeyFrame();
                }
                else
                {
                    if (selected)
                        RemoveSelectedKeyFrame(data);
                    else// remove 
                        ClearSelectedKeyFrame();
                }

                if (!selected)
                    AddSelectedKeyFrame(data);
            }
        }

        public virtual void SelectKeyframes(int frameIndex)
        {
            SelectedKeyFrameIndexes.Add(frameIndex);

            var datas = dic_bindingDatas.Select(x => x.Value);
            foreach (var curveData in datas)
            {
                foreach (var keyframeData in curveData.Keyframes)
                {
                    if (keyframeData.KeyframeIndex != frameIndex)
                        continue;

                    AddSelectedKeyFrame(keyframeData);
                }
            }
        }

        public virtual void DeSelectKeyframes(int frameIndex)
        {
            if (SelectedKeyFrameIndexes.Remove(frameIndex))
            {
                var datas = dic_bindingDatas.Select(x => x.Value);
                foreach (var curveData in datas)
                {
                    foreach (var keyframeData in curveData.Keyframes)
                    {
                        if (keyframeData.KeyframeIndex != frameIndex)
                            continue;

                        RemoveSelectedKeyFrame(keyframeData);
                    }
                }
            }
        }

        #region Rect Selection

        [NonSerialized]
        RectSelection RectSelection;
        /// <summary>
        /// Whether rect selection performed.
        /// </summary>
        protected bool hasSelectedRect;
        protected Rect SelectedRect;

        List<KeyFrameData> temp_RectSelectionKeyframes = new(100);

        protected virtual void RectSelection_OnSelectionChange(Rect rect)
        {
            Debug.Log(nameof(RectSelection_OnSelectionChange) + rect.ToString());
            hasSelectedRect = true;
            SelectedRect = rect;
        }
        #endregion

        #endregion

        #region Check Key

        /// <summary>
        /// Whether key frame already exist in animation curve by time.
        /// </summary>
        public virtual bool ExistKey(CurveData curveData, float time)
        {
            if (curveData.Keyframes.IsNullOrEmpty())
                return false;

            var minGap = curveData.Keyframes.Min(frameData => Math.Abs(frameData.Keyframe.time - time));

            if (minGap > (0.5f / DummyClip.frameRate)) // => 1f / DummyClip.frameRate*0.5f
                return false;

            return true;
        }

        /// <summary>
        /// Whether key frame already exist in animation curve by time.
        /// </summary>
        public virtual bool TryGetKey(CurveData curveData, float time, out KeyFrameData frameData)
        {
            var keyframes = curveData.Keyframes;

            if (keyframes.IsNullOrEmpty())
            {
                frameData = null;
                return false;
            }

            foreach (var keyframe in keyframes)
            {
                var gap = Mathf.Abs(keyframe.Keyframe.time - time);
                if (gap < QualifyGap)
                {
                    frameData = keyframe;
                    return true;
                }
            }

            frameData = null;
            return false;
        }

        #endregion

        #region Add Key

        public virtual void AddKeys(IArray<CurveData, KeyFrameData> pairs)
        {
            var length = pairs.Length;
            var editableTargets = new bool[length];

            // setup exist keyframes
            for (int i = 0; i < length; i++)
            {
                var data = pairs[i];
                if (TryGetKey(data.Item1, data.Item2.Keyframe.time, out var existKey))
                {
                    var keyframe = data.Item2.Keyframe;
                    // valid value
                    {
                        var newValue = keyframe.value;
                        if (ValidCurveParameter(data.Item1, keyframe.time, ref newValue))
                            keyframe.value = newValue;
                    }

                    existKey.Keyframe = keyframe;
                    editableTargets[i] = true;
                }
            }

            // add new keys

            for (int i = 0; i < length; i++)
            {
                if (editableTargets[i])
                    continue;

                var data = pairs[i];
                var curveData = data.Item1;
                var frameData = data.Item2;

                // valid value
                {
                    var keyframe = frameData.Keyframe;
                    var newValue = keyframe.value;
                    if (ValidCurveParameter(curveData, keyframe.time, ref newValue))
                    {
                        keyframe.value = newValue;
                        frameData.Keyframe = keyframe;
                    }
                }

                if (curveData.curve.AddKey(frameData.Keyframe) < 0)
                {
                    Debug.LogException(new InvalidProgramException($"?????Add key failure at [{curveData.BindingData.binding.propertyName}] by [{frameData.Keyframe.time}]."));
                    return;
                }

                curveData.Keyframes.Add(frameData);
            }

            AnimationUtility.SetEditorCurves(
                DummyClip,
                pairs.Elements1.Select(x => x.BindingData.binding).ToArray(),
                pairs.Elements1.Select(x => x.curve).ToArray()
                );

            if (!isRequiredUpdateKeyframeBar)
                isRequiredUpdateKeyframeBar = true;

        }

        /// <summary>
        /// Return false if contains invalid curve paramters. **Quaternion.w==0
        /// </summary>
        public virtual bool ValidCurveParameter(CurveData curveData, float frameTime, ref float value)
        {
            // valid rotation
            {
                var binding = curveData.BindingData.binding;

                if (binding.propertyName[^2] == '.')
                {
                    var parameter = binding.propertyName[^1];

                    // match quaternion valid
                    {
                        var propertyID = binding.propertyName[..^3];

                        // get exist parameter
                        float x, y, z, w;

                        var targets = dic_bindingDatas.Where(x => x.Key.binding.type == binding.type && x.Key.binding.path == binding.path && x.Key.binding.propertyName.StartsWith(propertyID));

                        if (parameter == 'x')
                            x = value;
                        else if (targets.First(x => x.Key.binding.propertyName.EndsWith(".x"), out var xPair))
                            x = xPair.Value.curve.Evaluate(frameTime);
                        else
                            x = 0;

                        if (parameter == 'y')
                            y = value;
                        else if (targets.First(x => x.Key.binding.propertyName.EndsWith(".y"), out var yPair))
                            y = yPair.Value.curve.Evaluate(frameTime);
                        else
                            y = 0;

                        if (parameter == 'z')
                            z = value;
                        else if (targets.First(x => x.Key.binding.propertyName.EndsWith(".z"), out var zPair))
                            z = zPair.Value.curve.Evaluate(frameTime);
                        else
                            z = 0;

                        if (parameter == 'w')
                            w = value;
                        else if (targets.First(x => x.Key.binding.propertyName.EndsWith(".w"), out var wPair))
                            w = wPair.Value.curve.Evaluate(frameTime);
                        else
                            w = 0;

                        var quat = new Quaternion(x, y, z, w);
                        if (quat.IsNaN())
                        {
                            Debug.LogException(new NotImplementedException(quat.ToString()));
                        }
                        else if (quat.InValid())
                        {
                            if (x + y + z + w == 0)
                            {
                                w = 1;

                                if (parameter == 'w')
                                    value = w;

                                return true;
                            }
                            else
                                Debug.LogException(new NotImplementedException(quat.ToString()));
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Add keyframe at ratio time.
        /// </summary>
        public virtual void AddKey(CurveData curveData, float frameTime, float defaultValue = float.NaN)
        {
            if (float.IsNaN(defaultValue))
            {
                if (curveData.curve.length > 0)
                    defaultValue = curveData.curve.Evaluate(frameTime);
                else
                    defaultValue = 0;
            }

            ValidCurveParameter(curveData, frameTime, ref defaultValue);

            var frame = new Keyframe(frameTime, defaultValue);

            if (curveData.curve.AddKey(frame) < 0)
            {
                Debug.LogException(new InvalidProgramException($"?????Add key failure at [{curveData.BindingData.binding.propertyName}] by [{frameTime}]."));
                return;
            }

            var frameData = new KeyFrameData()
            {
                KeyframeIndex = TimeTrack.GetIndex(frameTime),
                Keyframe = frame,
                RelativeData = curveData
            };

            curveData.Keyframes.Add(frameData);

            AnimationUtility.SetEditorCurve(
                    DummyClip,
                    curveData.BindingData.binding,
                    curveData.curve
                    );

            if (!isRequiredUpdateKeyframeBar)
                isRequiredUpdateKeyframeBar = true;
        }

        protected virtual void AddKeys(int frameIndex)
        {
            AddKeys(frameIndex, dic_bindingDatas.Select(x => x.Key).ToArray());
        }

        /// <summary>
        /// Add keyframe at target frame index.
        /// </summary>
        /// <param name="frameIndex"></param>
        public virtual void AddKeys(int frameIndex, CurveBindingData[] bindingDatas)
        {
            IEnumerable<CurveData> curveDatas;

            if (bindingDatas == null)
                curveDatas = dic_bindingDatas.Select(x => x.Value).ToArray();
            else
                curveDatas = bindingDatas.SelectWhere(x => dic_bindingDatas.ContainsKey(x), x => dic_bindingDatas[x]);

            var time = TimeTrack.GetTime(frameIndex);

            foreach (var curveData in curveDatas)
            {
                if (TryGetKey(curveData, time, out var frameData))
                    continue;

                AddKey(curveData, TimeTrack.GetTime(frameIndex));
            }
        }

        #endregion

        #region Remove Key

        /// <summary>
        /// Delete keyframe.
        /// </summary>
        public virtual void DeleteKeys(params KeyFrameData[] datas)
        {
            foreach (var keyData in datas)
            {
                keyData.RelativeData.curve.Remove(keyData.Keyframe.time);
                keyData.RelativeData.Keyframes.Remove(keyData);
                Debug.Log($"Remove Key [{keyData.KeyframeIndex}]");
            }

            AnimationUtility.SetEditorCurves(
             DummyClip,
             datas.Select(x => x.RelativeData.BindingData.binding).ToArray(),
             datas.Select(x => x.RelativeData.curve).ToArray()
             );

            UpdateKeyframeBarCache();
        }

        /// <summary>
        /// Delete keys at frame index.
        /// </summary>
        public virtual void DeleteKeys(int frameIndex)
        {
            var datas = dic_bindingDatas.SelectMany(x => x.Value.Keyframes).Where(x => x.KeyframeIndex == frameIndex).ToArray();
            DeleteKeys(datas);
        }

        public virtual void DeleteSelectedKeyFrames()
        {
            if (SelectedKeyFrames.IsNullOrEmpty())
                return;

            DeleteKeys(SelectedKeyFrames.ToArray());
            ForceRepaint(0f);
        }

        #endregion

        #region Move Key

        protected bool isDragginKeyFrame;
        protected Vector2 keyframe_BeginPosition;
        protected Vector2 keyframe_EndPosition;

        protected int draggingOffsetIndex;

        /// <summary>
        /// Move keyframes with offset time.
        /// </summary>
        public virtual void MoveKeyframes(float offsetTime, params KeyFrameData[] datas)
        {
            // insert data by frame index
            foreach (var data in datas)
            {
                var curveData = data.RelativeData;
                var frameDatas = curveData.Keyframes;

                try
                {
                    // remove origin key frame from curve
                    curveData.curve.Remove(data.Keyframe.time);

                    // try match exist keyframe at target time
                    if (TryGetKey(curveData, data.Keyframe.time + offsetTime, out var removedData))
                    {
                        if (removedData == data)
                        {
                            Debug.Log($"Ignore moving keyframe at same point [{data.KeyframeIndex}].");
                            continue;
                        }

                        var lastIndex = data.KeyframeIndex;

                        // multi overwrite **1=>2 & 2=>3
                        frameDatas.Remove(removedData);
                        // set new time for frame data
                        var frame = data.Keyframe;
                        frame.time += offsetTime;
                        // align with frame scale
                        data.KeyframeIndex = TimeTrack.GetIndex(frame.time);
                        frame.time = TimeTrack.GetTime(data.KeyframeIndex);
                        data.Keyframe = frame;

                        curveData.curve.ApplyKeyFrame(frame);
                        Debug.Log($"Overwriting keyframe [{curveData.BindingData.binding.propertyName}] move ({lastIndex})=>({data.KeyframeIndex}).");
                    }
                    else
                    {
                        // set new time for frame data
                        var lastIndex = data.KeyframeIndex;
                        var frame = data.Keyframe;
                        frame.time += offsetTime;
                        // align with frame scale
                        data.KeyframeIndex = TimeTrack.GetIndex(frame.time);

                        frame.time = TimeTrack.GetTime(data.KeyframeIndex);
                        data.Keyframe = frame;

                        Debug.Log($"Move keyframe [{curveData.BindingData.binding.propertyName}] move ({lastIndex})=>({data.KeyframeIndex}).");

                        if (curveData.curve.AddKey(frame) < 0)
                            throw new InvalidProgramException($"Invalid add key frame at [{frame.time}].");
                    }

                    frameDatas.CheckAdd(data);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }

                frameDatas.Sort();
                //AnimationUtility.SetEditorCurve(SourceClip, curveData.BindingData.binding, curveData.curve);
            }

            AnimationUtility.SetEditorCurves(
                DummyClip,
                datas.Select(x => x.RelativeData.BindingData.binding).ToArray(),
                datas.Select(x => x.RelativeData.curve).ToArray()
                );

            UpdateKeyframeBarCache();
        }

        /// <summary>
        /// Caculate mouse dragging offset from timetrack scale.
        /// </summary>
        /// <param name="scrollRectPosition">Offset should be ignore from curve scroll area.</param>
        /// <returns>Offset postion in timetrack scale.</returns>
        protected virtual float GetDragginKeyOffset(float scrollRectPosition)
        {
            var keyFramePos = TimeTrack.positions;

            // caculate frame offset
            var startIndex = 0;
            var endIndex = 0;
            var minStartOffset = float.MaxValue;
            var minEndOffset = float.MaxValue;

            var length = keyFramePos.Length;

            for (int i = 0; i < length; i++)
            {
                var pos = keyFramePos[i];

                var framePos = (pos - scrollRectPosition);

                var offsetStart = Mathf.Abs(keyframe_BeginPosition.x - framePos);
                if (offsetStart < minStartOffset)
                {
                    startIndex = i;
                    minStartOffset = offsetStart;
                }

                var offsetEnd = Mathf.Abs(keyframe_EndPosition.x - framePos);
                if (offsetEnd < minEndOffset)
                {
                    endIndex = i;
                    minEndOffset = offsetEnd;
                }
            }

            draggingOffsetIndex = endIndex - startIndex;

            Debug.Log($"Move key offset [{draggingOffsetIndex}]");

            // apply draggin GUI offset
            return keyFramePos[endIndex] - keyFramePos[startIndex];
        }

        #endregion

        #region Edit Key

        /// <summary>
        /// Edit exist key frame.
        /// </summary>
        public virtual void EditKey(CurveData curveData, float time, float value)
        {
            var bindingData = curveData.BindingData;
            var frameDatas = curveData.Keyframes;

            try
            {
                if (!TryGetKey(curveData, time, out var frameData))
                {
                    Debug.LogException(new InvalidOperationException($"Missing keyframe at required time {{{time}}}, please use {nameof(AddKey)} function instead."));
                    return;
                }

                // apply to frame data
                var existFrame = frameData.Keyframe;
                existFrame.value = value;
                frameData.Keyframe = existFrame;

                // apply to curve
                curveData.curve = curveData.curve.ApplyKeyFrame(time, value);

                // apply to clip
                AnimationUtility.SetEditorCurve(DummyClip, curveData.BindingData.binding, curveData.curve);

            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            if (!isRequiredUpdateKeyframeBar)
                isRequiredUpdateKeyframeBar = true;
        }

        #endregion

        #endregion

        #region Toolkit **Curve **Binding **Event

        /// <summary>
        /// Draw handles for animation curve batch operation.
        /// </summary>
        public virtual void DrawToolkitBar(Rect rect)
        {
            var options = GUILayout.ExpandHeight(true);
            {
                GUILayout.BeginArea(rect, GUI.skin.box);
                {
                    GUILayout.BeginHorizontal();

                    // animation curve operation
                    {
                        EditorGUI.BeginDisabledGroup(DummyClip == null);
                        if (GUILayout.Button("Clean Binding", options))
                        {
                            // unuse
                            var removeTargets = dic_bindingDatas.Where(pair => pair.Value.curve == null || pair.Value.curve.length == 0).Select(x => x.Key).ToArray();
                            Debug.Log($"Remove [{removeTargets.Length}] targets.");
                            RemoveProperty(removeTargets);
                        }
                        else if (GUILayout.Button("Optimize Curve", options))
                        {
                            // compress curve
                            EditorAnimationUtility.OptimizeCurveAccuracy(DummyClip);
                            EditorAnimationUtility.OptomizeConstantCurves(DummyClip);
                            AssetDatabase.SaveAssets();

                            InitializeClipData();
                        }

                        {
                            EditorGUI.BeginDisabledGroup(SourceClip == null);
                            if (GUILayout.Button("Copy Origin", options))
                            {
                                var time = TimeTrack.GetTime();
                                var keyframeIndex = TimeTrack.GetIndex();

                                var length = dic_bindingDatas.Count;
                                using var array = new ArrayBuffer<CurveData, KeyFrameData>(length);
                                dic_bindingDatas.Select(x => x.Value).CopyTo(array.Elements1, 0);

                                var curveDatas = array.Elements1;
                                var keyframeDatas = array.Elements2;


                                var curves = array.Elements1.Select(x => AnimationUtility.GetEditorCurve(SourceClip, x.BindingData.binding)).ToArray();

                                // match frame datas
                                for (int i = 0; i < length; i++)
                                {
                                    //remove exist key?
                                    if (curves[i] == null)
                                        continue;

                                    var hasMatch = false;

                                    var curve_origin = curves[i];

                                    var frameData = new KeyFrameData()
                                    {
                                        RelativeData = curveDatas[i],
                                        KeyframeIndex = keyframeIndex,
                                    };

                                    keyframeDatas[i] = frameData;

                                    // copy keyframe
                                    if (!hasMatch)
                                    {
                                        var keys = curve_origin.keys;

                                        foreach (var key in keys)
                                        {
                                            var gap = Mathf.Abs(key.time - time);
                                            if (gap < QualifyGap)
                                            {
                                                hasMatch = true;
                                                frameData.Keyframe = key;
                                                keyframeDatas[i] = frameData;
                                                break;
                                            }
                                        }
                                    }

                                    // evaluate value and tangent
                                    if (!hasMatch)
                                    {
                                        hasMatch = true;

                                        // sample tangent
                                        {
                                            var sampleCurve = new AnimationCurve();
                                            sampleCurve.CopyFrom(curve_origin);
                                            var index = sampleCurve.AddKey(time, curve_origin.Evaluate(time));
                                            sampleCurve.SmoothTangents(index, 0.5f);
                                            frameData.Keyframe = sampleCurve.keys[index];
                                        }
                                    }
                                }

                                AddKeys(array);
                            }
                            EditorGUI.EndDisabledGroup();
                        }

                        if (GUILayout.Button("Add Event", options))
                        {
                            // add event

                        }
                        else if (GUILayout.Button("Remove Event", options))
                        {
                            // remove event

                        }
                        else if (GUILayout.Button("Add Key", options))
                        {
                            // add new keyframe
                            if (TreeView_Properties.HasSelection())
                            {
                                var index = TimeTrack.GetIndex(TimeTrack.GetRatio());
                                var selectedProperties = TreeView_Properties.GetSelectedDatas();
                                AddKeys(index, selectedProperties.ToArray());
                            }
                        }

                        EditorGUI.EndDisabledGroup();
                    }

                    GUILayout.EndHorizontal();
                }
                GUILayout.EndArea();
            }
        }

        #endregion

        #region Timeline

        /// <summary>
        /// Timline controller.
        /// </summary>
        protected FrameTrack TimeTrack;

        public event Action OnValuesChanged;

        /// <summary>
        /// Get current evaluate time.
        /// </summary>
        public virtual float GetTime()
        {
            if (TimeTrack != null)
                return TimeTrack.GetTime();

            Debug.LogException(new InvalidOperationException("Time track is null."));
            return 0;
        }

        public virtual void SetTime(float time)
        {
            if (TimeTrack != null)
                TimeTrack.SetTime(time);
            else
                Debug.LogException(new InvalidOperationException("Time track is null."));
        }

        /// <summary>
        /// Start timeline playaing.
        /// </summary>
        public virtual void Play()
        {
            TimeTrack.Start();

            //TimeTrack.onTick -= ForceRepaint;
            //TimeTrack.onTick += ForceRepaint;

            if (EnablePreview)
            {
                StartPreviewTarget();
                PreviewTarget(TimeTrack.Timeline.Time);
            }
        }


        /// <summary>
        /// Pause timeline playaing.
        /// </summary>
        public virtual void Pause()
        {
            TimeTrack?.Pause();
            //TimeTrack.onTick -= ForceRepaint;
        }
        /// <summary>
        /// Resume timeline playing.
        /// </summary>
        public virtual void Resume()
        {
            TimeTrack?.Resume();
            //TimeTrack.onTick += ForceRepaint;
        }
        /// <summary>
        /// Stop timeline playaing.
        /// </summary>
        public virtual void Stop()
        {
            TimeTrack?.Stop();
            //TimeTrack.onTick -= ForceRepaint;
        }

        /// <summary>
        /// Force repaint when timeline tick or values changed.
        /// </summary>
        /// <param name="time"></param>
        protected virtual void ForceRepaint(float time = 0f)
        {
            Repaint();
            OnValuesChanged?.Invoke();
        }


        /// <summary>
        /// Timeline horizontal size.
        /// </summary>
        float Size_timeline_horizontal = 1f;

        [HideInInspector]
        public float ScrollSensitivity = 0.03f;

        /// <summary>
        /// Handle timeline GUI.
        /// </summary>
        void DrawTimelineTrack(Rect rect_track)
        {
            //EditorGUI.DrawRect(rect_track, Color.yellow);
            //rect_track = rect_track.SetXMin(rect_curve.xMin+ offset_curvePanel_content).SetWidth(rect_curves_scroll_fullContent.xMax);

            if (TimeTrack == null)
                return;

            if (Events.IsType(EventType.ScrollWheel) && Events.PointerInRect(rect_track, out _))
            {
                var scroll = Event.current.delta;

                if (scroll.y < 0)
                {
                    // room in
                    Size_timeline_horizontal -= scroll.y * ScrollSensitivity;
                }
                else if (scroll.y > 0)
                {
                    // room out
                    Size_timeline_horizontal -= scroll.y * ScrollSensitivity;
                }
                else
                {
                    Debug.LogException(new InvalidProgramException());
                }

                TimeTrack.LayoutDirty = true;

                Size_timeline_horizontal = Mathf.Clamp(Size_timeline_horizontal, 0.01f, 10f);
                //Debug.Log($"TimeTrack zoom size [{Size_timeline_horizontal}]");

                Repaint();
                EditorGUIUtility.ExitGUI();
            }
            else
            {
                TimeTrack.DrawTimeTrackBackground(rect_track);

                var rect_vision = rect_track.
                    AddXMin(offset_curvePanel_content).
                    SubXMax(offset_curvePanel_content).
                    AddYMin(2f).
                    SubYMax(2f);

                TimeTrack.DrawTimeTrackGUI(rect_vision, Size_timeline_horizontal);
                //TimeTrack.DoTrackControl(rect_vision);
            }
        }

        /// <summary>
        /// Draw controls for timeline track.
        /// </summary>
        /// <param name="rect"></param>
        protected virtual void DrawTimelineControl(Rect rect)
        {
            {
                GUILayout.BeginArea(rect, GUI.skin.box);

                {
                    GUILayout.BeginHorizontal();

                    // **stop
                    {
                        EditorGUI.BeginDisabledGroup(!TimeTrack.isPlaying);

                        if (GUILayout.Button(content_control_stop, options_control_buttons))
                        {
                            Stop();
                        }

                        EditorGUI.EndDisabledGroup();
                    }

                    // **play **pause **resume
                    {
                        EditorGUI.BeginDisabledGroup(DummyClip == null);

                        if (TimeTrack.isPlaying)
                        {
                            if (TimeTrack.isPause)
                            {
                                // resume
                                if (GUILayout.Button(content_control_play, options_control_buttons))
                                {
                                    Resume();
                                }
                            }
                            else
                            {
                                // pause
                                if (GUILayout.Button(content_control_pause, options_control_buttons))
                                {
                                    Pause();
                                }
                            }
                        }
                        else
                        {
                            // play
                            if (GUILayout.Button(content_control_play, options_control_buttons))
                            {
                                Play();
                            }
                        }

                        EditorGUI.EndDisabledGroup();
                    }

                    // **last frame **next frame
                    {
                        EditorGUI.BeginDisabledGroup(TimeTrack == null);

                        if (GUILayout.Button(content_control_lastFrame, options_control_buttons))
                            TimeTrack?.LastFrame();


                        if (GUILayout.Button(content_control_nextFrame, options_control_buttons))
                            TimeTrack?.NextFrame();


                        EditorGUI.EndDisabledGroup();
                    }

                    GUILayout.EndHorizontal();
                }
                GUILayout.EndArea();
            }
        }




        #endregion

        #region GUI Layout

        Vector2 lastWindowSize;

        /// <summary>
        /// Vertical scroll bar for curve data base.
        /// </summary>
        Vector2 scroll_panel_curve;
        Vector2 scroll_property;

        /// <summary>
        /// Whether pointer inside the rect which align top
        /// </summary>
        bool isScrollHorizontal = false;

        private static readonly int k_ResizePanelControlID = "ResizePanel".GetHashCode();

        float height_bar_offset => EditorGUIUtility.singleLineHeight;


        protected Rect rect_content;

        /// <summary>
        /// Rect for drawing control GUI.
        /// </summary>
        [SerializeField, HideInInspector]
        Rect rect_toolBar_top = new(0, 0, 0, 16);
        [SerializeField, HideInInspector]
        Rect rect_toolBar_general = new(0, 0, 0, 30);
        [SerializeField, HideInInspector]
        Rect rect_toolBar_buttons = new(0, 0, 0, 30);
        [SerializeField, HideInInspector]
        Rect rect_toolBar_toolkit = new(0, 0, 0, 30);

        [SerializeField, HideInInspector]
        Rect rect_recording_buttons = new(0, 0, 0, 30);
        [SerializeField, HideInInspector]
        Rect rect_timeControl = new(0, 0, 0, 30);
        [SerializeField, HideInInspector]
        Rect rect_timeTrack = new(0, 0, 0, 40);
        /// <summary>
        /// Rect of property panel.
        /// </summary>
        [SerializeField, HideInInspector]
        Rect rect_property_vision = new();
        /// <summary>
        /// Rect of property toolbar layout. **Properties **Bindings
        /// </summary>
        [SerializeField, HideInInspector]
        Rect rect_property_toolbar;
        /// <summary>
        /// Rect of curve toolbar layout. **Properties **Bindings
        /// </summary>
        [SerializeField, HideInInspector]
        Rect rect_curve_toolbar;
        /// <summary>
        /// Rect for curves GUI layout.
        /// </summary>
        [SerializeField, HideInInspector]
        Rect rect_curve = new();

        Rect rect_curve_control_scroll_horizontal;
        Rect rect_curve_control_scroll_vertical;

        [HideInInspector]
        public float height_toolbar_bottom = 30f;

        private float r_panelWidth = 300f;
        /// <summary>
        /// Layout bar parameter.
        /// </summary>
        private Vector2 m_lastMousePos;
        private Vector2 m_DragDistance;
        private float m_DragStartWidth;
        private bool refresh = false;

        static float minLayoutWidth_Property = 100f;
        static float minLayoutWidth_Curve = 200f;


        GUIContent content_keyframe_default;
        GUIContent content_keyframe_selected;

        GUIContent content_control_record;

        GUIContent content_control_play;
        GUIContent content_control_pause;
        GUIContent content_control_stop;

        GUIContent content_control_nextFrame;
        GUIContent content_control_lastFrame;

        Texture2D texture_property_searchBar_icon;

        GUIStyle style_general_bar;
        GUIStyle style_general_tooltip;

        GUIStyle style_keyframe;
        GUIStyle style_keyframe_selected;
        GUIStyle style_keyframe_Background;

        GUIStyle style_property_type;
        GUIStyle style_general_preview_begin;
        GUIStyle style_general_preview_failure;

        GUIStyle style_universal_middle;

        GUILayoutOption[] options_control_buttons;

        Color color_curve_background = new(0.1568628f, 0.1568628f, 0.1568628f);
        Color color_selectedKeyframe = new(0.3411765f, 0.5215687f, 0.8509805f, 1f);

        [NonSerialized]
        protected bool hasInitializedGUI = false;

        /// <summary>
        /// Initialize GUI options.
        /// </summary>
        protected void InitializeGUI()
        {
            hasInitializedGUI = false;

            if (lastWindowSize != position.size)
            {
                lastWindowSize = position.size;
                TimeTrack.LayoutDirty = true;
            }

            if (style_general_bar == null)
            {
                style_general_bar = new(GUI.skin.FindStyle("AC Button"));
                style_general_bar.alignment = TextAnchor.MiddleCenter;
                style_general_bar.fixedWidth = 0;
                style_general_bar.stretchWidth = true;
            }

            if (style_general_tooltip == null)
            {
                style_general_tooltip = new(GUI.skin.label);
                style_general_bar.alignment = TextAnchor.MiddleCenter;
                style_general_bar.fixedWidth = 0;
                style_general_bar.stretchWidth = true;
            }

            if (style_general_preview_begin == null)
            {
                style_general_preview_begin = new(GUI.skin.FindStyle("AC Button"));
                style_general_preview_begin.fixedWidth = 0;
                style_general_preview_begin.alignment = TextAnchor.MiddleCenter;
                style_general_preview_begin.stretchWidth = true;

                var state = style_general_preview_begin.onActive;
                state.textColor = Color.green;
                style_general_preview_begin.normal = state;
                style_general_preview_begin.onNormal = state;
                style_general_preview_begin.hover = state;
                style_general_preview_begin.onHover = state;
                style_general_preview_begin.focused = state;
                style_general_preview_begin.onFocused = state;
            }

            if (style_general_preview_failure == null)
            {
                style_general_preview_failure = new(style_general_preview_begin);
                var state = style_general_preview_failure.onActive;
                state.textColor = Color.red;
                style_general_preview_failure.normal = state;
                style_general_preview_failure.onNormal = state;
                style_general_preview_failure.hover = state;
                style_general_preview_failure.onHover = state;
                style_general_preview_failure.focused = state;
                style_general_preview_failure.onFocused = state;

            }

            if (style_keyframe == null)
            {
                style_keyframe = new GUIStyle(GUIStyle.none);
                style_keyframe.imagePosition = ImagePosition.ImageOnly;
            }

            if (style_keyframe_selected == null)
            {
                style_keyframe_selected = new(GUIStyle.none);
                style_keyframe_selected.imagePosition = ImagePosition.ImageOnly;
            }

            if (style_property_type == null)
            {
                style_property_type = new(GUI.skin.box) { wordWrap = false };

                style_property_type.alignment = TextAnchor.MiddleLeft;

                style_property_type.normal.textColor = Color.white;
                style_property_type.focused.textColor = Color.white;
                style_property_type.hover.textColor = Color.white;
            }

            if (style_universal_middle == null)
            {
                style_universal_middle = new(GUI.skin.label);
                style_universal_middle.alignment = TextAnchor.MiddleCenter;
            }

            if (style_boxBackground == null)
            {
                style_boxBackground = new GUIStyle("AvatarMappingBox");
            }

            if (Labels_PropertyPanel == null)
            {
                Labels_PropertyPanel = new GUIContent[2];
                Labels_PropertyPanel[0] = new GUIContent("Property", SdfIcons.CreateTransparentIconTexture(SdfIconType.Justify, Color.white, 20, 20, 5));
                Labels_PropertyPanel[1] = new GUIContent("Binding", SdfIcons.CreateTransparentIconTexture(SdfIconType.PlusSquareDotted, Color.white, 20, 20, 5));
            }

            if (Labels_BindingPanel == null)
            {
                Labels_BindingPanel = new GUIContent[3];
                Labels_BindingPanel[0] = new GUIContent("Dummy", "Manual bindings.");
                Labels_BindingPanel[1] = new GUIContent("Source", "Register by source clip.");
                Labels_BindingPanel[2] = new GUIContent("Skeleton", "Binding from exist skeleton");
            }

            if (Labels_CurvePanel == null)
            {
                Labels_CurvePanel = new GUIContent[2];
                Labels_CurvePanel[0] = new GUIContent("Dopesheet", SdfIcons.CreateTransparentIconTexture(SdfIconType.Signpost, Color.white, 20, 20, 5));
                Labels_CurvePanel[1] = new GUIContent("Curve", SdfIcons.CreateTransparentIconTexture(SdfIconType.Bezier2, Color.white, 20, 20, 5));
            }

            style_keyframe_Background = GUI.skin.FindStyle("AnimationKeyframeBackground");


            if (content_control_record == null)
            {
                var icon = SdfIcons.CreateTransparentIconTexture(SdfIconType.RecordCircle, Color.red, 25, 25, 2);
                content_control_record = new GUIContent(icon, "Record");
            }

            if (content_control_play == null)
            {
                var icon = SdfIcons.CreateTransparentIconTexture(SdfIconType.Play, Color.white, 25, 25, 2);
                content_control_play = new GUIContent(icon, "Play");
            }

            if (content_control_pause == null)
            {
                var icon = SdfIcons.CreateTransparentIconTexture(SdfIconType.Pause, Color.white, 25, 25, 2);
                content_control_pause = new GUIContent(icon, "Pause");
            }

            if (content_control_stop == null)
            {
                var icon = SdfIcons.CreateTransparentIconTexture(SdfIconType.Stop, Color.white, 25, 25, 2);
                content_control_stop = new GUIContent(icon, "Stop");
            }

            if (content_control_lastFrame == null)
            {
                var icon = SdfIcons.CreateTransparentIconTexture(SdfIconType.SkipStart, Color.white, 25, 25, 2);
                content_control_lastFrame = new GUIContent(icon, "Last Frame");
            }

            if (content_control_nextFrame == null)
            {
                var icon = SdfIcons.CreateTransparentIconTexture(SdfIconType.SkipEnd, Color.white, 25, 25, 2);
                content_control_nextFrame = new GUIContent(icon, "Next Frame");
            }

            if (content_keyframe_default == null)
                content_keyframe_default = EditorGUIUtility.IconContent("sv_icon_dot8_pix16_gizmo");

            if (content_keyframe_selected == null)
                content_keyframe_selected = EditorGUIUtility.IconContent("sv_icon_dot9_pix16_gizmo");

            if (texture_property_searchBar_icon == null)
                texture_property_searchBar_icon = SdfIcons.CreateTransparentIconTexture(SdfIconType.Search, Color.white, 25, 25, 2);

            if (options_control_buttons == null)
            {
                options_control_buttons = new GUILayoutOption[]
                {
                    GUILayout.ExpandHeight(true),
                    GUILayout.ExpandWidth(true),
                };
            }

            if (RectSelection == null)
            {
                RectSelection = new() { useEvent = true, Color_SelectionRect = Color.blue.SetAlpha(1f) };
                RectSelection.OnSelectionChange += RectSelection_OnSelectionChange;
            }
        }


        /// <summary>
        /// Handle user layout control.
        /// </summary>
        protected virtual void DrawnResizeControl()
        {
            Event current = Event.current;
            int controlID = GUIUtility.GetControlID(k_ResizePanelControlID, FocusType.Passive);
            int hotControl = GUIUtility.hotControl;

            // specify the area where you can click and drag from
            Rect dragRegion = new(rect_property_vision.xMax - 5, rect_property_vision.yMin - 5, 10, rect_property_vision.height + 10);

            EditorGUIUtility.AddCursorRect(dragRegion, MouseCursor.ResizeHorizontal);

            switch (current.GetTypeForControl(controlID))
            {
                case EventType.MouseDown:
                    if (current.button == 0)
                    {
                        var canDrag = dragRegion.Contains(current.mousePosition);
                        if (!canDrag)
                            return;

                        //record in screenspace, not GUI space so that the resizing is consistent even if the cursor leaves the window
                        this.m_lastMousePos = GUIUtility.GUIToScreenPoint(current.mousePosition);
                        this.m_DragDistance = Vector2.zero;
                        this.m_DragStartWidth = r_panelWidth;

                        GUIUtility.hotControl = controlID;
                        current.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (hotControl == controlID)
                    {
                        GUIUtility.hotControl = 0;
                        current.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (hotControl == controlID)
                    {
                        var mouse_screen = GUIUtility.GUIToScreenPoint(current.mousePosition);
                        this.m_DragDistance += mouse_screen - this.m_lastMousePos;
                        this.m_lastMousePos = mouse_screen;
                        r_panelWidth = Mathf.Max(minLayoutWidth_Property, Mathf.Min(m_DragStartWidth + this.m_DragDistance.x, position.width - minLayoutWidth_Curve));

                        TimeTrack.LayoutDirty = true;

                        this.refresh = true;
                        current.Use();
                    }
                    break;
                case EventType.KeyDown:
                    if (hotControl == controlID && current.keyCode == KeyCode.Escape)
                    {
                        r_panelWidth = Mathf.Max(minLayoutWidth_Property, Mathf.Min(m_DragStartWidth, position.width - minLayoutWidth_Curve));
                        TimeTrack.LayoutDirty = true;


                        GUIUtility.hotControl = 0;
                        current.Use();
                    }
                    break;


                    //uncomment if you want to debug the area to click - drag for resizing

                    //case EventType.Layout:
                    //case EventType.Repaint:
                    //    EditorGUI.DrawRect(dragRegion.AlignCenterX(7), Color.white.SetAlpha(0.7f));
                    //    break;
            }
        }


        #endregion

        #region General Panel **Clip Info **File Operation

        /// <summary>
        /// Draw general operation GUI handles.
        /// </summary>
        public virtual void DrawGeneralInfo(Rect rect)
        {
            var width_half = rect.width * 0.5f;
            {
                GUILayout.BeginArea(rect);

                // draw background for general info
                {
                    GUILayout.BeginVertical();
                    // draw menu bar
                    {
                        GUILayout.BeginHorizontal();

                        var options_button = new[] { GUILayout.ExpandHeight(false), GUILayout.ExpandWidth(true) };

                        if (GUILayout.Button("File", style_general_bar, options_button))
                        {
                            GenericMenu menu = new();

                            menu.AddItem(new GUIContent("Open File", "File Operation"), false, () => Debug.Log("Open File"));

                            // create empty clip
                            {
                                var content = new GUIContent("Create Empty Clip", "Empty Output Clip");

                                if (DummyClip == null)
                                {
                                    // Generic
                                    {
                                        menu.AddItem(content, false, CreateClip);
                                    }

                                    // T-Pose
                                    {
                                        content.text = "Create T-Pose Clip";
                                        menu.AddItem(content, false, CreateEmptyClip);
                                    }
                                }
                                else
                                    menu.AddDisabledItem(content);
                            }

                            if (SourceClip != null || DummyClip != null)
                                menu.AddItem(new GUIContent("Exit Edit", "Clear editing data."), false, ExitEdit);
                            else
                                menu.AddDisabledItem(new GUIContent("Exit Edit"));

                            menu.AddItem(new GUIContent("Save File", "File Operation"), false, (clip) => ArchiveClip(clip as AnimationClip), DummyClip);

                            menu.DropDown(new Rect(0, 0, rect.width * 0.2f, 10f));
                        }

                        // preview button
                        {
                            var style = EnablePreview ? (MixerPlayable.IsValid() ? style_general_preview_begin : style_general_preview_failure) : style_general_bar;
                            if (GUILayout.Button("Preview", style, options_button))
                            {
                                EnablePreview = !EnablePreview;
                            }
                        }

                        GUILayout.EndHorizontal();
                    }


                    // draw clip field
                    {
                        GUILayout.BeginHorizontal();
                        var option_label = GUILayout.Width(60);

                        // property left - clip bindings
                        {
                            GUILayout.BeginVertical();
                            // skeleton
                            {
                                GUILayout.BeginHorizontal();
                                GUILayout.Label("Skeleton", option_label);
                                {
                                    EditorGUI.BeginChangeCheck();
                                    var newTarget = EditorGUILayout.ObjectField(Animator, typeof(Animator), true);
                                    if (EditorGUI.EndChangeCheck())
                                    {
                                        if (EnablePreview)
                                            StopPreviewTarget();

                                        Animator = newTarget as Animator;
                                        if (Animator != null)
                                        {
                                            Director = Animator.GetComponent<GraphDirector>();
                                            if (EnablePreview)
                                            {
                                                StartPreviewTarget();
                                                PreviewTarget(TimeTrack.Timeline.Time);
                                            }
                                        }
                                        else
                                        {
                                            Director = null;
                                        }
                                    }
                                }
                                GUILayout.EndHorizontal();
                            }

                            // avatar mask
                            {
                                GUILayout.BeginHorizontal();
                                GUILayout.Label("Mask", option_label);
                                {
                                    EditorGUI.BeginChangeCheck();
                                    var newMask = EditorGUILayout.ObjectField(AvatarMask, typeof(AvatarMask), false);
                                    if (EditorGUI.EndChangeCheck())
                                    {
                                        AvatarMask = newMask as AvatarMask;
                                        SetPlayableMask();
                                    }
                                }
                                GUILayout.EndHorizontal();
                            }

                            // origin clip
                            {
                                GUILayout.BeginHorizontal();
                                GUILayout.Label("Origin", option_label);
                                {
                                    EditorGUI.BeginChangeCheck();
                                    var newTarget = EditorGUILayout.ObjectField(SourceClip, typeof(AnimationClip), false);
                                    if (EditorGUI.EndChangeCheck())
                                    {
                                        var clip = newTarget as AnimationClip;
                                        SourceClip = clip;

                                        if (clip != null)
                                        {
                                            if (DummyClip == null)
                                            {
                                                Debug.Log($"Craeting dummy clip for editor operation.", DummyClip);
                                                DummyClip = Object.Instantiate(SourceClip);

                                                if (AdditivePreview)
                                                {
                                                    var bindings = AnimationUtility.GetCurveBindings(DummyClip);
                                                    var curves = bindings.Select(x => AnimationUtility.GetEditorCurve(DummyClip, x)).ToArray();

                                                    foreach (var curve in curves)
                                                        curve.ClearKeys();

                                                    // clear exist keyframes for additive clip initialization
                                                    AnimationUtility.SetEditorCurves(DummyClip, bindings, curves);

                                                    var setting = AnimationUtility.GetAnimationClipSettings(clip);
                                                    setting.hasAdditiveReferencePose = true;
                                                    setting.additiveReferencePoseClip = SourceClip;
                                                    AnimationUtility.SetAnimationClipSettings(DummyClip, setting);
                                                }
                                                else
                                                {
                                                    //DummyClip = Object.Instantiate(SourceClip);
                                                    //DummyClip = EditorAnimationUtility.TPoseClip(Director == null ? null : Director.Director?.animator, time: clip.length);
                                                }

                                                InitializeClipData(); // source clip duplicate
                                            }
                                        }


                                        if (EnablePreview)
                                        {
                                            if (ClipPlayable_Origin.IsQualify())
                                            {
                                                ClipPlayable_Origin.SetAnimatedProperties(SourceClip);
                                            }
                                            else
                                            {
                                                StartPreviewTarget();
                                            }
                                        }
                                    }
                                }
                                GUILayout.EndHorizontal();
                            }

                            // editing clip
                            {
                                GUILayout.BeginHorizontal();
                                GUILayout.Label("Editing", option_label);
                                {
                                    EditorGUI.BeginChangeCheck();
                                    var newTarget = EditorGUILayout.ObjectField(DummyClip, typeof(AnimationClip), false);
                                    if (EditorGUI.EndChangeCheck())
                                    {
                                        var clip = newTarget as AnimationClip;

                                        if (DummyClip != clip)
                                        {
                                            DummyClip = clip;

                                            InitializeClipData(); // dummy clip field

                                            if (clip != null)
                                            {
                                                // bind addtitive clip
                                                {
                                                    var setting = AnimationUtility.GetAnimationClipSettings(clip);
                                                    if (setting.hasAdditiveReferencePose)
                                                    {
                                                        if (SourceClip == null)
                                                        {
                                                            // set additive reference
                                                            SourceClip = setting.additiveReferencePoseClip;
                                                        }
                                                        else if (setting.additiveReferencePoseClip != null && setting.additiveReferencePoseClip != SourceClip)
                                                        {
                                                            if (EditorUtility.DisplayDialog("Additive Config", "Whether apply additive clip source to clip editor?", "Yes", "No"))
                                                                SourceClip = setting.additiveReferencePoseClip;
                                                        }
                                                        else
                                                        {
                                                            Debug.LogException(new NotImplementedException());
                                                        }
                                                    }

                                                    AdditivePreview = setting.hasAdditiveReferencePose && setting.additiveReferencePoseClip == SourceClip;
                                                }

                                                if (EnablePreview)
                                                {
                                                    if (ClipPlayable_Editing.IsQualify())
                                                    {
                                                        ClipPlayable_Editing.SetAnimatedProperties(DummyClip);
                                                    }
                                                    else
                                                    {
                                                        StartPreviewTarget();
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                                GUILayout.EndHorizontal();
                            }

                            GUILayout.EndVertical();
                        }

                        // property right - blend weight
                        {
                            GUILayout.BeginVertical(GUILayout.MaxWidth(width_half));

                            //GUILayout.Space(height_bar_offset);
                            // Avarar Mask
                            {
                                EditorGUI.BeginChangeCheck();
                                var newMaskOption = EditorGUILayout.ToggleLeft("UseMask", EnableMask, GUILayout.MaxWidth(width_half - 10));
                                if (EditorGUI.EndChangeCheck())
                                {
                                    EnableMask = newMaskOption;
                                    SetPlayableMask();
                                }
                            }

                            // Clip Additive
                            {
                                EditorGUI.BeginChangeCheck();
                                var newAdditiveOption = EditorGUILayout.ToggleLeft("Additive", AdditivePreview, GUILayout.MaxWidth(width_half - 10));
                                if (EditorGUI.EndChangeCheck())
                                {
                                    if (DummyClip != null && SourceClip != null)
                                    {
                                        if (EditorUtility.DisplayDialog("Additive Option", $"Whether convert editing clip [{DummyClip}] into {(newAdditiveOption ? "additive" : "normal")} animation clip?", "Yes", "No"))
                                        {
                                            if (newAdditiveOption)
                                            {
                                                // rescale additive values from base clip
                                                Debug.LogError("[ToDo] get addititve clip.");
                                            }
                                            else
                                            {
                                                // caculate composite values for overlap clip
                                                Debug.LogError("[ToDo] get composite clip.");
                                            }
                                        }
                                        else
                                        {
                                            if (newAdditiveOption)
                                            {
                                                // extract additive values from base clip
                                                Debug.LogError("[ToDo] get addititve clip.");
                                            }
                                            else
                                            {
                                                // caculate composite values for overlap clip
                                                Debug.LogError("[ToDo] get composite clip.");
                                            }

                                            AdditivePreview = newAdditiveOption;
                                        }
                                    }
                                    else
                                    {
                                        AdditivePreview = newAdditiveOption;
                                    }
                                }
                            }

                            // tooltip
                            {
                                if (AdditivePreview)
                                    GUILayout.Label("Base <=> Additive", style_general_tooltip, GUILayout.ExpandWidth(true));
                                else
                                    GUILayout.Label("Source <=> Editing", style_general_tooltip, GUILayout.ExpandWidth(true));
                            }


                            // Clip Weighting
                            {
                                EditorGUI.BeginChangeCheck();
                                //Rect controlRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight, GUIStyle.none, options:null); //style ?? EditorStyles.numberField
                                //var newBlendWeight = SirenixEditorFields.RangeFloatField(controlRect, label:null, m_blendingWeight, 0, 1, null);
                                var newBlendWeight = EditorGUILayout.Slider(BlendingWeight, 0, 1f);
                                if (EditorGUI.EndChangeCheck())
                                {
                                    BlendingWeight = newBlendWeight;

                                    if (EnablePreview)
                                        SetAdditiveAndWeight();
                                }
                            }

                            GUILayout.EndVertical();
                        }

                        GUILayout.EndHorizontal();
                    }

                    GUILayout.Space(5);

                    // draw clip info 
                    {
                        var option_label = GUILayout.Width(80);

                        {
                            GUILayout.BeginHorizontal();
                            GUILayout.Label("Length", option_label);
                            {
                                EditorGUI.BeginChangeCheck();
                                var newLength = EditorGUILayout.DelayedFloatField(ClipLength, option_label);
                                if (EditorGUI.EndChangeCheck())
                                {
                                    newLength = Mathf.Clamp(newLength, 1f / ClipFrameRate * 2f, newLength);
                                    ClipLength = newLength;

                                    SetupTimeTrack(newLength, (int)ClipFrameRate);

                                    if (DummyClip != null)
                                    {
                                        var setting = AnimationUtility.GetAnimationClipSettings(DummyClip);
                                        setting.stopTime = ClipLength;
                                        AnimationUtility.SetAnimationClipSettings(DummyClip, setting);
                                    }
                                }
                            }
                            GUILayout.Label($"{(DummyClip == null ? 0 : DummyClip.length)} sec/{(SourceClip == null ? 0 : SourceClip.length)} sec");
                            GUILayout.EndHorizontal();
                        }

                        {
                            GUILayout.BeginHorizontal();
                            GUILayout.Label("FrameRate", option_label);
                            {
                                EditorGUI.BeginChangeCheck();
                                var newFrameRate = EditorGUILayout.DelayedIntField((int)ClipFrameRate, option_label);
                                if (EditorGUI.EndChangeCheck())
                                {
                                    newFrameRate = Mathf.Max(newFrameRate, 1);
                                    ClipFrameRate = newFrameRate;

                                    SetupTimeTrack(ClipLength, (int)ClipFrameRate);

                                    // re-sample?
                                    DummyClip.frameRate = newFrameRate;
                                }
                            }

                            GUILayout.Label($"Frame Count : {ClipFrameCount}");
                            GUILayout.EndHorizontal();
                        }
                    }
                    GUILayout.EndVertical();
                }
                GUILayout.EndArea();
            }
        }

        #endregion

        #region Property

        #region Property Binding
        /// <summary>
        /// Labels for binding option.
        /// </summary>
        GUIContent[] Labels_BindingPanel;
        /// <summary>
        /// Index of binding option.
        /// </summary>
        int index_binding_tab;
        /// <summary>
        /// Search field for binding option.
        /// </summary>
        SearchField SearchField_bindings;

        #region Source Binding
        protected TreeViewState state_binding_source;
        protected CurveBindingTreeView TreeView_bindings_source;
        #endregion

        #region Skeleton Binding
        protected TreeViewState state_binding_skeleton;
        protected PropertyBindingTreeView TreeView_bindings_skeleton;
        #endregion


        protected void DrawBindingPropertyTreeGUI(Rect rect)
        {
            // search bar
            {
                DoSearchbar(rect.AlignTopOut(height_bar_offset));
            }

            var rect_content = rect;

            // tabs
            {
                var rect_tab = rect.AlignTop(height_bar_offset);
                rect_content = rect_content.AddY(height_bar_offset);
                index_binding_tab = GUI.Toolbar(rect_tab, index_binding_tab, Labels_BindingPanel);
            }

            // manual binding
            if (index_binding_tab == 0)
            {
                if (GUI.Button(rect_content.AlignCenterY(height_bar_offset).Expand(-10, 0), "Manual."))
                {
                    // todo:binding property by custom input
                    var handle = new CustomBindingHandle() { };
                    handle.OnBindingSelected += OnBindingAdded;
                    UniversalPopWindow.OpenWindow("Manual Binding", 500, 700, handle.DrawBindingSelector);
                }
            }
            // source binding
            else if (index_binding_tab == 1)
            {
                if (SourceClip == null)
                    GUI.Label(rect_content, "Missing source clip.", style_universal_middle);
                else
                {
                    if (state_binding_source == null)
                        state_binding_source = new();
                    if (TreeView_bindings_source == null || TreeView_bindings_source.Clip != SourceClip)
                    {
                        TreeView_bindings_source = new(state_binding_source, SourceClip);
                        TreeView_bindings_source.CustomDrawer = DrawPropertyRow_AddBinding;
                        var menu = new GenericMenu();
                        menu.AddItem(new GUIContent("Add Bindings"), false, () =>
                        {
                            if (TreeView_bindings_source == null || !TreeView_bindings_source.HasSelection())
                                return;

                            var bindings = TreeView_bindings_source.GetSelectedDatas().Select(x => new CurveBinding(x.binding)).ToArray();
                            OnBindingAdded(bindings);
                        });
                        TreeView_bindings_source.RowContextMenu = menu;
                        TreeView_bindings_source.Reload();
                    }

                    TreeView_bindings_source.OnGUI(rect_content);
                }
            }
            // skeleton binding
            else if (index_binding_tab == 2)
            {
                if (Director == null || Director.Director == null || Director.Director.animator == null)
                    GUI.Label(rect_content, "Missing skeleton binding.", style_universal_middle);

                if (GUI.Button(rect_content.AlignCenterY(height_bar_offset).AlignBottomOut(height_bar_offset).Expand(-10, 0), "Skeleton"))
                {
                    // generic property bindings
                    {
                        if (state_binding_skeleton == null || isTreeViewDirty)
                            state_binding_skeleton = new TreeViewState();

                        if (TreeView_bindings_skeleton == null || isTreeViewDirty)
                        {
                            TreeView_bindings_skeleton = new PropertyBindingTreeView(state_binding_skeleton, Director.Director.animator.gameObject, true);
                            TreeView_bindings_skeleton.OnRightClick = StartBindingProperty;
                            SearchField_bindings.downOrUpArrowKeyPressed -= TreeView_bindings_skeleton.SetFocusAndEnsureSelectedItem;
                            SearchField_bindings.downOrUpArrowKeyPressed += TreeView_bindings_skeleton.SetFocusAndEnsureSelectedItem;

                            isTreeViewDirty = false;
                        }
                    }
                }

                if (TreeView_bindings_skeleton != null)
                {
                    GUILayout.BeginArea(rect_content, GUIContent.none, GUI.skin.window);
                    TreeView_bindings_skeleton.OnGUI(rect_content.SetPosition(Vector2.zero));
                    GUILayout.EndArea();
                }
            }
        }


        /// <summary>
        /// Drawing search field GUI for property binding.
        /// </summary>
        void DoSearchbar(Rect rect)
        {
            rect = rect.Expand(-2, -10, -1, -1);
            {
                EditorGUI.BeginChangeCheck();
                var searchString = SearchField_bindings.OnToolbarGUI(rect, m_property_filter);
                if (EditorGUI.EndChangeCheck())
                {
                    m_property_filter = searchString;
                    if (TreeView_bindings_skeleton != null)
                        TreeView_bindings_skeleton.searchString = searchString;
                }
            }
        }

        /// <summary>
        /// Draw custom GUI for property binding treeview.
        /// </summary>
        protected virtual void DrawPropertyRow_AddBinding(Rect rect, CurveBindingData bindingData)
        {
            {
                GUILayout.BeginArea(rect.AlignRight(20));

                if (GUILayout.Button("Add"))
                    OnBindingAdded(new CurveBinding(bindingData.binding));

                GUILayout.EndArea();
            }
        }


        /// <summary>
        /// Cache state for binding tree view.
        /// </summary>
        protected bool isTreeViewDirty;

        protected virtual void EditorApplication_hierarchyChanged()
        {
            isTreeViewDirty = true;
        }

        /// <summary>
        /// Open binding property window.
        /// </summary>
        protected virtual void StartBindingProperty(Rect rect, Object obj)
        {
            if (obj is not Component comp)
                return;

            GameObject root = null;

            foreach (var tf in comp.transform.TraverseUpward(true))
            {
                if (tf.TryGetComponent<Animator>(out var animator))
                {
                    root = animator.gameObject;
                    break;
                }
            }

            Debug.LogError(root);

            if (root == null)
                root = comp.transform.root.gameObject;

            var bindingHandle = new PropertyBindingHandle(comp, root);
            var dic = dic_bindingDatas.Select(x => x.Key.binding).ToHashSet();
            Func<CurveBinding, bool> func = (x) => dic.Contains(x.EditorBinding);
            bindingHandle.SetupExistBinding(func);

            bindingHandle.OnBindingAdded += OnBindingAdded;
            bindingHandle.OnBindingRemoved += OnBindingRemoved;

            var window = UniversalPopWindow.OpenWindow("Property Binding", 400, 600, bindingHandle.OnInspectorGUI);
            window.position = window.position.SetPosition(rect.position + Event.current.mousePosition);
        }

        /// Collect animation binding data.
        /// </summary>
        public virtual void SetBindingDatas(CurveBindingData[] datas)
        {
            Debug.Log(datas == null ? "null" : datas.Length);

            dic_bindingDatas = datas.Select(
                bindingData =>
                {
                    var curveData = new CurveData()
                    {
                        BindingData = bindingData,
                        curve = bindingData.GetCurve(DummyClip)
                    };

                    if (curveData.curve != null)
                    {
                        var keys = curveData.curve.keys;
                        var keyframeData = new List<KeyFrameData>(keys.Length);
                        for (int i = 0; i < keys.Length; i++)
                        {
                            keyframeData.Add(
                            new KeyFrameData()
                            {
                                KeyframeIndex = TimeTrack.GetIndex(keys[i].time),
                                Keyframe = keys[i],
                                RelativeData = curveData,
                            });
                        }

                        curveData.Keyframes = keyframeData;
                    }
                    else
                    {
                        curveData.curve = AnimationCurve.Constant(0, 1f, 0f);
                        curveData.Keyframes = new(2);
                    }


                    return curveData;
                }).ToDictionary(x => x.BindingData, x => x);
        }

        public virtual void OnBindingAdded(params CurveBinding[] newBindings)
        {
            Debug.Log($"{nameof(OnBindingAdded)} [{(newBindings == null ? 0 : newBindings.Length)}]");
            GameObject root;

            if (Director == null || Director.Director == null || Director.Director.animator == null)
                root = null;
            else
                root = Director.Director.animator.gameObject;

            var existBindings = TreeView_Properties.BindingData_ClipSource;

            Queue<CurveBinding> addedNewBinding = new(newBindings.Length);

            foreach (var newBinding in newBindings)
            {
                if (existBindings.Exist(x => x.binding.type == newBinding.Type && x.binding.path == newBinding.Path && x.binding.propertyName == newBinding.PropertyName))
                {
                    continue;
                }

                addedNewBinding.Enqueue(newBinding);
            }

            if (addedNewBinding.Count == 0)
            {
                Debug.LogException(new InvalidOperationException($"Failure to add new bindings [{newBindings?.Length:0}].\n{(newBindings.IsNullOrEmpty() ? "null" : string.Join("\n", newBindings.Select(x => x.PropertyName)))}"));
                return;
            }


            if (DummyClip != null)
            {
                foreach (var binding in addedNewBinding)
                {
                    // set time as same as clip length
                    if (binding.Curve == null)
                    {
                        Debug.LogError($"Null curve {binding.DisplayName}");
                        binding.Curve = new AnimationCurve();
                    }

                    AnimationUtility.SetEditorCurve(DummyClip, binding.EditorBinding, binding.Curve);
                }
            }

            existBindings = existBindings.
                Append(addedNewBinding.Select(x =>
                {
                    var binding = new CurveBindingData(x.EditorBinding, root);
                    return binding;
                }).
                //OrderBy(x=>x.binding.path).
                ToArray());


            Debug.Log(existBindings.Length);

            state_property.expandedIDs.Clear();
            TreeView_Properties.BindingData_ClipSource = existBindings;
            TreeView_Properties.Reload();
            SetBindingDatas(existBindings);
            ForceRepaint(0f);
        }

        public void OnBindingRemoved(params CurveBinding[] removeBindings)
        {
            var existBindings = TreeView_Properties.BindingData_ClipSource;
            HashSet<CurveBindingData> removeBinding = new(removeBindings.Length);

            var length = removeBindings.Length;
            var length_all = existBindings.Length;

            for (int i = 0; i < length; i++)
            {
                for (int k = 0; k < length_all; k++)
                {
                    if (removeBinding.Contains(existBindings[k]))
                        continue;

                    var existBinding = existBindings[k];
                    if (existBinding.binding.path == removeBindings[i].Path && existBinding.binding.propertyName == removeBindings[i].PropertyName && existBinding.binding.type == removeBindings[i].Type)
                        removeBinding.Add(existBinding);
                }
            }

            if (DummyClip != null)
            {
                foreach (var binding in removeBinding)
                {
                    AnimationUtility.SetEditorCurve(DummyClip, binding.binding, null);
                }
            }

            existBindings = existBindings.RemoveDuplicates(removeBinding);
            TreeView_Properties.BindingData_ClipSource = existBindings;
            TreeView_Properties.Reload();
            SetBindingDatas(existBindings);
            ForceRepaint(0f);
        }

        /// <summary>
        /// Remove property.
        /// </summary>
        public virtual void RemoveProperty(params CurveBindingData[] removeDatas)
        {
            if (TreeView_Properties == null)
                return;

            var datas = TreeView_Properties.BindingData_ClipSource.ToList();

            var hasClip = DummyClip != null;

            HashSet<CurveBindingData> removedDatas = new(datas.Count);

            foreach (var data in removeDatas)
            {
                if (removedDatas.Contains(data))
                    continue;

                var binding = data.binding;

                if (binding.type == typeof(Transform))
                {
                    if (binding.propertyName.StartsWith(CurveBinding.LocalPosition))
                        binding.propertyName = CurveBinding.LocalPosition;
                    else if (binding.propertyName.StartsWith(CurveBinding.LocalRotation))
                        binding.propertyName = CurveBinding.LocalRotation;
                    else if (binding.propertyName.StartsWith(CurveBinding.LocalScale))
                        binding.propertyName = CurveBinding.LocalScale;
                    else if (binding.propertyName.StartsWith(CurveBinding.EulerAngles))
                        binding.propertyName = CurveBinding.EulerAngles;
                    else
                    {
                        Debug.LogException(new NotImplementedException($"Unknow transform binding [{binding.propertyName}]."));
                    }

                    if (hasClip)
                    {
                        DummyClip.SetCurve(binding.path, binding.type, binding.propertyName, null);
                    }

                    var removeTargets = datas.RemoveWhere(x =>
                    x.binding.type == binding.type &&
                    x.binding.path == binding.path &&
                    x.binding.propertyName.StartsWith(binding.propertyName));
                    removedDatas.AddRange(removeTargets);
                    Debug.Log($"Remove by group [{binding.path}]\n" + string.Join("\n", removeTargets.Select(x => $"[{x.binding.propertyName}]")));
                }
                else if (data.binding.type == typeof(Animator))
                {
                    if (char.Equals(binding.propertyName[^2], '.'))
                    {
                        binding.propertyName = binding.propertyName[..^3];
                    }

                    //if (binding.propertyName.EndsWith(".x") || )
                    //    binding.propertyName = binding.propertyName.Replace(".x", string.Empty);
                    //else if (binding.propertyName.EndsWith(".y"))
                    //    binding.propertyName = binding.propertyName.Replace(".y", string.Empty);
                    //else if (binding.propertyName.EndsWith(".z"))
                    //    binding.propertyName = binding.propertyName.Replace(".z", string.Empty);
                    //else if (binding.propertyName.EndsWith(".w"))
                    //    binding.propertyName = binding.propertyName.Replace(".w", string.Empty);
                    //else
                    //{
                    //    //Debug.LogException(new NotImplementedException($"Unknow transform binding [{binding.propertyName}]."));
                    //}

                    var removeTargets = datas.RemoveWhere(x =>
                    x.binding.type == binding.type &&
                    x.binding.path == binding.path &&
                    x.binding.propertyName.StartsWith(binding.propertyName));
                    removedDatas.AddRange(removeTargets);
                    Debug.Log($"Remove by group [{binding.path}] [{binding.propertyName}]\n" + string.Join("\n", removeTargets.Select(x => $"[{x.binding.propertyName}]")));
                }
                else
                {
                    datas.Remove(data);
                }

                if (hasClip)
                {
                    DummyClip.SetCurve(binding.path, binding.type, binding.propertyName, null);
                }
            }

            Debug.Log(datas.Count);
            TreeView_Properties.BindingData_ClipSource = datas.ToArray();
            TreeView_Properties.Reload();
            Debug.Log(TreeView_Properties.BindingData_ClipSource.Length);
            SetBindingDatas(TreeView_Properties.BindingData_ClipSource);
            ForceRepaint();
        }

        /// <summary>
        /// Remove property.
        /// </summary>
        public virtual void EditPropertyPath(params CurveBindingData[] removeDatas)
        {
            if (TreeView_Properties == null)
                return;

            var datas = TreeView_Properties.BindingData_ClipSource;
            datas = datas.RemoveDuplicates(removeDatas);

            if (DummyClip != null)
            {
                var length = removeDatas.Length;
                for (int i = 0; i < length; i++)
                {
                    var data = removeDatas[i];
                    var binding = data.binding;

                    DummyClip.SetCurve(binding.path, binding.type, binding.propertyName, null);
                }
            }

            TreeView_Properties.BindingData_ClipSource = datas;
            TreeView_Properties.Reload();
            SetBindingDatas(datas);
            ForceRepaint();
        }

        #endregion

        #region Search Property
        /// <summary>
        /// Search field for property panel.
        /// </summary>
        protected SearchField SearchField_properties;

        /// <summary>
        /// Keyword to filter property group.
        /// </summary>
        [SerializeField, HideInInspector]
        string m_property_filter;

        /// <summary>
        /// Draw property filter field.
        /// </summary>
        protected virtual void DrawPropertySearchBar()
        {
            var height_line = EditorGUIUtility.singleLineHeight;
            var rect_bar = rect_property_vision.AlignTopOut(height_line).Expand(-2, -15, -1, -1);

            //GUI.DrawTexture(rect_bar.AlignLeft(height_line), texture_property_searchBar_icon);

            {
                EditorGUI.BeginChangeCheck();
                var filter = SearchField_properties.OnToolbarGUI(rect_bar, m_property_filter);
                if (EditorGUI.EndChangeCheck())
                {
                    m_property_filter = filter;
                    if (TreeView_Properties != null)
                        TreeView_Properties.searchString = filter;
                }
            }
        }

        #endregion

        #region Property Panel

        /// <summary>
        /// Rect of property layout.
        /// </summary>
        protected Rect rect_property_content;

        /// <summary>
        /// Tab menu index for property editing. **Inspector **Binding
        /// </summary>
        protected int PropertyPanelIndex = 0;

        /// <summary>
        /// Context for property menu tabs.
        /// </summary>
        protected GUIContent[] Labels_PropertyPanel;

        /// <summary>
        /// TreeView state for property panel.
        /// </summary>
        protected TreeViewState state_property = new();

        /// <summary>
        /// TreeView of animation property.
        /// </summary>
        protected CurveBindingTreeView TreeView_Properties;

        /// <summary>
        /// Setup propert tree view.
        /// </summary>
        protected virtual void Setup_Property(AnimationClip clip)
        {
            if (TreeView_Properties == null)
            {
                if (state_property == null)
                    state_property = new();

                Debug.Log("Initialize TreeView");

                if (Director != null && Director.Director != null && Director.Director.animator != null)
                    TreeView_Properties = new(state_property, Director.Director.animator.gameObject, clip);
                else
                    TreeView_Properties = new(state_property, clip);

                SearchField_properties.downOrUpArrowKeyPressed -= TreeView_Properties.SetFocusAndEnsureSelectedItem;
                SearchField_properties.downOrUpArrowKeyPressed += TreeView_Properties.SetFocusAndEnsureSelectedItem;

                TreeView_Properties.Reload();
                TreeView_Properties.CustomDrawer = DrawCustomPropertyRow;
                TreeView_Properties.OnEditBindingPath += EditPropertyPath;
                TreeView_Properties.OnRemoveBinding += RemoveProperty;
            }
            else
            {
                Debug.LogException(new InvalidProgramException("Reload TreeView"));
                TreeView_Properties.Reload();
            }
        }


        /// <summary>
        /// Draw clip property. **Type **Name **Path
        /// </summary>
        protected virtual void DrawPropertyPanel()
        {
            if (DummyClip == null)
            {
                EditorGUI.DrawRect(rect_property_vision, Color.white.SetAlpha(0.1f));
                GUI.Label(rect_property_vision, "Please setup editing animation clip.", style_universal_middle);
            }
            else
            {
                if (PropertyPanelIndex == 0)
                {
                    DrawPropertySearchBar();

                    if (TreeView_Properties != null)
                        TreeView_Properties.OnGUI(rect_property_vision.Expand(0, -5, 0, 0));

                    if (state_property.scrollPos != scroll_property)
                    {
                        scroll_property = state_property.scrollPos;
                        scroll_panel_curve.y = state_property.scrollPos.y;
                    }
                }
                else if (PropertyPanelIndex == 1)
                {
                    DrawBindingPropertyTreeGUI(rect_property_vision.Expand(0, -5, 0, 0));
                }
            }

            // dev GUI
            EditorGUI.DrawRect(rect_property_vision.AlignRight(4.3f).SubYMin(height_bar_offset), Color.black.SetAlpha(0.13f));

            // property tab GUI
            {
                PropertyPanelIndex = GUI.Toolbar(rect_property_toolbar.Expand(-5, -15, -3, -3), PropertyPanelIndex, Labels_PropertyPanel);
            }
        }

        /// <summary>
        /// Draw custom GUI for property treeview.
        /// </summary>
        protected virtual void DrawCustomPropertyRow(Rect rect, CurveBindingData bindingData)
        {
            if (!dic_bindingDatas.TryGetValue(bindingData, out var curveData))
                return;

            // edit value
            {
                var time = TimeTrack.GetTime();
                float value;

                if (curveData.curve != null)
                {
                    if (TryGetKey(curveData, time, out var frameData))
                        value = frameData.Keyframe.value;
                    else
                        value = curveData.curve.Evaluate(time);
                }
                else
                    value = 0f;

                {
                    EditorGUI.BeginChangeCheck();
                    var newValue = EditorGUI.DelayedFloatField(rect.AlignRight(80).SubX(10), value);
                    if (EditorGUI.EndChangeCheck())
                    {
                        var id = GUIUtility.GetControlID(FocusType.Keyboard);
                        GUIUtility.keyboardControl = 0;

                        Debug.Log($"time[{time}] {bindingData.binding.propertyName} : {value}->{newValue}");

                        if (TryGetKey(curveData, time, out var frameData))
                            EditKey(curveData, time, newValue);
                        else
                            AddKey(curveData, time, newValue);
                    }
                }
            }

            // GUI focus for selected property
            if (Events.IsType(EventType.Repaint) && SelectedBindingData.Contains(bindingData))
            {
                EditorGUI.DrawRect(rect, Color.white.SetAlpha(0.2f));
            }
        }

        #endregion


        #endregion

        #region Curve Panel

        /// <summary>
        /// Constant value for GUI offset.
        /// </summary>
        [NonSerialized]
        protected float offset_curvePanel_content = 30;

        [SerializeField, HideInInspector]
        protected int CurvePanelIndex;

        GUIContent[] Labels_CurvePanel;

        /// <summary>
        /// Draw clip contents.
        /// </summary>
        protected virtual void DrawKeyFramesPanel()
        {
            if (TreeView_Properties == null || TreeView_Properties.Rects == null)
            {
                GUI.Label(rect_curve, "Missing property bindings.", style_universal_middle);
            }
            else
            {
                var width_panel = TimeTrack.rect_scrollContent.width;
                var height_line = height_bar_offset;

                var rect_curves_scroll_vision = rect_curve.
                  AddYMin(height_line).
                  AddYMax(height_line);

                var rect_curves_content = new Rect().
                    SetWidth(width_panel);

                if (CurvePanelIndex == 0)
                    DrawDopesheetArea(rect_curves_scroll_vision, rect_curves_content.SetHeight(TreeView_Properties.totalHeight));
                else if (CurvePanelIndex == 1)
                    DrawCurveArea(rect_curves_scroll_vision, rect_curves_content.SetHeight(rect_curves_scroll_vision.height - height_line - 2f));
            }

            {
                CurvePanelIndex = GUI.Toolbar(rect_curve_toolbar.Expand(-5, -15, -3, -3), CurvePanelIndex, Labels_CurvePanel);
            }
        }



        /// <summary>
        /// Draw clip contents display by keyframes.
        /// </summary>
        protected virtual void DrawDopesheetArea(Rect rect_vision, Rect rect_content)
        {
            var rects = TreeView_Properties.Rects;
            var length_properties = rects.Length;


            float offset_start = offset_curvePanel_content;

            // prepare GUI options
            var style_box = GUI.skin.box;
            var height_line = height_bar_offset;

            var width_key = 13;
            var offset_key = 0.5f * width_key;

            var keyFramePos = TimeTrack.positions;

            var isPointerInRect = Events.PointerInRect(rect_vision.AddXMin(offset_start - 10));
            var isMouse = Events.IsType(EventType.MouseDown, EventType.MouseUp, EventType.MouseDrag);
            var hasMatchRightClick = Events.IsMouse(EventType.MouseUp, 1);

            // top key bar
            {
                var rect_bar_key = rect_vision.AlignTopOut(height_line).AlignCenterY(width_key);
                var hasFocus = Events.PointerInRect(rect_bar_key);
                var rect_key = rect_bar_key.SetWidth(width_key).SubY(rect_bar_key.y);
                var length_frames = keyFramePos.Length;

                {
                    GUI.BeginClip(rect_bar_key);
                    for (int i = 0; i < length_frames; i++)
                    {
                        rect_key.x = keyFramePos[i] - rect_bar_key.x - offset_key - scroll_panel_curve.x;
                        var selected = SelectedKeyFrameIndexes.Contains(i);

                        // draw key bar GUI
                        if (cache_existKeyframes[i])
                        {
                            if (selected && isDragginKeyFrame)
                                continue;

                            var label = selected ? content_keyframe_selected : content_keyframe_default;
                            GUI.DrawTexture(rect_key, label.image);
                        }

                        // handle key bar operation
                        if (isMouse && hasFocus && Events.PointerInRect(rect_key))
                        {
                            if (Events.IsMouse())
                            {
                                if (listener_crossSelection.isKey)
                                {
                                    if (SelectedKeyFrameIndexes.Count > 0)
                                    {
                                        // cross selection
                                        var min = Mathf.Min(SelectedKeyFrameIndexes.Min(), i);
                                        var max = Mathf.Max(SelectedKeyFrameIndexes.Max(), i);
                                        for (int q = min; q <= max; q++)
                                        {
                                            SelectKeyframes(q);
                                        }
                                    }
                                    else
                                    {
                                        if (selected)
                                            DeSelectKeyframes(i);
                                        else
                                            SelectKeyframes(i);
                                    }
                                }
                                else
                                {
                                    if (!listener_multiSelection.isKey)
                                        ClearSelectedKeyFrame();

                                    if (selected)
                                        DeSelectKeyframes(i);
                                    else
                                        SelectKeyframes(i);
                                }

                                Events.Use();
                            }
                            else if (Events.IsMouse(button: 1))
                            {
                                // add keyframes or remove keyframes
                                var menu = new GenericMenu();
                                var index = i;
                                menu.AddItem(new GUIContent($"Add Keys At [{index}]"), false, (x) => AddKeys((int)x), index);
                                menu.AddItem(new GUIContent($"Remove Keys From [{index}]"), false, (x) => DeleteKeys((int)x), index);

                                menu.ShowAsContext();

                                Events.Use();
                            }
                            else if (Events.IsMouse(EventType.MouseDrag))
                            {
                                //Debug.Log("Dragging");
                            }
                        }
                    }

                    // draw draggin bar
                    if (isDragginKeyFrame)
                    {
                        for (int i = 0; i < length_frames; i++)
                        {
                            var selected = SelectedKeyFrameIndexes.Contains(i);

                            if (!selected)
                                continue;

                            // draw key bar GUI
                            if (cache_existKeyframes[i])
                            {
                                var targetIndex = i + draggingOffsetIndex;
                                if (keyFramePos.Length > targetIndex)
                                {
                                    rect_key.x = keyFramePos[targetIndex] - offset_key - rect_bar_key.x - scroll_panel_curve.x;
                                    GUI.DrawTexture(rect_key, content_keyframe_selected.image);
                                }
                            }
                        }
                    }
                    GUI.EndClip();
                }
            }

            // custom scroll start
            {
                Vector2 newScroll = scroll_panel_curve;

                //if (isPointerInRect && Events.IsType(EventType.ScrollWheel))
                //{
                //    var scroll = Event.current.delta;
                //    const float sensitivity = 15f;

                //    //Debug.Log("Scroll In DopeSheet Area.");
                //    if (isScrollHorizontal)
                //    {
                //        newScroll.x = Mathf.Clamp(scroll_panel_curve.x - scroll.y * sensitivity, 0, float.MaxValue);
                //        //TimeTrack.scroll_timeTrack.x = scroll_panel_curve.x;
                //        //Debug.Log($"scroll horizontal {scroll_panel_curve}");
                //    }
                //    else
                //    {
                //        newScroll.y = Mathf.Clamp(scroll_panel_curve.y + scroll.y * sensitivity, 0, float.MaxValue);
                //        //Debug.Log($"scroll veritcal {scroll_panel_curve}");
                //    }

                //    //Events.Use();
                //}

                newScroll = GUI.BeginScrollView(rect_vision, newScroll, rect_content, true, true);
                if (scroll_panel_curve != newScroll)
                {
                    Debug.Log($"New scroll [{newScroll}].");
                    if (scroll_panel_curve.x != newScroll.x)
                    {
                        if (TimeTrack != null)
                            TimeTrack.scroll_timeTrack.x = newScroll.x;

                        scroll_panel_curve.x = newScroll.x;
                    }

                    if (scroll_panel_curve.y != newScroll.y)
                    {
                        if (state_property != null)
                            state_property.scrollPos.y = newScroll.y;

                        scroll_property.y = newScroll.y;
                        scroll_panel_curve.y = newScroll.y;
                    }
                }
            }

            // cache GUI color
            //using var colorCache = EditorUIColorCache.GetCache(() => GUI.color, x => GUI.color = x);

            // dev GUI
            if (Events.IsType(EventType.Repaint))
            {
                var rect_dev = rect_content.AddX(offset_start);
                EditorGUI.DrawRect(rect_dev.AlignLeft(1), Color.blue);
                EditorGUI.DrawRect(rect_dev.AlignBottom(1), Color.green);
                EditorGUI.DrawRect(rect_dev.AlignRight(1), Color.red);
                EditorGUI.DrawRect(rect_dev.AlignTop(1), Color.yellow);
            }

            var offsetY = height_line * 0.15f;

            var rect_horizontal = rect_content.
                SetHeight(height_line - offsetY).
                SetWidth(Mathf.Max(rect_vision.width, rect_content.width)).
                Expand(-10, -15, 0, 0);

            // draw back ground
            if (Events.IsType(EventType.Repaint))
            {
                EditorGUI.DrawRect(rect_content.Expand(-5, -10, 5, 0), Color.black.SetAlpha(0.1f));

                // draw horizontal graph
                {
                    var color = TreeView_Properties.color_split;
                    var color_dark = TreeView_Properties.color_dark;

                    for (int i = 0; i < length_properties; i++)
                    {
                        var rect_bar = rects[i].
                            SetXMin(rect_horizontal.xMin).
                            SetXMax(rect_horizontal.xMax);

                        // dev 
                        {
                            if (i % 2 == 0)
                                EditorGUI.DrawRect(rect_bar, color);
                            else
                                EditorGUI.DrawRect(rect_bar, color_dark);

                            //GUI.Label(rect.AlignLeft(50), $"[{i}]");
                        }
                    }
                }

                // draw vertical lines
                {
                    var keyFrames = TimeTrack.positions;
                    var width = 1.5f;
                    var widthOffset = width * 0.5f;
                    var rect_vertical = new Rect(rect_content).SetWidth(width).Expand(0, -4);
                    var color = Color.white.SetAlpha(0.15f);

                    foreach (var pos_x in keyFrames)
                    {
                        rect_vertical.x = pos_x - widthOffset - rect_vision.x;
                        EditorGUI.DrawRect(rect_vertical, color);
                    }
                }
            }

            // draw dopesheet data
            {
                var bindingDatas = TreeView_Properties.BindingData_GUITargets;
                if (bindingDatas.Length == rects.Length)
                {
                    // cache click result for performance
                    var hasMatchClick = true;

                    // apply dragging offset
                    var dragginOffsetX = 0f;
                    if (isDragginKeyFrame)
                    {
                        if (SelectedKeyFrames.Count > 0)
                            dragginOffsetX = GetDragginKeyOffset(rect_vision.x);
                        else
                            isDragginKeyFrame = false;
                    }

                    // draw curve data
                    for (int i = 0; i < length_properties; i++)
                    {
                        if (bindingDatas[i] == null)
                            continue;

                        //GUI.Label(rect.AlignLeft(180), $"[{i}] {datas[i].binding.propertyName}") ;

                        if (dic_bindingDatas.TryGetValue(bindingDatas[i], out var curveData))
                        {
                            var curve = curveData.curve;
                            var keys = curveData.Keyframes;
                            var keyRect = new Rect(0, rects[i].position.y, width_key, width_key);

                            var keyCount = keys.Count;

                            var selected = SelectedKeyFrameIndexes.Contains(i);

                            // draw default keyframe
                            for (int index_key = 0; index_key < keyCount; index_key++)
                            {
                                var index_keyScale = keys[index_key].KeyframeIndex;

                                // selection state
                                {
                                    selected = SelectedKeyFrames.Contains(keys[index_key]);
                                }

                                // evaluate GUI position
                                {
                                    float pos;
                                    try
                                    {
                                        if (selected && isDragginKeyFrame)
                                        {
                                            continue;
                                            //pos = dragginOffsetX - offset_key - rect_vision.x;
                                        }
                                        else
                                            pos = keyFramePos[index_keyScale] - offset_key - rect_vision.x;
                                    }
                                    catch (Exception e)
                                    {
                                        Debug.Log($"Clip Length [{DummyClip.length}]\nFrame Count [{TimeTrack.FrameCount + 1}]\n" + string.Join("\n", curveData.curve.keys.Select(x => x.time)));
                                        Debug.LogError($"[{curveData.BindingData.binding.type}-{curveData.BindingData.binding.path}] {index_keyScale}/{keyFramePos.Length}.");
                                        Assert.IsFalse(keys.ValidRepeat(x => x.KeyframeIndex));
                                        Debug.Log(string.Join("\n", keys.Select(x => x.KeyframeIndex)));
                                        throw e;
                                    }

                                    keyRect.x = pos;
                                }

                                // check rect selection
                                if (hasSelectedRect && SelectedRect.Contains(keyRect.center))
                                {
                                    temp_RectSelectionKeyframes.Add(keys[index_key]);
                                    Debug.Log($"On rect selection [{keys[index_key].KeyframeIndex}]");
                                }

                                // draw keyframe GUI
                                {
                                    var label = selected ? content_keyframe_selected : content_keyframe_default;
                                    GUI.DrawTexture(keyRect, label.image);
                                }

                                // apply keyframe interaction
                                if (isPointerInRect)
                                {
                                    if (hasMatchRightClick && Events.PointerInRect(keyRect))
                                    {
                                        // right click
                                        var menu = new GenericMenu();
                                        menu.AddDisabledItem(new GUIContent($"Add Key - Already Exist [{keys[index_key].KeyframeIndex}]"), false);
                                        // delete keyframe
                                        menu.AddItem(new GUIContent($"Remove Key [{keys[index_key].KeyframeIndex}]"), false, (x) => DeleteKeys(x as KeyFrameData), keys[index_key]);
                                        menu.ShowAsContext();
                                        Events.Use();
                                    }
                                    else if (hasMatchClick && Events.IsMouse() && Events.PointerInRect(keyRect))
                                    {
                                        // left click
                                        {
                                            var key = keys[index_key];
                                            Debug.Log(
                                                //$"{Event.current.type}\n" +
                                                $"[sn:{index_key} frame:{index_keyScale}]\n" +
                                                $"[t:{key.Keyframe.time}]\n" +
                                                $"[in:{key.Keyframe.inTangent} weight:{key.Keyframe.inWeight}]\n" +
                                                $"[out:{key.Keyframe.inTangent} weight:{key.Keyframe.outWeight}]\n" +
                                                $"[property:{bindingDatas[i]?.binding.propertyName} value:{keys[index_key].Keyframe.value}]");
                                        }

                                        hasMatchClick = false;
                                        Events.Use();

                                        EditorGUI.DrawRect(keyRect, Color.cyan);

                                        if (listener_crossSelection.isKey)
                                        {
                                            var maxSelectedIndex = keys.Max(x => SelectedKeyFrames.Contains(x) ? x.KeyframeIndex : -1);
                                            var minSelectedIndex = keys.Max(x => SelectedKeyFrames.Contains(x) ? x.KeyframeIndex : -1);

                                            maxSelectedIndex = Mathf.Max(index_key, maxSelectedIndex);
                                            minSelectedIndex = minSelectedIndex < 0 ? index_key : Mathf.Min(index_key, minSelectedIndex);
                                            // bug - ArgumentException: Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.
                                            var addKeys = keys.GetRange(minSelectedIndex, maxSelectedIndex - minSelectedIndex + 1);
                                            foreach (var key in addKeys)
                                            {
                                                AddSelectedKeyFrame(key);
                                            }
                                        }
                                        else if (!selected)
                                        {
                                            ToggleKeyframeSelection(keys[index_key]);
                                        }
                                    }
                                }
                            }

                            // draw selected(draggin) keyframe
                            if (isDragginKeyFrame)
                            {
                                Rect rect_wrap = new();

                                for (int k = 0; k < keyCount; k++)
                                {
                                    var index = keys[k].KeyframeIndex;

                                    // selection state
                                    {
                                        selected = SelectedKeyFrames.Contains(keys[k]);
                                    }

                                    if (!selected)
                                        continue;

                                    // evaluate GUI position
                                    {
                                        keyRect.x = keyFramePos[index] - offset_key - rect_vision.x + dragginOffsetX;
                                        if (rect_wrap.position == Vector2.zero)
                                            rect_wrap = keyRect.SetSize(Vector2.zero);
                                        else
                                            rect_wrap = rect_wrap.ExpandTo(keyRect.position);
                                    }

                                    // draw keyframe GUI
                                    {
                                        GUI.DrawTexture(keyRect, content_keyframe_selected.image);
                                    }
                                }

                                EditorGUI.DrawRect(rect_wrap, Color.green.SetAlpha(0.2f));
                            }
                        }
                    }
                }
            }

            if (hasSelectedRect)
            {
                hasSelectedRect = false;

                if (!listener_multiSelection.isKey)
                    ClearSelectedKeyFrame();

                foreach (var key in temp_RectSelectionKeyframes)
                    AddSelectedKeyFrame(key);

                temp_RectSelectionKeyframes.Clear();
                Debug.Log(nameof(hasSelectedRect));
            }
            else if (isPointerInRect)
            {
                if (isMouse)
                {
                    if (Events.IsType(EventType.MouseDrag) && Event.current.button == 0)
                    {
                    setDrag:
                        if (isDragginKeyFrame)
                        {
                            // update keyframe dragging
                            Events.Use();
                            keyframe_EndPosition = Event.current.mousePosition;
                            //Debug.Log($"Dragging KeyFrames.");
                        }
                        else if (!listener_multiSelection.isKey && SelectedKeyFrames.Count > 0)
                        {
                            // prepare keyframe dragging
                            isDragginKeyFrame = true;
                            draggingOffsetIndex = 0;
                            keyframe_BeginPosition = Event.current.mousePosition;

                            goto setDrag;
                        }
                        else
                        {
                            // pass event to rect selection - drag
                        }
                    }
                    else if (Events.IsType(EventType.MouseDown) && Event.current.button == 0)
                    {
                        Debug.Log(Event.current.type);

                        if (SelectedKeyFrames.Count == 0 || listener_multiSelection.isKey)
                        {
                            // pass event to rect selection - mouse down
                        }
                        else
                        {
                            // register move key start position
                            Events.Use();
                            keyframe_BeginPosition = Event.current.mousePosition;

                            if (!RectSelection.selecting)
                                ClearSelectedKeyFrame();
                        }
                    }
                    else if (Events.IsType(EventType.MouseUp))
                    {
                        Debug.Log($"{Event.current.type} [{Event.current.button}]");

                        if (Event.current.button == 1)
                        {
                            // match key exist
                            var canDelete = SelectedKeyFrames.Count > 0;

                            // add or delete keyframe
                            var menu = new GenericMenu();

                            {

                                var pos = Event.current.mousePosition;
                                // search x with time index
                                var index_x = keyFramePos.FirstIndexOf(x => x - rect_vision.x > pos.x);
                                //Debug.LogError($"{index_x}_{pos.x}/{keyFramePos[index_x<0?0:index_x]-rect_vision.x}");
                                if (index_x > -1)
                                {
                                    if (Mathf.Abs(pos.x - (keyFramePos[Mathf.Max(0, index_x - 1)] - rect_vision.x)) < Mathf.Abs(keyFramePos[index_x] - rect_vision.x - pos.x))
                                    {
                                        index_x--;
                                        //Debug.Log($"{Mathf.Abs(pos.x - (keyFramePos[Mathf.Max(0, index_x - 1)] - rect_vision.x))} / { Mathf.Abs(keyFramePos[index_x] - rect_vision.x - pos.x)}");
                                    }
                                }

                                var index_y = rects.FirstIndexOf(r => r.y + width_key > pos.y);
                                if (index_y > -1 && index_x > -1)
                                {
                                    var datas_inspecting = TreeView_Properties.BindingData_GUITargets;

                                    if (datas_inspecting[index_y] == null)
                                    {
                                        // add by group comps
                                        menu.AddItem(new GUIContent($"Add Group Keys"), false, () => Debug.LogError($"[Todo] : Add group keys{index_x}"));
                                    }
                                    else if (dic_bindingDatas.TryGetValue(datas_inspecting[index_y], out var curveData))
                                    {
                                        // check exist key
                                        if (curveData.Keyframes.Exist(x => x.KeyframeIndex == index_x))
                                        {
                                            menu.AddDisabledItem(new GUIContent($"Add Key - Already Exist [{index_x}]"));
                                        }
                                        else
                                        {
                                            menu.AddItem(new GUIContent($"Add Keys {curveData.BindingData.binding.propertyName} at [{index_x}]"), false, () => AddKey(curveData, TimeTrack.GetTime(index_x)));
                                        }
                                    }
                                }
                                else
                                {
                                    menu.AddDisabledItem(new GUIContent($"Add Key [{index_x},{index_y}]"));
                                }
                                // search y with property position
                            }

                            if (canDelete)
                                menu.AddItem(new GUIContent($"Remove Keys [{SelectedKeyFrames.Count}]"), false, (x) => DeleteKeys(x as KeyFrameData[]), SelectedKeyFrames.ToArray());
                            else
                                menu.AddDisabledItem(new GUIContent($"Remove Keys"));

                            menu.ShowAsContext();

                            Events.Use();
                        }
                        else if (SelectedKeyFrames.Count > 0)
                        {
                            if (isDragginKeyFrame)
                            {
                                Events.Use();

                                // confirm move keys
                                isDragginKeyFrame = false;
                                keyframe_EndPosition = Event.current.mousePosition;

                                // caculate offset time
                                var timeOffset = (1f / TimeTrack.FrameRate) * draggingOffsetIndex;
                                //TimeTrack.GetTime(Mathf.Abs(draggingOffsetIndex)) * Mathf.Sign(draggingOffsetIndex);
                                Debug.Log($"Confirm dragging offset frame [{draggingOffsetIndex}] offset time [{timeOffset}].");

                                MoveKeyframes(timeOffset, SelectedKeyFrames.ToArray());
                            }
                            else if (RectSelection.selecting || listener_multiSelection.isKey)
                            {

                            }
                            else
                            {
                                Events.Use();
                            }
                        }
                        else
                        {
                            // none selected
                        }
                    }
                }

                if (SelectedKeyFrames.Count == 0 || listener_multiSelection.isKey)
                    RectSelection.ProcessSelection();

                // dev GUI
                {
                    //if (isDragginKeyFrame)
                    //    EditorGUI.DrawRect(GUIUtil.PointsToRect(keyframe_BeginPosition, keyframe_EndPosition), Color.red.SetAlpha(0.3f));
                }
            }
            else
            {
                //Debug.Log($"Pointer out of bounce. {Event.current.type}");
            }

            // custom scroll stop
            {
                GUI.EndScrollView();
            }

            {
                DrawTimeIndicator(rect_vision);
            }
        }

        /// <summary>
        /// Cache curve data for selected GUI.
        /// </summary>
        CurveData lastCurveData;

        Curve2D curve_preview;

        protected GUIStyle style_boxBackground;

        /// <summary>
        /// Draw clip contents display by animation curve.
        /// </summary>
        protected virtual void DrawCurveArea(Rect rect_vision, Rect rect_content)
        {

            // draw back ground
            if (Events.IsType(EventType.Repaint))
            {
                GUI.BeginClip(rect_vision);
                EditorGUI.DrawRect(rect_content.Expand(0, 0, 5, 0).SetX(offset_curvePanel_content), Color.yellow.SetAlpha(0.1f));
                //UnityEditorInternal.AnimationWindowKeyframe
                var middle = rect_vision.height * 0.5f;
                var room = 1f;
                // draw horizontal lines - value scale
                {
                    var color = Color.white.SetAlpha(0.3f);
                    var color_scale = color.SetAlpha(0.5f);
                    var rect_bar = rect_content.SetHeight(1f).SetX(offset_curvePanel_content);

                    if(rect_bar.width<rect_vision.width)
                        rect_bar.width = rect_vision.width;

                    rect_bar.y = 0;
                    var offset_y = rect_content.height / 20f;

                    for (int i = 0; i < 20; i++)
                    {
                        if (i % 5 == 0)
                        {
                            EditorGUI.DrawRect(rect_bar, color_scale);
                            GUI.Label(rect_bar.AlignLeftOut(offset_curvePanel_content), $"{(((float)i) * 0.05f)}");
                        }
                        else
                            EditorGUI.DrawRect(rect_bar, color);

                        rect_bar.y += offset_y;
                    }

                    //for (int i = 0; i < length_properties; i++)
                    //{
                    //    var rect_bar = rects[i].
                    //        SetXMin(rect_horizontal.xMin).
                    //        SetXMax(rect_horizontal.xMax);

                    //    // dev 
                    //    {
                    //        if (i % 2 == 0)
                    //            EditorGUI.DrawRect(rect_bar, color);
                    //        else
                    //            EditorGUI.DrawRect(rect_bar, color_dark);

                    //        //GUI.Label(rect.AlignLeft(50), $"[{i}]");
                    //    }
                    //}
                }

                // draw vertical lines - time scale
                {
                    var keyFrames = TimeTrack.positions;
                    var width = 1.5f;
                    var widthOffset = width * 0.5f;
                    var rect_vertical = new Rect(rect_content).SetWidth(width).Expand(0, -4);
                    var color = Color.white.SetAlpha(0.15f);

                    foreach (var pos_x in keyFrames)
                    {
                        rect_vertical.x = pos_x - widthOffset - rect_vision.x;
                        EditorGUI.DrawRect(rect_vertical, color);
                    }
                }
                GUI.EndClip();
            }

            var selectedBindings = TreeView_Properties.BindingDatas_Selected;
            if (selectedBindings.IsNullOrEmpty() || !selectedBindings.Exist(x => x != null))
            {
                GUI.Label(rect_vision, "Select Property Binding To Edit Animation Curve", style_universal_middle);
                DrawTimeIndicator(rect_vision);
                return;
            }


            if (!dic_bindingDatas.TryGetValue(selectedBindings.First(), out var data))
                return;


            if (lastCurveData != data && data != null)
            {
                if (data.curve == null)
                {
                    GUI.Label(rect_vision, "Selected Property Binding Has None Keyframe", style_universal_middle);
                }
                else
                {
                    lastCurveData = data;
                    var samples = Sample.GetSamples(data.curve);
                    curve_preview = new(samples);
                }

            }

            // draw curve
            if (curve_preview != null && lastCurveData != null)
            {
                var positions = TimeTrack.positions;
                //rect_content = rect_content.SetX(offset_curvePanel_content);
                //rect_content.SetXMin(positions.FirstOrDefault() - rect_vision.xMin);//.SetXMax(positions.LastOrDefault() - rect_vision.xMin);

                //EditorGUI.DrawRect(rect_vision, Color.yellow.SetAlpha(0.2f));

                if (false)
                {
                    //CurveEditorWindow

                    var newScroll = GUI.BeginScrollView(rect_vision, new Vector2(scroll_panel_curve.x, 0), rect_content);
                    if (newScroll.x != scroll_panel_curve.x)
                    {
                        if (TimeTrack != null)
                            TimeTrack.scroll_timeTrack.x = newScroll.x;

                        scroll_panel_curve.x = newScroll.x;
                    }

                    {
                        EditorGUI.BeginChangeCheck();

                        lastCurveData.curve = EditorGUI.CurveField(rect_content.Expand(0, -10).SetX(offset_curvePanel_content), lastCurveData.curve);

                        if (EditorGUI.EndChangeCheck())
                        {
                            Debug.Log($"{lastCurveData.BindingData.binding.propertyName}");
                        }
                    }

                    //EditorGUI.DrawRect(rect_content.Expand(0, -10).SetX(offset_curvePanel_content), Color.blue.SetAlpha(0.1f));

                    GUI.EndScrollView();
                }
                else
                {
                    //GUI.Box(rect_vision, GUIContent.none, style_boxBackground);

                    var newScroll = GUI.BeginScrollView(rect_vision, new Vector2(scroll_panel_curve.x, 0), rect_content);
                    if (newScroll.x != scroll_panel_curve.x)
                    {
                        if (TimeTrack != null)
                            TimeTrack.scroll_timeTrack.x = newScroll.x;

                        scroll_panel_curve.x = newScroll.x;
                    }
                    
                    var rect_content_adjust = rect_content.SetX(offset_curvePanel_content);

                    //GUI.BeginClip(rect_content.SetX(offset_curvePanel_content));
                    {
                        //DrawGrid(rect_content, rect_content.width / 20f, rect_content.height / 20f, 0.25f, new Color(0.5f, 0.5f, 0.5f, 1f));
                        //DrawGrid(rect_content, rect_content.width / 5f, rect_content.height / 5f, 0.35f, new Color(0.75f, 0.75f, 0.75f, 1f));
                    }
                    // dev
                    {
                        EditorGUI.DrawRect(rect_content_adjust.AlignRight(1f), Color.red);
                        EditorGUI.DrawRect(rect_content_adjust.AlignBottom(1f), Color.red);
                        EditorGUI.DrawRect(rect_content_adjust.AlignLeft(1f), Color.red);
                        EditorGUI.DrawRect(rect_content_adjust.AlignTop(1f), Color.red);
                    }
                    // draw curve scale
                    {

                    }

                    //GUI.EndClip();

                    {
                        Curve2DDrawer.DrawCurve(rect_content_adjust, curve_preview,-1,1);
                        Curve2DDrawer.DrawCurveHandles(rect_content_adjust, curve_preview);
                    }

                    GUI.EndScrollView();
                }
            }

            {
                DrawTimeIndicator(rect_vision);
            }
        }

        protected static void DrawGrid(Rect curveRect, float horizontalSpacing, float verticalSpacing, float gridOpacity, Color gridColor)
        {
            Vector2 size = curveRect.size * 8;
            int widthDivs = Mathf.CeilToInt(size.x / 0.2f / horizontalSpacing);
            int heightDivs = Mathf.CeilToInt(size.y / 0.2f / verticalSpacing);

            Handles.BeginGUI();
            Handles.color = new Color(gridColor.r, gridColor.g, gridColor.b, gridOpacity);

            //Vector3 newOffset = new Vector3(realPositionOffset.x % gridSpacing, realPositionOffset.y % gridSpacing, 0);
            Vector3 newOffset = curveRect.position - size * 4;

            for (int i = 0; i < widthDivs; i++)
            {
                //Handles.DrawLine(new Vector3(horizontalSpacing * i, -horizontalSpacing, 0) + newOffset, new Vector3(horizontalSpacing * i, size.y / 0.2f, 0f) + newOffset);
            }

            for (int j = 0; j < heightDivs; j++)
            {
                Handles.DrawLine(new Vector3(-verticalSpacing, verticalSpacing * j, 0) + newOffset, new Vector3(size.x / 0.2f, verticalSpacing * j, 0f) + newOffset);
            }

            Handles.color = Color.white;
            Handles.EndGUI();
        }

        /// <summary>
        /// Draw time track indicator at content area.
        /// </summary>
        protected virtual void DrawTimeIndicator(Rect rect)
        {
            var rect_timeIndicator = TimeTrack.rect_timeIndicator;
            EditorGUI.DrawRect(rect_timeIndicator.SetYMin(rect.yMin - height_bar_offset).SetYMax(rect.yMax - height_bar_offset), Color.white);
        }

        #endregion

        #region Preview

        [SerializeField, HideInInspector]
        bool m_enablePreview = true;

        public bool EnablePreview
        {
            get => m_enablePreview;
            set
            {
                if (m_enablePreview.SetAs(value))
                {
                    if (value)
                    {
                        StartPreviewTarget();
                        PreviewTarget(TimeTrack.Timeline.Time);
                    }
                    else
                        StopPreviewTarget();
                }
            }
        }

        /// <summary>
        /// Preview handle.
        /// </summary>
        [SerializeField, HideInInspector]
        GraphDirector m_director;
        public GraphDirector Director { get => m_director; set => m_director = value; }

        [SerializeField, HideInInspector]
        Animator m_animator;

        /// <summary>
        /// Mask for additive clip. **Bottom motion clip + Top posture clip
        /// </summary>
        [SerializeField, HideInInspector]
        AvatarMask AvatarMask;



        [SerializeField, HideInInspector]
        protected bool EnableMask = true;

        protected virtual void SetPlayableMask()
        {
            if (MixerPlayable.IsQualify())
            {
                if (EnableMask)
                    MixerPlayable.SetLayerMaskFromAvatarMask(1, AvatarMask);
                else
                    MixerPlayable.SetLayerMaskFromAvatarMask(1, new AvatarMask());
            }
        }

        [SerializeField, HideInInspector]
        float m_blendingWeight = 1f;
        [SerializeField, HideInInspector]
        bool m_additivePreview = false;

        public float BlendingWeight { get => m_blendingWeight; set => m_blendingWeight = value; }

        /// <summary>
        /// Blend editing clip(<see cref="DummyClip"/>) with previous animation(<see cref="SourceClip"/>).
        /// </summary>
        public virtual bool AdditivePreview
        {
            get => m_additivePreview;
            set
            {
                if (m_additivePreview.SetAs(value))
                    SetAdditiveAndWeight();
            }
        }

        /// <summary>
        /// Setup weight and additive for mixer playable.
        /// </summary>
        protected virtual void SetAdditiveAndWeight()
        {
            if (MixerPlayable.IsQualify() && MixerPlayable.GetInputCount() == 2)
            {
                MixerPlayable.SetLayerAdditive(1, AdditivePreview);

                var blendWeight_0 = EnableMask | AdditivePreview ? 1f : 1f - BlendingWeight;
                var blendWeight_1 = BlendingWeight;

                if (MixerPlayable.GetInput(0).IsQualify())
                    MixerPlayable.SetInputWeight(0, blendWeight_0);

                if (MixerPlayable.GetInput(1).IsQualify())
                {
                    MixerPlayable.SetInputWeight(1, blendWeight_1);
                    SetPlayableMask();
                }
            }

            if (Director != null && Director.Graph.IsValid())
                Director.Graph.Evaluate();
        }

        public Animator Animator { get => m_animator; set => m_animator = value; }

        protected AnimationClipPlayable ClipPlayable_Origin;
        protected AnimationClipPlayable ClipPlayable_Editing;

        /// <summary>
        /// Playable node to blending source clip and editing clip.
        /// </summary>
        protected AnimationLayerMixerPlayable MixerPlayable;

        /// <summary>
        /// Get playable that clip editor output animation stream.
        /// </summary>
        public virtual Playable GetAnimationSource()
        {
            return MixerPlayable;
        }

        /// <summary>
        /// Handles to read muscle value. 
        /// </summary>
        [HideInInspector]
        public NativeArray<MuscleHandle> muscleHandles;

        /// <summary>
        /// Muscle values.
        /// </summary>
        [HideInInspector]
        public NativeArray<float> muscleValues;

        /// <summary>
        /// Collect animation stream data. **Muscle?
        /// </summary>
        [HideInInspector]
        public Playable SamplePlayable;

        /// <summary>
        /// Setup playable preview handle.
        /// </summary>
        public virtual void InitializePreviewTarget(Animator animator)
        {
            var director = animator.InstanceIfNull<GraphDirector>(Director);
            InitializePreviewTarget(director);
        }

        /// <summary>
        /// Setup playable preview handle.
        /// </summary>
        public virtual void InitializePreviewTarget(GraphDirector graphDirector)
        {
            isTreeViewDirty = false;

            if (Director != graphDirector)
            {
                if (Director != null)
                    Director.Disable();

                Director = graphDirector;
            }

            if (Director != null)
            {
                Director.Enable();
                Director.Initialize();
                Animator = Director.Director?.animator;
            }
        }

        /// <summary>
        /// Begin preview animation.
        /// </summary>
        protected virtual void StartPreviewTarget()
        {
            if (TimeTrack != null)
            {
                TimeTrack.onTick -= PreviewTarget;
                TimeTrack.onTick += PreviewTarget;
            }

            // binding playable ** editing clip + source clip => layer mixer => sample job playable => output
            if (Director != null)
            {
                Director.Initialize();

                var graph = Director.Graph;

                if (!ClipPlayable_Origin.IsQualify())
                {
                    ClipPlayable_Origin = AnimationClipPlayable.Create(graph, SourceClip);
                }

                if (!ClipPlayable_Editing.IsQualify() && DummyClip != null)
                {
                    ClipPlayable_Editing = AnimationClipPlayable.Create(graph, DummyClip);
                }

                if (!MixerPlayable.IsQualify())
                {
                    MixerPlayable = AnimationLayerMixerPlayable.Create(graph);
                    MixerPlayable.SetPropagateSetTime(true);

                    if (ClipPlayable_Origin.IsQualify())
                        MixerPlayable.AddInput(ClipPlayable_Origin, 0, 1);

                    if (ClipPlayable_Editing.IsQualify())
                    {
                        // overwrite additive weighting
                        if (!AdditivePreview)
                            MixerPlayable.SetInputWeight(0, 1f - BlendingWeight);

                        MixerPlayable.AddInput(ClipPlayable_Editing, 0, BlendingWeight);
                        MixerPlayable.SetLayerAdditive(1, AdditivePreview);
                        SetPlayableMask();
                    }

                    MixerPlayable.Play();
                    MixerPlayable.Pause();
                }

                if (!SamplePlayable.IsQualify())
                {
                    if (CustomSampleJob != null)
                    {
                        // use custom recording job
                        SamplePlayable = CustomSampleJob(graph);
                    }
                    else
                    {
                        // use simple human recording job
                        var handels = HumanoidAnimationUtility.MuscleHandles;

                        if (!muscleHandles.IsCreated)
                            muscleHandles = new(handels, Allocator.Persistent);

                        if (!muscleValues.IsCreated)
                            muscleValues = new(handels.Length, Allocator.Persistent);

                        var job = new RecordHumanoidClipJob()
                        {
                            HumanScale = Director.Director.animator.humanScale,
                            muscleHandles = muscleHandles,
                            muscleValues = muscleValues,
                        };

                        SamplePlayable = AnimationScriptPlayable.Create(graph, job, 0);
                    }

                    SamplePlayable.AddInput(MixerPlayable, 0, 1);
                }

                Director.Director.Graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);

                Director.SetOutputPlayable(SamplePlayable);

                Director.Play();
            }
        }

        /// <summary>
        /// Custom AnimationScriptPlayable handle to resolve recording stream. 
        /// </summary>
        public delegate AnimationScriptPlayable SampleJob(PlayableGraph graph);
        /// <summary>
        /// Setup this handle to record playable stream like you want.
        /// </summary>
        [HideInInspector]
        public SampleJob CustomSampleJob;



        /// <summary>
        /// Stop preview animation.
        /// </summary>
        protected virtual void StopPreviewTarget()
        {
            if (TimeTrack != null)
            {
                TimeTrack.onTick -= PreviewTarget;
            }

            if (Director != null)
            {
                Director.Stop();
            }
        }

        /// <summary>
        /// Evaluate animation at time.
        /// </summary>
        protected virtual void PreviewTarget(float time)
        {
            if (!MixerPlayable.IsQualify())
                return;

            MixerPlayable.SetTime(time);
            Director.Director.Graph.Evaluate();
        }

        #endregion

        #region Modifier (Extension Control) **Character Director

        protected List<Playable> Modifiers = new(3);

        /// <summary>
        /// ?????
        /// </summary>
        /// <param name="insertPlayable"></param>
        public virtual void AddModifier(Playable insertPlayable)
        {
            if (!insertPlayable.IsQualify())
                return;

            if (SamplePlayable.IsQualify() && Modifiers.CheckAdd(insertPlayable))
            {
                Debug.Log($"{nameof(AddModifier)} [{MixerPlayable.IsQualify()}] [{SamplePlayable.IsQualify()}].");
                insertPlayable.Insert(MixerPlayable, SamplePlayable);
            }
        }

        /// <summary>
        /// Remove modifier.
        /// </summary>
        public virtual void RemoveModifier(Playable playable)
        {
            if (!playable.IsQualify())
                return;

            if (Modifiers.Remove(playable))
            {
                playable.Fuse();
            }
        }

        /// <summary>
        /// Remove modifiers.
        /// </summary>
        public virtual void RemoveModifiers(params Playable[] playables)
        {
            foreach (var modifier in playables)
            {
                if (modifier.IsQualify() && Modifiers.Remove(modifier))
                {
                    continue;
                }

                Debug.LogError($"Removing invalid modifier [{modifier}].");
            }

            if (MixerPlayable.IsQualify() && SamplePlayable.IsQualify())
                PlayableUtil.Fuse(MixerPlayable, SamplePlayable, playables);
        }

        #endregion

        #region Recording

        public virtual void DrawRecordingControl(Rect rect)
        {
            {
                GUILayout.BeginArea(rect);

                {
                    GUILayout.BeginHorizontal();

                    if (GUILayout.Button(content_control_record, options_control_buttons))
                    {
                        Debug.Log($"[ToDo] Begin auto recording");
                    }

                    GUILayout.EndHorizontal();
                }

                GUILayout.EndArea();
            }
        }

        #endregion

        #region File Operation

        /// <summary>
        /// Create empty output clip.
        /// </summary>
        public void CreateClip()
        {
            DummyClip = new AnimationClip() { name = "DummyClip" };
        }

        /// <summary>
        /// Create empty output clip with T-Pose.
        /// </summary>
        public void CreateEmptyClip()
        {
            DummyClip = EditorAnimationUtility.TPoseClip(Director.Director.animator);
        }

        /// <summary>
        /// Quick save clip.
        /// </summary>
        public virtual void SaveClip()
        {
            ArchiveClip(DummyClip);
        }

        /// <summary>
        /// Save clip as persistent asset.
        /// </summary>
        protected virtual void ArchiveClip(AnimationClip clip)
        {
            if (!clip.IsPersistentAsset())
                EditorAssetsUtility.Archive(clip, clip.name, folderout: true, EditorPathUtility.cst_TempArchive);
            else
            {
                clip.Dirty();
                AssetDatabase.SaveAssetIfDirty(clip);
            }
        }

        public virtual void ExitEdit()
        {
            if (Director != null)
            {
                Director.Dispose();

            }

            if (SourceClip != null)
            {
                SourceClip = null;
            }

            if (DummyClip != null)
            {
                DummyClip = null;
            }

            Dispose();
        }

        #endregion
    }


    /// <summary>
    /// 
    /// </summary>
    [Serializable]
    public class CubicBezierEditor
    {
        protected const float MIN_X = 0.00000001f;
        protected const float MAX_X = 0.9999999f;
        protected const int POINT_SIZE = 10;

        public Action onRepaintNeeded;
        public Action onRecordUndoRequest;
        public Action onValueChanged;

        [SerializeField]
        Vector2[] m_points;
        public Vector2[] Points { get => m_points; set => m_points = value; }


        [SerializeField] private float zoomValue = 1f;

        #region Obsolete
        [SerializeField] private Vector2 m_startPoint = Vector2.zero;
        [SerializeField] private Vector2 m_endPoint = Vector2.one;
        [SerializeField] private Vector2 m_startTangent = new Vector2(0.25f, 0);
        [SerializeField] private Vector2 m_endTangent = new Vector2(0.75f, 1f);

        public Vector2 startPoint => m_startPoint;
        public Vector2 endPoint => m_endPoint;
        public Vector2 startTangent => m_startTangent;
        public Vector2 endTangent => m_endTangent;

        public Vector4 curveValue => new Vector4(m_startTangent.x, m_startTangent.y, m_endTangent.x, m_endTangent.y);

        public string GetStringValue()
        {
            return "(" + startTangent.x + ", " + startTangent.y + ", " + endTangent.x + ", " + endTangent.y + ")";
        }
        #endregion

        private bool isDragginStartTangent;
        private bool isDragginEndTangent;


        #region GUI

        public void Draw(Rect _curveRect)
        {
            GUI.Box(_curveRect, GUIContent.none, boxBackground);

            var originalRect = _curveRect;

            GUI.BeginClip(_curveRect);
            _curveRect.position = Vector2.zero;

            _curveRect.position += Vector2.one * 5f;
            _curveRect.size -= Vector2.one * 10f;

            Vector2 originalSize = _curveRect.size;
            _curveRect.size *= zoomValue;
            _curveRect.position += Vector2.right * (1 - zoomValue) * originalSize.x / 2f;
            _curveRect.position += Vector2.up * (1 - zoomValue) * originalSize.y / 2f;

            DrawGrid(_curveRect, _curveRect.width / 20f, _curveRect.height / 20f, 0.25f, new Color(0.5f, 0.5f, 0.5f, 1));
            DrawGrid(_curveRect, _curveRect.width / 5f, _curveRect.height / 5f, 0.35f, new Color(0.75f, 0.75f, 0.75f, 1));

            Vector2 startPointInv = new Vector2(m_startPoint.x, 1 - m_startPoint.y);
            Vector2 endPointInv = new Vector2(m_endPoint.x, 1 - m_endPoint.y);
            Vector2 startTangentInv = new Vector2(m_startTangent.x, 1 - m_startTangent.y);
            Vector2 endTangentInv = new Vector2(m_endTangent.x, 1 - m_endTangent.y);

            Handles.BeginGUI();
            Handles.DrawBezier(
                _curveRect.position + startPointInv * _curveRect.size,
                _curveRect.position + endPointInv * _curveRect.size,
                _curveRect.position + startTangentInv * _curveRect.size,
                _curveRect.position + endTangentInv * _curveRect.size,
                Color.white,
                null,
                2f
            );
            Handles.EndGUI();

            // Draw the points and tangents
            Handles.color = Color.red;
            DrawPoint(_curveRect, startPointInv);
            DrawPoint(_curveRect, endPointInv);
            DrawPoint(_curveRect, startTangentInv);
            DrawPoint(_curveRect, endTangentInv);
            Handles.color = Color.green;
            var startTanRect = DrawTangent(_curveRect, startPointInv, startTangentInv, Color.magenta);
            var endTanRect = DrawTangent(_curveRect, endPointInv, endTangentInv, Color.cyan);

            HandleEvents(originalRect, _curveRect, startTanRect, endTanRect);

            GUI.EndClip();
        }

        public static void DrawGrid(Rect curveRect, float horizontalSpacing, float verticalSpacing, float gridOpacity, Color gridColor)
        {
            Vector2 size = curveRect.size * 8;
            int widthDivs = Mathf.CeilToInt(size.x / 0.2f / horizontalSpacing);
            int heightDivs = Mathf.CeilToInt(size.y / 0.2f / verticalSpacing);

            Handles.BeginGUI();
            Handles.color = new Color(gridColor.r, gridColor.g, gridColor.b, gridOpacity);

            //Vector3 newOffset = new Vector3(realPositionOffset.x % gridSpacing, realPositionOffset.y % gridSpacing, 0);
            Vector3 newOffset = curveRect.position - size * 4;

            for (int i = 0; i < widthDivs; i++)
            {
                Handles.DrawLine(new Vector3(horizontalSpacing * i, -horizontalSpacing, 0) + newOffset, new Vector3(horizontalSpacing * i, size.y / 0.2f, 0f) + newOffset);
            }

            for (int j = 0; j < heightDivs; j++)
            {
                Handles.DrawLine(new Vector3(-verticalSpacing, verticalSpacing * j, 0) + newOffset, new Vector3(size.x / 0.2f, verticalSpacing * j, 0f) + newOffset);
            }

            Handles.color = Color.white;
            Handles.EndGUI();
        }

        protected void HandleEvents(Rect originalRect, Rect curveRect, Rect startTangentRect, Rect endTangentRect)
        {
            EditorGUIUtility.AddCursorRect(startTangentRect, MouseCursor.MoveArrow);
            EditorGUIUtility.AddCursorRect(endTangentRect, MouseCursor.MoveArrow);

            var e = Event.current;

            if (e.type == EventType.ScrollWheel)
            {
                if (originalRect.Contains(e.mousePosition))
                {
                    zoomValue -= e.delta.y * 0.01f;
                    if (zoomValue < 0.1f) zoomValue = 0.1f;
                    if (zoomValue > 1f) zoomValue = 1f;
                    onRepaintNeeded?.Invoke();
                }
            }

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                if (startTangentRect.Contains(e.mousePosition))
                {
                    GUI.FocusControl(null);
                    isDragginStartTangent = true;
                }
                else if (endTangentRect.Contains(e.mousePosition))
                {
                    GUI.FocusControl(null);
                    isDragginEndTangent = true;
                }
            }

            if (isDragginStartTangent)
            {
                if (e.type == EventType.MouseDrag)
                {
                    onRecordUndoRequest?.Invoke();
                    var startTangentInv = (e.mousePosition - curveRect.position) / curveRect.size;
                    m_startTangent = new Vector2(startTangentInv.x, 1 - startTangentInv.y);
                    m_startTangent.x = Mathf.Clamp(m_startTangent.x, MIN_X, MAX_X);
                    onValueChanged?.Invoke();
                    onRepaintNeeded?.Invoke();
                }

                if ((e.type == EventType.MouseUp && e.button == 0) || e.type == EventType.Ignore)
                {
                    isDragginStartTangent = false;
                }
            }

            if (isDragginEndTangent)
            {
                if (e.type == EventType.MouseDrag)
                {
                    onRecordUndoRequest?.Invoke();
                    var endTangentInv = (e.mousePosition - curveRect.position) / curveRect.size;
                    m_endTangent = new Vector2(endTangentInv.x, 1 - endTangentInv.y);
                    m_endTangent.x = Mathf.Clamp(m_endTangent.x, MIN_X, MAX_X);
                    onValueChanged?.Invoke();
                    onRepaintNeeded?.Invoke();
                }

                if ((e.type == EventType.MouseUp && e.button == 0) || e.type == EventType.Ignore)
                {
                    isDragginEndTangent = false;
                }
            }
        }

        protected void DrawPoint(Rect rect, Vector2 point)
        {
            Vector2 position = rect.position + point * rect.size;
            GUI.DrawTexture(new Rect(position.x - POINT_SIZE / 2f, position.y - POINT_SIZE / 2f, POINT_SIZE, POINT_SIZE), Texture2D.whiteTexture);
        }

        protected Rect DrawTangent(Rect rect, Vector2 point, Vector2 tangent, Color color)
        {
            Vector2 position = rect.position + point * rect.size;
            Vector2 handlePosition = rect.position + tangent * rect.size;
            Handles.DrawLine(position, handlePosition);
            var handleRect = new Rect(handlePosition.x - POINT_SIZE / 2f, handlePosition.y - POINT_SIZE / 2f, POINT_SIZE, POINT_SIZE);
            var guiColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(handleRect, Texture2D.whiteTexture);
            GUI.color = guiColor;
            return handleRect;
        }


        #endregion

        private static void RecalculateKeyframeTangents(ref Keyframe _startKeyframe, ref Keyframe _endKeyframe, out float inTangent, out float outTangent)
        {

            if (float.IsNaN(_startKeyframe.outTangent))
            {
                inTangent = 0;
            }
            else if (float.IsNegativeInfinity(_startKeyframe.outTangent))
            {
                inTangent = -1 * _startKeyframe.outWeight;
            }
            else if (float.IsPositiveInfinity(_startKeyframe.outTangent))
            {
                inTangent = 1 - _startKeyframe.outWeight;
            }
            else
            {
                inTangent = _startKeyframe.outTangent * _startKeyframe.outWeight;
            }

            if (float.IsNaN(_endKeyframe.inTangent))
            {
                outTangent = 0;
            }
            else if (float.IsNegativeInfinity(_endKeyframe.inTangent))
            {
                outTangent = -1 * _endKeyframe.inWeight;
            }
            else if (float.IsPositiveInfinity(_endKeyframe.inTangent))
            {
                outTangent = 1 - _endKeyframe.inWeight;
            }
            else
            {
                outTangent = _endKeyframe.inTangent * _endKeyframe.inWeight;
            }
        }

        /// <summary>
        /// ????Editor useage?
        /// </summary>
        /// <param name="keyframe1"></param>
        /// <param name="keyframe2"></param>
        /// <param name="inTangent"></param>
        /// <param name="outTangent"></param>
        /// <param name="_applyMode"></param>
        public static void ApplyCurveToKeyframes(ref Keyframe keyframe1, ref Keyframe keyframe2, Vector2 inTangent, Vector2 outTangent, int _applyMode = 1)
        {
            BezierUtility.BezierToTangents(Vector2.zero, inTangent, outTangent, Vector2.one, out var inTangentValue, out var outTangentValue);

            if (_applyMode == 0 || _applyMode == 1)
            {
                EditorAnimationUtil.SetKeyBroken(ref keyframe1, true);
                AnimationUtil.SetKeyRightWeightedMode(ref keyframe1, true);
                EditorAnimationUtil.SetKeyRightTangentMode(ref keyframe1, TangentMode.Free);
                keyframe1.outWeight = inTangent.x;
                keyframe1.outTangent = inTangentValue;
            }

            if (_applyMode == 2 || _applyMode == 1)
            {
                EditorAnimationUtil.SetKeyBroken(ref keyframe2, true);
                AnimationUtil.SetKeyLeftWeightedMode(ref keyframe2, true);
                EditorAnimationUtil.SetKeyLeftTangentMode(ref keyframe2, TangentMode.Free);
                keyframe2.inWeight = 1 - outTangent.x;
                keyframe2.inTangent = outTangentValue;
            }
        }

        /// <summary>
        /// Set bezier points from key frames.
        /// </summary>
        /// <param name="applyMode">?????????</param>
        [Obsolete("Input control points instead.")]
        public void SetByKeyframeValues(Keyframe startKeyframe, Keyframe endKeyframe, int applyMode = 1)
        {
            AnimationUtil.SetKeyRightWeightedMode(ref startKeyframe, true);
            AnimationUtil.SetKeyLeftWeightedMode(ref endKeyframe, true);
            
            RecalculateKeyframeTangents(ref startKeyframe, ref endKeyframe, out var inTangent, out var outTangent);
            
            BezierUtility.TangentsToBezier(Vector2.zero, inTangent, Vector2.one, outTangent, out var startTangent, out var endTangent);

            if (applyMode == 0 || applyMode == 1)
            {
                m_startTangent = startTangent;
                m_startTangent.x = startKeyframe.outWeight;
            }

            if (applyMode == 2 || applyMode == 1)
            {
                m_endTangent = endTangent;
                m_endTangent.x = 1 - endKeyframe.inWeight;
                float deltaY = endTangent.y - 1;
                m_endTangent.y = 1 - deltaY;
            }
        }

        /// <summary>
        /// ???
        /// </summary>
        public void SetValue(float[] curveValue)
        {
            if (curveValue.Length >= 2)
                m_startTangent = new Vector2(curveValue[0], curveValue[1]);
            if (curveValue.Length >= 4)
                m_endTangent = new Vector2(curveValue[2], curveValue[3]);
            onValueChanged?.Invoke();
        }

        /// <summary>
        /// 
        /// </summary>
        public float[] GetValue()
        {
            return new float[] { m_startTangent.x, m_startTangent.y, m_endTangent.x, m_endTangent.y };
        }


        [Obsolete]
        public void Parse(string _curveStringValue)
        {
            if (string.IsNullOrEmpty(_curveStringValue)) return;

            var splits = _curveStringValue.Split(',');
            for (int i = 0; i < splits.Length; i++)
            {
                splits[i] = Regex.Replace(splits[i], "[^0-9.-]", "");
            }
            if (0 < splits.Length)
            {
                if (float.TryParse(splits[0], out float newStartTangentX))
                {
                    m_startTangent.x = newStartTangentX;
                    m_startTangent.x = Mathf.Clamp(m_startTangent.x, MIN_X, MAX_X);
                }
            }
            if (1 < splits.Length)
            {
                if (float.TryParse(splits[1], out float newStartTangentY))
                {
                    m_startTangent.y = newStartTangentY;
                }
            }
            if (2 < splits.Length)
            {
                if (float.TryParse(splits[2], out float newEndTangentX))
                {
                    m_endTangent.x = newEndTangentX;
                    m_endTangent.x = Mathf.Clamp(m_endTangent.x, MIN_X, MAX_X);
                }
            }
            if (3 < splits.Length)
            {
                if (float.TryParse(splits[3], out float newEndTangentY))
                {
                    m_endTangent.y = newEndTangentY;
                }
            }

            onValueChanged?.Invoke();
        }

        private static GUIStyle m_boxBackground;
        public static GUIStyle boxBackground
        {
            get
            {
                if (m_boxBackground == null)
                {
                    m_boxBackground = new GUIStyle("AvatarMappingBox");
                }
                return m_boxBackground;
            }
        }       
    }
}
