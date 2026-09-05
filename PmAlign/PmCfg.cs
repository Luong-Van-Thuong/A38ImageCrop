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

    /// <summary>Mức ít hơn ngần này điểm coi như không dùng được, dò sẽ không bắt đầu từ đó.</summary>
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

    /// <summary>Ngưỡng nhận kết quả (Accept Threshold). Điểm đã trừ nền ngẫu nhiên nên 0.3 là đã khá chắc.</summary>
    public double DiemToiThieu = 0.30;

    /// <summary>Số kết quả tối đa trả về trên một ảnh.</summary>
    public int SoKetQua = 1;

    /// <summary>
    /// Hệ số nhân vào sàn gradient lúc CHẠY. Sàn gốc là <c>MucPm.NguongThap</c> mà chính mức
    /// đó đã dùng lúc train, nên 1.0 nghĩa là "chạy đúng bằng tiêu chí lúc train".
    ///
    /// Mặc định 0.5 chứ không phải 1.0, và con số này đo ra chứ không chọn cho đẹp.
    ///
    /// Ngưỡng train đo TRONG ROI của con hàng mẫu, nên con hàng nào tương phản thấp hơn mẫu là
    /// cạnh của nó tụt xuống dưới ngưỡng đó ở mức mịn nhất. Đo trên bộ cnc2_den góc c1:
    ///
    ///   hệ số   bắt đúng (c1)   bắt NHẦM sang góc c3 (model không hề train)
    ///   1.0     5/10            0/10      — 5 con chết ở đúng L0: L1 còn 0.706, L0 còn 0.000
    ///   0.5     9/10            0/10      — mặc định
    ///   0.4     10/10           5/10      — được thêm 1 con đúng, đổi lấy 5 con sai
    ///   0.3     10/10           6/10
    ///
    /// Nới quá là mất khả năng TỪ CHỐI, mà tool này dùng để phân loại nên từ chối được mới là
    /// thứ đáng giá. 0.5 là chỗ vừa giữ được 9/10 vừa loại sạch góc sai.
    ///
    /// Đây là núm duy nhất còn lại tác động lên sàn; bản cũ dùng hạn ngạch phân vị 0.85 nên sàn
    /// đổi theo cả khung tìm kiếm (khoanh vùng hẹp lại là ngưỡng vọt từ 176 lên 211), vặn kiểu
    /// gì cũng không ổn định được.
    /// </summary>
    public double HeSoSanChay = 0.5;

    /// <summary>Nội suy parabol quanh đỉnh để ra toạ độ dưới pixel và góc dưới bước quét.</summary>
    public bool NoiSuyDuoiPixel = true;

    public PmCfg Sao() => (PmCfg)MemberwiseClone();
}
