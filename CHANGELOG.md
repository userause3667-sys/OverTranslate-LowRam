# CHANGELOG — OverTranslate Low-RAM Edition

Dựa trên OverTranslate **2.11.0** (tag `2.11.0` của https://github.com/Hon-Lu/OverTranslate).
Giấy phép MIT, tác giả và thông báo bản quyền của dự án gốc được giữ nguyên (xem `LICENSE`, `THIRD-PARTY-NOTICES.txt`).
Không chức năng gốc nào bị xóa hay viết lại; mọi thay đổi bên dưới là bổ sung hoặc "móc" (hook) nhỏ vào mã có sẵn.

## Thêm mới

### Game Profiles
- `Services/Performance/GameProfiles.cs` (đọc/khớp/áp dụng, thuần logic), `GameProfileStore.cs` (đọc `game-profiles.json` khi file đổi, tạo file mẫu), `ISourceActivity.SourceProcessName/SourceWindowTitle` (cài trong `WgcWindowCaptureBackend`). Khi một phiên realtime bắt đầu trên một cửa sổ, tên tiến trình + tiêu đề được dò trong `game-profiles.json`; profile khớp ghi đè `Mode`, `StaticScanMaxDelayMs`, `PauseWhenSourceNotForeground`, `LowerProcessPriority` và chồng từ điển riêng (`glossaries\<file>`) lên `glossary.txt`; hết phiên thì gỡ. Mục sai bị bỏ qua và báo trong bảng Hiệu năng.

### Giảm giật game: ưu tiên tiến trình
- `PerformanceSettings.LowerProcessPriority` (null = theo profile: bật ở Ultra Low/Balanced) → `PerformanceManager` đặt tiến trình ở **BelowNormal** để game thắng khi tranh CPU. Chỉ hạ từ Normal, không bao giờ nâng lên, và chỉ trả lại đúng cái đã hạ. Đánh đổi: OCR có thể chậm hơn khi game dùng hết CPU. Chưa đo.

### Installer
- `tools/lowram/installer/OverTranslate-LowRam.nsi` + tham số `-Installer` của `build-lowram.ps1` — tạo file cài đặt NSIS (per-user, không cần Administrator, có gỡ cài đặt, giữ dữ liệu `%AppData%\OverTranslate`), đóng gói trong ZIP. Workflow GitHub Actions chạy sẵn bước này. Chưa ký số.

### Chế độ Ultra Low Memory (3 profile)
- `Models/PerformanceSettings.cs` — nhóm cài đặt mới `Performance` trong `appsettings.json` (thiếu khóa → dùng mặc định, dữ liệu cũ không bị đụng).
- `Services/Performance/PerformanceProfile.cs` — 3 profile **Ultra Low / Balanced / Quality**. `Mode = Auto` (mặc định) tự chọn theo RAM cài đặt: dưới 6 GB → Ultra Low, dưới 12 GB → Balanced, còn lại → Quality; không đọc được RAM → Balanced. **Quality giữ nguyên các thông số của bản gốc** (nhả model 60 s, không giãn nhịp quét, cache phiên 400); các tính năng thêm vào (tạm dừng khi game thu nhỏ, cache bền, từ điển) vẫn bật và có công tắc riêng trong `Performance`.
- `Services/Performance/SystemMemory.cs` — đọc RAM bằng `GlobalMemoryStatusEx` (một lần).
- `Services/Performance/PerformanceManager.cs` — chọn profile lúc khởi động, ghi lý do vào log; dọn heap **một lần** sau khi nhả model OCR (tối đa 1 lần/phút, chỉ khi profile cho phép).

### OCR / realtime thích ứng
- Nhả model OCR sớm hơn khi rảnh: Ultra Low 20 s, Balanced 45 s, Quality 60 s (bản gốc: 60 s) — `OnnxOcrEngine.IdleReleaseDelay` giờ đọc từ profile.
- `Services/Performance/ScanBackoff.cs` — khi vùng dịch liên tục **không có gì để đọc** (khung không đổi, hoặc bộ lọc chữ loại khung), tự chờ thêm giữa các lần quét (Ultra Low: sau ~3 s tĩnh, tăng dần tới +450 ms). Có nội dung cần đọc → về nhịp thường ngay. Chỉnh được bằng `Performance.StaticScanMaxDelayMs` (0 = tắt, tối đa 2000).
- `Services/Performance/SourceInactivityTracker.cs` + `ISourceActivity` — **tạm dừng khi cửa sổ game bị thu nhỏ/đóng** (không chụp, không băm khung), nhả model OCR nếu kéo dài (Ultra Low 30 s), và bật lại khi game quay lại. Tuỳ chọn `PauseWhenSourceNotForeground` để dừng cả khi cửa sổ game không ở phía trước (mặc định tắt; cửa sổ của chính OverTranslate được tính là "ở phía trước"). Chỉ áp dụng khi dịch liên tục một cửa sổ cụ thể, không áp dụng khi quét cả màn hình.
- Cache dịch trong phiên: giới hạn theo profile (Ultra Low 300, còn lại 400 như gốc).

### Cache dịch bền (Smart Translation Cache)
- `Services/Performance/TranslationCacheStore.cs` + `TranslationAssist.cs` — cắm vào `TranslationService.TranslateAsync`, nên **mọi** đường dịch (chụp ảnh, liên tục, …) đều hưởng. Chuẩn hóa văn bản (NFKC, gộp khoảng trắng; giữ nguyên hoa/thường), khóa theo engine + ngôn ngữ nguồn/đích; giới hạn số mục (Ultra Low 1500 / Balanced 3000 / Quality 5000), hạn dùng (14 / 30 / 60 ngày), loại bỏ mục ít dùng nhất; lưu `%AppData%\OverTranslate\translation-cache.json`; tải lười khi cần, lưu hoãn 20 s sau thay đổi và khi thoát; xóa tay bằng nút trong bảng Hiệu năng. Không dùng khi ngôn ngữ nguồn là AUTO hoặc khi yêu cầu có ngữ cảnh màn hình (OpenAI-compatible) — giữ nguyên hành vi gốc ở các trường hợp đó.

### Từ điển game (Custom Game Dictionary)
- `Services/Performance/GameGlossary.cs` — file `%AppData%\OverTranslate\glossary.txt`: `Health = Máu`. Dòng chỉ gồm một thuật ngữ → trả lời ngay, **không gọi dịch vụ dịch**; thuật ngữ nằm trong câu dài → thay trước khi gửi engine (là gợi ý, không đảm bảo tuyệt đối). Hỗ trợ `[VI]`/`[JA]`… theo ngôn ngữ đích, `[*]` cho tất cả; bỏ qua hoa/thường; từ có khoảng trắng chỉ khớp nguyên từ. Đọc lại khi file đổi, không cần khởi động lại.

### Performance Monitor
- `Views/Shell/PerformanceMonitorWindow.cs` + mục **"Hiệu năng / Performance"** trong menu khay: Working Set, Private Bytes, Managed Heap, CPU %, thời gian OCR gần nhất, số lần nhận diện/giây, số yêu cầu dịch đang chờ, thời gian dịch, chế độ, cache. Chỉ lấy mẫu mỗi giây **khi cửa sổ đang mở**; đóng là dừng và giải phóng. Có nút "Xóa cache dịch" và "Mở từ điển game".
- `Services/Performance/PerfCounters.cs` — vài bộ đếm nguyên tử (một phép cộng mỗi lần), luôn bật, chi phí không đáng kể.

### Cấu hình runtime .NET (`OverTranslate.csproj`)
- `ConcurrentGarbageCollection=false`, `TieredPGO=false`, `System.GC.ConserveMemory=3`. Xem rủi ro/đánh đổi trong `PERFORMANCE_REPORT.md`.

### Công cụ
- `build-lowram.ps1` (build tự động → ZIP + SHA256), `.github/workflows/build-lowram.yml` (build bằng GitHub Actions, không cần cài gì), `tools/lowram/measure-performance.ps1` (đo Working Set / Private / CPU), `tools/lowram/ap-dung-cau-hinh-gc.ps1` + `.bat` (áp dụng cấu hình GC lên Portable hiện có, có khôi phục).
- Test: `src/OverTranslate.LowRamTests` (96 kiểm tra logic) và `src/OverTranslate.LowRamIntegration` (42 kiểm tra chạy mã `TranslationAssist` thật).

## Thay đổi trong mã có sẵn (đều nhỏ)
| Tệp | Thay đổi |
|---|---|
| `App.xaml.cs` | Gọi `PerformanceManager.Initialize()` lúc khởi động; `TranslationService.FlushCache()` khi thoát |
| `Models/AppSettings.cs` | Thêm thuộc tính `Performance` |
| `Services/Ocr/OnnxOcrEngine.cs` | `IdleReleaseDelay` đọc từ profile; dọn heap sau khi nhả model; đếm thời gian OCR |
| `Services/Realtime/RealtimeTranslationSession.cs` | Bỏ qua khi cửa sổ nguồn không hoạt động; `ScanBackoff` ở hai nhánh "không có gì để đọc"; giới hạn cache theo profile |
| `Services/Realtime/Capture/WgcWindowCaptureBackend.cs` | Cài `ISourceActivity` (3 P/Invoke user32) |
| `Services/TranslationService.cs` | `TranslateAsync` đi qua `TranslationAssist`; thêm `FlushCache()` |
| `Views/Shell/TrayMenuWindow.xaml(.cs)` | Thêm nút "Hiệu năng / Performance" |
| `OverTranslate.csproj` | Cấu hình GC như trên |

## Danh sách vấn đề phát hiện khi audit
Sửa trong bản này:
1. Realtime vẫn chụp + băm khung của **cửa sổ đã thu nhỏ** mỗi ~150 ms → nay tạm dừng (xem trên).
2. Vùng dịch liên tục không có chữ nhưng nền chuyển động vẫn quét theo nhịp cố định → nay giãn dần (`ScanBackoff`).
3. Model OCR giữ trong RAM 60 s sau lần dùng cuối bất kể máy chỉ có 4 GB → nay theo profile.
4. Không có cấu hình GC cho một ứng dụng tray nhỏ (GC nền bật mặc định) → nay tắt GC nền, tắt TieredPGO.
5. Cache dịch chỉ sống trong phiên và bị xóa sạch khi đầy → nay có cache bền, giới hạn, hết hạn.

Phát hiện nhưng **chưa sửa** (xem lý do trong `PERFORMANCE_REPORT.md`):
- RapidOcrNet 3.0.0 không cho cấu hình ONNX `SessionOptions` (mem arena, mem pattern, execution mode…); chỉ chỉnh được số luồng, và bản gốc đã tự chọn số luồng theo số nhân. Cần nâng cấp thư viện hoặc fork để đổi.
- Cache trong phiên khi đầy bị xóa toàn bộ (thiết kế có chủ ý của tác giả gốc); cache bền mới đã đỡ phần mất mát này.
- Giao diện chưa có tiếng Việt (có en, ja, ko, ru, zh-Hans, zh-Hant). Các nút mới của bản này ghi bằng tiếng Việt cố định.

## Chưa làm
- **Game Profiles** hiện là file JSON, chưa có giao diện chỉnh, chưa lưu vùng OCR/kiểu overlay theo game (chỉ chế độ hiệu năng, độ trễ quét, dừng khi không ở foreground, ưu tiên tiến trình, từ điển).
- Smart Overlay: bản gốc **đã có** độ mờ nền, cỡ chữ, màu và phím tắt tạm dừng (Ctrl+Alt+S) — không viết lại.
