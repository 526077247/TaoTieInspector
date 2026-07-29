# TaoTie Inspector

Odin-like inspector attributes, unified Graph/Inspector drawing system, and node graph editor for Unity — without Odin dependency.

## Features

### Inspector Attributes (Inspector + Graph unified)

Both the standard Unity Inspector and the Graph node editor share the same attribute processing pipeline. All attributes work identically in both contexts.

| Attribute | Description |
|---|---|
| `[LabelText("name")]` | Override field display label |
| `[ShowIf("condition")]` | Show field when condition is true |
| `[ShowIf("member", value)]` | Show field when member equals value |
| `[ShowIf("@expr")]` | Show field based on expression (`&&`, `\|\|`, `!`, `==`, `!=`, `()`) |
| `[HideIf("condition")]` | Hide field when condition is true |
| `[EnableIf("condition")]` | Enable editing when condition is true |
| `[DisableIf("condition")]` | Disable editing when condition is true |
| `[ReadOnly]` | Prevent editing |
| `[PropertyOrder(n)]` | Control field draw order |
| `[Title("text")]` | Section title with horizontal line |
| `[InfoBox("msg")]` | Info/warning/error message box |
| `[PropertySpace(before, after)]` | Spacing before/after field |
| `[PropertyRange(min, max)]` | Slider for numeric fields (supports dynamic bounds via member names) |
| `[FoldoutGroup("name")]` | Collapsible group |
| `[BoxGroup("name")]` | Bordered box group (supports nesting via `/` paths) |
| `[TabGroup("tab")]` | Tabbed group (single tab name — auto-assigned to default group) |
| `[TabGroup("group", "tab")]` | Tabbed group (explicit group + tab name) |
| `[HorizontalGroup("name")]` | Horizontal layout group |
| `[EnumToggleButtons]` | Enum as toggle button row (supports Flags) |
| `[ValueDropdown("method")]` | Dropdown populated by method (supports `@` expression syntax) |
| `[ValueDropdown("method", AppendNextDrawer = true)]` | Draw original field + dropdown button side by side |
| `[OnValueChanged("method")]` | Call method when field value changes |
| `[OnCollectionChanged("method")]` | Call method when collection size changes |
| `[OnStateUpdate("method")]` | Call method every frame while drawing |
| `[Button("name")]` | Draw a button that invokes a method |
| `[Button("name", ButtonSizes.Large)]` | Button with size (Small/Medium/Large/Gigantic) |
| `[TableList]` | Render List/Array as a table with column headers, grid lines, and draggable column widths |
| `[TableMatrix]` | Render 2D array as a matrix table with custom cell drawing, dynamic labels, and resizable columns |
| `[NotNull]` | Show error if reference is null |
| `[TypeFilter("method")]` | Filter type selection for `[SerializeReference]` fields |
| `[HideReferenceObjectPicker]` | Hide Unity's default managed reference picker |
| `[DrawWithUnity]` | Fall back to Unity's default inspector for this type |
| `[DrawIgnore]` | Ignore field in Graph node view and/or details panel |
| `[DisableInEditorMode]` | Disable editing in edit mode |
| `[MinValue(n)]` / `[MaxValue(n)]` | Clamp numeric values |
| `[NotAssets]` | Mark Object field as non-asset |

> **Note:** `ShowIf`, `HideIf`, `EnableIf`, and `DisableIf` support `AllowMultiple` — stacking multiple attributes combines them with AND logic.

### Expression Syntax (`@` prefix)

`ShowIf`, `HideIf`, `EnableIf`, and `DisableIf` support expression strings starting with `@`:

```csharp
[ShowIf("@!IsGlobal")]
[ShowIf("@EnableVision && !ViewPanoramic")]
[ShowIf("@(IsGlobal || EnableVision) && !ViewPanoramic")]
[ShowIf("@FlagA == FlagB")]
```

Supported operators: `!`, `&&`, `||`, `==`, `!=`, `()`, and member names (bool fields/properties/methods).

### Multiple Conditions (AND logic)

