#define MyAppName "AeroHub"
#define MyAppVersion "0.1.0"
#define MyAppPublisher "AeroHub"
#define MyAppExeName "AeroHub.Api.exe"

[Setup]
AppId={{A6E2B7F1-6A16-4E07-9D31-6C4F8A6B0F44}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\release\installer\setup
OutputBaseFilename=AeroHub-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayIcon={app}\{#MyAppExeName}

[Files]
Source: "..\release\installer\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\AeroHub"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--urls http://localhost:5157"; WorkingDir: "{app}"
Name: "{commondesktop}\AeroHub"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--urls http://localhost:5157"; WorkingDir: "{app}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Run]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--urls http://localhost:5157"; Description: "Launch AeroHub"; Flags: nowait postinstall skipifsilent