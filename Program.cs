using OpenCvSharp;
using OpenCvSharp.XImgProc;
using OpenCvSharp.XPhoto;
using System.Net.WebSockets;
using System.Runtime.Intrinsics.X86;

namespace A38.ImageCrop;

/// <summary>
/// Tham số thuật toán — sửa ở đây rồi chạy lại là thấy khác ngay.
/// Để static (không phải const) để bạn còn sửa được ngay trong lúc debug qua Immediate Window.
/// </summary>
public static class Config
{
    /// <summary>Thu nhỏ ảnh về chiều rộng này để DÒ vùng cho nhanh (ảnh cắt vẫn ở độ phân giải gốc).</summary>
    public static int WorkWidth = 1000;

    /// <summary>Cỡ nhân làm mờ, phải là số lẻ. Càng lớn càng bớt nhiễu nhưng mất cạnh nhỏ.</summary>
    public static int BlurKernel = 5;

    /// <summary>Ngưỡng Canny. Cạnh mờ không bắt được thì giảm CannyLow xuống.</summary>
    public static int CannyLow = 50;
    public static int CannyHigh = 150;

    /// <summary>Cỡ nhân morphology để nối các cạnh bị đứt. 0 = bỏ qua bước này.</summary>
    public static int MorphKernel = 5;

    /// <summary>Vùng nhỏ hơn tỉ lệ này so với cả ảnh thì bỏ (lọc nhiễu).</summary>
    public static double MinAreaRatio = 0.02;

    /// <summary>Độ "làm thẳng" đường viền khi xấp xỉ đa giác. Tăng lên nếu viền răng cưa.</summary>
    public static double ApproxEpsRatio = 0.02;

    /// <summary>true = nếu vùng là tứ giác thì nắn phẳng (warp); false = luôn cắt theo hình chữ nhật bao.</summary>
    public static bool WarpIfQuad = true;

    /// <summary>
    /// Đường dẫn ảnh đầu vào — sửa trực tiếp ở đây rồi bấm Run (tiện khi chạy trong Visual Studio,
    /// không cần truyền tham số dòng lệnh). Để trống ("") thì chương trình sẽ dùng tham số dòng lệnh
    /// (dotnet run -- "duong_dan.jpg"), hoặc tự tạo sample.png nếu không có gì cả.
    /// </summary>
    public static string InputImagePath = "D:\\Images_\\V2\\CoilAssy\\CoilAssy\\1240S\\opencv\\Image__2026-07-28__09-35-18.bmp";
}

/// <summary>
/// Tham số riêng cho bước TÌM PHẦN NHÔ RA (top-hat).
/// Tách riêng khỏi Config để bạn biết chỗ nào chỉnh cho việc gì.
/// </summary>
public static class ProtrusionCfg
{
    // ===== Đèn báo mask có ổn không =====
    // Đo bằng: (số pixel trắng của mask) / (tổng số pixel ROI).
    // ROI góc trên-phải của con hàng này vật thể chiếm khoảng 30-45%.
    // In ra 0.5% hay 99% nghĩa là mask hỏng -> đừng debug tiếp các bước sau, sửa mask trước.
    public static double MinObjectRatio = 0.05;
    public static double MaxObjectRatio = 0.97;

    // ===== THAM SỐ QUAN TRỌNG NHẤT =====
    // Bán kính (pixel) của cái đĩa dùng để "lăn" bên trong mask.
    // Quy tắc: r phải LỚN HƠN nửa bề rộng phần nhô ra,
    //          nhưng NHỎ HƠN nửa bề rộng chỗ hẹp nhất của thân vật thể.
    //   - r quá nhỏ  -> đĩa lọt vào cả phần nhô -> opening giữ nguyên nó -> residual rỗng -> MISS
    //   - r quá lớn  -> đĩa không lọt vào cả thân -> thân cũng bị bào -> residual đầy rác
    // Cụm nhô ở ROI này đo được cỡ 150x170 px, nên r=40 (giá trị trong test.cs) là QUÁ NHỎ.
    // Chạy Program.SweepDiskRadius() để quét và tự tìm số đúng.
    public static int DiskRadius = 80;

    // Opening với đĩa lớn rất chậm (độ phức tạp tăng theo r^2).
    // Thu nhỏ ảnh xuống rồi mới opening -> nhanh hơn nhiều, sai số vài pixel không đáng kể.
    // Đặt 1.0 để tắt tối ưu này (chạy ở độ phân giải gốc, chính xác nhất nhưng chậm).
    public static double OpenDownScale = 0.5;

    // ===== Bộ lọc ứng viên =====
    // Tính theo tỉ lệ diện tích ROI, KHÔNG hardcode số pixel,
    // để đổi kích thước ROI hay đổi độ phân giải camera vẫn dùng lại được.
    public static double MinAreaRatio = 0.0005;  // nhỏ hơn -> coi là nhiễu
    public static double MaxAreaRatio = 0.20;    // lớn hơn -> chắc chắn đã ăn nhầm cả thân