Stack multiple `ShowIf` / `HideIf` / `EnableIf` / `DisableIf` attributes for AND logic:

```csharp
[ShowIf("flagA")]
[ShowIf("flagB")]
[ShowIf("flagC")]
public string visibleWhenAandBandC;
```

### Serialized Base Classes

TaoTie Inspector provides Odin-compatible base classes that force enhanced drawing without requiring any attributes on your fields. Simply inherit from these classes to get Dictionary editing, unified groups, collection drawing, and all other TaoTie features automatically.

| Class | Odin Equivalent | Base Class |
|---|---|---|
| `SerializedScriptableObject` | `OdinSerializedScriptableObject` | `ScriptableObject` |
| `SerializedMonoBehaviour` | `OdinSerializedMonoBehaviour` | `MonoBehaviour` |
| `SerializedStateMachineBehaviour` | `OdinSerializedStateMachineBehaviour` | `StateMachineBehaviour` |

```csharp
using TaoTie.Inspector;

// Inherit from SerializedScriptableObject — Dictionary and collections
// are automatically editable in the Inspector without any attributes
public class ItemDatabase : SerializedScriptableObject
{
    public Dictionary<string, ItemData> items;
    public List<ItemData> itemList;

    [LabelText("Version")]
    public string version;

    protected override void OnAfterDeserialize()
    {
        // Called after the Inspector applies modifications
        Debug.Log("Database updated");
    }
}
```

> **Tip:** You can also implement `IForceTaoTieDrawing` on any custom type to force enhanced drawing without inheriting from a specific base class.

### Unified Collection Drawing

All collection types (List, Array, Dictionary, TableList, TableMatrix, ValueDropdown arrays) share a unified box+grid visual style:

- **Box container** with subtle background
- **Toolbar title bar** with foldout toggle, count label, and `+`/`-` size controls
- **Grid layout** with alternating row colors, index column, and per-row delete buttons
- **Draggable column widths** (TableList, TableMatrix) — drag column borders to resize
- **Performance limiting** — collections with more than 50 items show a "Show All" button instead of rendering everything
- **Indent-safe layout** — title bar correctly aligns foldout, count, and buttons regardless of nesting depth

### TableMatrix

Renders a 2D array (`T[,]`) as a matrix table with full customization:

```csharp
[TableMatrix(
    DrawElementMethod = nameof(DrawCell),
    Labels = nameof(GetLabel),
    IsReadOnly = false,
    HorizontalTitle = "To State",
    VerticalTitle = "From State"
)]
public ConfigFsmTableItem[,] fsmTable = new ConfigFsmTableItem[3, 3];

// Custom cell drawing — signature: (Rect, T) => T
private ConfigFsmTableItem DrawCell(Rect rect, ConfigFsmTableItem value)
{
    if (value == null) value = new ConfigFsmTableItem();
    value.CanTransition = EditorGUI.Toggle(rect, value.CanTransition);
    return value;
}

// Dynamic row/column labels — signature: (T[,], TableAxis, int) => (string, LabelDirection)
private (string, LabelDirection) GetLabel(ConfigFsmTableItem[,] array, TableAxis axis, int index)
{
    return axis switch
    {
        TableAxis.Y => (FsmStates[index].Name, LabelDirection.LeftToRight),
        TableAxis.X => (FsmStates[index].Name, LabelDirection.LeftToRight),
        _ => (index.ToString(), LabelDirection.LeftToRight),
    };
}
```

Features:
- **Diagonal corner cell** — `HorizontalTitle` (top-right) and `VerticalTitle` (bottom-left) split by a diagonal line, with text truncation and tooltip on hover
- **Custom cell drawing** — `DrawElementMethod` receives a `Rect` and the current cell value, returns the new value
- **Dynamic labels** — `Labels` method provides per-row and per-column labels at runtime
- **Resizable columns** — drag column borders (including the label column) to adjust widths; widths are cached per matrix instance
- **Row/column resize** — `R+`/`R-`/`C+`/`C-` buttons to add/remove rows and columns
- **Read-only mode** — `IsReadOnly = true` disables all editing
- **Adaptive label width** — Title:Content = 4:6 ratio that adjusts to window size and nesting depth

