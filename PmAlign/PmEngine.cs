using OpenCvSharp;

namespace A38.ImageCrop.PmAlign;

/// <summary>
/// Engine dò mẫu theo hình dạng — MỘT bộ code duy nhất cho cả Train lẫn Run.
///
/// Đây là chỗ sửa quan trọng nhất so với branch cũ: <c>PatModel.TrichMotMuc</c> và
/// <c>YeaJoungCheckCoiNghieng.TrichMotMuc</c> là hai bản sao đã phân kỳ (2000 điểm vs 900,
/// có/không don-care). Train một đằng mà Run một nẻo thì điểm số không còn nghĩa gì.
/// Mọi thứ dưới đây dùng chung một hàm trích và một hàm chấm điểm.
///
/// QUY ƯỚC GÓC lấy nguyên của <see cref="RectXoay"/>: y hướng xuống, góc dương quay ngược
/// chiều kim đồng hồ trên màn hình, R(θ) = [[cos, sin], [−sin, cos]].
/// </summary>
public static class PmEngine
{
    // ==========================================================================
    //  TRAIN
    // ==========================================================================

    /// <summary>
    /// Trích model từ ROI XOAY ĐƯỢC trên ảnh mẫu.
    ///
    /// Mạch chính:
    ///   ảnh mẫu ─(warp gỡ góc φ)→ mẫu dựng thẳng
    ///           ─ mặt nạ thân / vùng che cũng warp y hệt
    ///           ─ với từng mức: thu nhỏ → làm mờ → Canny tự chọn ngưỡng → Sobel lấy hướng
    ///                           → thưa điểm cho đều → lưu toạ độ SO VỚI TÂM mẫu.
    ///
    /// Góc φ không bị nướng vào toạ độ điểm mà cất riêng trong <see cref="PmModel.Roi"/>,
    /// nên lúc Run mới quy chiếu ngược được về tư thế trên ảnh mẫu.
    /// </summary>
    public static PmModel Train(Mat anhMau, RectXoay roi, IEnumerable<RectXoay> mask,
                                PmCfg cfg, Action<string>? log = null)
    {
        if (!roi.HopLe) throw new ArgumentException("ROI mau qua nho.");

        int w = Math.Max(8, (int)Math.Round(roi.Rong));
        int h = Math.Max(8, (int)Math.Round(roi.Cao));
        var dsMask = mask?.ToList() ?? [];

        using var m = roi.MaTranVeMau(w, h);
        var kt0 = new Size(w, h);

        using var xam = ToXam(anhMau);
        using var mauXam = new Mat();
        Cv2.WarpAffine(xam, mauXam, m, kt0, InterpolationFlags.Linear, BorderTypes.Replicate);

        // --- mặt nạ THÂN (được phép lấy điểm) ---
        using var thanGoc = cfg.ChiLayTrenThan ? TachThan(anhMau)
                                               : new Mat(anhMau.Size(), MatType.CV_8UC1, Scalar.All(255));
        using var thanMau = new Mat();
        Cv2.WarpAffine(thanGoc, thanMau, m, kt0, InterpolationFlags.Nearest, BorderTypes.Constant, Scalar.All(0));

        // --- mặt nạ CHE (don-care): vùng đồng tự dò + các hình người dùng vẽ ---
        using var cheGoc = cfg.TuCheVungDong && anhMau.Channels() >= 3
                           ? VungDong(anhMau, cfg)
                           : new Mat(anhMau.Size(), MatType.CV_8UC1, Scalar.All(0));
        foreach (var mk in dsMask)
        {
            if (!mk.HopLe) continue;
            var d = mk.Dinh().Select(p => new Point((int)Math.Round(p.X), (int)Math.Round(p.Y))).ToArray();
            Cv2.FillConvexPoly(cheGoc, d, Scalar.All(255));
        }
        using var cheMau = new Mat();
        Cv2.WarpAffine(cheGoc, cheMau, m, kt0, InterpolationFlags.Nearest, BorderTypes.Constant, Scalar.All(0));

        var model = new PmModel { Roi = roi, Mask = dsMask, Rong = w, Cao = h };

        for (int muc = 0; muc < Math.Max(1, cfg.SoMuc); muc++)
        {
            double tiLe = 1.0 / (1 << muc);
            var kt = new Size(Math.Max(4, (int)Math.Round(w * tiLe)),
                              Math.Max(4, (int)Math.Round(h * tiLe)));

            using var anh = new Mat();
            Cv2.Resize(mauXam, anh, kt, 0, 0, InterpolationFlags.Area);
            using var care = CareChoMuc(thanMau, cheMau, kt, cfg);

            var mp = TrichMotMuc(anh, care, muc, tiLe, cfg);
            model.Muc.Add(mp);
            log?.Invoke($"L{muc} 1/{1 << muc}  {kt.Width}x{kt.Height}  {mp.Diem.Length} diem  " +
                        $"r={mp.BanKinh:F1}px  don bay={mp.DonBayXoay:F2}px/do  buoc goc={mp.BuocGocDo:F2}do  " +
                        $"[canny {mp.NguongThap:F0}/{mp.NguongCao:F0}, {mp.SoPixelBien} px bien, " +
                        $"{mp.SoUngVien} trong vung care {Cv2.CountNonZero(care)}]");

            if (kt.Width < 8 || kt.Height < 8) break;
        }

        return model;
    }

