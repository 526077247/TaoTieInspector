using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace TaoTie.Inspector.Editor
{
    /// <summary>
    /// Animator 窗口 SMB 接管绘制的简易分组引擎（纯 Rect，PropertyDrawer 友好）。
    /// 支持 BoxGroup / FoldoutGroup / TabGroup，分组优先级与普通 Inspector（GroupEntryData）一致：
    /// Box > Foldout > Tab。
    ///
    /// 本层还处理字段级特性：
    ///   [PropertyOrder]        —— 分组前按 Order 稳定排序
    ///   [LabelText]            —— 字段标签覆盖（透传给 SMBPropertyLayout）
    ///   [ReadOnly]             —— 禁用编辑（透传）
    ///   [MinValue]/[MaxValue]/[PropertyRange] —— 数值约束/滑条（透传）
    ///   [Title] / [InfoBox] / [PropertySpace] —— 标题/消息框/留白装饰
    ///   [ShowIf]/[HideIf]      —— 分组前的可见性过滤（宿主实例求值，见 IsSlotVisible）
    ///   [InfoBox(VisibleIf)]   —— 按宿主实例动态显隐
    ///   [Button]               —— 参数方法按钮条（根类型 + managed ref 类型）
    ///   [EnableIf]/[DisableIf]/[DisableInEditorMode]/[OnValueChanged]/[ValueDropdown]
    ///                        —— 宿主实例相关，绘制层（SMBPropertyLayout）处理
    ///
    /// 折叠 / 页签状态与普通 Inspector 共用同一 SessionState 键（TaoTie_Fold_* / TaoTie_Tab_*）。
    ///
    /// 能力边界（Animator 窗口）：不处理嵌套分组（如 tab 内 box）、HorizontalGroup（按普通行平铺）、
    /// 嵌套 [Serializable] 结构体内部字段的特性（无 FieldInfo，不参与求值）。
    /// </summary>
    public static class SMBGroupLayout
    {
        internal sealed class FieldSlot
        {
            public SerializedProperty P;
            public FieldInfo Fi;
            /// <summary>字段宿主实例：顶层 = 目标 SMB 实例；managed ref 子字段 = managed 实例。</summary>
            public object Host;
        }

        internal sealed class TabSection
        {
            public string TabName;
            public List<FieldSlot> Fields = new();
        }

        internal sealed class RenderUnit
        {
            public UnitKind Kind;
            public FieldSlot Slot;
            public string GroupName;
            public List<TabSection> Sections;
            public List<FieldSlot> Members;
        }

        internal enum UnitKind { Field, TabGroup, FoldoutGroup, BoxGroup }

        private struct FieldGroup
        {
            public UnitKind Kind;
            public string GroupName;
            public string TabName;

            public bool IsGrouped => Kind != UnitKind.Field;
        }

        // ------------------------------------------------------------------
        // 字段装饰（Title / InfoBox / PropertySpace）
        // ------------------------------------------------------------------
        private sealed class FieldSpec
        {
            public PropertySpaceAttribute Space;
            public TitleAttribute Title;
            public List<InfoBoxAttribute> InfoBoxes;
            public ShowIfAttribute[] ShowIf;
            public HideIfAttribute[] HideIf;
        }

        private static readonly Dictionary<FieldInfo, FieldSpec> specCache = new();
        private static GUIStyle s_TitleStyle;

        private static FieldSpec SpecOf(FieldInfo fi)
        {
            if (fi == null) return null;
            if (specCache.TryGetValue(fi, out var cached)) return cached;

            var spec = new FieldSpec
            {
                Space = fi.GetCustomAttribute<PropertySpaceAttribute>(),
                Title = fi.GetCustomAttribute<TitleAttribute>(),
                ShowIf = fi.GetCustomAttributes<ShowIfAttribute>(true).ToArray(),
                HideIf = fi.GetCustomAttributes<HideIfAttribute>(true).ToArray()
            };
            var boxes = new List<InfoBoxAttribute>();
            foreach (var attr in fi.GetCustomAttributes(typeof(InfoBoxAttribute), true))
                boxes.Add((InfoBoxAttribute)attr);
            if (boxes.Count > 0) spec.InfoBoxes = boxes;
            specCache[fi] = spec;
            return spec;
        }

        /// <summary>
        /// 可见性过滤：ShowIf/HideIf 均以字段宿主实例求值（表达式或成员引用）。
        /// Host 缺失（无实例承载）时不参与过滤，按可见处理。
        /// </summary>
        private static bool IsSlotVisible(FieldSlot slot)
        {
            var spec = SpecOf(slot.Fi);
            if (spec == null || slot.Host == null) return true;
            if (spec.ShowIf != null && spec.ShowIf.Length > 0)
            {
                for (int i = 0; i < spec.ShowIf.Length; i++)
                    if (!TaoTieConditionResolver.EvaluateShowIf(spec.ShowIf[i], slot.Host)) return false;
            }
            if (spec.HideIf != null && spec.HideIf.Length > 0)
            {
                for (int i = 0; i < spec.HideIf.Length; i++)
                    if (TaoTieConditionResolver.EvaluateHideIf(spec.HideIf[i], slot.Host)) return false;
            }
            return true;
        }

        /// <summary>InfoBox.VisibleIf 非空时按宿主实例动态求值（调用方负责保证 host 有效）。</summary>
        private static bool IsInfoBoxVisible(InfoBoxAttribute ib, object host)
        {
            if (string.IsNullOrEmpty(ib.VisibleIf)) return true;
            if (host == null) return false;
            return TaoTieConditionResolver.Evaluate(host, ib.VisibleIf);
        }

        private static GUIStyle TitleStyle()
        {
            if (s_TitleStyle == null) s_TitleStyle = new GUIStyle(EditorStyles.boldLabel);
            return s_TitleStyle;
        }

        private static float TitleHeight(TitleAttribute title)
        {
            return SMBPropertyLayout.LineHeight + (title.HorizontalLine ? 1f : 0f) + 2f;
        }

        private static float InfoBoxHeight(InfoBoxAttribute ib, float width)
        {
            return EditorStyles.helpBox.CalcHeight(new GUIContent(ib.Message), width) + 2f;
        }

        private static float GetDecoratedFieldHeight(FieldSlot slot, float width)
        {
            var spec = SpecOf(slot.Fi);
            float h = 0f;
            if (spec != null)
            {
                if (spec.Space != null && spec.Space.SpaceBefore > 0) h += spec.Space.SpaceBefore;
                if (spec.Title != null) h += TitleHeight(spec.Title);
                if (spec.InfoBoxes != null)
                    for (int i = 0; i < spec.InfoBoxes.Count; i++)
                        if (IsInfoBoxVisible(spec.InfoBoxes[i], slot.Host))
                            h += InfoBoxHeight(spec.InfoBoxes[i], width);
            }
            h += SMBPropertyLayout.GetFieldHeight(slot.P, width);
            if (spec != null && spec.Space != null && spec.Space.SpaceAfter > 0) h += spec.Space.SpaceAfter;
            return h;
        }

        private static float DrawDecoratedField(Rect area, FieldSlot slot, float y)
        {
            var spec = SpecOf(slot.Fi);
            if (spec != null)
            {
                if (spec.Space != null && spec.Space.SpaceBefore > 0) y += spec.Space.SpaceBefore;
                if (spec.Title != null)
                {
                    var style = TitleStyle();
                    style.alignment = spec.Title.TitleAlignment switch
                    {
                        TitleAlignmentType.Center => TextAnchor.MiddleCenter,
                        TitleAlignmentType.Right => TextAnchor.UpperRight,
                        _ => TextAnchor.UpperLeft
                    };
                    var tr = new Rect(area.x, y, area.width, SMBPropertyLayout.LineHeight);
                    EditorGUI.LabelField(tr, spec.Title.Title, style);
                    y += SMBPropertyLayout.LineHeight;
                    if (spec.Title.HorizontalLine)
                    {
                        EditorGUI.DrawRect(new Rect(area.x, tr.y + tr.height - 1f, area.width, 1f),
                            new Color(0.5f, 0.5f, 0.5f, 0.5f));
                        y += 1f;
                    }
                    y += 2f;
                }
                if (spec.InfoBoxes != null)
                {
                    for (int i = 0; i < spec.InfoBoxes.Count; i++)
                    {
                        if (IsInfoBoxVisible(spec.InfoBoxes[i], slot.Host))
                            y = DrawInfoBox(area, spec.InfoBoxes[i], y);
                    }
                }
            }
            y = SMBPropertyLayout.DrawField(area, slot.P, y, slot.Fi, slot.Host);
            if (spec != null && spec.Space != null && spec.Space.SpaceAfter > 0) y += spec.Space.SpaceAfter;
            return y;
        }

        private static float DrawInfoBox(Rect area, InfoBoxAttribute ib, float y)
        {
            var content = new GUIContent(ib.Message);
            float h = EditorStyles.helpBox.CalcHeight(content, area.width);
            MessageType type = ib.InfoMessageType switch
            {
                InfoMessageType.Warning => MessageType.Warning,
                InfoMessageType.Error => MessageType.Error,
                _ => MessageType.Info
            };
            EditorGUI.HelpBox(new Rect(area.x, y, area.width, h), content.text, type);
            return y + h + 2f;
        }

        // ------------------------------------------------------------------
        // [Button] 参数方法按钮条
        // ------------------------------------------------------------------
        private static readonly Dictionary<Type, MethodInfo[]> buttonCache = new();

        /// <summary>收集类型链上带 [Button] 的无参方法（基类 → 派生声明序，对齐 DrawBase）。</summary>
        public static MethodInfo[] GetButtonMethods(Type type)
        {
            if (type == null) return null;
            if (buttonCache.TryGetValue(type, out var cached)) return cached;

            var list = new List<MethodInfo>();
            var cur = type;
            while (cur != null && cur != typeof(object))
            {
                foreach (var m in cur.GetMethods(BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (m.GetParameters().Length > 0) continue;
                    if (m.IsDefined(typeof(ButtonAttribute), true)) list.Add(m);
                }
                cur = cur.BaseType;
            }
            list.Reverse();
            var arr = list.ToArray();
            buttonCache[type] = arr;
            return arr;
        }

        public static float ButtonHeight(ButtonSizes size)
        {
            return size switch
            {
                ButtonSizes.Small => 20f,
                ButtonSizes.Medium => 28f,
                ButtonSizes.Large => 40f,
                ButtonSizes.Gigantic => 60f,
                _ => 28f
            };
        }

        public static float GetButtonStripHeight(Type type)
        {
            var btns = GetButtonMethods(type);
            if (btns == null || btns.Length == 0) return 0f;
            float h = 0f;
            for (int i = 0; i < btns.Length; i++)
                h += ButtonHeight(btns[i].GetCustomAttribute<ButtonAttribute>().Size) + 2f;
            return h;
        }

        public static float DrawButtonStrip(Rect area, Type type, object target, float y)
        {
            var btns = GetButtonMethods(type);
            if (btns == null) return y;
            for (int i = 0; i < btns.Length; i++)
            {
                var attr = btns[i].GetCustomAttribute<ButtonAttribute>();
                string name = attr != null && !string.IsNullOrEmpty(attr.Name)
                    ? attr.Name
                    : ObjectNames.NicifyVariableName(btns[i].Name);
                float hh = ButtonHeight(attr != null ? attr.Size : default);
                var r = new Rect(area.x, y, area.width, hh);
                if (GUI.Button(r, name))
                {
                    if (btns[i].IsStatic || target != null)
                        btns[i].Invoke(btns[i].IsStatic ? null : target, null);
                }
                y = r.yMax + 2f;
            }
            return y;
        }

        // ------------------------------------------------------------------
        // 构建
        // ------------------------------------------------------------------
        internal static List<RenderUnit> Build(List<SerializedProperty> fields,
            Dictionary<string, FieldInfo> nameFi, object rootTarget = null)
        {
            var slots = new List<FieldSlot>(fields.Count);
            for (int i = 0; i < fields.Count; i++)
            {
                var p = fields[i];
                FieldInfo fi = null;
                if (nameFi != null && nameFi.TryGetValue(p.name, out FieldInfo f)) fi = f;
                slots.Add(new FieldSlot { P = p, Fi = fi, Host = rootTarget });
            }
            return Build(slots);
        }

        internal static List<RenderUnit> Build(List<FieldSlot> slots)          
        {
            var visible = FilterVisible(slots);
            var sorted = SortByOrder(visible);
            var units = new List<RenderUnit>();
            var rendered = new HashSet<string>();

            for (int i = 0; i < sorted.Count; i++)
            {
                var slot = sorted[i];
                var g = GroupOf(slot.Fi);
                if (!g.IsGrouped)
                {
                    units.Add(new RenderUnit { Kind = UnitKind.Field, Slot = slot });
                    continue;
                }

                if (rendered.Contains(g.GroupName)) continue;
                rendered.Add(g.GroupName);

                if (g.Kind == UnitKind.TabGroup)
                {
                    var sections = new List<TabSection>();
                    for (int j = i; j < sorted.Count; j++)
                    {
                        var qg = GroupOf(sorted[j].Fi);
                        if (qg.Kind != UnitKind.TabGroup || qg.GroupName != g.GroupName) continue;
                        TabSection sec = null;
                        for (int k = 0; k < sections.Count; k++)
                        {
                            if (sections[k].TabName == qg.TabName) { sec = sections[k]; break; }
                        }
                        if (sec == null)
                        {
                            sec = new TabSection { TabName = qg.TabName };
                            sections.Add(sec);
                        }
                        sec.Fields.Add(sorted[j]);
                    }
                    units.Add(new RenderUnit { Kind = UnitKind.TabGroup, GroupName = g.GroupName, Sections = sections });
                }
                else
                {
                    var members = new List<FieldSlot>();
                    for (int j = i; j < sorted.Count; j++)
                    {
                        var qg = GroupOf(sorted[j].Fi);
                        if (qg.Kind == g.Kind && qg.GroupName == g.GroupName) members.Add(sorted[j]);
                    }
                    units.Add(new RenderUnit { Kind = g.Kind, GroupName = g.GroupName, Members = members });
                }
            }
            return units;
        }

        /// <summary>ShowIf/HideIf 为 false 的字段不参与分组与绘制（分组单元按可见字段组成）。</summary>
        private static List<FieldSlot> FilterVisible(List<FieldSlot> slots)
        {
            var list = new List<FieldSlot>(slots.Count);
            for (int i = 0; i < slots.Count; i++)
                if (IsSlotVisible(slots[i])) list.Add(slots[i]);
            return list;
        }

        /// <summary>[PropertyOrder] 稳定排序：有 Order 用 Order，无则按声明顺序递增。</summary>
        private static List<FieldSlot> SortByOrder(List<FieldSlot> slots)
        {
            int noOrder = 0;
            var keys = new int[slots.Count];
            for (int i = 0; i < slots.Count; i++)
            {
                var orderAttr = slots[i].Fi?.GetCustomAttribute<PropertyOrderAttribute>();
                keys[i] = orderAttr != null ? orderAttr.Order : noOrder++;
            }

            var pos = new int[slots.Count];
            for (int i = 0; i < slots.Count; i++) pos[i] = i;
            Array.Sort(pos, (a, b) =>
            {
                int c = keys[a].CompareTo(keys[b]);
                return c != 0 ? c : a.CompareTo(b);
            });

            var result = new List<FieldSlot>(slots.Count);
            for (int i = 0; i < pos.Length; i++) result.Add(slots[pos[i]]);
            return result;
        }

        private static FieldGroup GroupOf(FieldInfo fi)
        {
            var g = default(FieldGroup);
            if (fi == null) return g;

            var box = fi.GetCustomAttribute<BoxGroupAttribute>();
            if (box != null) return new FieldGroup { Kind = UnitKind.BoxGroup, GroupName = box.GroupName };

            var fold = fi.GetCustomAttribute<FoldoutGroupAttribute>();
            if (fold != null) return new FieldGroup { Kind = UnitKind.FoldoutGroup, GroupName = fold.GroupName };

            var tab = fi.GetCustomAttribute<TabGroupAttribute>();
            if (tab != null) return new FieldGroup { Kind = UnitKind.TabGroup, GroupName = tab.GroupName, TabName = tab.TabName };
            return g;
        }

        // ------------------------------------------------------------------
        // 高度 —— 与 Draw 严格对应
        // ------------------------------------------------------------------
        internal static float GetHeight(List<RenderUnit> units, float width)   
        {
            float h = 0f;
            for (int i = 0; i < units.Count; i++)
                h += GetUnitHeight(units[i], width);
            return h;
        }

        private static float GetUnitHeight(RenderUnit u, float width)
        {
            switch (u.Kind)
            {
                case UnitKind.Field:
                    return GetDecoratedFieldHeight(u.Slot, width);
                case UnitKind.TabGroup:
                {
                    float h = SMBPropertyLayout.LineHeight + SMBPropertyLayout.VSpacing;
                    var sec = GetActiveSection(u);
                    if (sec != null)
                        for (int i = 0; i < sec.Fields.Count; i++)
                            h += GetDecoratedFieldHeight(sec.Fields[i], width);
                    return h;
                }
                case UnitKind.FoldoutGroup:
                {
                    float h = SMBPropertyLayout.LineHeight + SMBPropertyLayout.VSpacing;
                    if (GetFoldoutExpanded(u))
                        for (int i = 0; i < u.Members.Count; i++)
                            h += GetDecoratedFieldHeight(u.Members[i], width);
                    return h;
                }
                case UnitKind.BoxGroup:
                {
                    float h = SMBPropertyLayout.LineHeight + SMBPropertyLayout.VSpacing;
                    for (int i = 0; i < u.Members.Count; i++)
                        h += GetDecoratedFieldHeight(u.Members[i], width);
                    return h + SMBPropertyLayout.VSpacing;
                }
            }
            return 0f;
        }

        // ------------------------------------------------------------------
        // 绘制
        // ------------------------------------------------------------------
        internal static float Draw(Rect area, List<RenderUnit> units, float y) 
        {
            for (int i = 0; i < units.Count; i++)
                y = DrawUnit(area, units[i], y);
            return y;
        }

        private static float DrawUnit(Rect area, RenderUnit u, float y)
        {
            switch (u.Kind)
            {
                case UnitKind.Field: return DrawDecoratedField(area, u.Slot, y);
                case UnitKind.TabGroup: return DrawTabGroup(area, u, y);
                case UnitKind.FoldoutGroup: return DrawFoldout(area, u, y);
                case UnitKind.BoxGroup: return DrawBox(area, u, y);
            }
            return y;
        }

        private static TabSection GetActiveSection(RenderUnit u)
        {
            if (u.Sections == null || u.Sections.Count == 0) return null;
            int idx = SessionState.GetInt("TaoTie_Tab_" + u.GroupName, 0);
            if (idx < 0 || idx >= u.Sections.Count) idx = 0;
            return u.Sections[idx];
        }

        private static int GetActiveIndex(RenderUnit u)
        {
            int idx = SessionState.GetInt("TaoTie_Tab_" + u.GroupName, 0);
            if (idx < 0 || idx >= u.Sections.Count) idx = 0;
            // 注意：不写回。同一 SessionState 键可被多个分组单元共享（如 StateData 与根字段
            // 共用 "_DefaultTabGroup"），这里写回会把其他单元的越界索引钳成 0，冲掉用户选择。
            return idx;
        }

        private static float DrawTabGroup(Rect area, RenderUnit u, float y)
        {
            int idx = GetActiveIndex(u);
            var labels = new string[u.Sections.Count];
            for (int i = 0; i < labels.Length; i++) labels[i] = u.Sections[i].TabName;

            var toolbarRect = new Rect(area.x, y, area.width, SMBPropertyLayout.LineHeight);
            int newIdx = GUI.Toolbar(toolbarRect, idx, labels);
            // 仅在用户实际点击切换时才写回共享键：未点击时 GUI.Toolbar 返回传入的 idx，
            // 若每帧 SetInt 会把其他共享同一 GroupName 的单元（页签数不同）的选择钳回 0。
            if (newIdx != idx)
                SessionState.SetInt("TaoTie_Tab_" + u.GroupName, newIdx);
            idx = newIdx;

            y += SMBPropertyLayout.LineHeight + SMBPropertyLayout.VSpacing;
            var sec = idx >= 0 && idx < u.Sections.Count ? u.Sections[idx] : null;
            if (sec != null)
            {
                var subArea = new Rect(area.x + SMBPropertyLayout.FoldIndent, area.y,
                    area.width - SMBPropertyLayout.FoldIndent, area.height);
                for (int i = 0; i < sec.Fields.Count; i++)
                    y = DrawDecoratedField(subArea, sec.Fields[i], y);
            }
            return y;
        }

        private static bool GetFoldoutExpanded(RenderUnit u)
        {
            return SessionState.GetBool("TaoTie_Fold_" + u.GroupName, true);
        }

        private static float DrawFoldout(Rect area, RenderUnit u, float y)
        {
            bool exp = GetFoldoutExpanded(u);
            var head = new Rect(area.x, y, area.width, SMBPropertyLayout.LineHeight);
            exp = EditorGUI.Foldout(head, exp, u.GroupName, true);
            SessionState.SetBool("TaoTie_Fold_" + u.GroupName, exp);

            y += SMBPropertyLayout.LineHeight + SMBPropertyLayout.VSpacing;
            if (exp)
            {
                var subArea = new Rect(area.x + SMBPropertyLayout.FoldIndent, area.y,
                    area.width - SMBPropertyLayout.FoldIndent, area.height);
                for (int i = 0; i < u.Members.Count; i++)
                    y = DrawDecoratedField(subArea, u.Members[i], y);
            }
            return y;
        }

        private static float DrawBox(Rect area, RenderUnit u, float y)
        {
            float contentH = SMBPropertyLayout.LineHeight + SMBPropertyLayout.VSpacing;
            for (int i = 0; i < u.Members.Count; i++)
                contentH += GetDecoratedFieldHeight(u.Members[i], area.width);
            contentH += SMBPropertyLayout.VSpacing;

            EditorGUI.DrawRect(new Rect(area.x, y, area.width, contentH), new Color(0.30f, 0.30f, 0.30f, 0.18f));
            var titleRect = new Rect(area.x + 3f, y, area.width - 6f, SMBPropertyLayout.LineHeight);
            EditorGUI.LabelField(titleRect, u.GroupName, EditorStyles.boldLabel);

            y += SMBPropertyLayout.LineHeight + SMBPropertyLayout.VSpacing;
            var subArea = new Rect(area.x + SMBPropertyLayout.FoldIndent, area.y,
                area.width - SMBPropertyLayout.FoldIndent, area.height);
            for (int i = 0; i < u.Members.Count; i++)
                y = DrawDecoratedField(subArea, u.Members[i], y);
            return y + SMBPropertyLayout.VSpacing;
        }

        // ------------------------------------------------------------------
        // managed reference（[SerializeReference]）子树 —— 反射驱动，确定性取子属性
        // ------------------------------------------------------------------
        public static float GetManagedChildrenHeight(SerializedProperty parent, float width)
        {
            if (parent.propertyType != SerializedPropertyType.ManagedReference) return 0f;
            var target = parent.managedReferenceValue;
            if (target == null) return 0f;
            float h = GetButtonStripHeight(target.GetType());
            h += GetHeight(Build(BuildManaged(parent)), width);
            return h;
        }

        public static float DrawManagedChildren(Rect area, SerializedProperty parent, float y, float indentX)
        {
            var subArea = new Rect(indentX, area.y, area.width - (indentX - area.x), area.height);
            var target = parent.managedReferenceValue;
            if (target == null) return y;
            y = DrawButtonStrip(subArea, target.GetType(), target, y);
            return Draw(subArea, Build(BuildManaged(parent)), y);
        }

        private static List<FieldSlot> BuildManaged(SerializedProperty parent)
        {
            var slots = new List<FieldSlot>();
            if (parent.propertyType != SerializedPropertyType.ManagedReference) return slots;
            var val = parent.managedReferenceValue;
            if (val == null) return slots;

            var map = new Dictionary<string, FieldInfo>();
            var names = new List<string>();
            Type t = val.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            while (t != null && t != typeof(object))
            {
                foreach (var f in t.GetFields(flags))
                {
                    if (f.IsStatic) continue;
                    bool serializable = f.IsPublic || f.IsDefined(typeof(SerializeField), true);
                    if (serializable && !map.ContainsKey(f.Name))
                    {
                        map[f.Name] = f;
                        names.Add(f.Name);
                    }
                }
                t = t.BaseType;
            }

            foreach (var name in names)
            {
                var child = parent.FindPropertyRelative(name);
                if (child != null)
                    slots.Add(new FieldSlot { P = child, Fi = map[name], Host = val });
            }
            return slots;
        }
    }
}