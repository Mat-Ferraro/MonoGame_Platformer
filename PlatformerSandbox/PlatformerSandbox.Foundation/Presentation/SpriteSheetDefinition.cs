using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

namespace PlatformerSandbox.Foundation.Presentation;

/// <summary>Pixel rectangle in a sprite atlas. This geometry is intentionally
/// independent of MonoGame's Rectangle and Texture2D types.</summary>
public readonly record struct SpriteSheetRegion(int X, int Y, int Width, int Height);

/// <summary>
/// Validated, data-authored sprite sheet. Animation timing belongs to the
/// presentation layer; nothing here can affect the world simulation.
/// Coordinates are row-major, measured in texture pixels.
/// </summary>
public sealed class SpriteSheetDefinition
{
    private readonly IReadOnlyDictionary<string, SpriteAnimationClip> _clips;

    public string TextureAsset { get; }
    public int FrameWidth { get; }
    public int FrameHeight { get; }
    public int Columns { get; }
    public int Rows { get; }
    public int FrameCount => checked(Columns * Rows);
    public int TextureWidth => checked(Columns * FrameWidth);
    public int TextureHeight => checked(Rows * FrameHeight);
    public IReadOnlyDictionary<string, SpriteAnimationClip> Clips => _clips;

    private SpriteSheetDefinition(string textureAsset, int frameWidth,
        int frameHeight, int columns, int rows,
        Dictionary<string, SpriteAnimationClip> clips)
    {
        TextureAsset = textureAsset;
        FrameWidth = frameWidth;
        FrameHeight = frameHeight;
        Columns = columns;
        Rows = rows;
        _clips = new ReadOnlyDictionary<string, SpriteAnimationClip>(clips);
    }

    public SpriteAnimationClip GetClip(string name) =>
        _clips.TryGetValue(name, out SpriteAnimationClip? clip) ? clip :
        throw new KeyNotFoundException("Unknown animation clip: " + name);

    public SpriteSheetRegion RegionForFrame(int frameId)
    {
        if (frameId < 0 || frameId >= FrameCount)
            throw new ArgumentOutOfRangeException(nameof(frameId));
        return new SpriteSheetRegion((frameId % Columns) * FrameWidth,
            (frameId / Columns) * FrameHeight, FrameWidth, FrameHeight);
    }

    public static SpriteSheetDefinition FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Sprite definition root must be an object.");

        int version = RequiredInt(root, "version");
        if (version != 1)
            throw new InvalidDataException("Unsupported sprite definition version: " + version);
        string textureAsset = RequiredString(root, "textureAsset");
        // Content.Load uses an asset name, not a filesystem path. The definition
        // must not be permitted to load arbitrary files outside the game content.
        if (textureAsset.StartsWith('/') || textureAsset.StartsWith('\\') ||
            textureAsset.Contains("..", StringComparison.Ordinal) ||
            textureAsset.Contains(':') || textureAsset.Contains('\\') ||
            textureAsset.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            textureAsset.EndsWith(".xnb", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Texture asset must be a relative MonoGame content name.");

        int width = RequiredInt(root, "frameWidth");
        int height = RequiredInt(root, "frameHeight");
        int columns = RequiredInt(root, "columns");
        int rows = RequiredInt(root, "rows");
        if (width is < 1 or > 1024 || height is < 1 or > 1024 ||
            columns is < 1 or > 128 || rows is < 1 or > 128 ||
            (long)width * columns > 8192 || (long)height * rows > 8192 ||
            (long)columns * rows > 4096)
            throw new InvalidDataException("Invalid atlas dimensions or frame count.");

        if (!root.TryGetProperty("clips", out JsonElement clipsNode) ||
            clipsNode.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("clips must be an array.");
        var clips = new Dictionary<string, SpriteAnimationClip>(StringComparer.Ordinal);
        foreach (JsonElement clipNode in clipsNode.EnumerateArray())
        {
            string name = RequiredString(clipNode, "name");
            string modeText = RequiredString(clipNode, "mode");
            if (!Enum.TryParse(modeText, ignoreCase: false,
                out SpriteAnimationMode mode) || !Enum.IsDefined(mode))
                throw new InvalidDataException("Unsupported animation mode for " + name);
            if (!clipNode.TryGetProperty("frames", out JsonElement framesNode) ||
                framesNode.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("frames must be an array for " + name);
            var frames = new List<SpriteAnimationFrame>();
            foreach (JsonElement frameNode in framesNode.EnumerateArray())
            {
                int index = RequiredInt(frameNode, "id");
                if (index < 0 || index >= columns * rows)
                    throw new InvalidDataException("Frame index outside atlas in " + name);
                if (!frameNode.TryGetProperty("seconds", out JsonElement secondsNode) ||
                    secondsNode.ValueKind != JsonValueKind.Number ||
                    !secondsNode.TryGetDouble(out double duration) ||
                    !double.IsFinite(duration) || duration <= 0)
                    throw new InvalidDataException("Invalid frame duration in " + name);
                if (frames.Count >= 256)
                    throw new InvalidDataException("Too many animation frames in " + name);
                frames.Add(new SpriteAnimationFrame(index, duration));
            }
            if (frames.Count == 0)
                throw new InvalidDataException("Empty animation clip: " + name);
            if (!clips.TryAdd(name, new SpriteAnimationClip(name, frames, mode)))
                throw new InvalidDataException("Duplicate animation clip: " + name);
        }
        if (clips.Count == 0)
            throw new InvalidDataException("Sprite definition has no clips.");
        return new SpriteSheetDefinition(textureAsset, width, height, columns, rows, clips);
    }

    private static int RequiredInt(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object ||
            !parent.TryGetProperty(name, out JsonElement value) ||
            value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int result))
            throw new InvalidDataException("Missing or invalid integer: " + name);
        return result;
    }

    private static string RequiredString(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object ||
            !parent.TryGetProperty(name, out JsonElement value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException("Missing or invalid text: " + name);
        return value.GetString()!;
    }
}
