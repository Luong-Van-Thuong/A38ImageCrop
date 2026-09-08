namespace A38.ImageCrop.PmAlign;

/// <summary>
/// Cách dựng mặt nạ THÂN — vùng mà điểm model được phép sinh ra.
///
/// Đo trên D:\images_ (khung ROI, không phải toàn ảnh) để biết chọn cái nào:
///   V2/1240S       nền backlight 255, vật tối  → VatToiNenSang, hoặc ToiNenSang_Min3Kenh
///                  nếu dây đồng cháy sáng ngang nền trên ảnh xám
///   JeaYoung/MLCC  nền vàng sáng 231, vật tối  → VatToiNenSang
///   SmartTech/TCut vật sáng hơn nền (tb 157 / nền 113) → VatSangNenToi, và TẮT bao lồi
///   SIBV/A26       vật chiếm gần hết khung, không có nền để tách → Khong
///   Almus_/Aline   chưa đo đủ → Khong, dùng mask vẽ tay
/// </summary>
public enum KieuThan
{
    /// <summary>Không dùng mặt nạ thân: cả ROI đều được lấy điểm. Mặc định, luôn an toàn.</summary>
    Khong,

    /// <summary>Otsu nghịch trên ảnh xám: vật TỐI trên nền SÁNG.</summary>
    VatToiNenSang,

    /// <summary>Otsu thuận trên ảnh xám: vật SÁNG trên nền TỐI.</summary>
    VatSangNenToi,

    /// <summary>
    /// Như <see cref="VatToiNenSang"/> nhưng chạy trên min(B,G,R) thay vì ảnh xám.
    /// Đây là mẹo riêng của 1240S: dây đồng cháy sáng ngang với nền backlight trên ảnh xám
    /// nên Otsu ăn mất nguyên một cạnh dài; lấy min ba kênh thì dây đồng vẫn tối.
    /// Chỉ khác ảnh xám khi ảnh THẬT SỰ có màu.
    /// </summary>
    ToiNenSang_Min3Kenh,
}

/// <summary>Cách dựng mặt nạ CHE (don-care) — vùng bị loại khỏi model.</summary>
public enum KieuChe
{
    /// <summary>Chỉ dùng những hình người dùng tự vẽ. Mặc định.</summary>
    Khong,

    /// <summary>
    /// Vùng có (R − B) vượt ngưỡng — bắt phần ÁM ĐỎ/VÀNG như dây đồng của 1240S.
    /// CẢNH BÁO đo được: trên JeaYoung/MLCC nó ăn 15.3% khung vì băng dính màu vàng cũng
    /// thoả, và phần bị ăn nằm ngay trên cạnh cần train. Ảnh xám lưu ở 3 kênh thì R−B = 0
    /// nên mặt nạ rỗng — vô hại nhưng cũng vô dụng.
    /// </summary>
    HieuKenhRB,
}

/// <summary>
/// Tham số của tool. Tương ứng hai tab "Train Params" và "Run Params" của CogPMAlign.
///
/// Khác với ba nhánh console cũ (ModelCfg / HocCfg / NghiengCfg đều là <c>static</c>, sửa
/// code rồi build lại), đây là một OBJECT — giao diện sửa trực tiếp rồi Train/Run lại ngay.
/// </summary>
public sealed class PmCfg
{
    // ---------------- Train ----------------

    /// <summary>Số mức kim tự tháp muốn trích. Mức nào quá bé hoặc quá ít điểm sẽ tự bị loại.</summary>
    public int SoMuc = 6;

    public int BlurKernel = 5;

    /// <summary>
    /// Hạn ngạch điểm biên ≈ hệ số này × chu vi mức đó. Lấy theo CHU VI chứ không theo
    /// diện tích, vì biên là đối tượng một chiều — lý do đầy đủ nằm ở ModelCfg.HeSoMatDoBien.
    /// </summary>
    public double HeSoMatDoBien = 8.0;

    /// <summary>Trần cho số pixel biên, tính theo tỉ lệ vùng quan tâm.</summary>
    public double TiLeBienToiDa = 0.20;

    public double CannyTiLeThap = 0.4;
    public int NguongBienToiThieu = 8;

    /// <summary>Khoảng cách tối thiểu giữa hai điểm model, tính bằng pixel CỦA MỨC ĐÓ.</summary>
    public int KhoangCachDiem = 3;

