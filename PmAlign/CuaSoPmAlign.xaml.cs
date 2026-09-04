using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
// System.Windows.Shapes bi go khoi implicit using (xem ProjectAlign.csproj) vi no che mat
// System.IO.Path. Lay dich danh tung hinh can dung.
using Ellipse = System.Windows.Shapes.Ellipse;
using Line = System.Windows.Shapes.Line;
using Polygon = System.Windows.Shapes.Polygon;
using Rectangle = System.Windows.Shapes.Rectangle;
using Cv2 = OpenCvSharp.Cv2;
using ImreadModes = OpenCvSharp.ImreadModes;
using Mat = OpenCvSharp.Mat;
using Vec3b = OpenCvSharp.Vec3b;

namespace A38.ImageCrop.PmAlign;

/// <summary>
/// Giao diện tool dò mẫu, dựng theo đúng trình tự làm việc của CogPMAlign:
///
///   mở ảnh  →  khoanh VÙNG TÌM KIẾM (thô, giới hạn nơi tâm vật được phép nằm)
///           →  khoanh ROI MẪU và XOAY nó cho vừa khít vật
///           →  Train  →  Run
///
/// Ba hình khoanh đều là <see cref="RectXoay"/>, tức là đều xoay 360° được, giống
/// <c>CogRectangleAffine</c>. Vùng che (don-care) cũng vậy.
/// </summary>
public partial class CuaSoPmAlign : Window
{
    // ---------------- Trạng thái ----------------

    private Mat? _anh;
    private string _duongDanAnh = "";
    private Mat? _mauMau;                       // miếng mẫu MÀU đã dựng thẳng, giữ để soi model

    private RectXoay _vungTim, _roi;
    private readonly List<RectXoay> _mask = [];
    private int _iMask = -1;

    private PmModel? _model;
    private List<KetQuaPm> _ketQua = [];
    private readonly PmCfg _cfg = new();

    private bool _sanSang;

    // ---------------- Xem ảnh ----------------

    private readonly MatrixTransform _bd = new();
    private double _ty = 1, _ox, _oy;

    // ---------------- Thao tác chuột ----------------

    private enum Che { Tim, Roi, Mask }
    private Che _che = Che.Tim;

    private enum TT { Khong, Ve, DiChuyen, KeoGoc, Xoay, Truot }
    private TT _tt = TT.Khong;

    private int _numGoc = -1;
    private Point _neoAnh;                      // điểm neo, toạ độ ẢNH
    private RectXoay _hinhLucBam;
    private Point _neoManHinh;
    private double _oxLucBam, _oyLucBam;

    // ---------------- Bút vẽ ----------------

