using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using TaoTie.Inspector;
using TaoTie.Inspector.Editor;

namespace TaoTie.Inspector
{
    public class ExampleEditorWindow : TaoTieEditorWindow
    {
        [MenuItem("Tools/Example Editor Window")]
        public static void Open()
        {
            var window = GetWindow<ExampleEditorWindow>();
            window.titleContent = new GUIContent("Example Editor Window");
            window.Show();
        }

        protected override string GetWindowTitle() => "Example Editor Window";

        #region Basic Attributes

        [LabelText("Custom Label")]
        public string labelTest = "hello";

        [PropertyOrder(-1)]
        [Title("Configuration", true)]
        [InfoBox("This window demonstrates all TaoTie Inspector attributes.", InfoMessageType.Info)]
        [ReadOnly]
        [LabelText("Config ID")]
        public string configId = "Window_001";

        [Header("Unity Built-in Attribute Test")]
        [Space(20)]
        [Range(0, 100)]
        public int unityRangeInt = 50;

        [Range(0f, 1f)]
        public float unityRangeFloat = 0.5f;

        [Min(5)]
        public int unityMinInt = 10;

        #endregion

        #region Title & InfoBox

        [Title("Title Alignment Test", true)]
        [Title("Left Aligned Title", titleAlignment: TitleAlignmentType.Left)]
        public string leftAlignField = "left";

        [Title("Center Aligned Title", titleAlignment: TitleAlignmentType.Center)]
        public string centerAlignField = "center";

        [Title("Right Aligned Title", titleAlignment: TitleAlignmentType.Right)]
        public string rightAlignField = "right";

        [Title("No Line Title", horizontalLine: false)]
        public string noLineField = "no line";

        [Title("Indented Title", horizontalLine: true, indented: true)]
        public string indentedField = "indented";

        [Title("InfoBox Conditional Display Test", true)]
        public bool showInfoBox = true;

        [InfoBox("This message is only shown when showInfoBox=true", InfoMessageType.Warning, nameof(showInfoBox))]
        public string conditionalInfoBox = "check";

        [InfoBox("Info message", InfoMessageType.Info)]
        public string infoInfoBox = "info";

        [InfoBox("Warning message", InfoMessageType.Warning)]
        public string warningInfoBox = "warn";

        [InfoBox("Error message", InfoMessageType.Error)]
        public string errorInfoBox = "error";

        #endregion

        #region MinValue / MaxValue

        [Title("MinValue/MaxValue Test", true)]
        [MinValue(0)]
        [MaxValue(100)]
        public int clampedInt = 50;

        [MinValue(0f)]
        [MaxValue(1f)]
        public float clampedFloat = 0.5f;

        [MinValue(-10)]
        public long clampedLong = 5;

        [MinValue(-1.5)]
        [MaxValue(1.5)]
        public double clampedDouble = 0.5;

        #endregion

        #region DisableInEditorMode & PropertySpace

        [Title("DisableInEditorMode Test", true)]
        [DisableInEditorMode]
        public string disabledInEditor = "Disabled in edit mode";

        [Title("PropertySpace Test", true)]
        [PropertySpace(20, 30)]
        public string spacedBeforeAfter = "Spacing before and after";

        #endregion

        #region Nested Groups

        [Title("Nested Group Test", true)]
        [FoldoutGroup("Nest")]
        [BoxGroup("Nest/Inner")]
        [LabelText("Inner Field 1")]
        public string nestInner1 = "inner1";

        [BoxGroup("Nest/Inner")]
        [LabelText("Inner Field 2")]
        public string nestInner2 = "inner2";

        [FoldoutGroup("Nest")]
        [LabelText("Outer Direct Field")]
        public string nestDirect = "direct";

        [Title("HorizontalGroup Test", true)]
        [HorizontalGroup("HGroup")]
        public float hField1 = 1f;

        [HorizontalGroup("HGroup")]
        public float hField2 = 2f;

        #endregion

        #region TableList & NotNull

        [Title("TableList/NotNull Test", true)]
        [TableList]
        public List<TableRow> tableData = new List<TableRow>
        {
            new TableRow { name = "Row1", value = 1 },
            new TableRow { name = "Row2", value = 2 },
        };

        [NotNull("This object cannot be null!")]
        public GameObject requiredObject;

        [NotNull]
        public Transform requiredTransform;

        #endregion

        #region Buttons

