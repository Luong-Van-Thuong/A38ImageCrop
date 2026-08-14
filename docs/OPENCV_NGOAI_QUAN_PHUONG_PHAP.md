# Làm ngoại quan bằng OpenCV thuần — bản đồ phương pháp

> Bàn luận ngày **2026-08-07**. **Chưa viết code.**
> Câu hỏi: *"chỉ dùng OpenCV, không deep learning, thì làm bài toán ngoại quan thế nào?"*
> Neo vào tập ảnh thật `D:\Images_\SIBV\A38\Sut_me\{top,botton}` (63 + 64 ảnh, 3000×3000).

---

## Mục lục

1. [Nguyên lý gốc: đừng tìm lỗi, hãy đo độ lệch khỏi tham chiếu](#1-nguyên-lý-gốc)
2. [Bốn họ phương pháp, phân theo LOẠI THAM CHIẾU](#2-bốn-họ-phương-pháp-phân-theo-loại-tham-chiếu)
3. [Không gian biểu diễn — chỗ người mới quăng mất thông tin](#3-không-gian-biểu-diễn)
4. [Kiến trúc 6 tầng mà mọi hệ AOI cổ điển đều theo](#4-kiến-trúc-6-tầng)
5. [Kho nguyên thuỷ OpenCV: cái gì có sẵn mà ít ai dùng](#5-kho-nguyên-thuỷ-opencv)
6. [Thủ tục 5 câu hỏi để tự chọn phương pháp](#6-thủ-tục-5-câu-hỏi-để-tự-chọn-phương-pháp)
7. [Khi nào cổ điển THẮNG, khi nào THUA deep learning](#7-khi-nào-cổ-điển-thắng-khi-nào-thua)
8. [Áp vào bài `Sut_me` — 4 hướng cụ thể, xếp theo ưu tiên](#8-áp-vào-bài-sut_me)
9. [Những cái bẫy đã gặp, đừng đạp lại](#9-những-cái-bẫy-đã-gặp)

---

## 1. Nguyên lý gốc

Đây là câu trả lời ngắn nhất cho toàn bộ câu hỏi:

> **Deep learning học "lỗi trông như thế nào".**
> **OpenCV cổ điển dựng "hàng tốt trông như thế nào" rồi đo ĐỘ LỆCH.**

Hệ quả rất lớn, và nó quyết định mọi thiết kế:

| | DL | Cổ điển |
|---|---|---|
| Cần gì | **nhiều ảnh LỖI** đã gán nhãn | **ít ảnh TỐT** (đôi khi 1) |
| Ra cái gì | xác suất (0.87) | **con số có đơn vị** (1.34 mm) |
| Lỗi chưa từng thấy | thường bỏ sót | vẫn bắt (vì nó lệch khỏi tham chiếu) |
| Giải thích được | không | có — chỉ ra pixel nào, lệch bao nhiêu mm |
| Sập khi nào | đổi lô/đổi đèn | đổi **hình học**, đổi định vị |

Vì vậy câu hỏi thiết kế trung tâm **không phải** *"dùng thuật toán nào?"* mà là:

> ### **"Tham chiếu của tôi là gì, và tôi so với nó bằng đại lượng nào?"**

Trả lời được câu đó là xong 80% bài toán. Mục 2 là toàn bộ không gian câu trả lời.

---

## 2. Bốn họ phương pháp, phân theo LOẠI THAM CHIẾU

Đây là **bản đồ chính**. Mọi kỹ thuật AOI cổ điển đều rơi vào một trong bốn ô này.

### Họ A — Tham chiếu là **mô hình hình học lý tưởng**

Bạn biết trước cạnh đó *phải* là đường thẳng, lỗ đó *phải* là hình tròn.

```
đo điểm biên sub-pixel  →  fit (RANSAC)  →  residual của từng điểm  →  mm
```

- **Bất biến với:** độ sáng (dùng vị trí gradient), vị trí, góc xoay.
- **Cần fixture:** yếu — chỉ cần đủ để đặt ROI đúng chỗ.
- **Bắt được:** sai kích thước, cạnh không thẳng, sứt mẻ, méo, lệch tâm.
- **Không bắt được:** bẩn, xước, sai màu trên mặt phẳng lành.
- **Điểm mạnh nhất:** ra thẳng **mm**, truy vết được, **không cần ảnh mẫu nào cả**.
- **Điểm yếu:** chỉ dùng được ở chỗ hình học **đơn giản**. Góc bát giác có 6 chi tiết chồng nhau thì không fit được cái gì.

### Họ B — Tham chiếu là **chính con hàng đó** (đối xứng / lặp lại)

Kỹ thuật bị bỏ quên nhiều nhất, và với nhiều con hàng nó là **đòn mạnh nhất**.

```
con hàng có đối xứng xoay 90° (hoặc 180°, hoặc gương)
   →  xoay ảnh 90° rồi so với chính nó
   →  residual = chỗ PHÁ ĐỐI XỨNG = khuyết tật
```

- **Bất biến với:** ánh sáng, lô vật liệu, độ bóng, tuổi đèn, **biến thiên giữa các con hàng** —
  vì tử số và mẫu số là **cùng một con hàng, cùng một tấm ảnh, cùng một khoảnh khắc**.
- **Đây là lý do nó mạnh:** nó **triệt gần hết `σ_hàng_hợp_lệ`** và toàn bộ `σ_ánh_sáng`,
  hai thành phần thường chiếm phần lớn error budget.
- **Cần fixture:** chỉ cần **tâm xoay** chính xác — dễ hơn align với golden nhiều.
- **Cạm bẫy:** chỗ **cố tình bất đối xứng** (chữ dập nổi, mốc định hướng, một chân khác kiểu)
  sẽ báo lỗi ảo. Phải dựng **mask loại trừ** cho những chỗ đó — làm một lần, dùng mãi.
- **Biến thể:** con hàng có **N chi tiết giống nhau** (4 góc, 8 chân, 20 răng) → so **từng cái với
  median của N cái còn lại**. Robust theo đúng nghĩa thống kê: một cái lỗi không kéo được median.

> Với `Sut_me`, khung bát giác có đối xứng xoay 90° khá rõ. **Đây là hướng tôi xếp ưu tiên cao.**

### Họ C — Tham chiếu là **ảnh golden** (1 hoặc N mẫu tốt)

Mô thức kinh điển của AOI (`golden template comparison` / `difference imaging`).

```
align sub-pixel về hệ toạ độ chuẩn  →  trừ ảnh  →  so với tolerance band  →  vùng vượt = lỗi
```

Ba mức, và **khoảng cách giữa chúng là khoảng cách nghiệp dư / chuyên nghiệp**:

| Mức | Tham chiếu | Vấn đề |
|---|---|---|
| Thô | **1** ảnh golden, trừ trực tiếp | Biến thiên hợp lệ giữa các con hàng bị coi là lỗi ⇒ **lỗi ảo tràn** |
| Khá | **1** golden + ngưỡng toàn cục | Chỗ nào cũng dùng chung một ngưỡng, trong khi mép thì nhiễu cao, giữa mặt thì nhiễu thấp |
| **Đúng** | **N ≥ 30** mẫu tốt → tính `mean(x,y)` và `σ(x,y)` **cho từng pixel** → ngưỡng `mean ± k·σ(x,y)` | Chính là **tolerance band theo pixel**. Chỗ nào biến thiên tự nhiên nhiều thì tự động nới ngưỡng ra |

- **Bắt được:** **mọi thứ** — thiếu, thừa, lệch, bẩn, sai màu, ở **mọi vị trí**. Đây là ưu điểm độc nhất.
- **Điểm yếu chí tử:** **cực nhạy với sai lệch align.** Lệch 1 px ở chỗ có cạnh sắc sinh ra
  một dải residual sáng rực dọc toàn bộ cạnh đó ⇒ lỗi ảo. Quy tắc: **σ_align phải < 1/3 px.**
  Nếu chưa align được sub-pixel thì **đừng dùng họ C** — sẽ mất hàng tháng chỉnh ngưỡng vô ích.
- **Mẹo giảm nhạy align:** so trên **gradient magnitude** hoặc trên ảnh đã làm mờ nhẹ,
  hoặc dùng **so sánh có dung sai dịch chuyển** (mỗi pixel lấy min sai khác trong lân cận ±2 px).
  Kỹ thuật này (`shift-tolerant difference`) cứu được rất nhiều lỗi ảo mà ít ai biết.

### Họ D — Tham chiếu là **lân cận của chính pixel đó** (không cần golden)

Không cần biết hàng tốt trông thế nào; chỉ cần biết **"chỗ này khác hẳn xung quanh"**.

```
top-hat / black-hat  ·  local contrast  ·  local statistics (mean/σ trong cửa sổ)
thống kê texture  ·  Gabor / FFT cho texture định kỳ  ·  granulometry
```

- **Bất biến với:** biến dạng lớn, hình dạng không lặp lại, không cần align gì cả.
- **Bắt được:** đốm, xước, bẩn, rỗ — **khuyết tật cục bộ nhỏ** trên nền đồng nhất hoặc đều đặn.
- **Không bắt được:** sai **kích thước** (vì không có tham chiếu tuyệt đối), thiếu hẳn một chi tiết lớn.
- **Điểm yếu:** khó ra **mm**, dễ ăn nhiễu, và **không phân biệt được** khuyết tật với chi tiết
  thiết kế hợp lệ có cùng dáng.

> Pipeline hiện tại (`mask − opening` = top-hat) đang nằm ở **họ D**, và đang bị dùng để giải một
> bài toán vốn thuộc **họ A/B/C**. Đó là lý do phải chỉnh hằng số mãi.

### Bảng chọn nhanh

| Loại lỗi | Họ nên dùng |
|---|---|
| Sai kích thước, sai khoảng cách | **A** |
| Sứt mẻ ở biên, cạnh không thẳng | **A**, hoặc **B** nếu góc phức tạp |
| Thiếu / lệch một chi tiết | **C** |
| Bẩn, xước, đốm trên mặt lành | **D** |
| Sai màu, sai vật liệu | **C** (trên kênh màu) |
| Con hàng có đối xứng hoặc N chi tiết giống nhau | **B** — luôn thử trước |
| Không biết trước có lỗi gì | **C** với tolerance band |

**Và quan trọng nhất: một hệ AOI thật luôn dùng NHIỀU HỌ CÙNG LÚC**, mỗi họ một ROI, mỗi họ ra
một con số. Không có "một thuật toán bắt mọi lỗi".

---

## 3. Không gian biểu diễn

Chọn được họ rồi, câu hỏi tiếp: **so sánh trên đại lượng nào?** Đây là chỗ người mới quăng mất
thông tin ngay dòng đầu tiên — `CvtColor(BGR2GRAY)` là một **quyết định**, không phải thủ tục.

| Biểu diễn | Bất biến với | Dùng khi |
|---|---|---|
| **Gray** | — | mặc định, và thường là lựa chọn tệ nhất |
| **Kênh màu (HSV/LAB)** | độ sáng (nếu dùng H, hoặc a*b*) | vật liệu khác nhau: đồng vs nhựa, thiếc vs pad |
| **Gradient magnitude** | **offset độ sáng** | mọi bài toán về **biên**. Ổn định hơn gray rất nhiều |
| **Gradient orientation** | offset **và** gain | cực mạnh: hướng cạnh gần như miễn nhiễm ánh sáng. Nền tảng của shape-based matching |
| **Distance transform của mask** | — | biến **bề dày** thành **giá trị pixel**. Xem mục 8 |
| **Unwrap theo biên (polar / arc-length)** | hình dạng cong | biến biên cong thành **đường ngang** ⇒ mọi công cụ 1D dùng được. **Kỹ thuật bị đánh giá thấp nhất** |
| **Chiếu 1D (projection)** | nhiễu (SNR ~ √N) | đo vị trí cạnh, đếm chi tiết tuần hoàn |
| **FFT / Gabor** | pha, vị trí | texture **định kỳ** (lưới, sợi, răng) |
| **Granulometry** (opening với r tăng dần) | — | ra **phân bố kích thước** của các đốm ⇒ phân biệt nhiễu nhỏ với khuyết tật thật |
| **Skeleton + distance** | — | vật thể **mảnh, dài**: khung, dây, đường mạch |

### Hai cái đáng đào sâu

**a) Unwrap biên thành profile 1D**

```
contour (hoặc mask)  →  tham số hoá theo arc-length s
                     →  r(s) = khoảng cách từ điểm biên tới một trục tham chiếu
                     →  được MỘT tín hiệu 1D
```

Sau bước đó, sứt mẻ = **một cái hõm (dip) trong tín hiệu 1D**. Và trong miền 1D bạn có cả một
bộ công cụ mạnh hơn hẳn miền 2D:
- **median filter** trên `r(s)` → được đường "lành" mà **khuyết tật không kéo lệch được**
  (khác hoàn toàn least-squares, xem mục 9)
- `r(s) − median(r(s))` → **residual có dấu**: âm = mất vật liệu, dương = thừa/bavia
- **matched filter**: nếu biết dáng của một vết sứt điển hình, tương quan với nó → SNR tối ưu
- độ sâu và độ rộng của dip → ra **mm** trực tiếp

`Cv2.WarpPolar` làm việc này cho hình gần tròn. Với khung bát giác thì unwrap theo
**arc-length của contour** đúng hơn là polar quanh tâm.

**b) Distance transform = bản đồ bề dày**

Với vật thể **mảnh** như cái khung này:

```
mask nhị phân  →  distanceTransform  →  mỗi pixel = khoảng cách tới biên gần nhất
             →  trên trục giữa (skeleton), giá trị đó = NỬA BỀ DÀY của thành khung tại đó
```

Sứt mẻ = **thành khung mỏng đi cục bộ** ⇒ một dip trên profile bề dày dọc theo skeleton.
Đẹp ở chỗ: **không cần ROI, không cần align, không cần golden**, và ra ngay mm.
`Cv2.DistanceTransform` + `Cv2.Thinning` (ximgproc) là đủ.

---

## 4. Kiến trúc 6 tầng

Mọi hệ AOI cổ điển chạy được ở production đều có đúng 6 tầng này, theo đúng thứ tự:

| Tầng | Việc | Ra cái gì |
|---|---|---|
| **1. Chuẩn hoá ảnh** | flat-field (chia ảnh nền trắng), white balance, khử nhiễu | ảnh không còn gradient sáng của đèn |
| **2. Định vị** | coarse (`MinAreaRect`/`matchTemplate`) → fine (`ECC`/`phaseCorrelate`) | `(dx, dy, θ)` **sub-pixel** |
| **3. Chuẩn hoá hình học** | `WarpAffine` **một lần** về hệ toạ độ part | ảnh trong hệ chuẩn |
| **4. Bảng ROI** | ROI **định nghĩa một lần, cố định vĩnh viễn** trong hệ chuẩn | danh sách (ROI, phép đo, dung sai) |
| **5. Đo** | mỗi ROI một phép đo thuộc họ A/B/C/D | **một con số có đơn vị mm** |
| **6. Quyết định** | so số đó với dung sai lấy từ **phân bố** | OK/NG + lý do truy vết được |

Ba luật sắt:

1. **Tầng 5 phải ra SỐ, không ra boolean.** Ra boolean là mất thông tin, mất histogram,
   mất khả năng chọn điểm vận hành. Đây là luật quan trọng nhất trong cả tài liệu.
2. **Tầng 2 không được dùng chính thứ ở tầng 5 làm mốc.** Lấy góc từ toàn silhouette rồi đo
   khuyết tật trên silhouette = hệ quy chiếu bị nhiễm bởi thứ cần đo.
3. **Tầng 3 warp đúng MỘT lần.** Warp hai lần = nội suy hai lần = cạnh bị làm mềm hai lần.

---

## 5. Kho nguyên thuỷ OpenCV

Những thứ **có sẵn** trong OpenCvSharp mà hiếm ai dùng, nhưng chính là đáp án của nghề:

**Định vị / align**

| API | Vai trò |
|---|---|
| `Cv2.MatchTemplate` + `TemplateMatchModes.CCoeffNormed` | ZNCC — bất biến với offset **và** gain độ sáng |
| `Cv2.FindTransformECC` (`MotionTypes.Euclidean`) | align **sub-pixel**, đáp án của OpenCV cho fixture tinh |
| `Cv2.PhaseCorrelate` | dịch chuyển sub-pixel bằng FFT — **rất nhanh**, tốt cho coarse |
| `Cv2.EstimateAffinePartial2D` + RANSAC | align từ tập điểm tương ứng |
| `Cv2.CreateGeneralizedHoughGuil` | **shape-based matching** bất biến xoay + tỉ lệ — bản OpenCV của công cụ Halcon nổi tiếng |

**Đo hình học**

| API | Vai trò |
|---|---|
| `Cv2.Sobel`/`Scharr` + `Cv2.CartToPolar` | gradient magnitude **và orientation** |
| `Cv2.FitLine` (`DistanceTypes.Huber`/`L1`) | fit **robust** — không phải least-squares |
| `Cv2.FitEllipse`, `Cv2.MinEnclosingCircle` | fit lỗ, cung |
| `Cv2.ConvexHull` + `Cv2.ConvexityDefects` | **tìm hõm trên biên** — gần như sinh ra cho bài sứt mẻ |
| `Cv2.PointPolygonTest` | khoảng cách **có dấu** từ điểm tới contour ⇒ residual ra ngay |
| `Cv2.DistanceTransform` | bản đồ bề dày |
| `Cv2.WarpPolar` / `LinearPolar` | unwrap |
| `Cv2.Thinning` (ximgproc) | skeleton |
| `Cv2.FastLineDetector` (ximgproc) | dò đoạn thẳng, ổn định hơn `HoughLinesP` |

**So sánh / phân đoạn**

| API | Vai trò |
|---|---|
| `Cv2.MorphologyEx` + `TopHat`/`BlackHat`/`Gradient` | khuyết tật sáng / tối cục bộ |
| `Cv2.CreateCLAHE` | cân bằng tương phản **cục bộ** |
| `Cv2.NiBlackThreshold` (ximgproc) | ngưỡng thích ứng theo mean/σ cục bộ |
| `Cv2.ConnectedComponentsWithStats` | đo từng đốm: diện tích, bbox, trọng tâm — một lần gọi |
| `Cv2.MatchShapes` / `Cv2.HuMoments` | so hình dạng, bất biến xoay + tỉ lệ |
| `Cv2.CalcHist` + `Cv2.CompareHist` | so phân bố cường độ trong ROI |
| `Cv2.Filter2D` | **matched filter** — tự thiết kế nhân theo dáng khuyết tật |
| `Cv2.Bm3dDenoising` (xphoto) | khử nhiễu **giữ cạnh**, khi nhiễu cảm biến là nút cổ chai |
| `Cv2.GrabCut`, `Cv2.Watershed` | tách vật dính nhau |

> **Không có sẵn, phải tự viết (~50 dòng):** **caliper tool** — ROI nhỏ → chiếu 1D →
> đạo hàm → nội suy parabol quanh đỉnh gradient → vị trí cạnh **~1/10 px**.
> Đây là công cụ số một của nghề và OpenCV **không có**. Cognex gọi *Caliper*,
> Halcon gọi *metrology model*.
>
> **Lưu ý loại trừ:** `ximgproc.StructuredEdgeDetection` dùng **random forest đã huấn luyện** ⇒
> đó là machine learning, nằm ngoài phạm vi "không DL". `GrabCut` dùng GMM nhưng fit tại chỗ,
> không có model huấn luyện trước ⇒ vẫn tính là cổ điển.

---

## 6. Thủ tục 5 câu hỏi để tự chọn phương pháp

Đây là phần **để bạn tự sinh hướng**, thay vì tra bảng. Chạy trên giấy, 20 phút:

1. **Đại lượng vật lý nào phân biệt tốt/lỗi?**
   (thiếu vật liệu? sai vị trí? sai bề dày? nhiễm bẩn? sai vật liệu?)
2. **Nó biểu hiện ra ảnh thành gì?**
   (vị trí biên dịch / độ sáng đổi / hình dạng highlight đổi / texture đổi / bóng đổ đổi)
3. **Tôi có tham chiếu nào?** → chọn họ A/B/C/D ở mục 2.
   *Luôn hỏi trước: con hàng này có đối xứng hay có N chi tiết giống nhau không? (họ B)*
4. **Cái gì KHÁC cũng gây ra biểu hiện ở bước 2?** → danh sách nuisance.
   *Bước bị bỏ qua nhiều nhất, và là bước sinh ra toàn bộ giá trị.*
5. **Biểu diễn nào bất biến với nuisance đó mà vẫn giữ tín hiệu bước 2?** → tra bảng mục 3.

Rồi kiểm tra khả thi **trước khi code**:
- px/mm có đủ? (chi tiết nhỏ nhất ≥ 10 px)
- `σ_align` cần bao nhiêu? Họ C đòi < 1/3 px, họ A/B/D dễ hơn nhiều.
- tổng `3σ` của mọi nguồn nhiễu có < 1/3 dung sai không?

---

## 7. Khi nào cổ điển THẮNG, khi nào THUA

Nói thẳng, không bênh vực bên nào:

**Cổ điển THẮNG rõ ràng khi:**
- hình dạng con hàng **lặp lại** (hàng công nghiệp, khuôn cứng)
- đèn và cơ khí **kiểm soát được**
- lỗi định nghĩa bằng **hình học hoặc cường độ đo được**
- cần **giải thích** với khách hàng: *"lõm 1.34 mm, spec 1.0"*
- ít mẫu lỗi (thường là thực tế — hàng lỗi vốn hiếm)
- cần chạy nhanh, cần deterministic, cần validate được

**Cổ điển THUA khi:**
- bề mặt có **texture tự nhiên biến thiên**: gỗ, da, vải, đá, mối hàn tay
- lỗi định nghĩa bằng **cảm nhận thẩm mỹ** của người kiểm ("nhìn thấy rõ là xấu")
- con hàng **biến dạng mềm** (dây, vải, thực phẩm) — không có hệ quy chiếu ổn định
- **nhiều chục loại lỗi** chưa liệt kê được hết
- cần bắt lỗi mà chính người vận hành cũng không mô tả được bằng số

> **Với `Sut_me`:** khung cứng, hình lặp lại rất tốt (xem 6 ảnh mẫu — gần như trùng khít nhau),
> đèn kiểm soát, lỗi = **mất vật liệu ở biên** ⇒ đo được bằng mm.
> **Cổ điển thắng rõ ràng. Không cần deep learning cho bài này**, và dùng DL ở đây còn tệ hơn:
> mất khả năng ra số mm, mất truy vết, và tập ảnh lỗi thì hiếm.

Và trường hợp lai đáng biết: khi thật sự cần "học", thứ đúng để dùng **không phải** phân loại
có nhãn mà là **anomaly detection chỉ học ảnh TỐT** (PaDiM/PatchCore). Nó giữ được tinh thần
"tham chiếu = hàng tốt" của mục 1. Nhưng đó là **primitive thứ 16**, không phải cái thay thế 15 cái kia.

---

## 8. Áp vào bài `Sut_me`

### Nhận định từ ảnh (chưa đo, cần xác nhận bằng số)

Nhìn 6 ảnh mẫu `top` và `botton`:

- Ảnh **3000×3000**, khung chiếm gần trọn chiều ngang ⇒ px/mm khá cao.
- **Vị trí và hướng lặp lại rất tốt** giữa các ảnh ⇒ điều kiện tốt cho họ B và C.
- Khung là **vật thể MẢNH**: thành khung hẹp, phần giữa là lỗ lớn ⇒ **distance transform +
  skeleton là công cụ rất đúng** (mục 3b).
- Khung có **đối xứng xoay 90°** khá rõ (nhất là `botton` với 4 vòng tròn ở 4 góc)
  ⇒ **họ B khả dụng**.
- ⚠️ **Nền trắng nhưng con hàng SÁNG (xám nhạt), không phải TỐI.**
  Nếu là backlight thuần thì khung phải **đen tuyệt đối**. Vậy đang là **đèn trước trên nền sáng**.
  ⇒ tương phản vật/nền **thấp hơn** backlight ⇒ phân đoạn silhouette **kém ổn định hơn** tưởng.
  **Việc phải làm đầu tiên: đo `|mean(vật) − mean(nền)| / σ`.** Nếu số này thấp thì
  **tắt đèn trước, bật backlight thuần** sẽ biến bài toán biên thành gần như tầm thường —
  và đó là thay đổi rẻ hơn mọi thuật toán.

### Bốn hướng, xếp theo tỉ lệ (giá trị / công sức)

**Hướng 1 — Profile bề dày qua distance transform** *(họ A, không cần golden, không cần align)*

```
mask  →  distanceTransform  →  skeleton (Thinning)  →  lấy giá trị dt dọc skeleton
      →  profile bề dày theo chiều dài khung  →  median filter  →  dip = sứt mẻ  →  mm
```
Ưu: không cần ảnh mẫu, không cần align, ra mm ngay, bất biến với vị trí và góc xoay.
Nhược: sứt ở **góc** (chỗ skeleton phân nhánh) sẽ khó đọc — cần xử lý riêng vùng góc.
**Đây là hướng tôi làm trước.** Nó rẻ nhất và cho ngay một cột số để dựng histogram.

**Hướng 2 — Unwrap biên thành profile 1D + median/RANSAC** *(họ A)*

```
contour ngoài  →  tham số hoá arc-length  →  r(s)  →  median filter  →  r(s) − median
              →  residual có dấu: âm = mất vật liệu
```
Ưu: bắt trực tiếp đúng định nghĩa "sứt mẻ ở biên", ra mm, dùng được cả biên ngoài và biên trong.
Nhược: phải xử lý chỗ biên đổi hướng đột ngột (8 góc bát giác) — dùng median cửa sổ ngắn ở đó.
Bổ trợ rẻ: `ConvexityDefects` cho một danh sách hõm ứng viên gần như miễn phí.

**Hướng 3 — Tự đối xứng 90°** *(họ B)*

```
tìm tâm + góc  →  xoay ảnh 90°/180°/270°  →  so 4 phiên bản với nhau (lấy median)
              →  residual = chỗ phá đối xứng
```
Ưu: **triệt gần hết biến thiên đèn và biến thiên giữa các con hàng** — đúng thứ làm `d'` tăng.
Bắt được cả lỗi ở vùng góc phức tạp mà hướng 1/2 đọc khó.
Nhược: phải dựng **mask loại trừ** cho các chi tiết cố tình bất đối xứng (chữ dập nổi, mốc).
Làm một lần, dùng mãi. Cần tâm xoay chính xác — nhưng dễ hơn align golden.

**Hướng 4 — Golden + tolerance band theo pixel** *(họ C)*

```
30+ ảnh tốt  →  align sub-pixel (ECC)  →  mean(x,y), σ(x,y)  →  ngưỡng mean ± k·σ(x,y)
```
Ưu: bắt **mọi loại lỗi ở mọi chỗ**, kể cả lỗi chưa nghĩ tới.
Nhược: **đòi `σ_align` < 1/3 px** và đòi có tập hàng tốt đã xác nhận. Làm sau cùng.
Mẹo bắt buộc: so trên **gradient magnitude** và dùng **so sánh có dung sai dịch chuyển ±2 px**,
nếu không sẽ lỗi ảo dọc mọi cạnh sắc.

### Việc chặn tất cả

1. **Đo tương phản vật/nền** (nhận định ⚠️ ở trên). Quyết định có đổi đèn hay không.
2. **Đâu là nhóm OK?** `top`/`botton` là **hai mặt chụp**, không phải OK/NG.
   Không có nhóm tốt thì **không tính được `d'`**, mà `d'` là thứ duy nhất quyết định "ít lỗi ảo".
3. **px/mm** — một kích thước thật của khung theo mm.

---

## 9. Những cái bẫy đã gặp, đừng đạp lại

Tổng hợp từ hai buổi trước, tất cả đều là bẫy **có thật, đã xảy ra trong repo này**:

| Bẫy | Vì sao chết | Cách đúng |
|---|---|---|
| `ApproxPolyDP` để lấy 4 đỉnh rồi warp | ApproxPolyDP **làm mượt biên**, đối nghịch trực tiếp với mục tiêu **giữ chỗ lồi/hõm** | Lấy góc bằng `MinAreaRect` hoặc fit 2 cạnh dài |
| Homography (`WarpToRect`) khi chỉ cần **xoay** | 8 tham số thay vì 1, kéo giãn **phi tuyến** ⇒ khuyết tật 1 mm bị méo khác nhau ở mỗi chỗ | `GetRotationMatrix2D` + `WarpAffine` |
| Least-squares fit lên điểm biên | Chính khuyết tật **kéo lệch** đường fit ⇒ residual ≈ 0 ⇒ **không thấy gì** | **RANSAC / Huber / median** — coi khuyết tật là outlier |
| Đo trên **mask nhị phân** | Lượng tử hoá ±0.5 px, nhiễu ngang tầm khuyết tật cần đo | Giữ **grayscale**, nội suy sub-pixel |
| Ngưỡng tuyệt đối (`Canny 50/150`) | Đổi đèn / đổi lô là sập | Dùng **vị trí gradient**, hoặc ngưỡng tính từ phân bố ảnh đó |
| Morph kernel lớn (`Close 30×30`, `Ellipse 60×60`) trước khi đo | **Bo tròn hết** góc và xoá luôn khuyết tật cần bắt | Morph chỉ dùng ở tầng **định vị**, tuyệt đối không trước tầng **đo** |
| Đặc trưng **toàn cục** không có ROI | Gộp cả biến thiên hợp lệ vào phương sai ⇒ `d'` sập xuống < 1 | Neo ROI vào fixture rồi mới đo |
| ROI không khớp thứ muốn đo | `dilate` ăn cả nền; `erode` xoá mất chính chi tiết mảnh | Luôn hỏi: *"tập pixel này có chứa đúng thứ tôi muốn đo?"* |
| Tin một `d'` đẹp | `d' = 11.69` với **n = 1** là hiện vật thống kê | Xem **n** trước khi xem `d'` |
| Thêm `if`/ngưỡng mà không có histogram | Vòng lặp mò ngưỡng vô hạn | **Không hằng số nào vào code mà không có histogram đi kèm** |

---

*Tài liệu bàn luận, **chưa viết dòng code nào**.*
*Xem thêm: [`DO_LUONG_2026-08-06.md`](DO_LUONG_2026-08-06.md) ·
[`LO_TRINH_NEN_1_NAM.md`](LO_TRINH_NEN_1_NAM.md) ·
[`TU_DUY_MACHINE_VISION.md`](TU_DUY_MACHINE_VISION.md)*
