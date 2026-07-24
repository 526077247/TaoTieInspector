# TaoTie Inspector

Odin-like inspector attributes, unified Graph/Inspector drawing system, and node graph editor for Unity — without Odin dependency.

## Features

### Inspector Attributes (Inspector + Graph unified)

Both the standard Unity Inspector and the Graph node editor share the same attribute processing pipeline. All attributes work identically in both contexts.

| Attribute | Description |
|---|---|
| `[LabelText("name")]` | Override field display label |
| `[ShowIf("condition")]` | Show field when condition is true |
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
| `[TabGroup("group", "tab")]` | Tabbed group |
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
| `[NotNull]` | Show error if reference is null |
| `[TypeFilter("method")]` | Filter type selection for `[SerializeReference]` fields |
| `[HideReferenceObjectPicker]` | Hide Unity's default managed reference picker |
| `[DrawWithUnity]` | Fall back to Unity's default inspector for this type |
| `[DrawIgnore]` | Ignore field in Graph node view and/or details panel |
| `[DisableInEditorMode]` | Disable editing in edit mode |
| `[MinValue(n)]` / `[MaxValue(n)]` | Clamp numeric values |
| `[NotAssets]` | Mark Object field as non-asset |

### Expression Syntax (`@` prefix)

`ShowIf`, `HideIf`, `EnableIf`, and `DisableIf` support expression strings starting with `@`:

```csharp
[ShowIf("@!IsGlobal")]
[ShowIf("@EnableVision && !ViewPanoramic")]
[ShowIf("@(IsGlobal || EnableVision) && !ViewPanoramic")]
[ShowIf("@FlagA == FlagB")]
```

Supported operators: `!`, `&&`, `||`, `==`, `!=`, `()`, and member names (bool fields/properties/methods).

### Unified Collection Drawing

All collection types (List, Array, Dictionary, TableList, ValueDropdown arrays) share a unified box+grid visual style:

- **Box container** with subtle background
- **Toolbar title bar** with foldout toggle, count label, and `+`/`-` size controls
- **Grid layout** with alternating row colors, index column, and per-row delete buttons
- **Draggable column widths** (TableList only) — drag column borders to resize
- **Performance limiting** — collections with more than 50 items show a "Show All" button instead of rendering everything
- **Indent-safe layout** — title bar correctly aligns foldout, count, and buttons regardless of nesting depth

#### ValueDropdown Arrays

```csharp
// Standard dropdown — replaces the field with a popup
[ValueDropdown(nameof(GetOptions))]
public int selected;

// Append mode — draws the original field + a ▼ dropdown button
[ValueDropdown(nameof(GetOptions), AppendNextDrawer = true)]
public int valueWithDropdown;

// Works on arrays/lists too — each element gets its own dropdown
[ValueDropdown(nameof(GetOptions))]
public List<int> dropdownList;
```

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

### Graph Node Editor

- **Node-based visual graph editor** with pan/zoom, node dragging, Bezier edge connections
- **Node groups** with collapse/expand and external port aggregation
- **Copy/paste** with internal edge and group remapping
- **Undo/redo** via JSON snapshots
- **Custom node views** via `[NodeViewType(typeof(MyNodeView))]`
- **Port groups** via `[PortGroup(n)]` for connection filtering
- **Procedural rendering** — no external textures or GUISkin assets required
- **Adaptive node width** — automatically widens based on group nesting depth and table/dictionary content
- **Adaptive label width** — Title:Content = 4:6 ratio with minimum width, adapts to panel width
- **Bidirectional edge animation** — output→input (OnExit) and input→output (OnEnter) with ping/animation
- **Collapsed group ports** — external ports displayed on collapsed group boundary with custom labels

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

### TaoTieEditorWindow

An `OdinEditorWindow`-equivalent inspector window:

```csharp
// Open via menu: Window > TaoTie Inspector
// Or via code:
var window = TaoTieEditorWindow.Open(myObject);
```

- Supports any `UnityEngine.Object`
- Drag-and-drop to inspect
- Follows editor selection

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

### ValueDropdown

```csharp
public class SkillConfig : MonoBehaviour
{
    [ValueDropdown(nameof(GetSkillIds))]
    public int selectedSkillId;

    [ValueDropdown(nameof(GetSkillIds), AppendNextDrawer = true)]
    public int skillWithDropdown;

    [ValueDropdown(nameof(GetSkillIds))]
    public List<int> skillIdList;

    public static IEnumerable<ValueDropdownItem> GetSkillIds()
    {
        yield return new ValueDropdownItem("Fire Ball", 1001);
        yield return new ValueDropdownItem("Ice Nova", 1002);
        yield return new ValueDropdownItem("Heal", 1003);
    }
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

## License

MIT
