using OpenCvSharp;

namespace A38.ImageCrop.PmAlign;

/// <summary>
/// ĐO BIÊN — chạy một model trên cả một thư mục ảnh, với NHIỀU cấu hình chấm điểm một lượt,
/// rồi xuất ra CSV cùng những tấm ảnh ghép để mắt người phán xử.
///
/// Vì sao cần một tool riêng: con số đáng theo đuổi KHÔNG phải ngưỡng, mà là KHOẢNG CÁCH
/// giữa đỉnh ĐÚNG và đỉnh SAI cao nhất TRÊN CHÍNH TẤM ẢNH ĐÓ. Ngưỡng của hai tool không so
/// sánh được với nhau — Cognex chạy ở 0.5 không có nghĩa công thức của họ tốt gấp 2.5 lần
/// công thức chạy ở 0.2. Cái quyết định đặt được ngưỡng hay không là biên, không phải ngưỡng.
///
/// Cách đo: mỗi ảnh xin về nhiều kết quả (<see cref="PmCfg.SoKetQua"/>), rồi tách làm hai —
/// kết quả nằm gần CHÂN LÝ là đỉnh đúng, kết quả xa chân lý là đỉnh sai. Biên = (đỉnh đúng
/// thấp nhất toàn bộ) − (đỉnh sai cao nhất toàn bộ). Dương là đặt được ngưỡng, âm là không.
///
/// Chạy:
///   dotnet run -- --do-bien &lt;thu-muc-anh&gt; &lt;model.pmm.json&gt;
///                 [--vt cx,cy,w,h]  khung hẹp để dựng chân lý
///                 [--bo ten1,ten2]  loại vài ảnh khỏi thống kê (chân lý không đáng tin)
///                 [--ra &lt;thu-muc&gt;]
/// </summary>
public static class PmDoBien
{
    /// <summary>Coi là trúng chân lý nếu tâm lệch dưới ngần này pixel.</summary>
    private const double NguongTrung = 20.0;

    /// <summary>Một cấu hình có tên, để bảng kết quả nói được "đổi cái này thì được gì".</summary>
    private sealed record CauHinh(string Ten, string MoTa, Func<PmCfg> Tao);

    /// <summary>Kết quả của một cấu hình trên một ảnh.</summary>
    private sealed record MotAnh(string Ten, KetQuaPm? Top1, KetQuaPm? DinhDung, KetQuaPm? DinhSai, long Ms);

