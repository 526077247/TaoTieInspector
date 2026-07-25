using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace TaoTie.Inspector.Editor
{
    public class TaoTiePropertyProcessor
    {
        private readonly Dictionary<Type, List<TaoTiePropertyEntry>> entryCache = new();
        private readonly Dictionary<Type, bool> hasTaoTieAttrCache = new();
        private readonly Dictionary<Type, List<MethodInfo>> buttonMethodCache = new();
        private readonly Dictionary<Type, FieldInfo[]> fieldCache = new();

        // Cached hidden paths — rebuilt only when entry list reference changes
        private List<TaoTiePropertyEntry> lastRefreshedEntries;
        private string[] cachedHiddenPaths;

        public List<TaoTiePropertyEntry> BuildEntries(SerializedObject so)
        {
            Type targetType = so.targetObject.GetType();
            if (entryCache.TryGetValue(targetType, out List<TaoTiePropertyEntry> cached))
                return cached;

            var entries = new List<TaoTiePropertyEntry>();
            var fieldMap = BuildFieldMap(targetType);

            SerializedProperty iter = so.GetIterator();
            if (!iter.NextVisible(true)) return entries;

            int defaultOrder = 0;
            do
            {
                if (iter.propertyPath == "m_Script") continue;

                var entry = CreateEntry(iter, fieldMap);

                // For nested [Serializable] objects, draw as a foldout group
                // But NOT for array elements (handled by DrawArrayBox) or Vector2/3/4/Color etc.
                bool isStructWithChildren = iter.propertyType == SerializedPropertyType.Generic && iter.hasVisibleChildren
                    && !iter.isArray
                    && !iter.propertyPath.Contains(".Array.data[");
                // Check if this is actually a Generic type (not Vector2/3/4/Color/etc.)
                // Vector2/3/4 have propertyType == Vector2/Vector3/Vector4, NOT Generic
                // But we also need to skip drawing their children (.x, .y, .z) separately
                bool isMultiComponentField = iter.propertyType == SerializedPropertyType.Vector2
                    || iter.propertyType == SerializedPropertyType.Vector3
                    || iter.propertyType == SerializedPropertyType.Vector4
                    || iter.propertyType == SerializedPropertyType.Vector2Int
                    || iter.propertyType == SerializedPropertyType.Vector3Int
                    || iter.propertyType == SerializedPropertyType.Rect
                    || iter.propertyType == SerializedPropertyType.Bounds
                    || iter.propertyType == SerializedPropertyType.Quaternion
                    || iter.propertyType == SerializedPropertyType.Color
                    || iter.propertyType == SerializedPropertyType.Gradient;

                if (isStructWithChildren && !isMultiComponentField)
                {
                    entry.IsFoldoutGroup = true;
                    entry.FoldoutGroupName = entry.LabelOverride ?? ObjectNames.NicifyVariableName(iter.name);
                    entry.FoldoutExpanded = SessionState.GetBool("TaoTie_Fold_" + iter.propertyPath, true);
                }

                if (entry.Order == 0 && !HasPropertyOrderFromPath(fieldMap, iter))
                    entry.Order = defaultOrder;
                entries.Add(entry);
                defaultOrder++;
            } while (iter.NextVisible(true));

            entries.Sort((a, b) => a.Order.CompareTo(b.Order));
            entryCache[targetType] = entries;
            return entries;
        }

        public void RefreshDynamicState(List<TaoTiePropertyEntry> entries, object target)
        {
            // Cache hidden paths — rebuild only when the entry list reference changes
            // (entry list is cached by type in BuildEntries, so this is stable across frames)
            if (cachedHiddenPaths == null || lastRefreshedEntries != entries)
            {
                var hiddenSet = new HashSet<string>();
                foreach (var entry in entries)
                {
                    try
                    {
                        var pt = entry.Property?.propertyType ?? SerializedPropertyType.Generic;
                        bool isMultiComponent = (pt == SerializedPropertyType.Vector2 || pt == SerializedPropertyType.Vector3
                             || pt == SerializedPropertyType.Vector4 || pt == SerializedPropertyType.Vector2Int
                             || pt == SerializedPropertyType.Vector3Int || pt == SerializedPropertyType.Rect
                             || pt == SerializedPropertyType.Bounds || pt == SerializedPropertyType.Quaternion
                             || pt == SerializedPropertyType.Color || pt == SerializedPropertyType.Gradient);
                        bool isArray = entry.Property != null && entry.Property.isArray && entry.TableList == null;

                        if ((isMultiComponent || isArray) && entry.PropertyPath != null)
                        {
                            hiddenSet.Add(entry.PropertyPath + ".");
                        }
                    }
                    catch { /* property may be disposed during managed reference changes */ }
                }
                cachedHiddenPaths = new string[hiddenSet.Count];
                hiddenSet.CopyTo(cachedHiddenPaths);
                lastRefreshedEntries = entries;
            }

            // Single pass: check visibility + enabled
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                bool isHiddenChild = false;
                if (entry.PropertyPath != null && cachedHiddenPaths.Length > 0)
                {
                    for (int j = 0; j < cachedHiddenPaths.Length; j++)
                    {
                        if (entry.PropertyPath.StartsWith(cachedHiddenPaths[j]))
                        {
                            isHiddenChild = true;
                            break;
                        }
                    }
                }

                if (isHiddenChild)
                {
                    entry.Visible = false;
                }
                else
                {
                    try
                    {
                        entry.Visible = entry.IsVisible(target);
                    }
                    catch { entry.Visible = false; }
                }

                try
                {
                    entry.Enabled = entry.IsEnabled(target);
                }
                catch { entry.Enabled = true; }

                if (entry.IsFoldoutGroup)
                {
                    entry.FoldoutExpanded = SessionState.GetBool("TaoTie_Fold_" + entry.PropertyPath, true);
                }
            }
        }

        public static bool HasAnyTaoTieAttributes(Type type)
        {
            return HasAnyTaoTieAttributesInternal(type, new HashSet<Type>());
        }

        private static bool HasAnyTaoTieAttributesInternal(Type type, HashSet<Type> visited)
        {
            if (type.IsDefined(typeof(DrawWithUnityAttribute), true))
                return false;
            if (!visited.Add(type))
                return false; // already checked, prevent infinite recursion

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var field in type.GetFields(flags))
            {
                if (CheckFieldHasTaoTieAttr(field)) return true;

                // Recurse into nested class fields (with or without [Serializable])
                if (field.FieldType.IsClass
                    && field.FieldType != typeof(string)
                    && !field.FieldType.IsArray
                    && !typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType)
                    && !field.FieldType.IsGenericType
                    && !field.FieldType.IsAbstract)
                {
                    if (HasAnyTaoTieAttributesInternal(field.FieldType, visited)) return true;
                }
            }

            foreach (var method in type.GetMethods(flags))
            {
                if (method.IsDefined(typeof(ButtonAttribute), true)) return true;
            }

            return false;
        }

        private static bool CheckFieldHasTaoTieAttr(FieldInfo field)
        {
            if (field.IsDefined(typeof(LabelTextAttribute), true)) return true;
            if (field.IsDefined(typeof(ShowIfAttribute), true)) return true;
            if (field.IsDefined(typeof(HideIfAttribute), true)) return true;
            if (field.IsDefined(typeof(EnableIfAttribute), true)) return true;
            if (field.IsDefined(typeof(DisableIfAttribute), true)) return true;
            if (field.IsDefined(typeof(ReadOnlyAttribute), true)) return true;
            if (field.IsDefined(typeof(PropertyOrderAttribute), true)) return true;
            if (field.IsDefined(typeof(TitleAttribute), true)) return true;
            if (field.IsDefined(typeof(InfoBoxAttribute), true)) return true;
            if (field.IsDefined(typeof(PropertySpaceAttribute), true)) return true;
            if (field.IsDefined(typeof(PropertyRangeAttribute), true)) return true;
            if (field.IsDefined(typeof(FoldoutGroupAttribute), true)) return true;
            if (field.IsDefined(typeof(BoxGroupAttribute), true)) return true;
            if (field.IsDefined(typeof(TabGroupAttribute), true)) return true;
            if (field.IsDefined(typeof(HorizontalGroupAttribute), true)) return true;
            if (field.IsDefined(typeof(EnumToggleButtonsAttribute), true)) return true;
            if (field.IsDefined(typeof(ValueDropdownAttribute), true)) return true;
            if (field.IsDefined(typeof(OnValueChangedAttribute), true)) return true;
            if (field.IsDefined(typeof(ButtonAttribute), true)) return true;
            if (field.IsDefined(typeof(DrawIgnoreAttribute), true)) return true;
            if (field.IsDefined(typeof(DisableInEditorModeAttribute), true)) return true;
            if (field.IsDefined(typeof(MinValueAttribute), true)) return true;
            if (field.IsDefined(typeof(MaxValueAttribute), true)) return true;
            if (field.IsDefined(typeof(NotAssetsAttribute), true)) return true;
            if (field.IsDefined(typeof(OnStateUpdateAttribute), true)) return true;
            if (field.IsDefined(typeof(OnCollectionChangedAttribute), true)) return true;
            if (field.IsDefined(typeof(TypeFilterAttribute), true)) return true;
            if (field.IsDefined(typeof(HideReferenceObjectPickerAttribute), true)) return true;
            if (field.IsDefined(typeof(TableListAttribute), true)) return true;
            if (field.IsDefined(typeof(TableMatrixAttribute), true)) return true;
            if (field.IsDefined(typeof(NotNullAttribute), true)) return true;
            return false;
        }

        public List<MethodInfo> GetButtonMethods(Type type)
        {
            if (buttonMethodCache.TryGetValue(type, out List<MethodInfo> cached))
                return cached;

            var result = new List<MethodInfo>();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var method in type.GetMethods(flags))
            {
                if (method.IsDefined(typeof(ButtonAttribute), true))
                    result.Add(method);
            }

            buttonMethodCache[type] = result;
            return result;
        }

        /// <summary>
        /// Gets the first custom attribute of type T, avoiding AmbiguousMatchException
        /// when multiple attributes of the same type exist (e.g. multiple [Title] on one field).
        /// </summary>
        private static T GetFirstAttr<T>(FieldInfo field) where T : Attribute
        {
            var attrs = field.GetCustomAttributes(typeof(T), true);
            if (attrs != null && attrs.Length > 0)
                return attrs[0] as T;
            return null;
        }

        private FieldInfo ResolveFieldFromPath(SerializedProperty prop, Dictionary<string, FieldInfo> rootFieldMap)
        {
            // Simple case: top-level field
            if (!prop.propertyPath.Contains('.'))
            {
                rootFieldMap.TryGetValue(prop.name, out FieldInfo field);
                return field;
            }

            // Nested path: traverse parts, skipping Unity array internals (Array, data[N])
            var parts = new List<string>();
            foreach (var part in prop.propertyPath.Split('.'))
            {
                if (part == "Array") continue;
                if (part.StartsWith("data[")) continue;
                parts.Add(part);
            }

            if (parts.Count == 0) return null;
            // Top-level field
            if (!rootFieldMap.TryGetValue(parts[0], out FieldInfo result)) return null;
            if (parts.Count == 1) return result;

            Type currentType = result.FieldType;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            for (int i = 1; i < parts.Count; i++)
            {
                if (currentType == null) return null;
                // Strip array/list element type before looking up field
                if (currentType.IsArray)
                    currentType = currentType.GetElementType();
                else if (currentType.IsGenericType && currentType.GetGenericTypeDefinition() == typeof(List<>))
                    currentType = currentType.GetGenericArguments()[0];

                result = currentType.GetField(parts[i], flags);
                Type baseType = currentType.BaseType;
                while (result == null && baseType != null && baseType != typeof(object))
                {
                    result = baseType.GetField(parts[i], flags);
                    baseType = baseType.BaseType;
                }
                if (result == null) return null;
                currentType = result.FieldType;
            }

            return result;
        }

        public FieldInfo GetField(Type type, string fieldName)
        {
            if (!fieldCache.TryGetValue(type, out FieldInfo[] fields))
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                fields = type.GetFields(flags);
                fieldCache[type] = fields;
            }

            foreach (var f in fields)
            {
                if (f.Name == fieldName) return f;
            }

            // Try base type
            if (type.BaseType != null && type.BaseType != typeof(object))
                return GetField(type.BaseType, fieldName);

            return null;
        }

        private Dictionary<string, FieldInfo> BuildFieldMap(Type type)
        {
            var map = new Dictionary<string, FieldInfo>();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            Type current = type;
            while (current != null && current != typeof(object))
            {
                foreach (var field in current.GetFields(flags))
                {
                    if (!map.ContainsKey(field.Name))
                        map[field.Name] = field;
                }
                current = current.BaseType;
            }

            return map;
        }

        private bool HasPropertyOrderFromPath(Dictionary<string, FieldInfo> rootFieldMap, SerializedProperty prop)
        {
            FieldInfo field = ResolveFieldFromPath(prop, rootFieldMap);
            return field != null && field.IsDefined(typeof(PropertyOrderAttribute), true);
        }

        private bool HasPropertyOrder(Dictionary<string, FieldInfo> fieldMap, string propertyName)
        {
            if (fieldMap.TryGetValue(propertyName, out FieldInfo field))
                return field.IsDefined(typeof(PropertyOrderAttribute), true);
            return false;
        }

        private TaoTiePropertyEntry CreateEntry(SerializedProperty prop, Dictionary<string, FieldInfo> fieldMap)
        {
            var entry = new TaoTiePropertyEntry
            {
                Property = prop.Copy(),
                PropertyPath = prop.propertyPath,
                PropertyName = prop.name
            };

            // Resolve FieldInfo by following the full property path through the type hierarchy
            FieldInfo field = ResolveFieldFromPath(prop, fieldMap);
            if (field == null) return entry;
            entry.DeclaringType = field.DeclaringType;

            entry.LabelOverride = GetFirstAttr<LabelTextAttribute>(field)?.Text;
            entry.TooltipText = GetFirstAttr<TooltipAttribute>(field)?.tooltip;
            entry.ShowIf = field.GetCustomAttributes<ShowIfAttribute>().ToArray();
            entry.HideIf = field.GetCustomAttributes<HideIfAttribute>().ToArray();
            entry.EnableIf = field.GetCustomAttributes<EnableIfAttribute>().ToArray();
            entry.DisableIf = field.GetCustomAttributes<DisableIfAttribute>().ToArray();
            entry.ReadOnly = GetFirstAttr<ReadOnlyAttribute>(field);
            entry.Title = GetFirstAttr<TitleAttribute>(field);

            var infoBoxAttrs = field.GetCustomAttributes<InfoBoxAttribute>();
            if (infoBoxAttrs != null)
            {
                foreach (var ib in infoBoxAttrs)
                {
                    entry.InfoBoxes ??= new List<InfoBoxAttribute>();
                    entry.InfoBoxes.Add(ib);
                }
            }

            entry.Space = GetFirstAttr<PropertySpaceAttribute>(field);
            entry.Range = GetFirstAttr<PropertyRangeAttribute>(field);
            entry.Header = GetFirstAttr<HeaderAttribute>(field);
            entry.UnitySpace = GetFirstAttr<SpaceAttribute>(field);
            entry.UnityRange = GetFirstAttr<RangeAttribute>(field);
            entry.UnityMin = GetFirstAttr<MinAttribute>(field);
            entry.TableList = GetFirstAttr<TableListAttribute>(field);
            entry.TableMatrix = GetFirstAttr<TableMatrixAttribute>(field);
            entry.NotNull = GetFirstAttr<NotNullAttribute>(field);
            entry.FoldoutGroup = GetFirstAttr<FoldoutGroupAttribute>(field);
            entry.BoxGroup = GetFirstAttr<BoxGroupAttribute>(field);
            entry.TabGroup = GetFirstAttr<TabGroupAttribute>(field);
            entry.HorizontalGroup = GetFirstAttr<HorizontalGroupAttribute>(field);
            entry.EnumToggleButtons = GetFirstAttr<EnumToggleButtonsAttribute>(field);
            entry.ValueDropdown = GetFirstAttr<ValueDropdownAttribute>(field);
            entry.OnValueChanged = GetFirstAttr<OnValueChangedAttribute>(field);

            // Graph attributes
            entry.DrawIgnore = GetFirstAttr<DrawIgnoreAttribute>(field);
            entry.DisableInEditorMode = GetFirstAttr<DisableInEditorModeAttribute>(field);
            entry.MinValue = GetFirstAttr<MinValueAttribute>(field);
            entry.MaxValue = GetFirstAttr<MaxValueAttribute>(field);
            entry.NotAssets = GetFirstAttr<NotAssetsAttribute>(field);
            entry.HideReferenceObjectPicker = GetFirstAttr<HideReferenceObjectPickerAttribute>(field);
            entry.OnStateUpdate = GetFirstAttr<OnStateUpdateAttribute>(field);
            entry.OnCollectionChanged = GetFirstAttr<OnCollectionChangedAttribute>(field);
            entry.TypeFilter = GetFirstAttr<TypeFilterAttribute>(field);

            var orderAttr = GetFirstAttr<PropertyOrderAttribute>(field);
            if (orderAttr != null)
                entry.Order = orderAttr.Order;

            entry.HasTaoTieAttributes = entry.LabelOverride != null
                || entry.ShowIf != null || entry.HideIf != null
                || entry.EnableIf != null || entry.DisableIf != null
                || entry.ReadOnly != null || entry.Title != null
                || (entry.InfoBoxes != null && entry.InfoBoxes.Count > 0)
                || entry.Space != null || entry.Range != null
                || entry.Header != null || entry.UnitySpace != null
                || entry.UnityRange != null || entry.UnityMin != null
                || entry.TableList != null || entry.TableMatrix != null
                || entry.NotNull != null
                || entry.FoldoutGroup != null || entry.BoxGroup != null
                || entry.TabGroup != null || entry.HorizontalGroup != null
                || entry.EnumToggleButtons != null || entry.ValueDropdown != null
                || entry.OnValueChanged != null
                || entry.DrawIgnore != null || entry.DisableInEditorMode != null
                || entry.MinValue != null || entry.MaxValue != null
                || entry.NotAssets != null || entry.HideReferenceObjectPicker != null
                || entry.OnStateUpdate != null || entry.OnCollectionChanged != null
                || entry.TypeFilter != null;

            return entry;
        }

        public void ClearCache()
        {
            entryCache.Clear();
            hasTaoTieAttrCache.Clear();
            buttonMethodCache.Clear();
            fieldCache.Clear();
            cachedHiddenPaths = null;
            lastRefreshedEntries = null;
        }
    }
}
