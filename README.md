# hoc_patModel — tự dựng một CogPMAlign

Branch này chỉ có một mục đích: **học dò mẫu theo hình dạng và tự viết ra một tool
kiểu `CogPMAlign` của Cognex** — đưa vào ảnh, trả về toạ độ `(x, y)` và góc `θ` của
con hàng. Mọi thứ không phục vụ mục đích đó đã bị gỡ khỏi branch.

> 📐 **[docs/TU_DUY_MACHINE_VISION.md](docs/TU_DUY_MACHINE_VISION.md)** — tư duy machine
> vision cổ điển, đọc trước khi sửa thuật toán.

## Bốn file còn lại

| File | Vai trò |
|---|---|
| `Program.cs` | Chỉ là bảng điều phối: đọc cờ rồi gọi sang đúng nhánh. Không có pipeline nào ở đây. |
| `HocDoMau.cs` | Bài học 4 bước, làm dò mẫu bằng tay từ số 0 để hiểu bản chất. |
| `PatModel.cs` | Trích model thật: kim tự tháp nhiều mức, điểm biên + hướng gradient, có vùng don't-care. |
| `YeaJoungCheckCoiNghieng.cs` | Dùng model đó dò cả bộ ảnh và trả về góc α — đây là cái gần `CogPMAlign` nhất hiện có. |
| `Dbg.cs` | Xem ảnh / in số liệu từng bước. Tự thành no-op khi `Dbg.Enabled = false`. |

## Chạy

**Không nhánh nào nhận đường dẫn ảnh từ dòng lệnh** (trừ `--model`). Đường dẫn, vùng
khoanh, ngưỡng đều nằm trong class `*Cfg` đầu mỗi file — sửa code rồi `dotnet run` lại.

```powershell
dotnet run -- --nghieng                              # do goc alpha ca bo OK/NG  -> nghieng_out\
dotnet run -- --hoc 1 --no-pause                     # bai hoc buoc 1..4         -> hoc_out\
dotnet run -- --model --no-window --no-pause         # trich model tu ModelCfg.AnhMaster -> model_out\
dotnet run -- --model "D:\anh\master.bmp" --no-window --no-pause
dotnet run -- --model "D:\anh\ca_thu_muc"  --no-window --no-pause
```

Ba cờ nhánh là short-circuit ở đầu `Main`, thứ tự ưu tiên `--model` → `--hoc` → `--nghieng`.
Truyền hai cờ cùng lúc thì cái đứng trước ăn, cái sau bị bỏ im lặng.

### Cờ debug dùng chung

| Cờ | Tác dụng |
|---|---|
| `--no-pause` | Không dừng chờ phím |
| `--no-window` | Không bật cửa sổ, chỉ ghi ảnh ra đĩa |
| `--no-debug` | Tắt sạch debug |
| `--only <tên>` | Chỉ debug bước có tên chứa chuỗi này |

`--nghieng` không gọi `Dbg` lần nào nên không cần cờ debug. `--model` gọi `Dbg.Show`
với `pause: true` cho từng mức trong 7 mức, nên gần như luôn muốn `--no-window --no-pause`.

## Chỗ cần chỉnh của từng nhánh

**`--nghieng`** — `YeaJoungCheckCoiNghieng.cs`, class `NghiengCfg`:

- `AnhMaster` + `VungTu` — **luôn sửa cùng nhau**. `VungTu` là toạ độ tuyệt đối trên
  đúng ảnh master đó; đổi master mà quên đổi Rect thì model trích ra rác nhưng chương
  trình vẫn chạy êm và trả α bậy. Mở `nghieng_out\model_L0.png` để bắt sớm.
- `BoAnh` — danh sách (nhãn, thư mục) để chạy.
- `NguongAlphaDo` — ngưỡng kết luận NG. `DiemToiThieu` — hạ xuống nếu nhiều ảnh báo `(duoi nguong)`.

**`--hoc`** — `HocDoMau.cs`, class `HocCfg`: `AnhGoc`, `VungKhoanh`, `BanKinhNoi`.

**`--model`** — `PatModel.cs`, class `ModelCfg`: `VungKhoanh` (`null` = tự lấy theo thân
vật), `SoMuc`, `KhoangCachDiem`, `SoDiemToiDa`.

## Đặt debug ở chỗ mới

```csharp
Dbg.Show(mat, "tên bước");        // hiện ảnh + in số liệu, dừng chờ phím
Dbg.Stats(mat, "tên");            // chỉ in số liệu
Dbg.ShowPair(truoc, sau, "tên");  // 2 ảnh cạnh nhau
Dbg.Log($"biến x = {x}");
Dbg.Values(mat, new Rect(100, 100, 10, 10), "vùng nghi ngờ");
```

## Còn thiếu gì so với CogPMAlign

Chưa có: độ chính xác dưới pixel (nội suy đỉnh điểm khớp), quét tỉ lệ (scale), nhiều
kết quả trên một ảnh, và mặt nạ don't-care khai báo được từ ngoài.
