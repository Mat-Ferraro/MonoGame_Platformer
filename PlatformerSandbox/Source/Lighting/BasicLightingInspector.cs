using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using PlatformerSandbox.Gyms;

namespace PlatformerSandbox.Lighting;

/// <summary>
/// Collapsible inspector with Lights, Surfaces and two Shadow edit pages.
/// Captures the entire mouse gesture so dropdown/slider clicks never move a
/// world light. Does not require a third-party UI framework.
/// </summary>
internal sealed class BasicLightingInspector
{
    private enum Tab { Lights, Surfaces, Shadows }
    private enum ShadowPage { Geometry, Routing }
    private enum DropDown { None, Light, Color, View, Surface, Mode, Channel, Shading,
        Blocker, BlockerShape, WallOverlap, LightDepth, BlockerDepth }
    private enum DragTarget
    {
        None, Ambient, Intensity, Radius, Opacity, NormalStrength, RimStrength,
        ShadowX, ShadowY, ShadowWidth, ShadowHeight
    }
    private static readonly BasicLightWallOverlapMode[] WallOverlapModes =
        Enum.GetValues<BasicLightWallOverlapMode>();
    private static readonly BasicLightingDepthLayer[] DepthLayers =
        Enum.GetValues<BasicLightingDepthLayer>();
    private static readonly BasicShadowShapeKind[] ShadowShapes =
        Enum.GetValues<BasicShadowShapeKind>();
    private static readonly BasicLightColorPreset[] Colors = Enum.GetValues<BasicLightColorPreset>();
    private static readonly BasicLightingDebugView[] Views = Enum.GetValues<BasicLightingDebugView>();
    private static readonly BasicSurfaceLightingMode[] Modes = Enum.GetValues<BasicSurfaceLightingMode>();
    private static readonly BasicSurfaceShadingMode[] Shadings = Enum.GetValues<BasicSurfaceShadingMode>();
    private static readonly BasicLightReceiverChannels[] Channels =
    [BasicLightReceiverChannels.Both, BasicLightReceiverChannels.Light1,
     BasicLightReceiverChannels.Light2, BasicLightReceiverChannels.None];

    private bool _expanded = true;
    private bool _previousDown;
    private bool _consumeUntilRelease;
    private DropDown _dropDown;
    private DragTarget _dragTarget;
    private Tab _tab;
    private ShadowPage _shadowPage;

    public bool Expanded => _expanded;

    private static Rectangle Panel(int width, int height, bool expanded)
    {
        int panelWidth = Math.Min(320, Math.Max(180, width - 20));
        return new Rectangle(Math.Max(2, width - panelWidth - 12),
            height >= 460 ? 12 : 3, panelWidth, expanded ? Math.Min(558, Math.Max(400, height - 24)) : 30);
    }

    private static Rectangle Row(Rectangle panel, int offsetY) =>
        new(panel.X + 12, panel.Y + offsetY, panel.Width - 24, 24);

    private static Rectangle Slider(Rectangle panel, int offsetY) =>
        new(panel.X + 112, panel.Y + offsetY, Math.Max(35, panel.Width - 128), 14);

    private static Rectangle Header(Rectangle panel) =>
        new(panel.X, panel.Y, panel.Width, 30);

    private static Rectangle LightTabButton(Rectangle panel) =>
        new(panel.X + 12, panel.Y + 35, (panel.Width - 24) / 3, 24);

    private static Rectangle SurfaceTabButton(Rectangle panel) =>
        new(panel.X + 12 + (panel.Width - 24) / 3, panel.Y + 35,
            (panel.Width - 24) / 3, 24);

    private static Rectangle ShadowTabButton(Rectangle panel) =>
        new(panel.X + 12 + 2 * ((panel.Width - 24) / 3), panel.Y + 35,
            (panel.Width - 24) - 2 * ((panel.Width - 24) / 3), 24);

    private Rectangle DropDownAnchor(Rectangle panel) => _dropDown switch
    {
        DropDown.View => Row(panel, 128),
        DropDown.Light => Row(panel, 176),
        DropDown.Color => Row(panel, 250),
        DropDown.Surface => Row(panel, 93),
        DropDown.Mode => Row(panel, 149),
        DropDown.Channel => Row(panel, 205),
        DropDown.Shading => Row(panel, 261),
        DropDown.Blocker => Row(panel, 118),
        DropDown.BlockerShape => Row(panel, 174),
        DropDown.WallOverlap => Row(panel, 369),
        DropDown.LightDepth => Row(panel, 263),
        DropDown.BlockerDepth => Row(panel, 320),
        _ => Rectangle.Empty
    };

