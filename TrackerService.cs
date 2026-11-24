using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PortwatchDesktop;

public sealed record ProjectRow(string Rank, string Name, string ScoreText, string Stage, string CommitTime, string Status, string CommitTitle, string Repo, string Detail, double Score);
public sealed record CandidateRow(string Repo, string Source, string Substance, string Found, string Description);
public sealed record ActivityRow(string Time, string Project, string Kind, string Detail);
public sealed record ScanResult(JsonObject State, int ChangeCount, IReadOnlyList<ActivityRow> Alerts);

public static class TrackerService
{
    public const string Site = "https://portwatch-vi.xyz/";
    private static readonly string[] TrackerUrls = [Site + "data/tracker.json", "https://raw.githubusercontent.com/4lwayz/portwatch-vi/main/data/tracker.json"];
    public static readonly string StateDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PortWatchDesktop");
    public static readonly string StateFile = Path.Combine(StateDirectory, "state.json");
    public static readonly string SettingsFile = Path.Combine(StateDirectory, "settings.json");
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly Regex RepoPattern = new(@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", RegexOptions.Compiled);
    private static readonly Regex ClaimPattern = new(@"\b(?:in[- ]game|gameplay|main menu|title screen|first frame|first boot|boot(?:s|ed|ing)?|playable|\d{1,3}\s*fps|frames per second)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ContextPattern = new(@"\b(?:gta\s*(?:vi|6)|grand theft auto|game|world|fps|frame|render)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Dictionary<string, double> Stability = new() { ["unknown"]=0, ["constant"]=0, ["often"]=.25, ["sometimes"]=.55, ["rarely"]=.8, ["stable"]=1 };

    public static string Text(JsonNode? node, string key, string fallback = "") => node is JsonObject o && o[key] is JsonNode v ? v.ToString() : fallback;
    public static JsonObject Object(JsonNode? node, string key) => node is JsonObject o && o[key] is JsonObject child ? child : new();
    public static JsonArray Array(JsonNode? node, string key) => node is JsonObject o && o[key] is JsonArray child ? child : new();
    public static bool Flag(JsonNode? node, string key, bool fallback = false) => bool.TryParse(Text(node,key),out var b) ? b : fallback;
    public static double Number(JsonNode? node, string key) => double.TryParse(Text(node,key),NumberStyles.Any,CultureInfo.InvariantCulture,out var n) ? n : Flag(node,key) ? 1 : 0;
    public static string Stamp(string value) => DateTimeOffset.TryParse(value,out var t) ? t.ToLocalTime().ToString("MMM dd HH:mm",CultureInfo.InvariantCulture) : "—";
    public static bool ValidRepo(string repo) => RepoPattern.IsMatch(repo) && !repo.Split('/').Any(x => x is "." or "..");
    public static string Now() => DateTimeOffset.UtcNow.ToString("o");
    private static double Clamp(double x) => double.IsFinite(x) ? Math.Clamp(x,0,1) : 0;

    public static (double Points, string Stage) Score(JsonNode? effort)
    {
        var g = Object(effort,"groundwork"); var gta = Object(effort,"gta");
        double points = 25*(.2*Clamp(Number(g,"loader"))+.28*Clamp(Number(g,"shaders"))+.32*Clamp(Number(g,"graphics"))+.2*Clamp(Number(g,"proven")));
        string stage = "Groundwork";
        foreach (var (key,tick,label) in new (string,double,string)[]{("loads",25,"Exe runs"),("boots",40,"First frame"),("menu",55,"Main menu"),("ingame",70,"In-game")})
        {
            double v=Clamp(Number(gta,key));
            if (v == 1) { points=tick; stage=label; }
            else { points += v*(tick-points); break; }
        }
        if (stage == "In-game")
        {
            double fps = Clamp(Number(gta,"fps")/30), stability=Stability.GetValueOrDefault(Text(gta,"stability"));
            points += 15*fps+15*stability;
            if(fps==1) stage = stability==1 ? "Stable" : "30 FPS";
        }
        return (Math.Round(points,1,MidpointRounding.AwayFromZero),stage);
    }

    public static (string Heading,string Clock,string Detail) ReleaseClock(JsonNode? exe, DateTimeOffset? now = null)
    {
        bool released=Text(exe,"status")=="released";
        string raw=Text(exe,released?"releasedAt":"expectedAt");
        string heading=released?"TIME SINCE EXECUTABLE RELEASE":"EXECUTABLE RELEASE COUNTDOWN";
        if (!DateTimeOffset.TryParse(raw,out var target)) return (heading,"—d  —h  —m  —s","Awaiting tracker timestamp");
        long s=Math.Max(0,(long)(released?(now??DateTimeOffset.UtcNow)-target:target-(now??DateTimeOffset.UtcNow)).TotalSeconds);
        return (heading,$"{s/86400:00}d  {s/3600%24:00}h  {s/60%60:00}m  {s%60:00}s",$"{(released?"Released":"Expected")}: {Stamp(raw)}");
    }

    public static JsonObject Load(string filename)
    {
        try { return JsonNode.Parse(File.ReadAllText(filename)) as JsonObject ?? new(); }
        catch(IOException) { return new(); } catch(JsonException) { return new(); }
    }
    public static void Save(string filename, JsonObject obj)
    {
        Directory.CreateDirectory(StateDirectory);
        string temp=filename+".tmp"; File.WriteAllText(temp,obj.ToJsonString()); File.Move(temp,filename,true);
    }

    private record Fetch(byte[]? Bytes,string Etag,string Modified);
    private static async Task<Fetch> Request(string url,JsonObject? prior=null,int limit=4_000_000,string accept="*/*",CancellationToken token=default)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(25));var requestToken=timeout.Token;
        try
        {
        using var request=new HttpRequestMessage(HttpMethod.Get,url);
        request.Headers.TryAddWithoutValidation("User-Agent","PortwatchDesktop/2.0 (public project tracker)");
        request.Headers.TryAddWithoutValidation("Accept",accept);
        if(prior is not null)
        {
            if(Text(prior,"etag") is {Length:>0} etag) request.Headers.TryAddWithoutValidation("If-None-Match",etag);
            if(Text(prior,"modified") is {Length:>0} modified) request.Headers.TryAddWithoutValidation("If-Modified-Since",modified);
        }
        using var response=await Http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,requestToken).ConfigureAwait(false);
        if(response.StatusCode==HttpStatusCode.NotModified) return new(null,Text(prior,"etag"),Text(prior,"modified"));
        response.EnsureSuccessStatusCode();
        using var stream=await response.Content.ReadAsStreamAsync(requestToken).ConfigureAwait(false);
        using var ms=new MemoryStream(); byte[] buffer=new byte[8192];
        while(true){int size=await stream.ReadAsync(buffer,requestToken).ConfigureAwait(false);if(size==0)break;if(ms.Length+size>limit)throw new IOException("Response too large");ms.Write(buffer,0,size);}
        return new(ms.ToArray(),response.Headers.ETag?.ToString()??"",response.Content.Headers.LastModified?.ToString("R")??"");
        }
        catch(OperationCanceledException) when(!token.IsCancellationRequested){throw new TimeoutException("Source did not respond in time");}
    }

    private static async Task<JsonObject> GetTracker(CancellationToken token)
    {
        var failures=new List<string>();
        foreach(string url in TrackerUrls)
        {
            try { var f=await Request(url,accept:"application/json",token:token).ConfigureAwait(false);var data=JsonNode.Parse(f.Bytes!) as JsonObject;if(data?["efforts"] is JsonArray)return data;throw new JsonException("Missing efforts list"); }
            catch(Exception e) when(e is not OperationCanceledException){failures.Add(e.Message);}
        }
        throw new IOException(string.Join("; ",failures));
    }

    private static async Task<JsonObject> ReadFeed(string url,JsonObject prior,CancellationToken token)
    {
        var result=await Request(url,prior,2_000_000,"application/atom+xml",token).ConfigureAwait(false);
        if(result.Bytes is null)return (JsonObject)prior.DeepClone();
        using var stream=new MemoryStream(result.Bytes);
        var entry=XDocument.Load(stream).Root?.Elements().FirstOrDefault(x=>x.Name.LocalName=="entry");
        var info=new JsonObject { ["etag"]=result.Etag,["modified"]=result.Modified };
        if(entry is not null)foreach(string k in new[]{"id","title","updated"}) info[k]=entry.Elements().FirstOrDefault(x=>x.Name.LocalName==k)?.Value.Trim()??"";
        return info;
    }

    private static async Task<string?> ReadReadme(string repo,string branch,CancellationToken token)
    {
        string basis="https://raw.githubusercontent.com/"+repo+"/"+Uri.EscapeDataString(branch).Replace("%2F","/")+"/";
        foreach(string name in new[]{"README.md","README","README.rst","readme.md"})
        {
            try{return Encoding.UTF8.GetString((await Request(basis+name,limit:500_000,accept:"text/plain",token:token).ConfigureAwait(false)).Bytes!);}
            catch(HttpRequestException e) when(e.StatusCode==HttpStatusCode.NotFound){}
        }
        return null;
    }

    private static string[] Claims(string text) => text.Split('\n').Select(x=>Regex.Replace(x.Trim(),@"\s+"," ")).Where(x=>ClaimPattern.IsMatch(x)&&ContextPattern.IsMatch(x)).Select(x=>x.Length>300?x[..300]:x).Distinct().Take(80).ToArray();
    private static JsonObject Event(string project,string kind,string detail) => new(){["time"]=Now(),["project"]=project,["kind"]=kind,["detail"]=detail};
    private record RepoResult(string Repo,JsonObject Live,List<JsonObject> Events);
    private static async Task<RepoResult> PollRepo(JsonNode effort,JsonObject previous,CancellationToken token)
    {
        string repo=Text(effort,"repo"), name=Text(effort,"name",repo), branch=Text(effort,"branch","main");
        var current=(JsonObject)previous.DeepClone(); var changes=new List<JsonObject>(); var errors=new List<string>();
        foreach(var (kind,endpoint) in new[]{("commit","/commits/"+Uri.EscapeDataString(branch).Replace("%2F","/")+".atom"),("release","/releases.atom")})
        {
            var prior=Object(current,kind);
            try
            {
                var fresh=await ReadFeed("https://github.com/"+repo+endpoint,prior,token).ConfigureAwait(false);current[kind]=fresh;
                if(Text(prior,"id")!=""&&Text(fresh,"id")!=""&&Text(prior,"id")!=Text(fresh,"id"))changes.Add(Event(name,kind=="commit"?"Commit":"Release",Text(fresh,"title","New activity")));
            }
            catch(HttpRequestException e) when(kind=="release"&&e.StatusCode==HttpStatusCode.NotFound){}
            catch(Exception e) when(e is not OperationCanceledException){errors.Add(kind+": "+e.Message);}
        }
        string id=Text(Object(current,"commit"),"id");
        if(id!=""&&Text(current,"readme_commit")!=id)
        {
            try
            {
                var text=await ReadReadme(repo,branch,token).ConfigureAwait(false);
                if(text is not null)
                {
                    var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();var claims=Claims(text);
                    if(Text(current,"readme_hash")!=""&&Text(current,"readme_hash")!=hash)
                    {
                        changes.Add(Event(name,"README","README changed"));var old=Array(current,"claims").Select(x=>x?.ToString()??"").ToHashSet();
                        foreach(string line in claims.Where(x=>!old.Contains(x)).Take(3))changes.Add(Event(name,"Unverified claim",line));
                    }
                    current["readme_hash"]=hash;current["readme_commit"]=id;current["claims"]=new JsonArray(claims.Select(x=>(JsonNode?)JsonValue.Create(x)).ToArray());
                }
            }
            catch(Exception e) when(e is not OperationCanceledException){errors.Add("README: "+e.Message);}
        }
        current["checked"]=Now();current["status"]=errors.Count>0?(id!=""?"Partial":"Unavailable"):(changes.Count>0?"Updated":"Current");current["error"]=string.Join("; ",errors);
        return new(repo,current,changes);
    }

    private static List<JsonObject> Compare(JsonObject old,JsonObject tracker)
    {
        var changes=new List<JsonObject>();if(old.Count==0)return changes;
        var prior=Array(old,"efforts").Where(x=>x is JsonObject).GroupBy(x=>Text(x,"id")).ToDictionary(x=>x.Key,x=>x.First());
        foreach(var effort in Array(tracker,"efforts"))
        {
            prior.TryGetValue(Text(effort,"id"),out var before);string name=Text(effort,"name","Project");var score=Score(effort);var was=Score(before);
            if(before is null)changes.Add(Event(name,"New tracked project","Added by PortWatch"));else if(score.Points!=was.Points)changes.Add(Event(name,"Score",$"PortWatch: {was.Points:g}% → {score.Points:g}%"));else if(Flag(before,"nuked")!=Flag(effort,"nuked"))changes.Add(Event(name,"Status","PortWatch updated availability"));
            if(Flag(effort,"nuked"))continue;
            if(score.Stage=="Stable"&&was.Stage!="Stable")changes.Add(Event(name,"Milestone alert","PortWatch reports 100% stable gameplay (30 FPS or higher)."));
            else if(score.Stage=="30 FPS"&&was.Stage is not ("Stable" or "30 FPS"))changes.Add(Event(name,"Milestone alert","PortWatch reports 30 FPS in-game."));
        }
        var previous=Array(old,"candidates").Select(x=>Text(x,"repo")).ToHashSet();
        foreach(var c in Array(tracker,"candidates"))if(!previous.Contains(Text(c,"repo")))changes.Add(Event(Text(c,"name",Text(c,"repo")),"New candidate","Discovered by PortWatch; unreviewed"));
        return changes;
    }

    private static async Task<(JsonArray Candidates,List<JsonObject> Events,List<string> Errors)> Discover(JsonObject tracker,JsonArray prior,CancellationToken token)
    {
        var known=Array(tracker,"efforts").Concat(Array(tracker,"candidates")).Select(x=>Text(x,"repo").ToLowerInvariant()).ToHashSet();
        var current=prior.Where(x=>!known.Contains(Text(x,"repo").ToLowerInvariant())).GroupBy(x=>Text(x,"repo").ToLowerInvariant()).ToDictionary(x=>x.Key,x=>x.First()!.DeepClone());
        var changes=new List<JsonObject>();var errors=new List<string>();string since=DateTimeOffset.UtcNow.AddDays(-14).ToString("yyyy-MM-dd");
        foreach(string query in new[]{"gta vi port","gta6 recomp","xbox series compatibility layer"})
        {
            try
            {
                var url="https://api.github.com/search/repositories?q="+Uri.EscapeDataString(query+" created:>="+since)+"&sort=updated&order=desc&per_page=10";
                var data=JsonNode.Parse((await Request(url,limit:1_500_000,accept:"application/vnd.github+json",token:token).ConfigureAwait(false)).Bytes!);
                foreach(var item in Array(data,"items"))
                {
                    string repo=Text(item,"full_name"),key=repo.ToLowerInvariant(),desc=Text(item,"description"),label=(repo+" "+desc).ToLowerInvariant();
                    if(!ValidRepo(repo)||known.Contains(key)||current.ContainsKey(key)||Flag(item,"fork")||Text(item,"language")=="")continue;
                    if(new[]{"installer","free download","cracked","keygen","game download","torrent"}.Any(label.Contains)||!Regex.IsMatch(label,"port|recomp|compatib|emulat|runtime|translat|shader"))continue;
                    current[key]=new JsonObject{["repo"]=repo,["name"]=Text(item,"name",repo),["description"]=desc,["foundAt"]=Now(),["substance"]="—",["source"]="Local GitHub search"};
                    changes.Add(Event(Text(item,"name",repo),"New candidate","Local GitHub search; unreviewed"));
                }
            }
            catch(Exception e) when(e is not OperationCanceledException){errors.Add($"Candidate search ({query}): {e.Message}");}
        }
        return (new JsonArray(current.Values.OrderByDescending(x=>Text(x,"foundAt")).Take(100).ToArray()),changes,errors);
    }

    public static async Task<ScanResult> Scan(JsonObject state,IProgress<string>? progress,CancellationToken token)
    {
        string scanStarted=Now();var old=Object(state,"tracker");var tracker=(JsonObject)old.DeepClone();var errors=new List<string>();
        try{tracker=await GetTracker(token).ConfigureAwait(false);}catch(Exception e) when(e is not OperationCanceledException){errors.Add("PortWatch data: "+e.Message);}
        var changes=Compare(old,tracker);var live=(JsonObject)Object(state,"live").DeepClone();
        var efforts=Array(tracker,"efforts").Where(x=>x is not null&&ValidRepo(Text(x,"repo"))&&!Flag(x,"nuked")).ToList();
        int completed=0; progress?.Report($"Checking public repositories: 0/{efforts.Count}");using var gate=new SemaphoreSlim(4);
        var tasks=efforts.Select(async e=>{await gate.WaitAsync(token).ConfigureAwait(false);try{return await PollRepo(e!,Object(live,Text(e,"repo")),token).ConfigureAwait(false);}finally{gate.Release();progress?.Report($"Checking public repositories: {Interlocked.Increment(ref completed)}/{efforts.Count}");}}).ToArray();
        foreach(var r in await Task.WhenAll(tasks).ConfigureAwait(false)){live[r.Repo]=r.Live;changes.AddRange(r.Events);if(Text(r.Live,"error")!="")errors.Add(r.Repo+": "+Text(r.Live,"error"));}
        progress?.Report("Looking for newly created public projects…");var discovered=await Discover(tracker,Array(state,"local_candidates"),token).ConfigureAwait(false);changes.AddRange(discovered.Events);errors.AddRange(discovered.Errors);
        changes=changes.OrderByDescending(x=>Text(x,"time")).ToList();
        var result=new JsonObject{["tracker"]=tracker,["live"]=live,["local_candidates"]=discovered.Candidates,["events"]=new JsonArray(changes.Cast<JsonNode?>().Concat(Array(state,"events").Select(x=>x?.DeepClone())).Take(250).ToArray()),["last_scan_started"]=scanStarted,["last_scan"]=Now(),["errors"]=new JsonArray(errors.Take(60).Select(x=>(JsonNode?)JsonValue.Create(x)).ToArray())};
        try{Save(StateFile,result);}catch(IOException e){((JsonArray)result["errors"]!).Add("Cache: "+e.Message);}
        return new(result,changes.Count,changes.Where(x=>Text(x,"kind")=="Milestone alert").Select(ToActivity).ToList());
    }

    public static IReadOnlyList<ProjectRow> Projects(JsonObject state,bool watchlist=true)
    {
        var tracker=Object(state,"tracker");var live=Object(state,"live");int rank=0;
        return Array(tracker,"efforts").OrderBy(x=>Flag(x,"nuked")).ThenByDescending(x=>Score(x).Points).ThenByDescending(x=>Number(Object(x,"stats"),"stars")).Where(x=>watchlist||Text(x,"tier")=="tracked").Select(e=>{
            string repo=Text(e,"repo");var score=Score(e);var record=Object(live,repo);var commit=Object(record,"commit");bool offline=Flag(e,"nuked");string r=Text(e,"tier")=="tracked"&&!offline?(++rank).ToString():"—";
            return new ProjectRow(r,Text(e,"name",repo),$"{score.Points:g}%",score.Stage,Stamp(Text(commit,"updated",Text(Object(e,"stats"),"pushedAt"))),offline?"Offline":Text(record,"status",repo==""?"Private":"Not checked"),Text(commit,"title","—"),repo,Text(e,"summary","No summary")+"  ·  "+Text(e,"verification","unverified"),score.Points);
        }).ToList();
    }
    private static ActivityRow ToActivity(JsonNode? e)=>new(Stamp(Text(e,"time")),Text(e,"project"),Text(e,"kind"),Text(e,"detail"));
    public static IReadOnlyList<ActivityRow> Activities(JsonObject state)=>Array(state,"events").Select(ToActivity).ToList();
    public static IReadOnlyList<CandidateRow> Candidates(JsonObject state)=>Array(Object(state,"tracker"),"candidates").Concat(Array(state,"local_candidates")).Select(c=>new CandidateRow(Text(c,"repo"),Text(c,"source","PortWatch"),Text(c,"substance","—"),Stamp(Text(c,"foundAt")),Text(c,"description"))).ToList();

    public static string IntelligenceBrief(JsonObject state,int maxWords=75)
    {
        var ranked=Projects(state,true).Where(p=>p.Rank!="—"&&p.Status!="Offline").ToList();
        if(ranked.Count==0)return "Waiting for the first fetch. This brief summarizes the current leaders, milestone status, recent repository activity, and reported blockers.";
        string Clean(string value)=>Regex.Replace(value.Trim(),@"\s+"," ");
        string JoinNames(IEnumerable<string> source){var names=source.Select(Clean).Distinct().ToArray();return names.Length switch{0=>"",1=>names[0],2=>names[0]+" and "+names[1],_=>string.Join(", ",names[..^1])+", and "+names[^1]};}
        var lead=ranked[0];var publicLead=ranked.FirstOrDefault(p=>ValidRepo(p.Repo)&&p.Status!="Unavailable");var sentences=new List<string>();
        string stage=lead.Stage=="30 FPS"?"30 FPS in-game":lead.Stage.ToLowerInvariant();
        sentences.Add($"{Clean(lead.Name)} leads at {lead.ScoreText} ({stage}){(!ValidRepo(lead.Repo)?", with no public repository listed":"")}.");
        if(publicLead is not null&&publicLead.Name!=lead.Name)sentences.Add($"{Clean(publicLead.Name)} leads public efforts at {publicLead.ScoreText}.");
        else if(ranked.Count>1)sentences.Add($"{Clean(ranked[1].Name)} follows at {ranked[1].ScoreText}.");
        var stable=ranked.Where(p=>p.Stage=="Stable").ToList();var fast=ranked.Where(p=>p.Stage=="30 FPS").ToList();
        if(stable.Count>0)sentences.Add($"PortWatch marks {JoinNames(stable.Take(2).Select(p=>p.Name))} stable.");
        else if(fast.Count>0)sentences.Add($"PortWatch reports in-game 30 FPS for {JoinNames(fast.Take(2).Select(p=>p.Name))}; none is marked stable.");
        else sentences.Add("No ranked effort is listed at 30 FPS in-game yet.");
        var updated=ranked.Where(p=>p.Status=="Updated").Take(3).ToList();
        var changed=new List<ProjectRow>();
        if(DateTimeOffset.TryParse(Text(state,"last_scan_started"),out var started))
        {
            var namesChanged=Array(state,"events").Where(e=>Text(e,"kind")=="Score"&&DateTimeOffset.TryParse(Text(e,"time"),out var when)&&when>=started).Select(e=>Text(e,"project")).ToHashSet(StringComparer.OrdinalIgnoreCase);
            changed=ranked.Where(p=>namesChanged.Contains(p.Name)).Take(2).ToList();
        }
        if(changed.Count>0)sentences.Add("PortWatch updated scores for "+JoinNames(changed.Select(p=>$"{p.Name} ({p.ScoreText})"))+".");
        else if(updated.Count>0)sentences.Add($"Latest scan detected updates to {JoinNames(updated.Select(p=>p.Name))}.");
        var names=ranked.Take(4).Select(p=>p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var blockers=string.Join(" ",Array(Object(state,"tracker"),"efforts").Where(e=>names.Contains(Text(e,"name"))).SelectMany(e=>Array(e,"blockers")).Select(v=>v?.ToString()??""));
        var themes=new List<string>();
        foreach(var (pattern,label) in new[]{("render|lighting|graphics|shader|texture|pipeline","rendering"),("crash|hang|watchdog|stability|black screen","GPU stability"),("slow|fps|performance|seconds per frame","performance"),("input|controller","input"),("memory|corrupt","memory")})
            if(Regex.IsMatch(blockers,pattern,RegexOptions.IgnoreCase))themes.Add(label);
        if(themes.Count>0)sentences.Add($"Reported hurdles include {JoinNames(themes.Take(3))}.");
        var brief=new List<string>();int words=0;
        foreach(string sentence in sentences){int n=sentence.Split(' ',StringSplitOptions.RemoveEmptyEntries).Length;if(words+n<=maxWords){brief.Add(sentence);words+=n;}}
        return string.Join(" ",brief);
    }

    public static string Report(JsonObject state)
    {
        var b=new StringBuilder("PORTWATCH DESKTOP — SCAN REPORT\n==============================================\n");
        b.AppendLine($"Generated: {Stamp(Now())}\nLast scan: {Stamp(Text(state,"last_scan"))}\nPortWatch dataset: {Stamp(Text(Object(state,"tracker"),"updated"))}\nScores are editorial estimates, not independently verified benchmarks.\n\nINTELLIGENCE BRIEF\n------------------\n{IntelligenceBrief(state)}\n\nLEADERBOARD\n-----------");
        foreach(var p in Projects(state)){b.AppendLine($"{p.Name} — {p.ScoreText} | {p.Stage} | {p.Status}\n  Latest commit: {p.CommitTime} | {p.CommitTitle}");if(ValidRepo(p.Repo))b.AppendLine("  https://github.com/"+p.Repo);b.AppendLine("  "+p.Detail+"\n");}
        b.AppendLine("ACTIVITY\n--------");foreach(var a in Activities(state))b.AppendLine($"{a.Time} | {a.Project} | {a.Kind} | {a.Detail}");b.AppendLine("\nCANDIDATES (UNREVIEWED)\n-----------------------");foreach(var c in Candidates(state)){b.AppendLine($"{c.Repo} | {c.Source} | {c.Found}\n  {c.Description}");if(ValidRepo(c.Repo))b.AppendLine("  https://github.com/"+c.Repo);}
        b.AppendLine("\nSOURCE WARNINGS\n---------------");var warnings=Array(state,"errors");if(warnings.Count==0)b.AppendLine("No source warnings.");else foreach(var e in warnings)b.AppendLine(e?.ToString());return b.ToString();
    }

    public static void SelfTest()
    {
        var e=JsonNode.Parse("{\"gta\":{\"loads\":1,\"boots\":1,\"menu\":1,\"ingame\":0.8}}");if(Score(e)!=(67,"Main menu"))throw new Exception("Score failed");
        var exe=JsonNode.Parse("{\"status\":\"released\",\"releasedAt\":\"2026-11-23T14:36:00Z\"}");if(ReleaseClock(exe,DateTimeOffset.Parse("2026-11-24T15:36:01Z")).Clock!="01d  01h  00m  01s")throw new Exception("Timer failed");
        var old=JsonNode.Parse("{\"efforts\":[{\"id\":\"x\",\"name\":\"X\",\"gta\":{\"loads\":1,\"boots\":1,\"menu\":1,\"ingame\":1,\"fps\":4}}]}")!.AsObject();
        var fresh=JsonNode.Parse("{\"efforts\":[{\"id\":\"x\",\"name\":\"X\",\"gta\":{\"loads\":1,\"boots\":1,\"menu\":1,\"ingame\":1,\"fps\":30,\"stability\":\"stable\"}}]}")!.AsObject();
        if(!Compare(old,fresh).Any(x=>Text(x,"kind")=="Milestone alert"))throw new Exception("Alert failed");if(!ValidRepo("owner/repo")||ValidRepo("../repo"))throw new Exception("Identifier failed");
        var brief=IntelligenceBrief(JsonNode.Parse("""
        {"tracker":{"efforts":[{"id":"a","name":"Alpha","tier":"tracked","gta":{"loads":1,"boots":1,"menu":1,"ingame":1,"fps":4}},{"id":"b","name":"Beta","repo":"example/beta","tier":"tracked","gta":{"loads":1,"boots":1,"menu":1,"ingame":0.8},"blockers":["Rendering is incomplete, GPU hangs"]}]},"live":{"example/beta":{"status":"Updated"}}}
        """)!.AsObject());if(!brief.Contains("Beta leads public efforts at 67%")||!brief.Contains("No ranked effort is listed at 30 FPS")||brief.Split(' ',StringSplitOptions.RemoveEmptyEntries).Length>75)throw new Exception("Brief failed");
    }
}
