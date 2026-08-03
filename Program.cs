using OpenCvSharp;
using OpenCvSharp.XImgProc;
using OpenCvSharp.XPhoto;
using System.Net.WebSockets;
using System.Runtime.Intrinsics.X86;

namespace A38.ImageCrop;

/// <summary>
/// Bốn góc của ảnh đã cắt ở giai đoạn 1 — giai đoạn 2 soi lần lượt từng góc.
/// Thứ tự khai báo cũng là thứ tự chạy và thứ tự hiện trong log.
/// </summary>
public enum Goc
{
    TrenTrai,
    TrenPhai,
    DuoiPhai,
    DuoiTrai
}

/// <summary>
/// Tham số cắt ROI ở bốn góc (giai đoạn 2).
/// Các số lệch giữ nguyên theo code cũ: mép trên có dải nền thừa nên phải bỏ 50px,
/// mép dưới thừa nhiều hơn nên bỏ 100px.
/// </summary>
public static class GocCfg
{
    // Sử dụng tỷ lệ % (0.2 tương đương 20%)
    //public static double RoiW = 500;
    //public static double RoiH = 500;

    public static double RoiW = 0.20;
    public static double RoiH = 0.20;

    /// <summary>Bỏ bấy nhiêu % tính từ mép TRÊN trước khi cắt ROI hai góc trên.</summary>
    //public static double LechTren = 50; // 5%
    public static double LechTren = 0.05; // 5%

    /// <summary>Bỏ bấy nhiêu % tính từ mép DƯỚI trước khi cắt ROI hai góc dưới.</summary>
    //public static double LechDuoi = 100; // 10%
    public static double LechDuoi = 0.10; // 10%

