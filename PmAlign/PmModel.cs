using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace A38.ImageCrop.PmAlign;

/// <summary>
/// Một điểm của model: toạ độ so với TÂM mẫu, kèm vector gradient đã chuẩn hoá.
///
/// Chuẩn hoá về độ dài 1 chính là chỗ làm cho điểm số miễn nhiễm với độ sáng: ảnh sáng lên
/// gấp đôi thì gradient dài gấp đôi, nhưng hướng không đổi. Chấm điểm là tích vô hướng giữa
/// hai vector đơn vị, nên nó đo HƯỚNG CẠNH chứ không đo độ tương phản.
/// </summary>
public readonly struct DiemModel(float x, float y, float gx, float gy)
{
    public readonly float X = x, Y = y;
    public readonly float Gx = gx, Gy = gy;
}

/// <summary>Model ở một mức kim tự tháp.</summary>
public sealed class MucPm
{
    public int Muc;
    public double TiLe;
    public int Rong, Cao;
    public DiemModel[] Diem = [];
    public float BanKinh;

    public double NguongThap, NguongCao;
    public int SoPixelBien, SoUngVien;

    /// <summary>
    /// ĐÒN BẨY XOAY, pixel dịch chuyển trên mỗi độ xoay. Đóng góp của một điểm là
    /// |x·gy − y·gx|, nên điểm nằm trên đường tròn (gradient hướng tâm) đóng góp bằng 0
    /// dù nó ở xa tâm đến đâu — bán kính lớn KHÔNG đồng nghĩa với có đòn bẩy.
    /// </summary>
    public double DonBayXoay;

    [JsonIgnore] public bool DungDuoc => Diem.Length > 0 && Rong >= 8 && Cao >= 8;

    /// <summary>Bước góc nên dùng ở mức này: góc làm điểm số dịch đi đúng 1 pixel.</summary>
    [JsonIgnore] public double BuocGocDo => DonBayXoay < 1e-6 ? 180 : 1.0 / DonBayXoay;

    /// <summary>Đòn bẩy xoay so với bán kính hình học: 1.0 là dùng hết, 0 là vô dụng.</summary>
    [JsonIgnore] public double TiLeDonBay => BanKinh < 1 ? 0 : DonBayXoay * 180.0 / Math.PI / BanKinh;
}

/// <summary>
/// Model đầy đủ — thứ mà nút Train sinh ra và nút Run tiêu thụ.
///
/// Điểm được lưu trong HỆ TOẠ ĐỘ MẪU đã dựng thẳng, gốc ở tâm ROI. Góc φ của ROI cất riêng
/// trong <see cref="Roi"/>. Nhờ đó góc trả về khi Run quy chiếu được về đúng tư thế của vật
/// trên ẢNH MẪU, thay vì lệch một hằng số theo cách người dùng lỡ xoay ROI.
/// </summary>
public sealed class PmModel
{
    public string TenAnhMau = "";
    public RectXoay Roi;
    public List<RectXoay> Mask = [];
    public int Rong, Cao;
    public List<MucPm> Muc = [];

    /// <summary>
    /// MẶT NẠ CHE Ở TOẠ ĐỘ MẪU, mức L0: <see cref="Rong"/> × <see cref="Cao"/> byte, 1 = don't-care.
    /// Rỗng nghĩa là không che gì (hoặc model cũ chưa có trường này).
    ///
    /// Vì sao phải cất cả ảnh bit chứ không chỉ <see cref="Mask"/>: danh sách RectXoay mới là
    /// phần người dùng vẽ tay, còn phần tự dò (<c>TaoMatNaChe</c> theo ngưỡng R−B, mở/nới)
    /// có hình dạng bất kỳ, không dựng lại được từ vài hình chữ nhật.
    ///
    /// Vì sao lúc Run vẫn cần, dù điểm model trong vùng che đã bị loại từ lúc Train: CLUTTER
    /// đếm mọi pixel biên rơi vào dấu chân model rồi hỏi "điểm model nào giải thích được
    /// không". Vùng che không có điểm model nên mọi cạnh ở đó bị tính là cạnh LẠ — tức là
    /// che một vùng vì nó hay thay đổi thì chính vùng ấy quay lại phạt đều tay mọi kết quả
    /// ĐÚNG. Có mặt nạ này thì clutter bỏ qua hẳn vùng đó, đúng nghĩa don't-care.
    /// </summary>
    [JsonIgnore] public byte[] MatNaChe = [];

    /// <summary>Tỉ lệ diện tích mẫu bị che, 0..1. Chỉ để báo ra cho người dùng thấy.</summary>
    [JsonIgnore] public double TiLeChe
    {
        get
        {
            if (MatNaChe.Length == 0) return 0;
            long n = 0;
            foreach (var v in MatNaChe) if (v != 0) n++;
            return (double)n / MatNaChe.Length;
        }
    }