    /// <summary>Chặn trên số điểm mỗi mức. Nhiều điểm thì chính xác hơn nhưng chậm đi tuyến tính.</summary>
    public int SoDiemToiDa = 500;

    /// <summary>
    /// Mức ít hơn ngần này điểm coi như không dùng được, dò sẽ không bắt đầu từ đó.
    ///
    /// VẪN LÀ 12 — và đây là một chỗ mà lý luận đẹp đã bị số đo bác bỏ, đáng ghi lại.
    ///
    /// Lập luận (đúng, nhưng chỉ đúng cho công thức CŨ): điểm ở mức thô là trung bình của n
    /// mẫu; với |cos| thì độ lệch chuẩn một mẫu là 0.308, nên sd của trung bình n mẫu quy về
    /// thang điểm là 0.308/(0.363·√n). Quét thô duyệt cỡ 10⁴..10⁶ tư thế, cực đại của ngần
    /// ấy mẫu nhiễu nằm ở 4..5 sd:
    ///
    ///   n điểm   sd điểm   ~cực đại nhiễu (4.5 sd)
    ///     7       0.321          &gt; 1.0   ← nhiễu bão hoà, mức thô mù hoàn toàn
    ///    21       0.185            0.83
    ///    55       0.114            0.51
    ///
    /// Model JeaYoung/Coil (ROI 94×179) có L4 = 7 điểm, L3 = 21, nên theo bảng trên phải
    /// nâng trần lên 40 để mức thô rơi vào L2 (55 điểm). ĐÃ THỬ, và nó TỆ HƠN:
    ///
    ///   trần   top-1 đúng   ms/ảnh
    ///    12      34/34        235     ← giữ
    ///    40      32/34       1047
    ///
    /// Vì sao lập luận sai: cả bảng sd ở trên dựng trên giả định "mọi pixel đều có hướng",
    /// tức đúng công thức cũ. Khi đã bật <see cref="NmsLucChay"/> và bỏ |cos| thì phần lớn
    /// điểm model rơi vào chỗ trống và đóng góp 0, nền nhiễu sập từ 0.83 xuống gần 0 — mức
    /// thô hết bão hoà, và việc xuống mức mịn hơn chỉ còn tác dụng làm lưới quét đông gấp 4
    /// (nên chậm 4×) và cho nhiễu thêm 4× số lần bốc thăm.
    ///
    /// Bài học chung: sửa cái sàn nhiễu thì đúng hơn là né nó bằng cách thêm điểm.
    /// </summary>
    public int SoDiemToiThieuMoiMuc = 12;

    // ---- Mặt nạ tự động ------------------------------------------------------
    //
    // KHÔNG có heuristic nào đúng cho mọi dự án — đo trên 6 bộ ảnh trong D:\images_ thì
    // cùng một công thức cho ra từ 14.77% tới 100% diện tích. Nên đây là một BỘ CHỌN, mặc
    // định Khong, và mọi lựa chọn đều phải nhìn bằng mắt trong cửa sổ Debug trước khi tin.
    //
    // Mặt nạ được tính TRONG KHUNG ROI (nới ra NoiKhungMatNa) chứ không phải toàn ảnh:
    // trên ảnh 5064² nhiều con hàng, "blob lớn nhất toàn ảnh" thường là con hàng khác,
    // hoặc là vùng nền tối ngoài vòng đèn.

    /// <summary>Cách dựng mặt nạ THÂN — vùng được phép lấy điểm model.</summary>
    public KieuThan MatNaThan = KieuThan.Khong;

    /// <summary>Cách dựng mặt nạ CHE (don-care) — vùng bị loại khỏi model.</summary>
    public KieuChe MatNaChe = KieuChe.Khong;

    /// <summary>
    /// Bọc mặt nạ thân bằng BAO LỒI. Đúng cho vật lồi (1240S), SAI cho vật hình C / khung
    /// rỗng: đo trên SmartTech/TCut bao lồi cắt chéo mất hẳn góc trên-phải của vật.
    /// </summary>
    public bool BaoLoiThan = true;

    /// <summary>Nới khung tính mặt nạ ra ngoài ROI, theo tỉ lệ cạnh ROI. Cần có ít nền
    /// trong khung thì Otsu mới có hai đỉnh mà tách.</summary>
    public double NoiKhungMatNa = 0.25;

