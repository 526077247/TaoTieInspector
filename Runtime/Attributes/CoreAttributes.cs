using System;
using UnityEngine;

namespace TaoTie.Inspector
{
    [AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = true)]
    public class LabelTextAttribute : PropertyAttribute
    {
        public string Label { get; }

        public LabelTextAttribute(string label)
        {
            Label = label;
        }
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = true)]
    public class ShowIfAttribute : PropertyAttribute
    {
        public string[] Members { get; }
        public object Value { get; }
        public ConditionOperator Operator { get; }

        public ShowIfAttribute(string member)
        {
            Members = new[] { member };
            Value = null;
            Operator = ConditionOperator.And;
        }

        public ShowIfAttribute(string member, object value)
        {
            Members = new[] { member };
            Value = value;
            Operator = ConditionOperator.And;
        }

        public ShowIfAttribute(ConditionOperator conditionOperator, params string[] members)
        {
            Members = members;
            Value = null;
            Operator = conditionOperator;
        }
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = true)]
    public class HideIfAttribute : PropertyAttribute
    {
        public string[] Members { get; }
        public object Value { get; }
        public ConditionOperator Operator { get; }

        public HideIfAttribute(string member)
        {
            Members = new[] { member };
            Value = null;
            Operator = ConditionOperator.And;
        }

        public HideIfAttribute(string member, object value)
        {
            Members = new[] { member };
            Value = value;
            Operator = ConditionOperator.And;
        }

        public HideIfAttribute(ConditionOperator conditionOperator, params string[] members)
        {
            Members = members;
            Value = null;
            Operator = conditionOperator;
        }
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = true)]
    public class EnableIfAttribute : PropertyAttribute
    {
        public string[] Members { get; }
        public object Value { get; }
        public ConditionOperator Operator { get; }

        public EnableIfAttribute(string member)
        {
            Members = new[] { member };
            Value = null;
            Operator = ConditionOperator.And;
        }

        public EnableIfAttribute(string member, object value)
        {
            Members = new[] { member };
            Value = value;
            Operator = ConditionOperator.And;
        }

        public EnableIfAttribute(ConditionOperator conditionOperator, params string[] members)
        {
            Members = members;
            Value = null;
            Operator = conditionOperator;
        }
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = true)]
    public class DisableIfAttribute : PropertyAttribute
    {
        public string[] Members { get; }
        public object Value { get; }
        public ConditionOperator Operator { get; }

        public DisableIfAttribute(string member)
        {
            Members = new[] { member };
            Value = null;
            Operator = ConditionOperator.And;
        }

        public DisableIfAttribute(string member, object value)
        {
            Members = new[] { member };
            Value = value;
            Operator = ConditionOperator.And;
        }

        public DisableIfAttribute(ConditionOperator conditionOperator, params string[] members)
        {
            Members = members;
            Value = null;
            Operator = conditionOperator;
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
