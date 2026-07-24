using UnityEngine;
using UnityEditor;

namespace TaoTie.Inspector.Editor
{
    /// <summary>
    /// A persistent inspector window that uses TaoTie's drawing system for any target.
    /// Supports UnityEngine.Object, plain C# objects, drag-and-drop, and selection following.
    /// </summary>
    public class TaoTieDrawerWindow : UnityEditor.EditorWindow
    {
        private UnityEngine.Object unityTarget;
        private UnityEditor.Editor cachedEditor;
        private object plainTarget;
        private TaoTiePropertyTree propertyTree;
        private Vector2 scrollPosition;

        [MenuItem("Window/TaoTie Inspector/Drawer")]
        public static TaoTieDrawerWindow Open()
        {
            var window = GetWindow<TaoTieDrawerWindow>("TaoTie Drawer");
            window.minSize = new Vector2(300, 400);
            return window;
        }

        public static TaoTieDrawerWindow Open(object target)
        {
            var window = Open();
            window.SetTarget(target);
            return window;
        }

        public void SetTarget(object obj)
        {
            if (obj is UnityEngine.Object uo)
            {
                unityTarget = uo;
                plainTarget = null;
                propertyTree = null;

                if (cachedEditor != null)
                {
                    DestroyImmediate(cachedEditor);
                    cachedEditor = null;
                }

                UnityEditor.Editor.CreateCachedEditor(uo, typeof(TaoTieEditor), ref cachedEditor);
                titleContent = new GUIContent(uo.name, AssetPreview.GetMiniThumbnail(uo));
            }
            else if (obj != null)
            {
                unityTarget = null;
                cachedEditor = null;
                plainTarget = obj;
                propertyTree = TaoTiePropertyTree.Create(obj);
                titleContent = new GUIContent(obj.GetType().Name);
            }
            else
            {
                ClearTarget();
            }

            Repaint();
        }

        public void ClearTarget()
        {
            unityTarget = null;
            plainTarget = null;
            propertyTree = null;

            if (cachedEditor != null)
            {
                DestroyImmediate(cachedEditor);
                cachedEditor = null;
            }

            titleContent = new GUIContent("TaoTie Drawer");
            Repaint();
        }

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            if (cachedEditor != null && unityTarget != null)
            {
                DrawObjectHeader(unityTarget);
                EditorGUILayout.Space(4);
                cachedEditor.OnInspectorGUI();
            }
            else if (propertyTree != null && plainTarget != null)
            {
                DrawPlainObjectHeader(plainTarget);
                EditorGUILayout.Space(4);
                propertyTree.Draw();
            }
            else
            {
                DrawDropArea();
            }

            EditorGUILayout.EndScrollView();
            DrawFooter();
        }

        private void DrawObjectHeader(UnityEngine.Object obj)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            Texture icon = AssetPreview.GetMiniThumbnail(obj);
            if (icon != null)
                GUILayout.Label(icon, GUILayout.Width(20), GUILayout.Height(20));

            EditorGUILayout.LabelField(obj.name, EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Ping", EditorStyles.toolbarButton))
                EditorGUIUtility.PingObject(obj);

            EditorGUILayout.EndHorizontal();
        }

        private void DrawPlainObjectHeader(object obj)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUILayout.LabelField(obj.GetType().FullName, EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawDropArea()
        {
            GUILayout.FlexibleSpace();
            var dropRect = GUILayoutUtility.GetRect(0, 100, GUILayout.ExpandWidth(true));
            GUI.Box(dropRect, "Drag any object here to inspect");

            var centeredStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Italic
            };
            GUI.Label(dropRect, "Drag any UnityEngine.Object or\nplain C# object here", centeredStyle);
            GUILayout.FlexibleSpace();

            if (dropRect.Contains(Event.current.mousePosition))
            {
                if (Event.current.type == EventType.DragUpdated)
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    Event.current.Use();
                }
                else if (Event.current.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    if (DragAndDrop.objectReferences.Length > 0)
                    {
                        SetTarget(DragAndDrop.objectReferences[0]);
                    }
                    Event.current.Use();
                }
            }
        }

        private void DrawFooter()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Clear", EditorStyles.toolbarButton))
                ClearTarget();
            EditorGUILayout.EndHorizontal();
        }

        private void OnSelectionChange()
        {
            if (unityTarget != null || plainTarget != null) return;
            if (Selection.activeObject != null)
            {
                SetTarget(Selection.activeObject);
                Repaint();
            }
        }

        private void OnDestroy()
        {
            if (cachedEditor != null)
                DestroyImmediate(cachedEditor);
        }
    }
}
