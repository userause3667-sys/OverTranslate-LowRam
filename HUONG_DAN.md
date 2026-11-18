# OverTranslate Low-RAM Edition — Hướng dẫn cài đặt và sử dụng

Mục tiêu thiết kế: máy **Core i3-4130, 4 GB RAM, Windows 10/11, chạy bằng CPU**. Mọi chức năng gốc (dịch chụp màn hình, dịch liên tục, tra nhanh, dịch tại chỗ, dịch văn bản, overlay) được giữ nguyên.

> Đọc `PERFORMANCE_REPORT.md` trước khi kỳ vọng con số: bản này **chưa được đo trên máy 4 GB thật**, báo cáo ghi rõ cái gì đã kiểm tra và cái gì chưa.

## 1. Có 2 cách dùng

### Cách A — Nhanh nhất, chỉ cần double-click (chỉ áp dụng cấu hình bộ nhớ .NET)
Dành cho bản Portable bạn đang có. **Không có tính năng mới**, chỉ đổi 3 thiết lập GC (xem mục 5).
1. Thoát OverTranslate (menu khay hệ thống → Exit).
2. Chép `ap-dung-cau-hinh-gc.ps1`, `ap-dung-cau-hinh.bat`, `khoi-phuc-cau-hinh.bat` (thư mục `tools\lowram`) vào thư mục Portable, cạnh `OverTranslate.exe`.
3. Double-click `ap-dung-cau-hinh.bat`. Script không sửa EXE/DLL, chỉ sửa `current\OverTranslate.runtimeconfig.json` và lưu bản gốc cạnh đó.
4. Muốn quay lại: double-click `khoi-phuc-cau-hinh.bat`.
Lưu ý: nếu ứng dụng tự cập nhật qua Velopack, thư mục `current` được thay mới và cần chạy lại bước 3.

