namespace ServiceLib.Models.Dto;

/// <summary>
/// A subscription as shown in the sidebar: the stored item plus how many servers it holds.
/// </summary>
public partial class SubItemModel : ReactiveObject
{
    public SubItemModel(SubItem item)
    {
        Item = item;
    }

    public SubItem Item { get; }

    public string Id => Item.Id;

    public string Remarks => Item.Remarks;

    [Reactive]
    public partial int Count { get; set; }
}
