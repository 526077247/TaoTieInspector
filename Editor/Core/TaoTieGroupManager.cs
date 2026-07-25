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

        // Cached group tree — rebuilt only when entry count changes
        private GroupNode cachedRoot;
        private int cachedEntryCount = -1;

        public void DrawGroupedEntries(List<GroupEntryData> entries, Action<GroupEntryData> drawProperty)
        {
            drawnTabGroups.Clear();
            // Rebuild tree only when structure changes (entry count differs)
            if (cachedRoot == null || cachedEntryCount != entries.Count)
            {
                cachedRoot = BuildGroupTree(entries);
                cachedEntryCount = entries.Count;
            }
            // No need to update tree state — visibility is checked at draw time
            // since GroupEntryData objects are the same references updated by the caller
            DrawGroupNode(cachedRoot, null, drawProperty);
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

            // Build a set of ALL container paths (regardless of current visibility)
            // so children are always assigned to their container, even if visibility changes later
            var containerPaths = new HashSet<string>();
            foreach (var entry in entries)
            {
                if (entry.IsFoldoutContainer)
                    containerPaths.Add(entry.ContainerPath);
            }

            // Build tree with ALL entries — visibility is checked at draw time.
            // This allows ShowIf/HideIf to toggle visibility without rebuilding the tree.
            foreach (var entry in entries)
            {
                // [Serializable] nested object container
                if (entry.IsFoldoutContainer)
                {
                    string path = entry.ContainerPath;
                    if (!nodeMap.ContainsKey(path))
                    {
                        var containerNode = new GroupNode
                        {
                            Name = entry.ContainerName,
                            FullPath = path,
                            IsFoldoutContainer = true
                        };
                        nodeMap[path] = containerNode;
                        root.Children.Add(containerNode);
                        containerNode.Parent = root;
                    }
                    nodeMap[path].DirectEntries.Add(entry);
                    continue;
                }

                // Check if this entry is a child of a foldout container by PropertyPath
                string childPropPath = (entry.UserData as TaoTiePropertyEntry)?.PropertyPath
                    ?? (entry.UserData as System.Reflection.MemberInfo)?.Name;
                string containerParent = null;
                if (childPropPath != null)
                {
                    foreach (var cp in containerPaths)
                    {
                        if (childPropPath.StartsWith(cp + "."))
                        {
                            containerParent = cp;
                            break;
                        }
                    }
                }

                if (containerParent != null)
                {
                    // Add to container node as a direct entry
                    nodeMap[containerParent].DirectEntries.Add(entry);
                }
                else
                {
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
                if (!entry.Visible) continue;
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
                // Find the container entry without LINQ allocation
                GroupEntryData containerEntry = null;
                for (int i = 0; i < node.DirectEntries.Count; i++)
                {
                    if (node.DirectEntries[i].IsFoldoutContainer)
                    {
                        containerEntry = node.DirectEntries[i];
                        break;
                    }
                }
                if (containerEntry == null) return;
                if (!containerEntry.Visible) return;
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
                if (!drawnTabGroups.Contains(tabGroupKey))
                {
                    // Collect tab children without LINQ allocation
                    var tabChildren = new List<GroupNode>();
                    if (node.Parent != null)
                    {
                        for (int i = 0; i < node.Parent.Children.Count; i++)
                        {
                            if (node.Parent.Children[i].IsTabGroup)
                                tabChildren.Add(node.Parent.Children[i]);
                        }
                    }
                    if (tabChildren.Count > 0)
                    {
                        drawnTabGroups.Add(tabGroupKey);
                        string[] tabLabels = new string[tabChildren.Count];
                        for (int i = 0; i < tabChildren.Count; i++)
                            tabLabels[i] = tabChildren[i].Name;
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
