using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using TaoTie.Inspector;

namespace TaoTie.Inspector.Editor
{
    /// <summary>
    /// 纯 EditorGUI(Rect) 驱动的字段排版工具，专为 PropertyDrawer 约束设计。
    /// PropertyDrawer 的 OnGUI 里不能使用 EditorGUILayout（必须用 EditorGUI + Rect），
    /// 因此这里所有绘制基于传入的 (Rect area, float y) 逐字段向下排，并由成对的
    /// GetRowHeight / DrawPropertyRect 保证测量与绘制严格一致（避免 Animator 窗口布局抖动）。
    /// 视觉风格对齐 DrawBase（标题栏、box 底、集合网格、[SerializeReference] 类型下拉 + 隐藏 picker）。
    /// </summary>
    public static class SMBPropertyLayout
    {
        public const float RowHeight = 18f;
        public const float LineHeight = 20f;
        public const float VSpacing = 2f;
        internal const float FoldIndent = 15f;

        // ------------------------------------------------------------------
        // 高度测量 —— 与 DrawPropertyRect 严格对应
        // ------------------------------------------------------------------
        public static float GetFieldHeight(SerializedProperty p, float width)
        {
            if (p.propertyType == SerializedPropertyType.ManagedReference)
                return GetManagedRefHeight(p, width);
            if (p.isArray && p.propertyType != SerializedPropertyType.String)
                return GetArrayHeight(p);
            if (p.hasVisibleChildren && p.propertyType == SerializedPropertyType.Generic)
                return GetNestedClassHeight(p, width);
            return EditorGUI.GetPropertyHeight(p, true) + VSpacing;
        }

        private static float GetNestedClassHeight(SerializedProperty p, float width)
        {
            float h = LineHeight + VSpacing;
            if (!p.isExpanded) return h;
            h += GetChildrenHeight(p, width);
            return h;
        }

        private static float GetChildrenHeight(SerializedProperty parent, float width)
        {
            float h = 0f;
            foreach (var child in CollectChildren(parent))
                h += GetFieldHeight(child, width);
            return h;
        }

        private static float GetManagedRefHeight(SerializedProperty p, float width)
        {
            float h = LineHeight + VSpacing;
            if (p.managedReferenceValue != null && IsManagedRefExpanded(p))
                h += SMBGroupLayout.GetManagedChildrenHeight(p, width);
            return h;
        }

        private static bool IsManagedRefExpanded(SerializedProperty p)
        {
            return SessionState.GetBool("TaoTie_SMB_Expanded_" + p.propertyPath, true);
        }

        private static float GetArrayHeight(SerializedProperty p)
        {
            float h = LineHeight + VSpacing; // 标题栏
            if (!p.isExpanded) return h;
            h += LineHeight + VSpacing; // 列头
            for (int i = 0; i < p.arraySize; i++)
                h += LineHeight + VSpacing;
            return h;
        }

        // ------------------------------------------------------------------
        // 标题栏
        // ------------------------------------------------------------------
        public static float DrawHeader(Rect area, float y, UnityEngine.Object target)
        {
            var r = new Rect(area.x, y, area.width, RowHeight);
            EditorGUI.DrawRect(r, new Color(0.3f, 0.3f, 0.3f, 0.25f));
            string name = target != null ? ObjectNames.NicifyVariableName(target.GetType().Name) : "StateMachineBehaviour";
            EditorGUI.LabelField(r, name, EditorStyles.boldLabel);
            return r.yMax + VSpacing;
        }

        // ------------------------------------------------------------------
        // OnValueChanged 延迟回调（在抽屉 ApplyModifiedProperties 之后 Flush）
        // ------------------------------------------------------------------
        private static readonly Dictionary<(int, string), (object value, int count)> s_OnChangedState = new();
        private static readonly List<(object target, Type declaringType, string methodName)> s_PendingChanged = new();

