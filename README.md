# hoc_patModel — tự dựng một CogPMAlign

Branch này chỉ có một mục đích: **tự viết một tool kiểu `CogPMAlign` của Cognex** — đưa vào
ảnh, trả về toạ độ `(x, y)` và góc `θ` của con hàng.

> 📐 **[docs/TU_DUY_MACHINE_VISION.md](docs/TU_DUY_MACHINE_VISION.md)** — tư duy machine
> vision cổ điển, đọc trước khi sửa thuật toán.

## Chạy

```powershell
dotnet run                          # mo GIAO DIEN PmAlign  <-- duong chay chinh
dotnet run -- "D:\anh\1B.bmp"       # nhu tren, mo san mot tam anh
dotnet run -- --tu-kiem             # tu kiem engine bang chan ly biet truoc
```

## Giao diện PmAlign

Đúng trình tự làm việc của CogPMAlign:

```
Mo anh  →  khoanh VUNG TIM KIEM  →  khoanh ROI MAU va XOAY cho vua khit  →  Train  →  Run
```

| Thao tác | Cách làm |
|---|---|
| Chọn hình đang khoanh | Ba nút tròn trên thanh công cụ: *Vung tim kiem* / *ROI mau* / *Vung che* |
| Vẽ hình mới | Kéo chuột trái ở chỗ trống |
| Di chuyển | Kéo chuột trái bên trong hình |
| Đổi kích thước | Kéo một trong bốn núm vuông ở góc (giữ nguyên góc xoay) |
| **Xoay 360°** | Kéo núm tròn phía trên hình · hoặc gõ số vào ô *Goc ROI* · hoặc kéo thanh trượt · hoặc bấm 0°/90°/180°/270° · hoặc mũi tên trái/phải (Shift = 1°, thường = 0.1°) |
| Trượt ảnh | Kéo chuột giữa hoặc chuột phải |
| Phóng to / thu nhỏ | Con lăn chuột (phóng quanh con trỏ) · `F` vừa khung · `1` về 100% |
| Xoá vùng che | `Delete` xoá cái đang chọn · nút *Xoa che* xoá hết |

Cả **ba** hình đều xoay 360° được (giống `CogRectangleAffine`), kể cả vùng tìm kiếm và vùng che.

### Ba hình khoanh dùng để làm gì

- **Vùng tìm kiếm** (xanh lơ, nét đứt) — giới hạn nơi **tâm (origin) của mẫu** được phép nằm,
  đúng ngữ nghĩa của Cognex. Nó **không phải** là "crop ảnh nhỏ lại": engine tự nới thêm đúng
  bán kính model khi cắt, nên vật nằm sát mép vùng tìm kiếm vẫn bắt được. Bỏ trống = tìm toàn ảnh.
- **ROI mẫu** (xanh lá) — phần đặc trưng của vật để học. Góc φ mà bạn xoay ROI trở thành
  **gốc 0° của model**: model được trích từ miếng mẫu đã dựng thẳng, còn φ cất riêng, nên góc
  Run trả về quy chiếu đúng về tư thế trên ảnh mẫu chứ không lệch một hằng số.
- **Vùng che** (đỏ) — don't-care, vẽ được nhiều cái. Điểm biên nằm trong đó bị loại khỏi model.

### Kết quả Run

`x`, `y` là tâm mẫu trên ảnh chạy; `θ` là góc **so với ảnh mẫu** (vật nằm y như lúc train thì
θ = 0), tương đương `Angle` của `CogPMAlignResult`. Điểm khớp 0..1 đã trừ nền ngẫu nhiên 2/π,
nên 0.3 đã là khá chắc chứ không phải 0.7.

## Các file

| File | Vai trò |
|---|---|
| `PmAlign/CuaSoPmAlign.xaml(.cs)` | Giao diện WPF: khung xem có phóng/trượt, ba hình khoanh xoay được, bảng tham số, nhật ký. |
| `PmAlign/PmEngine.cs` | **Engine duy nhất** cho cả Train lẫn Run: kim tự tháp, điểm biên + hướng gradient, dò thô→mịn, nội suy dưới pixel. |
| `PmAlign/RectXoay.cs` | Hình chữ nhật xoay được + quy ước góc + ma trận warp về mẫu. |
| `PmAlign/PmModel.cs` | Cấu trúc model và lưu/nạp JSON (`*.pmm.json`). |
| `PmAlign/PmCfg.cs` | Tham số Train/Run, sửa được từ giao diện. |
| `PmAlign/PmVe.cs` | Mat ↔ BitmapSource, và bảng vẽ model để soi bằng mắt. |
| `PmAlign/PmTuKiem.cs` | Tự kiểm bằng chân lý biết trước (`--tu-kiem`). |
| `Program.cs` | Bảng điều phối: không cờ → giao diện; có cờ → ba nhánh console cũ. |

Ba file cũ `PatModel.cs`, `HocDoMau.cs`, `YeaJoungCheckCoiNghieng.cs` và `Dbg.cs` giữ nguyên
để chạy lại các bài đo trước đây; **không** còn nằm trên đường chạy chính.

## Tự kiểm

`--tu-kiem` train một ROI (cố tình xoay 23.7°) trên một ảnh thật, rồi xoay/dời chính tấm ảnh
đó đi một lượng **đã biết** và bắt Run tìm lại. Đây là cách duy nhất bắt được lỗi "lệch đúng
một hằng số" — dò trên ảnh khác nhìn vẫn đẹp trong khi góc trả về sai dấu hoặc lệch φ.

Kết quả đo ngày 04/09/2026 (ảnh `1B.bmp` 3648², mẫu 816², vùng tìm 260², dải góc ±20°, Release):

```
sai so lon nhat 0.97 px va 0.150 do tren 6/6 ca      44..101 ms moi lan Run
```

Sai số vị trí phần lớn đến từ chính phép nội suy khi dựng ảnh test, nên đó là **cận trên**.

## Ba nhánh console cũ

```powershell
dotnet run -- --nghieng                              # do goc alpha ca bo OK/NG  -> nghieng_out\
dotnet run -- --hoc 1 --no-pause                     # bai hoc buoc 1..4         -> hoc_out\
dotnet run -- --model --no-window --no-pause         # trich model tu ModelCfg.AnhMaster -> model_out\
```

Đường dẫn, vùng khoanh, ngưỡng của ba nhánh này nằm trong class `*Cfg` đầu mỗi file — sửa code
rồi `dotnet run` lại. Cờ debug dùng chung: `--no-pause`, `--no-window`, `--no-debug`, `--only <tên>`.

**`--nghieng`** — `NghiengCfg`: `AnhMaster` + `VungTu` **luôn sửa cùng nhau**; đổi master mà
quên đổi Rect thì model trích ra rác nhưng chương trình vẫn chạy êm và trả α bậy.

**`--hoc`** — `HocCfg`: `AnhGoc`, `VungKhoanh`, `BanKinhNoi`.

**`--model`** — `ModelCfg`: `VungKhoanh`, `SoMuc`, `KhoangCachDiem`, `SoDiemToiDa`.

## Còn thiếu gì so với CogPMAlign

Đã có: ROI xoay được, vùng tìm kiếm, don't-care khai báo từ ngoài, dò thô→mịn nhiều mức,
nhiều kết quả trên một ảnh, nội suy dưới pixel và dưới bước góc, lưu/nạp model.

Chưa có: **quét tỉ lệ (scale)**, đo **coverage / clutter** tách riêng khỏi điểm khớp, và
mẫu dạng đa giác hoặc hình vành khăn (mới có hình chữ nhật xoay).
