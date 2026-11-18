# PERFORMANCE_REPORT — OverTranslate Low-RAM Edition

**Kết luận trung thực trước tiên:** báo cáo này **không có số đo RAM/CPU nào của Windows**, cả cho bản gốc lẫn bản mới. Môi trường phát triển là một máy Linux đóng, không có Windows, không có GPU/DirectML, không chạy được ứng dụng WPF, và không có nguồn NuGet — nên không thể khởi chạy OverTranslate, càng không thể đo trên máy 4 GB. Các ô benchmark bên dưới để trống có chủ đích; `measure-performance.ps1` và bảng Hiệu năng trong ứng dụng có sẵn để bạn điền bằng số thật. Mọi mức giảm RAM/CPU nêu ở đây là **cơ chế dự kiến**, chưa phải kết quả đã chứng minh.

## 1. Đã kiểm tra thật những gì

| Hạng mục | Kết quả | Cách kiểm tra |
|---|---|---|
| Mã nguồn gốc 2.11.0 biên dịch được | ✅ 0 lỗi | Biên dịch toàn bộ mã nguồn thật (không sửa) với các DLL của bản Portable làm tham chiếu và stub thay cho phần XAML sinh tự động |
| Mã đã sửa biên dịch được | ✅ 0 lỗi | Cùng khung kiểm tra trên (trình biên dịch bắt được vài chỗ thiếu `using`, đã sửa) |
| Logic thuần: profile theo RAM, backoff, theo dõi cửa sổ, cache, từ điển | ✅ 96/96 | `src/OverTranslate.LowRamTests` chạy trên .NET 8 |
| Luồng `TranslationAssist` (cache + từ điển + hợp nhất kết quả, giữ thứ tự, giữ văn bản gốc, trả lời ngắn) | ✅ 42/42 | `src/OverTranslate.LowRamIntegration` chạy **mã thật** của `TranslationAssist`, với bản rút gọn của vài kiểu WPF |
| Dịch XAML (nút menu khay mới) | ⚠️ chưa | Chỉ sao chép đúng mẫu của nút Settings bên cạnh; chưa được nhìn thấy trên màn hình |
| Chạy ứng dụng, overlay, dịch liên tục, game, rò rỉ bộ nhớ nhiều giờ | ❌ chưa | Không có Windows trong môi trường phát triển |
| Bộ test gốc (`OverTranslate.Tests`, xUnit) | ❌ chưa chạy | Cần NuGet (xUnit); đã kiểm tra rằng không test nào tham chiếu cái tôi đổi (`IdleReleaseDelay`, hằng cache phiên) |
| Script PowerShell (`build-lowram.ps1`, `measure-performance.ps1`, `ap-dung-cau-hinh-gc.ps1`) và workflow GitHub | ❌ chưa chạy | Không có PowerShell; phần JSON của script áp dụng cấu hình đã được mô phỏng bằng Python |

