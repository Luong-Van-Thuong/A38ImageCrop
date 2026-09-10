using OpenCvSharp;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace A38.ImageCrop.PmAlign;

/// <summary>
/// Engine dò mẫu theo hình dạng — một bộ code duy nhất cho cả Train lẫn Run.
///
/// MẠCH CHÍNH
///   Train: ảnh mẫu + ROI xoay → dựng thẳng → kim tự tháp → mỗi mức trích một tập điểm
///          biên có hướng → <see cref="PmModel"/> (lưu ra .pmm.json được).
///   Run:   ảnh chạy + vùng tìm → dựng trường hướng gradient cho từng mức → quét ở mức
///          thô nhất → tinh chỉnh dần xuống L0 → nội suy dưới pixel → <see cref="KetQuaPm"/>.
///
/// RÀNG BUỘC PHẢI GIỮ: Train và Run dùng CHUNG một hàm trích điểm
/// (<see cref="TrichMotMuc"/>) và CHUNG một hàm chấm điểm (<see cref="DongGop"/>). Hai bên
/// mà định nghĩa "biên" hoặc "hướng" khác nhau thì điểm số mất hết ý nghĩa — 0.9 hay 0.3
/// đều không nói lên điều gì về con hàng. Cần đổi cách tính thì đổi ở một chỗ, đừng chép
/// ra bản thứ hai.
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

        // Cất luôn mặt nạ che ở toạ độ mẫu. Không phải để train lại — train xong là xong —
        // mà để lúc Run clutter biết bỏ qua vùng này (xem PmModel.MatNaChe).
        var model = new PmModel { Roi = roi, Mask = dsMask, Rong = w, Cao = h };
        if (tiLeChe > 0)
        {
            cheMau.GetArray(out byte[] aChe);
            var bit = new byte[w * h];
            for (int i = 0; i < bit.Length && i < aChe.Length; i++) bit[i] = (byte)(aChe[i] != 0 ? 1 : 0);
            model.MatNaChe = bit;
        }

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

    /// <summary>
    /// TRAIN NHIỀU ẢNH — train như thường trên ảnh mẫu, rồi bắt model tự chứng minh trên
    /// những ảnh khác và LOẠI những điểm không trụ được.
    ///
    /// Vì sao cần: train một ảnh thường cho ra nhiều điểm hơn chu vi thật của con hàng, tức
    /// một phần lớn điểm nằm TRONG LÒNG vật — trên các vệt phản quang đổi hoàn toàn theo góc
    /// nghiêng. Những điểm ấy hại cả hai đầu: chúng kéo điểm của tư thế ĐÚNG xuống (vì trên
    /// ảnh khác chúng không còn ở đó), và chúng dễ ăn may trên texture y như mọi điểm khác.
    ///
    /// Không cắt theo hình học (kiểu "chỉ giữ silhouette") vì cắt thế cũng sai: con hàng chỉ
    /// còn đường bao thì đúng bằng "một thanh sáng trên nền tối", tức hồ sơ của một vết
    /// xước. Cắt theo ĐO ĐƯỢC: điểm nào có mặt ở đủ nhiều ảnh thì giữ.
    ///
    /// Cách bỏ phiếu: dò model gốc trên từng ảnh phụ để lấy tư thế, rồi hỏi TỪNG điểm một
    /// "ở tư thế này mày có trụ được không". Không cần dán nhãn tay — chân lý là chính tư
    /// thế mà model gốc tìm ra, và nếu tư thế đó sai thì phiếu chỉ loãng chứ không lệch.
    /// </summary>
    public static PmModel TrainOnDinh(Mat anhMau, RectXoay roi, IEnumerable<RectXoay> mask, PmCfg cfg,
                                      IReadOnlyList<(string Ten, Mat Anh)> anhThem, RectXoay vungTim,
                                      Action<string>? log = null)
    {
        var model = Train(anhMau, roi, mask, cfg, log);
        model.VungTim = vungTim;
        if (anhThem == null || anhThem.Count == 0) return model;

        var phieu = model.Muc.Select(m => new int[m.Diem.Length]).ToArray();

        // Dò trên ảnh phụ phải NHẬN HẾT: ở đây mục đích là tìm con hàng để bỏ phiếu, không
        // phải phân loại. Để nguyên ngưỡng và clutter là tự loại mất chính những ảnh khó —
        // tức loại đúng những ảnh mang nhiều thông tin nhất.
        var cfgDo = cfg.Sao();
        cfgDo.DiemToiThieu = 0;
        cfgDo.SoKetQua = 1;
        cfgDo.HeSoClutter = 0;
        cfgDo.ClutterToiDa = 1.0;

        int soDung = 0;
        foreach (var (ten, anh) in anhThem)
        {
            var kq = Run(anh, model, vungTim, cfgDo);
            if (kq.Count == 0) { log?.Invoke($"  {ten}: KHONG TIM THAY — bo qua anh nay."); continue; }

            var k = kq[0];
            BoPhieu(anh, model, k.X, k.Y, k.GocMauDo, cfgDo, phieu);
            soDung++;
            log?.Invoke($"  {ten}: diem {k.Diem:F3}, clutter {k.Clutter:F3}, " +
                        $"tai ({k.X:F1}, {k.Y:F1}) goc {k.GocDo:F2}");
        }

        if (soDung == 0)
        {
            log?.Invoke("CANH BAO: khong do duoc anh phu nao — giu nguyen model mot anh.");
            return model;
        }

        int can = Math.Max(1, (int)Math.Ceiling(cfg.TiLeOnDinh * soDung));
        log?.Invoke($"Loc on dinh: {soDung} anh phu, giu diem tru duoc o >= {can} anh " +
                    $"(nguong tru {cfg.NguongTruDiem:F2}).");

        for (int i = 0; i < model.Muc.Count; i++)
        {
            var mm = model.Muc[i];
            var giu = new List<DiemModel>();
            for (int j = 0; j < mm.Diem.Length; j++)
                if (phieu[i][j] >= can) giu.Add(mm.Diem[j]);

            // Lọc mà còn quá ít điểm thì mức đó thành vô dụng, và tệ hơn là KHÔNG AI BIẾT
            // cho tới lúc Run trả rỗng. Thà giữ nguyên mức đó và nói to.
            int san = Math.Max(8, cfg.SoDiemToiThieuMoiMuc);
            if (giu.Count < san)
            {
                log?.Invoke($"  L{i}: {mm.Diem.Length} -> {giu.Count} diem — QUA IT (< {san}), " +
                            $"GIU NGUYEN muc nay. Ha TiLeOnDinh hoac NguongTruDiem neu muon loc that.");
                continue;
            }

            log?.Invoke($"  L{i}: {mm.Diem.Length} -> {giu.Count} diem " +
                        $"({giu.Count / (double)mm.Diem.Length:P0} tru lai)");
            mm.Diem = [.. giu];
            TinhLaiHinhHoc(mm);
        }

        return model;
    }

    /// <summary>
    /// Ở một tư thế đã biết, hỏi từng điểm model "mày có trụ được trên ảnh này không" và
    /// cộng phiếu. Dùng đúng <see cref="DongGop"/> mà lúc chấm điểm dùng, nên tiêu chí bỏ
    /// phiếu và tiêu chí cho điểm luôn là một.
    /// </summary>
    private static void BoPhieu(Mat anh, PmModel model, double x, double y, double gocMauDo,
                                PmCfg cfg, int[][] phieu)
    {
        int le = (int)Math.Ceiling(model.Muc[0].BanKinh) + 8;
        var bao = NoiRong(new Rect((int)Math.Round(x), (int)Math.Round(y), 1, 1), le, anh.Size());
        int boi = 1 << Math.Max(0, Math.Min(6, model.Muc.Count - 1));
        bao = LamTronBoi(bao, boi, anh.Size());
        if (bao.Width < 8 || bao.Height < 8) return;

        using var catMau = new Mat(anh, bao);
        using var cat = ToXam(catMau);

        double r = gocMauDo * Math.PI / 180.0, c = Math.Cos(r), s = Math.Sin(r);

        for (int i = 0; i < model.Muc.Count; i++)
        {
            var mm = model.Muc[i];
            if (mm.Diem.Length == 0) continue;

            var am = ChuanBiMucAnh(cat, mm, cfg);
            double cx = (x - bao.X) * mm.TiLe, cy = (y - bao.Y) * mm.TiLe;

            for (int j = 0; j < mm.Diem.Length; j++)
                if (DongGop(am, in mm.Diem[j], cx, cy, c, s) >= cfg.NguongTruDiem) phieu[i][j]++;
        }
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

        // Trần đọc thẳng là SỐ ĐIỂM, không phải mật độ hay khoảng cách quy đổi: đặt 2000 thì
        // nhận tối đa 2000 điểm, ở mẫu cỡ nào cũng vậy.
        int tran = Math.Max(8, cfg.SoDiemToiDa);

        float cx = w / 2f, cy = h / 2f;
        var diem = new List<DiemModel>();

        foreach (var (idx, mag) in ungVien)
        {
            if (diem.Count >= tran) break;
            int x = idx % w, y = idx / w;
            int o = (y / oLuoi) * cotLuoi + (x / oLuoi);
            if (daChiem[o]) continue;
            daChiem[o] = true;

            float gx = aDx[idx] / mag, gy = aDy[idx] / mag;
            diem.Add(new DiemModel(x - cx, y - cy, gx, gy));
        }

        var mp = new MucPm
        {
            Muc = muc,
            TiLe = tiLe,
            Rong = w,
            Cao = h,
            Diem = [.. diem],
            NguongThap = thap,
            NguongCao = cao,
            SoPixelBien = Cv2.CountNonZero(bien),
            SoUngVien = ungVien.Count,
        };
        TinhLaiHinhHoc(mp);
        return mp;
    }

    /// <summary>
    /// Tính lại bán kính và đòn bẩy xoay TỪ tập điểm hiện có.
    ///
    /// Có hàm riêng vì train-nhiều-ảnh loại bớt điểm sau khi đã trích: bỏ điểm mà quên tính
    /// lại hai con số này thì bước góc và lề cắt patch vẫn theo model cũ — sai âm thầm.
    /// </summary>
    private static void TinhLaiHinhHoc(MucPm mp) 
    {
        float banKinh = 0;
        double tongBinh = 0;
        foreach (var d in mp.Diem)
        {
            banKinh = MathF.Max(banKinh, MathF.Sqrt(d.X * d.X + d.Y * d.Y));
            double don = d.X * d.Gy - d.Y * d.Gx;
            tongBinh += don * don;
        }
        mp.BanKinh = banKinh;
        mp.DonBayXoay = mp.Diem.Length == 0 ? 0 : Math.Sqrt(tongBinh / mp.Diem.Length) * Math.PI / 180.0;
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

        /// <summary>
        /// Hướng của biên GẦN NHẤT, đã nhân trọng số theo khoảng cách tới biên đó.
        /// Khi <see cref="PmCfg.DungSaiPx"/> = 0 thì không có trải: mỗi ô mang đúng hướng
        /// gradient của chính pixel đó, độ dài 1.
        /// </summary>
        public float[] Ngx = [], Ngy = [];

        /// <summary>1 = pixel này CHÍNH LÀ biên. Dùng để đếm clutter, không dùng khi chấm điểm.</summary>
        public byte[] LaBien = [];

        /// <summary>Tổng số pixel biên trong cửa sổ.</summary>
        public int SoBien;

        /// <summary>Lấy |tích vô hướng| thay vì tích có dấu — xem PmCfg.BoQuaChieuTuongPhan.</summary>
        public bool BoDau;

        /// <summary>Ngx/Ngy đã có trọng số ⇒ nội suy xong KHÔNG được chuẩn hoá lại về độ dài 1.</summary>
        public bool CoTrongSo;

        /// <summary>
        /// Điểm mà một tư thế NGẪU NHIÊN đạt được — thứ phải trừ đi để con số nói lên gì đó.
        ///
        /// Giá trị phụ thuộc cấu hình chấm điểm, nên nó là một trường chứ không phải hằng số:
        ///  • Bỏ dấu + không NMS + không dung sai ⇒ mọi pixel đều có hướng, góc lệch phân bố
        ///    đều, nên nền = E[|cos|] = 2/π ≈ 0.637.
        ///  • Có NMS ⇒ phần lớn điểm model rơi vào chỗ trống và đóng góp 0; có dung sai ⇒
        ///    đóng góp còn bị nhân w &lt; 1. Trừ 0.637 ở đây là trừ oan, mọi thứ về 0 sạch,
        ///    nên nền = 0.
        /// </summary>
        public double Nen;
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

        // Kéo cạnh patch thành BỘI của 2^(số mức−1). Hai cái lợi:
        //
        //  • ĐÚNG hơn. Toàn bộ phần tinh chỉnh nhân toạ độ với 2 khi xuống một mức
        //    (`u.X * 2`), tức là ngầm coi mức i có kích thước đúng bằng W/2^i. Nhưng
        //    kích thước thật là round(W/2^i): với patch rộng 1917 thì L5 là 60, mà
        //    60×32 = 1920 ≠ 1917 — mép phải lệch tới 3 px ảnh gốc, và lệch đó phải
        //    nằm gọn trong cửa sổ dò ±3 px mới không mất vật.
        //  • NHANH hơn. Chia hết thì `ChuanBiMucAnh` được phép cắt trước rồi mới thu nhỏ;
        //    không chia hết thì mọi mức đều phải thu nhỏ CẢ patch.
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
        // gốc (L5 là 32), nên vài chục tư thế sai góc nhưng gần đúng chỗ chiếm hết suất và
        // đẩy văng giả thuyết GÓC đúng. Triệu chứng khó lần: mức thô vẫn báo điểm cao (0.9+)
        // mà tinh chỉnh xuống L0 lại ra 0.000, vì giả thuyết sống sót không phải giả thuyết
        // đúng. Điểm cao ở mức thô KHÔNG có nghĩa là mọi thứ ổn.
        var ungVien = new List<(double Diem, double X, double Y, double Goc)>();

        // Mỗi góc là một bài toán độc lập, không đụng gì vào nhau ngoài mấy mảng CHỈ ĐỌC.
        // Kết quả gom vào mảng THEO CHỈ SỐ GÓC rồi mới nối lại, nên thứ tự ứng viên không phụ
        // thuộc thứ tự luồng chạy xong: cùng đầu vào luôn cho cùng đầu ra, từng chữ số.
        // Giữ tính chất này khi sửa — mất nó là mất khả năng tái lập một ca lỗi.
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
        int ngoaiVung = 0, duoiNguong = 0, quaClutter = 0;
        double caoNhatBiLoai = 0;

        // Chỉ dựng lưới khi thật sự có ai dùng tới clutter — dựng thừa là cấp phát thừa.
        bool canClutter = cfg.HeSoClutter > 0 || cfg.ClutterToiDa < 1.0 || cfg.LuonDoClutter || Dbg.Enabled;
        var luoi = canClutter
            ? DungLuoiModel(m0, cfg.DungSaiPx, cfg.CheKhongTinhClutter ? model.MatNaChe : [])
            : null;
        if (luoi is { SoBoQua: > 0 })
            log?.Invoke($"  Clutter bo qua vung che: {luoi.SoBoQua} / {luoi.W * luoi.H} o cua dau chan " +
                        $"({(double)luoi.SoBoQua / (luoi.W * luoi.H):P1}).");

        foreach (var u in giu)
        {
            double x = u.X, y = u.Y, g = u.Goc, d = u.Diem;
            if (cfg.NoiSuyDuoiPixel)
                (x, y, g, d) = NoiSuyDinh(a0, m0.Diem, u.X, u.Y, u.Goc, buoc);

            double gx = bao.X + x, gy = bao.Y + y;
            if (!vt.Chua(gx, gy)) { ngoaiVung++; continue; }   // tâm phải nằm trong vùng tìm kiếm
            if (d < cfg.DiemToiThieu) { duoiNguong++; caoNhatBiLoai = Math.Max(caoNhatBiLoai, d); continue; }

            double clutter = 0;
            if (luoi != null)
            {
                double rg = g * Math.PI / 180.0;
                clutter = DoClutter(a0, luoi, x, y, Math.Cos(rg), Math.Sin(rg));
                if (clutter > cfg.ClutterToiDa) { quaClutter++; continue; }
            }

            ra.Add(new KetQuaPm
            {
                X = gx,
                Y = gy,
                GocMauDo = g,
                GocDo = ChuanHoaGocQuanh(g - phi, cfg),
                Diem = d,
                Clutter = clutter,
                DiemXep = d - cfg.HeSoClutter * clutter,
            });
        }

        if (ra.Count == 0 && (ngoaiVung > 0 || duoiNguong > 0 || quaClutter > 0))
            log?.Invoke($"Loai het: {ngoaiVung} tu the co tam ngoai vung tim kiem, " +
                        $"{duoiNguong} duoi diem toi thieu (cao nhat trong so do {caoNhatBiLoai:F3}), " +
                        $"{quaClutter} vuot clutter toi da {cfg.ClutterToiDa:F2}.");

        // Gộp trùng lần cuối CHỈ theo vị trí: đến đây các giả thuyết đã hội tụ, hai tư thế
        // cùng chỗ là cùng một vật chứ không còn là hai giả thuyết góc nữa.
        //
        // Xếp theo DiemXep (đã trừ clutter) chứ không theo Diem: nếu clutter không được phép
        // đổi thứ hạng thì nó chỉ là một con số trang trí. Với HeSoClutter = 0 — mặc định —
        // thì DiemXep = Diem, tức clutter chỉ được đo và báo ra chứ không can thiệp.
        var loc = LocTrung(ra.OrderByDescending(k => k.DiemXep)
                             .Select(k => (k.DiemXep, k.X, k.Y, k.GocMauDo)).ToList(),
                           Math.Max(2.0, model.Muc[0].BanKinh * 0.3), 0);

        log?.Invoke($"Tong {dhTong.ElapsedMilliseconds} ms " +
                    $"(chuan bi {mocChuanBi} ms, muc tho {mocTho - mocChuanBi} ms, " +
                    $"tinh chinh + noi suy {dhTong.ElapsedMilliseconds - mocTho} ms) " +
                    $"tren patch {bao.Width}x{bao.Height} (le {le} px theo ban kinh model).");

        return loc.Take(Math.Max(1, cfg.SoKetQua))
                  .Select(u => ra.First(k => k.X == u.X && k.Y == u.Y && k.GocMauDo == u.Goc))
                  .ToList();
    }

    /// <summary>
    /// Lưới "chỗ này model CÓ điểm không", trong hệ toạ độ MẪU đã dựng thẳng, đã nới sẵn
    /// bán kính dung sai. Dựng một lần cho mỗi lần Run, tra O(1) cho từng pixel biên.
    /// </summary>
    private sealed class LuoiModel
    {
        public int W, H, Ox, Oy;
        public byte[] Co = [];

        /// <summary>1 = pixel don't-care, clutter không được đếm ở đây. Rỗng = không che gì.</summary>
        public byte[] BoQua = [];

        /// <summary>Số ô don't-care, chỉ để báo ra.</summary>
        public long SoBoQua;
    }

    /// <param name="che">
    /// Mặt nạ che L0 (<c>mm.Rong × mm.Cao</c>, 1 = don't-care), hoặc rỗng. Được đưa vào lưới
    /// bằng ĐÚNG phép chiếu mà điểm model dùng trong <see cref="TrichMotMuc"/> — X = x − w/2,
    /// Y = y − h/2 — nên mặt nạ và điểm nằm khít lên nhau, không lệch nửa pixel ở kích thước lẻ.
    /// </param>
    private static LuoiModel DungLuoiModel(MucPm mm, double tol, byte[] che)
    {
        int t = (int)Math.Ceiling(Math.Max(1.0, tol));
        int w = mm.Rong + 2 * t + 2, h = mm.Cao + 2 * t + 2;
        var l = new LuoiModel { W = w, H = h, Ox = w / 2, Oy = h / 2, Co = new byte[w * h] };

        if (che.Length == (long)mm.Rong * mm.Cao && mm.Rong > 0 && mm.Cao > 0)
        {
            l.BoQua = new byte[w * h];
            float cx = mm.Rong / 2f, cy = mm.Cao / 2f;
            for (int y = 0; y < mm.Cao; y++)
                for (int x = 0; x < mm.Rong; x++)
                {
                    if (che[y * mm.Rong + x] == 0) continue;
                    int mx = (int)Math.Round(x - cx) + l.Ox, my = (int)Math.Round(y - cy) + l.Oy;
                    if (mx < 0 || my < 0 || mx >= w || my >= h) continue;
                    if (l.BoQua[my * w + mx] == 0) { l.BoQua[my * w + mx] = 1; l.SoBoQua++; }
                }
        }

        foreach (var p in mm.Diem)
        {
            int cx = (int)Math.Round(p.X) + l.Ox, cy = (int)Math.Round(p.Y) + l.Oy;
            for (int dy = -t; dy <= t; dy++)
                for (int dx = -t; dx <= t; dx++)
                {
                    if (dx * dx + dy * dy > t * t) continue;
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || y < 0 || x >= w || y >= h) continue;
                    l.Co[y * w + x] = 1;
                }
        }
        return l;
    }

    /// <summary>
    /// CLUTTER — tỉ lệ pixel biên nằm trong dấu chân của model mà KHÔNG điểm model nào giải
    /// thích được. 0 = vùng này chỉ có đúng những cạnh mà model biết; 1 = toàn cạnh lạ.
    ///
    /// Vì sao cần: điểm khớp một mình là COVERAGE, nó chỉ trả lời "model tìm thấy cạnh của
    /// nó chưa" chứ không phân biệt được "tìm thấy đủ cạnh của tôi" với "tìm thấy đủ cạnh
    /// của tôi CỘNG THÊM 500 cạnh khác". Trên nền thép xước thì vế sau đầy rẫy, và đó đúng
    /// là chỗ tool bắt sai. Cognex báo Score và Clutter tách riêng cũng vì lẽ đó.
    ///
    /// Chỉ tính ở L0 và chỉ cho các ứng viên sống sót — cỡ 40 × 40k pixel, không đáng kể so
    /// với phần quét.
    /// </summary>
    private static double DoClutter(MucAnh am, LuoiModel luoi, double cx, double cy, double c, double s)
    {
        if (am.LaBien.Length == 0 || luoi.Co.Length == 0) return 0;

        // Hộp bao của dấu chân ở góc xoay bất kỳ: nửa đường chéo của lưới.
        double r = 0.5 * Math.Sqrt((double)luoi.W * luoi.W + (double)luoi.H * luoi.H) + 1;
        int x0 = Math.Max(0, (int)Math.Floor(cx - r) - am.X0);
        int y0 = Math.Max(0, (int)Math.Floor(cy - r) - am.Y0);
        int x1 = Math.Min(am.Wc - 1, (int)Math.Ceiling(cx + r) - am.X0);
        int y1 = Math.Min(am.Hc - 1, (int)Math.Ceiling(cy + r) - am.Y0);

        long trong = 0, giaiThich = 0;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                if (am.LaBien[y * am.Wc + x] == 0) continue;

                // Nghịch đảo của phép đặt model: px = cx + X·c + Y·s, py = cy − X·s + Y·c.
                double dx = x + am.X0 - cx, dy = y + am.Y0 - cy;
                int mx = (int)Math.Round(dx * c - dy * s) + luoi.Ox;
                int my = (int)Math.Round(dx * s + dy * c) + luoi.Oy;
                if (mx < 0 || my < 0 || mx >= luoi.W || my >= luoi.H) continue;   // ngoài dấu chân

                // DON'T-CARE: không vào tử số mà cũng không vào mẫu số. Đếm vào mẫu số rồi
                // coi là "không giải thích được" chính là biến vùng mình cố tình bỏ qua thành
                // bằng chứng buộc tội, phạt đều tay cả những tư thế đúng.
                if (luoi.BoQua.Length > 0 && luoi.BoQua[my * luoi.W + mx] != 0) continue;

                trong++;
                if (luoi.Co[my * luoi.W + mx] != 0) giaiThich++;
            }

        return trong == 0 ? 0 : (double)(trong - giaiThich) / trong;
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
    /// Điểm khớp của một tư thế. Điểm rơi ra ngoài ảnh tính 0 chứ không bỏ, để tư thế thò ra
    /// ngoài biên không được điểm cao giả nhờ ít điểm.
    ///
    /// Tích vô hướng CÓ DẤU (trừ khi <see cref="PmCfg.BoQuaChieuTuongPhan"/>): một vết xước
    /// là gờ nên hai mép nó có gradient ngược dấu, còn cạnh vật là bậc nên chỉ một dấu — bỏ
    /// dấu là tự xoá mất chỗ phân biệt ấy. Chi tiết ở PmCfg.BoQuaChieuTuongPhan.
    ///
    /// Mức nền phải trừ đi nằm ở <see cref="MucAnh.Nen"/>, không còn là hằng 2/π viết cứng:
    /// hằng ấy chỉ đúng cho đúng một cấu hình (bỏ dấu, không NMS, không dung sai).
    /// </summary>
    private static double Cham(MucAnh am, DiemModel[] diem, double cx, double cy, double c, double s)
    {
        if (diem.Length == 0) return 0;
        double tong = 0;
        for (int i = 0; i < diem.Length; i++) tong += DongGop(am, in diem[i], cx, cy, c, s);
        return Math.Max(0, (tong / diem.Length - am.Nen) / (1 - am.Nen));
    }

    /// <summary>
    /// Đóng góp của MỘT điểm model vào điểm khớp, −1..1.
    ///
    /// Tách riêng ra vì train-nhiều-ảnh (<see cref="BoPhieu"/>) cần hỏi từng điểm một "mày có
    /// trụ được trên ảnh này không" — và nó PHẢI hỏi bằng đúng phép tính mà <see cref="Cham"/>
    /// dùng. Cần sửa cách cho điểm thì sửa ở đây, đừng chép ra bản thứ hai (xem ràng buộc ở
    /// đầu file). AggressiveInlining để vòng nóng của <see cref="Cham"/> không phải trả giá
    /// cho việc tách hàm.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double DongGop(MucAnh am, in DiemModel p, double cx, double cy, double c, double s)
    {
        double px = cx + p.X * c + p.Y * s;
        double py = cy - p.X * s + p.Y * c;
        int ix = (int)(px + 0.5) - am.X0, iy = (int)(py + 0.5) - am.Y0;
        if (ix < 0 || iy < 0 || ix >= am.Wc || iy >= am.Hc) return 0;

        int o = iy * am.Wc + ix;
        double gx = am.Ngx[o], gy = am.Ngy[o];
        if (gx == 0 && gy == 0) return 0;

        double mgx = p.Gx * c + p.Gy * s;
        double mgy = -p.Gx * s + p.Gy * c;
        double t = mgx * gx + mgy * gy;
        return am.BoDau ? Math.Abs(t) : t;
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

            // Chuẩn hoá lại CHỈ khi trường không mang trọng số. Có trọng số mà chuẩn hoá là
            // xoá sạch dung sai khoảng cách: một điểm cách biên 1.9 px sẽ được tính y như
            // điểm nằm đúng trên biên, và đỉnh bẹt ra đúng bằng bán kính dung sai.
            if (!am.CoTrongSo) { gx /= len; gy /= len; }

            double mgx = p.Gx * c + p.Gy * s;
            double mgy = -p.Gx * s + p.Gy * c;
            double t = mgx * gx + mgy * gy;
            tong += am.BoDau ? Math.Abs(t) : t;
        }

        return Math.Max(0, (tong / diem.Length - am.Nen) / (1 - am.Nen));
    }

    /// <summary>
    /// Dựng trường hướng gradient của ảnh chạy ở một mức — thứ mà <see cref="Cham"/> đọc.
    ///
    /// SÀN LẤY TỪ MODEL, không lấy từ ảnh chạy: <see cref="MucPm.NguongThap"/> là ngưỡng
    /// Canny mà chính mức này đã dùng lúc train, đã nằm sẵn trong .pmm.json. Nhờ vậy train và
    /// run cùng một tiêu chí, và sàn không phụ thuộc việc người dùng khoanh vùng to hay nhỏ.
    ///
    /// ĐỪNG đổi sang hạn ngạch kiểu "giữ 15% pixel mạnh nhất của khung tìm kiếm". Hạn ngạch
    /// nghe hợp lý nhưng hỏng ở mức thô: ở L5 con hàng chỉ chiếm cỡ 2,5% khung và cạnh của nó
    /// đã bị thu nhỏ 32 lần làm nhoè, nên nó thua suất trước nền — sàn vọt lên gấp 2..2,5 lần
    /// so với lúc chọn điểm, quá nửa số điểm model rơi vào pixel đã bị loại, và điểm của tư
    /// thế ĐÚNG ra bằng 0. Mức thô mù hoàn toàn, Run trả về rỗng.
    ///
    /// Dấu hiệu nhận ra kiểu thiết kế đó: hạn ngạch đổi theo khung, nên khoanh vùng tìm kiếm
    /// HẸP LẠI — tức cho tool thêm thông tin — lại làm sàn tăng và kết quả tệ đi.
    /// </summary>
    private static MucAnh ChuanBiMucAnh(Mat xam, MucPm mm, PmCfg cfg, Rect? cuaSo = null)
    {
        int muc = mm.Muc;
        double tiLe = mm.TiLe;
        var kt = new Size(Math.Max(8, (int)Math.Round(xam.Width * tiLe)),
                          Math.Max(8, (int)Math.Round(xam.Height * tiLe)));

        // CỬA SỔ — chỉ làm mờ + Sobel + chuẩn hoá trong đúng vùng mà Cham sẽ đọc.
        //
        // Đây là chỗ tốn nhất của cả Run, nên nó chỉ được phép làm đúng phần cần. Ở mức tinh
        // chỉnh chỉ còn 3..20 ứng viên, mỗi ứng viên dò 7×7 vị trí, nên số ô thật sự được đọc
        // ở L0 chỉ cỡ 3 × 49 × 500 điểm ≈ 73 nghìn — dựng cả mức là cấp hai mảng float hàng
        // trăm MB để rồi đọc vài phần nghìn trong đó.
        //
        // Nới thêm 8 px quanh cửa sổ để nhân làm mờ 5×5 và Sobel 3×3 ở sát mép vẫn cho ra
        // ĐÚNG con số như khi chạy trên cả mức. Sửa cửa sổ thì phải giữ được tính chất này:
        // điểm từng mức phải khớp từng chữ số với khi chạy không cắt.
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

        // Hiện ĐỘ LỚN GRADIENT, không hiện lại ảnh đã làm mờ: đây mới là thứ cần soi, nó cho
        // biết mức này còn cạnh nào đủ mạnh.
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
            Ngx = new float[n], Ngy = new float[n], LaBien = new byte[n],
        };

        // Sàn = đúng ngưỡng thấp mà Canny đã dùng lúc train mức này, nhân hệ số tinh chỉnh.
        // Lấy ngưỡng THẤP chứ không phải ngưỡng cao: điểm model sinh ra từ Canny có trễ, một
        // điểm hợp lệ chỉ cần mạnh tới ngưỡng thấp là đủ, đòi nó đạt ngưỡng cao là loại oan.
        //
        // Model đời trước không có trường này, khi đó NguongThap = 0 và sàn rơi về
        // NguongBienToiThieu — rộng rãi nhưng vẫn chạy được.
        double sanThap = Math.Max(cfg.NguongBienToiThieu, mm.NguongThap * cfg.HeSoSanChay);
        double sanCao = Math.Max(sanThap + 1, mm.NguongCao * cfg.HeSoSanChay);

        // ---- Bước 1: ĐÂU LÀ BIÊN ----
        //
        // Train và Run BẮT BUỘC cùng một định nghĩa "biên", nếu không thì điểm số vô nghĩa.
        // Xem PmCfg.NmsLucChay.
        if (cfg.NmsLucChay)
        {
            // ĐÚNG hàm mà train gọi, đúng cặp ngưỡng của chính mức này nhân HeSoSanChay.
            // Canny tự làm triệt phi cực đại + trễ nên cho ra chuỗi biên MẢNH 1 px, thay vì
            // cả một dải dày mà tư thế nào đặt vào cũng trúng.
            //
            // Canny của OpenCV tự tính Sobel 3×3 chuẩn L1 bên trong — cùng một phép với dx/dy
            // ở trên, nên hướng đọc từ dx/dy khớp đúng những pixel mà nó đánh dấu.
            using var bien = new Mat();
            Cv2.Canny(mo, bien, sanThap, sanCao);
            bien.GetArray(out byte[] aBien);
            for (int o = 0; o < n; o++) am.LaBien[o] = aBien[o] != 0 ? (byte)1 : (byte)0;
            Dbg.Show(bien, $"L{muc} 4. bien Canny luc chay ({sanThap:F0}/{sanCao:F0})");
        }
        else
        {
            // Nhánh không NMS: sàn độ lớn trần trụi, mọi pixel qua sàn đều coi là biên.
            // So bằng chuẩn L1 |gx|+|gy|, ĐÚNG như NguongTuTinh lúc train (nó dùng L1 cho
            // khớp với Canny khi L2gradient = false). Đừng đổi sang L2 √(gx²+gy²): L1 nằm
            // giữa L2 và √2·L2, đổi là tự siết sàn thêm 10..27% mà không ai cố ý.
            for (int o = 0; o < n; o++)
                if (MathF.Abs(gxs[o]) + MathF.Abs(gys[o]) >= sanThap) am.LaBien[o] = 1;
        }

        // ---- Bước 2: hướng đơn vị TẠI các pixel biên ----
        // Chuẩn hoá vẫn phải chia cho L2, vì hướng cần là vector đơn vị thật.
        var hx = new float[n];
        var hy = new float[n];
        int soBien = 0;
        for (int o = 0; o < n; o++)
        {
            if (am.LaBien[o] == 0) continue;
            float gx = gxs[o], gy = gys[o];
            float d = MathF.Sqrt(gx * gx + gy * gy);
            if (d < 1e-6f) { am.LaBien[o] = 0; continue; }
            hx[o] = gx / d; hy[o] = gy / d;
            soBien++;
        }
        am.SoBien = soBien;

        // ---- Bước 3: TRẢI hướng đó ra quanh biên theo dung sai ----
        //
        // Sau bước này, mỗi ô trong Ngx/Ngy không còn mang "gradient của chính pixel đó" mà
        // mang "hướng của biên GẦN NHẤT, mờ dần theo khoảng cách". Dung sai khoảng cách kiểu
        // PatMax nằm trọn ở đây, nên Cham vẫn chỉ là một phép tích vô hướng — muốn đổi cách
        // ghép điểm với biên thì đổi ở bước này, không phải trong Cham.
        double tol = cfg.DungSaiPx;
        if (tol > 1e-6 && soBien > 0)
        {
            var (kc, nguon) = ChamferGanNhat(am.LaBien, cs.Width, cs.Height);
            for (int o = 0; o < n; o++)
            {
                int p = nguon[o];
                if (p < 0) continue;
                float w = (float)(1.0 - kc[o] / tol);
                if (w <= 0) continue;
                am.Ngx[o] = w * hx[p];
                am.Ngy[o] = w * hy[p];
            }
            am.CoTrongSo = true;
        }
        else
        {
            Array.Copy(hx, am.Ngx, n);
            Array.Copy(hy, am.Ngy, n);
        }

        // Mức điểm của một tư thế ngẫu nhiên — xem MucAnh.Nen.
        am.BoDau = cfg.BoQuaChieuTuongPhan;
        bool duongCu = cfg.BoQuaChieuTuongPhan && !cfg.NmsLucChay && !am.CoTrongSo;
        am.Nen = duongCu ? 2.0 / Math.PI : 0.0;

        if (Dbg.Enabled)
        {
            using var huong = VeHuongGradient(am);
            Dbg.Show(huong, $"L{muc} 5. truong huong (san={sanThap:F0}/{sanCao:F0}, {soBien} px bien, " +
                            $"phu {TyLeGiu(am):P1}, dung sai {tol:F1} px, nen={am.Nen:F3})");
        }

        return am;
    }

    /// <summary>
    /// Chamfer hai lượt: với mỗi pixel trả về khoảng cách tới biên gần nhất VÀ CHỈ SỐ của
    /// chính biên đó.
    ///
    /// Cần chỉ số chứ không chỉ khoảng cách, vì thứ đi vào công thức chấm điểm là HƯỚNG của
    /// biên ấy. <c>Cv2.DistanceTransformWithLabels</c> có trả nhãn, nhưng thứ tự đánh nhãn
    /// của nó không được đặc tả — dựng lại bảng tra nhãn→toạ độ là đoán mò. Hai lượt quét
    /// dưới đây là 20 dòng, O(n), và biết chắc mình đang làm gì.
    ///
    /// Trọng số (1, √2) cho lưới 3×3: sai số tối đa ~8% ở hướng chéo. Với dung sai cỡ 2 px
    /// thì đó là 0.16 px — dưới mức đáng quan tâm, và rẻ hơn hẳn khoảng cách Euclid thật.
    /// </summary>
    private static (float[] Kc, int[] Nguon) ChamferGanNhat(byte[] laBien, int w, int h)
    {
        const float VoCuc = 1e9f;
        const float Cheo = 1.41421356f;

        int n = w * h;
        var kc = new float[n];
        var nguon = new int[n];
        for (int o = 0; o < n; o++)
        {
            bool b = laBien[o] != 0;
            kc[o] = b ? 0f : VoCuc;
            nguon[o] = b ? o : -1;
        }

        void Lan(int o, int oTruoc, float them)
        {
            if (nguon[oTruoc] < 0) return;
            float d = kc[oTruoc] + them;
            if (d >= kc[o]) return;
            kc[o] = d;
            nguon[o] = nguon[oTruoc];
        }

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int o = y * w + x;
                if (kc[o] == 0) continue;
                if (y > 0)
                {
                    if (x > 0) Lan(o, o - w - 1, Cheo);
                    Lan(o, o - w, 1f);
                    if (x + 1 < w) Lan(o, o - w + 1, Cheo);
                }
                if (x > 0) Lan(o, o - 1, 1f);
            }

        for (int y = h - 1; y >= 0; y--)
            for (int x = w - 1; x >= 0; x--)
            {
                int o = y * w + x;
                if (kc[o] == 0) continue;
                if (y + 1 < h)
                {
                    if (x + 1 < w) Lan(o, o + w + 1, Cheo);
                    Lan(o, o + w, 1f);
                    if (x > 0) Lan(o, o + w - 1, Cheo);
                }
                if (x + 1 < w) Lan(o, o + 1, 1f);
            }

        return (kc, nguon);
    }

    /// <summary>
    /// Vẽ ĐÚNG thứ mà <see cref="Cham"/> đọc: trường hướng gradient sau khi đã cắt sàn.
    /// Đen = pixel bị sàn loại, tức là với bộ chấm điểm nó không tồn tại. Nhìn ảnh này là
    /// biết mức đó còn giữ được đường viền nào, hay sàn đã ăn sạch mất cạnh cần tìm.
    ///
    /// Màu mã hoá hướng bằng chính (gx, gy) chứ không qua atan2: một lượt nhân, không lượng
    /// giác. Ở mức L0 vài triệu pixel thì riêng atan2 đã đủ làm người ta ngỡ là treo.
    /// </summary>
    private static Mat VeHuongGradient(MucAnh am)
    {
        var buf = new byte[am.W * am.H * 3];

        // Trường hướng chỉ phủ CỬA SỔ (X0,Y0,Wc,Hc), không phủ cả mức — nên phải cộng X0/Y0
        // khi đổ ra ảnh đầy. Quên cộng thì ảnh debug trượt lên góc trái và bóp méo, nhìn
        // tưởng thuật toán hỏng trong khi chỉ hỏng cái ảnh debug.
        for (int yw = 0; yw < am.Hc; yw++)
            for (int xw = 0; xw < am.Wc; xw++)
            {
                int o = yw * am.Wc + xw;
                float gx = am.Ngx[o], gy = am.Ngy[o];
                if (gx == 0 && gy == 0) continue;             // ngoài dung sai -> để đen

                int x = am.X0 + xw, y = am.Y0 + yw;
                if (x < 0 || y < 0 || x >= am.W || y >= am.H) continue;
                int q = (y * am.W + x) * 3;

                buf[q + 0] = 40;                              // B: nền mờ để thấy pixel còn sống
                buf[q + 1] = (byte)(127 + 127 * gy);          // G theo thành phần dọc
                buf[q + 2] = (byte)(127 + 127 * gx);          // R theo thành phần ngang
            }

        // Đổ thẳng byte[] vào bộ nhớ Mat. KHÔNG dùng Mat.SetArray ở đây: nó đòi kiểu phần tử
        // khớp với MatType, đưa byte[] vào CV_8UC3 là ném "Mat data type is not compatible".
        // Mat vừa cấp phát luôn liên tục nên Marshal.Copy một phát là đủ.
        var ra = new Mat(am.H, am.W, MatType.CV_8UC3);
        Marshal.Copy(buf, 0, ra.Data, buf.Length);
        return ra;
    }

    /// <summary>Tỉ lệ pixel sống sót qua sàn — đo thẳng cái ngưỡng phân vị 0.85.</summary>
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
    // Hai quy tắc chi phối cả mục này:
    //
    //  1. KHÔNG có một công thức mặt nạ nào đúng cho mọi dự án. Cùng một phép
    //     "min(B,G,R) + Otsu + bao lồi" cho ra mặt nạ đúng ở dự án này và vô nghĩa ở dự án
    //     khác — có bộ ảnh bao lồi chỉ ôm cái hốc giữa và bỏ cả khung ngoài (nơi có toàn bộ
    //     cạnh đáng train), có bộ mặt nạ phủ 96–100% tức không lọc gì. Vì vậy kiểu mặt nạ là
    //     một LỰA CHỌN của người dùng, mặc định Khong, chứ không phải một cái công tắc
    //     "chế độ thông minh" bật sẵn. Thêm kiểu mới thì thêm vào enum, đừng đoán tự động.
    //
    //  2. Mặt nạ phải tính TRONG KHUNG ROI, không phải toàn ảnh. Trên cả tấm 5064² thì blob
    //     lớn nhất thường là vùng nền tối ngoài vòng đèn hoặc một con hàng khác, chứ không
    //     phải con hàng người dùng vừa khoanh. Tính trong khung ROI nới NoiKhungMatNa cũng
    //     rẻ hơn hàng chục lần: Cv2.Split một tấm 5064×5064 là ba lần cấp phát 25 MB cho mỗi
    //     lần bấm Train.

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
