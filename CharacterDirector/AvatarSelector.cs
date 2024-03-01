using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Random = UnityEngine.Random;

namespace Return.Editors
{
    /// <summary>
    /// Avatar body mask drawer.
    /// </summary>
    [Serializable]
    public class AvatarSelector : SerializedScriptableObject
    {
        public const string Panel_Preview = "Vertical";
        public const string Panel_Avatar = "Vertical/Avatar";
        public const string Panel_Button = "Vertical/Avatar/Button";

        protected virtual void OnEnable()
        {
            if (AvatarTexture == null)
                AvatarTexture = EditorGUIUtility.FindTexture("AvatarInspector/BodyPartPicker");

        }

        AvatarMaskBodyPart m_partDof = AvatarMaskBodyPart.Body;

        public event Action<AvatarMaskBodyPart,int> onSelectedChanged;

        /// <summary>
        /// Selected body part.
        /// </summary>
        [HideInInspector]
        [HideLabel]
        [ReadOnly]
        public AvatarMaskBodyPart PartDof 
        { 
            get => m_partDof; 
            set
            {
                index_selectedBodyPart = (int)value-1;
                m_partDof = value;
            } 
        } 


        [HideLabel]
        [PropertyOrder(-4)]
        [HorizontalGroup(Panel_Preview)]
        [OnInspectorGUI]
        void DrawPreview()
        {
            rect_currentGUI = GUILayoutUtility.GetAspectRect((float)AvatarTexture.height / AvatarTexture.width, GUILayout.Width(1000f));
            DrawPreviewLayout(rect_currentGUI);
        }

        public Vector2 GetRectSize(float width)
        {
            // calculate texture aspect ratio
            var ratio = (float)AvatarTexture.height / AvatarTexture.width;
            var targetWidth = Mathf.Max(150, width);
            var targetHeight = targetWidth * ratio;

            return new(targetWidth, targetHeight);
        }