        [Title("Button Size Test", true)]
        [Button("Small", ButtonSizes.Small)]
        private void SmallButton() { Debug.Log("Small clicked"); }

        [Button("Medium", ButtonSizes.Medium)]
        private void MediumButton() { Debug.Log("Medium clicked"); }

        [Button("Large", ButtonSizes.Large)]
        private void LargeButton() { Debug.Log("Large clicked"); }

        [Button("Gigantic", ButtonSizes.Gigantic)]
        private void GiganticButton() { Debug.Log("Gigantic clicked"); }

        [Button("Default Name Method")]
        private void DefaultNameMethod() { Debug.Log("Default name clicked"); }

        [Button("Reset Values")]
        private void ResetValues()
        {
            clampedInt = 50;
            clampedFloat = 0.5f;
            Debug.Log("[ExampleEditorWindow] Values reset!");
        }

        #endregion

        #region TypeFilter & HideReferenceObjectPicker

        [Title("TypeFilter Test", true)]
        [TypeFilter(nameof(GetFilteredSkillTypes))]
        [LabelText("Skill Instance")]
        [SerializeReference]
        public BaseSkill filteredSkill;

        [TypeFilter(nameof(GetFilteredSkillTypes))]
        [BoxGroup("Filter/Nested")]
        [LabelText("Nested Skill")]
        [SerializeReference]
        public BaseSkill nestedFilteredSkill;

        public static IEnumerable<Type> GetFilteredSkillTypes()
        {
            yield return typeof(AttackSkill);
            yield return typeof(DefenseSkill);
            yield return typeof(HealSkill);
        }

        [Title("HideReferenceObjectPicker Test", true)]
        [HideReferenceObjectPicker]
        [LabelText("Hidden Picker")]
        [SerializeReference]
        public BaseSkill hiddenPickerSkill = new AttackSkill();

        [Title("DisableInEditorMode + TypeFilter Test", true)]
        [DisableInEditorMode]
        [TypeFilter(nameof(GetFilteredSkillTypes))]
        [LabelText("Disabled Skill (Editor Mode)")]
        [SerializeReference]
        public BaseSkill disabledFilteredSkill;

        [DisableInEditorMode]
        [TypeFilter(nameof(GetFilteredSkillTypes))]
        [BoxGroup("Disabled/Grouped")]
        [LabelText("Grouped Disabled Skill")]
        [SerializeReference]
        public BaseSkill groupedDisabledSkill = new HealSkill();

        #endregion

        #region Dictionary

        [Title("Dictionary Test", true)]
        [LabelText("Basic Dictionary")]
        public Dictionary<string, int> basicDict = new Dictionary<string, int>
        {
            { "HP", 100 },
            { "MP", 50 },
        };

        [LabelText("Object Dictionary")]
        public Dictionary<int, SkillDictEntry> objectDict = new Dictionary<int, SkillDictEntry>
        {
            { 1, new SkillDictEntry { id = 1, name = "Fireball", power = 30f } },
            { 2, new SkillDictEntry { id = 2, name = "Ice Nova", power = 25f } },
        };

        [LabelText("Nested Dictionary")]
        [BoxGroup("Dict/Inner")]
        public Dictionary<string, float> nestedDict = new Dictionary<string, float>
        {
            { "Speed", 5.5f },
            { "Jump", 3.2f },
        };

        [BoxGroup("Dict/Inner")]
        [LabelText("Empty Dictionary")]
        public Dictionary<string, string> emptyDict = new Dictionary<string, string>();

        #endregion

        #region Expression Conditions

        [Title("Expression Condition Test", true)]
        public bool condA = true;
        public bool condB = false;
        public bool condC = true;

        [HideIf("@!" + nameof(condA))]
        [LabelText("HideIf Expression")]
        public string hideIfExpr = "HideIf @!condA";

        [EnableIf("@(" + nameof(condA) + "&&" + nameof(condC) + ")||!" + nameof(condB))]
        [LabelText("EnableIf Expression")]
        public string enableIfExpr = "EnableIf (A&&C)||!B";

        [DisableIf("@!" + nameof(condC) + "||" + nameof(condB))]
        [LabelText("DisableIf Expression")]
        public string disableIfExpr = "DisableIf !C||B";

        [ShowIf("@" + nameof(condA) + "==" + nameof(condC))]
        [LabelText("ShowIf Equality Expression")]
        public string showIfEqualExpr = "ShowIf A==C";

