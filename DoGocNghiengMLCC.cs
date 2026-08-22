using OpenCvSharp;

namespace A38.ImageCrop;

/// <summary>
/// Kết quả đo góc nghiêng của một con tụ trong khung AI.
/// Mọi toạ độ ở đây đều là HỆ TOẠ ĐỘ ẢNH GỐC (ảnh truyền vào Do()), không phải hệ ROI.
/// </summary>
public struct KetQuaGocNghieng
{
    /// <summary>Đo được cả hai cạnh hay không. false thì mọi trường còn lại vô nghĩa.</summary>
    public bool HopLe;

    /// <summary>
    /// Góc giữa trục thân tụ và line dọc ảo, đơn vị độ.
    /// 0 = tụ dựng thẳng. Dương = đáy tụ lệch sang PHẢI so với đỉnh (đỉnh ngả sang trái).
    /// Âm = ngược lại. So với ngưỡng thì lấy trị tuyệt đối.
    /// </summary>
    public double GocDo;

    /// <summary>Trung tâm cạnh trên (biên thân tụ -> mối hàn phía trên).</summary>
    public Point2f TamCanhTren;

    /// <summary>Trung tâm cạnh dưới.</summary>
    public Point2f TamCanhDuoi;

    /// <summary>Góc của CẠNH TRÊN so với phương ngang (độ). Cạnh vuông góc trục nên số này phải xấp xỉ GocDo.</summary>
    public double GocCanhTren;

    /// <summary>Góc của CẠNH DƯỚI so với phương ngang (độ).</summary>
    public double GocCanhDuoi;

    /// <summary>Số điểm biên còn lại sau khi lọc — biết kết quả chắc hay mỏng.</summary>
    public int SoDiemTren;
    public int SoDiemDuoi;

    /// <summary>Sai số RMS của điểm so với line khớp (px). Càng nhỏ cạnh càng thẳng.</summary>
    public double SaiSoTrenPx;
    public double SaiSoDuoiPx;

    public string ThongDiep;

    /// <summary>
    /// Rỗng = không có gì bất thường. Khác rỗng = số đo VẪN có nhưng có dấu hiệu đáng ngờ
    /// (độ dốc cạnh lạ, hai cạnh không song song, hai cạnh quá gần nhau). Không tự loại vì
    /// đo thực tế cho thấy độ dốc cạnh nhiễu hơn nhiều so với trung tâm cạnh — xem ghi chú
    /// trong <see cref="DoGocNghiengMLCC.Do"/>.
    /// </summary>
    public string CanhBao;

    public override string ToString() =>
        HopLe
            ? $"goc={GocDo:+0.00;-0.00;0.00}do | canh tren {GocCanhTren:+0.0;-0.0;0.0}do ({SoDiemTren} diem, rms {SaiSoTrenPx:0.0}px)" +
              $" | canh duoi {GocCanhDuoi:+0.0;-0.0;0.0}do ({SoDiemDuoi} diem, rms {SaiSoDuoiPx:0.0}px)" +
              (string.IsNullOrEmpty(CanhBao) ? "" : $" | CANH BAO: {CanhBao}")
            : $"KHONG DO DUOC: {ThongDiep}";
}

/// <summary>
/// Đo góc nghiêng của con tụ MLCC nằm trong khung AI (<see cref="AiYoloCamCheo"/>).
///
/// Ý tưởng: thân tụ là một khối màu đồng nhất, hai đầu là mối hàn thiếc SÁNG hơn hẳn.
/// Biên thân–thiếc (cạnh trên / cạnh dưới) vì thế là một vạch gradient dọc mạnh.
/// Với mỗi cột ảnh trong dải giữa khung, tìm vị trí |gradient theo Y| lớn nhất ở nửa trên
/// và ở nửa dưới — được hai đám mây điểm. Khớp line qua mỗi đám mây (có loại điểm lạc),
/// lấy TRUNG TÂM mỗi cạnh, nối hai trung tâm thành TRỤC THÂN TỤ, rồi đo góc trục đó
/// với LINE DỌC ẢO (phương thẳng đứng của ảnh).
///
/// Vì sao là trục nối hai trung tâm chứ không phải cạnh bên: cạnh bên của tụ bị mối hàn
/// và bóng sáng ăn vào làm méo, còn hai cạnh đầu thì luôn lộ rõ vì độ tương phản
/// thân–thiếc là cái mạnh nhất trong khung.
/// </summary>
public static class DoGocNghiengMLCC
{
    // ---- Tham số, để public để còn chỉnh tay khi chạy thử ----------------------------

