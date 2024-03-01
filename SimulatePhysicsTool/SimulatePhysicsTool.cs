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
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;

namespace Return.Editors
{
    /// <summary>
    /// 
    /// </summary>
    public class SimulatePhysicsTool : OdinEditorWindow
    {
        [MenuItem(MenuUtil.Tool+"Physics/Scene Simulator")]
        static void OpenWindow()
        {
            var window = GetWindow<SimulatePhysicsTool>();
            window.position = GUIHelper.GetEditorWindowRect().AlignCenter(400, 500);
            window.titleContent = new GUIContent("Physics Simulator", SdfIcons.CreateTransparentIconTexture(SdfIconType.Gear, Color.white, 15, 15, 5));           
        }
        const string cst_simulate= "Tabs/Simulate/Basic";
        const string cst_simulate_explosion = "Explosion";


        #region Routine


        protected override void OnEnable()
        {
            base.OnEnable();

            if (DropZone == null)
            {
                DropZone = new();
            }

            DropZone.customText = "Drop Simulate Targets";
            DropZone.allowSceneObj = true;
            DropZone.allowType = typeof(GameObject);
            DropZone.autoClear = false;
            DropZone.onTargetAdded = (x) => MoveObjsToSimulateScene(x as GameObject);

        }

        protected virtual void Update()
        {
            // editor coroutine
            try
            {
                if (!isPause && Simulator != null)
                    if (Simulator.MoveNext())
                        Repaint();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        protected override void OnImGUI()
        {
            if (Initialization)
                base.OnImGUI();
            else
            {
                //DropZone.Draw("Drop Simulate Targets",40,30,Color.green,(x)=> MoveObjsToSimulateScene(x.SelectWhere<GameObject>().ToArray()));
                DropZone.Draw(50, 40, Color.green);

                if (GUILayout.Button("Simulate Custom Targets"))
                {
                    // craete simulate scene

                    // simulate only works in runtime hot loading scene

                    if (SimulateScene == null || !SimulateScene.IsValid())
                    {
                        if (EditorApplication.isPlaying)
                            SimulateScene = SceneManager.CreateScene("Temp Physics Simulate Scene");
                        else
                        {
                            SimulateScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                            //SimulateScene = EditorSceneManager.NewPreviewScene();
                            
                            //var param = new LoadSceneParameters(LoadSceneMode.Additive, LocalPhysicsMode.Physics3D);
                            //SimulateScene = EditorSceneManager.LoadScene(,param);
                        }
                    }

                    MoveObjsToSimulateScene();
                    Initialization = true;
                }

                if (GUILayout.Button("Simulate Current Scene"))
                {
                    if (EditorUtility.DisplayDialog("Simulate Option", "Physics simulate will affect entire scene, click Yes to contiune", "Yes", "No"))
                    {
                        Initialization = true;
                    }
                }
            }
        }


        #endregion


        #region Setup

        [TabGroup("Tabs","Setup")]
        [SerializeField, HideInInspector]
        Scene SimulateScene;

        [TabGroup("Tabs", "Setup")]
        [SerializeField]
        protected DropZone DropZone;

        [TabGroup("Tabs", "Setup")]
        [SerializeField]
        LayerMask m_interactLayerMask = -1;
        public LayerMask InteractLayerMask { get => m_interactLayerMask; set => m_interactLayerMask = value; }


        /// <summary>
        /// Physics scene operation.
        /// </summary>
        public bool Initialization { get; protected set; } = false;

        protected virtual void MoveObjsToSimulateScene()
        {
            if (SimulateScene.IsValid())
            {
                foreach (var item in DropZone.contents)
                {
                    if (item is GameObject go)
                    {
                        if (go.scene.Equals(SimulateScene))
                            continue;

                        if (EditorApplication.isPlaying)
                            SceneManager.MoveGameObjectToScene(go, SimulateScene);
                        else
                            EditorSceneManager.MoveGameObjectToScene(go, SimulateScene);
                    }
                }
            }
        }

        public virtual void MoveObjsToSimulateScene(params GameObject[] objs)
        {
            if (SimulateScene.IsValid())
            {
                foreach (var obj in objs)
                {
                    if (obj!=null)
                    {
                        if (obj.scene.Equals(SimulateScene))
                            continue;

                        SceneManager.MoveGameObjectToScene(obj, SimulateScene);
                    }
                }
            }
        }

        [TabGroup("Tabs", "Setup")]
        [Button]
        void LogScene()
        {
            Debug.Log(EditorSceneManager.sceneCount);
            var length = EditorSceneManager.sceneCount;
            for (int i = 0; i < length; i++)
            {
                var scene =EditorSceneManager.GetSceneAt(i);
                Debug.Log($"[{scene.name}] [{scene.GetPhysicsScene().GetHashCode()}]");
            }
        }

        #endregion



        #region Simulate

        [TabGroup("Tabs", "Simulate")]
        [BoxGroup(cst_simulate)]
        [ProgressBar(0d, nameof(SimulateDuration))]
        public double Progress;

        [BoxGroup(cst_simulate)]
        [SerializeField]
        float Duration = 2f;

        [BoxGroup(cst_simulate)]
        [SerializeField]
        float fixedDeltaTime = 0.02f;

        [BoxGroup(cst_simulate)]
        [SerializeField]
        float m_playbackSpeed = 1f;
        [BoxGroup(cst_simulate)]
        [SerializeField]
        bool m_asleep;


        public bool isPause { get; set; } = false;

        public float SimulateDuration { get; set; }


        bool simulating => Simulator != null;
        protected double lastSampleTime;


        public bool Asleep { get => m_asleep; set => m_asleep = value; }
        public float PlaybackSpeed { get => m_playbackSpeed; set => m_playbackSpeed = value; }

        IEnumerator Simulator;

        [BoxGroup(cst_simulate)]
        [Button]
        public virtual void StepPhysics()
        {
            CacheStatus(false);


            var delta = fixedDeltaTime * PlaybackSpeed;

            if (Record)
            {
                if (RecordingWindow!=null || EditorWindowUtility.TryGetWindow(out RecordingWindow))
                {
                    if (RecordingWindow.canRecord)
                    {
                        RecordingWindow.previewing = false;
                        RecordingWindow.recording = true;
                        
                        RecordingWindow.time += delta;
                        RecordingWindow.previewing = false;
                    }
                }
            }

            Progress += delta;

            Physics.simulationMode = SimulationMode.Script;
            Physics.Simulate(delta);
            Physics.simulationMode = SimulationMode.FixedUpdate;

            StepEvent.Invoke(delta);
            TickEvent.Invoke((float)Progress);
        }


        [DisableIf(nameof(simulating))]
        [BoxGroup(cst_simulate)]
        [Button]
        void Simulate()
        {
            OnStartSimualte?.Invoke();
            CacheStatus(true);

            SimulateDuration = Duration;
            Simulator = Simulate(Duration, fixedDeltaTime);

            if (Record)
            {
                TickEvent.Invoke(0);


                if (EditorWindowUtility.TryGetWindow(out RecordingWindow))
                {
                    RecordingWindow.recording = true;
                    RecordingWindow.previewing = false;
                }
            }

            //if (Asleep && go != null)
            //{
            //    var rigis = go.GetComponentsInChildren<Rigidbody>();
            //    foreach (var rig in rigis)
            //    {
            //        rig.Sleep();
            //    }
            //}
        }

        void EndSimulate()
        {
            Progress = 0;
            Simulator = null;
            isPause = false;

            Physics.simulationMode = SimulationMode.FixedUpdate;
            OnStopSimualte?.Invoke();

            if (Record && RecordingWindow)
            {
                RecordingWindow.recording = false;
            }

            if (Record)
            {
                TickEvent.Invoke((float)Duration);
            }
        }

        IEnumerator Simulate(float duration, float fixedDelta)
        {
            isPause = false;
            Physics.simulationMode = SimulationMode.Script;

            lastSampleTime = EditorApplication.timeSinceStartup;
            var endTime = lastSampleTime + (duration / PlaybackSpeed);

            var scaledFixedDelta = fixedDelta * PlaybackSpeed;
            var unEvaluateTime = 0d;
            Progress = 0;

            Debug.Log($"Simulate [{endTime - lastSampleTime}]");

            yield return null;

            do
            {
                var newTime = EditorApplication.timeSinceStartup;
                var gap = (newTime - lastSampleTime);
                unEvaluateTime += gap * PlaybackSpeed;
                lastSampleTime = newTime;

                // make step over current time
                while (unEvaluateTime > scaledFixedDelta)
                {
                    Progress += scaledFixedDelta;

                    if (Record)
                    {
                        if (RecordingWindow)
                        {
                            RecordingWindow.previewing = false;
                            RecordingWindow.time += scaledFixedDelta;
                            RecordingWindow.previewing = false;
                        }
                    }

                    if (SimulateScene.IsValid() && SimulateScene.isLoaded)
                    {
                        SimulateScene.GetPhysicsScene().Simulate(scaledFixedDelta);
                    }
                    else
                    {
                        Physics.Simulate(scaledFixedDelta);
                    }

                    StepEvent.Invoke(scaledFixedDelta);

                    if (Record)
                    {
                        TickEvent.Invoke((float)Progress);
                    }

                    //Debug.Log($"{nextTick}/{endTime}");
                    unEvaluateTime -= scaledFixedDelta;
                }

                yield return null;
            }
            while (Progress < duration);

            if (Progress >= duration)
            {
                Debug.Log($"End Simulation [{EditorApplication.timeSinceStartup - endTime}].");
                EndSimulate();
            }
        }


        [EnableIf(nameof(simulating))]
        [BoxGroup(cst_simulate)]
        [Button]
        void Pause()
        {
            isPause = !isPause;
            lastSampleTime = EditorApplication.timeSinceStartup;
        }

        [EnableIf(nameof(simulating))]
        [BoxGroup(cst_simulate)]
        [Button]
        void Stop()
        {
            EndSimulate();
        }


        #endregion


        #region Undo

        /// <summary>
        /// Cache undo id.
        /// </summary>
        protected int UndoGroup;

        /// <summary>
        /// Register undo targets.
        /// </summary>
        public virtual void CacheStatus(bool useGroup)
        {
            const string cst_id = "PhysicSimulation";

            if (useGroup)
            {
                //set group
                Undo.SetCurrentGroupName(cst_id);
                //get current group
                UndoGroup = Undo.GetCurrentGroup();
            }

            foreach (var item in DropZone.contents)
            {
                if (item is GameObject go)
                {
                    foreach (var tf in go.transform.Traverse())
                    {
                        Undo.RegisterCompleteObjectUndo(tf, cst_id);
                    }
                }
            }
        }

        [DisableIf(nameof(simulating))]
        [BoxGroup("Undo",ShowLabel =false)]
        [Button("Undo")]
        public virtual void Revert()
        {
            if (UndoGroup != 0)
            {
                Undo.CollapseUndoOperations(UndoGroup);
                Undo.PerformUndo();
                UndoGroup = 0;
            }
        }

        #endregion


        #region Output

        public event Action OnStartSimualte;
        public event Action OnStopSimualte;

        [TabGroup("Tabs", "Record")]
        [SerializeField]
        bool m_record = true;
        public bool Record { get => m_record; set => m_record = value; }

        [TabGroup("Tabs", "Record")]
        [SerializeField]
        StepEvent StepEvent;

        [TabGroup("Tabs", "Record")]
        [SerializeField]
        StepEvent TickEvent;

        [TabGroup("Tabs", "Record")]
        void BindTransformRecorder()
        {
            if (EditorWindowUtility.TryGetWindow(out TransformRecorderEditor transformRecorder))
            {
                UnityEditor.Events.UnityEventTools.AddPersistentListener(TickEvent, new UnityAction<float>(transformRecorder.InsertKeyFrame));
            }
        }

        protected AnimationWindow RecordingWindow;
      

        #endregion

        #region Force

        [TabGroup("Tabs","Explosion")]
        [SerializeField]
        [TabGroup("Tabs", "Explosion")]
        Transform Origin;
        [TabGroup("Tabs", "Explosion")]
        [SerializeField]
        float Radius = 10;
        [TabGroup("Tabs", "Explosion")]
        [SerializeField]
        float Force = 10;

        [TabGroup("Tabs", "Explosion")]
        [Button]
        void Explosion()
        {
            CacheStatus(true);
            SimulateDuration = Duration;
            Simulator = Simulate(Duration, fixedDeltaTime);

            var collides = Physics.OverlapSphere(Origin.position, Radius, InteractLayerMask);

            var rbs = collides.SelectMany(x => x.GetComponentsInParent<Rigidbody>());
            foreach (var rb in rbs)
            {
                rb.ResetInertiaTensor();
                rb.AddExplosionForce(Force, Origin.position, Radius);
            }
        }

        #endregion


    }
}