    /// <summary>Cắt ra đúng miếng ảnh MÀU mà model đã học, để soi bằng mắt.</summary>
    public static Mat CatMau(Mat anh, RectXoay roi)
    {
        int w = Math.Max(8, (int)Math.Round(roi.Rong));
        int h = Math.Max(8, (int)Math.Round(roi.Cao));
        using var m = roi.MaTranVeMau(w, h);
        var ra = new Mat();
        Cv2.WarpAffine(anh, ra, m, new Size(w, h), InterpolationFlags.Linear, BorderTypes.Replicate);
        return ra;
    }

    /// <summary>
    /// Vùng quan tâm của MỘT mức: thu nhỏ thân và vùng che về kích thước mức đó RỒI MỚI nới,
    /// nên bán kính nới luôn đúng bằng vài pixel ở chính mức đang xét.
    /// </summary>
    private static Mat CareChoMuc(Mat thanMau, Mat cheMau, Size kt, PmCfg cfg)
    {
        var care = new Mat();
        Cv2.Resize(thanMau, care, kt, 0, 0, InterpolationFlags.Area);
        Cv2.Threshold(care, care, 127, 255, ThresholdTypes.Binary);
        if (cfg.ChiLayTrenThan && cfg.NoiRongThan > 0)
            Cv2.Dilate(care, care, Dia(cfg.NoiRongThan));

        using var che = new Mat();
        Cv2.Resize(cheMau, che, kt, 0, 0, InterpolationFlags.Area);
        Cv2.Threshold(che, che, 127, 255, ThresholdTypes.Binary);
        if (Cv2.CountNonZero(che) > 0)
        {
            using var khong = new Mat();
            Cv2.BitwiseNot(che, khong);
            Cv2.BitwiseAnd(care, khong, care);
        }
        return care;
    }

    private static MucPm TrichMotMuc(Mat anh, Mat care, int muc, double tiLe, PmCfg cfg)
    {
        int k = Math.Max(1, cfg.BlurKernel) | 1;
        using var mo = new Mat();
        Cv2.GaussianBlur(anh, mo, new Size(k, k), 0);

        using var dx = new Mat();
        using var dy = new Mat();
        Cv2.Sobel(mo, dx, MatType.CV_32F, 1, 0, 3);
        Cv2.Sobel(mo, dy, MatType.CV_32F, 0, 1, 3);

        care.GetArray(out byte[] aCare);
        dx.GetArray(out float[] aDx);
        dy.GetArray(out float[] aDy);

        var (thap, cao) = NguongTuTinh(aDx, aDy, aCare, anh.Width + anh.Height, cfg);
        using var bien = new Mat();
        Cv2.Canny(mo, bien, thap, cao);
        bien.GetArray(out byte[] aBien);

        int w = anh.Width, h = anh.Height;
        var ungVien = new List<(int Idx, float Mag)>();
        for (int idx = 0; idx < aBien.Length; idx++)
        {
            if (aBien[idx] == 0 || aCare[idx] == 0) continue;
            float gx = aDx[idx], gy = aDy[idx];
            float mag = MathF.Sqrt(gx * gx + gy * gy);
            if (mag < 1e-3f) continue;
            ungVien.Add((idx, mag));
        }
        ungVien.Sort((a, b) => b.Mag.CompareTo(a.Mag));

        // Thưa điểm bằng lưới: mỗi ô chỉ giữ điểm mạnh nhất. Duyệt theo thứ tự mạnh dần
        // xuống nên ô nào cũng được điểm tốt nhất của nó.
        int oLuoi = Math.Max(1, cfg.KhoangCachDiem);
        int cotLuoi = (w + oLuoi - 1) / oLuoi;
        var daChiem = new bool[cotLuoi * ((h + oLuoi - 1) / oLuoi)];

        // Trần là SoDiemToiDa đúng nghĩa số điểm. Bản cũ ở PatModel.cs viết
        //     soDiemMax = ceil(sqrt(w*h / SoDiemToiDa))
        // nên với mẫu 200x200 và SoDiemToiDa = 2000 thì trần thật chỉ là 5 điểm.
        int tran = Math.Max(8, cfg.SoDiemToiDa);

        float cx = w / 2f, cy = h / 2f;
        var diem = new List<DiemModel>();
        float banKinh = 0;

        foreach (var (idx, mag) in ungVien)
        {
            if (diem.Count >= tran) break;
            int x = idx % w, y = idx / w;
            int o = (y / oLuoi) * cotLuoi + (x / oLuoi);
            if (daChiem[o]) continue;
            daChiem[o] = true;

            float gx = aDx[idx] / mag, gy = aDy[idx] / mag;
            float px = x - cx, py = y - cy;
            diem.Add(new DiemModel(px, py, gx, gy));
            banKinh = MathF.Max(banKinh, MathF.Sqrt(px * px + py * py));
        }

        double tongBinh = 0;
        foreach (var d in diem)
        {
            double don = d.X * d.Gy - d.Y * d.Gx;
            tongBinh += don * don;
        }
        double donBay = diem.Count == 0 ? 0 : Math.Sqrt(tongBinh / diem.Count) * Math.PI / 180.0;

        return new MucPm
        {
            Muc = muc,
            TiLe = tiLe,
            Rong = w,
            Cao = h,
            Diem = [.. diem],
            BanKinh = banKinh,
            DonBayXoay = donBay,
            NguongThap = thap,
            NguongCao = cao,
            SoPixelBien = Cv2.CountNonZero(bien),
            SoUngVien = ungVien.Count,
        };
    }

