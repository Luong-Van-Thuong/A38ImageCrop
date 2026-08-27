using OpenCvSharp;

namespace A38.ImageCrop;

/// <summary>
/// Tham số của bước 1a — trích model từ ảnh master cho tool dò mẫu kiểu CogPMAlign.
///
/// Model KHÔNG phải là ảnh nhị phân. Nó là danh sách điểm biên kèm hướng gradient
/// đã chuẩn hoá về độ dài 1, nên đổi phơi sáng / đổi độ tương phản không làm đổi model.
/// Đó là lý do ngưỡng nhị phân bị loại: Otsu tự thích nghi mà vẫn ăn mất một cạnh
/// dài ở 2/4 ảnh mẫu, còn gradient thì chỉ mất điểm chứ không sinh ra hình sai.
/// </summary>
public static class ModelCfg
{
    /// <summary>Ảnh master để trích model. Truyền đường dẫn sau --model thì cờ này bị ghi đè.</summary>
    public static string AnhMaster = "D:\\Images_\\V2\\CoilAssy\\CoilAssy\\1240S\\opencv\\1B.bmp";

    /// <summary>Vùng khoanh trên ảnh master. null = tự lấy theo thân vật.</summary>
    //public static Rect? VungKhoanh = new Rect(675, 155, 178, 154);
    public static Rect? VungKhoanh = null;

    /// <summary>Số mức kim tự tháp. 7 mức = L0 (1/1) đến L6 (1/64).</summary>
    public static int SoMuc = 7;

    public static int BlurKernel = 5;

    /// <summary>
    /// Ngưỡng Canny KHÔNG đặt cứng, mà tự tính từ phân bố gradient của chính ảnh đó,
    /// sao cho số pixel gradient mạnh ≈ hệ số này nhân chu vi ảnh (w + h).
    ///
    /// Hai điều đã đo được nằm sau con số này:
    ///  - Ngưỡng tuyệt đối sai y hệt ngưỡng nhị phân: với 40/120 cố định, cùng một mặt B,
    ///    ảnh 09-36-19 ra 2000 điểm còn ảnh 1B chỉ ra 99 điểm — model coi như rỗng.
    ///  - Nhưng lấy theo TỈ LỆ DIỆN TÍCH cũng sai: biên là đối tượng MỘT CHIỀU, số lượng
    ///    của nó tỉ lệ với chu vi chứ không với diện tích. Lấy 3% diện tích thì ở mức L0
    ///    hạn ngạch lên tới 150.000 pixel, thừa gấp chục lần chu vi thật, nên ngưỡng bị
    ///    kéo tụt xuống tận vùng nhiễu — đo được 302.749 pixel biên với ngưỡng 10/25.
    /// </summary>
    public static double HeSoMatDoBien = 8.0;

    /// <summary>Ngưỡng thấp của Canny = ngưỡng cao nhân hệ số này (trễ hysteresis).</summary>
    public static double CannyTiLeThap = 0.4;

    /// <summary>Sàn cho ngưỡng cao, chặn trường hợp ảnh phẳng lì sinh ra toàn nhiễu.</summary>
    public static int NguongBienToiThieu = 8;

    /// <summary>Khoảng cách tối thiểu giữa hai điểm model, tính bằng pixel CỦA MỨC ĐÓ.</summary>
    public static int KhoangCachDiem = 3;

    /// <summary>Chặn trên số điểm mỗi mức — mức L0 có thể ra hàng vạn điểm, không cần nhiều thế.</summary>
    public static int SoDiemToiDa = 2000;

    /// <summary>
    /// Coi vùng dây đồng là don't-care.
    ///
    /// Diện tích cuộn dây thay đổi giữa các con hàng nên nó là nguồn nhiễu, không phải
    /// thông tin. Bỏ nó ra khỏi model hình học. Riêng phần đồng thừa dùng để phân biệt
    /// 180° thì chấm bằng một ROI cục bộ riêng, không nằm trong model này.
    /// </summary>
    public static bool DongLaDontCare = true;

    /// <summary>Ngưỡng (R - B) để nhận ra màu đồng. Bền hơn HSV khi ảnh bị cháy sáng.</summary>
    public static int NguongDongRB = 25;
    public static int NguongSangBac = 150;

