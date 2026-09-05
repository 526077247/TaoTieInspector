using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace TaoTie.Inspector.Editor
{
    /// <summary>
    /// Shared utility for resolving display labels from attributes.
    /// Used by both Inspector (SerializedProperty) and Graph (Reflection) paths.
    /// </summary>
    public static class LabelResolver
    {
        private static readonly Dictionary<Type, string> s_TypeLabelCache = new();

        /// <summary>Get type display name: LabelText if present, otherwise type Name.</summary>
        public static string GetTypeLabel(Type type)
        {
            if (type == null) return "null";
            if (s_TypeLabelCache.TryGetValue(type, out var cached))
                return cached;
            var attr = type.GetCustomAttributes(typeof(LabelTextAttribute), false);
            string result = attr.Length > 0 ? ((LabelTextAttribute)attr[0]).Text : type.Name;
            s_TypeLabelCache[type] = result;
            return result;
        }

        /// <summary>Get member display name: LabelText if present, otherwise NicifyVariableName.</summary>
        public static string GetMemberLabel(MemberInfo member)
        {
            if (member == null) return "";
            var attr = member.GetCustomAttributes(typeof(LabelTextAttribute), true);
            return attr.Length > 0 ? ((LabelTextAttribute)attr[0]).Text : ObjectNames.NicifyVariableName(member.Name);
        }

        /// <summary>Get member label as GUIContent with tooltip.</summary>
        public static GUIContent GetMemberGUIContent(MemberInfo member)
        {
            string label = GetMemberLabel(member);
            string tip = null;
            var tooltipAttr = member.GetCustomAttributes(typeof(TooltipAttribute), true);
            if (tooltipAttr.Length > 0)
                tip = ((TooltipAttribute)tooltipAttr[0]).tooltip;
            return new GUIContent(label, tip);
        }
    }

    /// <summary>Shared Title attribute rendering.</summary>
    public static class TitleDrawer
    {
        private static GUIStyle s_TitleStyle;

        public static void Draw(TitleAttribute title)
        {
            if (s_TitleStyle == null)
                s_TitleStyle = new GUIStyle(EditorStyles.boldLabel);
            if (title.Indented) EditorGUI.indentLevel++;
            s_TitleStyle.alignment = title.TitleAlignment switch
            {
                TitleAlignmentType.Center => TextAnchor.MiddleCenter,
                TitleAlignmentType.Right => TextAnchor.UpperRight,
                _ => TextAnchor.UpperLeft
            };
            EditorGUILayout.LabelField(title.Title, s_TitleStyle);
            if (title.HorizontalLine)
            {
                var rect = GUILayoutUtility.GetLastRect();
                rect.y += rect.height - 1;
                rect.height = 1;
                EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.5f));
            }
            if (title.Indented) EditorGUI.indentLevel--;
            GUILayout.Space(2);
        }
    }

    /// <summary>Shared InfoBox attribute rendering.</summary>
    public static class InfoBoxDrawer
    {
        public static void Draw(InfoBoxAttribute infoBox)
        {
            MessageType msgType = infoBox.InfoMessageType switch
            {
                InfoMessageType.Info => MessageType.Info,
                InfoMessageType.Warning => MessageType.Warning,
                InfoMessageType.Error => MessageType.Error,
                _ => MessageType.None
            };
            EditorGUILayout.HelpBox(infoBox.Message, msgType);
            GUILayout.Space(2);
        }
    }

    /// <summary>Shared NotNull validation rendering.</summary>
    public static class NotNullRenderer
    {
        public static void Draw(string errorMessage, bool isNull)
        {
            if (!isNull) return;
            string msg = !string.IsNullOrEmpty(errorMessage)
                ? errorMessage
                : "This field cannot be null";
            EditorGUILayout.HelpBox(msg, MessageType.Error);
        }
    }

    /// <summary>Measured width (px) of GUI content, so buttons adapt to the active skin / editor font.</summary>
    public static class GuiSizing
    {
        private static readonly GUIContent s_SetNull = new GUIContent("SetNull");

        public static float SetNullButtonWidth()
        {
            return GUI.skin.button.CalcSize(s_SetNull).x + 4f;
        }
    }

    /// <summary>Shared no-arg method invocation via reflection.</summary>
    public static class ReflectionMethodInvoker
    {
        public static void InvokeNoArg(object target, Type declaringType, string methodName)
        {
            if (target == null || string.IsNullOrEmpty(methodName)) return;
            Type searchType = declaringType ?? target.GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            var method = searchType.GetMethod(methodName, flags);
            if (method != null && method.GetParameters().Length == 0)
            {
                var invokeTarget = method.IsStatic ? null : target;
                method.Invoke(invokeTarget, null);
            }
        }
    }
}
