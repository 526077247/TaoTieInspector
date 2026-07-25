using System;
using UnityEngine;
using UnityEditor;

namespace TaoTie.Inspector.Editor
{
    /// <summary>
    /// A generic EditorWindow base class that provides TaoTie's enhanced inspector drawing.
    /// This is the TaoTie equivalent of Odin's OdinEditorWindow.
    /// 
    /// Inherit from this class to auto-draw all fields and buttons of the window itself:
    /// <code>
    /// public class MyConfigWindow : TaoTieEditorWindow
    /// {
    ///     [MenuItem("Tools/My Config")]
    ///     static void Open() => GetWindow&lt;MyConfigWindow&gt;().Show();
    /// 
    ///     public Object target;
    ///     [Button("Start")]
    ///     public void Run() { /* ... */ }
    /// }
    /// </code>
    /// 
    /// Override OnGUI() for custom layout — call DrawInspector() to draw the TaoTie inspector.
    /// Override GetWindowTitle() to set the window title.
    /// </summary>
    public abstract class TaoTieEditorWindow : UnityEditor.EditorWindow
    {
        private UnityEditor.Editor cachedEditor;
        private Vector2 scrollPosition;

        /// <summary>
        /// Optional: override to provide a custom title for the window.
        /// </summary>
        protected virtual string GetWindowTitle() => GetType().Name;

        protected virtual void OnEnable()
        {
            titleContent = new GUIContent(GetWindowTitle());
            minSize = new Vector2(300, 400);
            CreateCachedEditor();
        }

        protected virtual void OnDisable()
        {
            if (cachedEditor != null)
                DestroyImmediate(cachedEditor);
        }

        protected virtual void OnDestroy()
        {
        }

        private void CreateCachedEditor()
        {
            if (cachedEditor != null)
                DestroyImmediate(cachedEditor);
            UnityEditor.Editor.CreateCachedEditor(this, typeof(TaoTieEditor), ref cachedEditor);
        }

        protected virtual void OnGUI()
        {
            // Set adaptive label width based on window width
            float ratioW = position.width * 0.4f;
            float oldLabelW = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Max(80f, ratioW);

            DrawInspector();

            EditorGUIUtility.labelWidth = oldLabelW;
        }

        /// <summary>
        /// Draw the TaoTie inspector for this window's fields and buttons.
        /// Call this from your OnGUI override if you need custom layout.
        /// </summary>
        protected void DrawInspector()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            if (cachedEditor != null)
            {
                EditorGUILayout.Space(2);
                cachedEditor.OnInspectorGUI();
            }
            else
            {
                GUILayout.FlexibleSpace();
                var centeredStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Italic
                };
                EditorGUILayout.LabelField("Inspector not initialized", centeredStyle,
                    GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                GUILayout.FlexibleSpace();
            }

            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// Force the inspector to rebuild (e.g. after adding new serializable fields).
        /// </summary>
        protected void RebuildInspector() => CreateCachedEditor();

        /// <summary>
        /// Repaint the window.
        /// </summary>
        protected void RepaintWindow() => Repaint();
    }
}