    /// <summary>
    /// Mở (opening) vùng đồng trước khi nới rộng, để xoá các vệt viền mảnh.
    ///
    /// Chỗ chuyển từ vật đen sang nền trắng luôn có quang sai màu: kênh R và kênh B
    /// không nét như nhau nên (R - B) vọt lên ngay TRÊN ĐƯỜNG BIÊN. Không mở thì bộ dò
    /// đồng nhận nhầm toàn bộ đường viền là đồng, rồi don't-care xoá mất đúng cái biên
    /// quý nhất — đo được ở ảnh 1B mức L3: 2581 pixel biên mà chỉ 106 lọt vào vùng
    /// quan tâm. Vệt quang sai rộng 1-3px, cuộn dây rộng hàng chục px, nên mở là tách được.
    /// </summary>
    public static int MoVungDong = 3;

    // Phải phân loại mọi bán kính morphology thành hai nhóm, lẫn lộn là hỏng — và
    // hỏng theo hai kiểu ngược nhau, cả hai đều đã đo được:
    //
    //  - VẬT LÝ (MoVungDong, NoiRongDontCare): mô tả kích thước có thật trên vật —
    //    vệt quang sai rộng 1-3px, lề an toàn quanh cuộn dây vài chục micromet.
    //    Phải làm ở ẢNH GỐC. Đem xuống từng mức thì ở L6 nó phình thành 2/43 kích thước
    //    ảnh, nuốt gần hết vùng quan tâm: đo được ảnh 1 ở L6 chỉ còn 126 ứng viên,
    //    ngưỡng tụt xuống sàn 3/8 và Canny bắt toàn nhiễu.
    //
    //  - DUNG SAI HÌNH HỌC (NoiRongThanTheoMuc): chỉ để mặt nạ nới ra ôm lấy đường biên.
    //    Phải làm THEO MỨC. Đặt 12px ở ảnh gốc thì xuống L6 còn 12/64 = 0.19px, tức là
    //    bằng không — mặt nạ cắt đúng vào giữa đường viền ngoài và xoá mất nó, model chỉ
    //    còn lại vòng tròn trong, mà vòng tròn thì không cho chút thông tin xoay nào.

    /// <summary>Lề an toàn quanh vùng đồng, tính ở ảnh gốc (bán kính vật lý).</summary>
    public static int NoiRongDontCare = 9;

    /// <summary>Nới thân vật, tính theo pixel của từng mức (dung sai hình học).</summary>
    public static int NoiRongThanTheoMuc = 3;

    /// <summary>
    /// Trần cho số pixel biên, tính theo tỉ lệ vùng quan tâm.
    ///
    /// Hạn ngạch theo chu vi mà lớn hơn cả số pixel đang có thì vòng tìm ngưỡng chạy
    /// thẳng xuống đáy histogram và ngưỡng sập về sàn. Trần này chặn đúng chỗ đó.
    /// </summary>
    public static double TiLeBienToiDa = 0.20;

    public static string ThuMucRa = "model_out";
}

/// <summary>
/// Một điểm của model: toạ độ so với gốc model, kèm vector gradient đã chuẩn hoá.
///
/// Chuẩn hoá về độ dài 1 chính là chỗ làm cho điểm số miễn nhiễm với độ sáng:
/// ảnh sáng lên gấp đôi thì gradient dài gấp đôi, nhưng hướng không đổi.
/// </summary>
public readonly struct DiemModel(float x, float y, float gx, float gy)
{
    public readonly float X = x, Y = y;
    public readonly float Gx = gx, Gy = gy;
}

/// <summary>Model ở một mức kim tự tháp.</summary>
public sealed class MucModel
{
    public int Muc;
    public double TiLe;
    public Size KichThuoc;
    public DiemModel[] Diem = [];
    public float BanKinh;

    // Số liệu chẩn đoán — để nhìn bảng là biết model nghèo điểm vì Canny hay vì don't-care.
    public double NguongThap, NguongCao;
    public int SoPixelBien;      // Canny bắt được bao nhiêu
    public int SoUngVien;        // trong đó bao nhiêu nằm trong vùng quan tâm