    /// <summary>Chỉ quét dải giữa khung theo bề ngang (0.55 = lấy 55% giữa). Bỏ hai bên vì
    /// góc bo tròn + mối hàn bên hông hay đánh lừa argmax.</summary>
    public static double TyLeCotGiua = 0.55;

    /// <summary>Dải tìm cạnh TRÊN, tính theo tỉ lệ chiều cao khung (từ trên xuống).</summary>
    public static double DaiTrenTu = 0.03;
    public static double DaiTrenDen = 0.48;

    /// <summary>Dải tìm cạnh DƯỚI.</summary>
    public static double DaiDuoiTu = 0.52;
    public static double DaiDuoiDen = 0.97;

    /// <summary>Bỏ điểm có gradient yếu hơn ngưỡng này (thang 0..255 sau Sobel + ConvertScaleAbs).</summary>
    public static int NguongGradToiThieu = 18;

    /// <summary>Và bỏ điểm yếu hơn tỉ lệ này so với điểm mạnh nhất của chính đám mây đó.</summary>
    public static double TyLeGradSoVoiManhNhat = 0.25;

    /// <summary>Loại điểm lạc: cách line khớp quá bao nhiêu lần sai số trung vị thì bỏ.</summary>
    public static double HeSoLoaiDiemLac = 2.5;

    /// <summary>Số điểm tối thiểu còn lại để coi một cạnh là đo được.</summary>
    public static int SoDiemToiThieu = 8;

    /// <summary>Nới khung AI ra mỗi phía trước khi quét (px). 0 = dùng nguyên khung AI.</summary>
    public static int NoiKhungPx = 0;

    // ---- Ngưỡng CẢNH BÁO (không loại kết quả) ----------------------------------------

    /// <summary>Cạnh đầu tụ vốn là vạch ngang; line khớp lệch phương ngang quá số độ này thì ghi cảnh báo.</summary>
    public static double NguongCanhLechNgangDo = 30;

    /// <summary>Hai cạnh đầu tụ song song nhau; lệch nhau quá số độ này thì ghi cảnh báo.</summary>
    public static double NguongLechHaiCanhDo = 20;

    /// <summary>Hai trung tâm cạnh phải cách nhau ít nhất tỉ lệ này của chiều cao khung.</summary>
    public static double TyLeKhoangCachToiThieu = 0.30;

