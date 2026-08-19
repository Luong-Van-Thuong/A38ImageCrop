using OpenCvSharp;
using OpenCvSharp.XImgProc;
using OpenCvSharp.XPhoto;
using System.Diagnostics.Metrics;
using System.Net.WebSockets;
using System.Runtime.Intrinsics.X86;

namespace A38.ImageCrop;

public enum Goc
{
    TrenTrai,
    TrenPhai,
    DuoiPhai,
    DuoiTrai
}

public static class GocCfg
{
    public static double RoiW = 0.20;
    public static double RoiH = 0.20;

    public static double LechTren = 0.05;

    public static double LechDuoi = 0.10;

    public static Goc[] CacGocCanSoi = { Goc.TrenTrai, Goc.TrenPhai, Goc.DuoiPhai, Goc.DuoiTrai };
}

public static class Config
{
    public static int WorkWidth = 1000;

    public static int BlurKernel = 5;

    public static int CannyLow = 50;
    public static int CannyHigh = 150;

    public static int MorphKernel = 5;

    public static double MinAreaRatio = 0.02;

    public static double ApproxEpsRatio = 0.02;

    public static bool WarpIfQuad = true;

    public static string InputImagePath = "D:\\Images_\\V2\\CoilAssy\\CoilAssy\\1240S\\opencv\\Image__2026-07-28__09-36-30.bmp";

    
    //public static string InputFolderPath = "D:\\Images_\\JeaYoung\\Coil_Check_Co_Khong_Nghieng\\1CamChieuThang\\Coil\\OPENCV\\anh1";
    //public static string InputFolderPath = "D:\\Images_\\JeaYoung\\Coil_Check_Co_Khong_Nghieng\\1CamChieuThang\\Coil\\OPENCV\\anh2";
    public static string InputFolderPath = "D:\\Images_\\JeaYoung\\Coil_Check_Co_Khong_Nghieng\\1CamChieuThang\\Coil\\OPENCV\\Ng";
    //public static string InputFolderPath = "D:\\Images_\\JeaYoung\\Coil_Check_Co_Khong_Nghieng\\1CamChieuThang\\Coil\\OPENCV\\Ng_";

    public static string[] ImageExtensions = { ".bmp", ".png", ".jpg", ".jpeg", ".tif", ".tiff" };

    public static bool Recursive = false;

    public static bool SaveDebugStepsInBatch = false;

    public static int SoLuong = 4;

    /// <summary>File Excel gom so lieu align cua ca luot chay. De rong = khong ghi.</summary>
    public static string ExcelOutPath = "align_data_2.xlsx";
}

public static class ProtrusionCfg
{
    public static double MinObjectRatio = 0.05;
    public static double MaxObjectRatio = 0.97;

    public static int DiskRadius = 80;

    public static double OpenDownScale = 0.5;

    public static double MinAreaRatio = 0.0005;
    public static double MaxAreaRatio = 0.20;

    public static double MinAspect = 1.1;
    public static double MaxAspect = 10.0;

    public static int TouchDilate = 5;

    public static int BorderMargin = 3;

    public static int BboxPadding = 20;
}

public static class Program
{
    private static readonly string _outDirG2 = "imgCheck";

    private static readonly string _outDirNotFound = "sut_me_botton_not_found";

    [ThreadStatic] private static string? _baseName;
    [ThreadStatic] private static int _demAnhG2;

    /// <summary>Đặt lại tên gốc + bộ đếm, gọi một lần cho mỗi ảnh đầu vào (trong đúng luồng đó).</summary>
    private static void ResetLuuAnhG2(string inputPath)
    {
        _baseName = Path.GetFileNameWithoutExtension(inputPath);
        _demAnhG2 = 0;
    }

    /// <summary>
    /// Ghi một ảnh của giai đoạn 2 ra thư mục output.
    /// Tên file có số thứ tự tăng dần nên nhìn thư mục là biết ngay thứ tự tìm ra.
    /// </summary>
    private static string LuuAnhG2(Mat img, string nameFolder, string tag)
    {
        Directory.CreateDirectory(nameFolder);
        var path = Path.Combine(nameFolder, $"{tag}.png");
        Cv2.ImWrite(path, img);
        Dbg.Log($"da luu anh giai doan 2: {path}");
        return path;
    }