    /// <summary>
    /// ĐÒN BẨY XOAY, tính bằng pixel dịch chuyển trên mỗi độ xoay. Đây là chỉ số quan
    /// trọng nhất của model, vì nó dự báo thẳng độ nhọn của đỉnh trong bản đồ điểm số
    /// theo góc — tức là bước 1b có làm được hay không.
    ///
    /// Xoay một góc dθ làm điểm (x, y) dịch đi dθ·(-y, x). Điểm số chỉ đổi theo thành
    /// phần chiếu lên gradient, nên đóng góp của mỗi điểm là |x·gy - y·gx|.
    ///
    /// Hệ quả quan trọng: điểm nằm trên ĐƯỜNG TRÒN có gradient hướng tâm, vuông góc với
    /// hướng dịch chuyển, nên đóng góp đúng bằng KHÔNG dù nó nằm xa tâm đến đâu. Bán kính
    /// lớn không có nghĩa là có đòn bẩy — chỉ cạnh thẳng mới cho đòn bẩy.
    /// </summary>
    public double DonBayXoay;

    /// <summary>Đòn bẩy xoay so với bán kính hình học: 1.0 là dùng hết, 0 là vô dụng.</summary>
    public double TiLeDonBay => BanKinh < 1 ? 0 : DonBayXoay * 180.0 / Math.PI / BanKinh;

    /// <summary>
    /// Bước góc nên dùng ở mức này: góc làm điểm số dịch đi 1 pixel.
    ///
    /// Suy từ ĐÒN BẨY XOAY chứ không phải từ bán kính. Công thức atan(1/r) theo bán kính
    /// giả định mọi điểm ở bán kính r đều sinh chuyển động nhìn thấy được, nhưng đo được
    /// đòn bẩy thật chỉ bằng 19-26% bán kính (phần lớn điểm nằm trên vòng tròn trong,
    /// đóng góp bằng không). Lấy theo bán kính là quét thừa gấp 4-5 lần số góc cần thiết.
    /// </summary>
    public double BuocGocDo => DonBayXoay < 1e-6 ? 180 : 1.0 / DonBayXoay;

    public int SoGocPhu360 => (int)Math.Ceiling(360.0 / BuocGocDo);
}

/// <summary>Model đầy đủ: một danh sách điểm cho mỗi mức kim tự tháp.</summary>
public sealed class ModelDoMau
{
    public string TenMaster = "";
    public Rect VungKhoanh;
    public List<MucModel> Muc = [];
}

/// <summary>
/// Bước 1a: trích model từ ảnh master và vẽ ra để soi bằng mắt.
///
/// Chưa dò tìm gì cả. Mục đích của bước này chỉ là nhìn thấy model,
/// để biết nó bắt đúng đường viền hay đang bám vào nhiễu.
/// </summary>
public static class PatModel
{
    /// <summary>Điểm vào khi chạy với cờ --model.</summary>
    public static int ChayTrichModel(string[] args)
    {
        var danhSach = GomAnhMaster(args);
        if (danhSach.Count == 0)
        {
            Console.WriteLine("Khong tim thay anh master. Kiem tra ModelCfg.AnhMaster.");
            return 1;
        }

        Directory.CreateDirectory(ModelCfg.ThuMucRa);

        foreach (var path in danhSach)
        {
            Console.WriteLine($"\n================ {Path.GetFileNameWithoutExtension(path)} ================");
            using var master = Cv2.ImRead(path, ImreadModes.Color);
            if (master.Empty()) { Console.WriteLine($"Doc khong duoc: {path}"); continue; }

            var model = TrichModel(master, ModelCfg.VungKhoanh, Path.GetFileNameWithoutExtension(path));
            //InBang(model);
            VeModel(master, model);
        }

        Console.WriteLine($"\nDa ghi anh minh hoa vao thu muc: {Path.GetFullPath(ModelCfg.ThuMucRa)}");
        return 0;
    }

