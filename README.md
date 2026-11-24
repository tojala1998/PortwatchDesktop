# Portwatch Desktop

A small, unofficial Windows desktop dashboard for following PortWatch VI's public project tracker and live GitHub repository activity.

## Features

- Editorial leaderboard, milestone progress, and executable release timer
- Fetch Now (F5) and adjustable 5-120 minute automatic refresh
- Public GitHub commit, release, and README change monitoring
- New public project candidates and repository links
- Milestone alerts for 30 FPS in-game or stable gameplay
- Save and view scan reports; F11 full screen (Esc to exit)
- Native Windows WPF interface with high-DPI scaling, neon sunset styling, and local cached state

No Python, API keys, game files, or account are required. This app tracks public project metadata; it does not run, provide, or download the game. PortWatch scores are editorial estimates, not independent gameplay benchmarks. Candidate repositories are unreviewed.

## Download

Download the Windows executable from the repository's Releases section. The self-contained release needs no separate .NET installation. A framework-dependent build requires the Windows .NET 8 Desktop Runtime.

## Build from source

Install the .NET 8 SDK, then run:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

The executable is in `bin/Release/net8.0-windows/win-x64/publish/`.

## Attribution

The public tracker and published scoring methodology are provided by [PortWatch VI](https://portwatch-vi.xyz/) ([source](https://github.com/4lwayz/portwatch-vi)). This independent desktop app is not affiliated with its creator, Rockstar Games, or Microsoft.

