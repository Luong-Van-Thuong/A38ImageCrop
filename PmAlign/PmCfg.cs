namespace A38.ImageCrop.PmAlign;

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

    // ---- Mặt nạ tự động, riêng cho bộ ảnh 1240S. Mặc định TẮT để tool còn dùng chung được ----

    /// <summary>Chỉ lấy điểm nằm trên thân vật (Otsu + bao lồi trên min(B,G,R)).</summary>
    public bool ChiLayTrenThan = false;

    /// <summary>Coi vùng dây đồng là don-care. Diện tích cuộn dây đổi giữa các con hàng nên nó là nhiễu.</summary>
    public bool TuCheVungDong = false;

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

    /// <summary>Nội suy parabol quanh đỉnh để ra toạ độ dưới pixel và góc dưới bước quét.</summary>
    public bool NoiSuyDuoiPixel = true;

    public PmCfg Sao() => (PmCfg)MemberwiseClone();
}
