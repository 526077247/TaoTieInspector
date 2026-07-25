using System;
using System.Collections.Generic;
using TaoTie.Inspector;
using UnityEngine;

namespace TaoTie.Inspector
{
    [CreateAssetMenu(fileName = "ExampleScriptableObject", menuName = "TaoTie/Example ScriptableObject", order = 1)]
    public class ExampleScriptableObject : SerializedScriptableObject
    {
        [Title("Basic Fields", true)]
        [LabelText("Name")]
        public string itemName = "Default Item";

        [LabelText("Count")]
        [MinValue(0)]
        [MaxValue(999)]
        public int count = 1;

        [LabelText("Price")]
        [PropertyRange(0, 1000)]
        public float price = 9.99f;

        [Title("Enum & Toggle", true)]
        [EnumToggleButtons]
        [LabelText("Rarity")]
        public ItemRarity rarity = ItemRarity.Common;

        [EnumToggleButtons]
        [LabelText("Categories")]
        public ItemCategory categories = ItemCategory.Consumable | ItemCategory.Tradeable;

        public enum ItemRarity
        {
            Common,
            Uncommon,
            Rare,
            Epic,
            Legendary
        }

        [Flags]
        public enum ItemCategory
        {
            None = 0,
            Consumable = 1,
            Equipment = 2,
            Material = 4,
            Tradeable = 8,
            Quest = 16
        }

        [Title("Conditional Display", true)]
        public bool showAdvanced = false;

        [ShowIf("showAdvanced")]
        [LabelText("Bonus Damage")]
        public float bonusDamage = 0f;

        [ShowIf("showAdvanced")]
        [LabelText("Bonus Defense")]
        public float bonusDefense = 0f;

        [HideIf("showAdvanced")]
        [LabelText("Simple Mode Hint")]
        public string simpleHint = "Enable Show Advanced for more options";

        [ShowIf("@showAdvanced && rarity == ItemRarity.Legendary")]
        [LabelText("Legendary Effect")]
        public string legendaryEffect = "Grants invulnerability for 3 seconds";

        [Title("Groups", true)]
        [FoldoutGroup("Stats")]
        [BoxGroup("Stats/Combat")]
        [LabelText("Attack")]
        public float attack = 10f;

        [BoxGroup("Stats/Combat")]
        [LabelText("Defense")]
        public float defense = 5f;

        [FoldoutGroup("Stats")]
        [BoxGroup("Stats/Survival")]
        [LabelText("Max HP")]
        public float maxHp = 100f;

        [BoxGroup("Stats/Survival")]
        [LabelText("Max MP")]
        public float maxMp = 50f;

        [TabGroup("Settings", "Visual")]
        [LabelText("Icon Color")]
        public Color iconColor = Color.white;

        [TabGroup("Settings", "Visual")]
        [LabelText("Show Glow")]
        public bool showGlow = false;

        [TabGroup("Settings", "Balance")]
        [LabelText("Tier")]
        public int tier = 1;

        [TabGroup("Settings", "Balance")]
        [PropertyRange(1, 10)]
        [LabelText("Difficulty Scale")]
        public float difficultyScale = 1f;

        [Title("Collections", true)]
        [LabelText("Tags")]
        public List<string> tags = new List<string> { "new", "default" };

        [TableList]
        [LabelText("Item Table")]
        public List<ItemTableEntry> itemTable = new List<ItemTableEntry>
        {
            new ItemTableEntry { name = "Potion", count = 5, weight = 1f },
            new ItemTableEntry { name = "Scroll", count = 2, weight = 0.5f },
        };

        [LabelText("Attribute Dictionary")]
        public Dictionary<string, float> attributes = new Dictionary<string, float>
        {
            { "STR", 10f },
            { "DEX", 8f },
            { "INT", 15f },
        };

        [BoxGroup("Nested/Dict")]
        [LabelText("Nested Dictionary")]
        public Dictionary<int, string> nestedDict = new Dictionary<int, string>
        {
            { 1, "Entry A" },
            { 2, "Entry B" },
        };

        [Title("ValueDropdown", true)]
        [ValueDropdown(nameof(GetSkillIds))]
        [LabelText("Selected Skill")]
        public int selectedSkillId = 1001;

        [ValueDropdown(nameof(GetSkillIds), AppendNextDrawer = true)]
        [LabelText("Skill With Dropdown")]
        public int skillWithDropdown = 1002;

        [ValueDropdown(nameof(GetSkillIds))]
        [LabelText("Skill List")]
        public List<int> skillList = new List<int> { 1001, 1003 };

        public static IEnumerable<ValueDropdownItem> GetSkillIds()
        {
            yield return new ValueDropdownItem("Fire Ball", 1001);
            yield return new ValueDropdownItem("Ice Nova", 1002);
            yield return new ValueDropdownItem("Heal", 1003);
            yield return new ValueDropdownItem("Lightning", 1004);
        }

        [Title("TypeFilter & SerializeReference", true)]
        [TypeFilter(nameof(GetEffectTypes))]
        [LabelText("Effect Instance")]
        [SerializeReference]
        public ItemEffect effectInstance;

        [HideReferenceObjectPicker]
        [LabelText("Hidden Picker Effect")]
        [SerializeReference]
        public ItemEffect hiddenPickerEffect = new DamageEffect();

