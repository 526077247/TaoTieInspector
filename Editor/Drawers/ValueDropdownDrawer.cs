using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace TaoTie.Inspector.Editor
{
    public static class ValueDropdownDrawer
    {
        public static void Draw(SerializedProperty property, ValueDropdownAttribute attribute,
            object target, GUIContent label)
        {
            if (label == null)
                label = new GUIContent(property.displayName);

            List<ValueDropdownItem> items = GetDropdownItems(target, attribute.MemberName);

            if (items == null || items.Count == 0)
            {
                EditorGUILayout.PropertyField(property, label);
                return;
            }

            if (attribute.AppendNextDrawer)
            {
                // Draw the original field first, then append a ▼ dropdown button beside it.
                // Use PrefixLabel so the label takes only its natural width, leaving room for the button.
                Rect rowRect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
                float btnW = 22f;
                Rect btnRect = new Rect(rowRect.xMax - btnW, rowRect.y, btnW, rowRect.height);

                // PrefixLabel consumes label portion and returns the remaining content rect
                Rect contentRect = EditorGUI.PrefixLabel(rowRect, label);
                // Shrink content rect to leave room for the button
                contentRect.width -= btnW + 2f;

                // Draw the default field in the content area (no label — already drawn by PrefixLabel)
                EditorGUI.PropertyField(contentRect, property, GUIContent.none, false);

                if (GUI.Button(btnRect, "▼", EditorStyles.miniButton))
                {
                    int selIdx = -1;
                    object curVal = GetPropertyValue(property);
                    for (int i = 0; i < items.Count; i++)
                    {
                        if (AreEqual(items[i].Value, curVal))
                        {
                            selIdx = i;
                            break;
                        }
                    }
                    ValueDropdownPopup.Show(btnRect, items, selIdx, (idx) =>
                    {
                        if (idx >= 0 && idx < items.Count)
                        {
                            SetPropertyValue(property, items[idx].Value);
                            property.serializedObject.ApplyModifiedProperties();
                        }
                    });
                }
                return;
            }

            // Non-AppendNextDrawer: draw a popup-style button showing the current selection text
            // Find current selection
            int selectedIndex = -1;
            object currentValue = GetPropertyValue(property);

            for (int i = 0; i < items.Count; i++)
            {
                if (AreEqual(items[i].Value, currentValue))
                {
                    selectedIndex = i;
                    break;
                }
            }

            string currentText = selectedIndex >= 0 ? items[selectedIndex].Text : property.displayName;

            Rect rect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
            GUIContent buttonContent = new GUIContent(currentText);

            if (label != null)
            {
                rect = EditorGUI.PrefixLabel(rect, label);
            }

            if (GUI.Button(rect, buttonContent, EditorStyles.popup))
            {
                ValueDropdownPopup.Show(rect, items, selectedIndex, (idx) =>
                {
                    if (idx >= 0 && idx < items.Count)
                    {
                        SetPropertyValue(property, items[idx].Value);
                        property.serializedObject.ApplyModifiedProperties();
                    }
                });
            }
        }

        /// <summary>
        /// Draw a SerializedProperty array/list with ValueDropdown per element.
        /// Uses the same box+toolbar+grid style as DrawTableList for visual consistency.
        /// </summary>
        public static bool DrawArray(SerializedProperty arrayProp, ValueDropdownAttribute attribute,
            object target, GUIContent label)
        {
            if (label == null)
                label = new GUIContent(arrayProp.displayName);

            List<ValueDropdownItem> items = GetDropdownItems(target, attribute.MemberName);

            if (items == null || items.Count == 0)
            {
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(arrayProp, label, true);
                return EditorGUI.EndChangeCheck();
            }

            bool changed = false;
            int arraySizeBefore = arrayProp.arraySize;
            string title = label.text;
            float indexColW = 28f;
            float deleteColW = 22f;
            float availableWidth = EditorGUIUtility.currentViewWidth - 40f;
            float dropdownColW = Mathf.Max(50f, availableWidth - indexColW - deleteColW);

            var boxStyle = new GUIStyle(GUI.skin.box) { padding = new RectOffset(2, 2, 2, 2) };
            EditorGUILayout.BeginVertical(boxStyle);

            // Foldout title bar with + / - controls
            string foldKey = "TaoTie_Fold_VD_" + arrayProp.propertyPath;
            bool foldout = SessionState.GetBool(foldKey, false);
            int oldIndent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            Rect titleBarRect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
            EditorGUI.DrawRect(titleBarRect, new Color(0.3f, 0.3f, 0.3f, 0.2f));
            float tbX = titleBarRect.x + 14f;
            // Anchor buttons to right edge (xMax is indent-independent)
            float minusX = titleBarRect.xMax - 24f - 2f;
            float plusX = minusX - 24f - 2f;
            // Count label before buttons
            string countText = $"({arrayProp.arraySize})";
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
                arrayProp.arraySize++;
                changed = true;
            }
            if (GUI.Button(new Rect(minusX, titleBarRect.y, 24f, titleBarRect.height), "-", EditorStyles.toolbarButton))
            {
                if (arrayProp.arraySize > 0) { arrayProp.arraySize--; changed = true; }
            }
            EditorGUI.indentLevel = oldIndent;

            if (foldout)
            {
            // Data rows — limit visible rows for performance
            const int k_MaxRows = 50;
            string vdShowAllKey = "TaoTie_ShowAll_VDD_" + arrayProp.propertyPath;
            bool vdShowAll = SessionState.GetBool(vdShowAllKey, false);
            int vdVisibleCount = vdShowAll ? arrayProp.arraySize : Mathf.Min(arrayProp.arraySize, k_MaxRows);

            for (int i = 0; i < vdVisibleCount; i++)
            {
                var element = arrayProp.GetArrayElementAtIndex(i);
                var rowRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight + 2f);
                if (i % 2 == 1)
                    EditorGUI.DrawRect(rowRect, new Color(0.5f, 0.5f, 0.5f, 0.1f));

                float x = rowRect.x;

                // Index column
                EditorGUI.LabelField(new Rect(x, rowRect.y, indexColW, rowRect.height), i.ToString());
                x += indexColW;

                // Find current selection
                object currentValue = GetPropertyValue(element);
                int selectedIndex = -1;
                for (int j = 0; j < items.Count; j++)
                {
                    if (AreEqual(items[j].Value, currentValue))
                    {
                        selectedIndex = j;
                        break;
                    }
                }

                float contentW = dropdownColW;

                if (attribute.AppendNextDrawer)
                {
                    // AppendNextDrawer: draw the original field + ▼ button
                    float btnW = 22f;
                    Rect btnRect = new Rect(x + contentW - btnW, rowRect.y, btnW, rowRect.height);
                    Rect fieldRect = new Rect(x, rowRect.y, contentW - btnW - 2f, rowRect.height);
                    EditorGUI.PropertyField(fieldRect, element, GUIContent.none, false);
                    if (GUI.Button(btnRect, "▼", EditorStyles.miniButton))
                    {
                        ValueDropdownPopup.Show(btnRect, items, selectedIndex, (idx) =>
                        {
                            if (idx >= 0 && idx < items.Count)
                            {
                                SetPropertyValue(element, items[idx].Value);
                                element.serializedObject.ApplyModifiedProperties();
                            }
                        });
                        changed = true;
                    }
                }
                else
                {
                    // Normal: popup-style button showing the current selection text
                    string currentText = selectedIndex >= 0 ? items[selectedIndex].Text : "—";
                    Rect dropdownRect = new Rect(x, rowRect.y, contentW, rowRect.height);
                    if (GUI.Button(dropdownRect, currentText, EditorStyles.popup))
                    {
                        ValueDropdownPopup.Show(dropdownRect, items, selectedIndex, (idx) =>
                        {
                            if (idx >= 0 && idx < items.Count)
                            {
                                SetPropertyValue(element, items[idx].Value);
                                element.serializedObject.ApplyModifiedProperties();
                            }
                        });
                        changed = true;
                    }
                }
                x += dropdownColW;

                // Delete button
                Rect delRect = new Rect(x, rowRect.y, deleteColW, rowRect.height);
                if (GUI.Button(delRect, "×"))
                {
                    arrayProp.DeleteArrayElementAtIndex(i);
                    changed = true;
                    break;
                }

                // Row bottom grid line
                EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.yMax - 1, rowRect.width, 1),
                    new Color(0.3f, 0.3f, 0.3f, 0.3f));
            }

            // Show All / Show Less toggle
            if (arrayProp.arraySize > k_MaxRows)
            {
                if (GUILayout.Button(vdShowAll ? $"Show Less ({k_MaxRows})" : $"Show All ({arrayProp.arraySize})", EditorStyles.miniButton))
                {
                    SessionState.SetBool(vdShowAllKey, !vdShowAll);
                }
            }
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);

            if (changed && arrayProp.arraySize != arraySizeBefore)
                return true;

            return changed;
        }

        private static List<ValueDropdownItem> GetDropdownItems(object target, string memberName)
        {
            if (target == null || string.IsNullOrEmpty(memberName)) return null;

            Type type = target.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

            MemberInfo member = type.GetField(memberName, flags)
                             ?? (MemberInfo)type.GetProperty(memberName, flags)
                             ?? (MemberInfo)type.GetMethod(memberName, flags);

            if (member == null) return null;

            object result = member switch
            {
                FieldInfo fi => fi.GetValue(fi.IsStatic ? null : target),
                PropertyInfo pi => pi.GetGetMethod(true).IsStatic ? pi.GetValue(null, null) : pi.GetValue(target, null),
                MethodInfo mi => mi.GetParameters().Length == 0 ? mi.Invoke(mi.IsStatic ? null : target, null) : null,
                _ => null
            };

            if (result == null) return null;

            // Convert to List<ValueDropdownItem>
            var items = new List<ValueDropdownItem>();

            if (result is IEnumerable<ValueDropdownItem> typedItems)
            {
                items.AddRange(typedItems);
            }
            else if (result is IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    if (item is ValueDropdownItem vdi)
                        items.Add(vdi);
                    else
                        items.Add(new ValueDropdownItem(item?.ToString() ?? "null", item));
                }
            }
            else
            {
                items.Add(new ValueDropdownItem(result.ToString(), result));
            }

            // If no items found via direct type matching, try reflection-based conversion
            // for ValueDropdownItem<T> and ValueDropdownList<T>
            if (items.Count == 0 && result != null)
            {
                var resultType = result.GetType();
                if (resultType.IsGenericType)
                {
                    var genericDef = resultType.GetGenericTypeDefinition();
                    if (genericDef == typeof(ValueDropdownList<>) || genericDef == typeof(List<>))
                    {
                        var itemType = resultType.GetGenericArguments()[0];
                        if (itemType.IsGenericType && itemType.GetGenericTypeDefinition() == typeof(ValueDropdownItem<>))
                        {
                            foreach (var item in (IEnumerable)result)
                            {
                                var textProp = itemType.GetField("Text");
                                var valueProp = itemType.GetField("Value");
                                items.Add(new ValueDropdownItem(
                                    textProp?.GetValue(item)?.ToString() ?? "",
                                    valueProp?.GetValue(item)));
                            }
                        }
                    }
                }
            }

            return items;
        }

        private static object GetPropertyValue(SerializedProperty property)
        {
            return property.propertyType switch
            {
                SerializedPropertyType.Integer => property.intValue,
                SerializedPropertyType.Float => property.floatValue,
                SerializedPropertyType.Boolean => property.boolValue,
                SerializedPropertyType.String => property.stringValue,
                SerializedPropertyType.Enum => property.enumValueIndex,
                SerializedPropertyType.ObjectReference => property.objectReferenceValue,
                SerializedPropertyType.Color => property.colorValue,
                SerializedPropertyType.Vector2 => property.vector2Value,
                SerializedPropertyType.Vector3 => property.vector3Value,
                SerializedPropertyType.Vector4 => property.vector4Value,
                SerializedPropertyType.Vector2Int => property.vector2IntValue,
                SerializedPropertyType.Vector3Int => property.vector3IntValue,
                _ => null
            };
        }

        private static void SetPropertyValue(SerializedProperty property, object value)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    property.intValue = Convert.ToInt32(value);
                    break;
                case SerializedPropertyType.Float:
                    property.floatValue = Convert.ToSingle(value);
                    break;
                case SerializedPropertyType.Boolean:
                    property.boolValue = Convert.ToBoolean(value);
                    break;
                case SerializedPropertyType.String:
                    property.stringValue = value?.ToString() ?? "";
                    break;
                case SerializedPropertyType.Enum:
                    if (value is int intVal)
                        property.enumValueIndex = intVal;
                    else if (value is Enum enumVal)
                        property.enumValueIndex = Convert.ToInt32(enumVal);
                    break;
                case SerializedPropertyType.ObjectReference:
                    property.objectReferenceValue = value as UnityEngine.Object;
                    break;
                case SerializedPropertyType.Color:
                    property.colorValue = (Color)value;
                    break;
                case SerializedPropertyType.Vector2:
                    property.vector2Value = (Vector2)value;
                    break;
                case SerializedPropertyType.Vector3:
                    property.vector3Value = (Vector3)value;
                    break;
                case SerializedPropertyType.Vector4:
                    property.vector4Value = (Vector4)value;
                    break;
            }
        }

        /// <summary>
        /// Robust equality check that handles enum/int mismatches, boxed values, and type conversions.
        /// </summary>
        private static bool AreEqual(object a, object b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;

            // Both enum — compare by underlying integer value
            if (a is Enum && b is Enum)
                return Convert.ToInt64(a) == Convert.ToInt64(b);

            // One enum, one convertible (e.g. int) — compare by underlying value
            if (a is Enum && b is IConvertible)
                return Convert.ToInt64(a) == Convert.ToInt64(b);
            if (b is Enum && a is IConvertible)
                return Convert.ToInt64(a) == Convert.ToInt64(b);

            // Numeric type mismatch (e.g. int vs long)
            if (a is IConvertible && b is IConvertible &&
                a.GetType() != b.GetType())
            {
                try
                {
                    return Convert.ToDecimal(a) == Convert.ToDecimal(b);
                }
                catch { }
            }

            return a.Equals(b) || b.Equals(a);
        }
    }

    public class ValueDropdownPopup : PopupWindowContent
    {
        private readonly List<ValueDropdownItem> items;
        private readonly Action<int> onSelect;
        private readonly int selectedIndex;
        private string searchText = "";
        private Vector2 scrollPosition;

        private ValueDropdownPopup(List<ValueDropdownItem> items, int selectedIndex, Action<int> onSelect)
        {
            this.items = items;
            this.selectedIndex = selectedIndex;
            this.onSelect = onSelect;
        }

        public static void Show(Rect activatorRect, List<ValueDropdownItem> items,
            int selectedIndex, Action<int> onSelect)
        {
            var popup = new ValueDropdownPopup(items, selectedIndex, onSelect);
            PopupWindow.Show(activatorRect, popup);
        }

        public override Vector2 GetWindowSize()
        {
            return new Vector2(300, Mathf.Min(items.Count * 22 + 30, 400));
        }

        public override void OnGUI(Rect rect)
        {
            // Search field
            Rect searchRect = new Rect(rect.x + 4, rect.y + 4, rect.width - 8, 20);
            GUI.SetNextControlName("SearchField");
            searchText = GUI.TextField(searchRect, searchText);
            EditorGUI.FocusTextInControl("SearchField");

            // List
            Rect scrollRect = new Rect(rect.x, rect.y + 28, rect.width, rect.height - 28);
            scrollPosition = GUI.BeginScrollView(scrollRect, scrollPosition,
                new Rect(0, 0, rect.width - 20, items.Count * 22));

            for (int i = 0; i < items.Count; i++)
            {
                if (!string.IsNullOrEmpty(searchText) &&
                    items[i].Text.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                Rect itemRect = new Rect(2, i * 22 + 2, rect.width - 22, 20);
                bool isSelected = i == selectedIndex;

                if (isSelected)
                    EditorGUI.DrawRect(itemRect, new Color(0.3f, 0.5f, 0.8f, 0.5f));

                GUIStyle style = new GUIStyle(EditorStyles.label);
                style.padding = new RectOffset(6, 0, 2, 0);

                if (GUI.Button(itemRect, items[i].Text, style))
                {
                    onSelect?.Invoke(i);
                    editorWindow.Close();
                }
            }

            GUI.EndScrollView();

            // Close on Escape
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                editorWindow.Close();
            }
        }
    }
}