        /// <summary>
        /// Draw avatar selection GUI.
        /// </summary>
        public void DrawPreviewLayout(Rect rect)
        {
            if (!AvatarTexture)
            {
                EditorGUILayout.HelpBox(string.Format("Missing texure resource : {0}", AvatarTexture), MessageType.Error);
                return;
            }

            if (style_background == null)
            {
                style_background = GUI.skin.FindStyle("flow node 0");
            }

            //if(Events.IsType(EventType.Layout))
            {
                var width = rect.width;
                var size = GetRectSize(width);
                rect_currentGUI = GUILayoutUtility.GetAspectRect(size.x / size.y, GUILayout.MaxWidth(size.x), GUILayout.MaxHeight(size.y));
            }

            Rect curRect = rect_currentGUI;

            // draw background
            {
                GUI.Label(curRect,GUIContent.none, style_background);
                GUI.DrawTexture(curRect, AvatarTexture, ScaleMode.ScaleToFit);
            }

            bool useEgg = false;

            // cache current GUI position
            var pos = curRect.position;

            // calculate resize ratio of GUI 
            var ratio_resizeGUI = curRect.width / 100f;

            // draw bodyPart options GUI
            {
                if (style_bodypart == null)
                {
                    style_bodypart = new GUIStyle() { alignment = TextAnchor.MiddleCenter };
                    style_bodypart.normal.background = null;
                }

                var length = ButtonPositions.Count;

                for (int i = 0; i < length; i++)
                {
                    var rect_option = new Rect(ButtonPositions[i] * ratio_resizeGUI + pos, ButtonSize * ratio_resizeGUI);

                    if (GUI.Button(rect_option, i == index_selectedBodyPart ? PointTexture_Selected : PointTexture, style_bodypart))
                    {
                        PartDof = (AvatarMaskBodyPart)(i + 1);
                        SetIKSelection(IKGoal.LastIKGoal);
                        onSelectedChanged?.Invoke(PartDof,Event.current.button);
                        useEgg = true;
                        break;
                    }
                }
            }
            
            // process IK area selection
            if (Events.IsMouse())
            {
                var goals = Enum.GetValues(typeof(IKGoal));

                foreach (IKGoal goal in goals)
                {
                    if (goal == IKGoal.LastIKGoal)
                        continue;

                    if (WhetherInRect(GetIKGoalArea(goal), ratio_resizeGUI, pos))
                    {
                        SetIKSelection(goal);
                        useEgg = true;
                        break;
                    }
                }
            }

            // draw IK selection
            if(selected_IKGoal!=IKGoal.LastIKGoal)
            {
                EditorGUI.DrawRect(GetIKGUIArea(GetIKGoalArea(selected_IKGoal),ratio_resizeGUI,pos), Color.green);
            }


            //dev - open this asset
            {
                //EditorGUI.DrawRect(rect.SetPosition(Vector2.zero), Color.red);

                if (Events.IsMouse(button: 1))
                {
                    useEgg = true;
                    if (Events.PointerInRect(rect.SetPosition(Vector2.zero)))
                    {
                        this.OpenAsset();
                    }
                }
            }
           

            // egg
            {
                if (style_eye == null)
                {
                    style_eye = new GUIStyle(GUIStyle.none) { alignment = TextAnchor.MiddleCenter,fontSize = 25 };
                }

                style_eye.fontSize = (int)(5 * ratio_resizeGUI);

                //EditorGUI.DrawRect(GetIKGUIArea(rect_eye_left, ratio_resizeGUI, pos), Color.red);
                //EditorGUI.DrawRect(GetIKGUIArea(rect_eye_right, ratio_resizeGUI, pos), Color.red);

                // change skin
                if (useEgg && Random.value < 0.01f)
                {
                    // different skin
                    if(Random.value < 0.15f)
                    {
                        if (Random.value < 0.07)
                        {
                            Symbo_Left = Symbos_Left.Random();
                            Symbo_Right = Symbos_Right.Random();
                        }
                        else
                        {
                            Symbo_Left = Symbos.Random();
                            Symbo_Right = Symbos.Random();
                        }
                    }
                    else
                    {
                        var newSymbo = Symbos.Random();
                        Symbo_Left =newSymbo;
                        Symbo_Right = newSymbo;
                    }
                }

                EditorGUI.LabelField(GetIKGUIArea(rect_eye_left, ratio_resizeGUI, pos), Symbo_Left, style_eye);
                EditorGUI.LabelField(GetIKGUIArea(rect_eye_right, ratio_resizeGUI, pos), Symbo_Right, style_eye);
            }
        }

        #region IKGoal

        public event Action<IKGoal> onSelectedIKChanged;


        [SerializeField]
        IKGoal m_IKGoal = IKGoal.LastIKGoal;

        /// <summary>
        /// Selected IK goal.
        /// </summary>
        public IKGoal selected_IKGoal { get => m_IKGoal; set => m_IKGoal = value; }



        /// <summary>
        /// Set IK selection by IKGoal.
        /// </summary>
        void SetIKSelection(IKGoal goal)
        {
            if (goal == selected_IKGoal)
                selected_IKGoal = IKGoal.LastIKGoal;
            else
                selected_IKGoal = goal;

            if (goal == IKGoal.LastIKGoal)
                return;

            PartDof = AvatarMaskBodyPart.LastBodyPart;
            onSelectedChanged?.Invoke(PartDof,-1);

            onSelectedIKChanged?.Invoke(goal);
        }

        /// <summary>
        /// Get rect by IK Goal.
        /// </summary>
        Rect GetIKGoalArea(IKGoal goal)
        {
            return goal switch
            {
                IKGoal.Head => rect_IK_Head,

                IKGoal.RightHand => rect_IK_RightHand,
                IKGoal.LeftHand => rect_IK_LeftHand,

                IKGoal.RightFoot => rect_IK_RightFoot,
                IKGoal.LeftFoot => rect_IK_LeftFoot,

                _ => throw new NotImplementedException(goal.ToString()),
            };
        }

