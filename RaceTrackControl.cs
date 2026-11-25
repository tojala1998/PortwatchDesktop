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
        if(w<120||h<78)return;
        dc.DrawRectangle(Brushes.Transparent,null,new Rect(0,0,w,h));
        double left=12,right=w-12,span=right-left,barY=Math.Clamp(h-48,30,60),barH=13;
        double highest=projects.Count>0?projects[0].Score:0;
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
        if(projects.Count==0){var waiting=Format("Waiting for ranked project data",13,Muted);dc.DrawText(waiting,new Point(Math.Max(0,(w-waiting.Width)/2),8));return;}

        const double labelHeight = 22, labelGap = 6, labelInset = 4, labelPadding = 8;
        double labelY = 3 + Math.Max(0, h - 96) / 2;
        double available = w - 2 * labelInset;
        int maxNameLength = w < 850 ? 12 : 18;
        var markers = new List<(ProjectRow Project, FormattedText Text, double Anchor, double Width, Brush Accent)>();
        for (int i = 0; i < projects.Count; i++)
        {
            var project = projects[i];
            string name = project.Name.Length > maxNameLength ? project.Name[..(maxNameLength - 1)] + "…" : project.Name;
            var text = Format($"{name}  {project.ScoreText}", 11, i == 0 ? Peach : Text, true);
            text.MaxTextWidth = w - 2 * (labelInset + labelPadding);
            text.MaxLineCount = 1;
            text.Trimming = TextTrimming.CharacterEllipsis;
            double labelWidth = text.Width + 2 * labelPadding;
            if (labelWidth > available) break;
            markers.Add((project, text, Anchor(project.Score), labelWidth, Accents[i]));
            available -= labelWidth + labelGap;
        }

        // Keep the labels in the same order as their score anchors so the connectors cannot cross.
        markers.Reverse();
        var positions = PlaceLabels(markers.Select(marker => (marker.Anchor, marker.Width)).ToArray(), labelInset, labelGap, Math.Max(0, available + labelGap));
        double anchorY = barY + barH / 2;
        dc.PushOpacity(.65);
        for (int i = 0; i < markers.Count; i++)
        {
            var marker = markers[i];
            var start = new Point(positions[i] + marker.Width / 2, labelY + labelHeight);
            dc.DrawLine(new Pen(marker.Accent, 1), start, new Point(marker.Anchor, anchorY));
        }
        dc.Pop();

        for (int i = 0; i < markers.Count; i++)
        {
            var marker = markers[i];
            bool leader = marker.Project == projects[0];
            var bounds = new Rect(positions[i], labelY, marker.Width, labelHeight);
            double dotRadius = leader ? 3.5 : 2.7;
            dc.DrawEllipse(marker.Accent, null, new Point(marker.Anchor, anchorY), dotRadius, dotRadius);
            dc.DrawRoundedRectangle(Pill, new Pen(leader ? Peach : Outline, leader ? 1.3 : 1), bounds, 9, 9);
            dc.DrawText(marker.Text, new Point(bounds.X + labelPadding, bounds.Y + (labelHeight - marker.Text.Height) / 2));
            hits.Add((bounds, marker.Project));
        }
    }

    private static double[] PlaceLabels(IReadOnlyList<(double Anchor, double Width)> markers, double inset, double gap, double slack)
    {
        var positions = new double[markers.Count];
        var preferredOffsets = new double[markers.Count];
        var rightwardOffsets = new double[markers.Count];
        double packedPosition = inset, rightwardOffset = 0;
        for (int i = 0; i < markers.Count; i++)
        {
            var (anchor, width) = markers[i];
            positions[i] = packedPosition;
            preferredOffsets[i] = Math.Clamp(anchor - width / 2 - packedPosition, 0, slack);
            rightwardOffset = Math.Max(rightwardOffset, preferredOffsets[i]);
            rightwardOffsets[i] = rightwardOffset;
            packedPosition += width + gap;
        }

        // Average the two passes to center crowded groups without reducing the gaps between labels.
        double leftwardOffset = slack;
        for (int i = markers.Count - 1; i >= 0; i--)
        {
            leftwardOffset = Math.Min(leftwardOffset, preferredOffsets[i]);
            positions[i] += (leftwardOffset + rightwardOffsets[i]) / 2;
        }
        return positions;
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