    private int OptionCount => _dropDown switch
    {
        DropDown.Light => 2,
        DropDown.Color => Colors.Length,
        DropDown.View => Views.Length,
        DropDown.Surface => BasicLightingPreviewState.SurfaceCount,
        DropDown.Mode => Modes.Length,
        DropDown.Channel => Channels.Length,
        DropDown.Shading => Shadings.Length,
        DropDown.Blocker => BasicLightingPreviewState.ShadowBlockerCount,
        DropDown.BlockerShape => ShadowShapes.Length,
        DropDown.WallOverlap => WallOverlapModes.Length,
        DropDown.LightDepth or DropDown.BlockerDepth => DepthLayers.Length,
        _ => 0
    };

    private void ChooseOption(int index, BasicLightingPreviewState state)
    {
        switch (_dropDown)
        {
            case DropDown.Light: state.SelectLight(index); break;
            case DropDown.Color: state.SetLightColor(Colors[index]); break;
            case DropDown.View: state.SetDebugView(Views[index]); break;
            case DropDown.Surface: state.SelectSurface(index); break;
            case DropDown.Mode: state.SetSelectedSurfaceMode(Modes[index]); break;
            case DropDown.Channel: state.SetSelectedSurfaceChannels(Channels[index]); break;
            case DropDown.Shading: state.SetSelectedSurfaceShading(Shadings[index]); break;
            case DropDown.Blocker: state.SelectShadowBlocker(index); break;
            case DropDown.BlockerShape: state.SetSelectedShadowBlockerKind(ShadowShapes[index]); break;
            case DropDown.WallOverlap: state.SetSelectedLightWallOverlapMode(WallOverlapModes[index]); break;
            case DropDown.LightDepth: state.SetSelectedLightDepthLayer(DepthLayers[index]); break;
            case DropDown.BlockerDepth: state.SetSelectedShadowBlockerDepthLayer(DepthLayers[index]); break;
        }
        _dropDown = DropDown.None;
    }

    private void ApplySlider(int mouseX, Rectangle panel, BasicLightingPreviewState state)
    {
        Rectangle bar = _dragTarget switch
        {
            DragTarget.Ambient => Slider(panel, 94),
            DragTarget.Intensity => Slider(panel, 304),
            DragTarget.Radius => Slider(panel, 341),
            DragTarget.Opacity => Slider(panel, 321),
            DragTarget.NormalStrength => Slider(panel, 365),
            DragTarget.RimStrength => Slider(panel, 410),
            DragTarget.ShadowX => Slider(panel, 337),
            DragTarget.ShadowY => Slider(panel, 375),
            DragTarget.ShadowWidth => Slider(panel, 413),
            DragTarget.ShadowHeight => Slider(panel, 451),
            _ => Rectangle.Empty
        };
        if (bar.Width <= 0) return;
        double fraction = Math.Clamp((mouseX - bar.X) / (double)bar.Width, 0, 1);
        switch (_dragTarget)
        {
            case DragTarget.Ambient: state.SetAmbient(fraction); break;
            case DragTarget.Intensity:
                state.SetLightIntensity(fraction * BasicLightingPreviewState.MaximumIntensity); break;
            case DragTarget.Radius:
                state.SetLightRadius(BasicLightingPreviewState.MinimumRadius + fraction *
                    (BasicLightingPreviewState.MaximumRadius - BasicLightingPreviewState.MinimumRadius));
                break;
            case DragTarget.Opacity: state.SetSelectedSurfaceOpacity(fraction); break;
            case DragTarget.NormalStrength: state.SetSelectedSurfaceNormalStrength(fraction); break;
            case DragTarget.RimStrength: state.SetSelectedSurfaceRimStrength(fraction * 2); break;
            case DragTarget.ShadowX: state.SetSelectedShadowBlockerPosition(
                fraction * (1 - state.SelectedShadowBlocker.Width), state.SelectedShadowBlocker.Y); break;
            case DragTarget.ShadowY: state.SetSelectedShadowBlockerPosition(
                state.SelectedShadowBlocker.X, fraction * (1 - state.SelectedShadowBlocker.Height)); break;
            case DragTarget.ShadowWidth: state.SetSelectedShadowBlockerSize(
                .025 + fraction * .175, state.SelectedShadowBlocker.Height); break;
            case DragTarget.ShadowHeight: state.SetSelectedShadowBlockerSize(
                state.SelectedShadowBlocker.Width, .08 + fraction * .47); break;
        }
    }

