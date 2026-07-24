using System;
using UnityEngine;

namespace TaoTie.Inspector
{
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = true)]
    public class TitleAttribute : PropertyAttribute
    {
        public string Title { get; }
        public bool HorizontalLine { get; }
        public bool Indented { get; }
        public TitleAlignmentType TitleAlignment { get; }

        public TitleAttribute(string title, bool horizontalLine = true, bool indented = false,
            TitleAlignmentType titleAlignment = TitleAlignmentType.Left)
        {
            Title = title;
            HorizontalLine = horizontalLine;
            Indented = indented;
            TitleAlignment = titleAlignment;
        }
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = true)]
    public class InfoBoxAttribute : PropertyAttribute
    {
        public string Message { get; }
        public InfoMessageType MessageType { get; }
        public string VisibleIf { get; }

        public InfoBoxAttribute(string message,
            InfoMessageType messageType = InfoMessageType.Info,
            string visibleIf = null)
        {
            Message = message;
            MessageType = messageType;
            VisibleIf = visibleIf;
        }
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = true)]
    public class PropertySpaceAttribute : PropertyAttribute
    {
        public int SpaceBefore { get; }
        public int SpaceAfter { get; }

        public PropertySpaceAttribute(int spaceBefore = 0, int spaceAfter = 0)
        {
            SpaceBefore = spaceBefore;
            SpaceAfter = spaceAfter;
        }
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = true)]
    public class PropertyRangeAttribute : PropertyAttribute
    {
        public double Min { get; }
        public double Max { get; }
        public string MinMember { get; }
        public string MaxMember { get; }

        public PropertyRangeAttribute(double min, double max)
        {
            Min = min;
            Max = max;
            MinMember = null;
            MaxMember = null;
        }

        public PropertyRangeAttribute(string minMember, string maxMember)
        {
            MinMember = minMember;
            MaxMember = maxMember;
            Min = 0;
            Max = 0;
        }
    }
}