    /// <summary>
    /// Ngưỡng an toàn: mặt nạ thân giữ lại ít hơn ngần này diện tích mẫu thì coi như hỏng,
    /// tự động bỏ mặt nạ và ghi cảnh báo — thà train không mặt nạ còn hơn train một model
    /// chỉ còn vài điểm mà không ai biết.
    /// </summary>
    public double ThanToiThieu = 0.05;

    public int NguongDongRB = 100;
    public int MoVungDong = 3;
    public int NoiRongDongChe = 9;
    public int NoiRongThan = 3;

    // ---------------- Run ----------------

    /// <summary>Dải góc quét, tính SO VỚI ẢNH MẪU. Đúng vai trò Zone Angle của Cognex.</summary>
    public double GocTuDo = -180, GocDenDo = 180;

    /// <summary>Số ứng viên giữ lại ở mức thô nhất rồi thu hẹp dần khi xuống mức mịn.</summary>
    public int SoUngVienDinh = 40;

    /// <summary>
    /// Ngưỡng nhận kết quả (Accept Threshold). Điểm đã trừ nền ngẫu nhiên rồi.
    ///
    /// 0.20 chứ không phải 0.30, và con số này đặt CÙNG LÚC với <see cref="HeSoSanChay"/> = 0.35
    /// chứ không tách rời — xem bảng đo ở đó. Với cặp đó, trên bộ cnc2_den (10 ảnh c1 phải nhận,
    /// 20 ảnh c2/c3 phải từ chối) khoảng trống là [0.000 … 0.321]; đặt ngưỡng vào giữa cho
    /// biên an toàn 0.12 về phía nhận và 0.20 về phía từ chối. Để ở 0.30 vẫn đúng 30/30 nhưng
    /// biên phía nhận chỉ còn 0.021 — một con hàng tương phản thấp hơn chút là trượt.
    /// </summary>
    public double DiemToiThieu = 0.20;

    /// <summary>Số kết quả tối đa trả về trên một ảnh.</summary>
    public int SoKetQua = 1;

    /// <summary>
    /// Hệ số nhân vào sàn gradient lúc CHẠY. Sàn gốc là <c>MucPm.NguongThap</c> mà chính mức
    /// đó đã dùng lúc train, nên 1.0 nghĩa là "chạy đúng bằng tiêu chí lúc train".
    ///
    /// Mặc định 0.35 chứ không phải 1.0, và con số này đo ra chứ không chọn cho đẹp.
    ///
    /// Ngưỡng train đo TRONG ROI của con hàng mẫu, nên con hàng nào tương phản thấp hơn mẫu là
    /// cạnh của nó tụt xuống dưới ngưỡng đó ở mức mịn nhất — điểm sập ĐÚNG ở L0 trong khi L1
    /// vẫn còn 0.81.
    ///
    /// ĐO ĐẦY ĐỦ (2026-09-07), bộ cnc2_den: 10 ảnh c1 PHẢI NHẬN, 20 ảnh c2/c3 PHẢI TỪ CHỐI,
    /// chạy trong khung tìm 475×608, dải 360°, DiemToiThieu = 0 để lấy điểm cao nhất:
    ///
    ///   hệ số   c1 thấp nhất   c2/c3 cao nhất   khoảng trống   ngưỡng giữa khoảng
    ///   1.00    0.000          0.000            —              không tách được (5 con c1 ra 0)
    ///   0.70    0.000          0.000            —              không tách được
    ///   0.50    0.262          0.000            0.262          0.13
    ///   0.35    0.321          0.000            0.321          0.16   ← rộng nhất, chọn cái này
    ///   0.25    0.377          0.072            0.306          0.22
    ///   0.15    0.360          0.259            0.100          0.31   ← bắt đầu nhận nhầm
    ///
    /// Kiểm chéo trên bộ khác để không chọn theo riêng một bộ: Almus_/Fix_Tape (4 ảnh, đều
    /// phải nhận) ở 0.35 có điểm thấp nhất 0.798 — thừa sức, nên 0.35 không phải là con số
    /// chỉ đúng cho SmartTech.
    ///
    /// Nới quá là mất khả năng TỪ CHỐI, mà tool này dùng để phân loại nên từ chối được mới là
    /// thứ đáng giá. 0.35 là chỗ khoảng trống rộng nhất giữa "phải nhận" và "phải từ chối".
    ///
    /// LƯU Ý QUAN TRỌNG: mọi con số trên chỉ đúng khi có KHOANH VÙNG TÌM KIẾM. Cùng bộ đó mà
    /// quét cả ảnh thì c2/c3 lên tới 0.982 — chồng lấn hoàn toàn với c1 ở MỌI mức sàn, không
    /// hệ số nào cứu được. Xem <see cref="PmModel.VungTim"/>.
    ///
    /// Đây là núm duy nhất còn lại tác động lên sàn; bản cũ dùng hạn ngạch phân vị 0.85 nên sàn
    /// đổi theo cả khung tìm kiếm (khoanh vùng hẹp lại là ngưỡng vọt từ 176 lên 211), vặn kiểu
    /// gì cũng không ổn định được.
    /// </summary>
    public double HeSoSanChay = 0.35;

