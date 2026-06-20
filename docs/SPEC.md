# SPEC.md — DotDownloader (IDM Clone)

## 1. Tổng quan

**Sản phẩm:** Trình quản lý & tăng tốc tải file (download manager) cho Windows, gồm một **desktop app (.NET 8 + WPF)** và một **browser extension (Chromium / Manifest V3)** giao tiếp với nhau qua local HTTP server.

**Mục tiêu:** Tái hiện các tính năng cốt lõi của IDM — tải đa luồng, resume, bắt link từ trình duyệt, hàng đợi, lịch tải, phân loại file.

**Codename:** DotDownloader

## 2. Phạm vi (Scope)

### Trong phạm vi (v1)
- Tải đa luồng (multi-segment, byte-range parallel)
- Tạm dừng / tiếp tục (resume) sau khi đóng app hoặc mất mạng
- Bắt link download từ Chrome/Edge qua extension
- **Bắt video đang phát trên trang web (TÍNH NĂNG CHÍNH):**
  - File trực tiếp: `.mp4`, `.webm`, `.mkv` (thẻ video / link trực tiếp)
  - HLS streaming: `.m3u8` (phổ biến nhất) → tải segment → ghép thành mp4
  - DASH streaming: `.mpd` → tải video+audio track → mux thành mp4
  - Hiện badge số video phát hiện trên tab + popup liệt kê để chọn tải
- Hàng đợi tải (queue) với giới hạn số tải đồng thời
- Lập lịch tải (scheduler) — tải vào giờ định trước
- Phân loại tự động theo phần mở rộng (Video, Document, Music, Compressed, Program, Other)
- Giới hạn tốc độ tải (speed limit) toàn cục
- UI: danh sách tải, tiến độ realtime, tốc độ, thời gian còn lại (ETA)
- Bundle FFmpeg kèm app (dùng để ghép/mux HLS & DASH)

### Ngoài phạm vi (v1)
- macOS / Linux (kiến trúc tách Core để mở rộng sau)
- Native Messaging (v1 dùng HTTP localhost; nâng cấp sau)
- Tải torrent / magnet
- **Stream có DRM (Widevine / PlayReady / FairPlay)** — Netflix, Spotify, các nền tảng trả phí.
  KHÔNG hỗ trợ vì phá DRM là bất hợp pháp ở hầu hết các nước.
- **YouTube** — cần xử lý signature/adaptive streams thay đổi liên tục; để v2, nếu làm sẽ wrap `yt-dlp` chứ không tự viết.
- Tài khoản đám mây / sync

## 3. Người dùng & use case

| Use case | Mô tả |
|---|---|
| Bắt link tự động | Người dùng click link tải trên trình duyệt → extension chặn → đẩy sang app |
| Thêm link thủ công | Dán URL vào app → app tự phân tích và tải |
| Tải nhiều file | App quản lý hàng đợi, tải N file đồng thời, phần còn lại chờ |
| Tiếp tục sau ngắt | Tắt app/mất mạng → mở lại → tải tiếp từ chỗ dừng |
| Hẹn giờ | Đặt tải lúc 2h sáng để không nghẽn băng thông ban ngày |

## 4. Yêu cầu chức năng

### 4.1 Download Engine (lõi)
- FR-1: `HEAD` request lấy `Content-Length`, kiểm tra `Accept-Ranges: bytes`
- FR-2: Nếu hỗ trợ range → chia thành N segment (mặc định 8, cấu hình được), tải song song
- FR-3: Nếu KHÔNG hỗ trợ range → tải đơn luồng, không resume được (báo cho user)
- FR-4: Ghi mỗi segment vào đúng offset của file đích (dùng `RandomAccess.Write`)
- FR-5: Lưu metadata tiến độ (`.dmmeta`) định kỳ (mỗi 1s hoặc mỗi 1MB)
- FR-6: Resume = đọc `.dmmeta`, tính phần còn thiếu mỗi segment, tải tiếp
- FR-7: Retry tự động khi segment lỗi (exponential backoff, tối đa 5 lần)
- FR-8: Báo cáo tiến độ qua event/callback: bytes, speed, ETA, state per segment

### 4.2 Local Server (cầu nối extension ↔ app)
- FR-9: ASP.NET Minimal API chạy ở `127.0.0.1:<port>` (mặc định 51820, fallback nếu bận)
- FR-10: Endpoint `POST /api/download` nhận `{url, filename, referrer, cookies, headers}`
- FR-11: Endpoint `GET /api/ping` để extension kiểm tra app có chạy không
- FR-12: Bảo mật: chỉ chấp nhận request từ `127.0.0.1`, kiểm tra token shared-secret trong header
- FR-13: Chỉ bind loopback, không expose ra mạng ngoài

