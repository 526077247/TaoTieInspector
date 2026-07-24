using System;
using System.Collections.Generic;

namespace TaoTie.Inspector
{
    /// <summary>
    /// Interface for value dropdown items.
    /// Implement this on custom types to provide dropdown text/value without using ValueDropdownItem structs.
    /// </summary>
    public interface IValueDropdownItem
    {
        /// <summary>Gets the label for the dropdown item.</summary>
        string GetText();

        /// <summary>Gets the value of the dropdown item.</summary>
        object GetValue();
    }

    /// <summary>
    /// Non-generic dropdown item used internally by the drawing system.
    /// Implements IValueDropdownItem for uniform handling.
    /// </summary>
    [Serializable]
    public struct ValueDropdownItem : IValueDropdownItem
    {
        public string Text;
        public object Value;

        public ValueDropdownItem(string text, object value)
        {
            Text = text;
            Value = value;
        }

        public string GetText() => Text;
        public object GetValue() => Value;
    }

    /// <summary>
    /// Generic typed dropdown item (Odin-compatible API).
    /// Implements IValueDropdownItem for uniform handling.
    /// </summary>
    [Serializable]
    public struct ValueDropdownItem<T> : IValueDropdownItem
    {
        public string Text;
        public T Value;

        public ValueDropdownItem(string text, T value)
        {
            Text = text;
            Value = value;
        }

        public string GetText() => Text;
        public object GetValue() => Value;

        public static implicit operator ValueDropdownItem(ValueDropdownItem<T> item)
            => new ValueDropdownItem(item.Text, item.Value);
    }

    /// <summary>
    /// Convenience list for populating ValueDropdown items (Odin-compatible API).
    /// </summary>
    public class ValueDropdownList<T> : List<ValueDropdownItem<T>>
    {
        public ValueDropdownList() { }

        public ValueDropdownList(int capacity) : base(capacity) { }

        /// <summary> Add a text-value pair. </summary>
        public void Add(string text, T value)
        {
            Add(new ValueDropdownItem<T>(text, value));
        }

        /// <summary> Add a value directly — uses ToString() as the display text. </summary>
        public void Add(T value)
        {
            Add(new ValueDropdownItem<T>(value?.ToString() ?? "null", value));
        }

        /// <summary> Implicit conversion to non-generic ValueDropdownItem list. </summary>
        public static implicit operator List<ValueDropdownItem>(ValueDropdownList<T> list)
        {
            var result = new List<ValueDropdownItem>(list.Count);
            foreach (var item in list)
                result.Add(item);
            return result;
        }
    }
}
