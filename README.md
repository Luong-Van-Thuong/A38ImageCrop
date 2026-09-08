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

App là `WinExe`: mở giao diện **không** kèm cửa sổ terminal đen nào. Các nhánh console
(`--model`, `--hoc`, `--nghieng`, `--do-bien`, `--tu-kiem`, `--help`) vẫn in bình thường —
`Program.MoConsoleNeuCan()` bám vào console của shell đang gọi, hoặc tự mở một cái nếu bấm
đúp từ Explorer.

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
- **Vùng che** (đỏ) — don't-care, vẽ được nhiều cái. Điểm biên nằm trong đó bị loại khỏi model,
  và lúc Run **clutter cũng bỏ qua** vùng đó (xem dưới).

Vùng che **được lưu vào file model** theo hai dạng, vì hai dạng dùng cho hai việc khác nhau:
`Mask` là danh sách hình chữ nhật xoay bạn vẽ tay (nạp lên là thấy lại trên màn hình), còn
`MatNaChe` là ảnh bit ở toạ độ mẫu — gộp cả phần **tự dò** (`KieuChe`, hình dạng bất kỳ, không
dựng lại được từ vài hình chữ nhật). Ảnh bit được đóng bit + Deflate + base64: mẫu 816² xuống
dưới 1 KB thay vì 651 KB.

Mặt nạ chốt **lúc Train**. Sửa hình che sau khi Train rồi bấm Lưu thì file vẫn giữ bản lúc
Train — giao diện ghi một dòng `LUU Y` chứ không lưu lặng lẽ. Muốn đổi thật thì Train lại.

### Kết quả Run

`x`, `y` là tâm mẫu trên ảnh chạy; `θ` là góc **so với ảnh mẫu** (vật nằm y như lúc train thì
θ = 0), tương đương `Angle` của `CogPMAlignResult`.

Mỗi kết quả có **hai** con số:

* **`diem`** — *coverage*: model tìm lại được bao nhiêu phần cạnh của chính nó, 0..1.
* **`clutter`** — tỉ lệ pixel biên nằm trong dấu chân của model mà model **không** giải thích
  được. Coverage cao + clutter cao là dấu hiệu kinh điển của bắt nhầm vào vùng nhiều texture.

Clutter **không đếm vùng che** — không vào tử số mà cũng không vào mẫu số (`CheKhongTinhClutter`,
mặc định bật). Nếu đếm thì vùng che quay lại cắn chính mình: nó không có điểm model (đã bị loại
lúc Train) nên mọi cạnh ở đó là "cạnh lạ", và tư thế **đúng** bị phạt đúng bằng tư thế sai — che
một vùng vì nó hay thay đổi lại thành tự bắn vào chân mình. Đo trên một ảnh dựng sẵn có ô nhiễu
nằm trong ROI: clutter của tư thế đúng **0.808 → 0.485** khi tôn trọng vùng che, còn `diem` và
vị trí không đổi một chữ số nào. Tắt cờ là quay về hành vi cũ để đối chứng.

Đừng so thẳng `diem` với ngưỡng của Cognex: ngưỡng của hai tool không cùng thang. Thứ quyết
định đặt được ngưỡng hay không là **biên** giữa đỉnh đúng và đỉnh sai — đo bằng `--do-bien`.

### Chấm điểm (sửa 08/09/2026)

Ba núm trong nhóm **CHẤM ĐIỂM** là **một gói**, bật lẻ từng cái là hỏng:

