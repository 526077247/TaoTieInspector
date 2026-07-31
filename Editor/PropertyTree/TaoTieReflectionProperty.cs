using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace TaoTie.Inspector.Editor
{
    public class TaoTieReflectionProperty
    {
        public FieldInfo FieldInfo;
        public string LabelOverride;
        public int Order;
        public ShowIfAttribute[] ShowIf;
        public HideIfAttribute[] HideIf;
        public EnableIfAttribute[] EnableIf;
        public DisableIfAttribute[] DisableIf;
        public ReadOnlyAttribute ReadOnly;
        public TitleAttribute Title;
        public List<InfoBoxAttribute> InfoBoxes;
        public PropertySpaceAttribute Space;
        public PropertyRangeAttribute Range;
        public FoldoutGroupAttribute FoldoutGroup;
        public BoxGroupAttribute BoxGroup;
        public TabGroupAttribute TabGroup;
        public HorizontalGroupAttribute HorizontalGroup;
        public EnumToggleButtonsAttribute EnumToggleButtons;
        public ValueDropdownAttribute ValueDropdown;
        public OnValueChangedAttribute OnValueChanged;
        // Graph attributes
        public DrawIgnoreAttribute DrawIgnore;
        public DisableInEditorModeAttribute DisableInEditorMode;
        public MinValueAttribute MinValue;
        public MaxValueAttribute MaxValue;
        public NotAssetsAttribute NotAssets;
        public OnStateUpdateAttribute OnStateUpdate;
        public bool HasTaoTieAttributes;
        public bool Visible = true;
        public bool Enabled = true;

        public object GetValue(object target) => FieldInfo.GetValue(target);

        public void SetValue(object target, object value)
        {
            if (target is UnityEngine.Object uo)
                Undo.RecordObject(uo, "Modify " + FieldInfo.Name);
            FieldInfo.SetValue(target, value);
        }

        public bool IsVisible(object target)
        {
            if (DrawIgnore != null && DrawIgnore.Ignore == Ignore.All) return false;
            if (ShowIf != null)
                foreach (var attr in ShowIf)
                    if (!TaoTieConditionResolver.EvaluateShowIf(attr, target)) return false;
            if (HideIf != null)
                foreach (var attr in HideIf)
                    if (TaoTieConditionResolver.EvaluateHideIf(attr, target)) return false;
            return true;
        }

        public bool IsEnabled(object target)
        {
            if (ReadOnly != null) return false;
            if (DisableInEditorMode != null && !EditorApplication.isPlaying) return false;
            if (EnableIf != null)
                foreach (var attr in EnableIf)
                    if (!TaoTieConditionResolver.EvaluateEnableIf(attr, target)) return false;
            if (DisableIf != null)
                foreach (var attr in DisableIf)
                    if (TaoTieConditionResolver.EvaluateDisableIf(attr, target)) return false;
            return true;
        }

        public void Draw(object target)
        {
            if (Space != null && Space.SpaceBefore > 0)
                GUILayout.Space(Space.SpaceBefore);

            if (Title != null)
                DrawTitle(Title);

            if (InfoBoxes != null)
            {
                foreach (var ib in InfoBoxes)
                {
                    bool show = string.IsNullOrEmpty(ib.VisibleIf) ||
                                TaoTieConditionResolver.Evaluate(target, ib.VisibleIf);
                    if (show) DrawInfoBox(ib);
                }
            }

            bool wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && Enabled;

            EditorGUI.BeginChangeCheck();
            object currentValue = GetValue(target);
            object newValue = DrawField(currentValue);

            if (EditorGUI.EndChangeCheck() && !Equals(currentValue, newValue))
            {
                // Clamp MinValue / MaxValue
                if (MinValue != null || MaxValue != null)
                    newValue = ClampMinMax(currentValue, newValue);
                SetValue(target, newValue);
                if (OnValueChanged != null)
                {
                    var member = TaoTieConditionResolver.GetMember(target, OnValueChanged.MethodName);
                    if (member is MethodInfo mi) mi.Invoke(target, null);
                }
            }

            // OnStateUpdate
            if (OnStateUpdate != null)
            {
                var member = TaoTieConditionResolver.GetMember(target, OnStateUpdate.Action);
                if (member is MethodInfo smi) smi.Invoke(target, null);
            }

            GUI.enabled = wasEnabled;

            if (Space != null && Space.SpaceAfter > 0)
                GUILayout.Space(Space.SpaceAfter);
        }

        private object DrawField(object value)
        {
            GUIContent label = new GUIContent(LabelOverride ?? ObjectNames.NicifyVariableName(FieldInfo.Name));
            Type fieldType = FieldInfo.FieldType;

            if (value == null && !fieldType.IsValueType)
            {
                EditorGUILayout.LabelField(label, "null");
                return value;
            }

            if (fieldType == typeof(int))
                return EditorGUILayout.IntField(label, (int)value);
            if (fieldType == typeof(float))
                return Range != null
                    ? EditorGUILayout.Slider(label, (float)value, (float)Range.Min, (float)Range.Max)
                    : EditorGUILayout.FloatField(label, (float)value);
            if (fieldType == typeof(bool))
                return EditorGUILayout.Toggle(label, (bool)value);
            if (fieldType == typeof(string))
                return EditorGUILayout.TextField(label, (string)value);
            if (fieldType == typeof(double))
                return EditorGUILayout.DoubleField(label, (double)value);
            if (fieldType == typeof(long))
                return EditorGUILayout.LongField(label, (long)value);
            if (fieldType == typeof(Vector2))
                return EditorGUILayout.Vector2Field(label, (Vector2)value);
            if (fieldType == typeof(Vector3))
                return EditorGUILayout.Vector3Field(label, (Vector3)value);
            if (fieldType == typeof(Vector4))
                return EditorGUILayout.Vector4Field(label, (Vector4)value);
            if (fieldType == typeof(Vector2Int))
                return EditorGUILayout.Vector2IntField(label, (Vector2Int)value);
            if (fieldType == typeof(Vector3Int))
                return EditorGUILayout.Vector3IntField(label, (Vector3Int)value);
            if (fieldType == typeof(Color))
                return EditorGUILayout.ColorField(label, (Color)value);
            if (fieldType == typeof(Rect))
                return EditorGUILayout.RectField(label, (Rect)value);
            if (fieldType == typeof(Bounds))
                return EditorGUILayout.BoundsField(label, (Bounds)value);
            if (fieldType == typeof(AnimationCurve))
                return EditorGUILayout.CurveField(label, (AnimationCurve)value);
            if (fieldType == typeof(LayerMask))
                return EditorGUILayout.LayerField(label, (LayerMask)value);
            if (fieldType.IsEnum)
            {
                if (EnumToggleButtons != null)
                {
                    return DrawEnumToggleButtons(fieldType, value, label);
                }
                return EditorGUILayout.EnumPopup(label, (Enum)value);
            }
            if (typeof(UnityEngine.Object).IsAssignableFrom(fieldType))
                return EditorGUILayout.ObjectField(label, (UnityEngine.Object)value, fieldType, true);

            // Fallback for unsupported types
            EditorGUILayout.LabelField(label, value?.ToString() ?? "null");
            return value;
        }

        private object DrawEnumToggleButtons(Type enumType, object value, GUIContent label)
        {
            Enum[] values = (Enum[])Enum.GetValues(enumType);
            string[] names = Enum.GetNames(enumType);
            bool isFlags = enumType.IsDefined(typeof(FlagsAttribute), false);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(label);
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
                        long flagVal = Convert.ToInt64(values[i]);
                        isActive = flagVal == 0 ? currentVal == 0 : (currentVal & flagVal) == flagVal;
                    }
                    else
                    {
                        isActive = Convert.ToInt64(value) == Convert.ToInt64(values[i]);
                    }

                    bool newActive = GUILayout.Toggle(isActive, names[i], EditorStyles.miniButton);
                    if (newActive != isActive)
                    {
                        if (isFlags)
                        {
                            long currentVal = Convert.ToInt64(value);
                            long flagVal = Convert.ToInt64(values[i]);
                            if (flagVal == 0)
                                value = Enum.ToObject(enumType, 0);
                            else if (newActive)
                                value = Enum.ToObject(enumType, currentVal | flagVal);
                            else
                                value = Enum.ToObject(enumType, currentVal & ~flagVal);
                        }
                        else
                        {
                            if (newActive) value = values[i];
                        }
                    }
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            return value;
        }

        private object ClampMinMax(object oldValue, object newValue)
        {
            if (newValue is int intVal)
            {
                if (MinValue != null) intVal = Mathf.Max(intVal, (int)System.Math.Ceiling(MinValue.MinValue));
                if (MaxValue != null) intVal = Mathf.Min(intVal, (int)System.Math.Floor(MaxValue.MaxValue));
                return intVal;
            }
            if (newValue is float floatVal)
            {
                if (MinValue != null) floatVal = Mathf.Max(floatVal, (float)MinValue.MinValue);
                if (MaxValue != null) floatVal = Mathf.Min(floatVal, (float)MaxValue.MaxValue);
                return floatVal;
            }
            if (newValue is long longVal)
            {
                if (MinValue != null) longVal = System.Math.Max(longVal, (long)System.Math.Ceiling(MinValue.MinValue));
                if (MaxValue != null) longVal = System.Math.Min(longVal, (long)System.Math.Floor(MaxValue.MaxValue));
                return longVal;
            }
            if (newValue is double doubleVal)
            {
                if (MinValue != null) doubleVal = System.Math.Max(doubleVal, MinValue.MinValue);
                if (MaxValue != null) doubleVal = System.Math.Min(doubleVal, MaxValue.MaxValue);
                return doubleVal;
            }
            return newValue;
        }

        private static void DrawTitle(TitleAttribute title)
        {
            if (title.Indented) EditorGUI.indentLevel++;
            var style = new GUIStyle(EditorStyles.boldLabel);
            style.alignment = title.TitleAlignment switch
            {
                TitleAlignmentType.Center => TextAnchor.MiddleCenter,
                TitleAlignmentType.Right => TextAnchor.UpperRight,
                _ => TextAnchor.UpperLeft
            };
            EditorGUILayout.LabelField(title.Title, style);
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

        private static void DrawInfoBox(InfoBoxAttribute infoBox)
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
}