        /// <summary>取 SerializedProperty 的盒装值（数组取其 count 以感知增删）。</summary>
        private static object BoxedValue(SerializedProperty p)
        {
            if (p.isArray && p.propertyType != SerializedPropertyType.String) return p.arraySize;
            return p.propertyType switch
            {
                SerializedPropertyType.Integer => p.intValue,
                SerializedPropertyType.Float => p.floatValue,
                SerializedPropertyType.Boolean => p.boolValue,
                SerializedPropertyType.String => p.stringValue,
                SerializedPropertyType.Enum => p.enumValueIndex,
                SerializedPropertyType.ObjectReference => p.objectReferenceValue,
                SerializedPropertyType.Color => p.colorValue,
                SerializedPropertyType.Vector2 => p.vector2Value,
                SerializedPropertyType.Vector3 => p.vector3Value,
                SerializedPropertyType.Vector4 => p.vector4Value,
                SerializedPropertyType.Vector2Int => p.vector2IntValue,
                SerializedPropertyType.Vector3Int => p.vector3IntValue,
                SerializedPropertyType.Quaternion => p.quaternionValue,
                SerializedPropertyType.LayerMask => p.intValue,
                SerializedPropertyType.ManagedReference => p.managedReferenceValue,
                _ => null
            };
        }

        private static bool IsSameValue(object a, object b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            return a.Equals(b);
        }

        private static int TargetKey(SerializedProperty p)
        {
            var t = p.serializedObject != null ? p.serializedObject.targetObject : null;
            return t != null ? t.GetInstanceID() : 0;
        }

        /// <summary>在抽屉 ApplyModifiedProperties 之后调用，执行本帧排队的 OnValueChanged 回调。</summary>
        public static void FlushPendingCallbacks()
        {
            if (s_PendingChanged.Count == 0) return;
            var pending = s_PendingChanged.ToArray();
            s_PendingChanged.Clear();
            for (int i = 0; i < pending.Length; i++)
                ReflectionMethodInvoker.InvokeNoArg(pending[i].target, pending[i].declaringType, pending[i].methodName);
        }

        // ------------------------------------------------------------------
        // 绘制入口 —— 返回下一 y
        // ------------------------------------------------------------------
        public static float DrawField(Rect area, SerializedProperty p, float y, FieldInfo fi = null, object host = null)
        {
            var onChanged = fi != null ? fi.GetCustomAttribute<OnValueChangedAttribute>() : null;
            object beforeVal = null;
            bool hasBefore = false;
            if (onChanged != null)
            {
                beforeVal = BoxedValue(p);
                hasBefore = true;
            }

            float ret = DrawFieldBody(area, p, y, fi, host);

            if (onChanged != null && hasBefore)
            {
                object afterVal = BoxedValue(p);
                bool changed = !IsSameValue(beforeVal, afterVal);
                if (!changed)
                {
                    if (s_OnChangedState.TryGetValue((TargetKey(p), p.propertyPath), out var prev))
                        changed = !IsSameValue(prev.value, afterVal) || prev.count != (p.isArray ? p.arraySize : -1);
                }
                s_OnChangedState[(TargetKey(p), p.propertyPath)] = (afterVal, p.isArray ? p.arraySize : -1);
                if (changed)
                {
                    var invokeTarget = host ?? (p.serializedObject != null ? p.serializedObject.targetObject : null);
                    if (invokeTarget != null)
                        s_PendingChanged.Add((invokeTarget, fi.DeclaringType, onChanged.MethodName));
                }
            }
            return ret;
        }

        private static float DrawFieldBody(Rect area, SerializedProperty p, float y, FieldInfo fi, object host)
        {
            bool enabled = IsFieldEnabled(fi, host);
            using (new EditorGUI.DisabledScope(!enabled))
            {
                if (p.propertyType == SerializedPropertyType.ManagedReference)
                    return DrawManagedRef(area, p, y, fi);
                if (p.isArray && p.propertyType != SerializedPropertyType.String)
                    return DrawArray(area, p, y, fi);
                if (p.hasVisibleChildren && p.propertyType == SerializedPropertyType.Generic)
                    return DrawNestedClass(area, p, y, fi);
                return DrawSimple(area, p, y, fi, host);
            }
        }

        /// <summary>
        /// 启用态求值（对齐 TaoTiePropertyEntry.IsEnabled）：
        /// [ReadOnly]、[DisableInEditorMode]、[EnableIf]、[DisableIf]，条件以宿主实例求值。
        /// </summary>
        private static bool IsFieldEnabled(FieldInfo fi, object host)
        {
            if (fi == null) return true;
            if (fi.IsDefined(typeof(ReadOnlyAttribute), true)) return false;
            if (fi.GetCustomAttribute<DisableInEditorModeAttribute>() != null && !EditorApplication.isPlaying) return false;
            if (host == null) return true;
            foreach (var a in fi.GetCustomAttributes<EnableIfAttribute>(true))
                if (!TaoTieConditionResolver.EvaluateEnableIf(a, host)) return false;
            foreach (var a in fi.GetCustomAttributes<DisableIfAttribute>(true))
                if (TaoTieConditionResolver.EvaluateDisableIf(a, host)) return false;
            return true;
        }

