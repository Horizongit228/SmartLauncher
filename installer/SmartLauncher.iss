#define MyAppName "Smart Launcher"
#define MyAppVersion "1.0.3"
#define MyAppPublisher "Smart Launcher"
#define MyAppExeName "SmartLauncher.exe"

[Setup]
AppId={{C67327C7-0471-4E31-BBBC-F3900AC92A39}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppComments=Расширенный поиск приложений
DefaultDirName={localappdata}\Programs\Smart Launcher
DefaultGroupName=Smart Launcher
DisableProgramGroupPage=yes
OutputDir=..\dist\installer
OutputBaseFilename=SmartLauncher-Setup-{#MyAppVersion}
SetupIconFile=..\Assets\Icons\SL.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=yes
AppMutex=SmartLauncher.0C2DA260-87A7-49A8-8BD4-F3F79718CB57
VersionInfoVersion={#MyAppVersion}

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Ярлыки:"; Flags: checkedonce
Name: "startup"; Description: "Запускать Smart Launcher вместе с Windows"; GroupDescription: "Автозапуск:"; Flags: unchecked

[Files]
Source: "..\dist\installed-win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Smart Launcher"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\Smart Launcher"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "SmartLauncher"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Запустить Smart Launcher"; Flags: nowait postinstall skipifsilent; Check: IsRegularInstall
Filename: "{app}\{#MyAppExeName}"; Flags: nowait; Check: IsSmartLauncherUpdate

[Code]
function IsSmartLauncherUpdate: Boolean;
begin
  Result :=
    CompareText(
      ExpandConstant('{param:SLUPDATE|0}'),
      '1') = 0;
end;

function IsRegularInstall: Boolean;
begin
  Result := not IsSmartLauncherUpdate;
end;
