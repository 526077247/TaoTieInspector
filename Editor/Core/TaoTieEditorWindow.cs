using System;
using UnityEngine;
using UnityEditor;
using TaoTie.Inspector;

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
        private DrawBase drawBase;
        private Vector2 scrollPosition;

        /// <summary>
        /// Optional: override to provide a custom title for the window.
        /// </summary>
        protected virtual string GetWindowTitle() => GetType().Name;

        protected virtual void OnEnable()
        {
            titleContent = new GUIContent(GetWindowTitle());
            minSize = new Vector2(300, 400);
            drawBase = new DrawBase();
        }

        protected virtual void OnDisable()
        {
        }

        protected virtual void OnDestroy()
        {
        }

        protected virtual void OnGUI()
        {
            // EditorWindow receives MouseMove events on every pixel of mouse movement,
            // each triggering a full GUILayout pass through the entire inspector.
            // Inspector does NOT receive MouseMove — skip it to match Inspector performance.
            // Tooltips are handled during Repaint, not MouseMove.
            if (Event.current.type == EventType.MouseMove)
                return;

            DrawInspector();
        }

        /// <summary>
        /// Draw the TaoTie inspector for this window's fields and buttons.
        /// Call this from your OnGUI override if you need custom layout.
        /// </summary>
        protected void DrawInspector()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            EditorGUILayout.Space(2);

            // Use DrawBase (reflection path) — same as Graph, gives consistent
            // foldout alignment without the SerializedProperty overhead.
            DrawBase.SetAvailableWidth(position.width - 40f);
            // Plain EditorWindow (not a Graph): [DrawIgnore(Ignore.EditorWindow)] hides
            // the internal drawBase/scrollPosition while user fields stay visible.
            bool oldGraphCtx = DrawBase.s_IsGraphContext;
            bool oldEditorWindowCtx = DrawBase.s_IsEditorWindowContext;
            DrawBase.s_IsGraphContext = false;
            DrawBase.s_IsEditorWindowContext = true;
            drawBase.DrawObjectInspector(this, true);
            DrawBase.s_IsGraphContext = oldGraphCtx;
            DrawBase.s_IsEditorWindowContext = oldEditorWindowCtx;

            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// Force the inspector to rebuild (e.g. after adding new serializable fields).
        /// </summary>
        protected void RebuildInspector()
        {
            drawBase = new DrawBase();
            Repaint();
        }

        /// <summary>
        /// Repaint the window.
        /// </summary>
        protected void RepaintWindow() => Repaint();
    }
}