    /// <summary>
    /// VÙNG TÌM KIẾM đi kèm model — nơi TÂM của mẫu được phép nằm khi chạy.
    ///
    /// Lưu chung với model chứ không để người dùng khoanh lại mỗi lần, vì đo được nó quyết
    /// định CẢ HAI thứ quan trọng nhất, không chỉ tốc độ:
    ///
    ///   • Tốc độ — bộ cnc2_den/c1, ảnh 5064², dải 360°: quét cả ảnh 199 ms/ảnh, còn quét
    ///     trong khung 475×608 chỉ 55 ms/ảnh (max 73). Mốc 50 ms của Cognex nằm ở vế sau.
    ///   • Khả năng TỪ CHỐI — cũng bộ đó, model học ở góc c1 rồi thả trên 20 ảnh c2/c3
    ///     (không hề có mẫu này):
    ///         quét cả ảnh  → điểm cao nhất trên c2/c3 là 0.982, CHỒNG LẤN hoàn toàn với
    ///                        c1 (thấp nhất 0.376) ở mọi mức sàn — không có ngưỡng nào tách được.
    ///         quét khung   → c2/c3 cao nhất 0.072 so với c1 thấp nhất 0.377, tách sạch.
    ///     Lý do vật lý: hình dạng ấy CÓ THẬT ở chỗ khác trong ảnh. Không khoanh vùng thì
    ///     không phải thuật toán yếu, mà là bài toán không có lời giải duy nhất.
    ///
    /// Rỗng (mặc định) nghĩa là model cũ chưa có trường này — khi đó giữ nguyên vùng người
    /// dùng đang khoanh trên giao diện.
    /// </summary>
    public RectXoay VungTim;

    /// <summary>Góc mà ROI đã bị xoay lúc train — GỐC 0° của model.</summary>
    [JsonIgnore] public double GocRoiDo => Roi.GocDo;

    [JsonIgnore] public int MucThoNhatDungDuoc
    {
        get
        {
            for (int i = Muc.Count - 1; i >= 0; i--) if (Muc[i].DungDuoc) return i;
            return 0;
        }
    }

    // ---------------- Lưu / nạp ----------------
    //
    // Điểm được dẹp thành mảng float phẳng (x, y, gx, gy, x, y, ...) thay vì mảng struct.
    // Lý do thực dụng: System.Text.Json không tuần tự hoá field của struct nếu không bật
    // IncludeFields, mà bật lên thì lại phụ thuộc tên tham số của primary constructor.
    // Mảng phẳng không có gì để hỏng, và file nhỏ hơn khoảng ba lần.

    private sealed class MucDto
    {
        public int Muc { get; set; }
        public double TiLe { get; set; }
        public int Rong { get; set; }
        public int Cao { get; set; }
        public float BanKinh { get; set; }
        public double DonBayXoay { get; set; }
        public double NguongThap { get; set; }
        public double NguongCao { get; set; }
        public float[] Diem { get; set; } = [];
    }

    private sealed class ModelDto
    {
        public string TenAnhMau { get; set; } = "";
        public double[] Roi { get; set; } = [];
        public double[] VungTim { get; set; } = [];
        public List<double[]> Mask { get; set; } = [];
        public int Rong { get; set; }
        public int Cao { get; set; }

        /// <summary>Mặt nạ che L0: đóng bit → Deflate → base64. Rỗng = không che gì.</summary>
        public string MatNaChe { get; set; } = "";

        public List<MucDto> Muc { get; set; } = [];
    }

    // ---- Mặt nạ che: đóng bit rồi nén ----
    //
    // Một mẫu 816² là 666k pixel. Ghi thẳng mỗi pixel một số thì file JSON phình lên megabyte
    // và át hết phần điểm model. Đóng bit đưa xuống 83 KB, Deflate ăn nốt phần lớn chỗ đó vì
    // mặt nạ che là những mảng liền khối chứ không phải nhiễu.

    private static string NenMatNa(byte[] mask, int w, int h)
    {
        if (mask.Length == 0 || mask.Length != (long)w * h) return "";

        var goi = new byte[(mask.Length + 7) / 8];
        bool coGi = false;
        for (int i = 0; i < mask.Length; i++)
            if (mask[i] != 0) { goi[i >> 3] |= (byte)(1 << (i & 7)); coGi = true; }
        if (!coGi) return "";                       // không che gì thì đừng cất chuỗi rác

        using var ms = new MemoryStream();
        using (var nen = new DeflateStream(ms, CompressionLevel.Optimal, true))
            nen.Write(goi, 0, goi.Length);
        return Convert.ToBase64String(ms.ToArray());
    }

    /// <summary>Giải nén mặt nạ. Hỏng hoặc thiếu byte thì trả rỗng — model vẫn chạy, chỉ mất don't-care.</summary>
    private static byte[] GiaiMatNa(string b64, int w, int h)
    {
        if (string.IsNullOrEmpty(b64) || w <= 0 || h <= 0) return [];

        int n = w * h;
        var goi = new byte[(n + 7) / 8];
        try
        {
            using var ms = new MemoryStream(Convert.FromBase64String(b64));
            using var giai = new DeflateStream(ms, CompressionMode.Decompress);
            int doc = 0, lan;
            while (doc < goi.Length && (lan = giai.Read(goi, doc, goi.Length - doc)) > 0) doc += lan;
            if (doc != goi.Length) return [];
        }
        catch (Exception e) when (e is FormatException or InvalidDataException) { return []; }

        var ra = new byte[n];
        for (int i = 0; i < n; i++) ra[i] = (byte)((goi[i >> 3] >> (i & 7)) & 1);
        return ra;
    }

