using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using TaoTie.Inspector;

namespace TaoTie.Inspector.Editor
{
    public class DrawBase
    {
        protected enum ValueDropdownFieldType
        {
            Normal,
            Array,
            IList
        }
        private List<ISort> sortTemp = new();
        private Dictionary<string, GroupItem> groupsTemp = new();
        private List<string> tabGroupKeys = new();

        // Per-table column widths — key: field name + object hash, value: column widths
        private readonly Dictionary<string, float[]> s_TableColumnWidths = new();
        private string s_DraggingTableKey;
        private int s_DraggingColumnIndex = -1;

        private static Type stringType = typeof(string);
        private static Type listType = typeof(List<>);
        private static Type dicType = typeof(Dictionary<,>);
        private static Type objectType = typeof(UnityEngine.Object);

        private static Dictionary<Type, ISort[]> sortsMap = new();
        private static Dictionary<MemberInfo, Attribute[]> memberAttrCache = new();
        private static Dictionary<(FieldInfo, int), object> onValueChangedTracker = new();

        /// <summary>Actual available width for layout (set by DrawObjectInspector from GUILayout context)</summary>
        public static float s_AvailableWidth = 0f;

        /// <summary>
        /// True when the current draw originates from a Graph (node view / details panel /
        /// graph inspector). When false (e.g. a plain TaoTieEditorWindow config editor),
        /// [DrawIgnore] attributes are ignored so all fields remain visible.
        /// </summary>
        public static bool s_IsGraphContext = false;

        /// <summary>Set the actual available width for box+grid layout calculations.</summary>
        public static void SetAvailableWidth(float width)
        {
            s_AvailableWidth = width;
        }

        private static T GetCachedAttr<T>(MemberInfo member) where T : Attribute
        {
            if (!memberAttrCache.TryGetValue(member, out var attrs))
            {
                attrs = BuildAttributeCache(member);
                memberAttrCache[member] = attrs;
            }
            for (int i = 0; i < attrs.Length; i++)
            {
                if (attrs[i] is T t) return t;
            }
            return null;
        }

        /// <summary>
        /// Build the attribute array for a member, merging native attributes
        /// with Odin-wrapped equivalents (when Odin is installed).
        /// </summary>
        private static Attribute[] BuildAttributeCache(MemberInfo member)
        {
            var native = (Attribute[])member.GetCustomAttributes(typeof(Attribute), true);
            var odin = OdinCompat.WrapOdinAttributes(member);
            if (odin == null || odin.Length == 0) return native;

            // Skip Odin-wrapped attributes whose TaoTie type already exists natively
            var existing = new HashSet<Type>();
            for (int i = 0; i < native.Length; i++)
                existing.Add(native[i].GetType());

            var additional = new List<Attribute>();
            for (int i = 0; i < odin.Length; i++)
            {
                if (!existing.Contains(odin[i].GetType()))
                    additional.Add(odin[i]);
            }

            if (additional.Count == 0) return native;

            var result = new Attribute[native.Length + additional.Count];
            Array.Copy(native, result, native.Length);
            additional.CopyTo(result, native.Length);
            return result;
        }

        protected HashSet<(FieldInfo, int)> foldoutState = new();
        protected HashSet<GroupItem> foldoutState2 = new();
        protected HashSet<string> drawnTabGroups = new();

        // Flag to control foldout x offset: Graph uses 4f, Mono/ScriptObject uses 14f
        protected static float s_FoldoutXOffset = 14f;
        public static void SetFoldoutXOffset(float offset) => s_FoldoutXOffset = offset;

        private Dictionary<(FieldInfo, int), HashSet<int>> listFoldoutState = new();
        private Dictionary<FieldInfo, object> dicInputKey = new();
        private static Dictionary<Type, string[]> enumDropDown = new();

        protected Dictionary<FieldInfo, ValueDropdownItem[]> valueDropdown = new();

        protected List<ValueDropdownItem> temp = new();
        private List<Type> temp2 = new();
        private List<string> temp3 = new();

        // Performance: max rows to render before requiring "Show All" expansion
        protected const int k_MaxVisibleRows = 50;
        // Cache for GUIContent to avoid per-frame allocations
        private static readonly Dictionary<string, GUIContent> s_GuiContentCache = new();

        public virtual void DrawObjectInspector(object obj, bool isDetails = false)
        {
            if (obj == null) return;
            if (obj.GetType().IsDefined(typeof(DrawWithUnityAttribute), true))
                return;

            // Graph context: use smaller foldout x offset
            s_FoldoutXOffset = 4f;

            // s_AvailableWidth should be set by the caller (e.g. GraphWindowDraw.DrawInspector)
            // via SetAvailableWidth before calling DrawObjectInspector.
            // If not set, fall back to a conservative default to avoid over-wide labels.
            if (s_AvailableWidth <= 0)
                s_AvailableWidth = Mathf.Min(EditorGUIUtility.currentViewWidth - 40f, 380f);

            // Set adaptive label width: Title:Content = 4:6 with minimum
            SetAdaptiveLabelWidth();

            var members = GetSortMember(obj);

            // Convert reflection members to GroupEntryData and use unified TaoTieGroupManager
            var groupEntries = new List<GroupEntryData>();
            foreach (var m in members)
            {
                if (m is MemberItem mi)
                {
                    groupEntries.Add(MemberItemToGroupData(mi, isDetails));
                }
                else if (m is GroupItem gi)
                {
                    // GroupItem from GetSortMember — already grouped, add as a batch
                    string boxName = null, foldoutName = null, tabGroupName = null, tabName = null;
                    if (gi.GroupKey != null && gi.GroupKey.StartsWith("Box:")) boxName = gi.GroupId;
                    else if (gi.GroupKey != null && gi.GroupKey.StartsWith("Fold:")) foldoutName = gi.GroupId;
                    else if (gi.GroupKey != null && gi.GroupKey.StartsWith("Tab:"))
                    {
                        var parts = gi.GroupKey.Substring(4).Split('/');
                        tabGroupName = parts[0];
                        tabName = parts.Length > 1 ? parts[1] : "";
                    }

                    foreach (var sub in gi.Members)
                    {
                        var data = MemberItemToGroupData(sub, isDetails);
                        // Override group info from the GroupItem
                        if (boxName != null) { data.BoxGroupName = boxName; data.FoldoutGroupName = null; data.TabGroupName = null; }
                        else if (foldoutName != null) { data.FoldoutGroupName = foldoutName; data.BoxGroupName = null; }
                        else if (tabGroupName != null) { data.TabGroupName = tabGroupName; data.TabName = tabName; data.BoxGroupName = null; data.FoldoutGroupName = null; }
                        groupEntries.Add(data);
                    }
                }
            }

            // Draw via unified TaoTieGroupManager
            var gm = new TaoTieGroupManager();
            gm.DrawGroupedEntries(groupEntries, data =>
            {
                DrawMemberInspector((System.Reflection.MemberInfo)data.UserData, obj, isDetails);
            });
            gm.Dispose();
        }

        /// <summary>
        /// Set adaptive label width based on current GUILayout width.
        /// Title:Content = 4:6 ratio with a minimum label width of 80px.
        /// </summary>
        protected static void SetAdaptiveLabelWidth(float minWidth = 80f)
        {
            float available = s_AvailableWidth > 0 ? s_AvailableWidth : EditorGUIUtility.currentViewWidth;
            // labelWidth = full width * 0.4, then add back indent so the label portion
            // stays constant and only the value area shrinks with indent.
            float indentSpace = EditorGUI.indentLevel * 15f;
            float ratioWidth = (available - indentSpace) * 0.4f + indentSpace;
            EditorGUIUtility.labelWidth = Mathf.Max(minWidth, ratioWidth);
        }
        

        private static GroupEntryData MemberItemToGroupData(MemberItem mi, bool isDetails)
        {
            var data = new GroupEntryData { UserData = mi.Member, SortOrder = mi.MinSort };
            if (mi.cachedAttributes == null) return data;

            string boxGroup = null, foldoutGroup = null, tabGroup = null, tabName = null, horizGroup = null;
            foreach (var attr in mi.cachedAttributes)
            {
                if (attr is BoxGroupAttribute bg) boxGroup = bg.GroupName;
                else if (attr is FoldoutGroupAttribute fg) foldoutGroup = fg.GroupName;
                else if (attr is TabGroupAttribute tg) { tabGroup = tg.GroupName; tabName = tg.TabName; }
                else if (attr is HorizontalGroupAttribute hg) horizGroup = hg.GroupName;
                else if (attr is DrawIgnoreAttribute dia)
                {
                    if (s_IsGraphContext && dia.Ignore == Ignore.All) data.Visible = false;
                }
            }

            // BoxGroup priority over FoldoutGroup
            data.BoxGroupName = boxGroup;
            data.FoldoutGroupName = (boxGroup == null) ? foldoutGroup : null;
            data.TabGroupName = (boxGroup == null && foldoutGroup == null) ? tabGroup : null;
            data.TabName = tabName;
            data.HorizontalGroupName = (boxGroup == null && foldoutGroup == null && tabGroup == null) ? horizGroup : null;
            return data;
        }

        protected virtual void DrawMemberInspector(MemberInfo member, object obj, bool isDetails = false)
        {
            if (!NeedShowInspector(member, obj, isDetails)) return;

            if (GetCachedAttr<InfoBoxAttribute>(member) is InfoBoxAttribute infoBox)
            {
                bool showInfo = true;
                if (!string.IsNullOrEmpty(infoBox.VisibleIf))
                    showInfo = CheckCondition(member, obj, new[] { infoBox.VisibleIf }, null);
                if (showInfo)
                {
                    InfoBoxDrawer.Draw(infoBox);
                }
            }

            if (GetCachedAttr<TitleAttribute>(member) is TitleAttribute titleAttr)
            {
                TitleDrawer.Draw(titleAttr);
            }

            if (GetCachedAttr<HeaderAttribute>(member) is HeaderAttribute header)
            {
                EditorGUILayout.LabelField(header.header);
            }

            if (GetCachedAttr<SpaceAttribute>(member) is SpaceAttribute space)
            {
                EditorGUILayout.Space(space.height);
            }

            if (GetCachedAttr<PropertySpaceAttribute>(member) is PropertySpaceAttribute propSpace)
            {
                if (propSpace.SpaceBefore > 0)
                {
                    GUILayout.Space(propSpace.SpaceBefore);
                }
            }

            // NotNull check
            if (GetCachedAttr<NotNullAttribute>(member) is NotNullAttribute notNullAttr
                && member is FieldInfo notNullField)
            {
                var notNullVal = notNullField.GetValue(obj);
                bool isNull = notNullVal == null || (notNullVal is UnityEngine.Object uo && uo == null);
                if (isNull)
                {
                    NotNullRenderer.Draw(notNullAttr.ErrorMessage, true);
                }
            }

            bool disable = false;
            if (GetCachedAttr<ReadOnlyAttribute>(member) is ReadOnlyAttribute ||
                GetCachedAttr<DisableInEditorModeAttribute>(member) is DisableInEditorModeAttribute)
            {
                disable = true;
                EditorGUI.BeginDisabledGroup(true);
            }
            else
            {
                // EnableIf / DisableIf (AllowMultiple — AND logic)
                var enableIfs = member.GetCustomAttributes<EnableIfAttribute>(true);
                foreach (var enableIfAttr in enableIfs)
                {
                    bool enabled = CheckCondition(member, obj, new[] { enableIfAttr.Condition }, enableIfAttr.Value);
                    if (!enabled)
                    {
                        disable = true;
                        EditorGUI.BeginDisabledGroup(true);
                        break;
                    }
                }
                if (!disable)
                {
                    var disableIfs = member.GetCustomAttributes<DisableIfAttribute>(true);
                    foreach (var disableIfAttr in disableIfs)
                    {
                        bool disabled = CheckCondition(member, obj, new[] { disableIfAttr.Condition }, disableIfAttr.Value);
                        if (disabled)
                        {
                            disable = true;
                            EditorGUI.BeginDisabledGroup(true);
                            break;
                        }
                    }
                }
            }

            if (member is FieldInfo field)
            {
                OnValueChangedAttribute attribute = null;
                object value = null;
                int collectionCount = -1;
                if (GetCachedAttr<OnValueChangedAttribute>(field) is OnValueChangedAttribute
                    valueChangedAttribute)
                {
                    value = field.GetValue(obj);
                    attribute = valueChangedAttribute;
                    // Track collection count for IList fields so mutations (add/remove) trigger callback
                    if (value is IList list)
                        collectionCount = list.Count;
                    // Detect async changes from previous frame (e.g. ValueDropdown menu callback)
                    var trackerKey = (field, obj.GetHashCode());
                    if (onValueChangedTracker.TryGetValue(trackerKey, out var prevValue))
                    {
                        onValueChangedTracker.Remove(trackerKey);
                        if (!IsEqual(value, prevValue))
                        {
                            // Value changed since last frame by an async callback
                            ReflectionMethodInvoker.InvokeNoArg(obj, field.DeclaringType, attribute.MethodName);
                            // Update value so the post-draw check doesn't re-trigger
                            value = field.GetValue(obj);
                        }
                    }
                }

                DrawFieldInspector(field, obj, isDetails);
                if (attribute != null)
                {
                    var newValue = field.GetValue(obj);
                    bool changed = !IsEqual(newValue, value);
                    // Also detect collection mutations (same reference, different count)
                    if (!changed && newValue is IList newList && collectionCount >= 0)
                        changed = newList.Count != collectionCount;
                    if (changed)
                    {
                        ReflectionMethodInvoker.InvokeNoArg(obj, field.DeclaringType, attribute.MethodName);
                    }
                    // For async callbacks (e.g. ValueDropdown menu), store the value so we can
                    // detect the change on the next frame when the menu callback fires.
                    onValueChangedTracker[(field, obj.GetHashCode())] = newValue;
                }

            }
            else if (member is MethodInfo method)
            {
                DrawMethodInspector(method, obj, isDetails);
            }

            if (disable)
            {
                EditorGUI.EndDisabledGroup();
            }
            else
            {
                if (GetCachedAttr<OnStateUpdateAttribute>(member) is OnStateUpdateAttribute
                    stateUpdateAttribute)
                {
                    ReflectionMethodInvoker.InvokeNoArg(obj, member.DeclaringType, stateUpdateAttribute.Action);
                }
            }

            if (GetCachedAttr<PropertySpaceAttribute>(member) is PropertySpaceAttribute propSpaceAfter)
            {
                if (propSpaceAfter.SpaceAfter > 0)
                {
                    GUILayout.Space(propSpaceAfter.SpaceAfter);
                }
            }

            return;
        }

        protected virtual bool NeedShowInspector(MemberInfo member, object obj, bool isDetails)
        {
            if (!SelectMemberInfo(member, obj, isDetails)) return false;
            // [DrawIgnore] only applies when drawing inside a Graph, not in a plain
            // EditorWindow (config editor) where every field should stay visible.
            if (s_IsGraphContext && GetCachedAttr<DrawIgnoreAttribute>(member) is DrawIgnoreAttribute ignoreAttribute)
            {
                if (ignoreAttribute.Ignore == Ignore.All) return false;
                if (ignoreAttribute.Ignore == Ignore.Details == isDetails) return false;
            }

            // AllowMultiple: all ShowIf must pass (AND)
            var showIfs = member.GetCustomAttributes<ShowIfAttribute>(true);
            foreach (var showIfAttribute in showIfs)
            {
                if (!CheckCondition(member, obj, new[] { showIfAttribute.Condition }, showIfAttribute.Value)) return false;
            }

            var hideIfs = member.GetCustomAttributes<HideIfAttribute>(true);
            foreach (var hideIfAttribute in hideIfs)
            {
                if (CheckCondition(member, obj, new[] { hideIfAttribute.Condition }, hideIfAttribute.Value)) return false;
            }
            return true;
        }
        #region DrawField

