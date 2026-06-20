# TASKS.md — DotDownloader

Danh sách task chi tiết cho AI agent làm **từng cái một, theo thứ tự**. Mỗi task nhỏ, kiểm chứng được. Tick `[x]` khi xong. Không nhảy task — mỗi phase phải build/test pass trước khi sang phase sau.

> Quy tắc cho agent: Làm đúng MỘT task mỗi lần. Sau khi code xong, build + chạy test liên quan, báo kết quả, rồi mới sang task kế. Nếu task mơ hồ → hỏi lại trước khi code.

---

## Phase 0 — Khởi tạo
- [x] T0.1 — Tạo `DotDownloader.sln` và 4 project: `DM.Core` (classlib net8.0), `DM.Server` (classlib net8.0), `DM.App` (WPF net8.0-windows), `DM.Core.Tests` (xUnit)
- [x] T0.2 — Thêm project reference: App→Core, App→Server, Server→Core, Tests→Core
- [x] T0.3 — Cài `CommunityToolkit.Mvvm` vào DM.App; `xunit` + `FluentAssertions` vào Tests
- [x] T0.4 — `MainWindow` hiển thị tiêu đề "DotDownloader", build & chạy được
- [x] T0.5 — Tạo `.gitignore` cho .NET, commit khởi tạo

## Phase 1 — Engine đơn luồng
- [x] T1.1 — `DM.Core/Models/DownloadState.cs`: enum (Queued, Connecting, Downloading, Paused, Completed, Failed, Canceled)
- [x] T1.2 — `DM.Core/Models/Segment.cs`: Start, End, Downloaded, CurrentPos, IsComplete
- [x] T1.3 — `DM.Core/Models/DownloadTask.cs`: Id, Url, FilePath, TotalBytes, SupportsRange, State, Segments, Category, ScheduledAt; computed DownloadedBytes
- [x] T1.4 — `DM.Core/Net/HttpProbe.cs`: HEAD request → trả `ProbeResult{ TotalBytes, SupportsRange, SuggestedFileName, ContentType }`. Xử lý server không trả Content-Length
- [x] T1.5 — `DM.Core/Models/ProgressReport.cs`: BytesDownloaded, TotalBytes, BytesPerSecond, Eta, State
- [x] T1.6 — `DM.Core/Download/SingleStreamDownloader.cs`: tải tuần tự bằng HttpClient stream, ghi FileStream, raise `IProgress<ProgressReport>`, hỗ trợ CancellationToken
- [x] T1.7 — Test T1: tải 1 file nhỏ thật (hoặc mock HttpMessageHandler), assert kích thước & nội dung đúng

## Phase 2 — Đa luồng
- [x] T2.1 — `DM.Core/Download/SegmentDownloader.cs`: tải 1 byte-range (header `Range: bytes=start-end`), ghi vào offset bằng `RandomAccess.Write`, cập nhật `Segment.Downloaded`, raise progress riêng
- [x] T2.2 — `DM.Core/Download/DownloadEngine.cs`: chia file thành N segment đều nhau (N cấu hình, mặc định 8), tạo file rỗng đúng size, chạy `Task.WhenAll` các SegmentDownloader
- [x] T2.3 — `DownloadEngine`: nếu `!SupportsRange` → fallback `SingleStreamDownloader`
- [x] T2.4 — `DM.Core/Util/SpeedCalculator.cs`: tính tốc độ tổng (sliding window) & ETA, tổng hợp progress từ các segment, throttle report
- [x] T2.5 — Test T2: tải file ~50–100MB đa luồng, so SHA256 với bản tải đơn luồng → phải khớp
- [x] T2.6 — Test T2: mock server trả 200 (không hỗ trợ range) → engine fallback đúng

