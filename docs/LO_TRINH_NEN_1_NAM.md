# Lộ trình nén: từ "10 năm" xuống 1 năm

> Buổi bàn luận ngày **2026-08-06**, tiếp nối `TU_DUY_MACHINE_VISION.md` (2026-08-05).
> Mục tiêu người dùng phát biểu: *"1 năm phải master, giải được mọi bài toán, chạy ổn định
> ít bắt lỗi ảo, và tự tư duy ra hướng mới."*
> Vẫn **chưa viết dòng code nào**.

---

## Mục lục

1. [Phản biện phát biểu mục tiêu](#1-phản-biện-phát-biểu-mục-tiêu)
2. [Không có thuật toán vạn năng — có thư viện phép đo](#2-không-có-thuật-toán-vạn-năng--có-thư-viện-phép-đo)
3. ["10 năm" kia thật ra gồm những gì](#3-10-năm-kia-thật-ra-gồm-những-gì)
4. ["Ít bắt lỗi ảo" là một đại lượng đo được](#4-ít-bắt-lỗi-ảo-là-một-đại-lượng-đo-được)
5. [Error budget — công cụ nén nhiều năm nhất](#5-error-budget--công-cụ-nén-nhiều-năm-nhất)
6. [Khung sinh hướng mới: nuisance → invariant](#6-khung-sinh-hướng-mới-nuisance--invariant)
7. [Hạ tầng tháng 1: cái quyết định 1 năm hay 10 năm](#7-hạ-tầng-tháng-1-cái-quyết-định-1-năm-hay-10-năm)
8. [Lộ trình 12 tháng](#8-lộ-trình-12-tháng)
9. [12 mốc tự kiểm tra](#9-12-mốc-tự-kiểm-tra)
10. [Điều kiện thất bại](#10-điều-kiện-thất-bại)

---

## 1. Phản biện phát biểu mục tiêu

Câu phát biểu gộp 4 mệnh đề rất khác nhau về độ khả thi. Tách ra mới làm được:

| Mệnh đề | Khả thi trong 1 năm? | Ghi chú |
|---|---|---|
| "Giải được **mọi** bài toán vision" | **Không.** Và người 10 năm cũng không. | Xem mục 2 — mục tiêu này phát biểu sai, không phải quá cao |
| "Master AOI 2D trong nhà máy điện tử" | **Có**, nếu đủ 8-10h/tuần **có đo lường** | Đây là mệnh đề nên nhắm |
| "Chạy ổn định, ít bắt lỗi ảo" | **Có, và đây là phần dễ nhất** | Là kỹ năng **đo**, không phải kỹ năng kinh nghiệm — mục 4 |
| "Tự tư duy ra hướng mới" | **Có** | Có khung sinh hướng, không cần chờ cảm hứng — mục 6 |

Nghịch lý đáng chú ý: **cái bạn tưởng khó nhất (ổn định, ít lỗi ảo) lại là cái có công thức rõ nhất.**
Còn cái bạn tưởng là đích đến ("mọi bài toán") thì lại là phát biểu sai đề.

---

## 2. Không có thuật toán vạn năng — có thư viện phép đo

Chính cấu trúc thư mục ảnh đã nói ra điều này. Có **4 nhóm lỗi riêng biệt**:

```
Sut_mat  (202)   Thieu_thiec (134)   Quan_Coi_NG (103)   Quan_Boss_pin (64)
```

Đây không phải 4 biến thể của một bài toán. Đây là **4 failure mode**, và trong AOI cổ điển,
nguyên tắc là:

> **Mỗi failure mode = một phép đo riêng, một ROI riêng, có thể một kiểu đèn riêng.**

Thử phân tích 2 cái đầu để thấy chúng **không cùng bản chất**:

| | `Sut_mat` / sứt mẻ ở phần nhô | `Thieu_thiec` |
|---|---|---|
| Đại lượng vật lý | Thiếu vật liệu **ở biên** | Thiếu vật liệu **trên mặt**, tại vị trí mối hàn |
| Biểu hiện trong ảnh | Biên lệch khỏi đường thẳng lý tưởng | Vùng phản xạ đổi độ sáng / đổi hình dạng highlight |
| Loại bài toán | **Đo lường hình học** (metrology) | **So sánh cường độ / diện tích** (photometric) |
| Đèn đúng | **Backlight** — biến thành bóng đen tuyệt đối | **Coaxial / dome** — làm phản xạ đều, triệt highlight ngẫu nhiên |
| Công cụ đúng | Caliper sub-pixel + RANSAC | Golden template + tolerance band từ N mẫu tốt |
| Backlight có giúp `Thieu_thiec`? | — | **Không.** Backlight xoá sạch thông tin bề mặt |

**Đây là nguyên nhân gốc của triệu chứng "đổi thư mục là phải sửa hằng số"** đã ghi ở
`TU_DUY_MACHINE_VISION.md` mục 1. Không phải vì ngưỡng chưa đủ khéo. Vì **một chuỗi đang bị
bắt gánh nhiều bài toán có bản chất khác nhau**. Không tồn tại bộ hằng số nào đúng cho cả bốn.

Suy ra định nghĩa lại "master":

> Không phải *"biết một thuật toán giải mọi bài toán"*.
> Mà là *"có ~15 phép đo nguyên thuỷ (primitive) trong tay, và **nhìn một failure mode là biết
> chọn primitive nào + đèn nào**"*.

15 primitive đó là **hữu hạn và học được trong 1 năm**. Đó là lý do mệnh đề "1 năm" khả thi —
không phải vì bạn học nhanh gấp 10, mà vì **tập cần học nhỏ hơn bạn tưởng rất nhiều**.

Danh sách primitive (học hết là đủ cho ~90% AOI 2D):

**Định vị:** ZNCC template match · shape-based match · ECC align · fixture/part coordinate system
**Đo hình học:** caliper sub-pixel · RANSAC line/circle fit · blob analysis với moment · khoảng cách/góc giữa các đối tượng
**So sánh cường độ:** golden template diff · tolerance band từ N mẫu · top-hat / high-pass · flat-field correction
**Bề mặt:** thống kê texture · multi-light composite
**Quyết định:** phân bố + ROC + operating point

---

## 3. "10 năm" kia thật ra gồm những gì

Tại sao nghề này thường mất 10 năm? Bóc ra thì phần lớn **không phải thời gian học**:

| Thành phần của 10 năm | Có nén được? |
|---|---|
| Chờ project mới để va vào bài toán mới (~15-25 project × 4-6 tháng) | **Nén được** — bạn có 5723 ảnh sẵn, 4 failure mode sẵn |
| Chờ line dừng / chờ lô hàng lỗi xuất hiện để có mẫu NG | **Nén được** — 503 ảnh NG đã phân loại. Nhiều kỹ sư đợi 2 năm mới có |
| Mò ngưỡng bằng cách nhìn từng ảnh, mỗi ý tưởng mất 3 ngày | **Nén được, nhiều nhất** — mục 7 |
| Không biết tầng nào tồn tại (caliper, ECC, error budget) | **Nén được** — chỉ là thông tin, đọc là biết |
| Trực giác quang học: tay đổi đèn, mắt thấy ảnh đổi | **KHÔNG nén được bằng đọc.** Phải tự tay làm — mục 8, tháng 4 |
| Cảm giác về drift nhà máy: bụi, đèn già, lô vật liệu mới, công nhân đặt hàng khác đi | **KHÔNG nén được.** Chỉ thời gian dạy | 

Kết luận thẳng:

> ~**7 trong 10 năm** đó là **thời gian chờ va chạm**, không phải thời gian học.
> Bạn nén được phần đó. **Phần không nén được là quang học và drift** — nên trong 1 năm phải
> **chủ động tạo va chạm** với hai thứ đó, chứ không đợi nó tới.

Điều này cũng nói rõ giới hạn: sau 1 năm bạn sẽ **giải bài toán tốt hơn 90% người mới**, nhưng
**vẫn sẽ bị bất ngờ** bởi cái mà chỉ 3 năm chạy production dạy được — hệ thống chạy tốt 6 tháng
rồi bỗng bắt lỗi ảo vì mùa hè xưởng nóng hơn. Cách bù: **mục 4 + monitor drift** biến chuyện đó
từ "bất ngờ" thành "cái alarm đã dựng sẵn".

---

## 4. "Ít bắt lỗi ảo" là một đại lượng đo được

Đây là mục quan trọng nhất của tài liệu này.

**Sai lầm phổ biến:** coi "ít bắt lỗi ảo" là kết quả của việc *viết thuật toán giỏi hơn*.
Nó không phải. Nó là **hệ quả số học** của một đại lượng duy nhất: **độ tách rời của phân bố**.

### 4.1. Đại lượng đó: `d'` (separation)

Với một đặc trưng đo được (ví dụ: độ lệch max khỏi đường fit, tính bằng mm):

```
d' = |μ_tốt − μ_lỗi| / sqrt( (σ_tốt² + σ_lỗi²) / 2 )
```

Nếu đặt ngưỡng ở giữa, tỉ lệ báo nhầm mỗi phía ≈ `Φ(−d'/2)`:

| d' | Tỉ lệ lỗi ảo lý thuyết | Dùng được ở production? |
|---|---|---|
| 2 | 16% | Vô vọng |
| 4 | 2.3% | Không |
| 6 | 0.13% (1300 ppm) | Vẫn còn cao |
| 8 | 32 ppm | Bắt đầu được |
| **≥ 10** | **0.3 ppm** | **Mục tiêu** |

Vì phân bố thật **có đuôi dày hơn Gaussian**, và vì bỏ sót hàng lỗi thường là ràng buộc **cứng
(escape = 0)** nên ngưỡng phải đặt **lệch sát nhóm tốt** thay vì ở giữa — cả hai lý do đều
đòi `d'` phải lớn hơn con số lý thuyết. **Nhắm d' ≥ 10.**

### 4.2. Hệ quả về cách làm việc — chỗ đảo ngược trực giác

> Khi hệ thống bắt lỗi ảo, **99% trường hợp không phải do ngưỡng sai.**
> Do **đặc trưng có `d'` quá thấp.** Và **không tồn tại ngưỡng nào cứu được `d'` thấp.**

Vậy nên chuỗi hành động đúng khi thấy lỗi ảo:

```
Lỗi ảo xuất hiện
   → đo d' của đặc trưng đang dùng        (KHÔNG phải: chỉnh ngưỡng)
   → d' < 6 ?
        → có: đổi ĐẶC TRƯNG hoặc đổi ĐÈN. Chỉnh ngưỡng là vô nghĩa.
        → không: xem σ_tốt gồm những gì → mục 5 (error budget)
```

Đây chính là điều tách người 1 năm khỏi người 10 năm mà **vẫn** bắt lỗi ảo: người 10 năm không
vẽ histogram thì vẫn mãi mò ngưỡng. **Kỷ luật này nén được nhiều năm nhất trong cả tài liệu.**

### 4.3. Kỷ luật cụ thể, áp dụng từ hôm nay

> **Không được thêm một `if` hay một hằng số ngưỡng nào vào code mà không có histogram đi kèm.**

Nếu chưa vẽ được histogram cho một quyết định, thì quyết định đó **chưa được phép tồn tại trong
code**. Nghe cực đoan, nhưng nó là thứ duy nhất chặn được vòng lặp "mò ngưỡng vô hạn" — vòng lặp
đã ăn của bạn mấy commit vừa rồi (`ebdd91e`, `35698bf`, `ce0e991`).

---

## 5. Error budget — công cụ nén nhiều năm nhất

Mục 4 nói `σ_tốt` quyết định `d'`. Nhưng `σ_tốt` **không phải một khối** — nó là tổng của nhiều
nguồn độc lập:

```
σ²_tổng = σ²_nhiễu_sensor + σ²_định_vị + σ²_ánh_sáng + σ²_hàng_hợp_lệ + σ²_drift
```

**Muốn giảm `σ_tổng` phải biết thành phần nào đang lớn nhất.** Đây là tư duy của kỹ sư đo lường,
và nó là lý do người mới cải tiến sai chỗ 3 tuần liền: tối ưu thuật toán trong khi 80% phương
sai đến từ cơ khí đặt hàng không lặp lại.

### Cách đo từng thành phần — làm được trong 1 ngày

| Thí nghiệm | Cách làm | Đo ra thành phần nào |
|---|---|---|
| **A. Nhiễu thuần** | 1 con hàng, **không chạm vào**, chụp 30 ảnh, chạy pipeline | `σ_nhiễu_sensor` |
| **B. Lặp lại đặt hàng** | Cùng con hàng, **tháo ra đặt lại** 30 lần | A + `σ_định_vị` |
| **C. Biến thiên hàng** | 30 con hàng **tốt khác nhau** | B + `σ_hàng_hợp_lệ` |
| **D. Drift** | Lặp A sau 8 giờ / hôm sau / sau khi bật tắt đèn | `σ_drift` |

Trừ ngược ra từng thành phần (`σ²_định_vị = σ²_B − σ²_A`, v.v.). Đây là **Gauge R&R rút gọn**.

Cái bảng đó nói thẳng cho biết nên sửa gì:

| Thành phần lớn nhất | Việc phải làm |
|---|---|
| `σ_nhiễu_sensor` | Tăng sáng / tăng exposure / trung bình nhiều frame / chiếu nhiều dòng (SNR ~ √N) |
| `σ_định_vị` | Fixture tốt hơn: ECC sub-pixel, hoặc **sửa cơ khí** — không sửa thuật toán đo |
| `σ_ánh_sáng` | Đổi đèn, che sáng môi trường, flat-field correction |
| `σ_hàng_hợp_lệ` | Đặc trưng đang bắt cả biến thiên hợp lệ ⇒ **đổi đặc trưng**, hoặc tolerance band theo pixel |
| `σ_drift` | Ngưỡng tính từ phân bố + SPC chart giám sát, tự cảnh báo khi phân bố dịch |

> **Quy tắc phân bổ:** khuyết tật cần bắt là 1mm ⇒ tổng `3σ` của mọi nguồn nhiễu phải **< 0.3mm**.
> Nếu chỉ riêng jitter định vị đã 0.4mm thì bài toán **đã hỏng trước khi bàn tới thuật toán đo.**

---

## 6. Khung sinh hướng mới: nuisance → invariant

Trả lời cho *"tôi có thể tư duy ra hướng mới"*. Hướng mới **không đến từ cảm hứng** — nó đến từ
một thủ tục 4 bước, chạy được trên giấy trong 20 phút.

### 6.1. Bốn câu hỏi

1. **Đại lượng vật lý nào** thật sự phân biệt hàng tốt / hàng lỗi? (thiếu vật liệu? sai vị trí?
   sai độ dày? nhiễm bẩn?)
2. Đại lượng đó **biểu hiện thành gì trong ảnh**? (vị trí biên / độ sáng / hình dạng highlight /
   texture / bóng đổ)
3. **Cái gì khác cũng gây ra biểu hiện đó** mà không phải lỗi? ⇒ đây là danh sách **nuisance**.
4. Có **phép biến đổi nào bất biến với nuisance** mà **vẫn giữ** tín hiệu ở bước 2? ⇒ tra bảng 6.2.

Bước 3 là bước người mới bỏ qua, và là bước sinh ra toàn bộ giá trị.

### 6.2. Bảng tra: cái gì biến thiên → công cụ bất biến với nó

| Nuisance đang biến thiên | Đừng dùng | Dùng |
|---|---|---|
| Độ sáng tổng thể (offset) | Ngưỡng tuyệt đối, `Canny 150/210` cố định | **Vị trí** gradient, ZNCC, ngưỡng theo phân bố trong ROI |
| Gain / contrast (scale) | Giá trị pixel thô | ZNCC, chuẩn hoá `(x−μ)/σ` trong ROI |
| Gradient sáng nền, bóng đổ | Ngưỡng toàn ảnh | High-pass, **top-hat**, flat-field (chia ảnh nền trắng) |
| Phản quang / specular ngẫu nhiên | Ảnh đơn | **Multi-light**: 4 hướng đèn → composite min/max; photometric stereo; phân cực chéo |
| Vị trí + góc xoay | ROI cố định theo toạ độ pixel | **Fixture**: ZNCC/shape match → ECC → ROI trong hệ toạ độ part |
| Scale (khoảng cách camera) | — | Ống kính **telecentric**; hoặc match đa tầng pyramid |
| Nhiễu ngẫu nhiên | 1 pixel, 1 dòng | **Chiếu N dòng** (SNR ~ √N), trung bình nhiều frame |
| Biến thiên hợp lệ giữa các con hàng | **1** ảnh golden | **Tolerance band** từ N ≥ 30 mẫu tốt: `mean ± kσ` **theo từng pixel** |
| Chính khuyết tật kéo lệch mô hình | Least-squares | **RANSAC**, Huber, trimmed fit |
| Lượng tử hoá nhị phân ±0.5px | mask nhị phân → đếm pixel | Giữ **grayscale**, nội suy parabol → sub-pixel |
| Drift theo thời gian / theo lô | Hằng số hard-code | Ngưỡng từ phân bố + **SPC chart** giám sát |
| Hệ quy chiếu nhiễm bởi thứ mình đo | Lấy góc từ toàn silhouette | Lấy từ **fiducial không bao giờ lỗi** |

### 6.3. Chạy thử khung này trên `Thieu_thiec` — để chứng minh nó sinh ra hướng

1. **Đại lượng vật lý:** thiếu khối lượng thiếc ở mối hàn.
2. **Biểu hiện:** thiếc đủ → bề mặt cong, phản xạ theo một dạng highlight đặc trưng. Thiếu →
   diện tích và hình dạng highlight khác, có thể lộ nền pad.
3. **Nuisance:** ① góc nghiêng con hàng đổi hướng phản xạ; ② độ bóng bề mặt khác nhau giữa các lô;
   ③ sáng môi trường; ④ vị trí mối hàn xê dịch.
4. **Tra bảng:**
   - ① + ② là specular ⇒ **multi-light 4 hướng + composite**, hoặc **dome/coaxial** để triệt
     tính định hướng của phản xạ. ⇒ *Đây là một hướng hoàn toàn mới, không có trong pipeline hiện tại.*
   - ③ ⇒ flat-field + ZNCC thay vì ngưỡng tuyệt đối.
   - ④ ⇒ fixture, ROI trong hệ part.
   - Biến thiên hợp lệ ⇒ tolerance band từ ≥30 mẫu tốt, không phải 1 golden.

Đối chiếu: `Sut_mat` chạy qua đúng khung này lại ra **backlight + caliper + RANSAC** —
**không giao nhau** với kết luận trên. Xác nhận lại mục 2.

> Khung này là thứ thay thế "10 năm trực giác". Nó chậm hơn trực giác thật, nhưng **nó không cần
> chờ 10 năm** và nó **không bỏ sót bước 3**, thứ mà trực giác hay bỏ sót.

---

## 7. Hạ tầng tháng 1: cái quyết định 1 năm hay 10 năm

Nếu chỉ làm được **một việc** trong tài liệu này thì làm việc này.

**Vấn đề thời gian thật sự:** hiện tại một ý tưởng mất **~3 ngày** để biết đúng hay sai (sửa hằng
số → `Dbg.Show` từng ảnh → cảm giác "có vẻ tốt hơn"). Với chu kỳ 3 ngày, 1 năm chỉ thử được
~100 ý tưởng — và mỗi kết luận đều **không đáng tin** vì dựa trên cảm giác trên vài tấm ảnh.

**Cần:** một harness biến chu kỳ đó thành **~10 phút và ra một con số**.

Yêu cầu của harness (chưa code, chỉ chốt yêu cầu):

1. **Chạy batch toàn bộ 5723 ảnh**, không interactive, không `Dbg.Show`.
2. Mỗi ảnh xuất **một dòng CSV**: đường dẫn, nhãn (suy từ tên thư mục), **các con số đo được**,
   tầng nào fail nếu fail. **Không phán OK/NG trong pipeline.**
3. Một script (Python/matplotlib là được, không cần C#) đọc CSV → vẽ **histogram xếp lớp theo
   nhãn**, in ra **`d'`**, **ROC/AUC**, và ngưỡng tại điểm vận hành *escape = 0*.
4. Chạy được với một dòng lệnh, và **so sánh được hai lần chạy** (ý tưởng A vs ý tưởng B).
5. Ghi lại **version của tham số** cùng CSV, để 3 tháng sau còn tái lập được.

Điểm mấu chốt — vì sao đây là đòn nén thời gian:

> Harness không làm thuật toán tốt hơn dòng nào. Nó làm **tốc độ học** nhanh gấp ~50 lần, vì mỗi
> ý tưởng ra **một con số so sánh được** thay vì một cảm giác. Kỹ sư 10 năm không có harness thì
> học chậm hơn người 1 năm có harness.

Và nó trả lời luôn **câu hỏi số 6** còn treo ở `TU_DUY_MACHINE_VISION.md` ("ảnh `not_found` fail
ở tầng nào") — chỉ cần cột "tầng fail" trong CSV.

---

## 8. Lộ trình 12 tháng

Nguyên tắc xuyên suốt: **mỗi tháng một primitive mới, và phải chứng minh bằng con số `d'` trên
5723 ảnh — không phải bằng "đã đọc xong".**

### Quý 1 — Nền móng đo lường (không có tháng nào được bỏ)

| Tháng | Nội dung | Deliverable phải có |
|---|---|---|
| **1** | **Hiệu chuẩn + harness.** px/mm. Batch runner → CSV. Script histogram/`d'`/ROC. | Một CSV 5723 dòng + histogram đầu tiên trong đời. Biết ngay bài toán có khả thi hay không |
| **2** | **Định vị.** Fiducial. ZNCC, shape match, `FindTransformECC`. Chốt nhập nhằng 90°/180°. | Số **`σ_định_vị` bằng pixel** (thí nghiệm B ở mục 5). Phải < 1/10 khuyết tật cần bắt |
| **3** | **Đo sub-pixel.** Tự viết caliper (~50 dòng): ROI → projection → gradient → nội suy parabol. RANSAC fit. | Xác minh trên **vật chuẩn** (thước, block gauge): sai số đo phải < 0.05mm. Nếu không đạt, không đi tiếp |

> Ba tháng này **không được đảo thứ tự**. Đo trước khi định vị = đo trong hệ quy chiếu trôi.
> Định vị trước khi có harness = không biết mình đã cải thiện hay chưa.

### Quý 2 — Quang học và đóng gói production

| Tháng | Nội dung | Deliverable |
|---|---|---|
| **4** | **Quang học — tháng ROI cao nhất, và là tháng người ta bỏ qua.** Tự tay dựng: backlight, ring góc thấp (dark-field), dome, coaxial, kính phân cực chéo. Chụp **cùng một con hàng NG** dưới cả 6 kiểu. | **Bảng `d'` × kiểu đèn.** Gần như chắc chắn sẽ thấy một kiểu đèn cho `d'` gấp 3-5 lần thuật toán tốt nhất của bạn. **Đây là bài học không nén được bằng đọc — phải tự thấy** |
| **5** | **Ghép thành hệ chạy được cho `Sut_mat`.** Full chain: fixture → ROI → caliper → RANSAC → mm → ngưỡng từ phân bố. | Chạy 5723 ảnh: escape = 0, false call < 1%. Có báo cáo "NG vì lõm 1.34mm / spec 1.0mm" |
| **6** | **Vận hành.** Log, lưu ảnh NG, SPC chart giám sát drift, quy trình re-tune ngưỡng, cách bàn giao cho công nhân. Cộng `Thieu_thiec` bằng golden template + tolerance band. | Hệ chạy trên line V2 ≥ 1 tuần, có số liệu thật. **Đây là mốc "đã làm nghề", không còn là học** |

### Quý 3 — Mở rộng để "giải được mọi bài toán"

Mỗi bài toán **3 tuần**, và **bắt buộc phải có dataset thật** (tự chụp nếu công ty không có):

| Bài toán | Primitive mới học được |
|---|---|
| **Đo kích thước chính xác** (đo một chi tiết ra mm, ±0.02mm) | Telecentric, camera calibration đầy đủ, undistort, Gauge R&R thật |
| **Đọc mã / OCR** (mã in, DataMatrix, ngày sản xuất) | Perspective rectify, binarize thích ứng, template OCR, ZXing/libdmtx |
| **Đếm & phân loại** (đếm chân, phân loại linh kiện lẫn) | Blob + moment + Hu invariant, watershed tách vật dính nhau |
| **Lỗi bề mặt** (xước, bẩn, rỗ trên mặt phẳng lớn) | Thống kê texture, lọc Gabor/FFT, tolerance band theo pixel, dark-field |
| **Căn ghép / dẫn robot** (cấp toạ độ cho cơ cấu) | Hand-eye calibration, chuyển hệ toạ độ ảnh → hệ máy |

Xong quý này bạn đã đụng **cả 5 họ bài toán chính** của vision công nghiệp. Đó là nghĩa khả thi
của "mọi bài toán".

### Quý 4 — Biên giới và đóng gói

| Tháng | Nội dung |
|---|---|
| **10** | **Khi nào cổ điển thật sự hết đường** — và cách nhận ra *trước* khi tốn 2 tháng. Học DL đúng chỗ: anomaly detection (PatchCore/PaDiM — chỉ cần ảnh tốt, phù hợp AOI), segmentation. **Vị trí của nó là "primitive thứ 16", không phải thay thế 15 cái kia** |
| **11** | **3D nếu bài toán cần**: line laser triangulation, stereo, structured light. Nhận biết bài toán nào **bắt buộc** 3D (đo cao, độ đồng phẳng, thể tích thiếc) — vì `Thieu_thiec` có thể chính là một bài toán 3D bị cố giải bằng 2D |
| **12** | Đóng thư viện primitive riêng (C#/OpenCvSharp) + **checklist nhận bài toán mới** (mục 6.1 thành form). Viết lại `TU_DUY_MACHINE_VISION.md` bằng hiểu biết mới — chỗ nào hồi 2026-08 mình nghĩ sai |

---

## 9. 12 mốc tự kiểm tra

Không tính "đã đọc". Tick được khi **đã làm và có số**:

- [ ] 1. Nói được **px/mm** của rig V2, và đã kiểm chứng bằng vật có kích thước biết trước
- [ ] 2. Chạy một lệnh → CSV 5723 dòng → histogram xếp theo nhãn
- [ ] 3. Nói được `d'` của **từng** đặc trưng đang dùng
- [ ] 4. Có bảng error budget: biết thành phần nào chiếm bao nhiêu % phương sai
- [ ] 5. `σ_định_vị` < 1/10 khuyết tật nhỏ nhất cần bắt
- [ ] 6. Đo được cạnh với sai số < 0.05mm, **đã xác minh bằng vật chuẩn**
- [ ] 7. Có bảng `d'` × 6 kiểu đèn, do **tự tay** chụp
- [ ] 8. Một hệ chạy production ≥ 1 tuần với escape = 0
- [ ] 9. Có SPC chart tự cảnh báo khi phân bố dịch
- [ ] 10. Giải xong ≥ 3 họ bài toán ngoài AOI biên
- [ ] 11. Đã **từ chối** một hướng đi vì tính error budget trên giấy thấy không khả thi — *mốc quan trọng nhất, nó chứng minh tư duy đã đổi*
- [ ] 12. Nhìn một bài toán mới, trong 20 phút viết được: đại lượng vật lý → biểu hiện → nuisance → primitive + đèn

---

## 10. Điều kiện thất bại

Nói thẳng những gì sẽ khiến 1 năm này **không** thành 1 năm:

1. **Không được chạm vào đèn / lens suốt 1 năm.** Trần của bạn sẽ là *"người viết thuật toán bù
   trừ cho rig sai"* — và bù trừ đó **không bao giờ ổn định**, nên mục tiêu "ít bắt lỗi ảo" tự
   động thất bại. Nếu công ty không cho, **tự mua một bộ đèn rẻ + webcam và tự dựng ở nhà.**
   Bài học quang học không đòi rig xịn, chỉ đòi được tự tay đổi.
2. **Học bằng đọc.** Đọc 30 bài về caliper vẫn = 0 nếu chưa có một `d'` nào trong tay.
   Tỉ lệ đúng: ~20% đọc / 80% chạy có đo.
3. **Bỏ tháng 1.** Không có harness thì mọi tháng sau chậm đi 50 lần. Đây là lỗi hấp dẫn nhất
   vì tháng 1 *cảm giác* như chưa làm gì cả (không có thuật toán mới nào).
4. **Nhảy vào deep learning ở tháng 2-3** khi thấy cổ điển khó. Sẽ có kết quả demo nhanh và
   **không ổn định trên hàng thật**, đồng thời **mất luôn năm học nền móng đo lường** — mà nền
   móng đó mới là cái phân biệt kỹ sư vision với người gọi API.
5. **Dưới 8h/tuần thực hành.** Dưới mức này thì mỗi lần quay lại phải nạp lại context, hiệu suất
   thật rơi xuống gần 0. 8h/tuần liên tục **hơn** 20h/tuần ngắt quãng.

---

## 11. Việc ngay tiếp theo

Vẫn là hai câu treo từ hôm qua, chưa có câu trả lời, và **cả lộ trình phụ thuộc vào chúng**:

1. **px/mm là bao nhiêu?** (lấy một kích thước con hàng đã biết theo mm, chia số pixel nó chiếm)
   → nếu < 10 px/mm thì **dừng, báo cáo vấn đề quang học**, đừng viết thuật toán.
2. **Có được động vào đèn / camera của rig V2 không?**

Cộng thêm một câu mới sinh ra từ mục 2:

3. **Bốn thư mục lỗi (`Sut_mat`, `Thieu_thiec`, `Quan_Coi_NG`, `Quan_Boss_pin`) — có phải 4 chế
   độ lỗi độc lập, cần 4 phép đo riêng?** Hay có cái nào là biến thể của cái khác? Chốt cái này
   quyết định kiến trúc: **một pipeline hay bốn.**

---

*Tài liệu này là kết quả bàn luận, **chưa có dòng code nào được viết**.*
*Xem thêm: [`TU_DUY_MACHINE_VISION.md`](TU_DUY_MACHINE_VISION.md)*
