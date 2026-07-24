using System;
using UnityEngine;

namespace TaoTie.Inspector
{
    [AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = true)]
    public class LabelTextAttribute : PropertyAttribute
    {
        public string Text { get; }

        public LabelTextAttribute(string text)
        {
            Text = text;
        }
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = true)]
    public class ShowIfAttribute : PropertyAttribute
    {
        public string Member { get; }
        public object Value { get; }
        public ConditionOperator Operator { get; }

        public ShowIfAttribute(string member)
        {
            Member = member;
            Value = null;
            Operator = ConditionOperator.And;
        }

        public ShowIfAttribute(string member, object value)
        {
            Member = member;
            Value = value;
            Operator = ConditionOperator.And;
        }
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = true)]
    public class HideIfAttribute : PropertyAttribute
    {
        public string Member { get; }
        public object Value { get; }
        public ConditionOperator Operator { get; }

        public HideIfAttribute(string member)
        {
            Member = member;
            Value = null;
            Operator = ConditionOperator.And;
        }

        public HideIfAttribute(string member, object value)
        {
            Member = member;
            Value = value;
            Operator = ConditionOperator.And;
        }
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = true)]
    public class EnableIfAttribute : PropertyAttribute
    {
        public string Member { get; }
        public object Value { get; }
        public ConditionOperator Operator { get; }

        public EnableIfAttribute(string member)
        {
            Member = member;
            Value = null;
            Operator = ConditionOperator.And;
        }

        public EnableIfAttribute(string member, object value)
        {
            Member = member;
            Value = value;
            Operator = ConditionOperator.And;
        }
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = true)]
    public class DisableIfAttribute : PropertyAttribute
    {
        public string Member { get; }
        public object Value { get; }
        public ConditionOperator Operator { get; }

        public DisableIfAttribute(string member)
        {
            Member = member;
            Value = null;
            Operator = ConditionOperator.And;
        }

        public DisableIfAttribute(string member, object value)
        {
            Member = member;
            Value = value;
            Operator = ConditionOperator.And;
        }
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = true)]
    public class ReadOnlyAttribute : PropertyAttribute
    {
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = true)]
    public class PropertyOrderAttribute : PropertyAttribute
    {
        public int Order { get; }

        public PropertyOrderAttribute(int order)
        {
            Order = order;
        }
    }
}
