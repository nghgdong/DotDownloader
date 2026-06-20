# PLAN.md — DotDownloader

Kế hoạch triển khai theo **phase tăng dần**. Mỗi phase tự đứng được, chạy & test được trước khi sang phase sau. Triết lý: dựng vòng end-to-end nhỏ nhất chạy được trước, rồi đắp tính năng.

---

## Phase 0 — Khởi tạo solution
**Mục tiêu:** Bộ khung project biên dịch được, chạy "hello".

- Tạo solution `DotDownloader.sln`
- 4 project: `DM.Core` (classlib), `DM.Server` (classlib), `DM.App` (WPF), `DM.Core.Tests` (xUnit)
- Reference: App → Core, App → Server, Server → Core, Tests → Core
- WPF mở lên một cửa sổ trống có tiêu đề
- Cài package: `CommunityToolkit.Mvvm`

**Done khi:** `dotnet build` pass, app mở cửa sổ.

---

## Phase 1 — Download engine đơn luồng
**Mục tiêu:** Tải được 1 file, ghi ra đĩa, không đa luồng, chưa resume.

- `DownloadTask`, `Segment`, `DownloadState` models
- `HttpProbe`: gửi HEAD, lấy `Content-Length`, `Accept-Ranges`, tên file gợi ý
- `SingleStreamDownloader`: tải tuần tự, ghi file, raise progress event
- Console test nhỏ (hoặc unit test với file thật nhỏ) để verify

**Done khi:** Tải 1 file qua URL, file trên đĩa đúng kích thước & nội dung.

---

## Phase 2 — Đa luồng (multi-segment)
**Mục tiêu:** Tải song song N segment khi server hỗ trợ range.

- `SegmentDownloader`: tải 1 byte-range, ghi vào offset đúng bằng `RandomAccess.Write`
- `DownloadEngine`: chia file thành N segment, chạy `Task.WhenAll`, tổng hợp progress
- Xử lý server không hỗ trợ range → fallback Phase 1
- Tính tốc độ tổng & ETA
- Test: so sánh checksum file đa luồng vs đơn luồng (phải khớp)

**Done khi:** File 100MB tải 8 luồng, checksum khớp bản gốc, nhanh hơn 1 luồng.

---

## Phase 3 — Resume + Metadata bền vững
**Mục tiêu:** Tắt ngang → mở lại → tải tiếp.

- `DownloadMetadata`: serialize task + tiến độ từng segment ra `.dmmeta`
- Ghi metadata atomic (write `.tmp` → `File.Move` ghi đè)
- Lưu định kỳ (1s hoặc mỗi 1MB)
- Khi start task có `.dmmeta` → đọc lại, mỗi segment tải tiếp từ `CurrentPos`
- Pause = hủy cancellation token + flush metadata
- Retry segment lỗi với exponential backoff (tối đa 5 lần)

**Done khi:** Kill app giữa chừng, mở lại, resume xong, checksum khớp.

---

## Phase 4 — Local Server
**Mục tiêu:** Có HTTP API loopback để extension gọi vào.

- `LocalServer`: ASP.NET Minimal API host trong app, bind `127.0.0.1`, auto-pick port nếu bận
- `GET /api/ping` → trả version + status
- `POST /api/download` → nhận payload, tạo task, enqueue
- Middleware: chặn non-loopback, kiểm tra header token (shared secret sinh lúc cài, lưu file config + đẩy cho extension qua... tạm thời hardcode dev, sẽ làm pairing sau)
- Test bằng curl/Postman

**Done khi:** `curl POST /api/download` tạo được task trong app.

---

## Phase 5 — Extension (Manifest V3)
**Mục tiêu:** Chrome bắt link file thường VÀ phát hiện video đang phát → đẩy sang app.

- `manifest.json`: permissions `downloads`, `contextMenus`, `storage`, `cookies`, `webRequest`, host `127.0.0.1` + `<all_urls>` (để nghe request video)
- `background.js` (service worker):
  - `chrome.downloads.onDeterminingFilename` → bắt file thường (như cũ)
  - `chrome.webRequest.onBeforeSendHeaders` / `onResponseStarted` → phát hiện media: lọc URL `.mp4/.webm/.m3u8/.mpd` & Content-Type `video/*`, `application/vnd.apple.mpegurl`, `application/dash+xml`
  - Gom video theo tabId, lưu kèm headers (Referer/Origin/Cookie/UA), set badge số lượng
  - Dọn danh sách khi tab đổi URL / đóng
  - ping app; file thường: cancel + POST như cũ
  - context menu "Download with DotDownloader"
