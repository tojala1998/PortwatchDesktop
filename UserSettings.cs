using System.Text.Json.Nodes;

namespace PortwatchDesktop;

internal sealed record UserSettings(bool AutoFetch=true,int Interval=15,bool Notifications=true,bool Notify30=true,bool NotifyStable=true,bool Sound=true,int Duration=16,bool CloseToTray=true,bool ShowWatchlist=true,bool AutoUpdate=false)
{
    public string[] HiddenDead {get;init;}=[];
    internal static readonly int[] Intervals=[5,10,15,30,60,120];
    internal static readonly int[] Durations=[5,10,16,30,60];
    public static UserSettings Load() => FromJson(TrackerService.Load(TrackerService.SettingsFile));

    internal static UserSettings FromJson(JsonObject data)
    {
        int interval=(int)TrackerService.Number(data,"interval"),duration=(int)TrackerService.Number(data,"duration");
        return new UserSettings(TrackerService.Flag(data,"auto",true),Intervals.Contains(interval)?interval:15,TrackerService.Flag(data,"notifications",true),TrackerService.Flag(data,"notify30",true),TrackerService.Flag(data,"notify_stable",true),TrackerService.Flag(data,"sound",true),Durations.Contains(duration)?duration:16,TrackerService.Flag(data,"close_to_tray",true),TrackerService.Flag(data,"show_watchlist",true),AutoUpdate:TrackerService.Flag(data,"auto_update")){HiddenDead=TrackerService.Array(data,"hidden_dead").Select(x=>x?.ToString()??"").Where(x=>x!="").ToArray()};
    }
    public void Save()=>TrackerService.Save(TrackerService.SettingsFile,new JsonObject{["auto"]=AutoFetch,["interval"]=Interval,["notifications"]=Notifications,["notify30"]=Notify30,["notify_stable"]=NotifyStable,["sound"]=Sound,["duration"]=Duration,["close_to_tray"]=CloseToTray,["show_watchlist"]=ShowWatchlist,["auto_update"]=AutoUpdate,["hidden_dead"]=new JsonArray(HiddenDead.Select(x=>(JsonNode?)JsonValue.Create(x)).ToArray())});
    public bool Accepts(ActivityRow alert)=>Notifications&&((alert.Detail.Contains("100% stable",StringComparison.OrdinalIgnoreCase)||alert.Detail.Contains("100% complete",StringComparison.OrdinalIgnoreCase))?NotifyStable:Notify30);
}