    /// <summary>
    /// Đo góc nghiêng trong một khung. <paramref name="anhVe"/> là bản sao có vẽ chồng kết quả
    /// (null nếu <paramref name="veDebug"/> = false); NGƯỜI GỌI TỰ DISPOSE.
    /// </summary>
    public static KetQuaGocNghieng Do(Mat src, Rect khung, out Mat? anhVe, bool veDebug = true)
    {
        anhVe = null;
        var kq = new KetQuaGocNghieng { HopLe = false, ThongDiep = "", CanhBao = "" };

        if (src is null || src.Empty()) { kq.ThongDiep = "anh rong"; return kq; }

        // Nới khung rồi cắt cho lọt trong ảnh — khung AI sát mép ảnh là chuyện thường.
        Rect roi = new Rect(khung.X - NoiKhungPx, khung.Y - NoiKhungPx,
                            khung.Width + 2 * NoiKhungPx, khung.Height + 2 * NoiKhungPx);
        roi = roi.Intersect(new Rect(0, 0, src.Width, src.Height));
        if (roi.Width < 20 || roi.Height < 30)
        {
            kq.ThongDiep = $"khung qua nho ({roi.Width}x{roi.Height})";
            return kq;
        }

        using var vung = new Mat(src, roi);
        using var xam = new Mat();
        if (vung.Channels() > 1) Cv2.CvtColor(vung, xam, ColorConversionCodes.BGR2GRAY);
        else vung.CopyTo(xam);

        // Làm mịn trước khi lấy đạo hàm, không thì nhiễu hạt cũng thành "cạnh".
        using var min = new Mat();
        Cv2.GaussianBlur(xam, min, new Size(5, 5), 1.2);

        // Sobel theo Y bắt vạch NGANG (biên thân–thiếc). Lấy trị tuyệt đối vì thân tụ
        // có ảnh sáng hơn mối hàn, cũng có ảnh tối hơn — dấu gradient đổi chiều tuỳ ảnh.
        using var gy = new Mat();
        Cv2.Sobel(min, gy, MatType.CV_16S, 0, 1, 3);
        using var doLon = new Mat();
        Cv2.ConvertScaleAbs(gy, doLon);

        int W = doLon.Width, H = doLon.Height;
        int beRong = Math.Max(4, (int)Math.Round(W * TyLeCotGiua));
        int xTu = (W - beRong) / 2, xDen = xTu + beRong;

        var diemTren = QuetDai(doLon, xTu, xDen, (int)(H * DaiTrenTu), (int)(H * DaiTrenDen));
        var diemDuoi = QuetDai(doLon, xTu, xDen, (int)(H * DaiDuoiTu), (int)(H * DaiDuoiDen));

        if (diemTren.Count < SoDiemToiThieu || diemDuoi.Count < SoDiemToiThieu)
        {
            kq.ThongDiep = $"khong du diem bien (tren {diemTren.Count}, duoi {diemDuoi.Count}, can >= {SoDiemToiThieu})";
            if (veDebug) anhVe = VeKetQua(src, roi, kq, diemTren, diemDuoi, null, null);
            return kq;
        }

        var canhTren = KhopCanh(diemTren);
        var canhDuoi = KhopCanh(diemDuoi);

        if (canhTren is null || canhDuoi is null)
        {
            kq.ThongDiep = "loc diem lac xong khong con du diem de khop line";
            if (veDebug) anhVe = VeKetQua(src, roi, kq, diemTren, diemDuoi, canhTren, canhDuoi);
            return kq;
        }

        // Trung tâm cạnh = điểm TRÊN LINE KHỚP, tại hoành độ trung bình của đám mây.
        // Dùng điểm trên line (không dùng trọng tâm thô) để vài điểm lệch không kéo tâm đi.
        var ct = canhTren.Value;
        var cd = canhDuoi.Value;

        kq.TamCanhTren = new Point2f(ct.Tam.X + roi.X, ct.Tam.Y + roi.Y);
        kq.TamCanhDuoi = new Point2f(cd.Tam.X + roi.X, cd.Tam.Y + roi.Y);
        kq.SoDiemTren = ct.SoDiem;
        kq.SoDiemDuoi = cd.SoDiem;
        kq.SaiSoTrenPx = ct.RmsPx;
        kq.SaiSoDuoiPx = cd.RmsPx;
        kq.GocCanhTren = GocSoVoiNgang(ct.Vx, ct.Vy);
        kq.GocCanhDuoi = GocSoVoiNgang(cd.Vx, cd.Vy);

        // Trục thân tụ = vector nối hai trung tâm. Line dọc ảo là (0,1).
        // Góc giữa hai vector: atan2(thành phần ngang, thành phần dọc).
        double vx = kq.TamCanhDuoi.X - kq.TamCanhTren.X;
        double vy = kq.TamCanhDuoi.Y - kq.TamCanhTren.Y;
        if (vy < 0) { vx = -vx; vy = -vy; }   // luôn hướng từ trên xuống dưới
        if (vy < 1e-6)
        {
            kq.ThongDiep = "hai trung tam gan trung nhau theo phuong doc";
            if (veDebug) anhVe = VeKetQua(src, roi, kq, diemTren, diemDuoi, canhTren, canhDuoi);
            return kq;
        }

        kq.GocDo = Math.Atan2(vx, vy) * 180.0 / Math.PI;

        // ---- Cảnh báo, KHÔNG loại bỏ ---------------------------------------------------
        // Đo trên 43 ảnh: TRUNG TÂM hai cạnh rất chắc (ảnh tụ dựng thẳng ra 0.0 độ), nhưng
        // ĐỘ DỐC của line khớp thì lệch tới ±20..60 độ dù cạnh thật nằm ngang — vì đỉnh
        // gradient bị ánh sáng chiếu xiên kéo trôi đều theo một chiều. Độ dốc trôi đều nên
        // không phá GocDo (hai cạnh trôi cùng chiều thì triệt tiêu nhau), nhưng cũng vì thế
        // KHÔNG dùng nó làm chốt loại được: chặn theo độ dốc là giết cả ca đúng.
        // Nên chỉ ghi cảnh báo, để chỗ gọi tự quyết có tin số đo này hay không.
        double khoangCach = Math.Sqrt(vx * vx + vy * vy);
        var canhBao = new List<string>();

        if (Math.Abs(kq.GocCanhTren) > NguongCanhLechNgangDo || Math.Abs(kq.GocCanhDuoi) > NguongCanhLechNgangDo)
            canhBao.Add($"canh doc bat thuong (tren {kq.GocCanhTren:0.0}do, duoi {kq.GocCanhDuoi:0.0}do)");
        if (Math.Abs(kq.GocCanhTren - kq.GocCanhDuoi) > NguongLechHaiCanhDo)
            canhBao.Add($"hai canh khong song song (lech {Math.Abs(kq.GocCanhTren - kq.GocCanhDuoi):0.0}do)");
        if (khoangCach < roi.Height * TyLeKhoangCachToiThieu)
            canhBao.Add($"hai canh qua gan nhau ({khoangCach:0}px)");

        kq.CanhBao = canhBao.Count > 0 ? string.Join("; ", canhBao) : "";
        kq.HopLe = true;
        kq.ThongDiep = "OK";

        if (veDebug) anhVe = VeKetQua(src, roi, kq, diemTren, diemDuoi, canhTren, canhDuoi);
        return kq;
    }