    public static int Main(string[] args)
    {
        Dbg.Enabled = !args.Contains("--no-debug");
        Dbg.ShowWindow = !args.Contains("--no-window");
        Dbg.Pause = !args.Contains("--no-pause");
        Dbg.SaveFile = true;

        if (args.Contains("--debug-steps")) Config.SaveDebugStepsInBatch = true;

        // Buoc 1a cua tool do mau: chi trich model tu anh master roi ve ra, khong chay pipeline cat anh.
        if (args.Contains("--model")) 
            return PatModel.ChayTrichModel(args);

        // Bai hoc do mau ban tho: chay tung buoc mot de hieu shape-based tu goc.
        if (args.Contains("--hoc")) 
            return HocDoMau.Chay(args);

        int i = Array.IndexOf(args, "--only");
        if (i >= 0 && i + 1 < args.Length) Dbg.Filter = args[i + 1];

        var danhSachAnh = ThuThapAnhVao(args);
        if (danhSachAnh.Count == 0)
        {
            Console.WriteLine("Khong co anh nao de chay. Kiem tra lai Config.InputFolderPath / Config.InputImagePath.");
            return 1;
        }

        bool chayCaMe = danhSachAnh.Count > 1;
        if (chayCaMe)
        {
            Dbg.ShowWindow = false;
            Dbg.Pause = false;
            Dbg.SaveFile = Config.SaveDebugStepsInBatch;
            Console.WriteLine($"Che do CHAY CA THU MUC: {danhSachAnh.Count} anh " +
                              $"(da tu tat cua so + dung cho phim" +
                              $"{(Config.SaveDebugStepsInBatch ? "" : ", khong ghi anh tung buoc")}).");
        }

        int soThanhCong = 0, soKhongThayPhanNho = 0, soLoi = 0;
        var thoiDiemBatDau = DateTime.Now;

        var congViec = danhSachAnh.Select((p, n) => (Path: p, ThuTu: n + 1)).ToList();

        int soLuong = Math.Max(1, Config.SoLuong);
        bool chaySongSong = chayCaMe && soLuong > 1;

        if (chaySongSong)
        {
            Dbg.SongSong = true;
            Console.WriteLine($"Chay SONG SONG {soLuong} luong (moi luong anh). " +
                              $"Log cua moi anh khi anh do chay, " +
                              $"nen thu tu khoi khong theo thu tu file.");
            Console.WriteLine();

            Parallel.ForEach(
                congViec,
                new ParallelOptions { MaxDegreeOfParallelism = soLuong },
                cv =>
                {
                    switch (ChayMotViec(cv.Path, cv.ThuTu, danhSachAnh.Count, chayCaMe))
                    {
                        case KetQuaXuLy.ThanhCong: Interlocked.Increment(ref soThanhCong); break;
                        case KetQuaXuLy.KhongThayPhanNho: Interlocked.Increment(ref soKhongThayPhanNho); break;
                        default: Interlocked.Increment(ref soLoi); break;
                    }
                });

            Dbg.SongSong = false;
        }
        else
        {
            foreach (var cv in congViec)
            {
                switch (ChayMotViec(cv.Path, cv.ThuTu, danhSachAnh.Count, chayCaMe))
                {
                    case KetQuaXuLy.ThanhCong: soThanhCong++; break;
                    case KetQuaXuLy.KhongThayPhanNho: soKhongThayPhanNho++; break;
                    default: soLoi++; break;
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine($"===== XONG {danhSachAnh.Count} anh trong {(DateTime.Now - thoiDiemBatDau).TotalSeconds:0.0}s" +
                          $"{(chaySongSong ? $" ({soLuong} luong)" : "")} =====");
        Console.WriteLine($"  bat duoc phan nho  : {soThanhCong}");
        Console.WriteLine($"  khong thay phan nho: {soKhongThayPhanNho}");
        Console.WriteLine($"  loi                : {soLoi}");
        Console.WriteLine($"  anh ket qua nam trong: {Path.GetFullPath(_outDirG2)}");

        XuatExcelAlign();

        Dbg.CloseAll();
        return soLoi > 0 ? 2 : 0;
    }

    /// <summary>
    /// Ghi kho số liệu align ra Excel. Gọi đúng một lần ở cuối lượt chạy, sau khi
    /// mọi luồng đã xong — ghi giữa chừng thì file thiếu ảnh mà lại tốn công mở/đóng.
    /// Hỏng ở đây không được làm hỏng lượt chạy: ảnh đã cắt vẫn còn nguyên trong thư mục.
    /// </summary>
    private static void XuatExcelAlign()
    {
        if (string.IsNullOrWhiteSpace(Config.ExcelOutPath)) return;

        try
        {
            var duongDan = ExcelAlign.XuatFile(Config.ExcelOutPath);
            Console.WriteLine(duongDan is null
                ? "  excel align         : khong co dong nao de ghi"
                : $"  excel align ({ExcelAlign.SoDong} dong): {duongDan}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  excel align         : ghi that bai - {ex.Message}");
        }
    }

    private static readonly object _khoaConsole = new();

    /// <summary>
    /// Chạy một ảnh và in trọn khối log của nó. Dùng chung cho cả chạy tuần tự lẫn song song,
    /// nên hai đường chạy không bao giờ lệch hành vi nhau.
    /// </summary>
    private static KetQuaXuLy ChayMotViec(string inputPath, int thuTu, int tong, bool chayCaMe)
    {
        var tenAnh = Path.GetFileNameWithoutExtension(inputPath);

        if (chayCaMe || Config.SaveDebugStepsInBatch) Dbg.BatDauLuong(Path.Combine("debug_out", tenAnh));

        Dbg.Info($"===== [{thuTu}/{tong}] {Path.GetFileName(inputPath)} =====");

        KetQuaXuLy ketQua;
        try
        {
            ketQua = XuLyMotAnh(inputPath);
        }
        catch (Exception ex)
        {
            ketQua = KetQuaXuLy.Loi;
            Dbg.Info($"  Loi khi xu ly{Path.GetFileName(inputPath)}: {ex.Message}");
        }

        var log = Dbg.KetThucLuong();
        if (log.Length > 0)
        {
            lock (_khoaConsole)
            {
                Console.Write(log);
                Console.WriteLine();
            }
        }

        return ketQua;
    }

    private enum KetQuaXuLy
    {
        ThanhCong,
        KhongThayPhanNho,
        Loi
    }

    /// <summary>
    /// Gom danh sách ảnh cần chạy, theo thứ tự ưu tiên:
    ///   1. Config.InputFolderPath  (chạy cả thư mục)
    ///   2. Config.InputImagePath   (chạy đúng 1 ảnh)
    ///   3. tham số dòng lệnh       (file hoặc thư mục đều nhận)
    ///   4. tự tạo sample.png
    /// </summary>
    private static List<string> ThuThapAnhVao(string[] args)
    {
        if (!string.IsNullOrWhiteSpace(Config.InputFolderPath))
        {
            if (!Directory.Exists(Config.InputFolderPath))
            {
                Console.WriteLine($"Khong tim thay thu muc: {Config.InputFolderPath}");
                return new List<string>();
            }
            var ds = QuetThuMuc(Config.InputFolderPath);
            Console.WriteLine($"Thu muc vao: {Config.InputFolderPath} -> {ds.Count} anh");
            return ds;
        }

        var duongDan = !string.IsNullOrWhiteSpace(Config.InputImagePath)
                       ? Config.InputImagePath
                       : args.FirstOrDefault(a => !a.StartsWith("--"));

        if (string.IsNullOrWhiteSpace(duongDan))
            duongDan = EnsureSampleImage();

        if (Directory.Exists(duongDan))
        {
            var ds = QuetThuMuc(duongDan);
            Console.WriteLine($"Thu muc vao: {duongDan} -> {ds.Count} anh");
            return ds;
        }

        if (!File.Exists(duongDan))
        {
            Console.WriteLine($"Khong tim thay anh: {duongDan}");
            return new List<string>();
        }

        return new List<string> { duongDan };
    }

    /// <summary>Lấy các file ảnh trong thư mục, sắp theo tên cho lần chạy nào cũng cùng thứ tự.</summary>
    private static List<string> QuetThuMuc(string folder)
    {
        var option = Config.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return Directory.EnumerateFiles(folder, "*.*", option)
                        .Where(f => Config.ImageExtensions.Contains(
                                        Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                        .ToList();
    }

    /// <summary>
    /// Chạy trọn pipeline cho MỘT ảnh. Tách hẳn ra khỏi Main để vòng foreach ở trên
    /// gọn và để mọi Mat trong này chắc chắn được Dispose trước khi sang ảnh kế tiếp
    /// — ảnh 3648x3648 mà rò vài chục cái là hết RAM giữa chừng.
    /// </summary>
    private static KetQuaXuLy XuLyMotAnh(string inputPath)
    {
        using var src = Cv2.ImRead(inputPath, ImreadModes.Color);
        string nameImg = Path.GetFileName(inputPath);
        string[] nameImgResult = nameImg.Split('.');
        string nameImgMain = nameImgResult[0];

        if (src.Empty())
        {
            Dbg.Info($"  Doc anh that bai: {inputPath}");
            return KetQuaXuLy.Loi;
        }

        Dbg.Info($"  Anh vao: {src.Width}x{src.Height}");
        Dbg.Reset();

        using var result = CropLargestRegion(src, nameImgMain);
        Dbg.Show(result, "Anh sau khi cat");

        if (result is null || result.Empty())
        {
            Dbg.Info("  Khong do duoc vung nao giam Config.CannyLow or Config.MinAreaRatio.");
            return KetQuaXuLy.Loi;
        }

        //Directory.CreateDirectory(_outDirG2);
        //var outPath = Path.Combine(_outDirG2, _baseName + "_crop.png");

        //var cacGoc = CropSmallRegion(result, nameImgMain);
        try
        {
            //var gocBatDuoc = cacGoc.Where(g => g.Anh is not null).ToList();

            //if (gocBatDuoc.Count != 2)
            //{
            //    Dbg.Info("  Giai doan 2: Ca 4 goc deu chua bat duoc phan nho " +
            //             "(xem log 'ratio mask' va anh 6_Residual cua tung goc).");
            //    LuuAnhG2(src, _outDirNotFound, $"{nameImgMain}");
            //    return KetQuaXuLy.KhongThayPhanNho;
            //}

            //var motTa = string.Join(", ", gocBatDuoc.Select(g => $"{g.Goc}({g.Anh!.Width}x{g.Anh.Height})"));
            //Dbg.Info($"  Giai doan 2: bat duoc {gocBatDuoc.Count}/{cacGoc.Count} goc -> {motTa}; " +
            //         $"da luu {_demAnhG2} anh.");
            return KetQuaXuLy.ThanhCong;
        }
        finally
        {
            //foreach (var g in cacGoc) g.Anh?.Dispose();
        }
    }

    /// <summary>
    /// Pipeline chính: dò vùng lớn nhất trong ảnh rồi cắt ra.
    /// Mỗi bước đều có Dbg.Show để bạn nhìn thấy ảnh biến đổi thế nào.
    /// </summary>
    private static Mat? CropLargestRegion_Old(Mat src, string nameImgMain)
    {
        double scale = Math.Min(1.0, (double)Config.WorkWidth / src.Width);
        using var work = new Mat();
        if (scale < 1.0)
            Cv2.Resize(src, work, new Size(), scale, scale, InterpolationFlags.Area);
        else
            src.CopyTo(work);

        using var gray = new Mat();

        Cv2.CvtColor(work, gray, ColorConversionCodes.BGR2GRAY);
        Dbg.Show(gray, "gray");

        using var blur = new Mat();

        Cv2.GaussianBlur(gray, blur, new Size(Config.BlurKernel, Config.BlurKernel), 0);
        Dbg.Show(blur, "blur");

        using var edges = new Mat();
        Cv2.Canny(blur, edges, Config.CannyLow, Config.CannyHigh);
        Dbg.Show(edges, "canny");

        using var closed = new Mat();
        if (Config.MorphKernel > 0)
        {
            using var kernel = Cv2.GetStructuringElement(
                MorphShapes.Rect, new Size(30, 30));
            Cv2.MorphologyEx(edges, closed, MorphTypes.Close, kernel);
            Dbg.Show(closed, "morph_close");
        }
        else
        {
            edges.CopyTo(closed);
        }

        Cv2.FindContours(closed, out Point[][] contours, out _,
            RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        Dbg.Show(closed, "morph_close");

        var imgContours = gray.Clone();
        Cv2.CvtColor(imgContours, imgContours, ColorConversionCodes.GRAY2BGR);
        for (int i = 0; i < contours.Length; i++)
        {
            byte hue = (byte)(i * 179 / contours.Length);
            using Mat hsvPixel = new Mat(1, 1, MatType.CV_8UC3, new Scalar(hue, 255, 255));
            using Mat bgrPixel = new Mat();

            Cv2.CvtColor(hsvPixel, bgrPixel, ColorConversionCodes.HSV2BGR);
            Vec3b bgr = bgrPixel.At<Vec3b>(0, 0);
            Scalar color = new Scalar(bgr.Item0, bgr.Item1, bgr.Item2);

            Cv2.DrawContours(imgContours, contours, i, color, 2);
            Dbg.Show(imgContours, "imgContours");
        }
        Dbg.Show(imgContours, "imgContours");

        double imageArea = work.Width * (double)work.Height;
        var candidates = contours
            .Select(c => (Contour: c, Area: Cv2.ContourArea(c)))
            .Where(x => x.Area >= imageArea * Config.MinAreaRatio)
            .OrderByDescending(x => x.Area)
            .ToList();

        Dbg.Log($"con {candidates.Count} contour sau khi loc dien tich " +
                $"(>= {Config.MinAreaRatio:P0} anh = {imageArea * Config.MinAreaRatio:0} px)");

        if (candidates.Count == 0) return null;

        foreach (var (_, area) in candidates.Take(5))
        {
            Dbg.Log($"  - dien tich {area:0} px ({area / imageArea:P1} anh)");
        }

        if (Dbg.Enabled)
        {
            using var overlay = work.Clone();
            Cv2.DrawContours(overlay, candidates.Select(x => x.Contour).ToArray(), -1,
                new Scalar(0, 255, 0), 2);
            Cv2.DrawContours(overlay, new[] { candidates[0].Contour }, -1,
                new Scalar(0, 0, 255), 3);
            //Cv2.DrawContours(overlay, new[] { candidates[1].Contour }, -1,
            //    new Scalar(255, 0, 0), 3);
            Dbg.Show(overlay, "contours");
        }

        //return imgContours;
        var best = candidates[0].Contour;

        double peri = Cv2.ArcLength(best, true);
        var approx = Cv2.ApproxPolyDP(best, Config.ApproxEpsRatio * peri, true);
        //Dbg.Show(approx, "");
        Dbg.Log($"Xap xi da giac: {approx.Length} dinh (eps = {Config.ApproxEpsRatio * peri:0.#})");
        var r = Cv2.BoundingRect(best);
        int paddingFull = 20;
        var full = new Rect(
            (int)(r.X / scale - paddingFull), (int)(r.Y / scale - paddingFull),
            (int)(r.Width / scale + 2 * paddingFull), (int)(r.Height / scale + 2 * paddingFull))
            .Intersect(new Rect(0, 0, src.Width, src.Height));

        Dbg.Log($"Cat theo hinh chu nhat bao quanh: {full}");
        var cropped = new Mat(src, full).Clone();
        Dbg.Show(cropped, "result_crop");
        LuuAnhG2(cropped, _outDirG2, $"{nameImgMain}");
        return cropped;

    }
    private static Mat Dia(int r) =>
    Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(2 * r + 1, 2 * r + 1));
    private static Mat VungDong(Mat src)
    {
        var dong = Mat.Zeros(src.Size(), MatType.CV_8UC1).ToMat();
        if (!ModelCfg.DongLaDontCare || src.Channels() < 3) return dong;

        var kenh = Cv2.Split(src);
        using var hieu = new Mat();
        Cv2.Subtract(kenh[2], kenh[0], hieu);            // R - B
        foreach (var c in kenh) c.Dispose();

        Cv2.Threshold(hieu, dong, ModelCfg.NguongDongRB, 255, ThresholdTypes.Binary);
        //Dbg.Show(hieu, "hieu");
        if (ModelCfg.MoVungDong > 0)
        {
            Cv2.MorphologyEx(dong, dong, MorphTypes.Open, Dia(ModelCfg.MoVungDong));
            //Dbg.Show(dong, "dong");
        }      
        if (ModelCfg.NoiRongDontCare > 0)
        {
            Cv2.Dilate(dong, dong, Dia(ModelCfg.NoiRongDontCare));
            //Dbg.Show(dong, "dong");
        }    
            
        return dong;
    }
    private static Mat VungDong_2(Mat src)
    {
        int nguongDong = 5;
        var dong = Mat.Zeros(src.Size(), MatType.CV_8UC1).ToMat();
        if (!ModelCfg.DongLaDontCare || src.Channels() < 3) return dong;

        var kenh = Cv2.Split(src);
        using var hieu = new Mat();
        Cv2.Subtract(kenh[2], kenh[0], hieu);            // R - B
        foreach (var c in kenh) c.Dispose();

        Cv2.Threshold(hieu, dong, nguongDong, 255, ThresholdTypes.Binary);
        //Dbg.Show(hieu, "hieu");
        if (ModelCfg.MoVungDong > 0)
        {
            Cv2.MorphologyEx(dong, dong, MorphTypes.Open, Dia(ModelCfg.MoVungDong));
            //Dbg.Show(dong, "dong");
        }
        if (ModelCfg.NoiRongDontCare > 0)
        {
            Cv2.Dilate(dong, dong, Dia(ModelCfg.NoiRongDontCare));
            //Dbg.Show(dong, "dong");
        }

        return dong;
    }

    private static Mat VungTrangBac_Gray(Mat src)
    {
        var mask = Mat.Zeros(src.Size(), MatType.CV_8UC1).ToMat();
        if (src.Empty()) return mask;

        using var binaryTho = new Mat();

        if (src.Channels() == 3)
        {
            // 1. Tách kênh an toàn
            Mat[] kenh = Cv2.Split(src);

            using (kenh[0]) // Kênh B
            using (kenh[1]) // Kênh G
            using (kenh[2]) // Kênh R
            using (var hieuBR = new Mat())
            {
                // B - R: Nền vàng R > B => ra 0, Kim loại B >= R => ra giá trị > 0
                Cv2.Subtract(kenh[0], kenh[2], hieuBR);

                // Phân ngưỡng trên ma trận hiệu
                Cv2.Threshold(hieuBR, binaryTho, 15, 255, ThresholdTypes.Binary);
                //Dbg.Show(hieuBR, "a");
            } // Hết block using này, 3 kênh kenh[0..2] và hieuBR mới được Dispose an toàn!
        }
        else
        {
            Cv2.Threshold(src, binaryTho, 100, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
        }

        // 2. Morphology CLOSE: Nối liền các vết nứt xước
        int kSize = ModelCfg.MoVungDong > 0 ? ModelCfg.MoVungDong : 7;
        using var kernelClose = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(kSize, kSize));
        using var closed = new Mat();
        Cv2.MorphologyEx(binaryTho, closed, MorphTypes.Close, kernelClose);
        //Dbg.Show(binaryTho, "a");
        //Dbg.Show(closed, "a");
        //Dbg.Show(src, "a");


        using var matDen = new Mat();
        Cv2.CvtColor(src, matDen, ColorConversionCodes.BGR2GRAY);
        Cv2.Threshold(matDen, matDen, 70, 255, ThresholdTypes.Binary);
        //Dbg.Show(matDen, "a");
        // 3. FILL HOLES: Lấy Contour ngoài cùng và vẽ đặc ruột
        Cv2.FindContours(
            closed,
            out Point[][] contours,
            out HierarchyIndex[] hierarchy,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple
        );

        for (int i = 0; i < contours.Length; i++)
        {
            double area = Cv2.ContourArea(contours[i]);
            if (area > 200) // Lọc nhiễu vụn
            {
                Cv2.DrawContours(mask, contours, i, Scalar.White, -1); // -1: Fill đặc
                //Dbg.Show(mask, "a");
            }
        }

        // 4. Dilate nếu cần nới rộng Don't Care
        if (ModelCfg.NoiRongDontCare > 0)
        {
            Cv2.Dilate(mask, mask, Dia(ModelCfg.NoiRongDontCare));
        }
        //Dbg.Show(mask, "a");
        //LuuAnhG2(mask, _outDirG2, $"anh1");

       // Dbg.Show(matDen, "a");
        //LuuAnhG2(matDen, _outDirG2, $"anh2");
        using Mat result = new Mat();
        Cv2.BitwiseAnd(matDen, mask, mask);
        //Dbg.Show(result, "a");

        Cv2.BitwiseNot(mask, mask);
        //Dbg.Show(result, "a");
        return mask;
    }

    private static Mat VungTrangBac_Gray_(Mat src)
    {
        var mask = Mat.Zeros(src.Size(), MatType.CV_8UC1).ToMat();
        if (src.Empty()) return mask;

        using var binaryTho = new Mat();

        if (src.Channels() == 3)
        {
            // 1. Tách kênh an toàn
            Mat[] kenh = Cv2.Split(src);

            using (kenh[0]) // Kênh B
            using (kenh[1]) // Kênh G
            using (kenh[2]) // Kênh R
            using (var hieuBR = new Mat())
            {
                // B - R: Nền vàng R > B => ra 0, Kim loại B >= R => ra giá trị > 0
                Cv2.Subtract(kenh[0], kenh[2], hieuBR);

                // Phân ngưỡng trên ma trận hiệu
                Cv2.Threshold(hieuBR, binaryTho, 50, 255, ThresholdTypes.Binary);
                Dbg.Show(hieuBR, "a");
            } // Hết block using này, 3 kênh kenh[0..2] và hieuBR mới được Dispose an toàn!
        }
        else
        {
            Cv2.Threshold(src, binaryTho, 100, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
        }

        // 2. Morphology CLOSE: Nối liền các vết nứt xước
        int kSize = 1;
        using var kernelClose = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(kSize, kSize));
        using var closed = new Mat();
        Cv2.MorphologyEx(binaryTho, closed, MorphTypes.Close, kernelClose);
        Dbg.Show(binaryTho, "a");
        Dbg.Show(closed, "a");
        Dbg.Show(src, "a");


        //using var matDen = new Mat();
        //Cv2.CvtColor(src, matDen, ColorConversionCodes.BGR2GRAY);
        //Cv2.Threshold(matDen, matDen, 70, 255, ThresholdTypes.Binary);
        //Dbg.Show(matDen, "a");
        // 3. FILL HOLES: Lấy Contour ngoài cùng và vẽ đặc ruột
        Cv2.FindContours(
            closed,
            out Point[][] contours,
            out HierarchyIndex[] hierarchy,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple
        );

        for (int i = 0; i < contours.Length; i++)
        {
            double area = Cv2.ContourArea(contours[i]);
            if (area > 200) // Lọc nhiễu vụn
            {
                Cv2.DrawContours(mask, contours, i, Scalar.White, -1); // -1: Fill đặc
                //Dbg.Show(mask, "a");
            }
        }

        // 4. Dilate nếu cần nới rộng Don't Care
        if (ModelCfg.NoiRongDontCare > 0)
        {
            Cv2.Dilate(mask, mask, Dia(5));
        }
        //Dbg.Show(mask, "a");
        ////LuuAnhG2(mask, _outDirG2, $"anh1");

        // Dbg.Show(matDen, "a");
        ////LuuAnhG2(matDen, _outDirG2, $"anh2");
        //using Mat result = new Mat();
        //Cv2.BitwiseAnd(matDen, mask, mask);
        //Dbg.Show(mask, "a");

        Cv2.BitwiseNot(mask, mask);
        Dbg.Show(mask, "a");
        return mask;
    }
    public static Mat LayVungTrangLonNhat(Mat binaryInput)
    {
        // 1. Khởi tạo ma trận kết quả đen hoàn toàn (cùng kích thước)
        Mat resultMask = Mat.Zeros(binaryInput.Size(), MatType.CV_8UC1).ToMat();

        // 2. Tìm tất cả các contour ngoài cùng
        Cv2.FindContours(
            binaryInput,
            out Point[][] contours,
            out HierarchyIndex[] hierarchy,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple
        );

        if (contours.Length == 0)
            return resultMask;

        // 3. Tìm Contour có diện tích lớn nhất
        int maxIndex = -1;
        double maxArea = 0;

        for (int i = 0; i < contours.Length; i++)
        {
            double area = Cv2.ContourArea(contours[i]);
            if (area > maxArea)
            {
                maxArea = area;
                maxIndex = i;
            }
        }

        // 4. Chỉ vẽ DUY NHẤT vùng lớn nhất lên mask kết quả (thickness = -1: Fill ruột nguyên bản)
        if (maxIndex != -1)
        {
            Cv2.DrawContours(resultMask, contours, maxIndex, Scalar.White, -1);
        }

        return resultMask;
    }
    public static Mat LayVungDenBenTrong(Mat binarySrc)
    {
        // 1. d MTạo ma trận lấp đầy toàn bộ khối trắng bên ngoài (Filleask)
        using Mat filledWhite = Mat.Zeros(binarySrc.Size(), MatType.CV_8UC1).ToMat();

        // Tìm contour bao ngoài cùng (External) của khối màu trắng
        Cv2.FindContours(
            binarySrc,
            out Point[][] contours,
            out HierarchyIndex[] hierarchy,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple
        );

        // Vẽ đặc toàn bộ viền ngoài để tạo khối trắng nguyên vẹn (lấp kín mọi lỗ đen bên trong)
        for (int i = 0; i < contours.Length; i++)
        {
            double area = Cv2.ContourArea(contours[i]);
            if (area > 1000) // Lọc bỏ contour vụn ở viền biên nếu có
            {
                Cv2.DrawContours(filledWhite, contours, i, Scalar.White, -1); // -1: Fill đặc ruột
            }
        }

        // 2. Phép trừ ma trận: Lấy Khối Trắng Đặc trừ đi Ảnh Gốc
        // Kết quả: Chỉ những chỗ là Đen (0) nằm bên trong ruột mới trở thành Trắng (255)
        Mat internalBlackMask = new Mat();
        Cv2.Subtract(filledWhite, binarySrc, internalBlackMask);

        // 3. (Tùy chọn) Morphology để lọc nhiễu các đường gân xước quá nhỏ
        // using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));
        // Cv2.MorphologyEx(internalBlackMask, internalBlackMask, MorphTypes.Open, kernel);

        return internalBlackMask; // Trả về ảnh có các vùng đen bên trong nổi lên thành màu TRẮNG
    }


    private static Mat? CropLargestRegion(Mat src, string nameImgMain)
    {
        //double scale = Math.Min(1.0, (double)Config.WorkWidth / src.Width);
        using var work = new Mat();
        //if (scale < 1.0)
        //    Cv2.Resize(src, work, new Size(), scale, scale, InterpolationFlags.Area);
        //else
        //    src.CopyTo(work);





        using var dong = VungDong(src);
        Dbg.Show(dong, "vungdong");

        using Mat nonZeroPts = new();
        Cv2.FindNonZero(dong, nonZeroPts);

        Mat workSub = new();
        if(!nonZeroPts.Empty())
        {
            Rect rectRoi = Cv2.BoundingRect(nonZeroPts);
            Rect imgBounds = new Rect(0, 0, src.Cols, src.Width);
            Rect safeRoi = rectRoi & imgBounds;
            if(safeRoi.Width > 0 && safeRoi.Height >0)
            {
                workSub = new(src, safeRoi);

            }    
            
        }
        Dbg.Show(workSub, "Img Cropp");

        // Vùng đồng 2
        //using Mat dong_ = VungDong_2(workSub);
        //Dbg.Show(dong_, "VungDong2");
        //LuuAnhG2(dong_, _outDirG2, $"VungDong2");


        // Vùng sáng bạc
        using Mat vungSangBac = VungTrangBac_Gray(workSub);
        Dbg.Show(vungSangBac, "VungSangBac");
        //LuuAnhG2(vungSangBac, _outDirG2, $"VungSangBac");


        // Lấy vùng có tụ
        using Mat vungMlcc = LayVungDenBenTrong(vungSangBac);
        Dbg.Show(vungMlcc, "a");
        // Lấy vùng trắng lớn nhất
        using Mat vungTrangLonNhat = LayVungTrangLonNhat(vungMlcc);
        Dbg.Show(vungTrangLonNhat, "a");
        //Loại bỏ vùng thừa
        using var kernelLoaiVung1 = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(150, 150));
        using Mat vungMlcc2 = new();
        Cv2.MorphologyEx(vungTrangLonNhat, vungMlcc2, MorphTypes.Open, kernelLoaiVung1);
        Dbg.Show(vungMlcc2, "a");
                
        using Mat nonZeroPtsVungMlcc = new();
        Mat vungMLCCMain = new();
        Cv2.FindNonZero(vungMlcc2, nonZeroPtsVungMlcc);
        if(!nonZeroPtsVungMlcc.Empty())
        {
            Rect rawRoi = Cv2.BoundingRect(nonZeroPtsVungMlcc);
            Rect imgBounds = new Rect(0, 0, workSub.Width, workSub.Height);
            Rect safeRoi = rawRoi & imgBounds;

            if(safeRoi.Width > 0 && safeRoi.Height>0)
            {
                vungMLCCMain = new Mat(workSub, safeRoi);

            }    

        }
        Dbg.Show(vungMLCCMain, "VungMLCCMain");
        LuuAnhG2(vungMLCCMain, _outDirG2, $"{nameImgMain}");


        // Tìm tọa độ 2 vùng thiếc
        //RotatedRect outVung = new();
        //var aaa = TimThanTu(vungMLCCMain, 30, out outVung);
        //var aaa_ = KiemTraCoTu(vungMLCCMain, 80);
        //using Mat vungMLCCMainGray = new();
        //Cv2.CvtColor(vungMLCCMain, vungMLCCMainGray, ColorConversionCodes.BGR2GRAY);
        //Dbg.Show(vungMLCCMainGray, "gray");
        //Cv2.GaussianBlur(vungMLCCMainGray, vungMLCCMainGray,new Size(5,5), 0.1);
        //Dbg.Show(vungMLCCMainGray, "Blur");
        //using Mat vungMLCCMainCanny = new();
        //Cv2.Canny(vungMLCCMainGray, vungMLCCMainCanny, 50, 100);
        //Dbg.Show(vungMLCCMainCanny, "cannay");
        //using Mat grayB1_ = new();
        ////using Mat chiChuaDong = 
        //// Chuyển toàn bộ con hàng về đen và trắng
        //using Mat grayB1 = new();
        //Cv2.CvtColor(workSub, grayB1, ColorConversionCodes.BGR2GRAY);
        //// Chuyển nhị phân
        //using Mat binaryAllImg = new();
        //Cv2.Threshold(grayB1, binaryAllImg, 220, 255, ThresholdTypes.Binary);
        //Dbg.Show(binaryAllImg, "binary_All_Img");
        //// Làm mờ
        //using Mat lamMo = new();
        //Cv2.GaussianBlur(workSub, lamMo, new Size(3,3), 0,3 );
        //Dbg.Show(lamMo, "mo");
        //// Tìm tụ
        //using Mat cannyTu = new();
        //Cv2.Canny(lamMo, cannyTu, 100, 200);
        //Dbg.Show(cannyTu, "cannyTu");


        using Mat haha = new();
        return haha;

    }

    public static bool KiemTraCoTu_EdgeScan(Mat src, int minEdgePixels = 30)
    {
        Dbg.Show(src, "a");
        if (src.Empty() || src.Channels() < 3) return false;

        // 1. Tách kênh B - R để tìm 2 đốm thiếc
        using Mat diffBR = new Mat();
        Mat[] channels = Cv2.Split(src);
        using (channels[0]) using (channels[1]) using (channels[2])
        {
            Cv2.Subtract(channels[0], channels[2], diffBR);
        }
        Dbg.Show(diffBR, "a");
        using Mat silverMask = new Mat();
        Cv2.Threshold(diffBR, silverMask, 5, 255, ThresholdTypes.Binary);
        Dbg.Show(silverMask, "ChuyenSangDenTrangAnhVungThiec");

        // Tìm vùng cháy sáng của thiếc
        using Mat vungThiec = new();
        Cv2.CvtColor(src, vungThiec, ColorConversionCodes.BGR2GRAY);
        Cv2.Threshold(vungThiec, vungThiec, 150, 255, ThresholdTypes.Binary);
        Dbg.Show(vungThiec, "ChuyenVungThieAnhGoc");
        var a = 1;


        //using var closeKernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new OpenCvSharp.Size(15, 15));
        //Cv2.MorphologyEx(silverMask, silverMask, MorphTypes.Close, closeKernel);
        //Dbg.Show(silverMask, "LamMinDomDen");

        //using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(30, 30));
        //Cv2.MorphologyEx(silverMask, silverMask, MorphTypes.Open, kernel);
        //Dbg.Show(silverMask, "phepMo");

        //using var cirKernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(30, 30));
        //using Mat opened = new();
        //Cv2.MorphologyEx(silverMask, opened, MorphTypes.Open, cirKernel);
        //Dbg.Show(opened, "a");


        //using Mat graySrc = new();
        //Cv2.CvtColor(src, graySrc, ColorConversionCodes.BGR2GRAY);
        //Dbg.Show(graySrc, "a");
        //Cv2.Threshold(graySrc, graySrc, 150, 255, ThresholdTypes.Binary);
        //Dbg.Show(graySrc, "a");


        // 2. Tìm 2 đốm thiếc lớn nhất (Pad trên & Pad dưới)



        return false;


    }

    public class KetQuaCheckTu
    {
        public bool CoTu;
        public double EdgeScore;      // điểm matched filter
        public double FocusVar;       // độ nét, để gate ảnh xấu
        public int SoLineDoc;
        public Rect VungThan;         // dải giữa 2 mối thiếc
        public string LyDo;
    }

    public static KetQuaCheckTu KiemTraCoTu(
        Mat src,
        int rongThanKyVong,           // chiều rộng thân tụ (pixel), đo từ ảnh mẫu
        double dungSaiRong = 0.20,
        double nguongEdge = 12.0,     // TUNE bằng dữ liệu thật
        double nguongFocus = 20.0)
    {
        var kq = new KetQuaCheckTu();
        if (src.Empty()) { kq.LyDo = "Ảnh rỗng"; return kq; }

        // ---------- 0. Chuẩn hoá: kênh L của Lab + CLAHE ----------
        using Mat gray = new Mat();
        if (src.Channels() >= 3)
        {
            using Mat lab = new Mat();
            Cv2.CvtColor(src, lab, ColorConversionCodes.BGR2Lab);
            Cv2.ExtractChannel(lab, gray, 0);
        }
        else src.CopyTo(gray);

        using (var clahe = Cv2.CreateCLAHE(2.0, new Size(8, 8)))
            clahe.Apply(gray, gray);

        // ---------- 1. Gate độ nét ----------
        using (Mat lap = new Mat())
        {
            Cv2.Laplacian(gray, lap, MatType.CV_64F);
            Cv2.MeanStdDev(lap, out _, out Scalar sd);
            kq.FocusVar = sd.Val0 * sd.Val0;
        }
        if (kq.FocusVar < nguongFocus)
        {
            kq.LyDo = $"Ảnh mờ (var={kq.FocusVar:F1}), không kết luận";
            return kq;   // báo lỗi ảnh, KHÔNG kết luận NG
        }

        //int nguongSang = TinhPercentile(gray, 0.92);   // 8% pixel sáng nhất
        int cy = (int)(gray.Rows * 0.20);
        using Mat than = new Mat(gray, new Rect(0, cy, gray.Cols, gray.Rows - 2 * cy));

        // ---------- 3. Matched filter trên gradient ngang ----------
        kq.EdgeScore = TinhDiemCapCanh(than, rongThanKyVong, dungSaiRong);

        // ---------- 4. Đếm line dọc (feature phụ) ----------
        kq.SoLineDoc = DemLineDoc(than);

        // ---------- 5. Kết luận: AND để giảm false call ----------
        kq.CoTu = kq.EdgeScore >= nguongEdge || kq.SoLineDoc >= 2;
        kq.LyDo = kq.CoTu ? "OK" : $"Thiếu tụ (edge={kq.EdgeScore:F1}, line={kq.SoLineDoc})";
        return kq;
    }

    public static bool TimThanTu(Mat src, int rongKyVong, out RotatedRect than)
    {
        than = default;

        using Mat gray = new Mat();
        if (src.Channels() >= 3)
        {
            using Mat lab = new Mat();
            Cv2.CvtColor(src, lab, ColorConversionCodes.BGR2Lab);
            Cv2.ExtractChannel(lab, gray, 0);
        }
        else src.CopyTo(gray);

        Cv2.Threshold(gray, gray, 235, 235, ThresholdTypes.Trunc);   // chặn chói specular
        Dbg.Show(gray, "gray"); 
        // Bản đồ texture: std cục bộ. Thân => thấp, thiếc & PCB => cao
        using Mat f = new Mat(), mu = new Mat(), mu2 = new Mat(), sq = new Mat();
        gray.ConvertTo(f, MatType.CV_32F);
        Cv2.Blur(f, mu, new Size(9, 9));
        Cv2.Multiply(f, f, sq);
        Cv2.Blur(sq, mu2, new Size(9, 9));

        using Mat variance = new Mat();
        Cv2.Subtract(mu2, mu.Mul(mu), variance);
        Cv2.Max(variance, 0, variance);
        Cv2.Sqrt(variance, variance);

        using Mat std8 = new Mat();
        Cv2.Normalize(variance, std8, 0, 255, NormTypes.MinMax);
        std8.ConvertTo(std8, MatType.CV_8U);
        Dbg.Show(std8, "TextureMap");

        // Vùng nhẵn = std thấp
        using Mat maskNhan = new Mat();
        Cv2.Threshold(std8, maskNhan, 0, 255,
                      ThresholdTypes.BinaryInv | ThresholdTypes.Otsu);
        Dbg.Show(maskNhan, "a");
        using (var k = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(7, 7)))
        {
            Cv2.MorphologyEx(maskNhan, maskNhan, MorphTypes.Close, k);
            Dbg.Show(maskNhan, "a");
            Cv2.MorphologyEx(maskNhan, maskNhan, MorphTypes.Open, k);
            Dbg.Show(maskNhan, "a");
        }
        Dbg.Show(maskNhan, "MaskNhan");

        Cv2.FindContours(maskNhan, out Point[][] cnts, out _,
                         RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        double bestScore = 0;
        Point2f tamRoi = new Point2f(src.Cols / 2f, src.Rows / 2f);

        foreach (var c in cnts)
        {
            var rr = Cv2.MinAreaRect(c);
            double wS = Math.Min(rr.Size.Width, rr.Size.Height);
            double hL = Math.Max(rr.Size.Width, rr.Size.Height);

            if (wS < rongKyVong * 0.2 || wS > rongKyVong * 1.5) continue;
            double ratio = hL / Math.Max(wS, 1);
            if (ratio < 1.2 || ratio > 3.0) continue;

            // Độ đặc: contour phải lấp đầy rect (thân là chữ nhật thật)
            double solidity = Cv2.ContourArea(c) / Math.Max(rr.Size.Width * rr.Size.Height, 1);
            if (solidity < 0.75) continue;

            // Ưu tiên gần tâm ROI
            double d = Math.Sqrt(Math.Pow(rr.Center.X - tamRoi.X, 2) +
                                 Math.Pow(rr.Center.Y - tamRoi.Y, 2));
            double score = solidity * 100 - d;

            if (score > bestScore) { bestScore = score; than = rr; }
        }

        return bestScore > 0;
    }

    public class KetQuaCanh
    {
        public double Score;    // đã chuẩn hoá theo sigma
        public double A, B;     // vị trí 2 cạnh (sub-pixel)
    }

    private static KetQuaCanh TimCapCanh(Mat anh, int khoangCachKyVong,
                                     double dungSai, bool theoTrucX)
    {
        var kq = new KetQuaCanh();

        using Mat blur = new Mat();
        Cv2.GaussianBlur(anh, blur, new Size(5, 5), 0);

        using Mat grad = new Mat();
        if (theoTrucX)
            Cv2.Sobel(blur, grad, MatType.CV_32F, 1, 0, 3);   // cạnh dọc
        else
            Cv2.Sobel(blur, grad, MatType.CV_32F, 0, 1, 3);   // cạnh ngang

        // Ép về profile 1D
        using Mat mean = new Mat();
        Cv2.Reduce(grad, mean,
                   theoTrucX ? ReduceDimension.Row : ReduceDimension.Column,
                   ReduceTypes.Avg, MatType.CV_32F);

        mean.GetArray(out float[] prof);   // Column => vector cột, vẫn đọc tuyến tính OK

        float mu = prof.Average();
        for (int i = 0; i < prof.Length; i++) prof[i] -= mu;

        double sigma = Math.Sqrt(prof.Select(v => (double)v * v).Average());
        if (sigma < 1e-6) return kq;

        int wMin = (int)(khoangCachKyVong * (1 - dungSai));
        int wMax = (int)(khoangCachKyVong * (1 + dungSai));
        int iBest = -1, jBest = -1;
        double best = 0;

        for (int w = wMin; w <= wMax && w < prof.Length; w++)
            for (int i = 0; i + w < prof.Length; i++)
            {
                if (Math.Sign(prof[i]) == Math.Sign(prof[i + w])) continue;
                double s = Math.Min(Math.Abs(prof[i]), Math.Abs(prof[i + w]));
                if (s > best) { best = s; iBest = i; jBest = i + w; }
            }

        if (iBest < 0) return kq;

        kq.Score = best / sigma;
        kq.A = NoiSuyDinh(prof, iBest);
        kq.B = NoiSuyDinh(prof, jBest);
        return kq;
    }

    // Nội suy parabol qua 3 điểm => vị trí đỉnh sub-pixel
    private static double NoiSuyDinh(float[] p, int i)
    {
        if (i <= 0 || i >= p.Length - 1) return i;
        double y0 = Math.Abs(p[i - 1]), y1 = Math.Abs(p[i]), y2 = Math.Abs(p[i + 1]);
        double mauSo = y0 - 2 * y1 + y2;
        if (Math.Abs(mauSo) < 1e-9) return i;
        double delta = 0.5 * (y0 - y2) / mauSo;
        if (Math.Abs(delta) > 1) return i;      // nội suy hỏng, trả về nguyên
        return i + delta;
    }

    private static int TinhPercentile(Mat gray, double p)
    {
        using Mat hist = new Mat();
        Cv2.CalcHist(new[] { gray }, new[] { 0 }, null, hist,
                     1, new[] { 256 }, new[] { new Rangef(0, 256) });
        double tong = gray.Rows * gray.Cols, cong = 0;
        for (int i = 0; i < 256; i++)
        {
            cong += hist.At<float>(i);
            if (cong / tong >= p) return i;
        }
        return 255;
    }

    private static double TinhDiemCapCanh(Mat than, int rongKyVong, double dungSai)
    {
        using Mat blur = new Mat();
        Cv2.GaussianBlur(than, blur, new Size(5, 5), 0);
        using Mat gx = new Mat();
        Cv2.Sobel(blur, gx, MatType.CV_32F, 1, 0, 3);
        Dbg.Show(gx, "a");

        using Mat mean = new Mat();
        Cv2.Reduce(gx, mean, ReduceDimension.Row, ReduceTypes.Avg, MatType.CV_32F);
        Dbg.Show(mean, "a");
        mean.GetArray(out float[] prof);

        float mu = prof.Average();
        for (int i = 0; i < prof.Length; i++) prof[i] -= mu;

        double sigma = Math.Sqrt(prof.Select(v => (double)v * v).Average());
        if (sigma < 1e-6) return 0;

        int wMin = (int)(rongKyVong * (1 - dungSai));
        int wMax = (int)(rongKyVong * (1 + dungSai));
        double best = 0;

        for (int w = wMin; w <= wMax && w < prof.Length; w++)
            for (int i = 0; i + w < prof.Length; i++)
            {
                if (Math.Sign(prof[i]) == Math.Sign(prof[i + w])) continue;
                double s = Math.Min(Math.Abs(prof[i]), Math.Abs(prof[i + w]));
                if (s > best) best = s;
            }

        return best / sigma;   // số không thứ nguyên, ~2-5 là có cạnh rõ
    }

    private static int DemLineDoc(Mat than)
    {
        using Mat edge = new Mat();
        Cv2.Canny(than, edge, 50, 150);
        Dbg.Show(edge, "than");
        var lines = Cv2.HoughLinesP(edge, 1, Math.PI / 180, 30,
                                    minLineLength: than.Rows * 0.5, maxLineGap: 5);
        if (lines == null) return 0;
        return lines.Count(l =>
        {
            double ang = Math.Abs(Math.Atan2(l.P2.Y - l.P1.Y, l.P2.X - l.P1.X) * 180 / Math.PI);
            return Math.Abs(ang - 90) < 12;
        });
    }

    /// <summary>
    /// Kiểm tra tụ có hiện diện hay không (Presence/Absence Check)
    /// </summary>
    /// <param name="src">Ảnh đầu vào (BGR hoặc Grayscale)</param>
    /// <param name="centerRoi">Vùng ROI nhỏ đặt đúng vào tâm thân tụ</param>
    /// <param name="minMeanGray">Ngưỡng xám tối thiểu của thân gốm (thường từ 80 - 110)</param>
    /// <returns>True nếu CÓ tụ (OK), False nếu MẤT tụ (NG)</returns>
    public static bool CheckComponentPresence(Mat src, Rect centerRoi, double minMeanGray = 90.0)
    {
        // 1. Khóa biên ROI an toàn chống tràn ma trận
        Rect imageBounds = new Rect(0, 0, src.Width, src.Height);
        Rect safeRoi = centerRoi & imageBounds;

        if (safeRoi.Width <= 0 || safeRoi.Height <= 0)
            return false; // ROI không hợp lệ -> Coi như NG

        // 2. Cắt Sub-Mat ROI tại tâm (Zero-copy)
        using Mat roiMat = new Mat(src, safeRoi);

        // 3. Chuyển sang Grayscale nếu là ảnh màu
        using Mat grayRoi = new Mat();
        if (roiMat.Channels() == 3)
        {
            Cv2.CvtColor(roiMat, grayRoi, ColorConversionCodes.BGR2GRAY);
        }
        else
        {
            roiMat.CopyTo(grayRoi);
        }

        // 4. Tính toán Cường độ sáng trung bình (Mean) và Độ lệch chuẩn (StdDev)
        Cv2.MeanStdDev(grayRoi, out Scalar mean, out Scalar stddev);
        double meanVal = mean.Val0;

        // 5. Quyết định: Nếu giá trị xám trung bình < ngưỡng nền đen -> Mất tụ
        if (meanVal < minMeanGray)
        {
            return false; // NG: MISSING COMPONENT
        }

        return true; // OK: Có tụ, tiếp tục sang Tầng 2
    }

    /// <summary>
    /// Gom số liệu align của MỘT ảnh vào kho <see cref="ExcelAlign"/> để cuối lượt xuất Excel.
    ///
    /// Hàm này chỉ ĐỌC kết quả đã đo được trong <see cref="CropLargestRegion"/>, không đo lại,
    /// không sửa gì của mạch xử lý — bỏ lời gọi nó đi thì pipeline vẫn chạy y hệt.
    ///
    /// Mọi đại lượng đo trên ảnh work (đã thu nhỏ theo <paramref name="scale"/>) đều được ghi
    /// thêm một cột quy về px ảnh gốc: dài chia scale, diện tích chia scale bình phương.
    /// Đánh giá sai số vài chục micromet thì phải nhìn số ở hệ ảnh gốc, không nhìn ảnh work.
    /// </summary>
    private static void ThemDuLieuAlign(string nameImgMain, Mat src, double scale,
                                        int soContour, int chiSoNgoai, Point[] contourNgoai,
                                        double dienTich, double chuVi, Point[] approx,
                                        RotatedRect minRect, double gocThuc)
    {
        if (string.IsNullOrWhiteSpace(Config.ExcelOutPath)) return;

        // Đường tròn nhỏ nhất bao contour ngoài: tâm của nó là "vị trí đường tròn"
        // dùng để so lệch tâm giữa các ảnh; bán kính cho biết con hàng to nhỏ khác nhau bao nhiêu.
        Cv2.MinEnclosingCircle(contourNgoai, out Point2f tamTron, out float banKinh);

        double canhDai = Math.Max(minRect.Size.Width, minRect.Size.Height);
        double canhNgan = Math.Min(minRect.Size.Width, minRect.Size.Height);
        double nghichScale = scale > 0 ? 1.0 / scale : 1.0;

        ExcelAlign.Them(new DongAlign
        {
            TenAnh = nameImgMain,

            RongAnhGoc = src.Width,
            CaoAnhGoc = src.Height,
            Scale = scale,

            SoContour = soContour,
            ChiSoContourNgoai = chiSoNgoai,
            DienTich = dienTich,
            DienTichGoc = dienTich * nghichScale * nghichScale,
            ChuVi = chuVi,
            ChuViGoc = chuVi * nghichScale,
            SoDinhApprox = approx.Length,
            DoTron = chuVi > 0 ? 4 * Math.PI * dienTich / (chuVi * chuVi) : 0,

            TamX = minRect.Center.X,
            TamY = minRect.Center.Y,
            TamXGoc = minRect.Center.X * nghichScale,
            TamYGoc = minRect.Center.Y * nghichScale,
            RongRect = minRect.Size.Width,
            CaoRect = minRect.Size.Height,
            RongRectGoc = minRect.Size.Width * nghichScale,
            CaoRectGoc = minRect.Size.Height * nghichScale,
            //if(minRect.Size.Width > minRect.Size.Height)
            //{
            //    GocRaw = minRect.Angle;
            //}
            //else
            //{
            //    GocRaw = minRect.Angle + 90;
            //}
            //GocRaw = (minRect.Size.Width > minRect.Size.Height) ? minRect.Angle : minRect.Angle + 90,
            GocRaw = minRect.Angle,
            GocThuc = gocThuc,
            TySoCanh = canhNgan > 0 ? canhDai / canhNgan : 0,

            TronX = tamTron.X,
            TronY = tamTron.Y,
            BanKinh = banKinh,
            TronXGoc = tamTron.X * nghichScale,
            TronYGoc = tamTron.Y * nghichScale,
            BanKinhGoc = banKinh * nghichScale
        });

        Dbg.Log($"Excel: tam rect=({minRect.Center.X:0.00},{minRect.Center.Y:0.00}) " +
                $"goc thuc={gocThuc:0.00} do, tam tron=({tamTron.X:0.00},{tamTron.Y:0.00}) r={banKinh:0.00}");
    }

    public sealed record KetQuaGoc(Goc Goc, Rect RoiTrongAnh, Rect? VungTrongRoi, Mat? Anh);

    /// <summary>
    /// Từ ảnh đã cắt (cả con hàng), soi LẦN LƯỢT BỐN GÓC để tìm phần nhô ra.
    /// Góc nào không thấy thì phần tử tương ứng có Anh = null (KHÔNG phải lỗi).
    ///
    /// Bốn góc chạy tuần tự trong đây, vì việc chia luồng đã làm ở tầng trên rồi
    /// (mỗi luồng một ảnh). Chia luồng thêm lần nữa ở đây chỉ tổ tranh CPU của nhau.
    /// </summary>
    public static List<KetQuaGoc> CropSmallRegion(Mat src, string nameImgMain)
    {
        var ketQua = new List<KetQuaGoc>();
        foreach (var goc in GocCfg.CacGocCanSoi)
        {
            Dbg.Log($"----- soi goc {goc} -----");
            ketQua.Add(SoiMotGoc(src, goc, nameImgMain));
        }
        return ketQua;
    }

    /// <summary>
    /// Vùng ROI của một góc, đã kẹp trong khung ảnh.
    ///
    /// Toán tử &amp; giữa hai Rect trong OpenCvSharp = phép GIAO. Bắt buộc phải kẹp:
    /// ảnh giai đoạn 1 nhỏ hơn 500px là new Mat(src, rect) ném exception ngay.
    /// </summary>
    private static Rect VungRoiCuaGoc(Goc goc, Size anh)
    {
        int w = anh.Width, h = anh.Height;
        int roiW = (int)Math.Round(w * GocCfg.RoiW);
        int roiH = (int)Math.Round(h * GocCfg.RoiH);
        int lechTren = (int)Math.Round(h * GocCfg.LechTren);
        int lechDuoi = (int)Math.Round(h * GocCfg.LechDuoi);

        Point topLeft = new(0, 0);
        Point topRight = new(w - 1, 0);
        Point bottomLeft = new(0, h - 1);
        Point bottomRight = new(w - 1, h - 1);

        Rect r = goc switch
        {
            Goc.TrenTrai => new Rect(topLeft.X, topLeft.Y , roiW, roiH),
            Goc.TrenPhai => new Rect(topRight.X - roiW, topRight.Y , roiW, roiH),
            Goc.DuoiPhai => new Rect(bottomRight.X - roiW, bottomRight.Y - roiH , roiW, roiH),
            Goc.DuoiTrai => new Rect(bottomLeft.X, bottomLeft.Y - roiH , roiW, roiH),
            _ => throw new ArgumentOutOfRangeException(nameof(goc), goc, "Goc khong hop le")
        };
        return r & new Rect(0, 0, w, h);
    }

    /// <summary>
    /// Soi MỘT góc để tìm phần nhô ra rồi cắt nó.
    ///
    /// MẠCH CHÍNH, nhớ đúng thứ tự này:
    ///   ảnh màu -> xám -> làm mờ -> nhị phân (Otsu) -> lấp lỗ -> giữ blob to nhất
    ///           -> opening bằng đĩa -> TRỪ đi -> lọc ứng viên -> ra bbox
    /// </summary>
    private static KetQuaGoc SoiMotGoc(Mat src, Goc goc, string nameImgMain)
    {
        Dbg.Show(src, "goc");
        Rect areaGoc = VungRoiCuaGoc(goc, src.Size());

        if (areaGoc.Width < 50 || areaGoc.Height < 50)
        {
            Dbg.Log($"ROI goc {goc} qua nho sau khi kep: {areaGoc}");
            return new KetQuaGoc(goc, areaGoc, null, null);
        }

        using Mat imgGoc = new(src, areaGoc);
        Dbg.Show(imgGoc, $"1_ROI_{goc}");

        using Mat gray = ToGray(imgGoc);

        using Mat blurred = new();
        Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 0);

        using var mask = new Mat();
        Cv2.Threshold(blurred, mask, 0, 255, ThresholdTypes.BinaryInv | ThresholdTypes.Otsu);
        Dbg.Show(mask, $"2_Threshold_{goc}");

        using var k7 = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(60, 60));
        Cv2.MorphologyEx(mask, mask, MorphTypes.Close, k7, iterations: 2);
        Dbg.Show(mask, $"3_Closed_{goc}");
        var test = "";

        using Mat objMask =
            LargestFilled(mask);
        Dbg.Show(objMask, $"4_ObjMask_{goc}");

        double roiArea = (double)objMask.Rows * objMask.Cols;
        double ratio = Cv2.CountNonZero(objMask) / roiArea;
        Dbg.Log($"ratio mask = {ratio:P1}   (kỳ vọng ~30-45% cho ROI ở góc)");

        if (ratio < ProtrusionCfg.MinObjectRatio || ratio > ProtrusionCfg.MaxObjectRatio)
        {
            Dbg.Log($"=> MASK HONG o goc {goc}. Mo anh 2_Threshold_{goc} xem co bi dao trang/den khong.");
            return new KetQuaGoc(goc, areaGoc, null, null);
        }

        using var opened = OpenWithDisk(objMask, ProtrusionCfg.DiskRadius, ProtrusionCfg.OpenDownScale);
        Dbg.Show(opened, $"5_Opened_{goc}");

        using var residual = new Mat();
        Cv2.Subtract(objMask, opened, residual);
        Dbg.Show(objMask, $"4_ObjMask_{goc}");
        Dbg.Show(residual, $"6_Residual_{goc}");

        Rect? found = PickProtrusion(residual, opened, objMask.Size(), roiArea, imgGoc, goc);

        if (found is null)
        {
            Dbg.Log($"Goc {goc}: khong ung vien nao qua duoc bo loc.");
            Dbg.Log($"=> Mo anh 6_Residual_{goc} xem co gi khong:");
            Dbg.Log("   - Residual RONG        -> DiskRadius qua NHO, tang len");
            Dbg.Log("   - Residual day rac     -> DiskRadius qua LON, giam xuong");
            Dbg.Log("   - Residual dung cho    -> bo loc qua chat, noi MinAreaRatio/MinAspect");
            Dbg.Log("   Hoac goi Program.SweepDiskRadius(anhDaCat) de quet tu dong.");
            return new KetQuaGoc(goc, areaGoc, null, null);
        }
        int padding = 50;
        Rect box = found.Value;

        box.X -= padding;
        box.Y -= padding;
        box.Width += 2 * padding;
        box.Height += 2 * padding;
        int imgWidth = imgGoc.Width;
        int imgHeight = imgGoc.Height;
        int x = Math.Max(0, box.X);
        int y = Math.Max(0, box.Y);
        int width = Math.Min(imgWidth - x, box.Width + (box.X < 0 ? box.X : 0));
        int height = Math.Min(imgHeight - y, box.Height + (box.Y < 0 ? box.Y : 0));

        Rect safeCropBox = new Rect(x, y, width, height);

        using (Mat vis = imgGoc.Clone())
        {
            Cv2.Rectangle(vis, safeCropBox, new Scalar(0, 255, 0), 2);
            Cv2.PutText(vis, $"{goc} {safeCropBox.Width}x{safeCropBox.Height}",
                        new Point(safeCropBox.X, Math.Max(14, safeCropBox.Y - 6)),
                        HersheyFonts.HersheySimplex, 0.6, new Scalar(0, 255, 0), 2);
            Dbg.Show(vis, $"7_KetQua_{goc}");
        }

        Dbg.Log($"Goc {goc}: TIM THAY phan nho tai (toa do trong ROI) {box}");

        var anhPhanNho1 = new Mat(imgGoc, safeCropBox).Clone();
        LuuAnhG2(anhPhanNho1, _outDirG2, $"{nameImgMain}_{goc}");
        return new KetQuaGoc(goc, areaGoc, box, anhPhanNho1);
    }

    /// <summary>
    /// Chọn ứng viên tốt nhất trong ảnh residual.
    /// </summary>
    /// <param name="residual">ảnh top-hat (mask - opening)</param>
    /// <param name="body">phần thân (chính là ảnh opening), dùng để kiểm tra ứng viên có dính thân không</param>
    /// <param name="roiSize">kích thước ROI, để biết đâu là mép</param>
    /// <param name="roiArea">diện tích ROI, để quy các ngưỡng về tỉ lệ</param>
    /// <param name="roiMau">
    /// ảnh MÀU của ROI (cùng kích thước với roiSize). Truyền vào thì mỗi ứng viên qua đủ
    /// bộ lọc sẽ được cắt và lưu ra file ngay trong vòng lặp. Để null (như SweepDiskRadius
    /// đang gọi) thì bỏ qua việc lưu — quét tham số mà lưu ảnh thì ngập thư mục output.
    /// </param>
    /// <param name="goc">góc đang soi, chỉ để ghi vào tên file và log</param>
    private static Rect? PickProtrusion(Mat residual, Mat body, Size roiSize, double roiArea,
                                        Mat? roiMau = null, Goc goc = Goc.TrenPhai)
    {
        using var work = residual.Clone();
        Cv2.FindContours(work, out Point[][] cnts, out _,
                         RetrievalModes.External, ContourApproximationModes.ApproxNone);
        Dbg.Show(work, $"Find contontours {goc}");

        if (cnts is null || cnts.Length == 0)
        {
            Dbg.Log($"PickProtrusion ({goc}): residual rong, khong co contour nao.");
            return null;
        }

        double minArea = roiArea * ProtrusionCfg.MinAreaRatio;
        double maxArea = roiArea * ProtrusionCfg.MaxAreaRatio;

        Rect? best = null;
        double bestScore = double.MinValue;
        int loaiDienTich = 0, loaiAspect = 0, loaiMep = 0, loaiKhongDinh = 0;
        int soUngVienDat = 0;

        double areaLonNhat = 0;

        foreach (var c in cnts)
        {
            double area = Cv2.ContourArea(c);
            if (area > areaLonNhat) areaLonNhat = area;
            if (area < 2000 || area > maxArea) { loaiDienTich++; continue; }

            RotatedRect rr = Cv2.MinAreaRect(c);
            double canhNgan = Math.Min(rr.Size.Width, rr.Size.Height);
            double canhDai = Math.Max(rr.Size.Width, rr.Size.Height);
            if (canhNgan < 1) { loaiAspect++; continue; }

            double aspect = canhDai / canhNgan;
            if (aspect < ProtrusionCfg.MinAspect || aspect > ProtrusionCfg.MaxAspect)
            {
                loaiAspect++;
                continue;
            }

            Rect br = Cv2.BoundingRect(c);
            int m = ProtrusionCfg.BorderMargin;
            if (br.X <= m || br.Y <= m ||
                br.Right >= roiSize.Width - m || br.Bottom >= roiSize.Height - m)
            {
                loaiMep++;
                continue;
            }

            if (!DinhVaoThan(c, body, roiSize, ProtrusionCfg.TouchDilate))
            {
                loaiKhongDinh++;
                continue;
            }

            Rect vung = NoiRongRect(br, ProtrusionCfg.BboxPadding, roiSize);
            soUngVienDat++;

            if (roiMau is not null && !roiMau.Empty())
            {
                Rect cat = vung & new Rect(0, 0, roiMau.Width, roiMau.Height);
                if (cat.Width > 0 && cat.Height > 0)
                {
                    using var anhUngVien = new Mat(roiMau, cat).Clone();
                }
            }

            double score = area * canhDai;
            if (score > bestScore)
            {
                bestScore = score;
                best = vung;
            }
        }

        Dbg.Log($"PickProtrusion ({goc}): {cnts.Length} contour -> loại vì " +
                $"dien tich={loaiDienTich}, aspect={loaiAspect}, sat mep={loaiMep}, khong dinh than={loaiKhongDinh}" +
                $" -> dat {soUngVienDat}, chon: {(best is null ? "khong co" : best.Value.ToString())}");
        Dbg.Log($"  (contour to nhat = {areaLonNhat:0} px; nguong dang dung: min=10000, " +
                $"max={maxArea:0}, con minArea theo ti le = {minArea:0})");

        return best;
    }

    /// <summary>Ứng viên có dính vào thân vật thể không.</summary>
    private static bool DinhVaoThan(Point[] contour, Mat body, Size size, int dilate)
    {
        using var m = new Mat(size, MatType.CV_8UC1, Scalar.All(0));
        Cv2.DrawContours(m, new[] { contour }, -1, Scalar.All(255), -1);

        using var k = Cv2.GetStructuringElement(
            MorphShapes.Ellipse, new Size(dilate * 2 + 1, dilate * 2 + 1));
        Cv2.Dilate(m, m, k);

        using var giao = new Mat();
        Cv2.BitwiseAnd(m, body, giao);
        return Cv2.CountNonZero(giao) > 0;
    }

    /// <summary>Nới rộng rect ra pad pixel mỗi phía, có kẹp trong khung ảnh.</summary>
    private static Rect NoiRongRect(Rect r, int pad, Size bounds)
    {
        int x1 = Math.Max(0, r.X - pad);
        int y1 = Math.Max(0, r.Y - pad);
        int x2 = Math.Min(bounds.Width, r.Right + pad);
        int y2 = Math.Min(bounds.Height, r.Bottom + pad);
        return new Rect(x1, y1, x2 - x1, y2 - y1);
    }

    /// <summary>
    /// CÔNG CỤ CHỈNH THAM SỐ — quét DiskRadius để tìm giá trị đúng, khỏi đoán mò.
    ///
    /// Cách dùng: trong Main, sau khi có "result", gọi:
    ///     SweepDiskRadius(result);
    /// rồi xem log + mở các ảnh sweep_r*_residual trong thư mục debug.
    ///
    /// Bán kính ĐÚNG là bán kính mà residual chỉ còn đúng cụm nhô, gọn và sạch.
    /// Trong log, area nhảy vọt rồi ổn định ở một dải r là dấu hiệu bạn đã vào đúng vùng.
    /// </summary>
    /// <param name="anhDaCat">ảnh kết quả của giai đoạn 1</param>
    /// <param name="goc">quét trên ROI của góc nào (mặc định trên-phải như trước)</param>
    public static void SweepDiskRadius(Mat anhDaCat, Goc goc = Goc.TrenPhai)
    {
        Rect area = VungRoiCuaGoc(goc, anhDaCat.Size());
        if (area.Width < 50 || area.Height < 50) { Dbg.Log("SweepDiskRadius: ROI qua nho."); return; }

        using Mat roi = new(anhDaCat, area);
        using Mat gray = ToGray(roi);
        using Mat blurred = new();
        Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 0);

        using var mask = new Mat();
        Cv2.Threshold(blurred, mask, 0, 255, ThresholdTypes.BinaryInv | ThresholdTypes.Otsu);
        using var k5 = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(5, 5));
        Cv2.MorphologyEx(mask, mask, MorphTypes.Close, k5, iterations: 2);
        using Mat objMask = LargestFilled(mask);

        double roiArea = (double)objMask.Rows * objMask.Cols;
        Dbg.Log("===== QUET DiskRadius =====");

        int cfgCu = ProtrusionCfg.DiskRadius;

        foreach (int r in new[] { 30, 40, 50, 60, 70, 80, 90, 100, 120, 140 })
        {
            using var opened = OpenWithDisk(objMask, r, ProtrusionCfg.OpenDownScale);
            using var residual = new Mat();
            Cv2.Subtract(objMask, opened, residual);
            int soPixel = Cv2.CountNonZero(residual);
            ProtrusionCfg.DiskRadius = r;
            Rect? hit = PickProtrusion(residual, opened, objMask.Size(), roiArea, null, goc);

            Dbg.Log($"r={r,4} : residual={soPixel,7} px ({soPixel / roiArea:P2})  ->  " +
                    (hit is null ? "KHONG BAT DUOC" : $"BAT DUOC {hit.Value}"));

            Dbg.Show(residual, $"sweep_r{r}_residual");
        }

        ProtrusionCfg.DiskRadius = cfgCu;
        Dbg.Log("===== HET QUET =====");
    }

