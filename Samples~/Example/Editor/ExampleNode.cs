using System;
using System.Collections.Generic;
using UnityEditor;
using TaoTie.Inspector;
using UnityEngine;

namespace TaoTie.Inspector.Example
{
    [Serializable]
    public class TestClass
    {
        [LabelText("Text")]
        public string TestClassText;

        [Range(0, 10)]
        public int innerNumber;
    }

    [NodeViewType(typeof(ExampleNodeView))]
    public class ExampleNode: NodeBase
    {
        [Title("Basic Fields", true)]
        public string Text;

        [Min(10)]
        [LabelText("Number")]
        public int Number;

        [Min(10)]
        [ValueDropdown("@Error(123)")]
        [LabelText("Dropdown Number")]
        public int NumberValueDropdown;

        [DrawIgnore(Ignore.NodeView)]
        public Vector4 Vector4;

        [ReadOnly]
        [LabelText("Ignore Type")]
        public Ignore Ignore;

        [Range(-20, 20)]
        [LabelText("Range")]
        public float Range;

        [Tooltip("Test Tooltip")]
        [LabelText("Color")]
        public Color Color;

        [Title("Nested Object", true)]
        [ReadOnly]
        [LabelText("Test Class")]
        public TestClass TestClass;

        [Title("Collections", true)]
        public int[] IntArray;
        public List<Rect> RectList;
        public List<TestClass> TestClasses;

        [Title("Group Test", true)]
        [NonSerialized]
        [OnValueChanged(nameof(SetPath))]
        [BoxGroup("Group")]
        [LabelText("Game Object")]
        public GameObject GameObject;

        public void SetPath()
        {
            if (GameObject == null) Path = null;
            var path = AssetDatabase.GetAssetPath(GameObject);
            if (path.StartsWith("Assets/AssetsPackage/"))
            {
                Path = path.Replace("Assets/AssetsPackage/","");
            }
            else
            {
                Path = null;
            }
        }

        [BoxGroup("Group")]
        [Button("ButtonTest")]
        public void Preview()
        {
            if (string.IsNullOrEmpty(Path)) return;
            if (!Path.StartsWith("Assets/AssetsPackage/"))
                GameObject = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssetsPackage/" +Path);
            else
                GameObject = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(Path);
        }

        [ReadOnly]
        [BoxGroup("Group")]
        [LabelText("Path")]
        public string Path;

        [NonSerialized]
        [LabelText("Sprite")]
        public Sprite Sprite;

        [LabelText("Animation Curve")]
        public AnimationCurve AnimationCurve;

        [Space(15)]
        [PropertyOrder(0)]
        [LabelText("Rect")]
        public Rect Rect;

        [NotAssets]
        [Header("Header")]
        [InfoBox("Note: Divisor cannot be 0")]
        [LabelText("Node Reference")]
        public NodeBase NodeBase;

        [LabelText("Dictionary")]
        public Dictionary<int, TestClass> TestClassDic;

        [Title("Unified Attribute Test", true)]
        [FoldoutGroup("Combat")]
        [BoxGroup("Combat/Stats")]
        [LabelText("Attack")]
        public float attack = 10f;

        [BoxGroup("Combat/Stats")]
        [LabelText("Defense")]
        public float defense = 5f;

        [BoxGroup("Combat/Stats")]
        [ShowIf("showAdvanced")]
        [LabelText("Critical Rate")]
        public float criticalRate = 0.1f;

        [FoldoutGroup("Combat")]
        [LabelText("Show Advanced")]
        public bool showAdvanced = false;

        [FoldoutGroup("Combat")]
        [EnumToggleButtons]
        [LabelText("Damage Type")]
        public TaoTieInspectorObject.DamageType damageType = TaoTieInspectorObject.DamageType.Physical;

        [TabGroup("Settings", "Visual")]
        [LabelText("Color")]
        public Color nodeColor = Color.white;

        [TabGroup("Settings", "Audio")]
        [PropertyRange(0, 1)]
        [LabelText("Volume")]
        public float nodeVolume = 0.8f;

        [TabGroup("Settings", "Movement")]
        [LabelText("Speed")]
        public float nodeSpeed = 5f;

