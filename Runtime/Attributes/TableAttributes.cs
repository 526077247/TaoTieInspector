using System;
using UnityEngine;

namespace TaoTie.Inspector
{
    /// <summary>
    /// Renders List/array as a table (one element per row, field names as column headers).
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
    public class TableListAttribute : PropertyAttribute
    {
        public bool DrawScrollView = true;
        public bool IsReadOnly;
        public int MinItemCount;
        public int MaxItemCount;
        public bool ShowItemCount = true;
        public bool AlwaysExpanded;
    }

    /// <summary>
    /// Renders a 2D array as a matrix table.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
    public class TableMatrixAttribute : PropertyAttribute
    {
        public bool DrawScrollView = true;
        public bool IsReadOnly;
        public bool Transpose;
        public bool SquareRows;
    }

    /// <summary>
    /// Marks a field as not nullable. Shows a red error box in the Inspector when null.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
    public class NotNullAttribute : PropertyAttribute
    {
        public string ErrorMessage;
        public NotNullAttribute() { }
        public NotNullAttribute(string message) { ErrorMessage = message; }
    }
}