| Núm | Mặc định | Làm gì |
|---|---|---|
| NMS lúc chạy | bật | Dùng Canny lúc chạy với đúng cặp ngưỡng mà mức đó đã dùng lúc train, thay cho sàn độ lớn trần trụi. Trước đây train dùng Canny còn Run chỉ dùng sàn — hai định nghĩa "biên" khác nhau, và đó là lỗi gốc làm tool bám vào vùng texture. |
| Bỏ chiều tương phản | **tắt** | Tắt = tích vô hướng **có dấu**, giống `IgnorePolarity = false` của Cognex. Một vết xước là *gờ* (hai mép ngược dấu), cạnh vật là *bậc* (một dấu) — bỏ dấu là không phân biệt được hai thứ đó. |
| Dung sai ghép biên | 2 px | Điểm model ghép với biên **gần nhất** trong bán kính này, trọng số giảm tuyến tính theo khoảng cách — thay cho việc đọc đúng một pixel. Tính bằng pixel *của mức đang xét* nên ở mức thô nó tự nới rộng ra. |

Đo trên `JeaYoung/Coil/.../NghiengLenXuong` (36 ảnh, vùng tìm 926×877 = 48× diện tích model,
dải góc 360°, chân lý dựng bằng khung hẹp rồi soi bằng mắt, 2 ảnh bị loại vì chân lý không
đáng tin):

| cấu hình | top-1 đúng | thấy đỉnh đúng | đỉnh ĐÚNG thấp nhất | biên |
|---|---|---|---|---|
| bản cũ | 31/34 | 33/34 | 0.243 | −0.076 |
| + NMS, **vẫn** `\|cos\|` | 24/34 | 27/34 | 0.332 | −0.095 |
| + có dấu | 33/34 | 34/34 | 0.188 | −0.076 |
| + dung sai 1 px | **34/34** | 34/34 | 0.126 | −0.042 |
| + dung sai 3 px | **34/34** | 34/34 | 0.235 | −0.062 |
| + lọc ổn định (0.40/0.30) | **34/34** | 34/34 | **0.282** | −0.072 |

Ba điều đáng nhớ:

1. **NMS một mình làm TỆ HƠN HẲN** (31 → 24). Nó chỉ thành lợi khi đi cùng dấu.
2. Bài **định vị** đã xong: top-1 từ 31/34 lên 34/34.
3. **Biên vẫn âm ở mọi cấu hình** — bài "một ngưỡng chung tách sạch đúng/sai" thì **chưa**
   xong. Trên bộ này không cần vế sau (mọi ảnh đều có vật), nhưng đừng nhầm hai chuyện đó.

### Train nhiều ảnh

Nút **Train nhieu anh…** train trên ảnh đang mở rồi bắt model tự chứng minh trên những ảnh
khác và **loại** những điểm không trụ được. Cần khi model học từ một ảnh có quá nửa số điểm
rơi vào phần nội thất đổi theo góc nghiêng / phản quang. Trên bộ trên nó nâng đỉnh đúng thấp
nhất từ 0.188 lên 0.282 — thang điểm tăng 50% mà không mất tấm nào.

Đọc dòng `L0: 339 -> N diem` trong nhật ký: nếu mọi mức đều báo `QUA IT ... GIU NGUYEN` thì
lọc **chưa hề chạy**, phải hạ `TiLeOnDinh` / `NguongTruDiem`.

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
| `PmAlign/PmDoBien.cs` | Đo **biên** giữa đỉnh đúng và đỉnh sai trên cả một thư mục ảnh, nhiều cấu hình một lượt (`--do-bien`). Ra CSV + ảnh ghép để mắt phán xử. |
| `Program.cs` | Bảng điều phối: không cờ → giao diện; có cờ → ba nhánh console cũ. |

Ba file cũ `PatModel.cs`, `HocDoMau.cs`, `YeaJoungCheckCoiNghieng.cs` và `Dbg.cs` giữ nguyên
để chạy lại các bài đo trước đây; **không** còn nằm trên đường chạy chính.

## Tự kiểm

`--tu-kiem` train một ROI (cố tình xoay 23.7°) trên một ảnh thật, rồi xoay/dời chính tấm ảnh
đó đi một lượng **đã biết** và bắt Run tìm lại. Đây là cách duy nhất bắt được lỗi "lệch đúng
một hằng số" — dò trên ảnh khác nhìn vẫn đẹp trong khi góc trả về sai dấu hoặc lệch φ.

