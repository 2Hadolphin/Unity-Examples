using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Events;
using UnityEditor;

using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities;
using Sirenix.Utilities.Editor;



namespace Return.Editors
{
    /// <summary>
    /// Record transform animations.
    /// </summary>
    public class TransformRecorderEditor : OdinEditorWindow
    {
        [MenuItem(MenuUtil.Tool_Asset_Animation + "Transform Recorder")]
        static void OpenWindow()
        {
            var window = GetWindow<TransformRecorderEditor>();
            window.position = GUIHelper.GetEditorWindowRect().AlignCenter(400, 500);
            window.titleContent = new GUIContent("TransformRecorder", SdfIcons.CreateTransparentIconTexture(SdfIconType.Record2, Color.white, 15, 15, 5));
        }

        [SerializeField]
        DropZone DropZone;

        protected override void OnEnable()
        {
            base.OnEnable();

            if (DropZone == null)
            {
                DropZone = new();
            }

            DropZone.customText = "Drop Recording Targets";
            DropZone.allowSceneObj = true;
            DropZone.allowType = typeof(GameObject);
            DropZone.onTargetAdded = (x) => AddTransform((x as GameObject).transform);

            if (!BindingTransforms.IsNullOrEmpty())
            {
                dic_bindings.Clear();

                var type = typeof(Transform);

                foreach (var tf in BindingTransforms)
                {
                    var path = AnimationUtility.CalculateTransformPath(tf, Root);
                    var binding = new EditorCurveBinding() { path = path, type = type };
                    dic_bindings.Add(tf, binding);
                }

                if(!dic_bindings.ContainsKey(Root))
                {
                    var path = AnimationUtility.CalculateTransformPath(Root, Root);
                    var binding = new EditorCurveBinding() { path = path, type = type };
                    dic_bindings.Add(Root, binding);
                }
            }
        }

        const string cst_binding= "Binding";
        const string cst_recording = "Record";
   
        [BoxGroup(cst_binding)]
        [HorizontalGroup(cst_binding+"\\Root")]
        [SerializeField,Required]
        Transform m_root;

        [HorizontalGroup(cst_binding + "\\Root")]
        [Button]
        void SearchChilds()
        {
            if (Root == null) return;

            var tfs = Root.Traverse();
            foreach (var tf in tfs)
            {
                AddTransform(tf);
            }
        }

        [HorizontalGroup(cst_binding + "\\Binding")]
        [SerializeField]
        bool m_recordPosition=true;
        [HorizontalGroup(cst_binding + "\\Binding")]
        [SerializeField]
        bool m_recordRotation=true;
        [HorizontalGroup(cst_binding + "\\Binding")]
        [SerializeField]
        bool m_recordScale;

        [BoxGroup(cst_binding)]
        [ListDrawerSettings(CustomRemoveElementFunction =nameof(RemoveTransform))]
        [SerializeField]
        List<Transform> m_bindingTransforms=new();

        public Dictionary<Transform, EditorCurveBinding> dic_bindings = new();


        public Transform Root { get => m_root; set => m_root = value; }
        public List<Transform> BindingTransforms { get => m_bindingTransforms; set => m_bindingTransforms = value; }


        /// <summary>
        /// Add bindings.
        /// </summary>
        public virtual void AddTransform(Transform tf)
        {
            if (tf == null) return;

            if (dic_bindings.ContainsKey(tf))
                return;

            BindingTransforms.Add(tf);
            var path= AnimationUtility.CalculateTransformPath(tf,Root);
            var binding = new EditorCurveBinding() { path= path,type = typeof(Transform) };
            dic_bindings.Add(tf, binding);
        }

        /// <summary>
        /// Remove bindings.
        /// </summary>
        public virtual void RemoveTransform(Transform tf)
        {
            if (tf == null) return;

            dic_bindings.Remove(tf);
            BindingTransforms.Remove(tf);
        }

        [BoxGroup(cst_recording)]
        [HorizontalGroup(cst_recording+"\\Clip")]
        [SerializeField]
        AnimationClip m_clip;
        public AnimationClip Clip { get => m_clip; set => m_clip = value; }

        bool hasClip => Clip != null;

        public bool RecordPosition { get => m_recordPosition; set => m_recordPosition = value; }
        public bool RecordRotation { get => m_recordRotation; set => m_recordRotation = value; }
        public bool RecordScale { get => m_recordScale; set => m_recordScale = value; }
        public float GapToInsertKeyframe { get => m_gapToInsertKeyframe; set => m_gapToInsertKeyframe = value; }

        [HorizontalGroup(cst_recording + "\\Clip")]
        [HideIf(nameof(hasClip))]
        [Button]
        void CreateDummyClip()
        {
            Clip = new AnimationClip();
            Clip.name = Root == null ? "Dummy Clip" : Root.name;
        }

        [HorizontalGroup(cst_recording + "\\Clip")]
        [ShowIf(nameof(hasClip))]
        [Button]
        void Save()
        {
            Clip = EditorAssetsUtility.Archive(Clip, Clip.name, true, EditorPathUtility.cst_TempArchive);
        }

