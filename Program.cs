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
    /// <summary>Đuôi ảnh chấp nhận khi <c>--model</c> trỏ vào một thư mục.</summary>
    public static string[] ImageExtensions = { ".bmp", ".png", ".jpg", ".jpeg", ".tif", ".tiff" };

    /// <summary>Bật bởi <c>--debug-steps</c>. Giữ lại cho tương thích cờ cũ.</summary>
    public static bool SaveDebugStepsInBatch = false;
}

public static class Program
{
    /// <summary>
    /// Không có cờ nhánh nào thì mở GIAO DIỆN PmAlign — đây mới là đường chạy chính của
    /// branch, ba cờ console cũ chỉ còn để chạy lại các bài đo cũ.
    ///
    /// [STAThread] là bắt buộc: WPF, hộp thoại mở file và clipboard đều đòi căn hộ đơn luồng.
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        Dbg.Enabled = !args.Contains("--no-debug");
        Dbg.ShowWindow = !args.Contains("--no-window");
        Dbg.Pause = !args.Contains("--no-pause");
        Dbg.SaveFile = true;

        int i = Array.IndexOf(args, "--only");
        if (i >= 0 && i + 1 < args.Length) Dbg.Filter = args[i + 1];

        if (args.Contains("--debug-steps")) Config.SaveDebugStepsInBatch = true;

        // Bước 1a của tool dò mẫu: chỉ trích model từ ảnh master rồi vẽ ra.
        if (args.Contains("--model")) return PatModel.ChayTrichModel(args);

        // Bài học dò mẫu bản thô: chạy từng bước một để hiểu shape-based từ gốc.
        if (args.Contains("--hoc")) return HocDoMau.Chay(args);

        // Bài từ MLCC có nghiêng lên không: đo góc alpha bằng dò mẫu theo hình dạng.
        if (args.Contains("--nghieng")) return YeaJoungCheckCoiNghieng.Chay(args);

        // Tu kiem engine bang chan ly biet truoc: xoay/doi anh mot luong da biet roi bat Run tim lai.
        if (args.Contains("--tu-kiem")) return PmTuKiem.Chay(args);

        if (args.Contains("--help") || args.Contains("-h")) { InCachDung(); return 0; }

        return MoGiaoDien(args);
    }

    private static int MoGiaoDien(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        var cs = new CuaSoPmAlign();

        // Tham số đầu tiên không phải cờ thì coi là ảnh, mở luôn cho đỡ một lần bấm.
        var anh = args.FirstOrDefault(a => !a.StartsWith('-') && File.Exists(a));
        if (anh != null) cs.Loaded += (_, _) => cs.MoAnhTuNgoai(anh);

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

            Ba nhanh console cu:
              --model [duong-dan]   Trich model tu anh master roi ve ra model_out\
              --hoc [1..4]          Bai hoc tung buoc, ket qua ra hoc_out\
              --nghieng             Do goc alpha ca bo anh OK/NG, ket qua ra nghieng_out\

            Co debug dung chung:
              --no-window   Khong bat cua so, chi ghi anh ra dia
              --no-pause    Khong dung cho bam phim
              --no-debug    Tat sach debug
              --only <ten>  Chi debug buoc co ten chua chuoi nay
            """);
    }
}
