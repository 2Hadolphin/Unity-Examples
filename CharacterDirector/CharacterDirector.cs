using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.Animations;
using System.Linq;
using UnityEngine.Playables;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using System;
using UnityEngine.UIElements;
using UnityEngine.Animations.Rigging;
using Return;
using Return.Animations;
using UnityEngine.Assertions;
using Unity.Collections;
using Sirenix.Utilities.Editor;
using Object = UnityEngine.Object;
using Return.Animations.IK;
using Return.Framework.Animations.IK;
using UnityEngine.Experimental.Animations;


namespace Return.Editors
{
    /// <summary>
    /// Humanoid posture toolkit which control by muscle and IK.
    /// Three Part Of Director : Animated Target(T0) => Muscle & FK Target(T1) => IK Target(T2) => Feed Back Result To Preview Target(T0)
    /// Phase : Update => Animator Update(T0) => LateUpdate(T1,T2,T3:Recording)
    /// </summary>
    public partial class CharacterDirector : OdinEditorWindow, IDisposable
    {
        [MenuItem(MenuUtil.Tool_Asset_Animation + "CharacterDirector")]
        static void OpenWindow()
        {
            var window = (CharacterDirector)GetWindow(typeof(CharacterDirector));
            window.position = GUIHelper.GetEditorWindowRect().AlignCenter(600, 800);

            window.Show();
        }

        #region Routine

        const int graphOrder_1 = 1500;
        const int graphOrder_2 = 1600;
        const int graphOrder_3 = 1700;
        const int graphOrder_4 = 1800;

        private void Awake()
        {
            if (Selector == null)
                Selector = CreateInstance<AvatarSelector>();

        }

        protected override void OnEnable()
        {
            base.OnEnable();


            if (Animator == null)
                if (SelectionUtil.TryGetSelected(out Animator animator))
                    Animator = animator;

            SetupMuscle();

            if (Animator != null && Animator.avatar != null)
            {
                SetupCharacter();
            }

            Selector.onSelectedChanged -= Selector_onSelectedChanged;
            Selector.onSelectedChanged += Selector_onSelectedChanged;

            Selector.onSelectedIKChanged -= Selector_onSelectedIKChanged;
            Selector.onSelectedIKChanged += Selector_onSelectedIKChanged;
        }

        protected override void OnDisable()
        {
            base.OnDisable();

            if (RoutineHandler != null)
                RoutineHandler.OnLateUpdate -= RoutineHandler_OnLateUpdate;

            SceneView.duringSceneGui -= OnSceneGUI_Main;

            Dispose();
        }

        protected virtual void Reset()
        {
            Debug.Log("Reset");

            if (AnimationDirector!=null)
            {
                Stop();
                PoseHandler_Origin.SetHumanPose(ref pose_originCache);
                if (bone_hips_origin != null)
                {
                    bone_hips_origin.localPosition = HipsPosition;
                    bone_hips_origin.localRotation = HipsRotation;
                }
                Play();
            }
            else
            {
                if (PoseHandler_Origin != null && pose_originCache.muscles?.Length > 0)
                {
                    PoseHandler_Origin.SetHumanPose(ref pose_originCache);
                    bone_hips_origin.localPosition = HipsPosition;
                    bone_hips_origin.localRotation = HipsRotation;
                }
            }

            Debug.Log(muscleDatas + " has been reset.");
        }

        public virtual void Dispose()
        {
            if (BoneRenderer != null)
                BoneRenderer.Destroy();

            if (BoneRenderer_selected != null)
                BoneRenderer_selected.Destroy();


            ClearCharacter_Muscle();
            ClearCharacter_FK();
            ClearCharacter_IK();
        }

        protected override void Initialize()
        {
            if (titleContent == null)
                titleContent = EditorGUIUtility.IconContent("Animation.Record", "CharacterDirector");

            // prepare GUI elements
            {
                content_syncEdit = new("SyncEdit", "Sync data to skeleton.");
                content_enableIK = new("EnableIK", "Activate IK layer and override to animation and muscle layer.");
                content_clampMuscle = new("ClampMuscle", "Wrapping muscle value inside limit(0-1).");

                InitializeGUIPag();
            }
        }

        protected bool RequireInitializeGUI;

        protected virtual void InitializeGUI()
        {
            RequireInitializeGUI = false;

            if (style_header == null)
            {
                style_header = new(GUI.skin.FindStyle("OL Title"));
                style_header.alignment = TextAnchor.MiddleCenter;
            }

            if (style_Handle == null)
                style_Handle = GUI.skin.GetStyle("Box");
        }

        protected virtual void OnInspectorUpdate()
        {
            if (isRequiredUpdateScene)
                UpdateScene();
        }

        protected override void OnImGUI()
        {
            editorDisableFunction = !VaildAnimator;
            if (RequireInitializeGUI)
                InitializeGUI();

            base.OnImGUI();
        }


        protected virtual void OnSelectionChange()
        {
            if (BoneSet == null)
                return;

            if (Selection.activeObject is GameObject go && BoneSet.Contains(go.transform))
            {
                Debug.Log($"Selected bone [{go.name}].");
                //Tools.current = Tool.Rotate;
                //Tools.pivotMode = PivotMode.Pivot;
                //Tools.pivotRotation = PivotRotation.Local;

                if (Tools.current != Tool.Custom)
                {
                    LastTool = Tools.current;
                    Tools.current = Tool.Custom;
                }

                SetSelectedBone(go.transform); // from editor selection event

                // apply selected bone
                if (BodyMap_Origin.TryGetValue(EditingBone, out var bodyBone))
                {
                    if ((int)bodyBone < 24)
                    {
                        switch (bodyBone)
                        {
                            case HumanBodyBones.Hips:
                                Selector.PartDof = AvatarMaskBodyPart.Body;
                                break;
                            case HumanBodyBones.LeftUpperLeg:
                                Selector.PartDof = AvatarMaskBodyPart.LeftLeg;
                                break;
                            case HumanBodyBones.RightUpperLeg:
                                Selector.PartDof = AvatarMaskBodyPart.RightLeg;
                                break;
                            case HumanBodyBones.LeftLowerLeg:
                                Selector.PartDof = AvatarMaskBodyPart.LeftLeg;
                                break;
                            case HumanBodyBones.RightLowerLeg:
                                Selector.PartDof = AvatarMaskBodyPart.RightLeg;
                                break;
                            case HumanBodyBones.LeftFoot:
                                Selector.PartDof = AvatarMaskBodyPart.LeftLeg;
                                break;
                            case HumanBodyBones.RightFoot:
                                Selector.PartDof = AvatarMaskBodyPart.RightLeg;
                                break;
                            case HumanBodyBones.Spine:
                                Selector.PartDof = AvatarMaskBodyPart.Body;
                                break;
                            case HumanBodyBones.Chest:
                                Selector.PartDof = AvatarMaskBodyPart.Body;
                                break;
                            case HumanBodyBones.UpperChest:
                                Selector.PartDof = AvatarMaskBodyPart.Body;
                                break;
                            case HumanBodyBones.Neck:
                                Selector.PartDof = AvatarMaskBodyPart.Head;
                                break;
                            case HumanBodyBones.Head:
                                Selector.PartDof = AvatarMaskBodyPart.Head;
                                break;
                            case HumanBodyBones.LeftShoulder:
                                Selector.PartDof = AvatarMaskBodyPart.LeftArm;
                                break;
                            case HumanBodyBones.RightShoulder:
                                Selector.PartDof = AvatarMaskBodyPart.RightArm;
                                break;
                            case HumanBodyBones.LeftUpperArm:
                                Selector.PartDof = AvatarMaskBodyPart.LeftArm;
                                break;
                            case HumanBodyBones.RightUpperArm:
                                Selector.PartDof = AvatarMaskBodyPart.RightArm;
                                break;
                            case HumanBodyBones.LeftLowerArm:
                                Selector.PartDof = AvatarMaskBodyPart.LeftFingers;
                                break;
                            case HumanBodyBones.RightLowerArm:
                                Selector.PartDof = AvatarMaskBodyPart.RightFingers;
                                break;
                            case HumanBodyBones.LeftHand:
                                Selector.PartDof = AvatarMaskBodyPart.LeftFingers;
                                break;
                            case HumanBodyBones.RightHand:
                                Selector.PartDof = AvatarMaskBodyPart.RightFingers;
                                break;
                            case HumanBodyBones.LeftToes:
                                Selector.PartDof = AvatarMaskBodyPart.LeftLeg;
                                break;
                            case HumanBodyBones.RightToes:
                                Selector.PartDof = AvatarMaskBodyPart.RightLeg;
                                break;
                            case HumanBodyBones.LeftEye:
                                Selector.PartDof = AvatarMaskBodyPart.Head;
                                break;
                            case HumanBodyBones.RightEye:
                                Selector.PartDof = AvatarMaskBodyPart.Head;
                                break;
                            case HumanBodyBones.Jaw:
                                Selector.PartDof = AvatarMaskBodyPart.Head;
                                break;
                        }
                    }
                    else if ((int)bodyBone >= 39)
                    {
                        Selector.PartDof = AvatarMaskBodyPart.RightFingers;
                    }
                    else
                    {
                        Selector.PartDof = AvatarMaskBodyPart.LeftFingers;
                    }
                }
            }
            else if (Selection.activeObject != null)
                return;
            else
                EditingBone = null;
        }

        protected override void OnDestroy()
        {
            Stop();

            if (HandIKRig)
                HandIKRig.gameObject.Destroy();

            Selector.Destroy();

            base.OnDestroy();
        }

        /// <summary>
        /// Handle mono routines for whole operation sequence.
        /// </summary>
        protected virtual void RoutineHandler_OnLateUpdate()
        {
            if (Animator == null)
                return;

            if(bone_hips_origin!=null && ReadMusclePlayable.IsQualify())
            {
                HipsPosition = bone_hips_origin.localPosition;
                HipsRotation = bone_hips_origin.localRotation;
            }


            //G2 read stream to FK
            RoutineHandler_OnTick_Muscle();

            // ???ToDo??
            RoutineHandler_OnTick_FK();

            //G3 overwrite IK
            RoutineHandler_OnTick_IK();


            // G4 write result to preview graph 
            if (PreviewIndex == 2)
            {
                // T2 => T0 in lateupdate phase
                PoseHandler_IK.GetHumanPose(ref pose_IK);
                PoseHandler_Origin.SetHumanPose(ref pose_IK);
            }
            else if (PreviewIndex == 1)
            {
                // T1 => T0 in lateupdate phase
                PoseHandler_Muscle.GetHumanPose(ref pose_muscle_current);
                PoseHandler_Origin.SetHumanPose(ref pose_muscle_current);
            }
            else if (PreviewIndex == 0)
            {
                if (AnimationDirector.Graph.IsValid())
                    AnimationDirector.Graph.Evaluate();
            }

            // align origin hips
            bone_hips_origin.localPosition = HipsPosition;
            bone_hips_origin.localRotation = HipsRotation;

            // align IK hips
            bone_hips_IK.localPosition = HipsPosition;
            bone_hips_IK.localRotation = HipsRotation;

            // align FK hips
            bone_hips_muscle_FK.localPosition = HipsPosition;
            bone_hips_muscle_FK.localRotation = HipsRotation;

            // G5 recording
            {

            }

            if (isRequiredUpdateScene)
                UpdateScene();
        }

        #endregion

        #region Layout

        protected GUITabGroup TabGroup;
        GUITabPage page_FK, page_IK, page_Muscle, page_Output;

        public virtual void SetPage(GUITabPage page)
        {
            if (TabGroup != null && page != null)
            {
                TabGroup.SetCurrentPage(page);
            }
        }

        #endregion

        #region GUI

        GUIStyle style_header;
        GUIStyle style_greenButton;

        /// <summary>
        /// Setup GUI tabs.
        /// </summary>
        protected virtual void InitializeGUIPag()
        {
            // register tabs
            if (TabGroup == null)
            {
                TabGroup = SirenixEditorGUI.CreateAnimatedTabGroup("Tabs");

                // If true, the tab group will have the height equal to the biggest page. Otherwise the tab group will animate in height as well when changing page.
                TabGroup.FixedHeight = true;
                TabGroup.ExpandHeight = true;

                // Control the animation speed.
                TabGroup.AnimationSpeed = 10f;


                page_FK = TabGroup.RegisterTab("FK");
                page_IK = TabGroup.RegisterTab("IK");
                page_Muscle = TabGroup.RegisterTab("Muscle");
                page_Output = TabGroup.RegisterTab("Output");
            }
        }

        protected Vector2 LastAvatarSize;

        [BoxGroup(ShowLabel = false)]
        [OnInspectorGUI]
        protected virtual void DrawInspector()
        {
            var width = position.width * 0.97f;
            var height = position.height;
            var height_top = height;
            var width_left = width * 0.29f;
            var width_right = width - width_left;

            if (Selector != null)
            {
                var size_avatar = Selector.GetRectSize(width_left);
                height_top = size_avatar.y;
                width_left = size_avatar.x;
                width_right = width - width_left;
            }

            // top layout
            {
                GUILayout.BeginHorizontal();

                // left layout
                {
                    GUILayout.BeginVertical(GUILayout.Width(width_left));

                    if (Selector != null)
                        Selector.DrawPreviewLayout(new Rect(Vector2.zero, new Vector2(width_left, height_top)));

                    {
                        GUILayout.BeginVertical(GUI.skin.box);

                        GUILayout.Label("Preview", style_header);
                        {
                            EditorGUI.BeginChangeCheck();
                            var newInspectOption = GUILayout.Toolbar(PreviewIndex, contents_previewTab);
                            if (EditorGUI.EndChangeCheck())
                            {
                                PreviewIndex = newInspectOption;
                                isRequiredUpdateScene = true;
                            }
                        }

                        if (GUILayout.Button("T-Pose"))
                            TPose();

                        if (GUILayout.Button("Capture Pose"))
                            CapturePose();

                        GUILayout.EndVertical();
                    }
                    GUILayout.EndVertical();
                }

                if (Events.IsType(EventType.Repaint))
                {
                    var rect_avatarGroup = GUILayoutUtility.GetLastRect();
                    LastAvatarSize = rect_avatarGroup.size;
                }

                // right layout
                {
                    GUILayout.BeginVertical(GUI.skin.box, GUILayout.Height(LastAvatarSize.y), GUILayout.Width(width_right));

                    // animator field
                    DrawAnimatorField();

                    // control menu
                    DrawPanel();

                    GUILayout.Space(5);

                    GUILayout.EndVertical();
                }

                GUILayout.EndHorizontal();
            }

            // record menu
            {
                DrawRecordingGUI(width);
            }
        }

        protected virtual void DrawAnimatorField()
        {
            {
                GUILayout.BeginHorizontal();

                {
                    EditorGUI.BeginDisabledGroup(Animator != null);
                    {
                        EditorGUI.BeginChangeCheck();
                        var newTarget = EditorGUILayout.ObjectField(Animator, typeof(Animator), true);
                        if (EditorGUI.EndChangeCheck())
                        {
                            if (newTarget == null)
                            {
                                ClearCharacter();
                            }
                            else
                            {
                                Animator = newTarget as Animator;
                                SetupCharacter();
                            }
                        }
                    }
                    EditorGUI.EndDisabledGroup();
                }

                if (GUILayout.Button("Clear"))
                    ClearCharacter();

                GUILayout.EndHorizontal();
            }
        }

        /// <summary>
        /// Draw main GUI panels.
        /// </summary>
        protected virtual void DrawPanel()
        {
            {
                EditorGUI.BeginDisabledGroup(Animator == null);

                // draw panels
                {
                    TabGroup.BeginGroup();
                    {
                        {
                            if (page_FK.BeginPage())
                            {
                                GUILayout.BeginVertical(GUILayout.Height(LastAvatarSize.y));
                                DrawPanel_FK();
                                GUILayout.EndVertical();
                            }
                            page_FK.EndPage();
                        }

                        {
                            if (page_IK.BeginPage())
                            {
                                GUILayout.BeginVertical(GUILayout.Height(LastAvatarSize.y));
                                DrawPanel_IK();
                                GUILayout.EndVertical();
                            }
                            page_IK.EndPage();
                        }

                        {
                            if (page_Muscle.BeginPage())
                            {
                                GUILayout.BeginVertical(GUILayout.Height(LastAvatarSize.y));
                                DrawPanel_Muscle();
                                GUILayout.EndVertical();
                            }
                            page_Muscle.EndPage();
                        }

                        {
                            if (page_Output.BeginPage())
                            {
                                GUILayout.BeginVertical(GUILayout.Height(LastAvatarSize.y));
                                DrawPanel_Output();
                                GUILayout.EndVertical();
                            }
                            page_Output.EndPage();
                        }
                    }
                    TabGroup.EndGroup();
                }
            }
            EditorGUI.EndDisabledGroup();
        }