    // Tỉ lệ cạnh dài / cạnh ngắn của minAreaRect.
    // Phần nhô ra thường thuôn dài; hình gần vuông (aspect ~1) thường là mảng bị bào nhầm.
    public static double MinAspect = 1.1;
    public static double MaxAspect = 10.0;

    // Ứng viên phải DÍNH vào thân vật thể, không được là mảnh trôi nổi.
    // Giãn ứng viên ra bấy nhiêu pixel rồi xem có chạm thân không.
    public static int TouchDilate = 5;

    // Bỏ ứng viên nằm sát mép ROI: đó thường là chỗ vật thể bị khung ROI cắt cụt,
    // không phải phần nhô thật. Đây cũng là cái cứu ta khi r lớn làm chóp nhọn của thân lòi ra.
    public static int BorderMargin = 3;

    // Nới rộng bbox trả về, để lát nữa cắt ảnh còn thấy được ngữ cảnh xung quanh.
    public static int BboxPadding = 20;
}

public static class Program
{
    public static int Main(string[] args)
    {
        // ---- Cấu hình debug ---------------------------------------------------
        Dbg.Enabled = !args.Contains("--no-debug");
        Dbg.ShowWindow = !args.Contains("--no-window");
        Dbg.Pause = !args.Contains("--no-pause");
        Dbg.SaveFile = true;

        // Chỉ muốn xem 1 bước? Bỏ comment dòng dưới (hoặc chạy: --only canny)
        // Dbg.Filter = "canny";
        int i = Array.IndexOf(args, "--only");
        if (i >= 0 && i + 1 < args.Length) Dbg.Filter = args[i + 1];

        // ---- Ảnh đầu vào ------------------------------------------------------
        // Ưu tiên Config.InputImagePath (sửa trong code), rồi tới tham số dòng lệnh,
        // cuối cùng mới tự tạo ảnh mẫu.
        var inputPath = !string.IsNullOrWhiteSpace(Config.InputImagePath)
                        ? Config.InputImagePath
                        : args.FirstOrDefault(a => !a.StartsWith("--"))
                          ?? EnsureSampleImage();

        if (!File.Exists(inputPath))
        {
            Console.WriteLine($"Không tìm thấy ảnh: {inputPath}");
            return 1;
        }

        using var src = Cv2.ImRead(inputPath, ImreadModes.Color);
        if (src.Empty())
        {
            Console.WriteLine($"Đọc ảnh thất bại (file hỏng hoặc không phải ảnh): {inputPath}");
            return 1;
        }

        Console.WriteLine($"Ảnh vào: {inputPath}  ({src.Width}x{src.Height})");
        Dbg.Reset();

        // ---- Chạy pipeline ----------------------------------------------------
        // Giai đoạn 1: dò và cắt vùng lớn nhất (cả con hàng) ra khỏi ảnh gốc.
        using var result = CropLargestRegion(src);

        // >>> SỬA LỖI: câu check null này PHẢI đứng TRƯỚC lời gọi CropSmallRegion.
        // Code cũ gọi CropSmallRegion(result) trong khi result vẫn có thể là null,
        // rồi mới check ở dưới -> chương trình chết vì NullReferenceException
        // ngay tại dòng "int w = src.Width;" bên trong CropSmallRegion.
        // Nguyên tắc: kiểm tra null NGAY SAU khi nhận giá trị, trước mọi lần dùng nó.
        if (result is null || result.Empty())
        {
            Console.WriteLine("KHÔNG dò được vùng nào. Thử giảm Config.CannyLow hoặc Config.MinAreaRatio.");
            Dbg.CloseAll();
            return 2;
        }

        // Giai đoạn 2: từ ảnh đã cắt, soi các góc để tìm phần nhô ra.
        // Trả về null nghĩa là không tìm thấy — không phải lỗi chương trình,
        // nên chỉ in cảnh báo rồi vẫn ghi kết quả giai đoạn 1 ra file.
        using var imgPhanNho = CropSmallRegion(result);
        if (imgPhanNho is null)
            Console.WriteLine("Chưa bắt được phần nhô ra. Xem dòng log 'ratio mask' và ảnh 5_Residual để biết kẹt ở đâu.");
        else
            Console.WriteLine($"Đã bắt được phần nhô ra: {imgPhanNho.Width}x{imgPhanNho.Height}");

        Directory.CreateDirectory("output");
        var outPath = Path.Combine("output",
            Path.GetFileNameWithoutExtension(inputPath) + "_crop.png");
        Cv2.ImWrite(outPath, result);

        Console.WriteLine($"Đã cắt xong: {outPath}  ({result.Width}x{result.Height})");
        Console.WriteLine($"Ảnh từng bước nằm trong: {Path.GetFullPath(Dbg.OutDir)}");

        Dbg.CloseAll();
        return 0;
    }