### Unified Group System

`FoldoutGroup`, `BoxGroup`, `TabGroup`, and `HorizontalGroup` can be nested using `/` path notation:

```csharp
[FoldoutGroup("Combat")]               // Collapsible group
[BoxGroup("Combat/Stats")]             // Box inside the foldout
public float attack;

[FoldoutGroup("Combat")]
public bool showAdvanced;

[TabGroup("Settings", "Visual")]        // Tab group
public Color color;

[TabGroup("Settings", "Movement")]
public float speed;
```

### Editor Windows

#### TaoTieEditorWindow

An `OdinEditorWindow`-equivalent inspector window. Inherit and auto-draw all fields:

```csharp
public class MyConfigWindow : TaoTieEditorWindow
{
    [MenuItem("Tools/My Config")]
    static void Open() => GetWindow<MyConfigWindow>().Show();

    protected override object InitializeTarget() => MyConfig.Instance;
    protected override string GetWindowTitle() => "Config Editor";
}

// Or set target at runtime:
GetWindow<MyConfigWindow>().SetTarget(newTarget);
```

#### TaoTieDrawerWindow

A persistent inspector window with drag-and-drop support:

```csharp
// Open via menu: Window > TaoTie Inspector > Drawer
// Or via code:
var window = TaoTieDrawerWindow.Open(myObject);
```

- Supports any `UnityEngine.Object` or plain C# object
- Drag-and-drop to inspect
- Follows editor selection

### Graph Node Editor

- **Node-based visual graph editor** with pan/zoom, node dragging, Bezier edge connections
- **Resizable nodes** — drag the right edge to adjust node width
- **Node groups** with collapse/expand and external port aggregation
- **Copy/paste** with internal edge and group remapping
- **Undo/redo** via JSON snapshots
- **Custom node views** via `[NodeViewType(typeof(MyNodeView))]`
- **Port groups** via `[PortGroup(n)]` for connection filtering
- **Procedural rendering** — no external textures or GUISkin assets required
- **Adaptive node width** — automatically widens based on group nesting depth and content
- **Adaptive label width** — Title:Content = 4:6 ratio with minimum width, adapts to panel width
- **Bidirectional edge animation** — output→input (OnExit) and input→output (OnEnter) with ping/animation
- **Collapsed group ports** — external ports displayed on collapsed group boundary with custom labels

### Drawing Pipeline

Both Inspector and Graph paths convert their entries to `GroupEntryData` and pass through the same `TaoTieGroupManager.DrawGroupedEntries()`:

```
Inspector (SerializedProperty)          Graph (Reflection)
        │                                       │
  TaoTiePropertyProcessor                 DrawBase.GetSortMember
  BuildEntries(so)                        (MemberInfo → MemberItem)
        │                                       │
  ConvertToGroupData()                    MemberItemToGroupData()
        │                                       │
        └──────────► GroupEntryData ◄──────────┘
                           │
                  TaoTieGroupManager
                  .DrawGroupedEntries()
                   (shared group tree + render)
                           │
              ┌────────────┴────────────┐
              │                         │
    TaoTiePropertyLayout        DrawBase.DrawMemberInspector
    .DrawProperty()             (Reflection-based)
    (SerializedProperty-based)
```

## Installation

Add to your Unity project via Package Manager:

Clone this repository into your Unity project's `Packages/` directory:
```bash
cd YourUnityProject/Packages
git clone https://github.com/526077247/TaoTieInspector.git
```
Or add it via `manifest.json`:
```json
"com.taotie.inspector": "https://github.com/526077247/TaoTieInspector.git"
```

## Samples

Import the example package via Package Manager:

1. Open `Window > Package Manager`
2. Select `TaoTie Inspector`
3. Click `Samples > Import` to import the Example sample

The sample includes:
- `TaoTieInspectorTest` — MonoBehaviour demonstrating all attributes
- `ExampleNode` / `ExampleGraph` — Node graph example with custom node view
- `TaoTieInspectorObject` — Plain C# object with attribute examples

