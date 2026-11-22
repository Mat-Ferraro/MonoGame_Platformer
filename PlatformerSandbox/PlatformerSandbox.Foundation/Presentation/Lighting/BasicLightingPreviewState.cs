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

/// <summary>Presentation-only translation, independent of gameplay motion.</summary>
public enum BasicBlockerMotionMode { Static, Horizontal, Vertical }

/// <summary>Clockwise is positive in screen-space coordinates (Y down).</summary>
public enum BasicBlockerRotationMode { Static, Clockwise, CounterClockwise, Oscillate }

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
    public BasicLightingPreviewState()
    {
        for (int i = 0; i < ShadowBlockerCount; i++)
        {
            ResetMotionSlot(i);
            ResetRotationSlot(i);
        }
    }

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
    // Stage 13: every authoring slot owns its own motion. The Home rectangle
    // is immutable during time advancement (no integration/drift). Its current
    // position is derived from an elapsed-time triangle wave each frame.
    private readonly BasicBlockerMotionMode[] _blockerMotionModes = new BasicBlockerMotionMode[ShadowBlockerCount];
    private readonly double[] _blockerMotionSpeeds = new double[ShadowBlockerCount];
    private readonly double[] _blockerMotionDistances = new double[ShadowBlockerCount];
    private readonly double[] _blockerMotionPhases = new double[ShadowBlockerCount];
    private readonly BasicShadowRectangle[] _blockerMotionHomes = new BasicShadowRectangle[ShadowBlockerCount];
    // Rotation is independent of translation, but both are evaluated on the
    // same frame before submitting wall faces and shadow vertices to the GPU.
    private readonly double[] _blockerAngles = new double[ShadowBlockerCount];
    private readonly double[] _blockerRotationAnchors = new double[ShadowBlockerCount];
    private readonly double[] _blockerRotationPhases = new double[ShadowBlockerCount];
    private readonly double[] _blockerAngularSpeeds = new double[ShadowBlockerCount];
    private readonly double[] _blockerRotationSweeps = new double[ShadowBlockerCount];
    private readonly BasicBlockerRotationMode[] _blockerRotationModes = new BasicBlockerRotationMode[ShadowBlockerCount];
    private double _shadowViewportAspectRatio = 1;
    public double ShadowViewportAspectRatio => _shadowViewportAspectRatio;
    public void SetShadowViewportAspectRatio(double ratio)
    {
        if (!double.IsFinite(ratio) || ratio <= 0)
            throw new ArgumentOutOfRangeException(nameof(ratio));
        _shadowViewportAspectRatio = ratio;
    }
    public const double MinimumAngularSpeed = 15; // degrees/second
    public const double MaximumAngularSpeed = 180;
    public const double MinimumRotationSweep = 5; // degrees from anchor
    public const double MaximumRotationSweep = 90;
    public BasicBlockerRotationMode SelectedBlockerRotationMode =>
        _blockerRotationModes[SelectedShadowBlockerIndex];
    public double SelectedBlockerRotationAngle => _blockerAngles[SelectedShadowBlockerIndex];
    public double SelectedBlockerAngularSpeed => _blockerAngularSpeeds[SelectedShadowBlockerIndex];
    public double SelectedBlockerRotationSweep => _blockerRotationSweeps[SelectedShadowBlockerIndex];
    public BasicBlockerRotationMode GetBlockerRotationMode(int index)
    {
        CheckShadowBlockerIndex(index);
        return _blockerRotationModes[index];
    }
    public double GetBlockerRotationAngle(int index)
    {
        CheckShadowBlockerIndex(index);
        return _blockerAngles[index];
    }
    private static double WrapDegrees(double angle) => ((angle % 360) + 360) % 360;
    private void AnchorRotationAtCurrentAngle(int index)
    {
        _blockerRotationAnchors[index] = _blockerAngles[index];
        _blockerRotationPhases[index] = 0;
    }
    private void ResetRotationSlot(int index)
    {
        _blockerAngles[index] = 0;
        _blockerRotationModes[index] = BasicBlockerRotationMode.Static;
        _blockerAngularSpeeds[index] = 60;
        _blockerRotationSweeps[index] = 45;
        AnchorRotationAtCurrentAngle(index);
    }
    public void SetSelectedBlockerRotationMode(BasicBlockerRotationMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        int index = SelectedShadowBlockerIndex;
        if (_blockerRotationModes[index] == mode) return;
        AnchorRotationAtCurrentAngle(index);
        _blockerRotationModes[index] = mode;
    }
    public void SetSelectedBlockerRotationAngle(double degrees)
    {
        if (!double.IsFinite(degrees)) throw new ArgumentOutOfRangeException(nameof(degrees));
        int index = SelectedShadowBlockerIndex;
        _blockerAngles[index] = WrapDegrees(degrees);
        AnchorRotationAtCurrentAngle(index);
    }
    public void SetSelectedBlockerAngularSpeed(double degreesPerSecond)
    {
        if (!double.IsFinite(degreesPerSecond)) throw new ArgumentOutOfRangeException(nameof(degreesPerSecond));
        _blockerAngularSpeeds[SelectedShadowBlockerIndex] = Math.Clamp(degreesPerSecond,
            MinimumAngularSpeed, MaximumAngularSpeed);
    }
    public void SetSelectedBlockerRotationSweep(double degrees)
    {
        if (!double.IsFinite(degrees)) throw new ArgumentOutOfRangeException(nameof(degrees));
        int index = SelectedShadowBlockerIndex;
        AnchorRotationAtCurrentAngle(index);
        _blockerRotationSweeps[index] = Math.Clamp(degrees, MinimumRotationSweep, MaximumRotationSweep);
    }
    public void ResetSelectedBlockerRotation() => ResetRotationSlot(SelectedShadowBlockerIndex);

    public const double MinimumMotionSpeed = .10; // complete round trips per second
    public const double MaximumMotionSpeed = 1.50;
    public const double MinimumMotionDistance = .02; // normalized viewport units
    public const double MaximumMotionDistance = .30;
    public bool BlockerMotionPlaying { get; private set; } = true;
    public void SetBlockerMotionPlaying(bool playing) => BlockerMotionPlaying = playing;
    public void ToggleBlockerMotionPlaying() => BlockerMotionPlaying = !BlockerMotionPlaying;
    public BasicBlockerMotionMode SelectedBlockerMotionMode =>
        _blockerMotionModes[SelectedShadowBlockerIndex];
    public double SelectedBlockerMotionSpeed => _blockerMotionSpeeds[SelectedShadowBlockerIndex];
    public double SelectedBlockerMotionDistance => _blockerMotionDistances[SelectedShadowBlockerIndex];

    public BasicBlockerMotionMode GetBlockerMotionMode(int index)
    {
        CheckShadowBlockerIndex(index);
        return _blockerMotionModes[index];
    }

    private void AnchorMotionAtCurrentPosition(int index)
    {
        _blockerMotionHomes[index] = _shadowBlockers[index];
        _blockerMotionPhases[index] = 0;
    }

    private void ResetMotionSlot(int index)
    {
        _blockerMotionModes[index] = BasicBlockerMotionMode.Static;
        _blockerMotionSpeeds[index] = .40;
        _blockerMotionDistances[index] = .16;
        AnchorMotionAtCurrentPosition(index);
    }

    public void SetSelectedBlockerMotionMode(BasicBlockerMotionMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        int i = SelectedShadowBlockerIndex;
        if (_blockerMotionModes[i] == mode) return;
        AnchorMotionAtCurrentPosition(i);
        _blockerMotionModes[i] = mode;
    }

    public void SetSelectedBlockerMotionSpeed(double cyclesPerSecond)
    {
        if (!double.IsFinite(cyclesPerSecond)) throw new ArgumentOutOfRangeException(nameof(cyclesPerSecond));
        _blockerMotionSpeeds[SelectedShadowBlockerIndex] = Math.Clamp(cyclesPerSecond,
            MinimumMotionSpeed, MaximumMotionSpeed);
    }

    public void SetSelectedBlockerMotionDistance(double normalizedDistance)
    {
        if (!double.IsFinite(normalizedDistance)) throw new ArgumentOutOfRangeException(nameof(normalizedDistance));
        int i = SelectedShadowBlockerIndex;
        AnchorMotionAtCurrentPosition(i); // distance edits cannot teleport the wall
        _blockerMotionDistances[i] = Math.Clamp(normalizedDistance,
            MinimumMotionDistance, MaximumMotionDistance);
    }

    public void ResetSelectedBlockerMotion()
    {
        int i = SelectedShadowBlockerIndex;
        ResetMotionSlot(i); // freeze at its current position, no jump
    }

    /// <summary>
    /// Step all enabled moving blockers using elapsed presentation time.
    /// A slot starts at its authored anchor, travels up to Distance in the
    /// positive direction, then returns. Near a viewport boundary it uses the
    /// negative direction instead. The full range is clamped to the viewport.
    /// Calls are deterministic and invariant to timestep splitting within
    /// floating-point tolerance. Pausing does not advance phase.
    /// </summary>
    public void AdvanceBlockerMotion(double elapsedSeconds, int skipBlockerIndex = -1)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        if (!BlockerMotionPlaying || elapsedSeconds == 0) return;
        for (int i = 0; i < ShadowBlockerCount; i++)
        {
            if (i == skipBlockerIndex) continue; // Mouse ownership prevents drag jitter.
            BasicBlockerMotionMode mode = _blockerMotionModes[i];
            if (!_shadowBlockerEnabled[i]) continue;
            // Evaluate rotation before translation. Each transform is derived
            // from an immutable anchor plus phase: no cumulative vertex drift.
            BasicBlockerRotationMode rotationMode = _blockerRotationModes[i];
            if (rotationMode != BasicBlockerRotationMode.Static)
            {
                double speed = _blockerAngularSpeeds[i];
                if (rotationMode == BasicBlockerRotationMode.Oscillate)
                {
                    double sweep = _blockerRotationSweeps[i];
                    double rotationPhase = (_blockerRotationPhases[i] + elapsedSeconds * speed / (4 * sweep)) % 1.0;
                    _blockerRotationPhases[i] = rotationPhase;
                    // Triangle wave: 0 -> +sweep -> 0 -> -sweep -> 0.
                    double signed = rotationPhase < .25 ? rotationPhase * 4 :
                        rotationPhase < .75 ? 2 - rotationPhase * 4 : rotationPhase * 4 - 4;
                    _blockerAngles[i] = WrapDegrees(_blockerRotationAnchors[i] + signed * sweep);
                }
                else
                {
                    double rotationPhase = (_blockerRotationPhases[i] + elapsedSeconds * speed) % 360;
                    _blockerRotationPhases[i] = rotationPhase;
                    _blockerAngles[i] = WrapDegrees(_blockerRotationAnchors[i] +
                        (rotationMode == BasicBlockerRotationMode.Clockwise ? rotationPhase : -rotationPhase));
                }
            }
            if (mode == BasicBlockerMotionMode.Static) continue;
            BasicShadowRectangle home = _blockerMotionHomes[i];
            double phase = (_blockerMotionPhases[i] + elapsedSeconds * _blockerMotionSpeeds[i]) % 1.0;
            _blockerMotionPhases[i] = phase;
            double t = phase <= .5 ? phase * 2 : (1 - phase) * 2;
            bool horizontal = mode == BasicBlockerMotionMode.Horizontal;
            double start = horizontal ? home.X : home.Y;
            double limit = 1 - (horizontal ? home.Width : home.Height);
            double span = _blockerMotionDistances[i];
            // Prefer positive travel; if there is insufficient positive room,
            // use negative travel. Always choose one endpoint consistently.
            double positive = Math.Min(span, limit - start);
            double negative = Math.Min(span, start);
            double signedTravel = positive >= span * .50 || positive >= negative
                ? positive : -negative;
            double position = Math.Clamp(start + signedTravel * t, 0, limit);
            _shadowBlockers[i] = horizontal
                ? home with { X = position }
                : home with { Y = position };
        }
    }

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
        return new BasicShadowShape(_shadowBlockers[index], _shadowBlockerKinds[index],
            _blockerAngles[index], _shadowViewportAspectRatio);
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
            ResetMotionSlot(i);
            ResetRotationSlot(i);
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
        ResetMotionSlot(SelectedShadowBlockerIndex);
        ResetRotationSlot(SelectedShadowBlockerIndex);
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
        // Manual drags/inspector edits re-anchor motion so the next frame does
        // not snap back to an old automatic position.
        AnchorMotionAtCurrentPosition(index);
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
        AnchorMotionAtCurrentPosition(index);
    }

    private void ResetShadowSlots()
    {
        _shadowBlockers[0] = DefaultBlocker;
        _shadowBlockers[1] = DefaultBlocker2;
        _shadowBlockers[2] = DefaultBlocker3;
        Array.Clear(_shadowBlockerEnabled, 0, _shadowBlockerEnabled.Length);
        Array.Clear(_shadowBlockerKinds, 0, _shadowBlockerKinds.Length);
        for (int i = 0; i < ShadowBlockerCount; i++)
        {
            _shadowBlockerLayers[i] = BasicLightingDepthLayer.World;
            ResetMotionSlot(i);
            ResetRotationSlot(i);
        }
        BlockerMotionPlaying = true;
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

    /// <summary>Reproducible Stage-13 fixture with moving convex silhouettes.</summary>
    public void LoadMovingShadowComparison()
    {
        LoadPolygonShadowComparison();
        _lights[0].CastShadows = true;
        _lights[1].CastShadows = true;
        _lights[1].DepthLayer = BasicLightingDepthLayer.Back;
        _shadowBlockerLayers[2] = BasicLightingDepthLayer.Front;
        _blockerMotionModes[0] = BasicBlockerMotionMode.Vertical;
        _blockerMotionModes[1] = BasicBlockerMotionMode.Horizontal;
        _blockerMotionModes[2] = BasicBlockerMotionMode.Horizontal;
        _blockerMotionSpeeds[0] = .33;
        _blockerMotionSpeeds[1] = .40;
        _blockerMotionSpeeds[2] = .55;
        _blockerMotionDistances[0] = .16;
        _blockerMotionDistances[1] = .15;
        _blockerMotionDistances[2] = .10;
        for (int i = 0; i < ShadowBlockerCount; i++) AnchorMotionAtCurrentPosition(i);
        BlockerMotionPlaying = true;
        SelectedLightIndex = 1;
        SelectedShadowBlockerIndex = 2;
        DebugView = BasicLightingDebugView.Final;
    }

    /// <summary>Stage-14 fixture: two rotating shapes and moving/rotating combinations.</summary>
    public void LoadRotatingShadowComparison()
    {
        LoadPolygonShadowComparison();
        _lights[0].CastShadows = true;
        _lights[1].CastShadows = true;
        _lights[0].DepthLayer = BasicLightingDepthLayer.World;
        _lights[1].DepthLayer = BasicLightingDepthLayer.Back;
        _shadowBlockerLayers[2] = BasicLightingDepthLayer.Front;
        // Wide silhouettes have legible changing edges and rim direction.
        _shadowBlockerKinds[0] = BasicShadowShapeKind.Rectangle;
        _shadowBlockerKinds[1] = BasicShadowShapeKind.Triangle;
        _shadowBlockerKinds[2] = BasicShadowShapeKind.Trapezoid;
        _blockerAngles[0] = 25;
        _blockerAngles[1] = 0;
        _blockerAngles[2] = 330;
        _blockerRotationModes[0] = BasicBlockerRotationMode.Static;
        _blockerRotationModes[1] = BasicBlockerRotationMode.Clockwise;
        _blockerRotationModes[2] = BasicBlockerRotationMode.Oscillate;
        _blockerAngularSpeeds[1] = 55;
        _blockerAngularSpeeds[2] = 60;
        _blockerRotationSweeps[2] = 55;
        _blockerMotionModes[0] = BasicBlockerMotionMode.Vertical;
        _blockerMotionModes[2] = BasicBlockerMotionMode.Horizontal;
        _blockerMotionSpeeds[0] = .22;
        _blockerMotionSpeeds[2] = .32;
        _blockerMotionDistances[0] = .09;
        _blockerMotionDistances[2] = .07;
        for (int i = 0; i < ShadowBlockerCount; i++)
        {
            AnchorMotionAtCurrentPosition(i);
            AnchorRotationAtCurrentAngle(i);
        }
        BlockerMotionPlaying = true;
        SelectedShadowBlockerIndex = 2;
        SelectedLightIndex = 1;
        DebugView = BasicLightingDebugView.Final;
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
