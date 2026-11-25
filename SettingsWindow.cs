using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PortwatchDesktop;

internal sealed class SettingsWindow : Window
{
    public UserSettings? Result {get;private set;}
    public PreparedAppUpdate? RequestedUpdate { get; private set; }
    private readonly CheckBox auto,notifications,fps,stable,sound,tray,watchlist;
    private readonly ComboBox interval,duration;

    public SettingsWindow(Window owner,UserSettings settings,AppUpdateService updates,PreparedAppUpdate? readyUpdate=null)
    {
        if(owner.IsLoaded)Owner=owner;Title="Portwatch settings";Width=510;Height=620;MinWidth=420;ResizeMode=ResizeMode.CanMinimize;WindowStartupLocation=WindowStartupLocation.CenterOwner;Style=(Style)FindResource(typeof(Window));Background=(Brush)FindResource("Page");Foreground=(Brush)FindResource("Text");
        var content=new DockPanel{Margin=new Thickness(22)};
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,14,0,0)};DockPanel.SetDock(buttons,Dock.Bottom);content.Children.Add(buttons);
        var cancel=new Button{Content="Cancel",IsCancel=true,Margin=new Thickness(0,0,8,0)};buttons.Children.Add(cancel);var save=new Button{Content="Save settings",IsDefault=true,Style=(Style)FindResource("PrimaryButton")};buttons.Children.Add(save);
        var stack=new StackPanel();content.Children.Add(new ScrollViewer{Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        stack.Children.Add(new TextBlock{Text="Settings",FontSize=26,FontWeight=FontWeights.Bold,Margin=new Thickness(0,0,0,14)});
        var software = Section(stack, "APP UPDATES");
        var autoUpdate = Check(software, "Install app updates automatically", settings.AutoUpdate);
        AddSoftwareUpdateControls(software, updates, readyUpdate);
        var fetch=Section(stack,"MONITORING");
        auto=Check(fetch,"Automatically refresh tracker data",settings.AutoFetch);
        interval=Select(fetch,"Fetch interval",UserSettings.Intervals,settings.Interval," min");
        watchlist=Check(fetch,"Show watchlist in the leaderboard",settings.ShowWatchlist);
        string[] hiddenDead=settings.HiddenDead;var restore=new Button{Content=$"Restore hidden takedowns ({hiddenDead.Length})",IsEnabled=hiddenDead.Length>0,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,6,0,0)};restore.Click+=(_,_)=>{hiddenDead=[];restore.Content="Hidden projects restored on save";restore.IsEnabled=false;};fetch.Children.Add(restore);
        var alerts=Section(stack,"NOTIFICATIONS");
        notifications=Check(alerts,"Enable milestone notifications",settings.Notifications);
        fps=Check(alerts,"30 FPS in-game",settings.Notify30);stable=Check(alerts,"100% complete / stable",settings.NotifyStable);sound=Check(alerts,"Play notification sound",settings.Sound);
        duration=Select(alerts,"Popup duration",UserSettings.Durations,settings.Duration," sec");
        var behavior=Section(stack,"WINDOW BEHAVIOR");tray=Check(behavior,"Close to notification area (keep monitoring)",settings.CloseToTray);
        behavior.Children.Add(new TextBlock{Text="When disabled, closing the dashboard exits Portwatch.",TextWrapping=TextWrapping.Wrap,Foreground=(Brush)FindResource("Muted"),FontSize=12});
        auto.Checked+=(_,_)=>interval.IsEnabled=true;auto.Unchecked+=(_,_)=>interval.IsEnabled=false;interval.IsEnabled=settings.AutoFetch;
        void UpdateAlerts(){foreach(var control in new Control[]{fps,stable,sound,duration})control.IsEnabled=notifications.IsChecked==true;}
        notifications.Checked+=(_,_)=>UpdateAlerts();notifications.Unchecked+=(_,_)=>UpdateAlerts();UpdateAlerts();
        save.Click+=(_,_)=>{Result=new UserSettings(auto.IsChecked==true,UserSettings.Intervals[interval.SelectedIndex],notifications.IsChecked==true,fps.IsChecked==true,stable.IsChecked==true,sound.IsChecked==true,UserSettings.Durations[duration.SelectedIndex],tray.IsChecked==true,watchlist.IsChecked==true,AutoUpdate:autoUpdate.IsChecked==true){HiddenDead=hiddenDead};DialogResult=true;};
        Content=content;
    }

    private void AddSoftwareUpdateControls(StackPanel panel, AppUpdateService updates, PreparedAppUpdate? readyUpdate)
    {
        panel.Children.Add(new TextBlock
        {
            Text="Downloads in the background and installs when you exit Portwatch.",
            TextWrapping=TextWrapping.Wrap, Foreground=(Brush)FindResource("Muted"), FontSize=12
        });
        var buttons = new WrapPanel { Margin=new Thickness(0,10,0,5) };
        var check = new Button { Content="Check for updates", Margin=new Thickness(0,0,6,0) };
        var install = new Button { Content="Update and restart", Visibility=readyUpdate is null ? Visibility.Collapsed : Visibility.Visible };
        buttons.Children.Add(check);
        buttons.Children.Add(install);
        panel.Children.Add(buttons);

        AppUpdate? available = readyUpdate?.Release;
        var status = new TextBlock
        {
            Text=available is null ? $"Current version {AppUpdateService.CurrentVersion}" : $"Version {available.Version} is ready to install.",
            Foreground=(Brush)FindResource("Muted"), TextWrapping=TextWrapping.Wrap, FontSize=12
        };
        panel.Children.Add(status);
        var lifetime = new CancellationTokenSource();
        var token = lifetime.Token;
        Closed += (_,_) => { lifetime.Cancel(); lifetime.Dispose(); };

        check.Click += async (_,_) =>
        {
            available = null;
            check.IsEnabled = false;
            install.Visibility = Visibility.Collapsed;
            status.Text = "Checking GitHub for updates…";
            try
            {
                available = await updates.CheckAsync(token);
                if (token.IsCancellationRequested) return;
                status.Text = available is null ? $"Portwatch {AppUpdateService.CurrentVersion} is up to date." : $"Version {available.Version} is available.";
                if (available is not null) install.Visibility = Visibility.Visible;
            }
            catch (Exception error) when (AppUpdateService.IsUpdateError(error))
            {
                if (!token.IsCancellationRequested)
                    status.Text = error is OperationCanceledException ? "The update check timed out. Try again." : $"Couldn’t check for updates. {error.Message}";
            }
            finally { check.IsEnabled = true; }
        };
        install.Click += async (_,_) =>
        {
            if (available is null) return;
            check.IsEnabled = install.IsEnabled = false;
            status.Text = $"Preparing version {available.Version}…";
            try
            {
                var prepared = readyUpdate?.Release == available ? readyUpdate : await updates.PrepareAsync(available, token);
                if (token.IsCancellationRequested)
                {
                    if (prepared != readyUpdate) prepared.Discard();
                    return;
                }
                RequestedUpdate = prepared;
                DialogResult = true;
            }
            catch (Exception error) when (AppUpdateService.IsUpdateError(error))
            {
                if (!token.IsCancellationRequested)
                    status.Text = error is OperationCanceledException ? "The update download timed out. Try again." : $"Couldn’t prepare the update. {error.Message}";
            }
            finally { check.IsEnabled = install.IsEnabled = true; }
        };
    }
    private StackPanel Section(StackPanel main,string title)
    {
        var stack=new StackPanel();stack.Children.Add(new TextBlock{Text=title,Foreground=(Brush)FindResource("Pink"),FontSize=12,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,7)});
        main.Children.Add(new Border{Style=(Style)FindResource("CardStyle"),Padding=new Thickness(14),Margin=new Thickness(0,0,0,10),Child=stack});return stack;
    }
    private static CheckBox Check(Panel panel,string label,bool value){var check=new CheckBox{Content=label,IsChecked=value,Margin=new Thickness(0,5,0,5)};panel.Children.Add(check);return check;}
    private ComboBox Select(Panel panel,string label,int[] choices,int selected,string suffix)
    {
        var row=new DockPanel{Margin=new Thickness(0,4,0,5)};var combo=new ComboBox{ItemsSource=choices.Select(x=>x+suffix),SelectedIndex=Math.Max(0,Array.IndexOf(choices,selected)),Width=100,Height=32};DockPanel.SetDock(combo,Dock.Right);row.Children.Add(combo);row.Children.Add(new TextBlock{Text=label,VerticalAlignment=VerticalAlignment.Center,Foreground=(Brush)FindResource("Text")});panel.Children.Add(row);return combo;
    }
}
