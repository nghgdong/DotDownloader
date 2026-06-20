# Đóng gói DotDownloader (Installer)

## Bước 1 — Publish app
```powershell
dotnet publish DM.App\DM.App.csproj -c Release -r win-x64 --self-contained false -o publish
```
(`--self-contained true` nếu muốn không phụ thuộc .NET runtime cài sẵn — file lớn hơn.)

## Bước 2 — Bundle FFmpeg
Đặt `ffmpeg.exe` vào `tools\ffmpeg\ffmpeg.exe`. `Ffmpeg.ResolvePath` ưu tiên
`tools/ffmpeg/ffmpeg.exe` cạnh app, nên copy vào `publish\tools\ffmpeg\` hoặc để
installer copy (đã khai trong `.iss`).

## Bước 3 — Build installer (Inno Setup)
Cài [Inno Setup](https://jrsoftware.org/isinfo.php) rồi:
```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\DotDownloader.iss
```
Ra file `installer\Output\DotDownloader-Setup-0.1.0.exe`.

## Ghi chú
- Installer tùy chọn "Khởi động cùng Windows" (registry HKCU\Run) trùng với toggle trong Settings của app.
- Extension load unpacked từ thư mục `extension/` (chưa đóng gói lên Chrome Web Store ở v1).
- MSIX là phương án thay thế (chưa dùng ở v1) nếu cần phân phối qua Store / chữ ký.
