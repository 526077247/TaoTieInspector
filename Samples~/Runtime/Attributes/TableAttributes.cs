using System;
using UnityEngine;

namespace TaoTie.Inspector
{
    /// <summary>
    /// Axis of a table matrix.
    /// </summary>
    public enum TableAxis
    {
        X,
        Y
    }

    /// <summary>
    /// Direction in which a label is drawn.
    /// </summary>
    public enum LabelDirection
    {
        LeftToRight,
        TopToBottom
    }

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

        /// <summary>
        /// Method name for custom cell drawing. Signature: (Rect, T) => T
        /// </summary>
        public string DrawElementMethod;

        /// <summary>
        /// Method name for dynamic row/column labels. Signature: (T[,], TableAxis, int) => (string, LabelDirection)
        /// </summary>
        public string Labels;

        /// <summary>
        /// Title shown above the horizontal axis (column headers).
        /// </summary>
        public string HorizontalTitle;

        /// <summary>
        /// Title shown to the left of the vertical axis (row headers).
        /// </summary>
        public string VerticalTitle;
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
