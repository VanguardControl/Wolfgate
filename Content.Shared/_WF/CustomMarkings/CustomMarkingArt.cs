using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Color = Robust.Shared.Maths.Color;
using static Content.Shared._WF.CustomMarkings.CustomMarkingRules;

namespace Content.Shared._WF.CustomMarkings;

/// <summary>
/// The pixels of a custom marking: four facings on one sheet, laid out the way an RSI state is, south and north on
/// the top row and east and west below.
/// </summary>
public sealed class CustomMarkingArt
{
    public const int South = 0;
    public const int North = 1;
    public const int East = 2;
    public const int West = 3;

    /// <summary>Straight-alpha RGBA, row by row. A fully transparent pixel is always all zero.</summary>
    public readonly byte[] Pixels;

    public CustomMarkingArt()
    {
        Pixels = new byte[PixelBytes];
    }

    private CustomMarkingArt(byte[] pixels)
    {
        Pixels = pixels;
    }

    /// <summary>Art from raw sheet bytes, or null when there are the wrong number of them. The bytes are copied.</summary>
    public static CustomMarkingArt? FromPixels(byte[]? rgba)
    {
        if (rgba is not { Length: PixelBytes })
            return null;

        var pixels = new byte[PixelBytes];
        for (var i = 0; i < PixelBytes; i += 4)
        {
            if (rgba[i + 3] == 0)
                continue;

            pixels[i] = rgba[i];
            pixels[i + 1] = rgba[i + 1];
            pixels[i + 2] = rgba[i + 2];
            pixels[i + 3] = rgba[i + 3];
        }

        return new CustomMarkingArt(pixels);
    }

    public CustomMarkingArt Clone()
    {
        return new CustomMarkingArt(Pixels.AsSpan().ToArray());
    }

    /// <summary>Replaces every pixel with those of a sheet taken from <see cref="Pixels"/> earlier.</summary>
    public void Restore(ReadOnlySpan<byte> pixels)
    {
        if (pixels.Length == PixelBytes)
            pixels.CopyTo(Pixels);
    }

    /// <summary>Whether nothing is drawn on any facing.</summary>
    public bool IsBlank()
    {
        for (var i = 3; i < PixelBytes; i += 4)
        {
            if (Pixels[i] != 0)
                return false;
        }

        return true;
    }

    public bool SamePixels(CustomMarkingArt other)
    {
        return Pixels.AsSpan().SequenceEqual(other.Pixels);
    }

    public static bool InFrame(int x, int y)
    {
        return x >= 0 && y >= 0 && x < FrameSize && y < FrameSize;
    }

    /// <summary>The nearest pixel value to a colour. Rounds, where the colour's own byte properties cut off.</summary>
    public static Rgba32 ToPixel(Color color)
    {
        return new Rgba32(Channel(color.R), Channel(color.G), Channel(color.B), Channel(color.A));

        static byte Channel(float value)
        {
            return (byte) Math.Clamp(MathF.Round(value * byte.MaxValue), byte.MinValue, byte.MaxValue);
        }
    }

    public static Color ToColor(Rgba32 pixel)
    {
        return new Color(pixel.R, pixel.G, pixel.B, pixel.A);
    }

    private static int Offset(int facing, int x, int y)
    {
        return ((facing / 2 * FrameSize + y) * SheetSize + facing % 2 * FrameSize + x) * 4;
    }

    public Rgba32 GetPixel(int facing, int x, int y)
    {
        var i = Offset(facing, x, y);
        return new Rgba32(Pixels[i], Pixels[i + 1], Pixels[i + 2], Pixels[i + 3]);
    }

    /// <summary>Sets one pixel. Returns whether it changed.</summary>
    public bool SetPixel(int facing, int x, int y, Rgba32 pixel)
    {
        if (pixel.A == 0)
            pixel = default;

        var i = Offset(facing, x, y);
        if (Pixels[i] == pixel.R && Pixels[i + 1] == pixel.G && Pixels[i + 2] == pixel.B && Pixels[i + 3] == pixel.A)
            return false;

        Pixels[i] = pixel.R;
        Pixels[i + 1] = pixel.G;
        Pixels[i + 2] = pixel.B;
        Pixels[i + 3] = pixel.A;
        return true;
    }

    public void Clear(int facing)
    {
        for (var y = 0; y < FrameSize; y++)
        {
            Pixels.AsSpan(Offset(facing, 0, y), FrameSize * 4).Clear();
        }
    }

