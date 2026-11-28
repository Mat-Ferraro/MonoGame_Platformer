using System;
using System.Runtime.InteropServices;

namespace PlatformerSandbox.Presentation;

/// <summary>Optional Windows DesktopGL timer-query ring. Never waits for a result,
/// never calls GL without a current context, never interprets CPU wall time as GPU time.
/// External profiler remains authoritative for per-pass timings and GPU utilization.</summary>
internal sealed class GpuTimestampWindow : IDisposable
{
    private const uint Timestamp=0x8E28, QueryResult=0x8866, ResultAvailable=0x8867;
    [DllImport("opengl32.dll")] private static extern IntPtr wglGetCurrentContext();
    [DllImport("opengl32.dll",CharSet=CharSet.Ansi)] private static extern IntPtr wglGetProcAddress(string name);
    [DllImport("opengl32.dll")] private static extern IntPtr glGetString(uint name);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void Gen(int n,[Out] uint[] names);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void Delete(int n,uint[] names);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void Counter(uint name,uint target);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void GetInt(uint name,uint pname,out int value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void GetLong(uint name,uint pname,out ulong value);
    private const int Capacity=8;
    private readonly uint[] _names=new uint[Capacity*2];
    private readonly bool[] _pending=new bool[Capacity];
    private IntPtr _context, _identityContext;
    private Delete? _delete; private Counter? _counter; private GetInt? _available; private GetLong? _result;
    private bool _attempted, _ready; private int _active=-1;
    public string Adapter {get;private set;}="NOT PROBED";
    public string Driver {get;private set;}="NOT PROBED";
    public double LastMilliseconds {get;private set;}=-1;
    public long Samples {get;private set;}
    public long Skipped {get;private set;}
    public string Status {get;private set;}="OFF";
    public string Readout=>_ready && Samples>0?$"{LastMilliseconds:F2} MS / {Samples} SAMPLES":Status;
    private static T? Proc<T>(string name) where T:Delegate
    {
        IntPtr p=wglGetProcAddress(name); long n=p.ToInt64();
        return n==0||n==1||n==2||n==3||n==-1?null:Marshal.GetDelegateForFunctionPointer<T>(p);
    }
    public void ProbeIdentity(bool desktopGl)
    {
        if(!desktopGl || !OperatingSystem.IsWindows()) { Adapter=Driver="UNAVAILABLE / NON-WGL BACKEND"; return; }
        try
        {
            var context=wglGetCurrentContext();
            if(context==IntPtr.Zero) { Adapter=Driver="UNAVAILABLE / NO CURRENT GL CONTEXT"; _identityContext=IntPtr.Zero; return; }
            if(context==_identityContext) return;
            Adapter=Marshal.PtrToStringAnsi(glGetString(0x1F01))??"UNKNOWN";
            Driver=Marshal.PtrToStringAnsi(glGetString(0x1F02))??"UNKNOWN";
            _identityContext=context;
        }
        catch(Exception ex) when(ex is DllNotFoundException or EntryPointNotFoundException or NotSupportedException)
        { Adapter=Driver="UNAVAILABLE / "+ex.GetType().Name; }
    }
    private void Probe()
    {
        _attempted=true;
        if(!OperatingSystem.IsWindows()){Status="UNAVAILABLE / WINDOWS WGL ONLY";return;}
        try
        {
            _context=wglGetCurrentContext();
            if(_context==IntPtr.Zero){Status="UNAVAILABLE / NO CURRENT GL CONTEXT";return;}
            Adapter=Marshal.PtrToStringAnsi(glGetString(0x1F01))??"UNKNOWN";
            Driver=Marshal.PtrToStringAnsi(glGetString(0x1F02))??"UNKNOWN";
            string version=Driver.Split(' ')[0];
            if(!Version.TryParse(version,out var v) || v<new Version(3,3)){Status="UNAVAILABLE / GL 3.3 REQUIRED";return;}
            var gen=Proc<Gen>("glGenQueries");_delete=Proc<Delete>("glDeleteQueries");
            _counter=Proc<Counter>("glQueryCounter");_available=Proc<GetInt>("glGetQueryObjectiv");_result=Proc<GetLong>("glGetQueryObjectui64v");
            if(gen is null||_delete is null||_counter is null||_available is null||_result is null){Status="UNAVAILABLE / TIMER QUERY API";return;}
            gen(_names.Length,_names);
            foreach(uint n in _names)if(n==0){Dispose();_attempted=true;Status="UNAVAILABLE / QUERY ALLOCATION";return;}
            _ready=true;Status="WAITING FOR GPU";
        }
        catch(Exception ex) when(ex is DllNotFoundException or EntryPointNotFoundException or NotSupportedException or ArgumentException)
        {Status="UNAVAILABLE / "+ex.GetType().Name;}
    }
    public void Begin(bool enabled, bool desktopGl=true)
    {
        ProbeIdentity(desktopGl);
        enabled &= desktopGl;
        if(!enabled){if(_attempted)Dispose();Status="OFF";return;}
        if(!_attempted)Probe();
        if(!_ready)return;
        if(wglGetCurrentContext()!=_context){_ready=false;Status="UNAVAILABLE / CONTEXT CHANGED";return;}
        for(int i=0;i<Capacity;i++)
        {
            if(!_pending[i])continue;
            _available!(_names[2*i+1],ResultAvailable,out int ready);
            if(ready==0)continue;
            _result!(_names[2*i],QueryResult,out ulong a);_result!(_names[2*i+1],QueryResult,out ulong b);
            _pending[i]=false;
            if(b>=a){LastMilliseconds=(b-a)/1_000_000.0;Samples++;Status="MEASURED";}
        }
        _active=-1;
        for(int i=0;i<Capacity;i++)if(!_pending[i]){_active=i;break;}
        if(_active<0){Skipped++;return;}
        _counter!(_names[_active*2],Timestamp);
    }
    public void End()
    {
        if(!_ready||_active<0)return;
        if(wglGetCurrentContext()!=_context){_ready=false;_active=-1;Status="UNAVAILABLE / CONTEXT CHANGED";return;}
        _counter!(_names[_active*2+1],Timestamp);_pending[_active]=true;_active=-1;
    }
    public void Dispose()
    {
        if(OperatingSystem.IsWindows() && _delete is not null && _context!=IntPtr.Zero && wglGetCurrentContext()==_context)
            _delete(_names.Length,_names);
        Array.Clear(_names);Array.Clear(_pending);_context=IntPtr.Zero;_ready=false;_attempted=false;_active=-1;
        _delete=null;_counter=null;_available=null;_result=null;LastMilliseconds=-1;Samples=Skipped=0;
    }
}