        [PropertyOrder(-1)]
        [Title("Configuration", true)]
        [InfoBox("Node configuration info", InfoMessageType.Warning)]
        [ReadOnly]
        [LabelText("Config ID")]
        public string nodeConfigId = "Node_001";

        [DisableInEditorMode]
        [LabelText("Disabled in Edit Mode")]
        public string editorDisabled = "disabled in edit mode";

        [MinValue(0)]
        [MaxValue(100)]
        [LabelText("Clamped Value")]
        public int clampedValue = 50;

        [DisableIf("showAdvanced")]
        [LabelText("Disable Test")]
        public string disableTest = "disabled when showAdvanced";

        [EnableIf("showAdvanced")]
        [LabelText("Enable Test")]
        public string enableTest = "enabled when showAdvanced";

        [ShowIf("@!" + nameof(showAdvanced))]
        [LabelText("Show When Not Advanced")]
        public string showWhenNotAdvanced = "visible when !showAdvanced";

        [HideIf("@" + nameof(showAdvanced))]
        [LabelText("Hide When Advanced")]
        public string hideWhenAdvanced = "hidden when showAdvanced";

        [Title("Expression Test", true)]
        public bool flagA = true;
        public bool flagB = false;

        [ShowIf("@(" + nameof(flagA) + "||" + nameof(flagB) + ")&&!" + nameof(showAdvanced))]
        [LabelText("Complex Condition")]
        public string complexExpr = "(A||B)&&!showAdvanced";

        [Button("Reset", ButtonSizes.Medium)]
        private void ResetNode()
        {
            attack = 10f;
            defense = 5f;
            showAdvanced = false;
            Debug.Log("[ExampleNode] Reset complete");
        }

        [Button("Upgrade", ButtonSizes.Large)]
        private void UpgradeNode()
        {
            clampedValue += 10;
            Debug.Log($"[ExampleNode] Upgraded to {clampedValue}");
        }

        [Title("TableList/NotNull Test", true)]
        [TableList]
        public List<TableRow> tableData = new List<TableRow>
        {
            new TableRow { name = "A", value = 1 },
        };

        [NotNull("Node reference cannot be null")]
        public NodeBase requiredNode;

        public TaoTieInspectorObject TaoTieInspectorObject;

        [Title("TypeFilter Test", true)]
        [TypeFilter(nameof(GetNodeSkillTypes))]
        [LabelText("Node Skill")]
        public BaseNodeSkill nodeSkill;

        public static System.Collections.Generic.IEnumerable<System.Type> GetNodeSkillTypes()
        {
            yield return typeof(NodeAttackSkill);
            yield return typeof(NodeHealSkill);
        }

        [HideReferenceObjectPicker]
        [LabelText("Hidden Picker Skill")]
        public BaseNodeSkill hiddenPickerSkill;

        [Title("DisableInEditorMode + TypeFilter Test", true)]
        [DisableInEditorMode]
        [TypeFilter(nameof(GetNodeSkillTypes))]
        [LabelText("Disabled Skill (Editor Mode)")]
        public BaseNodeSkill disabledFilteredSkill;

        [DisableInEditorMode]
        [TypeFilter(nameof(GetNodeSkillTypes))]
        [BoxGroup("Disabled/Grouped")]
        [LabelText("Grouped Disabled Skill")]
        public BaseNodeSkill groupedDisabledSkill = new NodeHealSkill();

        [Title("DisableInEditorMode + HideReferenceObjectPicker Test", true)]
        [DisableInEditorMode]
        [HideReferenceObjectPicker]
        [LabelText("Disabled Hidden Picker")]
        public BaseNodeSkill disabledHiddenPickerSkill = new NodeAttackSkill();

        [Title("Dictionary Test", true)]
        [LabelText("Attribute Dictionary")]
        public Dictionary<string, float> nodeAttrDict = new Dictionary<string, float>
        {
            { "ATK", 10f },
            { "DEF", 5f },
            { "SPD", 3f },
        };

        [LabelText("Empty Dictionary")]
        public Dictionary<int, string> nodeEmptyDict = new Dictionary<int, string>();

        [Title("Dictionary Nesting Test", true)]
        [LabelText("Object Value Dictionary")]
        public Dictionary<int, NodeDictEntry> nodeObjectDict = new Dictionary<int, NodeDictEntry>
        {
            { 1001, new NodeDictEntry { id = 1001, desc = "Attack Node", weight = 1.5f } },
            { 1002, new NodeDictEntry { id = 1002, desc = "Defense Node", weight = 0.8f } },
        };

