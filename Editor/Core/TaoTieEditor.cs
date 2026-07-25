using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace TaoTie.Inspector.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(object), true)]
    public class TaoTieEditor : UnityEditor.Editor
    {
        private TaoTiePropertyProcessor processor;
        private TaoTieGroupManager groupManager;
        private DrawBase drawBase;
        private bool useEnhancedDrawing;
        private bool initialized;

        private void Initialize()
        {
            if (initialized) return;

            processor = new TaoTiePropertyProcessor();
            groupManager = new TaoTieGroupManager();
            drawBase = new DrawBase();

            if (target != null)
            {
                System.Type targetType = target.GetType();
                bool hasDrawWithUnity = targetType.IsDefined(typeof(DrawWithUnityAttribute), true);
                bool forceEnhanced = typeof(IForceTaoTieDrawing).IsAssignableFrom(targetType);
                useEnhancedDrawing = !hasDrawWithUnity &&
                    (forceEnhanced || TaoTiePropertyProcessor.HasAnyTaoTieAttributes(targetType));
            }
            else
            {
                useEnhancedDrawing = false;
            }

            initialized = true;
        }

        protected void OnEnable()
        {
            initialized = false;
            Initialize();
        }

        protected void OnDisable()
        {
            groupManager?.Dispose();
        }

        public override void OnInspectorGUI()
        {
            if (!initialized) Initialize();

            if (!useEnhancedDrawing || target == null)
            {
                DrawDefaultInspector();
                return;
            }

            serializedObject.Update();

            // Rebuild entries every frame when there are pending managed reference changes
            if (TaoTiePropertyLayout.HasPendingManagedReferenceChanges())
            {
                processor.ClearCache();
                TaoTiePropertyLayout.ApplyPendingManagedReferences(serializedObject);
                serializedObject.ApplyModifiedProperties();
                serializedObject.Update();
            }

            var entries = processor.BuildEntries(serializedObject);
            processor.RefreshDynamicState(entries, target);

            // Set all entries for DrawArrayBox to find child entries (LabelText etc.)
            TaoTiePropertyLayout.SetAllEntries(entries);

            // Build a merged list of SerializedProperty entries AND unserialized fields (Dictionary etc.)
            // in declaration order, so Dictionary fields appear at their correct position among siblings.
            var mergedEntries = BuildMergedEntries(entries, target);

            // Convert to GroupEntryData and draw via unified TaoTieGroupManager
            var groupEntries = ConvertToGroupData(mergedEntries);

            // Insert button entries at correct declaration-order positions
            var buttonMethods = processor.GetButtonMethods(target.GetType());
            if (buttonMethods != null && buttonMethods.Count > 0)
            {
                // Build a map of field name → index in groupEntries
                var fieldIndexMap = new Dictionary<string, int>();
                for (int i = 0; i < groupEntries.Count; i++)
                {
                    var e = groupEntries[i].UserData as TaoTiePropertyEntry;
                    if (e != null && !string.IsNullOrEmpty(e.PropertyName))
                        fieldIndexMap[e.PropertyName] = i;
                }
                // Build a map of field MetadataToken for ordering
                var typeHierarchy = new List<Type>();
                Type currentType = target.GetType();
                while (currentType != null && currentType != typeof(object))
                {
                    typeHierarchy.Insert(0, currentType);
                    currentType = currentType.BaseType;
                }
                // Collect fields with their MetadataToken
                var fieldTokens = new Dictionary<string, int>();
                foreach (var t in typeHierarchy)
                {
                    foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        fieldTokens[f.Name] = f.MetadataToken;
                    }
                }
                // Create button entries and insert at correct positions
                var insertList = new List<(int index, GroupEntryData data)>();
                foreach (var method in buttonMethods)
                {
                    var btnEntry = new GroupEntryData
                    {
                        Visible = true,
                        UserData = method
                    };
                    // Find insertion index based on MetadataToken
                    int insertAt = groupEntries.Count;
                    for (int i = groupEntries.Count - 1; i >= 0; i--)
                    {
                        var e = groupEntries[i].UserData as TaoTiePropertyEntry;
                        if (e != null && fieldTokens.TryGetValue(e.PropertyName, out var token) && token < method.MetadataToken)
                        {
                            insertAt = i + 1;
                            break;
                        }
                    }
                    insertList.Add((insertAt, btnEntry));
                }
                // Insert in reverse order to preserve indices
                insertList.Sort((a, b) => b.index.CompareTo(a.index));
                foreach (var (index, data) in insertList)
                    groupEntries.Insert(index, data);
            }

            // Collect paths of managed reference fields (TypeFilter / HideReferenceObjectPicker)
            // whose children should be skipped (drawn manually inside the parent's foldout)
            var managedRefPaths = new HashSet<string>();
            foreach (var e in mergedEntries)
            {
                try
                {
                    if ((e.TypeFilter != null || e.HideReferenceObjectPicker != null)
                        && e.Property != null
                        && e.Property.propertyType == SerializedPropertyType.ManagedReference)
                    {
                        managedRefPaths.Add(e.PropertyPath + ".");
                    }
                }
                catch { /* property may be disposed after array element deletion */ }
            }
            // Collect pending managed reference clear paths
            var pendingClearPaths = TaoTiePropertyLayout.GetPendingClearPaths();
            foreach (var p in pendingClearPaths)
                managedRefPaths.Add(p + ".");

            // Skip children of managed reference fields
            if (managedRefPaths.Count > 0)
            {
                foreach (var ge in groupEntries)
                {
                    if (!ge.Visible) continue;
                    var e = ge.UserData as TaoTiePropertyEntry;
                    if (e?.PropertyPath == null) continue;
                    foreach (var mrefPath in managedRefPaths)
                    {
                        if (e.PropertyPath.StartsWith(mrefPath))
                        {
                            ge.Visible = false;
                            break;
                        }
                    }
                }
            }
            groupManager.DrawGroupedEntries(groupEntries, data =>
            {
                if (data.UserData is TaoTiePropertyEntry entry)
                {
                    TaoTiePropertyLayout.DrawProperty(entry, target);
                }
                else if (data.UserData is MethodInfo method)
                {
                    ButtonDrawer.DrawButtons(target, processor, method);
                }
            });

            serializedObject.ApplyModifiedProperties();
            TaoTiePropertyLayout.ApplyPendingManagedReferences(serializedObject);
            serializedObject.ApplyModifiedProperties();
            TaoTiePropertyLayout.FlushPendingCallbacks();
        }

        /// <summary>
        /// Build a merged list of SerializedProperty entries and unserialized reflection fields
        /// (Dictionary, etc.) in declaration order.
        /// </summary>
        private List<TaoTiePropertyEntry> BuildMergedEntries(List<TaoTiePropertyEntry> serializedEntries, object obj)
        {
            var result = new List<TaoTiePropertyEntry>();
            var type = obj.GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;

            // Collect all field names in declaration order (base to derived)
            var fieldOrder = new List<FieldInfo>();
            var typeHierarchy = new List<Type>();
            Type currentType = type;
            while (currentType != null && currentType != typeof(object))
            {
                typeHierarchy.Insert(0, currentType);
                currentType = currentType.BaseType;
            }
            foreach (var t in typeHierarchy)
                fieldOrder.AddRange(t.GetFields(flags));

            // Index serialized entries by their property name for quick lookup
            var serializedByName = new Dictionary<string, TaoTiePropertyEntry>();
            foreach (var e in serializedEntries)
            {
                // Only top-level (depth 0) entries
                if (e.PropertyPath != null && !e.PropertyPath.Contains('.'))
                    serializedByName[e.PropertyName] = e;
            }

            int defaultOrder = 0;
            var usedSerializedEntries = new HashSet<TaoTiePropertyEntry>();

            foreach (var field in fieldOrder)
            {
                if (serializedByName.TryGetValue(field.Name, out var serEntry))
                {
                    result.Add(serEntry);
                    usedSerializedEntries.Add(serEntry);
                }
                else
                {
                    // Check if this is an unserialized collection field (Dictionary, etc.)
                    var fieldType = field.FieldType;
                    bool isDictionary = typeof(IDictionary).IsAssignableFrom(fieldType);
                    bool isArray = fieldType.IsArray;
                    bool isList = typeof(IList).IsAssignableFrom(fieldType) && !isArray;

                    // Skip if it's serialized by Unity (e.g. has [SerializeField])
                    if (serializedObject.FindProperty(field.Name) != null) continue;
                    // Skip Unity built-in fields
                    if (field.DeclaringType == typeof(UnityEngine.Object) ||
                        field.DeclaringType == typeof(UnityEngine.ScriptableObject) ||
                        field.DeclaringType == typeof(UnityEngine.MonoBehaviour) ||
                        field.DeclaringType == typeof(UnityEngine.Behaviour) ||
                        field.DeclaringType == typeof(UnityEngine.Component) ||
                        field.DeclaringType == typeof(UnityEditor.EditorWindow) ||
                        field.DeclaringType == typeof(UnityEditor.Editor)) continue;
                    // Skip event backing fields
                    if (field.DeclaringType?.GetEvent(field.Name, flags) != null) continue;
                    // Skip NonSerialized (but Dictionary is inherently non-serialized, so check attribute explicitly)
                    if (field.IsDefined(typeof(NonSerializedAttribute), false)) continue;
                    // Skip DrawIgnore(All)
                    if (field.GetCustomAttribute<DrawIgnoreAttribute>() is DrawIgnoreAttribute dia && dia.Ignore == Ignore.All) continue;

                    // Also handle plain class fields (non-serialized, non-collection)
                    bool isPlainClass = field.FieldType.IsClass
                        && field.FieldType != typeof(string)
                        && !typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType)
                        && !isDictionary && !isArray && !isList
                        && !field.FieldType.IsAbstract;

                    if (!isDictionary && !isArray && !isList && !isPlainClass) continue;

                    // Create a synthetic entry for this unserialized field
                    var entry = new TaoTiePropertyEntry
                    {
                        PropertyPath = field.Name,
                        PropertyName = field.Name,
                        Order = defaultOrder,
                        LabelOverride = field.GetCustomAttribute<LabelTextAttribute>()?.Text,
                        TooltipText = field.GetCustomAttribute<TooltipAttribute>()?.tooltip,
                        FoldoutGroup = field.GetCustomAttribute<FoldoutGroupAttribute>(),
                        BoxGroup = field.GetCustomAttribute<BoxGroupAttribute>(),
                        TabGroup = field.GetCustomAttribute<TabGroupAttribute>(),
                        HorizontalGroup = field.GetCustomAttribute<HorizontalGroupAttribute>(),
                        Title = field.GetCustomAttribute<TitleAttribute>(),
                        ReadOnly = field.GetCustomAttribute<ReadOnlyAttribute>(),
                        DrawIgnore = field.GetCustomAttribute<DrawIgnoreAttribute>(),
                        DisableInEditorMode = field.GetCustomAttribute<DisableInEditorModeAttribute>(),
                        OnCollectionChanged = field.GetCustomAttribute<OnCollectionChangedAttribute>(),
                        OnStateUpdate = field.GetCustomAttribute<OnStateUpdateAttribute>(),
                        HasTaoTieAttributes = true,
                        Visible = true,
                        Enabled = true,
                        // Mark as reflection-drawn field
                        IsReflectionField = true,
                        ReflectionField = field,
                    };
                    result.Add(entry);
                }
                defaultOrder++;
            }

            // Add remaining serialized entries that weren't matched (nested properties, etc.)
            foreach (var e in serializedEntries)
            {
                if (!usedSerializedEntries.Contains(e))
                    result.Add(e);
            }

            return result;
        }

        private static List<GroupEntryData> ConvertToGroupData(List<TaoTiePropertyEntry> entries)
        {
            var result = new List<GroupEntryData>(entries.Count);
            // Collect TableList paths so we can mark children as invisible
            var tableListPaths = new HashSet<string>();
            foreach (var e in entries)
            {
                if (e.TableList != null && e.Property != null && e.Property.isArray)
                    tableListPaths.Add(e.PropertyPath + ".");
            }

            foreach (var e in entries)
            {
                var data = new GroupEntryData
                {
                    Visible = e.Visible,
                    IsFoldoutContainer = e.IsFoldoutGroup,
                    ContainerName = e.FoldoutGroupName,
                    ContainerPath = e.PropertyPath,
                    ContainerExpanded = e.FoldoutExpanded,
                    BoxGroupName = e.BoxGroup?.GroupName,
                    FoldoutGroupName = e.FoldoutGroup?.GroupName,
                    TabGroupName = e.TabGroup?.GroupName,
                    TabName = e.TabGroup?.TabName,
                    HorizontalGroupName = e.HorizontalGroup?.GroupName,
                    IsTableList = e.TableList != null && e.Property != null && e.Property.isArray,
                    UserData = e
                };
                // Skip children of TableList entries (they're drawn inside the table)
                if (e.PropertyPath != null)
                {
                    foreach (var tlp in tableListPaths)
                    {
                        if (e.PropertyPath.StartsWith(tlp))
                        {
                            data.Visible = false;
                            break;
                        }
                    }
                }
                result.Add(data);
            }
            return result;
        }
    }
}
