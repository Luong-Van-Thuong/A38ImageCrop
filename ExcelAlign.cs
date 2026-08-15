using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace A38.ImageCrop;

/// <summary>
/// Một dòng số liệu align của MỘT ảnh. Chỉ chứa số đã đo được, không tính toán gì thêm
/// — để sau này mở Excel lên sort/lọc thì cột nào cũng là số thật của ảnh đó.
/// </summary>
public sealed class DongAlign
{
    public string TenAnh = "";

    // kích thước ảnh gốc + tỉ lệ thu nhỏ khi xử lý (work = goc * Scale)
    public int RongAnhGoc;
    public int CaoAnhGoc;
    public double Scale;

    // contour ngoài cùng chọn được
    public int SoContour;
    public int ChiSoContourNgoai;
    public double DienTich;          // px trong ảnh work
    public double DienTichGoc;       // quy về px ảnh gốc
    public double ChuVi;
    public double ChuViGoc;
    public int SoDinhApprox;
    public double DoTron;            // 4*pi*A/P^2 : =1 la tron tuyet doi

    // hình biên ngoài: minAreaRect
    public double TamX, TamY;        // tâm rect trong ảnh work
    public double TamXGoc, TamYGoc;  // tâm rect quy về ảnh gốc
    public double RongRect, CaoRect;
    public double RongRectGoc, CaoRectGoc;
    public double GocRaw;            // minRect.Angle nguyen ban cua OpenCV
    public double GocThuc;           // sau khi cong 90 neu width < height
    public double TySoCanh;          // canh dai / canh ngan

    // đường tròn nhỏ nhất bao contour ngoài
    public double TronX, TronY, BanKinh;
    public double TronXGoc, TronYGoc, BanKinhGoc;

    public string GhiChu = "";
}

/// <summary>
/// Kho gom số liệu align của cả lượt chạy rồi xuất ra một file .xlsx.
///
/// Gom trước - ghi sau, vì pipeline chạy song song nhiều luồng: mỗi ảnh xong thì
/// <see cref="Them"/> (có khoá), đến cuối Main mới <see cref="XuatFile"/> đúng một lần.
/// </summary>
public static class ExcelAlign
{
    private static readonly object _khoa = new();
    private static readonly List<DongAlign> _cacDong = new();

    /// <summary>Thêm một dòng số liệu. An toàn khi gọi từ nhiều luồng.</summary>
    public static void Them(DongAlign dong)
    {
        lock (_khoa) _cacDong.Add(dong);
    }

    /// <summary>Số dòng đang giữ trong bộ nhớ.</summary>
    public static int SoDong
    {
        get { lock (_khoa) return _cacDong.Count; }
    }

    /// <summary>Xoá sạch, dùng khi muốn chạy nhiều lượt trong cùng một tiến trình.</summary>
    public static void Xoa()
    {
        lock (_khoa) _cacDong.Clear();
    }

    private static readonly string[] _tieuDe =
    {
        "Ten anh",
        "Rong anh goc", "Cao anh goc", "Scale",
        "So contour", "Chi so contour ngoai",
        "Dien tich (work)", "Dien tich (goc)",
        "Chu vi (work)", "Chu vi (goc)",
        "So dinh approx", "Do tron",
        "Tam X (work)", "Tam Y (work)", "Tam X (goc)", "Tam Y (goc)",
        "Rong rect (work)", "Cao rect (work)", "Rong rect (goc)", "Cao rect (goc)",
        "Goc raw", "Goc thuc", "Ty so canh",
        "Tron X (work)", "Tron Y (work)", "Ban kinh (work)",
        "Tron X (goc)", "Tron Y (goc)", "Ban kinh (goc)",
        "Ghi chu"
    };

    private static object?[] ThanhHang(DongAlign d) => new object?[]
    {
        d.TenAnh,
        d.RongAnhGoc, d.CaoAnhGoc, d.Scale,
        d.SoContour, d.ChiSoContourNgoai,
        d.DienTich, d.DienTichGoc,
        d.ChuVi, d.ChuViGoc,
        d.SoDinhApprox, d.DoTron,
        d.TamX, d.TamY, d.TamXGoc, d.TamYGoc,
        d.RongRect, d.CaoRect, d.RongRectGoc, d.CaoRectGoc,
        d.GocRaw, d.GocThuc, d.TySoCanh,
        d.TronX, d.TronY, d.BanKinh,
        d.TronXGoc, d.TronYGoc, d.BanKinhGoc,
        d.GhiChu
    };