    /// <summary>Chuyển ảnh về xám 1 kênh, nhận mọi loại đầu vào (1/3/4 kênh).</summary>
    private static Mat ToGray(Mat src)
    {
        var g = new Mat();
        if (src.Channels() == 3) Cv2.CvtColor(src, g, ColorConversionCodes.BGR2GRAY);
        else if (src.Channels() == 4) Cv2.CvtColor(src, g, ColorConversionCodes.BGRA2GRAY);
        else g = src.Clone();
        return g;
    }

    /// <summary>Sắp 4 đỉnh theo thứ tự: trên-trái, trên-phải, dưới-phải, dưới-trái.</summary>
    private static Point2f[] OrderCorners(Point2f[] pts)
    {
        var bySum = pts.OrderBy(p => p.X + p.Y).ToArray();
        var byDiff = pts.OrderBy(p => p.X - p.Y).ToArray();
        return new[] { bySum[0], byDiff[^1], bySum[^1], byDiff[0] };
    }

    /// <summary>Nắn tứ giác nghiêng thành hình chữ nhật thẳng.</summary>
    private static Mat WarpToRect(Mat src, Point2f[] corners)
    {
        static float Dist(Point2f a, Point2f b) =>
            (float)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        int w = (int)Math.Max(Dist(corners[0], corners[1]), Dist(corners[3], corners[2]));
        int h = (int)Math.Max(Dist(corners[0], corners[3]), Dist(corners[1], corners[2]));
        w = Math.Max(w, 1);
        h = Math.Max(h, 1);

        var dst = new[]
        {
            new Point2f(0, 0), new Point2f(w - 1, 0),
            new Point2f(w - 1, h - 1), new Point2f(0, h - 1)
        };

        using var m = Cv2.GetPerspectiveTransform(corners, dst);
        var output = new Mat();
        Cv2.WarpPerspective(src, output, m, new Size(w, h));
        Dbg.Show(output, "");
        return output;
    }

