using System;
using Godot;

public partial class PageControl : HBoxContainer
{
    private Button _firstItem;
    private Button _prevItem;
    private Button _nextItem;
    private Button _lastItem;
    private Label _curItemLabel;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        _firstItem = GetNode<Button>("%FirstItem");
        _firstItem.Pressed += () => SetPreviewItem(0);

        _prevItem = GetNode<Button>("%PrevItem");
        _prevItem.Pressed += () => ChangePreviewItem(-1);

        _curItemLabel = GetNode<Label>("%CurItemLabel");

        _nextItem = GetNode<Button>("%NextItem");
        _nextItem.Pressed += () => ChangePreviewItem(1);

        _lastItem = GetNode<Button>("%LastItem");
        _lastItem.Pressed += () => SetPreviewItem(int.MaxValue);
    }

    public void SetCurrentItem(int index)
    {
        _curItemLabel.Text = $"Item {index}";
    }

    public int GetCurrentItem()
    {
        return CurrentItem;
    }

    public void SetItemCount(int count)
    {
        ItemCount = count;
        UpdateMultiLabel();
    }

    private void ChangePreviewItem(int delta)
    {
        CurrentItem += delta;
        CurrentItem = Math.Clamp(CurrentItem, 0, ItemCount - 1);
        UpdateMultiLabel();
        ItemSelected?.Invoke(this, new ItemSelectedEventArgs(CurrentItem, _curItemLabel.Text));
    }

    private void UpdateMultiLabel()
    {
        _curItemLabel.Text = $"{CurrentItem + 1} of {ItemCount}";
    }

    private void SetPreviewItem(int item)
    {
        CurrentItem = Math.Clamp(item, 0, ItemCount - 1);
        UpdateMultiLabel();
        ItemSelected?.Invoke(this, new ItemSelectedEventArgs(CurrentItem, _curItemLabel.Text));
    }

    private int _itemCount = 1;

    public int ItemCount
    {
        get => _itemCount;
        set
        {
            if (value < 1)
            {
                _itemCount = 1;
            }
            else
            {
                _itemCount = value;
            }
            UpdateMultiLabel();
        }
    }

    public int CurrentItem { get; set; }

    public event EventHandler<ItemSelectedEventArgs> ItemSelected;
}
