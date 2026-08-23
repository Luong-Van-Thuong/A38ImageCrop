using System.Text.RegularExpressions;
using OpenCvSharp;
using OpenVinoSharp;

namespace A38.ImageCrop;

/// <summary>
/// Một khung model trả về. Toạ độ đã quy về hệ ẢNH TRUYỀN VÀO (ảnh crop 512),
/// không phải hệ ảnh vào của model — dùng thẳng để vẽ/đo, không phải scale lại.
/// </summary>
public sealed class AiKetQua
{
    public int ChiSoLop;
    public string TenLop = "";
    public float DoTinCay;

    /// <summary>Khung ngang, hệ toạ độ ảnh crop truyền vào.</summary>
    public Rect2f Khung;

    public Point2f Tam => new Point2f(Khung.X + Khung.Width / 2, Khung.Y + Khung.Height / 2);

    public Rect KhungInt => new Rect((int)Math.Round(Khung.X), (int)Math.Round(Khung.Y),
                                     (int)Math.Round(Khung.Width), (int)Math.Round(Khung.Height));

    public override string ToString() =>
        $"{TenLop}({ChiSoLop}) {DoTinCay:0.000} " +
        $"[{Khung.X:0.#},{Khung.Y:0.#} {Khung.Width:0.#}x{Khung.Height:0.#}]";
}

/// <summary>
/// Chạy model YOLO11 detect xuất sang OpenVINO IR (best.xml + best.bin + metadata.yaml)
/// nằm trong thư mục <see cref="ThuMucModel"/>.
///
/// Vì sao OpenVINO chứ không ONNX Runtime: thư mục modelAI chỉ có bản IR, không có
/// best.pt để export lại sang onnx. IR đọc thẳng được, khỏi phải convert vòng vèo.
///
/// Tiền xử lý phải khớp ĐÚNG với lúc train, đọc từ rt_info trong best.xml:
///   resize_type=fit_to_window_letterbox, pad_value=114, reverse_input_channels=YES, scale=255.
/// Tức là: letterbox giữ tỉ lệ, đệm xám 114, đổi BGR->RGB, chia 255. Sai một trong bốn
/// cái này thì model vẫn chạy, vẫn ra số, nhưng ra số rác — kiểu lỗi khó thấy nhất.
/// </summary>
public static class AiYolo
{
    /// <summary>
    /// Thư mục chứa best.xml/best.bin/metadata.yaml. Đường dẫn tương đối được dò lần lượt ở
    /// thư mục chạy exe rồi ngược lên các thư mục cha (bin\Debug\net8.0 -> gốc project).
    /// </summary>
    public static string ThuMucModel = "modelAIVungThanTu";

    /// <summary>Tên file IR trong thư mục trên (đổi tên model thì sửa đúng chỗ này).</summary>
    public static string TenFileIr = "best.xml";

    /// <summary>Thiết bị chạy: "CPU", "GPU", "AUTO". Máy không có GPU Intel thì để CPU.</summary>
    public static string ThietBi = "CPU";

    /// <summary>Bỏ mọi khung dưới ngưỡng này.</summary>
    public static float NguongTinCay = 0.4f;

    /// <summary>Ngưỡng IoU khi khử khung trùng (NMS). rt_info của model ghi 0.7.</summary>
    public static float NguongIoU = 0.7f;

    private static Core? _core;
    private static CompiledModel? _model;
    private static readonly object _khoa = new();

    /// <summary>
    /// Một InferRequest KHÔNG dùng chung được: gọi infer() khi request đang chạy thì
    /// OpenVINO ném "Infer Request is busy". Pipeline chạy Config.SoLuong luồng song song
    /// nên mỗi luồng giữ request riêng — đúng cách OpenVINO thiết kế, và không phải khoá
    /// nhau như khi bọc lock quanh cả lần suy luận.
    /// </summary>
    private static ThreadLocal<InferRequest>? _yeuCauTheoLuong;

