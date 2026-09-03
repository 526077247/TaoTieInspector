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
        private bool useEnhancedDrawing;
        private bool initialized;

        // Cached intermediate data — rebuilt only when type changes or managed-ref structure changes
        private List<TaoTiePropertyEntry> cachedMergedEntries;
        private List<GroupEntryData> cachedGroupEntries;
        private string[] cachedManagedRefBasePathArray;
        private string[] cachedTableListPathArray;
        private Type cachedTargetType;

        // Profiling — disabled
        // private System.Diagnostics.Stopwatch _sw = new System.Diagnostics.Stopwatch();
        // private int _profileCount;
        // private float _soUpdateMs, _buildEntriesMs, _refreshMs, _drawMs, _applyMs, _totalMs;

        private void Initialize()
        {
            if (initialized) return;

            processor = new TaoTiePropertyProcessor();
            groupManager = new TaoTieGroupManager();

            if (target != null)
            {
                System.Type targetType = target.GetType();
                bool hasDrawWithUnity = targetType.IsDefined(typeof(DrawWithUnityAttribute), true);
                bool forceEnhanced = typeof(IForceTaoTieDrawing).IsAssignableFrom(targetType);
                // StateMachineBehaviour is always drawn by the SMB panel engine (normal Inspector and
                // Animator window), so enhanced drawing is forced regardless of TaoTie attribute presence.
                bool forceSMB = target is UnityEngine.StateMachineBehaviour;
                useEnhancedDrawing = (forceSMB || !hasDrawWithUnity) &&
                    (forceSMB || forceEnhanced || TaoTiePropertyProcessor.HasAnyTaoTieAttributes(targetType));
            }
            else
            {
                useEnhancedDrawing = false;
            }

            initialized = true;
            cachedMergedEntries = null;
            cachedGroupEntries = null;
            cachedManagedRefBasePathArray = null;
            cachedTableListPathArray = null;
            cachedTargetType = null;
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
                // Invalidate cached structures — managed ref structure changed
                cachedMergedEntries = null;
                cachedGroupEntries = null;
            }

            var entries = processor.BuildEntries(serializedObject);
            processor.RefreshDynamicState(entries, target);

            // Set all entries for DrawArrayBox to find child entries (LabelText etc.)
            TaoTiePropertyLayout.SetAllEntries(entries);

            // Rebuild cached structures only when type changes or cache was invalidated
            if (cachedMergedEntries == null || cachedTargetType != target.GetType())
            {
                cachedMergedEntries = BuildMergedEntries(entries, target);
                cachedManagedRefBasePathArray = CollectManagedRefBasePaths(cachedMergedEntries,
                    target is UnityEngine.StateMachineBehaviour);
                cachedGroupEntries = ConvertToGroupData(cachedMergedEntries,
                    target is UnityEngine.StateMachineBehaviour, cachedManagedRefBasePathArray);
                InsertButtonEntries(cachedGroupEntries);
                cachedTableListPathArray = CollectTableListPaths(cachedMergedEntries);
                cachedTargetType = target.GetType();
            }

            // Per-frame: sync visibility from entries to cached GroupEntryData
            SyncGroupEntryVisibility(cachedGroupEntries, cachedManagedRefBasePathArray, cachedTableListPathArray);

            groupManager.DrawGroupedEntries(cachedGroupEntries, data =>
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
            SMBPropertyLayout.FlushPendingCallbacks();
        }

        /// <summary>
        /// Insert button entries at correct declaration-order positions (uses MetadataToken).
        /// Called only when cache is rebuilt.
        /// </summary>
        private void InsertButtonEntries(List<GroupEntryData> groupEntries)
        {
            var buttonMethods = processor.GetButtonMethods(target.GetType());
            if (buttonMethods == null || buttonMethods.Count == 0) return;

            // Build field MetadataToken map for ordering
            var typeHierarchy = new List<Type>();
            Type currentType = target.GetType();
            while (currentType != null && currentType != typeof(object))
            {
                typeHierarchy.Insert(0, currentType);
                currentType = currentType.BaseType;
            }
            var fieldTokens = new Dictionary<string, int>();
            foreach (var t in typeHierarchy)
            {
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    fieldTokens[f.Name] = f.MetadataToken;
                }
            }

            var insertList = new List<(int index, GroupEntryData data)>();
            foreach (var method in buttonMethods)
            {
                var btnEntry = new GroupEntryData { Visible = true, UserData = method };
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
            insertList.Sort((a, b) => b.index.CompareTo(a.index));
            foreach (var (index, data) in insertList)
                groupEntries.Insert(index, data);
        }

        /// <summary>
        /// Collect static managed-ref base paths (entries with TypeFilter / HideReferenceObjectPicker).
        /// For StateMachineBehaviour targets, also collects ALL top-level managed-ref paths so the
        /// SMB panel engine owns (and hides) the child entries. Called only when cache is rebuilt.
        /// </summary>
        private static string[] CollectManagedRefBasePaths(List<TaoTiePropertyEntry> mergedEntries,
            bool includeTopLevelManagedRefs = false)
        {
            var paths = new HashSet<string>();
            foreach (var e in mergedEntries)
            {
                try
                {
                    if ((e.TypeFilter != null || e.HideReferenceObjectPicker != null)
                        && e.Property != null
                        && e.Property.propertyType == SerializedPropertyType.ManagedReference)
                    {
                        paths.Add(e.PropertyPath + ".");
                    }
                    else if (includeTopLevelManagedRefs
                        && e.Property != null
                        && e.Property.propertyType == SerializedPropertyType.ManagedReference
                        && e.PropertyPath != null
                        && !e.PropertyPath.Contains('.'))
                    {
                        paths.Add(e.PropertyPath + ".");
                    }
                }
                catch { /* property may be disposed after array element deletion */ }
            }
            var result = new string[paths.Count];
            paths.CopyTo(result);
            return result;
        }

        /// <summary>
        /// Collect TableList base paths so children can be marked invisible.
        /// Called only when cache is rebuilt.
        /// </summary>
        private static string[] CollectTableListPaths(List<TaoTiePropertyEntry> mergedEntries)
        {
            var paths = new HashSet<string>();
            foreach (var e in mergedEntries)
            {
                if (e.TableList != null && e.Property != null && e.Property.isArray)
                    paths.Add(e.PropertyPath + ".");
            }
            var result = new string[paths.Count];
            paths.CopyTo(result);
            return result;
        }

        /// <summary>
        /// Per-frame: sync Visible from cached entries to GroupEntryData, applying managed-ref
        /// and TableList path overrides. Dynamic pending clear paths are also applied.
        /// Uses string[] arrays instead of HashSet to avoid enumerator allocation.
        /// </summary>
        private static void SyncGroupEntryVisibility(List<GroupEntryData> groupEntries,
            string[] managedRefBasePaths, string[] tableListPaths)
        {
            // Collect pending managed reference clear paths (dynamic, per-frame)
            var pendingClearPaths = TaoTiePropertyLayout.GetPendingClearPaths();
            string[] pendingClearArray = pendingClearPaths.Count > 0
                ? pendingClearPaths.ToArray()
                : null;

            bool hasManagedRefOverrides = managedRefBasePaths.Length > 0 || pendingClearArray != null;
            bool hasTableListOverrides = tableListPaths.Length > 0;

            for (int i = 0; i < groupEntries.Count; i++)
            {
                var ge = groupEntries[i];
                var e = ge.UserData as TaoTiePropertyEntry;

                // Sync base visibility from entry (RefreshDynamicState already updated it)
                if (e != null)
                    ge.Visible = e.Visible;
                else
                    ge.Visible = true; // button entries are always visible

                if (!ge.Visible) continue;
                if (e?.PropertyPath == null) continue;

                // TableList path override
                if (hasTableListOverrides)
                {
                    for (int j = 0; j < tableListPaths.Length; j++)
                    {
                        if (e.PropertyPath.StartsWith(tableListPaths[j]))
                        {
                            ge.Visible = false;
                            break;
                        }
                    }
                    if (!ge.Visible) continue;
                }

                // Managed-ref path override (static base paths + dynamic pending clears)
                if (hasManagedRefOverrides)
                {
                    for (int j = 0; j < managedRefBasePaths.Length; j++)
                    {
                        if (e.PropertyPath.StartsWith(managedRefBasePaths[j]))
                        {
                            ge.Visible = false;
                            break;
                        }
                    }
                    if (ge.Visible && pendingClearArray != null)
                    {
                        for (int j = 0; j < pendingClearArray.Length; j++)
                        {
                            if (e.PropertyPath.StartsWith(pendingClearArray[j] + "."))
                            {
                                ge.Visible = false;
                                break;
                            }
                        }
                    }
                }
            }
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
                    // Skip private fields without [SerializeField] (e.g. private DrawBase drawBase)
                    if (!field.IsPublic && !field.IsDefined(typeof(SerializeField), true)) continue;
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

        private static List<GroupEntryData> ConvertToGroupData(List<TaoTiePropertyEntry> entries,
            bool isStateMachineBehaviour, string[] managedRefBasePaths)
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
                // For StateMachineBehaviour, the entire managed-ref subtree is owned and drawn by the
                // SMB panel (DrawSmbManagedRefPanel). Exclude its child entries here so they cannot
                // form empty outer group nodes (their [TabGroup] would render a tab with no content
                // on top of the panel's own inner tab groups).
                if (isStateMachineBehaviour && e.PropertyPath != null && managedRefBasePaths != null)
                {
                    bool isManagedRefChild = false;
                    for (int b = 0; b < managedRefBasePaths.Length; b++)
                    {
                        if (e.PropertyPath.StartsWith(managedRefBasePaths[b]))
                        {
                            isManagedRefChild = true;
                            break;
                        }
                    }
                    if (isManagedRefChild) continue;
                }

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
