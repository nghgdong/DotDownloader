# DotDownloader

Download manager kiểu IDM cho Windows — **desktop app (.NET 8 + WPF)** + **Chromium extension (MV3)**, giao tiếp qua local HTTP server loopback (`127.0.0.1` + token).

## Tính năng
- **Tải đa luồng** (multi-segment byte-range) — nhanh hơn nhiều lần đơn luồng (đo thực tế ~10× trên file 100MB).
- **Resume bền vững**: kill app/mất mạng → mở lại tải tiếp; metadata ghi atomic, checksum khớp.
- **Bắt link & video từ trình duyệt**: file thường + **video đang phát** — MP4 trực tiếp, **HLS `.m3u8`**, **DASH `.mpd`** (kể cả **AES-128 key công khai**), ghép ra mp4 bằng FFmpeg (`-c copy`).
- **Torrent / magnet** (qua MonoTorrent): magnet, file `.torrent`, hoặc URL `.torrent`.
- **Hàng đợi** giới hạn tải đồng thời, **lập lịch** (ScheduledAt), **phân loại tự động** theo đuôi file.
- **Giới hạn tốc độ** toàn cục (token bucket).
- UI MVVM: DataGrid tiến độ realtime (throttle 250ms), tray icon + minimize to tray, khởi động cùng Windows.

> ⚠️ **Phạm vi pháp lý:** chỉ tải nội dung **công khai/hợp pháp**. **KHÔNG** bypass DRM (Widevine/PlayReady/FairPlay — Netflix/Spotify…); phá DRM là bất hợp pháp ở hầu hết các nước và nằm ngoài phạm vi dự án. AES-128 chỉ áp dụng khi key được công khai trong playlist (không phải DRM).

## Cấu trúc
```
DM.Core/        engine, models, persistence, queue, video, torrent — không phụ thuộc UI
DM.Server/      ASP.NET Minimal API loopback (host trong app)
DM.App/         WPF MVVM (CommunityToolkit.Mvvm)
DM.Core.Tests/  xUnit + FluentAssertions
extension/      Manifest V3
tools/ffmpeg/   FFmpeg binary (tải riêng, KHÔNG commit)
installer/      Inno Setup script
docs/           SPEC / PLAN / TASKS / CLAUDE
```

## Build & chạy
```bash
dotnet restore
dotnet build
dotnet test                  # 83 test offline
dotnet run --project DM.App  # chạy app (Windows)
```
Một số test e2e (HLS/DASH/torrent/benchmark) chỉ chạy khi đặt `DM_NET_TESTS=1` (cần mạng + FFmpeg).

**FFmpeg:** đặt `ffmpeg.exe` vào `tools/ffmpeg/` (hoặc để trên PATH). Resolve theo thứ tự: tham số → env `DM_FFMPEG` → `tools/ffmpeg/` → PATH.

## Extension
1. `chrome://extensions` → bật Developer mode → **Load unpacked** → chọn thư mục `extension/`.
2. Mở app, vào **Cài đặt** copy token → dán vào ô Token trong popup extension.
3. Click link/bấm Tải video trên trang → đẩy sang app.

## Đóng gói & ký số
Một lệnh publish self-contained + bundle FFmpeg/extension + (tùy chọn) ký số + zip:
```powershell
./publish.ps1                                   # tạo dist/ + DotDownloader-win-x64.zip (chưa ký)
./publish.ps1 -DevSelfSigned                    # ký bằng self-signed (dev; chỉ hợp lệ khi Smart App Control TẮT)
./publish.ps1 -Pfx C:\certs\dotdl.pfx -Password '***'   # ký bằng cert thật
```
`sign.ps1` chỉ ký **binary của DotDownloader** (`DM.App.exe/.dll`, `DM.Core.dll`, `DM.Server.dll`) — KHÔNG ký ffmpeg/.NET runtime/third-party. Cần `signtool` (Windows SDK).

> ⚠️ Windows 11 **Smart App Control** chỉ tin cert chain tới Microsoft + uy tín cloud → cần **EV code-signing cert** mới qua được; self-signed/ OV cert chưa đủ. Trên máy bật SAC, để chạy bản tự build phải tắt SAC hoặc dùng EV cert.

Cách khác: build installer 1-click bằng Inno Setup (`installer/DotDownloader.iss`). Xem `installer/README.md`.

## Trạng thái
Đã hoàn tất Phase 0–9 (xem `docs/TASKS.md`). Doc-first: đọc `docs/CLAUDE.md` trước mỗi phiên, làm theo `docs/TASKS.md` một task mỗi lần.
