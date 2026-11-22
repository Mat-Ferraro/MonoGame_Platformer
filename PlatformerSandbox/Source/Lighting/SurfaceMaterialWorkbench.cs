using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using PlatformerSandbox.Gyms;

namespace PlatformerSandbox.Lighting;

/// <summary>
/// L03A independent GPU material acceptance scene. A single pixel shader
/// handles Flat / Normal / Rim / Hybrid / Unlit; the material profiles own the
/// maps. No gameplay, character motor, collision, or LDtk art is touched.
/// Multiple lighting passes and virtual-depth occlusion belong to the next
/// integration pass; this deliberately tests surface response first.
/// </summary>
internal sealed class SurfaceMaterialWorkbench : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly Effect _effect;
    private readonly SurfaceMaterialCatalog _catalog;
    private readonly Dictionary<string, Texture2D> _textures = new(StringComparer.Ordinal);
    private readonly Texture2D _neutralNormal;
    private readonly Texture2D _black;
    private readonly Texture2D _white;
    private readonly Texture2D _neutralEdge;
    private readonly string _textureFolder;
    private KeyboardState _lastKeys;
    private int _activeMaterial = -1;  // -1: authored side-by-side comparison
    private bool _secondLight = true;
    private bool _showInfo = true;
    private bool _normals = true;
    private bool _rim = true;
    private bool _flip;
    private float _rotation;
    private Point _moveableLight;
    public bool ExitRequested { get; private set; }

    public SurfaceMaterialWorkbench(GraphicsDevice device, ContentManager content, string manifestPath)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        ArgumentNullException.ThrowIfNull(content);
        _catalog = SurfaceMaterialCatalog.FromJson(File.ReadAllText(manifestPath));
        _textureFolder = Path.Combine(Path.GetDirectoryName(manifestPath)!, "Textures");
        // Shader is compiled for DesktopGL by Content/Builder/Builder.cs.
        _effect = content.Load<Effect>("Effects/L03_SurfaceMaterial");
        _neutralNormal = Solid(device, new Color(128,128,255,255));
        _black = Solid(device, Color.Black);
        _white = Solid(device, Color.White);
        _neutralEdge = Solid(device, new Color(128,128,128,0));
        try
        {
            foreach (SurfaceMaterialProfile material in _catalog.Materials)
            {
                RequireTexture(material.Albedo);
                RequireTexture(material.Normal);
                RequireTexture(material.Mask);
                RequireTexture(material.Edge);
                RequireTexture(material.Emission);
            }
        }
        catch { Dispose(); throw; }
        _moveableLight = new Point(device.Viewport.Width / 2, device.Viewport.Height / 2);
        _lastKeys = Keyboard.GetState();
    }

    private static Texture2D Solid(GraphicsDevice device, Color value)
    {
        var tex = new Texture2D(device, 1, 1);
        tex.SetData(new[] { value });
        return tex;
    }

    private Texture2D? RequireTexture(string? filename)
    {
        if (string.IsNullOrEmpty(filename)) return null;
        if (_textures.TryGetValue(filename, out var found)) return found;
        using var stream = File.OpenRead(Path.Combine(_textureFolder, filename));
        Texture2D result = Texture2D.FromStream(_device, stream);
        _textures.Add(filename, result);
        return result;
    }

    private Texture2D MapOr(string? file, Texture2D substitute) =>
        string.IsNullOrWhiteSpace(file) ? substitute : _textures[file];

    private bool Pressed(KeyboardState state, Keys key) =>
        state.IsKeyDown(key) && _lastKeys.IsKeyUp(key);

    public void Update(KeyboardState keyboard, MouseState mouse)
    {
        if (Pressed(keyboard, Keys.Escape)) ExitRequested = true;
        if (Pressed(keyboard, Keys.H)) _showInfo = !_showInfo;
        if (Pressed(keyboard, Keys.C)) _secondLight = !_secondLight;
        if (Pressed(keyboard, Keys.N)) _normals = !_normals;
        if (Pressed(keyboard, Keys.R)) _rim = !_rim;
        if (Pressed(keyboard, Keys.F)) _flip = !_flip;
        if (Pressed(keyboard, Keys.Q)) _rotation -= .25f;
        if (Pressed(keyboard, Keys.E)) _rotation += .25f;
        if (Pressed(keyboard, Keys.D0)) _activeMaterial = -1;
        for (int i=0; i < Math.Min(9,_catalog.Materials.Count); i++)
            if (Pressed(keyboard, (Keys)((int)Keys.D1 + i))) _activeMaterial = i;
        if (mouse.LeftButton == ButtonState.Pressed)
            _moveableLight = new Point(mouse.X, mouse.Y);
        _lastKeys = keyboard;
    }

    private void SetParameter(string name, float value) => _effect.Parameters[name].SetValue(value);
    private void SetParameter(string name, Vector3 value) => _effect.Parameters[name].SetValue(value);
    private void SetParameter(string name, Vector4 value) => _effect.Parameters[name].SetValue(value);
    private void SetParameter(string name, Texture2D texture) => _effect.Parameters[name].SetValue(texture);

    private void BindMaterial(SurfaceMaterialProfile material, Point light0, Point light1,
        int radius, bool useLight1, Rectangle panel)
    {
        SetParameter("NormalMap", MapOr(material.Normal,_neutralNormal));
        SetParameter("EmissionMap", MapOr(material.Emission,_black));
        SetParameter("MaskMap", MapOr(material.Mask,_white));
        SetParameter("EdgeMap", MapOr(material.Edge,_neutralEdge));
        SetParameter("SurfaceMode", (float)material.Response);
        SetParameter("AmbientColor",new Vector3((float)material.Ambient, (float)material.Ambient, (float)material.Ambient));
        SetParameter("NormalStrength",_normals ? (float)material.NormalStrength : 0f);
        SetParameter("RimStrength",_rim ? (float)material.RimStrength : 0f);
        SetParameter("SpecularStrength",(float)material.SpecularStrength);
        SetParameter("SpecularPower",(float)material.SpecularPower);
        SetParameter("EmissionStrength",(float)material.EmissionStrength);
        SetParameter("LightMaskStrength",(float)material.LightMaskStrength);
        SetParameter("FlipTangentX",_flip ? -1f : 1f);
        SetParameter("RotationRadians",_rotation);
        SetParameter("SpriteRectangle",new Vector4(panel.X,panel.Y,panel.Width,panel.Height));
        SetParameter("Light0PositionRadius",new Vector4(light0.X,light0.Y,radius,54));
        SetParameter("Light0ColorIntensity",new Vector4(.28f,.85f,1f,2.4f));
        SetParameter("Light1PositionRadius",new Vector4(light1.X,light1.Y,radius * .7f,42));
        SetParameter("Light1ColorIntensity",new Vector4(1f,.4f,.16f,useLight1 ? 1.6f : 0f));
    }

    public void Draw(SpriteBatch batch, Texture2D pixel)
    {
        int w = _device.Viewport.Width, h = _device.Viewport.Height;
        _device.Clear(new Color(12,16,25));
        var panels = _catalog.Materials;
        // Resize-aware layout. One and the same lighting pair illuminates all
        // authored material profiles; any difference must be a material effect.
        int margin=30, gap=14, labelH=28;
        int available = Math.Max(120,w-margin*2-gap*(panels.Count-1));
        int pw = Math.Max(40,available/panels.Count);
        int ph = Math.Min(230,Math.Max(95,(int)(h*.32)));
        int x0 = Math.Max(4,(w - (pw*panels.Count + gap*(panels.Count-1)))/2);
        int y = Math.Max(125,h/2-ph/2);
        int radius = Math.Max(150,w/3);
        Point second = new(Math.Max(100,w/4),Math.Min(h-50,y+ph/2));

        batch.Begin(samplerState:SamplerState.PointClamp);
        batch.Draw(pixel,new Rectangle(0,0,w,Math.Min(83,h)),new Color(20,29,43));
        batch.End();
        for (int i=0;i<panels.Count;i++)
        {
            SurfaceMaterialProfile material = panels[i];
            int x=x0+i*(pw+gap);
            var rect = new Rectangle(x,y,pw,ph);
            batch.Begin(samplerState:SamplerState.PointClamp);
            batch.Draw(pixel,new Rectangle(x-3,y-3,pw+6,ph+6),
                _activeMaterial == i ? new Color(249,190,92) : new Color(53,71,92));
            batch.End();
            // Set uniforms BEFORE Begin/Draw. Immediate mode guarantees the
            // selected material parameters are not deferred until later panels.
            BindMaterial(material,_moveableLight,second,radius,_secondLight,rect);
            batch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend,
                SamplerState.LinearClamp, DepthStencilState.None,
                RasterizerState.CullNone, _effect);
            batch.Draw(_textures[material.Albedo],rect,null,Color.White,0,Vector2.Zero,
                _flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None,0);
            batch.End();
            batch.Begin(samplerState:SamplerState.PointClamp);
            GymDebugText.Draw(batch,pixel,x,y+ph+labelH/3,material.Id,Color.LightGray,1);
            batch.End();
        }
        batch.Begin(samplerState:SamplerState.PointClamp);
        if (_showInfo)
        {
            GymDebugText.Draw(batch,pixel,14,14,"L03 GPU MATERIAL WORKBENCH",new Color(174,235,255),2);
            GymDebugText.Draw(batch,pixel,14,41,
                "LEFT CLICK LIGHT   C SECOND LIGHT   0 ALL   1-5 SELECT",Color.White,1);
            GymDebugText.Draw(batch,pixel,14,56,
                "N NORMAL  R RIM  F FLIP  Q/E ROTATE NORMALS  H HELP  ESC EXIT",
                Color.White,1);
            GymDebugText.Draw(batch,pixel,14,h-30,
                "ONE SHADER - FLAT / NORMAL / RIM / HYBRID / UNLIT",new Color(174,235,255),1);
        }
        // light handles drawn over the scene; non-lighted UI is independent.
        batch.Draw(pixel,new Rectangle(_moveableLight.X-4,_moveableLight.Y-4,8,8),Color.Cyan);
        if (_secondLight)
            batch.Draw(pixel,new Rectangle(second.X-3,second.Y-3,6,6),Color.Orange);
        batch.End();
    }

    public void Dispose()
    {
        foreach (Texture2D tex in _textures.Values) tex.Dispose();
        _textures.Clear();
        _neutralNormal.Dispose(); _black.Dispose(); _white.Dispose(); _neutralEdge.Dispose();
        // _effect belongs to ContentManager and is disposed by the host.
    }
}