    // --------------------------------------------------------------------------------

    /// <summary>
    /// Với mỗi cột trong [xTu,xDen), tìm y có |gradient Y| lớn nhất trong dải [yTu,yDen).
    /// Trả về theo hệ toạ độ ROI. Lọc hai lần: ngưỡng tuyệt đối + ngưỡng tương đối so với
    /// cột mạnh nhất, để cột rơi vào vùng mờ/tối không đóng góp điểm rác.
    /// </summary>
    private static List<Point2f> QuetDai(Mat doLon, int xTu, int xDen, int yTu, int yDen)
    {
        var diem = new List<Point2f>();

        yTu = Math.Max(1, yTu);
        yDen = Math.Min(doLon.Height - 1, yDen);
        if (yDen - yTu < 3) return diem;

        var tho = new List<Point2f>();
        var manh = new List<int>();

        for (int x = xTu; x < xDen; x++)
        {
            int tot = -1, yTot = -1;
            for (int y = yTu; y < yDen; y++)
            {
                int v = doLon.At<byte>(y, x);
                if (v > tot) { tot = v; yTot = y; }
            }
            if (tot < NguongGradToiThieu) continue;
            tho.Add(new Point2f(x, yTot));
            manh.Add(tot);
        }
        if (tho.Count == 0) return diem;

        int nguong = (int)(manh.Max() * TyLeGradSoVoiManhNhat);
        for (int i = 0; i < tho.Count; i++)
            if (manh[i] >= nguong) diem.Add(tho[i]);

        return diem;
    }