        [DisableInEditorMode]
        [TypeFilter(nameof(GetEffectTypes))]
        [LabelText("Runtime Only Effect")]
        [SerializeReference]
        public ItemEffect runtimeEffect;

        public static IEnumerable<Type> GetEffectTypes()
        {
            yield return typeof(DamageEffect);
            yield return typeof(HealEffect);
            yield return typeof(BuffEffect);
        }

        [Title("Buttons", true)]
        [Button("Reset Stats", ButtonSizes.Medium)]
        private void ResetStats()
        {
            attack = 10f;
            defense = 5f;
            maxHp = 100f;
            maxMp = 50f;
            Debug.Log("[ExampleScriptableObject] Stats reset!");
        }

        [Button("Level Up")]
        private void LevelUp()
        {
            tier++;
            attack *= 1.1f;
            Debug.Log($"[ExampleScriptableObject] Leveled up to Tier {tier}! Attack now {attack}");
        }

        [Button("Print Info", ButtonSizes.Large)]
        private void PrintInfo()
        {
            Debug.Log($"[ExampleScriptableObject] {itemName} | Rarity: {rarity} | Tier: {tier} | Price: {price}");
        }

        [Title("OnValueChanged", true)]
        [OnValueChanged(nameof(OnNameChanged))]
        [LabelText("Display Name")]
        public string displayName = "Test Item";

        private void OnNameChanged()
        {
            Debug.Log($"[ExampleScriptableObject] Name changed to: {displayName}");
        }

        [Title("OnCollectionChanged", true)]
        [OnCollectionChanged(nameof(OnTagsChanged))]
        [LabelText("Tags With Callback")]
        public List<string> tagsWithCallback = new List<string> { "tag1", "tag2" };

        [OnCollectionChanged(nameof(OnAttrDictChanged))]
        [LabelText("Attr Dict With Callback")]
        public Dictionary<string, float> attrDictWithCallback = new Dictionary<string, float>
        {
            { "ATK", 10f },
            { "DEF", 5f },
        };

        private void OnTagsChanged()
        {
            Debug.Log($"[ExampleScriptableObject] Tags count: {tagsWithCallback.Count}");
        }

        private void OnAttrDictChanged()
        {
            Debug.Log($"[ExampleScriptableObject] AttrDict count: {attrDictWithCallback.Count}");
        }

        [Title("ReadOnly & DisableInEditorMode", true)]
        [ReadOnly]
        [LabelText("Item GUID")]
        public string itemGuid = "00000000-0000-0000-0000-000000000000";

        [DisableInEditorMode]
        [LabelText("Runtime Only Field")]
        public string runtimeOnlyField = "can't edit in editor mode";

        [Title("PropertyRange Dynamic", true)]
        public float minRange = 0f;
        public float maxRange = 100f;

        [PropertyRange(nameof(minRange), nameof(maxRange))]
        [LabelText("Dynamic Slider")]
        public float dynamicValue = 50f;

        [Title("Multi-Level Nesting", true)]
        [FoldoutGroup("Deep")]
        [BoxGroup("Deep/L1")]
        [LabelText("Level 1")]
        public string deepL1 = "level1";

        [BoxGroup("Deep/L1/L2")]
        [LabelText("Level 2")]
        public string deepL2 = "level2";

        [FoldoutGroup("Deep")]
        [BoxGroup("Deep/L1")]
        [LabelText("Level 1 Float")]
        public float deepL1Float = 3.14f;

        [Title("Cross-Class Static", true)]
        [ShowIf("@" + nameof(StaticHelper) + "." + nameof(StaticHelper.IsEnabled))]
        [LabelText("Visible When Enabled")]
        public string visibleWhenEnabled = "visible";

        [EnableIf("@" + nameof(StaticHelper) + "." + nameof(StaticHelper.IsEnabled))]
        [LabelText("Enabled By Static")]
        public float enabledByStatic = 1.0f;

        protected override void OnAfterDeserialize()
        {
            // Called after Inspector applies modifications
        }

        public static class StaticHelper
        {
            public static bool IsEnabled = true;
        }
    }

    [Serializable]
    public class ItemTableEntry
    {
        [LabelText("Name")]
        public string name;

        [LabelText("Count")]
        public int count;

        [LabelText("Weight")]
        public float weight;
    }

    [Serializable]
    public abstract class ItemEffect
    {
        [LabelText("Effect Name")]
        public string effectName = "";

        [LabelText("Duration")]
        public float duration = 1f;
    }

    [Serializable]
    public class DamageEffect : ItemEffect
    {
        [LabelText("Damage Amount")]
        public float damage = 20f;

        [LabelText("Damage Type")]
        public ExampleScriptableObject.ItemRarity damageType = ExampleScriptableObject.ItemRarity.Common;
    }

    [Serializable]
    public class HealEffect : ItemEffect
    {
        [LabelText("Heal Amount")]
        public float healAmount = 50f;
    }

    [Serializable]
    public class BuffEffect : ItemEffect
    {
        [LabelText("Buff Stat")]
        public string buffStat = "ATK";

        [LabelText("Buff Value")]
        public float buffValue = 10f;
    }
}
