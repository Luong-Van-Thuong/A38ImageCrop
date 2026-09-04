using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cv2 = OpenCvSharp.Cv2;
using CvSize = OpenCvSharp.Size;
using CvPoint = OpenCvSharp.Point;
using Mat = OpenCvSharp.Mat;
using MatType = OpenCvSharp.MatType;
using Scalar = OpenCvSharp.Scalar;
using Vec3b = OpenCvSharp.Vec3b;
using InterpolationFlags = OpenCvSharp.InterpolationFlags;
using ColorConversionCodes = OpenCvSharp.ColorConversionCodes;
using HersheyFonts = OpenCvSharp.HersheyFonts;
using LineTypes = OpenCvSharp.LineTypes;

namespace A38.ImageCrop.PmAlign;

/// <summary>Cầu nối OpenCV ↔ WPF, và bảng vẽ để soi model bằng mắt.</summary>
public static class PmVe
{
    /// <summary>
    /// Mat → BitmapSource. Ép về BGRA rồi copy một phát, thay vì tuần tự hoá qua PNG —
    /// ảnh 3648² mà mã hoá PNG mỗi lần vẽ lại thì giao diện đứng hình.
    /// </summary>
    public static BitmapSource ToBitmap(Mat src)
    {
        using var bgra = new Mat();
        switch (src.Channels())
        {
            case 1: Cv2.CvtColor(src, bgra, ColorConversionCodes.GRAY2BGRA); break;
            case 3: Cv2.CvtColor(src, bgra, ColorConversionCodes.BGR2BGRA); break;
            default: src.CopyTo(bgra); break;
        }

        int w = bgra.Width, h = bgra.Height, buoc = w * 4;
        var buf = new byte[buoc * h];
        System.Runtime.InteropServices.Marshal.Copy(bgra.Data, buf, 0, buf.Length);

        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, buf, buoc);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>
    /// Vẽ model của MỘT mức đè lên miếng mẫu: mỗi điểm một chấm + một vạch ngắn theo hướng
    /// gradient, màu mã hoá hướng. Nhìn ảnh này là biết model bám đúng đường viền hay đang
    /// bám vào vân bề mặt và nhiễu — đó là toàn bộ mục đích của nó.
    /// </summary>
    public static Mat VeMotMuc(Mat mauMau, MucPm m)
    {
        int phong = Math.Max(1, 700 / Math.Max(Math.Max(m.Rong, m.Cao), 1));
        var kt = new CvSize(m.Rong * phong, m.Cao * phong);

        var ve = new Mat();
        Cv2.Resize(mauMau, ve, kt, 0, 0, InterpolationFlags.Nearest);
        if (ve.Channels() == 1) Cv2.CvtColor(ve, ve, ColorConversionCodes.GRAY2BGR);

        using var lut = BangMauHuong();
        float cx = kt.Width / 2f, cy = kt.Height / 2f;
        int daiVach = Math.Max(3, phong * 2);
        int banKinhCham = Math.Max(1, phong / 2);

        foreach (var d in m.Diem)
        {
            float x = cx + d.X * phong, y = cy + d.Y * phong;
            double goc = Math.Atan2(d.Gy, d.Gx) * 180.0 / Math.PI;
            var mau = lut.Get<Vec3b>(0, (int)(((goc + 360) % 360) / 2));
            var sc = new Scalar(mau.Item0, mau.Item1, mau.Item2);

            Cv2.Line(ve, new CvPoint(x, y), new CvPoint(x + d.Gx * daiVach, y + d.Gy * daiVach),
                     sc, 1, LineTypes.AntiAlias);
            Cv2.Circle(ve, new CvPoint(x, y), banKinhCham, sc, -1, LineTypes.AntiAlias);
        }

        Cv2.PutText(ve, $"L{m.Muc}  1/{1 << m.Muc}  {m.Diem.Length} diem  r={m.BanKinh:F0}px  " +
                        $"don bay={m.DonBayXoay:F2}px/do  buoc goc={m.BuocGocDo:F2}deg",
                    new CvPoint(8, 22), HersheyFonts.HersheySimplex, 0.5, Scalar.Lime, 1, LineTypes.AntiAlias);
        return ve;
    }

    /// <summary>Ghép ảnh model của mọi mức thành một tấm để xem một lượt.</summary>
    public static Mat VeMoiMuc(Mat mauMau, PmModel model)
    {
        var tam = model.Muc.Where(m => m.Diem.Length > 0).Select(m => VeMotMuc(mauMau, m)).ToList();
        if (tam.Count == 0) return new Mat(new CvSize(320, 60), MatType.CV_8UC3, Scalar.All(40));

        int rong = tam.Max(t => t.Width);
        int cao = tam.Sum(t => t.Height) + 6 * (tam.Count - 1);
        var ra = new Mat(new CvSize(rong, cao), MatType.CV_8UC3, Scalar.All(30));

        int y = 0;
        foreach (var t in tam)
        {
            using var o = new Mat(ra, new OpenCvSharp.Rect(0, y, t.Width, t.Height));
            t.CopyTo(o);
            y += t.Height + 6;
            t.Dispose();
        }
        return ra;
    }

    /// <summary>Bảng 180 màu theo hướng gradient (hue của OpenCV chạy 0..179).</summary>
    private static Mat BangMauHuong()
    {
        using var hsv = new Mat(new CvSize(180, 1), MatType.CV_8UC3);
        for (int i = 0; i < 180; i++) hsv.Set(0, i, new Vec3b((byte)i, 255, 255));
        var bgr = new Mat();
        Cv2.CvtColor(hsv, bgr, ColorConversionCodes.HSV2BGR);
        return bgr;
    }
}