    /// <summary>Góc nào cần soi.</summary>
    public static Goc[] CacGocCanSoi = { Goc.TrenTrai, Goc.TrenPhai, Goc.DuoiPhai, Goc.DuoiTrai };
}

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
    public static string InputImagePath = "D:\\Images_\\V2\\CoilAssy\\CoilAssy\\1240S\\opencv\\Image__2026-07-28__09-36-30.bmp";

    /// <summary>
    /// THƯ MỤC ảnh đầu vào — điền vào đây thì chương trình chạy LẦN LƯỢT TỪNG ẢNH trong đó,
    /// và <see cref="InputImagePath"/> bị BỎ QUA (muốn quay lại chạy 1 ảnh thì để trống "").
    /// </summary>
    public static string InputFolderPath = "D:\\Images_\\V2\\CoilAssy\\CoilAssy\\1240S\\img_all\\1";

    /// <summary>Đuôi file được coi là ảnh khi quét thư mục.</summary>
    public static string[] ImageExtensions = { ".bmp", ".png", ".jpg", ".jpeg", ".tif", ".tiff" };

    /// <summary>Quét cả thư mục con hay chỉ thư mục hiện tại.</summary>
    public static bool Recursive = false;

    /// <summary>
    /// Khi chạy cả thư mục: có ghi ảnh từng bước (Dbg.Show) ra đĩa không.
    /// Mỗi ảnh sinh ra hơn chục file debug, chạy vài trăm ảnh là đầy đĩa và chậm hẳn,
    /// nên mặc định TẮT. Bật lên khi cần soi kỹ một mẻ nhỏ.
    /// </summary>
    public static bool SaveDebugStepsInBatch = false;

    /// <summary>
    /// Số ảnh chạy CÙNG LÚC khi quét thư mục. Mỗi luồng ôm trọn một XuLyMotAnh.
    /// Đặt 1 để quay lại chạy tuần tự (dễ đọc log, dễ đặt breakpoint khi debug).
    /// Đừng đặt cao hơn số nhân CPU: mỗi luồng ngốn vài trăm MB cho ảnh 3648x3648.
    /// </summary>
    public static int SoLuong = 4;
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
    // ================================================================
    //  NƠI LƯU ẢNH KẾT QUẢ GIAI ĐOẠN 2
    // ================================================================
    // Đừng nhầm với thư mục Dbg.OutDir ("debug_out"): thư mục đó bị Dbg.Reset()
    // XOÁ SẠCH mỗi lần chạy ảnh mới, chỉ dùng để soi bước trung gian.
    // Ảnh ở đây là kết quả cần giữ lại nên ghi sang "output" và đặt tên theo ảnh gốc.
    private static readonly string _outDirG2 = "img_train__";

    //Folder chua anh khong bat duoc phan nho
    private static readonly string _outDirNotFound = "img_not_found";

    // [ThreadStatic]: bốn luồng chạy bốn ảnh khác nhau cùng lúc, mà tên file lại dựng
    // từ hai biến này. Để static thường thì luồng B ghi đè _baseName của luồng A giữa chừng
    // -> ảnh của ảnh A mang tên ảnh B, và bộ đếm nhảy loạn. Lỗi kiểu này không crash,
    // chỉ ra kết quả sai, nên rất khó phát hiện — phải chặn ngay từ khai báo.
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
        //var path = Path.Combine(_outDirG2, $"{_baseName ?? "image"}_g2_{++_demAnhG2:00}_{tag}.png");
        var path = Path.Combine(nameFolder, $"{tag}.png");
        Cv2.ImWrite(path, img);
        Dbg.Log($"da luu anh giai doan 2: {path}");
        return path;
    }

    public static int Main(string[] args)
    {
        // ---- Cấu hình debug ---------------------------------------------------
        Dbg.Enabled = !args.Contains("--no-debug");
        Dbg.ShowWindow = !args.Contains("--no-window");
        Dbg.Pause = !args.Contains("--no-pause");
        Dbg.SaveFile = true;

        // Chạy cả thư mục mà vẫn muốn xem ảnh từng bước: thêm --debug-steps,
        // khỏi phải sửa Config rồi build lại. Mỗi ảnh một thư mục con trong debug_out.
        if (args.Contains("--debug-steps")) Config.SaveDebugStepsInBatch = true;

        // Chỉ muốn xem 1 bước? Bỏ comment dòng dưới (hoặc chạy: --only canny)
        // Dbg.Filter = "canny";
        int i = Array.IndexOf(args, "--only");
        if (i >= 0 && i + 1 < args.Length) Dbg.Filter = args[i + 1];

        // ---- Danh sách ảnh đầu vào -------------------------------------------
        var danhSachAnh = ThuThapAnhVao(args);
        if (danhSachAnh.Count == 0)
        {
            Console.WriteLine("Khong co anh nao de chay. Kiem tra lai Config.InputFolderPath / Config.InputImagePath.");
            return 1;
        }

        // Chạy cả mẻ mà vẫn bật cửa sổ + dừng chờ phím thì mỗi ảnh phải bấm hơn chục lần,
        // nên tự tắt. Muốn soi từng bước thì để Config.InputFolderPath = "" rồi chạy 1 ảnh.
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

        // ---- Chạy từng ảnh ----------------------------------------------------
        int soThanhCong = 0, soKhongThayPhanNho = 0, soLoi = 0;
        var thoiDiemBatDau = DateTime.Now;

        // Mỗi ảnh kèm số thứ tự để log biết đang ở ảnh nào — chạy song song thì thứ tự
        // in ra không còn theo số này nữa, nên càng cần nó để đối chiếu.
        var congViec = danhSachAnh.Select((p, n) => (Path: p, ThuTu: n + 1)).ToList();

        // Cả mẻ nhiều ảnh + Config.SoLuong > 1 thì chạy song song, mỗi luồng ôm trọn
        // một XuLyMotAnh. Không chia nhỏ hơn nữa (kiểu mỗi luồng một góc) vì như thế
        // các luồng phải chờ nhau ở cuối mỗi ảnh, còn chia theo ảnh thì luồng nào xong
        // là bốc ảnh kế tiếp ngay, CPU không có lúc nào rảnh.
        int soLuong = Math.Max(1, Config.SoLuong);
        bool chaySongSong = chayCaMe && soLuong > 1;

        if (chaySongSong)
        {
            Dbg.SongSong = true;   // Dbg chuyển sang bộ đếm + bộ đệm log riêng từng luồng
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
                        // Interlocked: ba biến đếm này bị bốn luồng cộng cùng lúc.
                        // soThanhCong++ KHÔNG phải thao tác nguyên tử (đọc - cộng - ghi),
                        // hai luồng cộng cùng lúc là mất một lượt đếm.
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

        // ---- Tổng kết ---------------------------------------------------------
        Console.WriteLine();
        Console.WriteLine($"===== XONG {danhSachAnh.Count} anh trong {(DateTime.Now - thoiDiemBatDau).TotalSeconds:0.0}s" +
                          $"{(chaySongSong ? $" ({soLuong} luong)" : "")} =====");
        Console.WriteLine($"  bat duoc phan nho  : {soThanhCong}");
        Console.WriteLine($"  khong thay phan nho: {soKhongThayPhanNho}");
        Console.WriteLine($"  loi                : {soLoi}");
        Console.WriteLine($"  anh ket qua nam trong: {Path.GetFullPath(_outDirG2)}");

        Dbg.CloseAll();
        return soLoi > 0 ? 2 : 0;
    }

    /// <summary>Khoá riêng cho Console: bốn luồng in cùng lúc thì các khối log cài răng lược vào nhau.</summary>
    private static readonly object _khoaConsole = new();

    /// <summary>
    /// Chạy một ảnh và in trọn khối log của nó. Dùng chung cho cả chạy tuần tự lẫn song song,
    /// nên hai đường chạy không bao giờ lệch hành vi nhau.
    /// </summary>
    private static KetQuaXuLy ChayMotViec(string inputPath, int thuTu, int tong, bool chayCaMe)
    {
        var tenAnh = Path.GetFileNameWithoutExtension(inputPath);

        // Mỗi ảnh một thư mục debug riêng: Dbg.Reset() xoá sạch *.png trong thư mục đó,
        // dùng chung một thư mục thì ảnh sau xoá mất ảnh trước (song song thì còn xoá
        // ngay lúc luồng khác đang ghi).
        if (chayCaMe || Config.SaveDebugStepsInBatch) Dbg.BatDauLuong(Path.Combine("debug_out", tenAnh));

        Dbg.Info($"===== [{thuTu}/{tong}] {Path.GetFileName(inputPath)} =====");

        KetQuaXuLy ketQua;
        // try/catch bọc TỪNG ảnh: một file hỏng không được phép làm chết cả mẻ.
        // Trong Parallel.ForEach còn quan trọng hơn: một exception lọt ra ngoài là
        // cả vòng lặp dừng, những ảnh chưa chạy bị bỏ luôn.
        try
        {
            ketQua = XuLyMotAnh(inputPath);
        }
        catch (Exception ex)
        {
            ketQua = KetQuaXuLy.Loi;
            Dbg.Info($"  Loi khi xu ly{Path.GetFileName(inputPath)}: {ex.Message}");
        }

        // Lấy log đã gom rồi in một phát — có khoá nên khối của ảnh này không bị
        // khối của ảnh khác chen ngang.
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

    /// <summary>Kết quả xử lý một ảnh — tách 3 mức để tổng kết cuối mẻ cho rõ ràng.</summary>
    private enum KetQuaXuLy
    {
        /// <summary>Chạy hết pipeline và bắt được phần nhô.</summary>
        ThanhCong,
        /// <summary>Giai đoạn 1 xong nhưng giai đoạn 2 không tìm ra phần nhô (chưa chắc là lỗi).</summary>
        KhongThayPhanNho,
        /// <summary>Không đọc được ảnh, hoặc giai đoạn 1 không dò ra vùng nào.</summary>
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
        // 1. Thư mục cấu hình sẵn trong code.
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

        // 2/3. Một đường dẫn cụ thể: có thể là file, cũng có thể là thư mục.
        var duongDan = !string.IsNullOrWhiteSpace(Config.InputImagePath)
                       ? Config.InputImagePath
                       : args.FirstOrDefault(a => !a.StartsWith("--"));

        if (string.IsNullOrWhiteSpace(duongDan))
            duongDan = EnsureSampleImage();   // 4. không có gì thì tự vẽ ảnh mẫu

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
       //ResetLuuAnhG2(inputPath);   // tên file ảnh giai đoạn 2 lấy theo tên ảnh vào

        // ---- Giai đoạn 1: dò và cắt vùng lớn nhất (cả con hàng) ra khỏi ảnh gốc.
        using var result = CropLargestRegion(src);

        // >>> SỬA LỖI: câu check null này PHẢI đứng TRƯỚC lời gọi CropSmallRegion.
        // Code cũ gọi CropSmallRegion(result) trong khi result vẫn có thể là null,
        // rồi mới check ở dưới -> chương trình chết vì NullReferenceException
        // ngay tại dòng "int w = src.Width;" bên trong CropSmallRegion.
        // Nguyên tắc: kiểm tra null NGAY SAU khi nhận giá trị, trước mọi lần dùng nó.
        if (result is null || result.Empty())
        {
            Dbg.Info("  Khong do duoc vung nao giam Config.CannyLow or Config.MinAreaRatio.");
            return KetQuaXuLy.Loi;
        }

        // Ảnh cắt giai đoạn 1 vẫn ghi ra như cũ, kể cả khi giai đoạn 2 trượt.
        Directory.CreateDirectory(_outDirG2);
        var outPath = Path.Combine(_outDirG2, _baseName + "_crop.png");
        //Cv2.ImWrite(outPath, result);
        //Dbg.Info($"  Giai đoạn 1: {outPath}  ({result.Width}x{result.Height})");

        // ---- Giai đoạn 2: từ ảnh đã cắt, soi CẢ BỐN GÓC để tìm phần nhô ra.
        // Ảnh của giai đoạn 2 được lưu ngay bên trong CropSmallRegion/PickProtrusion.
        var cacGoc = CropSmallRegion(result, nameImgMain);
        try
        {
            var gocBatDuoc = cacGoc.Where(g => g.Anh is not null).ToList();

            if (gocBatDuoc.Count == 0)
            {
                Dbg.Info("  Giai doan 2: Ca 4 goc deu chua bat duoc phan nho " +
                         "(xem log 'ratio mask' va anh 6_Residual cua tung goc).");
                LuuAnhG2(src, _outDirNotFound, $"{nameImgMain}");
                return KetQuaXuLy.KhongThayPhanNho;
            }

            var motTa = string.Join(", ", gocBatDuoc.Select(g => $"{g.Goc}({g.Anh!.Width}x{g.Anh.Height})"));
            Dbg.Info($"  Giai doan 2: bat duoc {gocBatDuoc.Count}/{cacGoc.Count} goc -> {motTa}; " +
                     $"da luu {_demAnhG2} anh.");
            return KetQuaXuLy.ThanhCong;
        }
        finally
        {
            // Mat KHÔNG do GC dọn (bộ nhớ nằm ngoài vùng quản lý). Ảnh đã ghi ra file rồi
            // nên tới đây là giải phóng được — chạy 4 luồng x 4 góc mà quên là hết RAM rất nhanh.
            foreach (var g in cacGoc) g.Anh?.Dispose();
        }
    }

    /// <summary>
    /// Pipeline chính: dò vùng lớn nhất trong ảnh rồi cắt ra.
    /// Mỗi bước đều có Dbg.Show để bạn nhìn thấy ảnh biến đổi thế nào.
    /// </summary>
    private static Mat? CropLargestRegion(Mat src)
    {
        //Dbg.Show(src, "original");

        // --- B1: thu nhỏ để dò cho nhanh. Toạ độ tìm được sẽ nhân ngược lại sau.
        double scale = Math.Min(1.0, (double)Config.WorkWidth / src.Width);
        using var work = new Mat();
        if (scale < 1.0)
            Cv2.Resize(src, work, new Size(), scale, scale, InterpolationFlags.Area);
        else
            src.CopyTo(work);
        //Dbg.Log($"scale dò = {scale:0.###} -> làm việc trên {work.Width}x{work.Height}");

        // --- B2: chuyển xám. Mọi thuật toán dò cạnh đều cần ảnh 1 kênh.
        using var gray = new Mat();


        // Tý thử đổi về 0 hoặc các cái khác xem nó biến đổi ảnh như thế nào  ------------

        Cv2.CvtColor(work, gray, ColorConversionCodes.BGR2GRAY);
        Dbg.Show(gray, "gray");

        // --- B3: làm mờ để bớt nhiễu, tránh Canny bắt phải hạt nhiễu.
        using var blur = new Mat();

        // Điều chỉnh BlurKernel và BlurKernel sang một số khác thì nó sẽ như nào 
        Cv2.GaussianBlur(gray, blur, new Size(Config.BlurKernel, Config.BlurKernel), 0);
        Dbg.Show(blur, "blur");

        // --- B4: dò cạnh.
        using var edges = new Mat();
        // Điều chỉnh cạnh CannyLow và CannyHigh sang số kahcs thì nó như nào ảnh tìm được trả về kết quả như nào 
        //Cv2.Canny(blur, edges, Config.CannyLow, Config.CannyHigh);
        Cv2.Canny(blur, edges, 150, 210);
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
        //Cv2.FindContours(closed, out Point[][] contours, out _,
        //    RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        Cv2.FindContours(closed, out Point[][] contours, out _,
            RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        Dbg.Show(closed, "morph_close");

        var imgContours = gray.Clone();
        Cv2.CvtColor(imgContours, imgContours, ColorConversionCodes.GRAY2BGR);
        for (int i = 0; i < contours.Length; i++)
        {
            // 1. Tạo màu HSV: H (0-179), S (255 - rực rỡ nhất), V (255 - sáng nhất)
            byte hue = (byte)(i * 179 / contours.Length);
            using Mat hsvPixel = new Mat(1, 1, MatType.CV_8UC3, new Scalar(hue, 255, 255));
            using Mat bgrPixel = new Mat();

            // 2. Convert HSV -> BGR để lấy Scalar chuẩn cho DrawContours
            Cv2.CvtColor(hsvPixel, bgrPixel, ColorConversionCodes.HSV2BGR);
            Vec3b bgr = bgrPixel.At<Vec3b>(0, 0);
            Scalar color = new Scalar(bgr.Item0, bgr.Item1, bgr.Item2);

            // 3. Vẽ contour
            Cv2.DrawContours(imgContours, contours, i, color, 2);

            // Giải phóng bộ nhớ Mat tạm
            //hsvPixel.Dispose();
            //bgrPixel.Dispose();
        }
        Dbg.Show(imgContours, "imgContours");


        

        //Dbg.Log($"tim duoc {contours.Length} contour");

        double imageArea = work.Width * (double)work.Height;
        var candidates = contours
            .Select(c => (Contour: c, Area: Cv2.ContourArea(c)))
            .Where(x => x.Area >= imageArea * Config.MinAreaRatio)
            .OrderByDescending(x => x.Area)
            .ToList();

        Dbg.Log($"con {candidates.Count} contour sau khi loc dien tich " +
                $"(>= {Config.MinAreaRatio:P0} anh = {imageArea * Config.MinAreaRatio:0} px)");

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
            Dbg.Log($"  - dien tich {area:0} px ({area / imageArea:P1} anh)");
        }

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
        Dbg.Log($"Xap xi da giac: {approx.Length} dinh (eps = {Config.ApproxEpsRatio * peri:0.#})");

        // --- B8: cắt. Toạ độ đang ở ảnh thu nhỏ nên phải chia lại cho scale.
        if (Config.WarpIfQuad && approx.Length == 4)
        {
            var corners = OrderCorners(approx.Select(p =>
                new Point2f((float)(p.X / scale), (float)(p.Y / scale))).ToArray());

            foreach (var c in corners) Dbg.Log($"  goc: ({c.X:0}, {c.Y:0})");

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

    /// <summary>Kết quả soi một góc: có bắt được gì không, ở đâu, và ảnh cắt ra.</summary>
    /// <param name="Goc">góc nào</param>
    /// <param name="RoiTrongAnh">vị trí ROI trong ảnh đã cắt của giai đoạn 1</param>
    /// <param name="VungTrongRoi">vị trí phần nhô, tính theo toạ độ trong ROI (null = không thấy)</param>
    /// <param name="Anh">ảnh màu của phần nhô — người gọi có trách nhiệm Dispose</param>
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
        //Dbg.Show(src, "0_AnhDaCat");

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
        // Tính toán kích thước pixel thực tế dựa trên phần trăm (%) của ảnh
        int roiW = (int)Math.Round(w * GocCfg.RoiW);
        int roiH = (int)Math.Round(h * GocCfg.RoiH);
        int lechTren = (int)Math.Round(h * GocCfg.LechTren);
        int lechDuoi = (int)Math.Round(h * GocCfg.LechDuoi);

        // Giữ nguyên cách đặt của code cũ: bốn đỉnh ảnh, rồi lùi vào theo chiều rộng/cao ROI.
        Point topLeft = new(0, 0);
        Point topRight = new(w - 1, 0);
        Point bottomLeft = new(0, h - 1);
        Point bottomRight = new(w - 1, h - 1);

        Rect r = goc switch
        {
            //Goc.TrenTrai => new Rect(topLeft.X, topLeft.Y + lechTren, roiW, roiH),
            //Goc.TrenPhai => new Rect(topRight.X - roiW, topRight.Y + lechTren, roiW, roiH),
            //Goc.DuoiPhai => new Rect(bottomRight.X - roiW, bottomRight.Y - roiH - lechDuoi, roiW, roiH),
            //Goc.DuoiTrai => new Rect(bottomLeft.X, bottomLeft.Y - roiH - lechDuoi, roiW, roiH),

            Goc.TrenTrai => new Rect(topLeft.X, topLeft.Y , roiW, roiH),
            Goc.TrenPhai => new Rect(topRight.X - roiW, topRight.Y , roiW, roiH),
            Goc.DuoiPhai => new Rect(bottomRight.X - roiW, bottomRight.Y - roiH , roiW, roiH),
            Goc.DuoiTrai => new Rect(bottomLeft.X, bottomLeft.Y - roiH , roiW, roiH),
            _ => throw new ArgumentOutOfRangeException(nameof(goc), goc, "Goc khong hop le")
        };
        return r & new Rect(0, 0, w, h);
    }


    //private static Rect VungRoiCuaGoc(Goc goc, Size anh)
    //{
    //    int w = anh.Width, h = anh.Height;
    //    int roiW = GocCfg.RoiW, roiH = GocCfg.RoiH;

    //    // Giữ nguyên cách đặt của code cũ: bốn đỉnh ảnh, rồi lùi vào theo chiều rộng/cao ROI.
    //    Point topLeft = new(0, 0);
    //    Point topRight = new(w - 1, 0);
    //    Point bottomLeft = new(0, h - 1);
    //    Point bottomRight = new(w - 1, h - 1);

    //    Rect r = goc switch
    //    {
    //        Goc.TrenTrai => new Rect(topLeft.X, topLeft.Y + GocCfg.LechTren, roiW, roiH),
    //        Goc.TrenPhai => new Rect(topRight.X - roiW, topRight.Y + GocCfg.LechTren, roiW, roiH),
    //        Goc.DuoiPhai => new Rect(bottomRight.X - roiW, bottomRight.Y - roiH - GocCfg.LechDuoi, roiW, roiH),
    //        Goc.DuoiTrai => new Rect(bottomLeft.X, bottomLeft.Y - roiH - GocCfg.LechDuoi, roiW, roiH),
    //        _ => throw new ArgumentOutOfRangeException(nameof(goc), goc, "Goc khong hop le")
    //    };
    //    return r & new Rect(0, 0, w, h);
    //}

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

        // using = tự gọi Dispose khi ra khỏi hàm. Mat giữ bộ nhớ ngoài vùng quản lý của GC,
        // quên using là rò bộ nhớ. Ảnh 40MB mà chạy vòng lặp nhiều file là hết RAM ngay.
        using Mat imgGoc = new(src, areaGoc);
        Dbg.Show(imgGoc, $"1_ROI_{goc}");

        //Program.SweepDiskRadius(anhDaCat: imgGoc, goc: goc);

        // ================================================================
        //  BƯỚC 1: chuyển xám
        // ================================================================
        // Mọi thuật toán nhị phân / morphology đều làm việc trên ảnh 1 kênh.
        using Mat gray = ToGray(imgGoc);

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
        Dbg.Show(mask, $"2_Threshold_{goc}");   // ★ KIỂM TRA: vật thể phải TRẮNG, nền phải ĐEN

        // ================================================================
        //  BƯỚC 4: closing — vá khe nứt và lỗ nhỏ bên trong vật thể
        // ================================================================
        // Closing = giãn (dilate) rồi co (erode). Nó bịt các lỗ/khe NHỎ HƠN kernel
        // mà không làm vật thể phình to ra.
        // Dùng Ellipse chứ đừng dùng Rect: hình tròn không thiên vị hướng nào,
        // nên kết quả không đổi khi vật thể đặt nghiêng.
        using var k7 = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(5, 5));
        Cv2.MorphologyEx(mask, mask, MorphTypes.Close, k7, iterations: 2);
        Dbg.Show(mask, $"3_Closed_{goc}");
        var test = "";

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
        using Mat objMask = 
            LargestFilled(mask);
        Dbg.Show(objMask, $"4_ObjMask_{goc}");

        // ================================================================
        //  BƯỚC 6: sanity check — cái đèn báo
        // ================================================================
        // >>> SỬA LỖI: phải đo trên objMask, tức đúng cái Mat sẽ dùng ở bước sau.
        // Code cũ đo trên solidMask (≈ vùng nền) nên ratio ra ~60% và LỌT QUA check,
        // trong khi mask thực chất đã hỏng. Đèn báo đo nhầm chỗ còn tệ hơn không có đèn:
        // nó khiến bạn tin tưởng rồi đi tìm lỗi ở bước sau, sai chỗ hoàn toàn.
        double roiArea = (double)objMask.Rows * objMask.Cols;
        double ratio = Cv2.CountNonZero(objMask) / roiArea;
        Dbg.Log($"ratio mask = {ratio:P1}   (kỳ vọng ~30-45% cho ROI ở góc)");

        if (ratio < ProtrusionCfg.MinObjectRatio || ratio > ProtrusionCfg.MaxObjectRatio)
        {
            Dbg.Log($"=> MASK HONG o goc {goc}. Mo anh 2_Threshold_{goc} xem co bi dao trang/den khong.");
            return new KetQuaGoc(goc, areaGoc, null, null);
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
        Dbg.Show(opened, $"5_Opened_{goc}");    // ★ KIỂM TRA: thân còn nguyên, cụm nhô đã BIẾN MẤT

        using var residual = new Mat();
        // THỨ TỰ THAM SỐ QUAN TRỌNG: objMask trước, opened sau.
        // Cv2.Subtract là phép trừ có bão hoà (kết quả âm bị kẹp về 0),
        // nên đảo ngược hai tham số sẽ ra ảnh rỗng hoàn toàn chứ không báo lỗi gì.
        Cv2.Subtract(objMask, opened, residual);
        Dbg.Show(residual, $"6_Residual_{goc}");

        // ================================================================
        //  BƯỚC 8: lọc ứng viên trong residual
        // ================================================================
        // Residual còn lẫn rác: chóp nhọn của thân bị bào, viền răng cưa, đốm nhiễu.
        // PickProtrusion lọc bằng 4 tiêu chí, đọc chi tiết trong hàm đó.
        //
        // Truyền thêm imgGoc (ảnh MÀU của ROI) để trong vòng foreach duyệt ứng viên,
        // mỗi ứng viên qua đủ bộ lọc đều được cắt và lưu ra file ngay tại chỗ.
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
        // 2. PHÒNG VỆ (Clamp Boundary) - BẮT BỘC PHẢI CÓ
        // Giới hạn Rect nằm hoàn toàn trong khung ảnh (imgWidth, imgHeight)
        int x = Math.Max(0, box.X);
        int y = Math.Max(0, box.Y);
        int width = Math.Min(imgWidth - x, box.Width + (box.X < 0 ? box.X : 0));
        int height = Math.Min(imgHeight - y, box.Height + (box.Y < 0 ? box.Y : 0));

        Rect safeCropBox = new Rect(x, y, width, height);

        // ---------- Vẽ overlay để mắt người kiểm tra ----------
        // Bước này không ảnh hưởng kết quả, nhưng đừng bỏ: nhìn 1 giây
        // biết ngay đúng/sai, nhanh hơn đọc log rất nhiều.
        using (Mat vis = imgGoc.Clone())
        {
            Cv2.Rectangle(vis, safeCropBox, new Scalar(0, 255, 0), 2);
            Cv2.PutText(vis, $"{goc} {safeCropBox.Width}x{safeCropBox.Height}",
                        new Point(safeCropBox.X, Math.Max(14, safeCropBox.Y - 6)),
                        HersheyFonts.HersheySimplex, 0.6, new Scalar(0, 255, 0), 2);
            Dbg.Show(vis, $"7_KetQua_{goc}");

            // Lưu luôn ảnh ROI có khung xanh: xem lại sau này biết ngay nó khoanh đúng chỗ chưa.
            //LuuAnhG2(vis, $"{goc}_overlay");
        }

        Dbg.Log($"Goc {goc}: TIM THAY phan nho tai (toa do trong ROI) {box}");

        // Trả về ảnh MÀU của phần nhô, cắt từ ROI.
        // .Clone() là bắt buộc: new Mat(src, rect) chỉ tạo một "cửa sổ nhìn" dùng chung
        // bộ nhớ với src. Không Clone thì khi imgGoc bị Dispose lúc ra khỏi hàm,
        // Mat trả về thành rác.

        var anhPhanNho1 = new Mat(imgGoc, safeCropBox).Clone();
        LuuAnhG2(anhPhanNho1, _outDirG2, $"{nameImgMain}_{goc}");   // ảnh kết quả của góc này
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
        // FindContours SỬA TRỰC TIẾP ảnh đầu vào -> luôn truyền bản Clone,
        // nếu không thì residual bị phá và những lần Dbg.Show sau sẽ thấy ảnh sai.
        //Dbg.Show(residual, $"img raw {goc}");
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
        int soUngVienDat = 0;   // đếm ứng viên qua đủ 4 bộ lọc, dùng đánh số tên file

        // Diện tích contour TO NHẤT trong residual — in ra ở log cuối hàm.
        // Khi cả mẻ đều bị loại vì diện tích, con số này cho biết ngay ngưỡng đang
        // đặt cao hơn thực tế bao nhiêu, khỏi phải mò từng nấc.
        double areaLonNhat = 0;

        foreach (var c in cnts)
        {
            // --- Lọc 1: diện tích ---
            double area = Cv2.ContourArea(c);
            if (area > areaLonNhat) areaLonNhat = area;
            if (area < 2000 || area > maxArea) { loaiDienTich++; continue; }

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

            // ================================================================
            //  ĐÃ TÌM ĐƯỢC MỘT VÙNG -> LƯU ẢNH NGAY TẠI ĐÂY
            // ================================================================
            // Tới dòng này nghĩa là ứng viên đã qua đủ 4 bộ lọc. Lưu ngay trong vòng lặp
            // (chứ không đợi ra ngoài) để giữ được CẢ những ứng viên thua điểm ở dưới:
            // khi bắt nhầm, mở thư mục output là so sánh được cái nào đúng cái nào sai,
            // khỏi phải chạy lại chương trình.
            //
            // Dùng chung NoiRongRect với phần chấm điểm bên dưới để ảnh lưu ra
            // đúng bằng vùng sẽ được trả về, không lệch nhau.
            Rect vung = NoiRongRect(br, ProtrusionCfg.BboxPadding, roiSize);
            soUngVienDat++;

            if (roiMau is not null && !roiMau.Empty())
            {
                // Kẹp thêm lần nữa theo kích thước THẬT của ảnh màu: roiSize là kích thước
                // của mask, về lý thuyết bằng nhau, nhưng cắt sai 1 pixel là Mat ném exception.
                Rect cat = vung & new Rect(0, 0, roiMau.Width, roiMau.Height);
                if (cat.Width > 0 && cat.Height > 0)
                {
                    using var anhUngVien = new Mat(roiMau, cat).Clone();
                    //LuuAnhG2(anhUngVien, $"{goc}_ungvien{soUngVienDat:00}_{cat.Width}x{cat.Height}");
                }
            }

            // --- Chấm điểm ---
            // Ưu tiên vùng vừa TO vừa NHÔ XA (canhDai lớn). Nếu sau này bắt nhầm,
            // đây là chỗ đầu tiên nên chỉnh — ví dụ đổi thành area thuần,
            // hoặc cộng thêm khoảng cách từ trọng tâm tới tâm ROI.
            double score = area * canhDai;
            if (score > bestScore)
            {
                bestScore = score;
                best = vung;
            }
        }

        // Log này quý lắm: nó nói CHÍNH XÁC bộ lọc nào đang giết ứng viên của bạn,
        // khỏi phải đoán mò.
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
    /// <param name="anhDaCat">ảnh kết quả của giai đoạn 1</param>
    /// <param name="goc">quét trên ROI của góc nào (mặc định trên-phải như trước)</param>
    public static void SweepDiskRadius(Mat anhDaCat, Goc goc = Goc.TrenPhai)
    {
        // Dùng chung VungRoiCuaGoc với luồng chạy thật: quét trên một ROI mà chạy thật
        // lại lấy ROI khác thì con số tìm ra chẳng dùng được vào đâu.
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

        // Nhớ giá trị cấu hình gốc để trả lại sau khi quét — nếu không,
        // lần chạy tiếp theo sẽ dùng nhầm giá trị của vòng lặp cuối cùng.
        int cfgCu = ProtrusionCfg.DiskRadius;

        foreach (int r in new[] { 30, 40, 50, 60, 70, 80, 90, 100, 120, 140 })
        {
            using var opened = OpenWithDisk(objMask, r, ProtrusionCfg.OpenDownScale);
            using var residual = new Mat();
            Cv2.Subtract(objMask, opened, residual);
            //Dbg.Show(residual, "");
            int soPixel = Cv2.CountNonZero(residual);
            ProtrusionCfg.DiskRadius = r;
            // roiMau = null: đang quét tham số, không lưu ảnh ứng viên (10 bán kính
            // x mấy ứng viên là ngập thư mục output).
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
        Console.WriteLine($"Chua truyen duong dan anh -> da tao anh mau {path} de chay thu.");
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
        //Dbg.Show(small, "Small");
        Cv2.Threshold(small, small, 127, 255, ThresholdTypes.Binary);
        //Dbg.Show(small, "Small");
        using var diskSmall = BuildDisk(50);

        using var openedSmall = new Mat();
        Cv2.MorphologyEx(small, openedSmall, MorphTypes.Open, diskSmall);
        //Dbg.Show(openedSmall, "Nhung phan con chua Disk");


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
        //Dbg.Show(k, "hinh tron");
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
        using var work = mask.Clone();   // FindContours co the sua source
        Cv2.FindContours(work, out Point[][] cnts, out _,
                         RetrievalModes.External, ContourApproximationModes.ApproxSimple);
       // Dbg.Show(work, "find contours");

        var res = new Mat(mask.Size(), MatType.CV_8UC1, Scalar.All(0));
        if (cnts == null || cnts.Length == 0) return res;

        var biggest = cnts.OrderByDescending(c => Cv2.ContourArea(c)).First();
        Cv2.DrawContours(res, new[] { biggest }, -1, Scalar.All(255), -1);
        //Dbg.Show(res, "Vung tim");
        return res;
    }

}
