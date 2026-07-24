using System;
using UnityEngine;
using UnityEditor;

namespace TaoTie.Inspector.Editor
{
    /// <summary>
    /// A generic EditorWindow base class that provides TaoTie's enhanced inspector drawing.
    /// This is the TaoTie equivalent of Odin's OdinEditorWindow.
    /// 
    /// Inherit from this class to create a window that auto-draws all fields of the target:
    /// <code>
    /// public class MyConfigWindow : TaoTieEditorWindow
    /// {
    ///     [MenuItem("Tools/My Config")]
    ///     static void Open() => GetWindow&lt;MyConfigWindow&gt;().Show();
    /// }
    /// </code>
    /// 
    /// For UnityEngine.Object targets (ScriptableObject, MonoBehaviour), the window uses
    /// SerializedProperty-based drawing via TaoTieEditor.
    /// For plain C# objects, it uses reflection-based drawing via TaoTiePropertyTree.
    /// 
    /// Override OnGUI() for custom layout — call DrawTargetInspector() to draw the inspector.
    /// Override GetWindowTitle() to set the window title.
    /// </summary>
    public abstract class TaoTieEditorWindow : UnityEditor.EditorWindow
    {
        private UnityEditor.Editor cachedEditor;
        private object plainTarget;
        private TaoTiePropertyTree propertyTree;
        private Vector2 scrollPosition;
        private bool targetDirty = true;

        /// <summary>
        /// The target object being inspected.
        /// Set via SetTarget() or override InitializeTarget() to provide a default target.
        /// </summary>
        public object Target { get; private set; }

        /// <summary>
        /// Override to provide a default target when the window opens.
        /// Default returns null (no target — shows empty state).
        /// </summary>
        protected virtual object InitializeTarget() => null;

        /// <summary>
        /// Optional: override to provide a custom title for the window.
        /// </summary>
        protected virtual string GetWindowTitle() => "TaoTie Editor Window";

        /// <summary>
        /// Set the target object to inspect.
        /// Pass a UnityEngine.Object for SerializedProperty-based drawing,
        /// or a plain C# object for reflection-based drawing.
        /// </summary>
        public void SetTarget(object target)
        {
            Target = target;
            targetDirty = true;
            Repaint();
        }

        /// <summary>
        /// Force a refresh of the target on next OnGUI.
        /// </summary>
        public void RefreshTarget() => targetDirty = true;

        protected virtual void OnEnable()
        {
            titleContent = new GUIContent(GetWindowTitle());
            minSize = new Vector2(300, 400);
            Target = InitializeTarget();
            targetDirty = true;
        }

        protected virtual void OnDisable()
        {
            if (cachedEditor != null)
                DestroyImmediate(cachedEditor);
        }

        protected virtual void OnDestroy()
        {
        }

        private void UpdateTarget()
        {
            if (Target == null)
            {
                if (cachedEditor != null || plainTarget != null)
                {
                    cachedEditor = null;
                    plainTarget = null;
                    propertyTree = null;
                }
                return;
            }

            // Check if target changed
            if (Target is UnityEngine.Object newUo)
            {
                if (newUo != (cachedEditor?.target))
                {
                    plainTarget = null;
                    propertyTree = null;

                    if (cachedEditor != null)
                    {
                        DestroyImmediate(cachedEditor);
                        cachedEditor = null;
                    }
                    UnityEditor.Editor.CreateCachedEditor(newUo, typeof(TaoTieEditor), ref cachedEditor);
                    titleContent = new GUIContent(GetWindowTitle(), AssetPreview.GetMiniThumbnail(newUo));
                }
            }
            else
            {
                if (Target != plainTarget)
                {
                    cachedEditor = null;
                    plainTarget = Target;
                    propertyTree = TaoTiePropertyTree.Create(Target);
                    titleContent = new GUIContent(GetWindowTitle());
                }
            }
        }

        protected virtual void OnGUI()
        {
            if (targetDirty)
            {
                UpdateTarget();
                targetDirty = false;
            }

            DrawTargetInspector();
        }

        /// <summary>
        /// Draw the TaoTie inspector for the current target.
        /// Call this from your OnGUI override if you need custom layout.
        /// </summary>
        protected void DrawTargetInspector()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            if (cachedEditor != null)
            {
                EditorGUILayout.Space(2);
                cachedEditor.OnInspectorGUI();
            }
            else if (propertyTree != null && plainTarget != null)
            {
                EditorGUILayout.Space(2);
                propertyTree.Draw();
            }
            else
            {
                GUILayout.FlexibleSpace();
                var centeredStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Italic
                };
                EditorGUILayout.LabelField("No target to inspect", centeredStyle,
                    GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                GUILayout.FlexibleSpace();
            }

            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// Repaint the window.
        /// </summary>
        protected void RepaintWindow() => Repaint();
    }
}
