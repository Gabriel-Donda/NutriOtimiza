; Instalador do NutriOtimiza (Inno Setup 6)
; Gerado pelo tools\build.ps1, que passa AppVersion, Arch (x64/x86) e SourceDir.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef Arch
  #define Arch "x64"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish\" + Arch
#endif

#define AppName "NutriOtimiza"
#define AppExe "NutriOtimiza.exe"
#define AppPublisher "Nutriport"

[Setup]
; AppId fixo: identifica o app entre versões (atualizar = substituir a instalação existente)
AppId={{4406D961-764D-4431-95DC-A8C890516474}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppCopyright=© 2026 {#AppPublisher}
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription=Instalador do {#AppName}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
OutputDir=..\artifacts
OutputBaseFilename={#AppName}-Setup-{#AppVersion}-{#Arch}
SetupIconFile=..\src\OtimizaPC.App\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
; Identidade visual da Nutriport (gerada por tools\generate-brand-assets.ps1)
WizardStyle=modern
WizardImageFile=wizard-large.bmp,wizard-large-2x.bmp
WizardSmallImageFile=wizard-small.bmp,wizard-small-2x.bmp
WizardImageStretch=no
WizardImageBackColor=$202020
Compression=lzma2/max
SolidCompression=yes
; Windows 8.1 / Server 2012 R2 ou superior
MinVersion=6.3
CloseApplications=yes
RestartApplications=no
#if Arch == "x64"
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#else
; A versão 32 bits só é oferecida para Windows 32 bits (em 64 bits, os sensores exigem o processo 64 bits)
ArchitecturesAllowed=x86os
#endif

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; Também roda após atualização silenciosa, reabrindo o app
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall shellexec

[UninstallRun]
; Remove a tarefa de limpeza automática, se tiver sido criada
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""NutriOtimiza\LimpezaAutomatica"" /F"; Flags: runhidden; RunOnceId: "RemoverTarefaLimpeza"

[Code]
const
  DotNet48Release = 528040;

function IsDotNet48Installed: Boolean;
var
  Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release)
            and (Release >= DotNet48Release);
end;

function InitializeSetup: Boolean;
var
  ErrorCode: Integer;
begin
  Result := IsDotNet48Installed;
  if not Result then
  begin
    if SuppressibleMsgBox('O {#AppName} precisa do .NET Framework 4.8, que não está instalado neste computador.' + #13#10#13#10 +
                          'Deseja abrir a página oficial de download da Microsoft?', mbConfirmation, MB_YESNO, IDNO) = IDYES then
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet-framework/net48', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
  end;
end;
