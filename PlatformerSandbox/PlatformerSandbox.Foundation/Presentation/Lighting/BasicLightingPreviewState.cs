using System;
using System.Collections.Generic;

namespace PlatformerSandbox.Foundation.Presentation.Lighting;

/// <summary>Headless, presentation-only state for the isolated lighting lab.</summary>
public enum BasicLightingDebugView { Final, Albedo, Illumination, SelectedLight, ShadowMask }
public enum BasicLightColorPreset { White, Red, Green, Blue, Amber, Cyan, Magenta }

/// <summary>
/// Artistic 2.5D placement when a light origin is inside an authored shadow
/// caster silhouette. A pair outside that overlap retains normal shadowing.
/// </summary>
public enum BasicLightWallOverlapMode { FrontOfWall, BehindWallWithRim }

/// <summary>Virtual draw depth, not screen Y: lower numbered layers are farther from the camera.</summary>
public enum BasicLightingDepthLayer { Back = 0, World = 1, Front = 2 }

/// <summary>
/// Per-draw receiver override, used ONLY for the wall face being intersected.
/// Other surfaces and blockers continue to receive/shadow both lights normally.
/// </summary>
public readonly record struct BasicWallLightOverlap(bool Light1Behind, bool Light2Behind);

/// <summary>Information required by the GPU to orient a directional back rim.</summary>
public readonly record struct BasicWallRimContext(bool Light1Inside, bool Light2Inside,
    LightPoint WallCenter);

public sealed class BasicLightingPreviewState
{
    private sealed class EditableLight
    {
        public bool Enabled;
        public LightPoint Position;
        public BasicLightColorPreset Color;
        public double Intensity;
        public double Radius;
        public bool CastShadows;
        public BasicLightWallOverlapMode WallOverlapMode;
        public BasicLightingDepthLayer DepthLayer = BasicLightingDepthLayer.World;

        public EditableLight(bool enabled, LightPoint position, BasicLightColorPreset color,
            double intensity, double radius)
        {
            Enabled = enabled;
            Position = position;
            Color = color;
            Intensity = intensity;
            Radius = radius;
        }
    }

    public const double DefaultAmbient = .15;
    public const int SurfaceCount = 4;
    private readonly BasicSurfaceMaterial[] _surfaces =
    [
        new(BasicSurfaceLightingMode.Lit, BasicLightReceiverChannels.Both, 1),
        new(BasicSurfaceLightingMode.Lit, BasicLightReceiverChannels.Light1, 1),
        new(BasicSurfaceLightingMode.Unlit, BasicLightReceiverChannels.None, 1),
        new(BasicSurfaceLightingMode.Lit, BasicLightReceiverChannels.Light2, .55)
    ];
    // Stage 7 adds a bounded set of independent blockers. Slot 1 retains the
    // original Stage-6 contract (ShadowBlocker/ActiveShadowBlocker), so older
    // level fixtures and tests still inspect the same rectangle.
    public const int ShadowBlockerCount = 3;
    public static readonly BasicShadowRectangle DefaultBlocker = new(.49, .23, .065, .42);
    public static readonly BasicShadowRectangle DefaultBlocker2 = new(.38, .56, .05, .18);
    public static readonly BasicShadowRectangle DefaultBlocker3 = new(.60, .51, .045, .20);
    private readonly BasicShadowRectangle[] _shadowBlockers =
        [DefaultBlocker, DefaultBlocker2, DefaultBlocker3];
    private readonly bool[] _shadowBlockerEnabled = new bool[ShadowBlockerCount];
    private readonly BasicShadowShapeKind[] _shadowBlockerKinds = new BasicShadowShapeKind[ShadowBlockerCount];
    private readonly BasicLightingDepthLayer[] _shadowBlockerLayers =
        [BasicLightingDepthLayer.World, BasicLightingDepthLayer.World, BasicLightingDepthLayer.World];
    public bool ShowBlockerOutlines { get; private set; } // Off by default: show actual light-driven rim.
    public void ToggleBlockerOutlines() => ShowBlockerOutlines = !ShowBlockerOutlines;
    public BasicLightingDepthLayer SelectedShadowBlockerDepthLayer =>
        _shadowBlockerLayers[SelectedShadowBlockerIndex];
    public BasicLightingDepthLayer GetShadowBlockerDepthLayer(int index)
    {
        CheckShadowBlockerIndex(index);
        return _shadowBlockerLayers[index];
    }
    public void SetSelectedShadowBlockerDepthLayer(BasicLightingDepthLayer layer)
    {
        if (!Enum.IsDefined(layer)) throw new ArgumentOutOfRangeException(nameof(layer));
        _shadowBlockerLayers[SelectedShadowBlockerIndex] = layer;
    }