    /// <summary>
    /// Pipeline chính: dò vùng lớn nhất trong ảnh rồi cắt ra.
    /// Mỗi bước đều có Dbg.Show để bạn nhìn thấy ảnh biến đổi thế nào.
    /// </summary>
    private static Mat? CropLargestRegion(Mat src)
    {
        Dbg.Show(src, "original");

        // --- B1: thu nhỏ để dò cho nhanh. Toạ độ tìm được sẽ nhân ngược lại sau.
        double scale = Math.Min(1.0, (double)Config.WorkWidth / src.Width);
        using var work = new Mat();
        if (scale < 1.0)
            Cv2.Resize(src, work, new Size(), scale, scale, InterpolationFlags.Area);
        else
            src.CopyTo(work);
        Dbg.Log($"scale dò = {scale:0.###} -> làm việc trên {work.Width}x{work.Height}");

        // --- B2: chuyển xám. Mọi thuật toán dò cạnh đều cần ảnh 1 kênh.
        using var gray = new Mat();


        // Tý thử đổi về 0 hoặc các cái khác xem nó biến đổi ảnh như thế nào  ------------

        Cv2.CvtColor(work, gray, ColorConversionCodes.BGR2GRAY);
        //Dbg.Show(gray, "gray");

        // --- B3: làm mờ để bớt nhiễu, tránh Canny bắt phải hạt nhiễu.
        using var blur = new Mat();

        // Điều chỉnh BlurKernel và BlurKernel sang một số khác thì nó sẽ như nào 
        Cv2.GaussianBlur(gray, blur, new Size(Config.BlurKernel, Config.BlurKernel), 0);
        //Dbg.Show(blur, "blur");

        // --- B4: dò cạnh.
        using var edges = new Mat();
        // Điều chỉnh cạnh CannyLow và CannyHigh sang số kahcs thì nó như nào ảnh tìm được trả về kết quả như nào 
        Cv2.Canny(blur, edges, Config.CannyLow, Config.CannyHigh);
        //Dbg.Show(edges, "canny");
        // Mẹo: nonzero% ở dòng stats nói lên nhiều thứ —
        //   quá thấp (<0.5%) = ngưỡng cao quá, cạnh biến mất
        //   quá cao (>15%)   = ngưỡng thấp quá, toàn nhiễu

        // --- B5: nối các cạnh bị đứt để contour khép kín được.
        using var closed = new Mat();
        if (Config.MorphKernel > 0)
        {
            using var kernel = Cv2.GetStructuringElement(
                MorphShapes.Rect, new Size(Config.MorphKernel, Config.MorphKernel));
            Cv2.MorphologyEx(edges, closed, MorphTypes.Close, kernel);
            Dbg.Show(closed, "morph_close");
        }
        else
        {
            edges.CopyTo(closed);
        }

        // --- B6: tìm đường viền.
        Cv2.FindContours(closed, out Point[][] contours, out _,
            RetrievalModes.External, ContourApproximationModes.ApproxSimple);


        var imgContours = gray.Clone();
        Cv2.CvtColor(imgContours, imgContours, ColorConversionCodes.GRAY2BGR);
        for (int i = 0; i < contours.Length; i++)
        {
            // 1. Tạo màu HSV: H (0-179), S (255 - rực rỡ nhất), V (255 - sáng nhất)
            byte hue = (byte)(i * 179 / contours.Length);
            Mat hsvPixel = new Mat(1, 1, MatType.CV_8UC3, new Scalar(hue, 255, 255));
            Mat bgrPixel = new Mat();

            // 2. Convert HSV -> BGR để lấy Scalar chuẩn cho DrawContours
            Cv2.CvtColor(hsvPixel, bgrPixel, ColorConversionCodes.HSV2BGR);
            Vec3b bgr = bgrPixel.At<Vec3b>(0, 0);
            Scalar color = new Scalar(bgr.Item0, bgr.Item1, bgr.Item2);

            // 3. Vẽ contour
            Cv2.DrawContours(imgContours, contours, i, color, 2);

            // Giải phóng bộ nhớ Mat tạm
            hsvPixel.Dispose();
            bgrPixel.Dispose();
        }
        //Dbg.Show(imgContours, "imgContours");


        //Dbg.Log($"tim duoc {contours.Length} contour");

        double imageArea = work.Width * (double)work.Height;
        var candidates = contours
            .Select(c => (Contour: c, Area: Cv2.ContourArea(c)))
            .Where(x => x.Area >= imageArea * Config.MinAreaRatio)
            .OrderByDescending(x => x.Area)
            .ToList();

        Dbg.Log($"còn {candidates.Count} contour sau khi lọc diện tích " +
                $"(>= {Config.MinAreaRatio:P0} ảnh = {imageArea * Config.MinAreaRatio:0} px)");

        // >>> SỬA LỖI (loại nguy hiểm: vẫn COMPILE SẠCH, không một warning nào).
        // Code cũ:
        //     foreach (var (_, area) in candidates.Take(5))
        //         //Dbg.Log(...);              <- thân vòng lặp bị comment mất
        //
        //     if (candidates.Count == 0) return null;
        //
        // C# không bắt buộc { } cho thân vòng lặp, nên khi bạn comment mất dòng Dbg.Log,
        // trình biên dịch lấy luôn CÂU LỆNH KẾ TIẾP làm thân -> câu "if ... return null"
        // bị hút vào trong vòng lặp.
        // Hậu quả ngược đời: return null chỉ chạy khi candidates CÓ phần tử (lúc không cần),
        // còn khi candidates RỖNG thì vòng lặp không chạy vòng nào -> guard bị bỏ qua
        // -> xuống dòng candidates[0] bên dưới ném ArgumentOutOfRangeException.
        //
        // BÀI HỌC: luôn đặt { } kể cả với thân một dòng. Đây là lỗi kinh điển của C/C++/C#.
        if (candidates.Count == 0) return null;

        foreach (var (_, area) in candidates.Take(5))
        {
            Dbg.Log($"  - diện tích {area:0} px ({area / imageArea:P1} ảnh)");
        }

        // Vẽ đè các contour lên ảnh để nhìn xem nó bắt đúng chỗ chưa.
        if (Dbg.Enabled)
        {
            using var overlay = work.Clone();
            Cv2.DrawContours(overlay, candidates.Select(x => x.Contour).ToArray(), -1,
                new Scalar(0, 255, 0), 2);
            Cv2.DrawContours(overlay, new[] { candidates[0].Contour }, -1,
                new Scalar(0, 0, 255), 3);   // đỏ = vùng được chọn
           // Dbg.Show(overlay, "contours");
        }

        var best = candidates[0].Contour;

        // --- B7: xấp xỉ thành đa giác, xem có phải tứ giác không.
        double peri = Cv2.ArcLength(best, true);
        var approx = Cv2.ApproxPolyDP(best, Config.ApproxEpsRatio * peri, true);
        Dbg.Log($"xấp xỉ đa giác: {approx.Length} đỉnh (eps = {Config.ApproxEpsRatio * peri:0.#})");

        // --- B8: cắt. Toạ độ đang ở ảnh thu nhỏ nên phải chia lại cho scale.
        if (Config.WarpIfQuad && approx.Length == 4)
        {
            var corners = OrderCorners(approx.Select(p =>
                new Point2f((float)(p.X / scale), (float)(p.Y / scale))).ToArray());

            foreach (var c in corners) Dbg.Log($"  góc: ({c.X:0}, {c.Y:0})");

            var warped = WarpToRect(src, corners);
            //Dbg.Show(warped, "result_warp");
            return warped;
        }
        else
        {
            var r = Cv2.BoundingRect(best);
            int paddingFull = 20;
            // x+y nhỏ nhất là trên trái
            var full = new Rect(
                (int)(r.X / scale - paddingFull), (int)(r.Y / scale - paddingFull),
                (int)(r.Width / scale + 2*paddingFull) , (int)(r.Height / scale + 2*paddingFull))
                .Intersect(new Rect(0, 0, src.Width, src.Height));

            Dbg.Log($"Cat theo hinh chu nhat bao quanh: {full}");
            var cropped = new Mat(src, full).Clone();
            //Dbg.Show(cropped, "result_crop");
            return cropped;
        }
    }