    /// <returns>True if the UI owns the mouse; do not move a world light.</returns>
    public bool Update(MouseState mouse, int width, int height,
        BasicLightingPreviewState state, bool interactive = true)
    {
        bool down = mouse.LeftButton == ButtonState.Pressed;
        bool clicked = down && !_previousDown;
        _previousDown = down;
        if (!down) _consumeUntilRelease = false;
        if (!interactive)
        {
            _dragTarget = DragTarget.None;
            return true;
        }
        Rectangle panel = Panel(width, height, _expanded);
        Point at = new(mouse.X, mouse.Y);
        if (!down) _dragTarget = DragTarget.None;
        if (_dragTarget != DragTarget.None && down)
        {
            ApplySlider(mouse.X, panel, state);
            return true;
        }
        if (_consumeUntilRelease) return true;
        if (clicked && Header(panel).Contains(at))
        {
            _expanded = !_expanded;
            _dropDown = DropDown.None;
            _consumeUntilRelease = true;
            return true;
        }
        if (!_expanded) return panel.Contains(at);
        if (_dropDown != DropDown.None && clicked)
        {
            Rectangle anchor = DropDownAnchor(panel);
            for (int i = 0; i < OptionCount; i++)
                if (new Rectangle(anchor.X, anchor.Bottom + i * 23, anchor.Width, 23).Contains(at))
                {
                    ChooseOption(i, state);
                    _consumeUntilRelease = true;
                    return true;
                }
            _dropDown = DropDown.None;
            _consumeUntilRelease = true;
            return true;
        }
        if (clicked && LightTabButton(panel).Contains(at))
        {
            _tab = Tab.Lights;
            _dropDown = DropDown.None;
            _consumeUntilRelease = true;
            return true;
        }
        if (clicked && SurfaceTabButton(panel).Contains(at))
        {
            _tab = Tab.Surfaces;
            _dropDown = DropDown.None;
            _consumeUntilRelease = true;
            return true;
        }
        if (clicked && ShadowTabButton(panel).Contains(at))
        {
            _tab = Tab.Shadows;
            _dropDown = DropDown.None;
            _consumeUntilRelease = true;
            return true;
        }
        if (clicked && panel.Contains(at))
        {
            _consumeUntilRelease = true;
            if (_tab == Tab.Lights)
            {
                if (Row(panel, 128).Contains(at)) _dropDown = DropDown.View;
                else if (Row(panel, 176).Contains(at)) _dropDown = DropDown.Light;
                else if (Row(panel, 250).Contains(at)) _dropDown = DropDown.Color;
                else if (Row(panel, 208).Contains(at)) state.ToggleLight();
                else if (Row(panel, 373).Contains(at)) state.Reset();
                else if (Row(panel, 89).Contains(at)) _dragTarget = DragTarget.Ambient;
                else if (Row(panel, 299).Contains(at)) _dragTarget = DragTarget.Intensity;
                else if (Row(panel, 336).Contains(at)) _dragTarget = DragTarget.Radius;
            }
            else if (_tab == Tab.Surfaces)
            {
                if (Row(panel, 93).Contains(at)) _dropDown = DropDown.Surface;
                else if (Row(panel, 149).Contains(at)) _dropDown = DropDown.Mode;
                else if (Row(panel, 205).Contains(at)) _dropDown = DropDown.Channel;
                else if (Row(panel, 261).Contains(at)) _dropDown = DropDown.Shading;
                else if (Row(panel, 459).Contains(at)) state.ResetSelectedSurface();
                else if (Row(panel, 497).Contains(at)) state.LoadDirectionalComparison();
                else if (Row(panel, 316).Contains(at)) _dragTarget = DragTarget.Opacity;
                else if (Row(panel, 360).Contains(at)) _dragTarget = DragTarget.NormalStrength;
                else if (Row(panel, 405).Contains(at)) _dragTarget = DragTarget.RimStrength;
            }
            else if (_tab == Tab.Shadows)
            {
                if (ShadowGeometryButton(panel).Contains(at))
                {
                    _shadowPage = ShadowPage.Geometry;
                    _dropDown = DropDown.None;
                }
                else if (ShadowRoutingButton(panel).Contains(at))
                {
                    _shadowPage = ShadowPage.Routing;
                    _dropDown = DropDown.None;
                }
                else if (_shadowPage == ShadowPage.Geometry)
                {
                    if (Row(panel, 118).Contains(at)) _dropDown = DropDown.Blocker;
                    else if (Row(panel, 174).Contains(at)) _dropDown = DropDown.BlockerShape;
                    else if (Row(panel, 215).Contains(at))
                        state.SetSelectedShadowBlockerEnabled(!state.SelectedShadowBlockerEnabled);
                    else if (AddBlockerButton(panel).Contains(at)) state.AddShadowBlocker();
                    else if (RemoveBlockerButton(panel).Contains(at)) state.RemoveSelectedShadowBlocker();
                    else if (Row(panel, 321).Contains(at)) _dragTarget = DragTarget.ShadowX;
                    else if (Row(panel, 359).Contains(at)) _dragTarget = DragTarget.ShadowY;
                    else if (Row(panel, 397).Contains(at)) _dragTarget = DragTarget.ShadowWidth;
                    else if (Row(panel, 435).Contains(at)) _dragTarget = DragTarget.ShadowHeight;
                    else if (PresetButton(panel, 0).Contains(at)) state.LoadShadowComparison();
                    else if (PresetButton(panel, 1).Contains(at)) state.LoadMultiShadowComparison();
                    else if (PresetButton(panel, 2).Contains(at)) state.LoadPolygonShadowComparison();
                    else if (PresetButton(panel, 3).Contains(at)) state.ResetShadows();
                }
                else
                {
                    if (Row(panel, 145).Contains(at))
                        state.SetSelectedLightCastsShadows(!state.SelectedLightCastsShadows);
                    else if (Row(panel, 205).Contains(at))
                        state.SetSelectedSurfaceReceivesShadows(!state.SelectedSurfaceReceivesShadows);
                    else if (Row(panel, 263).Contains(at)) _dropDown = DropDown.LightDepth;
                    else if (Row(panel, 320).Contains(at)) _dropDown = DropDown.BlockerDepth;
                    else if (Row(panel, 369).Contains(at)) _dropDown = DropDown.WallOverlap;
                    else if (Row(panel, 437).Contains(at)) state.LoadWallOverlapComparison();
                    else if (Row(panel, 469).Contains(at)) state.LoadDepthRimComparison();
                    else if (Row(panel, 505).Contains(at)) state.ToggleBlockerOutlines();
                }
            }
            if (_dragTarget != DragTarget.None) ApplySlider(mouse.X, panel, state);
        }
        return panel.Contains(at);
    }