## 2. Dữ kiện tĩnh đọc được từ bản Portable (2.11.0, .NET 8.0.31 tự chứa)
- Kích thước thư mục: 241 MB, 274 tệp trong `current\`. Lớn nhất: `Microsoft.Windows.SDK.NET.dll` 26 MB, `ocrmodels\` 23 MB (mô hình OCR), `onnxruntime.dll` 17 MB, `PresentationFramework.dll` 16 MB, `System.Windows.Forms.dll` 13 MB. Kích thước trên đĩa **không** bằng RAM (DLL được map lười).
- `OverTranslate.runtimeconfig.json` gốc **không có thiết lập GC nào** (GC nền bật mặc định).
- Gói ONNX Runtime là bản **DirectML**; `DirectML.dll` không đi kèm Portable (chỉ tải cùng mô hình manga), nên OCR thường chạy CPU.
- RapidOcrNet **3.0.0** (không có API `RapidOcrSessionOptions`; chỉ truyền được số luồng). Số luồng do `OcrThreadBudget` chọn theo số nhân (tối thiểu 2, tối đa 4) và số lượt suy luận đồng thời cũng bị giới hạn theo số nhân.

## 3. Bản gốc đã có sẵn gì (không viết lại)
Đã kiểm tra trong mã, nên **không** trùng lặp:
- Chọn vùng người dùng quét; thăm dò theo `PeriodicTimer` 150 ms (`RealtimeRegionState.PollInterval`).
- Phát hiện thay đổi bằng dấu vân khung (`FrameFingerprint`), bỏ OCR khi khung không đổi; bộ lọc `RealtimeGate` phát hiện chữ ở 1/3 kích thước trước khi đọc đầy đủ; quét rảnh mỗi ~2 s.
- Chống dịch trùng trong phiên (`RealtimeTranslationCache`, `RealtimeInFlightTranslations`), tối đa 4 yêu cầu dịch đồng thời trong một phiên.
- Một phiên bản model OCR dùng lại; nhả sau 1 phút rảnh; giữ ấm trong phiên realtime; cổng `SemaphoreSlim` giới hạn suy luận.
- Overlay có độ mờ nền, cỡ chữ, màu; phím tắt tạm dừng realtime (Ctrl+Alt+S).

## 4. Khoảng trống bản này lấp, và cái giá phải trả

| Thay đổi | Cơ chế kỳ vọng | Đánh đổi / rủi ro |
|---|---|---|
| Nhả model OCR sau 20/45/60 s theo profile | Model nằm trong RAM ngắn hơn khi rảnh | Lần dịch đầu sau khi rảnh >20 s (Ultra Low) phải nạp lại model: chậm hơn một nhịp. Trong phiên realtime model vẫn được giữ ấm (như gốc) trừ khi game bị thu nhỏ lâu |
| Dọn heap 1 lần (có nén LOH) sau mỗi lần nhả model | Trả lại phần managed (bitmap, buffer) và chống phân mảnh | Một lần GC chặn (blocking) trên heap vừa được dọn; giới hạn tối đa 1 lần/phút, chỉ khi profile bật. **Chưa đo độ dài khựng**; trên máy chơi game đây là điểm nên theo dõi |
| Tạm dừng khi cửa sổ game thu nhỏ/đóng | Không chụp, không băm khung, nhả model nếu >30 s | Chỉ áp dụng cho chế độ theo cửa sổ. Overlay vẫn hiển thị bản dịch cuối |
| `ScanBackoff` (Ultra Low: sau ~20 lần quét không có gì, chờ thêm +75 ms mỗi lần tới +450 ms) | Giảm số lần chụp/băm/dò chữ khi vùng tĩnh hoặc không có chữ | Chữ mới xuất hiện có thể hiện chậm hơn tối đa ~450 ms (Ultra Low) so với nhịp thường; chỉnh được bằng `StaticScanMaxDelayMs` |
| Cache dịch bền + từ điển | Ít yêu cầu mạng và ít CPU/chờ khi câu lặp lại | Cache giữ vài trăm KB RAM khi đã nạp (≤1500 mục ở Ultra Low); thêm 1 lệnh `stat` file từ điển mỗi yêu cầu dịch. Thay thế thuật ngữ trong câu dài là gợi ý cho engine, có thể cho kết quả lạ |
| `LowerProcessPriority` (BelowNormal ở Ultra Low/Balanced) | Game thắng khi tranh CPU → ít giật game hơn trên máy 2 nhân/4 luồng | OCR và dựng overlay có thể chậm hơn khi game dùng hết CPU. **Chưa đo** cả hai chiều. Tắt bằng `"LowerProcessPriority": false` |
| Game Profiles (`game-profiles.json`) | Mỗi game dùng cấu hình/từ điển hợp với nó thay vì một cấu hình chung | Thêm 1 lệnh `stat` file và dò tên tiến trình lúc bắt đầu phiên; từ điển riêng làm khóa cache khác đi nên cache cũ của game đó không dùng lại |
| `ConcurrentGarbageCollection=false` | Bỏ luồng GC nền và bookkeeping kèm theo | GC gen2 phải chặn thay vì chạy nền; với heap nhỏ của ứng dụng này ảnh hưởng nhỏ nhưng chưa đo |
| `TieredPGO=false` | Bỏ instrumentation ở tầng JIT đầu | Mã nóng có thể đạt tốc độ đỉnh chậm hơn chút |
| `System.GC.ConserveMemory=3` | Nén gen cũ chủ động hơn khi phân mảnh | Có thể tăng thời gian gen2; muốn bỏ đặt về 0 |

**Không làm:** không `GC.Collect()` theo vòng/hẹn giờ, không giới hạn heap cứng, không nâng ưu tiên tiến trình (chỉ hạ xuống BelowNormal, tắt được), không đụng Registry/Windows Defender/pagefile, không thêm dịch vụ nền hay model mới, không xóa chức năng gốc.

## 5. Vì sao chưa đụng ONNX Runtime (tuy đề bài yêu cầu thử)
`IntraOpNumThreads=1`, `InterOpNumThreads=1`, `ExecutionMode=ORT_SEQUENTIAL`… cần truyền `SessionOptions` cho RapidOcrNet. Bản 3.0.0 đang dùng không có API đó (chỉ có tham số số luồng), và việc nâng cấp thư viện/fork cần NuGet và kiểm thử OCR thật — không làm được ở đây. Điều chắc chắn: bản gốc đã dùng lại một session cho mỗi model, đã chọn số luồng theo số nhân và đã giới hạn suy luận đồng thời; chạy ORT_SEQUENTIAL là mặc định của ONNX Runtime. Việc đặt số luồng = 1 trên i3-4130 (2 nhân/4 luồng) có thể giảm CPU nhưng làm mỗi lần OCR chậm gần gấp đôi — **phải đo** trước khi quyết định; nên làm ở bước sau khi bạn có số liệu từ máy thật.

## 6. Bảng benchmark — để bạn điền (cùng máy, cùng kịch bản)

| Kịch bản | Chỉ số | Bản gốc 2.11.0 | Low-RAM |
|---|---|---|---|
| 1. Khởi động xong (30 s sau khi mở) | Working Set / Private (MB) | CHƯA ĐO | CHƯA ĐO |
| 2. Chờ ở khay 5 phút | Working Set / Private (MB), CPU % | CHƯA ĐO | CHƯA ĐO |
| 3. OCR một vùng (1 lần) | Đỉnh Working Set; thời gian OCR (ms) | CHƯA ĐO | CHƯA ĐO |
| 4. Dịch liên tục 10 phút (không game) | Working Set / Private, CPU %, OCR/giây | CHƯA ĐO | CHƯA ĐO |
| 5. Dịch liên tục khi chơi game | FPS game (trước/sau), CPU %, RAM | CHƯA ĐO | CHƯA ĐO |
| 6. Thu nhỏ game 2 phút | CPU % trong lúc thu nhỏ; RAM sau 60 s | CHƯA ĐO | CHƯA ĐO |
| 7. Mở lại game | Thời gian từ lúc mở lại đến khi có bản dịch | CHƯA ĐO | CHƯA ĐO |
| 8. Chạy 2–3 giờ | Working Set theo thời gian (xu hướng tăng?) | CHƯA ĐO | CHƯA ĐO |
| Độ trễ dịch | Bảng Hiệu năng: "Dịch gần nhất" | CHƯA ĐO | CHƯA ĐO |

Cách đo: `tools\lowram\measure-performance.ps1` (Working Set, Private Bytes, CPU từ bên ngoài) + bảng **Hiệu năng** trong ứng dụng (Managed Heap, OCR ms, OCR/giây, yêu cầu dịch chờ — bản gốc không có bảng này, nên với bản gốc chỉ có số liệu từ script). Cần đo cả **chất lượng OCR/bản dịch** với cùng bộ ảnh/đoạn thoại: bản này không đổi mô hình hay tham số OCR, nên chất lượng nhận diện kỳ vọng giữ nguyên, nhưng cache/từ điển có thể thay đổi nội dung bản dịch của câu cụ thể (đó là mục đích của chúng).

## 7. Tiêu chuẩn hoàn thành đối chiếu

| Tiêu chuẩn | Trạng thái |
|---|---|
| Chức năng gốc vẫn hoạt động | Chưa kiểm chứng chạy thật; mã gốc được giữ nguyên, thay đổi đều là hook nhỏ, mặc định Quality giữ thông số gốc |
| Chọn vùng / dịch liên tục / overlay không lỗi | Chưa kiểm chứng chạy thật |
| Game Profiles | Đã làm dạng file JSON (không có giao diện chỉnh); logic đọc/khớp/áp dụng có test (96 test logic, 42 test tích hợp qua); lấy tiến trình/tiêu đề cửa sổ chưa chạy thử trên Windows |
| Không rò rỉ bộ nhớ dài hạn | Chưa kiểm chứng (cần chạy nhiều giờ). Các cấu trúc mới đều có giới hạn (cache theo số mục, timer chỉ khi có thay đổi, cửa sổ Hiệu năng dừng khi đóng) |
| Không tác vụ OCR/dịch chạy vô hạn | Không thêm tác vụ nền mới; mọi vòng chờ mới đều nhận `CancellationToken` của vòng gốc |
| Không có lỗi build | Biên dịch sạch trong khung kiểm tra ngoại tuyến; build Windows thật (`build-lowram.ps1`) chưa chạy |
| Portable chạy độc lập | Chưa có bản build; cần chạy `build-lowram.ps1` hoặc workflow GitHub |
| Báo cáo hiệu năng trung thực | Đây |
