using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Mat = OpenCvSharp.Mat;

namespace A38.ImageCrop.PmAlign;

/// <summary>
/// Cửa sổ soi từng bước biến đổi ảnh — thay hẳn cho <c>Cv2.ImShow</c> + <c>Cv2.WaitKey</c>.
///
/// Vì sao không dùng HighGUI: <see cref="CuaSoPmAlign"/> chạy Train/Run trong
/// <c>Task.Run</c>, mà cửa sổ HighGUI chỉ bơm được thông điệp trên đúng luồng đã tạo ra nó.
/// Hậu quả đo được: cửa sổ mọc ra sau cửa sổ WPF, <c>WaitKey(0)</c> ngồi chờ một phím không
/// bao giờ tới, nhìn y hệt treo — cộng thêm 21 file PNG mỗi lần Run là 26 giây.
///
/// Đổi lại còn được hai thứ HighGUI không có: xem NGƯỢC lại bước trước (ảnh giữ trong danh
/// sách chứ không vẽ xong là mất), và nút "Tiep" nhìn thấy được thay cho phím bấm mù.
/// </summary>
public partial class CuaSoDbg : Window
{
    /// <summary>Một bước đã chụp. <paramref name="Anh"/> đã Freeze nên qua luồng thoải mái.</summary>
    private sealed record Buoc(int So, string Ten, string SoLieu, BitmapSource Anh)
    {
        public override string ToString() => $"{So:00}  {Ten}";
    }

    private readonly List<Buoc> _ds = [];

    /// <summary>
    /// Trần số bước giữ lại. Ảnh đã thu về <see cref="Dbg.MaxDisplaySize"/> nên mỗi bước
    /// cỡ 3 MB; không có trần thì bấm Run vài chục lần là hết RAM.
    /// </summary>
    private const int TranBuoc = 200;

    // ---- Chốt chặn "dừng từng bước" ------------------------------------------
    //
    // Luồng xử lý (Task.Run) gọi ChoNguoiDungBam rồi ngồi đợi trên _cho; luồng UI mở khoá
    // khi người dùng bấm nút. volatile vì hai luồng đọc/ghi mà không qua khoá nào.

    private readonly ManualResetEventSlim _cho = new(false);
    private volatile bool _dungTungBuoc;
    private volatile bool _chayThang;
    private volatile bool _dangCho;

    public CuaSoDbg()
    {
        InitializeComponent();
    }

    // ==========================================================================
    //  Đường vào từ Dbg (gọi trên LUỒNG XỬ LÝ)
    // ==========================================================================

    /// <summary>
    /// Cắm vào <see cref="Dbg.Sink"/>. Chuyển Mat sang <see cref="BitmapSource"/> NGAY tại đây,
    /// trên luồng gọi: <c>Dbg.Show</c> huỷ Mat ngay sau khi hàm này trả về, nên không được
    /// hoãn việc đọc nó sang lời gọi Dispatcher.
    /// </summary>
    public void NhanAnh(Mat view, string ten, int so, string soLieu)
    {
        var bmp = PmVe.ToBitmap(view);          // ToBitmap đã Freeze → qua luồng được
        Dispatcher.InvokeAsync(() => Them(new Buoc(so, ten, soLieu, bmp)));
    }

    /// <summary>Cắm vào <see cref="Dbg.SinkChu"/>: mọi dòng Log/Info/Stats hiện ngay ở đây.</summary>
    public void NhanChu(string dong) => Dispatcher.InvokeAsync(() =>
    {
        if (Nhat.LineCount > 600) Nhat.Clear();
        Nhat.AppendText(dong + Environment.NewLine);
        Nhat.ScrollToEnd();
    });

    /// <summary>
    /// Cắm vào <see cref="Dbg.ChoBuoc"/>. CHẶN luồng xử lý cho tới khi bấm "Tiep".
    /// Trả về false = người dùng bấm "Chay thang", <c>Dbg</c> sẽ tự tắt debug.
    /// </summary>
    public bool ChoNguoiDungBam()
    {
        if (_chayThang) return false;
        if (!_dungTungBuoc) return true;

        _cho.Reset();
        _dangCho = true;
        Dispatcher.InvokeAsync(CapNhatNut);

        _cho.Wait();

        _dangCho = false;
        Dispatcher.InvokeAsync(CapNhatNut);
        return !_chayThang;
    }

    // ==========================================================================
    //  Đường vào từ cửa sổ chính (gọi trên LUỒNG UI)
    // ==========================================================================

