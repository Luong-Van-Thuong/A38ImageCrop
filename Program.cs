using OpenCvSharp;
using OpenCvSharp.XImgProc;
using OpenCvSharp.XPhoto;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net.WebSockets;
using System.Numerics;
using System.Runtime.Intrinsics.X86;
using static ProtrusionDetector;
using static System.Net.Mime.MediaTypeNames;

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

    /// <summary>
    /// Bề ngang ảnh dùng cho BƯỚC THÔ (dò khối MLCC nằm chỗ nào).
    /// Mở ellipse 150px trên ảnh 2448px là chỗ chậm nhất cả pipeline; thu ảnh về ~800px
    /// thì cả ảnh lẫn kernel cùng nhỏ đi ~3 lần, nhanh hơn hàng chục lần.
    /// Bước này chỉ cần biết vùng ở đâu, sai vài chục px không sao vì đã có
    /// <see cref="PaddingThoPx"/> nới ra ngoài. Đặt = 0 để tắt, chạy nguyên cỡ ảnh gốc.
    /// </summary>
    public static int WorkWidthTho = 800;

    /// <summary>
    /// Nới khung cắt thô ra mỗi phía bao nhiêu px (đo ở ảnh GỐC).
    /// Thà cắt rộng ra ngoài chứ tuyệt đối không được liếm vào vùng cần soi.
    /// CHỈ dùng khi <see cref="KhungChuanPx"/> = 0; bật chuẩn hoá thì cửa sổ cố định
    /// đã rộng sẵn, nới thêm chỉ làm hỏng ngưỡng "khung có vừa cửa sổ không".
    /// </summary>
    public static int PaddingThoPx = 40;

    /// <summary>
    /// Cạnh ảnh vuông xuất ra cho YOLO, đo bằng px ảnh GỐC. 0 = tắt chuẩn hoá.
    ///
    /// Đo trên 193 ảnh cắt được: khung dò được rộng p50=346 p90=437 p99=535, cao p50=355
    /// p90=453 p99=716. Chọn 512 thì ~9/10 ảnh vừa gọn, cắt thẳng không phải nội suy.
    /// </summary>
    public static int KhungChuanPx = 512;

    /// <summary>Màu đệm khi cửa sổ chuẩn tràn ra ngoài ảnh — 114 xám là quy ước letterbox của YOLO.</summary>
    public static Scalar MauDemLetterbox = new Scalar(114, 114, 114);

    /// <summary>Đường kính ellipse mở để loại vùng thừa dính vào khối MLCC (px đo ở ảnh GỐC).</summary>
    public static int KernelLoaiVungThuaPx = 150;

    public static int BlurKernel = 5;

    public static int CannyLow = 50;
    public static int CannyHigh = 150;

    public static int MorphKernel = 5;

    public static double MinAreaRatio = 0.02;

    public static double ApproxEpsRatio = 0.02;

    public static bool WarpIfQuad = true;

    public static string InputImagePath = "D:\\Images_\\V2\\CoilAssy\\CoilAssy\\1240S\\opencv\\Image__2026-07-28__09-36-30.bmp";

    
    public static string InputFolderPath = "D:\\Images_\\JeaYoung\\Coil_Check_Co_Khong_Nghieng\\ChupNghieng\\nghiengLenX\\NG";
    //public static string InputFolderPath = "D:\\Images_\\JeaYoung\\Coil_Check_Co_Khong_Nghieng\\ChupNghieng\\nghiengLenX\\all";
    //public static string InputFolderPath = "D:\\Images_\\JeaYoung\\Coil_Check_Co_Khong_Nghieng\\1CamChieuThang\\Coil\\OPENCV\\anh3";
    //public static string InputFolderPath = "D:\\Images_\\JeaYoung\\Coil_Check_Co_Khong_Nghieng\\1CamChieuThang\\Coil\\OPENCV\\anh1";
    //public static string InputFolderPath = "D:\\Images_\\JeaYoung\\Coil_Check_Co_Khong_Nghieng\\1CamChieuThang\\Coil\\TRAIN_AI\\TEST";
    //public static string InputFolderPath = "D:\\Images_\\JeaYoung\\Coil_Check_Co_Khong_Nghieng\\1CamChieuThang\\Coil\\OPENCV\\ng";
   // public static string InputFolderPath = "D:\\Images_\\JeaYoung\\Coil_Check_Co_Khong_Nghieng\\1CamChieuThang\\Coil\\OPENCV\\Ng_";

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
    private static readonly string _outDirG2 = "crop_";
    private static readonly string _outDirNgang = "Ngang";
    private static readonly string _outDirDoc = "Doc";

    private static readonly string _outDirNotFound = "AIKhongTimThay_";

    /// <summary>Nơi ghi ảnh crop đã vẽ khung của model cam chéo (modelAICamCheo).</summary>
    private static readonly string _outDirAiCamCheo = "VungMLCC";

    /// <summary>Nơi ghi ảnh đã vẽ kết quả đo góc nghiêng. Tên file bắt đầu bằng góc đo được
    /// nên sắp theo tên là xem được ngay dải góc từ thẳng tới nghiêng nhất.</summary>
    private static readonly string _outDirGocNghieng = "GocNghieng_";

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

        // Bai tu MLCC co nghieng len khong: do goc alpha bang do mau theo hinh dang.
        if (args.Contains("--nghieng"))
            return YeaJoungCheckCoiNghieng.Chay(args);

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
                    //switch (ChayMotViec(cv.Path, cv.ThuTu, danhSachAnh.Count, chayCaMe))
                    //{
                    //    case KetQuaXuLy.ThanhCong: Interlocked.Increment(ref soThanhCong); break;
                    //    case KetQuaXuLy.KhongThayPhanNho: Interlocked.Increment(ref soKhongThayPhanNho); break;
                    //    default: Interlocked.Increment(ref soLoi); break;
                    //}
                    switch (ChayMotViec_AnhNghieng(cv.Path, cv.ThuTu, danhSachAnh.Count, chayCaMe))
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
                //switch (ChayMotViec(cv.Path, cv.ThuTu, danhSachAnh.Count, chayCaMe))
                //{
                //    case KetQuaXuLy.ThanhCong: soThanhCong++; break;
                //    case KetQuaXuLy.KhongThayPhanNho: soKhongThayPhanNho++; break;
                //    default: soLoi++; break;
                //}
                switch (ChayMotViec_AnhNghieng(cv.Path, cv.ThuTu, danhSachAnh.Count, chayCaMe))
                {
                    case KetQuaXuLy.ThanhCong: soThanhCong++; break;
                    case KetQuaXuLy.KhongThayPhanNho: soKhongThayPhanNho++; break;
                    default: soLoi++; break;
                }
            }
        }



        //XuatExcelAlign();

        // Tra InferRequest cua moi luong ve cho OpenVINO. Chay sau khi moi luong da xong.
        AiYoloCamCheo.Dong();

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
    private static KetQuaXuLy ChayMotViec_AnhNghieng(string inputPath, int thuTu, int tong, bool chayCaMe)
    {
        var tenAnh = Path.GetFileNameWithoutExtension(inputPath);

        if (chayCaMe || Config.SaveDebugStepsInBatch) Dbg.BatDauLuong(Path.Combine("debug_out", tenAnh));

        Dbg.Info($"===== [{thuTu}/{tong}] {Path.GetFileName(inputPath)} =====");

        KetQuaXuLy ketQua;
        try
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

            using Mat grayImg = new();
            Dbg.Show(src, "a");

            //Cv2.Resize(src, grayImg, new Size(640, 640), interpolation: InterpolationFlags.Linear);
            //Cv2.CvtColor(src, grayImg, ColorConversionCodes.BGR2GRAY);
            //Dbg.Show(grayImg, "grayImg");

            //using var laplacianImg = new Mat();
            //Cv2.Laplacian(grayImg, laplacianImg, MatType.CV_16S, 3);
            //Dbg.Show(laplacianImg, "laplacianImg");

            //using var absLaplacianImg = new Mat();
            //Cv2.ConvertScaleAbs(laplacianImg, absLaplacianImg);
            //Dbg.Show(absLaplacianImg, "absLaplacianImg");

            //using Mat threshLaplacian = new Mat();
            //Cv2.Threshold(absLaplacianImg, threshLaplacian, 60, 255, ThresholdTypes.Binary);
            //Dbg.Show(threshLaplacian, "threshLaplacian");

            //using Mat cleanMap = new Mat();
            //using var kernel_ = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(5, 5));
            //Cv2.Erode(threshLaplacian, cleanMap, kernel_, iterations: 1);
            //Cv2.Dilate(cleanMap, cleanMap, kernel_, iterations: 1);
            //Dbg.Show(cleanMap, "cleanMap");

            //using var energaMap = new Mat();
            //Cv2.BoxFilter(cleanMap, energaMap, MatType.CV_32F, new Size(15, 15), normalize: false, borderType: BorderTypes.Reflect);


            //Cv2.MinMaxLoc(energaMap, out _, out _, out _, out Point maxLoc);
            int kinhThuocCrop = 640;
            //int x = Math.Clamp(maxLoc.X - (kinhThuocCrop / 2), 0, src.Width - kinhThuocCrop);
            //int y = Math.Clamp(maxLoc.Y - (kinhThuocCrop / 2), 0, src.Height - kinhThuocCrop);

            //Rect cropRoi = new Rect(x, y, kinhThuocCrop, kinhThuocCrop);
            //using Mat imgSrcRoi = new Mat(src, cropRoi).Clone();
            //Dbg.Show(imgSrcRoi, "imgSrcRoi");

            // ---- Cho qua model AI cam cheo (thu muc modelAICamCheo) ------------------
            // QUAN TRONG: model nay duoc train tren ANH GOC NGUYEN CO 1280x1024 (nhan trong
            // DATA_TRAIN_200822: khung vungMLCC ~172x318 px tren anh goc), nen phai dua ANH GOC
            // vao, KHONG dua imgSrcRoi 512. Dua crop 512 vao thi con tu chiem gan het khung hinh,
            // lech han phan bo luc train -> model khong ra khung nao (da thu, count = 0).
            // Model an 640 nen ben trong AiYoloCamCheo tu letterbox roi tra khung ve DUNG he toa
            // do anh goc — dung thang de do/ve, khong phai nhan chia lai ti le.
            // Model 1 lop "vungMLCC"; khong nap duoc model thi tra list rong, pipeline chay tiep.
            var ketQuaAi = AiYoloCamCheo.Chay(src);

            if (ketQuaAi.Count == 0)
            {
                Dbg.Info($"  AI cam cheo: khong co khung nao vuot nguong {AiYoloCamCheo.May.NguongTinCay:0.00}.");
            }
            else
            {
                foreach (var kq in ketQuaAi) Dbg.Info($"  AI cam cheo: {kq}");
            }

            // Ve ra xem model nhin thay gi: khung + ten lop + do tin cay + tam khung.
            using Mat veAiCamCheo = AiYoloCamCheo.Ve(src, ketQuaAi);
            Dbg.Show(veAiCamCheo, "KetQuaAiCamCheo");

            // ---- Do goc nghieng trong khung AI (dang chay thu) ------------------------
            // Line doc ao vs truc noi trung tam canh tren - canh duoi cua than tu.
            // Ghi anh ve ra thu muc GocNghieng de soi tay xem cham canh dung chua.
            if (ketQuaAi.Count > 0)
            {
                Rect yoloBox = ketQuaAi[0].KhungInt;
                using Mat imgDoGoc = src.Clone();
                using Mat roiTu = new Mat(imgDoGoc, yoloBox);
                using Mat gray = new Mat();
                Cv2.CvtColor(roiTu, gray, ColorConversionCodes.BGR2GRAY);

                Dbg.Show(roiTu, "roiTu");
                // 2. Lấy đạo hàm theo trục Y (hoặc hướng nghiêng sơ bộ) để bắt ranh giới gốm - thiếc
                using Mat gradY = new Mat();
                Cv2.Sobel(gray, gradY, MatType.CV_16S, 0, 1, 3);
                using Mat absGradY = new Mat();
                Cv2.ConvertScaleAbs(gradY, absGradY);

                // 3. Lấy các điểm có gradient chuyển tiếp mạnh nhất trên từng cột (1D Peak Detection)
                List<Point2f> edgePoints = new List<Point2f>();
                int stepX = 2; // Quét cách 2 pixel một đường để tối ưu Cycle Time

                for (int col = 5; col < absGradY.Cols - 5; col += stepX)
                {
                    using Mat colData = absGradY.Col(col);
                    Cv2.MinMaxLoc(colData, out _, out double maxVal, out _, out Point maxLoc);

                    // Chỉ lấy điểm nếu độ tương phản gradient đủ rõ ràng
                    if (maxVal > 80)
                    {
                        // Tọa độ điểm biên trên ROI gốc
                        edgePoints.Add(new Point2f(yoloBox.X + col, yoloBox.Y + maxLoc.Y));
                        Cv2.Circle(imgDoGoc, new Point((int)yoloBox.X + col, (int)yoloBox.Y + maxLoc.Y), 4, Scalar.Red, -1);
                    }
                }

                if (edgePoints.Count < 5)
                {
                    Dbg.Log("NG");
                }

                // 4. Khớp đường thẳng ảo (FitLine) qua tập điểm biên bằng thuật toán Least Squares
                Line2D line = Cv2.FitLine(edgePoints, DistanceTypes.L2, 0, 0.01, 0.01);
                // 1. Lấy thông số toán học từ FitLine
                double vx = line.Vx; // hoặc line.Dx
                double vy = line.Vy; // hoặc line.Dy
                double x0 = line.X1; // hoặc line.X / line.Pt1.X (điểm đi qua)
                double y0 = line.Y1; // hoặc line.Y / line.Pt1.Y

                // 2. Kéo dài 2 đầu dựa trên kích thước ảnh để vẽ thành đường thẳng dài
                double length = Math.Max(imgDoGoc.Cols, imgDoGoc.Rows);

                Point pt1 = new Point((int)(x0 - vx * length), (int)(y0 - vy * length));
                Point pt2 = new Point((int)(x0 + vx * length), (int)(y0 + vy * length));

                // 3. Render lên ảnh
                Cv2.Line(imgDoGoc, pt1, pt2, Scalar.Red, 2, LineTypes.AntiAlias);
                Dbg.Show(imgDoGoc, "imgDoGoc");


                // 5. Tính góc của đường thẳng (line.Vx, line.Vy là vector chỉ phương)
                double angleRad = Math.Atan2(line.Vy, line.Vx);
                double angleDeg = angleRad * (180.0 / Math.PI);
                double rawDelta = Math.Abs(angleDeg) - 90.0;
                double deltaTilt = Math.Abs(rawDelta);
                float nguongGocNGLimit = 5.0f;
                bool isNG = deltaTilt > nguongGocNGLimit;

                float cx = yoloBox.X + yoloBox.Width / 2.0f;
                float cy = yoloBox.Y + yoloBox.Height / 2.0f;
                double refHalfLength = yoloBox.Height * 0.7;

                Point ptVertTop = new Point((int)cx, (int)(cy - refHalfLength));
                Point ptVertBottom = new Point((int)cx, (int)(cy + refHalfLength));
                // Đường xanh lá: Trục thẳng đứng chuẩn (0 độ lệch)
                Cv2.Line(imgDoGoc, ptVertTop, ptVertBottom, Scalar.LimeGreen, 2, LineTypes.AntiAlias);
                Cv2.Circle(imgDoGoc, new Point((int)cx, (int)cy), 4, Scalar.Yellow, -1);
                // Hiển thị text độ lệch góc lên ảnh để theo dõi trên màn hình UI
                Cv2.PutText(imgDoGoc, $"Tilt: {deltaTilt:F2} deg ({(isNG ? "NG" : "OK")})",
                            new Point((int)yoloBox.X, (int)yoloBox.Y - 10),
                            HersheyFonts.HersheySimplex, 0.5,
                            isNG ? Scalar.Red : Scalar.Green, 1);
                Dbg.Show(imgDoGoc, "imgDoGoc");


                // Chuẩn hóa góc về độ lệch so với phương thẳng đứng hoặc nằm ngang
                //double deltaAngle = Math.Abs(angleDeg);
                //if (deltaAngle > 90) deltaAngle = 180 - deltaAngle;

          

                // Tạo tọa độ vẽ Line ảo trực quan hóa
                Point2f p1 = new Point2f((float)(line.X1 - line.Vx * 50), (float)(line.Y1 - line.Vy * 50));
                Point2f p2 = new Point2f((float)(line.X1 + line.Vx * 50), (float)(line.Y1 + line.Vy * 50));
                Point pt3 = new Point((int)Math.Round(p1.X), (int)Math.Round(p1.Y));
                Point pt4 = new Point((int)Math.Round(p2.X), (int)Math.Round(p2.Y));
                Cv2.Line(src, pt3, pt4, new Scalar(0, 0, 255), thickness: 2, lineType: LineTypes.AntiAlias);
                Dbg.Show(imgDoGoc, "imgDoGoc");


                //var kqGoc = DoGocNghiengMLCC.Do(src, ketQuaAi[0].KhungInt, out Mat? veGoc);
                //Dbg.Info($"  Goc nghieng: {kqGoc}");
                //if (veGoc is not null)
                //{
                //    Dbg.Show(veGoc, "GocNghieng");
                LuuAnhG2(imgDoGoc, _outDirGocNghieng,
                         $"{nameImgMain}");
                //    veGoc.Dispose();
                //}
            }

            // Cat cua so 512 quanh TAM KHUNG AI (thay cho cach do nang luong Laplacian o tren:
            // cach cu chi bam vao cho net nhat, gap anh mo la bay ra cho khac han).
            Mat? roiTheoAi = null;
            if (ketQuaAi.Count > 0)
            {
                var kqTot = ketQuaAi[0];   // da sap theo do tin cay giam dan
                int xAi = Math.Clamp((int)Math.Round(kqTot.Tam.X) - kinhThuocCrop / 2, 0, Math.Max(0, src.Width - kinhThuocCrop));
                int yAi = Math.Clamp((int)Math.Round(kqTot.Tam.Y) - kinhThuocCrop / 2, 0, Math.Max(0, src.Height - kinhThuocCrop));
                int wAi = Math.Min(kinhThuocCrop, src.Width);
                int hAi = Math.Min(kinhThuocCrop, src.Height);

                Rect roiAi = new Rect(xAi, yAi, wAi, hAi);
                roiTheoAi = new Mat(src, roiAi).Clone();
                Dbg.Show(roiTheoAi, "imgSrcRoiTheoAi");

                //Dbg.Info($"  AI cam cheo: cua so 512 theo AI = ({roiAi.X},{roiAi.Y} {roiAi.Width}x{roiAi.Height}), " +
                //         $"cua so theo Laplacian = ({cropRoi.X},{cropRoi.Y}), " +
                //         $"lech ({roiAi.X - cropRoi.X},{roiAi.Y - cropRoi.Y}) px");

                LuuAnhG2(roiTheoAi, _outDirAiCamCheo, nameImgMain);
            }
            else
            {
                LuuAnhG2(veAiCamCheo, _outDirNotFound, nameImgMain);
            }



            // TODO (code tiep tu day): tu do nghieng bao nhieu, lech vi tri bao nhieu.
            // ketQuaAi da sap theo DoTinCay giam dan; cung lay lai duoc qua AiYoloCamCheo.KetQuaGanNhat.

            Mat results = new();
            //results = imgSrcRoi.Clone();
            // Co khung AI thi coi nhu tim thay vung, tra ve crop theo AI de buoc sau dung tiep.
            if (roiTheoAi is not null) { results.Dispose(); results = roiTheoAi; }
            if (results is null || results.Empty())
            {
                Dbg.Info("  Khong do duoc vung nao giam Config.CannyLow or Config.MinAreaRatio.");
                 //LuuAnhG2(results, _outDirNotFound, $"{nameImgMain}");
                return KetQuaXuLy.KhongThayPhanNho;
            }
            try
            {
                //LuuAnhG2(results, _outDirG2, $"{nameImgMain}");
                return KetQuaXuLy.ThanhCong;
            }
            finally
            {
                results.Dispose();
            }
            //ketQua = XuLyMotAnh(inputPath);
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


        if (result is null || result.Empty())
        {
            Dbg.Info("  Khong do duoc vung nao giam Config.CannyLow or Config.MinAreaRatio.");
           // LuuAnhG2(src, _outDirNotFound, $"{nameImgMain}");
            return KetQuaXuLy.KhongThayPhanNho;
        }
        try
        {
            return KetQuaXuLy.ThanhCong;
        }
        finally
        {
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

    /// <summary>
    /// Quy một kích thước DÀI (px đo ở ảnh gốc) về ảnh đã thu nhỏ theo <paramref name="scale"/>.
    /// Không bao giờ trả 0 — kernel 0px là morphology thành no-op, sai thầm lặng.
    /// </summary>
    private static int PxTheoScale(double pxGoc, double scale) =>
        Math.Max(1, (int)Math.Round(pxGoc * scale));

    /// <summary>
    /// Quy một ngưỡng DIỆN TÍCH (px² ở ảnh gốc) về ảnh thu nhỏ: dài co scale thì diện tích co scale².
    /// Quên bình phương ở đây là lọc mất sạch contour thật khi thu nhỏ 3 lần.
    /// </summary>
    private static double DienTichTheoScale(double dienTichGoc, double scale) =>
        dienTichGoc * scale * scale;

    /// <param name="scale">Tỉ lệ ảnh truyền vào so với ảnh gốc (1.0 = nguyên cỡ).
    /// Mọi kích thước hình thái học lấy từ ModelCfg đều đo ở ảnh gốc nên phải quy theo tỉ lệ này.</param>
    private static Mat VungDong(Mat src, double scale = 1.0)
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
            using var kMo = Dia(PxTheoScale(ModelCfg.MoVungDong, scale));
            Cv2.MorphologyEx(dong, dong, MorphTypes.Open, kMo);
            //Dbg.Show(dong, "dong");
        }
        if (ModelCfg.NoiRongDontCare > 0)
        {
            using var kNoi = Dia(PxTheoScale(ModelCfg.NoiRongDontCare, scale));
            Cv2.Dilate(dong, dong, kNoi);
            //Dbg.Show(dong, "dong");
        }

        // Phep no
        var result = Mat.Zeros(dong.Size(), MatType.CV_8UC1).ToMat();
        using Mat expanded = new();
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new OpenCvSharp.Size(50, 50));
        Cv2.Dilate(dong, expanded, kernel);
        //Dbg.Show(expanded, "dong");


        using var labels = new Mat();
        using var stats = new Mat();
        using var centroids = new Mat();
        int nLabels = Cv2.ConnectedComponentsWithStats(expanded, labels, stats, centroids, PixelConnectivity.Connectivity8, MatType.CV_32S);
        int maxLabel = 1;
        int maxArea = 0;
        int areaColIndex = (int)ConnectedComponentsTypes.Area;

        for (int i = 1; i < nLabels; i++)
        {
            int area = stats.At<int>(i, areaColIndex);
            if (area > maxArea)
            {
                maxArea = area;
                maxLabel = i;
            }
        }

        Cv2.Compare(labels, new Scalar(maxLabel), labels, CmpTypes.EQ);
        //Dbg.Show(labels, "a");

        Cv2.BitwiseAnd(labels, dong, dong);
        //Dbg.Show(dong, "a");
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

    /// <param name="scale">Tỉ lệ ảnh truyền vào so với ảnh gốc (1.0 = nguyên cỡ).</param>
    private static Mat VungTrangBac_Gray(Mat src, double scale = 1.0)
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
                Cv2.Threshold(hieuBR, binaryTho, 20, 255, ThresholdTypes.Binary);
                //Dbg.Show(hieuBR, "a");
            } // Hết block using này, 3 kênh kenh[0..2] và hieuBR mới được Dispose an toàn!
        }
        else
        {
            Cv2.Threshold(src, binaryTho, 100, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
        }

        // 2. Morphology CLOSE: Nối liền các vết nứt xước
        int kSize = PxTheoScale(ModelCfg.MoVungDong > 0 ? ModelCfg.MoVungDong : 7, scale);
        using var kernelClose = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(kSize, kSize));
        using var closed = new Mat();
        Cv2.MorphologyEx(binaryTho, closed, MorphTypes.Close, kernelClose);
        //Dbg.Show(binaryTho, "a");
        //Dbg.Show(closed, "a");
        //Dbg.Show(src, "a");


        using var matDen = new Mat();
        Cv2.CvtColor(src, matDen, ColorConversionCodes.BGR2GRAY);
        Cv2.Threshold(matDen, matDen, 80, 255, ThresholdTypes.Binary);
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
            if (area > DienTichTheoScale(200, scale)) // Lọc nhiễu vụn
            {
                Cv2.DrawContours(mask, contours, i, Scalar.White, -1); // -1: Fill đặc
                //Dbg.Show(mask, "a");
            }
        }

        // 4. Dilate nếu cần nới rộng Don't Care
        if (ModelCfg.NoiRongDontCare > 0)
        {
            using var kNoi = Dia(PxTheoScale(ModelCfg.NoiRongDontCare, scale));
            Cv2.Dilate(mask, mask, kNoi);
        }
        //Dbg.Show(mask, "a");
        //LuuAnhG2(mask, _outDirG2, $"anh1");

        //Dbg.Show(matDen, "a");
        //LuuAnhG2(matDen, _outDirG2, $"anh2");
        using Mat result = new Mat();
        Cv2.BitwiseAnd(matDen, mask, mask);
        //Dbg.Show(mask, "a");

        Cv2.BitwiseNot(mask, mask);
        //Dbg.Show(mask, "a");
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
        //Dbg.Show(binaryInput, "a");
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(5, 5));
        Cv2.Dilate(binaryInput, binaryInput, kernel);
        //Dbg.Show(binaryInput, "a");
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
    /// <param name="scale">Tỉ lệ ảnh truyền vào so với ảnh gốc (1.0 = nguyên cỡ).</param>
    public static Mat LayVungDenBenTrong(Mat binarySrc, double scale = 1.0)
    {
        //Dbg.Show(binarySrc, "a");
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
            if (area > DienTichTheoScale(1000, scale)) // Lọc bỏ contour vụn ở viền biên nếu có
            {
                Cv2.DrawContours(filledWhite, contours, i, Scalar.White, -1); // -1: Fill đặc ruột
            }
        }

        // 2. Phép trừ ma trận: Lấy Khối Trắng Đặc trừ đi Ảnh Gốc
        // Kết quả: Chỉ những chỗ là Đen (0) nằm bên trong ruột mới trở thành Trắng (255)
        Mat internalBlackMask = new Mat();
        //Dbg.Show(binarySrc, "a");
        //Dbg.Show(filledWhite, "a");
        Cv2.Subtract(filledWhite, binarySrc, internalBlackMask);
        //Dbg.Show(internalBlackMask, "a");
        // 3. (Tùy chọn) Morphology để lọc nhiễu các đường gân xước quá nhỏ
        // using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));
        // Cv2.MorphologyEx(internalBlackMask, internalBlackMask, MorphTypes.Open, kernel);

        return internalBlackMask; // Trả về ảnh có các vùng đen bên trong nổi lên thành màu TRẮNG
    }


    /// <summary>
    /// Cắt ra một ảnh VUÔNG cỡ chuẩn quanh <paramref name="khung"/> để làm đầu vào YOLO.
    ///
    /// Camera cố định, con tụ luôn cùng một cỡ pixel, nên chuẩn hoá đúng ở đây là lấy một
    /// CỬA SỔ CỐ ĐỊNH trên ảnh gốc, không phải resize khung dò được: resize thì khung 245px
    /// và khung 1049px cùng ra 512, model mất sạch manh mối kích thước thật — mà bài này
    /// phân biệt nghiêng/không nghiêng chính là nhìn kích thước với tỉ lệ.
    /// Giữ nguyên thang đo px/vật thì mọi ảnh huấn luyện cùng một hệ quy chiếu,
    /// lại không tốn lần nội suy nào.
    ///
    /// Ba tình huống, theo thứ tự hay gặp:
    ///  1. Cửa sổ nằm gọn trong ảnh  -> cắt thẳng, KHÔNG resize (đường thường, ~9/10 ảnh).
    ///  2. Cửa sổ thò ra mép ảnh     -> đẩy vào trong, vẫn đủ cạnh, vẫn đúng thang đo.
    ///     (đẩy chứ không xén: xén thì ảnh ra thiếu cạnh, phải đệm viền giả vô ích)
    ///  3. Khung to hơn cả cửa sổ    -> nới cửa sổ thành vuông trùm hết khung rồi thu ĐỀU
    ///     về cỡ chuẩn. Thang đo ảnh đó lệch, nhưng thà lệch còn hơn cắt cụt mất một đầu tụ.
    /// </summary>
    /// <param name="khung">Khung dò được, toạ độ ảnh gốc.</param>
    /// <param name="canh">Cạnh ảnh vuông muốn xuất ra (px ảnh gốc).</param>
    private static Mat ChuanHoaKhungYolo(Mat src, Rect khung, int canh)
    {
        // Bình thường cửa sổ đúng bằng cạnh chuẩn; chỉ nở ra khi khung dò được quá to.
        int canhCuaSo = Math.Max(canh, Math.Max(khung.Width, khung.Height));

        int tamX = khung.X + khung.Width / 2;
        int tamY = khung.Y + khung.Height / 2;
        Rect cuaSo = new Rect(tamX - canhCuaSo / 2, tamY - canhCuaSo / 2, canhCuaSo, canhCuaSo);

        // Đẩy cửa sổ vào trong ảnh thay vì xén nó.
        cuaSo.X = Math.Clamp(cuaSo.X, 0, Math.Max(0, src.Cols - canhCuaSo));
        cuaSo.Y = Math.Clamp(cuaSo.Y, 0, Math.Max(0, src.Rows - canhCuaSo));

        Rect phanThat = cuaSo & new Rect(0, 0, src.Cols, src.Rows);

        Mat vuong;
        if (phanThat.Width == canhCuaSo && phanThat.Height == canhCuaSo)
        {
            vuong = new Mat(src, phanThat).Clone();
        }
        else
        {
            // Chỉ tới đây khi cả ảnh gốc còn nhỏ hơn cửa sổ (đổi camera thì gặp).
            // Đệm cân hai bên để con tụ vẫn nằm giữa, lệch tâm là hỏng dữ liệu học.
            int thieuNgang = canhCuaSo - phanThat.Width;
            int thieuDoc = canhCuaSo - phanThat.Height;
            vuong = new Mat();
            using Mat cat = new Mat(src, phanThat);
            Cv2.CopyMakeBorder(cat, vuong,
                thieuDoc / 2, thieuDoc - thieuDoc / 2,
                thieuNgang / 2, thieuNgang - thieuNgang / 2,
                BorderTypes.Constant, Config.MauDemLetterbox);
        }

        if (canhCuaSo == canh) return vuong;

        // Vuông thu về vuông nên không méo tỉ lệ, chỉ đổi thang đo.
        using (vuong)
        {
            Dbg.Log($"khung {khung.Width}x{khung.Height} to hon cua so {canh}, " +
                    $"thu deu tu {canhCuaSo} ve {canh}");
            Mat ketQua = new Mat();
            Cv2.Resize(vuong, ketQua, new Size(canh, canh), 0, 0, InterpolationFlags.Area);
            return ketQua;
        }
    }

    private static Mat? CropLargestRegion(Mat src, string nameImgMain)
    {
        // ---- Bước THÔ chạy trên ảnh thu nhỏ --------------------------------------
        // Cả đoạn dưới chỉ để trả lời "khối MLCC nằm chỗ nào", không đo đạc gì, nên
        // chạy ở độ phân giải thấp là đủ. Mọi kernel/ngưỡng diện tích đều đo theo px
        // ảnh GỐC rồi quy về scale, nên đổi WorkWidthTho không làm lệch hình thái học.
        double scale = Config.WorkWidthTho > 0
            ? Math.Min(1.0, (double)Config.WorkWidthTho / src.Width)
            : 1.0;

        using var work = new Mat();
        if (scale < 1.0)
            Cv2.Resize(src, work, new Size(), scale, scale, InterpolationFlags.Area);
        else
            src.CopyTo(work);
        //Dbg.Show(work, "work_tho");

        using var dong = VungDong(work, scale);
        //Dbg.Show(dong, "vungdong");

        // Khung bao vùng đồng, tính trong hệ toạ độ ảnh work.
        // Không tìm được thì lấy cả ảnh, đừng để workSub rỗng rồi chết ở bước sau.
        Rect khungDong = new Rect(0, 0, work.Cols, work.Rows);
        using (Mat nonZeroPts = new())
        {
            Cv2.FindNonZero(dong, nonZeroPts);
            if (!nonZeroPts.Empty())
            {
                Rect r = Cv2.BoundingRect(nonZeroPts) & new Rect(0, 0, work.Cols, work.Rows);
                if (r.Width > 0 && r.Height > 0) khungDong = r;
            }
        }

        using Mat workSub = new(work, khungDong);
        Dbg.Show(workSub, "Img Cropp");

        // Vùng đồng 2
        //using Mat dong_ = VungDong_2(workSub);
        //Dbg.Show(dong_, "VungDong2");
        //LuuAnhG2(dong_, _outDirG2, $"VungDong2");


        // Vùng sáng bạc
        using Mat vungSangBac = VungTrangBac_Gray(workSub, scale);
        Dbg.Show(vungSangBac, "VungSangBac");
        //LuuAnhG2(vungSangBac, _outDirG2, $"VungSangBac");


        // Lấy vùng có tụ
        using Mat vungMlcc = LayVungDenBenTrong(vungSangBac, scale);
        Dbg.Show(vungMlcc, "a");
        // Lấy vùng trắng lớn nhất
        using Mat vungTrangLonNhat = LayVungTrangLonNhat(vungMlcc);
        //Dbg.Show(vungTrangLonNhat, "a");
        // Loại bỏ vùng thừa — đây là chỗ tốn thời gian nhất của cả hàm:
        // ellipse 150px trên ảnh gốc, sau khi thu nhỏ chỉ còn ~150*scale px.
        int dKernel = PxTheoScale(Config.KernelLoaiVungThuaPx, scale);
        using var kernelLoaiVung1 = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(dKernel, dKernel));
        using Mat vungMlcc2 = new();
        Cv2.MorphologyEx(vungTrangLonNhat, vungMlcc2, MorphTypes.Open, kernelLoaiVung1);
        //Dbg.Show(vungMlcc2, "a");

        using Mat nonZeroPtsVungMlcc = new();
        Cv2.FindNonZero(vungMlcc2, nonZeroPtsVungMlcc);
        if (nonZeroPtsVungMlcc.Empty())
        {
            Dbg.Info("  Buoc tho: khong tim thay vung MLCC nao.");
            return null;
        }

        // ---- Quy khung về TOẠ ĐỘ ẢNH GỐC rồi mới cắt -----------------------------
        // workSub -> work: cộng offset của lần cắt theo vùng đồng.
        Rect roiWork = Cv2.BoundingRect(nonZeroPtsVungMlcc);
        roiWork = new Rect(roiWork.X + khungDong.X, roiWork.Y + khungDong.Y,
                           roiWork.Width, roiWork.Height);

        // work -> gốc: chia scale. Cộng thêm 1/scale px để bù đúng phần bị làm tròn
        // khi thu nhỏ (1 px ảnh work = 1/scale px ảnh gốc), rồi mới nới padding.
        // Cắt rộng ra ngoài thì không sao, liếm vào vùng cần soi mới là hỏng.
        Rect khungAnh = new Rect(0, 0, src.Cols, src.Rows);
        int buLamTron = (int)Math.Ceiling(1.0 / scale);
        Rect roiGoc = new Rect(
            (int)Math.Floor(roiWork.X / scale) - buLamTron,
            (int)Math.Floor(roiWork.Y / scale) - buLamTron,
            (int)Math.Ceiling(roiWork.Width / scale) + 2 * buLamTron,
            (int)Math.Ceiling(roiWork.Height / scale) + 2 * buLamTron);
        roiGoc &= khungAnh;

        if (roiGoc.Width <= 0 || roiGoc.Height <= 0)
        {
            Dbg.Info($"  Buoc tho: khung quy ve anh goc bi rong ({roiWork} @ scale {scale:0.###}).");
            return null;
        }

        Dbg.Log($"cat tho: work {roiWork} @ scale {scale:0.###} -> goc {roiGoc}");

        // Clone: new Mat(src, roi) chỉ là header trỏ vào src, mà src bị Dispose ngay
        // khi ra khỏi XuLyMotAnh — trả về view là trả về con trỏ treo.
        Mat vungMLCCMain;
        if (Config.KhungChuanPx > 0)
        {
            // Chuẩn hoá về cỡ cố định cho YOLO. Padding thô không dùng ở nhánh này:
            // cửa sổ 512 đã rộng gấp rưỡi khung dò được, nới thêm chỉ tổ đẩy những
            // khung sát 512 vượt ngưỡng rồi phải thu nhỏ vô ích.
            vungMLCCMain = ChuanHoaKhungYolo(src, roiGoc, Config.KhungChuanPx);
        }
        else
        {
            var roiNoi = new Rect(
                roiGoc.X - Config.PaddingThoPx, roiGoc.Y - Config.PaddingThoPx,
                roiGoc.Width + 2 * Config.PaddingThoPx,
                roiGoc.Height + 2 * Config.PaddingThoPx) & khungAnh;
            vungMLCCMain = new Mat(src, roiNoi).Clone();
        }
        Dbg.Show(vungMLCCMain, "VungMLCCMain");
        //LuuAnhG2(vungMLCCMain, _outDirG2, $"{nameImgMain}");

        // ---- Chạy qua mô hình AI -------------------------------------------------
        // Ảnh vào đây là cửa sổ chuẩn 512 giữ nguyên thang đo px/vật, đúng thứ đã dùng
        // để huấn luyện, nên đưa thẳng vào model, không tiền xử lý gì thêm.
        // Toạ độ trong ketQuaAi là hệ ảnh vungMLCCMain — muốn quy về ảnh gốc thì cộng
        // offset cửa sổ (xem ChuanHoaKhungYolo), nhưng đo nghiêng/lệch thì dùng luôn hệ này.
        var ketQuaAi = AiYolo.Chay(vungMLCCMain);

        bool result = ProcessMLCCPipeline(vungMLCCMain, ketQuaAi, nameImgMain);

        //Text hiển thị trên ảnh kết quả AI NG
        //string textNG = "NG";
        //var font = HersheyFonts.Italic;
        //double fontScale = 3;
        //int thickness = 10;

        //var textSize = Cv2.GetTextSize(textNG, font, fontScale, thickness, out _);
        //int posX = vungMLCCMain.Width - textSize.Width - 10;
        //int posY = textSize.Height + 10;
        //// Tính kích thước chữ thực tế


        ////bool coMLCC = ketQuaAi.Any(x => x.TenLop.ToUpper().Contains("MLCC", StringComparer.OrdinalIgnoreCase));
        //bool coMLCC = ketQuaAi.Any(x => x.TenLop.ToUpper().Contains("MLCC", StringComparison.OrdinalIgnoreCase));
        //using Mat veAi_ = vungMLCCMain.Clone();
        //if (ketQuaAi.Count == 0 || !coMLCC)
        //{
        //    Dbg.Info("  AI: khong co ket qua nao vuot nguong.");
        //    Cv2.PutText(veAi_, textNG, new Point(posX, posY), font, fontScale, Scalar.Red, thickness);
        //    Dbg.Show(veAi_, "KetQuaAI");
        //}
        //else
        //{
        //    //foreach (var kq in ketQuaAi) Dbg.Info($"  AI: {kq}");

        //    // Vẽ ra xem model nhìn thấy gì — tắt bằng Dbg.Enabled như mọi bước khác.
        //    if (Dbg.Enabled)
        //    {                
        //        foreach (var kq in ketQuaAi)
        //        {
        //            Cv2.Rectangle(veAi_, kq.KhungInt, Scalar.Lime, 2);
        //            Cv2.PutText(veAi_, $"{kq.TenLop} {kq.DoTinCay:0.00}",
        //                        new Point(kq.KhungInt.X, Math.Max(12, kq.KhungInt.Y - 4)),
        //                        HersheyFonts.HersheySimplex, 0.5, Scalar.Lime, 1);

        //            using Mat veAi = veAi_.Clone();

        //            // Bắt đầu chỉnh ----------------
        //            int x = Math.Max(0, kq.KhungInt.X);
        //            int y = Math.Max(0, kq.KhungInt.Y);
        //            int w = Math.Min(veAi_.Width - x, kq.KhungInt.Width);
        //            int h = Math.Min(veAi_.Height - y, kq.KhungInt.Height);
        //            // Nếu AI bắt lỗi box quá bé hoặc nằm ngoài ảnh -> Bỏ qua
        //            if (w <= 2 || h <= 2) continue;
        //            var roiRect = new Rect(x, y, w, h);
        //            // 2. Ép kiểu ảnh tham chiếu (Không tốn RAM Clone)

        //            using Mat roi = new Mat(veAi_, roiRect); // Cần xửa lại vungMLCCMain vì đây không phải vùng AI tìm thấy
        //            using Mat gray = new Mat();
        //            using Mat binary = new Mat();
        //            if (roi.Channels() > 1)
        //                Cv2.CvtColor(roi, gray, ColorConversionCodes.BGR2GRAY);
        //            else
        //                roi.CopyTo(gray);
        //            // 3. Phân ngưỡng siêu tốc để bóc tách vật thể
        //            // (Nếu linh kiện tối trên nền sáng thì dùng ThresholdTypes.BinaryInv)
        //            Cv2.Threshold(gray, binary, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
        //            // 4. Tìm viền (chỉ lấy viền ngoài cùng)
        //            Cv2.FindContours(binary, out Point[][] contours, out _,
        //                             RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        //            if (contours.Length == 0) continue;
        //            // 5. Lấy vùng bự nhất (Blob to nhất trong ROI)
        //            Point[] maxContour = contours.OrderByDescending(c => Cv2.ContourArea(c)).First();
        //            // 6. Tính TRỌNG TÂM (Center of Mass) bằng Image Moments -> Cực kỳ chính xác
        //            Moments m = Cv2.Moments(maxContour);
        //            if (m.M00 > 10) // M00 chính là diện tích Pixel, phải lớn hơn 10px để tránh nhiễu
        //            {
        //                // Toạ độ tâm cục bộ trong ROI
        //                double localCx = m.M10 / m.M00;
        //                double localCy = m.M01 / m.M00;

        //                // Bù trừ Offset để ra TOẠ ĐỘ TRÊN ẢNH LỚN
        //                Point2d tamThucTe = new Point2d(localCx + roiRect.X, localCy + roiRect.Y);

        //                Dbg.Info($"[TỌA ĐỘ CHUẨN] {kq.TenLop}: X = {tamThucTe.X:0.00}, Y = {tamThucTe.Y:0.00}");

        //                // Vẽ chữ thập định vị (Crosshair) lên ảnh kết quả
        //                if (Dbg.Enabled)
        //                {
        //                    Point pt = new Point((int)tamThucTe.X, (int)tamThucTe.Y);
        //                    Cv2.DrawMarker(veAi_, pt, Scalar.Magenta, MarkerTypes.Cross, 15, 2);
        //                    Dbg.Show(veAi_, "KetQuaAI");
        //                }
        //            }
        //            //-------------------------------

        //        }
        //        Dbg.Show(veAi_, "KetQuaAI");
        //    }
        //}
        //Dbg.Show(veAi_, "KetQuaAI");
        int soA = 0;

        // TODO (bạn code tiếp từ đây): có tụ hay không, nghiêng bao nhiêu, lệch vị trí bao nhiêu.
        // ketQuaAi đã sắp theo DoTinCay giảm dần; cũng lấy lại được qua AiYolo.KetQuaGanNhat.



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
        return vungMLCCMain;

    }

    public static bool ProcessMLCCPipeline(Mat vungMLCCMain, List<AiKetQua> ketQuaAi, string nameImgMain)
    {
        bool trueOrFalse = false;
        // 1. Kiểm tra điều kiện tiên quyết (SLA Gate)
        bool coMLCC = ketQuaAi.Any(x => x.TenLop.Contains("MLCC", StringComparison.OrdinalIgnoreCase));  
        coMLCC = true;
        if (ketQuaAi==null || ketQuaAi.Count()<=0 || 
            ketQuaAi.Any(x => x.TenLop.Contains("NG", StringComparison.OrdinalIgnoreCase)) || !coMLCC)
        {
            using Mat veAi_ = vungMLCCMain.Clone();
            Dbg.Info("  AI: [NG] Không có kết quả hoặc thiếu nhãn MLCC.");
            VeThongBaoNG(veAi_, "NG", Scalar.Red);
            Dbg.Show(veAi_, "00_KetQua_NG");
            foreach (var kq in ketQuaAi)
            {
                if(kq.TenLop.ToLower().Contains("ng"))
                {
                    Cv2.Rectangle(veAi_, kq.KhungInt, Scalar.Red, 1);
                    Cv2.PutText(veAi_, $"{kq.TenLop} {kq.DoTinCay:0.00}",
                                new Point(kq.KhungInt.X, Math.Max(14, kq.KhungInt.Y - 4)),
                                HersheyFonts.HersheySimplex, 0.45, Scalar.Red, 1);
                }    
                else
                {
                    Cv2.Rectangle(veAi_, kq.KhungInt, Scalar.Lime, 1);
                    Cv2.PutText(veAi_, $"{kq.TenLop} {kq.DoTinCay:0.00}",
                                new Point(kq.KhungInt.X, Math.Max(14, kq.KhungInt.Y - 4)),
                                HersheyFonts.HersheySimplex, 0.45, Scalar.Lime, 1);
                }
                return trueOrFalse;
            }
            if (Dbg.Enabled)
            {
                
                VeThongBaoNG(veAi_, "NG", Scalar.Red);
                Dbg.Show(veAi_, "00_KetQua_NG");
            }
            return trueOrFalse;
        }

        // 2. Trích xuất tâm chính xác từng ROI từ ẢNH GỐC SẠCH
        var danhSachDiem = new List<(string TenLop, Point2d Tam)>();
        for (int i = 0; i < ketQuaAi.Count; i++)
        {
            var kq = ketQuaAi[i];
            if (TinhTamChinhXac(vungMLCCMain, kq, out Point2d tamThucTe, out Mat roiDebugBinary, 1500))
            {
                danhSachDiem.Add((kq.TenLop, tamThucTe));

                // Debug từng ROI cục bộ để soi threshold có bị vỡ viền hay không
                if (Dbg.Enabled && roiDebugBinary != null)
                {
                    //Dbg.Show(roiDebugBinary, $"01_ROI_Binary_{kq.TenLop}_{i}");
                    roiDebugBinary.Dispose();
                }
            }
        }

        // 3. Render Debug toàn cục (AI Bounding Box + Crosshair + Dựng đường)
        if (Dbg.Enabled)
        {
            using Mat veAi_ = vungMLCCMain.Clone();

            // 3.1. Vẽ Bounding Box thô của AI
            //foreach (var kq in ketQuaAi)
            //{
            //    Cv2.Rectangle(veAi_, kq.KhungInt, Scalar.Lime, 1);
            //    Cv2.PutText(veAi_, $"{kq.TenLop} {kq.DoTinCay:0.00}",
            //                new Point(kq.KhungInt.X, Math.Max(14, kq.KhungInt.Y - 4)),
            //                HersheyFonts.HersheySimplex, 0.45, Scalar.Lime, 1);
            //}

            // 3.2. Vẽ điểm tâm chính xác (Crosshair Magenta)
            //foreach (var item in danhSachDiem)
            //{
            //    Cv2.DrawMarker(veAi_, new Point((int)item.Tam.X, (int)item.Tam.Y),
            //                   Scalar.Magenta, MarkerTypes.Cross, 16, 2);
            //}


            Dbg.Show(veAi_, "02_KetQua_TongThe");
            // 3.3. Dựng các đường bao Đỏ - Xanh

            var toaDoList = danhSachDiem.Where(x=>x.TenLop.ToUpper().Contains("TOADO")).Select(x => x.Tam).ToList();

            var toaDoMLCC = ketQuaAi.Where(x => x.TenLop.ToUpper().Contains("MLCC")).FirstOrDefault();
            if(toaDoMLCC == null)
            {
                Dbg.Info("  AI: [NG] Không tìm thấy nhãn MLCC.");
                VeThongBaoNG(veAi_, "NG", Scalar.Red);
                Dbg.Show(veAi_, "02_KetQua_TongThe");
                return trueOrFalse;
            }
            bool kqVung_ = DungHeTrucVaDuongBao_New(veAi_, toaDoList, toaDoMLCC, 10, nameImgMain);
            //bool kqVung = DungHeTrucVaDuongBao(veAi_, toaDoList, toaDoMLCC, 30);
            bool kqVung = true;
            
            //if(kqVung)
            //{
            //    VeThongBaoNG(veAi_, "OK", Scalar.LimeGreen);
            //    LuuAnhG2(veAi_, _outDirG2, $"_{nameImgMain}");
            //    trueOrFalse = true;
            //}    
            //else
            //{
            //    VeThongBaoNG(veAi_, "NG", Scalar.Red);
            //    LuuAnhG2(veAi_, _outDirNotFound, $"_{nameImgMain}");
            //    trueOrFalse = false;
            //}

            //Dbg.Show(veAi_, "02_KetQua_TongThe");
            
        }
        return trueOrFalse;
    }



    private static Point2d? TimGiaoDiem(Point2d p1, Point2d d1, Point2d p2, Point2d d2)
    {
        // Định thức (Determinant / 2D Cross Product)
        double det = d1.X * d2.Y - d1.Y * d2.X;

        // Song song hoặc cùng phương -> Không có giao điểm hợp lệ
        if (Math.Abs(det) < 1e-6)
            return null;

        double dx = p2.X - p1.X;
        double dy = p2.Y - p1.Y;
        double t = (dx * d2.Y - dy * d2.X) / det;

        return new Point2d(p1.X + t * d1.X, p1.Y + t * d1.Y);
    }

    /// <summary>
    /// Bóc tách ROI sạch, Threshold và tính trọng tâm bằng Image Moments.
    /// </summary>
    private static bool TinhTamChinhXac(Mat anhGoc, AiKetQua kq, out Point2d tamThucTe, out Mat roiBinaryDebug, double dienTichToiThieu)
    {
        tamThucTe = default;
        roiBinaryDebug = null;

        // Clamp ROI an toàn trong biên ảnh gốc
        int x = Math.Max(0, kq.KhungInt.X);
        int y = Math.Max(0, kq.KhungInt.Y);
        int w = Math.Min(anhGoc.Width - x, kq.KhungInt.Width);
        int h = Math.Min(anhGoc.Height - y, kq.KhungInt.Height);

        if (w <= 2 || h <= 2) return false;
        var roiRect = new Rect(x, y, w, h);

        // Cắt Sub-matrix từ ảnh gốc sạch (Zero-copy)
        using Mat roi = new Mat(anhGoc, roiRect);
        using Mat gray = new Mat();
        Mat binary = new Mat();

        if (roi.Channels() > 1)
            Cv2.CvtColor(roi, gray, ColorConversionCodes.BGR2GRAY);
        else
            roi.CopyTo(gray);

        Cv2.Threshold(gray, binary, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

        Cv2.FindContours(binary, out Point[][] contours, out _,
                         RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        if (contours.Length == 0)
        {
            binary.Dispose();
            return false;
        }

        Point[] maxContour = contours.OrderByDescending(c => Cv2.ContourArea(c)).First();
        Moments m = Cv2.Moments(maxContour);

        if (m.M00 > dienTichToiThieu) // Diện tích tối thiểu để loại bỏ nhiễu hạt
        {
            double localCx = m.M10 / m.M00;
            double localCy = m.M01 / m.M00;

            tamThucTe = new Point2d(localCx + roiRect.X, localCy + roiRect.Y);
            roiBinaryDebug = binary; // Trả ra để debug
            return true;
        }

        binary.Dispose();
        return false;
    }

    private static bool DungHeTrucVaDuongBao_New(Mat img, List<Point2d> points, AiKetQua kqMLCC, double nguongLechChoPhep = 30, string nameImg="")
    {
        bool isPassViTri = false;
        bool isVertical = true; 
        Point2d dirMain, dirSub;
        if (isVertical)
        {
            Mat imgTest = img.Clone();
            Dbg.Show(imgTest, "a");
            Dbg.Show(img, "a");
            DetectWhitePads(imgTest, nameImg);
        }
        int a = 2;
            
        return isPassViTri;
    }

    public static List<Rect> DetectWhitePads(Mat srcBgr, string nameImg)
    {
        Mat srcTest = srcBgr.Clone();
        var padBoxes = new List<Rect>();
        // 1. Tách kênh BGR - Kênh Blue ở vị trí index 0
        Mat[] channels = Cv2.Split(srcBgr);
        using Mat blueChannel = channels[0];
        channels[1].Dispose(); // Giải phóng Green
        channels[2].Dispose(); // Giải phóng Red

        // 2. Threshold kênh Blue với ngưỡng cao (loại bỏ màu vàng)
        using Mat binary = new Mat();
        Cv2.Threshold(blueChannel, binary, 230, 255, ThresholdTypes.Binary);
        Dbg.Show(binary, "a");
        // 3. Khử nhiễu bằng Morphological Opening (Erode rồi Dilate)
        using Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(10,10));
        using Mat kernel_2 = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(30, 30));
        using Mat kernel_3 = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(10, 10));
        using Mat cleanBinary = new Mat();
        Cv2.MorphologyEx(binary, cleanBinary, MorphTypes.Close, kernel);
        Dbg.Show(cleanBinary, "a");
        Cv2.MorphologyEx(cleanBinary, cleanBinary, MorphTypes.Open, kernel_2);
        Dbg.Show(cleanBinary, "a");

        // 4. Tìm Contours ngoài cùng
        Cv2.FindContours(
            cleanBinary,
            out Point[][] contours,
            out HierarchyIndex[] hierarchy,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple
        );

        // 5. Lọc Blobs theo diện tích và vị trí bên phải
        int minArea = 300; // Điều chỉnh tùy theo độ phân giải FOV thực tế
        int midX = srcBgr.Width / 2;

        var validPads = new List<(Point[] Contour, double Area, Rect Box)>();

        foreach (var contour in contours)
        {
            double area = Cv2.ContourArea(contour);
            if (area < minArea) continue;

            Rect box = Cv2.BoundingRect(contour);

            // 2 đốm pad luôn nằm về phía bên phải so với thân MLCC
            if (box.X > midX)
            {
                validPads.Add((contour, area, box));
            }
        }

        // 6. Lấy 2 đốm to nhất và sắp xếp từ trên xuống dưới
        var sortedPads = validPads
            .OrderByDescending(p => p.Area)
            .Take(2)
            .OrderBy(p => p.Box.Y
            )
            .Select(p => p.Box)
            .ToList();

        if(sortedPads.Count>=2)
        {
            Rect padTop = default;
            Rect padBottom = default;
            int y = 10;

            padTop = sortedPads[0].Y < sortedPads[1].Y ? sortedPads[0] : sortedPads[1];
            padBottom = sortedPads[0].Y < sortedPads[1].Y ? sortedPads[1] : sortedPads[0];

            //Cv2.Rectangle(srcBgr, padTop, new Scalar(0, 0, 255), 2);
            //Point center = new Point(padTop.X + padTop.Width / 2, padTop.Y + padTop.Height / 2);
            //Cv2.Circle(srcBgr, center, 3, new Scalar(0, 255, 0), -1);

            CreateComponentRoi(srcBgr, padTop, padBottom, 10, 10, 80, 40, srcTest, nameImg);
            //foreach (var pad in sortedPads)
            //{   
            //    Cv2.Rectangle(srcBgr, pad, new Scalar(0,0,255), 2);
            //    Point center = new Point(pad.X + pad.Width / 2, pad.Y + pad.Height / 2);
            //    Cv2.Circle(srcBgr, center, 3, new Scalar(0, 255, 0), -1);
            //}
            Dbg.Show(srcBgr, "a");
        }    

        

        return sortedPads;
    }

    /// <summary>
    /// Tạo khung chữ nhật số 4 bao quanh tụ từ 2 Bounding Box của Pad hàn
    /// </summary>
    /// <param name="padTop">Bounding Box của Pad trên</param>
    /// <param name="padBottom">Bounding Box của Pad dưới</param>
    /// <param name="offsetX">Khoảng dịch sang trái (sang phía con tụ)</param>
    /// <param name="offsetY">Khoảng dịch tinh chỉnh lên/xuống</param>
    /// <param name="roiWidth">Chiều rộng khung số 4</param>
    /// <param name="roiHeight">Chiều cao khung số 4</param>
    public static Rect CreateComponentRoi(
        Mat img,
        Rect padTop,
        Rect padBottom,
        int offsetX,
        int offsetY,
        int roiWidth,
        int roiHeight,
        Mat debugDrawMat = null,
        string nameImg = null)
    {
        offsetX = -245;
        offsetY = 45;
        roiHeight = 200;
        roiWidth = 120;
        // -------------------------------------------------------------
        // BƯỚC 1: Xác định mép trái của 2 Pad và tạo đường Line 1
        // -------------------------------------------------------------
        Point2d p1_top = new Point2d(padTop.X, padTop.Y + padTop.Height / 2.0);
        VeToaDoDiem(img, p1_top, "p1_top", Scalar.Red);
        Point2d p1_bot = new Point2d(padBottom.X, padBottom.Y + padBottom.Height / 2.0);
        VeToaDoDiem(img, p1_bot, "p1_bot", Scalar.Red);
        Dbg.Show(img, "a");



        // Vector chỉ phương trục dọc v = P_bot - P_top
        Point2d v_dir = new Point2d(p1_bot.X - p1_top.X, p1_bot.Y - p1_top.Y);
        double len = Math.Sqrt(v_dir.X * v_dir.X + v_dir.Y * v_dir.Y);
        VeToaDoDiem(img, v_dir, "v_dir", Scalar.Red);
        Point2d u_dir = new Point2d(v_dir.X / len, v_dir.Y / len); // Unit vector dọc
        VeToaDoDiem(img, u_dir, "u_dir", Scalar.Red);
        Dbg.Show(img, "a");
        // Vector pháp tuyến vuông góc hướng sang trái n = (u_y, -u_x)
        Point2d n_left = new Point2d(u_dir.Y, -u_dir.X);
        VeToaDoDiem(img, n_left, "n_left", Scalar.Red);
        Dbg.Show(img, "a");
        // -------------------------------------------------------------
        // BƯỚC 2: Tìm tâm khoảng hở giữa 2 Pad (Line 3 màu tím)
        // -------------------------------------------------------------
        Point2d p_edgeTop = new Point2d(padTop.X + padTop.Width / 2.0, padTop.Bottom);
        VeToaDoDiem(img, p_edgeTop, "p_edgeTop", Scalar.Red);
        Point2d p_edgeBot = new Point2d(padBottom.X + padBottom.Width / 2.0, padBottom.Top);
        VeToaDoDiem(img, p_edgeBot, "p_edgeBot", Scalar.Red);
        Dbg.Show(img, "a");
        // Trung điểm nằm giữa 2 Pad
        Point2d midPadCenter = new Point2d(
            (p_edgeTop.X + p_edgeBot.X) / 2.0,
            (p_edgeTop.Y + p_edgeBot.Y) / 2.0
        );
        VeToaDoDiem(img, midPadCenter, "midPadCenter", Scalar.Red);
        Dbg.Show(img, "a");
        // -------------------------------------------------------------
        // BƯỚC 3: Giao điểm tâm linh kiện và tạo Bounding Box số 4
        // -------------------------------------------------------------
        // Chiếu từ midPadCenter sang trái theo khoảng cách offsetX và bù trừ offsetY theo trục dọc
        //Point2d chipCenter = new Point2d(
        //    midPadCenter.X + offsetX + (u_dir.X * offsetY),
        //    midPadCenter.Y - (u_dir.Y * offsetY)
        //);
        Point2d chipCenter = new Point2d(
            midPadCenter.X + (n_left.X * offsetX) + (u_dir.X * offsetY),
            midPadCenter.Y + (n_left.Y * offsetX) + (u_dir.Y * offsetY)
        );
        VeToaDoDiem(img, chipCenter, "chipCenter", Scalar.Red);
        Dbg.Show(img, "a");
        // Khung chữ nhật số 4
        int roiX = (int)Math.Round(chipCenter.X - roiWidth / 2.0);
        int roiY = (int)Math.Round(chipCenter.Y - roiHeight / 2.0);
        //roiX = (int)chipCenter.X;
        //roiY = (int)chipCenter.Y;
        Point rectRoiXY = new(roiX, roiY);
        VeToaDoDiem(img, rectRoiXY, "ROIXY_RECT", Scalar.Red);
        Dbg.Show(img, "a");
        //Rect componentRoi = new Rect(roiX, roiY, roiWidth, roiHeight);
        Rect componentRoi = new Rect(roiX, roiY, roiWidth, roiHeight);
        Cv2.Rectangle(img, componentRoi, new Scalar(0, 0, 0), 2);
        Dbg.Show(img, "a");
        LuuAnhG2(img, _outDirG2, $"{nameImg}");
        // -------------------------------------------------------------
        // VẼ MINH HỌA LÊN ẢNH DEBUG (NẾU CÓ TRUYỀN MAT VÀO)
        // -------------------------------------------------------------
        //if (debugDrawMat != null)
        //{
        //    // Vẽ Line 1 (đường đỏ qua mép trái 2 pad)
        //    Cv2.Line(debugDrawMat, (Point)p1_top, (Point)p1_bot, new Scalar(0, 0, 255), 2);
        //    Dbg.Show(debugDrawMat, "a");
        //    // Vẽ Line 2 (đường đỏ tịnh tiến qua tâm tụ)
        //    Point2d line2_p1 = new Point2d(p1_top.X - offsetX, p1_top.Y - 50);
        //    Point2d line2_p2 = new Point2d(p1_bot.X - offsetX, p1_bot.Y + 50);
        //    Cv2.Line(debugDrawMat, (Point)line2_p1, (Point)line2_p2, new Scalar(0, 0, 255), 2);
        //    Dbg.Show(debugDrawMat, "a");
        //    // Vẽ Line 3 (đường tím ngang vuông góc)
        //    Cv2.Line(debugDrawMat, (Point)midPadCenter, (Point)chipCenter, new Scalar(255, 0, 255), 2);
        //    Dbg.Show(debugDrawMat, "a");
        //    // Vẽ Khung chữ nhật số 4 (Màu đen/xanh lá đậm)
        //    Cv2.Rectangle(debugDrawMat, componentRoi, new Scalar(0, 0, 0), 2);
        //    Dbg.Show(debugDrawMat, "a");
        //    // Vẽ các chấm điểm neo
        //    Cv2.Circle(debugDrawMat, (Point)midPadCenter, 4, new Scalar(255, 0, 255), -1);
        //    Dbg.Show(debugDrawMat, "a");
        //    Cv2.Circle(debugDrawMat, (Point)chipCenter, 4, new Scalar(0, 255, 0), -1);
        //    Dbg.Show(debugDrawMat, "a");
        //    Dbg.Show(debugDrawMat, "a");
        //}

        return componentRoi;
    }



    private static bool DungHeTrucVaDuongBao(Mat img, List<Point2d> points, AiKetQua kqMLCC, double nguongLechChoPhep = 30)
    {
        if (points == null || points.Count < 3 || kqMLCC == null) return false;

        // 1. XÁC ĐỊNH HƯỚNG TỤ DỰA VÀO CẠNH DÀI CỦA BOX AI (ANCHOR)
        Rect box = kqMLCC.KhungInt;
        bool isVertical = box.Height >= box.Width; // True: Tụ Dọc, False: Tụ Ngang

        Point2d dirMain, dirSub; // dirMain: Trục chính theo cạnh dài, dirSub: Trục phụ vuông góc
        if (isVertical)
        {
            dirMain = new Point2d(0, 1);  // Dọc xuống
            dirSub = new Point2d(1, 0);  // Ngang sang phải
        }
        else
        {
            dirMain = new Point2d(1, 0);  // Ngang sang phải
            dirSub = new Point2d(0, -1); // Dọc lên
        }

        if (Dbg.Enabled)
        {
            using Mat dbg1 = img.Clone();
            Cv2.Rectangle(dbg1, box, Scalar.Lime, 1);
            for (int i = 0; i < points.Count; i++)
            {
                Cv2.Circle(dbg1, (Point)points[i], 4, Scalar.Yellow, -1);
                Cv2.PutText(dbg1, $"P{i}", new Point((int)points[i].X + 5, (int)points[i].Y - 5),
                            HersheyFonts.HersheySimplex, 0.4, Scalar.Yellow, 1);
            }
            Dbg.Show(dbg1, $"01_InputPoints_Huong_{(isVertical ? "DOC" : "NGANG")}");
        }

        // 2. CHIẾU CÁC ĐIỂM LÊN TRỤC CHÍNH ĐỂ TÌM 2 ĐIỂM BIÊN ĐẦU - CUỐI (P_MAIN_1 & P_MAIN_2)
        // Sắp xếp các điểm theo hình chiếu dọc theo trục chính
        var sortedByMain = points.OrderBy(p => p.X * dirMain.X + p.Y * dirMain.Y).ToList();

        Point2d pMain1 = sortedByMain.First(); // Điểm đầu
        Point2d pMain2 = sortedByMain.Last();  // Điểm cuối

        if(Dbg.Enabled)
        {
            using Mat test = img.Clone();
            VeToaDoDiem(test, pMain1, "pMain1", Scalar.Red);
            Dbg.Show(test, "test");

            VeToaDoDiem(test, pMain2, "pMain1", Scalar.Red);
            Dbg.Show(test, "test");

        }    

        // Tinh chỉnh vector trục chính thực tế từ 2 điểm biên bắt được
        double dMx = pMain2.X - pMain1.X;
        double dMy = pMain2.Y - pMain1.Y;
        double lenMain = Math.Sqrt(dMx * dMx + dMy * dMy);
        if (lenMain > 1e-5)
        {
            dirMain = new Point2d(dMx / lenMain, dMy / lenMain);
            dirSub = new Point2d(-dirMain.Y, dirMain.X); // Trực giao 90 độ
        }

        // Tâm đối xứng thực tế xác định từ trung điểm của 2 điểm biên trục chính
        Point2d center = new Point2d((pMain1.X + pMain2.X) / 2.0, (pMain1.Y + pMain2.Y) / 2.0);
        double halfLenMain = lenMain / 2.0;



        // [DEBUG BƯỚC 2]: Xem 2 điểm trục chính pMain1, pMain2, trục nối và Tâm Center
        if (Dbg.Enabled)
        {
            using Mat dbg2 = img.Clone();
            // Nối đường trục chính giữa 2 điểm cực
            Cv2.Line(dbg2, (Point)pMain1, (Point)pMain2, Scalar.Red, 2, LineTypes.AntiAlias);

            // Đánh dấu pMain1 (Đỏ) và pMain2 (Cam)
            Cv2.Circle(dbg2, (Point)pMain1, 5, Scalar.Red, -1);
            Cv2.PutText(dbg2, "pMain1", new Point((int)pMain1.X + 6, (int)pMain1.Y), HersheyFonts.HersheySimplex, 0.45, Scalar.Red, 1);

            Cv2.Circle(dbg2, (Point)pMain2, 5, Scalar.Orange, -1);
            Cv2.PutText(dbg2, "pMain2", new Point((int)pMain2.X + 6, (int)pMain2.Y), HersheyFonts.HersheySimplex, 0.45, Scalar.Orange, 1);

            // Vẽ tâm đối xứng (Crosshair Xanh Cyan)
            Cv2.DrawMarker(dbg2, (Point)center, Scalar.Cyan, MarkerTypes.Cross, 18, 2);
            Cv2.PutText(dbg2, $"Center({center.X:0.0},{center.Y:0.0})", new Point((int)center.X + 8, (int)center.Y + 4),
                        HersheyFonts.HersheySimplex, 0.4, Scalar.Cyan, 1);

            Dbg.Show(dbg2, "02_TrucChinh_Va_TamCenter");
        }

        // 3. TÍNH ĐỘ RỘNG TRỤC PHỤ (HALF WIDTH) TỪ CÁC ĐIỂM HÔNG (SIDE POINTS)
        // Lọc ra các điểm không phải là 2 điểm biên trên trục chính
        var sidePoints = points.Where(p => p != pMain1 && p != pMain2).ToList();

        double halfLenSub = 0;
        if (sidePoints.Count > 0)
        {
            // Tính khoảng cách trung bình từ các điểm hông tới trục chính
            double totalSubDist = 0;
            foreach (var sp in sidePoints)
            {
                double vx = sp.X - center.X;
                double vy = sp.Y - center.Y;
                double distSub = Math.Abs(vx * dirSub.X + vy * dirSub.Y);
                totalSubDist += distSub;
            }
            halfLenSub = totalSubDist / sidePoints.Count;
        }
        else
        {
            // Fallback theo tỉ lệ kích thước MLCC nếu bị mất toàn bộ điểm hông
            halfLenSub = isVertical ? (box.Width / 2.0) : (box.Height / 2.0);
        }

        // 4. DỰNG 4 GIAO ĐIỂM GÓC KHUNG TỪ TÂM VÀ 2 VECTOR TRỤC ĐỐI XỨNG
        Point2d gTopLeft = new Point2d(center.X - dirSub.X * halfLenSub - dirMain.X * halfLenMain,
                                           center.Y - dirSub.Y * halfLenSub - dirMain.Y * halfLenMain);
        Point2d gTopRight = new Point2d(center.X + dirSub.X * halfLenSub - dirMain.X * halfLenMain,
                                           center.Y + dirSub.Y * halfLenSub - dirMain.Y * halfLenMain);
        Point2d gBottomRight = new Point2d(center.X + dirSub.X * halfLenSub + dirMain.X * halfLenMain,
                                           center.Y + dirSub.Y * halfLenSub + dirMain.Y * halfLenMain);
        Point2d gBottomLeft = new Point2d(center.X - dirSub.X * halfLenSub + dirMain.X * halfLenMain,
                                           center.Y - dirSub.Y * halfLenSub + dirMain.Y * halfLenMain);

        // 5. KIỂM TRA ĐỘ LỆCH TÂM & RÀNG BUỘC BOUNDING BOX VỚI MLCC
        Point2f[] polygonKhung = new Point2f[]
        {
        new Point2f((float)gTopLeft.X, (float)gTopLeft.Y),
        new Point2f((float)gTopRight.X, (float)gTopRight.Y),
        new Point2f((float)gBottomRight.X, (float)gBottomRight.Y),
        new Point2f((float)gBottomLeft.X, (float)gBottomLeft.Y)
        };

        Point2f tamMLCC = new Point2f(kqMLCC.Tam.X, kqMLCC.Tam.Y);
        Point2f[] bonGocMLCC = new Point2f[]
        {
        new Point2f(box.Left, box.Top),
        new Point2f(box.Right, box.Top),
        new Point2f(box.Right, box.Bottom),
        new Point2f(box.Left, box.Bottom)
        };

        // Chống tràn viền: Cả 4 góc box MLCC phải nằm gọn trong polygon khung
        bool isInside = bonGocMLCC.All(pt => Cv2.PointPolygonTest(polygonKhung, pt, measureDist: false) >= 0);

        double deltaX = tamMLCC.X - center.X;
        double deltaY = tamMLCC.Y - center.Y;
        double doLechTam = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);

        bool isPassViTri = isInside && (doLechTam <= nguongLechChoPhep);

        Dbg.Info($"[SO SÁNH MLCC] Hướng: {(isVertical ? "DỌC" : "NGANG")} | Tâm Dựng: ({center.X:0.1}, {center.Y:0.1}) | Tâm AI: ({tamMLCC.X:0.1}, {tamMLCC.Y:0.1})");
        Dbg.Info($"[SO SÁNH MLCC] Độ lệch = {doLechTam:0.2} px -> {(isPassViTri ? "PASS" : "NG - LỆCH VỊ TRÍ")}");

        // 6. DEBUG RENDER CHUẨN XÁC
        double lineSpan = Math.Max(img.Width, img.Height);
        void VeLine(Point2d pt, Point2d dir, Scalar color)
        {
            Point p1 = new Point((int)(pt.X - dir.X * lineSpan), (int)(pt.Y - dir.Y * lineSpan));
            Point p2 = new Point((int)(pt.X + dir.X * lineSpan), (int)(pt.Y + dir.Y * lineSpan));
            Cv2.Line(img, p1, p2, color, 1, LineTypes.AntiAlias);
        }

        // 2 đường biên trục chính (Đỏ) đi qua 2 điểm pMain1 và pMain2
        VeLine(pMain1, dirSub, Scalar.Red);
        Dbg.Show(img, "bien truc do 1");
        VeLine(pMain2, dirSub, Scalar.Red);
        Dbg.Show(img, "bien truc do 2");

        // 2 đường biên hông (Xanh lá) cách tâm đều 2 phía halfLenSub
        Point2d pSide1 = new Point2d(center.X - dirSub.X * halfLenSub, center.Y - dirSub.Y * halfLenSub);
        Point2d pSide2 = new Point2d(center.X + dirSub.X * halfLenSub, center.Y + dirSub.Y * halfLenSub);
        VeLine(pSide1, dirMain, Scalar.Lime);
        Dbg.Show(img, "bien hong xanh 1");
        VeLine(pSide2, dirMain, Scalar.Lime);
        Dbg.Show(img, "bien hong xanh 2");

        // Khung Vàng nối 4 giao điểm
        Point[] polyPoints = polygonKhung.Select(p => new Point((int)p.X, (int)p.Y)).ToArray();
        Cv2.Polylines(img, new Point[][] { polyPoints }, isClosed: true, color: Scalar.Yellow, thickness: 1, lineType: LineTypes.AntiAlias);

        // Vẽ các chấm góc
        Cv2.Circle(img, (Point)gTopLeft, 4, Scalar.Yellow, -1);
        Cv2.Circle(img, (Point)gTopRight, 4, Scalar.Yellow, -1);
        Cv2.Circle(img, (Point)gBottomLeft, 4, Scalar.Yellow, -1);
        Cv2.Circle(img, (Point)gBottomRight, 4, Scalar.Yellow, -1);

        // Vẽ tâm Crosshair Cyan
        Cv2.DrawMarker(img, (Point)center, Scalar.Cyan, MarkerTypes.Cross, 16, 2);
        Dbg.Show(img, "tam Crosshair");
        return isPassViTri;
    }
    /// <summary>
    /// Vẽ chấm tròn và chuỗi tọa độ (X, Y) kèm nền đen chống lóa
    /// </summary>
    private static void VeToaDoDiem(Mat img, Point2d pt, string tenDiem, Scalar mauSac)
    {
        // 1. Vẽ tâm điểm (Chấm tròn + Crosshair nhỏ)
        Cv2.Circle(img, (Point)pt, 3, mauSac, -1);
        Cv2.DrawMarker(img, (Point)pt, mauSac, MarkerTypes.Cross, 10, 1);

        // 2. Chuẩn bị chuỗi hiển thị: "P1 (120.5, 340.2)"
        string text = string.IsNullOrEmpty(tenDiem)
            ? $"({pt.X:0.1}, {pt.Y:0.1})"
            : $"{tenDiem}: ({pt.X:0.1}, {pt.Y:0.1})";

        var font = HersheyFonts.HersheySimplex;
        double scale = 0.4;
        int thickness = 1;

        // 3. Tính toán kích thước chữ để vẽ nền đen chống chìm
        var textSize = Cv2.GetTextSize(text, font, scale, thickness, out int baseline);
        Point textPos = new Point((int)pt.X + 6, (int)pt.Y - 6);

        // Tránh chữ bị tràn khỏi góc trên ảnh
        if (textPos.Y - textSize.Height < 0)
            textPos.Y = (int)pt.Y + textSize.Height + 10;
        if (textPos.X + textSize.Width > img.Width)
            textPos.X = (int)pt.X - textSize.Width - 6;

        Rect bgRect = new Rect(textPos.X - 2, textPos.Y - textSize.Height - 2, textSize.Width + 4, textSize.Height + baseline + 4);

        // Vẽ hộp nền đen mờ
        Cv2.Rectangle(img, bgRect, Scalar.Black, -1);

        // 4. Vẽ chữ lên trên nền
        Cv2.PutText(img, text, textPos, font, scale, mauSac, thickness, LineTypes.AntiAlias);
    }
    /// <summary>
    /// Tìm cặp điểm xa nhất (Trục dài) và dựng các đường thẳng trực giao.
    /// </summary>
    private static bool DungHeTrucVaDuongBao_(Mat img, List<Point2d> points, AiKetQua kqMLCC, double nguongLechChoPhep = 10.0)
    {
        if (points.Count < 4) return false;
        bool isPassViTri = false;
        // 1. Tìm 2 điểm xa nhất (Cặp Trên - Dưới)
        double maxDistSq = -1;
        int idxR1 = 0, idxR2 = 1;

        for (int i = 0; i < points.Count; i++)
        {
            for (int j = i + 1; j < points.Count; j++)
            {
                double distSq = Math.Pow(points[i].X - points[j].X, 2) + Math.Pow(points[i].Y - points[j].Y, 2);
                if (distSq > maxDistSq)
                {
                    maxDistSq = distSq;
                    idxR1 = i;
                    idxR2 = j;
                }
            }
        }

        Point2d pTop = points[idxR1];
        Point2d pBottom = points[idxR2];

        // Sắp xếp lại pTop luôn có Y nhỏ hơn pBottom
        if (pTop.Y > pBottom.Y)
        {
            (pTop, pBottom) = (pBottom, pTop);
        }

        // 2 điểm còn lại bên hông (pointsSide)
        var pointsSide = points.Where((_, idx) => idx != idxR1 && idx != idxR2)
                               .OrderBy(p => p.Y)
                               .ToList();

        if (pointsSide.Count < 2) return false;

        Point2d pSideTop = pointsSide[0];     // Điểm mép trên bên trái
        Point2d pSideBottom = pointsSide[1];  // Điểm mép dưới bên trái

        // 2. Vector hướng chuẩn
        double dx = pBottom.X - pTop.X;
        double dy = pBottom.Y - pTop.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-5) return false;

        Point2d dirDoc = new Point2d(dx / len, dy / len);
        Point2d dirNgang = new Point2d(-dirDoc.Y, dirDoc.X);

        // Điểm mép trái và mép phải (Đối xứng qua trục tâm)
        Point2d pLeft = pSideTop;
        double vecX = pLeft.X - pTop.X;
        double vecY = pLeft.Y - pTop.Y;
        double offsetKhoangCach = Math.Abs(vecX * dirNgang.X + vecY * dirNgang.Y);

        Point2d pRight = new Point2d(pTop.X - dirNgang.X * offsetKhoangCach,
                                     pTop.Y - dirNgang.Y * offsetKhoangCach);

        // 3. TÍNH 4 GIAO ĐIỂM
        Point2d? gTL = TimGiaoDiem(pSideTop, dirNgang, pLeft, dirDoc);
        Point2d? gTR = TimGiaoDiem(pSideTop, dirNgang, pRight, dirDoc);
        Point2d? gBL = TimGiaoDiem(pSideBottom, dirNgang, pLeft, dirDoc);
        Point2d? gBR = TimGiaoDiem(pSideBottom, dirNgang, pRight, dirDoc);

        // Chặn lỗi: Nếu bất kỳ cặp đường nào song song không cắt nhau
        if (!gTL.HasValue || !gTR.HasValue || !gBL.HasValue || !gBR.HasValue)
            return false;

        // Lấy giá trị thực tế qua .Value
        Point2d gTopLeft = gTL.Value;
        Point2d gTopRight = gTR.Value;
        Point2d gBottomLeft = gBL.Value;
        Point2d gBottomRight = gBR.Value;

        // Tâm lý thuyết tạo bởi 4 giao điểm
        Point2d tamLyThuyet = new Point2d(
            (gTopLeft.X + gTopRight.X + gBottomLeft.X + gBottomRight.X) / 4.0,
            (gTopLeft.Y + gTopRight.Y + gBottomLeft.Y + gBottomRight.Y) / 4.0
        );

        // 4. SO SÁNH VỚI TỌA ĐỘ MLCC DO AI TRẢ VỀ
        Point2f[] polygonKhung = new Point2f[]
        {
            new Point2f((float)gTopLeft.X, (float)gTopLeft.Y),
            new Point2f((float)gTopRight.X, (float)gTopRight.Y),
            new Point2f((float)gBottomRight.X, (float)gBottomRight.Y),
            new Point2f((float)gBottomLeft.X, (float)gBottomLeft.Y)
        };
        if (kqMLCC != null)
        {
            Point2f tamMLCC = new Point2f(kqMLCC.Tam.X, kqMLCC.Tam.Y);

            // measureDist: false -> Trả về: +1 (Nằm trong), 0 (Nằm trên cạnh), -1 (NẰM NGOÀI)
            double checkTam = Cv2.PointPolygonTest(polygonKhung, tamMLCC, measureDist: false);
            bool tamNamTrongKhung = checkTam >= 0;

            // B. Kiểm tra TOÀN BỘ 4 GÓC của box MLCC có nằm trong khung không (Chống tràn viền)
            Rect box = kqMLCC.KhungInt;
            Point2f[] bonGocMLCC = new Point2f[]
            {
                new Point2f(box.Left, box.Top),
                new Point2f(box.Right, box.Top),
                new Point2f(box.Right, box.Bottom),
                new Point2f(box.Left, box.Bottom)
            };

            //bool toanBoNamTrong = bonGocMLCC.All(pt => Cv2.PointPolygonTest(polygonKhung, pt, measureDist: false) >= 0);

            // Tính khoảng cách lệch tâm Euclidean
            double deltaX = tamMLCC.X - tamLyThuyet.X;
            double deltaY = tamMLCC.Y - tamLyThuyet.Y;
            double doLechTam = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);

            isPassViTri = bonGocMLCC.All(pt => Cv2.PointPolygonTest(polygonKhung, pt, measureDist: false) >= 0);

            Dbg.Info($"[SO SÁNH MLCC] Tâm Dựng: ({tamLyThuyet.X:0.1}, {tamLyThuyet.Y:0.1}) | Tâm AI: ({tamMLCC.X:0.1}, {tamMLCC.Y:0.1})");
            Dbg.Info($"[SO SÁNH MLCC] Độ lệch = {doLechTam:0.2} px -> {(isPassViTri ? "PASS" : "NG - LỆCH VỊ TRÍ")}");
        }

        // 5. VẼ DEBUG
        double lineSpan = Math.Max(img.Width, img.Height);
        void VeLine(Point2d center, Point2d dir, Scalar color)
        {
            Point p1 = new Point((int)(center.X - dir.X * lineSpan), (int)(center.Y - dir.Y * lineSpan));
            Point p2 = new Point((int)(center.X + dir.X * lineSpan), (int)(center.Y + dir.Y * lineSpan));
            Cv2.Line(img, p1, p2, color, 2, LineTypes.AntiAlias);
        }

        // Vẽ 2 đường đỏ ngang
        VeLine(pSideTop, dirNgang, Scalar.Red);
        Dbg.Show(img, "pSideTop");
        VeLine(pSideBottom, dirNgang, Scalar.Red);
        Dbg.Show(img, "pSideBottom");
        // Vẽ 2 đường xanh dọc
        VeLine(pLeft, dirDoc, Scalar.Lime);
        Dbg.Show(img, "pLeft");
        VeLine(pRight, dirDoc, Scalar.Lime);
        Dbg.Show(img, "pRight");


        // Đánh dấu 4 giao điểm (Chấm Vàng)
        Cv2.Circle(img, (Point)gTopLeft, 4, Scalar.Yellow, -1);
        Dbg.Show(img, "cri1");
        Cv2.Circle(img, (Point)gTopRight, 4, Scalar.Yellow, -1);
        Cv2.Circle(img, (Point)gBottomLeft, 4, Scalar.Yellow, -1);
        Cv2.Circle(img, (Point)gBottomRight, 4, Scalar.Yellow, -1);

        // Đánh dấu tâm lý thuyết (Crosshair Xanh Dương)
        Cv2.DrawMarker(img, (Point)tamLyThuyet, Scalar.Cyan, MarkerTypes.Cross, 20, 2);
        return isPassViTri;
    }

    private static void VeThongBaoNG(Mat img, string Result, Scalar color)
    {
        // 1. Chọn font nét đôi/phức hợp, tránh Italic đơn nét
        var font = HersheyFonts.HersheyComplex;

        // 2. Tinh chỉnh Scale (cỡ) và Thickness (độ đậm)
        double fontScale = 3.5; // Tăng cỡ chữ lên (mặc định cũ của ông là 1.5)
        int thickness = 5;      // Tăng độ dày nét vẽ (mặc định cũ là 2)

        // 3. Tính toán kích thước bounding box chuẩn theo scale & thickness mới
        var size = Cv2.GetTextSize(Result, font, fontScale, thickness, out _);

        // 4. Vẽ với cờ AntiAlias để chữ mượt, không vỡ hạt
        Cv2.PutText(
            img,
            Result,
            new Point(img.Width - size.Width - 15, size.Height + 15),
            font,
            fontScale,
            color,
            thickness,
            LineTypes.AntiAlias
        );
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
