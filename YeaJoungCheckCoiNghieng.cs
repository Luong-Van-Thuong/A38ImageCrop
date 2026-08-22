using OpenCvSharp;

namespace A38.ImageCrop;

/// <summary>
/// Tham số của bài "tụ MLCC có nghiêng lên không".
///
/// Định nghĩa lỗi: con tụ nằm trên bo, camera nhìn gần vuông góc với mặt bo. Khi một
/// đầu tụ bị nhấc lên thì con tụ QUAY TRONG MẶT PHẲNG BO, nên trên ảnh nó hiện ra
/// đúng là một góc xoay. Gọi góc đó là α, đo so với TRỤC DỌC CỦA ẢNH (đồ gá bắt vít
/// cố định nên trục ảnh dùng làm mốc được).
///
/// Vì sao không dùng ngưỡng/contour để tìm con tụ: đã đo và hỏng. Thân gốm quá mịn nên
/// gradient thấp, nó không bao giờ nổi thành một khối liền; cái nổi lên chỉ là hai đốm
/// chói ở đầu bạc. Trong khi thanh ray kim loại và dây đồng lại nét hơn và tương phản
/// hơn con tụ, nên "chọn vùng nét nhất" luôn trỏ nhầm sang chúng. Trên 20 ảnh OK (đáng
/// lẽ α ≈ 0) cách đó trả về rải từ -90° đến +85°.
///
/// Nên ở đây dùng dò mẫu theo HÌNH DẠNG như PatModel: model là danh sách điểm biên kèm
/// hướng gradient đã chuẩn hoá. Đổi phơi sáng làm gradient dài ra ngắn lại nhưng không
/// đổi hướng — đúng chỗ bộ ảnh này cần, vì sáng tối giữa các ảnh chênh nhau rất mạnh.
/// Thêm một cái lợi quyết định: matcher trả về luôn góc khớp, mà góc đó CHÍNH LÀ α.
/// </summary>
public static class NghiengCfg
{
    public const string Goc = @"D:\Images_\JeaYoung\Coil_Check_Co_Khong_Nghieng\ChupNghieng\nghiengLenX";

    /// <summary>Ảnh master để trích model con tụ. Phải là ảnh tụ nằm THẲNG (α = 0).</summary>
    public static string AnhMaster = Goc + @"\OK\Image_20260822132402930.bmp";

    /// <summary>Khung con tụ trên ảnh master, toạ độ ảnh gốc.</summary>
    public static Rect VungTu = new Rect(588, 393, 112, 159);

    /// <summary>
    /// Góc thật của con tụ trên ảnh master, tính bằng độ so với trục dọc ảnh.
    /// Model coi tư thế master là 0, nên α = góc khớp + số này.
    /// </summary>
    public static double GocMasterDo = 0.0;

    /// <summary>
    /// Các bộ ảnh để chạy, kèm nhãn. Khai báo bằng danh sách chứ không suy từ tên thư
    /// mục con, vì cách xếp thư mục còn thay đổi — đổi chỗ ảnh thì sửa đúng chỗ này.
    /// </summary>
    public static (string Nhan, string ThuMuc)[] BoAnh =
    [
        ("OK", Goc + @"\OK"),
        ("NG", Goc + @"\NG"),
    ];

    /// <summary>
    /// Số mức kim tự tháp: L0 (1/1) đến L(SoMuc-1).
    ///
    /// Để 5 mức là hỏng, đã đo: L4 chỉ còn model 8x11 với 9 điểm. Chín điểm không đủ
    /// phân biệt con tụ với bất kỳ vệt sáng nào, nên mức thô chọn bừa, mà các mức dưới
    /// chỉ tinh chỉnh trong ±3 pixel nên không bao giờ kéo về được chỗ đúng — 13/26 ảnh
    /// bám nhầm sang đốm đèn nền. Dừng ở L2 (31x43, 68 điểm) thì mức thô mới đủ thông tin.
    /// </summary>
    public static int SoMuc = 3;

    public static int BlurKernel = 5;

    /// <summary>Hạn ngạch điểm biên ≈ hệ số này nhân chu vi mức đó. Lấy y như PatModel.</summary>
    public static double HeSoMatDoBien = 8.0;

    public static double CannyTiLeThap = 0.4;
    public static int NguongBienToiThieu = 8;

    /// <summary>Khoảng cách tối thiểu giữa hai điểm model, tính bằng pixel CỦA MỨC ĐÓ.</summary>
    public static int KhoangCachDiem = 3;