    /// <summary>Buffer NCHW cấp một lần cho mỗi luồng rồi dùng lại, khỏi cấp 786KB mỗi ảnh.</summary>
    private static ThreadLocal<float[]>? _demVaoTheoLuong;

    private static int _canhVao = 512;
    private static string[] _tenLop = Array.Empty<string>();

    public static IReadOnlyList<string> TenLop => _tenLop;
    public static int CanhVao => _canhVao;

    /// <summary>
    /// Kết quả lần chạy gần nhất CỦA LUỒNG HIỆN TẠI — tiện lấy ở hàm khác mà không phải
    /// truyền qua tham số. Để theo luồng vì pipeline chạy song song, để static chung thì
    /// ảnh này đọc nhầm kết quả ảnh khác.
    /// </summary>
    public static IReadOnlyList<AiKetQua> KetQuaGanNhat =>
        (IReadOnlyList<AiKetQua>?)_ketQuaGanNhat ?? Array.Empty<AiKetQua>();

    [ThreadStatic] private static List<AiKetQua>? _ketQuaGanNhat;

    /// <summary>Đã thử nạp và thất bại thì thôi, đừng thử lại mỗi ảnh cho khỏi spam log.</summary>
    private static bool _daThuNap;

    /// <summary>Nạp model một lần. false = không nạp được, pipeline cứ chạy tiếp không có AI.</summary>
    public static bool Nap()
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
                Dbg.Info($"  AI: khong thay '{Path.Combine(ThuMucModel, TenFileIr)}', bo qua buoc AI.");
                return false;
            }

            string duongDanIr = Path.Combine(thuMuc, TenFileIr);
            DocMetadata(thuMuc, duongDanIr);

            try
            {
                _core = new Core();
                _model = _core.compile_model(duongDanIr, ThietBi);

                // Tạo thử một request ngay để lỗi model hỏng nổ ở đây, chỗ có log tử tế,
                // chứ không nổ giữa vòng lặp ảnh trong một luồng nào đó.
                _model.create_infer_request().Dispose();

                _yeuCauTheoLuong = new ThreadLocal<InferRequest>(
                    () => _model!.create_infer_request(), trackAllValues: true);
                _demVaoTheoLuong = new ThreadLocal<float[]>(() => new float[3 * _canhVao * _canhVao]);
            }
            catch (Exception ex)
            {
                Dbg.Info($"  AI: nap model that bai ({ex.GetType().Name}: {ex.Message}), bo qua buoc AI.");
                _yeuCauTheoLuong = null;
                return false;
            }

            Dbg.Info($"  AI: nap '{duongDanIr}' tren {ThietBi} | vao {_canhVao}x{_canhVao} | " +
                     $"{_tenLop.Length} lop [{string.Join(", ", _tenLop)}]");
            return true;
        }
    }

    /// <summary>
    /// Dò thư mục model: thử nguyên đường dẫn, rồi từ chỗ đặt exe ngược lên các cha.
    /// Exe chạy ở bin\Debug\net8.0 nên đường dẫn tương đối "modelAI" không tự khớp;
    /// đi ngược lên vài tầng là gặp, khỏi phải hard-code đường dẫn tuyệt đối.
    /// </summary>
    private static string? TimThuMucModel()
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

    /// <summary>
    /// Lấy imgsz và tên lớp. Ưu tiên metadata.yaml của Ultralytics; thiếu file đó thì
    /// lấy tạm rt_info/labels và shape Parameter ngay trong best.xml.
    /// </summary>
    private static void DocMetadata(string thuMuc, string duongDanIr)
    {
        string fileYaml = Path.Combine(thuMuc, "metadata.yaml");
        if (File.Exists(fileYaml))
        {
            string yaml = File.ReadAllText(fileYaml);

            // imgsz ghi kiểu list:  imgsz:\n  - 512\n  - 512
            var mSize = Regex.Match(yaml, @"imgsz:\s*(?:\r?\n\s*-\s*)?(\d+)");
            if (mSize.Success) _canhVao = int.Parse(mSize.Groups[1].Value);

            // names ghi kiểu:  names:\n  0: NG_MLCC\n  1: OK_MLCC
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

        // Dự phòng: <labels value="NG_MLCC OK_MLCC toaDo" /> trong rt_info của best.xml.
        // Chỉ đọc phần đuôi file cho khỏi nuốt cả trăm nghìn dòng layer.
        try
        {
            string xml = File.ReadAllText(duongDanIr);
            var mLabels = Regex.Match(xml, @"<labels\s+value=""([^""]*)""");
            if (mLabels.Success)
                _tenLop = mLabels.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var mShape = Regex.Match(xml, @"shape=""1,\s*3,\s*(\d+),\s*(\d+)""");
            if (mShape.Success) _canhVao = int.Parse(mShape.Groups[1].Value);
        }
        catch { /* không đọc được thì dùng mặc định 512, không đáng để chết cả pipeline */ }
    }

    private static string Ten(int i) => i >= 0 && i < _tenLop.Length ? _tenLop[i] : i.ToString();

    /// <summary>
    /// Chạy model trên ảnh crop. Trả về danh sách khung sắp theo độ tin cậy giảm dần;
    /// list rỗng = không nạp được model hoặc không có gì vượt ngưỡng.
    /// Gọi song song từ nhiều luồng được: mỗi luồng có InferRequest và buffer riêng.
    /// </summary>
    public static List<AiKetQua> Chay(Mat anh)
    {
        var ketQua = new List<AiKetQua>();
        if (anh is null || anh.Empty() || !Nap()) return ketQua;

        // ---- Letterbox về cỡ vào của model, giữ nguyên tỉ lệ ----------------------
        // Ảnh crop đã vuông 512 nên thường không phải nội suy lần nào, nhưng vẫn tính đủ
        // gain/pad để lỡ đổi Config.KhungChuanPx thì toạ độ trả về vẫn đúng.
        float gain = Math.Min((float)_canhVao / anh.Width, (float)_canhVao / anh.Height);
        int wMoi = (int)Math.Round(anh.Width * gain);
        int hMoi = (int)Math.Round(anh.Height * gain);
        int padX = (_canhVao - wMoi) / 2;
        int padY = (_canhVao - hMoi) / 2;

        using var vao = new Mat();
        if (wMoi == anh.Width && hMoi == anh.Height && padX == 0 && padY == 0)
        {
            anh.CopyTo(vao);
        }
        else
        {
            using var thuNho = new Mat();
            Cv2.Resize(anh, thuNho, new Size(wMoi, hMoi), 0, 0,
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
            Vec3b p = px[i];              // OpenCV giữ thứ tự B,G,R
            buf[i] = p.Item2 / 255f;      // R
            buf[soPx + i] = p.Item1 / 255f;      // G
            buf[2 * soPx + i] = p.Item0 / 255f;      // B
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
        _ketQuaGanNhat = ketQua;
        return ketQua;
    }

    /// <summary>
    /// Đầu ra YOLO11 detect: [1, 4+nc, N] — với model này là [1, 7, 5376].
    /// 4 hàng đầu là cx,cy,w,h tính bằng px ảnh model; nc hàng sau là điểm từng lớp.
    /// Layout là [kênh, ô] chứ KHÔNG phải [ô, kênh]: phần tử của ô i ở kênh c nằm ở raw[c*N + i].
    /// </summary>
    private static void DocPhatHien(float[] raw, int soPhanTu, float gain, int padX, int padY,
                                    List<AiKetQua> ketQua)
    {
        int soKenh = 4 + Math.Max(1, _tenLop.Length);
        int soO = soPhanTu / soKenh;
        if (soO <= 0 || soO * soKenh != soPhanTu)
        {
            Dbg.Info($"  AI: dau ra {soPhanTu} phan tu khong chia het cho {soKenh} kenh — " +
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

            // Gỡ letterbox: trừ pad rồi chia gain là về đúng hệ ảnh crop truyền vào.
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
    private static List<AiKetQua> KhuKhungTrung(List<AiKetQua> vao)
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
    public static void Dong()
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
}
