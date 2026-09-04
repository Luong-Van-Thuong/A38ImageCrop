using OpenCvSharp;

namespace A38.ImageCrop.PmAlign;

/// <summary>
/// Hình chữ nhật XOAY ĐƯỢC — tương đương <c>CogRectangleAffine</c> của Cognex.
/// Dùng cho cả ba thứ người dùng khoanh: vùng tìm kiếm, ROI mẫu, và vùng che (mask).
///
/// QUY ƯỚC GÓC — chỉ có một, ghi ở đây, mọi chỗ khác tuân theo:
///   toạ độ ảnh có y HƯỚNG XUỐNG, góc dương = quay NGƯỢC chiều kim đồng hồ TRÊN MÀN HÌNH.
///   Ma trận quay tương ứng là  R(θ) = [[cosθ,  sinθ], [-sinθ, cosθ]].
///
/// Chọn đúng quy ước này vì nó TRÙNG với ma trận mà <c>Cv2.GetRotationMatrix2D</c> trả về
/// (α = cos, β = sin, hàng 2 là [-β, α]). Nhờ vậy phép warp về mẫu không phải đảo dấu ở
/// đâu cả — chỗ đảo dấu ngầm chính là chỗ hay sinh ra lỗi "góc lệch đúng một hằng số".
/// </summary>
public struct RectXoay(double cx, double cy, double rong, double cao, double gocDo)
{
    public double Cx = cx, Cy = cy, Rong = rong, Cao = cao, GocDo = gocDo;

    public RectXoay() : this(0, 0, 0, 0, 0) { }

    public readonly double Cos => Math.Cos(GocDo * Math.PI / 180.0);
    public readonly double Sin => Math.Sin(GocDo * Math.PI / 180.0);

    public readonly bool HopLe => Rong >= 2 && Cao >= 2;

    /// <summary>Toạ độ cục bộ (gốc ở TÂM hình) → toạ độ ảnh.</summary>
    public readonly (double X, double Y) VeAnh(double lx, double ly)
    {
        double c = Cos, s = Sin;
        return (Cx + lx * c + ly * s, Cy - lx * s + ly * c);
    }

    /// <summary>Toạ độ ảnh → toạ độ cục bộ (gốc ở TÂM hình). Là phép nghịch của <see cref="VeAnh"/>.</summary>
    public readonly (double X, double Y) VeCucBo(double px, double py)
    {
        double c = Cos, s = Sin, dx = px - Cx, dy = py - Cy;
        return (dx * c - dy * s, dx * s + dy * c);
    }

    /// <summary>Bốn đỉnh theo thứ tự trái-trên, phải-trên, phải-dưới, trái-dưới (trong hệ cục bộ).</summary>
    public readonly (double X, double Y)[] Dinh()
    {
        double a = Rong / 2, b = Cao / 2;
        return [VeAnh(-a, -b), VeAnh(a, -b), VeAnh(a, b), VeAnh(-a, b)];
    }

    /// <summary>Hộp bao thẳng trục, đã làm tròn ra ngoài.</summary>
    public readonly Rect BaoNgoai()
    {
        var d = Dinh();
        double x0 = d[0].X, x1 = d[0].X, y0 = d[0].Y, y1 = d[0].Y;
        foreach (var p in d)
        {
            x0 = Math.Min(x0, p.X); x1 = Math.Max(x1, p.X);
            y0 = Math.Min(y0, p.Y); y1 = Math.Max(y1, p.Y);
        }
        int ix = (int)Math.Floor(x0), iy = (int)Math.Floor(y0);
        return new Rect(ix, iy, (int)Math.Ceiling(x1) - ix, (int)Math.Ceiling(y1) - iy);
    }

    public readonly bool Chua(double px, double py)
    {
        var (lx, ly) = VeCucBo(px, py);
        return Math.Abs(lx) <= Rong / 2 && Math.Abs(ly) <= Cao / 2;
    }

    /// <summary>
    /// Ma trận affine 2×3 đưa TOẠ ĐỘ ẢNH về TOẠ ĐỘ MẪU (patch) kích thước <paramref name="w"/>×<paramref name="h"/>,
    /// tức là gỡ bỏ góc xoay của ROI và đặt tâm ROI vào giữa mẫu.
    ///
    /// Đây là mấu chốt của "ROI xoay được": model LUÔN được trích từ mẫu đã dựng thẳng,
    /// còn góc φ của ROI được cất riêng làm GỐC 0° của model.
    /// </summary>
    public readonly Mat MaTranVeMau(int w, int h)
    {
        var m = Cv2.GetRotationMatrix2D(new Point2f((float)Cx, (float)Cy), -GocDo, 1.0);
        m.Set(0, 2, m.At<double>(0, 2) + w / 2.0 - Cx);
        m.Set(1, 2, m.At<double>(1, 2) + h / 2.0 - Cy);
        return m;
    }

    /// <summary>Áp một affine 2×3 lên hình này, trả về hình mới (dùng để đưa mask sang hệ mẫu).</summary>
    public readonly Point2f[] DinhQuaAffine(Mat m)
    {
        double a00 = m.At<double>(0, 0), a01 = m.At<double>(0, 1), b0 = m.At<double>(0, 2);
        double a10 = m.At<double>(1, 0), a11 = m.At<double>(1, 1), b1 = m.At<double>(1, 2);
        var d = Dinh();
        var ra = new Point2f[d.Length];
        for (int i = 0; i < d.Length; i++)
            ra[i] = new Point2f((float)(a00 * d[i].X + a01 * d[i].Y + b0),
                                (float)(a10 * d[i].X + a11 * d[i].Y + b1));
        return ra;
    }

    /// <summary>Đưa góc về [0, 360).</summary>
    public static double ChuanGoc(double g) => ((g % 360) + 360) % 360;

    public override readonly string ToString() =>
        $"({Cx:F1}, {Cy:F1})  {Rong:F0}×{Cao:F0}  {ChuanGoc(GocDo):F2}°";
}