        [BoxGroup("Dict/Grouped")]
        [LabelText("Grouped Dictionary")]
        public Dictionary<string, int> nodeGroupedDict = new Dictionary<string, int>
        {
            { "Count", 3 },
            { "Max", 10 },
        };

        [Title("Expression Condition Supplementary Test", true)]
        public bool nCondA = true;
        public bool nCondB = false;
        public bool nCondC = true;

        [HideIf("@!" + nameof(nCondA))]
        [LabelText("HideIf Expression")]
        public string nHideIfExpr = "HideIf @!nCondA";

        [EnableIf("@(" + nameof(nCondA) + "&&" + nameof(nCondC) + ")||!" + nameof(nCondB))]
        [LabelText("EnableIf Expression")]
        public string nEnableIfExpr = "EnableIf (A&&C)||!B";

        [DisableIf("@!" + nameof(nCondC) + "||" + nameof(nCondB))]
        [LabelText("DisableIf Expression")]
        public string nDisableIfExpr = "DisableIf !C||B";

        [ShowIf("@" + nameof(nCondA) + "==" + nameof(nCondC))]
        [LabelText("ShowIf Equality Expression")]
        public string nShowIfEqualExpr = "ShowIf A==C";

        [Title("Vector Field Test", true)]
        public Vector2 nVec2 = new Vector2(1, 2);
        public Vector3 nVec3 = new Vector3(1, 2, 3);
        public Vector4 nVec4 = new Vector4(1, 2, 3, 4);
        public Vector2Int nVec2Int = new Vector2Int(1, 2);
        public Vector3Int nVec3Int = new Vector3Int(1, 2, 3);

        [Title("Multi-level Nesting Test", true)]
        [FoldoutGroup("Deep")]
        [BoxGroup("Deep/Level1")]
        [LabelText("L1 Field")]
        public string nDeepL1 = "level1";

        [BoxGroup("Deep/Level1/Level2")]
        [LabelText("L2 Field")]
        public string nDeepL2 = "level2";

        [FoldoutGroup("Deep")]
        [BoxGroup("Deep/Level1")]
        [LabelText("L1 Another Field")]
        public float nDeepL1Float = 3.14f;

        [Title("OnValueChanged Supplementary Test", true)]
        [OnValueChanged(nameof(OnNodeCountChanged))]
        [LabelText("Callback Trigger Value")]
        public int nOnChangeTest = 42;

        private void OnNodeCountChanged()
        {
            Debug.Log($"[ExampleNode] OnValueChanged: nOnChangeTest={nOnChangeTest}");
        }

        [Title("PropertyRange Dynamic Boundary Test", true)]
        public float nRangeMin = 0f;
        public float nRangeMax = 10f;

        [PropertyRange(nameof(nRangeMin), nameof(nRangeMax))]
        [LabelText("Dynamic Range Value")]
        public float nDynamicRangeValue = 5f;

        [Title("EnumToggleButtons Supplementary Test", true)]
        [EnumToggleButtons]
        [LabelText("Flags Enum")]
        public TaoTieInspectorObject.DamageType nFlagsEnum = TaoTieInspectorObject.DamageType.Fire | TaoTieInspectorObject.DamageType.Ice;

        [Title("ReadOnly Supplementary Test", true)]
        [ReadOnly]
        [LabelText("Read-only String")]
        public string nReadOnlyString = "can't edit";

        [ReadOnly]
        [LabelText("Read-only Integer")]
        public int nReadOnlyInt = 999;

        [ReadOnly]
        [BoxGroup("ReadOnly/Group")]
        [LabelText("Grouped Read-only")]
        public string nGroupedReadOnly = "grouped ro";

        [Title("OnCollectionChanged Test", true)]
        [OnCollectionChanged(nameof(OnNodeListChanged))]
        [LabelText("Node String List With Callback")]
        public List<string> nCallbackList = new List<string> { "alpha", "beta" };

        [OnCollectionChanged(nameof(OnNodeArrayChanged))]
        [LabelText("Node Int Array With Callback")]
        public int[] nCallbackArray = { 10, 20, 30 };