### Cách B — Bản Low-RAM đầy đủ (có mọi tính năng mới)
Cần build từ mã nguồn (môi trường phát triển của tôi không build được ứng dụng WPF Windows). Hai đường, chọn một:
- **GitHub Actions (không cần cài gì):** đưa mã nguồn này lên một repository GitHub của bạn → tab **Actions** → **Build Low-RAM Portable** → **Run workflow** → khi xong tải ở mục **Artifacts** một gói chứa 2 file ZIP: `OverTranslate-LowRam-Installer-<phiên bản>-win-x64.zip` (bên trong là file cài đặt `.exe`) và bản Portable.
- **Trên máy Windows:** cài .NET 8 SDK (https://dotnet.microsoft.com/download/dotnet/8.0), mở PowerShell trong thư mục mã nguồn, chạy `.\build-lowram.ps1`. Kết quả: `dist\OverTranslate-LowRam-2.11.0-win-x64.zip` (+ file `.sha256`). Script tự chạy test và dừng nếu test lỗi. Thêm tham số `-Installer` (cần NSIS: `choco install nsis`) để tạo luôn file cài đặt, đã đóng trong ZIP.

**Dùng bản cài đặt (Installer):** giải nén ZIP → chạy `OverTranslate-LowRam-Installer-...exe` → Next → Install. Không cần quyền Administrator; cài vào `%LocalAppData%\Programs\OverTranslate-LowRam`, tạo shortcut Start Menu + Desktop, có mục gỡ cài đặt trong *Apps & features*. Gỡ cài đặt **không** xóa dữ liệu của bạn ở `%AppData%\OverTranslate`. Installer chưa ký số nên Windows SmartScreen có thể báo "Windows protected your PC" → *More info* → *Run anyway*. Installer không đổi Registry hệ thống, Defender hay pagefile (chỉ 2 khóa dưới HKCU để hiện trong Apps & features).

Dùng bản Portable: giải nén ZIP ở bất kỳ đâu → chạy `OverTranslate.exe`. Không cần cài .NET. Dữ liệu người dùng vẫn ở `%AppData%\OverTranslate` (dùng chung với bản gốc).

## 2. Chế độ bộ nhớ
Trong `%AppData%\OverTranslate\appsettings.json` (sửa khi ứng dụng đã thoát), khóa `Performance`:

| Khóa | Mặc định | Ý nghĩa |
|---|---|---|
| `Mode` | `"Auto"` | `Auto` tự chọn theo RAM: dưới 6 GB → **Ultra Low**, dưới 12 GB → **Balanced**, còn lại → **Quality**. Có thể ép `"UltraLow"`, `"Balanced"`, `"Quality"`. |
| `PauseWhenSourceInactive` | `true` | Tạm dừng quét khi cửa sổ game đang dịch liên tục bị thu nhỏ/đóng; nhả model OCR nếu kéo dài; tự bật lại khi game quay lại. |
| `PauseWhenSourceNotForeground` | `false` | Dừng cả khi cửa sổ game không ở phía trước (ví dụ bạn chuyển sang trình duyệt). Cửa sổ của OverTranslate không bị tính là "rời game". |
| `StaticScanMaxDelayMs` | `null` | Thời gian chờ thêm tối đa giữa các lần quét khi vùng dịch tĩnh. `null` = theo profile (Ultra Low 450 ms), `0` = tắt, tối đa 2000. Số lớn hơn = tiết kiệm CPU hơn nhưng chữ mới hiện chậm hơn một chút. |
| `LowerProcessPriority` | `null` | Chạy OverTranslate ở mức ưu tiên CPU **Below Normal** để game được ưu tiên khi tranh CPU (giảm giật game, đổi lại OCR có thể chậm hơn lúc game ngốn hết CPU). `null` = theo profile (bật ở Ultra Low/Balanced, tắt ở Quality); `true`/`false` để ép. Chỉ hạ từ Normal xuống, không bao giờ nâng lên High/Realtime, và chỉ trả lại đúng cái mình đã hạ. |
| `UseTranslationCache` | `true` | Cache dịch bền (mục 3). |
| `UseGameGlossary` | `true` | Từ điển game (mục 4). |

Máy 4 GB sẽ tự vào **Ultra Low**; xem profile đang dùng trong bảng Hiệu năng (mục 6) hoặc trong log (`%AppData%\OverTranslate\logs`, dòng "Performance profile ...").

## 3. Cache dịch
Câu đã dịch được nhớ lại (giữa các lần chạy), nên câu lặp lại (menu, HUD, hội thoại lặp) không gửi lên dịch vụ dịch nữa. Có giới hạn số mục và hạn dùng. Xóa tay: bảng Hiệu năng → **Xóa cache dịch**. Không dùng cache khi ngôn ngữ nguồn đặt là AUTO.

## 4. Từ điển game
1. Mở bảng Hiệu năng → **Mở từ điển game** (tự tạo `%AppData%\OverTranslate\glossary.txt` có ví dụ).
2. Mỗi dòng một mục: `Health = Máu`, `Inventory = Túi đồ`, `Quest = Nhiệm vụ`, `Respawn = Hồi sinh`; tên nhân vật/địa danh/vật phẩm viết tương tự. Dòng bắt đầu bằng `#` là ghi chú.
3. `[VI]` ở một dòng riêng: các mục sau chỉ áp dụng khi dịch **sang tiếng Việt** (`[JA]`, `[EN]`… tương tự); `[*]` áp dụng mọi ngôn ngữ.
4. Lưu file, dịch tiếp — có hiệu lực ngay, không cần khởi động lại.
Cách hoạt động: dòng chỉ có đúng một thuật ngữ được trả lời thẳng từ từ điển (không gọi dịch vụ dịch). Thuật ngữ nằm trong câu dài được thay trước khi gửi đi dịch; đây là gợi ý cho engine, không đảm bảo tuyệt đối. Không phân biệt hoa/thường; từ tiếng Latin/Cyrillic chỉ khớp nguyên từ ("Quest" không khớp trong "Request").

## 5. Game Profiles — cấu hình riêng cho từng game
1. Mở bảng Hiệu năng → **Mở Game Profiles** (tự tạo `%AppData%\OverTranslate\game-profiles.json` kèm một ví dụ đang tắt).
2. Mỗi game một mục, khớp theo **tên file .exe** của cửa sổ đang dịch (không phân biệt hoa/thường, có hay không `.exe` đều được), có thể kèm `titleContains` (một đoạn của tiêu đề cửa sổ):

```json
{ "profiles": [
  { "name": "Game của tôi", "enabled": true,
    "process": "TenGame.exe",
    "mode": "UltraLow",
    "staticScanMaxDelayMs": 600,
    "pauseWhenNotForeground": true,
    "lowerPriority": true,
    "glossary": "game-cua-toi.txt" }
] }
```
3. Mục nào bỏ trống thì giữ nguyên cài đặt chung của bạn. `glossary` là **tên file** trong thư mục `%AppData%\OverTranslate\glossaries\`; mục trong đó được chồng lên `glossary.txt` chung (cùng một từ thì của game thắng), nên cùng từ có thể dịch khác nhau ở mỗi game.
4. Chỉ áp dụng khi dịch **một cửa sổ** (không áp dụng khi dịch cả màn hình, vì lúc đó không biết game nào). Áp dụng khi bấm bắt đầu dịch và gỡ khi kết thúc phiên; sửa file xong hãy dừng rồi bắt đầu lại phiên. Dòng "Game profile" trong bảng Hiệu năng cho biết profile nào đang dùng, kèm cảnh báo nếu file có mục sai (mục sai bị bỏ qua, các mục còn lại vẫn chạy). Profile khớp đầu tiên được dùng. File cho phép ghi chú `//` và dấu phẩy thừa.

## 6. Bảng Hiệu năng
Menu khay hệ thống → **Hiệu năng / Performance**. Hiện: profile, game profile đang dùng, mức ưu tiên tiến trình, RAM máy, Working Set, Private Bytes, Managed Heap, CPU, thời gian OCR gần nhất, số lần nhận diện/giây, yêu cầu dịch đang chờ, thời gian dịch gần nhất, thống kê cache. Chỉ cập nhật khi cửa sổ đang mở; đóng lại là dừng hẳn.

## 7. Tự đo trước/sau
`tools\lowram\measure-performance.ps1` (có sẵn trong ZIP): chạy `.\measure-performance.ps1 -Label goc-cho -Seconds 120` với bản gốc, rồi `-Label lowram-cho` với bản Low-RAM, làm **cùng một thao tác** trong lúc đo; so sánh hai bản tóm tắt. Các kịch bản gợi ý: khởi động; chờ ở khay; OCR một vùng; dịch liên tục; dịch liên tục khi chơi game; thu nhỏ game 2 phút rồi mở lại; chạy 2–3 giờ để xem RAM có tăng dần không.

## 8. Giới hạn cần biết
- Chưa đo trên máy 4 GB thật; con số RAM cụ thể phải do bạn đo (mục 7).
- **Game Profiles** hiện chỉ cấu hình bằng file JSON (chưa có giao diện), chỉ áp dụng khi dịch một cửa sổ, và **chưa chạy thử trên Windows thật**: phần đọc/khớp/áp dụng đã có test logic, phần lấy tên tiến trình và tiêu đề cửa sổ (Win32) mới chỉ biên dịch được.
- Hạ ưu tiên tiến trình là cách chống giật hợp lý nhưng **chưa đo**; nếu thấy overlay cập nhật chậm khi game chạy nặng, đặt `LowerProcessPriority` = `false`.
- Giao diện vẫn chưa có tiếng Việt (ngôn ngữ gốc: en, ja, ko, ru, zh-Hans, zh-Hant).
- Mô hình OCR (PP-OCR) và ONNX Runtime nhiều khả năng là phần chiếm RAM lớn nhất khi đang nhận diện (chưa đo riêng); bản này giảm **thời gian chúng nằm trong RAM** và **tần suất chạy**, không làm chúng nhỏ đi.