## Usage

### Basic Attributes

```csharp
using TaoTie.Inspector;

public class PlayerConfig : MonoBehaviour
{
    [LabelText("Player Name")]
    public string playerName;

    [PropertyRange(1, 99)]
    [LabelText("Level")]
    public int level = 1;

    [ShowIf("@level > 10")]
    [LabelText("Advanced Skill")]
    public string advancedSkill;

    [FoldoutGroup("Combat")]
    [BoxGroup("Combat/Stats")]
    [LabelText("Attack")]
    public float attack;

    [Button("Reset")]
    private void Reset() { level = 1; }
}
```

### Serialized Base Classes

```csharp
public class ItemDatabase : SerializedScriptableObject
{
    // Dictionary is directly editable in Inspector — no attributes needed
    public Dictionary<string, ItemData> items;
    public List<ItemData> itemList;
}
```

### ValueDropdown

```csharp
public class SkillConfig : SerializedMonoBehaviour
{
    [ValueDropdown(nameof(GetSkillIds))]
    public int selectedSkillId;

    [ValueDropdown(nameof(GetSkillIds), AppendNextDrawer = true)]
    public int skillWithDropdown;

    [ValueDropdown(nameof(GetSkillIds))]
    public List<int> skillIdList;

    public static ValueDropdownList<int> GetSkillIds()
    {
        var list = new ValueDropdownList<int>();
        list.Add("Fire Ball", 1001);
        list.Add("Ice Nova", 1002);
        list.Add(1003);
        return list;
    }
}
```

### Editor Window

```csharp
public class ConfigEditorWindow : TaoTieEditorWindow
{
    [MenuItem("Tools/Config Editor")]
    static void Open() => GetWindow<ConfigEditorWindow>().Show();

    protected override object InitializeTarget() => ConfigLoader.LoadConfig();
    protected override string GetWindowTitle() => "Config Editor";
}
```

### Node Graph

```csharp
[NodeViewType(typeof(MyNodeView))]
public class MyNode : NodeBase
{
    [LabelText("Name")]
    public string nodeName;

    [ShowIf("isAdvanced")]
    public float bonus;

    public bool isAdvanced = false;

    [Button("Execute")]
    public void Execute() { /* ... */ }

    public override void AddDefaultPorts()
    {
        AddInputPort("Input", EdgeMode.Multiple, true, EdgeType.Both);
        AddOutputPort("Output", EdgeMode.Multiple, true, EdgeType.Both);
    }
}

public class MyGraphWindow : GraphWindow<MyGraph>
{
    [MenuItem("Tools/My Graph")]
    public static void Open() => GetWindow<MyGraphWindow>().Show();
}
```

### TableMatrix

```csharp
public class FsmConfig : SerializedMonoBehaviour
{
    [TableMatrix(
        DrawElementMethod = nameof(DrawFsmCell),
        Labels = nameof(GetFsmLabel),
        HorizontalTitle = "To State",
        VerticalTitle = "From State"
    )]
    public FsmTransition[,] transitions = new FsmTransition[3, 3];

    public List<FsmState> states = new()
    {
        new() { Name = "Idle" },
        new() { Name = "Run" },
        new() { Name = "Jump" },
    };

#if UNITY_EDITOR
    private FsmTransition DrawFsmCell(Rect rect, FsmTransition value)
    {
        if (value == null) value = new FsmTransition();
        value.CanTransition = UnityEditor.EditorGUI.Toggle(rect, value.CanTransition);
        return value;
    }
#endif

    private (string, LabelDirection) GetFsmLabel(FsmTransition[,] array, TableAxis axis, int index)
    {
        return axis switch
        {
            TableAxis.Y => (states[index].Name, LabelDirection.LeftToRight),
            TableAxis.X => (states[index].Name, LabelDirection.LeftToRight),
            _ => (index.ToString(), LabelDirection.LeftToRight),
        };
    }
}

[Serializable]
public class FsmState { public string Name; }
[Serializable]
public class FsmTransition { public bool CanTransition; }
```

## License

MIT