    /// <summary>Sau --model có thể là một file, một thư mục, hoặc không có gì (dùng ModelCfg).</summary>
    private static List<string> GomAnhMaster(string[] args)
    {
        int i = Array.IndexOf(args, "--model");
        string? tuThamSo = (i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("--")) ? args[i + 1] : null;
        var nguon = tuThamSo ?? ModelCfg.AnhMaster;

        if (Directory.Exists(nguon))
            return Directory.GetFiles(nguon)
                            .Where(f => Config.ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                            .OrderBy(f => f).ToList();

        return File.Exists(nguon) ? [nguon] : [];
    }

    // ---- Trích model ----------------------------------------------------------

    /// <summary>
    /// MẠCH CHÍNH của bước 1a, nhớ đúng thứ tự này:
    ///   ảnh màu -> thân vật -> vùng quan tâm (trừ đi don't-care) -> cắt theo vùng khoanh
    ///           -> với từng mức: làm mờ -> Canny chọn điểm -> Sobel lấy hướng
    ///           -> thưa điểm cho đều -> lưu toạ độ so với tâm
    /// </summary>
    public static ModelDoMau TrichModel(Mat master, Rect? vungKhoanh, string ten)
    {
        using var than = TachThan(master);
        using var dong = VungDong(master);

        var vung = vungKhoanh ?? Cv2.BoundingRect(than);
        vung = NoiRong(vung, 24, master.Size());



        using var xam = ToXam(master);
        using var xamCat = new Mat(xam, vung);
        using var thanCat = new Mat(than, vung);
        using var dongCat = new Mat(dong, vung);

        var model = new ModelDoMau { TenMaster = ten, VungKhoanh = vung };
        // phần kim tự tháp giảm dần
        for (int muc = 0; muc < ModelCfg.SoMuc; muc++)
        {
            double tiLe = 1.0 / (1 << muc);
            var kt = KichThuocMuc(vung, tiLe);

            using var anh = new Mat();
            Cv2.Resize(xamCat, anh, kt, 0, 0, InterpolationFlags.Area);
            using var care = CareChoMuc(thanCat, dongCat, kt);

            model.Muc.Add(TrichMotMuc(anh, care, muc, tiLe));
        }

        return model;
    }


    private static Size KichThuocMuc(Rect vung, double tiLe) =>
        new(Math.Max(8, (int)Math.Round(vung.Width * tiLe)),
            Math.Max(8, (int)Math.Round(vung.Height * tiLe)));

    /// <summary>
    /// Vùng quan tâm của MỘT mức: thu nhỏ thân và vùng đồng về kích thước mức đó rồi
    /// mới nới rộng, nên bán kính nới luôn đúng bằng vài pixel ở chính mức đang xét.
    /// </summary>
    private static Mat CareChoMuc(Mat thanCat, Mat dongCat, Size kt)
    {
        var care = new Mat();
        Cv2.Resize(thanCat, care, kt, 0, 0, InterpolationFlags.Area);
        Cv2.Threshold(care, care, 127, 255, ThresholdTypes.Binary);
        Cv2.Dilate(care, care, Dia(ModelCfg.NoiRongThanTheoMuc));
        Dbg.Show(care, $"Care {kt.Width}x{kt.Height}", true);

        using var dongL = new Mat();
        Cv2.Resize(dongCat, dongL, kt, 0, 0, InterpolationFlags.Area);
        Cv2.Threshold(dongL, dongL, 127, 255, ThresholdTypes.Binary);
        Dbg.Show(dongL, $"Dong {kt.Width}x{kt.Height}", true);
        if (Cv2.CountNonZero(dongL) > 0)
        {
            using var khongDong = new Mat();
            Cv2.BitwiseNot(dongL, khongDong);
            Dbg.Show(khongDong, $"Khong dong {kt.Width}x{kt.Height}", true);
            Cv2.BitwiseAnd(care, khongDong, care);
            Dbg.Show(care, $"Care - Dong {kt.Width}x{kt.Height}", true);

        }

        return care;
    }

    private static MucModel TrichMotMuc(Mat anh, Mat care, int muc, double tiLe)
    {
        int k = ModelCfg.BlurKernel | 1;                   // kernel Gauss bắt buộc lẻ
        using var mo = new Mat();
        Cv2.GaussianBlur(anh, mo, new Size(k, k), 0);

        using var dx = new Mat();
        using var dy = new Mat();
        // Tính toán xem tích vô hướng với mỗi kernel 3*3 của ảnh gốc trả về giá trị ở mỗi pixel như nào 
        // Tính theo dx
        Cv2.Sobel(mo, dx, MatType.CV_32F, 1, 0, 3);
        // Tính theo dy
        Cv2.Sobel(mo, dy, MatType.CV_32F, 0, 1, 3);
        // aCare chuyển ảnh toàn bộ vật màu trắng, phần đồng nhô ra màu đen background màu đen mảng 2D về 1D
        care.GetArray(out byte[] aCare);
        // aDx, aDy chuyển ảnh gradient theo dx, dy về mảng 1D, giá trị của từng pixel là bao nhiêu
        dx.GetArray(out float[] aDx);
        dy.GetArray(out float[] aDy);
        // Tính ngưỡng Canny từ chính ảnh: hạ dần ngưỡng cho tới khi số pixel gradient mạnh
        var (thap, cao) = NguongTuTinh(aDx, aDy, aCare, anh.Width + anh.Height);

        // Canny chỉ để CHỌN pixel nào là biên (nó đã làm non-max suppression sẵn).
        // Còn hướng gradient thì lấy từ Sobel, vì Canny không trả ra hướng.
        using var bien = new Mat();
        Cv2.Canny(mo, bien, thap, cao);
        Dbg.Show(bien, $"Bien muc {muc} ti le 1/{1 << muc}", true);
        // Chuyển đổi ảnh sau Canny sang mảng 1D, giá trị của từng pixel là 0 hoặc 255
        bien.GetArray(out byte[] aBien);

        int w = anh.Width, h = anh.Height;

        // Gom ứng viên: là biên, nằm trong vùng quan tâm, và gradient không quá yếu.
        var ungVien = new List<(int Idx, float Mag)>();
        // Tính khoảng cách của các điểm nếu lớn hơn 0 thì là điểm cần tìm
        for (int idx = 0; idx < aBien.Length; idx++)
        {
            if (aBien[idx] == 0 || aCare[idx] == 0) continue;
            float gx = aDx[idx], gy = aDy[idx];
            float mag = MathF.Sqrt(gx * gx + gy * gy);
            if (mag < 1e-3f) continue;
            ungVien.Add((idx, mag));
        }

        // Điểm khoẻ được ưu tiên, nhưng mỗi ô lưới chỉ nhận MỘT điểm.
        // Không làm thế thì một vùng nhiều nhiễu sẽ chiếm hết chỗ và model
        // mất cân đối — phần lớn điểm dồn vào một góc, đòn bẩy góc coi như mất.
        ungVien.Sort((a, b) => b.Mag.CompareTo(a.Mag));
        // oLuoi = 3
        
        int oLuoi = Math.Max(1, ModelCfg.KhoangCachDiem);
        int cotLuoi = (w + oLuoi - 1) / oLuoi;
        var daChiem = new bool[cotLuoi * ((h + oLuoi - 1) / oLuoi)];

        float cx = w / 2f, cy = h / 2f;
        var diem = new List<DiemModel>();
        float banKinh = 0;
        int soDiemMax = (int)Math.Ceiling(Math.Sqrt(w * h / (double)ModelCfg.SoDiemToiDa));
        foreach (var (idx, mag) in ungVien)
        {
            // if (diem.Count >= ModelCfg.SoDiemToiDa) break;
            if (diem.Count >= soDiemMax) break;

            int x = idx % w, y = idx / w;
            int o = (y / oLuoi) * cotLuoi + (x / oLuoi);
            if (daChiem[o]) continue;
            daChiem[o] = true;

            float gx = aDx[idx] / mag, gy = aDy[idx] / mag;
            float px = x - cx, py = y - cy;
            diem.Add(new DiemModel(px, py, gx, gy));
            banKinh = MathF.Max(banKinh, MathF.Sqrt(px * px + py * py));
        }

        // Đòn bẩy xoay: trung bình bình phương của |x·gy - y·gx|, đổi sang pixel trên mỗi độ.
        double tongBinh = 0;
        foreach (var d in diem)
        {
            double don = d.X * d.Gy - d.Y * d.Gx;
            tongBinh += don * don;
        }
        double donBay = diem.Count == 0 ? 0 : Math.Sqrt(tongBinh / diem.Count) * Math.PI / 180.0;

        return new MucModel
        {
            Muc = muc,
            TiLe = tiLe,
            KichThuoc = new Size(w, h),
            Diem = [.. diem],
            BanKinh = banKinh,
            DonBayXoay = donBay,
            NguongThap = thap,
            NguongCao = cao,
            SoPixelBien = Cv2.CountNonZero(bien),
            SoUngVien = ungVien.Count
        };
    }

    /// <summary>
    /// Ngưỡng Canny tính từ chính ảnh: hạ dần ngưỡng cho tới khi số pixel gradient mạnh
    /// trong vùng quan tâm đạt <see cref="ModelCfg.HeSoMatDoBien"/> × chuVi.
    ///
    /// Dùng chuẩn L1 (|gx| + |gy|) chứ không phải căn bậc hai, vì Canny với
    /// L2gradient = false cũng tính đúng như vậy — sai chuẩn là ngưỡng lệch hẳn một hệ số.
    /// Đếm bằng histogram thay vì sắp xếp: ảnh L0 có 7.7 triệu pixel, sắp xếp là phí.
    /// </summary>
    private static (double Thap, double Cao) NguongTuTinh(float[] aDx, float[] aDy, byte[] aCare, int chuVi)
    {
        const int SoBin = 2048;                      // |gx|+|gy| tối đa của Sobel 3x3 trên ảnh 8-bit
        var hist = new int[SoBin + 1];
        long tong = 0;

        // kiểm tra xem giá trị ở pixel đó thuộc vùng có giá trị bao nhiêu
        for (int i = 0; i < aCare.Length; i++)
        {
            if (aCare[i] == 0) continue;
            int m = (int)(MathF.Abs(aDx[i]) + MathF.Abs(aDy[i]));
            // Nếu m > SoBin thì tăng giá trị của vị trí SoBin
            // Nếu m < SoBin thì tăng giá trị của vị trí m
            hist[m > SoBin ? SoBin : m]++;
            tong++;
        }

        // Hạn ngạch theo CHU VI, không theo diện tích — và không bao giờ vượt quá
        // toàn bộ số pixel quan tâm, để ảnh bé không tụt thẳng xuống sàn.
        long can = Math.Min((long)(tong * ModelCfg.TiLeBienToiDa), (long)(ModelCfg.HeSoMatDoBien * chuVi));
        long dem = 0;
        int muc = SoBin;
        // Cộng từ trên 2047 xuống đến khi tổng số pixel gradient mạnh đạt hạn ngạch can
        while (muc > 1 && dem < can) dem += hist[muc--];

        double cao = Math.Max(ModelCfg.NguongBienToiThieu, muc);
        // Tìm được giá trị của những phần tử cao nhất
        return (cao * ModelCfg.CannyTiLeThap, cao);
    }

    // ---- Vùng quan tâm / don't-care -------------------------------------------

    /// <summary>
    /// Thân vật: min(B,G,R) + Otsu + blob lớn nhất + bao lồi.
    ///
    /// Dùng min của ba kênh chứ không dùng ảnh xám, vì dây đồng cháy sáng ngang với
    /// nền backlight trên ảnh xám — đo được: Otsu trên ảnh xám ăn mất nguyên một cạnh
    /// dài, diện tích còn 2.13M thay vì 6.90M. Đồng có kênh B thấp nên min kéo nó xuống.
    /// Bao lồi để vá lại chỗ vẫn bị ăn khi đồng cháy cả ba kênh.
    /// </summary>
    private static Mat TachThan(Mat src)
    {
        var kenh = Cv2.Split(src);
        Dbg.Show(src, "src");
        using var min = new Mat();
        Cv2.Min(kenh[0], kenh[1], min);
        Cv2.Min(min, kenh[2], min);
        foreach (var c in kenh) c.Dispose();

        using var bin = new Mat();
        Cv2.Threshold(min, bin, 0, 255, ThresholdTypes.BinaryInv | ThresholdTypes.Otsu);
        Dbg.Show(bin, "bin");
        Cv2.FindContours(bin, out Point[][] cts, out _,
                         RetrievalModes.External, ContourApproximationModes.ApproxNone);

        var than = Mat.Zeros(src.Size(), MatType.CV_8UC1).ToMat();
        if (cts.Length == 0) return than;
        Dbg.Show(than, "than");
        var ngoai = cts.OrderByDescending(c => Cv2.ContourArea(c)).First();
        Cv2.DrawContours(than, new[] { Cv2.ConvexHull(ngoai) }, -1, Scalar.All(255), -1);
        Dbg.Show(than, "Than", true);
        return than;
    }

    /// <summary>
    /// Vùng dây đồng ở ảnh gốc — phần sẽ bị đánh dấu don't-care.
    ///
    /// Dùng (R - B) chứ không dùng HSV: khi đồng bị cháy sáng thì độ bão hoà tụt xuống
    /// và HSV mất dấu, còn hiệu hai kênh vẫn còn dương. Bước opening ở đây là bán kính
    /// VẬT LÝ nên phải làm ở ảnh gốc, trước khi thu nhỏ.
    /// </summary>
    private static Mat VungDong(Mat src)
    {
        Dbg.Show(src, "src");
        var dong = Mat.Zeros(src.Size(), MatType.CV_8UC1).ToMat();
        if (!ModelCfg.DongLaDontCare || src.Channels() < 3) return dong;

        var kenh = Cv2.Split(src);
        using var hieu = new Mat();
        Cv2.Subtract(kenh[2], kenh[0], hieu);            // R - B
        foreach (var c in kenh) c.Dispose();
        Dbg.Show(hieu, "hieu");
        Cv2.Threshold(hieu, dong, ModelCfg.NguongDongRB, 255, ThresholdTypes.Binary);
        Dbg.Show(hieu, "hieu");
        if (ModelCfg.MoVungDong > 0)
        {
            Cv2.MorphologyEx(dong, dong, MorphTypes.Open, Dia(ModelCfg.MoVungDong));
            Dbg.Show(dong, "dong");
        }

        if (ModelCfg.NoiRongDontCare > 0)
        {
            Cv2.Dilate(dong, dong, Dia(ModelCfg.NoiRongDontCare));
            Dbg.Show(dong, "dong");
        }
        return dong;
    }

    private static Mat Dia(int r) =>
        Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(2 * r + 1, 2 * r + 1));

