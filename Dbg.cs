using OpenCvSharp;
using System.Text;

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

    // ---- Duong ra cam duoc (giao dien WPF cam vao day) ----------------------
    //
    // Cv2.ImShow + WaitKey chi song duoc tren luong co bom thong diep rieng cua HighGUI.
    // Giao dien goi Train/Run trong Task.Run nen cua so HighGUI moc ra sau cua so WPF va
    // WaitKey(0) ngoi cho mot phim khong bao gio den - nhin y het treo. Con SaveFile thi
    // moi buoc ma hoa mot file PNG, 21 buoc cua mot lan Run het 26 giay.
    // Cam Sink vao la Show() giao anh thang cho giao dien, khong dung ca hai duong do nua.

    /// <summary>
    /// Nhan anh cua tung buoc: (anh da chuan hoa ve BGR 8-bit vua man hinh, ten buoc,
    /// so thu tu, dong so lieu). Dat khac null thi Show() KHONG goi ImShow/WaitKey nua.
    ///
    /// Mat truyen vao CHI SONG TRONG LUC GOI - Show() huy no ngay sau do. Ben nhan phai
    /// chuyen sang dang cua minh (BitmapSource dong bang chang han) ngay trong than ham,
    /// khong duoc cat lai tham chieu roi dung sau.
    /// </summary>
    public static Action<Mat, string, int, string>? Sink;

    /// <summary>Nhan moi dong chu cua Log / Info / Stats, de nhat ky hien ngay trong giao dien.</summary>
    public static Action<string>? SinkChu;

    /// <summary>
    /// Chot chan sau moi buoc, thay cho WaitKey - dung cho che do "dung tung buoc".
    /// Chay tren LUONG XU LY (Task.Run), nen ben dat phai tu lo dong bo.
    /// Tra ve false nghia la nguoi dung bam "chay thang": Show() tat debug luon.
    /// </summary>
    public static Func<bool>? ChoBuoc;

    /// <summary>Ảnh to hơn mức này sẽ được thu nhỏ lại khi hiển thị (không ảnh hưởng dữ liệu).</summary>
    public static int MaxDisplaySize = 900;

    private static int _step;

    // ---- Chế độ chạy song song (nhiều ảnh cùng lúc) ---------------------------
    // Bốn luồng cùng gọi Console.WriteLine thì log của bốn ảnh trộn vào nhau, đọc
    // không nổi; _step và OutDir dùng chung cũng làm ảnh debug đè lên nhau.
    // Bật SongSong thì mỗi luồng có bộ đếm, thư mục và bộ đệm log RIÊNG,
    // xong một ảnh mới in cả khối ra một lượt.
    //
    // [ThreadStatic] = mỗi luồng giữ một bản sao riêng, không cần khoá gì cả.
    // Lưu ý: field [ThreadStatic] KHÔNG chạy bộ khởi tạo trên luồng khác luồng đầu tiên,
    // nên mọi chỗ đọc đều phải chịu được giá trị null / 0.
    public static bool SongSong;

    [ThreadStatic] private static StringBuilder? _demLog;
    [ThreadStatic] private static int _stepLuong;
    [ThreadStatic] private static string? _outDirLuong;

    /// <summary>Gọi ở đầu mỗi ảnh trong luồng: đặt thư mục debug riêng và mở bộ đệm log.</summary>
    public static void BatDauLuong(string outDir)
    {
        _outDirLuong = outDir;
        _stepLuong = 0;
        _demLog = new StringBuilder();
    }

    /// <summary>Lấy trọn log của luồng hiện tại rồi đóng bộ đệm (gọi khi xong một ảnh).</summary>
    public static string KetThucLuong()
    {
        var s = _demLog?.ToString() ?? string.Empty;
        _demLog = null;
        return s;
    }

    // Dùng thư mục riêng ngay khi đã đặt, kể cả lúc chạy tuần tự — chạy cả thư mục
    // theo kiểu tuần tự cũng cần mỗi ảnh một thư mục debug riêng.
    private static string ThuMucHienTai => _outDirLuong ?? OutDir;

    private static int TangStep() => SongSong ? ++_stepLuong : ++_step;

    /// <summary>Mọi dòng chữ đều đi qua đây: song song thì gom vào đệm, không thì in thẳng.</summary>
    private static void Out(string line)
    {
        SinkChu?.Invoke(line);
        if (SongSong && _demLog is not null) _demLog.AppendLine(line);
        else Console.WriteLine(line);
    }

    // ---- API chính ------------------------------------------------------------

    /// <summary>Gọi đầu mỗi ảnh để đánh số bước lại từ 1 và dọn thư mục output.</summary>
    public static void Reset()
    {
        if (SongSong) _stepLuong = 0; else _step = 0;

        if (Enabled && SaveFile)
        {
            var dir = ThuMucHienTai;
            Directory.CreateDirectory(dir);
            foreach (var f in Directory.GetFiles(dir, "*.png")) File.Delete(f);
        }
    }

    /// <summary>In một dòng chữ ra console (chỉ khi debug đang bật).</summary>
    public static void Log(string message)
    {
        if (!Enabled) return;
        Out($"  {message}");
    }

    /// <summary>
    /// Dòng thông báo LUÔN in, kể cả khi đã tắt debug — dùng cho kết quả và tổng kết.
    /// Vẫn đi qua bộ đệm nên khi chạy song song vẫn nằm đúng khối của ảnh đó.
    /// </summary>
    public static void Info(string message) => Out(message);

    /// <summary>
    /// In số liệu của một Mat: kích thước, kiểu dữ liệu, min/max, trung bình,
    /// số điểm khác 0 (dùng để biết ảnh nhị phân có quá trắng / quá đen không).
    /// </summary>
    public static void Stats(Mat m, string name)
    {
        if (!Enabled || !Match(name)) return;
        Out(DongStats(m, name));
    }

    /// <summary>
    /// Dung dong so lieu cua mot Mat. Tach rieng khoi <see cref="Stats"/> vi giao dien can
    /// CHUOI de dan lam chu thich cho anh, chu khong phai in ra console.
    /// </summary>
    private static string DongStats(Mat m, string name)
    {
        if (m.Empty()) return $"  [{name}] Mat RONG";

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

        return line;
    }

    /// <summary>
    /// Hiện ảnh trong một cửa sổ + in số liệu. Bấm phím bất kỳ để đi tiếp,
    /// bấm Esc hoặc 'q' để tắt debug và chạy thẳng đến hết.
    /// </summary>
    public static void Show(Mat m, string name, bool? pause = null)
    {
        if (!Enabled || !Match(name)) return;
        if (m.Empty()) { Out($"  [{name}] Mat RỖNG, không hiện được"); return; }

        int step = TangStep();
        var soLieu = DongStats(m, name);
        Out(soLieu);

        using var view = ToDisplay(m);

        if (SaveFile)
        {
            var dir = ThuMucHienTai;
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"{step:00}_{Sanitize(name)}.png");
            Cv2.ImWrite(file, view);
        }

        // Giao dien da cam Sink thi di duong do: no tu ve trong cua so WPF cua no,
        // va tu lo chuyen dung tung buoc bang mot cai nut nhin thay duoc.
        if (Sink is { } guiAnh)
        {
            guiAnh(view, name, step, soLieu);
            if (ChoBuoc is { } cho && !cho())
            {
                Enabled = false;
                Out("     -> Da tat debug, chay den het.");
            }
            return;
        }

        // Cv2.ImShow/WaitKey chỉ được gọi từ MỘT luồng — bốn luồng cùng mở cửa sổ
        // là treo hoặc chết ngay trong native code, nên chạy song song thì cấm hẳn.
        if (!ShowWindow || SongSong) return;

        var title = $"{step:00} - {name}";
        Cv2.ImShow(title, view);
        Cv2.MoveWindow(title, 60, 60);

        if (pause ?? Pause)
        {
            Console.WriteLine("     (Bam phim bat ky, Esc/q de chay thang)");
            int key = Cv2.WaitKey(0);
            if (key == 27 || key == 'q' || key == 'Q')
            {
                Enabled = false;
                Console.WriteLine("     -> Da tat debug, chay den het.");
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
        if (m.Channels() != 1) { Out($"  [{name}] chỉ dump được ảnh 1 kênh"); return; }

        roi = roi.Intersect(new Rect(0, 0, m.Width, m.Height));
        if (roi.Width <= 0 || roi.Height <= 0) { Out($"  [{name}] ROI nằm ngoài ảnh"); return; }

        Out($"  [{name}] giá trị pixel tại {roi}:");
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
            Out("    " + string.Join("", cells));
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