    /// <summary>
    /// Từ ảnh đã cắt (cả con hàng), soi góc TRÊN-PHẢI để tìm phần nhô ra rồi cắt nó.
    /// Trả về null nếu không tìm thấy (KHÔNG phải lỗi — cứ đọc log để biết kẹt ở bước nào).
    ///
    /// MẠCH CHÍNH, nhớ đúng thứ tự này:
    ///   ảnh màu -> xám -> làm mờ -> nhị phân (Otsu) -> lấp lỗ -> giữ blob to nhất
    ///           -> opening bằng đĩa -> TRỪ đi -> lọc ứng viên -> ra bbox
    /// </summary>
    public static Mat? CropSmallRegion(Mat src)
    {
        Dbg.Show(src, "0_AnhDaCat");

        int w = src.Width;
        int h = src.Height;
        int roiW = 500;
        int roiH = 500;

        Point topRight = new(w - 1, 0);

        // ================================================================
        //  CẮT ROI GÓC TRÊN-PHẢI
        // ================================================================
        // >>> SỬA LỖI: phải CLAMP (giao với khung ảnh) trước khi cắt.
        // Toán tử & giữa hai Rect trong OpenCvSharp = phép giao (intersection).
        // Thiếu bước này, nếu ảnh cắt được nhỏ hơn 500px thì new Mat(src, rect)
        // ném exception ngay, vì rect thò ra ngoài biên ảnh.
        Rect areaTrenPhai = new Rect(topRight.X - roiW, topRight.Y + 50, roiW, roiH)
                            & new Rect(0, 0, src.Width, src.Height);

        if (areaTrenPhai.Width < 50 || areaTrenPhai.Height < 50)
        {
            Dbg.Log($"ROI tren-phai qua nho sau khi clamp: {areaTrenPhai}");
            return null;
        }

        // using = tự gọi Dispose khi ra khỏi hàm. Mat giữ bộ nhớ ngoài vùng quản lý của GC,
        // quên using là rò bộ nhớ. Ảnh 40MB mà chạy vòng lặp nhiều file là hết RAM ngay.
        using Mat imgTrenPhai = new(src, areaTrenPhai);
        Dbg.Show(imgTrenPhai, "1_ROI_raw");

        // ================================================================
        //  BƯỚC 1: chuyển xám
        // ================================================================
        // Mọi thuật toán nhị phân / morphology đều làm việc trên ảnh 1 kênh.
        using Mat gray = ToGray(imgTrenPhai);

        // ================================================================
        //  BƯỚC 2: làm mờ
        // ================================================================
        // Xoá hạt nhiễu để Otsu không bị mấy pixel lạc lõng kéo lệch ngưỡng.
        // Kernel phải là số LẺ. Càng lớn càng mịn nhưng biên càng bị "nhoè" ra.
        using Mat blurred = new();
        Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 0);