    private static Mat ToXam(Mat src)
    {
        if (src.Channels() == 1) return src.Clone();
        var xam = new Mat();
        Cv2.CvtColor(src, xam, src.Channels() == 4
            ? ColorConversionCodes.BGRA2GRAY
            : ColorConversionCodes.BGR2GRAY);
        return xam;
    }

    private static Rect NoiRong(Rect r, int pad, Size khung) =>
        new Rect(r.X - pad, r.Y - pad, r.Width + 2 * pad, r.Height + 2 * pad)
            .Intersect(new Rect(0, 0, khung.Width, khung.Height));

    // ---- In số liệu + vẽ ------------------------------------------------------

    private static void InBang(ModelDoMau model)
    {
        Console.WriteLine($"vung khoanh = {model.VungKhoanh}");
        Console.WriteLine("  muc   ti le    kich thuoc   nguong   px bien   ung vien   so diem   ban kinh   don bay xoay   buoc goc   goc/360");
        foreach (var m in model.Muc)
            Console.WriteLine($"  L{m.Muc}    1/{1 << m.Muc,-4} {m.KichThuoc.Width,4}x{m.KichThuoc.Height,-4}  " +
                              $"{m.NguongThap,4:F0}/{m.NguongCao,-4:F0} {m.SoPixelBien,8}  {m.SoUngVien,8}  " +
                              $"{m.Diem.Length,8}  {m.BanKinh,8:F1}  {m.DonBayXoay,7:F3} px/do ({m.TiLeDonBay * 100,4:F0}%)  " +
                              $"{m.BuocGocDo,7:F2}°  {m.SoGocPhu360,7}");
    }