- `popup.html`: toggle bật/tắt, trạng thái app, **danh sách video phát hiện trên tab + nút Tải từng video**
- Lấy cookie + referrer kèm theo mọi request

**Done khi:** Mở trang có video HLS (vd trang tin có player .m3u8) → badge hiện số, popup liệt kê, click Tải → app nhận.

---

## Phase 5.5 — Video Stream Engine (HLS/DASH) ⚠️ rủi ro cao
**Mục tiêu:** Tải được video HLS/DASH thành 1 file mp4.

- Bundle FFmpeg binary vào project (`tools/ffmpeg/`), `DM.Core` gọi qua `Process`
- `DM.Core/Video/M3u8Parser.cs`: parse master playlist (liệt kê variant theo độ phân giải/bitrate) + media playlist (danh sách segment, key AES-128 nếu có)
- `DM.Core/Video/HlsDownloader.cs`: tải toàn bộ segment (tái dùng đa luồng Phase 2), resume theo "segment thứ K", giải mã AES-128 nếu playlist có key công khai
- `DM.Core/Video/FfmpegMuxer.cs`: ghép segment → mp4 bằng `-c copy` (không re-encode)
- `DM.Core/Video/MpdParser.cs` + DASH downloader: tải track video+audio, mux bằng FFmpeg
- Truyền headers (Referer/Origin/Cookie/UA) khi tải segment — thiếu là 403
- Test: tải một HLS công khai (vd test stream của Apple/Mux) → ra mp4 phát được

**Done khi:** URL .m3u8 công khai → app tải & ghép ra mp4 hoàn chỉnh, phát được.

> ⚠️ Đây là phase rủi ro nhất. Mỗi CDN một kiểu. Test với nhiều nguồn HLS công khai. KHÔNG đụng tới stream DRM (Widevine/FairPlay) — ngoài scope & bất hợp pháp.
**Mục tiêu:** Quản lý nhiều tải, giới hạn đồng thời, hẹn giờ.

- `DownloadQueue`: giới hạn concurrent (mặc định 3), task thừa = Queued
- Sự kiện task xong → dequeue task kế
- `Scheduler`: timer kiểm tra task có `ScheduledAt`, đến giờ thì enqueue
- Hành động sau khi xong queue (none/shutdown/sleep)

**Done khi:** Thêm 10 task, chỉ 3 chạy cùng lúc, task hẹn giờ start đúng giờ.

---

## Phase 7 — UI hoàn chỉnh (MVVM)
**Mục tiêu:** Giao diện dùng được như IDM.

- `MainViewModel`, `DownloadItemViewModel` (binding progress, throttle 250ms)
- DataGrid: tên, size, %, speed, ETA, state, category
- Toolbar: add URL, pause, resume, cancel, delete, pause/resume all
- Sidebar category + filter trạng thái
- Dialog thêm URL, dialog settings
- Phân loại tự động theo extension → map category → thư mục lưu

**Done khi:** Toàn bộ thao tác làm được qua UI, progress realtime mượt.

---

## Phase 8 — Hoàn thiện & đóng gói
- Speed limit toàn cục (token bucket)
- Chọn chất lượng video trước khi tải (variant selector từ master playlist)
- Tray icon, khởi động cùng Windows (tùy chọn)
- Cài đặt persistence (`config.json`)
- Installer (MSIX hoặc Inno Setup), pairing token app ↔ extension tự động
- Test toàn diện theo Definition of Done trong SPEC

---

## Thứ tự ưu tiên rủi ro
Phần khó & rủi ro nhất nằm ở **Phase 2–3** (đa luồng + resume bền vững), **Phase 5** (extension bắt link + phát hiện video), và **Phase 5.5** (tải & ghép HLS/DASH — mỗi CDN một kiểu, dễ vỡ nhất). Làm kỹ, test kỹ với nhiều nguồn thật trước khi đầu tư vào UI.