    public static int Chay(string[] args)
    {
        var vt = args.SkipWhile(a => a != "--do-bien").Skip(1).Where(a => !a.StartsWith('-')).ToList();
        if (vt.Count < 2)
        {
            Console.WriteLine("Dung: --do-bien <thu-muc-anh> <model.pmm.json> [--vt cx,cy,w,h] [--bo a.bmp,b.bmp] [--ra <thu-muc>]");
            return 1;
        }

        string thuMuc = vt[0], modelDan = vt[1];
        string raDan = LayCo(args, "--ra") ?? "do_bien_out";
        var boQua = (LayCo(args, "--bo") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                                               .Select(s => s.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(thuMuc)) { Console.WriteLine($"Khong thay thu muc: {thuMuc}"); return 1; }
        if (!File.Exists(modelDan)) { Console.WriteLine($"Khong thay model: {modelDan}"); return 1; }
        Directory.CreateDirectory(raDan);

        var model = PmModel.Nap(modelDan);
        var anhDan = Directory.GetFiles(thuMuc, "*.*", SearchOption.AllDirectories)
                              .Where(f => Config.ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                              .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                              .ToList();
        if (anhDan.Count == 0) { Console.WriteLine("Thu muc khong co anh nao."); return 1; }

        Console.WriteLine($"Model : {modelDan}");
        Console.WriteLine($"        ROI {model.Rong}x{model.Cao}, {model.Muc.Count} muc, " +
                          $"diem/muc [{string.Join(", ", model.Muc.Select(m => m.Diem.Length))}]");
        Console.WriteLine($"        vung tim {(model.VungTim.HopLe ? model.VungTim.ToString() : "CHUA CO — se quet ca anh")}");
        Console.WriteLine($"Anh   : {anhDan.Count} tam trong {thuMuc}" +
                          (boQua.Count > 0 ? $"  (bo {boQua.Count} tam khoi thong ke)" : ""));
        Console.WriteLine();

        var dsCauHinh = new List<CauHinh>
        {
            new("cu",      "ban cu: |cos|, khong NMS, khong dung sai",     () => Nen(bo: true,  nms: false, tol: 0, cl: 0)),
            new("nms",     "+ NMS luc chay + dung sai 2px (van |cos|)",    () => Nen(bo: true,  nms: true,  tol: 2, cl: 0)),
            new("dau",     "+ tich vo huong CO DAU",                       () => Nen(bo: false, nms: true,  tol: 2, cl: 0)),
            new("tol1",    "nhu 'dau', dung sai 1 px",                     () => Nen(bo: false, nms: true,  tol: 1, cl: 0)),
            new("tol3",    "nhu 'dau', dung sai 3 px",                     () => Nen(bo: false, nms: true,  tol: 3, cl: 0)),
            new("tol3+cl", "dung sai 3 px + tru 0.5 x clutter",            () => Nen(bo: false, nms: true,  tol: 3, cl: 0.5)),
            new("tol2+cl", "dung sai 2 px + tru 0.5 x clutter",            () => Nen(bo: false, nms: true,  tol: 2, cl: 0.5)),
            new("muc40",   "dung sai 2 px, muc tho phai co >=40 diem",     () => { var c = Nen(false, true, 2, 0); c.SoDiemToiThieuMoiMuc = 40; return c; }),
        };

        // ---- CHÂN LÝ ----
        //
        // Không dán nhãn tay 36 tấm, mà dùng đúng thứ người dùng nói là chạy được: khoanh
        // vùng tìm THẬT HẸP quanh chỗ con hàng vẫn nằm. Hai điều đã đo được và phải nhớ:
        //
        //   • Chân lý dựng bằng CẤU HÌNH CŨ thì tự nó sai 12/36 ô — nó bám vào các cục thiếc
        //     quanh chip ngay cả khi chip nằm trong khung. Nên chân lý dựng bằng cấu hình mới.
        //   • Vẫn phải SOI ghep_chanly.png. Trên bộ NghiengLenXuong nó đúng 34/36; hai tấm
        //     còn lại (085013996, 090137501) là ảnh vật xoay ~90° với chip bị coil che, chân
        //     lý không đáng tin — đó là lý do có cờ --bo.
        RectXoay? khungHep = null;
        if (LayCo(args, "--vt") is { } sv)
        {
            var p = sv.Split(',');
            if (p.Length == 4 && p.All(t => double.TryParse(t, out _)))
                khungHep = new RectXoay(double.Parse(p[0]), double.Parse(p[1]),
                                        double.Parse(p[2]), double.Parse(p[3]), 0);
        }

        var chanLy = new Dictionary<string, KetQuaPm>();
        if (khungHep is { } kh)
        {
            var cfgCl = Nen(bo: false, nms: true, tol: 2, cl: 0);
            cfgCl.SoKetQua = 1;
            var hangCl = new List<MotAnh>();
            foreach (var dan in anhDan)
            {
                using var anh = Cv2.ImDecode(File.ReadAllBytes(dan), ImreadModes.Color);
                var kq = anh.Empty() ? [] : PmEngine.Run(anh, model, kh, cfgCl);
                var k = kq.Count > 0 ? kq[0] : null;
                string ten = Path.GetFileName(dan);
                if (k != null) chanLy[ten] = k;
                hangCl.Add(new MotAnh(ten, k, null, null, 0));
            }
            VeAnhGhep(anhDan, hangCl, model, boQua, Path.Combine(raDan, "ghep_chanly.png"));
            Console.WriteLine($"Chan ly: khung hep {kh}, do duoc {chanLy.Count}/{anhDan.Count} tam.");
            Console.WriteLine( "         SOI ghep_chanly.png TRUOC khi tin bang duoi day.");
            Console.WriteLine();
        }

        var csv = new List<string> { "cau_hinh;anh;bo_qua;x;y;goc;diem;clutter;lech;diem_dinh_dung;diem_dinh_sai;ms" };

        Console.WriteLine("cau hinh   ms/anh  TOP1 DUNG   dinh DUNG thap nhat   dinh SAI cao nhat   BIEN");
        Console.WriteLine(new string('-', 88));

        foreach (var ch in dsCauHinh)
            DoMotCauHinh(ch, model, anhDan, chanLy, boQua, csv, raDan, "");

        // ---- Lọc điểm theo ĐỘ ỔN ĐỊNH, rồi đo lại bằng đúng thước đo trên ----
        //
        // Model gốc học từ MỘT ảnh nên hơn nửa số điểm nằm trên phần nội thất đổi theo góc
        // nghiêng. Ở đây train lại đúng ROI ấy nhưng bắt từng điểm tự chứng minh trên cả bộ,
        // rồi chạy lại cấu hình tốt nhất trên model đã lọc. Cùng chân lý, cùng vùng tìm,
        // cùng cấu hình chấm điểm — chỉ khác mỗi tập điểm.
        if (args.Contains("--on-dinh") && model.TenAnhMau.Length > 0)
        {
            string? mauDan = anhDan.FirstOrDefault(d => string.Equals(Path.GetFileName(d), model.TenAnhMau,
                                                                      StringComparison.OrdinalIgnoreCase));
            if (mauDan == null)
                Console.WriteLine($"\n--on-dinh: khong thay anh mau '{model.TenAnhMau}' trong thu muc, bo qua.");
            else
            {
                Console.WriteLine($"\n--- Train lai co LOC ON DINH tu {model.TenAnhMau} ---");
                using var anhMau = Cv2.ImDecode(File.ReadAllBytes(mauDan), ImreadModes.Color);
                var them = new List<(string, Mat)>();
                try
                {
                    foreach (var d in anhDan.Where(d => d != mauDan))
                        them.Add((Path.GetFileName(d), Cv2.ImDecode(File.ReadAllBytes(d), ImreadModes.Color)));

                    // Quét vài cặp (tỉ lệ ảnh phải trụ, ngưỡng trụ). Cặp mặc định đầu tiên
                    // (0.6 / 0.5) đo được là QUÁ GẮT trên bộ này — chỉ 3/339 điểm sống sót
                    // nên chốt an toàn phải giữ nguyên model. Cần biết chỗ nào mới lọc thật.
                    (double TiLe, double Nguong)[] cap = [(0.6, 0.5), (0.4, 0.3), (0.3, 0.25), (0.25, 0.2)];

                    foreach (var (tl, ng) in cap)
                    {
                        var cfgTrain = Nen(bo: false, nms: true, tol: 2, cl: 0);
                        cfgTrain.TiLeOnDinh = tl;
                        cfgTrain.NguongTruDiem = ng;

                        var nhat = new List<string>();
                        var moi = PmEngine.TrainOnDinh(anhMau, model.Roi, model.Mask, cfgTrain,
                                                       them, model.VungTim, nhat.Add);
                        moi.TenAnhMau = model.TenAnhMau;
                        string tag = $"od{tl:F2}_{ng:F2}";
                        moi.Luu(Path.Combine(raDan, $"{tag}.pmm.json"));

                        Console.WriteLine($"  ti le {tl:F2}, nguong {ng:F2}  ->  diem/muc [" +
                                          string.Join(", ", moi.Muc.Select(m => m.Diem.Length)) + "]");
                        foreach (var s in nhat.Where(s => s.Contains("->") || s.StartsWith("CANH")))
                            Console.WriteLine("      " + s.Trim());

                        foreach (var ch in dsCauHinh.Where(c => c.Ten is "dau" or "tol3"))
                            DoMotCauHinh(ch, moi, anhDan, chanLy, boQua, csv, raDan, $"{tag}_");
                        Console.WriteLine();
                    }
                }
                finally { foreach (var (_, m) in them) m.Dispose(); }
            }
        }

        File.WriteAllLines(Path.Combine(raDan, "do_bien.csv"), csv);
        Console.WriteLine();
        Console.WriteLine("BIEN duong = co the dat nguong tach sach dung/sai. Am = khong nguong nao tach duoc.");
        Console.WriteLine($"CSV      : {Path.Combine(raDan, "do_bien.csv")}");
        Console.WriteLine($"Anh ghep : {raDan}\\ghep_*.png — o vien do la tam da bi --bo khoi thong ke.");
        return 0;
    }

    /// <summary>
    /// Chạy MỘT cấu hình trên cả bộ ảnh, ghi CSV + ảnh ghép, in một dòng thống kê.
    ///
    /// Tách ra thành hàm vì phần "train lại có lọc ổn định" phải đo bằng ĐÚNG thước đo đó
    /// trên một model khác — hai bản sao của cùng một phép đo là cách chắc chắn nhất để về
    /// sau chúng phân kỳ rồi so hai con số không cùng nghĩa với nhau.
    /// </summary>
    private static void DoMotCauHinh(CauHinh ch, PmModel model, List<string> anhDan,
                                     Dictionary<string, KetQuaPm> chanLy, HashSet<string> boQua,
                                     List<string> csv, string raDan, string tienTo)
    {
        var cfg = ch.Tao();
        var hang = new List<MotAnh>();
        long tongMs = 0;

        foreach (var dan in anhDan)
        {
            string ten = Path.GetFileName(dan);
            using var anh = Cv2.ImDecode(File.ReadAllBytes(dan), ImreadModes.Color);
            if (anh.Empty()) { hang.Add(new MotAnh(ten, null, null, null, 0)); continue; }

            var dh = System.Diagnostics.Stopwatch.StartNew();
            var kq = PmEngine.Run(anh, model, model.VungTim, cfg);
            dh.Stop();
            tongMs += dh.ElapsedMilliseconds;

            // Tách danh sách kết quả làm hai theo chân lý. Đây là chỗ khác biệt so với chỉ
            // nhìn top-1: đỉnh sai cao nhất phải đo TRÊN CHÍNH TẤM ẢNH có vật, chứ không
            // phải mượn từ một tấm khác.
            KetQuaPm? dung = null, sai = null;
            if (chanLy.TryGetValue(ten, out var t))
                foreach (var k in kq)
                {
                    bool gan = Math.Sqrt((k.X - t.X) * (k.X - t.X) + (k.Y - t.Y) * (k.Y - t.Y)) <= NguongTrung;
                    if (gan) dung ??= k; else sai ??= k;
                }

            var top1 = kq.Count > 0 ? kq[0] : null;
            hang.Add(new MotAnh(ten, top1, dung, sai, dh.ElapsedMilliseconds));

            csv.Add($"{tienTo}{ch.Ten};{ten};{(boQua.Contains(ten) ? 1 : 0)};" +
                    (top1 == null ? ";;;;;" : $"{top1.X:F2};{top1.Y:F2};{top1.GocDo:F3};{top1.Diem:F4};{top1.Clutter:F4};") +
                    $"{(top1 != null && chanLy.TryGetValue(ten, out var t2) ? Kc(top1, t2).ToString("F2") : "")};" +
                    $"{dung?.Diem.ToString("F4") ?? ""};{sai?.Diem.ToString("F4") ?? ""};{dh.ElapsedMilliseconds}");
        }

        VeAnhGhep(anhDan, hang, model, boQua, Path.Combine(raDan, $"ghep_{tienTo}{ch.Ten}.png"));

        // ---- Thống kê, ĐÃ BỎ những tấm chân lý không đáng tin ----
        var xet = hang.Where(h => !boQua.Contains(h.Ten) && chanLy.ContainsKey(h.Ten)).ToList();
        if (xet.Count == 0) { Console.WriteLine($"{tienTo}{ch.Ten,-10} (khong co tam nao co chan ly)"); return; }

        int dungTop1 = xet.Count(h => h.Top1 != null && h.DinhDung != null && h.Top1 == h.DinhDung);
        var dsDung = xet.Where(h => h.DinhDung != null).Select(h => h.DinhDung!.Diem).ToList();
        var dsSai = xet.Where(h => h.DinhSai != null).Select(h => h.DinhSai!.Diem).ToList();

        double dMin = dsDung.Count > 0 ? dsDung.Min() : double.NaN;
        double sMax = dsSai.Count > 0 ? dsSai.Max() : double.NaN;
        double bien = dsDung.Count > 0 && dsSai.Count > 0 ? dMin - sMax : double.NaN;

        Console.WriteLine($"{tienTo + ch.Ten,-10} {tongMs / (double)anhDan.Count,6:F0}   {dungTop1,3}/{xet.Count,-8}" +
                          $"{(dsDung.Count > 0 ? $"{dMin:F3} (thay {dsDung.Count}/{xet.Count})" : "—"),-22}" +
                          $"{(dsSai.Count > 0 ? $"{sMax:F3}" : "—"),-20}" +
                          $"{(double.IsNaN(bien) ? "—" : bien.ToString("+0.000;-0.000"))}");
    }

    private static string? LayCo(string[] args, string co)
    {
        int i = Array.IndexOf(args, co);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static double Kc(KetQuaPm a, KetQuaPm b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>Cấu hình nền dùng chung, chỉ khác nhau ở đúng những núm đang được đo.</summary>
    private static PmCfg Nen(bool bo, bool nms, double tol, double cl) => new()
    {
        // Dò phải NHẬN HẾT và trả về NHIỀU đỉnh — đặt ngưỡng hay chỉ lấy top-1 là tự giấu
        // mất đúng con số cần nhìn (đỉnh sai cao nhất).
        DiemToiThieu = 0,
        SoKetQua = 12,

        // Cả dải 360°: bộ ảnh này có vật ở HAI hướng lệch nhau ~90° (soi contact sheet).
        // Chạy -20..20 là tự tay làm hỏng một phần ba số ảnh rồi đo kết quả của chính mình —
        // đã dính đúng lỗi đó ở lần đo đầu, 31/36 tụt xuống 21/36 mà không phải lỗi thuật toán.
        GocTuDo = -180,
        GocDenDo = 180,

        BoQuaChieuTuongPhan = bo,
        NmsLucChay = nms,
        DungSaiPx = tol,
        HeSoClutter = cl,
        LuonDoClutter = true,
        SoDiemToiThieuMoiMuc = 12,
    };

    /// <summary>
    /// Ảnh ghép: mỗi ô là miếng cắt quanh tư thế tìm được, có vẽ khung model và ghi điểm.
    /// Đây mới là thứ phán xử được đúng/sai — CSV chỉ nói tool tự tin đến đâu, không nói
    /// nó tự tin về cái gì.
    /// </summary>
    private static void VeAnhGhep(List<string> anhDan, List<MotAnh> hang, PmModel model,
                                  HashSet<string> boQua, string raDan)
    {
        const int Cot = 6, ORong = 220, OCao = 260;
        int hangSo = (hang.Count + Cot - 1) / Cot;

        using var ghep = new Mat(hangSo * OCao, Cot * ORong, MatType.CV_8UC3, Scalar.All(30));

        for (int i = 0; i < hang.Count; i++)
        {
            var h = hang[i];
            int ox = (i % Cot) * ORong, oy = (i / Cot) * OCao;

            if (h.Top1 == null)
            {
                Cv2.PutText(ghep, "KHONG TIM THAY", new Point(ox + 8, oy + OCao / 2),
                            HersheyFonts.HersheySimplex, 0.45, Scalar.Red, 1);
            }
            else
            {
                var k = h.Top1;
                using var anh = Cv2.ImDecode(File.ReadAllBytes(anhDan[i]), ImreadModes.Color);
                if (!anh.Empty())
                {
                    // Cắt quanh tư thế rồi vẽ khung model vào ĐÚNG chỗ nó nghĩ vật đang nằm.
                    var cat = new Rect((int)Math.Round(k.X) - ORong / 2, (int)Math.Round(k.Y) - OCao / 2,
                                       ORong, OCao);
                    var an = cat & new Rect(0, 0, anh.Width, anh.Height);
                    if (an.Width > 0 && an.Height > 0)
                    {
                        using var mieng = new Mat(anh, an);
                        using var dich = new Mat(ghep, new Rect(ox + (an.X - cat.X), oy + (an.Y - cat.Y),
                                                                an.Width, an.Height));
                        mieng.CopyTo(dich);
                    }

                    var khung = new RectXoay(ox + ORong / 2.0, oy + OCao / 2.0, model.Rong, model.Cao, k.GocMauDo);
                    var d = khung.Dinh().Select(p => new Point((int)Math.Round(p.X), (int)Math.Round(p.Y))).ToArray();
                    Cv2.Polylines(ghep, [d], true, Scalar.LimeGreen, 2);
                }

                Cv2.PutText(ghep, $"{k.Diem:F2} c{k.Clutter:F2}", new Point(ox + 6, oy + 18),
                            HersheyFonts.HersheySimplex, 0.45, Scalar.Yellow, 1);
            }

            Cv2.PutText(ghep, h.Ten.Length > 26 ? h.Ten[^26..] : h.Ten, new Point(ox + 6, oy + OCao - 8),
                        HersheyFonts.HersheySimplex, 0.32, Scalar.White, 1);
            Cv2.Rectangle(ghep, new Rect(ox, oy, ORong, OCao),
                          boQua.Contains(h.Ten) ? Scalar.Red : Scalar.Gray, boQua.Contains(h.Ten) ? 3 : 1);
        }

        Cv2.ImWrite(raDan, ghep);
    }
}