### 4.3 Extension (Manifest V3)
- FR-14: Dùng `chrome.downloads.onDeterminingFilename` để chặn download trước khi browser tự tải
- FR-15: Nếu app đang chạy (ping OK) → hủy download của browser, đẩy URL + cookie + referrer sang app
- FR-16: Nếu app không chạy → để browser tải bình thường (graceful fallback)
- FR-17: Popup: bật/tắt extension, danh sách định dạng cần bắt, nút "tải file này bằng app"
- FR-18: Context menu "Download with DotDownloader" trên link và media
- FR-19: Bắt video đang phát qua `chrome.webRequest`:
  - Nghe request mạng mọi tab, lọc URL/Content-Type là media (.mp4/.webm/.m3u8/.mpd hoặc video/*)
  - Gom phát hiện theo từng tab; hiện badge số lượng video trên icon extension
  - Lưu kèm headers cần thiết (Referer, Origin, Cookie, User-Agent) — nhiều CDN bắt buộc
- FR-20: Popup hiển thị danh sách video phát hiện trên tab hiện tại: tên/độ phân giải (nếu có), loại (MP4/HLS/DASH), nút "Tải"
- FR-21: Khi user chọn video HLS/DASH → đẩy sang app kèm loại stream + headers; app tự xử lý tải & ghép

### 4.5 Video Stream Engine (Core)
- FR-22: HLS — parse master playlist `.m3u8`, liệt kê các variant (độ phân giải/bitrate), cho chọn chất lượng; parse media playlist lấy danh sách segment `.ts`/`.m4s`
- FR-23: HLS — tải toàn bộ segment (tận dụng đa luồng), có resume ở mức "đã tải tới segment thứ K"
- FR-24: HLS — ghép segment thành 1 file mp4 bằng FFmpeg (`-c copy`, không re-encode để giữ chất lượng & nhanh)
- FR-25: DASH — parse `.mpd`, tải track video + audio riêng, mux thành mp4 bằng FFmpeg
- FR-26: Hỗ trợ HLS có khóa AES-128 hợp pháp (key công khai trong playlist, KHÔNG phải DRM Widevine) — tải key, giải mã segment khi ghép
- FR-27: Bundle FFmpeg binary kèm app; `DM.Core` gọi qua process, không phụ thuộc FFmpeg cài sẵn của máy
- FR-28: Truyền đúng headers (Referer/Origin/Cookie/UA) khi tải segment — nếu thiếu nhiều CDN trả 403

### 4.4 Queue & Scheduler
- FR-20: Giới hạn số download đồng thời (mặc định 3), phần còn lại trạng thái Queued
- FR-21: Khi một download xong → tự lấy task tiếp theo trong queue
- FR-22: Scheduler: task có `ScheduledAt` → chỉ start khi đến giờ
- FR-23: Hành động sau khi tải xong toàn bộ queue: tùy chọn (none / shutdown / sleep)

### 4.5 UI (WPF, MVVM)
- FR-24: DataGrid danh sách tải: tên, kích thước, tiến độ %, tốc độ, ETA, trạng thái, category
- FR-25: Toolbar: thêm URL, pause, resume, cancel, xóa, pause all, resume all
- FR-26: Sidebar phân loại theo category, lọc theo trạng thái
- FR-27: Cập nhật progress realtime (binding, throttle ~250ms để không lag UI)
- FR-28: Cài đặt: số segment, số tải đồng thời, thư mục lưu theo category, speed limit, port

## 5. Yêu cầu phi chức năng
- NFR-1: Tải đa luồng phải nhanh hơn đáng kể tải đơn luồng trên server hỗ trợ range
- NFR-2: App không crash khi mất mạng giữa chừng — phải tự retry và giữ tiến độ
- NFR-3: Metadata không được hỏng nếu app bị kill đột ngột (ghi atomic: write temp → rename)
- NFR-4: UI không đơ khi tải file lớn / nhiều file (mọi IO chạy async, không block UI thread)
- NFR-5: Bảo mật: local server không được trở thành lỗ hổng (token + loopback only)
- NFR-6: Bộ nhớ ổn định khi tải file nhiều GB (stream, không load cả file vào RAM)

## 6. Stack kỹ thuật
| Thành phần | Công nghệ |
|---|---|
| Core engine | .NET 8, `HttpClient`, `RandomAccess`, `System.Text.Json` |
| Local server | ASP.NET Core Minimal API (hosted trong app) |
| UI | WPF + CommunityToolkit.Mvvm |
| Extension | Manifest V3, vanilla JS (hoặc TS build sang JS) |
| Lưu trữ trạng thái | JSON file (`.dmmeta` per task + `tasks.json` tổng) |
| Test | xUnit cho Core |

## 7. Tiêu chí hoàn thành (Definition of Done)
- [ ] Tải được file 1GB+ với 8 luồng, nhanh hơn 1 luồng
- [ ] Kill app giữa chừng → mở lại → resume thành công, file không hỏng (checksum khớp)
- [ ] Click link trong Chrome → app tự nhận và tải
- [ ] Queue giới hạn 3 tải đồng thời hoạt động đúng
- [ ] Lập lịch tải vào giờ định trước chạy đúng
- [ ] File tự vào đúng thư mục category
