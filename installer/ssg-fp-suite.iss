; SSG FP Suite - Inno Setup Installer Script
; Builds a single .exe installer that deploys to Revit 2023-2026
; and bundles shared .rfa families to C:\SSG FP\Revit Families\.
;
; Prerequisites:
;   - Inno Setup 6.x (https://jrsoftware.org/isinfo.php)
;   - Build both SSG24 and SSG25 in Release mode before compiling this
;
; To compile: right-click this file > Compile, or run:
;   "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\ssg-fp-suite.iss
;
; Output: installer\Output\SSG-FP-Suite-{version}-Setup.exe

#define MyAppName "SSG FP Suite"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "SSG Fire Protection"
#define MyAppURL "https://github.com/cv-us/ssg-fp-suite"

; Paths relative to this .iss file's location
#define SSG24Build "..\src\SSG24\bin\Release"
#define SSG25Build "..\src\SSG25\bin\Release"
#define FamiliesDir "Families"
#define FamiliesInstallDir "C:\SSG FP\Revit Families"

[Setup]
AppId={{B7E3A4F1-9D2C-4A8E-B6F5-1C3D7E9A2B4F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
DefaultDirName={commonpf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=SSG-FP-Suite-{#MyAppVersion}-Setup
Compression=lzma2/ultra64
SolidCompression=yes
PrivilegesRequired=admin
WizardStyle=modern
SetupIconFile=icon.ico
UninstallDisplayIcon={app}\icon.ico
UninstallDisplayName={#MyAppName}
; Allow re-install / upgrade without uninstalling first
UsePreviousAppDir=yes
UsePreviousGroup=yes
; Auto-prompt to close Revit if it's running during install/uninstall
CloseApplications=yes
CloseApplicationsFilter=*.exe
RestartApplications=no
; Minimum Windows 10
MinVersion=10.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
WelcomeLabel2=This will install [name/ver] for Autodesk Revit.%n%nThe add-in provides fire protection design automation tools including hanger placement, pipe routing, annotation, and model checking.%n%nRevit must be closed during installation — the installer will prompt you to close it if needed.

; ── InstallDelete: wipe old addin files before copying new ones ──
; Ensures a clean upgrade even if file names changed between versions.
[InstallDelete]
Type: filesandordirs; Name: "{commonappdata}\Autodesk\Revit\Addins\2023\SSG-FP-Suite"; Check: ShouldInstall2023
Type: filesandordirs; Name: "{commonappdata}\Autodesk\Revit\Addins\2024\SSG-FP-Suite"; Check: ShouldInstall2024
Type: filesandordirs; Name: "{commonappdata}\Autodesk\Revit\Addins\2025\SSG-FP-Suite"; Check: ShouldInstall2025
Type: filesandordirs; Name: "{commonappdata}\Autodesk\Revit\Addins\2026\SSG-FP-Suite"; Check: ShouldInstall2026

; ── Custom pages for Revit version selection ──
[Code]
var
  RevitPage: TWizardPage;
  chk2023, chk2024, chk2025, chk2026: TCheckBox;

function RevitAddinPath(Year: String): String;
begin
  Result := ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\' + Year);
end;

function RevitIsInstalled(Year: String): Boolean;
var
  RegPath: String;
begin
  // Check both 64-bit registry locations where Revit registers
  RegPath := 'SOFTWARE\Autodesk\Revit\Autodesk Revit ' + Year;
  Result := RegKeyExists(HKLM, RegPath);
  if not Result then
  begin
    RegPath := 'SOFTWARE\Autodesk\Revit';
    Result := RegKeyExists(HKLM, RegPath);
  end;
  // Also check if the Addins folder already exists (may have other addins)
  if not Result then
    Result := DirExists(RevitAddinPath(Year));
end;

procedure InitializeWizard;
var
  lbl: TNewStaticText;
  y: Integer;
begin
  RevitPage := CreateCustomPage(wpSelectDir,
    'Select Revit Versions',
    'Choose which Revit versions to install the add-in for.');

  y := 0;

  lbl := TNewStaticText.Create(RevitPage);
  lbl.Parent := RevitPage.Surface;
  lbl.Top := y;
  lbl.Caption := 'Check the Revit versions you want to install SSG FP Suite for:';
  lbl.AutoSize := True;
  y := y + 30;

  chk2023 := TCheckBox.Create(RevitPage);
  chk2023.Parent := RevitPage.Surface;
  chk2023.Top := y;
  chk2023.Width := 400;
  chk2023.Checked := RevitIsInstalled('2023');
  if RevitIsInstalled('2023') then
    chk2023.Caption := 'Revit 2023 (detected)'
  else
    chk2023.Caption := 'Revit 2023';
  y := y + 28;

  chk2024 := TCheckBox.Create(RevitPage);
  chk2024.Parent := RevitPage.Surface;
  chk2024.Top := y;
  chk2024.Width := 400;
  chk2024.Checked := RevitIsInstalled('2024');
  if RevitIsInstalled('2024') then
    chk2024.Caption := 'Revit 2024 (detected)'
  else
    chk2024.Caption := 'Revit 2024';
  y := y + 28;

  chk2025 := TCheckBox.Create(RevitPage);
  chk2025.Parent := RevitPage.Surface;
  chk2025.Top := y;
  chk2025.Width := 400;
  chk2025.Checked := RevitIsInstalled('2025');
  if RevitIsInstalled('2025') then
    chk2025.Caption := 'Revit 2025 (detected)'
  else
    chk2025.Caption := 'Revit 2025';
  y := y + 28;

  chk2026 := TCheckBox.Create(RevitPage);
  chk2026.Parent := RevitPage.Surface;
  chk2026.Top := y;
  chk2026.Width := 400;
  chk2026.Checked := RevitIsInstalled('2026');
  if RevitIsInstalled('2026') then
    chk2026.Caption := 'Revit 2026 (detected)'
  else
    chk2026.Caption := 'Revit 2026';
  y := y + 40;

  lbl := TNewStaticText.Create(RevitPage);
  lbl.Parent := RevitPage.Surface;
  lbl.Top := y;
  lbl.Caption := 'Detected versions are pre-checked. You can install for versions not yet installed.';
  lbl.Font.Color := clGray;
  lbl.AutoSize := True;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = RevitPage.ID then
  begin
    if not (chk2023.Checked or chk2024.Checked or chk2025.Checked or chk2026.Checked) then
    begin
      MsgBox('Please select at least one Revit version.', mbError, MB_OK);
      Result := False;
    end;
  end;
end;

function ShouldInstall2023: Boolean;
begin
  Result := chk2023.Checked;
end;

function ShouldInstall2024: Boolean;
begin
  Result := chk2024.Checked;
end;

function ShouldInstall2025: Boolean;
begin
  Result := chk2025.Checked;
end;

function ShouldInstall2026: Boolean;
begin
  Result := chk2026.Checked;
end;

// ── Uninstall: clean up addin files and folders ──
// Also prompts (default No) whether to remove the families folder.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Years: array[0..3] of String;
  i: Integer;
  AddinPath, SubFolder, AddinFile24, AddinFile25: String;
  FamInstallPath: String;
  RemoveFamilies: Integer;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    Years[0] := '2023';
    Years[1] := '2024';
    Years[2] := '2025';
    Years[3] := '2026';

    for i := 0 to 3 do
    begin
      AddinPath := ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\' + Years[i]);
      SubFolder := AddinPath + '\SSG-FP-Suite';
      AddinFile24 := AddinPath + '\SSG24.addin';
      AddinFile25 := AddinPath + '\SSG25.addin';

      // Delete .addin manifests
      if FileExists(AddinFile24) then
        DeleteFile(AddinFile24);
      if FileExists(AddinFile25) then
        DeleteFile(AddinFile25);

      // Delete the SSG-FP-Suite subfolder and everything in it
      if DirExists(SubFolder) then
        DelTree(SubFolder, True, True, True);
    end;

    // Ask whether to remove the shared Revit families folder.
    // Default is No so users who customized families don't lose them.
    FamInstallPath := '{#FamiliesInstallDir}';
    if DirExists(FamInstallPath) then
    begin
      RemoveFamilies := MsgBox(
        'Also remove the installed Revit families at:' + #13#10 + #13#10 +
        FamInstallPath + #13#10 + #13#10 +
        'Select "No" if you want to keep these families (for example, if you have added your own custom families to that folder).',
        mbConfirmation, MB_YESNO or MB_DEFBUTTON2);
      if RemoveFamilies = IDYES then
      begin
        DelTree(FamInstallPath, True, True, True);
      end;
    end;
  end;
end;

[Files]
; ── SSG24 files (Revit 2023 & 2024) ──
; .addin manifest goes in the Addins\{year}\ root
Source: "SSG24.addin"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2023"; Flags: ignoreversion; Check: ShouldInstall2023
Source: "SSG24.addin"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024"; Flags: ignoreversion; Check: ShouldInstall2024

; DLLs and config go in a subfolder
Source: "{#SSG24Build}\SSG24.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2023\SSG-FP-Suite"; Flags: ignoreversion; Check: ShouldInstall2023
Source: "{#SSG24Build}\SSG24.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\SSG-FP-Suite"; Flags: ignoreversion; Check: ShouldInstall2024

; Dependency DLLs for SSG24 (.NET Framework 4.8 needs these)
Source: "{#SSG24Build}\Microsoft.Bcl.AsyncInterfaces.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2023\SSG-FP-Suite"; Flags: ignoreversion; Check: ShouldInstall2023
Source: "{#SSG24Build}\Microsoft.Bcl.AsyncInterfaces.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\SSG-FP-Suite"; Flags: ignoreversion; Check: ShouldInstall2024

Source: "{#SSG24Build}\System.Buffers.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2023\SSG-FP-Suite"; Flags: ignoreversion; Check: ShouldInstall2023
Source: "{#SSG24Build}\System.Buffers.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\SSG-FP-Suite"; Flags: ignoreversion; Check: ShouldInstall2024

Source: "{#SSG24Build}\System.Memory.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2023\SSG-FP-Suite"; Flags: ignoreversion; Check: ShouldInstall2023
Source: "{#SSG24Build}\System.Memory.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\SSG-FP-Suite"; Flags: ignoreversion; Check: ShouldInstall2024

Source: "{#SSG24Build}\System.Numerics.Vectors.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2023\SSG-FP-Suite"; Flags: ignoreversion; Check: ShouldInstall2023
Source: "{#SSG24Build}\System.Numerics.Vectors.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\SSG-FP-Suite"; Flags: ignoreversion; Check: ShouldInstall2024

Source: "{#SSG24Build}\System.Runtime.CompilerServices.Unsafe.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2023\SSG-FP-Suite"; Flags: ignoreversion; Check: ShouldInstall2023
Source: "{#SSG24Build}\System.Runtime.CompilerServices.Unsafe.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\SSG-FP-Suite"; Flags: ignoreversion; Check: ShouldInstall2024

Source: "{#SSG24Build}\System.Text.Encodings.Web.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2023\SSG-FP-Suite"; Flags: ignoreversion; Check: ShouldInstall2023
Source: "{#SSG24Build}\System.Text.Encodings.Web.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\SSG-FP-Suite"; Flags: ignoreversion; Check: ShouldInstall2024

Source: "{#SSG24Build}\System.Text.Json.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2023\SSG-FP-Suite"; Flags: ignoreversion; Check: ShouldInstall2023
Source: "{#SSG24Build}\System.Text.Json.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\SSG-FP-Suite"; Flags: ignoreversion; Check: ShouldInstall2024

Source: "{#SSG24Build}\System.Threading.Tasks.Extensions.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2023\SSG-FP-Suite"; Flags: ignoreversion; Check: ShouldInstall2023
Source: "{#SSG24Build}\System.Threading.Tasks.Extensions.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\SSG-FP-Suite"; Flags: ignoreversion; Check: ShouldInstall2024

Source: "{#SSG24Build}\System.ValueTuple.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2023\SSG-FP-Suite"; Flags: ignoreversion; Check: ShouldInstall2023
Source: "{#SSG24Build}\System.ValueTuple.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\SSG-FP-Suite"; Flags: ignoreversion; Check: ShouldInstall2024

; Config
Source: "{#SSG24Build}\Config\defaults.json"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2023\SSG-FP-Suite\Config"; Flags: ignoreversion; Check: ShouldInstall2023
Source: "{#SSG24Build}\Config\defaults.json"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\SSG-FP-Suite\Config"; Flags: ignoreversion; Check: ShouldInstall2024

; ── SSG25 files (Revit 2025 & 2026) ──
; .addin manifest
Source: "SSG25.addin"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025"; Flags: ignoreversion; Check: ShouldInstall2025
Source: "SSG25.addin"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026"; Flags: ignoreversion; Check: ShouldInstall2026

; DLLs
Source: "{#SSG25Build}\SSG25.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\SSG-FP-Suite"; Flags: ignoreversion; Check: ShouldInstall2025
Source: "{#SSG25Build}\SSG25.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\SSG-FP-Suite"; Flags: ignoreversion; Check: ShouldInstall2026

Source: "{#SSG25Build}\SSG25.deps.json"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\SSG-FP-Suite"; Flags: ignoreversion; Check: ShouldInstall2025
Source: "{#SSG25Build}\SSG25.deps.json"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\SSG-FP-Suite"; Flags: ignoreversion; Check: ShouldInstall2026

; Config
Source: "{#SSG25Build}\Config\defaults.json"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\SSG-FP-Suite\Config"; Flags: ignoreversion; Check: ShouldInstall2025
Source: "{#SSG25Build}\Config\defaults.json"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\SSG-FP-Suite\Config"; Flags: ignoreversion; Check: ShouldInstall2026

; ── Shared Revit families (bundled with installer) ──
; All .rfa files under installer\Families\ are deployed to C:\SSG FP\Revit Families\
; Subfolder structure is preserved via recursesubdirs + createallsubdirs.
; skipifsourcedoesntexist lets the installer compile even when no .rfa files have
; been dropped in yet (useful during early development).
Source: "{#FamiliesDir}\*.rfa"; DestDir: "{#FamiliesInstallDir}"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "{#FamiliesDir}\*"; Excludes: "*.rfa,README.txt"; DestDir: "{#FamiliesInstallDir}"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist

; ── App icon (stored in install dir for uninstall display) ──
Source: "icon.ico"; DestDir: "{app}"; Flags: ignoreversion
