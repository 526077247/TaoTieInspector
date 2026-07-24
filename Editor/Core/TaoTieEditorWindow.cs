using System;
using UnityEngine;
using UnityEditor;

namespace TaoTie.Inspector.Editor
{
    /// <summary>
    /// A generic EditorWindow base class that provides TaoTie's enhanced inspector drawing
    /// for a target object. This is the TaoTie equivalent of Odin's OdinEditorWindow.
    /// 
    /// Inherit from this class and override GetTarget() to return the object you want to inspect:
    /// <code>
    /// public class MyConfigWindow : TaoTieEditorWindow
    /// {
    ///     [MenuItem("Tools/My Config")]
    ///     static void Open() => GetWindow&lt;MyConfigWindow&gt;().Show();
    /// 
    ///     protected override object GetTarget() => myConfigInstance;
    /// }
    /// </code>
    /// 
    /// You can also override OnGUI() for custom layout — call DrawTargetInspector() to draw the
    /// TaoTie inspector for the current target.
    /// </summary>
    public abstract class TaoTieEditorWindow : UnityEditor.EditorWindow
    {
        private UnityEngine.Object unityTarget;
        private UnityEditor.Editor cachedEditor;
        private object plainTarget;
        private TaoTiePropertyTree propertyTree;
        private Vector2 scrollPosition;
        private bool targetDirty = true;

        /// <summary>
        /// Override to provide the target object to inspect.
        /// Return a UnityEngine.Object to use SerializedProperty-based drawing,
        /// or a plain C# object to use reflection-based drawing.
        /// </summary>
        protected abstract object GetTarget();

        /// <summary>
        /// Optional: override to provide a custom title for the window.
        /// Default uses the target type name.
        /// </summary>
        protected virtual string GetWindowTitle() => "TaoTie Editor Window";

        /// <summary>
        /// Whether the window should auto-refresh the target each frame.
        /// Override and return false if the target is static.
        /// </summary>
        protected virtual bool AutoRefreshTarget => true;

        protected virtual void OnEnable()
        {
            titleContent = new GUIContent(GetWindowTitle());
            minSize = new Vector2(300, 400);
        }

        protected virtual void OnDisable()
        {
            if (cachedEditor != null)
                DestroyImmediate(cachedEditor);
        }
        protected virtual void OnDestroy()
        {
        }
        /// <summary>
        /// Force a refresh of the target on next OnGUI.
        /// </summary>
        public void RefreshTarget() => targetDirty = true;

        private void UpdateTarget()
        {
            object newTarget = GetTarget();

            if (newTarget == null)
            {
                if (unityTarget != null || plainTarget != null)
                {
                    unityTarget = null;
                    plainTarget = null;
                    propertyTree = null;
                    if (cachedEditor != null)
                    {
                        DestroyImmediate(cachedEditor);
                        cachedEditor = null;
                    }
                }
                return;
            }

            // Check if target changed
            if (newTarget is UnityEngine.Object newUo)
            {
                if (newUo != unityTarget)
                {
                    unityTarget = newUo;
                    plainTarget = null;
                    propertyTree = null;

                    if (cachedEditor != null)
                    {
                        DestroyImmediate(cachedEditor);
                        cachedEditor = null;
                    }
                    UnityEditor.Editor.CreateCachedEditor(unityTarget, typeof(TaoTieEditor), ref cachedEditor);
                    titleContent = new GUIContent(GetWindowTitle(), AssetPreview.GetMiniThumbnail(unityTarget));
                }
            }
            else
            {
                if (newTarget != plainTarget)
                {
                    unityTarget = null;
                    cachedEditor = null;
                    plainTarget = newTarget;
                    propertyTree = TaoTiePropertyTree.Create(newTarget);
                    titleContent = new GUIContent(GetWindowTitle());
                }
            }
        }

        protected virtual void OnGUI()
        {
            if (AutoRefreshTarget || targetDirty)
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

            if (cachedEditor != null && unityTarget != null)
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
        /// Repaint the window. Safe to call from any thread context.
        /// </summary>
        protected void RepaintWindow() => Repaint();
    }

    /// <summary>
    /// Generic version of TaoTieEditorWindow that takes a specific target type.
    /// Provides type-safe access to the target.
    /// </summary>
    public abstract class TaoTieEditorWindow<T> : TaoTieEditorWindow where T : class
    {
        private T cachedTarget;

        /// <summary> The current target object. </summary>
        protected T Target
        {
            get
            {
                if (cachedTarget == null || AutoRefreshTarget)
                    cachedTarget = GetTarget() as T;
                return cachedTarget;
            }
        }

        /// <summary> Override to provide the typed target. </summary>
        protected abstract T GetTypedTarget();

        protected override object GetTarget() => GetTypedTarget();
    }
}
