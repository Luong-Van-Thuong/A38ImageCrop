using OpenCvSharp;

namespace A38.ImageCrop;

/// <summary>
/// Bộ công cụ debug: gọi ở bất kỳ đâu trong pipeline để xem ảnh hoặc in số liệu.
/// Tất cả đều tự tắt khi <see cref="Enabled"/> = false, nên không cần xoá code khi chạy thật.
/// </summary>
public static class Dbg
{
    // ---- Công tắc (đổi trong Program.cs hoặc ngay lúc chạy) --------------------

    /// <summary>Tắt cái này là mọi lệnh Dbg.* trở thành no-op.</summary>
    public static bool Enabled = true;

    /// <summary>Bật cửa sổ ảnh (Cv2.ImShow).</summary>
    public static bool ShowWindow = true;

    /// <summary>Ghi ảnh ra thư mục <see cref="OutDir"/> để xem lại sau.</summary>
    public static bool SaveFile = true;

    /// <summary>Dừng chờ bấm phím ở mỗi Show. Tắt để chạy một mạch.</summary>
    public static bool Pause = true;

    /// <summary>
    /// Chỉ debug những bước có tên chứa chuỗi này. Ví dụ Filter = "thresh"
    /// thì chỉ bước threshold hiện ra, các bước khác bỏ qua. null = debug tất cả.
    /// </summary>
    public static string? Filter = null;

    public static string OutDir = "debug_out";

    /// <summary>Ảnh to hơn mức này sẽ được thu nhỏ lại khi hiển thị (không ảnh hưởng dữ liệu).</summary>
    public static int MaxDisplaySize = 900;

    private static int _step;

    // ---- API chính ------------------------------------------------------------

    /// <summary>Gọi đầu mỗi ảnh để đánh số bước lại từ 1 và dọn thư mục output.</summary>
    public static void Reset()
    {
        _step = 0;
        if (Enabled && SaveFile)
        {
            Directory.CreateDirectory(OutDir);
            foreach (var f in Directory.GetFiles(OutDir, "*.png")) File.Delete(f);
        }
    }

    /// <summary>In một dòng chữ ra console (chỉ khi debug đang bật).</summary>
    public static void Log(string message)
    {
        if (!Enabled) return;
        Console.WriteLine($"  {message}");
    }

    /// <summary>
    /// In số liệu của một Mat: kích thước, kiểu dữ liệu, min/max, trung bình,
    /// số điểm khác 0 (dùng để biết ảnh nhị phân có quá trắng / quá đen không).
    /// </summary>
    public static void Stats(Mat m, string name)
    {
        if (!Enabled || !Match(name)) return;
        if (m.Empty()) { Console.WriteLine($"  [{name}] Mat RỖNG"); return; }

        var line = $"  [{name}] {m.Width}x{m.Height} {m.Type()} ch={m.Channels()}";

        if (m.Channels() == 1)
        {
            Cv2.MinMaxLoc(m, out double min, out double max);
            var mean = Cv2.Mean(m).Val0;
            int nz = m.Depth() == MatType.CV_8U ? Cv2.CountNonZero(m) : -1;
            line += $" | min={min:0.##} max={max:0.##} mean={mean:0.##}";
            if (nz >= 0)
            {
                double pct = 100.0 * nz / (m.Width * m.Height);
                line += $" | nonzero={nz} ({pct:0.#}%)";
            }
        }
        else
        {
            var mean = Cv2.Mean(m);
            line += $" | mean=({mean.Val0:0.#}, {mean.Val1:0.#}, {mean.Val2:0.#})";
        }

        Console.WriteLine(line);
    }

    /// <summary>
    /// Hiện ảnh trong một cửa sổ + in số liệu. Bấm phím bất kỳ để đi tiếp,
    /// bấm Esc hoặc 'q' để tắt debug và chạy thẳng đến hết.
    /// </summary>
    public static void Show(Mat m, string name, bool? pause = null)
    {
        if (!Enabled || !Match(name)) return;
        if (m.Empty()) { Console.WriteLine($"  [{name}] Mat RỖNG, không hiện được"); return; }

        _step++;
        Stats(m, name);

        using var view = ToDisplay(m);

        if (SaveFile)
        {
            Directory.CreateDirectory(OutDir);
            var file = Path.Combine(OutDir, $"{_step:00}_{Sanitize(name)}.png");
            Cv2.ImWrite(file, view);
        }

        if (!ShowWindow) return;

        var title = $"{_step:00} - {name}";
        Cv2.ImShow(title, view);
        Cv2.MoveWindow(title, 60, 60);

        if (pause ?? Pause)
        {
            Console.WriteLine("     (bấm phím bất kỳ để tiếp, Esc/q để chạy thẳng)");
            int key = Cv2.WaitKey(0);
            if (key == 27 || key == 'q' || key == 'Q')
            {
                Enabled = false;
                Console.WriteLine("     -> đã tắt debug, chạy thẳng đến hết.");
            }
        }
        else
        {
            Cv2.WaitKey(1); // cho cửa sổ kịp vẽ
        }

        Cv2.DestroyWindow(title);
    }

