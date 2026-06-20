; Inno Setup script đóng gói DotDownloader + FFmpeg.
; Build trước: dotnet publish DM.App -c Release -r win-x64 --self-contained false -o publish
; rồi copy ffmpeg.exe vào publish\tools\ffmpeg\ (hoặc để sẵn trong tools\ffmpeg\ và publish copy).
; Sau đó chạy: ISCC.exe installer\DotDownloader.iss

#define AppName "DotDownloader"
#define AppVersion "0.1.0"
#define Publisher "DotDownloader"

[Setup]
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#Publisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\DM.App.exe
OutputBaseFilename=DotDownloader-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest

[Files]
; Toàn bộ output publish của DM.App.
Source: "..\publish\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs
; FFmpeg bundle (nếu chưa nằm trong publish\tools).
Source: "..\tools\ffmpeg\ffmpeg.exe"; DestDir: "{app}\tools\ffmpeg"; Flags: skipifsourcedoesntexist

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\DM.App.exe"
Name: "{commondesktop}\{#AppName}"; Filename: "{app}\DM.App.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Tạo shortcut ngoài desktop"; GroupDescription: "Tùy chọn:"
Name: "startup"; Description: "Khởi động cùng Windows"; GroupDescription: "Tùy chọn:"; Flags: unchecked

[Registry]
; Tùy chọn khởi động cùng Windows (HKCU\...\Run).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "DotDownloader"; ValueData: """{app}\DM.App.exe"""; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\DM.App.exe"; Description: "Khởi chạy {#AppName}"; Flags: nowait postinstall skipifsilent