    /// <summary>Chưa có ảnh thì tự vẽ một ảnh mẫu để chạy thử ngay.</summary>
    private static string EnsureSampleImage()
    {
        const string path = "sample.png";
        if (File.Exists(path)) return path;

        using var img = new Mat(900, 1200, MatType.CV_8UC3, new Scalar(40, 45, 50));

        var paper = new[]
        {
            new Point(220, 160), new Point(980, 240),
            new Point(920, 760), new Point(180, 660)
        };
        Cv2.FillConvexPoly(img, paper, new Scalar(235, 240, 245), LineTypes.AntiAlias);
        Cv2.PutText(img, "SAMPLE", new Point(380, 460), HersheyFonts.HersheyDuplex,
            2.4, new Scalar(60, 60, 60), 4, LineTypes.AntiAlias);

        Cv2.ImWrite(path, img);
        Console.WriteLine($"Chua truyen duong dan anh -> da tao anh mau {path} de chay thu.");
        return path;
    }

    private static Mat BuildSolidMask(Mat closedEdges)
    {
        using var padded = new Mat();
        Cv2.CopyMakeBorder(closedEdges, padded, 1, 1, 1, 1,
                           BorderTypes.Constant, Scalar.All(0));
        using var original = padded.Clone();

        using var ffMask = new Mat(padded.Rows + 2, padded.Cols + 2,
                                   MatType.CV_8UC1, Scalar.All(0));
        Cv2.FloodFill(padded, ffMask, new Point(0, 0), Scalar.All(255),
                      out _, Scalar.All(0), Scalar.All(0), FloodFillFlags.Link4);

        using var interior = new Mat();
        Cv2.BitwiseNot(padded, interior);

        using var full = new Mat();
        Cv2.BitwiseOr(interior, original, full);

        return new Mat(full, new Rect(1, 1, closedEdges.Cols, closedEdges.Rows)).Clone();
    }

