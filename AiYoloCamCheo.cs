using System.Text.RegularExpressions;
using OpenCvSharp;
using OpenVinoSharp;

namespace A38.ImageCrop;

/// <summary>
/// Bản chạy YOLO11 detect (OpenVINO IR) theo KIỂU ĐỐI TƯỢNG, khác <see cref="AiYolo"/> ở chỗ
/// AiYolo là static nên cả chương trình chỉ ôm được đúng một model. Bài check nghiêng cần thêm
/// model thứ hai (thư mục modelAICamCheo, ảnh chụp cam chéo) chạy cùng lúc với model cũ, nên tách
/// ra lớp tạo được nhiều thể hiện. Code cũ của AiYolo giữ nguyên, không đụng tới.
///
/// Tiền xử lý y hệt AiYolo vì cùng lò Ultralytics xuất ra: letterbox giữ tỉ lệ + đệm xám 114 +
/// BGR->RGB + chia 255. Sai một trong bốn cái đó thì model vẫn ra số, nhưng là số rác.
/// </summary>
public sealed class AiYoloMay
{
    /// <summary>Thư mục chứa best.xml/best.bin/metadata.yaml. Tương đối thì dò từ chỗ đặt exe ngược lên cha.</summary>
    public string ThuMucModel;

    /// <summary>Tên file IR trong thư mục trên.</summary>
    public string TenFileIr = "best.xml";

    /// <summary>"CPU", "GPU", "AUTO". Máy không có GPU Intel thì để CPU.</summary>
    public string ThietBi = "CPU";

    /// <summary>Bỏ mọi khung dưới ngưỡng này.</summary>
    public float NguongTinCay = 0.4f;

    /// <summary>Ngưỡng IoU khi khử khung trùng (NMS).</summary>
    public float NguongIoU = 0.7f;

    private Core? _core;
    private CompiledModel? _model;
    private readonly object _khoa = new();

    /// <summary>Mỗi luồng một InferRequest: gọi infer() khi request đang bận thì OpenVINO ném "Infer Request is busy".</summary>
    private ThreadLocal<InferRequest>? _yeuCauTheoLuong;

    /// <summary>Buffer NCHW cấp một lần cho mỗi luồng rồi dùng lại, khỏi cấp lại mỗi ảnh.</summary>
    private ThreadLocal<float[]>? _demVaoTheoLuong;

    private int _canhVao = 640;
    private string[] _tenLop = Array.Empty<string>();
    private bool _daThuNap;

    public IReadOnlyList<string> TenLop => _tenLop;
    public int CanhVao => _canhVao;

    /// <summary>Đã nạp được model chưa (chỉ hỏi trạng thái, không tự đi nạp).</summary>
    public bool DaSan => _yeuCauTheoLuong is not null;

    public AiYoloMay(string thuMucModel) => ThuMucModel = thuMucModel;

    /// <summary>Nạp model một lần. false = không nạp được, pipeline cứ chạy tiếp không có AI.</summary>
    public bool Nap()
    {
        if (_yeuCauTheoLuong is not null) return true;

        lock (_khoa)
        {
            if (_yeuCauTheoLuong is not null) return true;
            if (_daThuNap) return false;
            _daThuNap = true;

            string? thuMuc = TimThuMucModel();
            if (thuMuc is null)
            {
                Dbg.Info($"  AI[{ThuMucModel}]: khong thay '{Path.Combine(ThuMucModel, TenFileIr)}', bo qua buoc AI.");
                return false;
            }

            string duongDanIr = Path.Combine(thuMuc, TenFileIr);
            DocMetadata(thuMuc, duongDanIr);

            try
            {
                _core = new Core();
                _model = _core.compile_model(duongDanIr, ThietBi);

                // Tạo thử một request ngay để model hỏng thì nổ ở đây, chỗ có log tử tế,
                // chứ không nổ giữa vòng lặp ảnh trong một luồng nào đó.
                _model.create_infer_request().Dispose();

                _yeuCauTheoLuong = new ThreadLocal<InferRequest>(
                    () => _model!.create_infer_request(), trackAllValues: true);
                _demVaoTheoLuong = new ThreadLocal<float[]>(() => new float[3 * _canhVao * _canhVao]);
            }
            catch (Exception ex)
            {
                Dbg.Info($"  AI[{ThuMucModel}]: nap model that bai ({ex.GetType().Name}: {ex.Message}), bo qua buoc AI.");
                _yeuCauTheoLuong = null;
                return false;
            }

            Dbg.Info($"  AI[{ThuMucModel}]: nap '{duongDanIr}' tren {ThietBi} | vao {_canhVao}x{_canhVao} | " +
                     $"{_tenLop.Length} lop [{string.Join(", ", _tenLop)}]");
            return true;
        }
    }

