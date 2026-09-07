using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace TaoTie.Inspector.Editor
{
    /// <summary>
    /// Field-level copy/paste for Inspect object &amp; array values.
    /// Values are deep-cloned through a Json.NET round-trip (TypeNameHandling.Auto), which
    /// supports nested [Serializable] classes, List/Array/Dictionary and [SerializeReference]
    /// polymorphism. UnityEngine.Object references keep their identity (recorded in UnityRefs).
    /// Clipboard is session-scoped, like the graph-level copy/paste in GraphHandle.
    /// </summary>
    internal static class FieldCopyPaste
    {
        private sealed class ClipboardEntry
        {
            public Type ValueType;
            public string Json;
            public List<UnityEngine.Object> UnityRefs = new List<UnityEngine.Object>();
        }

        private static ClipboardEntry s_Clipboard;
        private static bool s_FieldMenuShown;
        private static double s_MenuShownTime = -1d;

        public static bool HasClipboard => s_Clipboard != null;

        // -------------------- Serialization --------------------

        private static JsonSerializerSettings BuildSettings(List<UnityEngine.Object> unityRefs)
        {
            return new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                NullValueHandling = NullValueHandling.Include,
                Converters = { new UnityObjectConverter(unityRefs) },
            };
        }

        /// <summary>
        /// Copy a boxed value (deep-cloned later, on paste). The clipboard keeps the serialized
        /// payload so the value is fully detached from the source object graph.
        /// </summary>
        public static void CopyValue(object value)
        {
            var valueType = value?.GetType();
            var refs = new List<UnityEngine.Object>();
            string json;
            try
            {
                json = JsonConvert.SerializeObject(value, valueType ?? typeof(object), BuildSettings(refs));
            }
            catch (Exception)
            {
                return; // value is not round-trippable (delegates, open generic, custom converters, ...)
            }
            s_Clipboard = new ClipboardEntry { ValueType = valueType, Json = json, UnityRefs = refs };
        }

        /// <summary>Round-trip the clipboard payload into a fresh object graph (deep clone).</summary>
        public static object CloneClipboard()
        {
            if (s_Clipboard == null) return null;
            try
            {
                // Pass the copied root type explicitly: TypeNameHandling.Auto omits the root
                // $type when it matches the copied (runtime) type, so an untyped DeserializeObject
                // would otherwise return a JObject/JArray instead of the real managed instance.
                return JsonConvert.DeserializeObject(s_Clipboard.Json,
                    s_Clipboard.ValueType ?? typeof(object), BuildSettings(s_Clipboard.UnityRefs));
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Copied value type (null when a null value was copied).</summary>
        public static Type GetClipboardValueType()
        {
            return s_Clipboard?.ValueType;
        }

        /// <summary>Can the clipboard be pasted into a field of type fieldType?</summary>
        public static bool CanPasteInto(Type fieldType)
        {
            if (s_Clipboard == null || fieldType == null) return false;
            if (s_Clipboard.ValueType == null)
                return !fieldType.IsValueType; // copying a null value: only reference types accept it
            return fieldType.IsAssignableFrom(s_Clipboard.ValueType);
        }

        /// <summary>
        /// Can the clipboard's collection items be appended onto a target array/list field?
        /// Requires the copied value to be a non-null collection whose element type fits the target.
        /// </summary>
        public static bool CanAppendInto(Type fieldType)
        {
            if (s_Clipboard == null || fieldType == null) return false;
            var srcType = s_Clipboard.ValueType;
            if (srcType == null || !typeof(IList).IsAssignableFrom(srcType)) return false;
            bool dstIsCollection = fieldType.IsArray || typeof(IList).IsAssignableFrom(fieldType);
            if (!dstIsCollection) return false;
            var srcElem = GetElementTypeOf(srcType);
            var dstElem = GetElementTypeOf(fieldType);
            if (srcElem == null || dstElem == null) return true; // non-generic collections — best effort
            return dstElem.IsAssignableFrom(srcElem);
        }

        // -------------------- SerializedProperty path --------------------

        /// <summary>
        /// Walk serializedObject.targetObject along propertyPath and return the live runtime value.
        /// Works for plain fields, nested [Serializable] Generic, arrays/lists (.Array.data[N]) and
        /// [SerializeReference]: the serialized object graph mirrors the runtime fields.
        /// </summary>
        public static object GetSPRuntimeValue(SerializedProperty property)
        {
            var root = property.serializedObject?.targetObject;
            if (root == null) return null;
            var path = property.propertyPath;
            if (string.IsNullOrEmpty(path)) return root;
            object current = root;
            var segments = path.Split('.');
            for (int i = 0; i < segments.Length && current != null; i++)
            {
                var seg = segments[i];
                if (seg == "Array") continue;
                if (seg.StartsWith("data[", StringComparison.Ordinal) && seg.EndsWith("]", StringComparison.Ordinal))
                {
                    int idx;
                    if (int.TryParse(seg.Substring(5, seg.Length - 6), out idx) && current is IList list && idx >= 0 && idx < list.Count)
                        current = list[idx];
                    else
                        return null;
                    continue;
                }
                var field = TaoTiePropertyEntry.GetCachedFieldPublic(current.GetType(), seg);
                if (field == null) return null;
                current = field.GetValue(current);
            }
            return current;
        }

        /// <summary>
        /// Resolve the DECLARED field type for a SerializedProperty (for paste validation), by
        /// walking the object graph types along propertyPath.
        /// </summary>
        public static Type ResolveDeclaredType(SerializedProperty property)
        {
            var root = property.serializedObject?.targetObject;
            if (root == null) return null;
            var path = property.propertyPath;
            if (string.IsNullOrEmpty(path)) return root.GetType();
            Type type = root.GetType();
            var segments = path.Split('.');
            for (int i = 0; i < segments.Length; i++)
            {
                var seg = segments[i];
                if (seg == "Array") continue;
                if (seg.StartsWith("data[", StringComparison.Ordinal) && seg.EndsWith("]", StringComparison.Ordinal))
                {
                    type = GetElementTypeOf(type);
                    if (type == null) return null;
                    continue;
                }
                var field = TaoTiePropertyEntry.GetCachedFieldPublic(type, seg);
                if (field == null) return null;
                type = field.FieldType;
            }
            return type;
        }

        private static Type GetElementTypeOf(Type collectionType)
        {
            if (collectionType == null) return null;
            if (collectionType.IsArray) return collectionType.GetElementType();
            foreach (var iface in collectionType.GetInterfaces())
            {
                if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IList<>))
                    return iface.GetGenericArguments()[0];
            }
            return null;
        }

        /// <summary>Copy the current value of a SerializedProperty into the clipboard.</summary>
        public static void CopyFromProperty(SerializedProperty property)
        {
            if (property == null) return;
            // The menu callback may fire several frames later — re-resolve the property from the
            // path so the handle is still valid for the current serializedObject state.
            if (property.serializedObject != null && !string.IsNullOrEmpty(property.propertyPath))
            {
                var fresh = property.serializedObject.FindProperty(property.propertyPath);
                if (fresh != null) property = fresh;
            }
            object value;
            try
            {
                value = GetSPRuntimeValue(property);
            }
            catch (Exception)
            {
                return;
            }
            CopyValue(value);
        }

        /// <summary>Paste the clipboard into a SerializedProperty using Unity's drafted value model.</summary>
        public static bool PasteIntoProperty(SerializedProperty property)
        {
            if (property == null) return false;
            if (property.serializedObject != null && !string.IsNullOrEmpty(property.propertyPath))
            {
                var fresh = property.serializedObject.FindProperty(property.propertyPath);
                if (fresh != null) property = fresh;
            }
            var declaredType = ResolveDeclaredType(property);
            if (!CanPasteInto(declaredType)) return false;
            var value = CloneClipboard();
            if (value == null && s_Clipboard?.ValueType != null) return false; // clone/cast failure
            try
            {
                if (!ApplySPValue(property, value)) return false;
            }
            catch (Exception)
            {
                return false;
            }
            var so = property.serializedObject;
            so?.ApplyModifiedProperties();
            so?.Update();
            if (so?.targetObject != null)
                EditorUtility.SetDirty(so.targetObject);
            TaoTiePropertyLayout.RequestInspectorRefresh();
            return true;
        }

        /// <summary>Append the clipboard's collection items onto a SerializedProperty array/list.</summary>
        public static bool AppendIntoProperty(SerializedProperty property)
        {
            if (property == null) return false;
            if (property.serializedObject != null && !string.IsNullOrEmpty(property.propertyPath))
            {
                var fresh = property.serializedObject.FindProperty(property.propertyPath);
                if (fresh != null) property = fresh;
            }
            var declaredType = ResolveDeclaredType(property);
            if (!CanAppendInto(declaredType)) return false;
            var value = CloneClipboard();
            if (!(value is IList items) || items.Count == 0) return false;
            try
            {
                int start = property.arraySize;
                property.arraySize = start + items.Count;
                for (int i = 0; i < items.Count; i++)
                {
                    if (!ApplySPValue(property.GetArrayElementAtIndex(start + i), items[i]))
                        return false;
                }
            }
            catch (Exception)
            {
                return false;
            }
            var so = property.serializedObject;
            so?.ApplyModifiedProperties();
            so?.Update();
            if (so?.targetObject != null)
                EditorUtility.SetDirty(so.targetObject);
            TaoTiePropertyLayout.RequestInspectorRefresh();
            return true;
        }

        /// <summary>
        /// Write a runtime value into a SerializedProperty with full coverage of leaf types,
        /// Generic (nested serializable classes), arrays/lists and ManagedReference.
        /// </summary>
        private static bool ApplySPValue(SerializedProperty prop, object value)
        {
            // Unity has no SerializedPropertyType.Array: arrays/lists are detected via isArray
            // (their propertyType reports Generic for object lists, but element access is uniform).
            if (prop.isArray)
            {
                if (value == null)
                {
                    prop.arraySize = 0;
                    return true;
                }
                var list = value as IList;
                if (list == null) return false;
                prop.arraySize = list.Count;
                for (int i = 0; i < list.Count; i++)
                {
                    if (!ApplySPValue(prop.GetArrayElementAtIndex(i), list[i]))
                        return false;
                }
                return true;
            }
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    prop.longValue = Convert.ToInt64(value);
                    return true;
                case SerializedPropertyType.Float:
                    prop.doubleValue = Convert.ToDouble(value);
                    return true;
                case SerializedPropertyType.Boolean:
                    prop.boolValue = (bool)value;
                    return true;
                case SerializedPropertyType.String:
                    prop.stringValue = (string)value;
                    return true;
                case SerializedPropertyType.Enum:
                    prop.intValue = Convert.ToInt32(value);
                    return true;
                case SerializedPropertyType.ObjectReference:
                    prop.objectReferenceValue = value as UnityEngine.Object;
                    return true;
                case SerializedPropertyType.Color:
                    prop.colorValue = (Color)value;
                    return true;
                case SerializedPropertyType.Vector2:
                    prop.vector2Value = (Vector2)value;
                    return true;
                case SerializedPropertyType.Vector3:
                    prop.vector3Value = (Vector3)value;
                    return true;
                case SerializedPropertyType.Vector4:
                    prop.vector4Value = (Vector4)value;
                    return true;
                case SerializedPropertyType.Vector2Int:
                    prop.vector2IntValue = (Vector2Int)value;
                    return true;
                case SerializedPropertyType.Vector3Int:
                    prop.vector3IntValue = (Vector3Int)value;
                    return true;
                case SerializedPropertyType.Rect:
                    prop.rectValue = (Rect)value;
                    return true;
                case SerializedPropertyType.RectInt:
                    prop.rectIntValue = (RectInt)value;
                    return true;
                case SerializedPropertyType.Bounds:
                    prop.boundsValue = (Bounds)value;
                    return true;
                case SerializedPropertyType.BoundsInt:
                    prop.boundsIntValue = (BoundsInt)value;
                    return true;
                case SerializedPropertyType.Quaternion:
                    prop.quaternionValue = (Quaternion)value;
                    return true;
                case SerializedPropertyType.AnimationCurve:
                    prop.animationCurveValue = (AnimationCurve)value;
                    return true;
                case SerializedPropertyType.Gradient:
                    prop.gradientValue = (Gradient)value;
                    return true;
                case SerializedPropertyType.LayerMask:
                    prop.intValue = ((LayerMask)value).value;
                    return true;
                case SerializedPropertyType.Hash128:
                    prop.hash128Value = (Hash128)value;
                    return true;
                case SerializedPropertyType.ExposedReference:
                    prop.exposedReferenceValue = value as UnityEngine.Object;
                    return true;
                case SerializedPropertyType.ManagedReference:
                    prop.managedReferenceValue = value;
                    return true;
                case SerializedPropertyType.Generic:
                    {
                        // A plain[Serializable] class; walk children and set leaf values one by one.
                        if (value == null) return false; // Generic can never be null in Unity's model
                        var child = prop.Copy();
                        int targetDepth = prop.depth + 1;
                        if (child.NextVisible(true))
                        {
                            do
                            {
                                if (child.depth != targetDepth) break;
                                var field = TaoTiePropertyEntry.GetCachedFieldPublic(value.GetType(), child.name);
                                object fieldValue = field?.GetValue(value);
                                if (field != null && !ApplySPValue(child, fieldValue))
                                    return false;
                            } while (child.NextVisible(false));
                        }
                        return true;
                    }
                default:
                    // Unsupported serialized types (Character, FixedBuffer...) — leave untouched.
                    return true;
            }
        }

        // -------------------- Reflection path --------------------

        /// <summary>Should a field show the copy/paste context menu at all?</summary>
        public static bool ShouldOfferMenu(SerializedProperty property)
        {
            if (property == null) return false;
            return property.isArray
                || property.propertyType == SerializedPropertyType.Generic
                || property.propertyType == SerializedPropertyType.ManagedReference;
        }

        /// <summary>Should a reflection field show the copy/paste context menu?</summary>
        public static bool ShouldOfferMenu(Type fieldType)
        {
            if (fieldType == null) return false;
            if (fieldType.IsArray) return true;
            if (typeof(IList).IsAssignableFrom(fieldType) || typeof(IDictionary).IsAssignableFrom(fieldType)) return true;
            // Reference types that are not plain Unity objects are (potentially) nested objects.
            if (!fieldType.IsClass) return false;
            return fieldType != typeof(string)
                && !typeof(UnityEngine.Object).IsAssignableFrom(fieldType);
        }

        /// <summary>Paste the clipboard into a reflection field (DrawBase path / unserialized fields).</summary>
        public static bool PasteIntoField(FieldInfo field, object obj)
        {
            if (field == null || obj == null) return false;
            if (!CanPasteInto(field.FieldType)) return false;
            var value = CloneClipboard();
            if (value == null && s_Clipboard?.ValueType != null) return false;
            try
            {
                if (obj is UnityEngine.Object uo && uo != null)
                    Undo.RecordObject(uo, "Paste Field");
                field.SetValue(obj, value);
if (obj is UnityEngine.Object uo2 && uo2 != null)
                        EditorUtility.SetDirty(uo2);
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        /// <summary>Append the clipboard's collection items onto a reflection array/IList field.</summary>
        public static bool AppendIntoField(FieldInfo field, object obj)
        {
            if (field == null || obj == null) return false;
            if (field.FieldType.IsArray)
            {
                if (!CanAppendInto(field.FieldType)) return false;
                try
                {
                    if (obj is UnityEngine.Object uo && uo != null)
                        Undo.RecordObject(uo, "Append to Array");
                    var value = CloneClipboard() as IList;
                    if (value == null || value.Count == 0) return false;
                    var elementType = field.FieldType.GetElementType();
                    var current = field.GetValue(obj) as Array;
                    int oldLen = current?.Length ?? 0;
                    var newArr = Array.CreateInstance(elementType, oldLen + value.Count);
                    if (current != null)
                        Array.Copy(current, newArr, oldLen);
                    for (int i = 0; i < value.Count; i++)
                        newArr.SetValue(ConvertToType(value[i], elementType), oldLen + i);
                    field.SetValue(obj, newArr);
                    if (obj is UnityEngine.Object uo2 && uo2 != null)
                        EditorUtility.SetDirty(uo2);
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            }
            if (typeof(IList).IsAssignableFrom(field.FieldType))
            {
                if (!CanAppendInto(field.FieldType)) return false;
                try
                {
                    if (obj is UnityEngine.Object uo && uo != null)
                        Undo.RecordObject(uo, "Append to Array");
                    var value = CloneClipboard() as IList;
                    if (value == null || value.Count == 0) return false;
                    var list = field.GetValue(obj) as IList;
                    if (list == null)
                    {
                        list = (IList)Activator.CreateInstance(field.FieldType);
                        field.SetValue(obj, list);
                    }
                    foreach (var item in value)
                        list.Add(item);
                    if (obj is UnityEngine.Object uo2 && uo2 != null)
                        EditorUtility.SetDirty(uo2);
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            }
            return false;
        }

        private static object ConvertToType(object value, Type targetType)
        {
            if (value == null || targetType == null || targetType.IsInstanceOfType(value)) return value;
            return Convert.ChangeType(value, targetType);
        }

        // -------------------- Context menu --------------------

        /// <summary>Show the Copy/Paste context menu when a right-click lands inside the field rect.</summary>
        public static void ShowFieldContextMenu(Rect rect, bool canPaste, bool canAppend,
            Action onCopy, Action onPaste, Action onAppend)
        {
            if (Event.current == null) return;
            var evt = Event.current;
            // Some IMGUI hosts (e.g. this graph window) deliver right-click as MouseUp(button 1)
            // and never send ContextClick; others (DrawerWindow) send ContextClick and a MouseUp
            // pair for the same gesture. Support both triggers.
            bool isTrigger = evt.type == EventType.ContextClick
                || (evt.type == EventType.MouseUp && evt.button == 1);
            if (!isTrigger) return;
            double tss = EditorApplication.timeSinceStartup;
            // One field menu per gesture: skips both a parent-object block + child row overlap
            // (same pass, identical time) and the [ContextClick, then MouseUp] pair Unity may
            // deliver for a single right-click.
            if (s_MenuShownTime > 0 && tss - s_MenuShownTime < 0.5d) return;
            s_FieldMenuShown = false;
            // Convert BOTH the layout rect and the (same-space) mouse position into screen pixels
            // through the identical matrix chain. This is space-agnostic: it cancels out GUI.Window
            // zoom scaling, BeginArea/BeginClip origins and ScrollView translations regardless of
            // how Unity reports Event.current.mousePosition in the current GUI context.
            var m0 = GUIUtility.GUIToScreenPoint(new Vector2(rect.xMin, rect.yMin));
            var m1 = GUIUtility.GUIToScreenPoint(new Vector2(rect.xMax, rect.yMax));
            var mouse = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);
            var screenRect = new Rect(
                Mathf.Min(m0.x, m1.x), Mathf.Min(m0.y, m1.y),
                Mathf.Abs(m1.x - m0.x), Mathf.Abs(m1.y - m0.y));
            if (!screenRect.Contains(mouse)) return;
            evt.Use();
            s_FieldMenuShown = true;
            s_MenuShownTime = tss;
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Copy Field"), false, () => onCopy());
            if (canPaste)
                menu.AddItem(new GUIContent("Paste Field"), false, () => onPaste());
            else
                menu.AddDisabledItem(new GUIContent("Paste Field"));
            if (canAppend)
                menu.AddItem(new GUIContent("Append to Array"), false, () => onAppend());
            else
                menu.AddDisabledItem(new GUIContent("Append to Array"));
            menu.ShowAsContext();
        }

        /// <summary>
        /// Consumed in the same right-click gesture: reports whether a field context menu was shown
        /// so the caller can skip its own (e.g. the graph's node/canvas) context menu for this click.
        /// </summary>
        public static bool ConsumeSuppressGraphMenu()
        {
            bool shown = s_FieldMenuShown;
            s_FieldMenuShown = false;
            return shown;
        }
    }

    /// <summary>
    /// Json.NET converter that keeps UnityEngine.Object references by identity instead of
    /// serializing their contents: each referenced object is recorded in a side list and
    /// replaced by its index during the round-trip.
    /// </summary>
    internal sealed class UnityObjectConverter : JsonConverter
    {
        private readonly List<UnityEngine.Object> m_Refs;

        public UnityObjectConverter(List<UnityEngine.Object> refs)
        {
            m_Refs = refs;
        }

        public override bool CanConvert(Type objectType)
        {
            return typeof(UnityEngine.Object).IsAssignableFrom(objectType);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var uo = value as UnityEngine.Object;
            if (uo == null)
            {
                writer.WriteNull();
                return;
            }
            int index = m_Refs.Count;
            m_Refs.Add(uo);
            writer.WriteStartObject();
            writer.WritePropertyName("__ref");
            writer.WriteValue(index);
            writer.WriteEndObject();
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;
            var obj = JObject.Load(reader);
            if (obj["__ref"] == null) return null;
            int index = obj["__ref"].Value<int>();
            if (index < 0 || index >= m_Refs.Count) return null;
            return m_Refs[index];
        }
    }
}