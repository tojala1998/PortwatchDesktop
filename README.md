# Portwatch Desktop

A small, unofficial Windows desktop dashboard for following PortWatch VI's public project tracker and live GitHub repository activity.

## Features

- Editorial leaderboard, milestone progress, and executable release timer
- Responsive race track with the top 5–8 efforts mapped to the milestone scale; click markers to select projects
- Compact Intelligence Brief synthesized locally from the tracker, milestone data, reported blockers, and recent GitHub activity (no external AI service or API key)
- Fetch Now (F5) and adjustable 5–120 minute automatic refresh
- Public GitHub commit, release, and README change monitoring
- New public project candidates and repository links
- Milestone alerts for 30 FPS in-game or stable gameplay
- Close the dashboard to the Windows notification area for passive monitoring; reopen or explicitly exit using the tray icon
- Save and view scan reports; F11 full screen (Esc to exit)
- Native Windows WPF interface with high-DPI scaling, neon sunset styling, and local cached state

No Python, API keys, game files, or account are required. This app tracks public project metadata; it does not run, provide, or download the game. PortWatch scores are editorial estimates, not independent gameplay benchmarks. Candidate repositories are unreviewed.

The app caches the latest scan in `%LOCALAPPDATA%\PortWatchDesktop\state.json` and stores preferences in `settings.json` in the same folder. It displays the cache immediately on launch, then refreshes if auto-fetch is enabled. When auto-fetch is paused, F5 or Fetch Now refreshes on demand.

Closing the window keeps it running in the notification area. Right-click the tray icon to fetch, toggle auto-fetch, reopen, or exit. Milestone alerts continue while the dashboard is hidden. Launching the executable again reopens the existing dashboard rather than starting a second monitor.

## Download

Download either `PortwatchDesktop-Setup.exe` (per-user installation with Start menu and optional desktop/startup shortcuts) or `PortwatchDesktop-Portable.exe` (no installation) from Releases. Both self-contained builds need no separate .NET installation and share settings and cached data. The installer uses `%LOCALAPPDATA%\Programs\PortwatchDesktop` and can be removed through Windows Installed Apps.

Open Settings with the gear icon to configure automatic fetch, notification types, sound, duration, watchlist visibility, and close-to-tray behavior. Holding **Shift** while clicking the gear opens developer mode: test either milestone notification, watch session fetch logs, and copy diagnostics. Test notifications do not modify tracked milestones.

## Build from source

Install the .NET 8 SDK, then run:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

The executable is in `bin/Release/net8.0-windows/win-x64/publish/`.

## Attribution

The public tracker and published scoring methodology are provided by [PortWatch VI](https://portwatch-vi.xyz/) ([source](https://github.com/4lwayz/portwatch-vi)). This independent desktop app is not affiliated with its creator, Rockstar Games, or Microsoft.