    /// <summary>
    /// Ghi tất cả dòng đã gom ra file Excel, sắp theo tên ảnh cho lần chạy nào cũng cùng thứ tự.
    /// File đang mở trong Excel thì tự ghi sang tên có gắn giờ, không làm hỏng lượt chạy.
    /// Trả về đường dẫn đã ghi, hoặc null nếu không có dòng nào.
    /// </summary>
    public static string? XuatFile(string path)
    {
        List<DongAlign> banSao;
        lock (_khoa) banSao = _cacDong.OrderBy(d => d.TenAnh, StringComparer.OrdinalIgnoreCase).ToList();

        if (banSao.Count == 0) return null;

        var hang = banSao.Select(ThanhHang).ToList();

        try
        {
            ExcelXlsx.Ghi(path, "Align", _tieuDe, hang);
            return Path.GetFullPath(path);
        }
        catch (IOException)
        {
            var duPhong = Path.Combine(
                Path.GetDirectoryName(path) is { Length: > 0 } thuMuc ? thuMuc : ".",
                $"{Path.GetFileNameWithoutExtension(path)}_{DateTime.Now:yyyyMMdd_HHmmss}" +
                $"{Path.GetExtension(path)}");
            ExcelXlsx.Ghi(duPhong, "Align", _tieuDe, hang);
            return Path.GetFullPath(duPhong);
        }
    }
}

/// <summary>
/// Ghi file .xlsx tối giản, không cần thư viện ngoài.
///
/// .xlsx chỉ là một file zip chứa vài file XML, mà .NET có sẵn ZipArchive nên viết tay
/// gọn hơn là kéo thêm NuGet vào một project vision. Chuỗi ghi kiểu inlineStr nên
/// không cần sharedStrings, số ghi theo InvariantCulture nên máy để locale VN
/// (dấu phẩy thập phân) cũng không làm lệch file.
/// </summary>
public static class ExcelXlsx
{
    public static void Ghi(string path, string tenSheet, IReadOnlyList<string> tieuDe,
                           IReadOnlyList<object?[]> cacHang)
    {
        var thuMuc = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(thuMuc)) Directory.CreateDirectory(thuMuc);

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

