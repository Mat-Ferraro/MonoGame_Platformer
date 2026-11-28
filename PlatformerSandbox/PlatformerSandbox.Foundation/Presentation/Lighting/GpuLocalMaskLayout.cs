using System;

namespace PlatformerSandbox.Foundation.Presentation.Lighting;

/// <summary>Bounded light-relative raster layout, independent of camera translation.</summary>
public static class GpuLocalMaskLayout
{
    public const int MaximumDimension=2048;
    public const long ByteBudget=32L*1024*1024;
    public static bool TrySize(double radius,double pixelsPerUnit,out int size)
    {
        size=0;
        double projected=radius*1.02*pixelsPerUnit;
        if(!double.IsFinite(projected)||projected<=0||projected>1020||radius<=0||pixelsPerUnit<=0)return false;
        size=2*((int)Math.Ceiling(projected)+2);
        return size<=MaximumDimension;
    }
    public static (int X,int Y) Origin(LightingViewport view,LightPoint center,int size)
    {
        var point=view.Project(center);
        return ((int)Math.Round(point.X-size*.5),(int)Math.Round(point.Y-size*.5));
    }
}
