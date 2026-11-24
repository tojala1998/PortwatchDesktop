#ifndef AppVersion
  #define AppVersion "2.1.0"
#endif
[Setup]
AppId={{AB7A3E42-9B1A-45E3-B224-4A1C68275F08}
AppName=Portwatch Desktop
AppVersion={#AppVersion}
AppPublisher=Portwatch Desktop
AppPublisherURL=https://github.com/tojala1998/PortwatchDesktop
DefaultDirName={localappdata}\Programs\PortwatchDesktop
DefaultGroupName=Portwatch Desktop
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts
OutputBaseFilename=PortwatchDesktop-Setup
SetupIconFile=..\portwatch.ico
UninstallDisplayIcon={app}\PortwatchDesktop.exe
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked
Name: "startup"; Description: "Start Portwatch when I sign in to Windows (in the notification area)"; Flags: unchecked

[Files]
Source: "..\artifacts\PortwatchDesktop.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Portwatch Desktop"; Filename: "{app}\PortwatchDesktop.exe"
Name: "{userdesktop}\Portwatch Desktop"; Filename: "{app}\PortwatchDesktop.exe"; Tasks: desktopicon
Name: "{userstartup}\Portwatch Desktop"; Filename: "{app}\PortwatchDesktop.exe"; Parameters: "--start-hidden"; Tasks: startup

[Run]
Filename: "{app}\PortwatchDesktop.exe"; Description: "Launch Portwatch Desktop"; Flags: nowait postinstall skipifsilent