        [BoxGroup(cst_recording)]
        [SerializeField]
        float m_gapToInsertKeyframe = 0.033333f;

        [ShowInInspector,ReadOnly]
        protected double lastInsertKeyframeTime =0;

        [BoxGroup(cst_recording)]
        [EnableIf(nameof(hasClip))]
        [Button]
        void Record(float time)
        {
            RecordTargets(time);
        }

        [BoxGroup(cst_recording)]
        [EnableIf(nameof(hasClip))]
        [Button]
        void Smooth()
        {
            Clip.SmoothCurves(0.5f);
            Clip.Dirty();
        }

        public void InsertKeyFrame(float time)
        {
            if(time <= lastInsertKeyframeTime || time - lastInsertKeyframeTime > GapToInsertKeyframe)
            {
                lastInsertKeyframeTime = time;
            }
            else
            {
                Debug.LogWarning($"Failure to insert key frame due to cool down [{time - lastInsertKeyframeTime}].");
                return;
            }

            RecordTargets(time);
        }

        protected virtual void RecordTargets(float time)
        {
            //var pos_x = CurveBinding.Position_x;
            if (RecordPosition)
            {
                var length = BindingTransforms.Count;

                for (int i = 0; i < length; i++)
                {
                    var tf = BindingTransforms[i];

                    if (!dic_bindings.TryGetValue(tf, out var binding))
                        continue;

                    if (RecordPosition)
                    {
                        var pos = tf.localPosition;

                        try
                        {
                            {
                                binding.propertyName = CurveBinding.Position_x;
                                var curve = AnimationUtility.GetEditorCurve(Clip, binding);
                                curve = curve.ApplyKeyFrame(time, pos.x);
                                AnimationUtility.SetEditorCurve(Clip, binding, curve);
                            }

                            {
                                binding.propertyName = CurveBinding.Position_y;
                                var curve = AnimationUtility.GetEditorCurve(Clip, binding);
                                curve = curve.ApplyKeyFrame(time, pos.y);
                                AnimationUtility.SetEditorCurve(Clip, binding, curve);
                            }

                            {
                                binding.propertyName = CurveBinding.Position_z;
                                var curve = AnimationUtility.GetEditorCurve(Clip, binding);
                                curve = curve.ApplyKeyFrame(time, pos.z);
                                AnimationUtility.SetEditorCurve(Clip, binding, curve);
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogError($"[{Clip}] [{binding.propertyName}] [{binding.path}] [{binding.type}]");
                            Debug.LogException(e);
                        }

                    }

                    if (RecordRotation)
                    {
                        var rot = tf.localRotation;

                        {
                            binding.propertyName = CurveBinding.Rotation_w;
                            var curve = AnimationUtility.GetEditorCurve(Clip, binding);
                            curve = curve.ApplyKeyFrame(time, rot.z);
                            AnimationUtility.SetEditorCurve(Clip, binding, curve);
                        }

                        {
                            binding.propertyName = CurveBinding.Rotation_x;
                            var curve = AnimationUtility.GetEditorCurve(Clip, binding);
                            curve = curve.ApplyKeyFrame(time, rot.x);
                            AnimationUtility.SetEditorCurve(Clip, binding, curve);
                        }

                        {
                            binding.propertyName = CurveBinding.Rotation_y;
                            var curve = AnimationUtility.GetEditorCurve(Clip, binding);
                            curve = curve.ApplyKeyFrame(time, rot.y);
                            AnimationUtility.SetEditorCurve(Clip, binding, curve);
                        }

                        {
                            binding.propertyName = CurveBinding.Rotation_z;
                            var curve = AnimationUtility.GetEditorCurve(Clip, binding);
                            curve = curve.ApplyKeyFrame(time, rot.z);
                            AnimationUtility.SetEditorCurve(Clip, binding, curve);
                        }
                    }

                    if (RecordScale)
                    {
                        var scale = tf.localScale;

                        {
                            binding.propertyName = CurveBinding.Scale_x;
                            var curve = AnimationUtility.GetEditorCurve(Clip, binding);
                            curve = curve.ApplyKeyFrame(time, scale.x);
                            AnimationUtility.SetEditorCurve(Clip, binding, curve);
                        }

                        {
                            binding.propertyName = CurveBinding.Scale_y;
                            var curve = AnimationUtility.GetEditorCurve(Clip, binding);
                            curve = curve.ApplyKeyFrame(time, scale.y);
                            AnimationUtility.SetEditorCurve(Clip, binding, curve);
                        }

                        {
                            binding.propertyName = CurveBinding.Scale_z;
                            var curve = AnimationUtility.GetEditorCurve(Clip, binding);
                            curve = curve.ApplyKeyFrame(time, scale.z);
                            AnimationUtility.SetEditorCurve(Clip, binding, curve);
                        }
                    }
                }
            }
        }
    }
}