    private static Mat OpenWithDisk(Mat mask, int radius, double scale)
    {
        if (scale >= 0.999)
        {
            using var disk = BuildDisk(radius);
            var o = new Mat();
            Cv2.MorphologyEx(mask, o, MorphTypes.Open, disk);
            return o;
        }

        int rs = Math.Max(2, (int)Math.Round(radius * scale));

        using var small = new Mat();
        Cv2.Resize(mask, small, new Size(), scale, scale, InterpolationFlags.Area);
        Cv2.Threshold(small, small, 127, 255, ThresholdTypes.Binary);
        using var diskSmall = BuildDisk(50);

        using var openedSmall = new Mat();
        Cv2.MorphologyEx(small, openedSmall, MorphTypes.Open, diskSmall);

        var opened = new Mat();
        Cv2.Resize(openedSmall, opened, mask.Size(), 0, 0, InterpolationFlags.Nearest);
        Cv2.Threshold(opened, opened, 127, 255, ThresholdTypes.Binary);
        return opened;
    }

    /// <summary>Structuring element hinh dia tron (doi xung xoay hoan toan).</summary>
    private static Mat BuildDisk(int r)
    {
        var k = new Mat(2 * r + 1, 2 * r + 1, MatType.CV_8UC1, Scalar.All(0));
        Cv2.Circle(k, new Point(r, r), r, Scalar.All(255), -1);
        return k;
    }