        protected virtual void DrawFieldInspector(FieldInfo field, object obj, bool isDetails = false)
        {
            object value = field.GetValue(obj);
            object newValue = value;

            if (field.FieldType.IsEnum)
            {
                DrawEnumFieldInspector(field, obj);
                return;
            }

            // TableList: draw array/list as a table (check before array/list/dict branches)
            if (field.GetCustomAttributes(typeof(TableListAttribute), true).Length > 0)
            {
                DrawTableListReflection(field, obj, value);
                return;
            }

            // TableMatrix: draw 2D array as a matrix
            if (field.GetCustomAttributes(typeof(TableMatrixAttribute), true).Length > 0
                && field.FieldType.IsArray && field.FieldType.GetArrayRank() == 2)
            {
                DrawTableMatrixReflection(field, obj, value as Array);
                return;
            }

            if (field.FieldType.IsArray)
            {
                if (value == null)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.PrefixLabel(GetShowName(field));
                    if (GUILayout.Button("New"))
                    {
                        value = Activator.CreateInstance(field.FieldType, 0);
                        field.SetValue(obj, value);
                    }

                    EditorGUILayout.EndHorizontal();
                    return;
                }
                else
                {
                    DrawFieldArrayInspector(field, obj, value as Array, isDetails);
                }

                return;
            }

            if (typeof(IList).IsAssignableFrom(field.FieldType) && field.FieldType.IsGenericType)
            {
                if (value == null)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.PrefixLabel(GetShowName(field));
                    if (GUILayout.Button("New"))
                    {
                        var newType = listType.MakeGenericType(field.FieldType.GenericTypeArguments);
                        value = Activator.CreateInstance(newType);
                        field.SetValue(obj, value);
                    }

                    EditorGUILayout.EndHorizontal();
                    return;
                }
                else
                {
                    DrawFieldListInspector(field, obj, value as IList, isDetails);
                }

                return;
            }

            if (typeof(IDictionary).IsAssignableFrom(field.FieldType) && field.FieldType.IsGenericType)
            {
                if (value == null)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.PrefixLabel(GetShowName(field));
                    if (GUILayout.Button("New"))
                    {
                        var newType = dicType.MakeGenericType(field.FieldType.GenericTypeArguments);
                        value = Activator.CreateInstance(newType);
                        field.SetValue(obj, value);
                    }

                    EditorGUILayout.EndHorizontal();
                    return;
                }
                else
                {
                    DrawFieldDictionaryInspector(field, obj, value as IDictionary, isDetails);
                }