    private static readonly Brush MauTim = new SolidColorBrush(Color.FromRgb(0x3F, 0xD0, 0xFF));
    private static readonly Brush MauRoi = new SolidColorBrush(Color.FromRgb(0x5C, 0xE6, 0x5C));
    private static readonly Brush MauChe = new SolidColorBrush(Color.FromRgb(0xFF, 0x60, 0x60));
    private static readonly Brush MauKq = new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0x2E));
    private static readonly Brush NenChe = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0x40, 0x40));

    public CuaSoPmAlign()
    {
        InitializeComponent();
        Toan.RenderTransform = _bd;
        DoCfgRaGiaoDien();
        KeyDown += CuaSo_KeyDown;
        _sanSang = true;
        Ghi("San sang. Mo mot tam anh de bat dau.");
        Ghi("Chuot: trai = ve/keo hinh · giua hoac phai = truot anh · con lan = phong to thu nho.");
        Ghi("Phim: F vua khung · 1 ty le 100% · Delete xoa vung che dang chon · mui ten trai/phai xoay 0.1 do.");
    }

    // ==========================================================================
    //  Ảnh
    // ==========================================================================

    private void MoAnh_Click(object sender, RoutedEventArgs e)
    {
        var hop = new OpenFileDialog
        {
            Title = "Chon anh",
            Filter = "Anh|*.bmp;*.png;*.jpg;*.jpeg;*.tif;*.tiff|Tat ca|*.*",
        };
        if (hop.ShowDialog(this) != true) return;
        NapAnh(hop.FileName);
    }

    /// <summary>Mở sẵn một ảnh truyền từ dòng lệnh.</summary>
    public void MoAnhTuNgoai(string duongDan) => NapAnh(duongDan);

    private void NapAnh(string duongDan)
    {
        try
        {
            // Đọc bằng ImDecode chứ không ImRead: ImRead của OpenCV gọi fopen theo ANSI nên
            // đường dẫn có dấu tiếng Việt là trả về Mat rỗng mà không báo lỗi gì.
            var byteAnh = File.ReadAllBytes(duongDan);
            var moi = Cv2.ImDecode(byteAnh, ImreadModes.Color);
            if (moi.Empty()) { Bao($"Khong doc duoc anh:\n{duongDan}"); return; }

            _anh?.Dispose();
            _anh = moi;
            _duongDanAnh = duongDan;

            AnhXem.Source = PmVe.ToBitmap(_anh);
            AnhXem.Width = _anh.Width;
            AnhXem.Height = _anh.Height;

            _ketQua = [];
            DsKetQua.Items.Clear();
            _daVuaKhung = false;
            VuaKhung();
            Ghi($"Mo anh {Path.GetFileName(duongDan)}  {_anh.Width}x{_anh.Height}  {_anh.Channels()} kenh");
            TtThongTin.Text = $"{Path.GetFileName(duongDan)}  —  {_anh.Width}×{_anh.Height}";
        }
        catch (Exception ex) { Bao(ex.Message); }
    }

    // ==========================================================================
    //  Khung xem: phóng to, thu nhỏ, trượt
    // ==========================================================================

    private void CapNhatBienDoi()
    {
        _bd.Matrix = new Matrix(_ty, 0, 0, _ty, _ox, _oy);
        // Phong to thi xem tung pixel mot, thu nho thi phai loc, khong anh 3648 nhin nhu ram.
        RenderOptions.SetBitmapScalingMode(AnhXem, _ty >= 1 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        TtZoom.Text = $"{_ty * 100:F0}%";
        VeLai();
    }

    private void VuaKhung()
    {
        if (_anh == null || Vung.ActualWidth < 4) return;
        _daVuaKhung = true;
        _ty = Math.Min(Vung.ActualWidth / _anh.Width, Vung.ActualHeight / _anh.Height);
        _ox = (Vung.ActualWidth - _anh.Width * _ty) / 2;
        _oy = (Vung.ActualHeight - _anh.Height * _ty) / 2;
        CapNhatBienDoi();
    }

    private bool _daVuaKhung;

    private void Vung_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Anh mo tu dong lenh co the nap xong TRUOC khi khung xem co kich thuoc thuc,
        // luc do VuaKhung() thoat som. Fit lai o lan bao kich thuoc dau tien.
        if (_anh != null && !_daVuaKhung) VuaKhung();
    }

    private void Vung_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_anh == null) return;
        var p = e.GetPosition(Vung);
        double cu = _ty;
        _ty = Math.Clamp(_ty * (e.Delta > 0 ? 1.2 : 1 / 1.2), 0.01, 40);
        _ox = p.X - (p.X - _ox) * (_ty / cu);
        _oy = p.Y - (p.Y - _oy) * (_ty / cu);
        CapNhatBienDoi();
    }

    private Point VeAnh(Point manHinh) => new((manHinh.X - _ox) / _ty, (manHinh.Y - _oy) / _ty);

    // ==========================================================================
    //  Chuột: vẽ / di chuyển / kéo góc / xoay
    // ==========================================================================

    private void DoiChe(object sender, RoutedEventArgs e)
    {
        if (!_sanSang) return;
        _che = RdRoi.IsChecked == true ? Che.Roi : RdMask.IsChecked == true ? Che.Mask : Che.Tim;
        CapNhatOGoc();
        VeLai();
    }

    /// <summary>Hình đang được chỉnh sửa theo chế độ hiện tại.</summary>
    private RectXoay HinhHienHanh() => _che switch
    {
        Che.Roi => _roi,
        Che.Mask => _iMask >= 0 && _iMask < _mask.Count ? _mask[_iMask] : new RectXoay(),
        _ => _vungTim,
    };

    private void DatHinhHienHanh(RectXoay r)
    {
        switch (_che)
        {
            case Che.Roi: _roi = r; break;
            case Che.Mask: if (_iMask >= 0 && _iMask < _mask.Count) _mask[_iMask] = r; break;
            default: _vungTim = r; break;
        }
    }

    private void Vung_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_anh == null) return;
        Vung.Focus();
        var sp = e.GetPosition(Vung);

        if (e.ChangedButton is MouseButton.Middle or MouseButton.Right)
        {
            _tt = TT.Truot;
            _neoManHinh = sp;
            _oxLucBam = _ox; _oyLucBam = _oy;
            Vung.CaptureMouse();
            return;
        }
        if (e.ChangedButton != MouseButton.Left) return;

        var ip = VeAnh(sp);
        double dungSai = 8 / _ty;

        // Ở chế độ vùng che, bấm vào một hình che có sẵn thì chọn nó trước đã.
        if (_che == Che.Mask)
        {
            int trung = -1;
            for (int i = _mask.Count - 1; i >= 0; i--)
                if (_mask[i].Chua(ip.X, ip.Y) || NumTrung(_mask[i], ip, dungSai) >= 0) { trung = i; break; }
            if (trung >= 0 && trung != _iMask) { _iMask = trung; VeLai(); CapNhatOGoc(); }
        }

        var h = HinhHienHanh();
        if (h.HopLe)
        {
            int num = NumTrung(h, ip, dungSai);
            if (num == 4) { _tt = TT.Xoay; _hinhLucBam = h; Vung.CaptureMouse(); return; }
            if (num >= 0) { _tt = TT.KeoGoc; _numGoc = num; _hinhLucBam = h; Vung.CaptureMouse(); return; }
            if (h.Chua(ip.X, ip.Y))
            {
                _tt = TT.DiChuyen; _hinhLucBam = h; _neoAnh = ip;
                Vung.CaptureMouse();
                return;
            }
        }

        // Bấm ra chỗ trống = vẽ hình mới. Giữ nguyên góc đang có, để người dùng vẽ lại
        // ROI mà không mất công xoay lại từ đầu.
        double gocGiu = h.HopLe ? h.GocDo : 0;
        if (_che == Che.Mask) { _mask.Add(new RectXoay(ip.X, ip.Y, 0, 0, gocGiu)); _iMask = _mask.Count - 1; }
        else DatHinhHienHanh(new RectXoay(ip.X, ip.Y, 0, 0, gocGiu));

        _tt = TT.Ve;
        _neoAnh = ip;
        Vung.CaptureMouse();
    }

    private void Vung_MouseMove(object sender, MouseEventArgs e)
    {
        if (_anh == null) return;
        var sp = e.GetPosition(Vung);
        var ip = VeAnh(sp);

        int ix = (int)Math.Floor(ip.X), iy = (int)Math.Floor(ip.Y);
        if (ix >= 0 && iy >= 0 && ix < _anh.Width && iy < _anh.Height)
        {
            var px = _anh.At<Vec3b>(iy, ix);
            TtToaDo.Text = $"x={ix,5}  y={iy,5}   B={px.Item0,3} G={px.Item1,3} R={px.Item2,3}";
        }

        switch (_tt)
        {
            case TT.Truot:
                _ox = _oxLucBam + (sp.X - _neoManHinh.X);
                _oy = _oyLucBam + (sp.Y - _neoManHinh.Y);
                CapNhatBienDoi();
                return;

            case TT.Ve:
                DatHinhHienHanh(TuHaiDiem(_neoAnh, ip, HinhHienHanh().GocDo));
                break;

            case TT.DiChuyen:
                {
                    var h = _hinhLucBam;
                    h.Cx += ip.X - _neoAnh.X;
                    h.Cy += ip.Y - _neoAnh.Y;
                    DatHinhHienHanh(h);
                    break;
                }

            case TT.KeoGoc:
                {
                    // Giữ nguyên góc, giữ nguyên đỉnh ĐỐI DIỆN, kéo đỉnh đang cầm.
                    var d = _hinhLucBam.Dinh();
                    var co = d[(_numGoc + 2) % 4];
                    DatHinhHienHanh(TuHaiDiem(new Point(co.X, co.Y), ip, _hinhLucBam.GocDo));
                    break;
                }

            case TT.Xoay:
                {
                    var h = _hinhLucBam;
                    double vx = ip.X - h.Cx, vy = ip.Y - h.Cy;
                    if (vx * vx + vy * vy < 4) break;
                    // Núm xoay nằm ở hướng "lên" của hình, tức cục bộ (0, −1).
                    // Trong hệ ảnh, hướng đó là R(φ)·(0,−1) = (−sinφ, −cosφ).
                    h.GocDo = RectXoay.ChuanGoc(Math.Atan2(-vx, -vy) * 180.0 / Math.PI);
                    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                        h.GocDo = Math.Round(h.GocDo / 15.0) * 15.0;
                    DatHinhHienHanh(h);
                    CapNhatOGoc();
                    break;
                }

            default: return;
        }
        VeLai();
    }

    private void Vung_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_tt == TT.Khong) return;
        Vung.ReleaseMouseCapture();

        // Vẽ hụt (chỉ lỡ tay bấm một cái) thì bỏ, đừng để lại một hình rác 0×0.
        if (_tt is TT.Ve or TT.KeoGoc && !HinhHienHanh().HopLe)
        {
            if (_che == Che.Mask && _iMask >= 0) { _mask.RemoveAt(_iMask); _iMask = _mask.Count - 1; }
            else DatHinhHienHanh(new RectXoay());
        }

        _tt = TT.Khong;
        _numGoc = -1;
        CapNhatOGoc();
        VeLai();
        MoTaHinh();
    }

    /// <summary>Dựng hình từ hai đỉnh đối diện, giữ nguyên góc xoay.</summary>
    private static RectXoay TuHaiDiem(Point a, Point b, double gocDo)
    {
        double r = gocDo * Math.PI / 180.0, c = Math.Cos(r), s = Math.Sin(r);
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double ux = dx * c - dy * s;        // R(−φ)·(b − a)
        double uy = dx * s + dy * c;
        return new RectXoay((a.X + b.X) / 2, (a.Y + b.Y) / 2, Math.Abs(ux), Math.Abs(uy), gocDo);
    }

    /// <summary>−1 = không trúng núm nào, 0..3 = bốn đỉnh, 4 = núm xoay.</summary>
    private int NumTrung(RectXoay r, Point ip, double dungSai)
    {
        if (!r.HopLe) return -1;
        var (hx, hy) = r.VeAnh(0, -r.Cao / 2 - 26 / _ty);
        if (Math.Abs(ip.X - hx) <= dungSai && Math.Abs(ip.Y - hy) <= dungSai) return 4;

        var d = r.Dinh();
        for (int i = 0; i < 4; i++)
            if (Math.Abs(ip.X - d[i].X) <= dungSai && Math.Abs(ip.Y - d[i].Y) <= dungSai) return i;
        return -1;
    }

    private void CuaSo_KeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox) return;
        switch (e.Key)
        {
            case Key.F: VuaKhung(); break;
            case Key.D1: _ty = 1; CapNhatBienDoi(); break;
            case Key.Delete:
                if (_che == Che.Mask && _iMask >= 0 && _iMask < _mask.Count)
                { _mask.RemoveAt(_iMask); _iMask = _mask.Count - 1; VeLai(); }
                break;
            case Key.Left:
            case Key.Right:
                {
                    var h = HinhHienHanh();
                    if (!h.HopLe) break;
                    double b = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 1.0 : 0.1;
                    h.GocDo = RectXoay.ChuanGoc(h.GocDo + (e.Key == Key.Left ? -b : b));
                    DatHinhHienHanh(h);
                    CapNhatOGoc(); VeLai(); MoTaHinh();
                    break;
                }
        }
    }

    // ==========================================================================
    //  Ô góc trên thanh công cụ
    // ==========================================================================

    private bool _dangDongBoGoc;

    private void CapNhatOGoc()
    {
        var h = HinhHienHanh();
        _dangDongBoGoc = true;
        double g = RectXoay.ChuanGoc(h.GocDo);
        ONhapGoc.Text = g.ToString("F2", CultureInfo.InvariantCulture);
        ThanhGoc.Value = g;
        _dangDongBoGoc = false;
    }

    private void DatGoc(double g)
    {
        var h = HinhHienHanh();
        if (!h.HopLe) return;
        h.GocDo = RectXoay.ChuanGoc(g);
        DatHinhHienHanh(h);
        VeLai();
        MoTaHinh();
    }

    private void ThanhGoc_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_sanSang || _dangDongBoGoc) return;
        _dangDongBoGoc = true;
        ONhapGoc.Text = e.NewValue.ToString("F2", CultureInfo.InvariantCulture);
        _dangDongBoGoc = false;
        DatGoc(e.NewValue);
    }

    private void ONhapGoc_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (!double.TryParse(ONhapGoc.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double g)) return;
        DatGoc(g);
        CapNhatOGoc();
    }

    private void GocNhanh_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string s) return;
        DatGoc(double.Parse(s, CultureInfo.InvariantCulture));
        CapNhatOGoc();
    }

    private void XoaMask_Click(object sender, RoutedEventArgs e)
    {
        _mask.Clear();
        _iMask = -1;
        VeLai();
        Ghi("Da xoa het vung che.");
    }

    private void VeLai_Click(object sender, RoutedEventArgs e) => VeLai();

    // ==========================================================================
    //  Vẽ lớp phủ
    // ==========================================================================

    private void VeLai()
    {
        if (!_sanSang) return;
        while (Toan.Children.Count > 1) Toan.Children.RemoveAt(1);
        if (_anh == null) return;

        // vùng che vẽ trước để hai hình kia nằm trên
        for (int i = 0; i < _mask.Count; i++)
            ThemDaGiac(_mask[i], MauChe, i == _iMask && _che == Che.Mask ? 2.0 : 1.2, NenChe);

        if (_vungTim.HopLe) ThemDaGiac(_vungTim, MauTim, _che == Che.Tim ? 2.0 : 1.2, null, [6, 4]);
        if (_roi.HopLe) ThemDaGiac(_roi, MauRoi, _che == Che.Roi ? 2.0 : 1.2);

        VeKetQua();

        var h = HinhHienHanh();
        if (h.HopLe) ThemNum(h, _che switch { Che.Roi => MauRoi, Che.Mask => MauChe, _ => MauTim });
    }

    private void ThemDaGiac(RectXoay r, Brush vien, double day, Brush? to = null, DoubleCollection? net = null)
    {
        if (!r.HopLe) return;
        var p = new Polygon
        {
            Stroke = vien,
            StrokeThickness = day / _ty,
            Fill = to,
            Points = new PointCollection(r.Dinh().Select(d => new Point(d.X, d.Y))),
        };
        if (net != null) p.StrokeDashArray = net;
        Toan.Children.Add(p);
    }

    private void ThemNum(RectXoay r, Brush mau)
    {
        foreach (var d in r.Dinh()) ThemO(d.X, d.Y, mau);

        var (tx, ty) = r.VeAnh(0, -r.Cao / 2);
        var (hx, hy) = r.VeAnh(0, -r.Cao / 2 - 26 / _ty);
        Toan.Children.Add(new Line
        {
            X1 = tx, Y1 = ty, X2 = hx, Y2 = hy,
            Stroke = mau, StrokeThickness = 1.4 / _ty,
        });
        double s = 11 / _ty;
        var e = new Ellipse
        {
            Width = s, Height = s, Fill = mau,
            Stroke = Brushes.Black, StrokeThickness = 1 / _ty,
        };
        Canvas.SetLeft(e, hx - s / 2);
        Canvas.SetTop(e, hy - s / 2);
        Toan.Children.Add(e);

        // gạch chéo đánh dấu cạnh trên, để nhìn là biết hình đang quay hướng nào
        var (ax, ay) = r.VeAnh(-r.Rong / 2, -r.Cao / 2);
        var (bx, by) = r.VeAnh(r.Rong / 2, -r.Cao / 2);
        Toan.Children.Add(new Line
        {
            X1 = ax, Y1 = ay, X2 = bx, Y2 = by,
            Stroke = mau, StrokeThickness = 3.0 / _ty, Opacity = 0.55,
        });
    }

    private void ThemO(double x, double y, Brush mau)
    {
        double s = 9 / _ty;
        var o = new Rectangle
        {
            Width = s, Height = s, Fill = mau,
            Stroke = Brushes.Black, StrokeThickness = 1 / _ty,
        };
        Canvas.SetLeft(o, x - s / 2);
        Canvas.SetTop(o, y - s / 2);
        Toan.Children.Add(o);
    }

    private void VeKetQua()
    {
        if (_model == null || _ketQua.Count == 0) return;

        foreach (var k in _ketQua)
        {
            var khung = new RectXoay(k.X, k.Y, _model.Rong, _model.Cao, k.GocMauDo);
            ThemDaGiac(khung, MauKq, 2.0);

            double l = 14 / _ty;
            Toan.Children.Add(new Line { X1 = k.X - l, Y1 = k.Y, X2 = k.X + l, Y2 = k.Y, Stroke = MauKq, StrokeThickness = 1.6 / _ty });
            Toan.Children.Add(new Line { X1 = k.X, Y1 = k.Y - l, X2 = k.X, Y2 = k.Y + l, Stroke = MauKq, StrokeThickness = 1.6 / _ty });

            if (CbVeDiem.IsChecked != true) continue;

            // Vẽ chính bộ điểm mà engine đã chấm, tại đúng tư thế tìm được. Điểm nào nằm
            // trên đường viền vật thì model khớp thật; lệch ra ngoài là khớp nhầm.
            var m = _model.Muc[0];
            double r = k.GocMauDo * Math.PI / 180.0, c = Math.Cos(r), s = Math.Sin(r);
            int buoc = Math.Max(1, m.Diem.Length / 400);
            double d = 2.4 / _ty;
            for (int i = 0; i < m.Diem.Length; i += buoc)
            {
                var p = m.Diem[i];
                double px = k.X + p.X * c + p.Y * s;
                double py = k.Y - p.X * s + p.Y * c;
                var o = new Ellipse { Width = d, Height = d, Fill = MauKq };
                Canvas.SetLeft(o, px - d / 2);
                Canvas.SetTop(o, py - d / 2);
                Toan.Children.Add(o);
            }
        }
    }

    private void MoTaHinh()
    {
        var h = HinhHienHanh();
        if (!h.HopLe) return;
        string ten = _che switch { Che.Roi => "ROI mau", Che.Mask => "Vung che", _ => "Vung tim kiem" };
        TtThongTin.Text = $"{ten}: {h}";
    }

    // ==========================================================================
    //  Train / Run
    // ==========================================================================

    private async void Train_Click(object sender, RoutedEventArgs e)
    {
        if (_anh == null) { Bao("Chua mo anh."); return; }
        if (!_roi.HopLe) { Bao("Chua khoanh ROI mau.\n\nChon 'ROI mau' tren thanh cong cu roi keo chuot tren anh."); return; }

        DocCfgTuGiaoDien();
        var anh = _anh;
        var roi = _roi;
        var mask = _mask.ToList();
        var cfg = _cfg.Sao();
        var nhat = new List<string>();

        BatNut(false);
        try
        {
            var dh = Stopwatch.StartNew();
            var (model, mau) = await Task.Run(() =>
            {
                var m = PmEngine.Train(anh, roi, mask, cfg, s => nhat.Add(s));
                return (m, PmEngine.CatMau(anh, roi));
            });
            dh.Stop();

            _model = model;
            _model.TenAnhMau = Path.GetFileName(_duongDanAnh);
            _mauMau?.Dispose();
            _mauMau = mau;
            _ketQua = [];
            DsKetQua.Items.Clear();

            Ghi($"─── TRAIN {_model.TenAnhMau}  ROI {roi}  ({dh.ElapsedMilliseconds} ms) ───");
            foreach (var s in nhat) Ghi("  " + s);

            int tho = _model.MucThoNhatDungDuoc;
            Ghi($"  Muc tho nhat dung duoc: L{tho}, buoc goc {_model.Muc[tho].BuocGocDo:F2} do, " +
                $"ti le don bay {_model.Muc[tho].TiLeDonBay:P0}");
            if (_model.Muc[tho].TiLeDonBay < 0.15)
                Ghi("  CANH BAO: don bay xoay rat thap — mau nay gan nhu tron xoay, goc se khong on dinh.");

            TtThongTin.Text = $"Train xong: {_model.Muc.Sum(m => m.Diem.Length)} diem / {_model.Muc.Count} muc, {dh.ElapsedMilliseconds} ms";
            VeLai();
        }
        catch (Exception ex) { Bao(ex.ToString()); }
        finally { BatNut(true); }
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (_anh == null) { Bao("Chua mo anh."); return; }
        if (_model == null) { Bao("Chua co model. Bam Train, hoac Nap model tu file."); return; }

        DocCfgTuGiaoDien();
        var anh = _anh;
        var model = _model;
        var vt = _vungTim;
        var cfg = _cfg.Sao();
        var nhat = new List<string>();

        BatNut(false);
        try
        {
            var dh = Stopwatch.StartNew();
            var kq = await Task.Run(() => PmEngine.Run(anh, model, vt, cfg, s => nhat.Add(s)));
            dh.Stop();

            _ketQua = kq;
            DsKetQua.Items.Clear();
            foreach (var k in kq) DsKetQua.Items.Add(k.ToString());

            Ghi($"─── RUN  vung tim {(vt.HopLe ? vt.ToString() : "toan anh")}  " +
                $"goc {cfg.GocTuDo:F1}..{cfg.GocDenDo:F1}  ({dh.ElapsedMilliseconds} ms) ───");
            foreach (var s in nhat) Ghi("  " + s);
            if (kq.Count == 0) Ghi("  KHONG TIM THAY (khong co tu the nao dat diem toi thieu).");
            foreach (var k in kq) Ghi("  " + k);

            TtThongTin.Text = kq.Count == 0
                ? $"Run: khong tim thay ({dh.ElapsedMilliseconds} ms)"
                : $"Run: {kq.Count} ket qua, cao nhat {kq[0].Diem:F3} ({dh.ElapsedMilliseconds} ms)";
            VeLai();
        }
        catch (Exception ex) { Bao(ex.ToString()); }
        finally { BatNut(true); }
    }

    private void XemModel_Click(object sender, RoutedEventArgs e)
    {
        if (_model == null) { Bao("Chua co model."); return; }

        Mat? mau = _mauMau;
        bool tuCat = false;
        if (mau == null)
        {
            if (_anh == null) { Bao("Chua co anh de cat lai mieng mau."); return; }
            mau = PmEngine.CatMau(_anh, _model.Roi);
            tuCat = true;
        }

        try
        {
            using var bang = PmVe.VeMoiMuc(mau, _model);
            var cs = new Window
            {
                Title = $"Model — {_model.TenAnhMau}",
                Width = 760, Height = 900, Owner = this,
                Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x1B)),
                Content = new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = new Image { Source = PmVe.ToBitmap(bang), Stretch = Stretch.None },
                },
            };
            cs.Show();
        }
        finally { if (tuCat) mau.Dispose(); }
    }

    // ==========================================================================
    //  Lưu / nạp model
    // ==========================================================================

    private void LuuModel_Click(object sender, RoutedEventArgs e)
    {
        if (_model == null) { Bao("Chua co model de luu."); return; }
        var hop = new SaveFileDialog
        {
            Title = "Luu model",
            Filter = "Model PmAlign|*.pmm.json|JSON|*.json",
            FileName = (Path.GetFileNameWithoutExtension(_duongDanAnh) is { Length: > 0 } t ? t : "model") + ".pmm.json",
        };
        if (hop.ShowDialog(this) != true) return;
        try { _model.Luu(hop.FileName); Ghi($"Da luu model: {hop.FileName}"); }
        catch (Exception ex) { Bao(ex.Message); }
    }

    private void NapModel_Click(object sender, RoutedEventArgs e)
    {
        var hop = new OpenFileDialog { Title = "Nap model", Filter = "Model PmAlign|*.pmm.json;*.json|Tat ca|*.*" };
        if (hop.ShowDialog(this) != true) return;
        try
        {
            _model = PmModel.Nap(hop.FileName);
            _mauMau?.Dispose();
            _mauMau = null;

            // Khôi phục luôn ROI và vùng che đã train, để nhìn thấy model được học từ đâu.
            _roi = _model.Roi;
            _mask.Clear();
            _mask.AddRange(_model.Mask);
            _iMask = _mask.Count - 1;
            _ketQua = [];
            DsKetQua.Items.Clear();

            Ghi($"Da nap model: {hop.FileName}");
            Ghi($"  anh mau {_model.TenAnhMau}, mau {_model.Rong}x{_model.Cao}, ROI {_model.Roi}");
            foreach (var m in _model.Muc)
                Ghi($"  L{m.Muc} {m.Rong}x{m.Cao}  {m.Diem.Length} diem  buoc goc {m.BuocGocDo:F2} do");

            CapNhatOGoc();
            VeLai();
        }
        catch (Exception ex) { Bao(ex.Message); }
    }

    // ==========================================================================
    //  Tham số ↔ giao diện
    // ==========================================================================

    private void DoCfgRaGiaoDien()
    {
        TSoMuc.Text = _cfg.SoMuc.ToString();
        TBlur.Text = _cfg.BlurKernel.ToString();
        THeSoBien.Text = _cfg.HeSoMatDoBien.ToString("F1", CultureInfo.InvariantCulture);
        TKcDiem.Text = _cfg.KhoangCachDiem.ToString();
        TSoDiem.Text = _cfg.SoDiemToiDa.ToString();
        TBienMin.Text = _cfg.NguongBienToiThieu.ToString();

        CbThan.IsChecked = _cfg.ChiLayTrenThan;
        CbDong.IsChecked = _cfg.TuCheVungDong;
        TDongRB.Text = _cfg.NguongDongRB.ToString();
        TMoDong.Text = _cfg.MoVungDong.ToString();
        TNoiChe.Text = _cfg.NoiRongDongChe.ToString();

        TGocTu.Text = _cfg.GocTuDo.ToString("F1", CultureInfo.InvariantCulture);
        TGocDen.Text = _cfg.GocDenDo.ToString("F1", CultureInfo.InvariantCulture);
        TUngVien.Text = _cfg.SoUngVienDinh.ToString();
        TDiemMin.Text = _cfg.DiemToiThieu.ToString("F2", CultureInfo.InvariantCulture);
        TSoKq.Text = _cfg.SoKetQua.ToString();
        CbNoiSuy.IsChecked = _cfg.NoiSuyDuoiPixel;
    }

    private void DocCfgTuGiaoDien()
    {
        _cfg.SoMuc = Math.Clamp(Nguyen(TSoMuc, _cfg.SoMuc), 1, 10);
        _cfg.BlurKernel = Math.Clamp(Nguyen(TBlur, _cfg.BlurKernel), 1, 31);
        _cfg.HeSoMatDoBien = Math.Max(0.5, Thuc(THeSoBien, _cfg.HeSoMatDoBien));
        _cfg.KhoangCachDiem = Math.Clamp(Nguyen(TKcDiem, _cfg.KhoangCachDiem), 1, 40);
        _cfg.SoDiemToiDa = Math.Clamp(Nguyen(TSoDiem, _cfg.SoDiemToiDa), 8, 20000);
        _cfg.NguongBienToiThieu = Math.Clamp(Nguyen(TBienMin, _cfg.NguongBienToiThieu), 1, 255);

        _cfg.ChiLayTrenThan = CbThan.IsChecked == true;
        _cfg.TuCheVungDong = CbDong.IsChecked == true;
        _cfg.NguongDongRB = Math.Clamp(Nguyen(TDongRB, _cfg.NguongDongRB), 1, 255);
        _cfg.MoVungDong = Math.Clamp(Nguyen(TMoDong, _cfg.MoVungDong), 0, 50);
        _cfg.NoiRongDongChe = Math.Clamp(Nguyen(TNoiChe, _cfg.NoiRongDongChe), 0, 100);

        _cfg.GocTuDo = Math.Clamp(Thuc(TGocTu, _cfg.GocTuDo), -360, 360);
        _cfg.GocDenDo = Math.Clamp(Thuc(TGocDen, _cfg.GocDenDo), -360, 360);
        _cfg.SoUngVienDinh = Math.Clamp(Nguyen(TUngVien, _cfg.SoUngVienDinh), 1, 500);
        _cfg.DiemToiThieu = Math.Clamp(Thuc(TDiemMin, _cfg.DiemToiThieu), 0, 1);
        _cfg.SoKetQua = Math.Clamp(Nguyen(TSoKq, _cfg.SoKetQua), 1, 100);
        _cfg.NoiSuyDuoiPixel = CbNoiSuy.IsChecked == true;

        DoCfgRaGiaoDien();
    }

    private static int Nguyen(TextBox t, int mac) =>
        int.TryParse(t.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : mac;

    private static double Thuc(TextBox t, double mac) =>
        double.TryParse(t.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : mac;

    // ==========================================================================
    //  Vặt vãnh
    // ==========================================================================

    private void BatNut(bool bat)
    {
        NutTrain.IsEnabled = bat;
        NutRun.IsEnabled = bat;
        NutMoAnh.IsEnabled = bat;
        NutNapModel.IsEnabled = bat;
        NutLuuModel.IsEnabled = bat;
        Cursor = bat ? Cursors.Arrow : Cursors.Wait;
    }

    private void Ghi(string s)
    {
        Nhat.AppendText(s + Environment.NewLine);
        Nhat.ScrollToEnd();
    }

    private void Bao(string s) => MessageBox.Show(this, s, "PmAlign", MessageBoxButton.OK, MessageBoxImage.Warning);

    protected override void OnClosed(EventArgs e)
    {
        _anh?.Dispose();
        _mauMau?.Dispose();
        base.OnClosed(e);
    }
}