## Phase 3 — Resume + Metadata
- [x] T3.1 — `DM.Core/Persistence/DownloadMetadata.cs`: DTO serialize task + segments (JSON)
- [x] T3.2 — `DM.Core/Persistence/MetadataStore.cs`: Save (atomic: ghi `.tmp` rồi `File.Move` ghi đè), Load, Delete file `.dmmeta`
- [x] T3.3 — `DownloadEngine`: gọi MetadataStore.Save định kỳ (mỗi 1s HOẶC mỗi 1MB, cái nào tới trước)
- [x] T3.4 — `DownloadEngine`: khi start mà có `.dmmeta` hợp lệ (URL & size khớp) → load, mỗi segment tải tiếp từ `CurrentPos`
- [x] T3.5 — Pause: cancel token, flush metadata, set state Paused. Resume: load lại & tải tiếp
- [x] T3.6 — `DM.Core/Download/RetryPolicy.cs`: retry segment lỗi, exponential backoff (1,2,4,8,16s), tối đa 5 lần
- [x] T3.7 — Khi hoàn tất: xóa `.dmmeta`, set Completed
- [x] T3.8 — Test T3: tải nửa chừng → kill (hủy token cứng) → load lại → resume → checksum khớp bản gốc
- [x] T3.9 — Test T3: metadata atomic — mô phỏng ghi dở không làm hỏng file cũ

## Phase 4 — Local Server
- [x] T4.1 — `DM.Server/Models/DownloadRequest.cs`: Url, FileName?, Referrer?, Cookies?, Headers?, Type?
- [x] T4.2 — `DM.Server/LocalServer.cs`: build ASP.NET Minimal API, bind `127.0.0.1`, auto-pick port (thử 51820, +1 nếu bận, tối đa 10), expose StartAsync/StopAsync
- [x] T4.3 — Endpoint `GET /api/ping` → `{ ok:true, version, activeDownloads, port }`
- [x] T4.4 — Endpoint `POST /api/download` → validate url, gọi callback enqueue, trả taskId
- [x] T4.5 — Middleware bảo mật (đặt trước mọi endpoint): reject non-loopback → 403; sai/thiếu `X-DM-Token` → 401
- [x] T4.6 — `DM.Server/Security/TokenProvider.cs`: env `DM_DEV_TOKEN` → config.json → tự sinh & lưu
- [x] T4.7 — Tích hợp: App khởi động → start LocalServer, callback nối vào `DownloadCoordinator`; OnExit dừng server
- [x] T4.8 — Test: tự động hóa bằng `LocalServerTests` (Kestrel thật) + lệnh curl trong báo cáo