Kết quả đo (ảnh `1B.bmp` 3648², mẫu 816², vùng tìm 260², dải góc ±20°):

```
04/09/2026, chấm điểm cũ  :  0.97 px  /  0.150 do   6/6 ca   44..101 ms
08/09/2026, chấm điểm mới :  0.99 px  /  0.072 do   6/6 ca   153..198 ms
```

Vị trí đứng yên, **góc tốt lên gấp đôi**. Lo ban đầu là "dung sai ghép biên làm bẹt đỉnh nên
mất độ chính xác dưới pixel" — đo ra thì sai: dung sai càng rộng góc càng chuẩn (0 → 0.172°,
1 px → 0.107°, 2 px → 0.107°, 3 px → 0.091°), vị trí chỉ nhích từ 0.70 lên ~1.0 px.

Sai số vị trí phần lớn đến từ chính phép nội suy khi dựng ảnh test, nên đó là **cận trên**.

## Đo biên trên một bộ ảnh thật

```
dotnet run -- --do-bien <thu-muc-anh> <model.pmm.json>
              [--vt cx,cy,w,h]   khung hẹp để dựng CHÂN LÝ
              [--bo a.bmp,b.bmp] loại vài ảnh khỏi thống kê
              [--on-dinh]        train lại có lọc ổn định rồi đo lại
              [--ra <thu-muc>]
```

Chạy một model trên cả thư mục với **nhiều cấu hình chấm điểm một lượt**, mỗi ảnh xin về 12
đỉnh, rồi tách theo chân lý: đỉnh gần chân lý là *đúng*, đỉnh xa là *sai*. Biên = (đỉnh đúng
thấp nhất) − (đỉnh sai cao nhất). **Dương** thì đặt được một ngưỡng chung, **âm** thì không.

Ba cái bẫy đã dính khi dựng bài đo này, đều làm ra con số đẹp/xấu giả:

1. **Dải góc.** Chạy ±20° trên bộ ảnh có vật ở hai hướng lệch ~90° làm 31/36 tụt xuống 21/36
   mà không phải lỗi thuật toán. Dựng contact sheet cả bộ **trước** khi đo.
2. **Chân lý.** Dựng chân lý bằng cấu hình cũ thì chính nó sai 12/36 ô. Luôn soi
   `ghep_chanly.png` bằng mắt trước khi tin bảng số.
3. **Chỉ nhìn top-1.** Đỉnh sai cao nhất phải đo *trên chính tấm ảnh có vật*, không phải mượn
   từ tấm khác — nếu không thì biên đo ra vô nghĩa.

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
nhiều kết quả trên một ảnh, nội suy dưới pixel và dưới bước góc, lưu/nạp model,
**biên chuỗi (NMS) lúc chạy**, **chấm điểm có chiều tương phản**, **dung sai ghép biên**,
**coverage / clutter tách riêng**, **train nhiều ảnh lọc theo độ ổn định**.

Chưa có, xếp theo mức đáng làm:

1. **Fit pose bằng bình phương tối thiểu** sau khi hội tụ. Hiện mới có nội suy parabol quanh
   đỉnh; PatMax ghép từng điểm với feature gần nhất **rồi giải pose**. Đây gần như chắc chắn
   là chỗ còn lại để biên chuyển từ âm sang dương.
2. **Quét tỉ lệ (scale)** và các bậc tự do khác (aspect, skew) — CogPMAlign bật/tắt được từng
   cái. Cần khi vật nghiêng thật sự chứ không chỉ xoay trong mặt phẳng.
3. **Biên dưới pixel**: hiện bản đồ biên là nhị phân nên đỉnh điểm số bám vào lưới nguyên.
   Nội suy đỉnh gradient ngang cạnh sẽ trả lại phần chính xác đã mất khi bật NMS.
4. Mẫu dạng đa giác hoặc hình vành khăn (mới có hình chữ nhật xoay).
