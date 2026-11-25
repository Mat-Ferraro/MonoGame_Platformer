using PlatformerSandbox.Foundation.Performance;
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PlatformerSandbox.Gyms;

/// <summary>
/// Tiny presentation-only 5x7 bitmap font for the diagnostic HUD.
/// No SpriteFont, assets, content pipeline setup, or simulation dependency.
/// </summary>
internal static class GymDebugText
{
    // Each glyph is encoded as five-bit rows. Unknown symbols render as blanks.
    private static readonly IReadOnlyDictionary<char, string[]> Glyphs =
        new Dictionary<char, string[]>
        {
            ['A'] = ["01110","10001","10001","11111","10001","10001","10001"],
            ['B'] = ["11110","10001","10001","11110","10001","10001","11110"],
            ['C'] = ["01111","10000","10000","10000","10000","10000","01111"],
            ['D'] = ["11110","10001","10001","10001","10001","10001","11110"],
            ['E'] = ["11111","10000","10000","11110","10000","10000","11111"],
            ['F'] = ["11111","10000","10000","11110","10000","10000","10000"],
            ['G'] = ["01111","10000","10000","10111","10001","10001","01110"],
            ['H'] = ["10001","10001","10001","11111","10001","10001","10001"],
            ['I'] = ["11111","00100","00100","00100","00100","00100","11111"],
            ['J'] = ["00111","00010","00010","00010","10010","10010","01100"],
            ['K'] = ["10001","10010","10100","11000","10100","10010","10001"],
            ['L'] = ["10000","10000","10000","10000","10000","10000","11111"],
            ['M'] = ["10001","11011","10101","10101","10001","10001","10001"],
            ['N'] = ["10001","11001","10101","10011","10001","10001","10001"],
            ['O'] = ["01110","10001","10001","10001","10001","10001","01110"],
            ['P'] = ["11110","10001","10001","11110","10000","10000","10000"],
            ['Q'] = ["01110","10001","10001","10001","10101","10010","01101"],
            ['R'] = ["11110","10001","10001","11110","10100","10010","10001"],
            ['S'] = ["01111","10000","10000","01110","00001","00001","11110"],
            ['T'] = ["11111","00100","00100","00100","00100","00100","00100"],
            ['U'] = ["10001","10001","10001","10001","10001","10001","01110"],
            ['V'] = ["10001","10001","10001","10001","10001","01010","00100"],
            ['W'] = ["10001","10001","10001","10101","10101","10101","01010"],
            ['X'] = ["10001","10001","01010","00100","01010","10001","10001"],
            ['Y'] = ["10001","10001","01010","00100","00100","00100","00100"],
            ['Z'] = ["11111","00001","00010","00100","01000","10000","11111"],
            ['0'] = ["01110","10001","10011","10101","11001","10001","01110"],
            ['1'] = ["00100","01100","00100","00100","00100","00100","01110"],
            ['2'] = ["01110","10001","00001","00010","00100","01000","11111"],
            ['3'] = ["11110","00001","00001","01110","00001","00001","11110"],
            ['4'] = ["00010","00110","01010","10010","11111","00010","00010"],
            ['5'] = ["11111","10000","10000","11110","00001","00001","11110"],
            ['6'] = ["01110","10000","10000","11110","10001","10001","01110"],
            ['7'] = ["11111","00001","00010","00100","01000","01000","01000"],
            ['8'] = ["01110","10001","10001","01110","10001","10001","01110"],
            ['9'] = ["01110","10001","10001","01111","00001","00001","01110"],
            ['_'] = ["00000","00000","00000","00000","00000","00000","11111"],
            ['.'] = ["00000","00000","00000","00000","00000","00100","00100"],
            [':'] = ["00000","00100","00100","00000","00100","00100","00000"],
            ['-'] = ["00000","00000","00000","11111","00000","00000","00000"],
            ['/'] = ["00001","00001","00010","00100","01000","10000","10000"],
            [' '] = ["00000","00000","00000","00000","00000","00000","00000"]
        };

    private static Texture2D? _atlas;
    private static readonly Dictionary<char, Rectangle> Regions = new();
    public static void Initialize(GraphicsDevice graphics)
    {
        if (!EnginePerformanceOptions.HudAtlas) return;
        Release(graphics);
        const int pitchX = 7, pitchY = 9, columns = 16;
        int width = columns * pitchX, height = ((Glyphs.Count + columns - 1) / columns) * pitchY;
        var pixels = new Color[width * height];
        int index = 0;
        foreach (var entry in Glyphs)
        {
            int x = index % columns * pitchX + 1, y = index / columns * pitchY + 1;
            Regions[entry.Key] = new Rectangle(x, y, 5, 7);
            for (int row = 0; row < 7; row++)
                for (int col = 0; col < 5; col++)
                    if (entry.Value[row][col] == '1') pixels[(y + row) * width + x + col] = Color.White;
            index++;
        }
        _atlas = new Texture2D(graphics, width, height);
        _atlas.SetData(pixels);
    }
    public static void Release(GraphicsDevice graphics)
    {
        if (_atlas is not null && ReferenceEquals(_atlas.GraphicsDevice, graphics))
        { _atlas.Dispose(); _atlas = null; Regions.Clear(); }
    }

    public static void Draw(SpriteBatch batch, Texture2D pixel, int x, int y,
        string message, Color color, int scale = 2)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(pixel);
        ArgumentNullException.ThrowIfNull(message);
        if (scale <= 0) throw new ArgumentOutOfRangeException(nameof(scale));
        int cursor = x;
        foreach (char raw in message)
        {
            char ch = char.ToUpperInvariant(raw);
            if (_atlas is not null && !_atlas.IsDisposed && ReferenceEquals(_atlas.GraphicsDevice, pixel.GraphicsDevice))
            {
                if (ch != ' ' && Regions.TryGetValue(ch, out Rectangle source))
                    batch.Draw(_atlas, new Rectangle(cursor, y, 5 * scale, 7 * scale), source, color);
            }
            else if (Glyphs.TryGetValue(ch, out string[]? glyph))
            {
                for (int row = 0; row < glyph.Length; row++)
                {
                    for (int column = 0; column < 5; column++)
                    {
                        if (glyph[row][column] == '1')
                            batch.Draw(pixel, new Rectangle(cursor + column * scale,
                                y + row * scale, scale, scale), color);
                    }
                }
            }
            cursor += 6 * scale;
        }
    }
}
