using System;

namespace TaoTie.Inspector
{
    public enum ConditionOperator
    {
        And,
        Or
    }

    public enum InfoMessageType
    {
        None,
        Info,
        Warning,
        Error
    }

    public enum TitleAlignmentType
    {
        Left,
        Center,
        Right
    }

    public enum ButtonSizes
    {
        Small,
        Medium,
        Large,
        Gigantic
    }

    [Serializable]
    public struct ValueDropdownItem
    {
        public string Text;
        public object Value;

        public ValueDropdownItem(string text, object value)
        {
            Text = text;
            Value = value;
        }
    }
}
