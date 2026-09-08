using OpenCvSharp;

namespace A38.ImageCrop.PmAlign;

/// <summary>
/// Tự kiểm engine bằng CHÂN LÝ BIẾT TRƯỚC: lấy một ảnh thật, train một ROI trên đó, rồi
/// xoay/dời chính tấm ảnh đó đi một lượng đã biết và bắt Run tìm lại.
///
/// Đây là cách duy nhất bắt được lỗi "lệch đúng một hằng số": dò trên ảnh khác nhìn vẫn
/// đẹp trong khi góc trả về sai dấu hoặc lệch φ, mà mắt thường không thấy.
///
/// Chạy: <c>dotnet run -- --tu-kiem [duong-dan-anh]</c>
/// </summary>
public static class PmTuKiem
{
    public static int Chay(string[] args)
    {
        // Đường dẫn ảnh = tham số trần đầu tiên sau --tu-kiem mà KHÔNG phải giá trị của một
        // cờ có tham số. Không lọc kiểu đó thì "--dung-sai 2" làm số 2 bị hiểu là tên ảnh.
        string[] coCoThamSo = ["--dung-sai", "--only"];
        string? anhDan = null;
        for (int i = Array.IndexOf(args, "--tu-kiem") + 1; i > 0 && i < args.Length; i++)
        {
            if (args[i].StartsWith('-')) continue;
            if (i > 0 && coCoThamSo.Contains(args[i - 1])) continue;
            anhDan = args[i];
            break;
        }
        anhDan ??= @"D:\Images_\V2\CoilAssy\CoilAssy\1240S\opencv\1B.bmp";

        if (!File.Exists(anhDan)) { Console.WriteLine($"Khong thay anh: {anhDan}"); return 1; }
        using var anh = Cv2.ImDecode(File.ReadAllBytes(anhDan), ImreadModes.Color);
        if (anh.Empty()) { Console.WriteLine("Khong doc duoc anh."); return 1; }
        Console.WriteLine($"Anh: {anhDan}  {anh.Width}x{anh.Height}");

        // ROI mẫu: lấy hộp bao thân vật rồi thu vào 55%, và CỐ TÌNH xoay 23.7° — nếu code
        // nướng góc ROI vào toạ độ điểm thay vì cất riêng, chỗ này sẽ lộ ra ngay.
        using var xam = PmEngine.ToXam(anh);
        using var bin = new Mat();
        Cv2.Threshold(xam, bin, 0, 255, ThresholdTypes.BinaryInv | ThresholdTypes.Otsu);
        Cv2.FindContours(bin, out Point[][] cts, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        var than = cts.Length > 0 ? Cv2.BoundingRect(cts.OrderByDescending(c => Cv2.ContourArea(c)).First())
                                  : new Rect(anh.Width / 4, anh.Height / 4, anh.Width / 2, anh.Height / 2);

        // Chọn ô có nhiều cấu trúc nhất trong lưới 3×3 phủ thân vật. Lấy đại ô giữa là hỏng:
        // vật này là khung bát giác CÓ LỖ TRÒN LỚN Ở GIỮA, ô giữa rơi trọn vào nền backlight
        // nên miếng mẫu trắng tinh 255..255 và model ra 0 điểm.
        double phi = 23.7;
        double canh = Math.Min(than.Width, than.Height) * 0.30;
        RectXoay roi = default;
        double totNhat = -1;
        for (int gy = 0; gy < 3; gy++)
            for (int gx = 0; gx < 3; gx++)
            {
                var thu = new RectXoay(than.X + than.Width * (0.2 + 0.3 * gx),
                                       than.Y + than.Height * (0.2 + 0.3 * gy), canh, canh, phi);
                using var mieng = PmEngine.CatMau(anh, thu);
                using var mx = PmEngine.ToXam(mieng);
                Cv2.MeanStdDev(mx, out var _, out var sd);
                if (sd.Val0 > totNhat) { totNhat = sd.Val0; roi = thu; }
            }
        Console.WriteLine($"ROI mau: {roi}   do lech chuan {totNhat:F1}");

        // Tự tạo thư mục: ImWrite vào thư mục không tồn tại KHÔNG ném lỗi, nó lặng lẽ trả
        // false — bài tự kiểm mà nuốt lỗi thì mất luôn ý nghĩa. Trước đây thư mục này do
        // nhánh --model sinh ra, nhánh đó đã bỏ nên phải tự lo.
        const string raDan = "tu_kiem_out";
        Directory.CreateDirectory(raDan);
        using (var mau = PmEngine.CatMau(anh, roi))
        {
            string dan = Path.Combine(raDan, "tukiem_mau.png");
            if (!Cv2.ImWrite(dan, mau)) Console.WriteLine($"  CANH BAO: khong ghi duoc {dan}");
            else Console.WriteLine($"  mieng mau ghi ra {dan}");
        }

        var cfg = new PmCfg { SoMuc = 6, SoDiemToiDa = 500, GocTuDo = -20, GocDenDo = 20, DiemToiThieu = 0.05 };

        // Cho phép vặn ba núm chấm điểm ngay trên dòng lệnh: bài này là bài duy nhất có CHÂN
        // LÝ dưới pixel, nên nó mới là chỗ chọn được DungSaiPx — dung sai càng rộng thì điểm
        // càng cao nhưng đỉnh càng bẹt, mà bẹt thì nội suy dưới pixel hết tác dụng.
        int ids = Array.IndexOf(args, "--dung-sai");
        if (ids >= 0 && ids + 1 < args.Length && double.TryParse(args[ids + 1], out double ds)) cfg.DungSaiPx = ds;
        if (args.Contains("--khong-nms")) cfg.NmsLucChay = false;
        if (args.Contains("--bo-dau")) cfg.BoQuaChieuTuongPhan = true;
        Console.WriteLine($"Cham diem: NMS={cfg.NmsLucChay}, dung sai={cfg.DungSaiPx:F1}px, " +
                          $"bo dau={cfg.BoQuaChieuTuongPhan}, san muc={cfg.SoDiemToiThieuMoiMuc}");
        var model = PmEngine.Train(anh, roi, [], cfg, s => Console.WriteLine("  " + s));
        Console.WriteLine();

        (double dx, double dy, double goc)[] caTest =
        [
            (0, 0, 0),
            (0, 0, 5),
            (0, 0, -12.34),
            (17, -23, 0),
            (-31.5, 12.25, 7.5),
            (8, 8, -3.2),
        ];

        Console.WriteLine("  dat vao           |  tim duoc                       |  sai so           | thoi gian");
        Console.WriteLine("  dx     dy   goc   |  dx      dy      goc     diem   |  vi tri   goc     |");
        Console.WriteLine("  " + new string('-', 90));

        double teNhatVt = 0, teNhatGoc = 0;
        int hong = 0;

        foreach (var (dx, dy, g) in caTest)
        {
            using var mBd = Cv2.GetRotationMatrix2D(new Point2f(anh.Width / 2f, anh.Height / 2f), g, 1.0);
            mBd.Set(0, 2, mBd.At<double>(0, 2) + dx);
            mBd.Set(1, 2, mBd.At<double>(1, 2) + dy);

            using var anhMoi = new Mat();
            Cv2.WarpAffine(anh, anhMoi, mBd, anh.Size(), InterpolationFlags.Linear, BorderTypes.Replicate);

            // Chân lý: tâm ROI đi theo đúng ma trận đó, còn góc mẫu tăng thêm đúng g.
            double txX = mBd.At<double>(0, 0) * roi.Cx + mBd.At<double>(0, 1) * roi.Cy + mBd.At<double>(0, 2);
            double txY = mBd.At<double>(1, 0) * roi.Cx + mBd.At<double>(1, 1) * roi.Cy + mBd.At<double>(1, 2);

            // Vùng tìm kiếm cố ý đặt lệch tâm 40px để chứng minh no khong an gian.
            var vt = new RectXoay(txX + 40, txY - 40, 260, 260, 0);

            var vet = new List<string>();
            var dongHo = System.Diagnostics.Stopwatch.StartNew();
            var kq = PmEngine.Run(anhMoi, model, vt, cfg, vet.Add);
            dongHo.Stop();
            if (kq.Count == 0)
            {
                Console.WriteLine($"  {dx,5:F1} {dy,5:F1} {g,6:F2}  |  KHONG TIM THAY");
                foreach (var v in vet) Console.WriteLine("        " + v);
                hong++;
                continue;
            }

            var k = kq[0];
            double saiVt = Math.Sqrt((k.X - txX) * (k.X - txX) + (k.Y - txY) * (k.Y - txY));
            double saiGoc = Math.Abs(((k.GocDo - g + 540) % 360) - 180);
            teNhatVt = Math.Max(teNhatVt, saiVt);
            teNhatGoc = Math.Max(teNhatGoc, saiGoc);

            Console.WriteLine($"  {dx,5:F1} {dy,5:F1} {g,6:F2}  |  {k.X - roi.Cx,7:F2} {k.Y - roi.Cy,7:F2} " +
                              $"{k.GocDo,7:F3} {k.Diem,7:F3}  |  {saiVt,6:F2}px {saiGoc,7:F3}deg  | {dongHo.ElapsedMilliseconds,5} ms");
        }

        Console.WriteLine();
        Console.WriteLine($"Sai so lon nhat: {teNhatVt:F2} px, {teNhatGoc:F3} do.  Truot: {hong}/{caTest.Length}");
        Console.WriteLine(teNhatVt < 2.0 && teNhatGoc < 0.5 && hong == 0
            ? "=> DAT nguong <2px va <0.5do tren bo test nay."
            : "=> CHUA DAT. Xem lai tham so hoac ROI mau.");
        return hong == 0 ? 0 : 2;
    }
}