        // ================================================================
        //  BƯỚC 3: nhị phân bằng Otsu   *** CHỖ SỬA QUAN TRỌNG NHẤT ***
        // ================================================================
        // Otsu tự tìm ngưỡng tối ưu, bạn không phải hardcode con số nào.
        //
        // Vật thể của bạn TỐI (xanh đậm) nằm trên nền SÁNG (trắng):
        //   ThresholdTypes.Binary    : pixel SÁNG hơn ngưỡng -> 255
        //                              => NỀN thành 255, vật thể thành 0     (SAI)
        //   ThresholdTypes.BinaryInv : pixel TỐI  hơn ngưỡng -> 255
        //                              => VẬT THỂ thành 255                  (ĐÚNG)
        //
        // Toàn bộ OpenCV quy ước 255 = tiền cảnh (thứ ta quan tâm), 0 = nền.
        // Đặt sai chỗ này thì FindContours/Open/Subtract đều đi xử lý cái NỀN.
        // Đúng là lỗi cũ của bạn: ảnh debug "vung tim" tô trắng tam giác nền
        // ở góc trên-phải, còn vật thể thì đen -> residual tất nhiên rỗng.
        //
        // MẸO nếu sau này gặp ảnh ngược sáng (vật sáng / nền tối): đổi về Binary.
        // Cách tự động: so độ sáng trung bình 4 góc ảnh với trung bình toàn ảnh.
        using var mask = new Mat();
        Cv2.Threshold(blurred, mask, 0, 255, ThresholdTypes.BinaryInv | ThresholdTypes.Otsu);
        Dbg.Show(mask, "2_Threshold");   // ★ KIỂM TRA: vật thể phải TRẮNG, nền phải ĐEN

