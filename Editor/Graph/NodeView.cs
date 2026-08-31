using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace TaoTie.Inspector.Editor
{
    public class NodeView
    {
        #region Private Variables

        private NodeBase m_Node;
        private GraphWindow m_graphWindow;
        private int m_WindowId;
        private GraphBase m_Graph;
        private Vector2 m_Offset;
        private Rect m_graphArea;
        private Rect m_DrawRect;
        public bool isVisible = true;
        public bool zoomedBeyondPortDrawThreshold = false;
        public bool isSelected;
        public bool isInGroup;

        protected DrawBase drawBase;

        // Cached styles (procedural, no external GUISkin)
        private static GUIStyle s_NodeTitle;
        private static GUIStyle s_PortLabel;
        // Cached inspector height from last Repaint (used on Layout events for accurate sizing)
        private float m_CachedInspectorHeight;

        // Node resize drag state
        internal static int s_ResizingNodeId = -1;
        internal static bool s_IsResizingWidth;

        private static GUIStyle NodeTitleStyle => s_NodeTitle ??= new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 13,
            normal = { textColor = NodeColors.HeaderText },
            alignment = TextAnchor.MiddleLeft
        };

        private static GUIStyle PortLabelStyle => s_PortLabel ??= new GUIStyle(EditorStyles.miniLabel)
        {
            normal = { textColor = new Color(0.85f, 0.85f, 0.85f, 1f) },
            alignment = TextAnchor.MiddleLeft
        };

        #endregion

        #region Layout Constants

        private const float TopPadding = 6f;
        private const float HeaderHeight = 32f;
        private const float HeaderIconSize = 20f;
        private const float FooterHeight = 10f;
        private const float FooterGap = 2f;       // gap between body content and footer
        private const float BottomPadding = 8f;   // padding below footer
        private const float SidePadding = 6f;
        private const float InspectorSideMargin = 20f;
        private const float IndentWidth = 15f;    // pixels per indent level
        internal const float BaseWidth = 310f;      // matches NodeBase.InitNode default

        private static float headerIconPadding => (HeaderHeight - HeaderIconSize) / 2;

        #endregion

        #region Properties

        public int windowId => m_WindowId;
        public NodeBase node => m_Node;
        public GraphBase graph => m_Graph;
        public float x => m_Node.GetX();
        public float y => m_Node.GetY();
        public float width => m_Node.GetWidth();
        public float height => m_Node.GetHeight();
        public Vector2 position => m_Node.GetPosition();
        public Vector2 size => m_Node.GetSize();
        public Rect rect => m_Node.GetRect();
        public Rect drawRect => m_DrawRect;
        protected float dynamicHeight { get; set; }
        public GraphWindow graphWindow => m_graphWindow;

        #endregion

        #region Virtual Methods

        public virtual void Init(int windowId, NodeBase node, GraphBase graph, GraphWindow graphWindow)
        {
            m_WindowId = windowId;
            m_Node = node;
            m_Graph = graph;
            m_graphWindow = graphWindow;
            m_Node.onDeletePort += OnDeletePort;
            drawBase = CreateDrawBase();
        }

        /// <summary>Factory for the DrawBase used by this node view's inspector.
        /// Override to return a custom DrawBase subclass (e.g. Odin-compatible drawing).</summary>
        protected virtual DrawBase CreateDrawBase() => new DrawBase();

        public virtual void OnDoubleClick(EditorWindow window) { }
        public virtual void OnUnFocus(EditorWindow window) { GUI.FocusControl(null); }

        protected virtual void OnNodeGUI()
        {
            DrawNodeLayout();
        }

        protected virtual GUIStyle GetIconStyle() => null;

        protected virtual void DrawIcon(Rect iconRect)
        {
            NodeColors.DrawDot(iconRect.center, iconRect.width * 0.35f, NodeColors.HeaderIcon);
        }

        protected virtual Rect DrawPort(Port port)
        {
            port.SetX(0);
            port.SetY(dynamicHeight);
            port.SetWidth(node.GetWidth());
            port.SetHeight(24f);
            if (zoomedBeyondPortDrawThreshold) return port.GetRect();

            var portColor = Color.gray;
            var dividerColor = NodeColors.Divider;

            if (port.IsConnected())
            {
                portColor = port.IsInput() ? NodeColors.PortInput : NodeColors.PortOutput;
            }

            var opacity = 0.3f;
            portColor.a = opacity;
            dividerColor.a = opacity * 0.8f;

            if (m_graphWindow.altKeyPressed)
            {
                if (port.IsConnected())
                {
                    portColor = Color.red;
                    dividerColor = Color.red;
                }
                portColor.a = opacity * 1.2f;
                dividerColor.a = opacity * 1.2f;
            }

            // Top divider
            var topDividerRect = new Rect(port.GetRect().x + SidePadding, port.GetRect().y + 1f,
                port.GetRect().width - SidePadding * 2, 1);
            NodeColors.DrawRect(topDividerRect, dividerColor);

            // Bottom divider
            var bottomDividerRect = new Rect(port.GetRect().x + SidePadding,
                port.GetRect().y + port.GetRect().height - 1,
                port.GetRect().width - SidePadding * 2, 1);
            NodeColors.DrawRect(bottomDividerRect, dividerColor);

            // Port label
            var label = port.portName;
            var areaRect = new Rect(port.GetX() + 24, port.GetY(), port.GetWidth() - 48, port.GetHeight());
            GUILayout.BeginArea(areaRect);
            {
                GUILayout.BeginHorizontal();
                var content = new GUIContent(label);
                var contentSize = PortLabelStyle.CalcSize(content);
                GUILayout.BeginVertical(GUILayout.Width(contentSize.x), GUILayout.Height(port.GetHeight()));
                GUILayout.Space((port.GetHeight() - contentSize.y) / 2);
                GUILayout.Label(content, PortLabelStyle, GUILayout.Width(contentSize.x));
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
            }
            GUILayout.EndArea();

            // Port hover rect
            port.UpdateHoverRect();
            if (port.showHover.faded > 0.01f)
            {
                var animatedRect = new Rect(port.hoverRect.x,
                    port.hoverRect.y + port.hoverRect.height * (1 - port.showHover.faded),
                    port.hoverRect.width,
                    port.hoverRect.height * port.showHover.faded);
                NodeColors.DrawRect(animatedRect, portColor);
            }

            return port.GetRect();
        }

        #endregion

        #region Public Methods

        public void DrawNodeGUI(Rect graphArea, Vector2 panOffset, float zoomLevel)
        {
            m_Offset = panOffset;
            m_graphArea = graphArea;
            Vector2 windowToGridPosition = m_Node.GetPosition() + m_Offset / zoomLevel;
            // Use actual node height for the GUI.Window rect.
            // A previous 5000px fallback caused massive window overlap in z-order,
            // stealing mouse events from nodes drawn underneath.
            // If content grows (foldout expands), UpdateNodeHeight updates the
            // stored height and the next frame uses the correct size.
            var clientRect = new Rect(windowToGridPosition, new Vector2(width, Mathf.Max(height, 50)));
            GUI.Window(m_WindowId, clientRect, DrawNode, string.Empty, GUIStyle.none);

            // Add resize cursor in screen space (outside GUI.Window clip)
            if (graph != null && graph.currentZoom > 0.7f)
            {
                float actualHeight = Mathf.Max(height, m_CachedInspectorHeight > 0 ? m_CachedInspectorHeight : 200);
                var resizeScreenRect = new Rect(
                    clientRect.xMax - 6f * zoomLevel,
                    clientRect.y,
                    6f * zoomLevel,
                    actualHeight * zoomLevel);
                EditorGUIUtility.AddCursorRect(resizeScreenRect, MouseCursor.ResizeHorizontal);
            }
        }

        public virtual void DrawInspector(bool isDetails = false)
        {
            drawBase.DrawObjectInspector(node, isDetails);
        }

        #endregion

        #region Private Methods — Core Layout

        /// <summary>
        /// Main layout method. Draws in correct Z-order:
        /// 1. Body background (estimated from old height)
        /// 2. Header
        /// 3. Ports + Inspector content
        /// 4. Body background extension (fills gap if content is taller)
        /// 5. Footer (at correct position)
        /// 6. Outline (at correct position)
        /// </summary>
        private void DrawNodeLayout()
        {
            // --- Phase 0: Width — always base width, only grows via manual resize ---
            float neededWidth = BaseWidth;

            // Don't shrink below user-set width (only grow)
            if (m_Node.GetWidth() > BaseWidth)
                neededWidth = m_Node.GetWidth();

            // First-time init: set to base width
            if (m_Node.GetWidth() <= 0)
                neededWidth = BaseWidth;

            if (neededWidth != m_Node.GetWidth())
            {
                UpdateNodeWidth(neededWidth);
            }

            dynamicHeight = 0;
            m_DrawRect = new Rect(0, 0, m_Node.GetWidth(), height);

            float leftX = SidePadding;
            float bodyWidth = m_DrawRect.width - SidePadding * 2;

            // --- Phase 1: Draw Body background with estimated height ---
            float estimatedTotalHeight = Mathf.Max(height, HeaderHeight + TopPadding + FooterHeight + FooterGap + BottomPadding);
            float bodyStartY = TopPadding + HeaderHeight;

            float estimatedBodyHeight = estimatedTotalHeight - bodyStartY - FooterHeight - FooterGap - BottomPadding;
            if (estimatedBodyHeight > 0)
            {
                var bodyRect = new Rect(leftX, bodyStartY, bodyWidth, estimatedBodyHeight);
                NodeColors.DrawRect(bodyRect, NodeColors.Body);
            }

            // --- Phase 2: Header ---
            dynamicHeight = TopPadding;

            var headerRect = new Rect(leftX, dynamicHeight, bodyWidth, HeaderHeight);
            var headerIconRect = new Rect(headerRect.x + headerIconPadding * 2f,
                headerRect.y + headerIconPadding + 1,
                HeaderIconSize, HeaderIconSize);
            var headerTitleRect = new Rect(headerIconRect.xMax + headerIconPadding * 1.5f,
                headerIconRect.y,
                headerRect.width - (HeaderIconSize + headerIconPadding * 5),
                headerIconRect.height);

            bool isRoot = m_Graph.startNodeId == node.id;
            var headerColor = isRoot ? NodeColors.RootHeaderFooter : NodeColors.HeaderFooter;

            NodeColors.DrawRect(headerRect, headerColor);
            DrawIcon(headerIconRect);
            GUI.Label(headerTitleRect, node.name, NodeTitleStyle);

            dynamicHeight += HeaderHeight;

            // --- Phase 3: Ports + Inspector content ---
            DrawPortsList(node.inputPorts);

            // Inspector area — draw with GUILayout, measure actual consumed height
            float inspectorStartY = dynamicHeight + 5;
            var inspectorArea = new Rect(InspectorSideMargin, inspectorStartY,
                width - InspectorSideMargin * 2, 10000);
            GUILayout.BeginArea(inspectorArea);

            // Insert zero-height marker to capture GUILayout cursor position
            GUILayoutUtility.GetRect(0, 0);
            float yBefore = GUILayoutUtility.GetLastRect().yMax;

            // Draw inspector content (GUILayout auto-positions)
            if (graph != null && graph.showNodeViewDetails && graph.currentZoom > 0.7f)
            {
                // Use node's own inspector area width, not the panel width
                float availableW = inspectorArea.width;
                float ratioW = availableW * 0.4f;
                float oldLabelW = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = Mathf.Max(60f, ratioW);
                float oldAvailW = DrawBase.s_AvailableWidth;
                DrawBase.SetAvailableWidth(availableW);
                bool oldGraphCtx = DrawBase.s_IsGraphContext;
                DrawBase.s_IsGraphContext = true;
                DrawInspector();
                DrawBase.s_IsGraphContext = oldGraphCtx;
                DrawBase.SetAvailableWidth(oldAvailW);
                EditorGUIUtility.labelWidth = oldLabelW;
            }
            GUILayoutUtility.GetRect(0, 0);
            float yAfter = GUILayoutUtility.GetLastRect().yMax;

            GUILayout.EndArea();

            // Actual inspector height = difference in GUILayout cursor
            float inspectorHeight = Mathf.Max(yAfter - yBefore, 0);

            // Cache for Layout events (GetLastRect returns 0 on Layout)
            if (Event.current.type == EventType.Repaint)
                m_CachedInspectorHeight = inspectorHeight;
            else if (m_CachedInspectorHeight > 0)
                inspectorHeight = m_CachedInspectorHeight;

            dynamicHeight += inspectorHeight + 5;

            DrawPortsList(node.outputPorts);

            // --- Phase 4: Body extension if taller than estimated ---
            float actualBodyEndY = dynamicHeight + FooterGap;
            float estimatedBodyEndY = bodyStartY + estimatedBodyHeight;
            if (actualBodyEndY > estimatedBodyEndY)
            {
                var extRect = new Rect(leftX, estimatedBodyEndY, bodyWidth,
                    actualBodyEndY - estimatedBodyEndY);
                NodeColors.DrawRect(extRect, NodeColors.Body);
            }

            // --- Phase 6: Footer ---
            dynamicHeight += FooterGap;
            var footerRect = new Rect(leftX, dynamicHeight, bodyWidth, FooterHeight);
            NodeColors.DrawRect(footerRect, headerColor);
            dynamicHeight += FooterHeight;

            // --- Phase 6b: Bottom padding ---
            dynamicHeight += BottomPadding;

            // --- Phase 5: Glow at FINAL height (after all content + footer + padding) ---
            var finalGlowRect = new Rect(0, 0, width, dynamicHeight - 2);
            NodeColors.DrawRect(finalGlowRect, NodeColors.Glow);

            // --- Phase 7: Outline ---
            var outlineColor = GetOutlineColor();
            if (outlineColor.a > 0.01f)
            {
                var outlineRect = new Rect(0, 0, width, dynamicHeight - 2);
                NodeColors.DrawBorder(outlineRect, outlineColor, 2f);
            }

            // --- Phase 8: Right-edge resize handle ---
            if (graph == null || graph.currentZoom > 0.7f)
            {
                var resizeRect = new Rect(width - 6, 0, 6, dynamicHeight);
                var ev = Event.current;
                bool isResizeClick = ev != null && ev.type == EventType.MouseDown &&
                    ev.button == 0 && resizeRect.Contains(ev.mousePosition);

                if (isResizeClick)
                {
                    s_ResizingNodeId = m_Node.GetInstanceID();
                    s_IsResizingWidth = true;
                    ev.Use();
                    // Request Repaint so the resize cursor and handle update immediately
                    m_graphWindow.Repaint();
                }
                // Draw subtle handle
                NodeColors.DrawRect(new Rect(width - 2, 0, 2, dynamicHeight),
                    new Color(1f, 1f, 1f, 0.15f));
            }

            // --- Update node height for next frame ---
            UpdateNodeHeight(dynamicHeight);
        }

        /// <summary>
        /// End node resize if active. Called from GraphWindow on MouseUp.
        /// </summary>
        internal static void EndResize()
        {
            s_ResizingNodeId = -1;
            s_IsResizingWidth = false;
        }

        /// <summary>
        /// Computes the maximum group nesting depth from sorted members.
        /// Each FoldoutGroup/TabGroup adds 1 indent level.
        /// </summary>
        /// <summary>
        /// Estimates the maximum indent depth by analyzing group nesting in the node's fields.
        /// Checks BoxGroup/FoldoutGroup/TabGroup paths for "/" separators.
        /// </summary>
        /// <summary>
        /// Estimates extra width needed for TableList and Dictionary fields.
        /// Each table/dict column needs ~80px minimum.
        /// </summary>
        private float EstimateExtraContentWidth(NodeBase node)
        {
            float extraWidth = 0;
            var type = node.GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            Type currentType = type;
            while (currentType != null && currentType != typeof(object))
            {
                foreach (var field in currentType.GetFields(flags))
                {
                    bool isTableList = field.GetCustomAttributes(typeof(TableListAttribute), true).Length > 0;
                    bool isDictionary = typeof(System.Collections.IDictionary).IsAssignableFrom(field.FieldType);
                    bool isArray = field.FieldType.IsArray;
                    bool isList = typeof(System.Collections.IList).IsAssignableFrom(field.FieldType) && !isArray;

                    if (isTableList || isDictionary || isArray || isList)
                    {
                        // Determine element type
                        Type elemType = null;
                        if (isDictionary)
                        {
                            var args = field.FieldType.GetGenericArguments();
                            if (args.Length >= 2) elemType = args[1]; // value type
                        }
                        else if (isArray)
                        {
                            elemType = field.FieldType.GetElementType();
                        }
                        else if (isList)
                        {
                            var args = field.FieldType.GetGenericArguments();
                            if (args.Length >= 1) elemType = args[0];
                        }

                        if (elemType != null && !elemType.IsPrimitive && elemType != typeof(string))
                        {
                            // Count fields in element type as columns
                            var elemFields = elemType.GetFields(BindingFlags.Public | BindingFlags.Instance);
                            int colCount = elemFields.Length;
                            // 2 columns (Key+Value for dict) or colCount columns for table/array
                            int actualCols = isDictionary ? 2 : Mathf.Max(colCount, 1);
                            float tableWidth = actualCols * 80f + 50f; // 80px per column + index/delete
                            if (tableWidth > extraWidth)
                                extraWidth = tableWidth;
                        }
                    }
                }
                currentType = currentType.BaseType;
            }
            return extraWidth;
        }

        private int EstimateMaxIndentDepth(NodeBase node)
        {
            int maxDepth = 0;
            var type = node.GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            Type currentType = type;
            while (currentType != null && currentType != typeof(object))
            {
                foreach (var field in currentType.GetFields(flags))
                {
                    int depth = 0;
                    // Check BoxGroup path depth (e.g. "Combat/Stats" = 2 levels)
                    var boxAttr = field.GetCustomAttributes(typeof(BoxGroupAttribute), true);
                    if (boxAttr != null && boxAttr.Length > 0)
                    {
                        var bg = (BoxGroupAttribute)boxAttr[0];
                        depth = Mathf.Max(depth, bg.GroupName.Split('/').Length);
                    }
                    // Check FoldoutGroup path depth
                    var foldAttrs = field.GetCustomAttributes(typeof(FoldoutGroupAttribute), true);
                    if (foldAttrs != null && foldAttrs.Length > 0)
                    {
                        var fg = (FoldoutGroupAttribute)foldAttrs[0];
                        depth = Mathf.Max(depth, fg.GroupName.Split('/').Length);
                    }
                    // Check TabGroup path depth
                    var tabAttrs = field.GetCustomAttributes(typeof(TabGroupAttribute), true);
                    if (tabAttrs != null && tabAttrs.Length > 0)
                    {
                        var tg = (TabGroupAttribute)tabAttrs[0];
                        // TabGroup adds 1 level for the tab bar + group name depth
                        depth = Mathf.Max(depth, tg.GroupName.Split('/').Length + 1);
                    }
                    // Check HorizontalGroup
                    var horizAttrs = field.GetCustomAttributes(typeof(HorizontalGroupAttribute), true);
                    if (horizAttrs != null && horizAttrs.Length > 0)
                    {
                        var hg = (HorizontalGroupAttribute)horizAttrs[0];
                        depth = Mathf.Max(depth, hg.GroupName.Split('/').Length);
                    }
                    // Check [Serializable] nested object — adds 1 indent level
                    if (field.FieldType.IsClass
                        && field.FieldType != typeof(string)
                        && !field.FieldType.IsArray
                        && !typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType)
                        && !field.FieldType.IsGenericType
                        && field.FieldType.IsDefined(typeof(System.SerializableAttribute), false))
                    {
                        // Also check nested type's own groups
                        int nestedDepth = 1 + EstimateMaxIndentDepthFromType(field.FieldType, 1);
                        depth = Mathf.Max(depth, nestedDepth);
                    }
                    if (depth > maxDepth) maxDepth = depth;
                }
                currentType = currentType.BaseType;
            }
            return maxDepth;
        }

        private int EstimateMaxIndentDepthFromType(Type type, int currentDepth)
        {
            if (currentDepth > 5) return currentDepth; // prevent infinite recursion
            int maxDepth = 0;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            foreach (var field in type.GetFields(flags))
            {
                int depth = 0;
                var boxAttrs = field.GetCustomAttributes(typeof(BoxGroupAttribute), true);
                if (boxAttrs != null && boxAttrs.Length > 0)
                {
                    var bg = (BoxGroupAttribute)boxAttrs[0];
                    depth = Mathf.Max(depth, bg.GroupName.Split('/').Length);
                }
                var foldAttrs = field.GetCustomAttributes(typeof(FoldoutGroupAttribute), true);
                if (foldAttrs != null && foldAttrs.Length > 0)
                {
                    var fg = (FoldoutGroupAttribute)foldAttrs[0];
                    depth = Mathf.Max(depth, fg.GroupName.Split('/').Length);
                }
                var tabAttrs = field.GetCustomAttributes(typeof(TabGroupAttribute), true);
                if (tabAttrs != null && tabAttrs.Length > 0)
                {
                    var tg = (TabGroupAttribute)tabAttrs[0];
                    depth = Mathf.Max(depth, tg.GroupName.Split('/').Length + 1);
                }
                if (depth > maxDepth) maxDepth = depth;
            }
            return maxDepth;
        }

        protected void DrawPortsList(List<Port> ports)
        {
            if (ports == null) return;
            foreach (Port port in ports)
            {
                dynamicHeight += DrawPort(port).height;
            }
        }

        protected void UpdateNodeWidth(float width) => m_Node.SetWidth(width);
        protected void UpdateNodeHeight(float height) => m_Node.SetHeight(height);
        protected void UpdateNodePosition(Vector2 position) => m_Node.SetPosition(position);

        private Color GetOutlineColor()
        {
            if (EditorApplication.isPlaying && node.ping)
                return NodeColors.OutlinePlaying;

            if (!EditorApplication.isPlaying && node.ping)
                node.ping = false;

            if (isSelected)
                return NodeColors.Outline;

            if (node.isHovered)
                return NodeColors.WithAlpha(NodeColors.Outline, 0.4f);

            return Color.clear;
        }

        private void DrawNode(int id)
        {
            var color = GUI.color;
            if (isInGroup)
                GUI.color = new Color(0.75f, 0.85f, 1.0f, 1f);
            OnNodeGUI();
            GUI.color = color;

            // Handle resize drag inside GUI.Window — events are captured here after MouseDown.
            // GUI.Window only routes events when mouse is inside the window rect.
            // When mouse leaves the rect (fast drag), HandleMouseLeftClicks handles the fallback.
            if (s_IsResizingWidth && s_ResizingNodeId == m_Node.GetInstanceID())
            {
                var ev = Event.current;
                if (ev != null && ev.type == EventType.MouseDrag && ev.button == 0)
                {
                    // Inside GUI.Window, the matrix is already scaled by zoom,
                    // so ev.delta.x is in screen-space pixels — divide by zoom
                    // to get grid-space delta.
                    float zoom = m_Graph.currentZoom;
                    float newWidth = m_Node.GetWidth() + ev.delta.x / zoom;
                    m_Node.SetWidth(Mathf.Max(BaseWidth, newWidth));
                    m_graphWindow.Repaint();
                    ev.Use();
                }
                else if (ev != null && ev.type == EventType.MouseUp && ev.button == 0)
                {
                    EndResize();
                    ev.Use();
                }
            }
        }

        private void OnDeletePort(Port port)
        {
            m_graphWindow.DisconnectPort(port);
        }

        #endregion
    }

    public abstract class NodeView<T> : NodeView where T : NodeBase
    {
        public T node => base.node as T;
    }
}
