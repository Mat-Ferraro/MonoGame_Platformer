using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PlatformerSandbox.Gyms;

namespace PlatformerSandbox.Lighting;

/// <summary>Presentation-only, mouse-operated developer controls. Does not touch
/// gameplay simulation and remains available while diagnostic HUD is hidden.</summary>
internal enum LightingToolAction
{
    None, TogglePanel, ToggleToolsPage, ToggleHdr, ToggleEmission, ToggleBloom,
    CycleExposure, CycleBloomThreshold, CycleBloomStrength,
    ToggleLights, NextPreset, NextQuality, ToggleMaterialLights, CycleShadowEdges, ToggleShadowFilter, ToggleShadowQualityPolicy,
    SelectShadowLight, CycleShadowEmitter, ToggleReducedFlashes,
    ToggleStress, ToggleWallMotion, ToggleInspectionLight, CycleHud, ToggleCollision, ToggleBackdrop,
    ToggleAudioMute, VolumeDown, VolumeUp, ExportReport
}

internal sealed class LightingToolsPanel
{
    // Page switching keeps *every* developer control mouse-accessible even
    // at 720p; no hidden keyboard shortcuts or unreachable bottom rows.
    private readonly LightingToolAction[] _lightingActions =
    [
        LightingToolAction.ToggleToolsPage,
        LightingToolAction.ToggleLights,
        LightingToolAction.NextPreset,
        LightingToolAction.NextQuality,
        LightingToolAction.ToggleMaterialLights,
        LightingToolAction.ToggleHdr,
        LightingToolAction.ToggleEmission,
        LightingToolAction.ToggleBloom,
        LightingToolAction.CycleExposure,
        LightingToolAction.CycleBloomThreshold,
        LightingToolAction.CycleBloomStrength,
        LightingToolAction.CycleShadowEdges,
        LightingToolAction.ToggleShadowFilter,
        LightingToolAction.ToggleShadowQualityPolicy,
        LightingToolAction.SelectShadowLight,
        LightingToolAction.CycleShadowEmitter
    ];
    private readonly LightingToolAction[] _systemActions =
    [
        LightingToolAction.ToggleToolsPage,
        LightingToolAction.ToggleReducedFlashes,
        LightingToolAction.ToggleStress,
        LightingToolAction.ToggleWallMotion,
        LightingToolAction.ToggleInspectionLight,
        LightingToolAction.CycleHud,
        LightingToolAction.ToggleCollision,
        LightingToolAction.ToggleBackdrop,
        LightingToolAction.ToggleAudioMute,
        LightingToolAction.VolumeDown,
        LightingToolAction.VolumeUp,
        LightingToolAction.ExportReport
    ];

    private bool _systemPage;
    private LightingToolAction[] Actions => _systemPage ? _systemActions : _lightingActions;
    public bool Open { get; private set; }
    public void Close() { Open = false; _systemPage = false; }

    private static Rectangle Trigger(int width) => new(Math.Max(8, width - 166), 8, 154, 28);
    private int RowHeight(int height) => Math.Clamp((height - 174) / Actions.Length, 19, 30);
    private Rectangle Panel(int width, int height)
    {
        int w = Math.Min(344, Math.Max(180, width - 16));
        int h = 53 + Actions.Length * RowHeight(height) + 94;
        return new Rectangle(Math.Max(4, width - w - 12), 43, w,
            Math.Min(h, Math.Max(1, height - 50)));
    }
    private Rectangle Row(Rectangle panel, int index, int height) =>
        new(panel.Left + 10, panel.Top + 42 + index * RowHeight(height),
            panel.Width - 20, RowHeight(height) - 2);

    public LightingToolAction Hit(int width, int height, int x, int y, bool clicked)
    {
        if (!clicked) return LightingToolAction.None;
        if (Trigger(width).Contains(x, y))
        {
            Open = !Open;
            return LightingToolAction.TogglePanel;
        }
        if (!Open) return LightingToolAction.None;
        Rectangle panel = Panel(width, height);
        if (!panel.Contains(x, y)) return LightingToolAction.None;
        for (int i = 0; i < Actions.Length; i++)
            if (Row(panel, i, height).Contains(x, y))
            {
                LightingToolAction action = Actions[i];
                if (action == LightingToolAction.ToggleToolsPage) _systemPage = !_systemPage;
                return action;
            }
        return LightingToolAction.None;
    }

