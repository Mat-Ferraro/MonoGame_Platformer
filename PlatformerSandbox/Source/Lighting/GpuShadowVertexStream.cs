using System;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework.Graphics;
using PlatformerSandbox.Foundation.Performance;

namespace PlatformerSandbox.Lighting;

/// <summary>Optional shadow submission strategies; exact vertex data and draw order are preserved.</summary>
internal sealed class GpuShadowVertexStream : IDisposable
{
    private readonly GraphicsDevice _device;
    private DynamicVertexBuffer? _buffer;
    private IndexBuffer? _fanIndices;
    private VertexPositionColor[] _fan = Array.Empty<VertexPositionColor>();
    private VertexPositionColor[] _fallback = Array.Empty<VertexPositionColor>();
    private const int MaxVertices = 262144;
    private const int MaxFanTriangles = ShadowFanIndexPlan.MaxTriangles; // 16-bit indices, 96 KiB
    private const int MaxResidentSlots = 64;
    private const long ResidentBudget = 4L * 1024 * 1024;
    private sealed class ResidentSlot : IDisposable
    {
        public readonly DynamicVertexBuffer Buffer;
        public readonly VertexPositionColor[] Copy;
        public int Count;
        public ResidentSlot(GraphicsDevice device, int capacity)
        {
            Copy = new VertexPositionColor[capacity];
            Buffer = new DynamicVertexBuffer(device, VertexPositionColor.VertexDeclaration, capacity, BufferUsage.WriteOnly);
        }
        public void Dispose() => Buffer.Dispose();
    }
    private readonly ResidentSlot?[] _resident = new ResidentSlot?[MaxResidentSlots];
    private long _residentBytes;
    private int _ordinal;
    private bool _failed, _residentFailed, _indexFailed;
    private static int Stride => VertexPositionColor.VertexDeclaration.VertexStride;
    public int BufferedDraws { get; private set; }
    public int ReferenceDraws { get; private set; }
    public int FallbackDraws { get; private set; }
    public int ResidentHits { get; private set; }
    public int ResidentMisses { get; private set; }
    public int ResidentBypasses { get; private set; }
    public int IndexedDraws { get; private set; }
    public int IndexBypasses { get; private set; }
    public double PrepareMilliseconds {get;private set;}
    public double DrawMilliseconds {get;private set;}
    private static double Elapsed(long started)=>started==0?0:(System.Diagnostics.Stopwatch.GetTimestamp()-started)*1000.0/System.Diagnostics.Stopwatch.Frequency;
    public long UploadedBytes { get; private set; }
    public long IndexUploadedBytes { get; private set; }
    public long ReservedBytes => (long)(_buffer?.VertexCount ?? 0) * Stride + _residentBytes + (_fanIndices?.IndexCount ?? 0) * 2L;
    public string Status { get; private set; } = "NOT EXERCISED";
    public string ResidentStatus => _residentFailed ? "UNAVAILABLE / STREAM FALLBACK" : ResidentHits > 0 ? "HIT" : "NO HIT";
    public string IndexStatus => _indexFailed ? "UNAVAILABLE / TRIANGLE LIST" : IndexedDraws > 0 ? "INDEXED" : "NOT EXERCISED";
    public GpuShadowVertexStream(GraphicsDevice device) { _device = device; }
    public void BeginFrame(bool enabled, bool resident, bool indexed)
    {
        _ordinal = 0;
        PrepareMilliseconds=DrawMilliseconds=0;
        BufferedDraws = ReferenceDraws = FallbackDraws = ResidentHits = ResidentMisses = ResidentBypasses = IndexedDraws = IndexBypasses = 0;
        UploadedBytes = IndexUploadedBytes = 0;
        if (!enabled) { Release(); Status = "COMPATIBILITY"; }
        else
        {
            if (!resident) ReleaseResident();
            if (!indexed) ReleaseIndices();
            if (!_failed) Status = "REQUESTED / NOT EXERCISED";
        }
    }
    private static bool Recoverable(Exception ex) => ex is NotSupportedException or InvalidOperationException;
    private static int Capacity(int count)
    {
        int capacity = 256;
        while (capacity < count) capacity *= 2;
        return capacity;
    }
    // Exact bit comparison, including signed zero. No hash-collision risk and no GPU readback.
    private static bool Same(VertexPositionColor[] a, VertexPositionColor[] b, int count) =>
        MemoryMarshal.AsBytes(a.AsSpan(0, count)).SequenceEqual(MemoryMarshal.AsBytes(b.AsSpan(0, count)));
    private VertexBuffer? TryResident(VertexPositionColor[] data, int count, int ordinal)
    {
        if (_residentFailed || ordinal >= MaxResidentSlots) { ResidentBypasses++; return null; }
        var slot = _resident[ordinal];
        if (slot is not null && !slot.Buffer.IsDisposed && slot.Count == count && Same(slot.Copy, data, count))
        { ResidentHits++; return slot.Buffer; }
        try
        {
            if (slot is null || slot.Buffer.IsDisposed || slot.Buffer.VertexCount < count)
            {
                if (slot is not null) { _residentBytes -= (long)slot.Copy.Length * Stride; slot.Dispose(); _resident[ordinal] = null; }
                int capacity = Capacity(count);
                if (_residentBytes + (long)capacity * Stride > ResidentBudget) { ResidentBypasses++; return null; }
                slot = new ResidentSlot(_device, capacity);
                _resident[ordinal] = slot;
                _residentBytes += (long)capacity * Stride;
            }
            slot.Count = 0; // do not publish a failed upload
            slot.Buffer.SetData(0, data, 0, count, Stride, SetDataOptions.Discard);
            Array.Copy(data, slot.Copy, count);
            slot.Count = count;
            UploadedBytes += (long)count * Stride;
            ResidentMisses++;
            return slot.Buffer;
        }
        catch (Exception ex) when (Recoverable(ex))
        { _residentFailed = true; ReleaseResident(); ResidentBypasses++; return null; }
    }
    // Only called for authored triangle fans (not directional polygon triangles).
    // Keep the closing vertex explicitly, so one immutable index prefix serves every fan length.
    private bool TryIndexFan(VertexPositionColor[] original, int triangles, bool compact)
    {
        if (_indexFailed || triangles > MaxFanTriangles) return false;
        try
        {
            if (_fanIndices is null || _fanIndices.IsDisposed)
            {
                var indices = ShadowFanIndexPlan.BuildIndices(MaxFanTriangles);
                _fanIndices = new IndexBuffer(_device, IndexElementSize.SixteenBits, indices.Length, BufferUsage.WriteOnly);
                _fanIndices.SetData(indices);
                IndexUploadedBytes += indices.Length * 2L;
            }
            if (compact) return true;
            if (_fan.Length < triangles + 2) _fan = new VertexPositionColor[Capacity(triangles + 2)];
            return ShadowFanIndexPlan.TryCompact<VertexPositionColor>(original, _fan, triangles);
        }
        catch (Exception ex) when (Recoverable(ex))
        { _indexFailed = true; ReleaseIndices(); return false; }
    }
    public void Draw(VertexPositionColor[] vertices, int triangleCount, bool enabled, bool resident, bool indexed, bool fan = false, bool compact = false)
    {
        int count = checked(triangleCount * 3);
        if (count == 0) return;
        int ordinal = _ordinal++;
        long prepare=EnginePerformanceOptions.GpuTiming?System.Diagnostics.Stopwatch.GetTimestamp():0;
        if (enabled && !_failed && count <= MaxVertices)
        {
            bool useIndex = indexed && fan && TryIndexFan(vertices, triangleCount, compact);
            if (indexed && fan && !useIndex) IndexBypasses++;
            var data = useIndex ? (compact ? vertices : _fan) : (compact ? ExpandFan(vertices,triangleCount) : vertices);
            int uploadCount = useIndex ? triangleCount+2 : count;
            VertexBuffer? selected = null;
            // Recover only before drawing: never replay an uncertain submission.
            try
            {
                _device.SetVertexBuffer(null);
                selected = resident ? TryResident(data, uploadCount, ordinal) : null;
                if (selected is null)
                {
                    if (_buffer is null || _buffer.IsDisposed || _buffer.VertexCount < uploadCount)
                    {
                        _buffer?.Dispose(); _buffer = null;
                        _buffer = new DynamicVertexBuffer(_device, VertexPositionColor.VertexDeclaration, Capacity(uploadCount), BufferUsage.WriteOnly);
                    }
                    _buffer.SetData(0, data, 0, uploadCount, Stride, SetDataOptions.Discard);
                    UploadedBytes += (long)uploadCount * Stride;
                    selected = _buffer;
                }
            }
            catch (Exception ex) when (Recoverable(ex))
            { Release(); _failed = true; Status = "FALLBACK / " + ex.GetType().Name; }
            if (!_failed)
            {
                PrepareMilliseconds+=Elapsed(prepare);
                long draw=EnginePerformanceOptions.GpuTiming?System.Diagnostics.Stopwatch.GetTimestamp():0;
                var previousIndices = _device.Indices;
                try
                {
                    _device.SetVertexBuffer(selected);
                    if (useIndex)
                    {
                        _device.Indices = _fanIndices;
                        _device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, triangleCount);
                        IndexedDraws++;
                    }
                    else _device.DrawPrimitives(PrimitiveType.TriangleList, 0, triangleCount);
                    BufferedDraws++; Status = "BUFFERED";
                }
                finally { _device.SetVertexBuffer(null); if (useIndex) _device.Indices = previousIndices; DrawMilliseconds+=Elapsed(draw); }
                return;
            }
        }
        if (enabled) { FallbackDraws++; if (!_failed) Status = "FALLBACK / SIZE LIMIT"; }
        var fallback=compact ? ExpandFan(vertices,triangleCount) : vertices;
        PrepareMilliseconds+=Elapsed(prepare);
        long referenceDraw=EnginePerformanceOptions.GpuTiming?System.Diagnostics.Stopwatch.GetTimestamp():0;
        _device.DrawUserPrimitives(PrimitiveType.TriangleList, fallback, 0, triangleCount);
        DrawMilliseconds+=Elapsed(referenceDraw);
        ReferenceDraws++;
    }
    private VertexPositionColor[] ExpandFan(VertexPositionColor[] vertices,int triangles)
    {
        if(_fallback.Length<triangles*3) _fallback=new VertexPositionColor[Capacity(triangles*3)];
        for(int i=0;i<triangles;i++) { _fallback[3*i]=vertices[0]; _fallback[3*i+1]=vertices[i+1]; _fallback[3*i+2]=vertices[i+2]; }
        return _fallback;
    }
    private void ReleaseResident()
    {
        if (_residentBytes == 0) return;
        for (int i=0;i<_resident.Length;i++) { _resident[i]?.Dispose(); _resident[i]=null; }
        _residentBytes=0;
    }
    private void ReleaseIndices() { _fanIndices?.Dispose(); _fanIndices=null; _fan=Array.Empty<VertexPositionColor>(); }
    private void Release() { _buffer?.Dispose(); _buffer=null; ReleaseResident(); ReleaseIndices(); }
    public void Reset() { Release(); _failed = _residentFailed = _indexFailed = false; Status = "NOT EXERCISED"; }
    public void Dispose() => Release();
}