    private struct Canh
    {
        public double Vx, Vy;        // vector chỉ phương, đã chuẩn hoá
        public Point2f Tam;          // trung tâm cạnh, hệ ROI
        public int SoDiem;
        public double RmsPx;
    }

    /// <summary>
    /// Khớp line qua đám mây điểm, chạy hai vòng: khớp -> bỏ điểm lạc -> khớp lại.
    /// Điểm lạc ở đây thường là cột mà argmax bám nhầm vào bóng sáng hoặc vào cạnh bên.
    /// </summary>
    private static Canh? KhopCanh(List<Point2f> diem)
    {
        var conLai = diem;

        for (int vong = 0; vong < 2; vong++)
        {
            if (conLai.Count < SoDiemToiThieu) return null;

            Line2D line = Cv2.FitLine(conLai, DistanceTypes.Huber, 0, 0.01, 0.01);
            double vx = line.Vx, vy = line.Vy;
            double x0 = line.X1, y0 = line.Y1;

            // Khoảng cách điểm -> line: tích có hướng với vector chỉ phương (FitLine đã chuẩn hoá).
            var phanDu = conLai.Select(p => Math.Abs((p.X - x0) * vy - (p.Y - y0) * vx)).ToList();

            if (vong == 0)
            {
                var sapXep = phanDu.OrderBy(d => d).ToList();
                double trungVi = Math.Max(0.8, sapXep[sapXep.Count / 2]);
                double nguong = trungVi * HeSoLoaiDiemLac;
                conLai = conLai.Where((p, i) => phanDu[i] <= nguong).ToList();
                continue;
            }

            // Vòng hai: chốt kết quả.
            double rms = Math.Sqrt(phanDu.Sum(d => d * d) / phanDu.Count);
            double xTb = conLai.Average(p => (double)p.X);
            double yTb = conLai.Average(p => (double)p.Y);

            // Chiếu trọng tâm lên line: bỏ phần lệch vuông góc, giữ phần dọc theo line.
            double t = (xTb - x0) * vx + (yTb - y0) * vy;
            return new Canh
            {
                Vx = vx,
                Vy = vy,
                Tam = new Point2f((float)(x0 + t * vx), (float)(y0 + t * vy)),
                SoDiem = conLai.Count,
                RmsPx = rms
            };
        }
        return null;
    }

    /// <summary>Góc của một vector chỉ phương so với phương NGANG, quy về (-90,90].</summary>
    private static double GocSoVoiNgang(double vx, double vy)
    {
        if (vx < 0) { vx = -vx; vy = -vy; }
        return Math.Atan2(vy, vx) * 180.0 / Math.PI;
    }

    // --------------------------------------------------------------------------------