                return;
            }
            // After List handling
            if (field.GetCustomAttribute(typeof(ValueDropdownAttribute)) is ValueDropdownAttribute
                valueDropdownAttribute)
            {
                bool remove = false;
                DrawValueDropdownFieldInspector(field.FieldType, obj, valueDropdownAttribute, value,
                    ValueDropdownFieldType.Normal,ref remove,field);
                return;
            }
            // Display field name and value
            if(DrawNormalField(field.FieldType, GetShowName(field, value), ref newValue, isDetails, field))
            {
                if (!IsEqual(value, newValue))
                {
                    field.SetValue(obj, newValue);
                }

                return;
            }

            if (field.FieldType.IsClass
                && field.FieldType != stringType
                && !field.FieldType.IsGenericType
                && !field.FieldType.IsArray
                && !objectType.IsAssignableFrom(field.FieldType))
            {
                if (value == null)
                {
                    var types = GetSubClassList(field, obj, field.FieldType, out var names);
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.PrefixLabel(GetShowName(field));
                    var index = EditorGUILayout.Popup(-1, names);
                    EditorGUILayout.EndHorizontal();
                    if (index >= 0)
                    {
                        value = Activator.CreateInstance(types[index]);
                        field.SetValue(obj, value);
                    }

                    return;
                }

                // Get display name for the type (LabelText if present, otherwise type name)
                var valueType = value.GetType();
                var typeLabelAttr = valueType.GetCustomAttributes(typeof(LabelTextAttribute), false);
                string typeDisplayName = LabelResolver.GetTypeLabel(valueType);

                // Use instance field for foldout state — DrawBase is reused across frames via DrawReflectionProperty
                bool foldout = foldoutState.Contains((field, value?.GetHashCode() ?? 0));
                var foldoutLabel = GetShowName(field, value);
                float labelW = EditorStyles.foldout.CalcSize(foldoutLabel).x + 18f;

                Rect foldRect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
                float buttonW = 55f;
                float buttonX = foldRect.xMax - buttonW - 2f;
                Rect buttonRect = new Rect(buttonX, foldRect.y, buttonW, foldRect.height);
                Rect actualFoldRect = new Rect(foldRect.x, foldRect.y, buttonX - foldRect.x - 4f, foldRect.height);
                Rect typeRect = new Rect(foldRect.x + labelW, foldRect.y,
                    buttonX - (foldRect.x + labelW) - 4f, foldRect.height);

                // Draw button first, then foldout in non-overlapping rect
                bool setNullClicked = false;
                if (field.GetCustomAttribute(typeof(HideReferenceObjectPickerAttribute)) == null)
                {
                    setNullClicked = GUI.Button(buttonRect, "SetNull");
                }
                // Truncate foldout label if it would overlap the type label or SetNull button
                float maxFoldLabelW = actualFoldRect.width - 15f;
                string truncatedFoldText = TruncateLabel(foldoutLabel.text, maxFoldLabelW);
                foldout = EditorGUI.Foldout(actualFoldRect, foldout, truncatedFoldText, true);
                // Truncate type display name if it would overlap the SetNull button
                if (typeRect.width > 0)
                {
                    typeDisplayName = TruncateLabel(typeDisplayName, typeRect.width);
                }
                EditorGUI.LabelField(typeRect, typeDisplayName, EditorStyles.boldLabel);

                if (setNullClicked)
                {
                    field.SetValue(obj, null);
                }
                EditorGUILayout.Space(1);
                if (foldout)
                {
                    EditorGUI.indentLevel++;
                    DrawObjectInspector(value, isDetails);
                    EditorGUI.indentLevel--;
                    foldoutState.Add((field, value?.GetHashCode() ?? 0));
                }
                else
                {
                    foldoutState.Remove((field, value?.GetHashCode() ?? 0));
                }

                return;
            }

            return;
        }

        protected virtual bool DrawNormalField(Type type, GUIContent showName, ref object value,
            bool isDetails = false, FieldInfo field = null, params GUILayoutOption[] options)
        {
            // Display field name and value
            if (type == typeof(string))
            {
                if (showName == null) value = EditorGUILayout.TextField((string) value);
                else value = EditorGUILayout.TextField(showName, (string) value, options);
            }
            else if (type == typeof(int))
            {
                int val = 0;
                if (field?.GetCustomAttribute(typeof(RangeAttribute)) is RangeAttribute rangeAttr)
                {
                    if (showName == null)
                        val = EditorGUILayout.IntSlider((int) value, Mathf.CeilToInt(rangeAttr.min),
                            Mathf.FloorToInt(rangeAttr.max));
                    else
                        val = EditorGUILayout.IntSlider(showName, (int) value, Mathf.CeilToInt(rangeAttr.min),
                            Mathf.FloorToInt(rangeAttr.max));
                }
                else if (field?.GetCustomAttribute(typeof(PropertyRangeAttribute)) is PropertyRangeAttribute propRangeAttr)
                {
                    if (showName == null)
                        val = EditorGUILayout.IntSlider((int) value, (int) propRangeAttr.Min, (int) propRangeAttr.Max);
                    else
                        val = EditorGUILayout.IntSlider(showName, (int) value, (int) propRangeAttr.Min, (int) propRangeAttr.Max);
                }
                else
                {
                    if (showName == null) val = EditorGUILayout.IntField((int) value);
                    else val = EditorGUILayout.IntField(showName, (int) value, options);
                }

                if (field?.GetCustomAttribute(typeof(MinAttribute)) is MinAttribute minAttr && val < minAttr.min)
                {
                    val = Mathf.CeilToInt(minAttr.min);
                }

                if (field?.GetCustomAttribute(typeof(MinValueAttribute)) is MinValueAttribute minVAttr &&
                    val < minVAttr.MinValue)
                {
                    if (minVAttr.MinValue < int.MinValue)
                        val = int.MinValue;
                    else
                        val = (int) Math.Ceiling(minVAttr.MinValue);
                }

                if (field?.GetCustomAttribute(typeof(MaxValueAttribute)) is MaxValueAttribute maxVAttr &&
                    val > maxVAttr.MaxValue)
                {
                    if (maxVAttr.MaxValue > int.MaxValue)
                        val = int.MaxValue;
                    else
                        val = (int) Math.Floor(maxVAttr.MaxValue);
                }

                value = val;
            }
            else if (type == typeof(long))
            {
                long val;
                if (showName == null) val = EditorGUILayout.LongField((long) value);
                else val = EditorGUILayout.LongField(showName, (long) value);
                if (field?.GetCustomAttribute(typeof(RangeAttribute)) is RangeAttribute rangeAttr)
                {
                    if (val < rangeAttr.min) val = (long) Mathf.Ceil(rangeAttr.min);
                    else if (val > rangeAttr.max) val = (long) Mathf.Floor(rangeAttr.max);
                }

                if (field?.GetCustomAttribute(typeof(MinAttribute)) is MinAttribute minAttr && val < minAttr.min)
                {
                    val = (long) Mathf.Ceil(minAttr.min);
                }

                if (field?.GetCustomAttribute(typeof(MinValueAttribute)) is MinValueAttribute minVAttr &&
                    val < minVAttr.MinValue)
                {
                    if (minVAttr.MinValue < long.MinValue)
                        val = long.MinValue;
                    else
                        val = (long) Math.Ceiling(minVAttr.MinValue);
                }

                if (field?.GetCustomAttribute(typeof(MaxValueAttribute)) is MaxValueAttribute maxVAttr &&
                    val > maxVAttr.MaxValue)
                {
                    if (maxVAttr.MaxValue > long.MaxValue)
                        val = long.MaxValue;
                    else
                        val = (long) Math.Floor(maxVAttr.MaxValue);
                }

                value = val;
            }
            else if (type == typeof(ulong))
            {
                long val;
                if (showName == null) val = EditorGUILayout.LongField((long) (ulong) value);
                else val = EditorGUILayout.LongField(showName, (long) (ulong) value);
                if (field?.GetCustomAttribute(typeof(RangeAttribute)) is RangeAttribute rangeAttr)
                {
                    if (val < rangeAttr.min) val = (long) Mathf.Ceil(rangeAttr.min);
                    else if (val > rangeAttr.max) val = (long) Mathf.Floor(rangeAttr.max);
                }

                if (field?.GetCustomAttribute(typeof(MinAttribute)) is MinAttribute minAttr && val < minAttr.min)
                {
                    val = (long) Mathf.Ceil(minAttr.min);
                }

                if (field?.GetCustomAttribute(typeof(MinValueAttribute)) is MinValueAttribute minVAttr &&
                    val < minVAttr.MinValue)
                {
                    if (minVAttr.MinValue < long.MinValue)
                        val = long.MinValue;
                    else
                        val = (long) Math.Ceiling(minVAttr.MinValue);
                }

                if (field?.GetCustomAttribute(typeof(MaxValueAttribute)) is MaxValueAttribute maxVAttr &&
                    val > maxVAttr.MaxValue)
                {
                    if (maxVAttr.MaxValue > long.MaxValue)
                        val = long.MaxValue;
                    else
                        val = (long) Math.Floor(maxVAttr.MaxValue);
                }

                value = (ulong) val;
            }
            else if (type == typeof(float))
            {
                float val = 0;
                if (field?.GetCustomAttribute(typeof(RangeAttribute)) is RangeAttribute rangeAttr)
                {
                    if (showName == null) val = EditorGUILayout.Slider((float) value, rangeAttr.min, rangeAttr.max);
                    else val = EditorGUILayout.Slider(showName, (float) value, rangeAttr.min, rangeAttr.max);
                }
                else if (field?.GetCustomAttribute(typeof(PropertyRangeAttribute)) is PropertyRangeAttribute propRangeAttr)
                {
                    if (showName == null) val = EditorGUILayout.Slider((float) value, (float) propRangeAttr.Min, (float) propRangeAttr.Max);
                    else val = EditorGUILayout.Slider(showName, (float) value, (float) propRangeAttr.Min, (float) propRangeAttr.Max);
                }
                else
                {
                    if (showName == null) val = EditorGUILayout.FloatField(showName, (float) value, options);
                    else val = EditorGUILayout.FloatField(showName, (float) value, options);
                }

                if (field?.GetCustomAttribute(typeof(MinAttribute)) is MinAttribute minAttr && val < minAttr.min)
                {
                    val = minAttr.min;
                }

                if (field?.GetCustomAttribute(typeof(MinValueAttribute)) is MinValueAttribute minVAttr &&
                    val < minVAttr.MinValue)
                {
                    if (minVAttr.MinValue < float.MinValue)
                        val = float.MinValue;
                    else
                        val = (float) Math.Ceiling(minVAttr.MinValue);
                }

                if (field?.GetCustomAttribute(typeof(MaxValueAttribute)) is MaxValueAttribute maxVAttr &&
                    val > maxVAttr.MaxValue)
                {
                    if (maxVAttr.MaxValue > float.MaxValue)
                        val = float.MaxValue;
                    else
                        val = (float) Math.Floor(maxVAttr.MaxValue);
                }

                value = val;
            }
            else if (type == typeof(bool))
            {
                if (showName == null) value = EditorGUILayout.Toggle((bool) value);
                else value = EditorGUILayout.Toggle(showName, (bool) value, options);
            }
            else if (type.IsEnum)
            {
                if (field?.GetCustomAttribute(typeof(EnumToggleButtonsAttribute)) is EnumToggleButtonsAttribute)
                {
                    value = DrawEnumToggleButtons(type, showName, value);
                }
                else
                {
                    value = EditorGUILayout.EnumPopup(showName, (Enum) value, options);
                }
            }
            else if (type == typeof(Vector2))
            {
                value = EditorGUILayout.Vector2Field(showName, (Vector2) value);
            }
            else if (type == typeof(Vector3))
            {
                value = EditorGUILayout.Vector3Field(showName, (Vector3) value);
            }
            else if (type == typeof(Vector4))
            {
                value = EditorGUILayout.Vector4Field(showName, (Vector4) value);
            }
            else if (type == typeof(Rect))
            {
                if (showName == null) value = EditorGUILayout.RectField((Rect) value);
                else value = EditorGUILayout.RectField(showName, (Rect) value);
            }
            else if (type == typeof(Color))
            {
                if (showName == null) value = EditorGUILayout.ColorField((Color) value);
                else value = EditorGUILayout.ColorField(showName, (Color) value);
            }
            else if (objectType.IsAssignableFrom(type)
                     && !(field?.GetCustomAttribute(typeof(NotAssetsAttribute)) is NotAssetsAttribute))
            {
                UnityEngine.Object newObj;
                if (showName == null) newObj = EditorGUILayout.ObjectField((UnityEngine.Object) value, type, false);
                else newObj = EditorGUILayout.ObjectField(showName, (UnityEngine.Object) value, type, false);
                value = newObj;
            }
            else if (type == typeof(AnimationCurve))
            {
                AnimationCurve res;
                if (showName == null) res = EditorGUILayout.CurveField((AnimationCurve) value);
                else res = EditorGUILayout.CurveField(showName, (AnimationCurve) value);
                if (res == null) res = new AnimationCurve();
                value = res;
            }
            else
            {
                return false;
            }

            return true;
        }

        protected virtual void DrawTableListReflection(FieldInfo field, object obj, object value)
        {
            bool tableChanged = false;
            var showName = GetShowName(field, value);
            string title = showName?.text ?? ObjectNames.NicifyVariableName(field.Name);

            int count = 0;
            IList list = null;
            if (value is Array arr) { count = arr.Length; list = arr; }
            else if (value is IList il) { count = il.Count; list = il; }

            // Collect column names from first non-null element
            var columnNames = new List<string>();
            var columnFields = new List<FieldInfo>();
            Type itemType = null;
            if (list != null && count > 0)
            {
                for (int i = 0; i < count; i++)
                {
                    if (list[i] != null)
                    {
                        itemType = list[i].GetType();
                        var fields = itemType.GetFields(BindingFlags.Public | BindingFlags.Instance);
                        foreach (var f in fields)
                        {
                            columnNames.Add(f.Name);
                            columnFields.Add(f);
                        }
                        break;
                    }
                }
            }

            int colCount = columnNames.Count;
            float indexColW = 28f;
            float deleteColW = 22f;
            float dragHandleW = 6f;

            string tableKey = "TL3_" + field.Name + "_" + obj.GetHashCode() + "_" + colCount;

            // Get or init column widths — initialization deferred to header drawing where actual width is known
            float[] colWidths = null;
            if (s_TableColumnWidths.TryGetValue(tableKey, out var cached) && cached.Length == colCount)
                colWidths = cached;

            // Background box
            var boxStyle = new GUIStyle(GUI.skin.box) { padding = new RectOffset(2, 2, 2, 2) };
            float tlIndentOffset = 15f * EditorGUI.indentLevel;
            int tlOldIndent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            EditorGUILayout.BeginHorizontal();
            if (tlIndentOffset > 0) GUILayout.Space(tlIndentOffset);
            EditorGUILayout.BeginVertical(boxStyle);

            // Title bar — rect-based gap-fill
            string tlFoldKey = "TaoTie_Fold_TL_" + field.Name + "_" + obj.GetHashCode();
            bool tlFoldout = SessionState.GetBool(tlFoldKey, false);
            Rect tlTitleRect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
            EditorGUI.DrawRect(tlTitleRect, new Color(0.3f, 0.3f, 0.3f, 0.2f));
            float tlTbX = tlTitleRect.x + 4f;
            float tlMinusX = tlTitleRect.xMax - 24f - 2f;
            float tlPlusX = tlMinusX - 24f - 2f;
            string tlCountText = $"({count})";
            var tlCountContent = new GUIContent(tlCountText);
            float tlCountW = EditorStyles.miniLabel.CalcSize(tlCountContent).x + 8f;
            Rect tlCountRect = new Rect(tlPlusX - tlCountW - 4f, tlTitleRect.y, tlCountW, tlTitleRect.height);
            Rect tlFoldRect = new Rect(tlTbX, tlTitleRect.y, tlCountRect.x - tlTbX - 4f, tlTitleRect.height);
            // Foldout with title text
            tlFoldout = EditorGUI.Foldout(tlFoldRect, tlFoldout, title, true);
            SessionState.SetBool(tlFoldKey, tlFoldout);
            EditorGUI.LabelField(tlCountRect, tlCountContent, EditorStyles.miniLabel);
            if (GUI.Button(new Rect(tlPlusX, tlTitleRect.y, 24f, tlTitleRect.height), "+", EditorStyles.toolbarButton))
            {
                if (field.FieldType.IsArray)
                {
                    var elemType = field.FieldType.GetElementType();
                    var newArr = Array.CreateInstance(elemType, count + 1);
                    if (count > 0) Array.Copy((Array)value, newArr, count);
                    field.SetValue(obj, newArr);
                }
                else
                {
                    var genericArgs = field.FieldType.GetGenericArguments();
                    ((IList)value).Add(genericArgs[0].IsValueType ? Activator.CreateInstance(genericArgs[0]) : null);
                    field.SetValue(obj, value);
                }
                tableChanged = true;
            }
            if (GUI.Button(new Rect(tlMinusX, tlTitleRect.y, 24f, tlTitleRect.height), "-", EditorStyles.toolbarButton))
            {
                if (count > 0)
                {
                    if (field.FieldType.IsArray)
                    {
                        var elemType = field.FieldType.GetElementType();
                        var newArr = Array.CreateInstance(elemType, count - 1);
                        if (count > 1) Array.Copy((Array)value, newArr, count - 1);
                        field.SetValue(obj, newArr);
                    }
                    else
                    {
                        ((IList)value).RemoveAt(count - 1);
                        field.SetValue(obj, value);
                    }
                }
                tableChanged = true;
            }

            // Column headers with drag handles
            if (tlFoldout && colCount > 0)
            {
                var headerRect = EditorGUILayout.GetControlRect(false, 20f);
                EditorGUI.DrawRect(headerRect, new Color(0.3f, 0.3f, 0.3f, 0.4f));

                // Initialize column widths from actual headerRect width (equal distribution)
                // Only cache when headerRect width is valid (skip first layout pass with width=1)
                if (colWidths == null)
                {
                    float contentW = headerRect.width - indexColW - deleteColW;
                    float eachW = Mathf.Max(50f, contentW / colCount);
                    colWidths = new float[colCount];
                    for (int i = 0; i < colCount; i++) colWidths[i] = eachW;
                    if (headerRect.width > 50f)
                        s_TableColumnWidths[tableKey] = colWidths;
                }

                // Use GUIUtility.hotControl for reliable drag tracking
                int dragCtrlId = GUIUtility.GetControlID(tableKey.GetHashCode(), FocusType.Passive);
                var ev = Event.current;

                // Render columns and detect drag handle clicks
                float hx = headerRect.x;
                EditorGUI.LabelField(new Rect(hx, headerRect.y, indexColW, headerRect.height), "#", EditorStyles.boldLabel);
                hx += indexColW;
                for (int c = 0; c < colCount; c++)
                {
                    float cw = colWidths[c];
                    if (c == colCount - 1)
                    {
                        float rightEdge = headerRect.x + headerRect.width - deleteColW;
                        cw = Mathf.Max(30f, rightEdge - hx);
                    }
                    EditorGUI.LabelField(new Rect(hx, headerRect.y, cw - dragHandleW, headerRect.height),
                        ObjectNames.NicifyVariableName(columnNames[c]), EditorStyles.boldLabel);
                    // Drag handle — skip for last column
                    if (c < colCount - 1)
                    {
                        Rect handleRect = new Rect(hx + cw - dragHandleW, headerRect.y, dragHandleW, headerRect.height);
                        EditorGUI.DrawRect(handleRect, new Color(0.5f, 0.5f, 0.5f, 0.3f));
                        EditorGUIUtility.AddCursorRect(handleRect, MouseCursor.ResizeHorizontal);
                        // Start drag
                        if (ev.GetTypeForControl(dragCtrlId) == EventType.MouseDown && handleRect.Contains(ev.mousePosition))
                        {
                            GUIUtility.hotControl = dragCtrlId;
                            s_DraggingTableKey = tableKey;
                            s_DraggingColumnIndex = c;
                            ev.Use();
                        }
                    }
                    hx += cw;
                }

                // Process drag
                if (GUIUtility.hotControl == dragCtrlId && s_DraggingTableKey == tableKey)
                {
                    int dragIdx = s_DraggingColumnIndex;
                    if (ev.GetTypeForControl(dragCtrlId) == EventType.MouseDrag && dragIdx >= 0 && dragIdx < colCount)
                    {
                        float delta = ev.delta.x;
                        float curW = colWidths[dragIdx];
                        float newWidth = curW + delta;
                        if (newWidth < 30f) newWidth = 30f;
                        float actualDelta = newWidth - curW;
                        if (actualDelta != 0f)
                        {
                            colWidths[dragIdx] = newWidth;
                            int nextIdx = dragIdx + 1;
                            if (nextIdx < colCount)
                            {
                                float nextNew = colWidths[nextIdx] - actualDelta;
                                if (nextNew < 30f)
                                {
                                    actualDelta -= 30f - nextNew;
                                    nextNew = 30f;
                                    colWidths[dragIdx] = Mathf.Max(30f, curW + actualDelta);
                                }
                                colWidths[nextIdx] = nextNew;
                            }
                        }
                        ev.Use();
                    }
                    if (ev.GetTypeForControl(dragCtrlId) == EventType.MouseUp)
                    {
                        GUIUtility.hotControl = 0;
                        s_DraggingTableKey = null;
                        s_DraggingColumnIndex = -1;
                        ev.Use();
                    }
                }

                // Header bottom border
                EditorGUI.DrawRect(new Rect(headerRect.x, headerRect.yMax - 1, headerRect.width, 1),
                    new Color(0.5f, 0.5f, 0.5f, 0.6f));
            }

            // Data rows
            if (tlFoldout && list != null && count > 0)
            {
                string tlShowAllKey = "TaoTie_ShowAll_TL_" + field.Name + "_" + obj.GetHashCode();
                bool tlShowAll = SessionState.GetBool(tlShowAllKey, false);
                int tlVisibleCount = tlShowAll ? count : Mathf.Min(count, k_MaxVisibleRows);

                for (int i = 0; i < tlVisibleCount; i++)
                {
                    // Guard against mid-frame collection mutation (e.g. delete button)
                    if (i >= list.Count) break;
                    var item = list[i];
                    var rowRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight + 2f);
                    // Alternating row background
                    if (i % 2 == 1)
                        EditorGUI.DrawRect(rowRect, new Color(0.5f, 0.5f, 0.5f, 0.1f));

                    float dx = rowRect.x;
                    // Index
                    EditorGUI.LabelField(new Rect(dx, rowRect.y, indexColW, rowRect.height), i.ToString());
                    dx += indexColW;

                    if (item == null)
                    {
                        if (itemType != null)
                        {
                            var types = TypeHelper.GetSubClassList(null, itemType, out var names);
                            int selIdx = EditorGUI.Popup(new Rect(dx, rowRect.y, colWidths.Length > 0 ? colWidths[0] : 50f, rowRect.height), -1, names);
                            if (selIdx >= 0)
                            {
                                item = Activator.CreateInstance(types[selIdx]);
                                list[i] = item;
                            }
                        }
                    }
                    else if (colCount > 0)
                    {
                        for (int c = 0; c < colCount; c++)
                        {
                            float cw = colWidths[c];
                            // Last column: fill remaining space (display only)
                            if (c == colCount - 1)
                            {
                                float rightEdge = rowRect.x + rowRect.width - deleteColW;
                                cw = Mathf.Max(30f, rightEdge - dx);
                            }
                            var f = columnFields[c];
                            var fieldValue = f.GetValue(item);
                            object newVal = fieldValue;
                            Rect colRect = new Rect(dx, rowRect.y, cw, rowRect.height);
                            EditorGUI.BeginChangeCheck();
                            DrawNormalFieldRect(f.FieldType, colRect, GUIContent.none, ref newVal, false, f);
                            if (EditorGUI.EndChangeCheck())
                                tableChanged = true;
                            if (!IsEqual(newVal, fieldValue))
                                f.SetValue(item, newVal);
                            dx += cw;
                        }
                    }
                    else
                    {
                        object val = item;
                        Rect primRect = new Rect(dx, rowRect.y, colWidths.Length > 0 ? colWidths[0] : 50f, rowRect.height);
                        EditorGUI.BeginChangeCheck();
                        DrawNormalFieldRect(itemType, primRect, GUIContent.none, ref val, false, null);
                        if (EditorGUI.EndChangeCheck())
                            tableChanged = true;
                        if (!IsEqual(val, item))
                            list[i] = val;
                    }

                    // Delete button anchored to right edge
                    Rect delRect = new Rect(rowRect.xMax - deleteColW - 2f, rowRect.y, deleteColW, rowRect.height);
                    if (GUI.Button(delRect, "×"))
                    {
                        if (field.FieldType.IsArray)
                        {
                            var elemType = field.FieldType.GetElementType();
                            var newArr = Array.CreateInstance(elemType, count - 1);
                            int j = 0;
                            for (int k = 0; k < count; k++)
                            {
                                if (k == i) continue;
                                newArr.SetValue(list[k], j++);
                            }
                            field.SetValue(obj, newArr);
                        }
                        else
                        {
                            ((IList)value).RemoveAt(i);
                            field.SetValue(obj, value);
                        }
                        tableChanged = true;
                        // Abort the current GUI pass — the collection has changed mid-layout,
                        // continuing would cause GUILayout mismatch / index out of range.
                        GUI.changed = true;
                        GUIUtility.ExitGUI();
                    }

                    // Row bottom grid line
                    EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.yMax - 1, rowRect.width, 1),
                        new Color(0.3f, 0.3f, 0.3f, 0.3f));
                }

                // Show All / Show Less toggle
                if (count > k_MaxVisibleRows)
                {
                    if (GUILayout.Button(tlShowAll ? $"Show Less ({k_MaxVisibleRows})" : $"Show All ({count})", EditorStyles.miniButton))
                    {
                        SessionState.SetBool(tlShowAllKey, !tlShowAll);
                    }
                }
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            EditorGUI.indentLevel = tlOldIndent;
            EditorGUILayout.Space(2);

            if (tableChanged &&
                field.GetCustomAttribute(typeof(OnCollectionChangedAttribute)) is OnCollectionChangedAttribute
                    collectionChangedAttribute)
            {
                ReflectionMethodInvoker.InvokeNoArg(obj, field.DeclaringType, collectionChangedAttribute.After);
            }
            return;
        }

        /// <summary>
        /// Draw a corner label with word-wrap, ellipsis truncation, and tooltip on hover.
        /// </summary>
        private static void DrawCornerLabel(Rect rect, string text, TextAnchor anchor)
        {
            var style = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = anchor,
                wordWrap = true,
                clipping = TextClipping.Clip
            };

            // Check if text fits; if not, truncate and add tooltip
            var content = new GUIContent(text);
            var size = style.CalcSize(content);
            bool fits = size.x <= rect.width || size.y <= rect.height;

            if (fits)
            {
                EditorGUI.LabelField(rect, content, style);
            }
            else
            {
                // Truncate with "..."
                string truncated = text;
                var charWidth = style.CalcSize(new GUIContent("M")).x;
                int maxChars = Mathf.Max(1, Mathf.FloorToInt(rect.width / charWidth) - 1);
                if (text.Length > maxChars)
                    truncated = text.Substring(0, maxChars) + "...";
                EditorGUI.LabelField(rect, new GUIContent(truncated, text), style);
            }

            // Tooltip on hover
            if (rect.Contains(Event.current.mousePosition))
            {
                GUI.Label(rect, new GUIContent("", text), style);
                EditorGUIUtility.AddCursorRect(rect, MouseCursor.Arrow);
            }
        }

        protected virtual void DrawTableMatrixReflection(FieldInfo field, object obj, Array matrix)
        {
            var attr = field.GetCustomAttribute<TableMatrixAttribute>();
            if (attr == null) return;

            var elementType = field.FieldType.GetElementType();
            int rows = matrix?.GetLength(0) ?? 0;
            int cols = matrix?.GetLength(1) ?? 0;
            bool changed = false;

            // Resolve label method
            MethodInfo labelsMethod = null;
            if (!string.IsNullOrEmpty(attr.Labels))
            {
                const BindingFlags bf = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
                labelsMethod = (field.DeclaringType ?? obj.GetType()).GetMethod(attr.Labels, bf);
            }

            // Resolve custom draw method
            MethodInfo drawMethod = null;
            if (!string.IsNullOrEmpty(attr.DrawElementMethod))
            {
                const BindingFlags bf = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
                drawMethod = (field.DeclaringType ?? obj.GetType()).GetMethod(attr.DrawElementMethod, bf);
            }

            var boxStyle = new GUIStyle(GUI.skin.box) { padding = new RectOffset(2, 2, 2, 2) };
            float indentOffset = 15f * EditorGUI.indentLevel;
            int oldIndent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            EditorGUILayout.BeginHorizontal();
            if (indentOffset > 0) GUILayout.Space(indentOffset);
            EditorGUILayout.BeginVertical(boxStyle);

            // Title bar with +/- for rows and cols
            string foldKey = "TaoTie_Fold_TM_" + field.Name + "_" + obj.GetHashCode();
            bool foldout = SessionState.GetBool(foldKey, true);
            Rect titleRect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
            EditorGUI.DrawRect(titleRect, new Color(0.3f, 0.3f, 0.3f, 0.2f));
            float tx = titleRect.x + 4f;
            float colMinusX = titleRect.xMax - 24f - 2f;
            float colPlusX = colMinusX - 24f - 2f;
            float rowMinusX = colPlusX - 24f - 2f;
            float rowPlusX = rowMinusX - 24f - 2f;
            string sizeText = $"{rows}×{cols}";
            var sizeContent = new GUIContent(sizeText);
            float sizeW = EditorStyles.miniLabel.CalcSize(sizeContent).x + 8f;
            Rect sizeRect = new Rect(rowPlusX - sizeW - 4f, titleRect.y, sizeW, titleRect.height);
            Rect foldRect = new Rect(tx, titleRect.y, sizeRect.x - tx - 4f, titleRect.height);
            var tmShowName = GetShowName(field);
            string title = tmShowName?.text ?? ObjectNames.NicifyVariableName(field.Name);
            foldout = EditorGUI.Foldout(foldRect, foldout, new GUIContent(title, tmShowName?.tooltip), true);
            SessionState.SetBool(foldKey, foldout);
            EditorGUI.LabelField(sizeRect, sizeContent, EditorStyles.miniLabel);

            if (!attr.IsReadOnly)
            {
                if (GUI.Button(new Rect(rowPlusX, titleRect.y, 24f, titleRect.height), "R+", EditorStyles.toolbarButton))
                {
                    var nm = Array.CreateInstance(elementType, rows + 1, cols);
                    if (matrix != null)
                        for (int r = 0; r < rows; r++)
                            for (int c = 0; c < cols; c++)
                                nm.SetValue(matrix.GetValue(r, c), r, c);
                    field.SetValue(obj, nm);
                    matrix = nm;
                    rows++;
                    changed = true;
                }
                if (GUI.Button(new Rect(rowMinusX, titleRect.y, 24f, titleRect.height), "R−", EditorStyles.toolbarButton))
                {
                    if (rows > 0)
                    {
                        var nm = Array.CreateInstance(elementType, rows - 1, cols);
                        for (int r = 0; r < rows - 1; r++)
                            for (int c = 0; c < cols; c++)
                                nm.SetValue(matrix.GetValue(r, c), r, c);
                        field.SetValue(obj, nm);
                        matrix = nm;
                        rows--;
                        changed = true;
                    }
                }
                if (GUI.Button(new Rect(colPlusX, titleRect.y, 24f, titleRect.height), "C+", EditorStyles.toolbarButton))
                {
                    var nm = Array.CreateInstance(elementType, rows, cols + 1);
                    for (int r = 0; r < rows; r++)
                        for (int c = 0; c < cols; c++)
                            nm.SetValue(matrix.GetValue(r, c), r, c);
                    field.SetValue(obj, nm);
                    matrix = nm;
                    cols++;
                    changed = true;
                }
                if (GUI.Button(new Rect(colMinusX, titleRect.y, 24f, titleRect.height), "C−", EditorStyles.toolbarButton))
                {
                    if (cols > 0)
                    {
                        var nm = Array.CreateInstance(elementType, rows, cols - 1);
                        for (int r = 0; r < rows; r++)
                            for (int c = 0; c < cols - 1; c++)
                                nm.SetValue(matrix.GetValue(r, c), r, c);
                        field.SetValue(obj, nm);
                        matrix = nm;
                        cols--;
                        changed = true;
                    }
                }
            }

            if (foldout && rows > 0 && cols > 0)
            {
                float labelColW = 80f;

                // Pre-fetch labels
                var colLabels = new string[cols];
                var rowLabels = new string[rows];
                for (int c = 0; c < cols; c++)
                {
                    colLabels[c] = c.ToString();
                    if (labelsMethod != null)
                    {
                        try
                        {
                            var result = labelsMethod.Invoke(labelsMethod.IsStatic ? null : obj, new object[] { matrix, TableAxis.X, c });
                            if (result is ValueTuple<string, LabelDirection> vt)
                                colLabels[c] = vt.Item1;
                        }
                        catch { }
                    }
                }
                for (int r = 0; r < rows; r++)
                {
                    rowLabels[r] = r.ToString();
                    if (labelsMethod != null)
                    {
                        try
                        {
                            var result = labelsMethod.Invoke(labelsMethod.IsStatic ? null : obj, new object[] { matrix, TableAxis.Y, r });
                            if (result is ValueTuple<string, LabelDirection> vt)
                                rowLabels[r] = vt.Item1;
                        }
                        catch { }
                    }
                }

                // Use a probe rect to get the actual content width inside the box
                float actualWidth = EditorGUILayout.GetControlRect(false, 0f).width;
                bool hasVerticalTitle = !string.IsNullOrEmpty(attr.VerticalTitle);
                bool hasHorizontalTitle = !string.IsNullOrEmpty(attr.HorizontalTitle);

                // Per-matrix column widths — index 0 = label column, index 1..cols = data columns
                int totalCols = cols + 1; // +1 for label column
                string matrixKey = "TM3_" + field.Name + "_" + obj.GetHashCode() + "_" + totalCols;
                if (!s_TableColumnWidths.TryGetValue(matrixKey, out var matrixColWidths) || matrixColWidths.Length != totalCols)
                {
                    matrixColWidths = null; // will be initialized after headerRect is obtained
                }
                float labelColWidth = matrixColWidths?[0] ?? 80f;

                float cellH = EditorGUIUtility.singleLineHeight + 2f;
                float headerH = EditorGUIUtility.singleLineHeight + 6f;
                float dragHandleW = 4f;

                // Column header row — diagonal corner cell + column labels
                {
                    var headerRect = EditorGUILayout.GetControlRect(false, headerH);
                    EditorGUI.DrawRect(headerRect, new Color(0.3f, 0.3f, 0.3f, 0.3f));

                    // Initialize column widths from actual headerRect width (equal distribution)
                    // Skip caching if headerRect width is invalid (can happen during first layout pass)
                    if (matrixColWidths == null)
                    {
                        float eachW = headerRect.width / totalCols;
                        if (eachW < 30f) eachW = 30f; // clamp but don't cache invalid values
                        if (headerRect.width > 50f) // only cache when width is valid
                        {
                            matrixColWidths = new float[totalCols];
                            for (int i = 0; i < totalCols; i++) matrixColWidths[i] = eachW;
                            s_TableColumnWidths[matrixKey] = matrixColWidths;
                            labelColWidth = matrixColWidths[0];
                        }
                        else if (matrixColWidths == null)
                        {
                            // Fallback: use a temporary array without caching
                            matrixColWidths = new float[totalCols];
                            for (int i = 0; i < totalCols; i++) matrixColWidths[i] = eachW;
                            labelColWidth = matrixColWidths[0];
                        }
                    }

                    // Use GUIUtility.hotControl for reliable drag tracking
                    int dragCtrlId = GUIUtility.GetControlID(matrixKey.GetHashCode(), FocusType.Passive);
                    var dragEv = Event.current;

                    // Diagonal corner cell: HorizontalTitle top-right, VerticalTitle bottom-left, diagonal line
                    Rect cornerRect = new Rect(headerRect.x, headerRect.y, labelColWidth, headerH);
                    if (hasHorizontalTitle || hasVerticalTitle)
                    {
                        // Draw diagonal line from top-left to bottom-right
                        EditorGUI.DrawRect(new Rect(cornerRect.x, cornerRect.y, cornerRect.width, 1), new Color(0.5f, 0.5f, 0.5f, 0.6f));
                        EditorGUI.DrawRect(new Rect(cornerRect.x, cornerRect.yMax - 1, cornerRect.width, 1), new Color(0.5f, 0.5f, 0.5f, 0.6f));
                        Handles.BeginGUI();
                        Handles.color = new Color(0.5f, 0.5f, 0.8f);
                        Handles.DrawLine(
                            new Vector3(cornerRect.x, cornerRect.y),
                            new Vector3(cornerRect.xMax, cornerRect.yMax));
                        Handles.EndGUI();

                        float halfW = cornerRect.width * 0.5f;

                        if (hasHorizontalTitle)
                        {
                            Rect trRect = new Rect(cornerRect.x + halfW, cornerRect.y, halfW, cornerRect.height);
                            DrawCornerLabel(trRect, attr.HorizontalTitle, TextAnchor.UpperRight);
                        }
                        if (hasVerticalTitle)
                        {
                            Rect blRect = new Rect(cornerRect.x, cornerRect.y, halfW, cornerRect.height);
                            DrawCornerLabel(blRect, attr.VerticalTitle, TextAnchor.LowerLeft);
                        }
                    }

                    // Label column drag handle
                    {
                        Rect handleRect = new Rect(headerRect.x + labelColWidth - dragHandleW * 0.5f, headerRect.y, dragHandleW, headerH);
                        EditorGUI.DrawRect(handleRect, new Color(0.5f, 0.5f, 0.5f, 0.3f));
                        EditorGUIUtility.AddCursorRect(handleRect, MouseCursor.ResizeHorizontal);
                        if (dragEv.GetTypeForControl(dragCtrlId) == EventType.MouseDown && handleRect.Contains(dragEv.mousePosition))
                        {
                            GUIUtility.hotControl = dragCtrlId;
                            s_DraggingTableKey = matrixKey;
                            s_DraggingColumnIndex = -1;
                            dragEv.Use();
                        }
                    }

                    // Column labels with drag handles
                    float colX = headerRect.x + labelColWidth;
                    for (int c = 0; c < cols; c++)
                    {
                        float cw = matrixColWidths[c + 1];
                        // Last column: fill remaining space from prev column right edge to headerRect right edge (display only)
                        if (c == cols - 1)
                        {
                            float rightEdge = headerRect.x + headerRect.width;
                            cw = Mathf.Max(30f, rightEdge - colX);
                        }
                        Rect cellRect = new Rect(colX, headerRect.y, cw, headerH);
                        var label = colLabels[c];
                        var colStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip };
                        EditorGUI.LabelField(cellRect, new GUIContent(label), colStyle);

                        // Drag handle between data columns
                        if (c < cols - 1)
                        {
                            Rect handleRect = new Rect(colX + cw - dragHandleW * 0.5f, headerRect.y, dragHandleW, headerH);
                            EditorGUI.DrawRect(handleRect, new Color(0.5f, 0.5f, 0.5f, 0.3f));
                            EditorGUIUtility.AddCursorRect(handleRect, MouseCursor.ResizeHorizontal);
                            if (dragEv.GetTypeForControl(dragCtrlId) == EventType.MouseDown && handleRect.Contains(dragEv.mousePosition))
                            {
                                GUIUtility.hotControl = dragCtrlId;
                                s_DraggingTableKey = matrixKey;
                                s_DraggingColumnIndex = c;
                                dragEv.Use();
                            }
                        }
                        colX += cw;
                    }

                    // Process drag
                    if (GUIUtility.hotControl == dragCtrlId && s_DraggingTableKey == matrixKey)
                    {
                        int dragArrIdx = s_DraggingColumnIndex + 1;
                        if (dragEv.GetTypeForControl(dragCtrlId) == EventType.MouseDrag && dragArrIdx >= 0 && dragArrIdx < totalCols)
                        {
                            float delta = dragEv.delta.x;
                            float curW = matrixColWidths[dragArrIdx];
                            float newWidth = curW + delta;
                            // Clamp to minimum
                            if (newWidth < 30f) newWidth = 30f;
                            float actualDelta = newWidth - curW;
                            // Only apply if there's actual change
                            if (actualDelta != 0f)
                            {
                                matrixColWidths[dragArrIdx] = newWidth;
                                int nextIdx = dragArrIdx + 1;
                                if (nextIdx < totalCols)
                                {
                                    float nextNew = matrixColWidths[nextIdx] - actualDelta;
                                    if (nextNew < 30f)
                                    {
                                        actualDelta -= 30f - nextNew;
                                        nextNew = 30f;
                                        matrixColWidths[dragArrIdx] = Mathf.Max(30f, curW + actualDelta);
                                    }
                                    matrixColWidths[nextIdx] = nextNew;
                                }
                            }
                            dragEv.Use();
                        }
                        if (dragEv.GetTypeForControl(dragCtrlId) == EventType.MouseUp)
                        {
                            GUIUtility.hotControl = 0;
                            s_DraggingTableKey = null;
                            s_DraggingColumnIndex = -1;
                            dragEv.Use();
                        }
                    }

                    EditorGUI.DrawRect(new Rect(headerRect.x, headerRect.yMax - 1, headerRect.width, 1), new Color(0.5f, 0.5f, 0.5f, 0.6f));
                }

                // Track start Y and X of data rows for vertical title
                float dataStartY = 0f;
                float dataStartX = 0f;
                float dataTotalH = rows * cellH;

                // Data rows
                for (int r = 0; r < rows; r++)
                {
                    var rowRect = EditorGUILayout.GetControlRect(false, cellH);
                    if (r == 0) { dataStartY = rowRect.y; dataStartX = rowRect.x; }
                    if (r % 2 == 1)
                        EditorGUI.DrawRect(rowRect, new Color(0.5f, 0.5f, 0.5f, 0.1f));

                    // Row label — always horizontal
                    var rowLabel = rowLabels[r];
                    var rowLabelRect = new Rect(rowRect.x, rowRect.y, labelColWidth, rowRect.height);
                    EditorGUI.LabelField(rowLabelRect, new GUIContent(rowLabel), EditorStyles.boldLabel);

                    // Cells
                    float cellX = rowRect.x + labelColWidth;
                    for (int c = 0; c < cols; c++)
                    {
                        float cw = matrixColWidths[c + 1];
                        // Last column: fill remaining space (display only)
                        if (c == cols - 1)
                        {
                            float rightEdge = rowRect.x + rowRect.width;
                            cw = Mathf.Max(30f, rightEdge - cellX);
                        }
                        Rect cellRect = new Rect(cellX, rowRect.y, cw, cellH);
                        object cellVal = matrix.GetValue(r, c);

                        if (attr.IsReadOnly)
                            EditorGUI.BeginDisabledGroup(true);

                        if (drawMethod != null)
                        {
                            try
                            {
                                // End disabled group temporarily so DrawCell can receive mouse events
                                if (attr.IsReadOnly)
                                    EditorGUI.EndDisabledGroup();
                                var result = drawMethod.Invoke(drawMethod.IsStatic ? null : obj, new object[] { cellRect, cellVal });
                                if (attr.IsReadOnly)
                                    EditorGUI.BeginDisabledGroup(true);
                                if (!IsEqual(result, cellVal))
                                {
                                    matrix.SetValue(result, r, c);
                                    changed = true;
                                }
                            }
                            catch { }
                        }
                        else
                        {
                            object newVal = cellVal;
                            DrawNormalFieldRect(elementType, cellRect, GUIContent.none, ref newVal, false, field);
                            if (!IsEqual(newVal, cellVal))
                            {
                                matrix.SetValue(newVal, r, c);
                                changed = true;
                            }
                        }

                        if (attr.IsReadOnly)
                            EditorGUI.EndDisabledGroup();
                        cellX += cw;
                    }

                    EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.yMax - 1, rowRect.width, 1), new Color(0.3f, 0.3f, 0.3f, 0.3f));
                }
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            EditorGUI.indentLevel = oldIndent;
            EditorGUILayout.Space(2);

            if (changed &&
                field.GetCustomAttribute(typeof(OnCollectionChangedAttribute)) is OnCollectionChangedAttribute
                    collectionChangedAttribute)
            {
                ReflectionMethodInvoker.InvokeNoArg(obj, field.DeclaringType, collectionChangedAttribute.After);
            }
        }

        protected virtual void DrawFieldArrayInspector(FieldInfo field, object obj, Array list, bool isDetails = false)
        {
            if (field.GetCustomAttribute(typeof(ValueDropdownAttribute)) is ValueDropdownAttribute vdAttr)
            {
                DrawValueDropdownArray(field, obj, vdAttr, list as IList, isDetails);
                return;
            }
            DrawIListBoxGrid(field, obj, list, list as IList, isDetails);
        }

        protected virtual void DrawFieldListInspector(FieldInfo field, object obj, IList list, bool isDetails = false)
        {
            if (field.GetCustomAttribute(typeof(ValueDropdownAttribute)) is ValueDropdownAttribute vdAttr)
            {
                DrawValueDropdownArray(field, obj, vdAttr, list, isDetails);
                return;
            }
            DrawIListBoxGrid(field, obj, list, list, isDetails);
        }

        /// <summary>
        /// Unified box+grid style drawing for reflection-based Array and IList.
        /// Matches the Inspector's DrawArrayBox visual style.
        /// </summary>
        protected virtual void DrawIListBoxGrid(FieldInfo field, object obj, IList list, object collection, bool isDetails = false)
        {
            bool isArray = field.FieldType.IsArray;
            var itemType = isArray
                ? field.FieldType.GetElementType()
                : field.FieldType.GenericTypeArguments[0];
            bool changed = false;
            int removeIndex = -1;
            int len = list.Count;
            var showName = GetShowName(field);
            string title = showName?.text ?? ObjectNames.NicifyVariableName(field.Name);
            string tooltip = showName?.tooltip;
            float indexColW = 28f;
            float deleteColW = 22f;
            float setNullColW = 50f;
            float availableWidth = s_AvailableWidth > 0 ? s_AvailableWidth : EditorGUIUtility.currentViewWidth - 40f;

            var boxStyle = new GUIStyle(GUI.skin.box) { padding = new RectOffset(2, 2, 2, 2) };
            float indentOffset = 15f * EditorGUI.indentLevel;
            int oldIndent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            EditorGUILayout.BeginHorizontal();
            if (indentOffset > 0) GUILayout.Space(indentOffset);
            EditorGUILayout.BeginVertical(boxStyle);

            // Foldout title bar with + / - controls
            string foldKey = "TaoTie_Fold_IList_" + field.Name + "_" + obj.GetHashCode();
            bool foldout = SessionState.GetBool(foldKey, false);
            Rect titleBarRect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
            EditorGUI.DrawRect(titleBarRect, new Color(0.3f, 0.3f, 0.3f, 0.2f));
            float tbX = titleBarRect.x + 4f;
            // Anchor buttons to right edge (xMax is indent-independent)
            float minusX = titleBarRect.xMax - 24f - 2f;
            float plusX = minusX - 24f - 2f;
            // Count label before buttons
            string countText = $"({len})";
            var countContent = new GUIContent(countText);
            float countW = EditorStyles.miniLabel.CalcSize(countContent).x + 8f;
            Rect countRect = new Rect(plusX - countW - 4f, titleBarRect.y, countW, titleBarRect.height);
            // Foldout fills space between tbX and count label
            Rect foldRect = new Rect(tbX, titleBarRect.y, countRect.x - tbX - 4f, titleBarRect.height);
            foldout = EditorGUI.Foldout(foldRect, foldout, new GUIContent(title, tooltip), true);
            SessionState.SetBool(foldKey, foldout);
            EditorGUI.LabelField(countRect, countContent, EditorStyles.miniLabel);
            if (GUI.Button(new Rect(plusX, titleBarRect.y, 24f, titleBarRect.height), "+", EditorStyles.toolbarButton))
            {
                if (isArray)
                {
                    var newArr = Array.CreateInstance(itemType, len + 1);
                    if (len > 0) Array.Copy(list as Array, newArr, len);
                    field.SetValue(obj, newArr);
                }
                else
                {
                    list.Add(itemType.IsValueType ? Activator.CreateInstance(itemType) : null);
                }
                changed = true;
            }
            if (GUI.Button(new Rect(minusX, titleBarRect.y, 24f, titleBarRect.height), "-", EditorStyles.toolbarButton))
            {
                if (len > 0)
                {
                    if (isArray)
                    {
                        var newArr = Array.CreateInstance(itemType, len - 1);
                        if (len > 1) Array.Copy(list as Array, newArr, len - 1);
                        field.SetValue(obj, newArr);
                    }
                    else
                    {
                        list.RemoveAt(len - 1);
                    }
                    changed = true;
                }
            }

            if (foldout)
            {
                string showAllKey = "TaoTie_ShowAll_IList_" + field.Name + "_" + obj.GetHashCode();
                bool showAll = SessionState.GetBool(showAllKey, false);
                int visibleCount = showAll ? list.Count : Mathf.Min(list.Count, k_MaxVisibleRows);

                for (int i = 0; i < visibleCount; i++)
                {
                    // Guard against mid-frame collection mutation
                    if (i >= list.Count) break;
                    var item = list[i];
                    bool isValueType = itemType.IsValueType || itemType == stringType;
                    bool isUnityObject = objectType.IsAssignableFrom(itemType);
                    bool subFold = false;
                    float rowHeight = EditorGUIUtility.singleLineHeight + 2f;

                    var rowRect = EditorGUILayout.GetControlRect(false, rowHeight);
                    if (i % 2 == 1)
                        EditorGUI.DrawRect(rowRect, new Color(0.5f, 0.5f, 0.5f, 0.1f));

                    float x = rowRect.x;
                    // Index
                    EditorGUI.LabelField(new Rect(x, rowRect.y, indexColW, rowRect.height), i.ToString());
                    x += indexColW;

                    // Delete button anchored to right edge (same as Dictionary)
                    Rect delRect = new Rect(rowRect.xMax - deleteColW - 2f, rowRect.y, deleteColW, rowRect.height);
                    // Content area between index and delete
                    float contentW = Mathf.Max(30f, delRect.x - x - 2f);
                    float setNullW = isValueType ? 0f : setNullColW + 2f;
                    float fieldColW = Mathf.Max(30f, contentW - setNullW);

                    if (isValueType)
                    {
                        // Value type / string — draw inline
                        object newValue = item;
                        Rect fieldRect = new Rect(x, rowRect.y, fieldColW, rowRect.height);
                        EditorGUI.BeginChangeCheck();
                        DrawNormalFieldRect(itemType, fieldRect, GUIContent.none, ref newValue, true, field);
                        if (EditorGUI.EndChangeCheck() && !IsEqual(newValue, item))
                        {
                            list[i] = newValue;
                            changed = true;
                        }
                    }
                    else if (isUnityObject)
                    {
                        // UnityEngine.Object — draw ObjectField
                        Rect objRect = new Rect(x, rowRect.y, contentW, rowRect.height);
                        UnityEngine.Object newObj = EditorGUI.ObjectField(objRect, (UnityEngine.Object)item, itemType, false);
                        if (!IsEqual(newObj, item))
                        {
                            list[i] = newObj;
                            changed = true;
                        }
                    }
                    else
                    {
                        // Reference type — foldout + SetNull + field
                        if (item == null)
                        {
                            Rect popRect = new Rect(x, rowRect.y, contentW, rowRect.height);
                            var types = GetSubClassList(field, obj, itemType, out var names);
                            int selIdx = EditorGUI.Popup(popRect, -1, names);
                            if (selIdx >= 0)
                            {
                                list[i] = Activator.CreateInstance(types[selIdx]);
                                changed = true;
                            }
                        }
                        else
                        {
                            if (!listFoldoutState.TryGetValue((field, obj.GetHashCode()), out var foldSet))
                            {
                                foldSet = new HashSet<int>();
                                listFoldoutState[(field, obj.GetHashCode())] = foldSet;
                            }
                            bool subFoldState = foldSet.Contains(i);
                            // SetNull button anchored just before the delete button
                            Rect snRect = new Rect(delRect.x - setNullColW - 2f, rowRect.y, setNullColW, rowRect.height);
                            // Foldout fills the space between index and SetNull
                            Rect subFoldRect = new Rect(x, rowRect.y, snRect.x - x - 2f, rowRect.height);
                            string foldLabel = GetShowName(item.GetType()).text;
                            // Truncate label to fit foldout rect (account for foldout arrow ~13px)
                            foldLabel = TruncateLabel(foldLabel, subFoldRect.width - 15f);
                            subFold = EditorGUI.Foldout(subFoldRect, subFoldState, foldLabel);
                            if (GUI.Button(snRect, "SetNull"))
                            {
                                list[i] = null;
                                changed = true;
                            }

                            if (subFold)
                            {
                                foldSet.Add(i);
                            }
                            else
                                foldSet.Remove(i);
                        }
                    }

                    // Delete button drawn last (on top)
                    if (GUI.Button(delRect, "×"))
                    {
                        removeIndex = i;
                        break;
                    }

                    // Row bottom grid line
                    EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.yMax - 1, rowRect.width, 1),
                        new Color(0.3f, 0.3f, 0.3f, 0.3f));

                    // Sub-object inspector for expanded reference-type elements
                    if (!isValueType && item != null && subFold)
                    {
                        EditorGUI.indentLevel++;
                        DrawObjectInspector(item, isDetails);
                        EditorGUI.indentLevel--;
                    }
                }

                // Show All / Show Less toggle
                if (list.Count > k_MaxVisibleRows)
                {
                    if (GUILayout.Button(showAll ? $"Show Less ({k_MaxVisibleRows})" : $"Show All ({list.Count})", EditorStyles.miniButton))
                    {
                        SessionState.SetBool(showAllKey, !showAll);
                    }
                }
            }

            if (removeIndex >= 0)
            {
                if (isArray)
                {
                    var newArr = Array.CreateInstance(itemType, len - 1);
                    int j = 0;
                    for (int k = 0; k < len; k++)
                    {
                        if (k == removeIndex) continue;
                        newArr.SetValue(list[k], j++);
                    }
                    field.SetValue(obj, newArr);
                }
                else
                {
                    list.RemoveAt(removeIndex);
                }
                changed = true;
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            EditorGUI.indentLevel = oldIndent;
            EditorGUILayout.Space(2);

            if (changed &&
                field.GetCustomAttribute(typeof(OnCollectionChangedAttribute)) is OnCollectionChangedAttribute
                    collectionChangedAttribute)
            {
                ReflectionMethodInvoker.InvokeNoArg(obj, field.DeclaringType, collectionChangedAttribute.After);
            }
        }

        /// <summary>
        /// Rect-based variant of DrawNormalField for use in box+grid rows.
        /// </summary>
        protected virtual void DrawNormalFieldRect(Type type, Rect rect, GUIContent label, ref object value,
            bool isDetails = false, FieldInfo field = null)
        {
            bool noLabel = label == GUIContent.none || string.IsNullOrEmpty(label.text);
            float oldLabelW = EditorGUIUtility.labelWidth;
            if (noLabel) EditorGUIUtility.labelWidth = 0f;

            if (type == typeof(int))
            {
                value = noLabel ? EditorGUI.IntField(rect, (int)value) : EditorGUI.IntField(rect, label, (int)value);
            }
            else if (type == typeof(float))
            {
                value = noLabel ? EditorGUI.FloatField(rect, (float)value) : EditorGUI.FloatField(rect, label, (float)value);
            }
            else if (type == typeof(bool))
            {
                value = noLabel ? EditorGUI.Toggle(rect, (bool)value) : EditorGUI.Toggle(rect, label, (bool)value);
            }
            else if (type == typeof(string))
            {
                value = noLabel ? EditorGUI.TextField(rect, (string)value) : EditorGUI.TextField(rect, label, (string)value);
            }
            else if (type == typeof(long))
            {
                value = noLabel ? EditorGUI.LongField(rect, (long)value) : EditorGUI.LongField(rect, label, (long)value);
            }
            else if (type == typeof(double))
            {
                value = noLabel ? EditorGUI.DoubleField(rect, (double)value) : EditorGUI.DoubleField(rect, label, (double)value);
            }
            else if (type == typeof(Vector2))
            {
                value = EditorGUI.Vector2Field(rect, noLabel ? "" : label.text, (Vector2)value);
            }
            else if (type == typeof(Vector3))
            {
                value = EditorGUI.Vector3Field(rect, noLabel ? "" : label.text, (Vector3)value);
            }
            else if (type == typeof(Vector4))
            {
                value = EditorGUI.Vector4Field(rect, noLabel ? "" : label.text, (Vector4)value);
            }
            else if (type == typeof(Color))
            {
                value = noLabel ? EditorGUI.ColorField(rect, (Color)value) : EditorGUI.ColorField(rect, label, (Color)value);
            }
            else if (type == typeof(Rect))
            {
                value = noLabel ? EditorGUI.RectField(rect, (Rect)value) : EditorGUI.RectField(rect, label, (Rect)value);
            }
            else if (type == typeof(AnimationCurve))
            {
                value = noLabel ? EditorGUI.CurveField(rect, (AnimationCurve)value) : EditorGUI.CurveField(rect, label, (AnimationCurve)value);
            }
            else if (type.IsEnum)
            {
                value = noLabel ? EditorGUI.EnumPopup(rect, (Enum)value) : EditorGUI.EnumPopup(rect, label, (Enum)value);
            }
            else if (objectType.IsAssignableFrom(type)
                     && !(field?.GetCustomAttribute(typeof(NotAssetsAttribute)) is NotAssetsAttribute))
            {
                value = noLabel ? EditorGUI.ObjectField(rect, (UnityEngine.Object)value, type, true)
                                : EditorGUI.ObjectField(rect, label, (UnityEngine.Object)value, type, true);
            }
            else
            {
                EditorGUI.LabelField(rect, label, new GUIContent(value?.ToString() ?? "null"));
            }

            if (noLabel) EditorGUIUtility.labelWidth = oldLabelW;
        }

        protected virtual void DrawFieldDictionaryInspector(FieldInfo field, object obj, IDictionary dictionary,
            bool isDetails = false)
        {
            object removeKey = null;
            object editorKey = null;
            object changeValue = null;
            bool addedItem = false;
            var keyType = field.FieldType.GenericTypeArguments[0];
            var itemType = field.FieldType.GenericTypeArguments[1];

            float availableWidth = s_AvailableWidth > 0 ? s_AvailableWidth : EditorGUIUtility.currentViewWidth - 40f;
            float colW = Mathf.Max(80f, (availableWidth - 22f) * 0.5f);
            float deleteColW = 22f;
            float dragHandleW = 6f;

            var boxStyle = new GUIStyle(GUI.skin.box) { padding = new RectOffset(2, 2, 2, 2) };
            float dicIndentOffset = 15f * EditorGUI.indentLevel;
            int dicOldIndent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            EditorGUILayout.BeginHorizontal();
            if (dicIndentOffset > 0) GUILayout.Space(dicIndentOffset);
            EditorGUILayout.BeginVertical(boxStyle);

            // Foldout title bar
            string dicFoldKey = "TaoTie_Fold_Dict_" + field.Name + "_" + obj.GetHashCode();
            bool dicFoldout = SessionState.GetBool(dicFoldKey, false);
            Rect dicTitleRect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
            EditorGUI.DrawRect(dicTitleRect, new Color(0.3f, 0.3f, 0.3f, 0.2f));
            var dicShowName = GetShowName(field);
            string dicTitle = (dicShowName?.text ?? ObjectNames.NicifyVariableName(field.Name)) + $" ({dictionary.Count})";
            string dicTooltip = dicShowName?.tooltip;
            dicFoldout = EditorGUI.Foldout(new Rect(dicTitleRect.x + s_FoldoutXOffset, dicTitleRect.y, dicTitleRect.width - s_FoldoutXOffset - 4f, dicTitleRect.height),
                dicFoldout, new GUIContent(dicTitle, dicTooltip), true);
            SessionState.SetBool(dicFoldKey, dicFoldout);

            if (dicFoldout)
            {
            // Column headers
            var headerRect = EditorGUILayout.GetControlRect(false, 20f);
            EditorGUI.DrawRect(headerRect, new Color(0.3f, 0.3f, 0.3f, 0.4f));
            EditorGUI.LabelField(new Rect(headerRect.x, headerRect.y, colW, headerRect.height), "Key", EditorStyles.boldLabel);
            EditorGUI.LabelField(new Rect(headerRect.x + colW, headerRect.y, colW, headerRect.height), "Value", EditorStyles.boldLabel);
            EditorGUI.DrawRect(new Rect(headerRect.x, headerRect.yMax - 1, headerRect.width, 1),
                new Color(0.5f, 0.5f, 0.5f, 0.6f));

            // Data rows
            int rowIdx = 0;
            string dicShowAllKey = "TaoTie_ShowAll_Dict_" + field.Name + "_" + obj.GetHashCode();
            bool dicShowAll = SessionState.GetBool(dicShowAllKey, false);
            int dicMaxRows = dicShowAll ? int.MaxValue : k_MaxVisibleRows;

            foreach (DictionaryEntry kv in dictionary)
            {
                if (rowIdx >= dicMaxRows) break;
                rowIdx++;
                var rowRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight + 2f);
                if (rowIdx % 2 == 1)
                    EditorGUI.DrawRect(rowRect, new Color(0.5f, 0.5f, 0.5f, 0.1f));

                var key = kv.Key;
                var item = kv.Value;
                float x = rowRect.x;

                // Delete button anchored to right edge (calculated first, same as List)
                Rect delRect = new Rect(rowRect.xMax - deleteColW - 2f, rowRect.y, deleteColW, rowRect.height);

                // Key column (read-only)
                float keyW = Mathf.Min(colW, (delRect.x - x) * 0.5f);
                EditorGUI.LabelField(new Rect(x, rowRect.y, keyW - dragHandleW, rowRect.height),
                    key?.ToString() ?? "", EditorStyles.label);
                x += keyW;

                // Value column — width derived from delRect to avoid overlap
                float valW = delRect.x - x - 2f;
                if (itemType.IsValueType || itemType == stringType)
                {
                    var newItem = item;
                    Rect valRect = new Rect(x, rowRect.y, valW, rowRect.height);
                    EditorGUI.BeginChangeCheck();
                    // For value types, use direct EditorGUI fields instead of DrawNormalField
                    // to avoid GUILayout layout pushing to next line
                    if (itemType == typeof(string))
                        newItem = EditorGUI.TextField(valRect, (string)newItem);
                    else if (itemType == typeof(int))
                        newItem = EditorGUI.IntField(valRect, (int)newItem);
                    else if (itemType == typeof(float))
                        newItem = EditorGUI.FloatField(valRect, (float)newItem);
                    else if (itemType == typeof(bool))
                        newItem = EditorGUI.Toggle(valRect, (bool)newItem);
                    else if (itemType == typeof(long))
                        newItem = EditorGUI.LongField(valRect, (long)newItem);
                    else if (itemType.IsEnum)
                        newItem = EditorGUI.EnumPopup(valRect, (Enum)newItem);
                    else
                        newItem = EditorGUI.TextField(valRect, newItem?.ToString() ?? "");
                    if (EditorGUI.EndChangeCheck() && !IsEqual(newItem, item))
                    {
                        editorKey = key;
                        changeValue = newItem;
                    }
                }
                else if (item == null)
                {
                    var types = GetSubClassList(field, obj, itemType, out var names);
                    var index = EditorGUI.Popup(new Rect(x, rowRect.y, valW, rowRect.height), -1, names);
                    if (index >= 0)
                    {
                        editorKey = key;
                        changeValue = Activator.CreateInstance(types[index]);
                    }
                }
                else
                {
                    EditorGUI.LabelField(new Rect(x, rowRect.y, valW, rowRect.height),
                        item.GetType().Name, EditorStyles.boldLabel);
                }

                // Delete button (already positioned above, just draw it)
                if (GUI.Button(delRect, "×"))
                    removeKey = key;

                // Row bottom grid line
                EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.yMax - 1, rowRect.width, 1),
                    new Color(0.3f, 0.3f, 0.3f, 0.3f));

                // Complex object — show foldout below
                if (item != null && !itemType.IsValueType && itemType != stringType)
                {
                    bool subFoldout = listFoldoutState.TryGetValue((field, obj.GetHashCode()), out var foldSet) && foldSet.Contains(rowIdx * 2 + 1);
                    var foldRect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
                    subFoldout = EditorGUI.Foldout(foldRect, subFoldout, "  └ " + item.GetType().Name + " Details");
                    if (!listFoldoutState.TryGetValue((field, obj.GetHashCode()), out foldSet))
                    {
                        foldSet = new HashSet<int>();
                        listFoldoutState[(field, obj.GetHashCode())] = foldSet;
                    }
                    if (subFoldout)
                    {
                        foldSet.Add(rowIdx * 2 + 1);
                        EditorGUI.indentLevel++;
                        DrawObjectInspector(item, isDetails);
                        EditorGUI.indentLevel--;
                    }
                    else
                        foldSet.Remove(rowIdx * 2 + 1);
                }
            }

            // Show All / Show Less toggle
            if (dictionary.Count > k_MaxVisibleRows)
            {
                if (GUILayout.Button(dicShowAll ? $"Show Less ({k_MaxVisibleRows})" : $"Show All ({dictionary.Count})", EditorStyles.miniButton))
                {
                    SessionState.SetBool(dicShowAllKey, !dicShowAll);
                }
            }

            // Add row — use same delRect-anchored layout
            var addRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight + 2f);
            Rect addBtnRect = new Rect(addRect.xMax - deleteColW - 2f, addRect.y, deleteColW, addRect.height);
            float addKeyW = Mathf.Min(colW, (addBtnRect.x - addRect.x) * 0.5f);
            float addValW = addBtnRect.x - addRect.x - addKeyW - 2f;
            object inputKey;
            if (keyType == stringType)
            {
                if (!dicInputKey.TryGetValue(field, out inputKey))
                {
                    inputKey = "";
                    dicInputKey.Add(field, inputKey);
                }
                var newKey = inputKey;
                Rect keyInputRect = new Rect(addRect.x, addRect.y, addKeyW, addRect.height);
                newKey = EditorGUI.TextField(keyInputRect, (string)newKey);
                inputKey = newKey;
            }
            else if (keyType.IsEnum)
            {
                if (!dicInputKey.TryGetValue(field, out inputKey))
                {
                    inputKey = Activator.CreateInstance(keyType);
                    dicInputKey.Add(field, inputKey);
                }
                var newKey = inputKey;
                Rect keyInputRect = new Rect(addRect.x, addRect.y, addKeyW, addRect.height);
                newKey = EditorGUI.EnumPopup(keyInputRect, (System.Enum)newKey);
                inputKey = newKey;
            }
            else if (keyType == typeof(int))
            {
                if (!dicInputKey.TryGetValue(field, out inputKey))
                {
                    inputKey = 0;
                    dicInputKey.Add(field, inputKey);
                }
                var newKey = inputKey;
                Rect keyInputRect = new Rect(addRect.x, addRect.y, addKeyW, addRect.height);
                newKey = EditorGUI.IntField(keyInputRect, (int)newKey);
                inputKey = newKey;
            }
            else if (keyType == typeof(float))
            {
                if (!dicInputKey.TryGetValue(field, out inputKey))
                {
                    inputKey = 0f;
                    dicInputKey.Add(field, inputKey);
                }
                var newKey = inputKey;
                Rect keyInputRect = new Rect(addRect.x, addRect.y, addKeyW, addRect.height);
                newKey = EditorGUI.FloatField(keyInputRect, (float)newKey);
                inputKey = newKey;
            }
            else
            {
                inputKey = keyType == stringType ? "" : Activator.CreateInstance(keyType);
                EditorGUI.LabelField(new Rect(addRect.x, addRect.y, addKeyW, addRect.height), keyType.Name);
            }
            dicInputKey[field] = inputKey;

            if (GUI.Button(addBtnRect, "+"))
            {
                if (!dictionary.Contains(inputKey))
                {
                    dictionary.Add(inputKey, itemType.IsValueType ? Activator.CreateInstance(itemType) : null);
                    dicInputKey.Remove(field);
                    addedItem = true;
                }
                else
                {
                    Debug.LogError("Key already exists");
                }
            }

            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            EditorGUI.indentLevel = dicOldIndent;
            EditorGUILayout.Space(2);

            if (removeKey != null)
                dictionary.Remove(removeKey);
            if (editorKey != null)
                dictionary[editorKey] = changeValue;

            if ((removeKey != null || editorKey != null || addedItem)
                && field.GetCustomAttribute(typeof(OnCollectionChangedAttribute)) is OnCollectionChangedAttribute
                collectionChangedAttribute)
            {
                ReflectionMethodInvoker.InvokeNoArg(obj, field.DeclaringType, collectionChangedAttribute.After);
            }

            return;
        }

        protected virtual void DrawEnumFieldInspector(FieldInfo field, object obj)
        {
            if (!enumDropDown.TryGetValue(field.FieldType, out var names))
            {
                names = Enum.GetNames(field.FieldType);
                bool has = false;
                for (int i = 0; i < names.Length; i++)
                {
                    var enumField = field.FieldType.GetField(names[i]);
                    names[i] = GetShowNameString(enumField, out bool rename);
                    has |= rename;
                }

                if (!has)
                {
                    names = null;
                }

                enumDropDown.Add(field.FieldType, names);
            }

            if (names == null)
            {
                var value = field.GetValue(obj);
                field.SetValue(obj, EditorGUILayout.EnumPopup(GetShowName(field, value), (Enum) value));
            }
            else
            {
                var value = field.GetValue(obj);
                int index = -1;
                var list = Enum.GetValues(field.FieldType);
                for (int i = 0; i < list.Length; i++)
                {
                    if (IsEqual(value, list.GetValue(i)))
                    {
                        index = i;
                        break;
                    }
                }

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel(GetShowName(field, value));
                var newindex = EditorGUILayout.Popup(index, names);
                if (newindex != index)
                {
                    field.SetValue(obj, list.GetValue(newindex));
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        protected virtual void DrawValueDropdownFieldInspector(Type fieldType, object obj,
            ValueDropdownAttribute valueDropdownAttribute,object value, ValueDropdownFieldType type,
            ref bool remove,FieldInfo field = null, int aIndex = 0,Array array=null,IList iList = null)
        {
            string showText = value?.ToString();
            if (valueDropdownAttribute.AppendNextDrawer)
            {
                EditorGUILayout.BeginHorizontal();
                object newValue = value;
                if (!DrawNormalField(fieldType,
                        type == ValueDropdownFieldType.Normal? GetShowName(field, value):new GUIContent(aIndex.ToString()), 
                        ref newValue, true, field))
                {
                    EditorGUILayout.LabelField(showText);
                }
                else if (!IsEqual(newValue, value))
                {
                    switch (type)
                    {
                        case ValueDropdownFieldType.Normal:
                            field.SetValue(obj, newValue);
                            break;
                        case ValueDropdownFieldType.IList:
                            iList[aIndex] = newValue;
                            break;
                        case ValueDropdownFieldType.Array:
                            array.SetValue(newValue,aIndex);
                            break;
                    }
                }

                if (GUILayout.Button(new GUIContent("▼")))
                {
                    GUI.FocusControl(null);
                    RefreshValueDropDown(field, obj, valueDropdownAttribute.MemberName);
                    if (!valueDropdown.TryGetValue(field, out var dropdownItems))
                    {
                        Debug.LogError(valueDropdownAttribute.MemberName);
                    }
                    else
                    {
                        int index = -1;
                        for (int i = 0; i < dropdownItems.Length; i++)
                        {
                            if (IsEqual(value, dropdownItems[i].Value))
                            {
                                index = i;
                                break;
                            }
                        }

                        var menu = new GenericMenu();
                        for (int i = 0; i < dropdownItems.Length; i++)
                        {
                            var ii = i;
                            menu.AddItem(new GUIContent(dropdownItems[i].Text), i == index, () =>
                            {
                                if (ii != index)
                                {
                                    newValue = dropdownItems[ii].Value;
                                    switch (type)
                                    {
                                        case ValueDropdownFieldType.Normal:
                                            field.SetValue(obj, newValue);
                                            break;
                                        case ValueDropdownFieldType.IList:
                                            iList[aIndex] = newValue;
                                            break;
                                        case ValueDropdownFieldType.Array:
                                            array.SetValue(newValue,aIndex);
                                            break;
                                    }
                                }
                            });
                        }

                        menu.ShowAsContext();
                    }
                }

                if (type != ValueDropdownFieldType.Normal)
                {
                    if (GUILayout.Button("-", GUILayout.Width(40)))
                    {
                        remove = true;
                    }
                }
                EditorGUILayout.EndHorizontal();
                return;
            }
            
            // Always ensure cache is populated before layout to avoid GUILayout mismatch
            if (!valueDropdown.TryGetValue(field, out var list))
            {
                RefreshValueDropDown(field, obj, valueDropdownAttribute.MemberName);
                valueDropdown.TryGetValue(field, out list);
            }

            if (list != null)
            {
                int index = -1;
                for (int i = 0; i < list.Length; i++)
                {
                    if (IsEqual(value, list[i].Value))
                    {
                        index = i;
                        break;
                    }
                }

                if (index == -1 && list.Length > 0)
                {
                    var newValue = list[0].Value;
                    index = 0;
                    switch (type)
                    {
                        case ValueDropdownFieldType.Normal:
                            field.SetValue(obj, newValue);
                            break;
                        case ValueDropdownFieldType.IList:
                            iList[aIndex] = newValue;
                            break;
                        case ValueDropdownFieldType.Array:
                            array.SetValue(newValue,aIndex);
                            break;
                    }
                }

                if (index >= 0 && index < list.Length)
                {
                    showText = list[index].Text;
                }
            }
            else
            {
                // Cache still empty — draw same layout as main path to avoid GUILayout mismatch
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel(GetShowName(field, value));
                EditorGUILayout.DropdownButton(new GUIContent(showText), FocusType.Passive);
                if (type != ValueDropdownFieldType.Normal)
                {
                    if (GUILayout.Button("-", GUILayout.Width(40)))
                    {
                        remove = true;
                    }
                }
                EditorGUILayout.EndHorizontal();
                return;
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(GetShowName(field, value));
            if (EditorGUILayout.DropdownButton(new GUIContent(showText), FocusType.Passive))
            {
                RefreshValueDropDown(field, obj, valueDropdownAttribute.MemberName);
                if (!valueDropdown.TryGetValue(field, out list))
                {
                    Debug.LogError(valueDropdownAttribute.MemberName);
                }
                else
                {
                    int index = -1;
                    for (int i = 0; i < list.Length; i++)
                    {
                        if (IsEqual(value, list[i].Value))
                        {
                            index = i;
                            break;
                        }
                    }

                    var menu = new GenericMenu();
                    for (int i = 0; i < list.Length; i++)
                    {
                        var ii = i;
                        menu.AddItem(new GUIContent(list[i].Text), i == index, () =>
                        {
                            if (ii != index)
                            {
                                var newValue = list[ii].Value;
                                switch (type)
                                {
                                    case ValueDropdownFieldType.Normal:
                                        field.SetValue(obj, newValue);
                                        break;
                                    case ValueDropdownFieldType.IList:
                                        iList[aIndex] = newValue;
                                        break;
                                    case ValueDropdownFieldType.Array:
                                        array.SetValue(newValue,aIndex);
                                        break;
                                }
                            }
                        });
                    }

                    menu.ShowAsContext();
                }
            }

            if (type != ValueDropdownFieldType.Normal)
            {
                if (GUILayout.Button("-", GUILayout.Width(40)))
                {
                    remove = true;
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        #endregion

        #region DrawMethod

        protected virtual void DrawMethodInspector(MethodInfo method, object obj, bool isDetails = false)
        {
            // Only supports parameterless methods
            if (method.GetParameters().Length != 0) return;
            if (GetCachedAttr<ButtonAttribute>(method) is ButtonAttribute buttonAttribute)
            {
                string btnName = buttonAttribute.Name ?? ObjectNames.NicifyVariableName(method.Name);
                var size = buttonAttribute.Size;
                float h = size switch
                {
                    ButtonSizes.Small => 20f,
                    ButtonSizes.Medium => 28f,
                    ButtonSizes.Large => 40f,
                    ButtonSizes.Gigantic => 60f,
                    _ => 28f
                };
                if (GUILayout.Button(btnName, GUILayout.Height(h)))
                {
                    method.Invoke(obj, null);
                }
            }
        }

        #endregion

        #region DrawValueDropdownArray (reflection-based, box+grid style)

        /// <summary>
        /// Draw a reflection-based array/list with ValueDropdown per element.
        /// Uses the same box+toolbar+grid style as the Inspector's DrawTableList.
        /// Supports both Array and IList via the IList adapter.
        /// </summary>
        protected virtual void DrawValueDropdownArray(FieldInfo field, object obj,
            ValueDropdownAttribute attr, IList list, bool isDetails = false)
        {
            var itemType = field.FieldType.IsArray
                ? field.FieldType.GetElementType()
                : field.FieldType.GenericTypeArguments[0];
            RefreshValueDropDown(field, obj, attr.MemberName);
            if (!valueDropdown.TryGetValue(field, out var items) || items == null || items.Length == 0)
            {
                // No dropdown items available (e.g. no configured source objects) —
                // fall back to the standard box+grid array drawing so the field remains
                // visible and editable instead of collapsing to a bare label.
                DrawIListBoxGrid(field, obj, list, list, isDetails);
                return;
            }

            bool changed = false;
            int removeIndex = -1;
            int len = list.Count;
            var showName = GetShowName(field);
            string title = showName?.text ?? ObjectNames.NicifyVariableName(field.Name);
            string vdTooltip = showName?.tooltip;
            float indexColW = 28f;
            float deleteColW = 22f;

            var boxStyle = new GUIStyle(GUI.skin.box) { padding = new RectOffset(2, 2, 2, 2) };
            float vdIndentOffset = 15f * EditorGUI.indentLevel;
            int vdOldIndent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            EditorGUILayout.BeginHorizontal();
            if (vdIndentOffset > 0) GUILayout.Space(vdIndentOffset);
            EditorGUILayout.BeginVertical(boxStyle);

            // Title bar — rect-based gap-fill
            Rect vdTitleRect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
            EditorGUI.DrawRect(vdTitleRect, new Color(0.3f, 0.3f, 0.3f, 0.2f));
            float vdTbX = vdTitleRect.x + 4f;
            float vdMinusX = vdTitleRect.xMax - 24f - 2f;
            float vdPlusX = vdMinusX - 24f - 2f;
            string vdCountText = $"({len})";
            var vdCountContent = new GUIContent(vdCountText);
            float vdCountW = EditorStyles.miniLabel.CalcSize(vdCountContent).x + 8f;
            Rect vdCountRect = new Rect(vdPlusX - vdCountW - 4f, vdTitleRect.y, vdCountW, vdTitleRect.height);
            Rect vdFoldRect = new Rect(vdTbX, vdTitleRect.y, vdCountRect.x - vdTbX - 4f, vdTitleRect.height);
            string vdFoldKey = "TaoTie_Fold_VD_" + field.Name + "_" + obj.GetHashCode();
            bool vdFoldout = SessionState.GetBool(vdFoldKey, false);
            vdFoldout = EditorGUI.Foldout(vdFoldRect, vdFoldout, new GUIContent(title, vdTooltip), true);
            SessionState.SetBool(vdFoldKey, vdFoldout);
            EditorGUI.LabelField(vdCountRect, vdCountContent, EditorStyles.miniLabel);
            bool isArray = field.FieldType.IsArray;
            if (GUI.Button(new Rect(vdPlusX, vdTitleRect.y, 24f, vdTitleRect.height), "+", EditorStyles.toolbarButton))
            {
                if (isArray)
                {
                    var newArr = Array.CreateInstance(itemType, len + 1);
                    if (len > 0) Array.Copy(list as Array, newArr, len);
                    field.SetValue(obj, newArr);
                }
                else
                {
                    list.Add(itemType.IsValueType ? Activator.CreateInstance(itemType) : null);
                }
                changed = true;
            }
            if (GUI.Button(new Rect(vdMinusX, vdTitleRect.y, 24f, vdTitleRect.height), "-", EditorStyles.toolbarButton))
            {
                if (len > 0)
                {
                    if (isArray)
                    {
                        var newArr = Array.CreateInstance(itemType, len - 1);
                        if (len > 1) Array.Copy(list as Array, newArr, len - 1);
                        field.SetValue(obj, newArr);
                    }
                    else
                    {
                        list.RemoveAt(len - 1);
                    }
                    changed = true;
                }
            }

            // Data rows
            if (vdFoldout)
            {
            string vdShowAllKey = "TaoTie_ShowAll_VD_" + field.Name + "_" + obj.GetHashCode();
            bool vdShowAll = SessionState.GetBool(vdShowAllKey, false);
            int vdVisibleCount = vdShowAll ? list.Count : Mathf.Min(list.Count, k_MaxVisibleRows);

            for (int i = 0; i < vdVisibleCount; i++)
            {
                // Guard against mid-frame collection mutation
                if (i >= list.Count) break;
                var value = list[i];
                var capturedIdx = i;
                var rowRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight + 2f);
                if (i % 2 == 1)
                    EditorGUI.DrawRect(rowRect, new Color(0.5f, 0.5f, 0.5f, 0.1f));

                float x = rowRect.x;

                // Index column
                EditorGUI.LabelField(new Rect(x, rowRect.y, indexColW, rowRect.height), i.ToString());
                x += indexColW;

                // Delete button anchored to right edge (same as Dictionary)
                Rect delRect = new Rect(rowRect.xMax - deleteColW - 2f, rowRect.y, deleteColW, rowRect.height);

                // Content area between index and delete
                float contentW = Mathf.Max(50f, delRect.x - x - 2f);

                // Find current selection
                int selectedIndex = -1;
                for (int j = 0; j < items.Length; j++)
                {
                    if (IsEqual(value, items[j].Value))
                    {
                        selectedIndex = j;
                        break;
                    }
                }

                string currentText = selectedIndex >= 0 ? items[selectedIndex].Text : "—";

                if (attr.AppendNextDrawer)
                {
                    // AppendNextDrawer: same layout as List — delRect first, then derive ▼ and field
                    float btnW = 22f;
                    Rect delRectVD = new Rect(rowRect.xMax - deleteColW - 2f, rowRect.y, deleteColW, rowRect.height);
                    Rect btnRect = new Rect(delRectVD.x - btnW - 2f, rowRect.y, btnW, rowRect.height);
                    Rect fieldRect = new Rect(x, rowRect.y, btnRect.x - x - 2f, rowRect.height);

                    // Draw field first
                    object newValue = value;
                    EditorGUI.BeginChangeCheck();
                    float oldLabelW = EditorGUIUtility.labelWidth;
                    EditorGUIUtility.labelWidth = 0;
                    DrawNormalFieldRect(itemType, fieldRect, GUIContent.none, ref newValue, true, field);
                    EditorGUIUtility.labelWidth = oldLabelW;
                    if (EditorGUI.EndChangeCheck() && !IsEqual(newValue, value))
                    {
                        list[capturedIdx] = newValue;
                        changed = true;
                    }

                    // ▼ button drawn last (on top, same as List delete button)
                    if (GUI.Button(btnRect, "▼"))
                    {
                        var menu = new GenericMenu();
                        for (int j = 0; j < items.Length; j++)
                        {
                            var jj = j;
                            menu.AddItem(new GUIContent(items[jj].Text), j == selectedIndex, () =>
                            {
                                if (jj != selectedIndex)
                                {
                                    list[capturedIdx] = items[jj].Value;
                                    changed = true;
                                }
                            });
                        }
                        menu.ShowAsContext();
                    }

                    // Delete button drawn last (on top)
                    if (GUI.Button(delRectVD, "×"))
                    {
                        removeIndex = capturedIdx;
                        break;
                    }
                }
                else
                {
                    // Normal: popup-style button showing the current selection text
                    Rect dropdownRect = new Rect(x, rowRect.y, contentW, rowRect.height);
                    if (GUI.Button(dropdownRect, currentText, EditorStyles.popup))
                    {
                        var menu = new GenericMenu();
                        for (int j = 0; j < items.Length; j++)
                        {
                            var jj = j;
                            menu.AddItem(new GUIContent(items[j].Text), j == selectedIndex, () =>
                            {
                                if (jj != selectedIndex)
                                {
                                    list[capturedIdx] = items[jj].Value;
                                    changed = true;
                                }
                            });
                        }
                        menu.ShowAsContext();
                    }
                }

                // Delete button
                if (GUI.Button(delRect, "×"))
                {
                    removeIndex = i;
                    break;
                }

                // Row bottom grid line
                EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.yMax - 1, rowRect.width, 1),
                    new Color(0.3f, 0.3f, 0.3f, 0.3f));
            }

            // Show All / Show Less toggle
            if (list.Count > k_MaxVisibleRows)
            {
                if (GUILayout.Button(vdShowAll ? $"Show Less ({k_MaxVisibleRows})" : $"Show All ({list.Count})", EditorStyles.miniButton))
                {
                    SessionState.SetBool(vdShowAllKey, !vdShowAll);
                }
            }
            } // end if (vdFoldout)

            if (removeIndex >= 0)
            {
                if (field.FieldType.IsArray)
                {
                    var newArr = Array.CreateInstance(itemType, len - 1);
                    int j = 0;
                    for (int k = 0; k < len; k++)
                    {
                        if (k == removeIndex) continue;
                        newArr.SetValue(list[k], j++);
                    }
                    field.SetValue(obj, newArr);
                }
                else
                {
                    list.RemoveAt(removeIndex);
                }
                changed = true;
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            EditorGUI.indentLevel = vdOldIndent;
            EditorGUILayout.Space(2);

            if (changed)
            {
                // For Array fields, add/remove/delete already assign a brand-new array
                // directly to the field (Array is fixed-size, so in-place IList mutation
                // is not possible). Element edits mutate the existing array in place, so
                // no extra write-back is needed here.
                if (field.GetCustomAttribute(typeof(OnCollectionChangedAttribute)) is OnCollectionChangedAttribute
                    collectionChangedAttribute)
                {
                    ReflectionMethodInvoker.InvokeNoArg(obj, field.DeclaringType, collectionChangedAttribute.After);
                }
            }
        }

        #endregion

        protected virtual string GetShowNameString(MemberInfo member, out bool rename)
        {
            if (member.GetCustomAttribute(typeof(LabelTextAttribute)) is LabelTextAttribute labelTextAttribute)
            {
                rename = true;
                return labelTextAttribute.Text;
            }

            rename = false;
            return ObjectNames.NicifyVariableName(member.Name);
        }

        protected static GUIContent GetCachedGUIContent(string text, string tooltip = null)
        {
            string key = text + "|" + (tooltip ?? "");
            if (!s_GuiContentCache.TryGetValue(key, out var content))
            {
                content = new GUIContent(text, tooltip);
                s_GuiContentCache[key] = content;
            }
            return content;
        }

        protected virtual GUIContent GetShowName(MemberInfo member, object value = null)
        {
            if (value != null && member is FieldInfo fieldInfo)
            {
                var valueType = value.GetType();
                if (valueType != fieldInfo.FieldType && valueType.IsClass && !valueType.IsArray &&
                    valueType != stringType && valueType != objectType
                    && valueType != dicType && valueType != listType && !valueType.IsGenericType)
                {
                    string typeLabel = GetShowNameString(valueType);
                    string tip2 = GetCachedAttr<TooltipAttribute>(member) is TooltipAttribute ta ? ta.tooltip : null;
                    return GetCachedGUIContent(typeLabel, tip2 ?? typeLabel);
                }
            }

            string tip = GetCachedAttr<TooltipAttribute>(member) is TooltipAttribute tooltipAttr ? tooltipAttr.tooltip : null;
            string showname = GetCachedAttr<LabelTextAttribute>(member) is LabelTextAttribute labelTextAttr
                ? labelTextAttr.Text
                : ObjectNames.NicifyVariableName(member.Name);
            // Prefix * to indicate tooltip presence, but only if the label doesn't already start with *
            if (!string.IsNullOrEmpty(tip) && (showname.Length == 0 || showname[0] != '*'))
                showname = "*" + showname;
            return GetCachedGUIContent(showname, tip ?? showname);
        }

        protected virtual string GetShowNameString(Type type)
        {
            if (type.GetCustomAttribute(typeof(LabelTextAttribute)) is LabelTextAttribute labelTextAttribute)
            {
                return labelTextAttribute.Text;
            }

            return ObjectNames.NicifyVariableName(type.Name);
        }

        protected virtual GUIContent GetShowName(Type type)
        {
            string tip = null;
            if (type.GetCustomAttribute(typeof(TooltipAttribute)) is TooltipAttribute tooltip)
            {
                tip = tooltip.tooltip;
            }

            string showname = null;
            if (type.GetCustomAttribute(typeof(LabelTextAttribute)) is LabelTextAttribute labelTextAttribute)
            {
                showname = labelTextAttribute.Text;
            }
            else
            {
                showname = ObjectNames.NicifyVariableName(type.Name);
            }

            return new GUIContent(showname, tip ?? showname);
        }

        /// <summary>
        /// Truncates a label string with ellipsis if it exceeds the given pixel width.
        /// </summary>
        private static string TruncateLabel(string label, float maxWidth)
        {
            if (string.IsNullOrEmpty(label) || maxWidth <= 0) return label;
            var content = new GUIContent(label);
            float fullW = EditorStyles.label.CalcSize(content).x;
            if (fullW <= maxWidth) return label;
            const string ellipsis = "…";
            float ellipsisW = EditorStyles.label.CalcSize(new GUIContent(ellipsis)).x;
            if (maxWidth <= ellipsisW) return ellipsis;
            // Binary search for max chars that fit
            int lo = 0, hi = label.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                float w = EditorStyles.label.CalcSize(new GUIContent(label.Substring(0, mid) + ellipsis)).x;
                if (w <= maxWidth) lo = mid;
                else hi = mid - 1;
            }
            return lo > 0 ? label.Substring(0, lo) + ellipsis : ellipsis;
        }

        protected virtual List<Type> GetSubClassList(FieldInfo fieldInfo, object obj, Type type, out string[] names)
        {
            if (fieldInfo.GetCustomAttribute(typeof(TypeFilterAttribute)) is TypeFilterAttribute typeFilterAttribute)
            {
                temp2.Clear();
                temp3.Clear();
                RefreshValueDropDown(fieldInfo, obj, typeFilterAttribute.FilterGetter);
                if (valueDropdown.TryGetValue(fieldInfo, out var list))
                {
                    for (int i = 0; i < list.Length; i++)
                    {
                        var val = list[i].Value;
                        if (val is Type t)
                        {
                            temp2.Add(t);
                            temp3.Add(list[i].Text);
                        }
                        else if (val != null)
                        {
                            temp2.Add(val.GetType());
                            temp3.Add(list[i].Text);
                        }
                    }

                    names = temp3.ToArray();
                    return temp2;
                }
                else
                {
                    Debug.LogError(typeFilterAttribute.FilterGetter);
                }
            }

            return TypeHelper.GetSubClassList(fieldInfo, type, out names);
        }

        protected virtual bool SelectMemberInfo(MemberInfo member, object obj, bool isDetails)
        {
            // Only accept fields, properties, and Button methods
            if (member is not FieldInfo and not PropertyInfo)
            {
                if (member is MethodInfo && GetCachedAttr<ButtonAttribute>(member) != null)
                {
                    // Button method — OK
                }
                else
                {
                    return false;
                }
            }
            // Skip event backing fields (e.g. onDeletePort)
            if (member is FieldInfo fi && (member.DeclaringType?.GetEvent(member.Name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly) != null
                || member.Name.StartsWith("add_") || member.Name.StartsWith("remove_")))
            {
                return false;
            }
            // Skip private fields unless they have [SerializeField] or a TaoTie attribute
            if (member is FieldInfo pf && !pf.IsPublic
                && !pf.IsDefined(typeof(SerializeField), true)
                && !pf.IsDefined(typeof(LabelTextAttribute), true)
                && !pf.IsDefined(typeof(ButtonAttribute), true)
                && !pf.IsDefined(typeof(ShowIfAttribute), true)
                && !pf.IsDefined(typeof(HideIfAttribute), true)
                && !pf.IsDefined(typeof(EnableIfAttribute), true)
                && !pf.IsDefined(typeof(DisableIfAttribute), true)
                && !pf.IsDefined(typeof(ReadOnlyAttribute), true)
                && !pf.IsDefined(typeof(PropertyOrderAttribute), true)
                && !pf.IsDefined(typeof(OnValueChangedAttribute), true)
                && !pf.IsDefined(typeof(OnCollectionChangedAttribute), true)
                && !pf.IsDefined(typeof(TableListAttribute), true)
                && !pf.IsDefined(typeof(ValueDropdownAttribute), true)
                && !pf.IsDefined(typeof(TypeFilterAttribute), true)
                && !pf.IsDefined(typeof(HideReferenceObjectPickerAttribute), true)
                && !pf.IsDefined(typeof(FoldoutGroupAttribute), true)
                && !pf.IsDefined(typeof(BoxGroupAttribute), true)
                && !pf.IsDefined(typeof(TabGroupAttribute), true)
                && !pf.IsDefined(typeof(OnStateUpdateAttribute), true)
                && !pf.IsDefined(typeof(InfoBoxAttribute), true)
                && !pf.IsDefined(typeof(TitleAttribute), true)
                && !pf.IsDefined(typeof(PropertySpaceAttribute), true)
                && !pf.IsDefined(typeof(PropertyRangeAttribute), true)
                && !pf.IsDefined(typeof(DisableInEditorModeAttribute), true)
                && !pf.IsDefined(typeof(MinValueAttribute), true)
                && !pf.IsDefined(typeof(MaxValueAttribute), true)
                && !pf.IsDefined(typeof(NotNullAttribute), true)
                && !pf.IsDefined(typeof(DrawIgnoreAttribute), true)
                && !pf.IsDefined(typeof(TableMatrixAttribute), true)
                && !pf.IsDefined(typeof(EnumToggleButtonsAttribute), true)
                && !pf.IsDefined(typeof(NotAssetsAttribute), true))
            {
                return false;
            }
            // Skip Unity built-in fields from ScriptableObject / UnityEngine.Object / MonoBehaviour / EditorWindow
            if (member is FieldInfo uf && (uf.DeclaringType == typeof(UnityEngine.Object)
                || uf.DeclaringType == typeof(UnityEngine.ScriptableObject)
                || uf.DeclaringType == typeof(UnityEngine.MonoBehaviour)
                || uf.DeclaringType == typeof(UnityEngine.Behaviour)
                || uf.DeclaringType == typeof(UnityEngine.Component)
                || uf.DeclaringType == typeof(UnityEditor.EditorWindow)
                || uf.DeclaringType == typeof(UnityEditor.Editor)))
            {
                return false;
            }
            if (member is PropertyInfo prop && !prop.CanWrite) return false;

            if (GetCachedAttr<HideInInspector>(member) is HideInInspector)
            {
                return false;
            }

            return true;
        }

        #region Private

        protected ISort[] GetSortMember(object obj)
        {
            var type = obj.GetType();
            if (sortsMap.TryGetValue(type, out var res))
            {
                return res;
            }

            sortTemp.Clear();
            groupsTemp.Clear();
            tabGroupKeys.Clear();
            // Get fields and Button methods in declaration order, walking from base to derived
            var memberList = new List<MemberInfo>();
            // Collect types from base to derived
            var typeHierarchy = new List<Type>();
            Type currentType = type;
            while (currentType != null && currentType != typeof(object))
            {
                typeHierarchy.Insert(0, currentType);
                currentType = currentType.BaseType;
            }
            // For each type in order, collect fields and Button methods interleaved by declaration order
            foreach (var t in typeHierarchy)
            {
                var fields = t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                var methods = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                // Collect Button methods only
                var buttonMethods = new List<MethodInfo>();
                foreach (var method in methods)
                {
                    if (method.GetCustomAttributes(typeof(ButtonAttribute), true).Length > 0)
                        buttonMethods.Add(method);
                }
                // Merge fields and Button methods by MetadataToken (declaration order)
                int fi = 0, mi = 0;
                while (fi < fields.Length || mi < buttonMethods.Count)
                {
                    if (fi >= fields.Length)
                        memberList.Add(buttonMethods[mi++]);
                    else if (mi >= buttonMethods.Count)
                        memberList.Add(fields[fi++]);
                    else if (fields[fi].MetadataToken < buttonMethods[mi].MetadataToken)
                        memberList.Add(fields[fi++]);
                    else
                        memberList.Add(buttonMethods[mi++]);
                }
            }

            int defaultOrder = 0;
            foreach (var member in memberList)
            {
                if (!SelectMemberInfo(member, obj, true)) continue;
                float sort = defaultOrder;
                if (GetCachedAttr<PropertyOrderAttribute>(member) is PropertyOrderAttribute orderAttribute)
                {
                    sort = orderAttribute.Order;
                }
                defaultOrder++;

                if (GetCachedAttr<BoxGroupAttribute>(member) is BoxGroupAttribute boxGroupAttribute)
                {
                    string groupKey = "Box:" + boxGroupAttribute.GroupName;
                    if (!groupsTemp.TryGetValue(groupKey, out var groupItem))
                    {
                        groupItem = new GroupItem()
                        {
                            MinSort = sort,
                            GroupId = boxGroupAttribute.GroupName,
                            GroupKey = groupKey,
                            Members = new()
                        };
                        sortTemp.Add(groupItem);
                        groupsTemp.Add(groupKey, groupItem);
                    }

                    groupItem.Members.Add(new MemberItem() {Member = member, MinSort = sort, cachedAttributes = (Attribute[])member.GetCustomAttributes(typeof(Attribute), true)});
                    if (sort < groupItem.MinSort)
                    {
                        groupItem.MinSort = sort;
                    }
                }
                else if (GetCachedAttr<FoldoutGroupAttribute>(member) is FoldoutGroupAttribute foldoutAttr)
                {
                    string groupKey = "Fold:" + foldoutAttr.GroupName;
                    if (!groupsTemp.TryGetValue(groupKey, out var groupItem))
                    {
                        groupItem = new GroupItem()
                        {
                            MinSort = sort,
                            GroupId = foldoutAttr.GroupName,
                            GroupKey = groupKey,
                            Members = new()
                        };
                        sortTemp.Add(groupItem);
                        groupsTemp.Add(groupKey, groupItem);
                    }

                    groupItem.Members.Add(new MemberItem() {Member = member, MinSort = sort, cachedAttributes = (Attribute[])member.GetCustomAttributes(typeof(Attribute), true)});
                    if (sort < groupItem.MinSort)
                    {
                        groupItem.MinSort = sort;
                    }
                }
                else if (GetCachedAttr<TabGroupAttribute>(member) is TabGroupAttribute tabAttr)
                {
                    string groupKey = "Tab:" + tabAttr.GroupName + "/" + tabAttr.TabName;
                    string tabGroupKey = "Tab:" + tabAttr.GroupName;
                    if (!groupsTemp.TryGetValue(groupKey, out var groupItem))
                    {
                        groupItem = new GroupItem()
                        {
                            MinSort = sort,
                            GroupId = tabAttr.GroupName + " / " + tabAttr.TabName,
                            GroupKey = groupKey,
                            Members = new()
                        };
                        sortTemp.Add(groupItem);
                        groupsTemp.Add(groupKey, groupItem);
                        if (!tabGroupKeys.Contains(tabGroupKey))
                            tabGroupKeys.Add(tabGroupKey);
                    }

                    groupItem.Members.Add(new MemberItem() {Member = member, MinSort = sort, cachedAttributes = (Attribute[])member.GetCustomAttributes(typeof(Attribute), true)});
                    if (sort < groupItem.MinSort)
                    {
                        groupItem.MinSort = sort;
                    }
                }
                else
                {
                    sortTemp.Add(new MemberItem() {Member = member, MinSort = sort, cachedAttributes = (Attribute[])member.GetCustomAttributes(typeof(Attribute), true)});
                }
            }

            sortTemp.Sort(SortAb);
            foreach (var kv in groupsTemp)
            {
                kv.Value.Members.Sort(SortAb);
            }

            res = sortTemp.ToArray();
            sortsMap.Add(type, res);
            return res;
        }

        protected virtual void RefreshValueDropDown(FieldInfo field, object obj, string valuesGetter)
        {
            Type type = obj.GetType();
            string funcName = valuesGetter;
            string[] paras = null;
            object[] paraData = null;
            if (funcName.StartsWith("@"))
            {
                funcName = funcName.Replace("@", "");
                if (funcName.Contains("."))
                {
                    var vs = funcName.Split(".");
                    type = TypeHelper.FindType(vs[0]);
                    funcName = vs[1];
                }

                if (funcName.EndsWith(")"))
                {
                    if (funcName.EndsWith("()"))
                    {
                        funcName = funcName.Replace("()", "");
                    }
                    else
                    {
                        var index = funcName.IndexOf("(");
                        var paraStr = funcName.Substring(index + 1, funcName.Length - index - 2);
                        funcName = funcName.Substring(0, index);
                        paras = paraStr.Split(",");
                        paraData = new object[paras.Length];
                    }
                }
            }

            if (type != null)
            {
                if (paras != null)
                {
                    for (int i = 0; i < paras.Length; i++)
                    {
                        if (paras[i].EndsWith("()"))
                        {
                            var methodItem = obj.GetType().GetMethod(paras[i].Replace("()", ""),
                                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                BindingFlags.Static);
                            if (methodItem != null)
                            {
                                if (methodItem.IsStatic)
                                {
                                    paraData[i] = methodItem.Invoke(null, null);
                                }
                                else
                                {
                                    paraData[i] = methodItem.Invoke(obj, null);
                                }
                            }
                        }
                        else
                        {
                            var fieldItem = obj.GetType().GetField(paras[i],
                                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                BindingFlags.Static);
                            if (fieldItem != null)
                            {
                                paraData[i] = fieldItem.GetValue(obj);
                            }

                            var propItem = obj.GetType().GetProperty(paras[i],
                                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                BindingFlags.Static);
                            if (propItem != null && propItem.CanRead)
                            {
                                paraData[i] = propItem.GetValue(obj);
                            }
                        }
                    }
                }

                var method = type.GetMethod(funcName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                if (method != null)
                {
                    if (!valueDropdown.TryGetValue(field, out var list) || (paraData != null && paraData.Length > 0))
                    {
                        IEnumerable data;
                        if (method.IsStatic)
                        {
                            data = method.Invoke(null, paraData) as IEnumerable;
                        }
                        else
                        {
                            data = method.Invoke(obj, paraData) as IEnumerable;
                        }

                        if (data == null) return;
                        temp.Clear();
                        foreach (var item in data)
                        {
                            // Handles TaoTie's ValueDropdownItem, IValueDropdownItem,
                            // ValueDropdownItem<T>, and Odin's equivalent types by name
                            if (OdinCompat.TryConvertToValueDropdownItem(item, out var converted))
                            {
                                temp.Add(converted);
                            }
                            else if (item is Type typ &&
                                typ.GetCustomAttribute(typeof(LabelTextAttribute)) is LabelTextAttribute
                                    labelTextAttribute)
                            {
                                temp.Add(new ValueDropdownItem(labelTextAttribute.Text, item));
                            }
                            else
                            {
                                temp.Add(new ValueDropdownItem(item?.ToString() ?? "null", item));
                            }
                        }

                        list = temp.ToArray();
                        valueDropdown[field] = list;
                    }
                }
            }
        }

        private bool CheckCondition(MemberInfo member, object obj, string[] members, object expectedValue)
        {
            if (members == null || members.Length == 0) return true;

            // Single @-expression or single member with value comparison
            if (members.Length == 1)
            {
                string condition = members[0];

                // @-expression: delegate to TaoTieExpressionEvaluator for full &&, ||, !, == support
                if (condition.StartsWith("@"))
                {
                    return TaoTieExpressionEvaluator.Evaluate(condition, obj);
                }

                // Single member name with expected value → equality check
                if (expectedValue != null)
                {
                    return CheckMemberEquals(obj, condition, expectedValue);
                }

                // Single member name → bool check
                return CheckMemberBool(obj, condition);
            }

            // Multiple members: default to And (matching ShowIf(ConditionOperator.And, ...) behavior)
            foreach (string m in members)
            {
                if (m.StartsWith("@"))
                {
                    if (!TaoTieExpressionEvaluator.Evaluate(m, obj)) return false;
                }
                else if (!CheckMemberBool(obj, m))
                {
                    return false;
                }
            }
            return true;
        }

        private bool CheckMemberBool(object obj, string memberName)
        {
            if (obj == null || string.IsNullOrEmpty(memberName)) return true;
            Type type = obj.GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

            var field = type.GetField(memberName, flags);
            if (field != null)
            {
                var val = field.IsStatic ? field.GetValue(null) : field.GetValue(obj);
                return ToBool(val);
            }

            var prop = type.GetProperty(memberName, flags);
            if (prop != null && prop.CanRead)
            {
                var val = prop.GetGetMethod(true).IsStatic ? prop.GetValue(null, null) : prop.GetValue(obj, null);
                return ToBool(val);
            }

            var method = type.GetMethod(memberName, flags);
            if (method != null && method.ReturnType == typeof(bool) && method.GetParameters().Length == 0)
            {
                var val = method.IsStatic ? method.Invoke(null, null) : method.Invoke(obj, null);
                return (bool)val;
            }

            return true;
        }

        private bool CheckMemberEquals(object obj, string memberName, object expectedValue)
        {
            if (obj == null || string.IsNullOrEmpty(memberName)) return true;
            Type type = obj.GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

            object actualValue = null;
            var field = type.GetField(memberName, flags);
            if (field != null)
                actualValue = field.IsStatic ? field.GetValue(null) : field.GetValue(obj);
            else
            {
                var prop = type.GetProperty(memberName, flags);
                if (prop != null && prop.CanRead)
                    actualValue = prop.GetGetMethod(true).IsStatic ? prop.GetValue(null, null) : prop.GetValue(obj, null);
            }

            return IsEqual(actualValue, expectedValue);
        }

        private static bool ToBool(object value)
        {
            if (value is bool b) return b;
            if (value == null) return false;
            try { return Convert.ToBoolean(value); }
            catch { return value != null; }
        }

        private object DrawEnumToggleButtons(Type enumType, GUIContent showName, object value)
        {
            var values = Enum.GetValues(enumType);
            var names = Enum.GetNames(enumType);
            bool isFlags = enumType.IsDefined(typeof(FlagsAttribute), false);

            if (showName != null)
                EditorGUILayout.PrefixLabel(showName);

            EditorGUILayout.BeginVertical();
            int maxPerRow = 6;
            int rows = Mathf.CeilToInt((float)values.Length / maxPerRow);
            for (int row = 0; row < rows; row++)
            {
                EditorGUILayout.BeginHorizontal();
                int start = row * maxPerRow;
                int end = Mathf.Min(start + maxPerRow, values.Length);
                for (int i = start; i < end; i++)
                {
                    bool isActive;
                    if (isFlags)
                    {
                        long currentVal = Convert.ToInt64(value);
                        long flagVal = Convert.ToInt64(values.GetValue(i));
                        isActive = flagVal == 0 ? currentVal == 0 : (currentVal & flagVal) == flagVal;
                    }
                    else
                    {
                        isActive = Convert.ToInt64(value) == Convert.ToInt64(values.GetValue(i));
                    }
                    bool newActive = GUILayout.Toggle(isActive, names[i], EditorStyles.miniButton);
                    if (newActive != isActive)
                    {
                        if (isFlags)
                        {
                            long currentVal = Convert.ToInt64(value);
                            long flagVal = Convert.ToInt64(values.GetValue(i));
                            if (flagVal == 0)
                                value = Enum.ToObject(enumType, 0);
                            else if (newActive)
                                value = Enum.ToObject(enumType, currentVal | flagVal);
                            else
                                value = Enum.ToObject(enumType, currentVal & ~flagVal);
                        }
                        else
                        {
                            if (newActive) value = values.GetValue(i);
                        }
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
            return value;
        }

        private bool IsEqual(object a, object b)
        {
            if (a == null && b == null) return true;
            if (a != null && b != null)
            {
                return a.Equals(b);
            }

            return false;
        }

        private int SortAb(ISort a, ISort b)
        {
            if (a.MinSort != b.MinSort)
            {
                return a.MinSort - b.MinSort > 0 ? 1 : -1;
            }

            // Same Order: stable sort by declaration order (MetadataToken).
            // Group items use their first member's token as the representative,
            // so the comparison is total and never returns 0 for distinct items.
            int ta = GetRepresentativeToken(a);
            int tb = GetRepresentativeToken(b);
            return ta.CompareTo(tb);
        }

        private static int GetRepresentativeToken(ISort item)
        {
            if (item is MemberItem mi)
            {
                return mi.Member.MetadataToken;
            }

            if (item is GroupItem gi && gi.Members.Count > 0)
            {
                return gi.Members[0].Member.MetadataToken;
            }

            return 0;
        }

        #endregion
    }
}