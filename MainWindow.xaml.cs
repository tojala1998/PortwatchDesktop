using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace PortwatchDesktop;

public partial class MainWindow : Window
{
    private JsonObject state = TrackerService.Load(TrackerService.StateFile);
    private readonly int[] intervals = [5,10,15,30,60,120];
    private readonly DispatcherTimer timer = new() { Interval=TimeSpan.FromMilliseconds(250) };
    private readonly CancellationTokenSource closing = new();
    private DateTimeOffset nextScan=DateTimeOffset.UtcNow;
    private bool initialized, busy, fullscreen;
    private WindowState previousState;
    private WindowStyle previousStyle;
    private ResizeMode previousResize;
    private double leadPoints;
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);

    public MainWindow()
    {
        InitializeComponent();
        SizeChanged+=(_,_)=>{RaceTrack.Height=ActualHeight<900?96:120;};
        RaceTrack.ProjectSelected+=project=>{Tabs.SelectedIndex=0;var row=Leaderboard.Items.OfType<ProjectRow>().FirstOrDefault(x=>x.Name==project.Name&&x.Repo==project.Repo);if(row is not null){Leaderboard.SelectedItem=row;Leaderboard.ScrollIntoView(row);}};
        SourceInitialized+=(_,_)=>{try{int dark=1;DwmSetWindowAttribute(new WindowInteropHelper(this).Handle,20,ref dark,sizeof(int));}catch(DllNotFoundException){}catch(EntryPointNotFoundException){}};
        var settings=TrackerService.Load(TrackerService.SettingsFile);
        int minutes=(int)TrackerService.Number(settings,"interval");if(!intervals.Contains(minutes))minutes=15;
        Interval.ItemsSource=intervals.Select(n=>$"{n} min").ToArray();
        Interval.SelectedIndex=Array.IndexOf(intervals,minutes);
        AutoFetch.IsChecked=TrackerService.Flag(settings,"auto",true);
        NotifyMilestones.IsChecked=TrackerService.Flag(settings,"notifications",true);
        initialized=true;
        Render();ClockTick();
        Loaded+=(_,_)=>{timer.Tick+=(_,_)=>ClockTick();timer.Start();};
        Closed+=(_,_)=>{timer.Stop();closing.Cancel();};
    }

    private int IntervalMinutes => Interval.SelectedIndex>=0&&Interval.SelectedIndex<intervals.Length?intervals[Interval.SelectedIndex]:15;

    internal void PreviewSample()
    {
        state=JsonNode.Parse("""
        {"tracker":{"updated":"2026-11-24T13:29:00Z","exe":{"status":"released","releasedAt":"2026-11-23T14:36:00Z"},"efforts":[{"id":"a","name":"Example Alpha","tier":"tracked","repo":"example/alpha","gta":{"loads":1,"boots":1,"menu":1,"ingame":1,"fps":4},"summary":"Example project showing the dashboard layout and project details."},{"id":"b","name":"Example Beta","tier":"tracked","repo":"example/beta","gta":{"loads":1,"boots":1,"menu":1,"ingame":0.8}},{"id":"c","name":"Example Gamma","tier":"tracked","repo":"example/gamma","gta":{"loads":1,"boots":1,"menu":1,"ingame":0.5}},{"id":"d","name":"Example Delta","tier":"watchlist","repo":"example/delta","gta":{"loads":1,"boots":1}}]},"live":{"example/beta":{"status":"Updated","commit":{"title":"Improve graphics support and fix rendering issues","updated":"2026-11-24T13:10:00Z"}},"example/gamma":{"status":"Current","commit":{"title":"Update compatibility notes","updated":"2026-11-24T12:00:00Z"}}}}
        """)!.AsObject();
        var examples=TrackerService.Array(TrackerService.Object(state,"tracker"),"efforts");
        for(int i=0;i<5;i++)examples.Add(new JsonObject{["id"]=$"demo{i}",["name"]=$"Example {i+5}",["tier"]="tracked",["repo"]=$"example/demo{i}",["gta"]=new JsonObject{["loads"]=1,["boots"]=1,["menu"]=Math.Max(0,.5-i*.1)}});
        Render();
    }

    private void SettingsChanged(object sender,RoutedEventArgs e)
    {
        if(!initialized)return;
        nextScan=DateTimeOffset.UtcNow.AddMinutes(IntervalMinutes);
        try{TrackerService.Save(TrackerService.SettingsFile,new JsonObject{["interval"]=IntervalMinutes,["auto"]=AutoFetch.IsChecked==true,["notifications"]=NotifyMilestones.IsChecked==true});}catch(IOException){}
    }
    private void WatchlistChanged(object sender,RoutedEventArgs e){if(initialized)Render();}

    private void Render()
    {
        var selected=(Leaderboard.SelectedItem as ProjectRow)?.Repo;
        var all=TrackerService.Projects(state,true);
        RaceTrack.SetProjects(all);
        var projects=TrackerService.Projects(state,ShowWatchlist.IsChecked==true);
        Leaderboard.ItemsSource=projects;
        ActivityGrid.ItemsSource=TrackerService.Activities(state);
        Candidates.ItemsSource=TrackerService.Candidates(state);
        if(selected is not null)Leaderboard.SelectedItem=projects.FirstOrDefault(p=>p.Repo==selected);
        if(Leaderboard.SelectedItem is null&&projects.Count>0)Leaderboard.SelectedIndex=0;
        var leader=all.FirstOrDefault(x=>x.Rank=="1");
        if(leader is not null){LeaderName.Text=leader.Name;LeaderScore.Text=leader.ScoreText;LeaderMilestone.Text=leader.Stage+"  ·  PortWatch editorial estimate";leadPoints=leader.Score;}
        else{LeaderName.Text="Waiting for data";LeaderScore.Text="—%";LeaderMilestone.Text="Awaiting PortWatch data";leadPoints=0;}
        UpdateProgress();
        Summary.Text=$"{all.Count} efforts  ·  PortWatch data: {TrackerService.Stamp(TrackerService.Text(TrackerService.Object(state,"tracker"),"updated"))}";
        string last=TrackerService.Text(state,"last_scan");if(last!="")Status.Text=$"Last fetch: {TrackerService.Stamp(last)}  ·  {TrackerService.Array(state,"errors").Count} source warnings";
        UpdateReleaseTimer();
    }

    private void UpdateProgress()=>ProgressFill.Width=Math.Max(0,ProgressTrack.ActualWidth*leadPoints/100);
    private void ProgressTrack_SizeChanged(object sender,SizeChangedEventArgs e){if(ProgressFill is not null)UpdateProgress();}
    private void UpdateReleaseTimer(){var clock=TrackerService.ReleaseClock(TrackerService.Object(TrackerService.Object(state,"tracker"),"exe"));ReleaseHeading.Text=clock.Heading;ReleaseClock.Text=clock.Clock;ReleaseDetails.Text=clock.Detail;}
    private void ClockTick()
    {
        UpdateReleaseTimer();
        var seconds=Math.Max(0,(int)(nextScan-DateTimeOffset.UtcNow).TotalSeconds);
        NextScan.Text=busy?"Scanning…":AutoFetch.IsChecked==true?$"Next: {seconds/60:00}:{seconds%60:00}":"Auto paused";
        if(initialized&&IsLoaded&&!busy&&AutoFetch.IsChecked==true&&DateTimeOffset.UtcNow>=nextScan)_=FetchAsync();
    }
    private async Task FetchAsync()
    {
        if(busy)return;
        busy=true;FetchButton.IsEnabled=false;FetchButton.Content="Fetching…";Status.Text="Fetching PortWatch and checking public repositories…";
        try
        {
            var progress=new Progress<string>(s=>Status.Text=s);var result=await TrackerService.Scan(state,progress,closing.Token);
            state=result.State;Render();Status.Text=$"Last fetch: {TrackerService.Stamp(TrackerService.Text(state,"last_scan"))}  ·  {result.ChangeCount} changes  ·  {TrackerService.Array(state,"errors").Count} warnings";
            if(NotifyMilestones.IsChecked==true&&result.Alerts.Count>0)ShowNotifications(result.Alerts);
        }
        catch(OperationCanceledException){}
        catch(Exception e){Status.Text="Fetch failed: "+e.Message;}
        finally{busy=false;FetchButton.IsEnabled=true;FetchButton.Content="Fetch now";nextScan=DateTimeOffset.UtcNow.AddMinutes(IntervalMinutes);}
    }
    private async void FetchButton_Click(object sender,RoutedEventArgs e)=>await FetchAsync();

    private void Window_KeyDown(object sender,KeyEventArgs e)
    {
        if(e.Key==Key.F5){_=FetchAsync();e.Handled=true;}
        else if(e.Key==Key.F11){ToggleFullscreen();e.Handled=true;}
        else if(e.Key==Key.Escape&&fullscreen){ToggleFullscreen();e.Handled=true;}
    }
    private void ToggleFullscreen()
    {
        if(!fullscreen){previousState=WindowState;previousStyle=WindowStyle;previousResize=ResizeMode;WindowState=WindowState.Normal;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;WindowState=WindowState.Maximized;fullscreen=true;}
        else{WindowState=WindowState.Normal;WindowStyle=previousStyle;ResizeMode=previousResize;WindowState=previousState;fullscreen=false;}
    }
    private void Leaderboard_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(ProjectDetail is null)return;
        ProjectDetail.Text=Leaderboard.SelectedItem is ProjectRow row?$"{row.Name} ({row.ScoreText}) — {row.Detail}":"Select a project for details.";
    }
    private static void Open(string url){try{Process.Start(new ProcessStartInfo(url){UseShellExecute=true});}catch{MessageBox.Show("Unable to open the link.","Portwatch Desktop");}}
    private static void OpenRepo(string? repo){if(repo is not null&&TrackerService.ValidRepo(repo))Open("https://github.com/"+repo);}
    private void OpenProject_Click(object sender,RoutedEventArgs e)=>OpenRepo((Leaderboard.SelectedItem as ProjectRow)?.Repo);
    private void Leaderboard_DoubleClick(object sender,MouseButtonEventArgs e)=>OpenProject_Click(sender,e);
    private void OpenCandidate_Click(object sender,RoutedEventArgs e)=>OpenRepo((Candidates.SelectedItem as CandidateRow)?.Repo);
    private void Candidates_DoubleClick(object sender,MouseButtonEventArgs e)=>OpenCandidate_Click(sender,e);
    private void OpenSite_Click(object sender,RoutedEventArgs e)=>Open(TrackerService.Site);

    private void SaveReport_Click(object sender,RoutedEventArgs e)=>SaveReport();
    private void SaveReport()
    {
        var dialog=new SaveFileDialog{Title="Save Portwatch scan report",Filter="Text report (*.txt)|*.txt",DefaultExt=".txt",FileName=$"PortWatch-Report-{DateTime.Now:yyyy-MM-dd_HHmm}.txt"};
        if(dialog.ShowDialog(this)==true){try{File.WriteAllText(dialog.FileName,TrackerService.Report(state));Status.Text="Report saved: "+Path.GetFileName(dialog.FileName);}catch(IOException e){MessageBox.Show(this,e.Message,"Could not save report",MessageBoxButton.OK,MessageBoxImage.Error);}}
    }
    private void ShowReport_Click(object sender,RoutedEventArgs e)
    {
        var report=new Window{Title="Portwatch scan report",Owner=this,Width=950,Height=720,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var layout=new DockPanel{Margin=new Thickness(20)};var button=new Button{Content="Save report",HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,0,0,12)};button.Click+=(_,_)=>SaveReport();DockPanel.SetDock(button,Dock.Top);layout.Children.Add(button);
        layout.Children.Add(new TextBox{Text=TrackerService.Report(state),IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Background=(Brush)FindResource("Card"),Foreground=(Brush)FindResource("Text"),BorderThickness=new Thickness(0),Padding=new Thickness(15),FontFamily=new FontFamily("Cascadia Mono, Consolas"),FontSize=13});report.Content=layout;report.Show();
    }

    private void ShowNotifications(IReadOnlyList<ActivityRow> alerts)
    {
        var toast=new Window{Width=390,SizeToContent=SizeToContent.Height,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,Topmost=true,AllowsTransparency=true,Background=Brushes.Transparent,ShowActivated=false};
        var card=new Border{Background=(Brush)FindResource("CardAlt"),BorderBrush=(Brush)FindResource("Pink"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(20),Padding=new Thickness(20)};
        var content=new StackPanel();content.Children.Add(new TextBlock{Text="PORTWATCH  ·  MILESTONE ALERT",Foreground=(Brush)FindResource("Pink"),FontWeight=FontWeights.Bold,FontSize=12});
        foreach(var alert in alerts.Take(4)){content.Children.Add(new TextBlock{Text=alert.Project,FontSize=21,FontWeight=FontWeights.Bold,Foreground=(Brush)FindResource("Peach"),Margin=new Thickness(0,12,0,2)});content.Children.Add(new TextBlock{Text=alert.Detail,TextWrapping=TextWrapping.Wrap});}
        var close=new Button{Content="Dismiss",HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};close.Click+=(_,_)=>toast.Close();content.Children.Add(close);card.Child=content;toast.Content=card;toast.Loaded+=(_,_)=>{toast.Left=SystemParameters.WorkArea.Right-toast.ActualWidth-20;toast.Top=SystemParameters.WorkArea.Bottom-toast.ActualHeight-20;};toast.Show();
        var disappear=new DispatcherTimer{Interval=TimeSpan.FromSeconds(16)};disappear.Tick+=(_,_)=>{disappear.Stop();if(toast.IsLoaded)toast.Close();};toast.Closed+=(_,_)=>disappear.Stop();disappear.Start();System.Media.SystemSounds.Asterisk.Play();
    }
}