        #endregion

        #region Setup


        Animator m_Animator;

        public Animator Animator { get => m_Animator; set => m_Animator = value; }

        /// <summary>
        /// Check character qualify.
        /// </summary>
        bool VaildAnimator
        {
            get => Animator && Animator.isHuman;
        }

        protected PlayableGraph Graph_2_ReadStream;

        [SerializeField, HideInInspector]
        protected RoutineHandler RoutineHandler;

        [SerializeField, HideInInspector]
        BoneRenderer BoneRenderer;

        [SerializeField, HideInInspector]
        BoneRenderer BoneRenderer_selected;

        /// <summary>
        /// Color for FK bone renderer.
        /// </summary>
        [HideInInspector]
        public Color color_origin = Color.gray.SetAlpha(0.5f);

        /// <summary>
        /// Disable toolkit while animator not been initialized.
        /// </summary>
        bool editorDisableFunction;

        [SerializeField, HideInInspector]
        HashSet<Transform> m_AnimatedBones;

        public HashSet<Transform> AnimatedBones { get => m_AnimatedBones; set => m_AnimatedBones = value; }

        /// <summary>
        /// Hips bone from animated target(T0).
        /// </summary>
        Transform bone_hips_origin;
        /// <summary>
        /// 
        /// </summary>
        HashSet<Transform> BoneSet;
        /// <summary>
        /// 
        /// </summary>
        Dictionary<string, Transform> SkeletonMap;
        /// <summary>
        /// 
        /// </summary>
        Dictionary<Transform, HumanBodyBones> BodyMap_Origin;

        protected HumanPoseHandler PoseHandler_Origin;

        /// <summary>
        /// Cache skeleton pose for reset character pose.
        /// </summary>
        HumanPose pose_originCache;

        /// <summary>
        /// Cache skeleton pose for reset character pose.
        /// </summary>
        HumanPose pose_originStream_current;

        /// <summary>
        /// Binding character datas.
        /// </summary>
        protected virtual void SetupCharacter()
        {
            var animator = Animator;

            // subscribe scene GUI
            {
                SceneView.duringSceneGui -= OnSceneGUI_Main;

                if (animator)
                    SceneView.duringSceneGui += OnSceneGUI_Main;
            }

            if (animator)
            {
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                // bind routine
                {
                    animator.InstanceIfNull(ref RoutineHandler);
                    RoutineHandler.OnLateUpdate -= RoutineHandler_OnLateUpdate;
                    RoutineHandler.OnLateUpdate += RoutineHandler_OnLateUpdate;
                    RoutineHandler.hideFlags = HideFlags.NotEditable;
                }

                // create read stream graph ???
                if (false)
                {
                    if (Graph_2_ReadStream.IsValid())
                        Graph_2_ReadStream.Destroy();

                    Graph_2_ReadStream = PlayableGraph.Create("Director_1_ReadStream");
                    var output = AnimationPlayableOutput.Create(Graph_2_ReadStream, "ReadStreamOutput", animator);
                    output.SetAnimationStreamSource(AnimationStreamSource.PreviousInputs);
                    output.SetSortingOrder(graphOrder_2);
                }


                if (animator.isHuman)
                {
                    // binding hips bone
                    {
                        var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                        Assert.IsNotNull(hips, "Hips bone should not be null.");
                        bone_hips_origin = hips;

                        if(animator.avatar!=null && animator.avatar.humanDescription.skeleton.First(x=>x.name==hips.name,out var skeleton))
                        {
                            HipsPosition = skeleton.position;
                            HipsRotation = skeleton.rotation;
                        }
                        else
                        {
                            HipsPosition = hips.localPosition;
                            HipsRotation = hips.localRotation;
                        }
                    }

                    // set read muscle job
                    // create muscle handler **main thread
                    PoseHandler_Origin = new HumanPoseHandler(animator.avatar, animator.avatarRoot);

                    // initialize muscles
                    PoseHandler_Origin.GetHumanPose(ref pose_originCache);

                    // zero body position
                    pose_originCache.bodyPosition = new Vector3(0, pose_originCache.bodyPosition.y, 0);

                    Debug.Log($"Avatar Body Pos [{pose_originCache.bodyPosition}].\nAvatar Body Rot[{pose_originCache.bodyRotation.eulerAngles}].");
                }
                else
                {
                    // set read stream handles array
                }

                // binding skeleton bones from renderer
                {
                    var renderers = animator.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    {
                        foreach (var renderer in renderers)
                            SceneVisibilityManager.instance.DisablePicking(renderer.gameObject, true);
                    }
                    var bones = renderers.SelectMany(renderer => renderer.bones);

                    BoneSet = bones.ToHashSet();
                    AnimatedBones = BoneSet;

                    SkeletonMap = BoneSet.ToDictionary(x => x.name, x => x);

                    BodyMap_Origin = animator.HumanBoneMap().Inverse();

                    // register bone renderer
                    {
                        // UnityEditor.Animations.Rigging.BoneRendererUtils

                        if (BoneRenderer == null)
                        {
                            BoneRenderer = animator.gameObject.AddComponent<BoneRenderer>();
                            BoneRenderer.boneSize = 2f;
                            BoneRenderer.boneColor = Color.gray.SetAlpha(0.5f);

                            if (animator.isHuman)
                            {
                                var root = animator.GetBoneTransform(HumanBodyBones.Hips).parent;
                                BoneRenderer.transforms = BoneSet.Where(x => x != root).ToArray();
                            }
                            else
                            {
                                BoneRenderer.transforms = BoneSet.ToArray();
                            }
                        }

                        // obsolete
                        if (false && BoneRenderer_selected == null)
                        {
                            BoneRenderer_selected = animator.gameObject.AddComponent<BoneRenderer>();
                            BoneRenderer_selected.boneColor = Color.green;
                            BoneRenderer_selected.transforms = Selection.GetTransforms(SelectionMode.Unfiltered).
                                Where(x => BoneSet.Contains(x)).
                                SelectMany(x => x.GetChilds().Append(x)).
                                ToHashSet().
                                ToArray();
                            BoneRenderer_selected.boneSize = 2f;
                        }
                    }
                }


                // try binding director
                if (AnimationDirector == null)
                    BindDirector();

                SetupCharacter_Muscle();
                SetupCharacter_FK();
                SetupCharacter_IK();
                // disable selection
                {

                }
            }
        }

        /// <summary>
        /// Unbinding character datas.
        /// </summary>
        protected virtual void ClearCharacter()
        {
            if (RoutineHandler != null)
            {
                RoutineHandler.OnLateUpdate -= RoutineHandler_OnLateUpdate;
                RoutineHandler.Destroy();
            }

            if (PoseHandler_Origin != null)
            {
                // revert origin character pose
                {
                    PoseHandler_Origin.SetHumanPose(ref pose_originCache);
                    bone_hips_origin.localPosition = HipsPosition;
                    bone_hips_origin.localRotation = HipsRotation;
                }
                PoseHandler_Origin.Dispose();
                PoseHandler_Origin = null;
            }

            if (BoneRenderer != null)
                BoneRenderer.Destroy();

            if (BoneRenderer_selected != null)
                BoneRenderer_selected.Destroy();

            Animator = null;

            ClearCharacter_Muscle();
            ClearCharacter_FK();
            ClearCharacter_IK();

            if (Graph_2_ReadStream.IsValid())
                Graph_2_ReadStream.Destroy();
        }


        /// <summary>
        /// Force reset character posture.
        /// </summary>
        protected virtual void TPose()
        {
            switch (PreviewIndex)
            {
                case 0:
                    if (Animator != null)
                    {
                        Undo.RecordObject(Animator.gameObject, "TPose");
                        Animator.ForceTPose();
                        if(PoseHandler_Origin!=null)
                            PoseHandler_Origin.GetHumanPose(ref pose_originStream_current);
                    }
                    break;
                case 1:
                    if (Animator_Muscle != null)
                    {
                        Undo.RecordObject(Animator_Muscle.gameObject, "TPose");
                        Animator_Muscle.ForceTPose();
                        if (PoseHandler_Muscle != null)
                            PoseHandler_Muscle.GetHumanPose(ref pose_muscle_current);
                    }
                    break;
                case 2:
                    if (Animator_IK != null)
                    {
                        Undo.RecordObject(Animator_IK.gameObject, "TPose");
                        Animator_IK.ForceTPose();

                        ResetCharacter_IK();
                    }
                    break;
            }

            if (bone_hips_origin != null)
            {
                HipsPosition = bone_hips_origin.localPosition;
                HipsRotation = bone_hips_origin.localRotation;
            }
        }

        /// <summary>
        /// Capture pose for persistent archive.
        /// </summary>
        protected virtual void CapturePose()
        {
            var menu = new GenericMenu();

            {
                var content = new GUIContent("Stream Pose", "Capture stream pose.");

                if (Animator == null)
                    menu.AddDisabledItem(content);
                else
                    menu.AddItem(content, false, () =>
                    {
                        if (PoseHandler_Origin != null)
                        {
                            PoseHandler_Origin.GetHumanPose(ref pose_originCache);
                            HipsPosition = bone_hips_origin.localPosition;
                            HipsRotation = bone_hips_origin.localRotation;
                        }
                    });
            }

            {
                var content = new GUIContent("Muscle Pose", "Capture muscle pose.");

                if (Animator_Muscle == null)
                    menu.AddDisabledItem(content);
                else
                    menu.AddItem(content, false, () =>
                    {
                        if (PoseHandler_Muscle != null)
                        {
                            PoseHandler_Muscle.GetHumanPose(ref pose_originCache);
                            HipsPosition = bone_hips_muscle_FK.localPosition;
                            HipsRotation = bone_hips_muscle_FK.localRotation;
                        }
                    });
            }

            {
                var content = new GUIContent("IK Pose", "Capture IK pose.");

                if (Animator_IK == null)
                    menu.AddDisabledItem(content);
                else
                    menu.AddItem(content, false, () =>
                    {
                        if (PoseHandler_IK != null)
                        {
                            PoseHandler_IK.GetHumanPose(ref pose_originCache);
                            HipsPosition = bone_hips_IK.localPosition;
                            HipsRotation = bone_hips_IK.localRotation;
                        }
                    });
            }

            menu.ShowAsContext();
        }