    /// <summary>Gọi ngay trước mỗi lần Train/Run: dọn danh sách cũ và bật lại debug.</summary>
    public void BatDauLuot(string ten)
    {
        _chayThang = false;
        Dbg.Enabled = true;
        Dbg.Reset();

        _ds.Clear();
        DsBuoc.Items.Clear();
        AnhXem.Source = null;
        TtTrangThai.Text = $"Dang chay: {ten}";
        Nhat.AppendText($"───── {ten} ─────{Environment.NewLine}");
        Nhat.ScrollToEnd();
        CapNhatNut();
    }

    /// <summary>Xong một lượt Train/Run.</summary>
    public void XongLuot() => TtTrangThai.Text = $"Xong. {_ds.Count} buoc.";

    /// <summary>
    /// Thả luồng xử lý nếu nó đang bị chặn — gọi khi tắt debug hoặc đóng cửa sổ, nếu không
    /// thì Task.Run nằm chờ vĩnh viễn đúng cái lỗi mà cửa sổ này sinh ra để chữa.
    /// </summary>
    public void MoKhoa()
    {
        _chayThang = true;
        _dungTungBuoc = false;
        _cho.Set();
    }

    // ==========================================================================
    //  Nút bấm
    // ==========================================================================

    // CANH BAO cho ca ba ham duoi: WPF phat Checked/Unchecked NGAY TRONG InitializeComponent,
    // khi cay giao dien moi dung duoc mot nua. Cai o nam tren thanh cong cu, con AnhXem
    // nam cuoi file XAML, nen luc do field AnhXem VAN CON NULL. Bat buoc phai chan.
    // (Da tung lam cua so nay chet ngay trong constructor bang dung mot dong IsChecked="True".)

    private void CbDung_Doi(object sender, RoutedEventArgs e)
    {
        if (NutTiep == null) return;
        _dungTungBuoc = CbDung.IsChecked == true;
        if (!_dungTungBuoc) _cho.Set();     // đang chờ mà bỏ tick thì chạy tiếp luôn
        CapNhatNut();
    }

    private void Tiep_Click(object sender, RoutedEventArgs e) => _cho.Set();

    private void ChayThang_Click(object sender, RoutedEventArgs e)
    {
        _chayThang = true;
        _dungTungBuoc = false;
        CbDung.IsChecked = false;
        _cho.Set();
        CapNhatNut();
    }

    private void CbGhiPng_Doi(object sender, RoutedEventArgs e) => Dbg.SaveFile = CbGhiPng.IsChecked == true;

    private void CbVuaKhung_Doi(object sender, RoutedEventArgs e)
    {
        if (AnhXem == null) return;     // XAML dat san Stretch="Uniform", khop voi IsChecked="True"
        AnhXem.Stretch = CbVuaKhung.IsChecked == true
                         ? System.Windows.Media.Stretch.Uniform
                         : System.Windows.Media.Stretch.None;
    }

    private void Xoa_Click(object sender, RoutedEventArgs e)
    {
        _ds.Clear();
        DsBuoc.Items.Clear();
        AnhXem.Source = null;
        Nhat.Clear();
        TtTrangThai.Text = "San sang.";
    }

    private void DsBuoc_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DsBuoc.SelectedIndex < 0 || DsBuoc.SelectedIndex >= _ds.Count) return;
        var b = _ds[DsBuoc.SelectedIndex];
        AnhXem.Source = b.Anh;
        TtTrangThai.Text = b.SoLieu.Trim();
    }

    // ==========================================================================
    //  Nội bộ
    // ==========================================================================

    private void Them(Buoc b)
    {
        _ds.Add(b);
        DsBuoc.Items.Add(b);
        if (_ds.Count > TranBuoc) { _ds.RemoveAt(0); DsBuoc.Items.RemoveAt(0); }

        // Bám bước mới nhất, trừ khi người dùng đang cố ý xem lại một bước cũ.
        bool dangXemCuoi = DsBuoc.SelectedIndex < 0 || DsBuoc.SelectedIndex == DsBuoc.Items.Count - 2;
        if (dangXemCuoi)
        {
            DsBuoc.SelectedIndex = DsBuoc.Items.Count - 1;
            DsBuoc.ScrollIntoView(DsBuoc.SelectedItem);
        }
    }

    private void CapNhatNut()
    {
        NutTiep.IsEnabled = _dangCho;
        NutChayThang.IsEnabled = _dangCho;
        if (_dangCho) TtTrangThai.Text = "Dang cho — bam Tiep de di buoc ke tiep.";
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        MoKhoa();
        base.OnClosing(e);
    }
}