    private static Rectangle ShadowGeometryButton(Rectangle panel) =>
        new(panel.X + 12, panel.Y + 70, (panel.Width - 24) / 2, 24);

    private static Rectangle ShadowRoutingButton(Rectangle panel) =>
        new(panel.X + 12 + (panel.Width - 24) / 2, panel.Y + 70,
            (panel.Width - 24) - (panel.Width - 24) / 2, 24);

    private static Rectangle AddBlockerButton(Rectangle panel) =>
        new(panel.X + 12, panel.Y + 253, (panel.Width - 32) / 2, 24);

    private static Rectangle RemoveBlockerButton(Rectangle panel) =>
        new(panel.X + 20 + (panel.Width - 32) / 2, panel.Y + 253,
            (panel.Width - 32) / 2, 24);

    private static Rectangle PresetButton(Rectangle panel, int index)
    {
        int total = panel.Width - 24;
        int left = panel.X + 12 + index * (total / 4);
        return new Rectangle(left, panel.Y + 496, index == 3 ? total - 3 * (total / 4) : total / 4, 24);
    }

    private static readonly Color Back = new(14, 20, 30, 244);
    private static readonly Color Control = new(39, 52, 68, 255);
    private static readonly Color White = new(228, 236, 244);
    private static readonly Color Muted = new(164, 188, 209);
    private static readonly Color Accent = new(105, 219, 176);

    private static void Fill(SpriteBatch batch, Texture2D pixel, Rectangle rect, Color color)
    {
        if (rect.Width > 0 && rect.Height > 0) batch.Draw(pixel, rect, color);
    }

    private static void Print(SpriteBatch batch, Texture2D pixel, int x, int y,
        string value, Color color, int scale = 2) =>
        GymDebugText.Draw(batch, pixel, x, y, value, color, scale);

