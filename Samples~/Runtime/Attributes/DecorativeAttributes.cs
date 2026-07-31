using System;
using UnityEngine;

namespace TaoTie.Inspector
{
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = true)]
    public class TitleAttribute : PropertyAttribute
    {
        public string Title { get; set; }
        public bool HorizontalLine { get; set; }
        public bool Indented { get; set; }
        public TitleAlignmentType TitleAlignment { get; set; }

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
        public string Message { get; set; }
        public InfoMessageType InfoMessageType { get; set; }
        public string VisibleIf { get; set; }

        public InfoBoxAttribute(string message,
            InfoMessageType messageType = InfoMessageType.Info,
            string visibleIf = null)
        {
            Message = message;
            InfoMessageType = messageType;
            VisibleIf = visibleIf;
        }
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = true)]
    public class PropertySpaceAttribute : PropertyAttribute
    {
        public int SpaceBefore { get; set; }
        public int SpaceAfter { get; set; }

        public PropertySpaceAttribute(int spaceBefore = 0, int spaceAfter = 0)
        {
            SpaceBefore = spaceBefore;
            SpaceAfter = spaceAfter;
        }
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = true)]
    public class PropertyRangeAttribute : PropertyAttribute
    {
        public double Min { get; set; }
        public double Max { get; set; }
        public string MinMember { get; set; }
        public string MaxMember { get; set; }

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
