using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace TaoTie.Inspector.Editor
{
    public class TaoTiePropertyEntry
    {
        public SerializedProperty Property;
        public string PropertyPath;
        public string PropertyName;

        public int Order;
        public string LabelOverride;
        public string TooltipText;

        public ShowIfAttribute[] ShowIf;
        public HideIfAttribute[] HideIf;
        public EnableIfAttribute[] EnableIf;
        public DisableIfAttribute[] DisableIf;
        public ReadOnlyAttribute ReadOnly;

        public TitleAttribute Title;
        public List<InfoBoxAttribute> InfoBoxes;
        public PropertySpaceAttribute Space;
        public PropertyRangeAttribute Range;
        public HeaderAttribute Header;      // Unity built-in
        public SpaceAttribute UnitySpace;  // Unity built-in
        public RangeAttribute UnityRange;  // Unity built-in [Range]
        public MinAttribute UnityMin;      // Unity built-in [Min]
        public TableListAttribute TableList;
        public TableMatrixAttribute TableMatrix;
        public NotNullAttribute NotNull;

        public FoldoutGroupAttribute FoldoutGroup;
        public BoxGroupAttribute BoxGroup;
        public TabGroupAttribute TabGroup;
        public HorizontalGroupAttribute HorizontalGroup;

        public EnumToggleButtonsAttribute EnumToggleButtons;
        public ValueDropdownAttribute ValueDropdown;
        public OnValueChangedAttribute OnValueChanged;

        // Graph attributes (also usable in normal Inspector)
        public DrawIgnoreAttribute DrawIgnore;
        public DisableInEditorModeAttribute DisableInEditorMode;
        public MinValueAttribute MinValue;
        public MaxValueAttribute MaxValue;
        public NotAssetsAttribute NotAssets;
        public HideReferenceObjectPickerAttribute HideReferenceObjectPicker;
        public OnStateUpdateAttribute OnStateUpdate;
        public OnCollectionChangedAttribute OnCollectionChanged;
        public TypeFilterAttribute TypeFilter;

        public bool HasTaoTieAttributes;

        // For unserialized fields (Dictionary etc.) — drawn via reflection, not SerializedProperty
        public bool IsReflectionField;
        public System.Reflection.FieldInfo ReflectionField;

        // For nested [Serializable] objects — draw as foldout header
        public bool IsFoldoutGroup;
        public string FoldoutGroupName;
        public bool FoldoutExpanded;

        // The declaring type of this property (for resolving condition targets on nested objects)
        public Type DeclaringType;

        // Dynamic state (re-evaluated each frame)
        public bool Visible = true;
        public bool Enabled = true;

        // Cached path parts for ResolveConditionTarget — avoids per-frame string.Split
        internal string[] cachedPathParts;

        // Static cache for (Type, fieldName) → FieldInfo, shared across all entries
        private static readonly Dictionary<(Type, string), FieldInfo> s_FieldCache = new();

        internal static FieldInfo GetCachedFieldPublic(Type type, string name)
        {
            var key = (type, name);
            if (s_FieldCache.TryGetValue(key, out var field))
                return field;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            field = type.GetField(name, flags);
            Type baseType = type.BaseType;
            while (field == null && baseType != null && baseType != typeof(object))
            {
                field = baseType.GetField(name, flags);
                baseType = baseType.BaseType;
            }
            s_FieldCache[key] = field; // may store null — avoids re-lookup for missing fields
            return field;
        }

        public bool IsVisible(object rootTarget)
        {
            if (DrawIgnore != null && DrawIgnore.Ignore == Ignore.All) return false;
            object condTarget = ResolveConditionTarget(rootTarget);
            if (ShowIf != null)
            {
                foreach (var attr in ShowIf)
                    if (!TaoTieConditionResolver.EvaluateShowIf(attr, condTarget))
                        return false;
            }
            if (HideIf != null)
            {
                foreach (var attr in HideIf)
                    if (TaoTieConditionResolver.EvaluateHideIf(attr, condTarget))
                        return false;
            }
            return true;
        }

        public bool IsEnabled(object rootTarget)
        {
            if (ReadOnly != null) return false;
            if (DisableInEditorMode != null && !EditorApplication.isPlaying) return false;
            object condTarget = ResolveConditionTarget(rootTarget);
            if (EnableIf != null)
            {
                foreach (var attr in EnableIf)
                    if (!TaoTieConditionResolver.EvaluateEnableIf(attr, condTarget))
                        return false;
            }
            if (DisableIf != null)
            {
                foreach (var attr in DisableIf)
                    if (!TaoTieConditionResolver.EvaluateDisableIf(attr, condTarget))
                        return false;
            }
            return true;
        }

        /// <summary>
        /// For nested properties (e.g. "obj.field"), traverse the root target
        /// to find the actual object that holds the condition fields.
        /// Uses cached path parts and field lookups to avoid per-frame allocations.
        /// </summary>
        private object ResolveConditionTarget(object rootTarget)
        {
            if (rootTarget == null || string.IsNullOrEmpty(PropertyPath))
                return rootTarget;
            if (!PropertyPath.Contains('.'))
                return rootTarget;

            if (cachedPathParts == null)
                cachedPathParts = PropertyPath.Split('.');

            object current = rootTarget;

            for (int i = 0; i < cachedPathParts.Length - 1 && current != null; i++)
            {
                var field = GetCachedFieldPublic(current.GetType(), cachedPathParts[i]);
                if (field == null) return rootTarget;
                current = field.GetValue(current);
            }
            return current ?? rootTarget;
        }
    }
}
