# PROMPT — Tạo SKILL.md cho dự án DotDownloader

Copy prompt dưới đây đưa cho Claude Code (hoặc skill-creator) để sinh ra `SKILL.md` cho dự án.

---

## Prompt

```
Tạo một file SKILL.md cho dự án "DotDownloader" — một download manager clone IDM
(desktop .NET 8 + WPF, extension Chromium MV3, giao tiếp qua local HTTP server).

Đọc trước 3 file để lấy ngữ cảnh: docs/SPEC.md, docs/PLAN.md, docs/TASKS.md.

SKILL.md phải tuân theo format chuẩn:
1. Frontmatter YAML có `name` và `description` (description viết kỹ, nhiều trigger
   tiếng Việt lẫn Anh, để skill tự kích hoạt đúng lúc — ví dụ: "khi làm việc với
   dự án DotDownloader", "thêm tính năng download", "sửa engine tải đa luồng",
   "viết video downloader HLS", "làm extension bắt video", v.v.)
2. Thân skill mô tả:
   - Kiến trúc 4 project (DM.Core / DM.Server / DM.App / DM.Core.Tests) và ranh giới
     phụ thuộc (Core không dính WPF)
   - Quy tắc làm việc: MỘT task/lần theo TASKS.md, build+test trước khi sang task kế,
     checkpoint "DỪNG và báo cáo" giữa các phase, hỏi lại khi task mơ hồ
   - Convention code C#: nullable enabled, file-scoped namespace, required field,
     async mọi IO, không block UI thread
   - Nguyên tắc cứng: metadata ghi atomic (tmp→rename), local server chỉ loopback+token,
     file lớn phải stream không load vào RAM
   - Video: bundle FFmpeg gọi qua Process, ghép -c copy, truyền headers tránh 403,
     phát hiện video bằng chrome.webRequest, KHÔNG đụng DRM, YouTube để v2
   - Giá trị mặc định: 8 segment, 3 concurrent, port 51820, retry 5 lần backoff 1/2/4/8/16s
   - Ranh giới pháp lý: chỉ nội dung công khai/hợp pháp, không bypass DRM/crack
3. Có mục "Khi nào KHÔNG dùng skill này" để tránh kích hoạt nhầm.

Viết bằng tiếng Việt. Đặt file ở .claude/skills/dotdownloader/SKILL.md
Ngắn gọn, súc tích, đúng trọng tâm — không lặp lại nguyên văn SPEC, chỉ chắt lọc
những gì agent cần nhớ mỗi phiên.
```

---

## Ghi chú
- Skill này là "trí nhớ làm việc" cho mọi phiên code dự án — khác với SPEC/PLAN/TASKS
  (tài liệu đặc tả). Skill chắt lọc *cách làm*, doc chứa *làm gì*.
- Sau khi agent sinh xong, review lại phần `description` — đây là phần quyết định
  skill có tự kích hoạt đúng lúc không.
