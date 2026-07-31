using System;
using UnityEngine;

namespace TaoTie.Inspector
{
    [AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = true)]
    public class LabelTextAttribute : PropertyAttribute
    {
        public string Text { get; set; }

        public LabelTextAttribute(string text)
        {
            Text = text;
        }
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = true)]
    public class ShowIfAttribute : PropertyAttribute
    {
        public string Condition { get; set; }
        public object Value { get; set; }
        public ConditionOperator Operator { get; set; }

        public ShowIfAttribute(string condition)
        {
            Condition = condition;
            Value = null;
            Operator = ConditionOperator.And;
        }

        public ShowIfAttribute(string condition, object value)
        {
            Condition = condition;
            Value = value;
            Operator = ConditionOperator.And;
        }
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = true)]
    public class HideIfAttribute : PropertyAttribute
    {
        public string Condition { get; set; }
        public object Value { get; set; }
        public ConditionOperator Operator { get; set; }

        public HideIfAttribute(string condition)
        {
            Condition = condition;
            Value = null;
            Operator = ConditionOperator.And;
        }

        public HideIfAttribute(string condition, object value)
        {
            Condition = condition;
            Value = value;
            Operator = ConditionOperator.And;
        }
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = true)]
    public class EnableIfAttribute : PropertyAttribute
    {
        public string Condition { get; set; }
        public object Value { get; set; }
        public ConditionOperator Operator { get; set; }

        public EnableIfAttribute(string condition)
        {
            Condition = condition;
            Value = null;
            Operator = ConditionOperator.And;
        }

        public EnableIfAttribute(string condition, object value)
        {
            Condition = condition;
            Value = value;
            Operator = ConditionOperator.And;
        }
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = true)]
    public class DisableIfAttribute : PropertyAttribute
    {
        public string Condition { get; set; }
        public object Value { get; set; }
        public ConditionOperator Operator { get; set; }

        public DisableIfAttribute(string condition)
        {
            Condition = condition;
            Value = null;
            Operator = ConditionOperator.And;
        }

        public DisableIfAttribute(string condition, object value)
        {
            Condition = condition;
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
        public int Order { get; set; }

        public PropertyOrderAttribute(int order)
        {
            Order = order;
        }
    }
}