        [OnCollectionChanged(nameof(OnNodeDictChanged))]
        [LabelText("Node Dict With Callback")]
        public Dictionary<string, float> nCallbackDict = new Dictionary<string, float>
        {
            { "speed", 5f },
            { "jump", 3f },
        };

        private void OnNodeListChanged()
        {
            Debug.Log($"[ExampleNode] OnCollectionChanged: nCallbackList count={nCallbackList.Count}");
        }

        private void OnNodeArrayChanged()
        {
            Debug.Log($"[ExampleNode] OnCollectionChanged: nCallbackArray length={nCallbackArray.Length}");
        }

        private void OnNodeDictChanged()
        {
            Debug.Log($"[ExampleNode] OnCollectionChanged: nCallbackDict count={nCallbackDict.Count}");
        }

        [Title("ValueDropdown Remove Test", true)]
        [ValueDropdown(nameof(GetDropdownInts))]
        [LabelText("Dropdown Int List")]
        public List<int> nDropdownIntList = new List<int> { 1, 2, 3 };

        [ValueDropdown(nameof(GetDropdownInts))]
        [LabelText("Dropdown Int Array")]
        public int[] nDropdownIntArray = { 10, 20, 30 };

        [ValueDropdown(nameof(GetDropdownInts), AppendNextDrawer = true)]
        [LabelText("Dropdown Append List")]
        public List<int> nDropdownAppendList = new List<int> { 100, 200 };

        [ValueDropdown(nameof(GetDropdownInts), AppendNextDrawer = true)]
        [LabelText("Dropdown Append Array")]
        public int[] nDropdownAppendArray = { 7, 14 };

        [ValueDropdown(nameof(GetDropdownStrings))]
        [LabelText("Dropdown String List")]
        public List<string> nDropdownStringList = new List<string> { "alpha", "beta" };

        [ValueDropdown(nameof(GetDropdownEnumValues))]
        [LabelText("Dropdown Enum List")]
        public List<TaoTieInspectorObject.DamageType> nDropdownEnumList =
            new List<TaoTieInspectorObject.DamageType>
            {
                TaoTieInspectorObject.DamageType.Fire,
                TaoTieInspectorObject.DamageType.Ice
            };

        public static IEnumerable<ValueDropdownItem> GetDropdownInts()
        {
            for (int i = 0; i <= 5; i++)
                yield return new ValueDropdownItem("Value_" + i, i);
        }

        public static IEnumerable<ValueDropdownItem> GetDropdownStrings()
        {
            yield return new ValueDropdownItem("Alpha", "alpha");
            yield return new ValueDropdownItem("Beta", "beta");
            yield return new ValueDropdownItem("Gamma", "gamma");
        }

        public static IEnumerable<ValueDropdownItem> GetDropdownEnumValues()
        {
            foreach (var val in System.Enum.GetValues(typeof(TaoTieInspectorObject.DamageType)))
            {
                yield return new ValueDropdownItem(val.ToString(), val);
            }
        }

        [Title("OnStateUpdate Test", true)]
        [OnStateUpdate(nameof(OnNodeStateUpdate))]
        [LabelText("Node State Watched Field")]
        public string nStateWatchedField = "watched";

        private void OnNodeStateUpdate()
        {
            // Called every frame while drawing in Graph
        }

        [Title("DrawIgnore Test", true)]
        [DrawIgnore(Ignore.Details)]
        [LabelText("Hidden In Details")]
        public string nHiddenInDetails = "only in node view";

        [DrawIgnore(Ignore.NodeView)]
        [LabelText("Hidden In NodeView")]
        public string nHiddenInNodeView = "only in details";

        [Title("Tooltip Test", true)]
        [LabelText("Node Name")]
        [Tooltip("The display name of this node")]
        public string nToolTipName = "Node";

        [Tooltip("Field with tooltip but no LabelText")]
        public int nToolTipCount = 5;

        [Range(0, 10)]
        [Tooltip("Rating with range and tooltip")]
        public float nToolTipRating = 7.5f;

        [EnumToggleButtons]
        [Tooltip("Select type using toggle buttons")]
        public TaoTieInspectorObject.DamageType nToolTipMode = TaoTieInspectorObject.DamageType.Fire;

