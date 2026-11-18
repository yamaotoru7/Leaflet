; Leaflet のインストーラー。tools/installer/build-installer.ps1 から ISCC に渡して作る。
; ユーザーごとのインストール（管理者権限なし）で %LocalAppData%\Programs\Leaflet に入れる。
; 中身は self-contained の publish（PublishDir）。.NET ランタイムは別に入れなくてよい。
; 設定と読んだ位置（%LocalAppData%\Leaflet\settings.json）はアンインストールでも消さない。

#ifndef AppVersion
  #define AppVersion "0.1.1"
#endif
#ifndef PublishDir
  #define PublishDir "..\.work\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\.work\installer"
#endif

[Setup]
; src/Leaflet.App/Updates/InstalledCopy.cs の UninstallKey と同じ値。インストールしたコピーの識別に使う（変えない）
AppId={{92262E79-CF64-4553-9D60-24CA2D62301B}
AppName=Leaflet
AppVersion={#AppVersion}
AppVerName=Leaflet {#AppVersion}
AppPublisher=toru
AppPublisherURL=https://github.com/yamaotoru7/Leaflet
DefaultDirName={localappdata}\Programs\Leaflet
DefaultGroupName=Leaflet
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=Leaflet-{#AppVersion}-setup
SetupIconFile=..\src\Leaflet.App\Assets\Leaflet.ico
UninstallDisplayIcon={app}\Leaflet.exe
LicenseFile=..\LICENSE
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

; 更新では古い版の DLL を先に消す。上書きだけだと、新しい版で無くなった DLL が残り、
; アプリがそれを読み込んでしまう。このアプリが入っているフォルダのときだけ、このアプリが置くものだけを消す。
[InstallDelete]
Type: files; Name: "{app}\*.dll"; Check: IsAppFolder
Type: filesandordirs; Name: "{app}\licenses"; Check: IsAppFolder

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Leaflet"; Filename: "{app}\Leaflet.exe"
Name: "{autodesktop}\Leaflet"; Filename: "{app}\Leaflet.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Leaflet.exe"; Description: "{cm:LaunchProgram,Leaflet}"; Flags: nowait postinstall skipifsilent
; アプリの更新（InstalledCopy.StartInstaller）は /VERYSILENT /RELAUNCH=1 で起動する。入れ終わったら起動し直す
Filename: "{app}\Leaflet.exe"; Flags: nowait; Check: RelaunchAfterUpdate

[Code]
function IsAppFolder: Boolean;
begin
  Result := FileExists(ExpandConstant('{app}\Leaflet.exe'));
end;

function RelaunchAfterUpdate: Boolean;
begin
  Result := WizardSilent and (ExpandConstant('{param:RELAUNCH|0}') = '1');
end;
