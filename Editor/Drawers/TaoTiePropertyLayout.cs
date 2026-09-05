using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace TaoTie.Inspector.Editor
{
    public static class TaoTiePropertyLayout
    {
        private static readonly List<(TaoTiePropertyEntry entry, object target)> _pendingValueChangedCallbacks = new();
        private static readonly List<(TaoTiePropertyEntry entry, object target)> _pendingCollectionChangedCallbacks = new();
        private static readonly List<string> _pendingManagedReferenceClears = new();
        private static readonly List<(string path, Type type)> _pendingManagedReferenceSets = new();
        // Per-table column widths — key: property path, value: column widths
        private static readonly Dictionary<string, float[]> _tableColumnWidths = new();
        // Per-table dragging state
        private static string _draggingTablePath;
        private static int _draggingColumnIndex = -1;

        // Performance: max rows to render before requiring "Show All" expansion
        private const int k_MaxVisibleRows = 50;

        /// <summary>
        /// Returns the list of property paths pending managed reference clear.
        /// Used by TaoTieEditor to skip drawing children of cleared references.
        /// </summary>
        public static List<string> GetPendingClearPaths()
        {
            return _pendingManagedReferenceClears;
        }

        /// <summary>
        /// Returns true if there are pending managed reference changes (set or clear).
        /// </summary>
        public static bool HasPendingManagedReferenceChanges()
        {
            return _pendingManagedReferenceClears.Count > 0 || _pendingManagedReferenceSets.Count > 0;
        }

        /// <summary>
        /// Flush all pending operations. Call this after serializedObject.ApplyModifiedProperties().
        /// </summary>
        public static void FlushPendingCallbacks()
        {
            // OnValueChanged callbacks
            foreach (var (entry, target) in _pendingValueChangedCallbacks)
            {
                if (entry.OnValueChanged == null) continue;
                var condTarget = ResolveConditionTarget(target, entry);
                var methodType = entry.DeclaringType ?? condTarget?.GetType() ?? target.GetType();
                ReflectionMethodInvoker.InvokeNoArg(condTarget ?? target, methodType, entry.OnValueChanged.MethodName);
            }
            _pendingValueChangedCallbacks.Clear();

            // OnCollectionChanged callbacks
            foreach (var (entry, target) in _pendingCollectionChangedCallbacks)
            {
                if (entry.OnCollectionChanged == null) continue;
                var condTarget = ResolveConditionTarget(target, entry);
                var methodType = entry.DeclaringType ?? condTarget?.GetType() ?? target.GetType();
                ReflectionMethodInvoker.InvokeNoArg(condTarget ?? target, methodType, entry.OnCollectionChanged.After);
            }
            _pendingCollectionChangedCallbacks.Clear();
        }

        /// <summary>
        /// Apply pending managed reference changes to the serialized object.
        /// Call this after ApplyModifiedProperties but before Update.
        /// </summary>
        public static void ApplyPendingManagedReferences(UnityEditor.SerializedObject so)
        {
            foreach (var path in _pendingManagedReferenceClears)
            {
                var prop = so.FindProperty(path);
                if (prop != null) prop.managedReferenceValue = null;
            }
            _pendingManagedReferenceClears.Clear();

            foreach (var (path, type) in _pendingManagedReferenceSets)
            {
                var prop = so.FindProperty(path);
                if (prop != null) prop.managedReferenceValue = Activator.CreateInstance(type);
            }
            _pendingManagedReferenceSets.Clear();

            // Invalidate type filter cache — managed reference structure changed
            s_TypeFilterCache.Clear();
        }

        public static void DrawProperty(TaoTiePropertyEntry entry, object target)
        {
            // Reflection-drawn field (Dictionary, etc.)
            if (entry.IsReflectionField && entry.ReflectionField != null)
            {
                DrawReflectionProperty(entry, target);
                return;
            }

            if (entry.Property == null) return;

            // Space before
            if (entry.Space != null && entry.Space.SpaceBefore > 0)
                GUILayout.Space(entry.Space.SpaceBefore);

            // Title
            if (entry.Title != null)
            {
                TitleDrawer.Draw(entry.Title);
            }

            // Info boxes
            if (entry.InfoBoxes != null)
            {
                foreach (var infoBox in entry.InfoBoxes)
                {
                    bool show = true;
                    if (!string.IsNullOrEmpty(infoBox.VisibleIf))
                        show = TaoTieConditionResolver.Evaluate(target, infoBox.VisibleIf);
                    if (show)
                        InfoBoxDrawer.Draw(infoBox);
                }
            }

            // Header (Unity built-in)
            if (entry.Header != null)
            {
                EditorGUILayout.LabelField(entry.Header.header);
            }

            // Space (Unity built-in)
            if (entry.UnitySpace != null)
            {
                EditorGUILayout.Space(entry.UnitySpace.height);
            }

            // NotNull check
            if (entry.NotNull != null && entry.Property.propertyType == SerializedPropertyType.ObjectReference
                && entry.Property.objectReferenceValue == null)
            {
                NotNullRenderer.Draw(entry.NotNull.ErrorMessage, true);
            }

            // Save GUI state
            bool wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && entry.Enabled;

            // Draw the property
            bool changed = false;

            // 顶层 [SerializeReference] 字段（StateMachineBehaviour）→ Odin 同款面板：
            // 普通 Inspector 里编辑侧反射 managed ref 子树并按 SMB 分组引擎绘制，无字段级钩子。
            if (DrawSmbManagedRefPanel(entry, target, ref changed))
            {
                // 已由面板接管
            }
            // TypeFilter: if the property is a managed reference with a TypeFilter,
            // provide a type selection dropdown when null, and a foldout + "SetNull" button when not null
            else if (entry.TypeFilter != null && entry.Property.propertyType == SerializedPropertyType.ManagedReference)
            {
                if (entry.Property.managedReferenceValue == null)
                {
                    var types = ResolveTypeFilter(entry.TypeFilter.FilterGetter, target, entry);
                    var names = new string[types.Count];
                    for (int i = 0; i < types.Count; i++)
                    {
                        names[i] = LabelResolver.GetTypeLabel(types[i]);
                    }
                    EditorGUILayout.LabelField(GetLabel(entry));
                    var idx = EditorGUILayout.Popup(-1, names);
                    if (idx >= 0)
                    {
                        _pendingManagedReferenceSets.Add((entry.Property.propertyPath, types[idx]));
                        changed = true;
                    }
                }
                else
                {
                    var refType = entry.Property.managedReferenceValue.GetType();

                    bool pendingClear = _pendingManagedReferenceClears.Contains(entry.Property.propertyPath);
                    if (pendingClear)
                    {
                        EditorGUILayout.LabelField(GetLabel(entry));
                    }
                    else
                    {
                        // Foldout + label + type name + SetNull button on same line
                        string foldKey = "TaoTie_Fold_" + entry.Property.propertyPath;
                        bool fold = SessionState.GetBool(foldKey, true);

                        // Measure label width to position type name right after it
                        var label = GetLabel(entry);
                        float labelW = label != null
                            ? EditorStyles.foldout.CalcSize(label).x + 18f
                            : 100f;

                        Rect foldRect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
                        float buttonW = GuiSizing.SetNullButtonWidth();
                        float buttonX = foldRect.xMax - buttonW - 2f;
                        Rect buttonRect = new Rect(buttonX, foldRect.y, buttonW, foldRect.height);
                        Rect actualFoldRect = new Rect(foldRect.x, foldRect.y, buttonX - foldRect.x - 4f, foldRect.height);

                        // Check if SetNull was clicked before drawing foldout
                        bool setNullClicked = GUI.Button(buttonRect, "SetNull");
                        // Draw foldout only on the area before the button
                        fold = EditorGUI.Foldout(actualFoldRect, fold, label, true);

                        if (setNullClicked)
                        {
                            _pendingManagedReferenceClears.Add(entry.Property.propertyPath);
                            changed = true;
                        }

                        SessionState.SetBool(foldKey, fold);

                        if (fold)
                        {
                            // Draw children manually (skip the parent PropertyField which adds a duplicate foldout)
                            EditorGUI.indentLevel++;
                            var childProp = entry.Property.Copy();
                            int targetDepth = entry.Property.depth + 1;
                            if (childProp.NextVisible(true))
                            {
                                do
                                {
                                    if (childProp.depth != targetDepth) break;
                                    EditorGUILayout.PropertyField(childProp, true);
                                } while (childProp.NextVisible(false));
                            }
                            EditorGUI.indentLevel--;
                        }
                    }
                }
            }
            // [SerializeReference] without TypeFilter but with HideReferenceObjectPicker
            else if (entry.HideReferenceObjectPicker != null && entry.Property.propertyType == SerializedPropertyType.ManagedReference)
            {
                if (entry.Property.managedReferenceValue == null)
                {
                    // null → allow type selection (find all non-abstract subclasses)
                    var fieldPath = entry.PropertyPath;
                    var fieldInfo = ResolveFieldFromPath(target, fieldPath);
                    if (fieldInfo != null)
                    {
                        var fieldType = fieldInfo.FieldType;
                        var types = new List<Type>();
                        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                        {
                            foreach (var t in asm.GetTypes())
                            {
                                if (t.IsClass && !t.IsAbstract && fieldType.IsAssignableFrom(t))
                                    types.Add(t);
                            }
                        }
                        var names = new string[types.Count];
                        for (int i = 0; i < types.Count; i++)
                        {
                            names[i] = LabelResolver.GetTypeLabel(types[i]);
                        }
                        EditorGUILayout.LabelField(GetLabel(entry));
                        var idx = EditorGUILayout.Popup(-1, names);
                        if (idx >= 0)
                        {
                            _pendingManagedReferenceSets.Add((entry.Property.propertyPath, types[idx]));
                            changed = true;
                        }
                    }
                }
                else
                {
                    // not null → no SetNull button (HideReferenceObjectPicker hides picker and clear)
                    // Foldout + label + type name on same line
                    var refType = entry.Property.managedReferenceValue.GetType();
                    string typeName = LabelResolver.GetTypeLabel(refType);

                    bool pendingClear = _pendingManagedReferenceClears.Contains(entry.Property.propertyPath);
                    if (pendingClear)
                    {
                        EditorGUILayout.LabelField(GetLabel(entry));
                    }
                    else
                    {
                        string foldKey = "TaoTie_Fold_" + entry.Property.propertyPath;
                        bool fold = SessionState.GetBool(foldKey, true);

                        var label = GetLabel(entry);
                        float labelW = label != null
                            ? EditorStyles.foldout.CalcSize(label).x + 18f
                            : 100f;

                        Rect foldRect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
                        fold = EditorGUI.Foldout(foldRect, fold, label, true);

                        Rect typeRect = new Rect(foldRect.x + labelW, foldRect.y,
                            EditorGUIUtility.currentViewWidth - labelW - 20f, foldRect.height);
                        EditorGUI.LabelField(typeRect, typeName, EditorStyles.boldLabel);

                        SessionState.SetBool(foldKey, fold);

                        if (fold)
                        {
                            // Draw children manually (skip parent PropertyField)
                            EditorGUI.indentLevel++;
                            var childProp = entry.Property.Copy();
                            int targetDepth = entry.Property.depth + 1;
                            if (childProp.NextVisible(true))
                            {
                                do
                                {
                                    if (childProp.depth != targetDepth) break;
                                    EditorGUILayout.PropertyField(childProp, true);
                                } while (childProp.NextVisible(false));
                            }
                            EditorGUI.indentLevel--;
                        }
                    }
                }
            }
            // TableList: draw as table rows instead of default foldout list
            else if (entry.TableList != null)
            {
                if (entry.Property.isArray)
                {
                    changed = DrawTableList(entry);
                }
                else
                {
                    // TableList on non-array property — fallback to default
                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.PropertyField(entry.Property, GetLabel(entry), true);
                    if (EditorGUI.EndChangeCheck())
                        changed = true;
                }
            }
            else if (entry.EnumToggleButtons != null && entry.Property.propertyType == SerializedPropertyType.Enum)
            {
                EditorGUI.BeginChangeCheck();
                EnumToggleButtonsDrawer.Draw(entry.Property, GetLabel(entry));
                if (EditorGUI.EndChangeCheck())
                    changed = true;
            }
            else if (entry.ValueDropdown != null)
            {
                if (entry.Property.isArray)
                {
                    int arraySizeBefore = entry.Property.arraySize;
                    bool dropdownChanged = ValueDropdownDrawer.DrawArray(
                        entry.Property, entry.ValueDropdown, target, GetLabel(entry));
                    if (dropdownChanged)
                    {
                        changed = true;
                        if (entry.OnCollectionChanged != null)
                        {
                            int arraySizeAfter = entry.Property.arraySize;
                            if (arraySizeAfter != arraySizeBefore)
                                _pendingCollectionChangedCallbacks.Add((entry, target));
                        }
                    }
                }
                else
                {
                    EditorGUI.BeginChangeCheck();
                    ValueDropdownDrawer.Draw(entry.Property, entry.ValueDropdown, target, GetLabel(entry));
                    if (EditorGUI.EndChangeCheck())
                        changed = true;
                }
            }
            else if (entry.Range != null && entry.Property.propertyType == SerializedPropertyType.Float)
            {
                EditorGUI.BeginChangeCheck();
                float min = entry.Range.MinMember != null
                    ? TaoTieConditionResolver.GetMember(target, entry.Range.MinMember) is System.Reflection.FieldInfo fi
                        ? (float)fi.GetValue(target)
                        : (float)entry.Range.Min
                    : (float)entry.Range.Min;
                float max = entry.Range.MaxMember != null
                    ? TaoTieConditionResolver.GetMember(target, entry.Range.MaxMember) is System.Reflection.FieldInfo fi2
                        ? (float)fi2.GetValue(target)
                        : (float)entry.Range.Max
                    : (float)entry.Range.Max;
                EditorGUILayout.Slider(entry.Property, min, max, GetLabel(entry));
                if (EditorGUI.EndChangeCheck())
                    changed = true;
            }
            else if (entry.Range != null && entry.Property.propertyType == SerializedPropertyType.Integer)
            {
                EditorGUI.BeginChangeCheck();
                int min = entry.Range.MinMember != null
                    ? TaoTieConditionResolver.GetMember(target, entry.Range.MinMember) is System.Reflection.FieldInfo fi
                        ? (int)fi.GetValue(target)
                        : (int)entry.Range.Min
                    : (int)entry.Range.Min;
                int max = entry.Range.MaxMember != null
                    ? TaoTieConditionResolver.GetMember(target, entry.Range.MaxMember) is System.Reflection.FieldInfo fi2
                        ? (int)fi2.GetValue(target)
                        : (int)entry.Range.Max
                    : (int)entry.Range.Max;
                EditorGUILayout.IntSlider(entry.Property, min, max, GetLabel(entry));
                if (EditorGUI.EndChangeCheck())
                    changed = true;
            }
            else if (entry.UnityRange != null && entry.Property.propertyType == SerializedPropertyType.Float)
            {
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.Slider(entry.Property, entry.UnityRange.min, entry.UnityRange.max, GetLabel(entry));
                if (EditorGUI.EndChangeCheck())
                    changed = true;
            }
            else if (entry.UnityRange != null && entry.Property.propertyType == SerializedPropertyType.Integer)
            {
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.IntSlider(entry.Property, (int)entry.UnityRange.min, (int)entry.UnityRange.max, GetLabel(entry));
                if (EditorGUI.EndChangeCheck())
                    changed = true;
            }
            else
            {
                if (entry.Property.isArray && entry.Property.propertyType != SerializedPropertyType.String)
                {
                    changed = DrawArrayBox(entry, target);
                }
                // HideReferenceObjectPicker only applies to ManagedReference, not ObjectReference
                else if (entry.HideReferenceObjectPicker != null && entry.Property.propertyType == SerializedPropertyType.ManagedReference)
                {
                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.PropertyField(entry.Property, GetLabel(entry), false);
                    if (EditorGUI.EndChangeCheck())
                        changed = true;
                }
                else
                {
                    // Track array size before to detect real collection changes (not foldout toggles)
                    int arraySizeBefore = entry.Property.isArray ? entry.Property.arraySize : -1;
                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.PropertyField(entry.Property, GetLabel(entry), true);
                    if (EditorGUI.EndChangeCheck())
                    {
                        changed = true;
                        if (entry.OnCollectionChanged != null && entry.Property.isArray)
                        {
                            int arraySizeAfter = entry.Property.arraySize;
                            if (arraySizeAfter != arraySizeBefore)
                                _pendingCollectionChangedCallbacks.Add((entry, target));
                        }
                    }
                }
            }

            // Clamp MinValue / MaxValue
            if (changed)
            {
                if (entry.MinValue != null || entry.MaxValue != null)
                    ClampMinMax(entry);
            }

            // Restore GUI state
            GUI.enabled = wasEnabled;

            // OnValueChanged callback — defer to end of frame to avoid invalidating SerializedProperty references
            if (changed && entry.OnValueChanged != null)
            {
                _pendingValueChangedCallbacks.Add((entry, target));
            }

            // OnCollectionChanged callback — handled per-field above (only for real array size changes)
            // No generic trigger here to avoid foldout toggle triggering it

            // OnStateUpdate callback
            if (entry.OnStateUpdate != null)
            {
                var condTarget2 = ResolveConditionTarget(target, entry);
                var methodType2 = entry.DeclaringType ?? condTarget2?.GetType() ?? target.GetType();
                ReflectionMethodInvoker.InvokeNoArg(condTarget2 ?? target, methodType2, entry.OnStateUpdate.Action);
            }

            // Space after
            if (entry.Space != null && entry.Space.SpaceAfter > 0)
                GUILayout.Space(entry.Space.SpaceAfter);
        }

        private static readonly Dictionary<string, GUIContent> s_LabelCache = new();
        private static GUIStyle s_BoxStyle;

        private static GUIStyle GetBoxStyle()
        {
            if (s_BoxStyle == null)
                s_BoxStyle = new GUIStyle(GUI.skin.box) { padding = new RectOffset(2, 2, 2, 2) };
            return s_BoxStyle;
        }

        private static GUIContent GetLabel(TaoTiePropertyEntry entry)
        {
            string text = entry.LabelOverride;
            string tooltip = entry.TooltipText;

            if (string.IsNullOrEmpty(text) && string.IsNullOrEmpty(tooltip))
                return null;

            // Prefix * when tooltip is present
            if (!string.IsNullOrEmpty(tooltip) && !string.IsNullOrEmpty(text))
                text = "*" + text;
            else if (!string.IsNullOrEmpty(tooltip))
                text = "*" + ObjectNames.NicifyVariableName(entry.PropertyName);

            if (string.IsNullOrEmpty(text)) return null;

            string cacheKey = text + "|" + (tooltip ?? "");
            if (!s_LabelCache.TryGetValue(cacheKey, out var content))
            {
                content = new GUIContent(text, tooltip);
                s_LabelCache[cacheKey] = content;
            }
            return content;
        }

        // Shared DrawBase instance for reflection field drawing — avoids per-call allocation
        private static readonly DrawBase s_SharedDrawBase = new DrawBase();
        // Cached MethodInfo for DrawBase.DrawFieldInspector — avoids per-frame reflection
        private static readonly MethodInfo s_DrawFieldInspectorMethod = typeof(DrawBase).GetMethod(
            "DrawFieldInspector", BindingFlags.Instance | BindingFlags.NonPublic);
        // Reusable args array for Invoke — avoids per-call allocation
        private static readonly object[] s_DrawFieldArgs = new object[3];

        /// <summary>
        /// Draw a reflection-based field (Dictionary, unserialized Array/List) using DrawBase.
        /// </summary>
        private static void DrawReflectionProperty(TaoTiePropertyEntry entry, object target)
        {
            var field = entry.ReflectionField;
            var obj = target;

            // Space before
            if (entry.Space != null && entry.Space.SpaceBefore > 0)
                GUILayout.Space(entry.Space.SpaceBefore);

            // Title
            if (entry.Title != null)
                TitleDrawer.Draw(entry.Title);

            // Info boxes
            if (entry.InfoBoxes != null)
            {
                foreach (var ib in entry.InfoBoxes)
                    InfoBoxDrawer.Draw(ib);
            }

            // Header / Space (Unity built-in)
            if (entry.Header != null)
                EditorGUILayout.LabelField(entry.Header.header);
            if (entry.UnitySpace != null)
                EditorGUILayout.Space(entry.UnitySpace.height);

            // Disabled state
            bool wasEnabled = GUI.enabled;
            bool enabled = true;
            if (entry.ReadOnly != null) enabled = false;
            if (entry.DisableInEditorMode != null && !EditorApplication.isPlaying) enabled = false;
            GUI.enabled = wasEnabled && enabled;

            DrawBase.SetFoldoutXOffset(14f);
            if (s_DrawFieldInspectorMethod != null)
            {
                s_DrawFieldArgs[0] = field;
                s_DrawFieldArgs[1] = obj;
                s_DrawFieldArgs[2] = true;
                s_DrawFieldInspectorMethod.Invoke(s_SharedDrawBase, s_DrawFieldArgs);
            }

            GUI.enabled = wasEnabled;

            // Space after
            if (entry.Space != null && entry.Space.SpaceAfter > 0)
                GUILayout.Space(entry.Space.SpaceAfter);
        }

        private static void ClampMinMax(TaoTiePropertyEntry entry)
        {
            switch (entry.Property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    if (entry.MinValue != null)
                        entry.Property.intValue = Mathf.Max(entry.Property.intValue, (int)System.Math.Ceiling(entry.MinValue.MinValue));
                    if (entry.MaxValue != null)
                        entry.Property.intValue = Mathf.Min(entry.Property.intValue, (int)System.Math.Floor(entry.MaxValue.MaxValue));
                    if (entry.UnityMin != null)
                        entry.Property.intValue = Mathf.Max(entry.Property.intValue, Mathf.CeilToInt(entry.UnityMin.min));
                    break;
                case SerializedPropertyType.Float:
                    if (entry.MinValue != null)
                        entry.Property.floatValue = Mathf.Max(entry.Property.floatValue, (float)entry.MinValue.MinValue);
                    if (entry.MaxValue != null)
                        entry.Property.floatValue = Mathf.Min(entry.Property.floatValue, (float)entry.MaxValue.MaxValue);
                    if (entry.UnityMin != null)
                        entry.Property.floatValue = Mathf.Max(entry.Property.floatValue, entry.UnityMin.min);
                    break;
            }
        }

        /// <summary>
        /// Draw array property as a table with grid lines and aligned columns.
        /// </summary>
        private static bool DrawTableList(TaoTiePropertyEntry entry)
        {
            var prop = entry.Property;
            bool changed = false;
            var label = GetLabel(entry);
            string title = label?.text ?? prop.displayName;

            // Collect column definitions from first element
            var columnNames = new List<string>();
            if (prop.arraySize > 0)
            {
                var firstElement = prop.GetArrayElementAtIndex(0);
                if (firstElement.hasVisibleChildren)
                {
                    int targetDepth = firstElement.depth + 1;
                    var colIter = firstElement.Copy();
                    if (colIter.NextVisible(true))
                    {
                        do
                        {
                            if (colIter.depth != targetDepth) break;
                            columnNames.Add(colIter.name);
                        } while (colIter.NextVisible(false));
                    }
                }
            }

            int colCount = columnNames.Count;
            float indexColW = 28f;
            float deleteColW = 22f;
            float dragHandleW = 6f;

            string tableKey = "TL3_" + entry.PropertyPath + "_" + colCount;

            // Get or init column widths — initialization deferred to header drawing where actual width is known
            float[] colWidths = null;
            if (_tableColumnWidths.TryGetValue(tableKey, out var cached) && cached.Length == colCount)
                colWidths = cached;

            // Background box
            var boxStyle = GetBoxStyle();
            EditorGUILayout.BeginVertical(boxStyle);

            // Foldout title bar
            string foldKey = "TaoTie_Fold_Table_" + entry.PropertyPath;
            bool foldout = SessionState.GetBool(foldKey, false);
            // Reset indent for title bar so Foldout doesn't add extra offset
            int oldIndent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            Rect titleBarRect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
            EditorGUI.DrawRect(titleBarRect, new Color(0.3f, 0.3f, 0.3f, 0.2f));
            float tbX = titleBarRect.x + 14f;
            // Anchor buttons to right edge (xMax is indent-independent)
            float minusX = titleBarRect.xMax - 24f - 2f;
            float plusX = minusX - 24f - 2f;
            // Count label before buttons
            string countText = $"({prop.arraySize})";
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
                prop.arraySize++;
                changed = true;
            }
            if (GUI.Button(new Rect(minusX, titleBarRect.y, 24f, titleBarRect.height), "-", EditorStyles.toolbarButton))
            {
                if (prop.arraySize > 0) { prop.arraySize--; changed = true; }
            }
            EditorGUI.indentLevel = oldIndent;

            if (foldout)
            {
            // Column headers with drag handles
            if (colCount > 0)
            {
                var headerRect = EditorGUILayout.GetControlRect(false, 20f);
                EditorGUI.DrawRect(headerRect, new Color(0.3f, 0.3f, 0.3f, 0.4f));

                // Initialize column widths from actual headerRect width (equal distribution)
                // Only cache when headerRect width is valid (skip first layout pass with width=1)
                if (colWidths == null)
                {
                    float contentW = headerRect.width - indexColW - deleteColW;
                    float eachW = Mathf.Max(50f, contentW / colCount);
                    colWidths = new float[colCount];
                    for (int i = 0; i < colCount; i++) colWidths[i] = eachW;
                    if (headerRect.width > 50f)
                        _tableColumnWidths[tableKey] = colWidths;
                }

                int dragCtrlId = GUIUtility.GetControlID(tableKey.GetHashCode(), FocusType.Passive);
                var ev = Event.current;

                float x = headerRect.x;
                EditorGUI.LabelField(new Rect(x, headerRect.y, indexColW, headerRect.height), "#", EditorStyles.boldLabel);
                x += indexColW;
                for (int c = 0; c < colCount; c++)
                {
                    float cw = colWidths[c];
                    if (c == colCount - 1)
                    {
                        float rightEdge = headerRect.x + headerRect.width - deleteColW;
                        cw = Mathf.Max(30f, rightEdge - x);
                    }
                    EditorGUI.LabelField(new Rect(x, headerRect.y, cw - dragHandleW, headerRect.height),
                        ObjectNames.NicifyVariableName(columnNames[c]), EditorStyles.boldLabel);
                    // Drag handle — skip for last column
                    if (c < colCount - 1)
                    {
                        Rect handleRect = new Rect(x + cw - dragHandleW, headerRect.y, dragHandleW, headerRect.height);
                        EditorGUI.DrawRect(handleRect, new Color(0.5f, 0.5f, 0.5f, 0.3f));
                        EditorGUIUtility.AddCursorRect(handleRect, MouseCursor.ResizeHorizontal);
                        if (ev.GetTypeForControl(dragCtrlId) == EventType.MouseDown && handleRect.Contains(ev.mousePosition))
                        {
                            GUIUtility.hotControl = dragCtrlId;
                            _draggingTablePath = tableKey;
                            _draggingColumnIndex = c;
                            ev.Use();
                        }
                    }
                    x += cw;
                }

                // Process drag
                if (GUIUtility.hotControl == dragCtrlId && _draggingTablePath == tableKey)
                {
                    int dragIdx = _draggingColumnIndex;
                    if (ev.GetTypeForControl(dragCtrlId) == EventType.MouseDrag && dragIdx >= 0 && dragIdx < colCount)
                    {
                        float delta = ev.delta.x;
                        float curW = colWidths[dragIdx];
                        float newWidth = curW + delta;
                        if (newWidth < 30f) newWidth = 30f;
                        float actualDelta = newWidth - curW;
                        if (actualDelta != 0f)
                        {
                            colWidths[dragIdx] = newWidth;
                            int nextIdx = dragIdx + 1;
                            if (nextIdx < colCount)
                            {
                                float nextNew = colWidths[nextIdx] - actualDelta;
                                if (nextNew < 30f)
                                {
                                    actualDelta -= 30f - nextNew;
                                    nextNew = 30f;
                                    colWidths[dragIdx] = Mathf.Max(30f, curW + actualDelta);
                                }
                                colWidths[nextIdx] = nextNew;
                            }
                        }
                        ev.Use();
                    }
                    if (ev.GetTypeForControl(dragCtrlId) == EventType.MouseUp)
                    {
                        GUIUtility.hotControl = 0;
                        _draggingTablePath = null;
                        _draggingColumnIndex = -1;
                        ev.Use();
                    }
                }

                // Header bottom border
                EditorGUI.DrawRect(new Rect(headerRect.x, headerRect.yMax - 1, headerRect.width, 1),
                    new Color(0.5f, 0.5f, 0.5f, 0.6f));
                GUILayout.Space(headerRect.height);
            }

            // Data rows
            string tlShowAllKey = "TaoTie_ShowAll_InsTL_" + entry.PropertyPath;
            bool tlShowAll = SessionState.GetBool(tlShowAllKey, false);
            int tlVisibleCount = tlShowAll ? prop.arraySize : Mathf.Min(prop.arraySize, k_MaxVisibleRows);

            for (int i = 0; i < tlVisibleCount; i++)
            {
                var element = prop.GetArrayElementAtIndex(i);
                var rowRect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight + 2f);
                if (i % 2 == 1)
                    EditorGUI.DrawRect(rowRect, new Color(0.5f, 0.5f, 0.5f, 0.1f));

                float x = rowRect.x;
                // Index
                EditorGUI.LabelField(new Rect(x, rowRect.y, indexColW, rowRect.height), i.ToString());
                x += indexColW;

                if (colCount > 0)
                {
                    var elIter = element.Copy();
                    int targetDepth = element.depth + 1;
                    int colIdx = 0;
                    if (elIter.NextVisible(true))
                    {
                        do
                        {
                            if (elIter.depth != targetDepth) break;
                            if (colIdx < colCount)
                            {
                                float cw = colWidths[colIdx];
                                // Last column: fill remaining space (display only)
                                if (colIdx == colCount - 1)
                                {
                                    float rightEdge = rowRect.x + rowRect.width - deleteColW;
                                    cw = Mathf.Max(30f, rightEdge - x);
                                }
                                Rect fieldRect = new Rect(x, rowRect.y, cw - dragHandleW, rowRect.height);
                                EditorGUI.BeginChangeCheck();
                                EditorGUI.PropertyField(fieldRect, elIter, GUIContent.none, false);
                                if (EditorGUI.EndChangeCheck())
                                    changed = true;
                                x += cw;
                                colIdx++;
                            }
                        } while (elIter.NextVisible(false));
                    }
                }
                else
                {
                    EditorGUI.BeginChangeCheck();
                    EditorGUI.PropertyField(new Rect(x, rowRect.y, 50f, rowRect.height),
                        element, GUIContent.none, false);
                    if (EditorGUI.EndChangeCheck())
                        changed = true;
                }

                // Delete button
                Rect delRect = new Rect(x, rowRect.y, deleteColW, rowRect.height);
                if (GUI.Button(delRect, "×"))
                {
                    prop.DeleteArrayElementAtIndex(i);
                    changed = true;
                    break;
                }

                // Row bottom grid line
                EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.yMax - 1, rowRect.width, 1),
                    new Color(0.3f, 0.3f, 0.3f, 0.3f));
            }

                // Show All / Show Less toggle
                if (prop.arraySize > k_MaxVisibleRows)
                {
                    if (GUILayout.Button(tlShowAll ? $"Show Less ({k_MaxVisibleRows})" : $"Show All ({prop.arraySize})", EditorStyles.miniButton))
                    {
                        SessionState.SetBool(tlShowAllKey, !tlShowAll);
                    }
                }
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
            return changed;
        }

        /// <summary>
        /// Draw a plain array/list in box+grid style, matching TableList layout.
        /// Each element is drawn as a single-line PropertyField with index label and delete button.
        /// </summary>
        private static List<TaoTiePropertyEntry> s_allEntries;
        private static Dictionary<string, TaoTiePropertyEntry> s_entriesByPath;
        private static List<TaoTiePropertyEntry> s_lastEntriesForDict;

        public static void SetAllEntries(List<TaoTiePropertyEntry> entries)
        {
            s_allEntries = entries;
            // Only rebuild the dictionary when the entry list reference changes
            if (s_lastEntriesForDict != entries)
            {
                s_entriesByPath = new Dictionary<string, TaoTiePropertyEntry>(entries.Count);
                foreach (var e in entries)
                {
                    if (e.PropertyPath != null)
                        s_entriesByPath[e.PropertyPath] = e;
                }
                s_lastEntriesForDict = entries;
            }
        }

        /// <summary>
        /// Find a TaoTiePropertyEntry for a child SerializedProperty by matching propertyPath.
        /// </summary>
        /// <summary>
        /// Draw a nested array inside a [Serializable] element — simple foldout + element children.
        /// Each [Serializable] element gets its own foldout with children drawn via DrawProperty.
        /// </summary>
        private static bool DrawNestedArray(TaoTiePropertyEntry entry, object target)
        {
            int savedIndent = EditorGUI.indentLevel;
            try
            {
                return DrawNestedArrayInternal(entry, target);
            }
            finally
            {
                EditorGUI.indentLevel = savedIndent;
            }
        }

        private static bool DrawNestedArrayInternal(TaoTiePropertyEntry entry, object target)
        {
            var prop = entry.Property;
            bool changed = false;
            var label = GetLabel(entry);
            string title = label?.text ?? prop.displayName;

            // Use prop.isExpanded for GUILayout consistency
            bool foldout = prop.isExpanded;
            // Draw foldout on its own line (no FlexibleSpace interference)
            Rect foldoutRect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
            // Draw + / - buttons at right edge first (they take priority over foldout click)
            float btnW = 24f;
            Rect minusRect = new Rect(foldoutRect.xMax - btnW, foldoutRect.y, btnW, foldoutRect.height);
            Rect plusRect = new Rect(minusRect.x - btnW - 2f, foldoutRect.y, btnW, foldoutRect.height);
            // Foldout rect excludes the button area so clicks on buttons don't toggle foldout
            Rect actualFoldoutRect = new Rect(foldoutRect.x, foldoutRect.y, plusRect.x - foldoutRect.x - 4f, foldoutRect.height);
            bool newFoldout = EditorGUI.Foldout(actualFoldoutRect, foldout, $"{title} ({prop.arraySize})", true);
            prop.isExpanded = newFoldout;
            if (GUI.Button(plusRect, "+"))
            {
                prop.arraySize++;
                changed = true;
            }
            if (GUI.Button(minusRect, "-"))
            {
                if (prop.arraySize > 0) { prop.arraySize--; changed = true; }
            }

            if (foldout)
            {
                EditorGUI.indentLevel++;
                for (int i = 0; i < prop.arraySize; i++)
                {
                    var element = prop.GetArrayElementAtIndex(i);
                bool elementIsSimple = !element.hasVisibleChildren || element.isArray;

                if (elementIsSimple)
                {
                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.PropertyField(element, GUIContent.none, true);
                    if (EditorGUI.EndChangeCheck())
                        changed = true;
                }
                else
                {
                    // [Serializable] class element — foldout + children
                    bool nestElemExpanded = element.isExpanded;
                    EditorGUILayout.BeginHorizontal();
                    bool nestNewExpanded = EditorGUILayout.Foldout(nestElemExpanded, element.displayName, true);
                    if (GUILayout.Button("×", GUILayout.Width(22)))
                    {
                        prop.DeleteArrayElementAtIndex(i);
                        changed = true;
                        break;
                    }
                    EditorGUILayout.EndHorizontal();

                    element.isExpanded = nestNewExpanded;

                    if (element.isExpanded)
                    {
                        EditorGUI.indentLevel++;
                        var childProp = element.Copy();
                        int targetDepth = element.depth + 1;
                        if (childProp.NextVisible(true))
                        {
                            do
                            {
                                if (childProp.depth != targetDepth) break;
                                var childEntry = FindChildEntry(entry, childProp);
                                if (childEntry != null)
                                {
                                    if (childEntry.Property != null && childEntry.Property.isArray
                                        && childEntry.Property.propertyType != SerializedPropertyType.String)
                                    {
                                        changed |= DrawNestedArray(childEntry, target);
                                    }
                                    else
                                    {
                                        DrawProperty(childEntry, target);
                                    }
                                }
                                else
                                    EditorGUILayout.PropertyField(childProp, true);
                            } while (childProp.NextVisible(false));
                        }
                        EditorGUI.indentLevel--;
                        EditorGUILayout.Space(2);
                    }
                }
            }
            }
            EditorGUI.indentLevel--;
            return changed;
        }

        private static TaoTiePropertyEntry FindChildEntry(TaoTiePropertyEntry parentEntry, SerializedProperty childProp)
        {
            if (s_entriesByPath == null) return null;
            s_entriesByPath.TryGetValue(childProp.propertyPath, out var entry);
            return entry;
        }

        private static bool DrawArrayBox(TaoTiePropertyEntry entry, object target = null)
        {
            int savedIndent = EditorGUI.indentLevel;
            try
            {
                return DrawArrayBoxInternal(entry, target);
            }
            finally
            {
                EditorGUI.indentLevel = savedIndent;
            }
        }

        private static bool DrawArrayBoxInternal(TaoTiePropertyEntry entry, object target)
        {
            var prop = entry.Property;
            bool changed = false;
            int arraySizeBefore = prop.arraySize;
            var label = GetLabel(entry);
            string title = label?.text ?? prop.displayName;
            float indexColW = 28f;
            float deleteColW = 22f;
            float availableWidth = EditorGUIUtility.currentViewWidth - 40f;
            float fieldColW = Mathf.Max(50f, availableWidth - indexColW - deleteColW);

            var boxStyle = GetBoxStyle();
            EditorGUILayout.BeginVertical(boxStyle);

            // Foldout title bar with + / - controls
            string foldKey = "TaoTie_Fold_Array_" + entry.PropertyPath;
            bool foldout = SessionState.GetBool(foldKey, false);
            // Reset indent for title bar so Foldout doesn't add extra offset
            int oldIndent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            Rect titleBarRect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
            EditorGUI.DrawRect(titleBarRect, new Color(0.3f, 0.3f, 0.3f, 0.2f));
            float tbX = titleBarRect.x + 14f;
            // Anchor buttons to right edge (xMax is indent-independent)
            float minusX = titleBarRect.xMax - 24f - 2f;
            float plusX = minusX - 24f - 2f;
            // Count label before buttons
            string countText = $"({prop.arraySize})";
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
                prop.arraySize++;
                changed = true;
            }
            if (GUI.Button(new Rect(minusX, titleBarRect.y, 24f, titleBarRect.height), "-", EditorStyles.toolbarButton))
            {
                if (prop.arraySize > 0) { prop.arraySize--; changed = true; }
            }
            EditorGUI.indentLevel = oldIndent;

            if (foldout)
            {
                string abShowAllKey = "TaoTie_ShowAll_AB_" + entry.PropertyPath;
                bool abShowAll = SessionState.GetBool(abShowAllKey, false);
                int abVisibleCount = abShowAll ? prop.arraySize : Mathf.Min(prop.arraySize, k_MaxVisibleRows);

                for (int i = 0; i < abVisibleCount; i++)
                {
                    var element = prop.GetArrayElementAtIndex(i);
                    bool elementIsSimple = !element.hasVisibleChildren || element.isArray;

                    if (elementIsSimple)
                    {
                        // Simple value type — draw as single-line row in grid
                        var rowRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight + 2f);
                        if (i % 2 == 1)
                            EditorGUI.DrawRect(rowRect, new Color(0.5f, 0.5f, 0.5f, 0.1f));

                        float x = rowRect.x;
                        EditorGUI.LabelField(new Rect(x, rowRect.y, indexColW, rowRect.height), i.ToString());
                        x += indexColW;

                        Rect fieldRect = new Rect(x, rowRect.y, fieldColW, rowRect.height);
                        EditorGUI.BeginChangeCheck();
                        EditorGUI.PropertyField(fieldRect, element, GUIContent.none, true);
                        if (EditorGUI.EndChangeCheck())
                            changed = true;
                        x += fieldColW;

                        Rect delRect = new Rect(x, rowRect.y, deleteColW, rowRect.height);
                        if (GUI.Button(delRect, "×"))
                        {
                            prop.DeleteArrayElementAtIndex(i);
                            changed = true;
                            break;
                        }

                        EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.yMax - 1, rowRect.width, 1),
                            new Color(0.3f, 0.3f, 0.3f, 0.3f));
                    }
                    else
                    {
                        // [Serializable] class element — foldout + children drawn vertically
                        // Header row: index + foldout + delete button
                        bool elemExpanded = element.isExpanded;
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(i.ToString(), GUILayout.Width(indexColW));
                        bool newElemExpanded = EditorGUILayout.Foldout(elemExpanded, element.displayName, true);
                        bool elemDeleted = false;
                        if (GUILayout.Button("×", GUILayout.Width(deleteColW)))
                        {
                            prop.DeleteArrayElementAtIndex(i);
                            changed = true;
                            elemDeleted = true;
                        }
                        EditorGUILayout.EndHorizontal();

                        if (!elemDeleted)
                            element.isExpanded = newElemExpanded;

                        if (!elemDeleted && element.isExpanded)
                        {
                            EditorGUI.indentLevel += 3;
                            var childProp = element.Copy();
                            int targetDepth = element.depth + 1;
                            if (childProp.NextVisible(true))
                            {
                                do
                                {
                                    if (childProp.depth != targetDepth) break;
                                    var childEntry = FindChildEntry(entry, childProp);
                                    if (childEntry != null)
                                    {
                                        // For nested arrays inside [Serializable] elements,
                                        // recursively draw with foldout + element children
                                        if (childEntry.Property != null && childEntry.Property.isArray
                                            && childEntry.Property.propertyType != SerializedPropertyType.String)
                                        {
                                            changed |= DrawNestedArray(childEntry, target);
                                        }
                                        else
                                        {
                                            DrawProperty(childEntry, target);
                                        }
                                    }
                                    else
                                        EditorGUILayout.PropertyField(childProp, true);
                                } while (childProp.NextVisible(false));
                            }
                            EditorGUI.indentLevel -= 3;
                            EditorGUILayout.Space(4);
                        }
                    }
                }

                // Show All / Show Less toggle
                if (prop.arraySize > k_MaxVisibleRows)
                {
                    if (GUILayout.Button(abShowAll ? $"Show Less ({k_MaxVisibleRows})" : $"Show All ({prop.arraySize})", EditorStyles.miniButton))
                    {
                        SessionState.SetBool(abShowAllKey, !abShowAll);
                    }
                }
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);

            if (changed && prop.arraySize != arraySizeBefore)
            {
                if (entry.OnCollectionChanged != null)
                    _pendingCollectionChangedCallbacks.Add((entry, null));
            }

            return changed;
        }

        /// <summary>
        /// Resolve a FieldInfo from a property path (e.g. "obj.field") on the root target.
        /// </summary>
        /// <summary>
        /// Draw array/list with HideReferenceObjectPicker — manual foldout + elements
        /// without Unity's object picker dropdown on each element.
        /// </summary>
        private static bool DrawArrayNoPicker(TaoTiePropertyEntry entry)
        {
            var prop = entry.Property;
            bool changed = false;
            int arraySizeBefore = prop.arraySize;
            var label = GetLabel(entry);
            string title = label?.text ?? prop.displayName;

            var boxStyle = GetBoxStyle();
            EditorGUILayout.BeginVertical(boxStyle);

            // Foldout title bar
            string foldKey = "TaoTie_Fold_NoPicker_" + entry.PropertyPath;
            bool foldout = SessionState.GetBool(foldKey, false);
            int oldIndent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            Rect titleBarRect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
            EditorGUI.DrawRect(titleBarRect, new Color(0.3f, 0.3f, 0.3f, 0.2f));
            float tbX = titleBarRect.x + 14f;
            float minusX = titleBarRect.xMax - 24f - 2f;
            float plusX = minusX - 24f - 2f;
            string countText = $"({prop.arraySize})";
            var countContent = new GUIContent(countText);
            float countW = EditorStyles.miniLabel.CalcSize(countContent).x + 8f;
            Rect countRect = new Rect(plusX - countW - 4f, titleBarRect.y, countW, titleBarRect.height);
            Rect foldRect = new Rect(tbX, titleBarRect.y, countRect.x - tbX - 4f, titleBarRect.height);
            foldout = EditorGUI.Foldout(foldRect, foldout, new GUIContent(title), true);
            SessionState.SetBool(foldKey, foldout);
            EditorGUI.LabelField(countRect, countContent, EditorStyles.miniLabel);
            if (GUI.Button(new Rect(plusX, titleBarRect.y, 24f, titleBarRect.height), "+", EditorStyles.toolbarButton))
            {
                prop.arraySize++;
                changed = true;
            }
            if (GUI.Button(new Rect(minusX, titleBarRect.y, 24f, titleBarRect.height), "-", EditorStyles.toolbarButton))
            {
                if (prop.arraySize > 0) { prop.arraySize--; changed = true; }
            }
            EditorGUI.indentLevel = oldIndent;

            if (foldout)
            {
                for (int i = 0; i < prop.arraySize; i++)
                {
                    var element = prop.GetArrayElementAtIndex(i);
                    var rowRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight + 2f);
                    if (i % 2 == 1)
                        EditorGUI.DrawRect(rowRect, new Color(0.5f, 0.5f, 0.5f, 0.1f));

                    float x = rowRect.x;
                    EditorGUI.LabelField(new Rect(x, rowRect.y, 28f, rowRect.height), i.ToString());
                    x += 28f;

                    // Draw ObjectField without picker — resolve element type from field info
                    Rect fieldRect = new Rect(x, rowRect.y, rowRect.width - 28f - 22f - 2f, rowRect.height);
                    Type objType = typeof(UnityEngine.Object);
                    var fieldInfo = ResolveFieldFromPath(entry, entry.PropertyPath);
                    if (fieldInfo != null && fieldInfo.FieldType.IsArray)
                        objType = fieldInfo.FieldType.GetElementType();
                    else if (fieldInfo != null && fieldInfo.FieldType.IsGenericType)
                        objType = fieldInfo.FieldType.GetGenericArguments()[0];
                    EditorGUI.BeginChangeCheck();
                    var newObj = EditorGUI.ObjectField(fieldRect, GUIContent.none,
                        element.objectReferenceValue, objType, true);
                    if (EditorGUI.EndChangeCheck())
                    {
                        element.objectReferenceValue = newObj;
                        changed = true;
                    }

                    // Delete button
                    Rect delRect = new Rect(rowRect.xMax - 22f - 2f, rowRect.y, 22f, rowRect.height);
                    if (GUI.Button(delRect, "×"))
                    {
                        prop.DeleteArrayElementAtIndex(i);
                        changed = true;
                        break;
                    }

                    EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.yMax - 1, rowRect.width, 1),
                        new Color(0.3f, 0.3f, 0.3f, 0.3f));
                }
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);

            if (changed && prop.arraySize != arraySizeBefore)
            {
                if (entry.OnCollectionChanged != null)
                    _pendingCollectionChangedCallbacks.Add((entry, null));
            }

            return changed;
        }

        private static FieldInfo ResolveFieldFromPath(TaoTiePropertyEntry entry, string propertyPath)
        {
            // Use the entry's ReflectionField if available, else resolve from serialized object
            if (entry.ReflectionField != null)
                return entry.ReflectionField;
            // Fallback: try to resolve from the property path on the target type
            if (entry.Property != null && entry.Property.serializedObject != null)
                return ResolveFieldFromPath(entry.Property.serializedObject.targetObject, propertyPath);
            return null;
        }

        private static FieldInfo ResolveFieldFromPath(object rootTarget, string propertyPath)
        {
            if (string.IsNullOrEmpty(propertyPath)) return null;
            var parts = propertyPath.Split('.');
            Type currentType = rootTarget.GetType();
            FieldInfo result = null;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (var part in parts)
            {
                if (currentType == null) return null;
                result = currentType.GetField(part, flags);
                Type baseType = currentType.BaseType;
                while (result == null && baseType != null && baseType != typeof(object))
                {
                    result = baseType.GetField(part, flags);
                    baseType = baseType.BaseType;
                }
                if (result == null) return null;
                currentType = result.FieldType;
            }
            return result;
        }

        /// <summary>
        /// For nested properties (e.g. "obj.field"), traverse the root target
        /// to find the actual object that holds the field/method.
        /// </summary>
        private static object ResolveConditionTarget(object rootTarget, TaoTiePropertyEntry entry)
        {
            if (rootTarget == null || string.IsNullOrEmpty(entry.PropertyPath))
                return rootTarget;
            if (!entry.PropertyPath.Contains('.'))
                return rootTarget;

            // Use cached path parts from the entry if available
            string[] parts = entry.cachedPathParts ?? (entry.cachedPathParts = entry.PropertyPath.Split('.'));
            object current = rootTarget;

            for (int i = 0; i < parts.Length - 1 && current != null; i++)
            {
                var field = TaoTiePropertyEntry.GetCachedFieldPublic(current.GetType(), parts[i]);
                if (field == null) return rootTarget;
                current = field.GetValue(current);
            }
            return current ?? rootTarget;
        }

        /// <summary>
        /// Resolve TypeFilter method to get a list of types.
        /// Supports three return value types:
        /// 1. IEnumerable<Type> — direct type list
        /// 2. IEnumerable<ValueDropdownItem> — ValueDropdownItem.Value should be a Type
        /// 3. List<int> or other IEnumerable — values are used as-is (type = value.GetType())
        /// </summary>
        // Cache for ResolveTypeFilter — key: (propertyPath, filterGetter), value: List<Type>
        // Type lists are stable across frames; cleared on managed reference changes
        private static readonly Dictionary<(string, string), List<Type>> s_TypeFilterCache = new();

        private static List<Type> ResolveTypeFilter(string filterGetter, object target, TaoTiePropertyEntry entry)
        {
            // Check cache first — type filter results are stable across frames
            var cacheKey = (entry.PropertyPath, filterGetter);
            if (s_TypeFilterCache.TryGetValue(cacheKey, out var cached))
                return cached;

            var result = new List<Type>();
            var condTarget = ResolveConditionTarget(target, entry);
            var searchType = entry.DeclaringType ?? condTarget?.GetType() ?? target.GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

            // Handle @-expressions for TypeFilter (e.g. "@TestStaticHelper.GetStaticSkillTypes()")
            if (filterGetter.StartsWith("@"))
            {
                var expr = filterGetter.Substring(1);
                // Parse "ClassName.MethodName()" format
                int dotIdx = expr.IndexOf('.');
                if (dotIdx > 0)
                {
                    string className = expr.Substring(0, dotIdx);
                    string methodPart = expr.Substring(dotIdx + 1);
                    // Remove trailing "()"
                    if (methodPart.EndsWith("()"))
                        methodPart = methodPart.Substring(0, methodPart.Length - 2);

                    // Find the type by name across all assemblies
                    Type staticType = null;
                    foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                    {
                        staticType = asm.GetType(className);
                        if (staticType != null) break;
                    }

                    if (staticType != null)
                    {
                        var method = staticType.GetMethod(methodPart, flags);
                        if (method != null)
                        {
                            var retVal = method.Invoke(method.IsStatic ? null : target, null);
                            if (retVal is IEnumerable enumerable)
                            {
                                foreach (var item in enumerable)
                                {
                                    if (item is Type t) result.Add(t);
                                    else if (item is ValueDropdownItem vdi && vdi.Value is Type vt) result.Add(vt);
                                    else if (item != null) result.Add(item.GetType());
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                var method = searchType.GetMethod(filterGetter, flags);
                if (method == null)
                    method = target.GetType().GetMethod(filterGetter, flags);
                if (method != null)
                {
                    var invokeTarget = method.IsStatic ? null : (condTarget ?? target);
                    var retVal = method.Invoke(invokeTarget, null);
                    if (retVal is IEnumerable enumerable)
                    {
                        foreach (var item in enumerable)
                        {
                            if (item is Type t) { result.Add(t); }
                            else if (item is ValueDropdownItem vdi)
                            {
                                if (vdi.Value is Type vt) result.Add(vt);
                                else if (vdi.Value != null) result.Add(vdi.Value.GetType());
                            }
                            else if (item is IValueDropdownItem ivdi)
                            {
                                var val = ivdi.GetValue();
                                if (val is Type vt2) result.Add(vt2);
                                else if (val != null) result.Add(val.GetType());
                            }
                            else if (item != null) result.Add(item.GetType());
                        }
                    }
                }

                // Fallback: if no method found, return all non-abstract subclasses of the FIELD type (not DeclaringType)
                if (result.Count == 0 && entry.DeclaringType != null)
                {
                    // Use the field's actual type, not the declaring type
                    var fieldType = entry.ReflectionField?.FieldType;
                    if (fieldType == null && entry.Property != null)
                    {
                        // Try to resolve from SerializedProperty
                        var fieldInfo = ResolveFieldFromPath(target, entry.PropertyPath);
                        fieldType = fieldInfo?.FieldType;
                    }
                    if (fieldType != null)
                    {
                        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                        {
                            foreach (var t in asm.GetTypes())
                            {
                                if (t.IsClass && !t.IsAbstract && fieldType.IsAssignableFrom(t))
                                    result.Add(t);
                            }
                        }
                    }
                }
            }

            s_TypeFilterCache[cacheKey] = result;
            return result;
        }

        /// <summary>
        /// Odin 同款：普通 Inspector 里选中 StateMachineBehaviour 时，顶层 [SerializeReference]
        /// 字段以 SMB 面板方式绘制（头部行 + SetNull + 按钮条 + 分组），不再走 Unity 默认
        /// managed-ref 折叠框。编辑侧直接反射 managed 子树，无字段级钩子。
        /// 返回 true 表示本字段已由面板接管。
        /// </summary>
        private static bool DrawSmbManagedRefPanel(TaoTiePropertyEntry entry, object target, ref bool changed)
        {
            if (!(target is UnityEngine.StateMachineBehaviour)) return false;
            var p = entry.Property;
            if (p == null || p.propertyType != SerializedPropertyType.ManagedReference) return false;
            if (string.IsNullOrEmpty(entry.PropertyPath) || entry.PropertyPath.IndexOf('.') >= 0) return false;

            var label = GetLabel(entry) ?? new GUIContent(ObjectNames.NicifyVariableName(entry.PropertyName));
            string foldKey = "TaoTie_Fold_" + p.propertyPath;
            bool fold = SessionState.GetBool(foldKey, true);

            if (p.managedReferenceValue == null)
            {
                var fieldType = SMBPropertyLayout.ManagedRefFieldType(p);
                var types = SMBPropertyLayout.CollectSubtypes(fieldType);
                EditorGUILayout.LabelField(label);
                int idx = EditorGUILayout.Popup(-1, SMBPropertyLayout.ToTypeNames(types));
                if (idx >= 0 && idx < types.Count)
                {
                    _pendingManagedReferenceSets.Add((p.propertyPath, types[idx]));
                    changed = true;
                }
                return true;
            }

            float buttonW = GuiSizing.SetNullButtonWidth();
            Rect foldRect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
            Rect buttonRect = new Rect(foldRect.xMax - buttonW - 2f, foldRect.y, buttonW, foldRect.height);
            Rect actualFoldRect = new Rect(foldRect.x, foldRect.y,
                Mathf.Max(0f, buttonRect.x - foldRect.x - 4f), foldRect.height);

            bool setNullClicked = GUI.Button(buttonRect, "SetNull");
            fold = EditorGUI.Foldout(actualFoldRect, fold, label, true);
            SessionState.SetBool(foldKey, fold);

            // 类型名显示在标签与按钮之间
            var refType = p.managedReferenceValue.GetType();
            float labelW = EditorStyles.foldout.CalcSize(label).x + 18f;
            var typeRect = new Rect(foldRect.x + labelW, foldRect.y,
                Mathf.Max(0f, buttonRect.x - (foldRect.x + labelW) - 4f), foldRect.height);
            EditorGUI.LabelField(typeRect, LabelResolver.GetTypeLabel(refType), EditorStyles.boldLabel);

            if (setNullClicked)
            {
                _pendingManagedReferenceClears.Add(p.propertyPath);
                changed = true;
            }

            if (fold && !setNullClicked)
            {
                float w = foldRect.width;
                float h = SMBGroupLayout.GetManagedChildrenHeight(p, w);
                var bodyRect = GUILayoutUtility.GetRect(w, h);
                SMBGroupLayout.DrawManagedChildren(bodyRect, p, bodyRect.y, bodyRect.x + SMBPropertyLayout.FoldIndent);
            }
            return true;
        }
    }
}