        public TooltipTestEntry TooltipTestEntry;
        public override void AddDefaultPorts()
        {
            AddOutputPort("DefaultOutputName", EdgeMode.Multiple, true, true);
        }
    }

    [Serializable]
    public abstract class BaseNodeSkill
    {
        [LabelText("Name")]
        public string name = "";
    }

    [Serializable]
    public class NodeAttackSkill : BaseNodeSkill
    {
        [LabelText("Damage")]
        public float damage = 10f;
    }

    [Serializable]
    public class NodeHealSkill : BaseNodeSkill
    {
        [LabelText("Heal Amount")]
        public float heal = 50f;
    }

    [Serializable]
    public class TableRow
    {
        [LabelText("Name")]
        public string name;

        [LabelText("Value")]
        public int value;
    }

    [Serializable]
    public class NodeDictEntry
    {
        [LabelText("ID")]
        public int id;

        [LabelText("Description")]
        public string desc;

        [LabelText("Weight")]
        public float weight;
    }

    [Title("Tooltip Test", true)]
    [Serializable]
    public class TooltipTestEntry
    {
        [LabelText("Name")]
        [Tooltip("Enter the entry name")]
        public string entryName;

        [Tooltip("Field with tooltip but no LabelText")]
        public int entryCount;

        [Range(0, 10)]
        [Tooltip("Rating with range and tooltip")]
        public float rating;

        [EnumToggleButtons]
        [Tooltip("Select type using toggle buttons")]
        public TaoTieInspectorObject.DamageType tipMode;

        [ShowIf("showDetail")]
        [Tooltip("Only visible when showDetail is true")]
        public string conditionalTip = "conditional";

        [Tooltip("Toggle to show detail")]
        public bool showDetail = false;
    }

    public static class NodeStaticHelper
    {
        public static bool IsCombatEnabled = true;
        public static bool IsPeacefulMode = false;

        public static bool ShouldShowCombat()
        {
            return IsCombatEnabled && !IsPeacefulMode;
        }

        public static void OnCombatChanged()
        {
            Debug.Log("[NodeStaticHelper] Combat data changed");
        }

        public static void OnValueChangedStatic()
        {
            Debug.Log("[NodeStaticHelper] OnValueChanged static callback");
        }

        public static System.Collections.Generic.IEnumerable<System.Type> GetNodeSkillTypes()
        {
            yield return typeof(NodeAttackSkill);
            yield return typeof(NodeHealSkill);
        }
    }

    [Serializable]
    public class CrossClassNodeTest
    {
        [Title("Cross-Class Static Test (Graph)", true)]

        [ShowIf("@" + nameof(NodeStaticHelper) + "." + nameof(NodeStaticHelper.IsCombatEnabled))]
        [LabelText("Visible When Combat")]
        public string showWhenCombat = "combat on";

        [HideIf("@" + nameof(NodeStaticHelper) + "." + nameof(NodeStaticHelper.IsPeacefulMode))]
        [LabelText("Hidden When Peaceful")]
        public string hideWhenPeaceful = "not peaceful";

        [EnableIf("@" + nameof(NodeStaticHelper) + "." + nameof(NodeStaticHelper.ShouldShowCombat) + "()")]
        [LabelText("Combat Enabled")]
        public float combatEnabled = 5f;

        [TypeFilter("@" + nameof(NodeStaticHelper) + "." + nameof(NodeStaticHelper.GetNodeSkillTypes) + "()")]
        [LabelText("Static Filtered Skill")]
        public BaseNodeSkill staticFilteredSkill;

        [OnValueChanged(nameof(TriggerStaticOnChanged))]
        [LabelText("Value With Static Callback")]
        public float valueWithStatic = 1.0f;

        private void TriggerStaticOnChanged()
        {
            NodeStaticHelper.OnValueChangedStatic();
        }

        [OnCollectionChanged(nameof(NodeStaticHelper.OnCombatChanged))]
        [LabelText("Combat List")]
        public List<string> combatList = new List<string> { "atk", "def" };

        [ShowIf("@(" + nameof(NodeStaticHelper) + "." + nameof(NodeStaticHelper.IsCombatEnabled) + "&&!" + nameof(NodeStaticHelper) + "." + nameof(NodeStaticHelper.IsPeacefulMode) + ")")]
        [LabelText("Complex Static Expr")]
        public string complexStaticExpr = "complex";
    }
}