    /// <summary>
    /// Ngưỡng Canny tính từ chính ảnh: hạ dần cho tới khi số pixel gradient mạnh trong vùng
    /// quan tâm đạt <c>HeSoMatDoBien × chuVi</c>. Hạn ngạch theo CHU VI vì biên là đối tượng
    /// một chiều; theo diện tích thì ngưỡng tụt xuống tận vùng nhiễu.
    /// Dùng chuẩn L1 (|gx| + |gy|) cho khớp với Canny khi L2gradient = false.
    /// </summary>
    private static (double Thap, double Cao) NguongTuTinh(float[] aDx, float[] aDy, byte[] aCare,
                                                          int chuVi, PmCfg cfg)
    {
        const int maxNguong = 2048;
        var hist = new int[maxNguong + 1];
        long tong = 0;

        for (int i = 0; i < aCare.Length; i++)
        {
            if (aCare[i] == 0) continue;
            int mg = (int)(MathF.Abs(aDx[i]) + MathF.Abs(aDy[i]));
            if (mg == 0) continue;
            hist[mg > maxNguong ? maxNguong : mg]++;
            tong++;
        }

        long can = Math.Min((long)(tong * cfg.TiLeBienToiDa), (long)(chuVi * cfg.HeSoMatDoBien));
        long lay = 0;
        int muc = maxNguong;
        while (muc > 1 && lay < can) lay += hist[muc--];

        double cao = Math.Max(cfg.NguongBienToiThieu, muc);
        return (cao * cfg.CannyTiLeThap, cao);
    }

    // ==========================================================================
    //  RUN
    // ==========================================================================

    /// <summary>Ảnh đích đã chuẩn bị ở một mức: hướng gradient đã chuẩn hoá về độ dài 1.</summary>
    private sealed class MucAnh
    {
        public int W, H;
        public float[] Ngx = [], Ngy = [];
    }