    // ================= Chấm điểm (sửa 08/09/2026) =================
    //
    // ĐO ĐẦY ĐỦ trên JeaYoung/Coil/CamChupTrenXuong/NghiengLenXuong — 36 ảnh, model 1.pmm.json
    // (ROI 94×179), vùng tìm 926×877 (48× diện tích model), dải góc 360°, DiemToiThieu = 0,
    // xin 12 đỉnh mỗi ảnh. Chân lý dựng bằng khung hẹp 520×420 rồi SOI BẰNG MẮT: đúng 34/36,
    // hai tấm còn lại (085013996, 090137501) là ảnh vật xoay ~90° với chip bị coil che nên
    // bị loại khỏi thống kê. Chạy lại bằng:
    //
    //   dotnet run -- --do-bien <thu-muc> <model.pmm.json> --vt 1120,1130,520,420
    //                 --bo Image_20260819085013996.bmp,Image_20260819090137501.bmp
    //
    //   cấu hình                                top-1   thấy đỉnh   đỉnh ĐÚNG   đỉnh SAI   biên
    //                                            đúng     đúng      thấp nhất   cao nhất
    //   bản cũ (|cos|, không NMS, không dung sai) 31/34   33/34        0.243      0.320    −0.076
    //   + NMS + dung sai 2px, VẪN |cos|           24/34   27/34        0.332      0.427    −0.095
    //   + tích vô hướng CÓ DẤU (dung sai 2px)     33/34   34/34        0.188      0.264    −0.076
    //   + dung sai 1px                            34/34   34/34        0.126      0.169    −0.042
    //   + dung sai 3px                            34/34   34/34        0.235      0.297    −0.062
    //   + dung sai 3px + 0.5×clutter              34/34   34/34        0.235      0.297    −0.062
    //   + dung sai 2px + 0.5×clutter              34/34   34/34        0.181      0.264    −0.083
    //
    // Ba điều rút ra, cả ba đều trái với dự đoán ban đầu ở một chỗ nào đó:
    //
    //  1. NMS MỘT MÌNH LÀM TỆ HƠN HẲN (31 → 24). Nó chỉ thành lợi khi đi cùng dấu. Lý do:
    //     một vết xước là GỜ nên hai mép có gradient ngược dấu; bỏ dấu thì làm biên mảnh đi
    //     chỉ khiến mỗi vết xước thành hai đường sắc nét khớp còn ngọt hơn.
    //  2. Thứ giải quyết bài toán là CẶP (NMS + dấu) cộng dung sai: top-1 từ 31/34 lên 34/34,
    //     và đỉnh đúng được TÌM THẤY trên 34/34 thay vì 33/34.
    //  3. BIÊN VẪN ÂM ở mọi cấu hình. Nghĩa là bài "định vị" đã xong, còn bài "một ngưỡng
    //     chung tách sạch đúng/sai" thì CHƯA. Trên bộ này không cần vế sau (mọi ảnh đều có
    //     vật), nhưng đừng nhầm hai chuyện đó với nhau.
    //
    // Clutter không cải thiện gì trên bộ này nên <see cref="HeSoClutter"/> để 0 — vẫn tính và
    // báo ra để nhìn, nhưng không cho đổi thứ hạng.
    //
    // Ba núm dưới đây sửa CÙNG MỘT lỗi gốc: train và run trước đây định nghĩa "biên" khác
    // nhau, nên con số gọi là "điểm khớp" không đo cái mà tên nó nói.
    //
    //   Train: Canny — có triệt phi cực đại + trễ → biên MẢNH 1 px, chỉ nằm trên đỉnh gradient.
    //   Run  : chỉ có sàn độ lớn → MỌI pixel qua sàn đều là "biên hợp lệ và có hướng".
    //
    // Trên nền thép đầy vết xước (JeaYoung/Coil) sàn ở L3 là 74.8 × 0.35 ≈ 26 theo chuẩn L1,
    // tức chỉ cần chênh ~6 mức xám là qua — gần 100% vùng đó "là biên". Khi mọi pixel đều có
    // hướng thì tư thế nào cũng trúng đủ điểm, và chỉ còn HƯỚNG quyết định. Ta đã tự tay bỏ
    // mất một nửa cơ chế phân biệt.