    public void Draw(SpriteBatch batch, Texture2D pixel, int width, int height,
        bool lights, string preset, string quality, string materialLights,
        bool hdr, bool hdrSupported, bool emission, bool bloom,
        string exposure, string bloomThreshold, string bloomStrength,
        string shadowEdge,
        string shadowFilter, string shadowQualityPolicy, string shadowLight, string shadowEmitter,
        bool reducedFlashes,
        bool stress, bool wallMotion, bool inspectionLight, string hud, bool collision, bool backdrop,
        bool mute, int masterVolume, IReadOnlyList<string> lightStatuses)
    {
        Rectangle trigger = Trigger(width);
        batch.Draw(pixel, trigger, new Color(35, 87, 83));
        GymDebugText.Draw(batch, pixel, trigger.X + 10, trigger.Y + 8,
            Open ? "CLOSE TOOLS" : "DEV TOOLS", new Color(214, 248, 234), 2);
        if (!Open) return;
        Rectangle panel = Panel(width, height);
        batch.Draw(pixel, panel, new Color(16, 26, 39) * .98f);
        batch.Draw(pixel, new Rectangle(panel.X, panel.Y, 3, panel.Height), new Color(94, 220, 178));
        GymDebugText.Draw(batch, pixel, panel.X + 11, panel.Y + 10,
            _systemPage ? "SYSTEM / DIAGNOSTICS" : "LIGHTING / POST FX",
            new Color(107, 223, 188), 1);

        string[] labels = _systemPage ?
        [
            "PAGE: SYSTEM  (CLICK FOR LIGHTING)",
            "REDUCE FLASH: " + (reducedFlashes ? "ON" : "OFF"),
            "STRESS LIGHTS: " + (stress ? "ON" : "OFF"),
            "MOVING WALLS: " + (wallMotion ? "PLAY" : "PAUSE"),
            "MOUSE LAMP: " + (inspectionLight ? "ON" : "OFF"),
            "HUD: " + hud,
            "COLLISION: " + (collision ? "ON" : "OFF"),
            "BACKDROP: " + (backdrop ? "ON" : "OFF"),
            "AUDIO MUTE: " + (mute ? "ON" : "OFF"),
            "VOLUME DOWN (" + masterVolume + " PCT)",
            "VOLUME UP (" + masterVolume + " PCT)",
            "EXPORT CRASH REPORT ZIP"
        ] :
        [
            "PAGE: LIGHTING  (CLICK FOR SYSTEM)",
            "LIGHTING: " + (lights ? "ON" : "OFF"),
            "LOOK: " + preset,
            "QUALITY: " + quality,
            "MATERIAL LIGHTS: " + materialLights,
            "HDR: " + (hdr ? (hdrSupported ? "ON FLOAT16" : "REQUESTED / FALLBACK") : "OFF"),
            "EMISSION: " + (emission ? "ON" : "OFF"),
            "BLOOM: " + (bloom ? "ON" : "OFF"),
            "EXPOSURE: " + exposure,
            "BLOOM THRESHOLD: " + bloomThreshold,
            "BLOOM GAIN: " + bloomStrength,
            "SHADOW EDGE: " + shadowEdge,
            "SHADOW FILTER: " + shadowFilter,
            "SHADOW QUALITY: " + shadowQualityPolicy,
            "EDIT LIGHT: " + shadowLight,
            "LIGHT SIZE: " + shadowEmitter
        ];
        for (int i = 0; i < Actions.Length; i++)
        {
            Rectangle r = Row(panel, i, height);
            if (r.Bottom > panel.Bottom - 70) break;
            batch.Draw(pixel, r, i % 2 == 0 ? new Color(38, 61, 76) : new Color(30, 48, 65));
            GymDebugText.Draw(batch, pixel, r.X + 8, r.Y + Math.Max(3, (r.Height - 7) / 2),
                labels[i], new Color(221, 231, 234), 1);
        }
        int statusY = Math.Min(panel.Bottom - 69, panel.Y + 45 + Actions.Length * RowHeight(height));
        GymDebugText.Draw(batch, pixel, panel.X + 12, statusY,
            "AUTHORED LIGHT STATUS", new Color(114, 223, 178), 1);
        for (int i = 0; i < Math.Min(lightStatuses.Count, 4); i++)
            GymDebugText.Draw(batch, pixel, panel.X + 12, statusY + 13 * (i + 1),
                lightStatuses[i], new Color(196, 211, 231), 1);
    }
}