    public static int SoDiemToiDaMoiMuc = 900;

    /// <summary>Dải góc quét, tính từ tư thế master. Đồ gá cố định nên ±60° là thừa.</summary>
    public static double GocQuetDo = 60.0;

    /// <summary>Số ứng viên giữ lại ở mức thô nhất, rồi thu hẹp dần khi xuống mức mịn.</summary>
    public static int SoUngVienDinh = 40;

    /// <summary>
    /// Điểm khớp tối thiểu để coi là tìm thấy con tụ.
    ///
    /// 0.13 là số đo được chứ không phải chọn bừa: rà tay 26 ảnh thì 22 ảnh bám đúng con
    /// tụ, 4 ảnh bám nhầm (sang đốm đèn hoặc thanh ray). Bốn ảnh nhầm đều có điểm ≤ 0.10,
    /// còn 22 ảnh đúng đều ≥ 0.14 — nên khe hở nằm gọn quanh 0.12.
    ///
    /// Điểm ở đây đã trừ mức ngẫu nhiên 2/π, nên 0 nghĩa là "không hơn gì đoán mò".
    /// </summary>
    public static double DiemToiThieu = 0.13;

    /// <summary>
    /// Ngưỡng α để kết luận NG, tính bằng độ.
    ///
    /// Đo trên bộ ảnh: nhóm OK có |α| lớn nhất 2.9°, nhóm NG có |α| nhỏ nhất 8.6°.
    /// Khe hở 2.9..8.6 nên đặt giữa khe. Có spec của khách thì thay bằng số của spec.
    /// </summary>
    public static double NguongAlphaDo = 5.5;

    public static string ThuMucRa = "nghieng_out";
}

/// <summary>Kết quả dò một ảnh.</summary>
public sealed class KetQuaNghieng
{
    public string Ten = "", Nhan = "", DuongDan = "";
    public bool Thay;
    public double X, Y;
    public double GocDo;        // góc khớp so với tư thế master
    public double Alpha;        // = GocDo + GocMasterDo, tức góc so với trục dọc ảnh
    public double Diem;
}

/// <summary>
/// Bài "tụ có nghiêng lên không": trích model con tụ từ ảnh master, dò trên cả bộ ảnh
/// bằng kim tự tháp (x, y, θ), rồi lấy θ làm α.
/// </summary>
public static class YeaJoungCheckCoiNghieng
{
    /// <summary>Điểm vào khi chạy với cờ --nghieng.</summary>
    public static int Chay(string[] args)
    {
        Directory.CreateDirectory(NghiengCfg.ThuMucRa);

        using var master = Cv2.ImRead(NghiengCfg.AnhMaster, ImreadModes.Color);
        if (master.Empty())
        {
            Console.WriteLine($"Doc khong duoc anh master: {NghiengCfg.AnhMaster}");
            return 1;
        }

        var model = TrichModel(master, NghiengCfg.VungTu);
        InBangModel(model);
        VeModel(master, model);

        var ketQua = new List<KetQuaNghieng>();
        foreach (var (nhan, thuMuc) in NghiengCfg.BoAnh)
        {
            if (!Directory.Exists(thuMuc)) { Console.WriteLine($"Khong co thu muc {thuMuc}"); continue; }

            var anhVao = Directory.GetFiles(thuMuc)
                .Where(p => p.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase)
                         || p.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p);

            foreach (var f in anhVao)
            {
                using var anh = Cv2.ImRead(f, ImreadModes.Color);
                if (anh.Empty()) { Console.WriteLine($"Doc khong duoc: {f}"); continue; }
                var kq = Do(anh, model);
                kq.Ten = Path.GetFileNameWithoutExtension(f);
                kq.Nhan = nhan;
                kq.DuongDan = f;
                ketQua.Add(kq);
                Console.WriteLine($"  {kq.Nhan} {kq.Ten.Replace("Image_20260822", ""),-12} " +
                                  $"alpha={kq.Alpha,6:F1}  diem={kq.Diem:F3}{(kq.Thay ? "" : "  (duoi nguong)")}");
            }
        }

