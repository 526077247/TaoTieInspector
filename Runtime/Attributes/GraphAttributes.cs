using System;
using UnityEngine;

namespace TaoTie.Inspector
{
    #region Graph-specific Enums

    public enum Ignore
    {
        All,
        Details,
        NodeView,
    }

    #endregion

    #region Graph Node Attributes

    /// <summary>
    /// Ignore field drawing in Graph NodeView and/or details panel.
    /// All = ignore everywhere, Details = ignore in details panel, NodeView = ignore in node view.
    /// Treated as HideInInspector in normal Inspector.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class DrawIgnoreAttribute : PropertyAttribute
    {
        public Ignore Ignore;

        public DrawIgnoreAttribute()
        {
            Ignore = Ignore.All;
        }

        public DrawIgnoreAttribute(Ignore type)
        {
            Ignore = type;
        }
    }

    /// <summary>
    /// Specifies the NodeView type for a Node class.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class NodeViewTypeAttribute : PropertyAttribute
    {
        public Type ViewType;

        public NodeViewTypeAttribute(Type baseViewNode)
        {
            ViewType = baseViewNode;
        }
    }

    /// <summary>
    /// For ScriptableObject fields — indicates this is not an asset (hides asset picker in ObjectField).
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class NotAssetsAttribute : PropertyAttribute
    {
    }

    /// <summary>
    /// Marks a Port type's connection group for filtering connectable ports.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class PortGroupAttribute : PropertyAttribute
    {
        public int Group;

        public PortGroupAttribute(int group)
        {
            Group = group;
        }
    }

    #endregion

    #region Graph DrawBase Attributes (also usable in normal Inspector)

    /// <summary>
    /// Calls the specified method when the field value changes (checked every frame).
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class OnStateUpdateAttribute : PropertyAttribute
    {
        public string Action;

        public OnStateUpdateAttribute(string action) => this.Action = action;
    }

    /// <summary>
    /// Calls the specified method after collection contents change (add/remove elements).
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class OnCollectionChangedAttribute : PropertyAttribute
    {
        public string After;

        public OnCollectionChangedAttribute(string after) => this.After = after;
    }

    /// <summary>
    /// Filters the type selection range for SerializeReference fields.
    /// Parameter is the name of a method/property returning IEnumerable&lt;Type&gt;.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class TypeFilterAttribute : PropertyAttribute
    {
        public string FilterGetter;

        public TypeFilterAttribute(string filterGetter) => this.FilterGetter = filterGetter;
    }

    /// <summary>
    /// Hides the object picker for SerializeReference fields.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class HideReferenceObjectPickerAttribute : PropertyAttribute
    {
    }

    /// <summary>
    /// Constrains the minimum value of a numeric field.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class MinValueAttribute : PropertyAttribute
    {
        public double MinValue;

        public MinValueAttribute(double minValue) => this.MinValue = minValue;
    }

    /// <summary>
    /// Constrains the maximum value of a numeric field.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class MaxValueAttribute : PropertyAttribute
    {
        public double MaxValue;

        public MaxValueAttribute(double maxValue) => this.MaxValue = maxValue;
    }

    /// <summary>
    /// Disables editing of the field in edit mode.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class DisableInEditorModeAttribute : PropertyAttribute
    {
    }

    #endregion
}
