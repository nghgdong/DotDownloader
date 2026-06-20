# CLAUDE.md — DotDownloader

Context bền vững cho AI agent (Claude Code). Đọc file này TRƯỚC mọi phiên làm việc.

## Dự án là gì
DotDownloader — clone IDM cho Windows. Gồm **desktop app (.NET 8 + WPF)** và **Chromium extension (MV3)**, giao tiếp qua local HTTP server loopback. Tính năng: tải đa luồng, resume, bắt link từ trình duyệt, hàng đợi, lịch tải, phân loại.

## Tài liệu nguồn (đọc theo thứ tự)
1. `docs/SPEC.md` — đặc tả chức năng, scope, Definition of Done
2. `docs/PLAN.md` — kế hoạch theo phase
3. `docs/TASKS.md` — task chi tiết, **nguồn sự thật về tiến độ**

## Quy tắc làm việc (QUAN TRỌNG)
- Làm đúng **MỘT task** trong TASKS.md mỗi lần, theo thứ tự, không nhảy cóc.
- Trước khi code task mới: đọc lại TASKS.md mục "Trạng thái hiện tại".
- Sau khi xong một task: `dotnet build` + chạy test liên quan, báo kết quả, tick `[x]`, cập nhật "Trạng thái hiện tại" & "Task kế tiếp".
- Mỗi phase phải build + test pass mới sang phase sau.
- Task mơ hồ hoặc thiếu thông tin → **hỏi lại trước khi code**, đừng đoán.
- Commit nhỏ, message rõ theo task id (vd: `feat(core): T2.2 multi-segment engine`).

## Kiến trúc & nguyên tắc
- `DM.Core` KHÔNG phụ thuộc WPF/UI — giữ thuần logic để tái dùng (CLI/test/đổi UI sau).
- Mọi IO async, không block. UI thread không được chặn.
- File lớn: stream, KHÔNG load cả file vào RAM.
- Ghi metadata atomic: write `.tmp` → `File.Move` ghi đè. Tránh hỏng khi app bị kill.
- Local server: CHỈ loopback `127.0.0.1` + kiểm tra token. Đây là ranh giới bảo mật, không nới lỏng.
- Resume dựa trên `.dmmeta` cạnh file đang tải; segment tải tiếp từ `CurrentPos`.

## Cấu trúc thư mục
```
DotDownloader.sln
DM.Core/        # engine, models, persistence, queue — không UI
DM.Server/      # ASP.NET Minimal API loopback
DM.App/         # WPF MVVM (CommunityToolkit.Mvvm)
DM.Core.Tests/  # xUnit + FluentAssertions
extension/      # Manifest V3
docs/           # SPEC, PLAN, TASKS, CLAUDE
```

## Convention code
- C# nullable enabled, file-scoped namespace, `required` cho field bắt buộc.
- Đặt tên rõ nghĩa, ưu tiên đọc hiểu hơn ngắn gọn.
- Test: đặt theo `<Class>Tests`, dùng FluentAssertions (`result.Should().Be(...)`).
- Không thêm package ngoài nếu chưa cần; nêu lý do khi thêm.

## Giá trị mặc định
- Số segment: 8 (cấu hình được)
- Số tải đồng thời: 3
- Port local server: 51820 (fallback +1 nếu bận)
- Retry segment: tối đa 5 lần, backoff 1/2/4/8/16s
- Lưu metadata: mỗi 1s hoặc mỗi 1MB

## Lưu ý pháp lý / phạm vi
Chỉ tải nội dung công khai / hợp pháp. KHÔNG làm: bypass DRM, crack license, tải nội dung bảo vệ bản quyền trái phép.
- Bắt video chỉ áp dụng stream công khai KHÔNG DRM (HLS/DASH thường, kể cả AES-128 key công khai trong playlist).
- TUYỆT ĐỐI không xử lý Widevine/PlayReady/FairPlay (Netflix, Spotify...) — phá DRM bất hợp pháp, ngoài scope.
- YouTube để v2; nếu làm thì wrap `yt-dlp`, không tự viết signature extractor.

## Video stream (HLS/DASH)
- Bundle FFmpeg trong `tools/ffmpeg/`, gọi qua `Process` — KHÔNG phụ thuộc FFmpeg cài sẵn của máy.
- Ghép HLS/DASH dùng `-c copy` (không re-encode) để giữ chất lượng & tốc độ.
- Phải truyền headers Referer/Origin/Cookie/User-Agent khi tải segment — thiếu là CDN trả 403.
- Phát hiện video ở extension dùng `chrome.webRequest` (KHÔNG phải `downloads` API — stream không qua download API).

## Trạng thái
Xem mục "Trạng thái hiện tại" cuối `docs/TASKS.md`.