    /// <summary>
    /// Dò thư mục model: đường dẫn tuyệt đối thì lấy luôn, tương đối thì từ chỗ đặt exe
    /// (bin\Debug\net8.0) ngược lên các thư mục cha — nên để model ở bin hay ở gốc project đều gặp.
    /// </summary>
    private string? TimThuMucModel()
    {
        if (Path.IsPathRooted(ThuMucModel))
            return File.Exists(Path.Combine(ThuMucModel, TenFileIr)) ? ThuMucModel : null;

        var thuMuc = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && thuMuc is not null; i++, thuMuc = thuMuc.Parent)
        {
            string thu = Path.Combine(thuMuc.FullName, ThuMucModel);
            if (File.Exists(Path.Combine(thu, TenFileIr))) return thu;
        }
        return null;
    }

    /// <summary>Lấy imgsz + tên lớp: ưu tiên metadata.yaml, thiếu thì moi rt_info/labels trong best.xml.</summary>
    private void DocMetadata(string thuMuc, string duongDanIr)
    {
        string fileYaml = Path.Combine(thuMuc, "metadata.yaml");
        if (File.Exists(fileYaml))
        {
            string yaml = File.ReadAllText(fileYaml);

            // imgsz ghi kiểu list:  imgsz:\n  - 640\n  - 640
            var mSize = Regex.Match(yaml, @"imgsz:\s*(?:\r?\n\s*-\s*)?(\d+)");
            if (mSize.Success) _canhVao = int.Parse(mSize.Groups[1].Value);

            // names ghi kiểu:  names:\n  0: vungMLCC
            var mNames = Regex.Match(yaml, @"names:\s*\r?\n((?:\s+\d+:.*\r?\n?)+)");
            if (mNames.Success)
            {
                var cap = Regex.Matches(mNames.Groups[1].Value, @"(\d+)\s*:\s*['""]?([^'""\r\n]+?)['""]?\s*$",
                                        RegexOptions.Multiline);
                if (cap.Count > 0)
                {
                    _tenLop = new string[cap.Max(m => int.Parse(m.Groups[1].Value)) + 1];
                    for (int i = 0; i < _tenLop.Length; i++) _tenLop[i] = i.ToString();
                    foreach (Match m in cap) _tenLop[int.Parse(m.Groups[1].Value)] = m.Groups[2].Value;
                }
            }
        }

        if (_tenLop.Length > 0) return;

        try
        {
            string xml = File.ReadAllText(duongDanIr);
            var mLabels = Regex.Match(xml, @"<labels\s+value=""([^""]*)""");
            if (mLabels.Success)
                _tenLop = mLabels.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var mShape = Regex.Match(xml, @"shape=""1,\s*3,\s*(\d+),\s*(\d+)""");
            if (mShape.Success) _canhVao = int.Parse(mShape.Groups[1].Value);
        }
        catch { /* không đọc được thì dùng mặc định, không đáng để chết cả pipeline */ }
    }

    private string Ten(int i) => i >= 0 && i < _tenLop.Length ? _tenLop[i] : i.ToString();

    /// <summary>
    /// Chạy model trên một ảnh. Khung trả về đã quy về HỆ TOẠ ĐỘ ẢNH TRUYỀN VÀO, sắp theo độ tin
    /// cậy giảm dần. List rỗng = không nạp được model hoặc không có gì vượt ngưỡng.
    /// Gọi song song nhiều luồng được: mỗi luồng có InferRequest và buffer riêng.
    /// </summary>
    public List<AiKetQua> Chay(Mat anh)
    {
        var ketQua = new List<AiKetQua>();
        if (anh is null || anh.Empty() || !Nap()) return ketQua;

        // Model ăn 3 kênh; ảnh xám thì đổi sang BGR trước, chứ nhét thẳng là lệch layout.
        using var anh3Kenh = new Mat();
        Mat nguon = anh;
        if (anh.Channels() == 1)
        {
            Cv2.CvtColor(anh, anh3Kenh, ColorConversionCodes.GRAY2BGR);
            nguon = anh3Kenh;
        }

        // ---- Letterbox về cỡ vào của model, giữ nguyên tỉ lệ ----------------------
        // Crop 512 vào model 640 nên có phóng to; gain/pad tính đủ để toạ độ trả về vẫn
        // đúng hệ ảnh truyền vào dù sau này đổi cỡ crop.
        float gain = Math.Min((float)_canhVao / nguon.Width, (float)_canhVao / nguon.Height);
        int wMoi = (int)Math.Round(nguon.Width * gain);
        int hMoi = (int)Math.Round(nguon.Height * gain);
        int padX = (_canhVao - wMoi) / 2;
        int padY = (_canhVao - hMoi) / 2;

        using var vao = new Mat();
        if (wMoi == nguon.Width && hMoi == nguon.Height && padX == 0 && padY == 0)
        {
            nguon.CopyTo(vao);
        }
        else
        {
            using var thuNho = new Mat();
            Cv2.Resize(nguon, thuNho, new Size(wMoi, hMoi), 0, 0,
                       gain < 1 ? InterpolationFlags.Area : InterpolationFlags.Linear);
            Cv2.CopyMakeBorder(thuNho, vao, padY, _canhVao - hMoi - padY, padX, _canhVao - wMoi - padX,
                               BorderTypes.Constant, Config.MauDemLetterbox);
        }

        // BGR/HWC-byte -> RGB/CHW-float 0..1 (reverse_input_channels + scale 255 của rt_info).
        int soPx = _canhVao * _canhVao;
        var buf = _demVaoTheoLuong!.Value!;
        vao.GetArray(out Vec3b[] px);
        for (int i = 0; i < soPx; i++)
        {
            Vec3b p = px[i];                        // OpenCV giữ thứ tự B,G,R
            buf[i] = p.Item2 / 255f;                // R
            buf[soPx + i] = p.Item1 / 255f;         // G
            buf[2 * soPx + i] = p.Item0 / 255f;     // B
        }

        // ---- Suy luận -------------------------------------------------------------
        InferRequest yeuCau = _yeuCauTheoLuong!.Value!;
        using (Tensor tVao = yeuCau.get_input_tensor())
        {
            tVao.set_data(buf);
        }
        yeuCau.infer();

        float[] raw;
        int soPhanTu;
        using (Tensor tRa = yeuCau.get_output_tensor())
        {
            soPhanTu = (int)tRa.get_size();
            raw = tRa.get_data<float>(soPhanTu);
        }

        DocPhatHien(raw, soPhanTu, gain, padX, padY, ketQua);
        ketQua.Sort((a, b) => b.DoTinCay.CompareTo(a.DoTinCay));
        return ketQua;
    }

    /// <summary>
    /// Đầu ra YOLO11 detect: [1, 4+nc, N] — model này 1 lớp nên là [1, 5, N].
    /// Layout là [kênh, ô] chứ KHÔNG phải [ô, kênh]: ô i kênh c nằm ở raw[c*N + i].
    /// </summary>
    private void DocPhatHien(float[] raw, int soPhanTu, float gain, int padX, int padY,
                             List<AiKetQua> ketQua)
    {
        int soKenh = 4 + Math.Max(1, _tenLop.Length);
        int soO = soPhanTu / soKenh;
        if (soO <= 0 || soO * soKenh != soPhanTu)
        {
            Dbg.Info($"  AI[{ThuMucModel}]: dau ra {soPhanTu} phan tu khong chia het cho {soKenh} kenh — " +
                     "so lop trong metadata khong khop model.");
            return;
        }
        int soLop = soKenh - 4;

        var tho = new List<AiKetQua>();
        for (int i = 0; i < soO; i++)
        {
            // Lấy lớp điểm cao nhất của ô này trước; dưới ngưỡng thì bỏ luôn cho nhanh.
            int lopTot = -1;
            float diemTot = NguongTinCay;
            for (int c = 0; c < soLop; c++)
            {
                float d = raw[(4 + c) * soO + i];
                if (d > diemTot) { diemTot = d; lopTot = c; }
            }
            if (lopTot < 0) continue;

            float cx = raw[i], cy = raw[soO + i], w = raw[2 * soO + i], h = raw[3 * soO + i];

            // Gỡ letterbox: trừ pad rồi chia gain là về đúng hệ ảnh truyền vào.
            tho.Add(new AiKetQua
            {
                ChiSoLop = lopTot,
                TenLop = Ten(lopTot),
                DoTinCay = diemTot,
                Khung = new Rect2f((cx - w / 2 - padX) / gain, (cy - h / 2 - padY) / gain,
                                   w / gain, h / gain)
            });
        }

        ketQua.AddRange(KhuKhungTrung(tho));
    }

    /// <summary>NMS greedy theo từng lớp. Sau ngưỡng chỉ còn dăm chục khung nên O(n²) thừa nhanh.</summary>
    private List<AiKetQua> KhuKhungTrung(List<AiKetQua> vao)
    {
        var giu = new List<AiKetQua>();
        foreach (var nhom in vao.GroupBy(k => k.ChiSoLop))
        {
            var xep = nhom.OrderByDescending(k => k.DoTinCay).ToList();
            while (xep.Count > 0)
            {
                var tot = xep[0];
                giu.Add(tot);
                xep.RemoveAt(0);
                xep.RemoveAll(k => IoU(tot.Khung, k.Khung) > NguongIoU);
            }
        }
        return giu;
    }

    private static float IoU(Rect2f a, Rect2f b)
    {
        float x1 = Math.Max(a.X, b.X), y1 = Math.Max(a.Y, b.Y);
        float x2 = Math.Min(a.X + a.Width, b.X + b.Width);
        float y2 = Math.Min(a.Y + a.Height, b.Y + b.Height);
        float giao = Math.Max(0, x2 - x1) * Math.Max(0, y2 - y1);
        float hop = a.Width * a.Height + b.Width * b.Height - giao;
        return hop <= 0 ? 0 : giao / hop;
    }

    /// <summary>Gọi khi thoát chương trình. Không gọi cũng không sao, process chết là dọn theo.</summary>
    public void Dong()
    {
        if (_yeuCauTheoLuong is not null)
        {
            foreach (var r in _yeuCauTheoLuong.Values) r?.Dispose();
            _yeuCauTheoLuong.Dispose();
            _yeuCauTheoLuong = null;
        }
        _demVaoTheoLuong?.Dispose(); _demVaoTheoLuong = null;
        _model?.Dispose(); _model = null;
        _core?.Dispose(); _core = null;
    }
}

