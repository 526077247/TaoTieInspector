using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TaoTie.Inspector.Editor
{
    public class TaoTieGroupManager : IDisposable
    {
        private readonly HashSet<string> drawnTabGroups = new();

        public void DrawGroupedEntries(List<GroupEntryData> entries, Action<GroupEntryData> drawProperty)
        {
            drawnTabGroups.Clear();
            var root = BuildGroupTree(entries);
            DrawGroupNode(root, null, drawProperty);
        }

        #region Group Tree

        private class GroupNode
        {
            public string Name;
            public string FullPath;
            public GroupNode Parent;
            public List<GroupNode> Children = new();
            public List<GroupEntryData> DirectEntries = new();
            public bool IsFoldoutContainer;
            public bool IsBox;
            public bool IsFoldoutGroup;
            public bool IsTabGroup;
            public string TabName;
        }

        private GroupNode BuildGroupTree(List<GroupEntryData> entries)
        {
            var root = new GroupNode { Name = "", FullPath = "" };
            var nodeMap = new Dictionary<string, GroupNode> { [""] = root };

            foreach (var entry in entries)
            {
                if (!entry.Visible) continue;

                // [Serializable] nested object container
                if (entry.IsFoldoutContainer)
                {
                    string path = entry.ContainerPath;
                    if (!nodeMap.ContainsKey(path))
                    {
                        nodeMap[path] = new GroupNode
                        {
                            Name = entry.ContainerName,
                            FullPath = path,
                            IsFoldoutContainer = true
                        };
                    }
                    nodeMap[path].DirectEntries.Add(entry);
                    continue;
                }

                string groupPath = entry.GetGroupPath();
                if (string.IsNullOrEmpty(groupPath))
                {
                    root.DirectEntries.Add(entry);
                }
                else
                {
                    EnsureNode(nodeMap, root, groupPath, entry);
                }
            }

            return root;
        }

        private void EnsureNode(Dictionary<string, GroupNode> nodeMap, GroupNode root, string path, GroupEntryData entry)
        {
            if (nodeMap.TryGetValue(path, out var existing))
            {
                existing.DirectEntries.Add(entry);
                SetFlags(existing, entry);
                return;
            }

            string[] parts = path.Split('/');
            string currentPath = "";
            GroupNode parentNode = root;

            for (int i = 0; i < parts.Length; i++)
            {
                currentPath = i == 0 ? parts[0] : currentPath + "/" + parts[i];
                if (!nodeMap.TryGetValue(currentPath, out var node))
                {
                    node = new GroupNode { Name = parts[i], FullPath = currentPath, Parent = parentNode };
                    parentNode.Children.Add(node);
                    nodeMap[currentPath] = node;
                }
                if (i == parts.Length - 1) SetFlags(node, entry);
                parentNode = node;
            }
            parentNode.DirectEntries.Add(entry);
        }

        private void SetFlags(GroupNode node, GroupEntryData entry)
        {
            if (entry.BoxGroupName != null) node.IsBox = true;
            if (entry.FoldoutGroupName != null && entry.FoldoutGroupName == node.FullPath) node.IsFoldoutGroup = true;
            if (entry.TabGroupName != null)
            {
                node.IsTabGroup = true;
                node.TabName = entry.TabName;
            }
        }

        #endregion

        #region Drawing

        private void DrawGroupNode(GroupNode node, string containerPath, Action<GroupEntryData> drawProperty)
        {
            // Draw direct entries (skip container entries — they are foldout headers)
            foreach (var entry in node.DirectEntries)
            {
                if (entry.IsFoldoutContainer) continue;
                drawProperty(entry);
            }

            // Draw child groups
            foreach (var child in node.Children)
            {
                DrawSingleGroup(child, containerPath, drawProperty);
            }
        }

        private void DrawSingleGroup(GroupNode node, string containerPath, Action<GroupEntryData> drawProperty)
        {
            // [Serializable] foldout container
            if (node.IsFoldoutContainer)
            {
                var containerEntry = node.DirectEntries.FirstOrDefault(e => e.IsFoldoutContainer);
                if (containerEntry == null) return;
                containerEntry.ContainerExpanded = EditorGUILayout.Foldout(containerEntry.ContainerExpanded, containerEntry.ContainerName, true);
                SessionState.SetBool("TaoTie_Fold_" + containerEntry.ContainerPath, containerEntry.ContainerExpanded);
                if (!containerEntry.ContainerExpanded) return;
                EditorGUI.indentLevel++;
                DrawGroupNode(node, containerEntry.ContainerPath, drawProperty);
                EditorGUI.indentLevel--;
                return;
            }

            // Tab group
            if (node.IsTabGroup)
            {
                string tabGroupKey = node.Parent != null ? node.Parent.FullPath : node.FullPath;
                var tabChildren = node.Parent?.Children.Where(c => c.IsTabGroup).ToList() ?? new List<GroupNode>();
                if (tabChildren.Count > 0 && !drawnTabGroups.Contains(tabGroupKey))
                {
                    drawnTabGroups.Add(tabGroupKey);
                    string[] tabLabels = tabChildren.Select(c => c.Name).ToArray();
                    int currentTab = SessionState.GetInt("TaoTie_Tab_" + tabGroupKey, 0);
                    currentTab = GUILayout.Toolbar(currentTab, tabLabels);
                    SessionState.SetInt("TaoTie_Tab_" + tabGroupKey, currentTab);
                    if (currentTab >= 0 && currentTab < tabChildren.Count)
                    {
                        EditorGUI.indentLevel++;
                        DrawGroupNode(tabChildren[currentTab], containerPath, drawProperty);
                        EditorGUI.indentLevel--;
                    }
                }
                return;
            }

            // Foldout group
            if (node.IsFoldoutGroup)
            {
                string foldKey = "TaoTie_Fold_" + node.FullPath;
                bool fold = SessionState.GetBool(foldKey, true);
                fold = EditorGUILayout.Foldout(fold, node.Name, true);
                SessionState.SetBool(foldKey, fold);
                if (fold)
                {
                    EditorGUI.indentLevel++;
                    DrawGroupNode(node, containerPath, drawProperty);
                    EditorGUI.indentLevel--;
                }
                return;
            }

            // Box group
            if (node.IsBox)
            {
                EditorGUILayout.BeginVertical(GUI.skin.box);
                EditorGUILayout.LabelField(node.Name, EditorStyles.boldLabel);
                DrawGroupNode(node, containerPath, drawProperty);
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(2);
                return;
            }

            // Default
            DrawGroupNode(node, containerPath, drawProperty);
        }

        #endregion

        public void Dispose()
        {
            drawnTabGroups.Clear();
        }
    }
}
