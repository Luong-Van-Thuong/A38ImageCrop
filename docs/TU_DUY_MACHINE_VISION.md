# Tư duy Machine Vision cổ điển cho bài toán A38.ImageCrop

> Ghi lại buổi bàn luận ngày **2026-08-05** — bàn **tư duy giải bài toán**, chưa viết code.
> Bối cảnh: nhánh `loi_cuon_day`, phát hiện sứt mẻ / sụt mặt / thiếu thiếc cỡ **1-2mm**
> trên con hàng CoilAssy 1240S, **không dùng deep learning**.

---

## Mục lục

1. [Hiện trạng pipeline](#1-hiện-trạng-pipeline)
2. [Phản biện ý tưởng xoay ảnh bằng ApproxPolyDP](#2-phản-biện-ý-tưởng-xoay-ảnh-bằng-approxpolydp)
3. [Tư duy nghề: 5 trụ cột](#3-tư-duy-nghề-5-trụ-cột)
4. [Bản đồ đối chiếu](#4-bản-đồ-đối-chiếu-pipeline-hiện-tại-vs-chuẩn-nghề)
5. [Thứ tự làm lại đề xuất](#5-thứ-tự-làm-lại-đề-xuất)
6. [Câu hỏi còn treo](#6-câu-hỏi-còn-treo)
7. [Từ khoá để tra cứu thêm](#7-từ-khoá-để-tra-cứu-thêm)

---

## 1. Hiện trạng pipeline

Hai giai đoạn trong `Program.cs`:

**Giai đoạn 1 — `CropLargestRegion()`** (cắt cả con hàng ra khỏi ảnh gốc)

```
gray -> blur -> Canny -> morph Close -> FindContours -> lấy contour lớn nhất
     -> ApproxPolyDP -> nếu 4 đỉnh thì WarpToRect, không thì cắt BoundingRect
```

**Giai đoạn 2 — `CropSmallRegion()`** (soi 4 góc tìm "phần nhô")

```
cắt 4 góc -> tạo objMask -> MorphologyEx Open (kernel ellipse lớn)
          -> Subtract(objMask, opened) = residual  ⇒ residual chính là phần nhô
```

### Những điểm dễ vỡ đã nhận diện

- **Toàn bộ chuỗi là ngưỡng tuyệt đối**: `Canny 150/210`, kernel `30×30`, ellipse `60×60`.
  Mỗi lần đổi thư mục ảnh (`Sut_mat` → `not_found` → `Thieu_thiec`) lại phải sửa hằng số.
  Đây **không phải bug** — đây là dấu hiệu thuật toán **chưa bất biến với thứ nó nên bất biến**
  (độ sáng, độ phóng đại, độ tương phản).

- **Giai đoạn 1 quyết định giai đoạn 2.** Nếu bounding rect lệch hoặc thừa nền, 4 góc cắt ra
  đã sai vị trí ⇒ giai đoạn 2 không bao giờ cứu được. Nhưng log "not found" lại ghi ở
  giai đoạn 2 ⇒ **đang chẩn đoán sai tầng**.

- `if (gocBatDuoc.Count != 2)` — đang giả định "luôn đúng 2 góc có phần nhô". Nếu đó là ràng buộc
  vật lý thật thì rất mạnh, nên khai thác **sớm** chứ không dùng để lọc ở cuối. Và nó **phụ thuộc
  chiều ảnh** — xem mục 2.4b.

- Cả hướng fit-line (commit `ebdd91e`) lẫn hướng residual đều là **hình học thuần**. Nếu ảnh fail
  vì ánh sáng / phản quang thì đổi hình học kiểu gì cũng không giải quyết được.

---

## 2. Phản biện ý tưởng xoay ảnh bằng ApproxPolyDP

**Ý tưởng đề xuất:** sau `CropLargestRegion`, nếu ảnh nghiêng thì dùng
`ArcLength` + `ApproxPolyDP` lấy toạ độ các đỉnh, so với trục Oxy để xoay cho
cạnh trái/phải vuông góc Ox, cạnh trên/dưới song song Ox.

### 2.1. Ý tưởng này **đã có sẵn trong code**, và nó đang không chạy

`Program.cs:633-649` chính xác là thứ đó: `ArcLength` → `ApproxPolyDP` → nếu `approx.Length == 4`
thì `OrderCorners` + `WarpToRect`. Nhưng thực tế ảnh đang rơi vào nhánh `else` (`BoundingRect`,
`Program.cs:652`), nghĩa là **`approx.Length != 4`**.

Nên câu hỏi không phải "code chi tiết thế nào" mà là **tại sao không ra 4 đỉnh**:

| Nguyên nhân | Giải thích |
|---|---|
| Morph Close `30×30` (`Program.cs:543`) | Đã bo tròn 4 góc silhouette. Góc bo tròn ⇒ ApproxPolyDP rải nhiều đỉnh trên cung tròn, không bao giờ ra đúng 4. |
| Chính "phần nhô" cần bắt | Nó cũng là chỗ lồi trên viền ⇒ tự sinh thêm đỉnh. Muốn ép về 4 đỉnh phải tăng eps, mà tăng eps thì **nuốt luôn phần nhô**. |
| `ApproxEpsRatio = 0.02 * peri` | Bước nhảy rất thô. Thường **không tồn tại** khoảng eps ổn định cho ra đúng 4 với vật thể có bump thật. |

> **Mâu thuẫn cốt lõi:** ApproxPolyDP có mục tiêu là *làm mượt viền*, đối nghịch trực tiếp với
> mục tiêu bài toán là *giữ chỗ lồi*. Sai công cụ ngay từ đầu.

Kiểm chứng nhanh: xem log `Xap xi da giac: N dinh` (`Program.cs:636`) trên mấy ảnh nghiêng.

### 2.2. Phản biện nặng nhất: warp tứ giác ≠ xoay ảnh

Mục tiêu phát biểu — *"cạnh trái/phải vuông góc Ox, trên/dưới song song Ox"* — là
**phép xoay thuần (rotation)**: **1 tham số góc**, **bảo toàn hình dạng**.

Nhưng `WarpToRect` từ 4 đỉnh là **phép biến đổi phối cảnh (homography)**: **8 tham số**,
**kéo giãn phi tuyến** để ép tứ giác thành chữ nhật.

Hậu quả: nếu 4 đỉnh lấy được lệch một chút (chắc chắn lệch, vì viền đã bị morph bo tròn),
phần nhô ở mỗi góc bị **kéo méo với hệ số khác nhau**. Đang đo một khuyết tật cỡ 1-2mm rồi
cho nó qua một phép biến đổi làm thay đổi kích thước không đồng đều ⇒ **residual không còn
so sánh được giữa các ảnh**.

Homography chỉ đáng dùng nếu **camera thật sự đặt xiên** (có biến dạng phối cảnh). Nếu camera
vuông góc mặt hàng và con hàng chỉ **nằm nghiêng trên băng tải**, dùng homography là sai công cụ,
và còn nguy hiểm vì nó "chữa" luôn cả những biến dạng không có thật.

### 2.3. Công cụ đúng: `MinAreaRect`

Đã dùng ở `Program.cs:1019`, và comment tại đó tự giải thích đúng lý do:
*"MinAreaRect cho ra kích thước thật, không phụ thuộc góc đặt"*.

`Cv2.MinAreaRect(best)` → `RotatedRect { Center, Size, Angle }`. Có ngay góc nghiêng:
- không cần đếm đỉnh
- không cần `OrderCorners`
- không phụ thuộc eps

Sau đó `GetRotationMatrix2D` + `WarpAffine`. **1 tham số thay vì 8, bảo toàn hình dạng.**

### 2.4. Nhưng MinAreaRect có 3 cái bẫy phải quyết trước

**a) Nhập nhằng 90°.** OpenCV trả `Angle` trong khoảng hẹp, Width/Height có thể hoán đổi.
Xoay theo `rect.Angle` mù quáng thì có ảnh đúng chiều, có ảnh nằm ngang 90°.
Cần **luật phân định**, ví dụ *"cạnh dài luôn nằm ngang"* — nhưng luật này **sập nếu con hàng
gần vuông**.

**b) Nhập nhằng 180°.** Xoay thẳng trục **không** cho biết đâu là trên đâu là dưới.
Mà giai đoạn 2 cắt **4 góc riêng biệt** và có `if (gocBatDuoc.Count != 2)` — tức đang giả định
phần nhô nằm ở 2 góc **cụ thể**. Ảnh lật 180° ⇒ "góc trên-trái" đổi nghĩa ⇒ **logic sai âm thầm,
không crash**. Cần một **mốc bất đối xứng** để chốt chiều (vết khía, chân linh kiện, phân bố sáng…).

**c) Chính khuyết tật kéo lệch góc — vấn đề tư duy sâu nhất.**
Đang dùng **đường viền** để xác định hệ quy chiếu, rồi lại dùng hệ quy chiếu đó để **đo khuyết tật
trên đường viền**. Con hàng bị sụt mặt / thiếu thiếc có silhouette khác con hàng tốt ⇒ MinAreaRect
ôm ra góc hơi khác ⇒ **hàng lỗi và hàng tốt bị xoay khác nhau**. Hệ quy chiếu bị nhiễm bởi chính
thứ cần đo.

> **Cách thoát:** đừng lấy góc từ toàn bộ silhouette. Lấy từ **2 cạnh dài thẳng nhất** — chúng là
> phần ổn định, không phải nơi khuyết tật xuất hiện. Dùng `HoughLinesP` trên ảnh `edges` rồi lấy
> trung bình góc (mod 90°, có trọng số theo độ dài đoạn), hoặc fit line lên riêng các điểm viền
> thuộc cạnh dài.

**Lưu ý về commit `ebdd91e`:** hướng fit-line bị bỏ là fit-line để **tìm phần nhô** — việc khó,
vì bump rất nhỏ so với nhiễu. Fit-line để **tìm góc nghiêng** là việc **dễ hơn hẳn**: fit lên một
cạnh dài hàng trăm pixel, sai số góc rất nhỏ. **Đừng vứt công cụ vì lần trước dùng sai chỗ.**

### 2.5. Xoay ở đâu — lỗi kỹ thuật suýt mắc

Ý định ban đầu là xoay **sau** dòng `using var result = CropLargestRegion(src)` (`Program.cs:446`).
**Không nên**:

- `result` là ảnh đã cắt theo `BoundingRect` **thẳng trục** của một vật thể **nghiêng** ⇒
  chứa nhiều nền thừa ở 4 góc.
- Xoay ảnh đã cắt ⇒ sinh **viền đen** ở góc. Viền đen đi thẳng vào `CropSmallRegion` → tạo cạnh
  giả cho Canny, bị tính vào `objMask` / `residual`. **Đây đúng là kiểu lỗi tạo ra thư mục `not_found`.**
- **Nội suy hai lần** nếu sau đó còn cắt lại.

**Thứ tự đúng:** tính góc từ contour trong toạ độ **ảnh gốc** → `WarpAffine` trên `src`
→ **rồi mới cắt** hình chữ nhật thẳng trục quanh tâm đã biết.
⇒ một lần nội suy, không viền đen (miễn padding còn nằm trong `src`).

Hai chi tiết kèm theo:
- `WarpAffine` **làm mềm cạnh** (nội suy). Ngưỡng Canny/morph giai đoạn 2 vốn đã rất nhạy ⇒ xoay
  **trước mọi phép đo**, và sau khi thêm bước xoay thì **phải hiệu chỉnh lại ngưỡng**, đừng giả
  định giữ nguyên.
- Nếu `|góc| < ~0.5°` thì **bỏ qua không xoay**, tránh nội suy vô ích cho phần lớn ảnh vốn đã thẳng.

### 2.6. Kết luận mục 2

Hướng "xoay cho thẳng trục" **đúng và đáng làm** — nó khiến giai đoạn 2 xác định 4 góc một cách
**tất định**, đó là giá trị thật. Nhưng cách lấy góc bằng `ApproxPolyDP` → 4 đỉnh → warp thì **bác bỏ**:
sai công cụ (homography thay vì rotation), không ổn định (phụ thuộc eps, xung đột với chính bump
cần giữ), và code đã chứng minh nó không ra 4 đỉnh.

---

## 3. Tư duy nghề: 5 trụ cột

### Đảo ngược tư duy

| | Cách nghĩ |
|---|---|
| **Người mới** | Chụp ảnh → **tìm chỗ bất thường** trong ảnh |
| **Kỹ sư vision** | Tôi **đã biết trước** con hàng tốt trông thế nào. Việc của tôi là (1) định vị thật chính xác, (2) đưa về hệ toạ độ chuẩn, (3) **đo** những con số cụ thể ở những chỗ cụ thể, (4) so với dung sai. |

Khác biệt: cách 1 sinh ra thuật toán "dò tìm" mù mờ, phải chỉnh ngưỡng mãi.
Cách 2 sinh ra chuỗi **phép đo có đơn vị** — mỗi bước ra một con số **mm**, không phải một boolean.

> Trực giác *"mỗi ảnh coi như một hệ toạ độ Oxy giống nhau"* chính là khái niệm
> **Part Coordinate System / Fixture** — viên gạch nền móng của cả ngành. Trực giác đúng.
> Khoảng cách nằm ở **cách thiết lập** hệ toạ độ đó và **độ chính xác** của nó.

---

### Trụ 1 — Định vị bằng **fiducial**, tuyệt đối không bằng thứ mình đo

Nguyên tắc số một, và là chỗ pipeline hiện tại đang vi phạm (xem 2.4c).

Fiducial được chọn theo **3 tiêu chí**: luôn xuất hiện — tương phản cao — **không bao giờ bị lỗi**.
Lỗ định vị, chân linh kiện, một cạnh gia công chuẩn — cái gì cũng được, miễn **không phải vùng kiểm tra**.

Mô thức chuẩn là **coarse-to-fine**:

| Bước | Công cụ OpenCV | Kết quả |
|---|---|---|
| **Coarse** | `FindContours` + `MinAreaRect` | góc thô, sai vài độ — đủ để bắt được |
| **Fine** | `Cv2.FindTransformECC` với `MotionTypes.Euclidean` | (dx, dy, θ) **dưới mức pixel**, căn vào **ảnh mẫu golden** |

`FindTransformECC` là câu trả lời của OpenCV cho bài toán align mà rất ít người dùng, trong khi nó
đúng là thứ cần. Nó còn **giải luôn nhập nhằng 90°/180°** ở mục 2.4, vì căn vào một chiều tham
chiếu cụ thể chứ không suy diễn từ hình học.

Các lựa chọn khác: `matchTemplate` quét qua dải góc (thô thiển nhưng chạy được),
ORB/SIFT + `EstimateAffinePartial2D` (cần có vân bề mặt).

---

### Trụ 2 — Đo cạnh **sub-pixel** bằng caliper, không nhị phân hoá

**Khoảng cách lớn nhất giữa nghiệp dư và chuyên nghiệp.**

Hiện tại: threshold → mask nhị phân → morphology → đếm pixel. Mọi thứ phụ thuộc **một ngưỡng sáng**
⇒ đổi ánh sáng là sập ⇒ đó là lý do phải sửa `Canny 150/210` mỗi lần đổi thư mục.

Kỹ sư vision dùng **caliper tool** (Cognex gọi *Caliper*, Halcon gọi *metrology model*;
OpenCV **không có sẵn** — tự viết ~50 dòng):

1. Đặt ROI nhỏ hình chữ nhật, **vuông góc với cạnh cần đo**, vị trí lấy từ fixture.
2. **Chiếu (project)** cường độ sáng dọc theo chiều **song song** cạnh → còn lại 1 profile 1 chiều.
   Việc chiếu này trung bình hoá nhiễu, **tăng SNR theo căn bậc hai số dòng**.
3. Đạo hàm profile → tìm đỉnh gradient → **nội suy parabol quanh đỉnh** → vị trí cạnh
   chính xác **~1/10 pixel**.
4. Rải hàng chục caliper dọc cạnh → được **đám điểm cạnh sub-pixel**.
5. **Fit đường thẳng bằng RANSAC** (hoặc Huber) — **không** dùng least-squares thường.
6. Điểm nào lệch khỏi đường fit quá dung sai = **sứt mẻ**. Độ lệch đó chính là kích thước
   khuyết tật, quy ra **mm**.

Ưu điểm cốt lõi: dùng **vị trí gradient**, không dùng **giá trị tuyệt đối** ⇒ **miễn nhiễm với
biến động ánh sáng**.

> **Tại sao fit-line lần trước thất bại (commit `ebdd91e`)**
>
> Nhiều khả năng đã fit least-squares lên điểm contour **nhị phân**. Hai lỗi cùng lúc:
> 1. Điểm bị **lượng tử hoá ±0.5px** — nhiễu ngang tầm khuyết tật cần đo.
> 2. Least-squares bị **chính khuyết tật kéo lệch** đường fit — đường thẳng chạy theo chỗ sứt,
>    nên residual gần bằng 0, **không thấy gì**.
>
> RANSAC thì ngược lại: coi điểm khuyết tật là **outlier**, đường fit bám vào **phần cạnh lành**,
> khuyết tật hiện lên rõ mồn một.
>
> **Cùng một ý tưởng, hai kết quả trái ngược.** Đừng vứt fit-line — chỉ là đã dùng sai phiên bản.

---

### Trụ 3 — Hiệu chuẩn: mọi ngưỡng phải có đơn vị **mm**

Spec nói "sứt mẻ 1-2mm", nhưng code có `area < 2000` (px), `Size(60,60)`, `Canny 150`.
Đó là những con số **không mang ý nghĩa vật lý** ⇒ không suy luận được, không bảo vệ được trước
khách hàng, không tái lập được khi đổi ống kính.

Kỹ sư vision chụp **thước chuẩn / bàn cờ** → biết **mm/pixel** → viết ngưỡng theo spec:

```
nguongSutMe_mm = 1.0
```

Khi khách hỏi *"sao con này NG?"* thì trả lời được: **"vết lõm 1.34mm, spec 1.0mm"** — truy vết được.

#### Phép tính khả thi phải làm **trên giấy, trước khi viết dòng code nào**

> **1mm bằng bao nhiêu pixel?**
>
> Quy tắc ngón tay cái: chi tiết nhỏ nhất cần chiếm **≥ 10 pixel** thì mới đo ổn định.
> Nếu quang học hiện tại chỉ cho ~3 px/mm thì **không thuật toán nào cứu được** —
> phải đổi ống kính hoặc đưa camera lại gần.

Kỹ sư vision kiểm tra điều này **trước**. Người mới code 3 tuần rồi mới phát hiện.

---

### Trụ 4 — Chiếu sáng giải quyết 80% bài toán; **đổi đèn trước khi đổi code**

Điểm văn hoá nghề không có trong tutorial OpenCV.

Triệu chứng **"phải chỉnh ngưỡng theo từng thư mục ảnh"** gần như luôn là
**bài toán chiếu sáng bị đẩy sang phần mềm**.

Với sứt mẻ trên cạnh, các đáp án kinh điển:

| Kiểu đèn | Hiệu quả |
|---|---|
| **Backlight (đèn nền)** | Con hàng thành **bóng đen tuyệt đối** trên nền trắng. Sứt mẻ thành khuyết trên biên. Nhị phân hoá trở nên tầm thường và **bất biến với ánh sáng môi trường**. Cả project có thể rút còn vài chục dòng. |
| **Dark-field / đèn vòng góc thấp** | Khuyết tật bề mặt **sáng lên**, mặt phẳng lành tối đen. |
| **Coaxial** | Cho bề mặt bóng, phản quang. |

Có thể không được quyền đổi rig ở trạm A38. Nhưng **phải biết** điều này và **phải nêu ra** —
nếu ảnh đầu vào tương phản kém thì mọi công sức thuật toán chỉ là **bù trừ cho phần cứng sai**,
và bù trừ đó **không bao giờ ổn định**.

---

### Trụ 5 — Ngưỡng đến từ **phân bố dữ liệu**, không từ mò trên 5 tấm ảnh

Khác biệt về **phương pháp làm việc**, và nó thay đổi mọi thứ.

Đã có sẵn các thư mục phân loại (`Sut_mat`, `Thieu_thiec`, `not_found`, `img_all`). Quy trình đúng:

1. Chạy pipeline trên **toàn bộ** tập ảnh.
2. Mỗi ảnh xuất ra **các con số đo được** (độ lệch max mm, diện tích residual mm², …) vào **CSV** —
   **không phán OK/NG vội**.
3. Vẽ **histogram** của nhóm hàng tốt và nhóm hàng lỗi **trên cùng trục**.
4. Nhìn hai phân bố:
   - **Tách rời rõ** → đặt ngưỡng vào giữa khe. **Xong. Bài toán đã giải.**
   - **Chồng lấn** → đặc trưng đang chọn **không đủ phân biệt**. **Không ngưỡng nào cứu được.**
     Phải quay lại đổi đặc trưng / đổi đèn / đổi cách đo.
     ⇒ **Đây là thông tin quý nhất, và pipeline hiện tại không hề sinh ra nó.**
5. Chọn **điểm vận hành** có chủ đích: bỏ sót hàng lỗi thường là ràng buộc **cứng (= 0)**,
   báo nhầm hàng tốt là **chi phí chấp nhận được**.

> Hiện đang debug bằng cách nhìn `Dbg.Show` từng ảnh một. Đó là **kính lúp** — cần, nhưng
> **không thay được cái histogram**. Kỹ sư vision sống bằng **phân bố**, không bằng ảnh lẻ.

---

## 4. Bản đồ đối chiếu: pipeline hiện tại vs chuẩn nghề

| Đang có | Tương ứng trong tư duy nghề | Thiếu gì |
|---|---|---|
| Canny + FindContours + contour lớn nhất | Định vị **thô** | Đang bị dùng như **phép đo cuối**, không phải chỉ là bước định vị |
| Cắt 4 góc | **ROI theo fixture** ✅ | Vị trí ROI chưa neo vào hệ toạ độ ổn định |
| `mask − opening` = residual | Morphological **top-hat** — kỹ thuật cổ điển **hợp lệ** ✅ | Chạy trên **miền nhị phân** ⇒ thừa hưởng toàn bộ sự mong manh của ngưỡng |
| — | Căn tinh sub-pixel (ECC) | **chưa có** |
| — | Hiệu chuẩn mm/pixel | **chưa có** |
| — | Đo sub-pixel + RANSAC | **chưa có** |
| — | Thống kê ngưỡng theo phân bố | **chưa có** |

Trực giác đúng ở **2/7 hàng** — không tệ với người mới bước vào ngành.
Cái thiếu **không phải là thông minh hơn**, mà là **thêm các tầng chưa biết là chúng tồn tại**.

---

## 5. Thứ tự làm lại đề xuất

1. **Tính px/mm.** Nếu 1mm < 10px → **dừng**, báo cáo vấn đề quang học.
2. **Hỏi về đèn.** Có backlight được không? Nếu có → làm lại từ đây, mọi thứ **dễ đi 10 lần**.
3. **Chọn fiducial** — đặc trưng không bao giờ lỗi — để định vị.
4. **Coarse** (`MinAreaRect`) → **fine** (`FindTransformECC` với golden template)
   ⇒ pose sub-pixel, **hết luôn nhập nhằng chiều**.
5. **`WarpAffine` một lần** về hệ toạ độ chuẩn (thực hiện trên `src`, xem 2.5).
6. **Định nghĩa ROI kiểm tra một lần duy nhất** trong hệ chuẩn đó —
   chúng **cố định vĩnh viễn**, không tính lại theo từng ảnh.
7. Trong mỗi ROI: **caliper → điểm cạnh sub-pixel → RANSAC → residual tính ra mm**.
8. Chạy **toàn bộ tập ảnh** → CSV → histogram → chọn ngưỡng.

> Bước **1, 2, 8** là thứ kỹ sư vision làm mà lập trình viên thường bỏ qua — và chúng quyết định
> bài toán **có giải được hay không** nhiều hơn cả bước 7.

---

## 6. Câu hỏi còn treo

Chưa có câu trả lời, cần chốt trước khi code:

1. **1mm trên ảnh bằng bao nhiêu pixel?**
   (đo thô: lấy một kích thước con hàng đã biết theo mm, chia cho số pixel nó chiếm)
   → **quan trọng nhất**, quyết định bài toán có khả thi không.
2. **Có được quyền động vào đèn / camera** ở trạm A38 không, hay ảnh đầu vào là bất di bất dịch?
3. Nghiêng là do **đặt lệch** hay do **camera chụp xiên**?
   → quyết định dùng **rotation** hay **homography**.
4. Con hàng có **tỉ lệ dài/rộng rõ rệt** không, và có **mốc bất đối xứng** để chốt chiều 180° không?
5. Góc nghiêng thực tế cỡ bao nhiêu — **vài độ** hay tới **30-40°**?
6. Ảnh trong `not_found` fail ở **tầng nào** — cắt con hàng đã sai, hay cắt đúng mà residual rỗng?

---

## 7. Từ khoá để tra cứu thêm

**Định vị / align**
`fiducial` · `part coordinate system` · `pattern matching` · `NCC (normalized cross correlation)` ·
`shape-based matching` · `ECC image alignment` · `coarse-to-fine registration`

**Đo lường**
`caliper tool` · `subpixel edge detection` · `gradient peak parabolic interpolation` ·
`edge projection / 1D profile` · `RANSAC line fitting` · `robust regression (Huber)` ·
`metrology model (Halcon)`

**Kiểm tra ngoại quan**
`golden template comparison` · `difference imaging` · `morphological top-hat` ·
`tolerance band from N good samples` · `AOI / ADI`

**Hiệu chuẩn & quang học**
`camera calibration checkerboard` · `mm per pixel` · `spatial resolution rule of thumb` ·
`backlight` · `dark-field illumination` · `coaxial / diffuse dome light`

**Thống kê quyết định**
`ROC curve` · `operating point` · `escape rate vs false call rate` · `Gauge R&R`

**API OpenCvSharp liên quan**
`Cv2.MinAreaRect` · `Cv2.GetRotationMatrix2D` · `Cv2.WarpAffine` · `Cv2.FindTransformECC` ·
`Cv2.HoughLinesP` · `Cv2.EstimateAffinePartial2D` · `Cv2.Sobel` · `Cv2.Remap`

---

*Tài liệu này là kết quả bàn luận, **chưa có dòng code nào được viết**.
Bước tiếp theo: trả lời các câu hỏi ở mục 6, đặc biệt câu 1 và 2.*
