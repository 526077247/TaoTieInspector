using System;
using System.Collections.Generic;
using UnityEngine;
using TaoTie.Inspector;

namespace TaoTie.Inspector
{
    [Serializable]
    public class TaoTieInspectorObject
    {
        public enum DamageType
        {
            Physical,
            Fire,
            Ice,
            Lightning,
            Poison
        }

        [Title("Basic Attributes", true)]
        [LabelText("Character Name")]
        public string characterName = "Hero";

        [LabelText("Level")]
        [PropertyRange(1, 99)]
        public int level = 1;

        [LabelText("Max Health")]
        [PropertySpace(10, 10)]
        public float maxHealth = 100f;

        [Title("Combat Attributes", true)]
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

        [BoxGroup("Combat/Stats")]
        [ShowIf("showAdvanced")]
        [LabelText("Critical Damage")]
        public float criticalDamage = 1.5f;

        [FoldoutGroup("Combat")]
        [LabelText("Show Advanced Attributes")]
        public bool showAdvanced = false;

        [FoldoutGroup("Combat")]
        [EnumToggleButtons]
        [LabelText("Damage Type")]
        public DamageType damageType = DamageType.Physical;

        [TabGroup("Settings", "Visual")]
        [LabelText("Character Color")]
        public Color characterColor = Color.white;

        [TabGroup("Settings", "Audio")]
        [LabelText("Volume")]
        [PropertyRange(0, 1)]
        public float volume = 0.8f;

        [TabGroup("Settings", "Movement")]
        [LabelText("Move Speed")]
        public float moveSpeed = 5f;

        [TabGroup("Settings", "Movement")]
        [LabelText("Jump Height")]
        public float jumpHeight = 3f;

        [PropertyOrder(-1)]
        [Title("Configuration Info", true)]
        [InfoBox("This is a test MonoBehaviour used to validate various attribute features of TaoTie Inspector.", InfoMessageType.Info)]
        [ReadOnly]
        [LabelText("Config ID")]
        public string configId = "TT_001";

        [DisableIf("level")]
        [LabelText("Disable Test")]
        public string disabledTest = "Disabled when level > 0";

        [EnableIf("showAdvanced")]
        [LabelText("Enable Test")]
        public string enabledTest = "Enabled when showAdvanced is true";

        [Title("Expression Condition Test", true)]
        public bool IsGlobal = false;
        public bool EnableVision = true;
        public bool ViewPanoramic = false;

        [ShowIf("@!" + nameof(IsGlobal))]
        [LabelText("Show When Not Global")]
        public string showWhenNotGlobal = "Visible when IsGlobal=false";

        [ShowIf("@" + nameof(EnableVision) + "&&!" + nameof(ViewPanoramic))]
        [LabelText("Show When Vision And Not Panoramic")]
        public string showWhenVisionAndNotPanoramic = "Visible when EnableVision && !ViewPanoramic";

        [ShowIf("@(" + nameof(IsGlobal) + "||" + nameof(EnableVision) + ")&&!" + nameof(ViewPanoramic))]
        [LabelText("Complex Expression")]
        public string showWhenComplexExpr = "Visible when (IsGlobal||EnableVision)&&!ViewPanoramic";

        [HideIf("@" + nameof(IsGlobal) + "&&" + nameof(ViewPanoramic))]
        [LabelText("Hide When Global And Panoramic")]
        public string hideWhenGlobalAndPanoramic = "Hidden when IsGlobal && ViewPanoramic";

        [Title("OnValueChanged Test", true)]
        [OnValueChanged(nameof(OnNameChanged))]
        public string watchedName = "initial";

        private void OnNameChanged()
        {
            Debug.Log($"[OnValueChanged] name changed to: {watchedName}");
        }
    }
}
