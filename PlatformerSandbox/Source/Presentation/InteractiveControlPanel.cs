using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PlatformerSandbox.Foundation.Presentation.UI;
using PlatformerSandbox.Gyms;

namespace PlatformerSandbox.Presentation;

/// <summary>Presentation-only controls, shared by labs and graphics settings. No game/renderer ownership.</summary>
internal sealed class InteractiveControlPanel
{
    internal sealed record Item(string Label, string Value, Action? Activate, bool Selected = false);
    private readonly List<Item> _items = new();
    private List<Item>? _choiceItems;
    private string _choiceTitle="";
    private int _page, _focus, _returnPage, _returnFocus;
    private List<Item> Items => _choiceItems ?? _items;
    public bool Choosing => _choiceItems is not null;
    public void DismissChoice() { if(!Choosing) return; _choiceItems=null; _page=_returnPage; _focus=_returnFocus; }
    public void Choice(string label,string value,IReadOnlyList<string> options,Action<int>? choose)
    {
        Add(label,value,choose is null?null:() =>
        {
            _choiceTitle=label; _returnPage=_page; _returnFocus=_focus;
            _choiceItems=new List<Item>();
            _choiceItems.Add(new("BACK TO CONTROLS","BACK",DismissChoice));
            for(int i=0;i<options.Count;i++)
            {
                int selected=i;
                bool active=string.Equals(options[i],value,StringComparison.OrdinalIgnoreCase);
                _choiceItems.Add(new(options[i],active?"CURRENT":"SELECT",() => { DismissChoice(); choose(selected); },active));
            }
            _page=_focus=0;
        });
    }
    public Action? CompactAction { get; set; }
    public bool Expanded { get; set; } = true;
    public bool Focused { get; private set; }
    public void Reset() { CompactAction=null; _choiceItems=null; Expanded = true; Focused = false; _page = _focus = 0; }
    public void Release() { DismissChoice(); Focused = false; }
    public void Clear() => _items.Clear();
    public void Add(string label, string value, Action? action, bool selected = false) => _items.Add(new(label,value,action,selected));
    public void Toggle(string label, bool value, Action action) => Add(label,value?"ON":"OFF",action,value);
    private ControlPanelLayout Layout(int w,int h,bool modal) => ControlPanelLayout.Create(w,h,Items.Count,_page,Expanded || modal,modal);
    private void Page(int delta, ControlPanelLayout l) { _page=Math.Clamp(l.Page+delta,0,l.Pages-1); _focus=0; }
    private int FocusIndex(ControlPanelLayout l) => l.First+Math.Clamp(_focus,0,Math.Max(0,l.Count-1));
    public bool Handle(int w,int h,Point point,bool click,bool focus,bool back,bool up,bool down,bool left,bool right,bool confirm,bool modal = false)
    {
        var l=Layout(w,h,modal); _page=l.Page;
        if (modal) Focused=true;
        if (focus && !modal) { if(Focused) Release(); else { Focused=true; Expanded=true; } return true; }
        if (back && Choosing) { DismissChoice(); return true; }
        if (back && Focused && !modal) { Release(); return true; }
        if (click)
        {
            if (!new Rectangle(l.X,l.Y,l.Width,l.Height).Contains(point)) { if (!modal) Release(); return modal; }
            if (!Expanded && CompactAction is not null && point.Y<l.Y+30 && point.X>=l.X+l.Width-100) { CompactAction.Invoke(); return true; }
            if (point.Y<l.Y+30 && !modal) { Expanded=!Expanded; if(Expanded) Focused=true; else Release(); return true; }
            if (!Expanded && !modal) return true;
            Focused=true;
            int i=l.HitRow(point.X,point.Y);
            if (i>=0) { _focus=i-l.First; Items[i].Activate?.Invoke(); return true; }
            if (point.Y>=l.Y+l.Height-ControlPanelLayout.Footer)
            {
                int third=Math.Max(1,l.Width/3);
                if (point.X<l.X+third) Page(-1,l);
                else if (point.X>=l.X+2*third) Page(1,l);
                else if (!modal) Release();
            }
            return true;
        }
        if (!Focused) return false;
        if (left) Page(-1,l); else if (right) Page(1,l);
        else if (up) _focus=Math.Max(0,_focus-1);
        else if (down) _focus=Math.Min(Math.Max(0,l.Count-1),_focus+1);
        else if (confirm && l.Count>0) Items[FocusIndex(l)].Activate?.Invoke();
        return true;
    }
    public void Draw(SpriteBatch batch, Texture2D pixel,int w,int h,string title,string status,bool modal=false)
    {
        var l=Layout(w,h,modal);
        var accent=new Color(110,223,194); var muted=new Color(168,185,200);
        void Fill(Rectangle r,Color c) => batch.Draw(pixel,r,c);
        void Text(Rectangle r,string text,Color c,int size=1)
        {
            text=text.ToUpperInvariant();
            if (r.Width<6 || r.Height<7) return;
            if (text.Length*6*size>r.Width) size=1;
            int cap=r.Width/(6*size);
            if (text.Length>cap) text=cap>3?text[..(cap-3)]+"...":text[..cap];
            GymDebugText.Draw(batch,pixel,r.X,r.Y+Math.Max(0,(r.Height-7*size)/2),text,c,size);
        }
        Fill(new Rectangle(l.X,l.Y,l.Width,l.Height),new Color(15,22,32,248));
        Fill(new Rectangle(l.X,l.Y,3,l.Height),accent);
        int headerWidth=l.Width-18-(!Expanded && CompactAction is not null?100:0);
        Text(new Rectangle(l.X+9,l.Y+2,headerWidth,25),(Choosing?_choiceTitle:title)+(modal?"":Expanded?" / COLLAPSE -":" / EXPAND +"),accent,2);
        if (!Expanded && !modal) { if(CompactAction is not null) { Fill(new Rectangle(l.X+l.Width-98,l.Y+3,94,24),new Color(124,50,48)); Text(new Rectangle(l.X+l.Width-91,l.Y+3,84,24),"CANCEL",Color.White); } Text(new Rectangle(l.X+9,l.Y+30,l.Width-18,15),status,Color.White); return; }
        Text(new Rectangle(l.X+9,l.Y+30,l.Width-18,15),status,Color.White);
        Text(new Rectangle(l.X+9,l.Y+47,l.Width-18,15),modal?"UP/DOWN SELECT / LEFT RIGHT PAGE / ENTER A CHANGE":Focused?"UI FOCUS: UP/DOWN SELECT / ENTER A / ESC B RELEASE":"CLICK CONTROL / TAB OR PAD RB FOR UI FOCUS",muted);
        for(int i=0;i<l.Count;i++)
        {
            var item=Items[l.First+i]; var row=new Rectangle(l.X+8,l.Y+ControlPanelLayout.Header+i*ControlPanelLayout.Row,l.Width-16,ControlPanelLayout.Row-3);
            bool current=Focused && i==Math.Clamp(_focus,0,Math.Max(0,l.Count-1));
            Fill(row,item.Activate is null?new Color(32,38,48):item.Selected?new Color(29,88,75):new Color(38,49,63));
            if(current) Fill(new Rectangle(row.X,row.Y,3,row.Height),Color.Yellow);
            int split=row.Width*55/100;
            Text(new Rectangle(row.X+7,row.Y,Math.Max(1,split-10),row.Height),item.Label,item.Activate is null?muted:Color.White);
            Text(new Rectangle(row.X+split,row.Y,Math.Max(1,row.Width-split-5),row.Height),item.Value,item.Activate is null?muted:accent);
        }
        int fy=l.Y+l.Height-ControlPanelLayout.Footer+3, tw=l.Width/3;
        for(int i=0;i<3;i++) Fill(new Rectangle(l.X+i*tw+4,fy,Math.Max(1,tw-8),23),new Color(38,49,63));
        Text(new Rectangle(l.X+10,fy,Math.Max(1,tw-16),23),l.Page>0?"< PREVIOUS":"FIRST PAGE",l.Page>0?accent:muted);
        Text(new Rectangle(l.X+tw+10,fy,Math.Max(1,tw-16),23),(l.Page+1)+"/"+l.Pages+(modal?"":" / PLAY"),accent);
        Text(new Rectangle(l.X+2*tw+10,fy,Math.Max(1,tw-16),23),l.Page+1<l.Pages?"NEXT >":"LAST PAGE",l.Page+1<l.Pages?accent:muted);
    }
}