        #endregion

        #region Multiple Conditions (AllowMultiple)

        [Title("Multiple Conditions (AND) Test", true)]
        public bool flagA = true;
        public bool flagB = true;

        [ShowIf("flagA")]
        [ShowIf("flagB")]
        [LabelText("Visible When A and B")]
        public string multiShowIf = "visible when both";

        [EnableIf("flagA")]
        [EnableIf("flagB")]
        [LabelText("Enabled When A and B")]
        public string multiEnableIf = "enabled when both";

        #endregion

        #region Vectors

        [Title("Vector Field Test", true)]
        public Vector2 vec2 = new Vector2(1, 2);
        public Vector3 vec3 = new Vector3(1, 2, 3);
        public Vector4 vec4 = new Vector4(1, 2, 3, 4);
        public Vector2Int vec2Int = new Vector2Int(1, 2);
        public Vector3Int vec3Int = new Vector3Int(1, 2, 3);

        #endregion

        #region Multi-level Nesting

        [Title("Multi-level Nesting Test", true)]
        [FoldoutGroup("Deep")]
        [BoxGroup("Deep/Level1")]
        [LabelText("L1 Field")]
        public string deepL1 = "level1";

        [BoxGroup("Deep/Level1/Level2")]
        [LabelText("L2 Field")]
        public string deepL2 = "level2";

        [FoldoutGroup("Deep")]
        [BoxGroup("Deep/Level1")]
        [LabelText("L1 Another Field")]
        public float deepL1Float = 3.14f;

        #endregion

        #region OnValueChanged

        [Title("OnValueChanged Test", true)]
        [OnValueChanged(nameof(OnCountChanged))]
        [LabelText("Callback Trigger Value")]
        public int onChangeTest = 42;

        private void OnCountChanged()
        {
            Debug.Log($"[OnValueChanged] onChangeTest changed to: {onChangeTest}");
        }

        #endregion

        #region PropertyRange

        [Title("PropertyRange Dynamic Boundary Test", true)]
        public float rangeMin = 0f;
        public float rangeMax = 10f;

        [PropertyRange(nameof(rangeMin), nameof(rangeMax))]
        [LabelText("Dynamic Range Value")]
        public float dynamicRangeValue = 5f;

        #endregion

        #region EnumToggleButtons

        [Title("EnumToggleButtons Test", true)]
        public enum TestFlags
        {
            None = 0,
            FlagA = 1,
            FlagB = 2,
            FlagC = 4,
            All = 7,
        }

        [EnumToggleButtons]
        [LabelText("Flags Enum")]
        public TestFlags flagsEnum = TestFlags.FlagA | TestFlags.FlagC;

        #endregion

        #region ReadOnly

        [Title("ReadOnly Test", true)]
        [ReadOnly]
        [LabelText("Read-only String")]
        public string readOnlyString = "can't edit";

        [ReadOnly]
        [LabelText("Read-only Integer")]
        public int readOnlyInt = 999;

        [ReadOnly]
        [BoxGroup("ReadOnly/Group")]
        [LabelText("Grouped Read-only")]
        public string groupedReadOnly = "grouped ro";

        #endregion

        #region OnCollectionChanged

        [Title("OnCollectionChanged Test", true)]
        [OnCollectionChanged(nameof(OnListChanged))]
        [LabelText("String List With Callback")]
        public List<string> callbackList = new List<string> { "apple", "banana" };

        [OnCollectionChanged(nameof(OnArrayChanged))]
        [LabelText("Int Array With Callback")]
        public int[] callbackArray = { 1, 2, 3 };

        [OnCollectionChanged(nameof(OnDictChanged))]
        [LabelText("Dict With Callback")]
        public Dictionary<string, int> callbackDict = new Dictionary<string, int>
        {
            { "x", 1 },
            { "y", 2 },
        };

        [OnCollectionChanged(nameof(OnListChanged))]
        [TableList]
        [LabelText("Table List With Callback")]
        public List<TableRow> callbackTableList = new List<TableRow>
        {
            new TableRow { name = "a", value = 1 },
        };

        private void OnListChanged()
        {
            Debug.Log($"[OnCollectionChanged] callbackList count={callbackList.Count}");
        }

        private void OnArrayChanged()
        {
            Debug.Log($"[OnCollectionChanged] callbackArray length={callbackArray.Length}");
        }

