using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace TaoTie.Inspector.Editor
{
    public static class EnumToggleButtonsDrawer
    {
        private static readonly Dictionary<Type, Enum[]> enumValueCache = new();
        private static readonly Dictionary<Type, string[]> enumNameCache = new();

        public static void Draw(SerializedProperty property, GUIContent label)
        {
            if (label == null)
                label = new GUIContent(property.displayName);

            Type enumType = GetEnumType(property);
            if (enumType == null)
            {
                EditorGUILayout.PropertyField(property, label);
                return;
            }

            Enum[] values = GetEnumValues(enumType);
            string[] names = GetEnumNames(enumType);

            bool isFlags = enumType.IsDefined(typeof(FlagsAttribute), false);

            EditorGUILayout.BeginHorizontal();

            // Draw label
            Rect labelRect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
            EditorGUI.LabelField(labelRect, label);

            // Draw buttons
            int buttonCount = values.Length;
            if (buttonCount == 0)
            {
                EditorGUILayout.EndHorizontal();
                return;
            }

            // Limit buttons per row
            int maxPerRow = 6;
            int rows = Mathf.CeilToInt((float)buttonCount / maxPerRow);

            EditorGUILayout.BeginVertical();
            for (int row = 0; row < rows; row++)
            {
                EditorGUILayout.BeginHorizontal();
                int startIndex = row * maxPerRow;
                int endIndex = Mathf.Min(startIndex + maxPerRow, buttonCount);

                for (int i = startIndex; i < endIndex; i++)
                {
                    bool isActive;
                    if (isFlags)
                    {
                        long currentVal = property.enumValueFlag;
                        long flagVal = Convert.ToInt64(values[i]);
                        isActive = (currentVal & flagVal) == flagVal && flagVal != 0;
                        if (flagVal == 0)
                            isActive = currentVal == 0;
                    }
                    else
                    {
                        isActive = property.enumValueIndex == i;
                    }

                    bool newActive = GUILayout.Toggle(isActive, names[i], EditorStyles.miniButton);
                    if (newActive != isActive)
                    {
                        if (isFlags)
                        {
                            long currentVal = property.enumValueFlag;
                            long flagVal = Convert.ToInt64(values[i]);
                            if (flagVal == 0)
                            {
                                property.enumValueFlag = 0;
                            }
                            else if (newActive)
                            {
                                property.enumValueFlag = (int)(currentVal | flagVal);
                            }
                            else
                            {
                                property.enumValueFlag = (int)(currentVal & ~flagVal);
                            }
                        }
                        else
                        {
                            if (newActive)
                                property.enumValueIndex = i;
                        }
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        private static Type GetEnumType(SerializedProperty property)
        {
            // Try to get the enum type from the field
            if (property.enumNames == null || property.enumNames.Length == 0)
                return null;

            // Use reflection to find the actual enum type
            // SerializedProperty doesn't expose the type directly, so we match by name
            Type targetType = property.serializedObject.targetObject.GetType();
            string fieldName = property.name;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            FieldInfo field = targetType.GetField(fieldName, flags);
            if (field == null && targetType.BaseType != null)
                field = targetType.BaseType.GetField(fieldName, flags);

            if (field != null && field.FieldType.IsEnum)
                return field.FieldType;

            // Fallback: use the enum names from the property
            return null;
        }

        private static Enum[] GetEnumValues(Type enumType)
        {
            if (!enumValueCache.TryGetValue(enumType, out Enum[] values))
            {
                values = Enum.GetValues(enumType).Cast<Enum>().ToArray();
                enumValueCache[enumType] = values;
            }
            return values;
        }

        private static string[] GetEnumNames(Type enumType)
        {
            if (!enumNameCache.TryGetValue(enumType, out string[] names))
            {
                names = Enum.GetNames(enumType);
                enumNameCache[enumType] = names;
            }
            return names;
        }
    }
}
