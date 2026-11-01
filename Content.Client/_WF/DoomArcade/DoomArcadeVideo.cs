using Content.Client._WF.DoomArcade.Engine;
using Content.Client._WF.DoomArcade.Engine.Video;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._WF.DoomArcade;

/// <summary>Runs the software renderer and turns its column-major output into row-major pixels.</summary>
public sealed class DoomArcadeVideo : IVideo
{
    private readonly Renderer _renderer;
    private readonly uint[] _columns;

    public readonly Rgba32[] Pixels;

    public int Width => _renderer.Width;
    public int Height => _renderer.Height;

    public DoomArcadeVideo(Config config, GameContent content)
    {
        _renderer = new Renderer(config, content);
        _columns = new uint[Width * Height];
        Pixels = new Rgba32[Width * Height];
    }

    public void Render(Doom doom, Fixed frameFrac)
    {
        _renderer.Render(doom, _columns, frameFrac);

        var width = Width;
        var height = Height;
        for (var x = 0; x < width; x++)
        {
            var column = x * height;
            for (var y = 0; y < height; y++)
            {
                Pixels[y * width + x] = new Rgba32(_columns[column + y]);
            }
        }
    }

    public void InitializeWipe() => _renderer.InitializeWipe();

    public bool HasFocus() => true;

    public int MaxWindowSize => _renderer.MaxWindowSize;

    public int WindowSize
    {
        get => _renderer.WindowSize;
        set => _renderer.WindowSize = value;
    }

    public bool DisplayMessage
    {
        get => _renderer.DisplayMessage;
        set => _renderer.DisplayMessage = value;
    }

    public int MaxGammaCorrectionLevel => _renderer.MaxGammaCorrectionLevel;

    public int GammaCorrectionLevel
    {
        get => _renderer.GammaCorrectionLevel;
        set => _renderer.GammaCorrectionLevel = value;
    }

    public int WipeBandCount => _renderer.WipeBandCount;
    public int WipeHeight => _renderer.WipeHeight;
}
