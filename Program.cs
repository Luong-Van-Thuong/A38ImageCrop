namespace A38.ImageCrop;

/// <summary>
/// Cấu hình dùng chung cho cả ba nhánh của branch này.
///
/// Branch hoc_patModel chỉ còn một mục đích: dựng một tool kiểu CogPMAlign của Cognex —
/// đưa vào ảnh, trả ra toạ độ (x, y) và góc θ của con hàng. Nên Program.cs ở đây không
/// còn pipeline nào, nó chỉ là bảng điều phối gọi sang đúng nhánh.
///
/// Mọi thứ cần chỉnh (ảnh master, vùng khoanh, ngưỡng) đều nằm trong ModelCfg /
/// HocCfg / NghiengCfg của từng file, KHÔNG truyền qua dòng lệnh.
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
    public static int Main(string[] args)
    {
        Dbg.Enabled = !args.Contains("--no-debug");
        Dbg.ShowWindow = !args.Contains("--no-window");
        Dbg.Pause = !args.Contains("--no-pause");
        Dbg.SaveFile = true;

        int i = Array.IndexOf(args, "--only");
        if (i >= 0 && i + 1 < args.Length) Dbg.Filter = args[i + 1];

        if (args.Contains("--debug-steps")) Config.SaveDebugStepsInBatch = true;

        // Buoc 1a cua tool do mau: chi trich model tu anh master roi ve ra, khong chay pipeline cat anh.
        //if (args.Contains("--model"))
        //    return PatModel.ChayTrichModel(args);
       PatModel.ChayTrichModel(args);

        // Bai hoc do mau ban tho: chay tung buoc mot de hieu shape-based tu goc.
        //if (args.Contains("--hoc"))
        //    return HocDoMau.Chay(args);
        HocDoMau.Chay(args);

        // Bai tu MLCC co nghieng len khong: do goc alpha bang do mau theo hinh dang.
        //if (args.Contains("--nghieng"))
        //    return YeaJoungCheckCoiNghieng.Chay(args);
        YeaJoungCheckCoiNghieng.Chay(args);

        InCachDung();
        return 1;
    }

    private static void InCachDung()
    {
        Console.WriteLine("""
            Branch hoc_patModel — tool do mau theo hinh dang (kieu CogPMAlign).
            Phai chon mot nhanh:

              --model [duong-dan]   Trich model tu anh master roi ve ra model_out\
                                    Duong dan la file hoac thu muc; bo trong = ModelCfg.AnhMaster
              --hoc [1..4]          Bai hoc tung buoc, ket qua ra hoc_out\  (mac dinh buoc 1)
              --nghieng             Do goc alpha ca bo anh OK/NG, ket qua ra nghieng_out\

            Co debug dung chung:
              --no-window   Khong bat cua so, chi ghi anh ra dia
              --no-pause    Khong dung cho bam phim
              --no-debug    Tat sach debug
              --only <ten>  Chi debug buoc co ten chua chuoi nay
            """);
    }
}
