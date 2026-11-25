using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PortwatchDesktop;

/// <summary>Resolution-independent milestone scale with collision-aware project markers.</summary>
public sealed class RaceTrackControl : FrameworkElement
{
    private readonly List<(Rect Bounds, ProjectRow Project)> hits = [];
    private IReadOnlyList<ProjectRow> projects = [];
    private ProjectRow? hovered;
    private bool legacyScoring;
    public event Action<ProjectRow>? ProjectSelected;

    private static readonly Brush Text = BrushFrom("#FFF2EB"), Muted = BrushFrom("#C2AAC7"), Pink = BrushFrom("#FF8BB5"), Peach = BrushFrom("#FFC78C"), Track = BrushFrom("#382A48"), Pill = BrushFrom("#392B4C"), Outline = BrushFrom("#584567");
    private static readonly Brush[] Accents = [Pink, Peach, BrushFrom("#BFA4F5"), BrushFrom("#E1B2DC"), BrushFrom("#EEC59F"), BrushFrom("#BDA9D3"), BrushFrom("#DBA0BB"), BrushFrom("#D9C4A0")];
    private static readonly (double Score,string Name,string Short)[] Milestones = [(0,"Groundwork","Start"),(10,"Exe runs","Exe"),(20,"First frame","Frame"),(30,"Main menu","Menu"),(40,"In the world","World"),(50,"Playable","Playable"),(100,"Complete","Complete")];
    private static readonly (double Score,string Name,string Short)[] LegacyMilestones = [(0,"Groundwork","Start"),(25,"Exe runs","Exe"),(40,"First frame","Frame"),(55,"Main menu","Menu"),(70,"In-game","In-game"),(85,"30 FPS","30 FPS"),(100,"Stable","Stable")];
    private static readonly Brush Gradient = new LinearGradientBrush(Color.FromRgb(255,208,141),Color.FromRgb(201,89,209),new Point(0,0),new Point(1,0));
    private static Brush BrushFrom(string value)=>new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));

    public RaceTrackControl(){Cursor=Cursors.Arrow;ToolTipService.SetInitialShowDelay(this,250);}
    public void SetProjects(IEnumerable<ProjectRow> rows,bool legacy=false)
    {
        legacyScoring=legacy;
        projects=rows.Where(x=>x.Tracked&&!x.Dead&&x.Status!="Offline").OrderByDescending(x=>x.Score).Take(8).ToList();
        InvalidateVisual();
    }

    private FormattedText Format(string text,double size,Brush brush,bool bold=false)
        => new(text,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"),FontStyles.Normal,bold?FontWeights.SemiBold:FontWeights.Normal,FontStretches.Normal),size,brush,VisualTreeHelper.GetDpi(this).PixelsPerDip);

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);hits.Clear();double w=ActualWidth,h=ActualHeight;
        if(w<120||h<65)return;
        dc.DrawRectangle(Brushes.Transparent,null,new Rect(0,0,w,h));
        int laneCount=h<110?2:3;
        double left=12,right=w-12,span=right-left,barY=laneCount*22+5,barH=13;
        int count=Math.Min(projects.Count,w<780?5:w<1060?6:w<1390?7:8);
        double highest=count>0?projects[0].Score:0;
        var range=new Rect(left,barY,span,barH);
        dc.DrawRoundedRectangle(Track,null,range,barH/2,barH/2);
        if(highest>0){var fill=new Rect(left,barY,span*Math.Clamp(highest,0,100)/100,barH);dc.DrawRoundedRectangle(Gradient,null,fill,barH/2,barH/2);}

        double Anchor(double value)=>left+span*Math.Clamp(value,0,100)/100;
        foreach(var milestone in legacyScoring?LegacyMilestones:Milestones)
        {
            double x=Anchor(milestone.Score);
            if(milestone.Score is not (0 or 100))dc.DrawLine(new Pen(BrushFrom("#786585"),1),new Point(x,barY),new Point(x,barY+barH));
            var label=Format($"{milestone.Score:g}%",11,milestone.Score<=highest?Peach:Muted,true);
            var title=Format(w<1000?milestone.Short:milestone.Name,11,Muted);
            double labelX=Math.Clamp(x-label.Width/2,0,Math.Max(0,w-label.Width));
            double titleX=Math.Clamp(x-title.Width/2,0,Math.Max(0,w-title.Width));
            dc.DrawText(label,new Point(labelX,barY+barH+5));dc.DrawText(title,new Point(titleX,barY+barH+19));
        }
        if(!legacyScoring&&w>520){var quality=Format("Story · Rendering · FPS · Stability",11,Muted);double center=Anchor(75);if(quality.Width<span*.42)dc.DrawText(quality,new Point(center-quality.Width/2,barY+barH+19));}
        if(count==0){var waiting=Format("Waiting for ranked project data",13,Muted);dc.DrawText(waiting,new Point(Math.Max(0,(w-waiting.Width)/2),24));return;}

        // Three short lanes prevent collisions, while connectors always end on the exact score.
        var occupied=Enumerable.Range(0,laneCount).Select(_=>new List<Rect>()).ToArray();
        var markers=new List<(ProjectRow Project,Rect Label,FormattedText Text,double Anchor,Brush Accent)>();
        const double labelH=20, laneSpacing=22, gap=5;
        for(int i=0;i<count;i++)
        {
            var p=projects[i]; double x=Anchor(p.Score); Brush accent=Accents[i];
            string name=p.Name.Length>(w<850?12:18)?p.Name[..(w<850?11:17)]+"…":p.Name;
            var text=Format($"{name}  {p.ScoreText}",11, i==0?Peach:Text,true);
            double labelW=Math.Min(w-4,text.Width+16), ideal=Math.Clamp(x-labelW/2,2,w-labelW-2);
            (int Lane,double X,double Cost) best=(-1,ideal,double.PositiveInfinity);
            for(int lane=0;lane<occupied.Length;lane++)
            {
                var positions=new List<double>{ideal,2,w-labelW-2};
                foreach(Rect r in occupied[lane]){positions.Add(r.Right+gap);positions.Add(r.Left-labelW-gap);}
                foreach(double proposed in positions)
                {
                    double px=Math.Clamp(proposed,2,w-labelW-2);var proposal=new Rect(px,lane*laneSpacing,labelW,labelH);
                    if(occupied[lane].Any(r=>proposal.Left<r.Right+gap&&proposal.Right>r.Left-gap))continue;
                    double cost=Math.Abs(px-ideal)+lane*9;
                    if(cost<best.Cost)best=(lane,px,cost);
                }
            }
            if(best.Lane<0)continue; // only possible at unusually tiny widths
            var rect=new Rect(best.X,best.Lane*laneSpacing,labelW,labelH);occupied[best.Lane].Add(rect);
            markers.Add((p,rect,text,x,accent));
        }
        foreach(var marker in markers)
        {
            var center=new Point(marker.Label.X+marker.Label.Width/2,marker.Label.Bottom);
            dc.DrawLine(new Pen(marker.Accent,1),center,new Point(marker.Anchor,barY+barH/2));
            dc.DrawEllipse(marker.Accent,null,new Point(marker.Anchor,barY+barH/2),3.3,3.3);
        }
        foreach(var marker in markers)
        {
            bool leader=marker.Project==projects[0];
            dc.DrawRoundedRectangle(Pill,new Pen(leader?Peach:Outline,leader?1.3:1),marker.Label,9,9);
            dc.DrawText(marker.Text,new Point(marker.Label.X+(marker.Label.Width-marker.Text.Width)/2,marker.Label.Y+(labelH-marker.Text.Height)/2));
            hits.Add((marker.Label,marker.Project));
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);var point=e.GetPosition(this);var match=hits.FirstOrDefault(x=>x.Bounds.Contains(point)).Project;
        if(match==hovered)return;hovered=match;Cursor=match is null?Cursors.Arrow:Cursors.Hand;
        ToolTip=match is null?null:$"{match.Name} · {match.ScoreText} · {match.Stage}\nClick to select project";
    }
    protected override void OnMouseLeave(MouseEventArgs e){base.OnMouseLeave(e);hovered=null;Cursor=Cursors.Arrow;ToolTip=null;}
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e){base.OnMouseLeftButtonDown(e);var match=hits.FirstOrDefault(x=>x.Bounds.Contains(e.GetPosition(this))).Project;if(match is not null){ProjectSelected?.Invoke(match);e.Handled=true;}}
}