    private static void Drop(SpriteBatch batch, Texture2D pixel,
        Rectangle rectangle, string value, bool active)
    {
        Fill(batch, pixel, rectangle, active ? new Color(55, 76, 91) : Control);
        Print(batch, pixel, rectangle.X + 7, rectangle.Y + 7, value, White);
        Print(batch, pixel, rectangle.Right - 17, rectangle.Y + 7, "V", Accent);
    }

    private static void DrawSlider(SpriteBatch batch, Texture2D pixel,
        Rectangle bar, double fraction)
    {
        Fill(batch, pixel, new Rectangle(bar.X, bar.Y + 5, bar.Width, 4), Control);
        int fill = (int)Math.Round(Math.Clamp(fraction, 0, 1) * bar.Width);
        Fill(batch, pixel, new Rectangle(bar.X, bar.Y + 5, fill, 4), Accent);
        Fill(batch, pixel, new Rectangle(bar.X + Math.Clamp(fill - 4, 0, bar.Width - 8),
            bar.Y + 1, 8, 12), White);
    }

    public void Draw(SpriteBatch batch, Texture2D pixel, int width, int height,
        BasicLightingPreviewState state)
    {
        Rectangle panel = Panel(width, height, _expanded);
        Fill(batch, pixel, panel, Back);
        Fill(batch, pixel, new Rectangle(panel.X, panel.Y, 3, panel.Height), Accent);
        Print(batch, pixel, panel.X + 12, panel.Y + 10, "LIGHTING INSPECTOR", Accent);
        Print(batch, pixel, panel.Right - 58, panel.Y + 10,
            _expanded ? "HIDE" : "SHOW", White);
        if (!_expanded) return;

        Rectangle lightTab = LightTabButton(panel);
        Rectangle surfaceTab = SurfaceTabButton(panel);
        Rectangle shadowTab = ShadowTabButton(panel);
        Fill(batch, pixel, lightTab, _tab == Tab.Lights ? new Color(47, 86, 81) : Control);
        Fill(batch, pixel, surfaceTab, _tab == Tab.Surfaces ? new Color(47, 86, 81) : Control);
        Fill(batch, pixel, shadowTab, _tab == Tab.Shadows ? new Color(47, 86, 81) : Control);
        Print(batch, pixel, lightTab.X + 9, lightTab.Y + 8, "LIGHTS", White, 1);
        Print(batch, pixel, surfaceTab.X + 9, surfaceTab.Y + 8, "SURFACES", White, 1);
        Print(batch, pixel, shadowTab.X + 9, shadowTab.Y + 8, "SHADOWS", White, 1);

        if (_tab == Tab.Lights) DrawLightControls(batch, pixel, panel, state);
        else if (_tab == Tab.Surfaces) DrawSurfaceControls(batch, pixel, panel, state);
        else DrawShadowControls(batch, pixel, panel, state);
        DrawOpenedDropdown(batch, pixel, panel);
    }

    private void DrawLightControls(SpriteBatch batch, Texture2D pixel,
        Rectangle panel, BasicLightingPreviewState state)
    {
        int x = panel.X + 13, y = panel.Y;
        Print(batch, pixel, x, y + 81, "AMBIENT", Muted);
        Print(batch, pixel, panel.Right - 54, y + 81, state.Ambient.ToString("0.00"), White);
        DrawSlider(batch, pixel, Slider(panel, 94), state.Ambient);

        Print(batch, pixel, x, y + 112, "RENDER VIEW", Muted);
        Drop(batch, pixel, Row(panel, 128), ViewText(state.DebugView), _dropDown == DropDown.View);

        Print(batch, pixel, x, y + 160, "SELECT LIGHT", Muted);
        Drop(batch, pixel, Row(panel, 176), "LIGHT " + (state.SelectedLightIndex + 1),
            _dropDown == DropDown.Light);

        Print(batch, pixel, x, y + 216, "LIGHT ENABLED", Muted);
        Rectangle toggle = new(panel.Right - 72, y + 208, 60, 24);
        Fill(batch, pixel, toggle, state.LightEnabled ? new Color(40, 103, 75) : Control);
        Print(batch, pixel, toggle.X + 16, toggle.Y + 8, state.LightEnabled ? "ON" : "OFF", White);

        Print(batch, pixel, x, y + 234, "LIGHT COLOR", Muted);
        Drop(batch, pixel, Row(panel, 250), state.LightColor.ToString().ToUpperInvariant(),
            _dropDown == DropDown.Color);
        LightRgb rgb = BasicLightingPreviewState.PresetRgb(state.LightColor);
        Fill(batch, pixel, new Rectangle(panel.Right - 44, y + 237, 30, 8),
            new Color((float)rgb.R, (float)rgb.G, (float)rgb.B));

        Print(batch, pixel, x, y + 290, "INTENSITY", Muted);
        Print(batch, pixel, panel.Right - 54, y + 290, state.LightIntensity.ToString("0.00"), White);
        DrawSlider(batch, pixel, Slider(panel, 304),
            state.LightIntensity / BasicLightingPreviewState.MaximumIntensity);

        Print(batch, pixel, x, y + 327, "RADIUS", Muted);
        Print(batch, pixel, panel.Right - 54, y + 327, state.LightRadius.ToString("0.00"), White);
        DrawSlider(batch, pixel, Slider(panel, 341),
            (state.LightRadius - BasicLightingPreviewState.MinimumRadius) /
            (BasicLightingPreviewState.MaximumRadius - BasicLightingPreviewState.MinimumRadius));

        Rectangle reset = Row(panel, 373);
        Fill(batch, pixel, reset, Control);
        Print(batch, pixel, reset.X + 11, reset.Y + 8, "RESET ALL LIGHTING", White);
        Print(batch, pixel, x, y + 407, "DRAG SCENE TO MOVE LIGHT", Muted, scale: 1);
    }

