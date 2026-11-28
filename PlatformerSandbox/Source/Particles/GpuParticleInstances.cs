using System;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PlatformerSandbox.Particles;

/// <summary>Bounded opt-in raster instancing. No compute API or gameplay ownership.</summary>
internal sealed class GpuParticleInstances : IDisposable
{
    public const int MaximumInstances=50000;
    [StructLayout(LayoutKind.Sequential)]
    internal struct Instance : IVertexType
    {
        public Vector4 Data;
        public Color Tint;
        public static readonly VertexDeclaration Declaration=new(
            new VertexElement(0,VertexElementFormat.Vector4,VertexElementUsage.TextureCoordinate,1),
            new VertexElement(16,VertexElementFormat.Color,VertexElementUsage.Color,1));
        VertexDeclaration IVertexType.VertexDeclaration=>Declaration;
        public Instance(Vector4 data,Color tint) {Data=data; Tint=tint;}
    }
    private readonly GraphicsDevice _device;
    private readonly Effect? _effect;
    private VertexBuffer? _quad;
    private IndexBuffer? _indices;
    private DynamicVertexBuffer? _instances,_stress;
    private int _stressCount;
    private bool _failed;
    public int Draws {get;private set;}
    public int Instances {get;private set;}
    public int Fallbacks {get;private set;}
    public int AnalyticInstances {get;private set;}
    public long UploadedBytes {get;private set;}
    public string Status=>_failed?"UNAVAILABLE / CPU FALLBACK":_effect is null?"EFFECT UNAVAILABLE":Draws>0?"INSTANCED":"NOT EXERCISED";
    public GpuParticleInstances(GraphicsDevice device,Effect? effect)
    {_device=device; _effect=effect; _device.DeviceResetting+=Resetting;}
    private void Resetting(object? s,EventArgs e) {Release();_failed=false;}
    public void BeginFrame(bool enabled)
    {Draws=Instances=Fallbacks=AnalyticInstances=0; UploadedBytes=0;if(!enabled) Release();}
    private void Release()
    {_quad?.Dispose();_indices?.Dispose();_instances?.Dispose();_stress?.Dispose();_quad=null;_indices=null;_instances=null;_stress=null;_stressCount=0;}
    private static bool Recoverable(Exception e)=>e is NotSupportedException or InvalidOperationException or ArgumentException;
    public bool TryDraw(Instance[] values,int count,Texture2D texture,BlendState blend,bool analytic,float time)
    {
        if(count==0)return true;
        if(_effect is null || _failed || count>MaximumInstances || !_device.Adapter.IsProfileSupported(GraphicsProfile.HiDef))
        {Fallbacks++;return false;}
        // Only recover before the draw. Never replay a possibly submitted batch.
        DynamicVertexBuffer buffer;
        try
        {
            if(_quad is null || _quad.IsDisposed || _indices is null || _indices.IsDisposed)
            {
                Release();
                _quad=new VertexBuffer(_device,VertexPositionTexture.VertexDeclaration,4,BufferUsage.WriteOnly);
                _quad.SetData(new[]{new VertexPositionTexture(new Vector3(-.5f,-.5f,0),new Vector2(0,0)),
                    new VertexPositionTexture(new Vector3(.5f,-.5f,0),new Vector2(1,0)),
                    new VertexPositionTexture(new Vector3(-.5f,.5f,0),new Vector2(0,1)),
                    new VertexPositionTexture(new Vector3(.5f,.5f,0),new Vector2(1,1))});
                _indices=new IndexBuffer(_device,IndexElementSize.SixteenBits,6,BufferUsage.WriteOnly);
                _indices.SetData(new ushort[]{0,1,2,1,3,2});
            }
            _device.SetVertexBuffer(null);
            if(analytic)
            {
                if(_stress is null || _stress.IsDisposed || _stress.VertexCount<count)
                {_stress?.Dispose(); _stress=new DynamicVertexBuffer(_device,Instance.Declaration,MaximumInstances,BufferUsage.WriteOnly); _stressCount=0;}
                buffer=_stress;
                if(_stressCount!=count){buffer.SetData(0,values,0,count,20,SetDataOptions.Discard);_stressCount=count;UploadedBytes+=20L*count;}
            }
            else
            {
                if(_instances is null || _instances.IsDisposed)
                    _instances=new DynamicVertexBuffer(_device,Instance.Declaration,MaximumInstances,BufferUsage.WriteOnly);
                buffer=_instances;
                buffer.SetData(0,values,0,count,20,SetDataOptions.Discard);UploadedBytes+=20L*count;
            }
            var view=_device.Viewport;
            _effect.Parameters["GPU01Projection"].SetValue(Matrix.CreateOrthographicOffCenter(0,view.Width,view.Height,0,0,1));
            _effect.Parameters["GPU01Stress"].SetValue(new Vector4(analytic?1:0,time,view.Width,view.Height));
            _effect.Parameters["GPU01ParticleTexture"].SetValue(texture);
        }
        catch(Exception ex) when(Recoverable(ex)) {_failed=true; Release();Fallbacks++;return false;}
        var oldIndices=_device.Indices;
        try
        {
            _device.BlendState=blend;_device.DepthStencilState=DepthStencilState.None;_device.RasterizerState=RasterizerState.CullNone;
            _device.SetVertexBuffers(new VertexBufferBinding(_quad!,0,0),new VertexBufferBinding(buffer,0,1));
            _device.Indices=_indices;
            foreach(var pass in _effect.CurrentTechnique.Passes)
            {pass.Apply();_device.DrawInstancedPrimitives(PrimitiveType.TriangleList,0,0,2,count);Draws++;}
            Instances+=count; if(analytic)AnalyticInstances+=count;
        }
        catch(NotSupportedException) when(Draws==0) {_failed=true;Fallbacks++;return false;}
        finally {_device.SetVertexBuffer(null);_device.Indices=oldIndices;}
        return true;
    }
    public void Dispose(){_device.DeviceResetting-=Resetting;Release();}
}
