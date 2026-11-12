using Robust.Client.Graphics;
using Robust.Client.Utility;
using Robust.Shared.ContentPack;
using Robust.Shared.Graphics.RSI;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._WF.AppearanceStudio.UI;

/// <summary>The first frame of each facing of an item's sprite state, read from its PNG so the warp editor can bend it.</summary>
public static class StudioItemFrames
{
    public const int Size = 32;

    /// <summary>
    /// South, north, east and west 32x32 frames of a state, each row by row, or null when it can't be read or isn't
    /// 32 pixels square (warps are 32x32 only).
    /// </summary>
    public static Rgba32[][]? Read(IResourceManager resources, RSI rsi, RSI.StateId stateId)
    {
        if (rsi.Size != new Vector2i(Size, Size) || !rsi.TryGetState(stateId, out var state))
            return null;

        Image<Rgba32> sheet;
        try
        {
            using var stream = resources.ContentFileRead(rsi.Path / $"{stateId.Name}.png");
            sheet = Image.Load<Rgba32>(stream);
        }
        catch (Exception)
        {
            return null;
        }

        using (sheet)
        {
            var columns = sheet.Width / Size;
            if (columns == 0)
                return null;

            var pixels = sheet.GetPixelSpan();
            var frames = state.DelayCount;
            var directions = state.RsiDirections == RsiDirectionType.Dir1 ? 1 : 4;
            var result = new Rgba32[4][];
            for (var facing = 0; facing < 4; facing++)
            {
                var cell = (directions == 1 ? 0 : facing) * frames;
                var left = cell % columns * Size;
                var top = cell / columns * Size;
                var frame = new Rgba32[Size * Size];
                if (top + Size <= sheet.Height)
                {
                    for (var y = 0; y < Size; y++)
                    {
                        for (var x = 0; x < Size; x++)
                        {
                            frame[y * Size + x] = pixels[(top + y) * sheet.Width + left + x];
                        }
                    }
                }

                result[facing] = frame;
            }

            return result;
        }
    }
}