    private void DrawSurfaceControls(SpriteBatch batch, Texture2D pixel,
        Rectangle panel, BasicLightingPreviewState state)
    {
        int x = panel.X + 13, y = panel.Y;
        BasicSurfaceMaterial surface = state.SelectedSurface;
        Print(batch, pixel, x, y + 75, "SELECT SAMPLE", Muted);
        Drop(batch, pixel, Row(panel, 93), "SAMPLE " + (char)('A' + state.SelectedSurfaceIndex),
            _dropDown == DropDown.Surface);
        Print(batch, pixel, x, y + 133, "LIGHTING MODE", Muted);
        Drop(batch, pixel, Row(panel, 149), surface.Mode.ToString().ToUpperInvariant(),
            _dropDown == DropDown.Mode);
        Print(batch, pixel, x, y + 189, "RECEIVES LIGHT", Muted);
        Drop(batch, pixel, Row(panel, 205), ChannelText(surface.Receives),
            _dropDown == DropDown.Channel);
        Print(batch, pixel, x, y + 245, "SURFACE SHADING", Muted);
        Drop(batch, pixel, Row(panel, 261), surface.Shading.ToString().ToUpperInvariant(),
            _dropDown == DropDown.Shading);
        Print(batch, pixel, x, y + 304, "OPACITY", Muted);
        Print(batch, pixel, panel.Right - 60, y + 304, surface.Opacity.ToString("0.00"), White);
        DrawSlider(batch, pixel, Slider(panel, 321), surface.Opacity);
        Print(batch, pixel, x, y + 348, "NORMAL", Muted);
        Print(batch, pixel, panel.Right - 60, y + 348, surface.NormalStrength.ToString("0.00"), White);
        DrawSlider(batch, pixel, Slider(panel, 365), surface.NormalStrength);
        Print(batch, pixel, x, y + 393, "RIM", Muted);
        Print(batch, pixel, panel.Right - 60, y + 393, surface.RimStrength.ToString("0.00"), White);
        DrawSlider(batch, pixel, Slider(panel, 410), surface.RimStrength / 2.0);
        Fill(batch, pixel, Row(panel, 459), Control);
        Print(batch, pixel, x + 8, y + 466, "RESET SELECTED SAMPLE", White);
        Fill(batch, pixel, Row(panel, 497), new Color(35, 78, 71));
        Print(batch, pixel, x + 8, y + 504, "LOAD SHADING COMPARISON", White);
    }

