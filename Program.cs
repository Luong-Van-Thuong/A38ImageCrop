using System.Runtime.InteropServices;
using System.Windows;
using A38.ImageCrop.PmAlign;

namespace A38.ImageCrop;

/// <summary>
/// Cấu hình dùng chung cho các nhánh console của branch này.
///
/// Branch hoc_patModel chỉ còn một mục đích: dựng một tool kiểu CogPMAlign của Cognex —
/// đưa vào ảnh, trả ra toạ độ (x, y) và góc θ của con hàng. Program.cs không chứa
/// pipeline nào, nó chỉ là bảng điều phối gọi sang đúng nhánh.
/// </summary>
public static class Config
{
    /// <summary>Đuôi ảnh chấp nhận khi một nhánh trỏ vào cả một thư mục (<c>--do-bien</c>).</summary>
    public static string[] ImageExtensions = { ".bmp", ".png", ".jpg", ".jpeg", ".tif", ".tiff" };
}

public static class Program
{
    /// <summary>Các cờ mở một nhánh console; mọi cờ khác đều đi vào giao diện.</summary>
    private static readonly string[] CoConsole = { "--do-bien", "--tu-kiem", "--help", "-h" };

    /// <summary>
    /// Không có cờ nhánh nào thì mở GIAO DIỆN PmAlign — đó là đường chạy chính. Hai cờ console
    /// còn lại đều là THƯỚC ĐO, không phải tính năng: chúng trả lời "Train/Run chạy đúng chưa",
    /// câu mà bấm nút trên giao diện không bao giờ trả lời được.
    ///
    /// [STAThread] là bắt buộc: WPF, hộp thoại mở file và clipboard đều đòi căn hộ đơn luồng.
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        // App là WinExe nên mặc định KHÔNG có console. Chỉ nhánh console mới cần một cái,
        // đường chạy giao diện thì tuyệt đối không đụng vào — đó là chỗ cửa sổ terminal đen
        // vẫn bám theo app trước đây.
        if (args.Any(CoConsole.Contains)) MoConsoleNeuCan();

        Dbg.Enabled = !args.Contains("--no-debug");
        Dbg.ShowWindow = !args.Contains("--no-window");
        Dbg.Pause = !args.Contains("--no-pause");
        Dbg.SaveFile = true;

        int i = Array.IndexOf(args, "--only");
        if (i >= 0 && i + 1 < args.Length) Dbg.Filter = args[i + 1];

        // Do bien: chay mot model tren ca thu muc voi nhieu cau hinh cham diem, ra CSV + anh ghep.
        if (args.Contains("--do-bien")) return PmDoBien.Chay(args);

        // Tu kiem engine bang chan ly biet truoc: xoay/doi anh mot luong da biet roi bat Run tim lai.
        if (args.Contains("--tu-kiem")) return PmTuKiem.Chay(args);

        if (args.Contains("--help") || args.Contains("-h")) { InCachDung(); return 0; }

        return MoGiaoDien(args);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    private const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;

    /// <summary>
    /// Móc process vào một console để Console.WriteLine có chỗ chảy ra.
    ///
    /// Gọi từ cmd/PowerShell thì bám luôn vào console của shell đó (AttachConsole) — chữ hiện
    /// ngay trong cửa sổ đang gõ, không đẻ thêm cửa sổ nào. Bấm đúp từ Explorer thì không có
    /// console cha, lúc đó mới tự mở một cái (AllocConsole).
    ///
    /// Phải SetOut/SetError lại: Console của .NET đã bị buộc vào thiết bị null từ lúc process
    /// khởi động ở chế độ WinExe, không tự nhận handle mới. Mở lại standard handle cũng chính
    /// là đường giữ nguyên hành vi khi người dùng chuyển hướng ra file (<c>&gt; out.txt</c>).
    /// </summary>
    private static void MoConsoleNeuCan()
    {
        if (!AttachConsole(ATTACH_PARENT_PROCESS) && !AllocConsole()) return;

        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
        Console.SetIn(new StreamReader(Console.OpenStandardInput()));
    }

    private static int MoGiaoDien(string[] args)
    {
        // GUI mac dinh TAT debug. Nhanh console chay mot lan roi thoat nen bat debug san
        // la tien; con GUI thi moi lan bam Run lai di qua ~21 lan Dbg.Show, moi lan ghi mot
        // file PNG (26 giay cho mot lan Run) roi ket o Cv2.WaitKey(0) trong Task.Run - cua so
        // HighGUI moc ra sau cua so WPF nen nhin y het treo.
        //
        // Muon soi tung buoc thi tick "Debug" tren thanh cong cu: anh hien trong cua so Debug
        // cua chinh app, co nut "Tiep" thay cho phim bam mu. Co --debug chi la tick san.
        Dbg.Enabled = false;
        Dbg.ShowWindow = false;
        Dbg.SaveFile = false;
        Dbg.Pause = false;

        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        var cs = new CuaSoPmAlign();

        // Tham số đầu tiên không phải cờ thì coi là ảnh, mở luôn cho đỡ một lần bấm.
        var anh = args.FirstOrDefault(a => !a.StartsWith('-') && File.Exists(a));
        if (anh != null) cs.Loaded += (_, _) => cs.MoAnhTuNgoai(anh);

        if (args.Contains("--debug")) cs.Loaded += (_, _) => cs.BatDebugTuNgoai();

        app.MainWindow = cs;
        cs.Show();
        return app.Run();
    }

    private static void InCachDung()
    {
        Console.WriteLine("""
            Branch hoc_patModel — tool do mau theo hinh dang (kieu CogPMAlign).

              (khong co co)         Mo GIAO DIEN PmAlign: mo anh -> khoanh vung tim kiem
                                    -> khoanh ROI mau va xoay 360 do -> Train -> Run
              <duong-dan-anh>       Nhu tren, mo san tam anh do

            Hai nhanh DO LUONG (khong phai tinh nang — de kiem engine):
              --tu-kiem [anh]       Tu kiem bang chan ly biet truoc (xoay/doi anh mot luong da biet)
                                    them --dung-sai <px> / --khong-nms / --bo-dau de van tung num
              --do-bien <thu-muc> <model.pmm.json>
                                    Chay mot model tren ca thu muc voi nhieu cau hinh cham diem,
                                    ra CSV + anh ghep. Do BIEN giua dinh dung va dinh sai.
                    --vt cx,cy,w,h  khung hep de dung chan ly
                    --bo a.bmp,...  loai vai anh khoi thong ke
                    --on-dinh       train lai co loc on dinh roi do lai
                    --ra <thu-muc>  noi ghi ket qua

            Co debug dung chung:
              --no-window   Khong bat cua so, chi ghi anh ra dia
              --no-pause    Khong dung cho bam phim
              --no-debug    Tat sach debug
              --only <ten>  Chi debug buoc co ten chua chuoi nay
            """);
    }
}