        InBangKetQua(ketQua);
        VeHistogram(ketQua);
        VeBangKiemTra(ketQua, model);
        return 0;
    }

    // ================= TRÍCH MODEL =================

    /// <summary>
    /// Trích model con tụ. Khác PatModel.TrichModel ở một chỗ: không có vùng don't-care.
    /// PatModel phải loại dây đồng vì diện tích cuộn dây đổi theo từng con hàng; còn ở đây
    /// khung đã khoanh sát con tụ nên mọi điểm biên trong khung đều là của con tụ.
    /// </summary>
    public static ModelDoMau TrichModel(Mat master, Rect vung)
    {
        using var xam = ToXam(master);
        var v = NoiRong(vung, 6, master.Size());
        using var cat = new Mat(xam, v);

        var model = new ModelDoMau { TenMaster = Path.GetFileNameWithoutExtension(NghiengCfg.AnhMaster), VungKhoanh = v };
        for (int muc = 0; muc < NghiengCfg.SoMuc; muc++)
        {
            double tiLe = 1.0 / (1 << muc);
            var kt = new Size(Math.Max(8, (int)Math.Round(v.Width * tiLe)),
                              Math.Max(8, (int)Math.Round(v.Height * tiLe)));
            using var anh = new Mat();
            Cv2.Resize(cat, anh, kt, 0, 0, InterpolationFlags.Area);
            model.Muc.Add(TrichMotMuc(anh, muc, tiLe));
        }
        return model;
    }

    private static MucModel TrichMotMuc(Mat anh, int muc, double tiLe)
    {
        int k = NghiengCfg.BlurKernel | 1;
        using var mo = new Mat();
        Cv2.GaussianBlur(anh, mo, new Size(k, k), 0);

        using var dx = new Mat();
        using var dy = new Mat();
        Cv2.Sobel(mo, dx, MatType.CV_32F, 1, 0, 3);
        Cv2.Sobel(mo, dy, MatType.CV_32F, 0, 1, 3);
        using var bien = new Mat();
        Cv2.Magnitude(dx, dy, bien);

        // Ngưỡng Canny tự tính từ phân bố gradient của chính mức này: số pixel gradient
        // mạnh ≈ HeSoMatDoBien × chu vi. Ngưỡng tuyệt đối không dùng được vì độ tương
        // phản giữa các mức và giữa các ảnh chênh nhau nhiều lần.
        int hanNgach = (int)(NghiengCfg.HeSoMatDoBien * (anh.Width + anh.Height));
        double nguongCao = NguongTheoHanNgach(bien, hanNgach);
        double nguongThap = nguongCao * NghiengCfg.CannyTiLeThap;

        using var canny = new Mat();
        Cv2.Canny(mo, canny, nguongThap, nguongCao);

        float cx = anh.Width / 2f, cy = anh.Height / 2f;
        int d = Math.Max(1, NghiengCfg.KhoangCachDiem);
        int gw = (anh.Width + d - 1) / d, gh = (anh.Height + d - 1) / d;
        var oDaDung = new bool[gw * gh];

        var diem = new List<DiemModel>();
        var thuTu = new List<(float manh, int x, int y)>();
        for (int y = 0; y < anh.Height; y++)
            for (int x = 0; x < anh.Width; x++)
                if (canny.At<byte>(y, x) != 0)
                    thuTu.Add((bien.At<float>(y, x), x, y));

        int soPixelBien = thuTu.Count;

        // Ưu tiên điểm gradient mạnh khi phải thưa hoá, để giữ lại cạnh thật.
        foreach (var (manh, x, y) in thuTu.OrderByDescending(t => t.manh))
        {
            if (diem.Count >= NghiengCfg.SoDiemToiDaMoiMuc) break;
            int o = (y / d) * gw + (x / d);
            if (oDaDung[o]) continue;

            float gx = dx.At<float>(y, x), gy = dy.At<float>(y, x);
            float len = MathF.Sqrt(gx * gx + gy * gy);
            if (len < 1e-3f) continue;

            oDaDung[o] = true;
            diem.Add(new DiemModel(x - cx, y - cy, gx / len, gy / len));
        }

        var m = new MucModel
        {
            Muc = muc,
            TiLe = tiLe,
            KichThuoc = anh.Size(),
            Diem = diem.ToArray(),
            NguongThap = nguongThap,
            NguongCao = nguongCao,
            SoPixelBien = soPixelBien,
            SoUngVien = soPixelBien,
        };
        m.BanKinh = diem.Count == 0 ? 0 : diem.Max(p => MathF.Sqrt(p.X * p.X + p.Y * p.Y));

        // Đòn bẩy xoay: xoay dθ làm điểm (x,y) dịch dθ·(-y,x), nhưng chỉ phần chiếu lên
        // gradient mới đổi điểm số. Cạnh thẳng cho đòn bẩy, cung tròn thì không.
        m.DonBayXoay = diem.Count == 0 ? 0
            : diem.Average(p => Math.Abs(p.X * p.Gy - p.Y * p.Gx)) * Math.PI / 180.0;

        return m;
    }

    /// <summary>Giá trị ngưỡng sao cho số pixel có gradient lớn hơn nó ≈ hạn ngạch.</summary>
    private static double NguongTheoHanNgach(Mat bien, int hanNgach)
    {
        int n = bien.Rows * bien.Cols;
        hanNgach = Math.Clamp(hanNgach, 1, (int)(n * 0.20));

        var v = new float[n];
        int i = 0;
        for (int y = 0; y < bien.Rows; y++)
            for (int x = 0; x < bien.Cols; x++)
                v[i++] = bien.At<float>(y, x);

        Array.Sort(v);
        double nguong = v[Math.Max(0, n - hanNgach)];
        return Math.Max(NghiengCfg.NguongBienToiThieu, nguong);
    }

    // ================= DÒ TÌM =================

    /// <summary>Ảnh đích đã chuẩn bị sẵn ở một mức: hướng gradient đã chuẩn hoá.</summary>
    private sealed class AnhMuc
    {
        public int W, H;
        public float[] Ngx = [], Ngy = [];
    }

    private static AnhMuc ChuanBiMuc(Mat xam, double tiLe)
    {
        var kt = new Size(Math.Max(8, (int)Math.Round(xam.Width * tiLe)),
                          Math.Max(8, (int)Math.Round(xam.Height * tiLe)));
        using var anh = new Mat();
        Cv2.Resize(xam, anh, kt, 0, 0, InterpolationFlags.Area);

        int k = NghiengCfg.BlurKernel | 1;
        using var mo = new Mat();
        Cv2.GaussianBlur(anh, mo, new Size(k, k), 0);
        using var dx = new Mat();
        using var dy = new Mat();
        Cv2.Sobel(mo, dx, MatType.CV_32F, 1, 0, 3);
        Cv2.Sobel(mo, dy, MatType.CV_32F, 0, 1, 3);

        int n = kt.Width * kt.Height;
        var am = new AnhMuc { W = kt.Width, H = kt.Height, Ngx = new float[n], Ngy = new float[n] };

        var gxs = new float[n];
        var gys = new float[n];
        var manh = new float[n];
        for (int y = 0; y < kt.Height; y++)
            for (int x = 0; x < kt.Width; x++)
            {
                int o = y * kt.Width + x;
                gxs[o] = dx.At<float>(y, x);
                gys[o] = dy.At<float>(y, x);
                manh[o] = MathF.Sqrt(gxs[o] * gxs[o] + gys[o] * gys[o]);
            }

        // Sàn cho độ lớn gradient, lấy theo PHÂN VỊ của chính ảnh đó.
        //
        // Lấy sàn theo trung bình là sai ở bộ ảnh này: nền tối và phẳng chiếm gần hết
        // khung nên trung bình tụt rất thấp, sàn theo nó cho gần như mọi pixel lọt qua.
        // Chuẩn hoá một gradient nhiễu lên độ dài 1 nghĩa là bơm một hướng ngẫu nhiên
        // vào điểm số, mà điểm ngẫu nhiên của |tích vô hướng| đã là 2/π ≈ 0.64 — nền
        // nhiễu tự nó ăn 0.64 điểm, đỉnh thật chìm nghỉm trong đó.
        var sapXep = (float[])manh.Clone();
        Array.Sort(sapXep);
        double san = Math.Max(4.0, sapXep[(int)(n * 0.85)]);

        for (int o = 0; o < n; o++)
            if (manh[o] >= san) { am.Ngx[o] = gxs[o] / manh[o]; am.Ngy[o] = gys[o] / manh[o]; }

        return am;
    }

    /// <summary>
    /// Điểm khớp của một tư thế. Lấy TRỊ TUYỆT ĐỐI của tích vô hướng nên đảo tương phản
    /// vẫn ăn điểm — cần thiết vì con tụ khi thì sáng hơn nền, khi thì tối hơn nền.
    /// Điểm rơi ra ngoài ảnh tính 0 chứ không bỏ, để tư thế thò ra ngoài biên không được
    /// điểm cao giả nhờ ít điểm.
    /// </summary>
    private static double Cham(AnhMuc am, DiemModel[] diem, double cx, double cy, double cos, double sin)
    {
        if (diem.Length == 0) return 0;
        double tong = 0;
        for (int i = 0; i < diem.Length; i++)
        {
            var p = diem[i];
            double px = cx + p.X * cos - p.Y * sin;
            double py = cy + p.X * sin + p.Y * cos;
            int ix = (int)(px + 0.5), iy = (int)(py + 0.5);
            if (ix < 0 || iy < 0 || ix >= am.W || iy >= am.H) continue;

            int o = iy * am.W + ix;
            double gx = am.Ngx[o], gy = am.Ngy[o];
            if (gx == 0 && gy == 0) continue;

            double mgx = p.Gx * cos - p.Gy * sin;
            double mgy = p.Gx * sin + p.Gy * cos;
            tong += Math.Abs(mgx * gx + mgy * gy);
        }

        // Trừ đi mức ngẫu nhiên rồi kéo giãn về [0,1]. Hai hướng ngẫu nhiên cho
        // E[|cos|] = 2/π ≈ 0.637, nên nếu để nguyên thì mọi tư thế đều ra 0.7 và con số
        // đó không nói lên điều gì — đã đo, cả 26 ảnh đều rơi vào 0.69..0.81 kể cả khi
        // khung bám nhầm sang đốm đèn nền.
        const double NgauNhien = 2.0 / Math.PI;
        double tb = tong / diem.Length;
        return Math.Max(0, (tb - NgauNhien) / (1 - NgauNhien));
    }

    private static double BuocGoc(MucModel m)
        => Math.Clamp(m.BuocGocDo, 0.4, 8.0);

    public static KetQuaNghieng Do(Mat anh, ModelDoMau model)
    {
        using var xam = ToXam(anh);
        int soMuc = model.Muc.Count;

        var mucAnh = new AnhMuc[soMuc];
        for (int i = 0; i < soMuc; i++) mucAnh[i] = ChuanBiMuc(xam, model.Muc[i].TiLe);

        // ---- Mức thô nhất: quét toàn ảnh × toàn dải góc ----
        int mThô = soMuc - 1;
        var mm = model.Muc[mThô];
        var am = mucAnh[mThô];

        double buoc = BuocGoc(mm);
        var gocs = new List<double>();
        for (double g = -NghiengCfg.GocQuetDo; g <= NghiengCfg.GocQuetDo + 1e-9; g += buoc) gocs.Add(g);

        int le = (int)Math.Ceiling(mm.BanKinh * 0.6);
        var ungVien = new List<(double diem, double x, double y, double goc)>();
        foreach (var g in gocs)
        {
            double r = g * Math.PI / 180.0, c = Math.Cos(r), s = Math.Sin(r);
            for (int y = le; y < am.H - le; y++)
                for (int x = le; x < am.W - le; x++)
                {
                    double d = Cham(am, mm.Diem, x, y, c, s);
                    if (d > 0.05) ungVien.Add((d, x, y, g));
                }
        }
        if (ungVien.Count == 0) return new KetQuaNghieng { Thay = false };

        var giu = ungVien.OrderByDescending(u => u.diem).Take(NghiengCfg.SoUngVienDinh * 4).ToList();
        giu = LocTrung(giu, 3.0);
        giu = giu.Take(NghiengCfg.SoUngVienDinh).ToList();

        // ---- Xuống dần từng mức, mỗi mức chỉ tinh chỉnh quanh ứng viên ----
        for (int muc = mThô - 1; muc >= 0; muc--)
        {
            var mCon = model.Muc[muc];
            var aCon = mucAnh[muc];
            double buocCon = BuocGoc(mCon);

            var moi = new List<(double diem, double x, double y, double goc)>();
            foreach (var u in giu)
            {
                double bx = u.x * 2, by = u.y * 2;
                double tot = -1, tx = bx, ty = by, tg = u.goc;
                for (double dg = -buoc; dg <= buoc + 1e-9; dg += buocCon)
                {
                    double g = u.goc + dg;
                    double r = g * Math.PI / 180.0, c = Math.Cos(r), s = Math.Sin(r);
                    for (int dyy = -3; dyy <= 3; dyy++)
                        for (int dxx = -3; dxx <= 3; dxx++)
                        {
                            double x = bx + dxx, y = by + dyy;
                            if (x < 0 || y < 0 || x >= aCon.W || y >= aCon.H) continue;
                            double d = Cham(aCon, mCon.Diem, x, y, c, s);
                            if (d > tot) { tot = d; tx = x; ty = y; tg = g; }
                        }
                }
                moi.Add((tot, tx, ty, tg));
            }
            buoc = buocCon;
            giu = LocTrung(moi.OrderByDescending(u => u.diem).ToList(), 4.0);
            int soGiu = Math.Max(3, NghiengCfg.SoUngVienDinh >> (mThô - muc));
            giu = giu.Take(soGiu).ToList();
        }

        var best = giu[0];
        return new KetQuaNghieng
        {
            Thay = best.diem >= NghiengCfg.DiemToiThieu,
            X = best.x,
            Y = best.y,
            GocDo = best.goc,
            Alpha = best.goc + NghiengCfg.GocMasterDo,
            Diem = best.diem,
        };
    }

    /// <summary>Bỏ các ứng viên trùng chỗ, giữ cái điểm cao nhất.</summary>
    private static List<(double diem, double x, double y, double goc)> LocTrung(
        List<(double diem, double x, double y, double goc)> ds, double banKinh)
    {
        var ra = new List<(double diem, double x, double y, double goc)>();
        foreach (var u in ds)
        {
            bool trung = false;
            foreach (var g in ra)
                if (Math.Abs(g.x - u.x) <= banKinh && Math.Abs(g.y - u.y) <= banKinh) { trung = true; break; }
            if (!trung) ra.Add(u);
        }
        return ra;
    }

    // ================= BÁO CÁO =================

    private static void InBangModel(ModelDoMau model)
    {
        Console.WriteLine($"\nMODEL tu anh master: {model.TenMaster}   khung {model.VungKhoanh}");
        Console.WriteLine("muc   kich thuoc   so diem   ban kinh   don bay   buoc goc");
        foreach (var m in model.Muc)
            Console.WriteLine($"L{m.Muc}  {m.KichThuoc.Width,5}x{m.KichThuoc.Height,-5} {m.Diem.Length,8} " +
                              $"{m.BanKinh,10:F1} {m.DonBayXoay,9:F2} {m.BuocGocDo,10:F2}");
    }

    private static void InBangKetQua(List<KetQuaNghieng> ds)
    {
        Console.WriteLine("\nnhan ten             thay    alpha    diem   ket luan");
        foreach (var k in ds.OrderBy(k => k.Nhan).ThenBy(k => k.Ten))
            Console.WriteLine($"{k.Nhan,-4} {k.Ten.Replace("Image_20260822", ""),-15} " +
                              $"{(k.Thay ? "co" : "KHONG"),-6} {k.Alpha,7:F1} {k.Diem,7:F3}   {KetLuan(k)}");

        foreach (var nhom in ds.GroupBy(k => k.Nhan).OrderBy(g => g.Key))
        {
            var t = nhom.Where(k => k.Thay).Select(k => Math.Abs(k.Alpha)).OrderBy(v => v).ToList();
            if (t.Count == 0) { Console.WriteLine($"\n{nhom.Key}: khong anh nao tim thay"); continue; }
            Console.WriteLine($"\n{nhom.Key}  (doc duoc {t.Count}/{nhom.Count()})  |alpha|: " +
                              $"min={t[0]:F1}  trung vi={t[t.Count / 2]:F1}  max={t[^1]:F1}");
        }

        // Khe hở giữa hai nhóm — đây mới là con số quyết định tool có dùng được không.
        var okMax = ds.Where(k => k.Thay && k.Nhan == "OK").Select(k => Math.Abs(k.Alpha)).DefaultIfEmpty(double.NaN).Max();
        var ngMin = ds.Where(k => k.Thay && k.Nhan == "NG").Select(k => Math.Abs(k.Alpha)).DefaultIfEmpty(double.NaN).Min();
        Console.WriteLine($"\nKHE HO: OK cao nhat = {okMax:F1} do | NG thap nhat = {ngMin:F1} do");
        if (okMax < ngMin)
            Console.WriteLine($"  Tach duoc. Nguong dang dat {NghiengCfg.NguongAlphaDo:F1} do, " +
                              $"le an toan: phia OK {NghiengCfg.NguongAlphaDo - okMax:F1} do, " +
                              $"phia NG {ngMin - NghiengCfg.NguongAlphaDo:F1} do.");
        else
            Console.WriteLine("  CHONG NHAU - dac trung alpha khong tach duoc bo anh nay, dung dat nguong.");

        int dung = ds.Count(k => k.Thay && KetLuan(k) == k.Nhan);
        int khongDoc = ds.Count(k => !k.Thay);
        Console.WriteLine($"  Dung {dung}/{ds.Count - khongDoc} anh doc duoc; {khongDoc} anh khong doc duoc.");
    }

    /// <summary>Kết luận cho một ảnh. Không đọc được thì KHÔNG đoán bừa.</summary>
    private static string KetLuan(KetQuaNghieng k)
        => !k.Thay ? "?" : Math.Abs(k.Alpha) > NghiengCfg.NguongAlphaDo ? "NG" : "OK";

    private static void VeHistogram(List<KetQuaNghieng> ds)
    {
        int W = 1100, H = 520, le = 70;
        var anh = new Mat(new Size(W, H), MatType.CV_8UC3, new Scalar(28, 28, 30));
        Cv2.PutText(anh, "PHAN BO |alpha| (do) - OK xanh, NG do", new Point(le, 34),
                    HersheyFonts.HersheySimplex, 0.7, new Scalar(235, 235, 235), 1, LineTypes.AntiAlias);

        const int soO = 18;
        double max = 45;
        var demOK = new int[soO];
        var demNG = new int[soO];
        foreach (var k in ds)
        {
            if (!k.Thay) continue;
            int o = Math.Clamp((int)(Math.Abs(k.Alpha) / max * soO), 0, soO - 1);
            if (k.Nhan == "OK") demOK[o]++; else demNG[o]++;
        }
        int cao = Math.Max(1, Math.Max(demOK.Max(), demNG.Max()));

        int x0 = le, y0 = H - 70, rongO = (W - 2 * le) / soO;
        for (int i = 0; i < soO; i++)
        {
            int hOK = demOK[i] * (y0 - 70) / cao, hNG = demNG[i] * (y0 - 70) / cao;
            int x = x0 + i * rongO;
            Cv2.Rectangle(anh, new Rect(x + 2, y0 - hOK, rongO / 2 - 3, hOK), new Scalar(90, 210, 90), -1);
            Cv2.Rectangle(anh, new Rect(x + rongO / 2, y0 - hNG, rongO / 2 - 3, hNG), new Scalar(70, 90, 255), -1);
            if (i % 2 == 0)
                Cv2.PutText(anh, $"{i * max / soO:F0}", new Point(x, y0 + 20),
                            HersheyFonts.HersheySimplex, 0.45, new Scalar(160, 160, 160), 1, LineTypes.AntiAlias);
        }
        Cv2.Line(anh, new Point(x0, y0), new Point(W - le, y0), new Scalar(120, 120, 120), 1);
        Cv2.PutText(anh, "|alpha| (do)", new Point(W / 2 - 40, H - 22),
                    HersheyFonts.HersheySimplex, 0.5, new Scalar(180, 180, 180), 1, LineTypes.AntiAlias);

        var f = Path.Combine(NghiengCfg.ThuMucRa, "histogram_alpha.png");
        Cv2.ImWrite(f, anh);
        Console.WriteLine($"\nDa luu histogram: {f}");
    }

    /// <summary>Bảng ảnh để soi bằng mắt xem matcher có bám đúng con tụ không.</summary>
    private static void VeBangKiemTra(List<KetQuaNghieng> ds, ModelDoMau model)
    {
        int TW = 380, TH = 300, cot = 5;
        var sx = ds.OrderBy(k => k.Nhan).ThenBy(k => k.Ten).ToList();
        int hang = (sx.Count + cot - 1) / cot;
        var canvas = new Mat(new Size(TW * cot, TH * hang), MatType.CV_8UC3, new Scalar(18, 18, 20));

        var m0 = model.Muc[0];
        double nua_w = m0.KichThuoc.Width / 2.0, nua_h = m0.KichThuoc.Height / 2.0;

        for (int i = 0; i < sx.Count; i++)
        {
            var k = sx[i];
            using var anh = Cv2.ImRead(k.DuongDan, ImreadModes.Color);
            if (anh.Empty()) continue;

            var mau = k.Nhan == "OK" ? new Scalar(90, 210, 90) : new Scalar(70, 90, 255);
            // Vẽ cả tư thế dưới ngưỡng (màu xám) — không nhìn thấy nó bám đâu thì không
            // biết ngưỡng đang loại đúng hay loại oan.
            {
                var mauKhung = k.Thay ? new Scalar(60, 220, 240) : new Scalar(130, 130, 130);
                double r = k.GocDo * Math.PI / 180.0, c = Math.Cos(r), s = Math.Sin(r);
                var goc4 = new[] { (-nua_w, -nua_h), (nua_w, -nua_h), (nua_w, nua_h), (-nua_w, nua_h) }
                    .Select(p => new Point(k.X + p.Item1 * c - p.Item2 * s, k.Y + p.Item1 * s + p.Item2 * c))
                    .ToArray();
                Cv2.Polylines(anh, new[] { goc4 }, true, mauKhung, 3, LineTypes.AntiAlias);
                // trục dọc của ảnh đi qua tâm khớp = mốc đo alpha
                Cv2.Line(anh, new Point(k.X, k.Y - 130), new Point(k.X, k.Y + 130), new Scalar(255, 255, 255), 2);
                // trục dài của con tụ
                Cv2.Line(anh,
                    new Point(k.X + nua_h * s, k.Y - nua_h * c),
                    new Point(k.X - nua_h * s, k.Y + nua_h * c), new Scalar(60, 120, 255), 2, LineTypes.AntiAlias);
            }

            int ox = (i % cot) * TW, oy = (i / cot) * TH;
            using var nho = new Mat();
            Cv2.Resize(anh, nho, new Size(TW - 12, (int)((TW - 12) * anh.Height / (double)anh.Width)));
            int ch = Math.Min(nho.Height, TH - 54);
            using var cat = new Mat(nho, new Rect(0, 0, nho.Width, ch));
            cat.CopyTo(new Mat(canvas, new Rect(ox + 6, oy + 26, nho.Width, ch)));
            Cv2.Rectangle(canvas, new Rect(ox + 2, oy + 2, TW - 4, TH - 4), mau, 1);
            Cv2.PutText(canvas, $"{k.Nhan} {k.Ten.Replace("Image_20260822", "")}",
                        new Point(ox + 8, oy + 18), HersheyFonts.HersheySimplex, 0.4, mau, 1, LineTypes.AntiAlias);
            Cv2.PutText(canvas, k.Thay ? $"alpha={k.Alpha:F1} do   diem={k.Diem:F3}" : "KHONG THAY TU",
                        new Point(ox + 8, oy + TH - 12), HersheyFonts.HersheySimplex, 0.45,
                        k.Thay ? new Scalar(230, 230, 230) : new Scalar(140, 140, 140), 1, LineTypes.AntiAlias);
        }

        var f = Path.Combine(NghiengCfg.ThuMucRa, "kiem_tra_do.png");
        Cv2.ImWrite(f, canvas);
        Console.WriteLine($"Da luu bang kiem tra: {f}");
    }

    private static void VeModel(Mat master, ModelDoMau model)
    {
        var m = model.Muc[0];
        using var ve = new Mat(master, model.VungKhoanh);
        var to = new Mat();
        Cv2.Resize(ve, to, new Size(ve.Width * 4, ve.Height * 4), 0, 0, InterpolationFlags.Nearest);
        float cx = m.KichThuoc.Width / 2f, cy = m.KichThuoc.Height / 2f;
        foreach (var p in m.Diem)
            Cv2.Circle(to, new Point((p.X + cx) * 4, (p.Y + cy) * 4), 2, new Scalar(60, 220, 240), -1);
        var f = Path.Combine(NghiengCfg.ThuMucRa, "model_L0.png");
        Cv2.ImWrite(f, to);
        to.Dispose();
        Console.WriteLine($"Da luu model: {f}");
    }

    private static Mat ToXam(Mat src)
    {
        if (src.Channels() == 1) return src.Clone();
        var xam = new Mat();
        Cv2.CvtColor(src, xam, src.Channels() == 4 ? ColorConversionCodes.BGRA2GRAY : ColorConversionCodes.BGR2GRAY);
        return xam;
    }

    private static Rect NoiRong(Rect r, int pad, Size khung)
    {
        var v = new Rect(r.X - pad, r.Y - pad, r.Width + 2 * pad, r.Height + 2 * pad);
        int x = Math.Max(0, v.X), y = Math.Max(0, v.Y);
        int w = Math.Min(khung.Width - x, v.Width), h = Math.Min(khung.Height - y, v.Height);
        return new Rect(x, y, w, h);
    }
}