    private static Mat BuildRectangle(int r)
    {
        var k = new Mat(2 * r + 1, 2 * r + 1, MatType.CV_8UC1, Scalar.All(255));
        Dbg.Show(k, "hinh vuong");
        return k;
    }

    /// <summary>
    /// Giu component lon nhat VA lap lo ben trong cung mot luc:
    /// FindContours(External) chi tra ve bien ngoai, ve lai voi thickness = -1 la duoc mask dac.
    /// </summary>
    private static Mat LargestFilled(Mat mask)
    {
        using var work = mask.Clone();
        Cv2.FindContours(work, out Point[][] cnts, out _,
                         RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        Dbg.Show(work, "find contours");

        var res = new Mat(mask.Size(), MatType.CV_8UC1, Scalar.All(0));
        if (cnts == null || cnts.Length == 0) return res;

        var biggest = cnts.OrderByDescending(c => Cv2.ContourArea(c)).First();
        Cv2.DrawContours(res, new[] { biggest }, -1, Scalar.All(255), -1);
        Dbg.Show(res, "Vung tim");
        return res;
    }


    public static bool SaveImage(Mat image, string directoryPath, string prefix = "canny")
    {
        if (image == null || image.IsDisposed || image.Empty())
            return false;

        try
        {
            if (!Directory.Exists(directoryPath))
                Directory.CreateDirectory(directoryPath);

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            string fileName = $"{prefix}_{timestamp}.bmp";
            string fullPath = Path.Combine(directoryPath, fileName);

            // Ghi ảnh trực tiếp (Block thread hiện tại)
            return Cv2.ImWrite(fullPath, image);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SaveImage Error]: {ex.Message}");
            return false;
        }
    }

}