        /// <summary>
        /// Get dummy skeleton for muscle and FK or IK.
        /// </summary>
        protected virtual Animator GetDummyCharacter()
        {
            var dummy = Instantiate(Animator.gameObject,Animator.transform.position,Animator.transform.rotation,Animator.transform.parent);
        
            // disable child components
            {
                var comps = dummy.GetComponentsInChildren<Component>(true);
                foreach (var comp in comps)
                {
                    if (comp is Animator || comp is Transform)
                        continue;

                    comp.gameObject.SetActive(false);
                }
            }

            // disable root components
            {
                var comps = dummy.GetComponents<Component>();
                foreach (var comp in comps)
                {
                    if (comp is Animator || comp is Transform)
                        continue;

                    try
                    {
                        comp.Destroy();
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                }

                dummy.SetActive(true);
            }

            dummy.hideFlags = //HideFlags.HideInHierarchy | 
                HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;

            return dummy.GetComponent<Animator>();
        }

        #endregion

        #region Avatar Seletor

        /// <summary>
        /// Avatar part selector.
        /// </summary>
        protected AvatarSelector Selector;

        /// <summary>
        /// Process avatar selector event and setup bone selection.
        /// </summary>
        protected virtual void Selector_onSelectedChanged(AvatarMaskBodyPart bodyPart, int button)
        {
            //Debug.Log($"{bodyPart} [{button}]");

            if (Animator != null)
            {
                Transform bone = null;

                switch (bodyPart)
                {
                    case AvatarMaskBodyPart.Body:
                        bone = Animator.GetBoneTransform(HumanBodyBones.Hips);
                        break;
                    case AvatarMaskBodyPart.Head:
                        bone = Animator.GetBoneTransform(HumanBodyBones.Head);
                        break;
                    case AvatarMaskBodyPart.LeftLeg:
                        bone = Animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                        break;
                    case AvatarMaskBodyPart.RightLeg:
                        bone = Animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                        break;
                    case AvatarMaskBodyPart.LeftArm:
                        bone = Animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                        break;
                    case AvatarMaskBodyPart.RightArm:
                        bone = Animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                        break;
                    case AvatarMaskBodyPart.LeftFingers:
                        bone = Animator.GetBoneTransform(HumanBodyBones.LeftHand);
                        break;
                    case AvatarMaskBodyPart.RightFingers:
                        bone = Animator.GetBoneTransform(HumanBodyBones.RightHand);
                        break;
                    case AvatarMaskBodyPart.LeftFootIK:
                        bone = Animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                        break;
                    case AvatarMaskBodyPart.RightFootIK:
                        bone = Animator.GetBoneTransform(HumanBodyBones.RightFoot);
                        break;
                    case AvatarMaskBodyPart.LeftHandIK:
                        bone = Animator.GetBoneTransform(HumanBodyBones.LeftHand);
                        break;
                    case AvatarMaskBodyPart.RightHandIK:
                        bone = Animator.GetBoneTransform(HumanBodyBones.RightHand);
                        break;
                }
                //Debug.Log($"Avatar Selected Bone [{bone}] with [{button}].");

                SetSelectedBone(bone); // from avatar selector changed
            }


            if (button == 0)
            {

            }
            else if (button == 1)
            {
                // switch tab
                if (page_FK != null && page_FK.IsVisible)
                {
                    SetPage(page_Muscle);
                }
                else if (page_Muscle != null && page_Muscle.IsVisible)
                {
                    SetPage(page_FK);
                }
            }
            else if (button < 0)
            {
                // IK
            }
        }

        /// <summary>
        /// Process avatar selector IK event and setup bone selection.
        /// </summary>
        protected virtual void Selector_onSelectedIKChanged(AvatarSelector.IKGoal goal)
        {
            Transform target = null;

            switch (goal)
            {
                case AvatarSelector.IKGoal.Head:
                    break;
                case AvatarSelector.IKGoal.LeftHand:
                    target = LeftHand?.Goal;
                    break;
                case AvatarSelector.IKGoal.RightHand:
                    target = RightHand?.Goal;
                    break;
                case AvatarSelector.IKGoal.LeftFoot:
                    target = LeftFoot?.Goal;
                    break;
                case AvatarSelector.IKGoal.RightFoot:
                    target = RightFoot?.Goal;
                    break;
            }

            if (!page_IK.IsVisible)
            {
                SetPage(page_IK);
                Repaint();
            }

            if (target == null)
                SetSelectedBone(null); // disable gizmos

            Selection.activeObject = target;
            Tools.current = LastTool;

            if (LastSceneView != null)
                LastSceneView.Repaint();
        }

        /// <summary>
        /// Setup selection gizmos in scene view.
        /// </summary>
        protected virtual void SetSelectedBone(Transform bone)
        {
            EditingBone = bone;
            Selection.activeObject = bone;
            Selection.SetActiveObjectWithContext(bone, bone);

            // set selection gizmos
            {
                if (EditingBone == null)
                {
                    if (BoneRenderer_selected != null)
                        BoneRenderer_selected.transforms = new Transform[0];

                    //if (BoneRenderer != null)
                    //    BoneRenderer.transforms = SkeletonMap.ToArray();
                }
                else
                {
                    var selectedBones = EditingBone.
                        GetChilds().
                        Append(EditingBone).
                        ToHashSet();

                    if (BoneRenderer_selected != null)
                        BoneRenderer_selected.transforms = selectedBones.ToArray();

                    //if (BoneRenderer != null)
                    //    BoneRenderer.transforms = SkeletonMap.ToArray();//.Where(x => !selectedBones.Contains(x)).ToArray();
                }

                if (LastSceneView != null)
                    LastSceneView.Repaint();

                if (bone != null && page_FK != null && !page_FK.IsVisible)
                {
                    if (page_Muscle == null || !page_Muscle.IsVisible)
                    {
                        SetPage(page_FK);
                        Repaint();
                    }
                }
            }
        }


        /// <summary>
        /// Get selector human dof.
        /// </summary>
        HumanPartDof GetPartDof => Selector.PartDof.ToHumanDof();

        #endregion

        #region Scene Assist

        public SceneView LastSceneView { get; protected set; }

        protected bool isRequiredUpdateScene;

        /// <summary>
        /// prevent animator freeze update until scene repaint
        /// </summary>
        protected virtual void UpdateScene()
        {
            isRequiredUpdateScene = false;

            if (!UnityEditor.EditorApplication.isPlaying)
            {
                if (LastSceneView != null)
                    LastSceneView.Repaint();
                else
                    EditorWindowUtility.ShowSceneEditorWindow().Show(true);
            }
        }

        /// <summary>
        /// Scene GUI layout.
        /// </summary>
        protected Rect rect_mainPanel;

        /// <summary>
        /// Scene GUI helper.
        /// </summary>
        protected virtual void OnSceneGUI_Main(SceneView sceneView)
        {
            if (LastSceneView != sceneView)
                LastSceneView = sceneView;

            if (Event.current == null)
                return;

            if (Animator == null)
                return;

            using (var colorCache = GUIColorCache.GetCache(() => GUI.color, (x) => GUI.color = x, Color.white))
            {
                Handles.BeginGUI();

                var pos = sceneView.position;

                // main panel
                {
                    var width = 130;
                    var height = 180;
                    var width_colorPicker = 50f;
                    rect_mainPanel = pos.SetPosition(Vector2.zero).AlignRight(width).AlignBottom(height).SubX(10).SubY(20);
                    GUILayout.BeginArea(rect_mainPanel);

                    GUILayout.FlexibleSpace();

                    {
                        // selection
                        if (false)
                        {
                            var lockSelection = SceneVisibilityManager.instance.IsPickingDisabled(Animator.gameObject);
                            if (lockSelection)
                            {
                                colorCache.SetColor(Color.red);
                                if (GUILayout.Button("Selection"))
                                {
                                    DisableSelection();
                                }
                            }
                            else
                            {
                                colorCache.SetColor(Color.green);
                                if (GUILayout.Button("Selection"))
                                {
                                    EnableSelection();
                                }
                            }
                            colorCache.SetColor();
                        }

                        // constraint
                        {

                        }

                        // sync skeleton
                        {
                            GUILayout.BeginHorizontal();
                            {
                                EditorGUI.BeginChangeCheck();
                                var newColor = EditorGUILayout.ColorField(GUIContent.none, color_origin, false, false, false, GUILayout.Width(width_colorPicker));
                                if (EditorGUI.EndChangeCheck())
                                {
                                    color_origin = newColor;
                                    if (BoneRenderer != null)
                                        BoneRenderer.boneColor = color_origin;
                                }
                            }

                            {
                                var enableSyncLayers = ((page_Muscle.IsVisible || page_FK.IsVisible) && IsSyncMusclePosture) || (page_IK.IsVisible && IsSyncIKPosture);

                                if (enableSyncLayers)
                                    colorCache.SetColor(Color.green);
                                else
                                    colorCache.SetColor();

                                if (GUILayout.Button("Sync"))
                                {
                                    if (Events.IsMouse(button: 1))
                                    {
                                        Debug.Log($"Setup sync layers as [{!enableSyncLayers}].");
                                        IsSyncMusclePosture = !enableSyncLayers;
                                        IsSyncIKPosture = !enableSyncLayers;
                                    }
                                    else
                                    {
                                        if (page_Muscle.IsVisible || page_FK.IsVisible)
                                        {
                                            IsSyncMusclePosture = !enableSyncLayers;
                                            Debug.Log($"Setup sync T0 => T1 as [{!enableSyncLayers}].");
                                        }
                                        else if (page_IK.IsVisible)
                                        {
                                            IsSyncIKPosture = !enableSyncLayers;
                                            Debug.Log($"Setup sync T1 => T2 as [{!enableSyncLayers}].");
                                        }
                                    }
                                }
                            }
                            GUILayout.EndHorizontal();
                        }

                        // FK
                        {
                            GUILayout.BeginHorizontal();

                            {
                                EditorGUI.BeginChangeCheck();
                                var newColor = EditorGUILayout.ColorField(GUIContent.none, color_FKBone, false, false, false, GUILayout.Width(width_colorPicker), GUILayout.Height(EditorGUIUtility.singleLineHeight * 2));
                                if (EditorGUI.EndChangeCheck())
                                {
                                    color_FKBone = newColor;
                                    if (BoneRenderer_muscleFK != null)
                                        BoneRenderer_muscleFK.boneColor = color_FKBone;
                                }
                            }

                            {
                                GUILayout.BeginVertical();
                                // Muscle
                                {
                                    if (page_Muscle.IsVisible)
                                        colorCache.SetColor(Color.green);
                                    else
                                        colorCache.SetColor();

                                    if (GUILayout.Button("Muscle"))
                                    {
                                        SetPage(page_Muscle);
                                        Repaint();
                                    }
                                }
                                // FK
                                {
                                    if (page_FK.IsVisible)
                                        colorCache.SetColor(Color.green);
                                    else
                                        colorCache.SetColor();

                                    if (GUILayout.Button("FK"))
                                    {
                                        SetPage(page_FK);
                                        Repaint();
                                    }
                                }
                                GUILayout.EndVertical();
                            }
                            GUILayout.EndHorizontal();
                        }

                        // IK
                        {
                            GUILayout.BeginHorizontal();
                            {
                                EditorGUI.BeginChangeCheck();
                                var newColor = EditorGUILayout.ColorField(GUIContent.none, color_IKBone, false, false, false, GUILayout.Width(width_colorPicker));
                                if (EditorGUI.EndChangeCheck())
                                {
                                    color_IKBone = newColor;
                                    if (BoneRenderer_IK != null)
                                        BoneRenderer_IK.boneColor = color_IKBone;
                                }
                            }

                            {
                                if (page_IK.IsVisible)
                                    colorCache.SetColor(Color.green);
                                else
                                    colorCache.SetColor();

                                if (GUILayout.Button("IK"))
                                {
                                    SetPage(page_IK);
                                    Repaint();
                                }
                            }
                            GUILayout.EndHorizontal();
                        }

                    }

                    var eventType = Event.current.type;

                    if (eventType == EventType.Repaint)
                    {

                    }

                    GUILayout.FlexibleSpace();

                    GUILayout.EndArea();
                }

                colorCache.SetColor();

                // assist panel
                try
                {
                    // ToDo scene control util
                    if (page_Muscle.IsVisible)
                        DrawSceneGUI_MuscleFK(rect_mainPanel);
                    else if (page_FK.IsVisible)
                        DrawSceneGUI_MuscleFK(rect_mainPanel);
                    else if (page_IK.IsVisible)
                        DrawSceneGUI_IK(rect_mainPanel);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }

                Handles.EndGUI();
            }

            OnSceneGUI_Selection(sceneView);
        }

        #region Selection

        protected Tool LastTool;

        /// <summary>
        /// Activate scene bone selection.
        /// </summary>
        public virtual void EnableSelection()
        {
            //HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

            SceneVisibilityManager.instance.DisablePicking(Animator.gameObject, true);

            if (SceneView.lastActiveSceneView)
                SceneView.lastActiveSceneView.ShowNotification(new GUIContent("Begin motion editing tool."), .1f);
        }

        /// <summary>
        /// Deactivate scene bone selection.
        /// </summary>
        public virtual void DisableSelection()
        {
            SceneVisibilityManager.instance.EnablePicking(Animator.gameObject, true);

            if (SceneView.lastActiveSceneView)
                SceneView.lastActiveSceneView.ShowNotification(new GUIContent("Quit motion editing tool."), .1f);

            EditorUtility.SetDirty(this);
        }

        void OnSceneGUI_Selection(SceneView sceneView)
        {
            if (EditingBone != null)
            {
                EditorGUI.BeginChangeCheck();
                var newRot = Handles.DoRotationHandle(EditingBone.rotation, EditingBone.position);
                if (EditorGUI.EndChangeCheck())
                {
                    EditingBone.rotation = newRot;
                    Repaint();
                    // check limit
                }
            }
        }

        #endregion

        #endregion

        #region Output **Preview **Template

        /// <summary>
        /// Current index for inspect option. **Origin => FK/Muscle => IK
        /// </summary>
        protected int PreviewIndex;

        /// <summary>
        /// Labels for animation inspect options.
        /// </summary>
        GUIContent[] contents_previewTab = new GUIContent[]
        {
            new GUIContent("None"),
            new GUIContent("FK"),
            new GUIContent("IK"),
        };

        [SerializeField, HideInInspector]
        FolderPathFinder m_folderFinder = new($"{nameof(CharacterDirector)}_path", EditorPathUtility.cst_TempArchive);

        public FolderPathFinder FolderFinder { get => m_folderFinder; set => m_folderFinder = value; }

        /// <summary>
        /// Animation playable node to output character director result, output by IK playable or muscle playable.
        /// </summary>
        public Playable FinalNode
        {
            // animation stream => muscle control => IK control
            get
            {
                if (IKPlayable.IsQualify())
                    return IKPlayable;

                if (WriteMusclePlayable.IsQualify())
                    return WriteMusclePlayable;

                return Playable.Null;
            }
        }

        #region Director

        /// <summary>
        /// Playable director which provide animation stream. **do not change directly
        /// </summary>
        protected BaseDirector AnimationDirector;

        /// <summary>
        /// Draw output contents.
        /// </summary>
        protected void DrawPanel_Output()
        {
            {
                EditorGUI.BeginChangeCheck();
                var newPath = EditorGUILayout.DelayedTextField(FolderFinder.Path);
                if (EditorGUI.EndChangeCheck())
                {
                    FolderFinder = new(nameof(CharacterDirector), newPath);
                }
            }
        }

        /// <summary>
        /// Search director.
        /// </summary>
        void BindDirector()
        {
            if (Animator.TryGetComponent<BaseDirector>(out var director))
            {
                BindAnimationStream(director);
            }
        }

       
        /// <summary>
        /// Binding custom playable director.  Execute after???
        /// </summary>
        public virtual void BindAnimationStream(BaseDirector director)
        {
            Debug.Log($"{nameof(BindAnimationStream)} {director}.");

            // reload domain ??
            if (AnimationDirector == director)
                return;
            else if (AnimationDirector != null)
            {
                Debug.LogWarning($"Unbind director {AnimationDirector} => {director}.");
                UnbindDirector();
            }

            AnimationDirector = director;

            // initialize director graph
            director.Initialize();

            // setup reading muscle job
            {
                var muscleHandles = HumanoidAnimationUtility.MuscleHandles;

                if (!MuscleHanldes_Reading.IsCreated)
                    MuscleHanldes_Reading = new(muscleHandles, Allocator.Persistent);
                if (!MuscleValues_Reading.IsCreated)
                    MuscleValues_Reading = new(muscleHandles.Length, Allocator.Persistent);

                ReadMuscleJob = new()
                {
                    MuscleHandles = MuscleHanldes_Reading,
                    MuscleValues = MuscleValues_Reading,
                };

                ReadMusclePlayable = AnimationScriptPlayable.Create(director.Graph, ReadMuscleJob);
            }

            // setup writing muscle job
            {
                WriteMuscleJob = new()
                {
                    MuscleHandles = MuscleHanldes_Writing,
                    MuscleValues = MuscleValues_Writing,
                    BlendingWeight = 0,
                };

                WriteMusclePlayable = AnimationScriptPlayable.Create(director.Graph, WriteMuscleJob);
            }

            // reset IK


            // connect graph ** source => reading muscle => writing muscle => fk playable(obsolete) => ik playable(obsolete) 
            {
                // works with clip editor
                if (ClipEditor != null && ClipEditor.Director == director)
                {
                    // required insert playable control into clip editor graph
                    Assert.IsTrue(director is GraphDirector graphDirector, $"Invalid director type for binding playable source [{director}].");

                    // set playable control as final node
                    int lastPort = 0;

                    // get clip editor source playable
                    var sourcePlayable = ClipEditor.GetAnimationSource();

                    //sourcePlayable.DisconnectOutputs();

                    //lastPort = ReadMusclePlayable.AddInput(sourcePlayable, lastPort, 1f);
                    // link write muscle playable
                    lastPort = WriteMusclePlayable.AddInput(ReadMusclePlayable, lastPort, 1f);

                    // binding IK playable
                    if (IKPlayable.IsQualify())
                    {
                        lastPort = IKPlayable.AddInput(WriteMusclePlayable, lastPort, 1f);
                        ClipEditor.AddModifier(IKPlayable);
                    }
                    else
                    {
                        ClipEditor.AddModifier(WriteMusclePlayable);
                    }
                }
                // works with universal director
                else
                {
                    // set playable control as final node
                    int lastPort = 0;

                    // link read muscle playable to source animation stream
                    {
                        if (director is ControllerDirector controllerDirector)
                        {
                            controllerDirector.ControllerPlayable.SetOutputCount(1);
                            lastPort = ReadMusclePlayable.AddInput(controllerDirector.ControllerPlayable, lastPort, 1f);
                        }
                        else if (director is ClipDirector clipDirector)
                        {
                            clipDirector.ClipPlayable.SetOutputCount(1);
                            lastPort = ReadMusclePlayable.AddInput(clipDirector.ClipPlayable, lastPort, 1f);
                        }
                        else if (director is GraphDirector graphDirector)
                        {
                            graphDirector.Initialize();
                            graphDirector.SetOutputPlayable(FinalNode);
                        }
                        else
                        {
                            Debug.LogError(new NotImplementedException($"Invalid playable director [{director.GetType()}]."));

                            var rootPlayable = director.Director.Output.GetSourcePlayable();
                            if (!rootPlayable.IsQualify())
                            {
                                Debug.LogException(new NotImplementedException($"Invalid playable from source director."));
                                return;
                            }
                            //rootPlayable.SetOutputCount(1);
                            lastPort = ReadMusclePlayable.AddInput(rootPlayable, lastPort, 1f);
                        }
                    }

                    // link write muscle playable
                    lastPort = WriteMusclePlayable.AddInput(ReadMusclePlayable, lastPort, 1f);

                    // link FK playable **has change to mono routine

                    // binding IK playable
                    if (IKPlayable.IsQualify())
                    {
                        lastPort = IKPlayable.AddInput(WriteMusclePlayable, lastPort, 1f);
                        director.Director.Output.SetSourcePlayable(IKPlayable, lastPort);
                    }
                    else
                    {
                        director.Director.Output.SetSourcePlayable(WriteMusclePlayable, lastPort);
                    }
                }
            }
        }

        /// <summary>
        /// Unbind animation director.
        /// </summary>
        public virtual void UnbindDirector()
        {
            if (AnimationDirector == null)
                return;

            var lastDirector = AnimationDirector;
            AnimationDirector = null;

            Debug.Log($"Unbind director {lastDirector}.");

            // clear exist director and reset playable control

            if (ClipEditor != null && ClipEditor.Director == lastDirector)
            {
                // works with clip editor graph
                var queue = new Queue<Playable>(3);
                if (ReadMusclePlayable.IsQualify())
                    queue.Enqueue(ReadMusclePlayable);
                if (WriteMusclePlayable.IsQualify())
                    queue.Enqueue(WriteMusclePlayable);
                if (IKPlayable.IsQualify())
                    queue.Enqueue(IKPlayable);

                ClipEditor.RemoveModifiers(queue.ToArray());
            }
            else
            {
                // works with universal graph
                var lastGraph = lastDirector.Graph;

                {
                    if (lastGraph.IsValid())
                    {
                        if (ReadMusclePlayable.IsQualify())
                            lastGraph.DestroySubgraph(ReadMusclePlayable);

                        if (WriteMusclePlayable.IsQualify())
                            lastGraph.DestroySubgraph(WriteMusclePlayable);

                        lastGraph.Destroy();
                    }
                }
            }

            ClearCharacter();
        }

        /// <summary>
        /// Begin director preview stream.
        /// </summary>
        protected virtual void Play()
        {
            if (ClipEditor != null)
            {
                ClipEditor.Play();
            }
            else if (AnimationDirector != null)
            {
                AnimationDirector.Play();
            }

            UpdateScene();

            UpdateParameter();
        }

        /// <summary>
        /// End director preview stream.
        /// </summary>
        protected virtual void Stop()
        {
            if (ClipEditor != null)
            {
                ClipEditor.Stop();
            }
            else if (AnimationDirector != null)
            {
                AnimationDirector.Stop();
            }

            UpdateScene();
        }

        #endregion

        /// <summary>
        /// Update rigging data after muscle change.
        /// </summary>
        protected virtual void UpdateParameter()
        {
            // useless in this toolkit
        }

        #endregion

        #region Recording

        /// <summary>
        /// Humanoid stream handler for main skeleton.
        /// </summary>
        HumanPoseHandler PoseHandler_Recording;

        #region Posture

        /// <summary>
        /// Current muscle pose.
        /// </summary>
        HumanPose pose_record;

        public virtual void SavePosture()
        {
            var posture = CreateInstance<HumanPosture>();
            PoseHandler_Muscle.GetHumanPose(ref pose_record);
            posture.SavePose(pose_record);

            if (string.IsNullOrEmpty(FolderFinder))
            {
                Debug.LogError(FolderFinder);
                return;
            }

            EditorAssetsUtility.Archive(posture, $"HumanPosture_{DateTime.Now.ToString("yyyy_MMdd_HHmmss")}", false, FolderFinder.Path);
        }

        #endregion

        #region Clip Editing

        [SerializeField, HideInInspector]
        AnimationClipEditorWindow m_clipEditor;
        public AnimationClipEditorWindow ClipEditor { get => m_clipEditor; protected set => m_clipEditor = value; }

        /// <summary>
        /// Bind clip editor.
        /// </summary>
        public virtual void SetClipEditor(AnimationClipEditorWindow editor)
        {
            if (ClipEditor == editor)
                return;

            if (ClipEditor != null)// && ClipEditor != editor)
            {
                //m_clipEditor.RemoveModifier(musclePlayable);
                ClipEditor.Stop();
                UnbindDirector();
            }

            // assign
            ClipEditor = editor;

            if (editor == null)
                return;

            if (EditorUtility.DisplayDialog("Initialize Options", $"Whether set source director from clip editor {editor.ClipName}?", "Yes", "No"))
            {
                {
                    Animator = editor.Animator;
                    // execute order ??
                    BindAnimationStream(editor.Director);
                    SetupCharacter();
                }

                ClipEditor.OnValuesChanged -= ClipEditor_OnValuesChanged;
                ClipEditor.OnValuesChanged += ClipEditor_OnValuesChanged;
            }
        }

        protected virtual void ClipEditor_OnValuesChanged()
        {
            // ?????? update muscle value? remap fk posture?
        }

        [SerializeField, HideInInspector]
        bool m_autoSaveClip;

        /// <summary>
        /// Whether auto save clip when key frame, otherwise manually save by recorder window.
        /// </summary>
        public bool AutoSaveClip { get => m_autoSaveClip; set => m_autoSaveClip = value; }

        void DrawRecordingGUI(float width)
        {
            {
                GUILayout.BeginHorizontal(GUILayout.Height(80), GUILayout.Width(width));
                var optios_button = new GUILayoutOption[] { GUILayout.ExpandHeight(true) };

                // green when has binding??
                if (GUILayout.Button(ClipEditor == null ? "Bind Recorder" : $"Unbind [{ClipEditor.ClipName}]", optios_button))
                {
                    var menu = new GenericMenu();

                    if (ClipEditor != null)
                    {
                        Debug.Log("Remove animation recorder..");
                        SetClipEditor(null);
                        //menu.AddItem(new GUIContent("Remove"), false, () => SetClipEditor(null));
                    }
                    else
                    {
                        Debug.Log("Searching animation recorder..");

                        if (AnimationClipEditorWindow.Instances.Count() == 1)
                        {
                            SetClipEditor(AnimationClipEditorWindow.Instances.First());
                        }
                        else
                        {
                            foreach (var editor in AnimationClipEditorWindow.Instances)
                            {
                                var id = editor.ClipName;
                                if (string.IsNullOrEmpty(id))
                                    id = "[null clip]";
                                else
                                    id = $"[{id}]";

                                menu.AddItem(new GUIContent(id), false, () => SetClipEditor(editor));
                            }

                            menu.ShowAsContext();
                        }

                    }
                }

                if (GUILayout.Button("Save Posture", optios_button))
                {
                    SavePosture();
                }

                // key posture by current time
                {
                    EditorGUI.BeginDisabledGroup(ClipEditor == null);

                    if (GUILayout.Button("Key Posture", optios_button))
                        KeyPosture();

                    if (GUILayout.Button("Key Group", optios_button))
                        KeyCurrentPart();

                    EditorGUI.EndDisabledGroup();
                }

                GUILayout.EndHorizontal();
            }
        }

        /// <summary>
        /// Key entire posture into animation clip.
        /// </summary>
        public virtual void KeyPosture()
        {
            bool dirty_clip = false;

            // humanoid animator
            {
                // get clip bindings
                var dic_bindings = ClipEditor.dic_bindingDatas.
                    Where(pair => pair.Key.binding.type == typeof(Animator)).
                    ToDictionary(pair => pair.Key.binding.propertyName, pair => pair.Value);

                var time = ClipEditor.GetTime();
                var additive = ClipEditor.AdditivePreview;

                // key muscle
                {
                    // get final muscle values
                    PoseHandler_Muscle.GetHumanPose(ref pose_record);
                    var muscleValues_final = pose_record.muscles;

                    Assert.IsTrue(muscleValues_final.Length == muscleDatas.Length);

                    // ensure all animation curve been register
                    {
                        var newBindings = new Queue<CurveBinding>(muscleDatas.Length);
                        CollectMissingBindings(dic_bindings, newBindings, muscleDatas);

                        // add required curve binding
                        if (newBindings.Count > 0)
                            ClipEditor.OnBindingAdded(newBindings.ToArray());
                    }

                    dirty_clip |= ApplyMuscles(dic_bindings, muscleValues_final, muscleDatas);
                }

                // key IK
                {
                    // to do : save IK bindings to animation clip

                }
            }

            // save clip asset
            if (AutoSaveClip && dirty_clip)
                ClipEditor.SaveClip();

            // align origin muscle values after push new keyframes
            SyncFromAnimatedTarget();
        }

        /// <summary>
        /// Get inspecting muscle groups.
        /// </summary>
        protected virtual IEnumerable<MuscleGroup> GetGroups()
        {
            var dof = GetPartDof;

            if ((int)dof < 6)
            {
                // body group
                yield return Groups[dof];
            }
            else
            {
                // finger group
                var groups = FingerGroup(dof);
                foreach (var group in groups)
                    yield return group;
            }
        }

        /// <summary>
        /// Key current part into animation clip.
        /// </summary>
        public void KeyCurrentPart()
        {
            // get clip bindings
            var dic_bindings = ClipEditor.dic_bindingDatas.
                Select(binding => binding.Value).
                Where(data => data.BindingData.binding.type == typeof(Animator)).
                ToDictionary(data => data.BindingData.binding.propertyName);

            bool dirty_clip = false;

            // key muscle
            {
                // get final muscle values ** pass IK? visual??
                if (PreviewIndex == 0)
                    PoseHandler_Origin.GetHumanPose(ref pose_record);
                else if (PreviewIndex == 1)
                    PoseHandler_Muscle.GetHumanPose(ref pose_record);
                else if (PreviewIndex == 2)
                    PoseHandler_IK.GetHumanPose(ref pose_record);
                else
                    throw new NotImplementedException($"Invalid preview index [{PreviewIndex}].");

                var muscleValues_final = pose_record.muscles;

                // ensure all animation curve been register
                {
                    var newBindings = new Queue<CurveBinding>(muscleDatas.Length);

                    foreach (var group in GetGroups())
                        CollectMissingBindings(dic_bindings, newBindings, group);

                    // add required curve binding
                    if (newBindings.Count > 0)
                        ClipEditor.OnBindingAdded(newBindings.ToArray());
                }

                // apply muscle values by group
                foreach (var group in GetGroups())
                {
                    dirty_clip |= ApplyMuscles(dic_bindings,
                        //muscleValues_final,
                        group.Select(x=>x.Value).ToArray(),
                        group.ToArray());
                }
            }

            // key IK
            {

            }

            if (dirty_clip && AutoSaveClip)
                ClipEditor.SaveClip();
        }


        /// <summary>
        /// Collect missing binding muscles.
        /// </summary>
        protected virtual void CollectMissingBindings(IDictionary<string, CurveData> dic_bindings, Queue<CurveBinding> newBindings, IEnumerable<MuscleData> muscleDatas)
        {
            foreach (var muscleData in muscleDatas)
            {
                var name = muscleData.Handle.name;

                if (!dic_bindings.ContainsKey(name))
                {
                    var editorBinding = new EditorCurveBinding() { propertyName = name, type = typeof(Animator) };
                    var newCurveBinding = new CurveBinding(editorBinding);
                    newBindings.Enqueue(newCurveBinding);
                }
            }
        }

        /// <summary>
        /// Apply muscle values to clip editor. **Must execute after ensure muscle handle been register.
        /// </summary>
        /// <param name="muscles">New muscle value to apply.</param>
        /// <param name="muscleDatas">Target muscle handles.</param>
        /// <returns>Has any animation curve been modify.</returns>
        protected virtual bool ApplyMuscles(IDictionary<string, CurveData> dic_bindings, float[] muscles, MuscleData[] muscleDatas)
        {
            Assert.IsTrue(muscles.Length == muscleDatas.Length);

            bool hasModify = false;

            var time = ClipEditor.GetTime();
            var additive = ClipEditor.AdditivePreview;

            var length = muscleDatas.Length;

            void ApplyKeyframe(CurveData curveData, float value)
            {
                if (ClipEditor.ExistKey(curveData, time))
                    ClipEditor.EditKey(curveData, time, value);
                else
                    ClipEditor.AddKey(curveData, time, value);
            }

            // caculate insert muscle values
            for (int i = 0; i < length; i++)
            {
                var name = muscleDatas[i].Handle.name;

                if (!dic_bindings.TryGetValue(name, out var curveData))
                {
                    Debug.LogException(new ArgumentNullException($"Failure to find required muscle curve binding [{name}]."));
                    continue;
                }

                if (curveData.curve == null)
                {
                    Debug.LogException(new ArgumentNullException($"Failure to find required muscle curve [{curveData.BindingData.binding.propertyName}]."));
                    continue;
                }

                // compare to source clip
                if (additive)
                {
                    var originCurve = AnimationUtility.GetEditorCurve(ClipEditor.SourceClip, curveData.BindingData.binding);
                    var additiveValue = muscles[i] - originCurve.Evaluate(time);
                    if (
                        additiveValue != curveData.curve.Evaluate(time)
                        //!Mathf.Approximately(additiveValue, datas[i].curve.Evaluate(time))
                        )
                    {
                        if (!hasModify)
                            hasModify = true;

                        ApplyKeyframe(curveData, additiveValue);
                    }
                    else
                    {
                        // same muscle value
                    }
                }
                else
                {
                    // valid clip value that doesn't equal to origin poseture
                    if (curveData.curve.Evaluate(time) != muscles[i])
                    {
                        if (!hasModify)
                            hasModify = true;

                        ApplyKeyframe(curveData, muscles[i]);
                    }
                    else
                    {
                        // same muscle value
                    }
                }
            }

            return hasModify;
        }

        #endregion
        #endregion
    }


