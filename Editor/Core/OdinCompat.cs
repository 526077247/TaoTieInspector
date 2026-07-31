using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;

namespace TaoTie.Inspector.Editor
{
    /// <summary>
    /// Odin (Sirenix) compatibility layer — wraps Odin attributes into TaoTie attributes
    /// by matching type names, and converts Odin's ValueDropdownItem types.
    /// Works entirely via reflection so the package has no hard Odin dependency.
    /// </summary>
    public static class OdinCompat
    {
#if ODIN_INSPECTOR
        public const bool IsOdinAvailable = true;
#else
        private static bool? s_OdinAvailable;

        /// <summary>True if a Sirenix.OdinInspector assembly is loaded in the current domain.</summary>
        public static bool IsOdinAvailable
        {
            get
            {
                if (s_OdinAvailable.HasValue) return s_OdinAvailable.Value;
                s_OdinAvailable = AppDomain.CurrentDomain.GetAssemblies()
                    .Any(a => a.GetName().Name == "Sirenix.OdinInspector.Attributes");
                return s_OdinAvailable.Value;
            }
        }
#endif
        private static bool s_Initialized;

        /// <summary>Odin attribute type name → TaoTie attribute type</summary>
        private static readonly Dictionary<string, Type> s_AttrTypeMap = new();

        private static void EnsureInitialized()
        {
            if (s_Initialized) return;
            s_Initialized = true;

            // Build simple-type-name → Type map for all TaoTie attribute types.
            // Attributes are split across TaoTie.Inspector (runtime) and
            // TaoTie.Inspector.Editor assemblies — scan all loaded assemblies
            // whose name starts with "TaoTie.Inspector".
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var name = asm.GetName().Name;
                if (!name.StartsWith("TaoTie.Inspector")) continue;
                foreach (var type in asm.GetTypes())
                {
                    if (typeof(Attribute).IsAssignableFrom(type) && type.Name.EndsWith("Attribute"))
                        s_AttrTypeMap[type.Name] = type;
                }
            }
        }

        /// <summary>
        /// Inspect a member for Odin (Sirenix) attributes and wrap each into the
        /// corresponding TaoTie attribute instance. Returns an empty array when
        /// Odin is not installed or no Odin attributes are present.
        /// </summary>
        public static Attribute[] WrapOdinAttributes(MemberInfo member)
        {
            if (!IsOdinAvailable) return Array.Empty<Attribute>();
            EnsureInitialized();

            var odinAttrs = member.GetCustomAttributes(false);
            if (odinAttrs.Length == 0) return Array.Empty<Attribute>();

            var result = new List<Attribute>();
            foreach (var attr in odinAttrs)
            {
                var attrType = attr.GetType();
                if (attrType.Namespace == null || !attrType.Namespace.StartsWith("Sirenix"))
                    continue;

                if (s_AttrTypeMap.TryGetValue(attrType.Name, out var taoTieType))
                {
                    var wrapped = WrapAsTaoTie(attr, attrType, taoTieType);
                    if (wrapped != null)
                        result.Add(wrapped);
                }
            }
            return result.ToArray();
        }

        /// <summary>
        /// Create a TaoTie attribute instance from an Odin attribute by copying
        /// all matching members (properties and fields). Handles Odin field → TaoTie
        /// property mismatches. Uses FormatterServices to bypass constructors.
        /// </summary>
        private static Attribute WrapAsTaoTie(object odinAttr, Type odinType, Type taoTieType)
        {
            var taoTieAttr = (Attribute)FormatterServices.GetUninitializedObject(taoTieType);

            // Build unified writable-member map for TaoTie (properties + fields)
            var taoTieMembers = new Dictionary<string, (PropertyInfo prop, FieldInfo field)>();
            foreach (var tp in taoTieType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (tp.CanWrite)
                    taoTieMembers[tp.Name] = (tp, null);
            }
            foreach (var tf in taoTieType.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!taoTieMembers.ContainsKey(tf.Name))
                    taoTieMembers[tf.Name] = (null, tf);
            }

            var usedTargets = new HashSet<string>();

            // Copy from Odin properties → TaoTie properties/fields
            var odinProps = odinType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var odinProp in odinProps)
            {
                if (!odinProp.CanRead) continue;
                if (usedTargets.Contains(odinProp.Name)) continue;
                if (!taoTieMembers.TryGetValue(odinProp.Name, out var target)) continue;
                try
                {
                    var value = odinProp.GetValue(odinAttr);
                    if (target.prop != null) target.prop.SetValue(taoTieAttr, value);
                    else target.field.SetValue(taoTieAttr, value);
                    usedTargets.Add(odinProp.Name);
                }
                catch { }
            }

            // Copy from Odin fields → TaoTie properties/fields
            var odinFields = odinType.GetFields(BindingFlags.Public | BindingFlags.Instance);
            foreach (var odinField in odinFields)
            {
                if (usedTargets.Contains(odinField.Name)) continue;
                if (!taoTieMembers.TryGetValue(odinField.Name, out var target)) continue;
                try
                {
                    var value = odinField.GetValue(odinAttr);
                    if (target.prop != null) target.prop.SetValue(taoTieAttr, value);
                    else target.field.SetValue(taoTieAttr, value);
                    usedTargets.Add(odinField.Name);
                }
                catch { }
            }

            return taoTieAttr;
        }

        /// <summary>
        /// Try to convert an arbitrary object (possibly Odin's ValueDropdownItem or
        /// ValueDropdownItem&lt;T&gt;) into TaoTie's ValueDropdownItem.
        /// Handles both TaoTie's and Odin's types by checking the type name.
        /// </summary>
        public static bool TryConvertToValueDropdownItem(object item, out ValueDropdownItem result)
        {
            // TaoTie's non-generic ValueDropdownItem
            if (item is ValueDropdownItem vdi)
            {
                result = vdi;
                return true;
            }

            // IValueDropdownItem (TaoTie interface, may also be implemented by Odin types)
            if (item is IValueDropdownItem ivdi)
            {
                result = new ValueDropdownItem(ivdi.GetText(), ivdi.GetValue());
                return true;
            }

            var type = item?.GetType();
            if (type == null)
            {
                result = default;
                return false;
            }

            // Generic ValueDropdownItem<T> — matches both TaoTie's and Odin's by type name
            if (type.IsGenericType && type.Name == "ValueDropdownItem`1")
            {
                var textField = type.GetField("Text");
                var valueField = type.GetField("Value");
                result = new ValueDropdownItem(
                    textField?.GetValue(item)?.ToString() ?? "",
                    valueField?.GetValue(item));
                return true;
            }

            // Odin's non-generic ValueDropdownItem struct (TaoTie's is already handled above)
            if (type.IsValueType && type.Name == "ValueDropdownItem")
            {
                var textField = type.GetField("Text");
                var valueField = type.GetField("Value");
                result = new ValueDropdownItem(
                    textField?.GetValue(item)?.ToString() ?? "",
                    valueField?.GetValue(item));
                return true;
            }

            result = default;
            return false;
        }
    }
}
