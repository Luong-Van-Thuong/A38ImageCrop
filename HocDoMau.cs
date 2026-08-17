using OpenCvSharp;

namespace A38.ImageCrop;

/// <summary>
/// BÀI HỌC: dò mẫu bằng "chùm điểm" — bản thô nhất, đủ để hiểu shape-based hoạt động ra sao.
///
/// Đây KHÔNG phải bản dùng cho máy chạy thật. Nó cố tình bỏ hết mọi thứ tinh vi
/// (gradient, kim tự tháp, don't-care) để chỉ còn lại đúng cái lõi:
///
///     model = một DANH SÁCH TOẠ ĐỘ, không phải một tấm ảnh.
///     chấm điểm = đặt danh sách đó lên ảnh rồi đếm xem bao nhiêu điểm rơi trúng biên.
///
/// Hiểu xong bản này thì đọc PatModel.cs sẽ thấy nó chỉ là bản này cộng thêm gia vị.
///
/// Chạy:  dotnet run -- --hoc 1     (vùng khoanh -> danh sách toạ độ)
///        dotnet run -- --hoc 2     (nới biên để cho phép sai số)
///        dotnet run -- --hoc 3     (đặt chùm điểm lên và đếm trúng)
///        dotnet run -- --hoc 4     (chuẩn hoá thành điểm số, trượt khắp ảnh)
/// </summary>
public static class HocCfg
{
    /// <summary>Ảnh Canny đã lưu sẵn — dùng đúng ảnh này để đối chiếu số liệu trong bài.</summary>
    public static string AnhGoc = @"D:\Images_\V2\CoilAssy\CoilAssy\1240S\opencv\test\goc.bmp";

    /// <summary>Vùng đã khoanh đỏ trên align.bmp, đo được là bbox này.</summary>
    public static Rect VungKhoanh = new Rect(675, 155, 178, 154);
    public static Rect VungKhoanhDuoiPhai = new Rect(147, 691, 178, 154);

    /// <summary>Bán kính nới biên khi chấm điểm — chính là "dung sai" cho phép model lệch.</summary>
    public static int BanKinhNoi = 2;

    public static string ThuMucRa = "hoc_out";
}

public static class HocDoMau
{
    /// <summary>Điểm vào khi chạy với cờ --hoc.</summary>
    public static int Chay(string[] args)
    {
        int i = Array.IndexOf(args, "--hoc");
        int buoc = (i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var b)) ? b : 1;

        using var anh = Cv2.ImRead(HocCfg.AnhGoc, ImreadModes.Grayscale);
        if (anh.Empty())
        {
            Console.WriteLine($"Doc anh that bai: {HocCfg.AnhGoc}");
            return 1;
        }

        Directory.CreateDirectory(HocCfg.ThuMucRa);
        Console.WriteLine($"Anh vao: {HocCfg.AnhGoc}");
        Console.WriteLine($"Kich thuoc: {anh.Width} x {anh.Height}, so kenh: {anh.Channels()}");
        Console.WriteLine();