    public abstract class DirectorHandle
    {
        public abstract void SetupCharacter();
        public abstract void ClearCharacter();

        /// <summary>
        /// Invoke by LateUpdate, solve animation control.
        /// </summary>
        public abstract void Tick();

        public abstract void InitializeGUI();
        public abstract void DrawPanelGUI();
        public abstract void DrawSceneGUI(Rect rect);
    }

    public partial class CharacterDirector // Muscle(T1) 
    {
        #region Posture - Muscle

        [SerializeField, HideInInspector]
        Animator Animator_Muscle;

        [SerializeField, HideInInspector]
        BoneRenderer BoneRenderer_muscleFK;

        [SerializeField, HideInInspector]
        bool m_isQualifyMuscleCharacter;
        public bool IsQualifyMuscleCharacter { get => m_isQualifyMuscleCharacter; set => m_isQualifyMuscleCharacter = value; }

        /// <summary>
        /// Color for FK bone renderer.
        /// </summary>
        [HideInInspector]
        public Color color_FKBone = Color.yellow.SetAlpha(0.5f);

        /// <summary>
        /// Cache skeleton map for sync FK operation.
        /// </summary>
        Dictionary<Transform, Transform> SkeletonMap_Origin2Muscle;

        /// <summary>
        /// Humanoid posture handler for FK and muscle editing.
        /// </summary>
        HumanPoseHandler PoseHandler_Muscle;

        [SerializeField, HideInInspector]
        Transform bone_hips_muscle_FK;

        [SerializeField, HideInInspector]
        private bool isSyncMusclePosture;

        /// <summary>
        /// Whether auto align FK posture to origin animation stream.
        /// </summary>
        protected bool IsSyncMusclePosture { get => isSyncMusclePosture; set => isSyncMusclePosture = value; }

        protected bool RequiredSyncT0T1Posture;