        /// <summary>
        /// Get resize rect.
        /// </summary>
        static Rect GetIKGUIArea(Rect rect,float resizeRatio,Vector2 offsetPosition)
        {
            return rect.SetPosition(rect.position * resizeRatio + offsetPosition).SetSize(rect.size * resizeRatio);
        }

        /// <summary>
        /// Whether pointer inside rect.
        /// </summary>
        static bool WhetherInRect(Rect rect,float resizeRatio,Vector2 offsetPosition)
        {
            rect = GetIKGUIArea(rect, resizeRatio, offsetPosition);
            return Events.PointerInRect(rect);
        }


        #endregion


        #region Editor GUI Options


        [VerticalGroup(Panel_Avatar)]
        [Tooltip("Character Texture")]
        public Texture2D AvatarTexture;

        /// <summary>
        /// GUI texture to draw body parts.
        /// </summary>
        [VerticalGroup(Panel_Button)]
        [Tooltip("Button Icon")]
        public Texture PointTexture;

        /// <summary>
        /// GUI texture to draw selected body part.
        /// </summary>
        [VerticalGroup(Panel_Button)]
        [Tooltip("High Light Button Icon")]
        public Texture PointTexture_Selected;

        /// <summary>
        /// Size to draw body part texture.
        /// </summary>
        [VerticalGroup(Panel_Button)]
        [Tooltip("Size of button when lines of avatar rect is 100")]
        public Vector2 ButtonSize = new (17, 17);

        [PropertyOrder(3)]
        [VerticalGroup(Panel_Button)]
        [ShowInInspector]
        public List<Vector2> ButtonPositions = new List<Vector2>
        {
            new Vector2(41.98f,75.9f),
            new Vector2(41.3f,20f),
            new Vector2(54.4f,144f),
            new Vector2(28.9f,124.9f),
            new Vector2(65.3f,42.8f),
            new Vector2(18.4f,46f),
            new Vector2(71.1f,90.6f),
            new Vector2(12.6f,90.7f),
        };

        [Obsolete]
        [PropertyOrder(2)]
        [VerticalGroup(Panel_Button)]
        [Tooltip("Copy button data as const string.")]
        [Button("Copy")]
        void CopyButtonPositions()
        {
            var sb = new StringBuilder();
            var ratio = rect_currentGUI.width / 200;
            foreach (var p in ButtonPositions)
            {
                sb.AppendLine(string.Format("new Vector2({0}f,{1}f),", p.x / ratio, p.y / ratio));
            }
            EditorGUIUtility.systemCopyBuffer = sb.ToString();
        }


        /// <summary>
        /// Cache painting GUI.
        /// </summary>
        public Rect rect_currentGUI = new(0, EditorGUIUtility.singleLineHeight, 150, 300);


        public Rect rect_IK_Head = new(15.5f, 4.25f, 17.7f, 8.2f);

        public Rect rect_IK_LeftHand = new(8.8f, 121.62f, 18.4f, 9.2f);
        public Rect rect_IK_RightHand = new(73.56f, 121.62f, 18.4f, 9.2f);

        public Rect rect_IK_LeftFoot = new(2.6f, 196.73f, 18.4f, 9.2f);
        public Rect rect_IK_RightFoot = new(78.7f, 196.73f, 18.4f, 9.2f);

        public Rect rect_eye_right = new(51,15,5,5);
        public Rect rect_eye_left = new(44, 15, 5, 5);


        public string Symbo_Left;
        public string Symbo_Right;

        public string[] Symbos = new string[] { "-", "O", "o", "x", "u", "Q", "T","@","$","z" };

        public string[] Symbos_Left = new string[] { "<" };
        public string[] Symbos_Right = new string[] { ">" };

        GUIStyle style_bodypart;
        GUIStyle style_eye;
        GUIStyle style_background;


        /// <summary>
        /// Selected body part.
        /// </summary>
        int index_selectedBodyPart = 0;

        #endregion



        /// <summary>
        /// Humanoid IK goals.
        /// </summary>
        public enum IKGoal
        {
            Head,
            LeftHand,
            RightHand,
            LeftFoot,
            RightFoot,


            LastIKGoal,
        }
    }
}