# tools/ffmpeg

Nơi đặt **FFmpeg binary bundle** đi kèm app (KHÔNG phụ thuộc FFmpeg cài sẵn của máy).

- Windows: đặt `ffmpeg.exe` vào thư mục này.
- `DM.Core/Video/Ffmpeg.cs` resolve theo thứ tự: đường dẫn truyền vào → biến môi trường
  `DM_FFMPEG` → `tools/ffmpeg/ffmpeg(.exe)` (copy cạnh app khi build/đóng gói) → PATH (fallback dev).

Việc bundle binary thật vào repo/installer là bước **đóng gói ở Phase 8 (T8.5)**.
Trong lúc phát triển, code dùng fallback PATH nếu chưa có binary ở đây.
