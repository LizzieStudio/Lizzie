using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Lizzie.AssetManagement;

public partial class GridEntry : MarginContainer
{
    //Grid Tab elements
    private ImageSelector _gridFrontImageSelector;
    private ImageSelector _gridBackImageSelector;

    private LineEdit _gridRowCount;
    private LineEdit _gridColCount;
    private LineEdit _gridCardCount;

    private CheckButton _gridSingleBack;

    private int _gridRows;
    private int _gridCols;
    private int _gridCount;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        InitializeGridBindings();
        EventBus.Instance.Subscribe<ImageChangedEvent>(ImageChanged);
    }

    private void ImageChanged(ImageChangedEvent obj)
    {
        //reload image selectors
        var f = _gridFrontImageSelector.SelectedImage;
        var b = _gridBackImageSelector.SelectedImage;

        _gridFrontImageSelector.SetProject(ProjectService.Instance.CurrentProject);
        _gridBackImageSelector.SetProject(ProjectService.Instance.CurrentProject);

        //try to re-select the items
        _gridFrontImageSelector.SelectedImage = f;
        _gridBackImageSelector.SelectedImage = b;

        UpdatePreview();
    }

    private void InitializeGridBindings()
    {
        _gridFrontImageSelector = GetNode<ImageSelector>("%FrontImageSelector");
        _gridFrontImageSelector.ImageSelected += FrontImageSelected;
        _gridFrontImageSelector.SetProject(ProjectService.Instance.CurrentProject);

        _gridBackImageSelector = GetNode<ImageSelector>("%BackImageSelector");
        _gridBackImageSelector.ImageSelected += BackImageSelected;
        _gridBackImageSelector.SetProject(ProjectService.Instance.CurrentProject);

        _gridRowCount = GetNode<LineEdit>("%GridRows");
        _gridRowCount.TextChanged += t => GenerateGridCards();
        _gridColCount = GetNode<LineEdit>("%GridCols");
        _gridColCount.TextChanged += t => GenerateGridCards();
        _gridCardCount = GetNode<LineEdit>("%GridCardCount");
        _gridCardCount.TextChanged += t => GenerateGridCards();

        _gridSingleBack = GetNode<CheckButton>("%GridSingleBack");
        _gridSingleBack.Pressed += GenerateGridCards;
    }

    private string _frontGridImage;
    private string _backGridImage;

    private async void FrontImageSelected(object sender, SelectedEventArgs<Asset> e)
    {
        if (e.SelectedItem == null)
        {
            _frontGridImage = string.Empty;
            /*
            _frontMasterSprite = new ImageTexture(); //maybe set to blank white?
            UpdatePreview();
            return;
            */
        }

        {
            var a = e.SelectedItem;
            _frontGridImage = a.AssetId.ToString();
        }

        //ProjectService.Instance.FetchImageAsync(a, UpdateFrontGridTexture);
        UpdatePreview();
    }

    private async void BackImageSelected(object sender, SelectedEventArgs<Asset> e)
    {
        if (e.SelectedItem == null)
        {
            _backGridImage = string.Empty;
            /*
            _backMasterSprite = new ImageTexture(); //maybe set to blank white?
            UpdatePreview();
            return;
            */
        }
        else
        {
            var a = e.SelectedItem;
            _backGridImage = a.AssetId.ToString();
        }
        UpdatePreview();

        //ProjectService.Instance.FetchImageAsync(a, UpdateBackGridTexture);
    }

    private void GenerateGridCards()
    {
        int.TryParse(_gridRowCount.Text, out _gridRows);
        int.TryParse(_gridColCount.Text, out _gridCols);
        int.TryParse(_gridCardCount.Text, out _gridCount);

        if (_gridCount == 0)
            _gridCount = _gridRows * _gridCols;

        _gridCount = Math.Min(_gridRows * _gridCols, _gridCount);

        UpdateCardCount();
        UpdatePreview();
    }

    public int CardCount => _gridCount;

    public void AddGridParameters(Dictionary<string, object> d)
    {
        d.Add("FrontGridImageKey", _frontGridImage);
        d.Add("BackGridImageKey", _backGridImage);
        d.Add("GridRows", _gridRows);
        d.Add("GridCols", _gridCols);
        d.Add("GridCount", _gridCount);

        d.Add("GridSingleBack", _gridSingleBack.ButtonPressed);
        d.Add("Mode", VcToken.TokenBuildMode.Grid);
        d.Add("DifferentBack", true);
    }

    public void UpdateGridControls(Dictionary<string, object> parameters)
    {
        _gridRowCount.Text = parameters.ContainsKey("GridRows")
            ? parameters["GridRows"].ToString()
            : "";
        _gridColCount.Text = parameters.ContainsKey("GridCols")
            ? parameters["GridCols"].ToString()
            : "";
        _gridCardCount.Text = parameters.ContainsKey("GridCount")
            ? parameters["GridCount"].ToString()
            : "";

        if (parameters.ContainsKey("FrontGridImageKey"))
        {
            string frontKey = parameters["FrontGridImageKey"].ToString();
            var asset = ProjectService.Instance.CurrentProject?.Images.Values.FirstOrDefault(a =>
                a.AssetId.ToString() == frontKey
            );
            _gridFrontImageSelector.SelectedImage = asset;
        }
        else
        {
            _gridFrontImageSelector.SelectedImage = null;
        }

        if (parameters.ContainsKey("BackGridImageKey"))
        {
            string backKey = parameters["BackGridImageKey"].ToString();
            var asset = ProjectService.Instance.CurrentProject?.Images.Values.FirstOrDefault(a =>
                a.AssetId.ToString() == backKey
            );
            _gridBackImageSelector.SelectedImage = asset;
        }
        else
        {
            _gridBackImageSelector.SelectedImage = null;
        }
    }

    public event EventHandler GridUpdated;

    private void UpdatePreview()
    {
        GridUpdated?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler CardCountUpdated;

    private void UpdateCardCount()
    {
        CardCountUpdated?.Invoke(this, EventArgs.Empty);
    }
}