    /// <summary>
    /// Vẽ model đè lên ảnh master: mỗi điểm một chấm + một vạch ngắn theo hướng gradient,
    /// màu mã hoá hướng. Vùng don't-care tô đỏ mờ.
    ///
    /// Đây là sản phẩm chính của bước 1a — nhìn ảnh này là biết model có bám đúng
    /// đường viền không, hay đang bám vào vân bề mặt và nhiễu.
    /// </summary>
    private static void VeModel(Mat master, ModelDoMau model)
    {
        using var than = TachThan(master);
        using var dong = VungDong(master);
        using var thanCat = new Mat(than, model.VungKhoanh);
        using var dongCat = new Mat(dong, model.VungKhoanh);
        using var lut = BangMauHuong();

        foreach (var m in model.Muc)
        {
            // Mức thô ảnh bé tí, phóng to lên mới nhìn thấy điểm.
            int phong = Math.Max(1, 900 / Math.Max(m.KichThuoc.Width, m.KichThuoc.Height));
            var kt = new Size(m.KichThuoc.Width * phong, m.KichThuoc.Height * phong);

            using var nen = new Mat(master, model.VungKhoanh);
            using var ve = new Mat();
            Cv2.Resize(nen, ve, kt, 0, 0, InterpolationFlags.Area);

            // Vẽ đúng mặt nạ mà mức này thật sự dùng, rồi phóng to — nhìn là biết ngay
            // mặt nạ có ăn vào đường viền hay không.
            using var careMuc = CareChoMuc(thanCat, dongCat, m.KichThuoc);
            using var careNho = new Mat();
            Cv2.Resize(careMuc, careNho, kt, 0, 0, InterpolationFlags.Nearest);
            using var khongCare = new Mat();
            Cv2.BitwiseNot(careNho, khongCare);
            using var phu = ve.Clone();
            phu.SetTo(new Scalar(0, 0, 200), khongCare);
            Cv2.AddWeighted(ve, 0.65, phu, 0.35, 0, ve);

            float cx = kt.Width / 2f, cy = kt.Height / 2f;
            int daiVach = Math.Max(3, phong * 2);
            int banKinhCham = Math.Max(1, phong / 2);

            foreach (var d in m.Diem)
            {
                float x = cx + d.X * phong, y = cy + d.Y * phong;
                double goc = Math.Atan2(d.Gy, d.Gx) * 180.0 / Math.PI;
                var mau = lut.Get<Vec3b>(0, (int)(((goc + 360) % 360) / 2));

                Cv2.Line(ve, new Point(x, y), new Point(x + d.Gx * daiVach, y + d.Gy * daiVach),
                         new Scalar(mau.Item0, mau.Item1, mau.Item2), 1, LineTypes.AntiAlias);
                Cv2.Circle(ve, new Point(x, y), banKinhCham,
                           new Scalar(mau.Item0, mau.Item1, mau.Item2), -1, LineTypes.AntiAlias);
            }

            Cv2.PutText(ve, $"L{m.Muc}  1/{1 << m.Muc}  {m.Diem.Length} diem  r={m.BanKinh:F0}px  " +
                            $"buoc goc {m.BuocGocDo:F2}deg", new Point(10, 26),
                        HersheyFonts.HersheySimplex, 0.7, Scalar.Lime, 2);

            var file = Path.Combine(ModelCfg.ThuMucRa, $"{model.TenMaster}_L{m.Muc}.png");
            Cv2.ImWrite(file, ve);
        }
    }

    /// <summary>Bảng 180 màu theo hướng gradient (hue của OpenCV chạy 0..179).</summary>
    private static Mat BangMauHuong()
    {
        using var hsv = new Mat(1, 180, MatType.CV_8UC3);
        for (int i = 0; i < 180; i++) hsv.Set(0, i, new Vec3b((byte)i, 255, 255));
        var bgr = new Mat();
        Cv2.CvtColor(hsv, bgr, ColorConversionCodes.HSV2BGR);
        return bgr;
    }
}
