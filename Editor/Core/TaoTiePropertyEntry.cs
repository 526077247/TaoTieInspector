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

        public ShowIfAttribute ShowIf;
        public HideIfAttribute HideIf;
        public EnableIfAttribute EnableIf;
        public DisableIfAttribute DisableIf;
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

        public bool IsVisible(object rootTarget)
        {
            if (DrawIgnore != null && DrawIgnore.Ignore == Ignore.All) return false;
            object condTarget = ResolveConditionTarget(rootTarget);
            if (ShowIf != null && !TaoTieConditionResolver.EvaluateShowIf(ShowIf, condTarget))
                return false;
            if (HideIf != null && TaoTieConditionResolver.EvaluateHideIf(HideIf, condTarget))
                return false;
            return true;
        }

        public bool IsEnabled(object rootTarget)
        {
            if (ReadOnly != null) return false;
            if (DisableInEditorMode != null && !EditorApplication.isPlaying) return false;
            object condTarget = ResolveConditionTarget(rootTarget);
            if (EnableIf != null && !TaoTieConditionResolver.EvaluateEnableIf(EnableIf, condTarget))
                return false;
            if (DisableIf != null && TaoTieConditionResolver.EvaluateDisableIf(DisableIf, condTarget))
                return false;
            return true;
        }

        /// <summary>
        /// For nested properties (e.g. "obj.field"), traverse the root target
        /// to find the actual object that holds the condition fields.
        /// </summary>
        private object ResolveConditionTarget(object rootTarget)
        {
            if (rootTarget == null || string.IsNullOrEmpty(PropertyPath))
                return rootTarget;
            if (!PropertyPath.Contains('.'))
                return rootTarget;

            string[] parts = PropertyPath.Split('.');
            object current = rootTarget;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            // All parts except the last (which is the field itself)
            for (int i = 0; i < parts.Length - 1 && current != null; i++)
            {
                var type = current.GetType();
                var field = type.GetField(parts[i], flags);
                if (field == null)
                {
                    Type baseType = type.BaseType;
                    while (field == null && baseType != null && baseType != typeof(object))
                    {
                        field = baseType.GetField(parts[i], flags);
                        baseType = baseType.BaseType;
                    }
                }
                if (field == null) return rootTarget;
                current = field.GetValue(current);
            }
            return current ?? rootTarget;
        }
    }
}