    /// <summary>
    /// Dùng Canny (triệt phi cực đại + trễ) lúc chạy thay cho sàn độ lớn trần trụi, với
    /// ĐÚNG cặp ngưỡng mà mức đó đã dùng lúc train, nhân <see cref="HeSoSanChay"/>.
    ///
    /// Đây là thứ Cognex làm mà bản cũ không có: run-time họ trích CHUỖI BIÊN, không phải
    /// một trường gradient đặc. Nền thưa đi thì một tư thế ngẫu nhiên hầu như không trúng
    /// gì, điểm của nó sập về 0 — chính là khả năng từ chối mà ta đang thiếu.
    ///
    /// BẮT BUỘC đi kèm <see cref="DungSaiPx"/> &gt; 0: biên mảnh 1 px mà chấm điểm không có
    /// dung sai khoảng cách thì đỉnh ĐÚNG cũng chết theo. Hai thứ này là một cặp.
    ///
    /// VÀ BẮT BUỘC đi kèm dấu (<see cref="BoQuaChieuTuongPhan"/> = false). Đo được: bật NMS mà
    /// vẫn |cos| thì top-1 TỤT từ 31/34 xuống 24/34 — tệ hơn cả bản cũ. Biên mảnh đi chỉ làm
    /// mỗi vết xước thành hai đường sắc nét khớp còn ngọt hơn, khi không có dấu để phân biệt
    /// gờ với bậc. Ba núm này là MỘT gói, bật lẻ từng cái là hỏng.
    /// </summary>
    public bool NmsLucChay = true;

    /// <summary>
    /// Bỏ qua chiều tương phản — lấy |tích vô hướng| thay vì tích vô hướng có dấu.
    ///
    /// Mặc định FALSE, tức CÓ xét dấu. Đây là mặc định của CogPMAlign (IgnorePolarity =
    /// false); bật lên là ngoại lệ dành cho bài mà tương phản thật sự đảo.
    ///
    /// Bỏ dấu thì nền ngẫu nhiên cho E[|cos|] = 2/π = 0.637 thay vì E[cos] = 0, và toàn bộ
    /// dải hữu ích bị nén còn 36%. Quan trọng hơn con số: một VẾT XƯỚC là gờ (sáng trên nền
    /// tối) nên hai mép nó có gradient NGƯỢC DẤU, còn cạnh vật là bậc nên chỉ một dấu. Có
    /// dấu thì hai thứ đó phân biệt được, bỏ dấu thì không — mà chỗ bắt sai đo được trên
    /// JeaYoung/Coil nằm đúng trên vùng thép xước.
    /// </summary>
    public bool BoQuaChieuTuongPhan = false;

