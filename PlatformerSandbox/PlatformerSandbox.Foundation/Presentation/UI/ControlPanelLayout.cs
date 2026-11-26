using System;

namespace PlatformerSandbox.Foundation.Presentation.UI;

/// <summary>Headless layout/navigation for a paged list of controls. Drawing and hit testing use the same rows.</summary>
public readonly record struct ControlPanelLayout(int X, int Y, int Width, int Height, int First, int Count, int Page, int Pages)
{
    public const int Header = 66, Row = 27, Footer = 32;
    public static ControlPanelLayout Create(int width, int height, int count, int page, bool expanded, bool centered = false)
    {
        int w = Math.Max(1, Math.Min(560, width - 24));
        int x = centered ? Math.Max(0,(width-w)/2) : Math.Max(0,width-w-12);
        int y = centered ? 12 : 42;
        int capacity = Math.Max(1, (height-y-12-Header-Footer)/Row);
        int pages = Math.Max(1, (count+capacity-1)/capacity);
        page = Math.Clamp(page,0,pages-1);
        int first = page*capacity;
        int visible = expanded ? Math.Max(0,Math.Min(capacity,count-first)) : 0;
        return new(x,y,w,expanded ? Header+visible*Row+Footer : 48,first,visible,page,pages);
    }
    public int HitRow(int x, int y) => x >= X+8 && x < X+Width-8 && y >= Y+Header && y < Y+Header+Count*Row && (y-Y-Header)%Row < Row-3 ? First+(y-Y-Header)/Row : -1;
}