        ThemPhan(zip, "[Content_Types].xml", ContentTypes());
        ThemPhan(zip, "_rels/.rels", RelsGoc());
        ThemPhan(zip, "xl/workbook.xml", Workbook(tenSheet));
        ThemPhan(zip, "xl/_rels/workbook.xml.rels", RelsWorkbook());
        ThemPhan(zip, "xl/styles.xml", Styles());
        ThemPhan(zip, "xl/worksheets/sheet1.xml", Sheet(tieuDe, cacHang));
    }

    private static void ThemPhan(ZipArchive zip, string ten, string noiDung)
    {
        var entry = zip.CreateEntry(ten, CompressionLevel.Optimal);
        using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        w.Write(noiDung);
    }

    private const string KhaiBao = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>";

    private static string ContentTypes() => KhaiBao +
        "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
        "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
        "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
        "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
        "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
        "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
        "</Types>";

    private static string RelsGoc() => KhaiBao +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
        "</Relationships>";

    private static string Workbook(string tenSheet) => KhaiBao +
        "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
        "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
        $"<sheets><sheet name=\"{Escape(TenSheetHopLe(tenSheet))}\" sheetId=\"1\" r:id=\"rId1\"/></sheets>" +
        "</workbook>";

    private static string RelsWorkbook() => KhaiBao +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
        "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
        "</Relationships>";

    /// <summary>Hai style thôi: 0 = thường, 1 = đậm (dùng cho hàng tiêu đề).</summary>
    private static string Styles() => KhaiBao +
        "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
        "<fonts count=\"2\">" +
        "<font><sz val=\"11\"/><name val=\"Calibri\"/></font>" +
        "<font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font>" +
        "</fonts>" +
        "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill>" +
        "<fill><patternFill patternType=\"gray125\"/></fill></fills>" +
        "<borders count=\"1\"><border/></borders>" +
        "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
        "<cellXfs count=\"2\">" +
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
        "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +
        "</cellXfs></styleSheet>";

    private static string Sheet(IReadOnlyList<string> tieuDe, IReadOnlyList<object?[]> cacHang)
    {
        var sb = new StringBuilder(1024 + cacHang.Count * 256);
        sb.Append(KhaiBao)
          .Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">")
          // đóng băng hàng tiêu đề: cuộn 300 dòng vẫn biết cột nào là cột nào
          .Append("<sheetViews><sheetView workbookViewId=\"0\">")
          .Append("<pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/>")
          .Append("</sheetView></sheetViews>")
          .Append("<sheetData>");

        sb.Append("<row r=\"1\">");
        for (int c = 0; c < tieuDe.Count; c++) sb.Append(OChu(c, 1, tieuDe[c], styleDam: true));
        sb.Append("</row>");

        for (int h = 0; h < cacHang.Count; h++)
        {
            int r = h + 2;
            sb.Append("<row r=\"").Append(r).Append("\">");
            var hang = cacHang[h];
            for (int c = 0; c < hang.Length; c++) sb.Append(O(c, r, hang[c]));
            sb.Append("</row>");
        }

        sb.Append("</sheetData>");

        if (tieuDe.Count > 0)
        {
            sb.Append("<autoFilter ref=\"A1:")
              .Append(TenCot(tieuDe.Count - 1)).Append(cacHang.Count + 1).Append("\"/>");
        }

        sb.Append("</worksheet>");
        return sb.ToString();
    }

    private static string O(int cot, int hang, object? giaTri)
    {
        if (giaTri is null) return "";

        if (giaTri is string s) return OChu(cot, hang, s, styleDam: false);

        double? so = giaTri switch
        {
            int v => v,
            long v => v,
            float v => v,
            double v => v,
            decimal v => (double)v,
            bool v => v ? 1 : 0,
            _ => null
        };

        if (so is null) return OChu(cot, hang, giaTri.ToString() ?? "", styleDam: false);

        // NaN / Infinity không phải số hợp lệ trong xlsx, ghi ra chữ cho khỏi hỏng file
        if (double.IsNaN(so.Value) || double.IsInfinity(so.Value))
            return OChu(cot, hang, so.Value.ToString(CultureInfo.InvariantCulture), styleDam: false);

        return $"<c r=\"{TenCot(cot)}{hang}\"><v>" +
               so.Value.ToString("R", CultureInfo.InvariantCulture) + "</v></c>";
    }

    private static string OChu(int cot, int hang, string giaTri, bool styleDam)
    {
        if (giaTri.Length == 0) return "";
        var style = styleDam ? " s=\"1\"" : "";
        return $"<c r=\"{TenCot(cot)}{hang}\"{style} t=\"inlineStr\"><is><t xml:space=\"preserve\">" +
               Escape(giaTri) + "</t></is></c>";
    }

    /// <summary>0 -&gt; A, 25 -&gt; Z, 26 -&gt; AA...</summary>
    private static string TenCot(int chiSo)
    {
        var sb = new StringBuilder(3);
        int n = chiSo;
        do
        {
            sb.Insert(0, (char)('A' + n % 26));
            n = n / 26 - 1;
        } while (n >= 0);
        return sb.ToString();
    }

    /// <summary>Tên sheet của Excel: tối đa 31 ký tự và cấm : \ / ? * [ ]</summary>
    private static string TenSheetHopLe(string ten)
    {
        var sb = new StringBuilder();
        foreach (var ch in ten)
            sb.Append(":\\/?*[]".Contains(ch) ? '_' : ch);
        var kq = sb.ToString().Trim();
        if (kq.Length == 0) kq = "Sheet1";
        return kq.Length > 31 ? kq[..31] : kq;
    }

    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