    /// <summary>
    /// Dung sai khoảng cách khi ghép một điểm model với biên trong ảnh, tính bằng pixel
    /// CỦA MỨC ĐANG XÉT (nên ở L3 nó tự bằng 8× ngần này pixel ảnh gốc — đúng thứ ta muốn).
    ///
    /// Bản cũ đọc gradient tại ĐÚNG MỘT pixel (nearest neighbour). PatMax làm khác hẳn: mỗi
    /// điểm model ghép với feature ảnh GẦN NHẤT trong dung sai, có trọng số theo khoảng
    /// cách, rồi fit pose. Đó là lý do họ được 0.8..0.9 trên con hàng lệch nhẹ / biến dạng
    /// nhẹ, còn ta tụt xuống 0.35 — tức nửa còn lại của khoảng cách giữa hai tool.
    ///
    /// Trọng số tuyến tính: w(d) = max(0, 1 − d/DungSaiPx). Tuyến tính chứ không phải
    /// Gauss hay bậc hai, vì hai cái kia có đạo hàm 0 tại d = 0 — đỉnh bẹt, mà nội suy dưới
    /// pixel lại sống nhờ đỉnh nhọn.
    ///
    /// 2.0 là mặc định TRUNG TÍNH, không phải giá trị tốt nhất của một bộ ảnh. Đo được:
    ///
    ///   • Định vị (36 ảnh JeaYoung/Coil, bảng ở đầu mục): 1, 2 và 3 px đều cho top-1 34/34.
    ///     Khác nhau ở THANG ĐIỂM — 1 px cho đỉnh đúng 0.126..0.605, 3 px cho 0.235..0.865.
    ///     Muốn con số nhìn giống Cognex hơn thì nới lên 3; muốn biên rộng nhất thì siết
    ///     xuống 1 (biên −0.042 so với −0.062). Cả hai đều là một ô nhập.
    ///   • Sai số dưới pixel (--tu-kiem, chân lý biết trước, 6 ca):
    ///
    ///       dung sai   sai vị trí   sai góc
    ///       0 (cũ)       0.70 px    0.172°
    ///       1            1.33 px    0.107°
    ///       2            1.09 px    0.107°
    ///       3            1.02 px    0.091°
    ///
    ///     Lo ban đầu là "dung sai làm bẹt đỉnh, mất độ chính xác dưới pixel" — SAI một nửa:
    ///     vị trí có kém đi (0.70 → ~1.0 px) nhưng vẫn trong ngưỡng 2 px, còn GÓC thì tốt lên
    ///     hẳn ở mọi mức dung sai. Đổi lại là dò trúng chỗ trên 3 tấm mà bản cũ trượt.
    ///
    /// Đặt 0 để tắt (quay về đọc đúng một pixel như bản cũ).
    /// </summary>
    public double DungSaiPx = 2.0;

    /// <summary>
    /// Hệ số trừ clutter khi XẾP HẠNG: điểm xếp hạng = Diem − HeSoClutter × Clutter.
    ///
    /// Coverage một mình không phân biệt được "tìm thấy đủ cạnh của tôi" với "tìm thấy đủ
    /// cạnh của tôi CỘNG THÊM 500 cạnh khác". Cognex báo Score và Clutter tách riêng đúng vì
    /// lý do đó. <see cref="KetQuaPm.Clutter"/> = tỉ lệ pixel biên nằm trong dấu chân của
    /// model mà KHÔNG điểm model nào giải thích được, 0..1.
    ///
    /// Mặc định 0 = chỉ ĐO và báo ra, KHÔNG cho đổi thứ hạng — và con số 0 này là đo ra chứ
    /// không phải thận trọng suông. Trên 36 ảnh JeaYoung/Coil, clutter của tư thế ĐÚNG trung
    /// bình 0.70 còn của tư thế SAI là 0.79: có phân biệt, nhưng chồng lấn quá nhiều nên trừ
    /// 0.5×clutter không đổi được tấm nào (34/34 cả khi có lẫn khi không), mà trừ 1.0×clutter
    /// thì làm hỏng thêm một tấm. Lý do vật lý: dấu chân của model phủ cả nền quanh con hàng,
    /// mà nền ở đây là thép xước — nên clutter cao ở KHẮP NƠI, kể cả đúng chỗ.
    /// </summary>
    public double HeSoClutter = 0.0;

    /// <summary>Loại thẳng kết quả có clutter vượt ngưỡng này. 1.0 (hoặc hơn) = không loại ai.</summary>
    public double ClutterToiDa = 1.0;

    /// <summary>Luôn ĐO clutter và báo ra, kể cả khi nó không được phép đổi thứ hạng.</summary>
    public bool LuonDoClutter = false;