/// <summary>
/// Model của bài ẢNH CHỤP CAM CHÉO: thư mục modelAICamCheo, 1 lớp "vungMLCC", vào 640x640.
/// Bọc static cho gọn chỗ gọi, bên trong vẫn là một <see cref="AiYoloMay"/> — muốn thêm model
/// thứ ba thì new thêm một cái nữa, không phải copy lại cả lớp.
/// </summary>
public static class AiYoloCamCheo
{
    /// <summary>Đổi sang đường dẫn tuyệt đối cũng được, nhưng phải đổi TRƯỚC lần chạy đầu tiên.</summary>
    public static readonly AiYoloMay May = new("modelAICamCheo");

    /// <summary>
    /// Kết quả lần chạy gần nhất CỦA LUỒNG HIỆN TẠI — tiện lấy ở hàm khác mà không phải truyền
    /// qua tham số. Để theo luồng vì pipeline chạy song song Config.SoLuong luồng.
    /// </summary>
    public static IReadOnlyList<AiKetQua> KetQuaGanNhat =>
        (IReadOnlyList<AiKetQua>?)_ketQuaGanNhat ?? Array.Empty<AiKetQua>();

    [ThreadStatic] private static List<AiKetQua>? _ketQuaGanNhat;

    public static bool Nap() => May.Nap();

