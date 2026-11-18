# Hướng dẫn khôi phục phiên bản ban đầu

Dữ liệu của bạn nằm ở `%AppData%\OverTranslate` (cài đặt, log, cache, từ điển), **tách biệt** với thư mục chương trình. Bản Low-RAM chỉ **thêm** khóa `Performance` vào `appsettings.json`; bản gốc bỏ qua khóa lạ nên cài đặt cũ vẫn dùng được.

## A. Bạn chỉ áp dụng cấu hình GC lên Portable hiện có
1. Thoát OverTranslate (menu khay → Exit).
2. Chạy `khoi-phuc-cau-hinh.bat` (đặt cạnh `OverTranslate.exe`). Script chép lại `OverTranslate.runtimeconfig.json.original` về chỗ cũ.
3. Hoặc làm tay: vào `current\`, xóa `OverTranslate.runtimeconfig.json`, đổi tên `OverTranslate.runtimeconfig.json.original` thành `OverTranslate.runtimeconfig.json`.

## B. Bạn đã dùng bản build Low-RAM đầy đủ
1. Thoát OverTranslate.
2. Xóa thư mục bản Low-RAM; giải nén lại bản Portable gốc (4 file RAR bạn có) hoặc cài bản chính thức từ https://github.com/Hon-Lu/OverTranslate/releases.
3. Dữ liệu cũ giữ nguyên. Nếu muốn sạch hoàn toàn các thứ Low-RAM đã tạo, xóa trong `%AppData%\OverTranslate`: `translation-cache.json`, `glossary.txt` (và khóa `Performance` trong `appsettings.json` nếu muốn).

## C. Tắt từng tính năng mà không gỡ bản Low-RAM
Sửa `%AppData%\OverTranslate\appsettings.json` (khi ứng dụng đã thoát), trong khóa `Performance`:
- `"Mode": "Quality"` — thông số giống bản gốc (nhả model 60 s, không giãn nhịp quét, cache phiên 400).
- `"PauseWhenSourceInactive": false` — không tạm dừng khi game thu nhỏ.
- `"StaticScanMaxDelayMs": 0` — không giãn nhịp quét khi màn hình tĩnh.
- `"UseTranslationCache": false`, `"UseGameGlossary": false` — tắt cache bền / từ điển.