    /// <summary>Copies one facing onto another, mirrored left to right when <paramref name="mirror"/> is set.</summary>
    public void CopyFacing(int from, int to, bool mirror)
    {
        var source = Clone();
        for (var y = 0; y < FrameSize; y++)
        {
            for (var x = 0; x < FrameSize; x++)
            {
                SetPixel(to, x, y, source.GetPixel(from, mirror ? FrameSize - 1 - x : x, y));
            }
        }
    }

    /// <summary>
    /// Flips a facing left to right about a vertical line, given as the sum of any two columns that swap places:
    /// 31 for the middle of the frame, 30 for column 15, where a body's middle is. Pixels flipped past an edge are
    /// lost.
    /// </summary>
    public void Flip(int facing, int axis)
    {
        var source = Clone();
        for (var y = 0; y < FrameSize; y++)
        {
            for (var x = 0; x < FrameSize; x++)
            {
                var from = axis - x;
                SetPixel(facing, x, y, InFrame(from, y) ? source.GetPixel(facing, from, y) : default);
            }
        }
    }

    /// <summary>Moves a facing's pixels; those pushed past an edge are lost.</summary>
    public void Shift(int facing, int dx, int dy)
    {
        var source = Clone();
        for (var y = 0; y < FrameSize; y++)
        {
            for (var x = 0; x < FrameSize; x++)
            {
                var (fromX, fromY) = (x - dx, y - dy);
                SetPixel(facing, x, y, InFrame(fromX, fromY) ? source.GetPixel(facing, fromX, fromY) : default);
            }
        }
    }

    /// <summary>Recolours the pixel at a point and every same-coloured pixel joined to it by an edge.</summary>
    public void Fill(int facing, int x, int y, Rgba32 pixel)
    {
        if (!InFrame(x, y))
            return;

        var target = GetPixel(facing, x, y);
        if (!SetPixel(facing, x, y, pixel))
            return;

        var open = new Stack<(int X, int Y)>();
        open.Push((x, y));
        while (open.TryPop(out var at))
        {
            Spread(at.X + 1, at.Y);
            Spread(at.X - 1, at.Y);
            Spread(at.X, at.Y + 1);
            Spread(at.X, at.Y - 1);
        }

        void Spread(int nextX, int nextY)
        {
            if (InFrame(nextX, nextY) && GetPixel(facing, nextX, nextY) == target && SetPixel(facing, nextX, nextY, pixel))
                open.Push((nextX, nextY));
        }
    }

    /// <summary>A copy without the pixels a test picks out by facing and position, such as those over a lost limb.</summary>
    public CustomMarkingArt Without(Func<int, int, int, bool> gone)
    {
        var copy = Clone();
        for (var facing = 0; facing < Facings; facing++)
        {
            for (var y = 0; y < FrameSize; y++)
            {
                for (var x = 0; x < FrameSize; x++)
                {
                    if (copy.Pixels[Offset(facing, x, y) + 3] != 0 && gone(facing, x, y))
                        copy.SetPixel(facing, x, y, default);
                }
            }
        }

        return copy;
    }

    /// <summary>
    /// A short name for exactly these pixels, for art the client derives from other art. Not a secure hash: the
    /// server's hash is what identifies art.
    /// </summary>
    public string Fingerprint()
    {
        // FNV-1a.
        var hash = 14695981039346656037UL;
        foreach (var value in Pixels)
        {
            hash = unchecked((hash ^ value) * 1099511628211UL);
        }

        return hash.ToString("x16");
    }

    public byte[] ToPng()
    {
        using var image = new Image<Rgba32>(SheetSize, SheetSize);
        for (var y = 0; y < SheetSize; y++)
        {
            for (var x = 0; x < SheetSize; x++)
            {
                var at = (y * SheetSize + x) * 4;
                image[x, y] = new Rgba32(Pixels[at], Pixels[at + 1], Pixels[at + 2], Pixels[at + 3]);
            }
        }

        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// Art from an image that is a whole sheet, or one facing, which becomes the south facing. Null for any other
    /// size. <paramref name="pixels"/> holds the image row by row.
    /// </summary>
    public static CustomMarkingArt? FromImage(int width, int height, ReadOnlySpan<Rgba32> pixels)
    {
        if (height != width || width != SheetSize && width != FrameSize || pixels.Length != width * height)
            return null;

        var art = new CustomMarkingArt();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                art.SetPixel(x / FrameSize + y / FrameSize * 2, x % FrameSize, y % FrameSize, pixels[y * width + x]);
            }
        }

        return art;
    }
}
