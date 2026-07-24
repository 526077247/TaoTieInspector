using System;
using System.Collections.Generic;
using System.Reflection;

namespace TaoTie.Inspector.Editor
{
    public interface ISort
    {
        public float MinSort { get; }
    }
    public class GroupItem:ISort
    {
        public float MinSort { get; set; }
        public string GroupId;
        public string GroupKey; // "Fold:Name", "Box:Name", "Tab:Group/Tab"
        public List<MemberItem> Members = new ();
    }

    public class MemberItem:ISort
    {
        public float MinSort{ get; set; }
        public MemberInfo Member;
        public Attribute[] cachedAttributes;
    }

}