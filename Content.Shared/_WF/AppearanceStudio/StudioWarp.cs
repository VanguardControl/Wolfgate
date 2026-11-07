using Content.Shared._WF.CustomMarkings;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Shared._WF.AppearanceStudio;

/// <summary>
/// A displacement map a player paints for a worn or held item: for every pixel of every facing, where in the item's
/// own frame to draw from, or nothing at all (cut). Stored as one-frame custom marking art: red is 128 plus the
/// x step, green 128 minus the y step (image y runs down, the shader's up), alpha 255 to draw and 0 to cut.
/// </summary>
public sealed class StudioWarp
{
    public const int Size = CustomMarkingRules.FrameSize;
    public const int Facings = CustomMarkingRules.Facings;
    public const int Neutral = 128;

    private const int Pixels = Facings * Size * Size;

    /// <summary>Steps in pixels, per facing, row by row.</summary>
    public readonly sbyte[] Dx = new sbyte[Pixels];

    public readonly sbyte[] Dy = new sbyte[Pixels];

    public readonly bool[] Cut = new bool[Pixels];

    public static int Index(int facing, int x, int y)
    {
        return (facing * Size + y) * Size + x;
    }

    public StudioWarp Clone()
    {
        var copy = new StudioWarp();
        Dx.CopyTo(copy.Dx, 0);
        Dy.CopyTo(copy.Dy, 0);
        Cut.CopyTo(copy.Cut, 0);
        return copy;
    }

    /// <summary>Sets one pixel's step, pulled back so the pixel it draws from is inside the frame.</summary>
    public void Set(int facing, int x, int y, int dx, int dy)
    {
        var i = Index(facing, x, y);
        Dx[i] = (sbyte) (Math.Clamp(x + dx, 0, Size - 1) - x);
        Dy[i] = (sbyte) (Math.Clamp(y + dy, 0, Size - 1) - y);
    }

    /// <summary>Whether this warp leaves every pixel where it is.</summary>
    public bool IsNeutral()
    {
        for (var i = 0; i < Pixels; i++)
        {
            if (Dx[i] != 0 || Dy[i] != 0 || Cut[i])
                return false;
        }

        return true;
    }

    public void ClearFacing(int facing)
    {
        var start = Index(facing, 0, 0);
        Array.Clear(Dx, start, Size * Size);
        Array.Clear(Dy, start, Size * Size);
        Array.Clear(Cut, start, Size * Size);
    }

    /// <summary>Copies one facing onto another, mirrored left to right when asked (east to west).</summary>
    public void CopyFacing(int from, int to, bool mirror)
    {
        if (from == to)
            return;

        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var source = Index(from, mirror ? Size - 1 - x : x, y);
                var target = Index(to, x, y);
                Dx[target] = (sbyte) (mirror ? -Dx[source] : Dx[source]);
                Dy[target] = Dy[source];
                Cut[target] = Cut[source];
            }
        }
    }

    public static Rgba32 Encode(int dx, int dy, bool cut)
    {
        return cut ? default : new Rgba32((byte) (Neutral + dx), (byte) (Neutral - dy), 0, 255);
    }

    public CustomMarkingArt ToArt()
    {
        var art = new CustomMarkingArt();
        for (var facing = 0; facing < Facings; facing++)
        {
            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    var i = Index(facing, x, y);
                    art.SetPixel(0, facing, x, y, Encode(Dx[i], Dy[i], Cut[i]));
                }
            }
        }

        return art;
    }

    /// <summary>The warp a piece of art holds, or null when it isn't a well formed warp (see <see cref="IsValid"/>).</summary>
    public static StudioWarp? FromArt(CustomMarkingArt art)
    {
        if (!IsValid(art))
            return null;

        var warp = new StudioWarp();
        for (var facing = 0; facing < Facings; facing++)
        {
            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    var pixel = art.GetPixel(0, facing, x, y);
                    var i = Index(facing, x, y);
                    if (pixel.A == 0)
                    {
                        warp.Cut[i] = true;
                        continue;
                    }

                    warp.Dx[i] = (sbyte) (pixel.R - Neutral);
                    warp.Dy[i] = (sbyte) (Neutral - pixel.G);
                }
            }
        }

        return warp;
    }

    /// <summary>
    /// Whether art is a warp: one frame, no erase mask, every pixel either cut or fully opaque with no blue, and
    /// drawing from inside its own frame. A step out of the frame would sample whatever sprite sits beside the item
    /// in the texture atlas.
    /// </summary>
    public static bool IsValid(CustomMarkingArt art)
    {
        if (art.Frames != 1 || art.HasErase())
            return false;

        for (var facing = 0; facing < Facings; facing++)
        {
            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    var pixel = art.GetPixel(0, facing, x, y);
                    if (pixel.A == 0)
                        continue;

                    if (pixel.A != 255 || pixel.B != 0)
                        return false;

                    if (!CustomMarkingArt.InFrame(x + pixel.R - Neutral, y + Neutral - pixel.G))
                        return false;
                }
            }
        }

        return true;
    }
}