        switch (buoc)
        {
            case 1: Buoc1_LayChumDiem(anh); break;
            case 2: Buoc2_NoiBien(anh); break;
            case 3: Buoc3_DatVaDem(anh); break;
            case 4: Buoc4_TruotKhapAnh(anh); break;
            default:
                Console.WriteLine($"Chua co buoc {buoc}. Hien co: 1, 2, 3, 4.");
                return 1;
        }
        return 0;
    }

    // =====================================================================
    // BƯỚC 1 — biến vùng khoanh thành một danh sách toạ độ
    // =====================================================================

    /// <summary>
    /// Quét từng pixel trong vùng khoanh. Pixel nào trắng thì ghi lại toạ độ của nó
    /// SO VỚI GÓC TRÊN-TRÁI CỦA VÙNG, không phải toạ độ trong ảnh.
    ///
    /// Chỗ "so với góc vùng" là mấu chốt và cũng là chỗ dễ sai nhất. Model phải độc lập
    /// với việc nó được cắt ra từ đâu, thì mới đem đặt sang chỗ khác được. Ghi toạ độ
    /// tuyệt đối thì model chỉ dùng được đúng tại chỗ cũ — tức là vô dụng.
    /// </summary>
    public static List<Point> LayChumDiem(Mat anh, Rect vung)
    {
        var diem = new List<Point>();

        for (int y = 0; y < vung.Height; y++)
        {
            for (int x = 0; x < vung.Width; x++)
            {
                // At<byte>(hang, cot) = At<byte>(y, x). Nguoc thu tu la sai am tham.
                byte v = anh.At<byte>(vung.Y + y, vung.X + x);
                if (v > 128) diem.Add(new Point(x, y));
            }
        }

        return diem;
    }

    private static void Buoc1_LayChumDiem(Mat anh)
    {
        Console.WriteLine("===== BUOC 1: bien vung khoanh thanh danh sach toa do =====");
        Console.WriteLine();

        int tongTrangCaAnh = Cv2.CountNonZero(anh);
        Console.WriteLine($"  So pixel trang trong CA ANH        : {tongTrangCaAnh}");

        //var vung = HocCfg.VungKhoanh;
        var vung = HocCfg.VungKhoanhDuoiPhai;
        Console.WriteLine($"  Vung khoanh                        : x={vung.X} y={vung.Y} " +
                          $"rong={vung.Width} cao={vung.Height}  ({vung.Width * vung.Height} pixel)");

        var diem = LayChumDiem(anh, vung);
        Console.WriteLine($"  So diem model (pixel trang trong vung): {diem.Count}");
        Console.WriteLine($"  Mat do: {(double)diem.Count / (vung.Width * vung.Height):P1} " +
                          $"vung khoanh la bien");
        Console.WriteLine();

        Console.WriteLine("  5 diem dau tien (toa do TUONG DOI so voi goc vung):");
        foreach (var p in diem.Take(5))
            Console.WriteLine($"     ({p.X,3}, {p.Y,3})   <- trong anh la ({vung.X + p.X}, {vung.Y + p.Y})");
        Console.WriteLine();

        VeChumDiem(diem, vung, Path.Combine(HocCfg.ThuMucRa, "buoc1_chum_diem_2.png"));

        Console.WriteLine($"  Da ve chum diem ra: {Path.GetFullPath(Path.Combine(HocCfg.ThuMucRa, "buoc1_chum_diem.png"))}");
        Console.WriteLine("  Mo anh do ra xem: no phai giong het vung ban khoanh.");
        Console.WriteLine("  Neu giong => ban vua tu tay bien mot vung anh thanh mot MODEL.");
        Console.WriteLine();
        Console.WriteLine($"  >>> SO CAN KHOP: {diem.Count} diem. Dung la 1571.");
    }

    /// <summary>
    /// Vẽ chùm điểm ra một ảnh trắng đen mới, KHÔNG dùng lại ảnh gốc.
    ///
    /// Vẽ đè lên ảnh gốc thì không chứng minh được gì — nhìn vào không biết nét nào là
    /// của model, nét nào là của ảnh. Vẽ ra nền trống mới thấy: danh sách toạ độ này,
    /// tự nó, đã đủ để dựng lại hình.
    /// </summary>
    private static void VeChumDiem(List<Point> diem, Rect vung, string duongDan)
    {
        using var ve = new Mat(vung.Height, vung.Width, MatType.CV_8UC1, Scalar.All(0));
        foreach (var p in diem) ve.Set<byte>(p.Y, p.X, 255);
        Dbg.Show(ve, "a");
        Cv2.ImWrite(duongDan, ve);
    }

    // =====================================================================
    // BƯỚC 2 — nới biên của ảnh đích để cho phép sai số
    // =====================================================================

    /// <summary>
    /// Giãn mọi pixel trắng ra bán kính r. Sau bước này, "trúng biên" nghĩa là
    /// "cách biên không quá r pixel" — tức là model được phép lệch r pixel mà vẫn tính đúng.
    ///
    /// Không có bước này thì lệch ĐÚNG MỘT pixel là điểm tụt gần về 0, vì biên Canny
    /// chỉ dày 1 pixel. Bước 2 chạy xong hãy tự hỏi: nới to lên thì sao? Nới quá to thì
    /// mọi vị trí đều trúng, điểm bão hoà, hết phân biệt. r là đánh đổi, không phải hằng số.
    /// </summary>
    public static Mat NoiBien(Mat anh, int banKinh)
    {
        using Mat kernel = Cv2.GetStructuringElement(
            MorphShapes.Rect, new Size(2 * banKinh + 1, 2 * banKinh + 1));
        //Dbg.Show(kernel, $"kernel r={banKinh}");
        var noi = new Mat();
        Cv2.Dilate(anh, noi, kernel);
        //Dbg.Show(noi, $"anh da noi r={banKinh}");
        return noi;
    }

    private static void Buoc2_NoiBien(Mat anh)
    {
        Console.WriteLine("===== BUOC 2: noi bien de cho phep sai so =====");
        Console.WriteLine();

        int truoc = Cv2.CountNonZero(anh);
        Console.WriteLine($"  Pixel trang TRUOC khi noi : {truoc}");
        Console.WriteLine();

        foreach (int r in new[] { 0, 1, 2, 3, 5, 10 })
        {
            if (r == 0)
            {
                Console.WriteLine($"    r = {r,2}  ->  {truoc,8} px trang  ({(double)truoc / (anh.Width * anh.Height):P1} anh)");
                continue;
            }
            using var noi = NoiBien(anh, r);
            int sau = Cv2.CountNonZero(noi);
            Console.WriteLine($"    r = {r,2}  ->  {sau,8} px trang  ({(double)sau / (anh.Width * anh.Height):P1} anh)" +
                              $"   gap {(double)sau / truoc:F1} lan");
        }
        Console.WriteLine();

        using var dung = NoiBien(anh, HocCfg.BanKinhNoi);
        var file = Path.Combine(HocCfg.ThuMucRa, $"buoc2_noi_r{HocCfg.BanKinhNoi}.png");
        Cv2.ImWrite(file, dung);
        Console.WriteLine($"  Da ghi anh da noi (r={HocCfg.BanKinhNoi}): {Path.GetFullPath(file)}");
        Console.WriteLine();
        Console.WriteLine("  Nhin bang so tren: r cang to, dien tich trang cang phinh.");
        Console.WriteLine("  r = 10 la gan mot phan tu anh da trang -> dat diem nao vao cung trung.");
        Console.WriteLine("  Do la ly do r phai nho. r = 2 la cho dung sai vua du.");
    }

    // =====================================================================
    // BƯỚC 3 — đặt chùm điểm lên ảnh và đếm số điểm trúng
    // =====================================================================

    /// <summary>
    /// Đếm số điểm của model rơi trúng biên khi đặt chùm điểm tại vị trí (ox, oy).
    ///
    /// Nhận <paramref name="anhNoi"/> là byte[] chứ không nhận Mat: <c>Mat.At&lt;byte&gt;()</c>
    /// đi qua lớp marshalling native mỗi lần gọi. Đo được trên máy này là chậm hơn
    /// khoảng 4 lần — không phải hàng chục lần như người ta thường nói, nhưng bước 4
    /// chấm 697.081 vị trí nên 4 lần là từ 6 giây thành 25 giây. Bước 3 đo thẳng con số đó.
    ///
    /// Điểm nào ra ngoài khung ảnh thì tính là TRƯỢT, không phải bỏ qua. Bỏ qua thì
    /// model càng nhô ra ngoài ảnh càng ít điểm để trượt, điểm số tự nhiên cao lên —
    /// và bạn được một đỉnh giả ngay ở bốn mép ảnh.
    /// </summary>
    public static int DemTrung(byte[] anhNoi, int rongAnh, int caoAnh,
                              List<Point> diem, int ox, int oy)
    {
        int trung = 0;

        foreach (var p in diem)
        {
            int x = ox + p.X;
            int y = oy + p.Y;
            if (x < 0 || x >= rongAnh || y < 0 || y >= caoAnh) continue;   // ra ngoai = truot
            if (anhNoi[y * rongAnh + x] > 128) trung++;
        }

        return trung;
    }

    /// <summary>
    /// Xoay chùm điểm quanh TÂM của nó, dùng ma trận xoay 2D.
    ///
    ///     x' = x·cos - y·sin
    ///     y' = x·sin + y·cos
    ///
    /// Phải trừ tâm trước rồi cộng lại sau, vì công thức trên xoay quanh gốc (0,0).
    /// Xoay quanh gốc mà không dịch tâm thì cả model bay khỏi vị trí.
    ///
    /// Xoay chùm 1571 ĐIỂM, không xoay 1 TRIỆU PIXEL của ảnh. Cùng một kết quả,
    /// rẻ hơn khoảng 600 lần. Đây là lợi ích thực tế của việc coi model là danh sách toạ độ.
    /// </summary>
    public static List<Point> XoayChumDiem(List<Point> diem, double gocDo, int rong, int cao)
    {
        double rad = gocDo * Math.PI / 180.0;
        double cs = Math.Cos(rad), sn = Math.Sin(rad);
        double cx = (rong - 1) / 2.0, cy = (cao - 1) / 2.0;

        var ra = new List<Point>(diem.Count);
        foreach (var p in diem)
        {
            double dx = p.X - cx, dy = p.Y - cy;
            double nx = dx * cs - dy * sn;
            double ny = dx * sn + dy * cs;
            ra.Add(new Point((int)Math.Round(nx + cx), (int)Math.Round(ny + cy)));
        }
        return ra;
    }

    /// <summary>Dạng rút gọn của xoay 180°: cos = -1, sin = 0 nên không cần lượng giác.</summary>
    public static List<Point> XoayChumDiem180(List<Point> diem, int rong, int cao)
    {
        var ra = new List<Point>(diem.Count);
        foreach (var p in diem) ra.Add(new Point(rong - 1 - p.X, cao - 1 - p.Y));
        return ra;
    }

    private static void Buoc3_DatVaDem(Mat anh)
    {
        Console.WriteLine("===== BUOC 3: dat chum diem len anh va dem so diem trung =====");
        Console.WriteLine();

        var vung = HocCfg.VungKhoanh;
        var diem = LayChumDiem(anh, vung);
        int N = diem.Count;

        using var noi = NoiBien(anh, HocCfg.BanKinhNoi);
        using var khongNoi = anh.Clone();
        //Dbg.Show(noi, "noi");

        // Lay ra byte[] mot lan roi dung mai - day la chuan bi cho buoc 4.
        noi.GetArray(out byte[] bufNoi);
        khongNoi.GetArray(out byte[] bufTho);
        int w = anh.Width, h = anh.Height;

        Console.WriteLine($"  Model: {N} diem   |   anh: {w}x{h}   |   ban kinh noi r={HocCfg.BanKinhNoi}");
        Console.WriteLine();

        // ---- A. Dat dung cho ----
        int trungDung = DemTrung(bufNoi, w, h, diem, vung.X, vung.Y);
        Console.WriteLine($"  A. Dat DUNG CHO ({vung.X},{vung.Y}):");
        Console.WriteLine($"       trung {trungDung} / {N}   truot {N - trungDung}   " +
                          $"diem = {(double)trungDung / N:F4}");
        Console.WriteLine("       Phai la 1571/1571 = 1.0000. Dat model len chinh cho no duoc cat ra");
        Console.WriteLine("       thi khong the truot diem nao - day la phep tu kiem cua buoc 3.");
        Console.WriteLine();

        // ---- B. Dat lech dan: bang dung sai ----
        Console.WriteLine("  B. Dat LECH dan - day la bang cho thay buoc 2 lam gi:");
        Console.WriteLine();
        Console.WriteLine("       lech      diem voi r=2      diem voi r=0 (khong noi)");
        foreach (int d in new[] { 0, 1, 2, 3, 4, 5, 8, 15 })
        {
            int co = DemTrung(bufNoi, w, h, diem, vung.X + d, vung.Y);
            int khong = DemTrung(bufTho, w, h, diem, vung.X + d, vung.Y);
            Console.WriteLine($"       {d,2} px       {(double)co / N,10:F4}          {(double)khong / N,10:F4}");
        }
        Console.WriteLine();
        Console.WriteLine("       Cot r=0 sap ngay tu 1 px -> khong co buoc 2 thi thuat toan vo dung.");
        Console.WriteLine("       Cot r=2 giu diem cao den ~2-3 px roi moi tut -> do chinh la dung sai.");
        Console.WriteLine();

        // ---- C. Hai cach xoay 180 phai cho cung ket qua ----
        var xoayTongQuat = XoayChumDiem(diem, 180.0, vung.Width, vung.Height);
        var xoayRutGon = XoayChumDiem180(diem, vung.Width, vung.Height);

        int soLech = 0;
        for (int k = 0; k < N; k++)
            if (xoayTongQuat[k] != xoayRutGon[k]) soLech++;

        Console.WriteLine("  C. Kiem tra cong thuc xoay:");
        Console.WriteLine($"       ma tran xoay (cos/sin)  vs  dang rut gon (rong-1-x)");
        Console.WriteLine($"       so diem khac nhau: {soLech} / {N}   " +
                          $"{(soLech == 0 ? "-> HAI CACH LA MOT" : "-> LECH, xem lai tam xoay")}");
        Console.WriteLine();

        // ---- D. Dat chum da xoay o vi tri song sinh ----
        var songSinh = new Point(118, 669);
        int trungSong = DemTrung(bufNoi, w, h, xoayRutGon, songSinh.X, songSinh.Y);
        Console.WriteLine($"  D. Dat chum da XOAY 180 tai vi tri song sinh ({songSinh.X},{songSinh.Y}):");
        Console.WriteLine($"       trung {trungSong} / {N}   truot {N - trungSong}   " +
                          $"diem = {(double)trungSong / N:F4}");
        Console.WriteLine("       Phai la 1432/1571 = 0.9115.");
        Console.WriteLine($"       {N - trungSong} diem truot do gan het nam o vanh LO TRON - do la");
        Console.WriteLine("       toan bo su khac biet giua 0 do va 180 do, va no chi chiem 8.85%.");
        Console.WriteLine();

        VeTrungTruot(anh, noi, xoayRutGon, songSinh, vung.Size,
                     Path.Combine(HocCfg.ThuMucRa, "buoc3_trung_truot.png"));
        Console.WriteLine($"  Da ve anh trung/truot: " +
                          $"{Path.GetFullPath(Path.Combine(HocCfg.ThuMucRa, "buoc3_trung_truot.png"))}");
        Console.WriteLine("  Xanh = trung, Do = truot. Mo ra xem 139 diem do nam o dau.");
        Console.WriteLine();

        // ---- E. Do gia cua At<byte>() so voi byte[] ----
        DoTocDoTruyCap(noi, bufNoi, diem, vung);
    }

    /// <summary>
    /// Vẽ từng điểm model theo trúng/trượt, trên nền là chính vùng ảnh đó.
    /// Nhìn ảnh này là biết điểm số tụt vì chỗ nào, thay vì chỉ biết nó tụt.
    /// </summary>
    private static void VeTrungTruot(Mat anh, Mat noi, List<Point> diem, Point tai,
                                     Size kt, string duongDan)
    {
        var vungVe = new Rect(tai.X, tai.Y, kt.Width, kt.Height)
                     .Intersect(new Rect(0, 0, anh.Width, anh.Height));

        using var nen = new Mat(anh, vungVe);
        using var ve = new Mat();
        Cv2.CvtColor(nen, ve, ColorConversionCodes.GRAY2BGR);
        ve.SetTo(new Scalar(70, 70, 70), nen);            // bien cua anh: xam mo

        int phong = 3;
        using var to = new Mat();
        Cv2.Resize(ve, to, new Size(ve.Width * phong, ve.Height * phong), 0, 0,
                   InterpolationFlags.Nearest);

        noi.GetArray(out byte[] bufNoi);
        foreach (var p in diem)
        {
            int gx = tai.X + p.X, gy = tai.Y + p.Y;
            bool trung = gx >= 0 && gx < anh.Width && gy >= 0 && gy < anh.Height
                         && bufNoi[gy * anh.Width + gx] > 128;

            var mau = trung ? new Scalar(120, 220, 90) : new Scalar(60, 60, 255);
            Cv2.Rectangle(to, new Rect(p.X * phong, p.Y * phong, phong, phong), mau, -1);
        }

        Cv2.ImWrite(duongDan, to);
    }

    /// <summary>
    /// Đo thẳng giá của <c>Mat.At&lt;byte&gt;()</c> so với byte[] đã lấy ra sẵn.
    ///
    /// Đây không phải chuyện tối ưu sớm. Bước 4 phải chấm ~700.000 vị trí; nếu mỗi vị trí
    /// chậm đi 20 lần thì bước 4 đi từ vài giây thành vài phút, và bạn sẽ tưởng là
    /// thuật toán sai thay vì cách truy cập sai.
    /// </summary>
    private static void DoTocDoTruyCap(Mat noi, byte[] bufNoi, List<Point> diem, Rect vung)
    {
        const int soLan = 300;
        int w = noi.Width, h = noi.Height;

        var dh1 = System.Diagnostics.Stopwatch.StartNew();
        long tong1 = 0;
        for (int k = 0; k < soLan; k++)
            tong1 += DemTrung(bufNoi, w, h, diem, vung.X + (k % 7), vung.Y);
        dh1.Stop();

        var dh2 = System.Diagnostics.Stopwatch.StartNew();
        long tong2 = 0;
        for (int k = 0; k < soLan; k++)
        {
            int ox = vung.X + (k % 7), oy = vung.Y;
            int trung = 0;
            foreach (var p in diem)
            {
                int x = ox + p.X, y = oy + p.Y;
                if (x < 0 || x >= w || y < 0 || y >= h) continue;
                if (noi.At<byte>(y, x) > 128) trung++;      // cach CHAM
            }
            tong2 += trung;
        }
        dh2.Stop();

        Console.WriteLine($"  E. Do toc do ({soLan} lan cham diem, moi lan {diem.Count} diem):");
        Console.WriteLine($"       byte[] da lay ra san : {dh1.Elapsed.TotalMilliseconds,8:F1} ms");
        Console.WriteLine($"       Mat.At<byte>()       : {dh2.Elapsed.TotalMilliseconds,8:F1} ms" +
                          $"   -> cham hon {dh2.Elapsed.TotalMilliseconds / Math.Max(0.001, dh1.Elapsed.TotalMilliseconds):F0} lan");
        Console.WriteLine($"       (ket qua giong nhau: {tong1} vs {tong2})");
        Console.WriteLine("       Vi vay buoc 4 chi dung byte[]. At<byte>() chi de doc vai pixel le.");
    }

    // =====================================================================
    // BƯỚC 4 — chuẩn hoá thành điểm số rồi trượt khắp ảnh
    // =====================================================================

    /// <summary>
    /// Trượt chùm điểm khắp ảnh và trả về bản đồ điểm số.
    ///
    /// <paramref name="buocNhay"/> = 1 là quét từng pixel; = 2 là bỏ một pixel cách một.
    /// Bỏ bớt được vì đỉnh điểm số không nhọn hoắt — đã nới biên r=2 nên đỉnh rộng
    /// vài pixel, nhảy 2 vẫn không trượt mất đỉnh. Đó là bậc thang đầu tiên dẫn tới
    /// ý tưởng kim tự tháp trong PatModel.cs: quét thô rồi tinh lại.
    /// </summary>
    public static float[] TruotKhapAnh(byte[] anhNoi, int rongAnh, int caoAnh,
                                       List<Point> diem, Size ktModel, int buocNhay,
                                       out int soViTri)
    {
        var banDo = new float[rongAnh * caoAnh];
        int N = Math.Max(1, diem.Count);
        soViTri = 0;

        int maxX = rongAnh - ktModel.Width;
        int maxY = caoAnh - ktModel.Height;

        for (int oy = 0; oy <= maxY; oy += buocNhay)
        {
            for (int ox = 0; ox <= maxX; ox += buocNhay)
            {
                int trung = DemTrung(anhNoi, rongAnh, caoAnh, diem, ox, oy);
                banDo[oy * rongAnh + ox] = (float)trung / N;
                soViTri++;
            }
        }

        return banDo;
    }

    /// <summary>
    /// Lấy các đỉnh cao nhất, mỗi đỉnh cách nhau ít nhất <paramref name="cachNhau"/> pixel.
    ///
    /// Không có ràng buộc khoảng cách thì "top 5" sẽ là 5 pixel sát nhau của cùng MỘT đỉnh,
    /// đọc xong không biết gì thêm. Đây là dạng đơn giản nhất của non-maximum suppression.
    /// </summary>
    public static List<(Point Tai, float Diem)> LayCacDinh(float[] banDo, int rongAnh, int caoAnh,
                                                          int soDinh, int cachNhau)
    {
        var ungVien = new List<(int Idx, float Diem)>();
        for (int i = 0; i < banDo.Length; i++)
            if (banDo[i] > 0.60f) ungVien.Add((i, banDo[i]));

        ungVien.Sort((a, b) => b.Diem.CompareTo(a.Diem));

        var dinh = new List<(Point, float)>();
        foreach (var (idx, d) in ungVien)
        {
            if (dinh.Count >= soDinh) break;
            int x = idx % rongAnh, y = idx / rongAnh;

            bool quaGan = false;
            foreach (var (t, _) in dinh)
                if (Math.Abs(t.X - x) < cachNhau && Math.Abs(t.Y - y) < cachNhau) { quaGan = true; break; }

            if (!quaGan) dinh.Add((new Point(x, y), d));
        }
        return dinh;
    }

    private static void Buoc4_TruotKhapAnh(Mat anh)
    {
        Console.WriteLine("===== BUOC 4: chuan hoa thanh diem so roi truot khap anh =====");
        Console.WriteLine();

        var vung = HocCfg.VungKhoanh;
        var diem = LayChumDiem(anh, vung);
        int N = diem.Count;
        int w = anh.Width, h = anh.Height;

        using var noi = NoiBien(anh, HocCfg.BanKinhNoi);
        noi.GetArray(out byte[] bufNoi);

        // ---- Tinh chi phi TRUOC khi chay ----
        long soViTriDuKien = (long)(w - vung.Width + 1) * (h - vung.Height + 1);
        Console.WriteLine("  Chi phi du kien (tinh truoc khi chay, buoc nhay = 1):");
        Console.WriteLine($"       so vi tri  = ({w}-{vung.Width}+1) x ({h}-{vung.Height}+1) = {soViTriDuKien:N0}");
        Console.WriteLine($"       so phep    = {N} diem x {soViTriDuKien:N0} = {N * soViTriDuKien:N0}");
        Console.WriteLine();

        foreach (int buocNhay in new[] { 4, 6, 8 })
        {
            var dongHo = System.Diagnostics.Stopwatch.StartNew();
            var banDo = TruotKhapAnh(bufNoi, w, h, diem, vung.Size, buocNhay, out int soViTri);
            dongHo.Stop();

            var dinh = LayCacDinh(banDo, w, h, 4, 60);
            double giay = dongHo.Elapsed.TotalSeconds;

            Console.WriteLine($"  --- buoc nhay = {buocNhay} ---");
            Console.WriteLine($"       {soViTri:N0} vi tri trong {giay:F2}s   " +
                              $"({(long)(N * soViTri / Math.Max(0.001, giay)):N0} phep/giay)");
            foreach (var (tai, d) in dinh)
                Console.WriteLine($"       dinh: diem {d:F4} tai ({tai.X,3},{tai.Y,3})" +
                                  $"{(Math.Abs(tai.X - vung.X) < 20 && Math.Abs(tai.Y - vung.Y) < 20 ? "   <- chinh no" : "")}");
            Console.WriteLine();

            if (buocNhay == 1) VeBanDoDiem(banDo, w, h,
                                           Path.Combine(HocCfg.ThuMucRa, "buoc4_ban_do_diem.png"));
        }

        // ---- Chay lai voi chum diem da xoay 180: day la cai bay ----
        Console.WriteLine("  --- lap lai voi chum diem da XOAY 180 (buoc nhay = 2) ---");
        var xoay = XoayChumDiem180(diem, vung.Width, vung.Height);
        var banDo180 = TruotKhapAnh(bufNoi, w, h, xoay, vung.Size, 8, out _);
        foreach (var (tai, d) in LayCacDinh(banDo180, w, h, 3, 60))
            Console.WriteLine($"       dinh: diem {d:F4} tai ({tai.X,3},{tai.Y,3})");
        Console.WriteLine();

        Console.WriteLine("  DOC KET QUA:");
        Console.WriteLine("    - O 0 do, dinh cao nhat la chinh no (1.0000), dinh nhi chi ~0.57");
        Console.WriteLine("      -> neu phoi chac chan o 0 do thi bai nay de, khoang cach rat rong.");
        Console.WriteLine("    - O 180 do, co mot dinh ~0.91 o goc duoi-trai.");
        Console.WriteLine("      -> 1.00 so voi 0.91 la QUA SAT. Do chinh la bai toan cua ban:");
        Console.WriteLine("         diem so hinh hoc KHONG phan biet duoc 0 do va 180 do.");
        Console.WriteLine("    - Va buoc nhay 4 cho gan dung dinh nhu buoc nhay 1, chi nhanh gap ~16 lan");
        Console.WriteLine("      -> hat giong cua y tuong kim tu thap trong PatModel.cs.");
    }

    /// <summary>
    /// Vẽ bản đồ điểm số thành ảnh nhiệt. Nhìn được cả bề mặt điểm số thay vì chỉ vài con số —
    /// thấy ngay có mấy đỉnh, đỉnh nhọn hay tù, và các đỉnh cạnh tranh nằm ở đâu.
    /// </summary>
    private static void VeBanDoDiem(float[] banDo, int w, int h, string duongDan)
    {
        using var m = new Mat(h, w, MatType.CV_8UC1, Scalar.All(0));
        var buf = new byte[w * h];
        for (int i = 0; i < banDo.Length; i++)
            buf[i] = (byte)Math.Clamp((int)(banDo[i] * 255f), 0, 255);
        m.SetArray(buf);

        using var mau = new Mat();
        Cv2.ApplyColorMap(m, mau, ColormapTypes.Jet);
        Cv2.ImWrite(duongDan, mau);
    }
}