        private static float DrawSimple(Rect area, SerializedProperty p, float y, FieldInfo fi, object host)
        {
            float h = EditorGUI.GetPropertyHeight(p, true) + VSpacing;
            var r = new Rect(area.x, y, area.width, h);

            var range = fi != null ? fi.GetCustomAttribute<PropertyRangeAttribute>() : null;
            if (range != null && range.MinMember == null && range.MaxMember == null)
            {
                if (p.propertyType == SerializedPropertyType.Float)
                {
                    var fieldRect = EditorGUI.PrefixLabel(r, LabelOf(p, fi));
                    EditorGUI.Slider(fieldRect, p, (float)range.Min, (float)range.Max);
                    return r.yMax;
                }
                if (p.propertyType == SerializedPropertyType.Integer)
                {
                    var fieldRect = EditorGUI.PrefixLabel(r, LabelOf(p, fi));
                    EditorGUI.IntSlider(fieldRect, p, (int)range.Min, (int)range.Max);
                    return r.yMax;
                }
            }

            var vd = fi != null ? fi.GetCustomAttribute<ValueDropdownAttribute>() : null;
            if (vd != null && host != null)
            {
                var items = ValueDropdownDrawer.GetItems(host, vd.MemberName);
                if (items != null && items.Count > 0)
                {
                    var fieldRect = EditorGUI.PrefixLabel(r, LabelOf(p, fi));
                    object curVal = ValueDropdownDrawer.GetValue(p);
                    int sel = -1;
                    for (int i = 0; i < items.Count; i++)
                    {
                        if (ValueDropdownDrawer.EqualValue(items[i].Value, curVal)) { sel = i; break; }
                    }

                    if (vd.AppendNextDrawer)
                    {
                        // 字段本体 + 尾随 ▼ 按钮
                        float btnW = 22f;
                        var btnRect = new Rect(fieldRect.xMax - btnW, fieldRect.y, btnW, fieldRect.height);
                        var mainRect = new Rect(fieldRect.x, fieldRect.y,
                            Mathf.Max(0f, fieldRect.width - btnW - 2f), fieldRect.height);
                        EditorGUI.PropertyField(mainRect, p, GUIContent.none, true);
                        if (GUI.Button(btnRect, "▼", EditorStyles.miniButton))
                            ShowValueDropdown(btnRect, p, items, sel);
                    }
                    else
                    {
                        // 弹出式：显示当前选中文本
                        string text = sel >= 0 ? items[sel].Text : p.displayName;
                        if (GUI.Button(fieldRect, text, EditorStyles.popup))
                            ShowValueDropdown(fieldRect, p, items, sel);
                    }
                    return r.yMax;
                }
            }

            EditorGUI.PropertyField(r, p, LabelOf(p, fi), true);

            // 数值约束：[MinValue] / [MaxValue]
            if (fi != null && (p.propertyType == SerializedPropertyType.Float || p.propertyType == SerializedPropertyType.Integer))
            {
                var minAttr = fi.GetCustomAttribute<MinValueAttribute>();
                var maxAttr = fi.GetCustomAttribute<MaxValueAttribute>();
                if (minAttr != null || maxAttr != null)
                {
                    if (p.propertyType == SerializedPropertyType.Float)
                    {
                        float min = minAttr != null ? (float)minAttr.MinValue : float.MinValue;
                        float max = maxAttr != null ? (float)maxAttr.MaxValue : float.MaxValue;
                        p.floatValue = Mathf.Clamp(p.floatValue, min, max);
                    }
                    else
                    {
                        int min = minAttr != null ? (int)minAttr.MinValue : int.MinValue;
                        int max = maxAttr != null ? (int)maxAttr.MaxValue : int.MaxValue;
                        p.intValue = Mathf.Clamp(p.intValue, min, max);
                    }
                }
            }
            return r.yMax;
        }

        private static void ShowValueDropdown(Rect rect, SerializedProperty p,
            System.Collections.Generic.List<ValueDropdownItem> items, int selectedIndex)
        {
            // 打开时清缓存，下次 GetItems 重新求值（成员数据可能已变化）
            ValueDropdownDrawer.ClearCache();
            ValueDropdownPopup.Show(rect, items, selectedIndex, idx =>
            {
                if (idx >= 0 && idx < items.Count)
                {
                    ValueDropdownDrawer.SetValue(p, items[idx].Value);
                    p.serializedObject.ApplyModifiedProperties();
                }
            });
        }

