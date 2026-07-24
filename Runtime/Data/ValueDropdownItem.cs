namespace TaoTie.Inspector
{
    /// <summary>
    /// Interface for value dropdown items
    /// </summary>
    public interface IValueDropdownItem
    {
        /// <summary>Gets the label for the dropdown item.</summary>
        string GetText();

        /// <summary>Gets the value of the dropdown item.</summary>
        object GetValue();
    }
}
