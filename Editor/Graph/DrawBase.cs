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
        private static List<ISort> sortTemp = new();
        private static Dictionary<string, GroupItem> groupsTemp = new();
        private static List<string> tabGroupKeys = new();

        // Per-table column widths — key: field name + object hash, value: column widths
        private static readonly Dictionary<string, float[]> s_TableColumnWidths = new();
        private static string s_DraggingTableKey;
        private static int s_DraggingColumnIndex = -1;

        private static Type stringType = typeof(string);
        private static Type listType = typeof(List<>);
        private static Type dicType = typeof(Dictionary<,>);
        private static Type objectType = typeof(UnityEngine.Object);

        private static Dictionary<Type, ISort[]> sortsMap = new();
        private static Dictionary<MemberInfo, Attribute[]> memberAttrCache = new();

        /// <summary>Actual available width for layout (set by DrawObjectInspector from GUILayout context)</summary>
        protected static float s_AvailableWidth = 0f;

        /// <summary>Set the actual available width for box+grid layout calculations.</summary>
        public static void SetAvailableWidth(float width)
        {
            s_AvailableWidth = width;
        }

        private static T GetCachedAttr<T>(MemberInfo member) where T : Attribute
        {
            if (!memberAttrCache.TryGetValue(member, out var attrs))
            {
                attrs = (Attribute[])member.GetCustomAttributes(typeof(Attribute), true);
                memberAttrCache[member] = attrs;
            }
            for (int i = 0; i < attrs.Length; i++)
            {
                if (attrs[i] is T t) return t;
            }
            return null;
        }

        protected static HashSet<FieldInfo> foldoutState = new();
        protected HashSet<GroupItem> foldoutState2 = new();
        protected HashSet<string> drawnTabGroups = new();

        // Flag to control foldout x offset: Graph uses 4f, Mono/ScriptObject uses 14f
        protected static float s_FoldoutXOffset = 14f;
        public static void SetFoldoutXOffset(float offset) => s_FoldoutXOffset = offset;

        private static Dictionary<FieldInfo, HashSet<int>> listFoldoutState = new();
        private static Dictionary<FieldInfo, object> dicInputKey = new();
        private static Dictionary<Type, string[]> enumDropDown = new();

        protected static Dictionary<FieldInfo, ValueDropdownItem[]> valueDropdown = new();

        protected static List<ValueDropdownItem> temp = new();
        private static List<Type> temp2 = new();
        private static List<string> temp3 = new();

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
            // If not set, fall back to currentViewWidth.
            if (s_AvailableWidth <= 0)
                s_AvailableWidth = EditorGUIUtility.currentViewWidth - 40f;

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
            var data = new GroupEntryData { UserData = mi.Member };
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
                    if (dia.Ignore == Ignore.All) data.Visible = false;
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
                    bool enabled = CheckCondition(member, obj, new[] { enableIfAttr.Member }, enableIfAttr.Value);
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
                        bool disabled = CheckCondition(member, obj, new[] { disableIfAttr.Member }, disableIfAttr.Value);
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
                if (GetCachedAttr<OnValueChangedAttribute>(field) is OnValueChangedAttribute
                    valueChangedAttribute)
                {
                    value = field.GetValue(obj);
                    attribute = valueChangedAttribute;
                }

                DrawFieldInspector(field, obj, isDetails);
                if (attribute != null)
                {
                    var newValue = field.GetValue(obj);
                    if (!IsEqual(newValue, value))
                    {
                        ReflectionMethodInvoker.InvokeNoArg(obj, field.DeclaringType, attribute.MethodName);
                    }
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
            if (GetCachedAttr<DrawIgnoreAttribute>(member) is DrawIgnoreAttribute ignoreAttribute)
            {
                if (ignoreAttribute.Ignore == Ignore.All) return false;
                if (ignoreAttribute.Ignore == Ignore.Details == isDetails) return false;
            }

            // AllowMultiple: all ShowIf must pass (AND)
            var showIfs = member.GetCustomAttributes<ShowIfAttribute>(true);
            foreach (var showIfAttribute in showIfs)
            {
                if (!CheckCondition(member, obj, new[] { showIfAttribute.Member }, showIfAttribute.Value)) return false;
            }

            var hideIfs = member.GetCustomAttributes<HideIfAttribute>(true);
            foreach (var hideIfAttribute in hideIfs)
            {
                if (CheckCondition(member, obj, new[] { hideIfAttribute.Member }, hideIfAttribute.Value)) return false;
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
                bool foldout = foldoutState.Contains(field);
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
                foldout = EditorGUI.Foldout(actualFoldRect, foldout, foldoutLabel, true);
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
                    foldoutState.Add(field);
                }
                else
                {
                    foldoutState.Remove(field);
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
                if (typeof(Sprite).IsAssignableFrom(type) || typeof(Texture).IsAssignableFrom(type))
                {
                    UnityEngine.Object newObj;
                    if (showName == null) newObj = EditorGUILayout.ObjectField((UnityEngine.Object) value, type, false);
                    else newObj = EditorGUILayout.ObjectField(showName, (UnityEngine.Object) value, type, false);
                    value = newObj;
                }
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
            float availableWidth = s_AvailableWidth > 0 ? s_AvailableWidth : EditorGUIUtility.currentViewWidth - 40f;
            float defaultColW = colCount > 0 ? (availableWidth - indexColW - deleteColW) / colCount : 0f;
            if (defaultColW < 50f) defaultColW = 50f;

            string tableKey = field.Name + "_" + obj.GetHashCode();

            // Get or init column widths
            float[] colWidths;
            if (!s_TableColumnWidths.TryGetValue(tableKey, out colWidths) || colWidths.Length != colCount)
            {
                colWidths = new float[colCount];
                for (int i = 0; i < colCount; i++) colWidths[i] = defaultColW;
                s_TableColumnWidths[tableKey] = colWidths;
            }

            // Handle column drag
            var ev = Event.current;
            if (ev != null && s_DraggingTableKey == tableKey && s_DraggingColumnIndex >= 0 && s_DraggingColumnIndex < colCount)
            {
                if (ev.type == EventType.MouseDrag)
                {
                    colWidths[s_DraggingColumnIndex] = Mathf.Max(30f, colWidths[s_DraggingColumnIndex] + ev.delta.x);
                    ev.Use();
                }
                if (ev.type == EventType.MouseUp)
                {
                    s_DraggingTableKey = null;
                    s_DraggingColumnIndex = -1;
                    ev.Use();
                }
            }

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
                float hx = headerRect.x;
                // Index column header
                EditorGUI.LabelField(new Rect(hx, headerRect.y, indexColW, headerRect.height), "#", EditorStyles.boldLabel);
                hx += indexColW;
                // Column headers with drag handles
                for (int c = 0; c < colCount; c++)
                {
                    float cw = colWidths[c];
                    EditorGUI.LabelField(new Rect(hx, headerRect.y, cw - dragHandleW, headerRect.height),
                        ObjectNames.NicifyVariableName(columnNames[c]), EditorStyles.boldLabel);
                    // Drag handle area
                    Rect handleRect = new Rect(hx + cw - dragHandleW, headerRect.y, dragHandleW, headerRect.height);
                    EditorGUI.DrawRect(handleRect, new Color(0.5f, 0.5f, 0.5f, 0.3f));
                    EditorGUIUtility.AddCursorRect(handleRect, MouseCursor.ResizeHorizontal);
                    if (ev != null && ev.type == EventType.MouseDown && handleRect.Contains(ev.mousePosition))
                    {
                        s_DraggingTableKey = tableKey;
                        s_DraggingColumnIndex = c;
                        ev.Use();
                    }
                    hx += cw;
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
                            int selIdx = EditorGUI.Popup(new Rect(dx, rowRect.y, colWidths.Length > 0 ? colWidths[0] : defaultColW, rowRect.height), -1, names);
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
                        Rect primRect = new Rect(dx, rowRect.y, colWidths.Length > 0 ? colWidths[0] : defaultColW, rowRect.height);
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
            foldout = EditorGUI.Foldout(foldRect, foldout, new GUIContent(title), true);
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
                    else
                    {
                        // Reference type — foldout + SetNull + field
                        if (item == null)
                        {
                            Rect popRect = new Rect(x, rowRect.y, fieldColW, rowRect.height);
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
                            if (!listFoldoutState.TryGetValue(field, out var foldSet))
                            {
                                foldSet = new HashSet<int>();
                                listFoldoutState[field] = foldSet;
                            }
                            bool subFoldState = foldSet.Contains(i);
                            Rect subFoldRect = new Rect(x, rowRect.y, fieldColW - setNullColW - 2f, rowRect.height);
                            subFold = EditorGUI.Foldout(subFoldRect, subFoldState, GetShowName(item.GetType()));
                            Rect snRect = new Rect(x + fieldColW - setNullColW - 2f, rowRect.y, setNullColW, rowRect.height);
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
            string dicTitle = (GetShowName(field)?.text ?? ObjectNames.NicifyVariableName(field.Name)) + $" ({dictionary.Count})";
            dicFoldout = EditorGUI.Foldout(new Rect(dicTitleRect.x + s_FoldoutXOffset, dicTitleRect.y, dicTitleRect.width - s_FoldoutXOffset - 4f, dicTitleRect.height),
                dicFoldout, new GUIContent(dicTitle), true);
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
                    bool subFoldout = listFoldoutState.TryGetValue(field, out var foldSet) && foldSet.Contains(rowIdx * 2 + 1);
                    var foldRect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
                    subFoldout = EditorGUI.Foldout(foldRect, subFoldout, "  └ " + item.GetType().Name + " Details");
                    if (!listFoldoutState.TryGetValue(field, out foldSet))
                    {
                        foldSet = new HashSet<int>();
                        listFoldoutState[field] = foldSet;
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
            if (keyType.IsValueType || keyType == stringType)
            {
                if (!dicInputKey.TryGetValue(field, out inputKey))
                {
                    inputKey = keyType == stringType ? "" : Activator.CreateInstance(keyType);
                    dicInputKey.Add(field, inputKey);
                }
                var newKey = inputKey;
                Rect keyInputRect = new Rect(addRect.x, addRect.y, addKeyW, addRect.height);
                if (keyType == typeof(string))
                    newKey = EditorGUI.TextField(keyInputRect, (string)newKey);
                else if (keyType == typeof(int))
                    newKey = EditorGUI.IntField(keyInputRect, (int)newKey);
                else if (keyType == typeof(float))
                    newKey = EditorGUI.FloatField(keyInputRect, (float)newKey);
                else
                    newKey = EditorGUI.TextField(keyInputRect, newKey?.ToString() ?? "");
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
                // No dropdown items — fallback to default list drawing
                EditorGUILayout.LabelField(GetShowName(field)?.text ?? field.Name, EditorStyles.boldLabel);
                return;
            }

            bool changed = false;
            int removeIndex = -1;
            int len = list.Count;
            var showName = GetShowName(field);
            string title = showName?.text ?? ObjectNames.NicifyVariableName(field.Name);
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
            vdFoldout = EditorGUI.Foldout(vdFoldRect, vdFoldout, title, true);
            SessionState.SetBool(vdFoldKey, vdFoldout);
            EditorGUI.LabelField(vdCountRect, vdCountContent, EditorStyles.miniLabel);
            if (GUI.Button(new Rect(vdPlusX, vdTitleRect.y, 24f, vdTitleRect.height), "+", EditorStyles.toolbarButton))
            {
                list.Add(itemType.IsValueType ? Activator.CreateInstance(itemType) : null);
                changed = true;
            }
            if (GUI.Button(new Rect(vdMinusX, vdTitleRect.y, 24f, vdTitleRect.height), "-", EditorStyles.toolbarButton))
            {
                if (len > 0) { list.RemoveAt(len - 1); changed = true; }
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
                list.RemoveAt(removeIndex);
                changed = true;
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            EditorGUI.indentLevel = vdOldIndent;
            EditorGUILayout.Space(2);

            if (changed)
            {
                // Sync back to field for Array (IList adapter is the array itself for List)
                if (field.FieldType.IsArray)
                    field.SetValue(obj, list);
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
            if (!string.IsNullOrEmpty(tip))
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

            // Filter out DrawIgnore(All) members early
            if (GetCachedAttr<DrawIgnoreAttribute>(member) is DrawIgnoreAttribute ignoreAttr
                && ignoreAttr.Ignore == Ignore.All)
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
                            if (item is ValueDropdownItem vdi)
                            {
                                temp.Add(vdi);
                            }
                            else if (item is IValueDropdownItem valueDropdownItem)
                            {
                                temp.Add(new ValueDropdownItem(valueDropdownItem.GetText(), valueDropdownItem.GetValue()));
                            }
                            else
                            {
                                // Handle ValueDropdownItem<T> via reflection
                                var itemType = item?.GetType();
                                if (itemType != null && itemType.IsGenericType &&
                                    itemType.GetGenericTypeDefinition() == typeof(ValueDropdownItem<>))
                                {
                                    var textField = itemType.GetField("Text");
                                    var valueField = itemType.GetField("Value");
                                    temp.Add(new ValueDropdownItem(
                                        textField?.GetValue(item)?.ToString() ?? "",
                                        valueField?.GetValue(item)));
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
            if (a.MinSort == b.MinSort)
            {
                if (a is MemberItem ma && b is MemberItem mb)
                {
                    return ma.Member.MemberType - mb.Member.MemberType;
                }

                return 0;
            }

            return a.MinSort - b.MinSort > 0 ? 1 : -1;
        }

        #endregion
    }
}