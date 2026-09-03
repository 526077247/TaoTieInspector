using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace TaoTie.Inspector.Editor
{
    /// <summary>
    /// Runtime registration of custom editors into Unity's internal CustomEditorAttributes,
    /// so that editors are honored even in the Animator window (where a compile-time
    /// [CustomEditor(typeof(object), true)] fallback is not consulted for StateMachineBehaviour).
    ///
    /// This is a faithful port of the mechanism Odin uses (Sirenix.OdinInspector.Editor.CustomEditorUtility):
    /// it writes the internal MonoEditorType records that Unity's editor factory consults.
    /// </summary>
    internal static class CustomEditorUtility
    {
        private static class UniversalAPI
        {
            public static readonly Type CustomEditorAttributesType;
            public static readonly Type MonoEditorType;
            public static readonly FieldInfo MonoEditorType_InspectorType;
            public static readonly FieldInfo MonoEditorType_EditorForChildClasses;
            public static readonly FieldInfo MonoEditorType_IsFallback;
            public static readonly bool IsValid;

            static UniversalAPI()
            {
                bool valid = false;
                Type attributesType = null;
                Type monoEditorType = null;
                FieldInfo inspectorType = null;
                FieldInfo editorForChildClasses = null;
                FieldInfo isFallback = null;
                try
                {
                    attributesType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.CustomEditorAttributes");
                    monoEditorType = attributesType.GetNestedType("MonoEditorType", BindingFlags.NonPublic | BindingFlags.Public);
                    inspectorType = monoEditorType.GetField("m_InspectorType", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                        ?? monoEditorType.GetField("inspectorType", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    editorForChildClasses = monoEditorType.GetField("m_EditorForChildClasses", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                        ?? monoEditorType.GetField("editorForChildClasses", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    isFallback = monoEditorType.GetField("m_IsFallback", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                        ?? monoEditorType.GetField("isFallback", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    valid = inspectorType != null && editorForChildClasses != null && isFallback != null;
                }
                catch
                {
                    valid = false;
                }
                CustomEditorAttributesType = attributesType;
                MonoEditorType = monoEditorType;
                MonoEditorType_InspectorType = inspectorType;
                MonoEditorType_EditorForChildClasses = editorForChildClasses;
                MonoEditorType_IsFallback = isFallback;
                IsValid = valid;
            }
        }

        private static class Unity_2023_1_API
        {
            public static readonly bool IsValid;
            public static readonly PropertyInfo CustomEditorAttributesType_Instance;
            public static readonly MethodInfo CustomEditorAttributesType_Rebuild;
            public static readonly FieldInfo CustomEditorAttributesType_Cache;
            public static readonly FieldInfo CustomEditorCache_CustomEditorCacheDict;
            public static readonly Type MonoEditorTypeStorage_Type;
            public static readonly FieldInfo MonoEditorTypeStorage_CustomEditors;
            public static readonly FieldInfo MonoEditorTypeStorage_CustomEditorsMultiEdition;
            public static readonly Type Dictionary_Type_MonoEditorTypeStorage;
            public static readonly MethodInfo Dictionary_Type_MonoEditorTypeStorage_Add;
            public static readonly MethodInfo Dictionary_Type_MonoEditorTypeStorage_TryGetValue;

            static Unity_2023_1_API()
            {
                if (!UniversalAPI.IsValid)
                {
                    IsValid = false;
                    return;
                }
                bool valid = false;
                try
                {
                    CustomEditorAttributesType_Instance = UniversalAPI.CustomEditorAttributesType.GetProperty("instance", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                    CustomEditorAttributesType_Rebuild = UniversalAPI.CustomEditorAttributesType.GetMethod("Rebuild", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                    CustomEditorAttributesType_Cache = UniversalAPI.CustomEditorAttributesType.GetField("m_Cache", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    MonoEditorTypeStorage_Type = UniversalAPI.CustomEditorAttributesType.GetNestedType("MonoEditorTypeStorage", BindingFlags.NonPublic | BindingFlags.Public);
                    MonoEditorTypeStorage_CustomEditors = MonoEditorTypeStorage_Type.GetField("customEditors", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    MonoEditorTypeStorage_CustomEditorsMultiEdition = MonoEditorTypeStorage_Type.GetField("customEditorsMultiEdition", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    Type customEditorCacheType = UniversalAPI.CustomEditorAttributesType.GetNestedType("CustomEditorCache", BindingFlags.NonPublic | BindingFlags.Public);
                    CustomEditorCache_CustomEditorCacheDict = customEditorCacheType.GetField("m_CustomEditorCache", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    Dictionary_Type_MonoEditorTypeStorage = typeof(Dictionary<,>).MakeGenericType(typeof(Type), MonoEditorTypeStorage_Type);
                    Dictionary_Type_MonoEditorTypeStorage_Add = Dictionary_Type_MonoEditorTypeStorage.GetMethod("Add", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(Type), MonoEditorTypeStorage_Type }, null);
                    Dictionary_Type_MonoEditorTypeStorage_TryGetValue = Dictionary_Type_MonoEditorTypeStorage.GetMethod("TryGetValue", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(Type), MonoEditorTypeStorage_Type.MakeByRefType() }, null);
                    valid = CustomEditorAttributesType_Instance != null
                        && CustomEditorAttributesType_Rebuild != null
                        && CustomEditorAttributesType_Cache != null
                        && MonoEditorTypeStorage_CustomEditors != null
                        && MonoEditorTypeStorage_CustomEditorsMultiEdition != null
                        && CustomEditorCache_CustomEditorCacheDict != null
                        && Dictionary_Type_MonoEditorTypeStorage_Add != null
                        && Dictionary_Type_MonoEditorTypeStorage_TryGetValue != null;
                }
                catch
                {
                    valid = false;
                }
                IsValid = valid;
            }

            public static void RegisterCustomMonoEditorEntry(object entry, Type inspectedType, bool isMultiEditor)
            {
                if (!IsValid) return;
                object instance = CustomEditorAttributesType_Instance.GetValue(null);
                object cache = CustomEditorAttributesType_Cache.GetValue(instance);
                object dict = CustomEditorCache_CustomEditorCacheDict.GetValue(cache);
                object[] args = { inspectedType, null };
                if (!(bool)Dictionary_Type_MonoEditorTypeStorage_TryGetValue.Invoke(dict, args))
                {
                    object storage = Activator.CreateInstance(MonoEditorTypeStorage_Type);
                    MonoEditorTypeStorage_CustomEditors.SetValue(storage, Activator.CreateInstance(MonoEditorTypeStorage_CustomEditors.FieldType));
                    MonoEditorTypeStorage_CustomEditorsMultiEdition.SetValue(storage, Activator.CreateInstance(MonoEditorTypeStorage_CustomEditorsMultiEdition.FieldType));
                    args[1] = storage;
                    Dictionary_Type_MonoEditorTypeStorage_Add.Invoke(dict, args);
                }
                object entryStorage = args[1];
                ((IList)MonoEditorTypeStorage_CustomEditors.GetValue(entryStorage)).Insert(0, entry);
                if (isMultiEditor)
                {
                    ((IList)MonoEditorTypeStorage_CustomEditorsMultiEdition.GetValue(entryStorage)).Insert(0, entry);
                }
            }

            public static void ResetCustomEditors()
            {
                if (!IsValid) return;
                if (CustomEditorAttributesType_Rebuild.IsStatic)
                {
                    CustomEditorAttributesType_Rebuild.Invoke(null, null);
                }
                else
                {
                    object instance = CustomEditorAttributesType_Instance.GetValue(null);
                    CustomEditorAttributesType_Rebuild.Invoke(instance, null);
                }
            }
        }

        private static class Unity_Pre_2023_API
        {
            public static readonly bool IsValid;
            public static readonly bool IsBackedByADictionary;
            public static readonly FieldInfo CustomEditorAttributesType_CachedEditorForType;
            public static readonly FieldInfo CustomEditorAttributesType_CachedMultiEditorForType;
            public static readonly FieldInfo CustomEditorAttributesType_CustomEditors;
            public static readonly FieldInfo CustomEditorAttributesType_CustomMultiEditors;
            public static readonly FieldInfo MonoEditorType_InspectedType;

            static Unity_Pre_2023_API()
            {
                if (!UniversalAPI.IsValid)
                {
                    IsValid = false;
                    return;
                }
                bool valid = false;
                try
                {
                    CustomEditorAttributesType_CachedEditorForType = UniversalAPI.CustomEditorAttributesType.GetField("kCachedEditorForType", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                    CustomEditorAttributesType_CachedMultiEditorForType = UniversalAPI.CustomEditorAttributesType.GetField("kCachedMultiEditorForType", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                    CustomEditorAttributesType_CustomEditors = UniversalAPI.CustomEditorAttributesType.GetField("kSCustomEditors", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                    CustomEditorAttributesType_CustomMultiEditors = UniversalAPI.CustomEditorAttributesType.GetField("kSCustomMultiEditors", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                    MonoEditorType_InspectedType = UniversalAPI.MonoEditorType.GetField("m_InspectedType", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (CustomEditorAttributesType_CustomEditors == null
                        || CustomEditorAttributesType_CustomMultiEditors == null
                        || MonoEditorType_InspectedType == null)
                    {
                        throw new NullReferenceException();
                    }
                    IsBackedByADictionary = typeof(IDictionary).IsAssignableFrom(CustomEditorAttributesType_CustomEditors.FieldType);
                    valid = true;
                }
                catch
                {
                    valid = false;
                }
                IsValid = valid;
            }

            public static void RegisterCustomMonoEditorEntry(object entry, Type inspectedType, Type editorType, bool isMultiEditor)
            {
                if (!IsValid) return;
                MonoEditorType_InspectedType.SetValue(entry, inspectedType);
                if (IsBackedByADictionary)
                {
                    AddEntryToDictList((IDictionary)CustomEditorAttributesType_CustomEditors.GetValue(null), entry, inspectedType);
                    if (isMultiEditor)
                    {
                        AddEntryToDictList((IDictionary)CustomEditorAttributesType_CustomMultiEditors.GetValue(null), entry, inspectedType);
                    }
                    return;
                }
                if (CustomEditorAttributesType_CachedEditorForType != null && CustomEditorAttributesType_CachedMultiEditorForType != null)
                {
                    ((IDictionary)CustomEditorAttributesType_CachedEditorForType.GetValue(null))[inspectedType] = editorType;
                    if (isMultiEditor)
                    {
                        ((IDictionary)CustomEditorAttributesType_CachedMultiEditorForType.GetValue(null))[inspectedType] = editorType;
                    }
                }
                ((IList)CustomEditorAttributesType_CustomEditors.GetValue(null)).Insert(0, entry);
                if (isMultiEditor)
                {
                    ((IList)CustomEditorAttributesType_CustomMultiEditors.GetValue(null)).Insert(0, entry);
                }
            }

            private static void AddEntryToDictList(IDictionary dict, object entry, Type inspectedType)
            {
                object val;
                if (dict.Contains(inspectedType))
                {
                    val = dict[inspectedType];
                }
                else
                {
                    val = Activator.CreateInstance(typeof(List<>).MakeGenericType(UniversalAPI.MonoEditorType));
                    dict[inspectedType] = val;
                }
                ((IList)val).Insert(0, entry);
            }

            public static void ResetCustomEditors()
            {
                if (!IsValid) return;
                if (IsBackedByADictionary)
                {
                    ((IDictionary)CustomEditorAttributesType_CustomEditors.GetValue(null)).Clear();
                    ((IDictionary)CustomEditorAttributesType_CustomMultiEditors.GetValue(null)).Clear();
                }
                else
                {
                    if (CustomEditorAttributesType_CachedEditorForType != null)
                        ((IDictionary)CustomEditorAttributesType_CachedEditorForType.GetValue(null)).Clear();
                    if (CustomEditorAttributesType_CachedMultiEditorForType != null)
                        ((IDictionary)CustomEditorAttributesType_CachedMultiEditorForType.GetValue(null)).Clear();
                    ((IList)CustomEditorAttributesType_CustomEditors.GetValue(null)).Clear();
                    ((IList)CustomEditorAttributesType_CustomMultiEditors.GetValue(null)).Clear();
                }
                if (UniversalAPI.CustomEditorAttributesType.GetMethod("Rebuild", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static) != null)
                {
                    UniversalAPI.CustomEditorAttributesType.GetMethod("Rebuild", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
                }
            }
        }

        public static readonly bool IsValid;

        static CustomEditorUtility()
        {
            IsValid = UniversalAPI.IsValid && (Unity_2023_1_API.IsValid || Unity_Pre_2023_API.IsValid);
            if (!IsValid)
            {
                Debug.LogWarning(
                    "TaoTie.Inspector: Unity's internal custom editor management classes changed in this Unity version ("
                    + Application.unityVersion
                    + "). StateMachineBehaviour panels in the Animator window will not be taken over by TaoTie.Inspector.");
            }
        }

        public static void RegisterCustomEditor(Type inspectedType, Type editorType, bool isFallbackEditor, bool isEditorForChildClasses)
        {
            if (!IsValid) return;
            bool isMultiEditor = Attribute.IsDefined(editorType, typeof(CanEditMultipleObjects), false);
            object entry = Activator.CreateInstance(UniversalAPI.MonoEditorType);
            UniversalAPI.MonoEditorType_InspectorType.SetValue(entry, editorType);
            UniversalAPI.MonoEditorType_IsFallback.SetValue(entry, isFallbackEditor);
            UniversalAPI.MonoEditorType_EditorForChildClasses.SetValue(entry, isEditorForChildClasses);
            if (Unity_2023_1_API.IsValid)
                Unity_2023_1_API.RegisterCustomMonoEditorEntry(entry, inspectedType, isMultiEditor);
            else if (Unity_Pre_2023_API.IsValid)
                Unity_Pre_2023_API.RegisterCustomMonoEditorEntry(entry, inspectedType, editorType, isMultiEditor);
        }

        /// <summary>
        /// Rebuild Unity's internal custom editor table from the compile-time [CustomEditor] scan,
        /// clearing any runtime-injected entries. Called before re-registering to keep the table
        /// deterministic across script reloads (same as Odin). This may transiently drop other
        /// packages' runtime-injected editors until they re-register on their own reload.
        /// </summary>
        public static void ResetCustomEditors()
        {
            if (Unity_2023_1_API.IsValid)
                Unity_2023_1_API.ResetCustomEditors();
            else if (Unity_Pre_2023_API.IsValid)
                Unity_Pre_2023_API.ResetCustomEditors();
        }
    }
}
