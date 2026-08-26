namespace TaoTie.Inspector.Editor
{
    /// <summary>
    /// Unified group entry — both Inspector (SerializedProperty) and Graph (Reflection)
    /// convert their entries to this type, then call TaoTieGroupManager.DrawGroupedEntries.
    /// </summary>
    public class GroupEntryData
    {
        public bool Visible = true;

        // [Serializable] nested object foldout container
        public bool IsFoldoutContainer;
        public string ContainerName;
        public string ContainerPath;
        public bool ContainerExpanded;

        // Group attributes (priority: BoxGroup > FoldoutGroup > TabGroup > HorizontalGroup)
        public string BoxGroupName;
        public string FoldoutGroupName;
        public string TabGroupName;
        public string TabName;
        public string HorizontalGroupName;

        // TableList: this entry is a table-rendered array; its children should be skipped
        public bool IsTableList;
        public string TableListPath;

        // Original entry data (TaoTiePropertyEntry or MemberItem) — passed back in draw callback
        public object UserData;

        // Sort weight used to interleave grouped and ungrouped entries.
        // Reflection path sets this from MemberItem.MinSort; SerializedProperty path leaves it 0
        // (entries keep their natural SerializedProperty order).
        public float SortOrder;

        /// <summary>Returns the group path for tree building, or null if ungrouped.</summary>
        public string GetGroupPath()
        {
            if (BoxGroupName != null) return BoxGroupName;
            if (FoldoutGroupName != null) return FoldoutGroupName;
            if (TabGroupName != null) return TabGroupName + "/" + TabName;
            if (HorizontalGroupName != null) return HorizontalGroupName;
            return null;
        }
    }
}
