using OpenCvSharp;
using System.Runtime.InteropServices;

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
        Dbg.Show(mauXam, "train 1. mau da dung thang");

        // --- mặt nạ THÂN (được phép lấy điểm) ---
        using var thanGoc = TaoMatNaThan(anhMau, roi, cfg, log);
        using var thanMau = new Mat();
        Cv2.WarpAffine(thanGoc, thanMau, m, kt0, InterpolationFlags.Nearest, BorderTypes.Constant, Scalar.All(0));

        // Chốt an toàn: mặt nạ thân ăn gần hết mẫu thì train ra một model vài điểm mà không
        // ai nhận ra cho tới lúc Run trả rỗng. Thà bỏ mặt nạ và nói to còn hơn.
        double tiLeThan = Cv2.CountNonZero(thanMau) / (double)(w * h);
        bool daBoThan = cfg.MatNaThan != KieuThan.Khong && tiLeThan < cfg.ThanToiThieu;
        if (daBoThan)
        {
            log?.Invoke($"CANH BAO: mat na than {cfg.MatNaThan} chi giu {tiLeThan:P1} cua mau " +
                        $"(< {cfg.ThanToiThieu:P0}) — DA BO mat na than, train tren toan ROI. " +
                        $"Doi kieu mat na, tat bao loi, hoac dung mask ve tay.");
            thanMau.SetTo(Scalar.All(255));
            tiLeThan = 1.0;
        }
        Dbg.Show(thanMau, "train 2. mat na than (trang = duoc lay diem)");

        // --- mặt nạ CHE (don-care): vùng tự dò + các hình người dùng vẽ ---
        using var cheGoc = TaoMatNaChe(anhMau, roi, cfg, log);
        foreach (var mk in dsMask)
        {
            if (!mk.HopLe) continue;
            var d = mk.Dinh().Select(p => new Point((int)Math.Round(p.X), (int)Math.Round(p.Y))).ToArray();
            Cv2.FillConvexPoly(cheGoc, d, Scalar.All(255));
        }
        using var cheMau = new Mat();
        Cv2.WarpAffine(cheGoc, cheMau, m, kt0, InterpolationFlags.Nearest, BorderTypes.Constant, Scalar.All(0));
        Dbg.Show(cheMau, "train 3. mat na che (trang = don-care)");

        double tiLeChe = Cv2.CountNonZero(cheMau) / (double)(w * h);
        log?.Invoke($"Mat na: than {cfg.MatNaThan}{(daBoThan ? " (DA BO)" : "")} giu {tiLeThan:P1}, " +
                    $"che {cfg.MatNaChe}{(dsMask.Count > 0 ? $" + {dsMask.Count} hinh ve tay" : "")} " +
                    $"phu {tiLeChe:P1} cua mau.");
        if (tiLeChe > 0.5)
            log?.Invoke($"CANH BAO: vung don-care phu {tiLeChe:P0} cua mau — soi anh " +
                        $"'train 3' xem no co dang an vao canh can train khong.");

        var model = new PmModel { Roi = roi, Mask = dsMask, Rong = w, Cao = h };

        for (int muc = 0; muc < Math.Max(1, cfg.SoMuc); muc++)
        {
            double tiLe = 1.0 / (1 << muc);
            var kt = new Size(Math.Max(4, (int)Math.Round(w * tiLe)),
                              Math.Max(4, (int)Math.Round(h * tiLe)));

            using var anh = new Mat();
            Cv2.Resize(mauXam, anh, kt, 0, 0, InterpolationFlags.Area);
            using var care = CareChoMuc(thanMau, cheMau, kt, muc, cfg);

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
    private static Mat CareChoMuc(Mat thanMau, Mat cheMau, Size kt, int muc, PmCfg cfg)
    {
        var care = new Mat();
        Cv2.Resize(thanMau, care, kt, 0, 0, InterpolationFlags.Area);
        Cv2.Threshold(care, care, 127, 255, ThresholdTypes.Binary);

        // Nới thân ra vài px để mặt nạ ôm lấy chính ĐƯỜNG BIÊN của vật. Otsu cắt đúng ở
        // biên, không nới thì cạnh ngoài cùng — cạnh đáng train nhất — rơi ra ngoài care.
        if (cfg.MatNaThan != KieuThan.Khong && cfg.NoiRongThan > 0)
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
        Dbg.Show(care, $"L{muc} care (than − che)");
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
        Dbg.Show(bien, "bien canny");

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

    /// <summary>
    /// Ảnh đích đã chuẩn bị ở một mức: hướng gradient đã chuẩn hoá về độ dài 1.
    ///
    /// Trường gradient KHÔNG nhất thiết phủ cả mức. <see cref="W"/>/<see cref="H"/> là kích
    /// thước đầy đủ của mức (mọi toạ độ trong Run vẫn tính theo hệ đó), còn mảng Ngx/Ngy chỉ
    /// phủ CỬA SỔ <see cref="X0"/>,<see cref="Y0"/>,<see cref="Wc"/>,<see cref="Hc"/>.
    /// Điểm rơi ngoài cửa sổ tính 0, đúng như điểm rơi ngoài ảnh.
    /// </summary>
    private sealed class MucAnh
    {
        public int W, H;
        public int X0, Y0, Wc, Hc;
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

        var dhTong = System.Diagnostics.Stopwatch.StartNew();
        long mocChuanBi = 0, mocTho = 0;

        var vt = vungTim.HopLe
                 ? vungTim
                 : new RectXoay(anh.Width / 2.0, anh.Height / 2.0, anh.Width, anh.Height, 0);

        float r0 = model.Muc[0].BanKinh;
        int le = (int)Math.Ceiling(r0) + 4;
        Rect bao = NoiRong(vt.BaoNgoai(), le, anh.Size());

        // Kéo cạnh patch thành BỘI của 2^(số mức−1). Hai cái lợi, cả hai đều đo được:
        //
        //  • ĐÚNG hơn. Toàn bộ phần tinh chỉnh nhân toạ độ với 2 khi xuống một mức
        //    (`u.X * 2`), tức là ngầm coi mức i có kích thước đúng bằng W/2^i. Nhưng
        //    kích thước thật là round(W/2^i): với patch rộng 1917 thì L5 là 60, mà
        //    60×32 = 1920 ≠ 1917 — mép phải lệch tới 3 px ảnh gốc, và lệch đó phải
        //    nằm gọn trong cửa sổ dò ±3 px mới không mất vật.
        //  • NHANH hơn. Chia hết thì `ChuanBiMucAnh` được phép cắt trước rồi mới thu nhỏ.
        //    Không chia hết thì mọi mức đều phải thu nhỏ CẢ patch: đo trên bộ c1, riêng
        //    khoản đó là 20 ms trong 79 ms.
        //
        // Nới ra chứ không cắt vào, và chỉ nới khi ảnh còn chỗ — thà bỏ tối ưu còn hơn
        // cắt mất một dải mà vật có thể đang nằm ở đó.
        int boi = 1 << Math.Max(0, Math.Min(6, model.Muc.Count - 1));
        bao = LamTronBoi(bao, boi, anh.Size());
        if (bao.Width < 8 || bao.Height < 8) return [];

        // Chuyển xám trên ĐÚNG miếng cần dùng, không phải cả ảnh: trên ảnh 5064² mà vùng
        // tìm chỉ 800×900 thì đây là 3,9 triệu pixel thay vì 25,6 triệu.
        using var catMau = new Mat(anh, bao);
        using var cat = ToXam(catMau);
        Dbg.Show(cat, "anh xam (chi patch)");

        // Mặt nạ "tâm được phép nằm ở đâu", ở độ phân giải gốc của patch.
        using var chophep0 = new Mat(bao.Size, MatType.CV_8UC1, Scalar.All(0));
        var dinh = vt.Dinh()
                     .Select(p => new Point((int)Math.Round(p.X - bao.X), (int)Math.Round(p.Y - bao.Y)))
                     .ToArray();
        Cv2.FillConvexPoly(chophep0, dinh, Scalar.All(255));
        Dbg.Show(chophep0, "chophep0");

      //  Dbg.Show(chophep0, "chophep0");
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
        var msMuc = new long[model.Muc.Count];
        MucAnh LayMuc(int i, Rect? cuaSo = null)
        {
            if (anhMuc[i] is { } san) return san;
            var d = System.Diagnostics.Stopwatch.StartNew();
            var am = ChuanBiMucAnh(cat, model.Muc[i], cfg, cuaSo);
            msMuc[i] = d.ElapsedMilliseconds;
            return anhMuc[i] = am;
        }

        // Hộp bao các ô mà Cham sẽ đọc ở mức con, suy từ danh sách ứng viên của mức CHA.
        // Mỗi ứng viên cần: vị trí ×2, cộng ±3 px dò quanh, cộng bán kính model của mức đó.
        static Rect BaoUngVien(List<(double Diem, double X, double Y, double Goc)> cha, double banKinh)
        {
            double le = banKinh + 5;
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            foreach (var u in cha)
            {
                x0 = Math.Min(x0, u.X * 2 - le); x1 = Math.Max(x1, u.X * 2 + le);
                y0 = Math.Min(y0, u.Y * 2 - le); y1 = Math.Max(y1, u.Y * 2 + le);
            }
            return new Rect((int)Math.Floor(x0), (int)Math.Floor(y0),
                            (int)Math.Ceiling(x1 - x0), (int)Math.Ceiling(y1 - y0));
        }

        mocChuanBi = dhTong.ElapsedMilliseconds;

        // --- Mức thô: quét toàn vùng cho phép × toàn dải góc ---
        double phi = model.GocRoiDo;
        var mTho = model.Muc[mucTho];
        var aTho = LayMuc(mucTho);
        using var chophepTho = MatNaMuc(chophep0, aTho.W, aTho.H);
        Dbg.Show(chophepTho, "img after mat na muc");

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

        // Mỗi góc là một bài toán độc lập, không đụng gì vào nhau ngoài mấy mảng CHỈ ĐỌC.
        // Kết quả gom vào mảng theo chỉ số góc rồi mới nối lại, nên thứ tự ứng viên giống hệt
        // bản một luồng — đo trên bộ c1: 10/10 ảnh ra đúng từng chữ số, quét thô 2176 → 262 ms.
        var theoGoc = new List<(double Diem, double X, double Y, double Goc)>?[soGoc];
        Parallel.For(0, soGoc, ig =>
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
            if (cuaGoc.Count == 0) return;

            theoGoc[ig] = LocTrung(cuaGoc.OrderByDescending(u => u.Diem).Take(cfg.SoUngVienDinh * 4).ToList(), 2.0, 0)
                          .Take(cfg.SoUngVienDinh).ToList();
        });
        foreach (var t in theoGoc) if (t != null) ungVien.AddRange(t);
        if (ungVien.Count == 0) { log?.Invoke("Khong co ung vien nao o muc tho."); return []; }

        // Gộp trùng có xét CẢ GÓC: hai tư thế cùng chỗ nhưng khác góc là hai giả thuyết
        // khác nhau, không được coi là một.
        var giu = LocTrung(ungVien.OrderByDescending(u => u.Diem).ToList(), 2.0, buoc * 0.9)
                  .Take(cfg.SoUngVienDinh).ToList();
        mocTho = dhTong.ElapsedMilliseconds;
        log?.Invoke($"Muc tho L{mucTho}: {aTho.W}x{aTho.H}, {soGoc} goc buoc {buoc:F2}do, " +
                    $"giu {giu.Count} ung vien, cao nhat {giu[0].Diem:F3} " +
                    $"[{mocTho - mocChuanBi} ms quet, {msMuc[mucTho]} ms chuan bi muc]");

        // --- Xuống dần từng mức, mỗi mức chỉ tinh chỉnh quanh ứng viên ---
        for (int muc = mucTho - 1; muc >= 0; muc--)
        {
            var dhMuc = System.Diagnostics.Stopwatch.StartNew();
            var mCon = model.Muc[muc];
            var aCon = LayMuc(muc, BaoUngVien(giu, mCon.BanKinh));
            double buocCon = Math.Clamp(mCon.BuocGocDo, 0.05, 10.0);

            var moiMang = new (double Diem, double X, double Y, double Goc)[giu.Count];
            Parallel.For(0, giu.Count, iu =>
            {
                var u = giu[iu];
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
                moiMang[iu] = (tot, tx, ty, tg);
            });
            var moi = moiMang.ToList();
            buoc = buocCon;
            giu = LocTrung(moi.OrderByDescending(u => u.Diem).ToList(), 4.0, Math.Max(1.0, buocCon))
                  .Take(Math.Max(3, cfg.SoUngVienDinh >> (mucTho - muc))).ToList();
            log?.Invoke($"  Tinh chinh L{muc}: {aCon.W}x{aCon.H}, {mCon.Diem.Length} diem, " +
                        $"{giu.Count} ung vien, cao nhat {giu[0].Diem:F3} " +
                        $"[{dhMuc.ElapsedMilliseconds} ms, trong do {msMuc[muc]} ms chuan bi muc]");
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

        log?.Invoke($"Tong {dhTong.ElapsedMilliseconds} ms " +
                    $"(chuan bi {mocChuanBi} ms, muc tho {mocTho - mocChuanBi} ms, " +
                    $"tinh chinh + noi suy {dhTong.ElapsedMilliseconds - mocTho} ms) " +
                    $"tren patch {bao.Width}x{bao.Height} (le {le} px theo ban kinh model).");

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
            int ix = (int)(px + 0.5) - am.X0, iy = (int)(py + 0.5) - am.Y0;
            if (ix < 0 || iy < 0 || ix >= am.Wc || iy >= am.Hc) continue;

            int o = iy * am.Wc + ix;
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
            int xf = (int)Math.Floor(px), yf = (int)Math.Floor(py);
            int x0 = xf - am.X0, y0 = yf - am.Y0;
            if (x0 < 0 || y0 < 0 || x0 + 1 >= am.Wc || y0 + 1 >= am.Hc) continue;

            double fx = px - xf, fy = py - yf;
            int o = y0 * am.Wc + x0;
            double w00 = (1 - fx) * (1 - fy), w10 = fx * (1 - fy), w01 = (1 - fx) * fy, w11 = fx * fy;

            double gx = am.Ngx[o] * w00 + am.Ngx[o + 1] * w10 + am.Ngx[o + am.Wc] * w01 + am.Ngx[o + am.Wc + 1] * w11;
            double gy = am.Ngy[o] * w00 + am.Ngy[o + 1] * w10 + am.Ngy[o + am.Wc] * w01 + am.Ngy[o + am.Wc + 1] * w11;
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

    /// <summary>
    /// Dung truong huong gradient cua anh chay o mot muc — thu ma <see cref="Cham"/> doc.
    ///
    /// SAN LAY TU MODEL, khong lay tu anh chay. Ban cu dat san bang phan vi 0.85 cua chinh
    /// khung tim kiem: luon giu dung 15% pixel manh nhat, bat ke anh nao. Do la mot HAN NGACH
    /// chu khong phai mot nguong, va no hong o muc tho — o L5 con hang chi chiem 2,5% khung
    /// (22x29 trong 158x158) va canh cua no da bi thu nho 32 lan lam nhoe, nen no thua suat
    /// truoc nen va moi thu khac trong tam anh 5064². Do duoc tren bo cnc2_: san hien tai
    /// 176..231 trong khi luc train chi lay tu 93.2 — siet gap 2..2,5 lan so voi luc chon diem.
    /// Hau qua: dat model vao DUNG cho dung, chi 27/59 diem roi vao pixel con song, va vi
    /// <see cref="Cham"/> tru di muc ngau nhien 2/pi ~ 0.637 nen 27/59 = 0.458 ra diem DUNG
    /// BANG 0 — muc tho mu hoan toan, Run tra ve rong.
    ///
    /// Con mot hong nua: han ngach doi theo khung, nen khoanh vung tim kiem HEP LAI (tuc la
    /// cho tool them thong tin) lai lam san tang tu 176 len 211 va hong nang hon.
    ///
    /// <see cref="MucPm.NguongThap"/> da duoc tinh luc train va da luu trong .pmm.json, chi la
    /// truoc day khong ai doc. Dung no thi train va run cung mot tieu chi, va san het phu thuoc
    /// vao viec nguoi dung khoanh vung to hay nho.
    /// </summary>
    private static MucAnh ChuanBiMucAnh(Mat xam, MucPm mm, PmCfg cfg, Rect? cuaSo = null)
    {
        int muc = mm.Muc;
        double tiLe = mm.TiLe;
        var kt = new Size(Math.Max(8, (int)Math.Round(xam.Width * tiLe)),
                          Math.Max(8, (int)Math.Round(xam.Height * tiLe)));

        // CỬA SỔ — chỉ làm mờ + Sobel + chuẩn hoá trong đúng vùng mà Cham sẽ đọc.
        //
        // Đây là chỗ tốn nhất của cả Run, và trước đây tốn oan gần hết. Ở mức tinh chỉnh chỉ
        // còn 3..20 ứng viên, mỗi ứng viên dò 7×7 vị trí, nên số ô thật sự được đọc ở L0 là
        // cỡ 3 × 49 × 500 điểm ≈ 73 nghìn — trong khi bản cũ dựng đủ 25,6 triệu pixel của ảnh
        // 5064² và cấp hai mảng float 102 MB cho mỗi mức. Đo trên bộ cnc2_den/c1: riêng khoản
        // này là 361 ms trong 531 ms của toàn bộ phần tinh chỉnh; sau khi cắt cửa sổ còn 39 ms.
        //
        // Nới thêm 8 px quanh cửa sổ để nhân làm mờ 5×5 và Sobel 3×3 ở sát mép vẫn cho ra
        // ĐÚNG con số như khi chạy trên cả mức — đã đối chiếu: điểm từng mức khớp từng chữ số.
        var cs = cuaSo is { } c0 ? NoiRong(c0, 8, kt) : new Rect(0, 0, kt.Width, kt.Height);
        if (cs.Width < 8 || cs.Height < 8) cs = new Rect(0, 0, kt.Width, kt.Height);
        bool trong = cs.Width == kt.Width && cs.Height == kt.Height;

        // Thu nhỏ. Khi tỉ lệ chia hết ĐÚNG (5064 → 2532 → 1266 → 633) thì cắt trước rồi mới
        // thu nhỏ, cho ra đúng từng pixel như thu nhỏ cả ảnh rồi mới cắt, mà chỉ phải đọc
        // đúng phần cần. Khi KHÔNG chia hết (L4: 5064/316 = 16,025) thì lưới lấy mẫu sẽ lệch
        // tới nửa pixel, nên vẫn thu nhỏ cả ảnh — thà chậm hơn là lệch.
        int he = 1 << muc;
        bool chiaHet = kt.Width * he == xam.Width && kt.Height * he == xam.Height;

        using var anh = new Mat();
        if (trong || !chiaHet)
        {
            using var anhDay = new Mat();
            if (tiLe < 1.0) Cv2.Resize(xam, anhDay, kt, 0, 0, InterpolationFlags.Area);
            else xam.CopyTo(anhDay);
            Dbg.Show(anhDay, $"L{muc} 1. thu nho 1/{he}");
            if (trong) anhDay.CopyTo(anh); else new Mat(anhDay, cs).CopyTo(anh);
        }
        else
        {
            var csGoc = new Rect(cs.X * he, cs.Y * he, cs.Width * he, cs.Height * he);
            using var mieng = new Mat(xam, csGoc);
            if (he == 1) mieng.CopyTo(anh);
            else Cv2.Resize(mieng, anh, new Size(cs.Width, cs.Height), 0, 0, InterpolationFlags.Area);
            Dbg.Show(anh, $"L{muc} 1. thu nho 1/{he} (chi cua so {cs.Width}x{cs.Height})");
        }

        int k = Math.Max(1, cfg.BlurKernel) | 1;
        using var mo = new Mat();
        Cv2.GaussianBlur(anh, mo, new Size(k, k), 0);
        Dbg.Show(mo, $"L{muc} 2. lam mo k={k}");

        using var dx = new Mat();
        using var dy = new Mat();
        Cv2.Sobel(mo, dx, MatType.CV_32F, 1, 0, 3);
        Cv2.Sobel(mo, dy, MatType.CV_32F, 0, 1, 3);

        // Ban cu hien lai chinh 'mo' o day nen ket qua Sobel chua bao gio nhin thay duoc.
        // Do lon gradient moi la thu can soi: no cho biet muc nay con canh nao du manh.
        if (Dbg.Enabled)
        {
            using var doLon = new Mat();
            Cv2.Magnitude(dx, dy, doLon);
            Dbg.Show(doLon, $"L{muc} 3. do lon gradient (Sobel)");
        }

        dx.GetArray(out float[] gxs);
        dy.GetArray(out float[] gys);

        int n = cs.Width * cs.Height;
        var am = new MucAnh
        {
            W = kt.Width, H = kt.Height,
            X0 = cs.X, Y0 = cs.Y, Wc = cs.Width, Hc = cs.Height,
            Ngx = new float[n], Ngy = new float[n],
        };

        // Sàn = đúng ngưỡng thấp mà Canny đã dùng lúc train mức này, nhân hệ số tinh chỉnh.
        // Lấy ngưỡng THẤP chứ không phải ngưỡng cao: điểm model sinh ra từ Canny có trễ, một
        // điểm hợp lệ chỉ cần mạnh tới ngưỡng thấp là đủ, đòi nó đạt ngưỡng cao là loại oan.
        //
        // Model cũ chưa có trường này thì NguongThap = 0, khi đó rơi về NguongBienToiThieu —
        // rộng rãi nhưng không chết, hơn hẳn việc siết mù như hạn ngạch phân vị cũ.
        double san = Math.Max(cfg.NguongBienToiThieu, mm.NguongThap * cfg.HeSoSanChay);

        // So bằng chuẩn L1 |gx|+|gy|, ĐÚNG như NguongTuTinh lúc train (nó dùng L1 cho khớp
        // với Canny khi L2gradient = false). Bản cũ so ngưỡng đó với chuẩn L2 √(gx²+gy²);
        // L1 nằm giữa L2 và √2·L2 nên chỗ đó tự siết thêm 10..27% mà không ai cố ý.
        // Chuẩn hoá thì vẫn phải chia cho L2, vì hướng cần là vector đơn vị thật.
        //
        // Bỏ luôn histogram và mảng manh[]: ở L0 của ảnh 5064² đó là một lượt quét thừa và
        // 102 MB cấp phát thừa cho mỗi mức.
        for (int o = 0; o < n; o++)
        {
            float gx = gxs[o], gy = gys[o];
            if (MathF.Abs(gx) + MathF.Abs(gy) < san) continue;
            float d = MathF.Sqrt(gx * gx + gy * gy);
            if (d < 1e-6f) continue;
            am.Ngx[o] = gx / d;
            am.Ngy[o] = gy / d;
        }

        if (Dbg.Enabled)
        {
            using var huong = VeHuongGradient(am);
            Dbg.Show(huong, $"L{muc} 4. huong gradient sau san (san={san:F0}, giu {TyLeGiu(am):P1})");
        }

        return am;
    }

    /// <summary>
    /// Ve DUNG thu ma <see cref="Cham"/> doc: truong huong gradient sau khi da cat san.
    /// Den = pixel bi san loai, tuc la voi bo cham diem no khong ton tai. Nhin anh nay la
    /// biet muc do con giu duoc duong vien nao, hay san da an sach mat canh can tim.
    ///
    /// Mau ma hoa huong bang chinh (gx, gy) chu khong qua atan2: mot luot nhan, khong luong
    /// giac. O muc L0 vai trieu pixel thi rieng atan2 da du lam nguoi ta ngo la treo.
    /// </summary>
    private static Mat VeHuongGradient(MucAnh am)
    {
        var buf = new byte[am.W * am.H * 3];
        for (int o = 0; o < am.Ngx.Length; o++)
        {
            float gx = am.Ngx[o], gy = am.Ngy[o];
            if (gx == 0 && gy == 0) continue;                 // bi san loai -> de den
            buf[o * 3 + 0] = 40;                              // B: nen mo de thay pixel con song
            buf[o * 3 + 1] = (byte)(127 + 127 * gy);          // G theo thanh phan doc
            buf[o * 3 + 2] = (byte)(127 + 127 * gx);          // R theo thanh phan ngang
        }

        // Do thang byte[] vao bo nho Mat. KHONG dung Mat.SetArray o day: no doi kieu phan tu
        // khop voi MatType, dua byte[] vao CV_8UC3 la nem "Mat data type is not compatible".
        // Mat vua cap phat luon lien tuc nen Marshal.Copy mot phat la du.
        var ra = new Mat(am.H, am.W, MatType.CV_8UC3);
        Marshal.Copy(buf, 0, ra.Data, buf.Length);
        return ra;
    }

    /// <summary>Ti le pixel song sot qua san - do thang cua nguong phan vi 0.85.</summary>
    private static double TyLeGiu(MucAnh am)
    {
        int song = 0;
        for (int o = 0; o < am.Ngx.Length; o++)
            if (am.Ngx[o] != 0 || am.Ngy[o] != 0) song++;
        return am.Ngx.Length == 0 ? 0 : (double)song / am.Ngx.Length;
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
    //  Mặt nạ tự động
    // ==========================================================================
    //
    // Hai điều đã đo trên D:\images_ (6 dự án) và quyết định hình dạng của mục này:
    //
    //  1. KHÔNG có một công thức nào đúng cho mọi dự án. Cùng "min(B,G,R) + Otsu + bao lồi"
    //     cho ra: 52% trên V2/1240S (đúng), 14.77% trên SIBV/A26 (bao lồi chỉ ôm cái hốc
    //     giữa, cả khung ngoài — nơi có toàn bộ cạnh đáng train — nằm ngoài mặt nạ),
    //     72.8% trên SmartTech/TCut (bao lồi cắt chéo mất góc trên-phải), 96.5–100% trên
    //     Aline/Almus_ (mặt nạ vô nghĩa). Vì vậy kiểu mặt nạ là một LỰA CHỌN của người dùng,
    //     mặc định Khong, chứ không phải một cái công tắc bật/tắt "chế độ thông minh".
    //
    //  2. Mặt nạ phải tính TRONG KHUNG ROI, không phải toàn ảnh. Bản cũ chạy Otsu +
    //     "blob lớn nhất" trên cả tấm 5064²: blob lớn nhất ở đó là vùng nền tối ngoài vòng
    //     đèn hoặc một con hàng khác, chứ không phải con hàng mà người dùng vừa khoanh.
    //     Tính trong khung ROI nới NoiKhungMatNa còn nhanh hơn hàng chục lần: Cv2.Split
    //     một tấm 5064×5064 là ba lần cấp phát 25 MB cho mỗi lần bấm Train.

    /// <summary>
    /// Khung tính mặt nạ: hộp bao của ROI nới thêm theo tỉ lệ cạnh, cắt trong ảnh.
    /// Phải có ít NỀN trong khung thì Otsu mới có hai đỉnh để tách — ROI khít quá thì
    /// Otsu quay ra cắt đôi chính vật.
    /// </summary>
    private static Rect KhungMatNa(RectXoay roi, Size khung, PmCfg cfg)
    {
        int pad = (int)Math.Ceiling(Math.Max(roi.Rong, roi.Cao) * Math.Max(0, cfg.NoiKhungMatNa));
        return NoiRong(roi.BaoNgoai(), pad, khung);
    }

    /// <summary>
    /// Mặt nạ THÂN trên hệ toạ độ ẢNH GỐC — trắng ở nơi được phép sinh điểm model.
    /// Ngoài khung ROI luôn là 0; phần đó dù sao cũng bị warp cắt bỏ.
    /// </summary>
    private static Mat TaoMatNaThan(Mat src, RectXoay roi, PmCfg cfg, Action<string>? log)
    {
        var than = new Mat(src.Size(), MatType.CV_8UC1, Scalar.All(0));
        if (cfg.MatNaThan == KieuThan.Khong) { than.SetTo(Scalar.All(255)); return than; }

        var khung = KhungMatNa(roi, src.Size(), cfg);
        if (khung.Width < 8 || khung.Height < 8) { than.SetTo(Scalar.All(255)); return than; }

        using var cat = new Mat(src, khung);
        using var nen = NenChoOtsu(cat, cfg.MatNaThan);

        // Otsu chọn ngưỡng, và ngưỡng đó được ghi ra nhật ký: khi mặt nạ sai, biết Otsu cắt ở
        // đâu là biết ngay nó tách nhầm vật với nền hay tách nhầm hai phần của chính vật.
        using var bin = new Mat();
        var kieu = cfg.MatNaThan == KieuThan.VatSangNenToi
                   ? ThresholdTypes.Binary : ThresholdTypes.BinaryInv;
        double nguong = Cv2.Threshold(nen, bin, 0, 255, kieu | ThresholdTypes.Otsu);
        Dbg.Show(bin, $"mat na 1. otsu {cfg.MatNaThan} nguong={nguong:F0}");

        Cv2.FindContours(bin, out Point[][] cts, out _, RetrievalModes.External, ContourApproximationModes.ApproxNone);
        if (cts.Length == 0)
        {
            log?.Invoke("CANH BAO: mat na than khong tim duoc blob nao — bo qua mat na than.");
            than.SetTo(Scalar.All(255));
            return than;
        }

        // Blob lớn nhất TRONG KHUNG ROI. Bao lồi là tuỳ chọn: vật lồi thì hull vá được các
        // lỗ do bóng/ánh sáng, vật hình C hay khung rỗng thì hull nuốt luôn cả phần rỗng.
        var ngoai = cts.OrderByDescending(c => Cv2.ContourArea(c)).First();
        var hinh = cfg.BaoLoiThan ? Cv2.ConvexHull(ngoai) : ngoai;

        using var thanCat = new Mat(khung.Size, MatType.CV_8UC1, Scalar.All(0));
        Cv2.DrawContours(thanCat, new[] { hinh }, -1, Scalar.All(255), -1);

        double pBlob = Cv2.CountNonZero(thanCat) / (double)(khung.Width * khung.Height);
        log?.Invoke($"Mat na than: khung {khung.Width}x{khung.Height} quanh ROI, Otsu={nguong:F0}, " +
                    $"{cts.Length} blob, blob lon nhat{(cfg.BaoLoiThan ? " + bao loi" : "")} phu {pBlob:P1} khung.");

        thanCat.CopyTo(new Mat(than, khung));
        return than;
    }

    /// <summary>
    /// Ảnh một kênh đưa cho Otsu. <see cref="KieuThan.ToiNenSang_Min3Kenh"/> dùng min ba kênh
    /// thay ảnh xám vì trên 1240S dây đồng cháy sáng ngang với nền backlight trên ảnh xám —
    /// Otsu trên ảnh xám ăn mất nguyên một cạnh dài, còn min ba kênh thì dây đồng vẫn tối.
    /// </summary>
    private static Mat NenChoOtsu(Mat cat, KieuThan kieu)
    {
        if (kieu != KieuThan.ToiNenSang_Min3Kenh || cat.Channels() < 3) return ToXam(cat);

        var kenh = Cv2.Split(cat);
        var min = new Mat();
        Cv2.Min(kenh[0], kenh[1], min);
        Cv2.Min(min, kenh[2], min);
        foreach (var c in kenh) c.Dispose();
        return min;
    }

    /// <summary>
    /// Mặt nạ CHE (don-care) trên hệ toạ độ ẢNH GỐC. Các hình người dùng vẽ được cộng thêm
    /// ở <see cref="Train"/>, hàm này chỉ lo phần tự dò.
    ///
    /// <see cref="KieuChe.HieuKenhRB"/>: dùng (R − B) chứ không dùng HSV, vì khi đồng cháy
    /// sáng thì độ bão hoà tụt và HSV mất dấu, còn hiệu hai kênh vẫn dương. Bước MỞ là bắt
    /// buộc: chỗ chuyển từ vật đen sang nền trắng luôn có quang sai màu nên (R − B) vọt lên
    /// ngay TRÊN ĐƯỜNG BIÊN; không mở thì don-care xoá mất đúng cái biên quý nhất. Vệt quang
    /// sai rộng 1–3px, cuộn dây rộng hàng chục px, nên mở là tách được.
    /// </summary>
    private static Mat TaoMatNaChe(Mat src, RectXoay roi, PmCfg cfg, Action<string>? log)
    {
        var che = new Mat(src.Size(), MatType.CV_8UC1, Scalar.All(0));
        if (cfg.MatNaChe == KieuChe.Khong) return che;

        if (src.Channels() < 3)
        {
            log?.Invoke("CANH BAO: anh 1 kenh, mat na che theo mau khong dung duoc — bo qua.");
            return che;
        }

        var khung = KhungMatNa(roi, src.Size(), cfg);
        if (khung.Width < 8 || khung.Height < 8) return che;

        using var cat = new Mat(src, khung);
        var kenh = Cv2.Split(cat);
        using var hieu = new Mat();
        Cv2.Subtract(kenh[2], kenh[0], hieu);
        double lech = Cv2.Mean(hieu).Val0;
        foreach (var c in kenh) c.Dispose();

        using var cheCat = new Mat();
        Cv2.Threshold(hieu, cheCat, cfg.NguongDongRB, 255, ThresholdTypes.Binary);
        if (cfg.MoVungDong > 0) Cv2.MorphologyEx(cheCat, cheCat, MorphTypes.Open, Dia(cfg.MoVungDong));
        if (cfg.NoiRongDongChe > 0) Cv2.Dilate(cheCat, cheCat, Dia(cfg.NoiRongDongChe));
        Dbg.Show(cheCat, $"mat na 2. che (R−B) > {cfg.NguongDongRB}");

        double p = Cv2.CountNonZero(cheCat) / (double)(khung.Width * khung.Height);
        log?.Invoke($"Mat na che (R−B > {cfg.NguongDongRB}): phu {p:P1} khung, R−B trung binh {lech:F1}.");

        // Ảnh xám lưu ở 3 kênh thì R − B = 0 khắp nơi: mặt nạ rỗng, vô hại nhưng cũng vô dụng.
        // Nói ra để người dùng khỏi ngồi vặn ngưỡng một buổi.
        if (p < 1e-6)
            log?.Invoke("  (che rong — anh nay khong co thanh phan mau, hoac nguong qua cao)");

        cheCat.CopyTo(new Mat(che, khung));
        return che;
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

    /// <summary>
    /// Kéo cạnh hình chữ nhật lên thành bội của <paramref name="boi"/> bằng cách NỚI RA,
    /// không bao giờ cắt vào. Nới sang phải/xuống trước, hết chỗ thì lùi trái/lên. Nếu cả
    /// khung ảnh cũng không đủ chỗ thì trả lại nguyên hình cũ — mất tối ưu, không mất dữ liệu.
    /// </summary>
    private static Rect LamTronBoi(Rect r, int boi, Size khung)
    {
        if (boi <= 1) return r;

        int w = (r.Width + boi - 1) / boi * boi;
        int h = (r.Height + boi - 1) / boi * boi;
        if (w > khung.Width || h > khung.Height) return r;

        int x = Math.Min(r.X, khung.Width - w);
        int y = Math.Min(r.Y, khung.Height - h);
        return new Rect(Math.Max(0, x), Math.Max(0, y), w, h);
    }

    private static Rect NoiRong(Rect r, int pad, Size khung) =>
        new Rect(r.X - pad, r.Y - pad, r.Width + 2 * pad, r.Height + 2 * pad)
            .Intersect(new Rect(0, 0, khung.Width, khung.Height));
}