        protected virtual void SetupCharacter_Muscle()
        {
            if (Animator_Muscle != null)
                ClearCharacter_Muscle();

            Animator_Muscle = GetDummyCharacter();

            var animator = Animator_Muscle;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            IsQualifyMuscleCharacter = animator.isHuman && animator.avatar != null;

            // bind bones with origin skeleton
            {
                var tfs = animator.GetComponentsInChildren<Transform>(true);
                SkeletonMap_Origin2Muscle = tfs.
                    Where(x => SkeletonMap.ContainsKey(x.name)).
                    ToDictionary(x => SkeletonMap[x.name], x => x);
            }

            // bind bone renderer
            if (BoneRenderer_muscleFK == null)
            {
                BoneRenderer_muscleFK = animator.gameObject.AddComponent<BoneRenderer>();
                BoneRenderer_muscleFK.boneSize = 2f;
                BoneRenderer_muscleFK.boneColor = color_FKBone;

                if (animator.isHuman)
                {
                    var root = animator.GetBoneTransform(HumanBodyBones.Hips).parent;
                    BoneRenderer_muscleFK.transforms = SkeletonMap_Origin2Muscle.Values.Where(x => x != root).ToArray();
                }
                else
                {
                    BoneRenderer_muscleFK.transforms = SkeletonMap_Origin2Muscle.Values.ToArray();
                }
            }

            // create stream handle
            {
                MuscleHandle[] muscleHandles;

                // binding required handles **enable group by avatar mask?
                {
                    muscleHandles = HumanoidAnimationUtility.MuscleHandles;

                    if (!MuscleHanldes_Writing.IsCreated)
                        MuscleHanldes_Writing = new(muscleHandles, Allocator.Persistent);

                    if (!MuscleValues_Writing.IsCreated)
                        MuscleValues_Writing = new(muscleHandles.Length, Allocator.Persistent);
                }
            }


            // set independent skeleton for FK and muscle operation
            if (IsQualifyMuscleCharacter)
            {
                var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                bone_hips_muscle_FK = hips;

                // create muscle handler **main thread
                PoseHandler_Muscle = new HumanPoseHandler(animator.avatar, animator.avatarRoot);

                // sync pose
                if (pose_muscle_current.muscles == null)
                {
                    pose_muscle_current = new HumanPose()
                    {
                        bodyPosition = pose_originCache.bodyPosition,
                        bodyRotation = pose_originCache.bodyRotation,

                        muscles = new float[pose_originCache.muscles.Length]
                    };

                    Array.Copy(pose_originCache.muscles, pose_muscle_current.muscles, pose_originCache.muscles.Length);
                }

                {
                    var muscles = pose_originCache.muscles;
                    var length = muscles.Length;

                    // copy muscle data value
                    if (muscleDatas?.Length == length)
                    {
                        for (int i = 0; i < length; i++)
                        {
                            muscleDatas[i].Value = muscles[i];
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        protected virtual void ClearCharacter_Muscle()
        {
            if (PoseHandler_Muscle != null)
            {
                // revert origin character pose
                PoseHandler_Muscle.Dispose();
                PoseHandler_Muscle = null;
            }

            if (MuscleHanldes_Reading.IsCreated)
                MuscleHanldes_Reading.Dispose();

            if (MuscleValues_Reading.IsCreated)
                MuscleValues_Reading.Dispose();

            if (MuscleHanldes_Writing.IsCreated)
                MuscleHanldes_Writing.Dispose();

            if (MuscleValues_Writing.IsCreated)
                MuscleValues_Writing.Dispose();

            pose_originCache = default;
            pose_muscle_current = default;

            PoseHandler_Muscle = null;

            if (Animator_Muscle != null)
                Animator_Muscle.gameObject.Destroy();

            //if (BoneRenderer_muscleFK != null)
            //    BoneRenderer_muscleFK.gameObject.Destroy();

            SkeletonMap_Origin2Muscle = null;

            Assert.IsTrue(Animator_Muscle == null);

            Animator_Muscle = null;
        }

        /// <summary>
        /// Copy muscle from animated skeleton(T0) and paste to FK skeleton(T1).
        /// </summary>
        protected virtual void RoutineHandler_OnTick_Muscle()
        {
            if (Animator_Muscle == null || !IsQualifyMuscleCharacter)
                return;

            // set pose to muscle skeleton

            if (RequiredSyncT0T1Posture || IsSyncMusclePosture)
            {
                RequiredSyncT0T1Posture = false;

                SyncFromAnimatedTarget();
            }
            else
            {
                PoseHandler_Muscle.SetHumanPose(ref pose_muscle_current);
                bone_hips_muscle_FK.localPosition = HipsPosition;
                bone_hips_muscle_FK.localRotation = HipsRotation;
            }
        }

        /// <summary>
        /// T0 => T1
        /// </summary>
        protected virtual void SyncFromAnimatedTarget()
        {
            // get pose from animation stream
            PoseHandler_Origin.GetHumanPose(ref pose_originStream_current);

            // set pose to FK skeleton
            PoseHandler_Muscle.SetHumanPose(ref pose_originStream_current);
            bone_hips_muscle_FK.localPosition = HipsPosition;
            bone_hips_muscle_FK.localRotation = HipsRotation;

            // push current posture values to editing handles
            {
                PoseHandler_Muscle.GetHumanPose(ref pose_muscle_current);

                Assert.IsTrue(MuscleValues_Writing.Length == pose_muscle_current.muscles.Length, $"{MuscleValues_Writing.Length}/{pose_muscle_current.muscles.Length}");
                MuscleValues_Writing.CopyFrom(pose_muscle_current.muscles);

                var length = muscleDatas.Length;
                for (int i = 0; i < length; i++)
                {
                    muscleDatas[i].Value = pose_muscle_current.muscles[i];
                }
            }

            Repaint();
        }

        /// <summary>
        /// Cache human pose.
        /// </summary>
        [Obsolete]
        HumanPose HumanMuscles;

        /// <summary>
        /// Current muscle pose from muscle skeleton.
        /// </summary>
        HumanPose pose_muscle_current;

        /// <summary>
        /// Cache hip position to prevent muscle glitch bug. 
        /// **worldPosition = humanPose.bodyPosition * animator. humanScale;
        /// </summary>
        Vector3 HipsPosition;
        /// <summary>
        /// Cache hip rotation to prevent muscle glitch bug.
        /// </summary>
        Quaternion HipsRotation;

        /// <summary>
        /// Cache editing muscle handles and editing muscle values.
        /// </summary>
        MuscleData[] muscleDatas;


        [SerializeField, HideInInspector]
        Dictionary<HumanPartDof, MuscleGroup> m_Groups;

        /// <summary>
        /// Cache muscle groups.
        /// </summary>
        public Dictionary<HumanPartDof, MuscleGroup> Groups { get => m_Groups; set => m_Groups = value; }


        [SerializeField, HideInInspector]
        Dictionary<string, int> m_dic_muscleBinding;

        /// <summary>
        /// Binding muscle hanldeand value index.
        /// </summary>
        public Dictionary<string, int> dic_muscleBinding { get => m_dic_muscleBinding; set => m_dic_muscleBinding = value; }

        /// <summary>
        /// Record muscle before editing.
        /// </summary>
        [Obsolete]
        float time_lastMuscleEdit;

        /// <summary>
        /// Duration of editing gap to record muscle values. 
        /// </summary>
        [HideInInspector]
        [Obsolete]
        public float time_muscleEditingGap = 3f;

        /// <summary>
        /// Wrapping muscle between 0-1.
        /// </summary>
        [SerializeField, HideInInspector]
        bool m_muscleConstraint = true;

        public bool MuscleConstraint { get => m_muscleConstraint; set => m_muscleConstraint = value; }

        /// <summary>
        /// Initialize muscle database. **Standar muscle groups
        /// </summary>
        protected virtual void SetupMuscle()
        {
            muscleDatas = MuscleData.CreateAllMuscleDatas;
            dic_muscleBinding = HumanoidAnimationUtility.GetMuscleIndexes();

            var groups = new Dictionary<HumanPartDof, MuscleGroup>((int)HumanPartDof.LastHumanPartDof);
            var length = muscleDatas.Length;
            for (int i = 0; i < length; i++)
            {
                var handle = muscleDatas[i].Handle;
                var partDof = handle.humanPartDof;
                var dof = string.Empty;

                if (!groups.TryGetValue(partDof, out var group))
                {
                    group = new MuscleGroup(partDof, muscleDatas);
                    groups.Add(partDof, group);
                    group.onMuscleEditing += UpdateParameter;
                }

                group.KeyVolume(i, muscleDatas[i]);
            }

            Groups = groups;
        }

        #region Muscle Handle

        GUIContent content_muscleLimit;

        GUIStyle style_Handle;
        GUIStyle style_toggle;

        GUILayoutOption[] option_Slider;
        GUILayoutOption[] layout_Toggle;

        /// <summary>
        /// Draw muscle controls.
        /// </summary>
        void DrawPanel_Muscle()
        {
            //GUILayout.Label(nameof(DrawPanel_Muscle));

            // initialize GUI
            {
                if (option_Slider == null)
                    option_Slider = new[] { GUILayout.Width(200) };

                if (layout_Toggle == null)
                    layout_Toggle = new[] { GUILayout.Width(20) };

                if (content_muscleLimit == null)
                    content_muscleLimit = new GUIContent("", "Limit muscle value.");
            }

            var drawContent = Animator != null;

            // draw muscle behaviour ??
            {

            }

            // draw muscle control
            {
                if (GUILayout.Button("Get Stream Muscle"))
                {
                    if (ReadMusclePlayable.IsQualify())
                    {
                        //ReadMuscleJob = ReadMusclePlayable.GetJobData<ReadMuscleJob>();

                        var muscles_originStream = MuscleValues_Reading.ToArray();

                        // reorder
                        Assert.IsTrue(MuscleValues_Writing.Length == MuscleValues_Reading.Length, $"{MuscleValues_Writing.Length}/{MuscleValues_Reading.Length}");
                        MuscleValues_Writing.CopyFrom(muscles_originStream);

                        if (Groups != null)
                        {
                            foreach (var group in Groups.Values)
                            {
                                var datas = group.Datas;

                                for (int i = 0; i < datas.Length; i++)
                                {
                                    if (dic_muscleBinding.TryGetValue(datas[i].Name, out var index))
                                    {
                                        pose_muscle_current.muscles[index] = muscles_originStream[index];
                                        datas[i].Value = muscles_originStream[index];
                                    }
                                    else
                                        Debug.LogError($"Failure to find muscle data {datas[i].Name}");
                                }
                            }
                        }
                    }
                }

                if (GUILayout.Button("Get Posture Muscle"))
                {
                    if (PoseHandler_Muscle != null)
                        PoseHandler_Muscle.GetHumanPose(ref pose_muscle_current);

                    HipsPosition = bone_hips_origin.localPosition;
                    HipsRotation = bone_hips_origin.localRotation;

                    if (Groups != null)
                    {
                        foreach (var group in Groups.Values)
                        {
                            var datas = group.Datas;

                            for (int i = 0; i < datas.Length; i++)
                            {
                                if (dic_muscleBinding.TryGetValue(datas[i].Name, out var index))
                                    datas[i].Value = pose_muscle_current.muscles[index];
                                else
                                    Debug.LogError($"Failure to find muscle data {datas[i].Name}");
                            }
                        }
                    }

                    var muscles = string.Join("f,\n", pose_muscle_current.muscles);
                    Debug.Log(muscles);
                }

                // ??? apply from stream or 
                if (GUILayout.Button("Set Posture Muscle"))
                {
                    UpdateMusclePose(); //??? 
                }

                if (GUILayout.Button("Archive Muscle Preset"))
                {
                    var posture = ScriptableObject.CreateInstance<HumanPosture>();
                    posture.SavePose(pose_muscle_current);
                    EditorAssetsUtility.Archive(posture, "Posture_", false, FolderFinder);
                }
            }

            // draw muscle handles
            {
                scroll_musclePanel = GUILayout.BeginScrollView(scroll_musclePanel, GUI.skin.box, GUILayout.Height(450f));

                var dof = Selector.PartDof;
                switch (dof)
                {
                    case AvatarMaskBodyPart.LeftFingers:
                        DrawTitle_Fingers("Left");
                        if (drawContent) DrawControl_LeftFingers();
                        break;

                    case AvatarMaskBodyPart.RightFingers:
                        DrawTitle_Fingers("Right");
                        if (drawContent) DrawControl_RightFingers();
                        break;

                    case AvatarMaskBodyPart.Body:
                        DrawTitle_PartDof(Selector.PartDof);
                        if (drawContent) Handle_Body();
                        break;
                    case AvatarMaskBodyPart.Head:
                        DrawTitle_PartDof(Selector.PartDof);
                        if (drawContent) Handle_Head();
                        break;

                    case AvatarMaskBodyPart.LeftLeg:
                        DrawTitle_PartDof(Selector.PartDof);
                        if (drawContent) DrawControls_RightLeg();
                        break;

                    case AvatarMaskBodyPart.RightLeg:
                        DrawTitle_PartDof(Selector.PartDof);
                        if (drawContent) DrawControls_LeftLeg();
                        break;

                    case AvatarMaskBodyPart.LeftArm:
                        DrawControlBar_Arms();
                        if (drawContent) DrawControls_LeftArm();
                        break;

                    case AvatarMaskBodyPart.RightArm:
                        DrawControlBar_Arms();
                        if (drawContent) DrawControls_RightArm();
                        break;

                    default:
                        break;
                }
                GUILayout.EndScrollView();
            }
        }


        /// <summary>
        /// Draw title by part dof.
        /// </summary>
        void DrawTitle_PartDof(AvatarMaskBodyPart partDof)
        {
            GUILayout.BeginVertical(style_Handle);
            GUILayout.Label(partDof.ToString());
            GUILayout.EndVertical();
        }

        #region Top

        void Handle_Head()
        {
            var group = Groups[HumanPartDof.Head];
            var length = (int)HeadDof.LastHeadDof;
            DrawMuscleControls(group, length);
        }

        void Handle_Body()
        {
            var group = Groups[HumanPartDof.Body];
            var length = (int)BodyDof.LastBodyDof;
            DrawMuscleControls(group, length);
        }

        #endregion

        #region Hand

        void DrawControlBar_Arms()
        {
            //DrawIKOptions(Selector.PartDof.ToString());
            DrawTitle_PartDof(Selector.PartDof);
        }

        void DrawControls_RightArm()
        {
            var group = Groups[HumanPartDof.RightArm];
            DrawControls_Arm(group);
        }

        void DrawControls_LeftArm()
        {
            var group = Groups[HumanPartDof.LeftArm];
            DrawControls_Arm(group);
        }

        void DrawControls_Arm(MuscleGroup group)
        {
            var length = (int)ArmDof.LastArmDof;
            DrawMuscleControls(group, length);
        }

        #endregion

        [HideInInspector]
        public MuscleData SelectedMuscleData;

        /// <summary>
        /// GUI scroll value.
        /// </summary>
        Vector2 scroll_musclePanel;

        /// <summary>
        /// Draw muscle control.
        /// </summary>
        void DrawMuscleControls(MuscleGroup group, int muscleCount)
        {
            {
                GUILayout.BeginVertical(style_Handle);
                EditorGUILayout.Space();

                for (int i = 0; i < muscleCount; i++)
                {
                    var muscle = group[i];

                    {
                        GUILayout.BeginHorizontal();

                        // set selection
                        if (GUILayout.Button(muscle.Name, GUI.skin.label))
                        {
                            SelectedMuscleData = muscle;
                            Debug.Log(muscle.Name);
                        }

                        // edit muscle value
                        {
                            EditorGUI.BeginChangeCheck();

                            if (muscle.Clamp)
                                muscle.Value = EditorGUILayout.Slider(muscle.Value, -1, 1, option_Slider);
                            else
                                muscle.Value = EditorGUILayout.Slider(muscle.Value, -5, 5, option_Slider);

                            if (EditorGUI.EndChangeCheck())
                            {
                                SetMuscleData(muscle);
                                UpdateMusclePose();  // muscle control
                            }
                        }

                        // whether limit muscle value
                        muscle.Clamp = EditorGUILayout.Toggle(content_muscleLimit, muscle.Clamp, layout_Toggle);

                        GUILayout.EndHorizontal();
                    }

                    group[i] = muscle;
                }
                GUILayout.EndVertical();
            }
        }

        #region Finger

        void DrawTitle_Fingers(string side)
        {
            GUILayout.BeginVertical(style_Handle);
            GUILayout.Label(string.Format("{0} hand fingers : ", side));
            GUILayout.EndVertical();
        }

        void DrawControl_RightFingers()
        {
            var groups = FingerGroup(HumanPartDof.RightThumb);
            DrawFingers(groups);
        }

        void DrawControl_LeftFingers()
        {
            var groups = FingerGroup(HumanPartDof.LeftThumb);
            DrawFingers(groups);
        }

        /// <summary>
        /// Return muscle groups from finger dof.
        /// </summary>
        MuscleGroup[] FingerGroup(HumanPartDof fingerDof)
        {
            if (Groups == null)
                return new MuscleGroup[0];

            return new[]
            {
                Groups[fingerDof],
                Groups[fingerDof+1],
                Groups[fingerDof+2],
                Groups[fingerDof+3],
                Groups[fingerDof+4],
            };
        }

        /// <summary>
        /// Editor finger muscle spread batch parameter.
        /// </summary>
        float cache_finger_spread;
        /// Editor finger muscle stretched batch parameter.
        float cache_finger_stretched;

        /// <summary>
        /// Drawing GUI for finger muscle datas and batch control.
        /// </summary>
        void DrawFingers(params MuscleGroup[] groups)
        {
            var count = (int)FingerDof.LastFingerDof;

            int index_spread = 1;

            // batch edit
            {
                GUILayout.BeginVertical(style_Handle);

                // spread
                {
                    EditorGUI.BeginChangeCheck();
                    cache_finger_spread = EditorGUILayout.Slider(cache_finger_spread, -1, 1f, option_Slider);
                    if (EditorGUI.EndChangeCheck())
                    {
                        foreach (var group in groups)
                        {
                            var muscle = group[index_spread];
                            muscle.Value = cache_finger_spread;
                            group[index_spread] = muscle;

                            SetMuscleData(muscle);
                        }

                        UpdateMusclePose(); // batch finger control
                    }
                }

                // stretched
                {
                    EditorGUI.BeginChangeCheck();
                    cache_finger_stretched = EditorGUILayout.Slider(cache_finger_stretched, -1, 1f, option_Slider);
                    if (EditorGUI.EndChangeCheck())
                    {
                        foreach (var group in groups)
                        {
                            for (int i = 0; i < group.Count; i++)
                            {
                                if (i == index_spread)
                                    continue;

                                var muscle = group[i];
                                muscle.Value = cache_finger_stretched;
                                group[i] = muscle;

                                SetMuscleData(muscle);
                            }
                        }

                        UpdateMusclePose(); // batch finger control
                    }
                }

                GUILayout.EndVertical();
            }

            foreach (var group in groups)
                DrawFingerMuscleControls(group, count);
        }

        /// <summary>
        /// Draw muscle control for fingers.
        /// </summary>
        void DrawFingerMuscleControls(MuscleGroup group, int muscleCount)
        {
            {
                GUILayout.BeginVertical(style_Handle);
                EditorGUILayout.Space();

                for (int i = 0; i < muscleCount; i++)
                {
                    var muscle = group[i];

                    {
                        GUILayout.BeginHorizontal();

                        // set selection
                        if (GUILayout.Button(muscle.Name, GUI.skin.label))
                        {
                            SelectedMuscleData = muscle;
                            Debug.Log(muscle.Name);
                        }

                        // edit muscle value
                        {
                            EditorGUI.BeginChangeCheck();

                            if (muscle.Clamp)
                                muscle.Value = EditorGUILayout.Slider(muscle.Value, -1, 1, option_Slider);
                            else
                                muscle.Value = EditorGUILayout.Slider(muscle.Value, float.MinValue, float.MaxValue, option_Slider);

                            if (EditorGUI.EndChangeCheck())
                            {
                                SetMuscleData(muscle);
                                UpdateMusclePose(); // finger control
                            }
                        }

                        // whether limit muscle value
                        muscle.Clamp = EditorGUILayout.Toggle(content_muscleLimit, muscle.Clamp, layout_Toggle);

                        GUILayout.EndHorizontal();
                    }

                    group[i] = muscle;
                }
                GUILayout.EndVertical();
            }
        }

        /// <summary>
        /// Update <see cref="pose_muscle_current"/> with <see cref="MuscleData"/>.
        /// </summary>
        protected virtual void SetMuscleData(MuscleData muscleData)
        {
            if (dic_muscleBinding.TryGetValue(muscleData.Name, out var index))
            {
                pose_muscle_current.muscles[index] = muscleData.Value;
            }
            else
            {
                Debug.LogError(muscleData.Name);
            }
        }

        /// <summary>
        /// Update skeleton by HumanPose. **Invoke by 
        /// </summary>
        protected virtual void UpdateMusclePose()
        {
            // set new muscle value for playable stream
            if (WriteMusclePlayable.IsQualify())
            {
                if (MuscleValues_Writing.IsCreated)
                {
                    Assert.IsTrue(MuscleValues_Writing.Length == pose_muscle_current.muscles.Length);

                    // additive ????
                    MuscleValues_Writing.CopyFrom(pose_muscle_current.muscles);
                }

                Debug.Log($"Update muscle posture by playable.");

                WriteMusclePlayable.GetGraph().Evaluate();
                //if (AnimationDirector != null && AnimationDirector.Graph.IsValid())
                //    AnimationDirector.Graph.Evaluate();
            }
            // post new muscle value for clip modify or animation posture
            else if (PoseHandler_Muscle != null)
            {
                //var originPos = pose_current.bodyPosition;
                //var originRot = pose_current.bodyRotation;

                Debug.Log("Apply muscle editing with muscle handler.");

                {
                    PoseHandler_Muscle.SetHumanPose(ref pose_muscle_current);
                    bone_hips_muscle_FK.localPosition = HipsPosition;
                    bone_hips_muscle_FK.localRotation = HipsRotation;
                }

                //pose_current.bodyPosition = originPos;
                //pose_current.bodyRotation = originRot;

                //bone_hips.SetLocalPositionAndRotation(HipPosition, HipRotation);
            }
            else
            {
                Debug.LogException(new NotImplementedException());
            }

            isRequiredUpdateScene = true;
        }

        #endregion

        #region Leg

        void DrawControls_RightLeg()
        {
            var group = Groups[HumanPartDof.RightLeg];
            var length = (int)LegDof.LastLegDof;
            DrawMuscleControls(group, length);
        }

        void DrawControls_LeftLeg()
        {
            var group = Groups[HumanPartDof.LeftLeg];
            var length = (int)LegDof.LastLegDof;
            DrawMuscleControls(group, length);
        }

        #endregion

        #endregion

        #region Muscle Group

        /// <summary>
        /// Collection of <see cref="MuscleData"/>, use to grouping human dof .
        /// </summary>
        [Serializable]
        public class MuscleGroup : IEnumerable<MuscleData>
        {
            /// <summary>
            /// Invoke while value been modify.
            /// </summary>
            public event Action onMuscleEditing;

            /// <summary>
            /// Create muscle group with human binding.
            /// </summary>
            /// <param name="dof"></param>
            /// <param name="datas"></param>
            public MuscleGroup(HumanPartDof dof, MuscleData[] datas)
            {
                Datas = datas;
                GroupName = dof;
                EditingDatas = new List<MuscleData>();
            }

            [HideInInspector]
            public readonly HumanPartDof GroupName;

            /// <summary>
            /// **Important this array is reference to same origin handle array, must be careful when editing target that between <see cref="Min"/> and <see cref="Max"/>.  
            /// </summary>
            [HideInInspector]
            public readonly MuscleData[] Datas;

            //public MuscleData[] GetMuscleDatas()
            //{
            //    return Datas.GetWithInIndex(Min,Count).ToArray();
            //}

            [HideInInspector]
            public int Max = int.MinValue;
            [HideInInspector]
            public int Min = int.MaxValue;

            /// <summary>
            /// Quick access to <see cref="Datas"/>, index should between child part dof.
            /// </summary>
            public MuscleData this[int index]
            {
                get
                {
                    return Datas[index + Min];
                }
                set
                {
                    var sn = index + Min;
                    var old = Datas[sn];
                    if (Datas[sn].Equals(value))
                        return;

                    Datas[index + Min] = value;
                    onMuscleEditing?.Invoke();
                }
            }

            public int Count => Max - Min + 1;

            /// <summary>
            /// ????
            /// </summary>
            /// <param name="sn"></param>
            /// <param name="muscleData"></param>
            public void KeyVolume(int sn, MuscleData muscleData)
            {
                Max = Mathf.Max(sn, Max);
                Min = Mathf.Min(sn, Min);
                EditingDatas.Add(muscleData);
            }

            /// <summary>
            /// 
            /// </summary>
            [HideLabel]
            [ShowInInspector]
            [OnValueChanged(nameof(Update), IncludeChildren = true, InvokeOnInitialize = false)]
            protected List<MuscleData> EditingDatas;

            /// <summary>
            /// Post muscle editing event.
            /// </summary>
            void Update()
            {
                var length = EditingDatas.Count;

                for (int i = 0; i < length; i++)
                {
                    Debug.Log(Datas[Min + i]);
                    Datas[Min + i].Value = EditingDatas[i].Value;
                }

                onMuscleEditing?.Invoke();
            }

            public IEnumerator<MuscleData> GetEnumerator()
            {
                for (int i = Min; i <= Max; i++)
                {
                    yield return Datas[i];
                }
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        #endregion

        #region Read Muscle 

        /// <summary>
        /// Read muscle value in animation stream.
        /// </summary>
        ReadMuscleJob ReadMuscleJob;

        /// <summary>
        /// Reading muscle job in playable stream.
        /// </summary>
        AnimationScriptPlayable ReadMusclePlayable;

        /// <summary>
        /// Muscle handles for reading job.
        /// </summary>
        NativeArray<MuscleHandle> MuscleHanldes_Reading;

        /// <summary>
        /// Muscle values for reading job.
        /// </summary>
        NativeArray<float> MuscleValues_Reading;

        #endregion

        #region Write Muscle

        /// <summary>
        /// Overwrite muscle value in animation stream.
        /// </summary>
        WriteMuscleJob WriteMuscleJob;

        /// <summary>
        /// Writing muscle job in playable stream.
        /// </summary>
        AnimationScriptPlayable WriteMusclePlayable;

        /// <summary>
        /// Muscle handles for writing job.
        /// </summary>
        NativeArray<MuscleHandle> MuscleHanldes_Writing;

        /// <summary>
        /// Muscle values for writing job.
        /// </summary>
        NativeArray<float> MuscleValues_Writing;

        #endregion



        #endregion

    }

    public partial class CharacterDirector // FK(T1)
    {
        #region Posture - FK

        [SerializeField, HideInInspector]
        protected Animator Animator_FK;

        Dictionary<Transform, Transform> SkeletonMap_FK2Origin;

        [SerializeField, HideInInspector]
        Transform EditingBone;

        protected virtual void SetupCharacter_FK()
        {
            SkeletonMap_FK2Origin = SkeletonMap_Origin2Muscle.Inverse();

            if (Animator_FK != null)
                ClearCharacter_FK();

            if (Animator_FK == null)
            {
                if (Animator_Muscle != null)
                    Animator_FK = Animator_Muscle;
                else
                {
                    throw new InvalidOperationException($"Initialize {Animator_Muscle} first with {nameof(SetupCharacter_Muscle)}.");
                    Animator_Muscle = GetDummyCharacter();
                    Animator_FK = Animator_Muscle;
                }

            }

            var animator = Animator_FK;
            SceneVisibilityManager.instance.DisablePicking(animator.gameObject, false);
        }

        protected virtual void ClearCharacter_FK()
        {
            Animator_FK = null;
        }

        /// <summary>
        /// ???? [ToDo]?? *empty
        /// </summary>
        protected virtual void RoutineHandler_OnTick_FK()
        {
            if (Animator_FK == null || Animator_FK.isHuman)
                return;
        }

        /// <summary>
        /// Draw inspector for FK operation.
        /// </summary>
        protected virtual void DrawPanel_FK()
        {
            if (Animator_FK != null)
            {
                EditorGUI.BeginChangeCheck();
                var flag = Animator_FK.gameObject.hideFlags;
                var visuable = flag.HasFlag(HideFlags.HideInHierarchy);
                visuable = EditorGUILayout.ToggleLeft("Hide Skeleton", visuable);
                if (EditorGUI.EndChangeCheck())
                {
                    Animator_FK.gameObject.hideFlags = visuable ? flag | HideFlags.HideInHierarchy : flag ^ HideFlags.HideInHierarchy;
                }
            }

            EditingBone = EditorGUILayout.ObjectField(EditingBone, typeof(Transform), true) as Transform;

            if (EditingBone != null)
            {
                EditorGUI.BeginChangeCheck();
                var newRot = EditorGUILayout.Vector3Field("Rotation", EditingBone.localEulerAngles);
                if (EditorGUI.EndChangeCheck())
                {
                    EditingBone.localEulerAngles = newRot;
                }

                {
                    GUILayout.BeginHorizontal();

                    if (GUILayout.Button("Parent"))
                    {
                        if (EditingBone.parent != null)
                            EditingBone = EditingBone.parent;

                        if (LastSceneView != null)
                            LastSceneView.Repaint();
                    }

                    if (GUILayout.Button("Child"))
                    {
                        if (EditingBone.childCount > 0)
                            EditingBone = EditingBone.GetChild(0);

                        if (LastSceneView != null)
                            LastSceneView.Repaint();
                    }

                    GUILayout.EndHorizontal();
                }
            }
        }

        protected virtual void DrawSceneGUI_MuscleFK(Rect mainPanelRect)
        {
            var width = LastSceneView.position.width * 0.7f;
            {
                GUILayout.BeginArea(mainPanelRect.AlignLeftOut(500, 10f));//.SubX(10).SubY(20));

                {
                    GUILayout.BeginHorizontal();

                    if (GUILayout.Button("Sync Posture"))
                    {
                        RequiredSyncT0T1Posture = true;
                    }

                    //if (GUILayout.Button("Disable Posture"))
                    //    RigBuilder.layers.ForEach(x => x.rig.weight = Mathf.Abs(x.rig.weight - 1f));

                    //if (RigBuilder != null && !RigBuilder.layers.IsNullOrEmpty())
                    //{
                    //    EditorGUI.BeginChangeCheck();
                    //    var rig = RigBuilder.layers.First().rig;
                    //    var newWeight = GUILayout.VerticalSlider(rig.weight, 1f, 0f, GUILayout.Height(100), GUILayout.Width(40));
                    //    if (EditorGUI.EndChangeCheck())
                    //        rig.weight = newWeight;
                    //}
                    //else
                    //    GUILayout.VerticalSlider(0.5f, 1f, 0f, GUILayout.Height(100), GUILayout.Width(40));

                    GUILayout.EndHorizontal();
                }

                GUILayout.EndArea();
            }
        }


        #endregion

    }

    public partial class CharacterDirector // IK(T2)
    {
        #region IK

        [SerializeField, HideInInspector]
        Animator Animator_IK;

        [SerializeField, HideInInspector]
        BoneRenderer BoneRenderer_IK;

        /// <summary>
        /// Color for FK bone renderer.
        /// </summary>
        [HideInInspector]
        public Color color_IKBone = Color.blue.SetAlpha(0.5f);


        /// <summary>
        /// Cache skeleton map for sync IK operation.
        /// </summary>
        Dictionary<Transform, Transform> SkeletonMap_IK2Origin;

        /// <summary>
        /// Humanoid posture handler for IK editing.
        /// </summary>
        HumanPoseHandler PoseHandler_IK;

        /// <summary>
        /// Posture of IK result.
        /// </summary>
        HumanPose pose_IK;

        [SerializeField, HideInInspector]
        Transform bone_hips_IK;

        /// <summary>
        /// Setup universal IK datas.
        /// </summary>
        protected virtual void SetupCharacter_IK()
        {
            if (Animator_IK != null)
                ClearCharacter_IK();

            if (Animator_IK == null)
                Animator_IK = GetDummyCharacter();

            // ?? unitybug - crash
            try
            {
                Animator_IK.gameObject.hideFlags
                    //|= HideFlags.NotEditable;
                    = HideFlags.NotEditable | HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Animator_IK.gameObject.hideFlags = HideFlags.None;
            }


            var animator = Animator_IK;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            if (IsQualifyMuscleCharacter)
            {
                bone_hips_IK = animator.GetHipsBone();
            }

            // bind bones with origin skeleton
            {
                var tfs = animator.GetComponentsInChildren<Transform>(true);
                SkeletonMap_IK2Origin = tfs.
                    Where(x => SkeletonMap.ContainsKey(x.name)).
                    ToDictionary(x => x, x => SkeletonMap[x.name]);
            }

            if (BoneRenderer_IK == null)
            {
                BoneRenderer_IK = animator.gameObject.AddComponent<BoneRenderer>();
                BoneRenderer_IK.boneSize = 2f;
                BoneRenderer_IK.boneColor = color_IKBone;

                if (animator.isHuman)
                {
                    var root = animator.GetBoneTransform(HumanBodyBones.Hips).parent;
                    BoneRenderer_IK.transforms = SkeletonMap_IK2Origin.Keys.Where(x => x != root).ToArray();
                    SceneVisibilityManager.instance.DisablePicking(root.gameObject, true);
                }
                else
                {
                    BoneRenderer_IK.transforms = SkeletonMap_IK2Origin.Keys.ToArray();
                    // todo get skeleton
                }
            }

            // create muscle handler **main thread
            PoseHandler_IK = new HumanPoseHandler(animator.avatar, animator.avatarRoot);

            // initialize muscles
            PoseHandler_IK.GetHumanPose(ref pose_IK);

            // setup rigging IK
            SetupIK_Rigging();

            // setup playable IK
            if (false)
                SetupPlayableIK();

            ActivateIK();
        }

        protected virtual void ClearCharacter_IK()
        {
            if (OriginSkeletonHandles.IsCreated)
                OriginSkeletonHandles.Dispose();

            if (IKSkeletonHandles.IsCreated)
                IKSkeletonHandles.Dispose();

            if (Animator_IK != null)
                Animator_IK.gameObject.Destroy();

            SkeletonMap_IK2Origin = null;
            PoseHandler_IK = null;

            if (RigBuilder != null)
                RigBuilder.Clear();

            if (CustomRiggingGraph.IsValid())
                CustomRiggingGraph.Destroy();

            Animator_IK = null;
        }

        protected virtual void ResetCharacter_IK()
        {
            if (RightHandRiggingIK != null)
                RightHandRiggingIK.ResetGoal();

            if (LeftHandRiggingIK != null)
                LeftHandRiggingIK.ResetGoal();

            if (RightFootRiggingIK != null)
                RightFootRiggingIK.ResetGoal();

            if (LeftFootRiggingIK != null)
                LeftFootRiggingIK.ResetGoal();
        }

        /// <summary>
        /// Mapping FK skeleton(T1) and evaluate rigging graph(T2).
        /// </summary>
        protected virtual void RoutineHandler_OnTick_IK()
        {
            if (CustomRiggingGraph.IsValid())
            {
                CustomRiggingGraph.Evaluate(RoutineHandler.lastDeltaTime);

                if (IsSyncIKPosture)
                    SyncFK2IK();
            }
        }

        [SerializeField, HideInInspector]
        private bool isSyncIKPosture;

        /// <summary>
        /// Whether auto align FK posture to IK posture.
        /// </summary>
        protected bool IsSyncIKPosture { get => isSyncIKPosture; set => isSyncIKPosture = value; }



        GUIContent content_syncEdit;
        GUIContent content_enableIK;
        GUIContent content_clampMuscle;


        protected virtual void DrawPanel_IK()
        {
            if (Animator_Muscle != null)
            {
                EditorGUI.BeginChangeCheck();
                var flag = Animator_IK.gameObject.hideFlags;
                var visuable = flag.HasFlag(HideFlags.HideInHierarchy);
                visuable = EditorGUILayout.ToggleLeft("Hide Skeleton", visuable);
                if (EditorGUI.EndChangeCheck())
                {
                    Animator_IK.gameObject.hideFlags = visuable ? flag | HideFlags.HideInHierarchy : flag ^ HideFlags.HideInHierarchy;
                }
            }

            {
                GUILayout.BeginHorizontal();

                isUsingRiggingIK = EditorGUILayout.ToggleLeft("Use Rigging", isUsingRiggingIK);

                GUILayout.EndHorizontal();
            }

            //usingIK = true;
            if (isUsingRiggingIK)
            {
                DrawIKOptions("Rigging IK");

                // rigging IK - obsolete
                {
                    DrawIKCreation_Rigging();
                    DrawIKConfigs_Rigging();
                }
            }
            else
            {
                DrawIKOptions("IK Goal");

                // playable IK - read-writeable
                if (Selector != null)
                {
                    switch (Selector.selected_IKGoal)
                    {
                        case AvatarSelector.IKGoal.Head:
                            break;
                        case AvatarSelector.IKGoal.LeftHand:
                            break;
                        case AvatarSelector.IKGoal.RightHand:
                            break;
                        case AvatarSelector.IKGoal.LeftFoot:
                            break;
                        case AvatarSelector.IKGoal.RightFoot:
                            break;
                        case AvatarSelector.IKGoal.LastIKGoal:
                            break;
                    }
                }
            }

            // draw IK configs
        }

        protected virtual void DrawSceneGUI_IK(Rect mainPanelRect)
        {
            var width = LastSceneView.position.width * 0.7f;
            {
                GUILayout.BeginArea(mainPanelRect.AlignLeftOut(500, 10f));//.SubX(10).SubY(20));

                {
                    GUILayout.BeginHorizontal();

                    if (GUILayout.Button("Sync Posture"))
                        SyncFK2IK();

                    if (GUILayout.Button("Disable Posture"))
                        RigBuilder.layers.ForEach(x => x.rig.weight = Mathf.Abs(x.rig.weight - 1f));

                    if (RigBuilder != null && !RigBuilder.layers.IsNullOrEmpty())
                    {
                        EditorGUI.BeginChangeCheck();
                        var rig = RigBuilder.layers.First().rig;
                        var newWeight = GUILayout.VerticalSlider(rig.weight, 1f, 0f, GUILayout.Height(100), GUILayout.Width(40));
                        if (EditorGUI.EndChangeCheck())
                            rig.weight = newWeight;
                    }
                    else
                        GUILayout.VerticalSlider(0.5f, 1f, 0f, GUILayout.Height(100), GUILayout.Width(40));

                    GUILayout.EndHorizontal();
                }

                GUILayout.EndArea();
            }
        }

        /// <summary>
        /// Draw control options. **Sync **IK **Muscle
        /// </summary>
        void DrawIKOptions(string title)
        {
            if (style_toggle == null)
                style_toggle = new GUIStyle(GUI.skin.toggle) { alignment = TextAnchor.MiddleCenter };

            if (ValidIKQualify)
            {
                GUILayout.BeginHorizontal();

                // title
                {
                    GUILayout.BeginVertical(style_Handle);
                    GUILayout.Label(title);
                    GUILayout.EndVertical();
                }

                IsSyncIKPosture = GUILayout.Toggle(IsSyncIKPosture, content_syncEdit, style_toggle);

                {
                    EditorGUI.BeginDisabledGroup(PoseHandler_IK == null);
                    MuscleConstraint = GUILayout.Toggle(MuscleConstraint, content_clampMuscle, style_toggle);
                    EditorGUI.EndDisabledGroup();
                }

                GUILayout.EndHorizontal();
            }
            else
            {
                if (GUILayout.Button("Setup IK"))
                    ActivateIK();
            }
        }

        /// <summary>
        /// Whether IK been initialized.
        /// </summary>
        bool ValidIKQualify => isUsingRiggingIK ? RigBuilder != null : IKSolver.Jobs != null;


        /// <summary>
        /// ???? IK job playable not include rigging graph.
        /// </summary>
        [Obsolete]
        Playable IKPlayable;

        /// <summary>
        /// Read muscle and FK posture and process rigging playable.
        /// </summary>
        PlayableGraph CustomRiggingGraph;

        NativeArray<TransformSceneHandle> OriginSkeletonHandles;
        NativeArray<TransformStreamHandle> IKSkeletonHandles;

        /// <summary>
        /// Activae IK and initialize playable graph.
        /// </summary>
        protected virtual void ActivateIK()
        {
            if (isUsingRiggingIK)
            {
                if (RigBuilder == null)
                {
                    Debug.LogException(new NotImplementedException("Required rigbuilder."));
                    return;
                }

                Debug.Log(RigBuilder.runInEditMode);


                // Reset Rigging
                ResetCharacter_IK();

                //if (AnimationDirector != null && AnimationDirector.Graph.IsValid())
                //{
                //    Debug.Log($"Inherit custom graph for rigging playable [{AnimationDirector.Graph.IsValid()}].");

                //    IKPlayable = RigBuilder.BuildPreviewGraph(AnimationDirector.Graph, Playable.Null);
                //}
                //else
                {
                    Debug.Log($"Creating custom rigging graph [{RigBuilder.graph.IsValid()}].");

                    if (Animator_IK.isHuman)
                    {
                        Debug.Log("Creating humanoid rigging layers.");

                        // set default posture playable
                        var graph = PlayableGraph.Create("Rigging");
                        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);

                        {
                            if (CustomRiggingGraph.IsValid())
                                CustomRiggingGraph.Destroy();

                            CustomRiggingGraph = graph;
                        }

                        Playable setPosePlayable;

                        //setPosePlayable = AnimationScriptPlayable.Create(graph, TPoseJob.Create(Animator_IK));

                        //var clip = EditorAnimationUtility.TPoseClip(Animator_IK);
                        //setPosePlayable = AnimationClipPlayable.Create(graph, clip);

                        {
                            if (OriginSkeletonHandles.IsCreated)
                                OriginSkeletonHandles.Dispose();

                            if (IKSkeletonHandles.IsCreated)
                                IKSkeletonHandles.Dispose();

                            var pairs = SkeletonMap_IK2Origin.ToArray();

                            var length = pairs.Length;
                            OriginSkeletonHandles = new(length, Allocator.Persistent);
                            IKSkeletonHandles = new(length, Allocator.Persistent);

                            Debug.Log(string.Join("\n", pairs.Select(x => x.Key.name)));

                            for (int i = 0; i < length; i++)
                            {
                                var fkBone = SkeletonMap_Origin2Muscle[pairs[i].Value];
                                OriginSkeletonHandles[i] = Animator_IK.BindSceneTransform(fkBone);
                                IKSkeletonHandles[i] = Animator_IK.BindStreamTransform(pairs[i].Key);
                            }

                            var job = new CopyPosetureJob()
                            {
                                from = OriginSkeletonHandles,
                                to = IKSkeletonHandles
                            };

                            setPosePlayable = AnimationScriptPlayable.Create(graph, job);
                        }

                        // build rigging playables

                        {
                            RigBuilder.Clear();

                            // prevent bug code, equal to RigBuilder.StartPreview();
                            {
                                foreach (var layer in RigBuilder.layers)
                                {
                                    if (layer.IsValid())
                                        layer.Reset();

                                    layer.Initialize(Animator_IK);
                                }

                                IRigLayer[] layers = RigBuilder.layers.ToArray();
                                ReflectionExtension.SetFieldValue(typeof(RigBuilder), RigBuilder, "m_RuntimeRigLayers", layers, System.Reflection.BindingFlags.Instance);
                            }

                            var newRiggingPlayable = RigBuilder.BuildPreviewGraph(graph, setPosePlayable);

                            AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, "Rigging Playable Output", Animator_IK);
                            output.SetAnimationStreamSource(AnimationStreamSource.DefaultValues);
                            output.SetSortingOrder(1000);

                            // Connect last rig playable to output
                            output.SetSourcePlayable(newRiggingPlayable);
                            RigBuilder.SyncLayers();
                        }

                        graph.Play();
                        ReflectionExtension.SetPropValue(typeof(RigBuilder), RigBuilder, "graph", graph, System.Reflection.BindingFlags.Instance);

                        Debug.Log($"Graph is valid [{graph.IsValid()}].");
                    }
                    else
                    {
                        RigBuilder.Build();
                    }
                }
            }
            else
            {
                // from playable IK job
                SetupPlayableIK();
            }
        }


        #region Playable

        IKGoalSolver IKSolver;

        [SerializeField, HideInInspector]
        protected Transform IKHandler;
        //,
        //LeftFootGoal,
        //LeftFootHint,
        //RightFootGoal,
        //RightFootHint,
        //LeftHandGoal,
        //LeftHandHint,
        //RightHandGoal,
        //RightHandHint;

        protected IKData LeftHand;
        protected IKData RightHand;
        protected IKData LeftFoot;
        protected IKData RightFoot;

        protected HandIKJob LeftHandIKJob;
        protected HandIKJob RightHandIKJob;
        protected HandIKJob LeftFootIKJob;
        protected HandIKJob RightFootIKJob;

        //protected TwoBoneIKAnimationJob LeftHandIKJob;
        //protected TwoBoneIKAnimationJob RightHandIKJob;
        //protected TwoBoneIKAnimationJob LeftFootIKJob;
        //protected TwoBoneIKAnimationJob RightFootIKJob;

        //protected AnimationScriptPlayable LeftHandIKPlayable;
        //protected AnimationScriptPlayable RightHandIKPlayable;
        //protected AnimationScriptPlayable LeftFootIKPlayable;
        //protected AnimationScriptPlayable RightFootIKPlayable;

        protected class IKData
        {
            public IKData(Animator animator, AvatarIKGoal goal, Transform rootNode)
            {
                goal.CreateHandles(out Goal, out Hint, rootNode);
                Job = HandIKJob.Create(goal);
                Job.target = ReadOnlyTransformHandle.Bind(animator, Goal);
                Job.hint = ReadOnlyTransformHandle.Bind(animator, Hint);
            }

            public Transform Goal;
            public Transform Hint;

            public bool PinnedGoal;
            public bool PinnedHint;

            public HandIKJob Job;
        }

        /// <summary>
        /// Setup animator playable IK Goals.
        /// </summary>
        protected virtual void SetupPlayableIK()
        {
            var animator = Animator_IK;

            // create IK handles
            if (IKHandler == null)
            {
                IKHandler = new GameObject("IK Handles").transform;
                IKHandler.SetParent(animator.transform);
                IKHandler.Zero();

                LeftHand = new(animator, AvatarIKGoal.LeftHand, IKHandler);
                RightHand = new(animator, AvatarIKGoal.RightHand, IKHandler);

                LeftFoot = new(animator, AvatarIKGoal.LeftFoot, IKHandler);
                RightFoot = new(animator, AvatarIKGoal.RightFoot, IKHandler);

                //AvatarIKGoal.LeftFoot.CreateHandles(out LeftFootGoal, out LeftFootHint, IKHandler);
                //AvatarIKGoal.RightFoot.CreateHandles(out RightFootGoal, out RightFootHint, IKHandler);

                //AvatarIKGoal.LeftHand.CreateHandles(out LeftHandGoal, out LeftHandHint, IKHandler);
                //AvatarIKGoal.RightHand.CreateHandles(out RightHandGoal, out RightHandHint, IKHandler);
            }

            // setup IK Jobs
            {
                //LeftHandIKJob = HandIKJob.Create(AvatarIKGoal.LeftHand);
                //LeftHandIKJob.target = ReadOnlyTransformHandle.Bind(Animator, LeftHandGoal);
                //LeftHandIKJob.hint = ReadOnlyTransformHandle.Bind(Animator, LeftHandHint);

                //RightHandIKJob = HandIKJob.Create(AvatarIKGoal.RightHand);
                //RightHandIKJob.target = ReadOnlyTransformHandle.Bind(Animator, RightHandGoal);
                //RightHandIKJob.hint = ReadOnlyTransformHandle.Bind(Animator, RightHandHint);

                //LeftFootIKJob = HandIKJob.Create(AvatarIKGoal.LeftFoot);
                //LeftFootIKJob.target = ReadOnlyTransformHandle.Bind(Animator, LeftFootGoal);
                //LeftFootIKJob.hint = ReadOnlyTransformHandle.Bind(Animator, LeftFootHint);

                //RightHandIKJob = HandIKJob.Create(AvatarIKGoal.RightFoot);
                //RightHandIKJob.target = ReadOnlyTransformHandle.Bind(Animator, RightFootGoal);
                //RightHandIKJob.hint = ReadOnlyTransformHandle.Bind(Animator, RightFootHint);
            }

            //LeftHandIKJob = TwoBoneIKAnimationJob.Create(Animator, HumanBodyBones.LeftHand);
            //RightHandIKJob = TwoBoneIKAnimationJob.Create(Animator, HumanBodyBones.RightHand);
            //LeftFootIKJob = TwoBoneIKAnimationJob.Create(Animator, HumanBodyBones.LeftFoot);
            //RightFootIKJob = TwoBoneIKAnimationJob.Create(Animator, HumanBodyBones.RightFoot);

            // create IK playables
            {
                //LeftHandIKPlayable = AnimationScriptPlayable.Create(AnimationDirector.Graph, LeftHandIKJob);
                //RightHandIKPlayable = AnimationScriptPlayable.Create(AnimationDirector.Graph, RightHandIKJob);
                //LeftFootIKPlayable = AnimationScriptPlayable.Create(AnimationDirector.Graph, LeftFootIKJob);
                //RightFootIKPlayable = AnimationScriptPlayable.Create(AnimationDirector.Graph, RightFootIKJob);

                IKSolver = new IKGoalSolver()
                {
                    Jobs = new IAnimationJob[4]
                    {
                        LeftHand.Job,
                        RightHand.Job,
                        LeftFoot.Job,
                        RightFoot.Job,
                    }
                };

                if (AnimationDirector != null && AnimationDirector.Graph.IsValid())
                    IKPlayable = AnimationScriptPlayable.Create(AnimationDirector.Graph, IKSolver);
            }
        }

        protected virtual void DrawTwoBonePlayableIK(ref TwoBoneIKAnimationJob job)
        {
            AnimationStream stream = default;
            Animator.OpenAnimationStream(ref stream);
            var hs = stream.AsHuman();

        }

        #endregion


        #region Rigging

        protected virtual void SetupIK_Rigging()
        {
            RigBuilder = Animator_IK.InstanceIfNull<RigBuilder>();
            RigBuilder.runInEditMode = true;

            CreateRiggingIK_Hand();
            CreateRiggingIK_Foot();
        }

        [SerializeField, HideInInspector]
        public bool isUsingRiggingIK = true;

        protected virtual void DrawIKCreation_Rigging()
        {
            var options_label = GUILayout.Width(80);
            var options_control = GUILayout.Width(150);

            if (GUILayout.Button("Refresh IK"))
                ActivateIK();

            // draw IK creation
            {
                GUILayout.BeginVertical(GUI.skin.box);

                // draw Hand IK rig
                {
                    GUILayout.BeginHorizontal();

                    GUILayout.Label("Hand", options_label);
                    EditorGUILayout.ObjectField(HandIKRig, typeof(Rig), true);

                    if (HandIKRig == null)
                    {
                        if (GUILayout.Button("Create IK", options_control))
                            CreateRiggingIK_Hand();
                    }
                    else
                    {
                        HandIKRig.weight = EditorGUILayout.Slider(HandIKRig.weight, 0, 1f, options_control);
                    }

                    GUILayout.EndHorizontal();
                }

                // draw Foot IK rig
                {
                    GUILayout.BeginHorizontal();

                    GUILayout.Label("Foot", options_label);
                    EditorGUILayout.ObjectField(FootIKRig, typeof(Rig), true);

                    if (FootIKRig == null)
                    {
                        // create foot IK
                        if (GUILayout.Button("Create IK", options_control))
                            CreateRiggingIK_Foot();
                    }
                    else
                    {
                        FootIKRig.weight = EditorGUILayout.Slider(FootIKRig.weight, 0, 1f, options_control);
                    }

                    GUILayout.EndHorizontal();
                }
                GUILayout.EndVertical();
            }
        }

        protected virtual void DrawIKConfigs_Rigging()
        {
            {
                GUILayout.BeginVertical(GUI.skin.box);

                switch (Selector.selected_IKGoal)
                {
                    case AvatarSelector.IKGoal.Head:
                        break;
                    case AvatarSelector.IKGoal.LeftHand:
                        LeftHandRiggingIK = EditorGUILayout.ObjectField("LeftHand IK", LeftHandRiggingIK, typeof(TwoBoneIKConstraint), true) as TwoBoneIKConstraint;
                        DrawTwoBoneIKField(LeftHandRiggingIK);
                        break;
                    case AvatarSelector.IKGoal.RightHand:
                        RightHandRiggingIK = EditorGUILayout.ObjectField("RightHand IK", RightHandRiggingIK, typeof(TwoBoneIKConstraint), true) as TwoBoneIKConstraint;
                        DrawTwoBoneIKField(RightHandRiggingIK);
                        break;
                    case AvatarSelector.IKGoal.LeftFoot:
                        LeftFootRiggingIK = EditorGUILayout.ObjectField("LeftFoot IK", LeftFootRiggingIK, typeof(TwoBoneIKConstraint), true) as TwoBoneIKConstraint;
                        DrawTwoBoneIKField(LeftFootRiggingIK);

                        break;
                    case AvatarSelector.IKGoal.RightFoot:
                        RightFootRiggingIK = EditorGUILayout.ObjectField("RightFoot IK", RightFootRiggingIK, typeof(TwoBoneIKConstraint), true) as TwoBoneIKConstraint;
                        DrawTwoBoneIKField(RightFootRiggingIK);

                        break;
                    case AvatarSelector.IKGoal.LastIKGoal:
                        break;
                }
                GUILayout.EndVertical();
            }

        }

        /// <summary>
        /// Draw properties for two bone IK rigging datas.
        /// </summary>
        protected void DrawTwoBoneIKField(TwoBoneIKConstraint constraint)
        {
            if (constraint != null)
            {
                EditorGUI.BeginChangeCheck();

                Object newGoal, newHint;

                {
                    GUILayout.BeginVertical(GUI.skin.box);

                    // draw goal selection
                    {
                        GUILayout.BeginHorizontal();
                        if (GUILayout.Button("Goal", GUI.skin.label))
                        {
                            Selection.activeObject = constraint.data.target;
                            Selection.SetActiveObjectWithContext(constraint.data.target, constraint.data.target);
                            Tools.current = LastTool;
                        }
                        newGoal = EditorGUILayout.ObjectField(constraint.data.target, typeof(Transform), true);
                        GUILayout.EndHorizontal();
                    }

                    // draw hint selection
                    {
                        GUILayout.BeginHorizontal();
                        if (GUILayout.Button("Hint", GUI.skin.label))
                        {
                            Selection.activeObject = constraint.data.hint;
                            Selection.SetActiveObjectWithContext(constraint.data.hint, constraint.data.hint);
                            Tools.current = LastTool;
                        }
                        newHint = EditorGUILayout.ObjectField(constraint.data.hint, typeof(Transform), true);
                        GUILayout.EndHorizontal();
                    }

                    GUILayout.EndVertical();
                }

                Object newTip, newMiddle, newRoot;
                {
                    GUILayout.BeginVertical();
                    newTip = EditorGUILayout.ObjectField("Tip", SkeletonMap_IK2Origin[constraint.data.tip], typeof(Transform), true);
                    newMiddle = EditorGUILayout.ObjectField("Middle", SkeletonMap_IK2Origin[constraint.data.mid], typeof(Transform), true);
                    newRoot = EditorGUILayout.ObjectField("Root", SkeletonMap_IK2Origin[constraint.data.root], typeof(Transform), true);
                    GUILayout.EndVertical();
                }

                var newHintWeight = EditorGUILayout.Slider("Hint Weight", constraint.data.hintWeight, 0, 1f);
                var newTargetPositionWeight = EditorGUILayout.Slider("Target Position Weight", constraint.data.targetPositionWeight, 0, 1f);
                var newTargetRotationWeight = EditorGUILayout.Slider("Target Rotation Weight", constraint.data.targetRotationWeight, 0, 1f);

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(constraint, "Modify IK");

                    constraint.data.target = newGoal as Transform;
                    constraint.data.hint = newHint as Transform;

                    constraint.data.tip = SkeletonMap_IK2Origin.First(x => x.Value == newTip as Transform).Key;
                    constraint.data.mid = SkeletonMap_IK2Origin.First(x => x.Value == newMiddle as Transform).Key;
                    constraint.data.root = SkeletonMap_IK2Origin.First(x => x.Value == newRoot as Transform).Key;

                    constraint.data.hintWeight = newHintWeight;
                    constraint.data.targetPositionWeight = newTargetPositionWeight;
                    constraint.data.targetRotationWeight = newTargetRotationWeight;
                }
            }
        }

        protected virtual RigEffectorData.Style GetEffectorStyle()
        {
            return new()
            {
                color = Color.green,
                size = 0.13f,
                shape = EditorAssetsUtility.LoadAssetFromUniqueAssetPath<Mesh>("Library/unity default resources::Sphere"),
                position = Vector3.zero,
                rotation = Vector3.zero,
            };
        }

        /// <summary>
        /// Create rigging IK for hands.
        /// </summary>
        protected virtual void CreateRiggingIK_Hand()
        {
            var animator = Animator_IK;

            var twoBoneIKs = animator.GetComponentsInChildren<TwoBoneIKConstraint>();

            foreach (var ik in twoBoneIKs)
            {
                if (ik.name.ToUpper().Contains("Hand", StringComparison.InvariantCultureIgnoreCase))
                {
                    HandIKRig = ik.GetComponentInParent<Rig>();

                    if (HandIKRig)
                        break;
                }
            }

            if (HandIKRig == null)
            {
                HandIKRig = RiggingUtility.BuildHandRigs(animator, PR.identity, PR.identity, out RightHandRiggingIK, out LeftHandRiggingIK);

                var style = GetEffectorStyle();

                RigBuilder.AddEffector(RightHandRiggingIK.data.target, style);
                RigBuilder.AddEffector(LeftHandRiggingIK.data.target, style);

                style.color = new(1f, 0.6470588f, 0);
                RigBuilder.AddEffector(RightHandRiggingIK.data.hint, style);
                RigBuilder.AddEffector(LeftHandRiggingIK.data.hint, style);
            }

            if (dic_IKUpdate == null)
                dic_IKUpdate = new Dictionary<Transform, AvatarMaskBodyPart>(46);

            if (RigBuilder)
            {
                //var hands = HandIK.GetComponentsInChildren<TwoBoneIKConstraint>();

                //foreach (var hand in hands)
                //{
                //    var id = hand.gameObject.name.ToUpper();
                //    var maskPart = AvatarMaskBodyPart.LastBodyPart;

                //    if (id.Contains("RIGHT"))
                //    {
                //        RightHandIK = hand;
                //        maskPart = AvatarMaskBodyPart.RightArm;
                //    }
                //    else
                //    {
                //        LeftHandIK = hand;
                //        maskPart = AvatarMaskBodyPart.LeftArm;
                //    }
                //}

                SubscribeTransformUpdate(RightHandRiggingIK.data.target.gameObject, AvatarMaskBodyPart.RightArm);
                SubscribeTransformUpdate(RightHandRiggingIK.data.hint.gameObject, AvatarMaskBodyPart.RightArm);

                SubscribeTransformUpdate(LeftHandRiggingIK.data.target.gameObject, AvatarMaskBodyPart.LeftArm);
                SubscribeTransformUpdate(LeftHandRiggingIK.data.hint.gameObject, AvatarMaskBodyPart.LeftArm);
            }
        }

        /// <summary>
        /// Create rigging IK for foot.
        /// </summary>
        protected virtual void CreateRiggingIK_Foot()
        {
            var animator = Animator_IK;

            var twoBoneIKs = animator.GetComponentsInChildren<TwoBoneIKConstraint>();

            foreach (var ik in twoBoneIKs)
            {
                if (ik.name.ToUpper().Contains("Foot", StringComparison.InvariantCultureIgnoreCase))
                {
                    FootIKRig = ik.GetComponentInParent<Rig>();

                    if (FootIKRig)
                        break;
                }
            }

            if (FootIKRig == null)
            {
                FootIKRig = RiggingUtility.BuildFootRigs(animator, PR.identity, PR.identity, out RightFootRiggingIK, out LeftFootRiggingIK);

                var style = GetEffectorStyle();

                RigBuilder.AddEffector(RightFootRiggingIK.data.target, style);
                RigBuilder.AddEffector(LeftFootRiggingIK.data.target, style);

                style.color = new(1f, 0.6470588f, 0);

                RigBuilder.AddEffector(RightFootRiggingIK.data.hint, style);
                RigBuilder.AddEffector(LeftFootRiggingIK.data.hint, style);
            }

            if (dic_IKUpdate == null)
                dic_IKUpdate = new Dictionary<Transform, AvatarMaskBodyPart>(46);

            if (RigBuilder)
            {
                //var hands = HandIK.GetComponentsInChildren<TwoBoneIKConstraint>();

                //foreach (var hand in hands)
                //{
                //    var id = hand.gameObject.name.ToUpper();
                //    var maskPart = AvatarMaskBodyPart.LastBodyPart;

                //    if (id.Contains("RIGHT"))
                //    {
                //        RightHandIK = hand;
                //        maskPart = AvatarMaskBodyPart.RightArm;
                //    }
                //    else
                //    {
                //        LeftHandIK = hand;
                //        maskPart = AvatarMaskBodyPart.LeftArm;
                //    }
                //}

                SubscribeTransformUpdate(RightFootRiggingIK.data.target.gameObject, AvatarMaskBodyPart.RightLeg);
                SubscribeTransformUpdate(RightFootRiggingIK.data.hint.gameObject, AvatarMaskBodyPart.RightLeg);

                SubscribeTransformUpdate(LeftFootRiggingIK.data.target.gameObject, AvatarMaskBodyPart.LeftLeg);
                SubscribeTransformUpdate(LeftFootRiggingIK.data.hint.gameObject, AvatarMaskBodyPart.LeftLeg);
            }
        }



        RigBuilder RigBuilder;

        [HideInInspector]
        public Rig HandIKRig;
        [HideInInspector]
        public Rig FootIKRig;

        [HideInInspector]
        public TwoBoneIKConstraint LeftHandRiggingIK;
        [HideInInspector]
        public TwoBoneIKConstraint RightHandRiggingIK;
        [HideInInspector]
        public TwoBoneIKConstraint LeftFootRiggingIK;
        [HideInInspector]
        public TwoBoneIKConstraint RightFootRiggingIK;

        #endregion


        protected virtual void SyncFK2IK()
        {
            if (LeftHandRiggingIK != null)
                MappingTwoBoneIK(LeftHandRiggingIK);
            if (RightHandRiggingIK != null)
                MappingTwoBoneIK(RightHandRiggingIK);
            if (LeftFootRiggingIK != null)
                MappingTwoBoneIK(LeftFootRiggingIK);
            if (RightFootRiggingIK != null)
                MappingTwoBoneIK(RightFootRiggingIK);
        }

        /// <summary>
        /// 
        /// </summary>
        protected virtual void MappingTwoBoneIK(TwoBoneIKConstraint constraint)
        {
            // mapping goal coordinate
            if (constraint.data.maintainTargetPositionOffset || constraint.data.maintainTargetRotationOffset)
            {
                if (SkeletonMap_IK2Origin.TryGetValue(constraint.data.tip, out var mappinpTip) && SkeletonMap_Origin2Muscle.TryGetValue(mappinpTip, out mappinpTip))
                {
                    if (mappinpTip.hasChanged)
                    {
                        mappinpTip.hasChanged = false;

                        var localGoalPR = constraint.data.tip.InverseTransformPR(constraint.data.target);
                        constraint.data.target.SetWorldPR(mappinpTip.TransformPR(localGoalPR));
                    }
                }
            }
            else
            {
                if (SkeletonMap_IK2Origin.TryGetValue(constraint.data.tip, out var mappinpTip) && SkeletonMap_Origin2Muscle.TryGetValue(mappinpTip, out mappinpTip))
                {
                    if (mappinpTip.hasChanged)
                    {
                        mappinpTip.hasChanged = false;

                        constraint.data.target.Copy(mappinpTip);
                    }
                }
            }

            // mapping hint coordinate
            if (SkeletonMap_IK2Origin.TryGetValue(constraint.data.mid, out var mappingMiddle) && SkeletonMap_Origin2Muscle.TryGetValue(mappingMiddle, out mappingMiddle))
            {
                if (mappingMiddle.hasChanged)
                {
                    mappingMiddle.hasChanged = false;
                    var localHintPR = constraint.data.mid.InverseTransformPR(constraint.data.hint);
                    constraint.data.hint.SetWorldPR(mappingMiddle.TransformPR(localHintPR));
                }
            }
        }

        /// <summary>
        /// Register transform update event.
        /// </summary>
        void SubscribeTransformUpdate(GameObject obj, AvatarMaskBodyPart maskPart)
        {
            var checkUpdate = obj.InstanceIfNull<TransformPoster>();
            Assert.IsFalse(checkUpdate == null);
            Assert.IsFalse(dic_IKUpdate == null);

            dic_IKUpdate.SafeAdd(checkUpdate.transform, maskPart);
            checkUpdate.OnMove += UpdateIK;
        }

        /// <summary>
        /// Binding IK for transform update.
        /// </summary>
        [Obsolete]
        [HideInInspector, Sirenix.OdinInspector.ReadOnly]
        Dictionary<Transform, AvatarMaskBodyPart> dic_IKUpdate;

        /// <summary>
        /// ?????
        /// </summary>
        void UpdateIK(Transform tf)
        {
            if (dic_IKUpdate.TryGetValue(tf, out var maskPart))
                SetMuscleIK(false, maskPart.ToHumanDof());
        }



        /// <summary>
        /// ?????
        /// </summary>
        /// <param name="enable"></param>
        /// <param name="humanDof"></param>
        [Obsolete("??????")]
        void SetMuscleIK(bool enable, HumanPartDof humanDof)
        {
            var group = Groups[humanDof];

            PoseHandler_Muscle.GetHumanPose(ref HumanMuscles);

            var muscles = HumanMuscles.muscles;
            var length = group.Count;
            var index = group.Min;
            for (int i = 0; i < length; i++)
            {
                var data = group[i];
                data.Value = muscles[i + index];
                group[i] = data;
            }

            //var length = group.Max - group.Min;
            //for (int i = 0; i < length; i++)
            //{
            //    var data= group[i];
            //    data.InheritIKOverwrite = enable;
            //    group[i] = data;
            //}
        }



        #endregion

    }
}