    private void DrawShadowControls(SpriteBatch batch, Texture2D pixel,
        Rectangle panel, BasicLightingPreviewState state)
    {
        int x = panel.X + 13, y = panel.Y;
        Rectangle geometry = ShadowGeometryButton(panel);
        Rectangle routing = ShadowRoutingButton(panel);
        Fill(batch, pixel, geometry, _shadowPage == ShadowPage.Geometry ? new Color(47, 86, 81) : Control);
        Fill(batch, pixel, routing, _shadowPage == ShadowPage.Routing ? new Color(47, 86, 81) : Control);
        Print(batch, pixel, geometry.X + 8, geometry.Y + 8, "BLOCKERS", White, 1);
        Print(batch, pixel, routing.X + 8, routing.Y + 8, "LIGHT RULES", White, 1);
        if (_shadowPage == ShadowPage.Routing)
        {
            Print(batch, pixel, x, y + 124,
                "LIGHT " + (state.SelectedLightIndex + 1) + " CASTS SHADOWS", Muted);
            DrawShadowToggle(batch, pixel, Row(panel, 145), state.SelectedLightCastsShadows);
            Print(batch, pixel, x, y + 184,
                "SAMPLE " + (char)('A' + state.SelectedSurfaceIndex) + " RECEIVES SHADOWS", Muted);
            DrawShadowToggle(batch, pixel, Row(panel, 205), state.SelectedSurfaceReceivesShadows);
            Print(batch, pixel, x, y + 244, "LIGHT " + (state.SelectedLightIndex + 1) + " DEPTH LAYER", Muted, 1);
            Drop(batch, pixel, Row(panel, 263), DepthText(state.SelectedLightDepthLayer),
                _dropDown == DropDown.LightDepth);
            Print(batch, pixel, x, y + 302,
                "BLOCKER " + (state.SelectedShadowBlockerIndex + 1) + " DEPTH LAYER", Muted, 1);
            Drop(batch, pixel, Row(panel, 320), DepthText(state.SelectedShadowBlockerDepthLayer),
                _dropDown == DropDown.BlockerDepth);
            Print(batch, pixel, x, y + 350, "IF SAME DEPTH + ORIGIN INSIDE", Muted, 1);
            Drop(batch, pixel, Row(panel, 369),
                WallOverlapText(state.SelectedLightWallOverlapMode), _dropDown == DropDown.WallOverlap);
            Print(batch, pixel, x, y + 405, "LOWER LIGHT = BACK RIM / NO FACE", Accent, 1);
            Fill(batch, pixel, Row(panel, 437), new Color(35, 78, 71));
            Print(batch, pixel, x + 8, y + 445, "LOAD FRONT / BEHIND", White, 1);
            Fill(batch, pixel, Row(panel, 469), new Color(35, 78, 71));
            Print(batch, pixel, x + 8, y + 477, "LOAD DEPTH RIM DEMO", White, 1);
            DrawShadowToggle(batch, pixel, Row(panel, 505), state.ShowBlockerOutlines);
            Print(batch, pixel, x, y + 514, "SHOW WALL DEBUG OUTLINES", Muted, 1);
            return;
        }

        Print(batch, pixel, x, y + 101, "SELECT SHADOW BLOCKER", Muted);
        Drop(batch, pixel, Row(panel, 118),
            "BLOCKER " + (state.SelectedShadowBlockerIndex + 1) +
            (state.SelectedShadowBlockerEnabled ? "  ON" : "  OFF"),
            _dropDown == DropDown.Blocker);

        Print(batch, pixel, x, y + 157, "SHAPE", Muted);
        Drop(batch, pixel, Row(panel, 174), state.SelectedShadowBlockerKind.ToString().ToUpperInvariant(),
            _dropDown == DropDown.BlockerShape);
        Print(batch, pixel, x, y + 199, "SELECTED BLOCKER ENABLED", Muted, 1);
        DrawShadowToggle(batch, pixel, Row(panel, 215), state.SelectedShadowBlockerEnabled);

        Fill(batch, pixel, AddBlockerButton(panel), Control);
        Fill(batch, pixel, RemoveBlockerButton(panel), Control);
        Print(batch, pixel, AddBlockerButton(panel).X + 8, y + 261, "ADD WALL", White, 1);
        Print(batch, pixel, RemoveBlockerButton(panel).X + 8, y + 261, "REMOVE", White, 1);
        Print(batch, pixel, x, y + 286,
            "ACTIVE " + state.ActiveShadowBlockerCount + "/" + BasicLightingPreviewState.ShadowBlockerCount +
            "  (GPU BUDGET)", Muted, 1);

        BasicShadowRectangle rect = state.SelectedShadowBlocker;
        Print(batch, pixel, x, y + 310, "X", Muted);
        DrawSlider(batch, pixel, Slider(panel, 337), rect.X / (1 - rect.Width));
        Print(batch, pixel, x, y + 348, "Y", Muted);
        DrawSlider(batch, pixel, Slider(panel, 375), rect.Y / (1 - rect.Height));
        Print(batch, pixel, x, y + 386, "WIDTH", Muted);
        DrawSlider(batch, pixel, Slider(panel, 413), (rect.Width - .025) / .175);
        Print(batch, pixel, x, y + 424, "HEIGHT", Muted);
        DrawSlider(batch, pixel, Slider(panel, 451), (rect.Height - .08) / .47);

        Print(batch, pixel, x, y + 477, "PRESETS", Muted, 1);
        string[] names = ["ONE", "THREE", "POLY", "CLEAR"];
        for (int i = 0; i < names.Length; i++)
        {
            Rectangle button = PresetButton(panel, i);
            Fill(batch, pixel, button, i == 2 ? new Color(35, 78, 71) : Control);
            Print(batch, pixel, button.X + 9, button.Y + 8, names[i], White, 1);
        }
        Print(batch, pixel, x, y + 530, "CLICK WALL TO SELECT + DRAG", Muted, 1);
    }