        private static float DrawNestedClass(Rect area, SerializedProperty p, float y, FieldInfo fi)
        {
            var head = new Rect(area.x, y, area.width, LineHeight);
            p.isExpanded = EditorGUI.Foldout(head, p.isExpanded, LabelOf(p, fi), true);
            y = head.yMax + VSpacing;
            if (!p.isExpanded) return y;
            return DrawChildren(area, p, y, head.x + FoldIndent);
        }

        private static float DrawChildren(Rect area, SerializedProperty parent, float y, float indentX)
        {
            var subArea = new Rect(indentX, area.y, area.width - (indentX - area.x), area.height);
            foreach (var child in CollectChildren(parent))
                y = DrawField(subArea, child, y);
            return y;
        }

        /// <summary>
        /// 收集 parent 的可绘制子字段。优先走标准 NextVisible 遍历；若枚举不出任何子字段
        /// （Animator 窗口对 [SerializeReference] managed reference 子树存在此情况），
        /// 退化为用反射取实际实例的序列化字段名并通过 FindPropertyRelative 直接取子属性。
        /// 高度测量与绘制共用本方法，保证两侧一致。
        /// </summary>
        private static List<SerializedProperty> CollectChildren(SerializedProperty parent)
        {
            var children = new List<SerializedProperty>();

            var c = parent.Copy();
            var end = parent.GetEndProperty();
            if (c.NextVisible(true))
            {
                while (!SerializedProperty.EqualContents(c, end))
                {
                    children.Add(c.Copy());
                    if (!c.NextVisible(false)) break;
                }
                if (children.Count > 0) return children;
            }

            foreach (var name in ManagedRefFieldNames(parent))
            {
                var child = parent.FindPropertyRelative(name);
                if (child != null) children.Add(child);
            }
            return children;
        }

        private static IEnumerable<string> ManagedRefFieldNames(SerializedProperty parent)
        {
            if (parent.propertyType != SerializedPropertyType.ManagedReference) yield break;
            var val = parent.managedReferenceValue;
            if (val == null) yield break;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            Type t = val.GetType();
            while (t != null && t != typeof(object))
            {
                foreach (var f in t.GetFields(flags))
                {
                    if (f.IsStatic) continue;
                    bool serializable = f.IsPublic || f.IsDefined(typeof(SerializeField), true);
                    if (serializable) yield return f.Name;
                }
                t = t.BaseType;
            }
        }

        // ------------------------------------------------------------------
        // [SerializeReference] —— 类型下拉 + 隐藏 picker + 子字段折叠
        // ------------------------------------------------------------------
        private static float DrawManagedRef(Rect area, SerializedProperty p, float y, FieldInfo fi)
        {
            var head = new Rect(area.x, y, area.width, LineHeight);
            var val = p.managedReferenceValue;
            GUIContent label = LabelOf(p, fi);
            if (val == null)
            {
                // 空值：类型下拉
                var types = CollectSubtypes(ManagedRefFieldType(p));
                EditorGUI.LabelField(new Rect(head.x, head.y, 90f, head.height), label);
                var popupRect = new Rect(head.x + 100f, head.y, Mathf.Max(0f, head.width - 100f), head.height);
                int idx = EditorGUI.Popup(popupRect, -1, ToTypeNames(types));
                if (idx >= 0)
                {
                    p.managedReferenceValue = Activator.CreateInstance(types[idx]);
                    p.serializedObject.ApplyModifiedProperties();
                }
                return head.yMax + VSpacing;
            }

            // 有值：折叠 + 隐藏 picker（不显示原生 managed-ref picker）
            bool exp = IsManagedRefExpanded(p);
            exp = EditorGUI.Foldout(head, exp, label, true);
            SessionState.SetBool("TaoTie_SMB_Expanded_" + p.propertyPath, exp);
            float labelW = EditorStyles.foldout.CalcSize(label).x + 18f;
            var typeRect = new Rect(head.x + labelW, head.y, Mathf.Max(0f, head.width - labelW), head.height);
            EditorGUI.LabelField(typeRect, LabelResolver.GetTypeLabel(val.GetType()), EditorStyles.boldLabel);
            y = head.yMax + VSpacing;
            if (exp)
                y = SMBGroupLayout.DrawManagedChildren(area, p, y, head.x + FoldIndent);
            return y;
        }