    public static List<AiKetQua> Chay(Mat anh)
    {
        var kq = May.Chay(anh);
        _ketQuaGanNhat = kq;
        return kq;
    }

    /// <summary>Vẽ khung + tên lớp + độ tin cậy + tâm khung lên BẢN SAO của ảnh. Người gọi tự dispose ảnh trả về.</summary>
    public static Mat Ve(Mat anh, IReadOnlyList<AiKetQua> ketQua)
    {
        Mat ve;
        if (anh.Channels() == 1)
        {
            ve = new Mat();
            Cv2.CvtColor(anh, ve, ColorConversionCodes.GRAY2BGR);
        }
        else ve = anh.Clone();

        foreach (var kq in ketQua)
        {
            Cv2.Rectangle(ve, kq.KhungInt, Scalar.Lime, 2);
            Cv2.PutText(ve, $"{kq.TenLop} {kq.DoTinCay:0.00}",
                        new Point(kq.KhungInt.X, Math.Max(12, kq.KhungInt.Y - 4)),
                        HersheyFonts.HersheySimplex, 0.5, Scalar.Lime, 1);
            Cv2.DrawMarker(ve, new Point((int)kq.Tam.X, (int)kq.Tam.Y), Scalar.Magenta,
                           MarkerTypes.Cross, 15, 2);
            //Dbg.Show(ve, "ve");
        }
        return ve;
    }

    public static void Dong() => May.Dong();
}
