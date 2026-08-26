; Dream Unreal Manager 安装包脚本（Inno Setup 6）
;
; 由 .github/workflows/release.yml 调用，所有可变参数从命令行传入：
;   ISCC.exe /DAppVersion=0.4.0 /DAppVersionNumeric=0.4.0.0 /DAppArch=x64 ^
;            /DSourceDir=<publish 目录> /DOutputDir=<产物目录> /DOutputName=<不含扩展名的文件名> ^
;            installer\DreamUnrealManager.iss
;
; 本地手动打包示例：
;   dotnet publish DreamUnrealManager\DreamUnrealManager.csproj -c Release -r win-x64 -p:Platform=x64 -o publish\win-x64
;   iscc /DSourceDir=%CD%\publish\win-x64 /DOutputDir=%CD%\artifacts installer\DreamUnrealManager.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef AppVersionNumeric
  #define AppVersionNumeric "0.0.0.0"
#endif
#ifndef AppArch
  #define AppArch "x64"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif
#ifndef OutputName
  #define OutputName "DreamUnrealManager-Setup"
#endif

#define AppName "Dream Unreal Manager"
#define AppPublisher "64HzGames"
#define AppUrl "https://github.com/TypeDreamMoon/DreamUnrealManager"
#define AppExeName "DreamUnrealManager.exe"

[Setup]
; AppId 决定升级识别，任何时候都不要改动
AppId={{B663AC5B-0D17-4C38-B000-466388AE4E1F}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
VersionInfoVersion={#AppVersionNumeric}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases

; 默认按当前用户安装，不弹 UAC；向导里仍可切换为所有用户安装
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
AllowNoIcons=yes
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}

ArchitecturesAllowed={#AppArch}
#if AppArch != "x86"
ArchitecturesInstallIn64BitMode={#AppArch}
#endif

OutputDir={#OutputDir}
OutputBaseFilename={#OutputName}
SetupIconFile={#SourcePath}\..\DreamUnrealManager\Assets\WindowIcon.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; 升级安装时先关掉正在运行的实例，避免文件占用
CloseApplications=yes
RestartApplications=no

[Languages]
; Inno Setup 官方发行版只自带这些语言，简体中文属于非官方翻译，
; 需要中文向导时把 ChineseSimplified.isl 放进 installer\ 并在这里追加一行。
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; 卸载时清掉运行期生成的残留文件，用户数据在 %LOCALAPPDATA%\DreamUnrealManager，保留不动
Type: filesandordirs; Name: "{app}"