    private static readonly JsonSerializerOptions JsonOpt = new() { WriteIndented = true };

    private static double[] Dep(RectXoay r) => [r.Cx, r.Cy, r.Rong, r.Cao, r.GocDo];
    private static RectXoay Bung(double[] a) => new(a[0], a[1], a[2], a[3], a[4]);

    public void Luu(string duongDan)
    {
        var dto = new ModelDto
        {
            TenAnhMau = TenAnhMau,
            Roi = Dep(Roi),
            VungTim = Dep(VungTim),
            Mask = Mask.Select(Dep).ToList(),
            Rong = Rong,
            Cao = Cao,
            MatNaChe = NenMatNa(MatNaChe, Rong, Cao),
        };

        foreach (var m in Muc)
        {
            var phang = new float[m.Diem.Length * 4];
            for (int i = 0; i < m.Diem.Length; i++)
            {
                phang[i * 4 + 0] = m.Diem[i].X;
                phang[i * 4 + 1] = m.Diem[i].Y;
                phang[i * 4 + 2] = m.Diem[i].Gx;
                phang[i * 4 + 3] = m.Diem[i].Gy;
            }
            dto.Muc.Add(new MucDto
            {
                Muc = m.Muc,
                TiLe = m.TiLe,
                Rong = m.Rong,
                Cao = m.Cao,
                BanKinh = m.BanKinh,
                DonBayXoay = m.DonBayXoay,
                NguongThap = m.NguongThap,
                NguongCao = m.NguongCao,
                Diem = phang,
            });
        }

        File.WriteAllText(duongDan, JsonSerializer.Serialize(dto, JsonOpt));
    }

    public static PmModel Nap(string duongDan)
    {
        var dto = JsonSerializer.Deserialize<ModelDto>(File.ReadAllText(duongDan))
                  ?? throw new InvalidDataException($"Khong doc duoc model: {duongDan}");

        var mo = new PmModel
        {
            TenAnhMau = dto.TenAnhMau,
            Roi = dto.Roi.Length == 5 ? Bung(dto.Roi) : new RectXoay(),
            VungTim = dto.VungTim.Length == 5 ? Bung(dto.VungTim) : new RectXoay(),
            Mask = dto.Mask.Where(a => a.Length == 5).Select(Bung).ToList(),
            Rong = dto.Rong,
            Cao = dto.Cao,
            MatNaChe = GiaiMatNa(dto.MatNaChe, dto.Rong, dto.Cao),
        };

        foreach (var d in dto.Muc)
        {
            int n = d.Diem.Length / 4;
            var diem = new DiemModel[n];
            for (int i = 0; i < n; i++)
                diem[i] = new DiemModel(d.Diem[i * 4], d.Diem[i * 4 + 1], d.Diem[i * 4 + 2], d.Diem[i * 4 + 3]);

            mo.Muc.Add(new MucPm
            {
                Muc = d.Muc,
                TiLe = d.TiLe,
                Rong = d.Rong,
                Cao = d.Cao,
                BanKinh = d.BanKinh,
                DonBayXoay = d.DonBayXoay,
                NguongThap = d.NguongThap,
                NguongCao = d.NguongCao,
                Diem = diem,
            });
        }
        return mo;
    }
}

/// <summary>Một kết quả dò được — tương đương một <c>CogPMAlignResult</c>.</summary>
public sealed class KetQuaPm
{
    /// <summary>Tâm mẫu trên ảnh chạy, đơn vị pixel của ảnh gốc.</summary>
    public double X, Y;

    /// <summary>Góc của MẪU so với trục ảnh (θ của patch đã dựng thẳng).</summary>
    public double GocMauDo;

    /// <summary>Góc SO VỚI ẢNH MẪU — con số tương đương Angle của CogPMAlign.</summary>
    public double GocDo;

    /// <summary>
    /// COVERAGE — điểm khớp 0..1: model tìm lại được bao nhiêu phần cạnh của chính nó.
    /// Đã trừ mức nền ngẫu nhiên (0 với chấm điểm có dấu, 2/π với đường chạy cũ bỏ dấu).
    /// </summary>
    public double Diem;

    /// <summary>
    /// CLUTTER 0..1 — tỉ lệ pixel biên trong dấu chân của model mà model KHÔNG giải thích
    /// được. Nói cách khác: vùng này có bao nhiêu cạnh LẠ. Coverage cao + clutter cao là
    /// dấu hiệu kinh điển của bắt nhầm vào một vùng nhiều texture.
    /// </summary>
    public double Clutter;

    /// <summary>Điểm dùng để XẾP HẠNG: <see cref="Diem"/> − HeSoClutter × <see cref="Clutter"/>.</summary>
    public double DiemXep;

    public override string ToString() =>
        $"x={X,9:F2}  y={Y,9:F2}  goc={GocDo,8:F3}  diem={Diem:F3}  clutter={Clutter:F3}";
}
