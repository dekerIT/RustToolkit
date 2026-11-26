#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{41F48AF1-F9D8-4941-A6BB-F6873C199FCE}
AppName=RustToolkit
AppVersion={#AppVersion}
AppPublisher=dekerIT
AppPublisherURL=https://github.com/dekerIT/RustToolkit
DefaultDirName={localappdata}\Programs\RustToolkit
DefaultGroupName=RustToolkit
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
CloseApplications=yes
UninstallDisplayIcon={app}\RustToolkit.Desktop.exe
OutputDir=..\artifacts
OutputBaseFilename=RustToolkit-Setup-v{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Dirs]
Name: "{localappdata}\RustToolkit\Notes"

[Icons]
Name: "{autoprograms}\RustToolkit"; Filename: "{app}\RustToolkit.Desktop.exe"
Name: "{autoprograms}\Rust Notes"; Filename: "{app}\notes\notes.exe"; WorkingDir: "{localappdata}\RustToolkit\Notes"
Name: "{autodesktop}\RustToolkit"; Filename: "{app}\RustToolkit.Desktop.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\RustToolkit.Desktop.exe"; Description: "Launch RustToolkit"; Flags: nowait postinstall skipifsilent