    private static void DrawShadowToggle(SpriteBatch batch, Texture2D pixel,
        Rectangle row, bool enabled)
    {
        Rectangle toggle = new(row.Right - 60, row.Y, 60, row.Height);
        Fill(batch, pixel, toggle, enabled ? new Color(40, 103, 75) : Control);
        Print(batch, pixel, toggle.X + 16, toggle.Y + 8,
            enabled ? "ON" : "OFF", White);
    }

    private void DrawOpenedDropdown(SpriteBatch batch, Texture2D pixel, Rectangle panel)
    {
        if (_dropDown == DropDown.None) return;
        Rectangle anchor = DropDownAnchor(panel);
        for (int i = 0; i < OptionCount; i++)
        {
            Rectangle option = new(anchor.X, anchor.Bottom + i * 23, anchor.Width, 23);
            Fill(batch, pixel, option, i % 2 == 0 ? new Color(29, 42, 57) : Control);
            string label = _dropDown switch
            {
                DropDown.Light => "LIGHT " + (i + 1),
                DropDown.Color => Colors[i].ToString().ToUpperInvariant(),
                DropDown.View => ViewText(Views[i]),
                DropDown.Surface => "SAMPLE " + (char)('A' + i),
                DropDown.Mode => Modes[i].ToString().ToUpperInvariant(),
                DropDown.Channel => ChannelText(Channels[i]),
                DropDown.Shading => Shadings[i].ToString().ToUpperInvariant(),
                DropDown.Blocker => "BLOCKER " + (i + 1),
                DropDown.BlockerShape => ShadowShapes[i].ToString().ToUpperInvariant(),
                DropDown.WallOverlap => WallOverlapText(WallOverlapModes[i]),
                DropDown.LightDepth or DropDown.BlockerDepth => DepthText(DepthLayers[i]),
                _ => ""
            };
            Print(batch, pixel, option.X + 8, option.Y + 7, label, White);
        }
    }

    private static string DepthText(BasicLightingDepthLayer depth) => depth switch
    {
        BasicLightingDepthLayer.Back => "BACK (LOW)",
        BasicLightingDepthLayer.World => "WORLD (MIDDLE)",
        BasicLightingDepthLayer.Front => "FRONT (HIGH)",
        _ => "UNKNOWN"
    };

    private static string WallOverlapText(BasicLightWallOverlapMode mode) => mode switch
    {
        BasicLightWallOverlapMode.FrontOfWall => "FRONT OF WALL",
        BasicLightWallOverlapMode.BehindWallWithRim => "BEHIND WALL + RIM",
        _ => "UNKNOWN"
    };

    private static string ChannelText(BasicLightReceiverChannels channels) => channels switch
    {
        BasicLightReceiverChannels.Both => "BOTH LIGHTS",
        BasicLightReceiverChannels.Light1 => "LIGHT 1 ONLY",
        BasicLightReceiverChannels.Light2 => "LIGHT 2 ONLY",
        BasicLightReceiverChannels.None => "AMBIENT ONLY",
        _ => "UNKNOWN"
    };

    private static string ViewText(BasicLightingDebugView view) => view switch
    {
        BasicLightingDebugView.Final => "FINAL IMAGE",
        BasicLightingDebugView.Albedo => "ALBEDO",
        BasicLightingDebugView.Illumination => "LIGHT BUFFER",
        BasicLightingDebugView.SelectedLight => "SELECTED LIGHT",
        BasicLightingDebugView.ShadowMask => "SHADOW MASK",
        _ => "UNKNOWN"
    };
}
