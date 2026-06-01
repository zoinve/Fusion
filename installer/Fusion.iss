#define MyAppId "{{4B2B8E28-4721-4DFA-9341-630891381580}}"
#define MyAppName "Fusion"
#define MyAppExeName "Fusion.exe"

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

#ifndef SourceDir
  #error SourceDir must be defined.
#endif

#ifndef InstallerOutputDir
  #error InstallerOutputDir must be defined.
#endif

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppName}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir={#InstallerOutputDir}
OutputBaseFilename=Fusion-Setup-{#ArchSuffix}-{#MyAppVersion}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
#ifndef ArchSuffix
  #define ArchSuffix "x64"
#endif

#ifndef Architecture
  #define Architecture "x64compatible"
#endif
ArchitecturesAllowed={#Architecture}
ArchitecturesInstallIn64BitMode={#Architecture}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "default"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional tasks:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