        private void OnDictChanged()
        {
            Debug.Log($"[OnCollectionChanged] callbackDict count={callbackDict.Count}");
        }

        #endregion

        #region ValueDropdown

        [Title("ValueDropdown Test", true)]
        [ValueDropdown(nameof(GetDropdownInts))]
        [LabelText("Dropdown Int Field")]
        public int dropdownInt = 1;

        [ValueDropdown(nameof(GetDropdownInts))]
        [LabelText("Dropdown Int List")]
        public List<int> dropdownIntList = new List<int> { 1, 2, 3 };

        [ValueDropdown(nameof(GetDropdownInts))]
        [LabelText("Dropdown Int Array")]
        public int[] dropdownIntArray = { 10, 20, 30 };

        [ValueDropdown(nameof(GetDropdownInts), AppendNextDrawer = true)]
        [LabelText("Dropdown Append Int")]
        public int dropdownAppendInt = 0;

        [ValueDropdown(nameof(GetDropdownInts), AppendNextDrawer = true)]
        [LabelText("Dropdown Append List")]
        public List<int> dropdownAppendList = new List<int> { 100, 200 };

        [ValueDropdown(nameof(GetDropdownInts), AppendNextDrawer = true)]
        [LabelText("Dropdown Append Array")]
        public int[] dropdownAppendArray = { 7, 14 };

        [ValueDropdown(nameof(GetDropdownStrings))]
        [LabelText("Dropdown String Field")]
        public string dropdownString = "alpha";

        [ValueDropdown(nameof(GetDropdownStrings))]
        [LabelText("Dropdown String List")]
        public List<string> dropdownStringList = new List<string> { "alpha", "beta" };

        [ValueDropdown(nameof(GetDropdownEnumValues))]
        [LabelText("Dropdown Enum Field")]
        public TaoTieInspectorObject.DamageType dropdownEnum = TaoTieInspectorObject.DamageType.Fire;

        [ValueDropdown(nameof(GetDropdownEnumValues))]
        [LabelText("Dropdown Enum List")]
        public List<TaoTieInspectorObject.DamageType> dropdownEnumList =
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
            foreach (var val in Enum.GetValues(typeof(TaoTieInspectorObject.DamageType)))
            {
                yield return new ValueDropdownItem(val.ToString(), val);
            }
        }

        #endregion

        #region OnStateUpdate

        [Title("OnStateUpdate Test", true)]
        [OnStateUpdate(nameof(OnStateUpdateMethod))]
        [LabelText("State Watched Field")]
        public string stateWatchedField = "watched";

        private void OnStateUpdateMethod()
        {
            // Called every frame while drawing
        }

        #endregion

        #region Cross-Class Static Test

        [Title("Cross-Class Static Test", true)]

        [ShowIf("@" + nameof(TestStaticHelper) + "." + nameof(TestStaticHelper.IsFeatureEnabled))]
        [LabelText("Visible When Feature Enabled")]
        public string showWhenFeatureEnabled = "feature on";

        [HideIf("@" + nameof(TestStaticHelper) + "." + nameof(TestStaticHelper.IsDebugMode))]
        [LabelText("Hidden When Debug Mode")]
        public string hideWhenDebugMode = "not in debug";

        [EnableIf("@" + nameof(TestStaticHelper) + "." + nameof(TestStaticHelper.IsFeatureEnabled))]
        [LabelText("Enabled By Static Flag")]
        public float staticEnabledField = 3.14f;

        [DisableIf("@" + nameof(TestStaticHelper) + "." + nameof(TestStaticHelper.IsDebugMode))]
        [LabelText("Disabled By Static Flag")]
        public int staticDisabledField = 42;

        [ShowIf("@" + nameof(TestStaticHelper) + "." + nameof(TestStaticHelper.ShouldShowAdvanced) + "()")]
        [LabelText("Advanced (Static Method)")]
        public string advancedField = "advanced";

        [Title("TypeFilter From Static Class", true)]
        [TypeFilter("@" + nameof(TestStaticHelper) + "." + nameof(TestStaticHelper.GetStaticSkillTypes) + "()")]
        [LabelText("Static Filtered Skill")]
        [SerializeReference]
        public BaseSkill staticFilteredSkill;

        [Title("OnValueChanged Static Callback", true)]
        [OnValueChanged(nameof(TriggerStaticValueChanged))]
        [LabelText("Value With Static Callback")]
        public float valueWithStaticCallback = 1.0f;

