using System;
using UnityEngine;

namespace TaoTie.Inspector
{
    [AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = true)]
    public class FoldoutGroupAttribute : PropertyAttribute
    {
        public string GroupName { get; set; }
        public bool Expanded { get; set; }

        public FoldoutGroupAttribute(string groupName)
        {
            GroupName = groupName;
            Expanded = true;
        }
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = true)]
    public class BoxGroupAttribute : PropertyAttribute
    {
        public string GroupName { get; set; }
        public bool ShowLabel { get; set; }

        public BoxGroupAttribute(string groupName)
        {
            GroupName = groupName;
            ShowLabel = true;
        }
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = true)]
    public class TabGroupAttribute : PropertyAttribute
    {
        public string GroupName { get; set; }
        public string TabName { get; set; }

        public TabGroupAttribute(string tabName)
        {
            GroupName = "_DefaultTabGroup";
            TabName = tabName;
        }

        public TabGroupAttribute(string groupName, string tabName)
        {
            GroupName = groupName;
            TabName = tabName;
        }
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = true)]
    public class HorizontalGroupAttribute : PropertyAttribute
    {
        public string GroupName { get; set; }
        public float LabelWidth { get; set; }
        public float MarginLeft { get; set; }
        public float MarginRight { get; set; }

        public HorizontalGroupAttribute(string groupName)
        {
            GroupName = groupName;
            LabelWidth = -1;
            MarginLeft = 0;
            MarginRight = 0;
        }
    }
}
