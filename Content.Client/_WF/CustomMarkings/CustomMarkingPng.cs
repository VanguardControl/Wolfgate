using System.IO;
using System.Numerics;
using Content.Shared._WF.CustomMarkings;
using Robust.Client.Utility;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._WF.CustomMarkings;

/// <summary>Reads marking art from PNG: sheets the server sends, and files a player imports.</summary>
public static class CustomMarkingPng
{
    /// <summary>Largest file the importer will try to read: room for a sheet of every frame, however noisy.</summary>
    public const int MaxFileBytes = 1024 * 1024;

    /// <summary>Art from a PNG of a whole sheet or of one facing; null for anything else.</summary>
    public static CustomMarkingArt? Read(byte[] png)
    {
        if (png.Length > MaxFileBytes)
            return null;

        try
        {
            if (CustomMarkingColorSpace.Read(png) is { } space)
                return ReadConverted(png, space);

            using var image = Image.Load<Rgba32>(new MemoryStream(png, false));
            return CustomMarkingArt.FromImage(image.Width, image.Height, image.GetPixelSpan());
        }
        catch (Exception)
        {
            // Not an image ImageSharp can read.
            return null;
        }
    }

    /// <summary>Reads a PNG saved in another colour space at full depth and brings its pixels to sRGB.</summary>
    private static CustomMarkingArt? ReadConverted(byte[] png, CustomMarkingColorSpace space)
    {
        using var image = Image.Load<Rgba64>(new MemoryStream(png, false));
        var stored = image.GetPixelSpan();
        var pixels = new Rgba32[stored.Length];
        for (var i = 0; i < stored.Length; i++)
        {
            var pixel = stored[i];
            var srgb = space.ToSrgb(new Vector3(pixel.R, pixel.G, pixel.B) / ushort.MaxValue);
            pixels[i] = new Rgba32(Channel(srgb.X), Channel(srgb.Y), Channel(srgb.Z), (byte) (pixel.A >> 8));
        }

        return CustomMarkingArt.FromImage(image.Width, image.Height, pixels);

        static byte Channel(float value)
        {
            return (byte) Math.Clamp(MathF.Round(value * byte.MaxValue), byte.MinValue, byte.MaxValue);
        }
    }
}
