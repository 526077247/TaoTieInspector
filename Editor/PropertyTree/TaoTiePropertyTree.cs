using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace TaoTie.Inspector.Editor
{
    public class TaoTiePropertyTree
    {
        private readonly object target;
        private readonly List<TaoTieReflectionProperty> properties;
        private readonly TaoTiePropertyProcessor processor;
        private readonly TaoTieGroupManager groupManager;
        private readonly bool useEnhancedDrawing;

        private TaoTiePropertyTree(object target)
        {
            this.target = target;
            properties = new List<TaoTieReflectionProperty>();
            processor = new TaoTiePropertyProcessor();
            groupManager = new TaoTieGroupManager();

            useEnhancedDrawing = TaoTiePropertyProcessor.HasAnyTaoTieAttributes(target.GetType());
            BuildProperties();
        }

        public static TaoTiePropertyTree Create(object target)
        {
            if (target == null) return null;
            return new TaoTiePropertyTree(target);
        }

        private void BuildProperties()
        {
            Type type = target.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            int defaultOrder = 0;
            Type current = type;
            var processedNames = new HashSet<string>();

            while (current != null && current != typeof(object))
            {
                foreach (var field in current.GetFields(flags))
                {
                    // Skip readonly fields that aren't serialized
                    if (field.IsInitOnly) continue;
                    // Skip backing fields
                    if (field.Name.StartsWith("<") && field.Name.EndsWith(">k__BackingField")) continue;
                    if (processedNames.Contains(field.Name)) continue;
                    processedNames.Add(field.Name);

                    // Skip non-public fields without [SerializeField]
                    if (!field.IsPublic && !field.IsDefined(typeof(SerializeField), true))
                        continue;

                    var prop = CreateReflectionProperty(field);
                    if (prop != null)
                    {
                        if (prop.Order == 0 && !field.IsDefined(typeof(PropertyOrderAttribute), true))
                            prop.Order = defaultOrder;
                        properties.Add(prop);
                        defaultOrder++;
                    }
                }
                current = current.BaseType;
            }

            properties.Sort((a, b) => a.Order.CompareTo(b.Order));
        }

        private TaoTieReflectionProperty CreateReflectionProperty(FieldInfo field)
        {
            var prop = new TaoTieReflectionProperty { FieldInfo = field };

            prop.LabelOverride = field.GetCustomAttribute<LabelTextAttribute>()?.Text;
            prop.ShowIf = field.GetCustomAttributes<ShowIfAttribute>().ToArray();
            prop.HideIf = field.GetCustomAttributes<HideIfAttribute>().ToArray();
            prop.EnableIf = field.GetCustomAttributes<EnableIfAttribute>().ToArray();
            prop.DisableIf = field.GetCustomAttributes<DisableIfAttribute>().ToArray();
            prop.ReadOnly = field.GetCustomAttribute<ReadOnlyAttribute>();
            prop.Title = field.GetCustomAttribute<TitleAttribute>();

            var infoBoxAttrs = field.GetCustomAttributes<InfoBoxAttribute>();
            if (infoBoxAttrs != null)
            {
                foreach (var ib in infoBoxAttrs)
                {
                    prop.InfoBoxes ??= new List<InfoBoxAttribute>();
                    prop.InfoBoxes.Add(ib);
                }
            }

            prop.Space = field.GetCustomAttribute<PropertySpaceAttribute>();
            prop.Range = field.GetCustomAttribute<PropertyRangeAttribute>();
            prop.FoldoutGroup = field.GetCustomAttribute<FoldoutGroupAttribute>();
            prop.BoxGroup = field.GetCustomAttribute<BoxGroupAttribute>();
            prop.TabGroup = field.GetCustomAttribute<TabGroupAttribute>();
            prop.HorizontalGroup = field.GetCustomAttribute<HorizontalGroupAttribute>();
            prop.EnumToggleButtons = field.GetCustomAttribute<EnumToggleButtonsAttribute>();
            prop.ValueDropdown = field.GetCustomAttribute<ValueDropdownAttribute>();
            prop.OnValueChanged = field.GetCustomAttribute<OnValueChangedAttribute>();

            // Graph attributes
            prop.DrawIgnore = field.GetCustomAttribute<DrawIgnoreAttribute>();
            prop.DisableInEditorMode = field.GetCustomAttribute<DisableInEditorModeAttribute>();
            prop.MinValue = field.GetCustomAttribute<MinValueAttribute>();
            prop.MaxValue = field.GetCustomAttribute<MaxValueAttribute>();
            prop.NotAssets = field.GetCustomAttribute<NotAssetsAttribute>();
            prop.OnStateUpdate = field.GetCustomAttribute<OnStateUpdateAttribute>();

            var orderAttr = field.GetCustomAttribute<PropertyOrderAttribute>();
            if (orderAttr != null)
                prop.Order = orderAttr.Order;

            prop.HasTaoTieAttributes = prop.LabelOverride != null
                || prop.ShowIf != null || prop.HideIf != null
                || prop.EnableIf != null || prop.DisableIf != null
                || prop.ReadOnly != null || prop.Title != null
                || (prop.InfoBoxes != null && prop.InfoBoxes.Count > 0)
                || prop.Space != null || prop.Range != null
                || prop.FoldoutGroup != null || prop.BoxGroup != null
                || prop.TabGroup != null || prop.HorizontalGroup != null
                || prop.EnumToggleButtons != null || prop.ValueDropdown != null
                || prop.OnValueChanged != null
                || prop.DrawIgnore != null || prop.DisableInEditorMode != null
                || prop.MinValue != null || prop.MaxValue != null
                || prop.NotAssets != null || prop.OnStateUpdate != null;

            return prop;
        }

        public void Draw()
        {
            if (target == null) return;

            if (!useEnhancedDrawing)
            {
                DrawDefault();
                return;
            }

            // Refresh dynamic state
            foreach (var prop in properties)
            {
                prop.Visible = prop.IsVisible(target);
                prop.Enabled = prop.IsEnabled(target);
            }

            // Draw buttons
            ButtonDrawer.DrawButtons(target, processor);

            // Draw grouped properties using the same group manager
            DrawGrouped();
        }

        private void DrawGrouped()
        {
            // Use a simplified version of group manager for reflection properties
            var ungrouped = new List<TaoTieReflectionProperty>();
            var foldoutEntries = new Dictionary<string, List<TaoTieReflectionProperty>>();
            var boxEntries = new Dictionary<string, List<TaoTieReflectionProperty>>();
            var tabEntries = new Dictionary<string, Dictionary<string, List<TaoTieReflectionProperty>>>();
            var horizontalEntries = new Dictionary<string, List<TaoTieReflectionProperty>>();

            foreach (var prop in properties)
            {
                if (!prop.Visible) continue;

                if (prop.TabGroup != null)
                {
                    string gk = prop.TabGroup.GroupName;
                    string tk = prop.TabGroup.TabName;
                    if (!tabEntries.ContainsKey(gk))
                        tabEntries[gk] = new Dictionary<string, List<TaoTieReflectionProperty>>();
                    if (!tabEntries[gk].ContainsKey(tk))
                        tabEntries[gk][tk] = new List<TaoTieReflectionProperty>();
                    tabEntries[gk][tk].Add(prop);
                }
                else if (prop.FoldoutGroup != null)
                {
                    string gk = prop.FoldoutGroup.GroupName;
                    if (!foldoutEntries.ContainsKey(gk))
                        foldoutEntries[gk] = new List<TaoTieReflectionProperty>();
                    foldoutEntries[gk].Add(prop);
                }
                else if (prop.BoxGroup != null)
                {
                    string gk = prop.BoxGroup.GroupName;
                    if (!boxEntries.ContainsKey(gk))
                        boxEntries[gk] = new List<TaoTieReflectionProperty>();
                    boxEntries[gk].Add(prop);
                }
                else if (prop.HorizontalGroup != null)
                {
                    string gk = prop.HorizontalGroup.GroupName;
                    if (!horizontalEntries.ContainsKey(gk))
                        horizontalEntries[gk] = new List<TaoTieReflectionProperty>();
                    horizontalEntries[gk].Add(prop);
                }
                else
                {
                    ungrouped.Add(prop);
                }
            }

            // Draw ungrouped
            foreach (var prop in ungrouped)
                prop.Draw(target);

            // Draw foldout groups
            foreach (var kvp in foldoutEntries)
            {
                string key = "TaoTie_Foldout_" + kvp.Key;
                bool expanded = SessionState.GetBool(key, true);
                expanded = EditorGUILayout.Foldout(expanded, kvp.Key, true);
                SessionState.SetBool(key, expanded);

                if (expanded)
                {
                    EditorGUI.indentLevel++;
                    foreach (var prop in kvp.Value)
                        prop.Draw(target);
                    EditorGUI.indentLevel--;
                }
            }

            // Draw box groups
            foreach (var kvp in boxEntries)
            {
                EditorGUILayout.BeginVertical(GUI.skin.box);
                EditorGUILayout.LabelField(kvp.Key, EditorStyles.boldLabel);
                foreach (var prop in kvp.Value)
                    prop.Draw(target);
                EditorGUILayout.EndVertical();
                GUILayout.Space(2);
            }

            // Draw horizontal groups
            foreach (var kvp in horizontalEntries)
            {
                EditorGUILayout.BeginHorizontal();
                foreach (var prop in kvp.Value)
                    prop.Draw(target);
                EditorGUILayout.EndHorizontal();
            }

            // Draw tab groups
            foreach (var kvp in tabEntries)
            {
                var tabs = new string[kvp.Value.Keys.Count];
                kvp.Value.Keys.CopyTo(tabs, 0);
                Array.Sort(tabs);

                if (tabs.Length == 0) continue;

                string tabKey = "TaoTie_Tab_" + kvp.Key;
                int currentTab = SessionState.GetInt(tabKey, 0);
                currentTab = GUILayout.Toolbar(currentTab, tabs);
                SessionState.SetInt(tabKey, currentTab);

                if (currentTab >= 0 && currentTab < tabs.Length)
                {
                    string activeTab = tabs[currentTab];
                    if (kvp.Value.TryGetValue(activeTab, out var tabProps))
                    {
                        EditorGUI.indentLevel++;
                        foreach (var prop in tabProps)
                            prop.Draw(target);
                        EditorGUI.indentLevel--;
                    }
                }
            }
        }

        private void DrawDefault()
        {
            foreach (var prop in properties)
            {
                prop.Visible = true;
                prop.Enabled = true;
                prop.Draw(target);
            }
        }
    }
}