        public static Type ManagedRefFieldType(SerializedProperty p)
        {
            // 优先 fieldInfo（PropertyDrawer 提供）；退化用 managedReferenceValue 的实际类型
            var fi = ManagedRefFieldInfoResolver(p);
            if (fi != null) return fi.FieldType;
            return p.managedReferenceValue?.GetType();
        }

        private static FieldInfo ManagedRefFieldInfoResolver(SerializedProperty p)
        {
            try
            {
                var type = p.serializedObject.targetObject.GetType();
                var path = p.propertyPath;
                var last = path.LastIndexOf('.');
                string name = last >= 0 ? path.Substring(last + 1) : path;
                var fi = type.GetField(name,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                return fi;
            }
            catch { return null; }
        }

        public static List<Type> CollectSubtypes(Type baseType)
        {
            var result = new List<Type>();
            if (baseType == null) return result;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (System.Reflection.ReflectionTypeLoadException) { continue; }
                foreach (var t in types)
                {
                    if (t.IsClass && !t.IsAbstract && baseType.IsAssignableFrom(t))
                        result.Add(t);
                }
            }
            return result;
        }

        public static string[] ToTypeNames(List<Type> types)
        {
            var names = new string[types.Count];
            for (int i = 0; i < types.Count; i++)
                names[i] = LabelResolver.GetTypeLabel(types[i]);
            return names;
        }

        // ------------------------------------------------------------------
        // 数组/列表 —— box + 标题栏 + 行网格（风格对齐 DrawBase）
        // ------------------------------------------------------------------
        private static float DrawArray(Rect area, SerializedProperty p, float y, FieldInfo fi)
        {
            // 标题栏（折叠 + 计数 + 增删按钮）
            var bar = new Rect(area.x, y, area.width, LineHeight);
            EditorGUI.DrawRect(bar, new Color(0.3f, 0.3f, 0.3f, 0.2f));
            float btnW = 22f;
            var minusRect = new Rect(bar.xMax - btnW - 2f, bar.y, btnW, bar.height);
            var plusRect = new Rect(minusRect.x - btnW - 2f, bar.y, btnW, bar.height);
            var foldRect = new Rect(bar.x, bar.y, plusRect.x - bar.x, bar.height);
            p.isExpanded = EditorGUI.Foldout(foldRect, p.isExpanded, $"{LabelOf(p, fi).text} ({p.arraySize})", true);
            if (GUI.Button(plusRect, "+")) { p.arraySize++; p.serializedObject.ApplyModifiedProperties(); }
            if (GUI.Button(minusRect, "-")) { if (p.arraySize > 0) { p.arraySize--; p.serializedObject.ApplyModifiedProperties(); } }
            y = bar.yMax + VSpacing;

            if (!p.isExpanded) return y;

            // 列头
            var header = new Rect(area.x, y, area.width, LineHeight);
            EditorGUI.DrawRect(header, new Color(0.3f, 0.3f, 0.3f, 0.35f));
            EditorGUI.LabelField(new Rect(header.x, header.y, 28f, header.height), "#", EditorStyles.boldLabel);
            y = header.yMax + VSpacing;

            // 行
            for (int i = 0; i < p.arraySize; i++)
            {
                var elem = p.GetArrayElementAtIndex(i);
                var row = new Rect(area.x, y, area.width, LineHeight);
                if (i % 2 == 1)
                    EditorGUI.DrawRect(row, new Color(0.5f, 0.5f, 0.5f, 0.1f));
                EditorGUI.LabelField(new Rect(row.x, row.y, 28f, row.height), i.ToString());
                var delRect = new Rect(row.xMax - btnW, row.y, btnW, row.height);
                bool del = GUI.Button(delRect, "×");
                var fieldRect = new Rect(row.x + 28f, row.y, delRect.x - (row.x + 28f), row.height);
                EditorGUI.PropertyField(fieldRect, elem, GUIContent.none, true);
                if (del) { p.DeleteArrayElementAtIndex(i); p.serializedObject.ApplyModifiedProperties(); }
                y = row.yMax + VSpacing;
            }
            return y;
        }

        // ------------------------------------------------------------------
        // 标签 —— LabelText 覆盖，否则 displayName
        // ------------------------------------------------------------------
        private static GUIContent LabelOf(SerializedProperty p, FieldInfo fi)
        {
            if (fi != null)
            {
                var lt = fi.GetCustomAttribute<LabelTextAttribute>();
                if (lt != null) return new GUIContent(lt.Text);
            }
            return new GUIContent(p.displayName);
        }
    }
}