        private void TriggerStaticValueChanged()
        {
            TestStaticHelper.OnValueChangedCallback(this);
        }

        [Title("OnCollectionChanged Static Callback", true)]
        [OnCollectionChanged(nameof(TriggerStaticCollectionChanged))]
        [LabelText("Static Callback List")]
        public List<string> staticCallbackList = new List<string> { "a", "b" };

        private void TriggerStaticCollectionChanged()
        {
            TestStaticHelper.OnCollectionChangedCallback();
        }

        [Title("ShowIf With Static Value Comparison", true)]
        [ShowIf("@" + nameof(TestStaticHelper) + "." + nameof(TestStaticHelper.ValidateValue) + "(" + nameof(testValue) + ")")]
        [LabelText("Valid Value Field")]
        public string validValueField = "value is valid";

        [LabelText("Test Value")]
        public int testValue = 50;

        [Title("Cross-Class Expression Test", true)]
        [ShowIf("@(" + nameof(TestStaticHelper) + "." + nameof(TestStaticHelper.IsFeatureEnabled) + "||" + nameof(TestStaticHelper) + "." + nameof(TestStaticHelper.IsDebugMode) + ")&&!" + nameof(testValue) + ">0")]
        [LabelText("Complex Cross-Class Expr")]
        public string complexCrossExpr = "complex static";

        #endregion

        #region Tooltip Test

        public TooltipTestClass tooltipTest;

        #endregion
    }

    #region Shared Serializable Classes

    [Serializable]
    public abstract class BaseSkill
    {
        [LabelText("Skill Name")]
        public string skillName = "";

        [LabelText("Cooldown")]
        public float cooldown = 1f;
    }

    [Serializable]
    public class AttackSkill : BaseSkill
    {
        [LabelText("Damage")]
        public float damage = 10f;

        [LabelText("Attack Range")]
        public float range = 5f;
    }

    [Serializable]
    public class DefenseSkill : BaseSkill
    {
        [LabelText("Defense Value")]
        public float defense = 20f;

        [LabelText("Duration")]
        public float duration = 3f;
    }

    [Serializable]
    public class HealSkill : BaseSkill
    {
        [LabelText("Heal Amount")]
        public float healAmount = 50f;

        [LabelText("Heal Range")]
        public float healRange = 10f;
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
    public class SkillDictEntry
    {
        [LabelText("ID")]
        public int id;

        [LabelText("Name")]
        public string name;

        [LabelText("Power")]
        public float power;
    }

    [Title("Tooltip Test", true)]
    [Serializable]
    public class TooltipTestClass
    {
        [LabelText("Name")]
        [Tooltip("Enter the name of the item")]
        public string name;

        [Tooltip("This field has tooltip but no LabelText")]
        public int count;

        [LabelText("Enabled")]
        [Tooltip("Toggle to enable special behavior")]
        public bool enabled;

        [Range(0, 100)]
        [Tooltip("Progress percentage with range and tooltip")]
        public float progress;

        [EnumToggleButtons]
        [Tooltip("Select mode using toggle buttons")]
        public TaoTieInspectorObject.DamageType mode;

        [ReadOnly]
        [Tooltip("This read-only field has a tooltip")]
        public string readOnlyWithTip = "locked";

        [ShowIf("enabled")]
        [Tooltip("Only visible when enabled is true")]
        public string conditionalWithTip = "conditional";

        [BoxGroup("Tip/Group")]
        [Tooltip("Inside a box group with tooltip")]
        public float groupedValue = 1.5f;
    }

    public static class TestStaticHelper
    {
        public static bool IsFeatureEnabled = true;
        public static bool IsDebugMode = false;

        public static bool ShouldShowAdvanced()
        {
            return IsFeatureEnabled && !IsDebugMode;
        }

        public static bool ValidateValue(int value)
        {
            return value > 0 && value < 100;
        }

        public static void OnValueChangedCallback(object sender)
        {
            Debug.Log($"[StaticCallback] OnValueChanged triggered from {sender?.GetType().Name ?? "null"}");
        }

        public static void OnCollectionChangedCallback()
        {
            Debug.Log("[StaticCallback] OnCollectionChanged triggered");
        }

        public static IEnumerable<Type> GetStaticSkillTypes()
        {
            yield return typeof(AttackSkill);
            yield return typeof(HealSkill);
        }
    }

    #endregion
}