    /// <summary>
    /// Vẽ chồng kết quả lên bản sao ảnh gốc: khung AI (vàng), điểm biên (xanh/cam),
    /// line khớp hai cạnh, line dọc ảo (xanh lá), trục thân tụ (đỏ), và số đo.
    /// </summary>
    private static Mat VeKetQua(Mat src, Rect roi, KetQuaGocNghieng kq,
                                List<Point2f> diemTren, List<Point2f> diemDuoi,
                                Canh? canhTren, Canh? canhDuoi)
    {
        Mat ve;
        if (src.Channels() == 1) { ve = new Mat(); Cv2.CvtColor(src, ve, ColorConversionCodes.GRAY2BGR); }
        else ve = src.Clone();

        Cv2.Rectangle(ve, roi, Scalar.Yellow, 1);

        foreach (var p in diemTren)
            Cv2.Circle(ve, (int)p.X + roi.X, (int)p.Y + roi.Y, 1, Scalar.DeepSkyBlue, -1);
        foreach (var p in diemDuoi)
            Cv2.Circle(ve, (int)p.X + roi.X, (int)p.Y + roi.Y, 1, Scalar.Orange, -1);

        // Hai cạnh kéo dài hết bề ngang khung cho dễ nhìn.
        VeLineCanh(ve, roi, canhTren, Scalar.DeepSkyBlue);
        VeLineCanh(ve, roi, canhDuoi, Scalar.Orange);

        // Vẽ hình học cả khi bị chốt kiểm tra loại — nhìn hình mới biết nó bám nhầm vào đâu.
        if (canhTren is not null && canhDuoi is not null)
        {
            Scalar mauTruc = kq.HopLe && string.IsNullOrEmpty(kq.CanhBao) ? Scalar.Red : Scalar.Magenta;

            // Line dọc ảo: dựng thẳng, đi qua trung điểm của hai tâm cạnh.
            var giua = new Point2f((kq.TamCanhTren.X + kq.TamCanhDuoi.X) / 2,
                                   (kq.TamCanhTren.Y + kq.TamCanhDuoi.Y) / 2);
            int nua = (int)(roi.Height * 0.6);
            Cv2.Line(ve, new Point((int)giua.X, (int)giua.Y - nua),
                         new Point((int)giua.X, (int)giua.Y + nua), Scalar.Lime, 1);

            // Trục thân tụ: nối hai trung tâm cạnh, kéo dài ra hai đầu cho thấy độ lệch.
            double vx = kq.TamCanhDuoi.X - kq.TamCanhTren.X;
            double vy = kq.TamCanhDuoi.Y - kq.TamCanhTren.Y;
            double d = Math.Sqrt(vx * vx + vy * vy);
            if (d > 1e-6)
            {
                vx /= d; vy /= d;
                Cv2.Line(ve, new Point((int)(giua.X - vx * nua), (int)(giua.Y - vy * nua)),
                             new Point((int)(giua.X + vx * nua), (int)(giua.Y + vy * nua)), mauTruc, 2);
            }

            Cv2.DrawMarker(ve, new Point((int)kq.TamCanhTren.X, (int)kq.TamCanhTren.Y),
                           Scalar.White, MarkerTypes.Cross, 11, 2);
            Cv2.DrawMarker(ve, new Point((int)kq.TamCanhDuoi.X, (int)kq.TamCanhDuoi.Y),
                           Scalar.White, MarkerTypes.Cross, 11, 2);

            Cv2.PutText(ve, kq.HopLe ? $"{kq.GocDo:+0.00;-0.00;0.00} do" : $"LOAI: {kq.GocDo:+0.00;-0.00;0.00} do",
                        new Point(roi.X, Math.Max(14, roi.Y - 6)),
                        HersheyFonts.HersheySimplex, 0.55, mauTruc, 2);
        }

        if (!kq.HopLe)
            Cv2.PutText(ve, kq.ThongDiep, new Point(Math.Max(4, roi.X - 120), Math.Min(ve.Height - 6, roi.Y + roi.Height + 16)),
                        HersheyFonts.HersheySimplex, 0.42, Scalar.Magenta, 1);

        return ve;
    }

    private static void VeLineCanh(Mat ve, Rect roi, Canh? canh, Scalar mau)
    {
        if (canh is null) return;
        var c = canh.Value;
        double nua = roi.Width * 0.55;
        var a = new Point((int)(c.Tam.X + roi.X - c.Vx * nua), (int)(c.Tam.Y + roi.Y - c.Vy * nua));
        var b = new Point((int)(c.Tam.X + roi.X + c.Vx * nua), (int)(c.Tam.Y + roi.Y + c.Vy * nua));
        Cv2.Line(ve, a, b, mau, 1);
    }
}
