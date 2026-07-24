using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace TaoTie.Inspector.Editor
{
    public static class ButtonDrawer
    {
        public static void DrawButtons(object target, TaoTiePropertyProcessor processor)
        {
            if (target == null) return;

            List<MethodInfo> methods = processor.GetButtonMethods(target.GetType());
            if (methods == null || methods.Count == 0) return;

            foreach (var method in methods)
            {
                DrawSingleButton(target, method);
            }
        }

        public static void DrawButtons(object target, TaoTiePropertyProcessor processor, MethodInfo method)
        {
            if (target == null || method == null) return;
            DrawSingleButton(target, method);
        }

        private static void DrawSingleButton(object target, MethodInfo method)
        {
            var buttonAttr = method.GetCustomAttribute<ButtonAttribute>();
            if (buttonAttr == null) return;

            string buttonName = buttonAttr.Name ?? method.Name;
            var parameters = method.GetParameters();

            if (parameters.Length == 0)
            {
                DrawSimpleButton(buttonName, buttonAttr.Size, () => method.Invoke(target, null));
            }
            else
            {
                DrawParameterButton(buttonName, buttonAttr.Size, method, target, parameters);
            }
        }

        private static void DrawSimpleButton(string name, ButtonSizes size, System.Action onClick)
        {
            GUILayoutOption heightOpt = GetHeightOption(size);

            if (GUILayout.Button(name, heightOpt))
            {
                onClick?.Invoke();
            }
        }

        private static void DrawParameterButton(string name, ButtonSizes size,
            MethodInfo method, object target, ParameterInfo[] parameters)
        {
            // For methods with parameters, store parameter values in a cached dictionary
            // Using a simple approach: draw a foldout with parameter inputs and a button
            string key = method.DeclaringType.FullName + "." + method.Name;
            bool expanded = SessionState.GetBool(key + "_expanded", false);

            expanded = EditorGUILayout.Foldout(expanded, name, true);
            SessionState.SetBool(key + "_expanded", expanded);

            if (!expanded) return;

            EditorGUI.indentLevel++;

            // Draw parameter inputs
            var paramValues = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                string paramKey = key + "_" + parameters[i].Name;
                paramValues[i] = DrawParameterField(parameters[i], paramKey);
            }

            if (GUILayout.Button("Invoke", GetHeightOption(size)))
            {
                method.Invoke(target, paramValues);
            }

            EditorGUI.indentLevel--;
        }

        private static object DrawParameterField(ParameterInfo param, string key)
        {
            System.Type type = param.ParameterType;
            string label = ObjectNames.NicifyVariableName(param.Name);

            if (type == typeof(int))
            {
                int val = SessionState.GetInt(key, 0);
                val = EditorGUILayout.IntField(label, val);
                SessionState.SetInt(key, val);
                return val;
            }
            if (type == typeof(float))
            {
                float val = SessionState.GetFloat(key, 0f);
                val = EditorGUILayout.FloatField(label, val);
                SessionState.SetFloat(key, val);
                return val;
            }
            if (type == typeof(bool))
            {
                bool val = SessionState.GetBool(key, false);
                val = EditorGUILayout.Toggle(label, val);
                SessionState.SetBool(key, val);
                return val;
            }
            if (type == typeof(string))
            {
                string val = SessionState.GetString(key, "");
                val = EditorGUILayout.TextField(label, val);
                SessionState.SetString(key, val);
                return val;
            }
            if (type == typeof(Vector2))
            {
                string val = SessionState.GetString(key, "0,0");
                var v = ParseVector2(val);
                v = EditorGUILayout.Vector2Field(label, v);
                SessionState.SetString(key, v.x + "," + v.y);
                return v;
            }
            if (type == typeof(Vector3))
            {
                string val = SessionState.GetString(key, "0,0,0");
                var v = ParseVector3(val);
                v = EditorGUILayout.Vector3Field(label, v);
                SessionState.SetString(key, v.x + "," + v.y + "," + v.z);
                return v;
            }
            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            {
                var obj = EditorGUILayout.ObjectField(label, null, type, true);
                return obj;
            }
            if (type.IsEnum)
            {
                int val = SessionState.GetInt(key, 0);
                var enumVal = EditorGUILayout.EnumPopup(label, (System.Enum)System.Enum.ToObject(type, val));
                val = System.Convert.ToInt32(enumVal);
                SessionState.SetInt(key, val);
                return val;
            }

            EditorGUILayout.LabelField(label, $"Unsupported type: {type.Name}");
            return null;
        }

        private static GUILayoutOption GetHeightOption(ButtonSizes size)
        {
            return size switch
            {
                ButtonSizes.Small => GUILayout.Height(20),
                ButtonSizes.Medium => GUILayout.Height(28),
                ButtonSizes.Large => GUILayout.Height(40),
                ButtonSizes.Gigantic => GUILayout.Height(60),
                _ => GUILayout.Height(28)
            };
        }

        private static GUILayoutOption GetWidthOption(ButtonSizes size)
        {
            return null; // null = expand to full width
        }

        private static Vector2 ParseVector2(string s)
        {
            string[] parts = s.Split(',');
            if (parts.Length >= 2 && float.TryParse(parts[0], out float x) && float.TryParse(parts[1], out float y))
                return new Vector2(x, y);
            return Vector2.zero;
        }

        private static Vector3 ParseVector3(string s)
        {
            string[] parts = s.Split(',');
            if (parts.Length >= 3 &&
                float.TryParse(parts[0], out float x) &&
                float.TryParse(parts[1], out float y) &&
                float.TryParse(parts[2], out float z))
                return new Vector3(x, y, z);
            return Vector3.zero;
        }
    }
}