## Phase 5 — Extension
- [x] T5.1 — `extension/manifest.json` MV3: permissions [downloads, contextMenus, storage, cookies, webRequest], host_permissions [http://127.0.0.1/*, <all_urls>], background service_worker, action popup
- [x] T5.2 — `extension/background.js`: hàm `pingApp()` fetch `/api/ping` timeout 1s, quét port 51820..51829 (Promise.any), cache 3s
- [x] T5.3 — `background.js`: listener `chrome.downloads.onDeterminingFilename` → check định dạng → app sống: `cancel(id)` + POST; app chết: `suggest()` cho browser tải
- [x] T5.4 — `background.js`: lấy cookie (`chrome.cookies.getAll`) + referrer, gắn vào payload
- [x] T5.5 — `background.js`: context menu "Download with DotDownloader" cho link & media
- [x] T5.6 — `background.js`: **phát hiện video** — `chrome.webRequest.onResponseStarted` lọc URL `.mp4/.webm/.m3u8/.mpd` & Content-Type media; gom theo tabId vào Map
- [x] T5.7 — `background.js`: lưu kèm request headers (Referer/Origin/Cookie/UA) qua `onBeforeSendHeaders`; set badge số lượng; dọn khi `tabs.onUpdated` (đổi url)/`tabs.onRemoved`
- [x] T5.8 — `extension/popup.html` + `popup.js`: toggle bật/tắt (lưu storage), trạng thái app, **danh sách video trên tab hiện tại (MP4/HLS/DASH) + nút Tải**
- [x] T5.9 — Khi click Tải video → POST sang app kèm `{url, type, headers}`
- [~] T5.10 — Test thủ công (checklist trong báo cáo): click link .zip → app nhận & tải
- [~] T5.11 — Test thủ công (checklist): mở trang có player HLS công khai → badge hiện số, popup liệt kê
- [~] T5.12 — Test thủ công (checklist): tắt app → click link → browser tải bình thường

## Phase 5.5 — Video Stream Engine (HLS/DASH) ⚠️
- [x] T5.5.1 — `DM.Core/Video/Ffmpeg.cs` wrapper gọi qua `Process`, resolve binary (explicit→env DM_FFMPEG→tools/ffmpeg→PATH). Bundle binary thật để Phase 8 (T8.5); `tools/ffmpeg/README.md` đã có.
- [x] T5.5.2 — `M3u8Parser`: phân biệt master/media, parse variant (resolution, bandwidth, uri tuyệt đối)
- [x] T5.5.3 — `M3u8Parser`: parse media → segment (uri, duration, media-sequence), `#EXT-X-KEY` (AES-128: method/uri/iv), `#EXT-X-MAP` (init fMP4)
- [x] T5.5.4 — `HlsDownloader`: tải segment song song (SemaphoreSlim) vào temp, progress "segment K/N", resume bỏ qua segment đã có
- [x] T5.5.5 — `HlsDownloader`: AES-128 key công khai → tải key (cache), giải mã AES-CBC PKCS7, IV từ #EXT-X-KEY hoặc media-sequence. Method khác AES-128 → ném (nghi DRM, ngoài scope)
- [x] T5.5.6 — `VideoHeaders` truyền Referer/Origin/Cookie/UA khi tải playlist/segment/key
- [x] T5.5.7 — `FfmpegMuxer`: nối byte segment → 1 file rồi remux `-c copy -movflags +faststart`; xóa temp
- [x] T5.5.8 — `MpdParser`: SegmentTemplate ($Number$ + @duration HOẶC SegmentTimeline), chọn video+audio bitrate cao nhất
- [x] T5.5.9 — `DashDownloader` + `FfmpegMuxer.MuxVideoAudioAsync` (map 0:v/1:a, -c copy)
- [x] T5.5.10 — `DownloadEngine` route `StreamType=HLS|DASH` → `VideoDownloadEngine`; còn lại → byte-range
- [x] T5.5.11 — Test e2e PASS: HLS Mux `https://test-streams.mux.dev/x36xhzz/x36xhzz.m3u8` → mp4 488MB
- [x] T5.5.12 — Test e2e PASS: HLS AES-128 `https://playertest.longtailvideo.com/adaptive/oceans_aes/oceans_aes.m3u8` → mp4 31MB. + test offline giải mã AES synthetic.
- [x] T5.5.13 — Test e2e PASS: DASH `https://dash.akamaized.net/akamai/bbb_30fps/bbb_30fps.mpd` → mp4 939MB (video+audio)

## Phase 6 — Queue & Scheduler
- [x] T6.1 — `DM.Core/Queue/DownloadQueue.cs`: giới hạn concurrent (mặc định 3, cấu hình), `SemaphoreSlim` điều phối; abstraction `IDownloadRunner` (+ `EngineDownloadRunner` adapter)
- [x] T6.2 — Task xong/lỗi → release slot → task Queued kế tiếp tự chạy (không block khi 1 task Failed)
- [x] T6.3 — `DM.Core/Queue/Scheduler.cs`: timer (mặc định 1 phút, cấu hình) quét `ScheduledAt <= now` → enqueue
- [x] T6.4 — Pause All / Resume All ở cấp queue (cancel token → Paused → resume tạo cts mới)
- [x] T6.5 — `AfterQueueAction` enum (None/Shutdown/Sleep) + `ISystemPowerController` (NoOp mặc định; không tắt máy thật trong test)
- [x] T6.6 — Test T6: enqueue 10 task (runner giả chậm) → max đồng thời = 3
- [x] T6.7 — Test T6: task ScheduledAt +5s → start quanh mốc đó (+ test Tick chỉ enqueue task đến giờ)

## Phase 7 — UI MVVM
- [x] T7.1 — `DM.App/ViewModels/DownloadItemViewModel.cs`: wrap DownloadTask, `[ObservableProperty]` %, speed, ETA, state; report gom đệm + `DispatcherTimer` 250ms đẩy UI
- [x] T7.2 — `DM.App/ViewModels/MainViewModel.cs`: ObservableCollection + `[RelayCommand]` Add/Pause/Resume/Cancel/Delete/PauseAll/ResumeAll; thêm Pause/Resume/Cancel/Remove per-item vào DownloadQueue
- [x] T7.3 — `MainWindow.xaml`: DataGrid tên/size/%(ProgressBar)/speed/ETA/state/category, toolbar nút lệnh
- [x] T7.4 — Sidebar category + filter trạng thái (ICollectionView.Filter, All/Downloading/Completed/Paused)
- [x] T7.5 — `Views/AddUrlDialog`: dán URL → Phân tích (probe) → hiện size & tên trước khi Tải
- [x] T7.6 — `Views/SettingsDialog`: số segment, concurrent, speed limit, port, thư mục lưu (lưu settings.json)
- [x] T7.7 — `DM.Core/Util/CategoryClassifier.cs`: map extension → category → thư mục đích
- [x] T7.8 — `DM.App/Services/TaskStore.cs` (tasks.json) load lúc mở app; task Downloading → Paused
- [~] T7.9 — Test thủ công end-to-end (checklist trong báo cáo, chờ user verify trên Windows GUI)

## Phase 8 — Hoàn thiện
- [x] T8.1 — `DM.Core/Util/RateLimiter.cs`: token bucket toàn cục, áp vào Single/Segment downloader qua `DownloadEngine(rateLimiter:)`
- [x] T8.2 — Tray icon (NotifyIcon) + minimize to tray + `StartupRegistry` (HKCU\Run) + checkbox trong Settings
- [x] T8.3 — Pairing copy-paste token: app hiện token (Settings, có Copy) — extension thêm ô nhập token lưu chrome.storage
- [x] T8.4 — Variant selector: `HlsDownloader.GetVariantsAsync` + tham số `selectVariant` (mặc định bitrate cao nhất)
- [x] T8.5 — Installer: `installer/DotDownloader.iss` (Inno Setup) + README (publish + bundle ffmpeg). Build ISCC thủ công.
- [x] T8.6 — Chạy checklist Definition of Done (xem báo cáo tổng kết) — phần tự động PASS; phần GUI/1GB cần verify thủ công.

## Phase 9 — Torrent (mở rộng ngoài SPEC v1)
- [x] T9.1 — Thêm `MonoTorrent` 3.0.2 vào DM.Core (tự viết BitTorrent là bất khả thi → dùng lib)
- [x] T9.2 — `DM.Core/Torrent/TorrentDownloader.cs`: add magnet / .torrent (file/URL http), progress (%, speed, ETA), dừng seed khi xong
- [x] T9.3 — `DM.Core/Queue/RoutingDownloadRunner.cs`: route `StreamType="Torrent"` → torrent, còn lại → HTTP runner; queue dùng chung
- [x] T9.4 — UI: `MainViewModel` nhận diện `magnet:`/`.torrent` → StreamType=Torrent, tên từ magnet `dn`; AddUrlDialog bỏ probe cho torrent; App dùng RoutingDownloadRunner
- [x] T9.5 — App: `TorrentDownloader(seedAfterComplete:false)`, dispose khi thoát (cổng/seed mặc định; tùy chọn nâng cao để sau)
- [x] T9.6 — Test: offline (routing, magnet parse) + e2e PASS tải torrent Ubuntu công khai (metadata 2.13GB, nhận 3.67MB từ peers thật)

---

## Trạng thái hiện tại
**Đang ở:** Phase 9 — XONG (Torrent qua MonoTorrent). Phase 0–8 + 9 hoàn tất. `dotnet test` offline (gồm 4 test torrent) xanh; e2e HLS/DASH + benchmark + torrent (DM_NET_TESTS=1) PASS.
**Torrent:** `TorrentDownloader` (magnet/.torrent/URL), `RoutingDownloadRunner` route theo `StreamType="Torrent"`; UI nhận magnet/.torrent, đặt tên từ `dn`. Mặc định KHÔNG seed sau khi xong; cổng MonoTorrent mặc định. Tùy chọn nâng cao (cổng/upload limit/seed) để sau.
**(các mục Phase 8 cũ):** `dotnet test` offline **79/79**; e2e HLS/DASH + benchmark (DM_NET_TESTS=1) PASS.
**Đã verify thật (live):** app chạy được (roll-forward 10.x), `/api/ping` 200, thiếu token 401, POST tải → taskId; tải HLS công khai → mp4 phát được (ffmpeg PATH); **DoD #1**: 100MB range, 1 luồng 464.7s vs 8 luồng 44.7s = **10.4× nhanh hơn**, checksum khớp. Extension đã bắt được endpoint `.php` (xasiat ~1GB, SupportsRange).
**Đóng gói:** `ffmpeg.exe` đã bundle vào `tools/ffmpeg/` (141MB); `dotnet publish` ra `publish/` (kèm ffmpeg). Còn lại: cài Inno Setup (ISCC) để build `setup.exe`.
**Còn cần người dùng:** click-through GUI (Phase 7), build installer bằng ISCC, đổi đuôi `.php`→`.mp4` cần extension reload + MIME video.
**Task kế tiếp:** — (hết task). Còn lại là verify GUI thủ công + build installer + test 1GB thật.
**Lưu ý Phase 8:**
- `RateLimiter` token bucket toàn cục; `DownloadEngine(rateLimiter:)` → Single/Segment downloader gọi `ThrottleAsync` mỗi chunk. Rate 0 = unlimited. App tạo từ `settings.SpeedLimitBytesPerSec` (đổi cần restart).
- Tray: `UseWindowsForms=true` + `System.Windows.Forms.NotifyIcon`; minimize → Hide + balloon; double-click/menu mở lại; menu Thoát shutdown. Icon dùng SystemIcons (chưa có icon riêng).
- Startup: `StartupRegistry` HKCU\...\Run; checkbox trong Settings.
- Pairing (chốt: copy-paste): Settings hiện token (TokenProvider, có nút Copy); popup extension thêm ô Token lưu chrome.storage.local. Khi không set env `DM_DEV_TOKEN`, TokenProvider sinh & lưu token ngẫu nhiên.
- Variant selector: `selectVariant` truyền tới HlsDownloader; UI chọn chất lượng có thể nối sau (hiện mặc định cao nhất).
- Installer: `installer/DotDownloader.iss` + README; cần ISCC + bundle ffmpeg.exe vào `tools/ffmpeg/` để chạy thật.
- WinForms+WPF chung assembly → phải fully-qualify `System.Windows.Application/MessageBox/Clipboard` (trùng tên với WinForms).
**Lưu ý Phase 7:**
- `DownloadItemViewModel`: report từ engine ghi vào đệm (SyncProgress, không đụng UI); `DispatcherTimer` 250ms trên UI thread đẩy %/speed/ETA/state → mượt, không lag khi tải nhanh. Video dùng segment K/N làm %.
- `MainViewModel`: lệnh per-item (Pause/Resume/Cancel/Delete) + PauseAll/ResumeAll; Resume task nạp từ tasks.json (chưa ở queue) → `queue.Add` lại (engine resume qua .dmmeta). Filter qua `ICollectionView`.
- `DownloadQueue` đã thêm `Pause/Resume/Cancel/Remove(task)` per-item (mỗi item có cờ CancelRequested phân biệt pause vs cancel).
- App: bỏ `StartupUri`, `OnStartup` dựng engine→`EngineDownloadRunner`→queue→VM→server, `OnExit` Persist + stop. `DownloadCoordinator.cs` cũ thành dead code (đã thay bằng MainViewModel.EnqueueFromRequest).
- **Speed limit** mới lưu cấu hình, CHƯA thực thi — token bucket ở T8.1. **Chưa chạy GUI** trong môi trường này (chỉ build); cần verify thủ công.
**Lưu ý Phase 6:**
- `DownloadQueue(IDownloadRunner runner, maxConcurrent=3, ISystemPowerController?)`. Mỗi task có CTS riêng; worker `await SemaphoreSlim.WaitAsync` rồi chạy runner. App dùng `EngineDownloadRunner` (wrap `DownloadEngine`).
- `Scheduler(queue, interval, now?)`: giữ task hẹn giờ riêng, `Tick()` (public, test gọi trực tiếp) enqueue task đến giờ. `now` injectable để test.
- After-queue action chỉ fire khi đã drain (không còn Queued/Downloading), có ≥1 Completed, và KHÔNG đang PauseAll.
**Lưu ý video (Phase 5.5):**
- Toàn bộ ở `DM.Core/Video/`: `Ffmpeg`, `M3u8Parser`+`HlsModels`, `HlsDownloader`, `FfmpegMuxer`, `MpdParser`+`DashModels`, `DashDownloader`, `VideoHeaders`, `VideoDownloadEngine`.
- **FFmpeg chưa bundle** — dev dùng fallback PATH (máy có `C:\ffmpeg\bin\ffmpeg.exe`). Phase 8 (T8.5) bundle vào `tools/ffmpeg/`.
- HLS/DASH ghép bằng nối-byte segment (TS/fMP4) → remux `-c copy` (không re-encode). DASH mux 2 track bằng `-map 0:v:0 -map 1:a:0`.
- `DownloadEngine` route theo `task.StreamType` (HLS/DASH). Headers video lấy từ `task.RequestHeaders`.
- Giới hạn đã biết: MPD chỉ hỗ trợ SegmentTemplate ($Number$ + duration/Timeline); CHƯA hỗ trợ SegmentList/SegmentBase/multi-period. Chọn chất lượng = bitrate cao nhất (variant selector để T8.4). TUYỆT ĐỐI không đụng DRM.
- E2E test gated bằng env `DM_NET_TESTS=1` (mặc định bỏ qua để suite offline xanh & không cần mạng).
**Phase 5 (extension):** XONG code (T5.1→T5.9); T5.10–T5.12 vẫn chờ user verify thủ công trên Chrome/Edge (checklist ở báo cáo Phase 5). Token dev `dev-token`, `pingApp` quét 51820..51829.
**Cách chạy curl thủ công:** đặt env `DM_DEV_TOKEN=dev-token` rồi mở `DM.App` (server nghe ở port in ra qua /api/ping). Lệnh:
- `curl -H "X-DM-Token: dev-token" http://127.0.0.1:51820/api/ping`
- `curl -X POST -H "X-DM-Token: dev-token" -H "Content-Type: application/json" -d "{\"url\":\"https://example.com/f.zip\"}" http://127.0.0.1:51820/api/download`
- thiếu token → 401; non-loopback → 403 (test bằng cách gọi từ máy khác tới IP LAN — sẽ bị từ chối, mà thực ra Kestrel chỉ bind 127.0.0.1 nên không reachable).
**Lưu ý kỹ thuật:**
- Phase 1: `Net/SharedHttpClient.cs` (HttpClient chung, timeout vô hạn) + `Net/ProbeResult.cs`. `HttpProbe`/`SingleStreamDownloader` nhận `HttpClient?` qua ctor để inject mock.
- Phase 2: ghi file dùng MỘT `SafeFileHandle` chung + `RandomAccess.WriteAsync(offset)` (positioned I/O) → an toàn khi nhiều segment ghi vùng byte rời nhau. Pre-allocate bằng `RandomAccess.SetLength`.
- Phase 3: `MetadataStore.SaveAsync` ghi `.dmmeta.tmp` → `File.Move(overwrite)` (atomic). Engine lưu mỗi 1s (SaverLoop) HOẶC 1MB (SemaphoreSlim signal trong OnBytes); khi pause/fail flush lần cuối (giữ meta), khi hoàn tất xóa meta. Resume mở handle `OpenOrCreate` (KHÔNG `Create`) để không truncate. Validate `MatchesSource` (URL+size); không khớp → xóa meta + cảnh báo qua `ProgressReport.Warning` + tải lại. `RetryPolicy` backoff 2^n (1/2/4/8/16s), tối đa 5 retry, KHÔNG retry OperationCanceledException; segment retry tự resume từ `CurrentPos`.
- `DownloadEngine` ctor nhận thêm `MetadataStore? store, RetryPolicy? retry` (inject `RetryPolicy.NoDelay()` khi test để khỏi chờ backoff thật).
- **Fix:** `SpeedCalculator.ShouldReport` từng tràn `TimeSpan` (khởi tạo `MinValue`) → đổi sang cờ `_hasReported`.
- Test: `FrameworkReference Microsoft.AspNetCore.App` + `RollForward=Major` (máy chỉ có shared framework 10.x) → tự host Kestrel `RangeTestServer` (`/range`, `/norange`, `/slow` throttle 30ms để test hủy/resume). `InternalsVisibleTo` cho DM.Core.Tests.
- **Môi trường:** SDK .NET 10.0.103, shared framework chỉ 10.x (không có 8.0) → test dựa roll-forward.
