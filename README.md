# A38.ImageCrop

Console app C# dùng OpenCvSharp4 để dò vùng và cắt ảnh, có sẵn công cụ debug xem ảnh từng bước.

> 📐 **[docs/TU_DUY_MACHINE_VISION.md](docs/TU_DUY_MACHINE_VISION.md)** — phân tích hiện trạng pipeline,
> phản biện hướng xoay ảnh bằng ApproxPolyDP, và tư duy machine vision cổ điển cho bài toán
> phát hiện sứt mẻ 1-2mm. Đọc trước khi sửa thuật toán.

## Chạy

```powershell
cd A38.ImageCrop
dotnet run                              # chưa có ảnh -> tự tạo sample.png để thử
dotnet run -- "D:\anh\cccd.jpg"         # chạy trên ảnh của bạn
```

Mỗi bước sẽ bật một cửa sổ ảnh. Bấm phím bất kỳ để đi tiếp, **Esc / q** để tắt debug và chạy thẳng đến hết.

### Các cờ

| Cờ | Tác dụng |
|---|---|
| `--no-pause` | Không dừng chờ phím, cửa sổ nháy qua nhanh |
| `--no-window` | Không bật cửa sổ, chỉ in số liệu + ghi ảnh ra `debug_out/` |
| `--no-debug` | Tắt sạch debug, chạy như chương trình thật |
| `--only canny` | Chỉ debug bước có tên chứa "canny", các bước khác bỏ qua |

Ví dụ chỉ muốn soi bước threshold mà không phải bấm phím 6 lần:

```powershell
dotnet run -- anh.jpg --only canny
```

## Kết quả

- `output/<tên ảnh>_crop.png` — ảnh đã cắt
- `debug_out/01_original.png`, `02_gray.png`, ... — ảnh từng bước, xem lại bằng File Explorer

## Sửa thuật toán

Toàn bộ pipeline nằm trong `CropLargestRegion()` ở `Program.cs`, viết tuần tự từ trên xuống:

```
original -> gray -> blur -> canny -> morph_close -> findContours -> approxPolyDP -> warp/crop
```

Tham số nằm ở class `Config` ngay đầu file. Sửa số rồi `dotnet run` lại.

## Đặt debug ở chỗ mới

Ở bất kỳ đâu trong code, gọi:

```csharp
Dbg.Show(mat, "tên bước");        // hiện ảnh + in số liệu, dừng chờ phím
Dbg.Stats(mat, "tên");            // chỉ in số liệu, không hiện ảnh
Dbg.ShowPair(truoc, sau, "tên");  // 2 ảnh cạnh nhau để so sánh
Dbg.Log($"biến x = {x}");         // in text
Dbg.Values(mat, new Rect(100, 100, 10, 10), "vùng nghi ngờ");  // in thẳng giá trị pixel
```

Tất cả tự thành no-op khi `Dbg.Enabled = false`, nên **không cần xoá đi khi chạy thật**.

## Đọc số liệu để chỉnh tham số

Dòng stats in ra kiểu:

```
[canny] 1000x750 CV_8UC1 ch=1 | min=0 max=255 mean=1.16 | nonzero=3413 (0.5%)
```

`nonzero%` ở bước canny/threshold là chỉ số hữu ích nhất:

- **< 0.5%** — ngưỡng cao quá, cạnh biến mất → giảm `Config.CannyLow`
- **> 15%** — ngưỡng thấp quá, toàn nhiễu → tăng `Config.CannyLow`, hoặc tăng `Config.BlurKernel`

Không tìm được contour nào thì giảm `Config.MinAreaRatio`.
Contour bắt đúng vùng nhưng `approxPolyDP` không ra 4 đỉnh thì tăng `Config.ApproxEpsRatio` (0.02 → 0.03).

```
Phần có thể cái tiến thêm
chưa cần NCC (TM_CCOEFF_NORMED), distance transform, gradient/Sobel, kim tự tháp, ICP, gradient descent
Đó là lý do tồn tại của mọi kỹ thuật tăng tốc mà bạn sẽ gặp: kim tự tháp (quét thô trước để khỏi phải quét tinh khắp nơi), tích chập qua FFT, ảnh tích phân.
```