    /// <summary>Hiện 2 ảnh cạnh nhau để so sánh trước/sau một bước.</summary>
    public static void ShowPair(Mat left, Mat right, string name)
    {
        if (!Enabled || !Match(name)) return;

        using var a = ToDisplay(left);
        using var b = ToDisplay(right);
        int h = Math.Max(a.Height, b.Height);
        using var canvas = new Mat(h, a.Width + b.Width + 10, MatType.CV_8UC3, Scalar.All(30));

        a.CopyTo(new Mat(canvas, new Rect(0, 0, a.Width, a.Height)));
        b.CopyTo(new Mat(canvas, new Rect(a.Width + 10, 0, b.Width, b.Height)));

        Show(canvas, name);
    }

    /// <summary>
    /// In thẳng giá trị pixel trong một vùng nhỏ — dùng khi cần biết
    /// "chỗ này giá trị bao nhiêu mà threshold lại ăn/không ăn".
    /// </summary>
    public static void Values(Mat m, Rect roi, string name)
    {
        if (!Enabled || !Match(name)) return;
        if (m.Channels() != 1) { Console.WriteLine($"  [{name}] chỉ dump được ảnh 1 kênh"); return; }

        roi = roi.Intersect(new Rect(0, 0, m.Width, m.Height));
        if (roi.Width <= 0 || roi.Height <= 0) { Console.WriteLine($"  [{name}] ROI nằm ngoài ảnh"); return; }

        Console.WriteLine($"  [{name}] giá trị pixel tại {roi}:");
        using var patch = new Mat(m, roi);
        using var u8 = new Mat();
        patch.ConvertTo(u8, MatType.CV_8U);

        int rows = Math.Min(u8.Height, 20);
        int cols = Math.Min(u8.Width, 20);
        for (int y = 0; y < rows; y++)
        {
            var cells = new List<string>();
            for (int x = 0; x < cols; x++)
                cells.Add(u8.At<byte>(y, x).ToString().PadLeft(4));
            Console.WriteLine("    " + string.Join("", cells));
        }
    }

    /// <summary>Đóng hết cửa sổ còn sót (gọi cuối chương trình).</summary>
    public static void CloseAll() => Cv2.DestroyAllWindows();

    // ---- Nội bộ ---------------------------------------------------------------

    private static bool Match(string name) =>
        Filter is null || name.Contains(Filter, StringComparison.OrdinalIgnoreCase);

    /// <summary>Chuẩn hoá bất kỳ Mat nào về ảnh BGR 8-bit vừa màn hình để hiển thị.</summary>
    private static Mat ToDisplay(Mat m)
    {
        var work = new Mat();

        // Ảnh float/16-bit: kéo về 0..255 để mắt người nhìn được.
        if (m.Depth() != MatType.CV_8U)
            Cv2.Normalize(m, work, 0, 255, NormTypes.MinMax, MatType.CV_8U);
        else
            m.CopyTo(work);

        // cvtColor/resize không làm in-place được khi đổi số kênh hoặc đổi kích thước.
        if (work.Channels() != 3)
        {
            var bgr = new Mat();
            Cv2.CvtColor(work, bgr, work.Channels() == 1
                ? ColorConversionCodes.GRAY2BGR
                : ColorConversionCodes.BGRA2BGR);
            work.Dispose();
            work = bgr;
        }

        int longest = Math.Max(work.Width, work.Height);
        if (longest > MaxDisplaySize)
        {
            double s = (double)MaxDisplaySize / longest;
            var small = new Mat();
            Cv2.Resize(work, small, new Size(), s, s, InterpolationFlags.Area);
            work.Dispose();
            work = small;
        }

        return work;
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Replace(' ', '_');
    }
}