    public int SelectedShadowBlockerIndex { get; private set; }
    public BasicShadowRectangle SelectedShadowBlocker => _shadowBlockers[SelectedShadowBlockerIndex];
    public bool SelectedShadowBlockerEnabled => _shadowBlockerEnabled[SelectedShadowBlockerIndex];
    public BasicShadowShapeKind SelectedShadowBlockerKind => _shadowBlockerKinds[SelectedShadowBlockerIndex];
    public int ActiveShadowBlockerCount
    {
        get
        {
            int count = 0;
            foreach (bool enabled in _shadowBlockerEnabled) if (enabled) count++;
            return count;
        }
    }

    public BasicShadowShape GetShadowShape(int index)
    {
        CheckShadowBlockerIndex(index);
        return new BasicShadowShape(_shadowBlockers[index], _shadowBlockerKinds[index]);
    }

    public IReadOnlyList<BasicShadowShape> ActiveShadowShapes
    {
        get
        {
            var result = new List<BasicShadowShape>(ShadowBlockerCount);
            for (int i = 0; i < ShadowBlockerCount; i++)
                if (_shadowBlockerEnabled[i]) result.Add(GetShadowShape(i));
            return result;
        }
    }

    public void SetSelectedShadowBlockerKind(BasicShadowShapeKind kind)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        _shadowBlockerKinds[SelectedShadowBlockerIndex] = kind;
    }

    /// <summary>Add a new visible blocker in the first available GPU slot.</summary>
    public bool AddShadowBlocker(BasicShadowShapeKind kind = BasicShadowShapeKind.Rectangle)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        for (int i = 0; i < ShadowBlockerCount; i++)
        {
            if (_shadowBlockerEnabled[i]) continue;
            _shadowBlockers[i] = i switch
            {
                0 => DefaultBlocker, 1 => DefaultBlocker2, _ => DefaultBlocker3
            };
            _shadowBlockerKinds[i] = kind;
            _shadowBlockerLayers[i] = BasicLightingDepthLayer.World;
            bool firstBlocker = ActiveShadowBlockerCount == 0;
            _shadowBlockerEnabled[i] = true;
            if (firstBlocker) _lights[SelectedLightIndex].CastShadows = true;
            SelectedShadowBlockerIndex = i;
            return true;
        }
        return false; // Explicit shader budget, never silently discard a caster.
    }

    public bool RemoveSelectedShadowBlocker()
    {
        if (!SelectedShadowBlockerEnabled) return false;
        _shadowBlockerEnabled[SelectedShadowBlockerIndex] = false;
        for (int i = 0; i < ShadowBlockerCount; i++)
            if (_shadowBlockerEnabled[i])
            {
                SelectedShadowBlockerIndex = i;
                return true;
            }
        SelectedShadowBlockerIndex = 0;
        return true;
    }

    /// <summary>Select the topmost visible silhouette, not just its bounds.</summary>
    public bool SelectShadowBlockerAt(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y)) return false;
        var at = new LightPoint(x, y);
        for (int i = ShadowBlockerCount - 1; i >= 0; i--)
        {
            if (!_shadowBlockerEnabled[i] || !GetShadowShape(i).Contains(at)) continue;
            SelectedShadowBlockerIndex = i;
            return true;
        }
        return false;
    }
    public BasicShadowRectangle ShadowBlocker => _shadowBlockers[0];
    public bool ShadowBlockerEnabled => _shadowBlockerEnabled[0];
    public BasicShadowRectangle? ActiveShadowBlocker => ShadowBlockerEnabled ? ShadowBlocker : null;
    public bool SelectedSurfaceReceivesShadows => SelectedSurface.ReceivesShadows;

    public IReadOnlyList<BasicShadowRectangle> ActiveShadowBlockers
    {
        get
        {
            var result = new List<BasicShadowRectangle>(ShadowBlockerCount);
            for (int i = 0; i < ShadowBlockerCount; i++)
                if (_shadowBlockerEnabled[i]) result.Add(_shadowBlockers[i]);
            return result;
        }
    }

    public BasicShadowRectangle GetShadowBlocker(int index)
    {
        CheckShadowBlockerIndex(index);
        return _shadowBlockers[index];
    }

    public bool GetShadowBlockerEnabled(int index)
    {
        CheckShadowBlockerIndex(index);
        return _shadowBlockerEnabled[index];
    }

    private static void CheckShadowBlockerIndex(int index)
    {
        if (index < 0 || index >= ShadowBlockerCount)
            throw new ArgumentOutOfRangeException(nameof(index));
    }

    public void SelectShadowBlocker(int index)
    {
        CheckShadowBlockerIndex(index);
        SelectedShadowBlockerIndex = index;
    }

    // Original Stage-6 methods explicitly control slot 1 for compatibility.
    public void SetShadowBlockerEnabled(bool enabled) => _shadowBlockerEnabled[0] = enabled;
    public void SetSelectedShadowBlockerEnabled(bool enabled) =>
        _shadowBlockerEnabled[SelectedShadowBlockerIndex] = enabled;

    public void SetSelectedSurfaceReceivesShadows(bool receives) =>
        SetSelectedSurface(SelectedSurface with { ReceivesShadows = receives });

    public void SetShadowBlockerPosition(double x, double y) => SetBlockerPosition(0, x, y);
    public void SetSelectedShadowBlockerPosition(double x, double y) =>
        SetBlockerPosition(SelectedShadowBlockerIndex, x, y);

    private void SetBlockerPosition(int index, double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y))
            throw new ArgumentOutOfRangeException(nameof(x));
        BasicShadowRectangle blocker = _shadowBlockers[index];
        _shadowBlockers[index] = blocker with
        {
            X = Math.Clamp(x, 0, 1 - blocker.Width),
            Y = Math.Clamp(y, 0, 1 - blocker.Height)
        };
    }

    public void SetShadowBlockerSize(double width, double height) => SetBlockerSize(0, width, height);
    public void SetSelectedShadowBlockerSize(double width, double height) =>
        SetBlockerSize(SelectedShadowBlockerIndex, width, height);

    private void SetBlockerSize(int index, double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height))
            throw new ArgumentOutOfRangeException(nameof(width));
        width = Math.Clamp(width, .025, .20);
        height = Math.Clamp(height, .08, .55);
        BasicShadowRectangle old = _shadowBlockers[index];
        _shadowBlockers[index] = new BasicShadowRectangle(
            Math.Min(old.X, 1 - width), Math.Min(old.Y, 1 - height), width, height);
    }

    private void ResetShadowSlots()
    {
        _shadowBlockers[0] = DefaultBlocker;
        _shadowBlockers[1] = DefaultBlocker2;
        _shadowBlockers[2] = DefaultBlocker3;
        Array.Clear(_shadowBlockerEnabled, 0, _shadowBlockerEnabled.Length);
        Array.Clear(_shadowBlockerKinds, 0, _shadowBlockerKinds.Length);
        for (int i = 0; i < ShadowBlockerCount; i++)
            _shadowBlockerLayers[i] = BasicLightingDepthLayer.World;
        ShowBlockerOutlines = false;
        SelectedShadowBlockerIndex = 0;
    }

    /// <summary>Clear only shadow settings; leave light colors and materials intact.</summary>
    public void ResetShadows()
    {
        ResetShadowSlots();
        foreach (EditableLight light in _lights)
        {
            light.CastShadows = false;
            light.WallOverlapMode = BasicLightWallOverlapMode.FrontOfWall;
            light.DepthLayer = BasicLightingDepthLayer.World;
        }
        for (int i = 0; i < SurfaceCount; i++)
            _surfaces[i] = _surfaces[i] with { ReceivesShadows = true };
    }

    /// <summary>Retain the original one-blocker comparison fixture.</summary>
    public void LoadShadowComparison()
    {
        ResetShadowSlots();
        _shadowBlockerEnabled[0] = true;
        _lights[0].Enabled = true;
        _lights[1].Enabled = true;
        _lights[0].CastShadows = true;
        _lights[1].CastShadows = true;
        // Reproducible Stage-6/7 fixture irrespective of previous overlap edits.
        _lights[0].WallOverlapMode = BasicLightWallOverlapMode.FrontOfWall;
        _lights[1].WallOverlapMode = BasicLightWallOverlapMode.FrontOfWall;
        _lights[0].DepthLayer = BasicLightingDepthLayer.World;
        _lights[1].DepthLayer = BasicLightingDepthLayer.World;
        _lights[0].Position = new LightPoint(.61, .48);
        _lights[1].Position = new LightPoint(.38, .48);
        _lights[0].Radius = .85;
        _lights[1].Radius = .85;
        _lights[0].Color = BasicLightColorPreset.Amber;
        _lights[1].Color = BasicLightColorPreset.Blue;
        SelectedLightIndex = 0;
        DebugView = BasicLightingDebugView.Final;
    }

    /// <summary>Test multiple occluders while keeping old light/material rules.</summary>
    public void LoadMultiShadowComparison()
    {
        LoadShadowComparison();
        _shadowBlockerEnabled[1] = true;
        _shadowBlockerEnabled[2] = true;
        SelectedShadowBlockerIndex = 1;
    }

    /// <summary>Two rectangular/convex silhouettes plus original rectangle.</summary>
    public void LoadPolygonShadowComparison()
    {
        LoadMultiShadowComparison();
        _shadowBlockerKinds[0] = BasicShadowShapeKind.Rectangle;
        _shadowBlockerKinds[1] = BasicShadowShapeKind.Triangle;
        _shadowBlockerKinds[2] = BasicShadowShapeKind.Trapezoid;
        // A wider isolated test fixture makes non-rectangular silhouettes easy
        // to see, without altering the original Stage-6/7 comparison presets.
        _shadowBlockers[1] = new BasicShadowRectangle(.34, .28, .13, .30);
        _shadowBlockers[2] = new BasicShadowRectangle(.62, .29, .12, .34);
        _lights[0].Position = new LightPoint(.18, .32);
        _lights[1].Position = new LightPoint(.82, .31);
        _lights[0].Radius = .9;
        _lights[1].Radius = .9;
        SelectedShadowBlockerIndex = 1;
    }

    public int SelectedSurfaceIndex { get; private set; }
    public BasicSurfaceMaterial SelectedSurface => _surfaces[SelectedSurfaceIndex];

    public BasicSurfaceMaterial GetSurface(int index)
    {
        if (index < 0 || index >= SurfaceCount) throw new ArgumentOutOfRangeException(nameof(index));
        return _surfaces[index];
    }

    public void SelectSurface(int index)
    {
        _ = GetSurface(index);
        SelectedSurfaceIndex = index;
    }

    private void SetSelectedSurface(BasicSurfaceMaterial surface)
    {
        surface.Validate();
        _surfaces[SelectedSurfaceIndex] = surface;
    }

    public void SetSelectedSurfaceMode(BasicSurfaceLightingMode mode) =>
        SetSelectedSurface(SelectedSurface with { Mode = mode });

    public void SetSelectedSurfaceChannels(BasicLightReceiverChannels channels) =>
        SetSelectedSurface(SelectedSurface with { Receives = channels });

    public void SetSelectedSurfaceOpacity(double opacity)
    {
        if (!double.IsFinite(opacity)) throw new ArgumentOutOfRangeException(nameof(opacity));
        SetSelectedSurface(SelectedSurface with { Opacity = Math.Clamp(opacity, 0, 1) });
    }

    public void SetSelectedSurfaceShading(BasicSurfaceShadingMode shading) =>
        SetSelectedSurface(SelectedSurface with { Shading = shading });

    public void SetSelectedSurfaceNormalStrength(double value)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        SetSelectedSurface(SelectedSurface with { NormalStrength = Math.Clamp(value, 0, 1) });
    }

    public void SetSelectedSurfaceRimStrength(double value)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        SetSelectedSurface(SelectedSurface with { RimStrength = Math.Clamp(value, 0, 2) });
    }

    /// <summary>Opt-in directional comparison; Reset restores all Stage-4 defaults.</summary>
    public void LoadDirectionalComparison()
    {
        _surfaces[0] = DefaultSurface(0);
        _surfaces[1] = DefaultSurface(1) with { Shading = BasicSurfaceShadingMode.Normal };
        _surfaces[2] = DefaultSurface(2);
        _surfaces[3] = DefaultSurface(3) with { Shading = BasicSurfaceShadingMode.Rim };
        _lights[1].Enabled = true;
        _lights[1].Color = BasicLightColorPreset.Blue;
        _lights[0].Color = BasicLightColorPreset.Amber;
        SelectedSurfaceIndex = 1;
    }

    public void ResetSelectedSurface() => _surfaces[SelectedSurfaceIndex] = DefaultSurface(SelectedSurfaceIndex);

    private static BasicSurfaceMaterial DefaultSurface(int index) => index switch
    {
        0 => new(BasicSurfaceLightingMode.Lit, BasicLightReceiverChannels.Both, 1),
        1 => new(BasicSurfaceLightingMode.Lit, BasicLightReceiverChannels.Light1, 1),
        2 => new(BasicSurfaceLightingMode.Unlit, BasicLightReceiverChannels.None, 1),
        3 => new(BasicSurfaceLightingMode.Lit, BasicLightReceiverChannels.Light2, .55),
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
    public const double MinimumRadius = .08;
    public const double MaximumRadius = .9;
    public const double MaximumIntensity = 2;
    private readonly EditableLight[] _lights =
    [
        new(true, new LightPoint(.40, .45), BasicLightColorPreset.White, 1, .40),
        new(false, new LightPoint(.64, .46), BasicLightColorPreset.Blue, 1, .40)
    ];

    public int SelectedLightIndex { get; private set; }
    public int LightCount => _lights.Length;
    public double Ambient { get; private set; } = DefaultAmbient;
    public BasicLightingDebugView DebugView { get; private set; } = BasicLightingDebugView.Final;
    public LightRgb AmbientRgb => new(Ambient, Ambient, Ambient);
    public bool LightEnabled => _lights[SelectedLightIndex].Enabled;
    public LightPoint LightPositionNormalized => _lights[SelectedLightIndex].Position;
    public BasicLightColorPreset LightColor => _lights[SelectedLightIndex].Color;
    public double LightIntensity => _lights[SelectedLightIndex].Intensity;
    public double LightRadius => _lights[SelectedLightIndex].Radius;
    public string SelectedLightId => "basics." + (SelectedLightIndex + 1);
    public bool SelectedLightCastsShadows => _lights[SelectedLightIndex].CastShadows;
    public BasicLightWallOverlapMode SelectedLightWallOverlapMode => _lights[SelectedLightIndex].WallOverlapMode;
    public BasicLightingDepthLayer SelectedLightDepthLayer => _lights[SelectedLightIndex].DepthLayer;
    public void SetSelectedLightDepthLayer(BasicLightingDepthLayer layer)
    {
        if (!Enum.IsDefined(layer)) throw new ArgumentOutOfRangeException(nameof(layer));
        _lights[SelectedLightIndex].DepthLayer = layer;
    }

    public void SetSelectedLightWallOverlapMode(BasicLightWallOverlapMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        _lights[SelectedLightIndex].WallOverlapMode = mode;
    }

    /// <summary>
    /// Compatibility overload: Stage-9 tests and existing callers use World-depth walls.
    /// Actual lab draws provide the selected wall's authored virtual depth.
    /// </summary>
    public BasicWallLightOverlap GetWallLightOverlap(BasicShadowShape wall) =>
        GetWallLightOverlap(wall, BasicLightingDepthLayer.World);

    /// <summary>
    /// Only the particular light-wall pair is affected. Any light BEHIND a wall
    /// suppresses face illumination and contributes a colored directional rim,
    /// even without XY origin overlap. Same-depth lights use the old explicit
    /// origin-inside Front/Behind policy; higher lights can light the wall face.
    /// </summary>
    public BasicWallLightOverlap GetWallLightOverlap(BasicShadowShape wall,
        BasicLightingDepthLayer wallLayer)
    {
        wall.Validate();
        if (!Enum.IsDefined(wallLayer)) throw new ArgumentOutOfRangeException(nameof(wallLayer));
        bool IsBehind(EditableLight light) => light.Enabled &&
            (light.DepthLayer < wallLayer ||
             (light.DepthLayer == wallLayer &&
              light.WallOverlapMode == BasicLightWallOverlapMode.BehindWallWithRim &&
              wall.Contains(light.Position)));
        return new BasicWallLightOverlap(IsBehind(_lights[0]), IsBehind(_lights[1]));
    }

    public BasicWallRimContext GetWallRimContext(BasicShadowShape wall)
    {
        wall.Validate();
        // For sloped polygons use the silhouette's vertex centroid, not the
        // axis-aligned bounds center. This stabilizes the facing direction
        // of internally placed backlights on triangles and trapezoids.
        LightPoint[] vertices = wall.Vertices();
        double centerX = 0, centerY = 0;
        foreach (LightPoint vertex in vertices)
        {
            centerX += vertex.X;
            centerY += vertex.Y;
        }
        return new BasicWallRimContext(wall.Contains(_lights[0].Position),
            wall.Contains(_lights[1].Position),
            new LightPoint(centerX / vertices.Length, centerY / vertices.Length));
    }

    /// <summary>Purpose-built visual fixture: amber front light, blue back-rim light.</summary>
    public void LoadWallOverlapComparison()
    {
        LoadMultiShadowComparison();
        _shadowBlockers[0] = new BasicShadowRectangle(.475, .33, .045, .34);
        _shadowBlockers[1] = new BasicShadowRectangle(.28, .29, .12, .38);
        _shadowBlockers[2] = new BasicShadowRectangle(.61, .29, .12, .38);
        _shadowBlockerKinds[0] = BasicShadowShapeKind.Rectangle;
        _shadowBlockerKinds[1] = BasicShadowShapeKind.Rectangle;
        _shadowBlockerKinds[2] = BasicShadowShapeKind.Rectangle;
        _lights[0].Position = new LightPoint(.34, .45);
        _lights[1].Position = new LightPoint(.67, .45);
        _lights[0].Color = BasicLightColorPreset.Amber;
        _lights[1].Color = BasicLightColorPreset.Blue;
        _lights[0].Radius = .45;
        _lights[1].Radius = .45;
        _lights[0].Intensity = 1;
        _lights[1].Intensity = 1;
        Ambient = .12;
        _lights[0].WallOverlapMode = BasicLightWallOverlapMode.FrontOfWall;
        _lights[1].WallOverlapMode = BasicLightWallOverlapMode.BehindWallWithRim;
        _lights[0].CastShadows = true;
        _lights[1].CastShadows = true;
        SelectedLightIndex = 1;
        SelectedShadowBlockerIndex = 2;
        DebugView = BasicLightingDebugView.Final;
    }

    /// <summary>Demonstrate a non-overlapping blue light on a lower layer than a foreground wall.</summary>
    public void LoadDepthRimComparison()
    {
        LoadWallOverlapComparison();
        _lights[1].Position = new LightPoint(.55, .45); // Outside wall 3, left of it and clear of the inspector.
        _lights[1].DepthLayer = BasicLightingDepthLayer.Back;
        _lights[1].WallOverlapMode = BasicLightWallOverlapMode.FrontOfWall;
        _shadowBlockerLayers[2] = BasicLightingDepthLayer.Front;
        // Include sloped shapes so the depth demo exercises diagonal rims.
        _shadowBlockerKinds[1] = BasicShadowShapeKind.Triangle;
        _shadowBlockerKinds[2] = BasicShadowShapeKind.Trapezoid;
        // Stage-11 clean rim fixture: suppress the large hard-shadow wedges
        // only in this preset, so users can inspect the actual wall-edge rim.
        // Other presets and per-light cast-shadow controls remain unchanged.
        _lights[0].CastShadows = false;
        _lights[1].CastShadows = false;
        SelectedLightIndex = 1;
        SelectedShadowBlockerIndex = 2;
    }

    public void SetSelectedLightCastsShadows(bool casts) => _lights[SelectedLightIndex].CastShadows = casts;

    public void SelectLight(int index)
    {
        if (index < 0 || index >= _lights.Length) throw new ArgumentOutOfRangeException(nameof(index));
        SelectedLightIndex = index;
    }

    public void SetAmbient(double amount)
    {
        if (!double.IsFinite(amount)) throw new ArgumentOutOfRangeException(nameof(amount));
        Ambient = Math.Clamp(amount, 0, 1);
    }

    public void ToggleLight() => _lights[SelectedLightIndex].Enabled = !LightEnabled;

    public void SetLightEnabled(bool enabled) => _lights[SelectedLightIndex].Enabled = enabled;

    public void SetLightPositionNormalized(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y))
            throw new ArgumentOutOfRangeException(nameof(x));
        _lights[SelectedLightIndex].Position =
            new LightPoint(Math.Clamp(x, 0, 1), Math.Clamp(y, 0, 1));
    }

    public void SetLightColor(BasicLightColorPreset color)
    {
        if (!Enum.IsDefined(color)) throw new ArgumentOutOfRangeException(nameof(color));
        _lights[SelectedLightIndex].Color = color;
    }

    public static LightRgb PresetRgb(BasicLightColorPreset color) => color switch
    {
        BasicLightColorPreset.White => LightRgb.White,
        BasicLightColorPreset.Red => new LightRgb(1, 0, 0),
        BasicLightColorPreset.Green => new LightRgb(0, 1, 0),
        BasicLightColorPreset.Blue => new LightRgb(0, 0, 1),
        BasicLightColorPreset.Amber => new LightRgb(1, .56, .14),
        BasicLightColorPreset.Cyan => new LightRgb(0, 1, 1),
        BasicLightColorPreset.Magenta => new LightRgb(1, 0, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(color))
    };

    public void SetLightIntensity(double intensity)
    {
        if (!double.IsFinite(intensity)) throw new ArgumentOutOfRangeException(nameof(intensity));
        _lights[SelectedLightIndex].Intensity = Math.Clamp(intensity, 0, MaximumIntensity);
    }

    public void SetLightRadius(double normalizedRadius)
    {
        if (!double.IsFinite(normalizedRadius)) throw new ArgumentOutOfRangeException(nameof(normalizedRadius));
        _lights[SelectedLightIndex].Radius = Math.Clamp(normalizedRadius, MinimumRadius, MaximumRadius);
    }

    public void SetDebugView(BasicLightingDebugView view)
    {
        if (!Enum.IsDefined(view)) throw new ArgumentOutOfRangeException(nameof(view));
        DebugView = view;
    }

    private RadialLight? BuildLight(int index, LightingViewport viewport)
    {
        EditableLight data = _lights[index];
        if (!data.Enabled) return null;
        return new RadialLight
        {
            Id = "basics." + (index + 1),
            Position = viewport.Unproject(new LightPoint(
                data.Position.X * viewport.Width, data.Position.Y * viewport.Height)),
            Radius = data.Radius * Math.Min(viewport.Width, viewport.Height) / viewport.PixelsPerUnit,
            Color = PresetRgb(data.Color),
            Intensity = data.Intensity,
            CastShadows = data.CastShadows
        };
    }

    /// <summary>Compatibility helper for stage-1/2 tests: the first light.</summary>
    public RadialLight? BuildLight(LightingViewport viewport)
    {
        viewport.Validate();
        return BuildLight(0, viewport);
    }

    public IReadOnlyList<RadialLight> BuildLights(LightingViewport viewport)
    {
        viewport.Validate();
        var result = new List<RadialLight>(_lights.Length);
        for (int i = 0; i < _lights.Length; i++)
            if (BuildLight(i, viewport) is RadialLight light) result.Add(light);
        return result;
    }

    public IReadOnlyList<BasicMaterialLight> BuildMaterialLights(LightingViewport viewport)
    {
        viewport.Validate();
        var result = new List<BasicMaterialLight>(_lights.Length);
        for (int i = 0; i < _lights.Length; i++)
            if (BuildLight(i, viewport) is RadialLight light)
                result.Add(new BasicMaterialLight(light, i == 0
                    ? BasicLightReceiverChannels.Light1 : BasicLightReceiverChannels.Light2));
        return result;
    }

    public void Reset()
    {
        ResetShadowSlots();
        SelectedSurfaceIndex = 0;
        for (int i = 0; i < SurfaceCount; i++)
            _surfaces[i] = DefaultSurface(i);
        SelectedLightIndex = 0;
        Ambient = DefaultAmbient;
        DebugView = BasicLightingDebugView.Final;
        _lights[0] = new EditableLight(true, new LightPoint(.40, .45),
            BasicLightColorPreset.White, 1, .40);
        _lights[1] = new EditableLight(false, new LightPoint(.64, .46),
            BasicLightColorPreset.Blue, 1, .40);
    }
}
