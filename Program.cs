using OpenCvSharp;

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
        var inputPath = args.FirstOrDefault(a => !a.StartsWith("--"))
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
        using var result = CropLargestRegion(src);

        if (result is null || result.Empty())
        {
            Console.WriteLine("KHÔNG dò được vùng nào. Thử giảm Config.CannyLow hoặc Config.MinAreaRatio.");
            Dbg.CloseAll();
            return 2;
        }

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
        Cv2.CvtColor(work, gray, ColorConversionCodes.BGR2GRAY);
        Dbg.Show(gray, "gray");

        // --- B3: làm mờ để bớt nhiễu, tránh Canny bắt phải hạt nhiễu.
        using var blur = new Mat();
        Cv2.GaussianBlur(gray, blur, new Size(Config.BlurKernel, Config.BlurKernel), 0);
        Dbg.Show(blur, "blur");

        // --- B4: dò cạnh.
        using var edges = new Mat();
        Cv2.Canny(blur, edges, Config.CannyLow, Config.CannyHigh);
        Dbg.Show(edges, "canny");
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
        Dbg.Log($"tìm được {contours.Length} contour");

        double imageArea = work.Width * (double)work.Height;
        var candidates = contours
            .Select(c => (Contour: c, Area: Cv2.ContourArea(c)))
            .Where(x => x.Area >= imageArea * Config.MinAreaRatio)
            .OrderByDescending(x => x.Area)
            .ToList();

        Dbg.Log($"còn {candidates.Count} contour sau khi lọc diện tích " +
                $"(>= {Config.MinAreaRatio:P0} ảnh = {imageArea * Config.MinAreaRatio:0} px)");

        foreach (var (_, area) in candidates.Take(5))
            Dbg.Log($"  - diện tích {area:0} px ({area / imageArea:P1} ảnh)");

        if (candidates.Count == 0) return null;

        // Vẽ đè các contour lên ảnh để nhìn xem nó bắt đúng chỗ chưa.
        if (Dbg.Enabled)
        {
            using var overlay = work.Clone();
            Cv2.DrawContours(overlay, candidates.Select(x => x.Contour).ToArray(), -1,
                new Scalar(0, 255, 0), 2);
            Cv2.DrawContours(overlay, new[] { candidates[0].Contour }, -1,
                new Scalar(0, 0, 255), 3);   // đỏ = vùng được chọn
            Dbg.Show(overlay, "contours");
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
            Dbg.Show(warped, "result_warp");
            return warped;
        }
        else
        {
            var r = Cv2.BoundingRect(best);
            var full = new Rect(
                (int)(r.X / scale), (int)(r.Y / scale),
                (int)(r.Width / scale), (int)(r.Height / scale))
                .Intersect(new Rect(0, 0, src.Width, src.Height));

            Dbg.Log($"cắt theo hình chữ nhật bao: {full}");
            var cropped = new Mat(src, full).Clone();
            Dbg.Show(cropped, "result_crop");
            return cropped;
        }
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
}