    /// <summary>
    /// Vùng CHE (don't-care) không được tính vào clutter — cả tử số lẫn mẫu số.
    ///
    /// Mặc định bật vì đó mới đúng nghĩa don't-care: vùng che không có điểm model (đã bị loại
    /// từ lúc Train), nên nếu vẫn đếm thì mọi cạnh ở đó là "cạnh lạ" và tư thế ĐÚNG bị phạt
    /// đúng bằng tư thế sai — che một vùng vì nó hay thay đổi lại thành tự bắn vào chân mình.
    ///
    /// Để cờ này lại (thay vì sửa cứng) vì trên một model không che gì thì nó không đổi gì cả,
    /// còn model có che thì phải ĐO mới biết biên nhích bao nhiêu. Tắt đi là quay về hành vi cũ.
    /// </summary>
    public bool CheKhongTinhClutter = true;

    /// <summary>Nội suy parabol quanh đỉnh để ra toạ độ dưới pixel và góc dưới bước quét.</summary>
    public bool NoiSuyDuoiPixel = true;

    // ---------------- Train nhiều ảnh ----------------

    /// <summary>
    /// Tỉ lệ ảnh phụ mà một điểm model phải trụ được thì mới giữ lại, khi train nhiều ảnh.
    ///
    /// Lý do có mục này: model JeaYoung/Coil có 339 điểm ở L0 cho chu vi 546 px với
    /// KhoangCachDiem = 3 ⇒ HƠN NỬA số điểm nằm trong lòng chip, trên các vệt phản quang.
    /// So hai ảnh nghiêng thì nội thất chip đổi hoàn toàn, chỉ silhouette là ổn định. Những
    /// điểm đó vừa kéo đỉnh đúng xuống, vừa dễ ăn may trên texture — hại cả hai đầu.
    ///
    /// Nhưng cắt sạch nội thất cũng sai: chip chỉ còn silhouette thì đúng bằng "một thanh
    /// sáng trên nền tối", tức hồ sơ của một vết xước. Nên không cắt theo hình học mà cắt
    /// theo ĐO ĐƯỢC: điểm nào có mặt ở đủ nhiều ảnh thì giữ.
    ///
    /// ĐO ĐƯỢC (36 ảnh JeaYoung/Coil, ảnh mẫu + 35 ảnh phụ, chấm điểm dung sai 2px có dấu):
    ///
    ///   tỉ lệ / ngưỡng   điểm L0 còn lại   top-1 đúng   đỉnh ĐÚNG thấp nhất   biên
    ///     (không lọc)       339            33/34             0.188           −0.076
    ///     0.60 / 0.50         3            —                 —               —      ← quá gắt
    ///     0.40 / 0.30       226 (67%)      34/34             0.282           −0.072  ← chốt
    ///     0.30 / 0.25       262 (77%)      34/34             0.263           −0.023
    ///     0.25 / 0.20       284 (84%)      34/34             0.242           −0.029
    ///
    /// Chọn 0.40/0.30 vì nó nâng đỉnh ĐÚNG thấp nhất từ 0.188 lên 0.282 — tức **thang điểm
    /// tăng 50%** mà không mất tấm nào. Đó đúng là thứ cần khi muốn đặt ngưỡng cao hơn.
    ///
    /// 0.60/0.50 chỉ giữ 3/339 điểm nên chốt an toàn trong <see cref="PmEngine.TrainOnDinh"/>
    /// phải bỏ qua việc lọc — mất công train mà model không đổi. Nếu thấy dòng "QUA IT" ở
    /// mọi mức thì hạ hai con số này chứ đừng tưởng là lọc đã chạy.
    /// </summary>
    public double TiLeOnDinh = 0.4;

    /// <summary>
    /// Ngưỡng để coi một điểm model là "có trụ được" trên một ảnh (0..1) — chính là đóng góp
    /// của điểm đó vào điểm khớp, tức (trọng số theo khoảng cách) × (cos lệch hướng).
    ///
    /// 0.30 chứ không phải 0.50: với <see cref="DungSaiPx"/> = 2 thì một điểm nằm cách biên
    /// đúng 1 px đã bị nhân 0.5 rồi, nên đòi 0.5 là ngầm đòi điểm phải nằm chính xác trên
    /// biên VÀ hướng khớp gần tuyệt đối. Bảng đo ở <see cref="TiLeOnDinh"/>.
    /// </summary>
    public double NguongTruDiem = 0.3;

    public PmCfg Sao() => (PmCfg)MemberwiseClone();
}
