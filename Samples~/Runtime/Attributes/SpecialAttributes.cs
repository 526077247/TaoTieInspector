using System;
using UnityEngine;

namespace TaoTie.Inspector
{
    [AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = true)]
    public class EnumToggleButtonsAttribute : PropertyAttribute
    {
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = true)]
    public class ValueDropdownAttribute : PropertyAttribute
    {
        public string MemberName { get; set; }
        public bool AppendNextDrawer { get; set; }

        public ValueDropdownAttribute(string memberName)
        {
            MemberName = memberName;
            AppendNextDrawer = false;
        }
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = true)]
    public class OnValueChangedAttribute : PropertyAttribute
    {
        public string MethodName { get; set; }

        public OnValueChangedAttribute(string methodName)
        {
            MethodName = methodName;
        }
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = true)]
    public class ButtonAttribute : PropertyAttribute
    {
        public string Name { get; set; }
        public ButtonSizes Size { get; set; }

        public ButtonAttribute(string name = null, ButtonSizes size = ButtonSizes.Medium)
        {
            Name = name;
            Size = size;
        }

        public ButtonAttribute(ButtonSizes size = ButtonSizes.Medium) : this(null, size)
        {
        }
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = true)]
    public class DrawWithUnityAttribute : PropertyAttribute
    {
    }
}