        // ================================================================
        //  BƯỚC 4: closing — vá khe nứt và lỗ nhỏ bên trong vật thể
        // ================================================================
        // Closing = giãn (dilate) rồi co (erode). Nó bịt các lỗ/khe NHỎ HƠN kernel
        // mà không làm vật thể phình to ra.
        // Dùng Ellipse chứ đừng dùng Rect: hình tròn không thiên vị hướng nào,
        // nên kết quả không đổi khi vật thể đặt nghiêng.
        using var k5 = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(5, 5));
        Cv2.MorphologyEx(mask, mask, MorphTypes.Close, k5, iterations: 2);
        Dbg.Show(mask, "3_Closed");

        // ================================================================
        //  BƯỚC 5: giữ blob to nhất + lấp kín ruột
        // ================================================================
        // >>> SỬA LỖI: đã BỎ HẲN BuildSolidMask ở đây.
        //
        // BuildSolidMask sinh ra để xử lý ảnh ĐƯỜNG VIỀN (Canny): nó floodfill từ nền
        // rồi đảo ngược để suy ra phần ruột. Giờ đầu vào đã là ảnh nhị phân ĐẶC rồi,
        // floodfill không còn việc gì để làm.
        //
        // Tệ hơn, nó còn phá hoại: vật thể trong ROI này CHẠM MÉP ảnh. Hàm đó
        // CopyMakeBorder thêm một vòng viền 1px toàn số 0 quanh ảnh — vòng viền đó
        // trở thành CÂY CẦU nối từ góc (0,0) vòng quanh rồi chui thẳng vào ruột vật thể
        // qua chỗ chạm mép. FloodFill tràn vào trong -> mask chỉ còn mấy sợi chỉ.
        // (Chính là ảnh debug 24_SolidMask.png hôm nay của bạn.)
        //
        // LargestFilled làm đúng việc cần và đơn giản hơn nhiều:
        // FindContours(External) chỉ trả về đường biên NGOÀI cùng, vẽ lại nó với
        // thickness = -1 (tô đặc) là vừa bỏ được blob rác vừa lấp kín ruột, một công đôi việc.
        using Mat objMask = LargestFilled(mask);
        Dbg.Show(objMask, "4_ObjMask");

        // ================================================================
        //  BƯỚC 6: sanity check — cái đèn báo
        // ================================================================
        // >>> SỬA LỖI: phải đo trên objMask, tức đúng cái Mat sẽ dùng ở bước sau.
        // Code cũ đo trên solidMask (≈ vùng nền) nên ratio ra ~60% và LỌT QUA check,
        // trong khi mask thực chất đã hỏng. Đèn báo đo nhầm chỗ còn tệ hơn không có đèn:
        // nó khiến bạn tin tưởng rồi đi tìm lỗi ở bước sau, sai chỗ hoàn toàn.
        double roiArea = (double)objMask.Rows * objMask.Cols;
        double ratio = Cv2.CountNonZero(objMask) / roiArea;
        Dbg.Log($"ratio mask = {ratio:P1}   (kỳ vọng ~30-45% cho ROI góc trên-phải)");

        if (ratio < ProtrusionCfg.MinObjectRatio || ratio > ProtrusionCfg.MaxObjectRatio)
        {
            Dbg.Log("=> MASK HỎNG. Mở ảnh 2_Threshold xem có bị đảo trắng/đen không.");
            return null;
        }

        // ================================================================
        //  BƯỚC 7: TOP-HAT — tách phần nhô ra khỏi thân
        // ================================================================
        // Đây là trái tim của thuật toán. Hình dung thế này:
        //
        //   Lăn một cái ĐĨA bán kính r khắp bên trong vùng trắng.
        //   opening = tập hợp tất cả những chỗ mà đĩa CHẠM TỚI được.
        //     - Thân vật thể to  -> đĩa lăn thoải mái  -> opening giữ nguyên
        //     - Phần nhô mảnh    -> đĩa không lọt vào  -> opening xoá sạch
        //
        //   residual = mask - opening  =  đúng phần đĩa không với tới  =  phần nhô ra.
        //
        // Tên gọi chính thức: white top-hat transform.
        //
        // VÌ SAO DÙNG ĐĨA TRÒN (BuildDisk) mà không dùng Rect/Cross:
        // chỉ hình tròn mới đối xứng xoay hoàn toàn. Nhờ vậy kết quả KHÔNG ĐỔI
        // dù con hàng đặt nghiêng bao nhiêu độ — đúng cái bạn cần vì vị trí bất định.
        // Đây cũng là lý do cách này hơn hẳn FitLine: FitLine giả định biên là
        // đường THẲNG, sai ngay khi biên cong hoặc có nhiều phần nhô.
        using var opened = OpenWithDisk(objMask, ProtrusionCfg.DiskRadius, ProtrusionCfg.OpenDownScale);
        Dbg.Show(opened, "5_Opened");    // ★ KIỂM TRA: thân còn nguyên, cụm nhô đã BIẾN MẤT

        using var residual = new Mat();
        // THỨ TỰ THAM SỐ QUAN TRỌNG: objMask trước, opened sau.
        // Cv2.Subtract là phép trừ có bão hoà (kết quả âm bị kẹp về 0),
        // nên đảo ngược hai tham số sẽ ra ảnh rỗng hoàn toàn chứ không báo lỗi gì.
        Cv2.Subtract(objMask, opened, residual);
        Dbg.Show(residual, "6_Residual");

        // ================================================================
        //  BƯỚC 8: lọc ứng viên trong residual
        // ================================================================
        // Residual còn lẫn rác: chóp nhọn của thân bị bào, viền răng cưa, đốm nhiễu.
        // PickProtrusion lọc bằng 4 tiêu chí, đọc chi tiết trong hàm đó.
        Rect? found = PickProtrusion(residual, opened, objMask.Size(), roiArea);

        if (found is null)
        {
            Dbg.Log("Không ứng viên nào qua được bộ lọc.");
            Dbg.Log("=> Mở ảnh 6_Residual xem có gì không:");
            Dbg.Log("   - Residual RỖNG        -> DiskRadius quá NHỎ, tăng lên");
            Dbg.Log("   - Residual đầy rác     -> DiskRadius quá LỚN, giảm xuống");
            Dbg.Log("   - Residual đúng chỗ    -> bộ lọc quá chặt, nới MinAreaRatio/MinAspect");
            Dbg.Log("   Hoặc gọi Program.SweepDiskRadius(imgTrenPhai) để quét tự động.");
            return null;
        }

        Rect box = found.Value;

        // ---------- Vẽ overlay để mắt người kiểm tra ----------
        // Bước này không ảnh hưởng kết quả, nhưng đừng bỏ: nhìn 1 giây
        // biết ngay đúng/sai, nhanh hơn đọc log rất nhiều.
        using (Mat vis = imgTrenPhai.Clone())
        {
            Cv2.Rectangle(vis, box, new Scalar(0, 255, 0), 2);
            Cv2.PutText(vis, $"{box.Width}x{box.Height}",
                        new Point(box.X, Math.Max(14, box.Y - 6)),
                        HersheyFonts.HersheySimplex, 0.6, new Scalar(0, 255, 0), 2);
            Dbg.Show(vis, "7_KetQua");
        }

        Dbg.Log($"TÌM THẤY phần nhô ra tại (toạ độ trong ROI): {box}");

        // Trả về ảnh MÀU của phần nhô, cắt từ ROI.
        // .Clone() là bắt buộc: new Mat(src, rect) chỉ tạo một "cửa sổ nhìn" dùng chung
        // bộ nhớ với src. Không Clone thì khi src bị Dispose, Mat trả về thành rác.
        return new Mat(imgTrenPhai, box).Clone();
    }

    /// <summary>
    /// Chọn ứng viên tốt nhất trong ảnh residual.
    /// </summary>
    /// <param name="residual">ảnh top-hat (mask - opening)</param>
    /// <param name="body">phần thân (chính là ảnh opening), dùng để kiểm tra ứng viên có dính thân không</param>
    /// <param name="roiSize">kích thước ROI, để biết đâu là mép</param>
    /// <param name="roiArea">diện tích ROI, để quy các ngưỡng về tỉ lệ</param>
    private static Rect? PickProtrusion(Mat residual, Mat body, Size roiSize, double roiArea)
    {
        // FindContours SỬA TRỰC TIẾP ảnh đầu vào -> luôn truyền bản Clone,
        // nếu không thì residual bị phá và những lần Dbg.Show sau sẽ thấy ảnh sai.
        using var work = residual.Clone();
        Cv2.FindContours(work, out Point[][] cnts, out _,
                         RetrievalModes.External, ContourApproximationModes.ApproxNone);

        if (cnts is null || cnts.Length == 0)
        {
            Dbg.Log("PickProtrusion: residual rỗng, không có contour nào.");
            return null;
        }

        double minArea = roiArea * ProtrusionCfg.MinAreaRatio;
        double maxArea = roiArea * ProtrusionCfg.MaxAreaRatio;

        Rect? best = null;
        double bestScore = double.MinValue;
        int loaiDienTich = 0, loaiAspect = 0, loaiMep = 0, loaiKhongDinh = 0;

        foreach (var c in cnts)
        {
            // --- Lọc 1: diện tích ---
            double area = Cv2.ContourArea(c);
            if (area < minArea || area > maxArea) { loaiDienTich++; continue; }

            // --- Lọc 2: hình dạng, đo bằng minAreaRect ---
            // BẮT BUỘC dùng MinAreaRect (hình chữ nhật XOAY ôm sát nhất), KHÔNG dùng
            // BoundingRect (hình chữ nhật thẳng trục). Một thanh dài đặt nghiêng 45 độ
            // cho BoundingRect gần vuông (aspect ~1) -> lọc sẽ loại nhầm.
            // MinAreaRect cho ra kích thước thật, không phụ thuộc góc đặt.
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

            // --- Lọc 3: bỏ ứng viên sát mép ROI ---
            // Sát mép thường là chỗ vật thể bị khung ROI cắt cụt, không phải phần nhô thật.
            // Đây cũng là cái cứu ta khi DiskRadius để lớn: chóp nhọn của thân bị bào
            // sẽ lòi ra trong residual, nhưng nó nằm sát mép nên bị loại ở đây.
            Rect br = Cv2.BoundingRect(c);
            int m = ProtrusionCfg.BorderMargin;
            if (br.X <= m || br.Y <= m ||
                br.Right >= roiSize.Width - m || br.Bottom >= roiSize.Height - m)
            {
                loaiMep++;
                continue;
            }

            // --- Lọc 4: phải DÍNH vào thân ---
            // Phần nhô ra thì theo định nghĩa phải mọc từ thân. Đốm trắng lơ lửng giữa
            // nền là nhiễu. Cách kiểm tra: giãn ứng viên ra vài pixel rồi AND với thân,
            // còn pixel nào chung là có dính.
            if (!DinhVaoThan(c, body, roiSize, ProtrusionCfg.TouchDilate))
            {
                loaiKhongDinh++;
                continue;
            }

            // --- Chấm điểm ---
            // Ưu tiên vùng vừa TO vừa NHÔ XA (canhDai lớn). Nếu sau này bắt nhầm,
            // đây là chỗ đầu tiên nên chỉnh — ví dụ đổi thành area thuần,
            // hoặc cộng thêm khoảng cách từ trọng tâm tới tâm ROI.
            double score = area * canhDai;
            if (score > bestScore)
            {
                bestScore = score;
                best = NoiRongRect(br, ProtrusionCfg.BboxPadding, roiSize);
            }
        }

        // Log này quý lắm: nó nói CHÍNH XÁC bộ lọc nào đang giết ứng viên của bạn,
        // khỏi phải đoán mò.
        Dbg.Log($"PickProtrusion: {cnts.Length} contour -> loại vì " +
                $"diện tích={loaiDienTich}, aspect={loaiAspect}, sát mép={loaiMep}, không dính thân={loaiKhongDinh}" +
                $" -> còn lại: {(best is null ? "KHÔNG CÓ" : best.Value.ToString())}");

        return best;
    }

    /// <summary>Ứng viên có dính vào thân vật thể không.</summary>
    private static bool DinhVaoThan(Point[] contour, Mat body, Size size, int dilate)
    {
        using var m = new Mat(size, MatType.CV_8UC1, Scalar.All(0));
        Cv2.DrawContours(m, new[] { contour }, -1, Scalar.All(255), -1);

        // Giãn ra vài pixel: sau phép trừ top-hat, ứng viên và thân thường hở nhau
        // đúng 1-2 pixel ở đường cắt, không giãn thì phép AND ra rỗng và loại oan.
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
        // LỖI KINH ĐIỂN cần tránh: tính Width mới bằng "r.Width + 2*pad" rồi mới clamp.
        // Khi rect nằm sát mép, X bị kẹp về 0 nhưng Width vẫn giữ nguyên -> rect thò ra ngoài.
        // Cách đúng: đổi sang toạ độ hai góc (x1,y1)-(x2,y2), clamp TỪNG GÓC, rồi trừ ra Width.
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
    public static void SweepDiskRadius(Mat anhDaCat)
    {
        int roiW = 500, roiH = 500;
        Rect area = new Rect(anhDaCat.Width - 1 - roiW, 50, roiW, roiH)
                    & new Rect(0, 0, anhDaCat.Width, anhDaCat.Height);
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

        // Nhớ giá trị cấu hình gốc để trả lại sau khi quét — nếu không,
        // lần chạy tiếp theo sẽ dùng nhầm giá trị của vòng lặp cuối cùng.
        int cfgCu = ProtrusionCfg.DiskRadius;

        foreach (int r in new[] { 30, 40, 50, 60, 70, 80, 90, 100, 120, 140 })
        {
            using var opened = OpenWithDisk(objMask, r, ProtrusionCfg.OpenDownScale);
            using var residual = new Mat();
            Cv2.Subtract(objMask, opened, residual);

            int soPixel = Cv2.CountNonZero(residual);
            ProtrusionCfg.DiskRadius = r;
            Rect? hit = PickProtrusion(residual, opened, objMask.Size(), roiArea);

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
        // Tổng x+y nhỏ nhất = trên-trái, lớn nhất = dưới-phải.
        // Hiệu x-y nhỏ nhất = dưới-trái, lớn nhất = trên-phải.
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
        return output;
    }

    /// <summary>Chưa có ảnh thì tự vẽ một ảnh mẫu để chạy thử ngay.</summary>
    private static string EnsureSampleImage()
    {
        const string path = "sample.png";
        if (File.Exists(path)) return path;

        using var img = new Mat(900, 1200, MatType.CV_8UC3, new Scalar(40, 45, 50));

        // Một "tờ giấy" trắng đặt nghiêng trên nền tối.
        var paper = new[]
        {
            new Point(220, 160), new Point(980, 240),
            new Point(920, 760), new Point(180, 660)
        };
        Cv2.FillConvexPoly(img, paper, new Scalar(235, 240, 245), LineTypes.AntiAlias);
        Cv2.PutText(img, "SAMPLE", new Point(380, 460), HersheyFonts.HersheyDuplex,
            2.4, new Scalar(60, 60, 60), 4, LineTypes.AntiAlias);

        Cv2.ImWrite(path, img);
        Console.WriteLine($"Chưa truyền đường dẫn ảnh -> đã tạo ảnh mẫu {path} để chạy thử.");
        return path;
    }

    private static Mat BuildSolidMask(Mat closedEdges)
    {
        using var padded = new Mat();
        Cv2.CopyMakeBorder(closedEdges, padded, 1, 1, 1, 1,
                           BorderTypes.Constant, Scalar.All(0));
        using var original = padded.Clone();   // giu lai vong bien

        using var ffMask = new Mat(padded.Rows + 2, padded.Cols + 2,
                                   MatType.CV_8UC1, Scalar.All(0));
        Cv2.FloodFill(padded, ffMask, new Point(0, 0), Scalar.All(255),
                      out _, Scalar.All(0), Scalar.All(0), FloodFillFlags.Link4);
        // Sau floodFill: nen = 255, ruot vat the = 0, duong bien = 255

        using var interior = new Mat();
        Cv2.BitwiseNot(padded, interior);      // ruot vat the = 255

        using var full = new Mat();
        Cv2.BitwiseOr(interior, original, full);   // cong lai vong bien

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

        using var diskSmall = BuildDisk(rs);
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

    /// <summary>
    /// Giu component lon nhat VA lap lo ben trong cung mot luc:
    /// FindContours(External) chi tra ve bien ngoai, ve lai voi thickness = -1 la duoc mask dac.
    /// </summary>
    private static Mat LargestFilled(Mat mask)
    {
        using var work = mask.Clone();   // FindContours co the sua source
        Cv2.FindContours(work, out Point[][] cnts, out _,
                         RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        var res = new Mat(mask.Size(), MatType.CV_8UC1, Scalar.All(0));
        if (cnts == null || cnts.Length == 0) return res;

        var biggest = cnts.OrderByDescending(c => Cv2.ContourArea(c)).First();
        Cv2.DrawContours(res, new[] { biggest }, -1, Scalar.All(255), -1);
        return res;
    }

}
