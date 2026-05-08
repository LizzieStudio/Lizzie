using System;
using System.Collections.Generic;
using Godot;

public class SpriteSheetBuilder
{
    private readonly TextureFactory _factory;
    private readonly List<TextureFactory.TextureDefinition> _defs;
    private readonly int _hframes;
    private readonly int _vframes;
    private readonly int _cellW;
    private readonly int _cellH;
    private readonly Action<Texture2D> _onComplete;
    private Image _sheet;
    private int _nextIndex;

    public SpriteSheetBuilder(
        TextureFactory factory,
        List<TextureFactory.TextureDefinition> defs,
        int cellW,
        int cellH,
        int hframes,
        int vframes,
        Action<Texture2D> onComplete
    )
    {
        _factory = factory;
        _defs = defs;
        _cellW = cellW;
        _cellH = cellH;
        _hframes = Math.Max(hframes, 1);
        _vframes = Math.Max(vframes, 1);
        _onComplete = onComplete;
    }

    public void Start()
    {
        _sheet = Image.CreateEmpty(_cellW * _hframes, _cellH * _vframes, false, Image.Format.Rgba8);
        QueueNext();
    }

    private void QueueNext()
    {
        if (_nextIndex >= _defs.Count)
        {
            Finish();
            return;
        }

        int idx = _nextIndex++;
        _factory.GenerateTexture(_defs[idx], tex => OnCellReady(idx, tex));
    }

    private void OnCellReady(int index, ImageTexture tex)
    {
        if (tex != null)
        {
            var cellImg = tex.GetImage();
            if (cellImg != null)
            {
                int col = index % _hframes;
                int row = index / _hframes;
                _sheet.BlitRect(
                    cellImg,
                    new Rect2I(0, 0, cellImg.GetWidth(), cellImg.GetHeight()),
                    new Vector2I(col * _cellW, row * _cellH)
                );
            }
        }

        QueueNext();
    }

    private void Finish()
    {
        _sheet.GenerateMipmaps();
        _sheet.Compress(Image.CompressMode.S3Tc, Image.CompressSource.Generic);

        var tex = ImageTexture.CreateFromImage(_sheet);
        _onComplete?.Invoke(tex);
    }
}
