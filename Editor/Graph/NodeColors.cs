using UnityEditor;
using UnityEngine;

namespace TaoTie.Inspector.Editor
{
    /// <summary>
    /// Hardcoded color palette — replaces UIColor.asset dependency.
    /// All Node / Edge / Port drawing reads from these constants.
    /// </summary>
    public static class NodeColors
    {
        // Node
        public static readonly Color Glow = new(1f, 1f, 1f, 0.08f);
        public static readonly Color Body = EditorGUIUtility.isProSkin
            ? new Color(0.18f, 0.18f, 0.18f, 1f)
            : new Color(0.82f, 0.82f, 0.82f, 1f);
        public static readonly Color HeaderFooter = EditorGUIUtility.isProSkin
            ? new Color(0.24f, 0.24f, 0.24f, 1f)
            : new Color(0.75f, 0.75f, 0.75f, 1f);
        public static readonly Color RootHeaderFooter = EditorGUIUtility.isProSkin
            ? new Color(0.3f, 0.5f, 0.3f, 1f)
            : new Color(0.5f, 0.7f, 0.5f, 1f);
        public static readonly Color Outline = new(1f, 0.4f, 0.6f, 1f);
        public static readonly Color OutlinePlaying = new(0f, 1f, 0.1f, 1f);
        public static readonly Color HeaderIcon = Color.white;
        public static readonly Color HeaderText = Color.white;
        public static readonly Color Divider = new(0.5f, 0.5f, 0.5f, 0.6f);

        // Ports
        public static readonly Color PortInput = new(0.2f, 0.85f, 0.2f, 1f);
        public static readonly Color PortOutput = new(1f, 0.35f, 0.1f, 1f);

        // Edges
        public static readonly Color EdgeNormal = EditorGUIUtility.isProSkin
            ? new Color(0.8f, 0.8f, 0.8f, 1f)
            : new Color(0.3f, 0.3f, 0.3f, 1f);
        public static readonly Color EdgeOutput = new(1f, 0.35f, 0.1f, 1f);
        public static readonly Color EdgeInput = new(0.25f, 0.07f, 1f, 1f);

        /// <summary>Quick alpha helper — replaces ColorExtensions.WithAlpha.</summary>
        public static Color WithAlpha(Color c, float a) => new(c.r, c.g, c.b, a);

        /// <summary>Draw a flat-color rect using the built-in white texture.</summary>
        public static void DrawRect(Rect rect, Color color)
        {
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, EditorGUIUtility.whiteTexture);
            GUI.color = old;
        }

        /// <summary>Draw a rounded-rect outline (border only).</summary>
        public static void DrawBorder(Rect rect, Color color, float width = 2f)
        {
            DrawRect(new Rect(rect.x, rect.y, rect.width, width), color);                         // top
            DrawRect(new Rect(rect.x, rect.y + rect.height - width, rect.width, width), color);   // bottom
            DrawRect(new Rect(rect.x, rect.y, width, rect.height), color);                        // left
            DrawRect(new Rect(rect.x + rect.width - width, rect.y, width, rect.height), color);  // right
        }

        /// <summary>Draw a small circle (connection point) using Handles.</summary>
        public static void DrawDot(Vector2 center, float radius, Color color)
        {
            var old = Handles.color;
            Handles.color = color;
            Handles.DrawSolidDisc(center, Vector3.forward, radius);
            Handles.color = old;
        }
    }
}