    /// <summary>
    /// Dò mẫu trên ảnh chạy, trong VÙNG TÌM KIẾM do người dùng khoanh.
    ///
    /// Ngữ nghĩa vùng tìm kiếm lấy đúng của Cognex: nó giới hạn nơi TÂM (origin) của mẫu
    /// được phép nằm, chứ không phải nơi toàn bộ mẫu phải lọt vào. Vì thế patch được cắt
    /// ra phải NỚI THÊM đúng bán kính model — cắt sát mép là vật ở rìa vùng tìm kiếm sẽ
    /// mất một nửa mẫu rồi trượt.
    /// </summary>
    public static List<KetQuaPm> Run(Mat anh, PmModel model, RectXoay vungTim, PmCfg cfg,
                                     Action<string>? log = null)
    {
        if (model.Muc.Count == 0) return [];

        var vt = vungTim.HopLe
                 ? vungTim
                 : new RectXoay(anh.Width / 2.0, anh.Height / 2.0, anh.Width, anh.Height, 0);

        float r0 = model.Muc[0].BanKinh;
        int le = (int)Math.Ceiling(r0) + 4;
        Rect bao = NoiRong(vt.BaoNgoai(), le, anh.Size());
        if (bao.Width < 8 || bao.Height < 8) return [];

        using var xam = ToXam(anh);
        using var cat = new Mat(xam, bao);

        // Mặt nạ "tâm được phép nằm ở đâu", ở độ phân giải gốc của patch.
        using var chophep0 = new Mat(bao.Size, MatType.CV_8UC1, Scalar.All(0));
        var dinh = vt.Dinh()
                     .Select(p => new Point((int)Math.Round(p.X - bao.X), (int)Math.Round(p.Y - bao.Y)))
                     .ToArray();
        Cv2.FillConvexPoly(chophep0, dinh, Scalar.All(255));

        // Mức thô nhất dùng được: đủ điểm model VÀ patch còn đủ to để quét.
        int mucTho = 0;
        for (int i = model.Muc.Count - 1; i >= 0; i--)
        {
            var mm = model.Muc[i];
            if (mm.Diem.Length < cfg.SoDiemToiThieuMoiMuc) continue;
            if (bao.Width * mm.TiLe < 12 || bao.Height * mm.TiLe < 12) continue;
            mucTho = i;
            break;
        }

        var anhMuc = new MucAnh?[model.Muc.Count];
        MucAnh LayMuc(int i) => anhMuc[i] ??= ChuanBiMucAnh(cat, model.Muc[i].TiLe, cfg);

        // --- Mức thô: quét toàn vùng cho phép × toàn dải góc ---
        double phi = model.GocRoiDo;
        var mTho = model.Muc[mucTho];
        var aTho = LayMuc(mucTho);
        using var chophepTho = MatNaMuc(chophep0, aTho.W, aTho.H);
        chophepTho.GetArray(out byte[] aCho);

        double buoc = Math.Clamp(mTho.BuocGocDo, 0.25, 10.0);
        double gTu = phi + Math.Min(cfg.GocTuDo, cfg.GocDenDo);
        double gDen = phi + Math.Max(cfg.GocTuDo, cfg.GocDenDo);
        int soGoc = Math.Max(1, (int)Math.Round((gDen - gTu) / buoc) + 1);
        // Quét trọn 360° thì góc đầu và góc cuối là một, bỏ bớt cái cuối.
        if (gDen - gTu >= 359.999) soGoc = Math.Max(1, (int)Math.Round(360.0 / buoc));

        // Gom ứng viên THEO TỪNG GÓC MỘT, mỗi góc giữ riêng phần tốt nhất của nó.
        //
        // Gom chung rồi mới cắt theo điểm là hỏng: ở mức thô một pixel bằng 2^L pixel ảnh
        // gốc (L5 là 32), nên vài chục tư thế sai góc nhưng gần đúng chỗ có thể chiếm hết
        // suất và đẩy văng giả thuyết GÓC đúng — đo được: góc đúng 23.7° bị loại sạch trong
        // khi mức thô vẫn báo điểm cao nhất 0.927, rồi tinh chỉnh xuống L0 ra 0.000.
        var ungVien = new List<(double Diem, double X, double Y, double Goc)>();

        for (int ig = 0; ig < soGoc; ig++)
        {
            double g = gTu + ig * buoc;
            double r = g * Math.PI / 180.0, c = Math.Cos(r), s = Math.Sin(r);

            var cuaGoc = new List<(double Diem, double X, double Y, double Goc)>();
            for (int y = 0; y < aTho.H; y++)
                for (int x = 0; x < aTho.W; x++)
                {
                    if (aCho[y * aTho.W + x] == 0) continue;
                    double d = Cham(aTho, mTho.Diem, x, y, c, s);
                    if (d <= 0.02) continue;
                    cuaGoc.Add((d, x, y, g));
                }
            if (cuaGoc.Count == 0) continue;

            ungVien.AddRange(
                LocTrung(cuaGoc.OrderByDescending(u => u.Diem).Take(cfg.SoUngVienDinh * 4).ToList(), 2.0, 0)
                .Take(cfg.SoUngVienDinh));
        }
        if (ungVien.Count == 0) { log?.Invoke("Khong co ung vien nao o muc tho."); return []; }

        // Gộp trùng có xét CẢ GÓC: hai tư thế cùng chỗ nhưng khác góc là hai giả thuyết
        // khác nhau, không được coi là một.
        var giu = LocTrung(ungVien.OrderByDescending(u => u.Diem).ToList(), 2.0, buoc * 0.9)
                  .Take(cfg.SoUngVienDinh).ToList();
        log?.Invoke($"Muc tho L{mucTho}: {aTho.W}x{aTho.H}, {soGoc} goc buoc {buoc:F2}do, " +
                    $"giu {giu.Count} ung vien, cao nhat {giu[0].Diem:F3}");

        // --- Xuống dần từng mức, mỗi mức chỉ tinh chỉnh quanh ứng viên ---
        for (int muc = mucTho - 1; muc >= 0; muc--)
        {
            var mCon = model.Muc[muc];
            var aCon = LayMuc(muc);
            double buocCon = Math.Clamp(mCon.BuocGocDo, 0.05, 10.0);

            var moi = new List<(double Diem, double X, double Y, double Goc)>(giu.Count);
            foreach (var u in giu)
            {
                double bx = u.X * 2, by = u.Y * 2;
                double tot = -1, tx = bx, ty = by, tg = u.Goc;
                for (double dg = -buoc; dg <= buoc + 1e-9; dg += buocCon)
                {
                    double g = u.Goc + dg;
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
            giu = LocTrung(moi.OrderByDescending(u => u.Diem).ToList(), 4.0, Math.Max(1.0, buocCon))
                  .Take(Math.Max(3, cfg.SoUngVienDinh >> (mucTho - muc))).ToList();
        }

        // --- Nội suy dưới pixel / dưới bước góc ở mức mịn nhất ---
        var a0 = LayMuc(0);
        var m0 = model.Muc[0];
        var ra = new List<KetQuaPm>();
        int ngoaiVung = 0, duoiNguong = 0;
        double caoNhatBiLoai = 0;

        foreach (var u in giu)
        {
            double x = u.X, y = u.Y, g = u.Goc, d = u.Diem;
            if (cfg.NoiSuyDuoiPixel)
                (x, y, g, d) = NoiSuyDinh(a0, m0.Diem, u.X, u.Y, u.Goc, buoc);

            double gx = bao.X + x, gy = bao.Y + y;
            if (!vt.Chua(gx, gy)) { ngoaiVung++; continue; }   // tâm phải nằm trong vùng tìm kiếm
            if (d < cfg.DiemToiThieu) { duoiNguong++; caoNhatBiLoai = Math.Max(caoNhatBiLoai, d); continue; }

            ra.Add(new KetQuaPm
            {
                X = gx,
                Y = gy,
                GocMauDo = g,
                GocDo = ChuanHoaGocQuanh(g - phi, cfg),
                Diem = d,
            });
        }

        if (ra.Count == 0 && (ngoaiVung > 0 || duoiNguong > 0))
            log?.Invoke($"Loai het: {ngoaiVung} tu the co tam ngoai vung tim kiem, " +
                        $"{duoiNguong} duoi diem toi thieu (cao nhat trong so do {caoNhatBiLoai:F3}).");

        // Gộp trùng lần cuối CHỈ theo vị trí: đến đây các giả thuyết đã hội tụ, hai tư thế
        // cùng chỗ là cùng một vật chứ không còn là hai giả thuyết góc nữa.
        var loc = LocTrung(ra.OrderByDescending(k => k.Diem)
                             .Select(k => (k.Diem, k.X, k.Y, k.GocMauDo)).ToList(),
                           Math.Max(2.0, model.Muc[0].BanKinh * 0.3), 0);

        return loc.Take(Math.Max(1, cfg.SoKetQua))
                  .Select(u => ra.First(k => k.X == u.X && k.Y == u.Y && k.GocMauDo == u.Goc))
                  .ToList();
    }

    /// <summary>Đưa góc về đúng dải mà người dùng đã đặt, thay vì để nó nhảy ra ngoài vì cộng trừ 360.</summary>
    private static double ChuanHoaGocQuanh(double g, PmCfg cfg)
    {
        double lo = Math.Min(cfg.GocTuDo, cfg.GocDenDo);
        while (g < lo - 1e-9) g += 360;
        while (g >= lo + 360 - 1e-9) g -= 360;
        return g;
    }

    /// <summary>
    /// Nội suy parabol trên ba trục độc lập: x, y, và góc. Đỉnh của parabol qua ba điểm
    /// cách đều là δ = ½·(s₋ − s₊) / (s₋ − 2s₀ + s₊). Đây là thứ README cũ ghi là còn
    /// thiếu so với CogPMAlign.
    /// </summary>
    private static (double X, double Y, double Goc, double Diem) NoiSuyDinh(
        MucAnh am, DiemModel[] diem, double x, double y, double g, double buoc)
    {
        double r = g * Math.PI / 180.0, c = Math.Cos(r), s = Math.Sin(r);
        double s0 = Cham(am, diem, x, y, c, s);

        double dx = Dinh1D(Cham(am, diem, x - 1, y, c, s), s0, Cham(am, diem, x + 1, y, c, s));
        double dy = Dinh1D(Cham(am, diem, x, y - 1, c, s), s0, Cham(am, diem, x, y + 1, c, s));

        double h = Math.Max(buoc, 1e-3);
        double rm = (g - h) * Math.PI / 180.0, rp = (g + h) * Math.PI / 180.0;
        double dg = Dinh1D(Cham(am, diem, x, y, Math.Cos(rm), Math.Sin(rm)), s0,
                           Cham(am, diem, x, y, Math.Cos(rp), Math.Sin(rp))) * h;

        double nx = x + dx, ny = y + dy, ng = g + dg;
        double nr = ng * Math.PI / 180.0;
        double nd = ChamNoiSuy(am, diem, nx, ny, Math.Cos(nr), Math.Sin(nr));

        // Nội suy chỉ được phép tinh chỉnh, không được phép làm điểm tụt. Tụt là dấu hiệu
        // đỉnh không phải parabol (hay bị vướng biên), khi đó giữ nguyên kết quả nguyên pixel.
        return nd >= s0 ? (nx, ny, ng, nd) : (x, y, g, s0);
    }

    private static double Dinh1D(double sm, double s0, double sp)
    {
        double den = sm - 2 * s0 + sp;
        if (Math.Abs(den) < 1e-12) return 0;
        return Math.Clamp(0.5 * (sm - sp) / den, -1, 1);
    }

    /// <summary>
    /// Điểm khớp của một tư thế. Lấy TRỊ TUYỆT ĐỐI của tích vô hướng nên đảo tương phản vẫn
    /// ăn điểm. Điểm rơi ra ngoài ảnh tính 0 chứ không bỏ, để tư thế thò ra ngoài biên không
    /// được điểm cao giả nhờ ít điểm.
    ///
    /// Trừ đi mức ngẫu nhiên 2/π ≈ 0.637 rồi kéo giãn về [0,1]: hai hướng ngẫu nhiên cho
    /// E[|cos|] = 2/π, để nguyên thì mọi tư thế đều ra ~0.7 và con số đó không nói lên gì.
    /// </summary>
    private static double Cham(MucAnh am, DiemModel[] diem, double cx, double cy, double c, double s)
    {
        if (diem.Length == 0) return 0;
        double tong = 0;
        for (int i = 0; i < diem.Length; i++)
        {
            var p = diem[i];
            double px = cx + p.X * c + p.Y * s;
            double py = cy - p.X * s + p.Y * c;
            int ix = (int)(px + 0.5), iy = (int)(py + 0.5);
            if (ix < 0 || iy < 0 || ix >= am.W || iy >= am.H) continue;

            int o = iy * am.W + ix;
            double gx = am.Ngx[o], gy = am.Ngy[o];
            if (gx == 0 && gy == 0) continue;

            double mgx = p.Gx * c + p.Gy * s;
            double mgy = -p.Gx * s + p.Gy * c;
            tong += Math.Abs(mgx * gx + mgy * gy);
        }

        const double NgauNhien = 2.0 / Math.PI;
        double tb = tong / diem.Length;
        return Math.Max(0, (tb - NgauNhien) / (1 - NgauNhien));
    }

    /// <summary>Như <see cref="Cham"/> nhưng lấy mẫu song tuyến, dùng cho tư thế dưới pixel.</summary>
    private static double ChamNoiSuy(MucAnh am, DiemModel[] diem, double cx, double cy, double c, double s)
    {
        if (diem.Length == 0) return 0;
        double tong = 0;
        for (int i = 0; i < diem.Length; i++)
        {
            var p = diem[i];
            double px = cx + p.X * c + p.Y * s;
            double py = cy - p.X * s + p.Y * c;
            int x0 = (int)Math.Floor(px), y0 = (int)Math.Floor(py);
            if (x0 < 0 || y0 < 0 || x0 + 1 >= am.W || y0 + 1 >= am.H) continue;

            double fx = px - x0, fy = py - y0;
            int o = y0 * am.W + x0;
            double w00 = (1 - fx) * (1 - fy), w10 = fx * (1 - fy), w01 = (1 - fx) * fy, w11 = fx * fy;

            double gx = am.Ngx[o] * w00 + am.Ngx[o + 1] * w10 + am.Ngx[o + am.W] * w01 + am.Ngx[o + am.W + 1] * w11;
            double gy = am.Ngy[o] * w00 + am.Ngy[o + 1] * w10 + am.Ngy[o + am.W] * w01 + am.Ngy[o + am.W + 1] * w11;
            double len = Math.Sqrt(gx * gx + gy * gy);
            if (len < 1e-6) continue;
            gx /= len; gy /= len;

            double mgx = p.Gx * c + p.Gy * s;
            double mgy = -p.Gx * s + p.Gy * c;
            tong += Math.Abs(mgx * gx + mgy * gy);
        }

        const double NgauNhien = 2.0 / Math.PI;
        return Math.Max(0, (tong / diem.Length - NgauNhien) / (1 - NgauNhien));
    }

    private static MucAnh ChuanBiMucAnh(Mat xam, double tiLe, PmCfg cfg)
    {
        var kt = new Size(Math.Max(8, (int)Math.Round(xam.Width * tiLe)),
                          Math.Max(8, (int)Math.Round(xam.Height * tiLe)));
        using var anh = new Mat();
        Cv2.Resize(xam, anh, kt, 0, 0, InterpolationFlags.Area);

        int k = Math.Max(1, cfg.BlurKernel) | 1;
        using var mo = new Mat();
        Cv2.GaussianBlur(anh, mo, new Size(k, k), 0);
        using var dx = new Mat();
        using var dy = new Mat();
        Cv2.Sobel(mo, dx, MatType.CV_32F, 1, 0, 3);
        Cv2.Sobel(mo, dy, MatType.CV_32F, 0, 1, 3);

        dx.GetArray(out float[] gxs);
        dy.GetArray(out float[] gys);

        int n = kt.Width * kt.Height;
        var am = new MucAnh { W = kt.Width, H = kt.Height, Ngx = new float[n], Ngy = new float[n] };

        // Sàn cho độ lớn gradient, lấy theo PHÂN VỊ của chính ảnh đó.
        //
        // Lấy sàn theo trung bình là sai: nền phẳng chiếm gần hết khung nên trung bình tụt
        // rất thấp, gần như mọi pixel lọt qua. Chuẩn hoá một gradient nhiễu lên độ dài 1 là
        // bơm một hướng ngẫu nhiên vào điểm số, mà nền ngẫu nhiên đã ăn sẵn 2/π ≈ 0.64.
        //
        // Đếm bằng histogram thay vì Array.Sort: bản cũ ở YeaJoung sort nguyên mảng n phần
        // tử mỗi mức, với ảnh L0 vài triệu pixel thì riêng chỗ đó đã hết ngân sách 250ms.
        const int nbin = 2048;
        var hist = new int[nbin + 1];
        var manh = new float[n];
        for (int o = 0; o < n; o++)
        {
            float g = MathF.Sqrt(gxs[o] * gxs[o] + gys[o] * gys[o]);
            manh[o] = g;
            int b = (int)g;
            hist[b > nbin ? nbin : b]++;
        }
        long moc = (long)(n * 0.85);
        long acc = 0;
        int bin = 0;
        while (bin < nbin && acc + hist[bin] < moc) { acc += hist[bin]; bin++; }
        double san = Math.Max(4.0, bin);

        for (int o = 0; o < n; o++)
            if (manh[o] >= san) { am.Ngx[o] = gxs[o] / manh[o]; am.Ngy[o] = gys[o] / manh[o]; }

        return am;
    }

    private static Mat MatNaMuc(Mat mask0, int w, int h)
    {
        var ra = new Mat();
        Cv2.Resize(mask0, ra, new Size(w, h), 0, 0, InterpolationFlags.Nearest);
        return ra;
    }

    /// <summary>
    /// Bỏ các ứng viên trùng nhau, giữ cái điểm cao nhất. Danh sách phải đã sắp giảm dần.
    ///
    /// <paramref name="dungSaiGoc"/> ≤ 0 nghĩa là chỉ xét vị trí (dùng ở bước cuối, khi hai
    /// tư thế cùng chỗ chắc chắn là một vật). Ở mức thô thì PHẢI truyền dung sai góc, nếu
    /// không thì giả thuyết góc đúng bị giả thuyết góc sai đứng gần đó nuốt mất.
    /// </summary>
    private static List<(double Diem, double X, double Y, double Goc)> LocTrung(
        List<(double Diem, double X, double Y, double Goc)> ds, double banKinh, double dungSaiGoc)
    {
        var ra = new List<(double Diem, double X, double Y, double Goc)>();
        foreach (var u in ds)
        {
            bool trung = false;
            foreach (var g in ra)
            {
                if (Math.Abs(g.X - u.X) > banKinh || Math.Abs(g.Y - u.Y) > banKinh) continue;
                if (dungSaiGoc > 0 && Math.Abs(((g.Goc - u.Goc + 540) % 360) - 180) > dungSaiGoc) continue;
                trung = true;
                break;
            }
            if (!trung) ra.Add(u);
        }
        return ra;
    }

    // ==========================================================================
    //  Mặt nạ tự động (chỉ đúng cho bộ ảnh 1240S — nền backlight trắng, vật có dây đồng)
    // ==========================================================================

    /// <summary>
    /// Thân vật: min(B,G,R) + Otsu + blob lớn nhất + bao lồi.
    /// Dùng min ba kênh chứ không dùng ảnh xám, vì dây đồng cháy sáng ngang với nền
    /// backlight trên ảnh xám — Otsu trên ảnh xám ăn mất nguyên một cạnh dài.
    /// </summary>
    private static Mat TachThan(Mat src)
    {
        var than = new Mat(src.Size(), MatType.CV_8UC1, Scalar.All(0));
        if (src.Channels() < 3) { than.SetTo(Scalar.All(255)); return than; }

        var kenh = Cv2.Split(src);
        using var min = new Mat();
        Cv2.Min(kenh[0], kenh[1], min);
        Cv2.Min(min, kenh[2], min);
        foreach (var c in kenh) c.Dispose();

        using var bin = new Mat();
        Cv2.Threshold(min, bin, 0, 255, ThresholdTypes.BinaryInv | ThresholdTypes.Otsu);
        Cv2.FindContours(bin, out Point[][] cts, out _, RetrievalModes.External, ContourApproximationModes.ApproxNone);
        if (cts.Length == 0) { than.SetTo(Scalar.All(255)); return than; }

        var ngoai = cts.OrderByDescending(c => Cv2.ContourArea(c)).First();
        Cv2.DrawContours(than, new[] { Cv2.ConvexHull(ngoai) }, -1, Scalar.All(255), -1);
        return than;
    }

    /// <summary>
    /// Vùng dây đồng ở ảnh gốc. Dùng (R − B) chứ không dùng HSV: khi đồng cháy sáng thì độ
    /// bão hoà tụt và HSV mất dấu, còn hiệu hai kênh vẫn dương.
    ///
    /// Bước MỞ (opening) là bắt buộc: chỗ chuyển từ vật đen sang nền trắng luôn có quang sai
    /// màu nên (R − B) vọt lên ngay TRÊN ĐƯỜNG BIÊN. Không mở thì don-care xoá mất đúng cái
    /// biên quý nhất. Vệt quang sai rộng 1–3px, cuộn dây rộng hàng chục px, nên mở là tách được.
    /// </summary>
    private static Mat VungDong(Mat src, PmCfg cfg)
    {
        var dong = new Mat(src.Size(), MatType.CV_8UC1, Scalar.All(0));
        var kenh = Cv2.Split(src);
        using var hieu = new Mat();
        Cv2.Subtract(kenh[2], kenh[0], hieu);
        foreach (var c in kenh) c.Dispose();

        Cv2.Threshold(hieu, dong, cfg.NguongDongRB, 255, ThresholdTypes.Binary);
        if (cfg.MoVungDong > 0) Cv2.MorphologyEx(dong, dong, MorphTypes.Open, Dia(cfg.MoVungDong));
        if (cfg.NoiRongDongChe > 0) Cv2.Dilate(dong, dong, Dia(cfg.NoiRongDongChe));
        return dong;
    }

    // ==========================================================================
    //  Vặt vãnh
    // ==========================================================================

    private static Mat Dia(int r) =>
        Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(2 * r + 1, 2 * r + 1));

    public static Mat ToXam(Mat src)
    {
        if (src.Channels() == 1) return src.Clone();
        var xam = new Mat();
        Cv2.CvtColor(src, xam, src.Channels() == 4 ? ColorConversionCodes.BGRA2GRAY : ColorConversionCodes.BGR2GRAY);
        return xam;
    }

    private static Rect NoiRong(Rect r, int pad, Size khung) =>
        new Rect(r.X - pad, r.Y - pad, r.Width + 2 * pad, r.Height + 2 * pad)
            .Intersect(new Rect(0, 0, khung.Width, khung.Height));
}
