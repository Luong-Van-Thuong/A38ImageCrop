using A38.ImageCrop;
using OpenCvSharp;
using System;
using System.Linq;

// =====================================================================================
//  PHAT HIEN PHAN NHO RA KHOI THAN VAT THE
//
//  Y tuong (khong dung FitLine):
//    A. Dung MASK DAC cua vat the:  Canny -> Close -> FloodFill nen -> dao nguoc
//    B. Tach phan nho:              residual = mask - opening(mask, dia ban kinh r)
//       (opening voi dia se xoa moi chi tiet ma dia ban kinh r khong lot vao duoc:
//        than to -> giu nguyen, phan nho manh -> bien mat -> hien ra trong residual)
//    C. Loc ung vien theo dien tich / minAreaRect / co dinh vao than khong
//
//  Bat bien voi xoay 360 do va vi tri bat dinh, vi dia tron doi xung hoan toan
//  va moi phep do deu dung minAreaRect thay vi boundingRect.
// =====================================================================================

public static class ProtrusionDetector
{
    // ---------------------------------------------------------------------------------
    // THAM SO
    // ---------------------------------------------------------------------------------
    public class Params
    {
        // --- A. Dung mask ---
        public int BlurSize = 5;            // kernel Gaussian (so le)
        public double CannyLowRatio = 0.5;  // low = ratio * nguong Otsu
        public int CloseSize = 3;           // kernel closing de va duong bien bi dut
        public int CloseIter = 2;

        // Sanity check: dien tich mask / dien tich ROI.
        // Neu ngoai khoang nay => bien chua khep kin, floodFill bi "ro" ra ngoai.
        public double MinObjectRatio = 0.03;
        public double MaxObjectRatio = 0.97;

        // --- B. Tach phan nho ---
        public int DiskRadius = 40;         // ~ 0.6 * be rong phan nho. THAM SO QUAN TRONG NHAT.
        public double OpenDownScale = 0.5;  // 1.0 = tat. Opening voi dia lon rat cham.

        // --- C. Loc ung vien ---
        public double MinAreaRatio = 0.0005; // theo dien tich ROI
        public double MaxAreaRatio = 0.10;
        public double MinAspect = 1.2;       // canh dai / canh ngan cua minAreaRect
        public double MaxAspect = 8.0;
        public int TouchDilate = 5;          // ban kinh kiem tra "co dinh vao than khong"
        public int BorderMargin = 3;         // bo ung vien sat mep ROI
        public int BboxPadding = 20;         // noi rong bbox tra ve
    }

    // ---------------------------------------------------------------------------------
    // KET QUA
    // ---------------------------------------------------------------------------------
    public class Result : IDisposable
    {
        public Rect BoundingBox;        // bbox truc toa do (da padding)
        public RotatedRect OrientedBox; // bbox xoay - DUNG CAI NAY de do kich thuoc
        public double Area;
        public Point[] Contour;
        public Mat ObjectMask;          // debug
        public Mat Residual;            // debug

        public void Dispose()
        {
            ObjectMask?.Dispose();
            Residual?.Dispose();
        }
    }

    // =================================================================================
    //  HAM CHINH
    // =================================================================================
    public static Result Detect(Mat roi, Params p = null, Action<Mat, string> debug = null)
    {
        if (roi == null || roi.Empty()) return null;
        p ??= new Params();

        double roiArea = (double)roi.Rows * roi.Cols;

        // ---------- 1. Chuyen xam ----------
        using var gray = ToGray(roi);

        // ---------- 2. Blur + Canny voi nguong tu dong theo Otsu ----------
        using var blurred = new Mat();
        Cv2.GaussianBlur(gray, blurred, new Size(p.BlurSize, p.BlurSize), 0);

        using var otsuTmp = new Mat();
        double high = Cv2.Threshold(blurred, otsuTmp, 0, 255,
                                    ThresholdTypes.Binary | ThresholdTypes.Otsu);
        using var edges = new Mat();
        Cv2.Canny(blurred, edges, high * p.CannyLowRatio, high);
        debug?.Invoke(edges, "1_Canny");

        // ---------- 3. Closing de khep kin duong bien ----------
        using var closeK = Cv2.GetStructuringElement(
            MorphShapes.Ellipse, new Size(p.CloseSize, p.CloseSize));
        using var closed = new Mat();
        Cv2.MorphologyEx(edges, closed, MorphTypes.Close, closeK, iterations: p.CloseIter);
        debug?.Invoke(closed, "2_Closed");

        // ---------- 4. FloodFill nen -> mask dac ----------
        using var rawMask = BuildSolidMask(closed);
        debug?.Invoke(rawMask, "3_MaskRaw");

        double ratio = Cv2.CountNonZero(rawMask) / roiArea;
        if (ratio < p.MinObjectRatio || ratio > p.MaxObjectRatio)
            return null;   // bien chua kin -> tang CloseIter/CloseSize hoac ha CannyLowRatio

        // Giu component lon nhat + lap lo ben trong (mot phat bang FindContours)
        Mat objMask = LargestFilled(rawMask);
        debug?.Invoke(objMask, "4_MaskClean");

        // ---------- 5. Opening residual ----------
        using var opened = OpenWithDisk(objMask, p.DiskRadius, p.OpenDownScale);
        Mat residual = new Mat();
        Cv2.Subtract(objMask, opened, residual);
        debug?.Invoke(residual, "5_Residual");

        // ---------- 6. Loc ung vien ----------
        Result best = SelectBest(residual, opened, gray.Size(), roiArea, p);
        if (best == null)
        {
            objMask.Dispose();
            residual.Dispose();
            return null;
        }

        best.ObjectMask = objMask;
        best.Residual = residual;
        return best;
    }

    // =================================================================================
    //  CAC HAM PHU
    // =================================================================================

    private static Mat ToGray(Mat src)
    {
        var g = new Mat();
        if (src.Channels() == 3) Cv2.CvtColor(src, g, ColorConversionCodes.BGR2GRAY);
        else if (src.Channels() == 4) Cv2.CvtColor(src, g, ColorConversionCodes.BGRA2GRAY);
        else g = src.Clone();
        return g;
    }

    /// <summary>
    /// Pad 1px -> floodFill tu goc (0,0) -> dao nguoc -> OR lai voi duong bien goc.
    /// Pad de dam bao goc luon la nen, ke ca khi vat the cham mep ROI.
    /// </summary>
    private static Mat BuildSolidMask(Mat closedEdges)
    {
        using var padded = new Mat();
        Cv2.CopyMakeBorder(closedEdges, padded, 1, 1, 1, 1,
                           BorderTypes.Constant, Scalar.All(0));
        using var original = padded.Clone();   // giu lai vong bien

        using var ffMask = new Mat(padded.Rows + 2, padded.Cols + 2,
                                   MatType.CV_8UC1, Scalar.All(0));
        Cv2.FloodFill(padded, ffMask, new Point(0, 0), Scalar.All(255),
                      out _, Scalar.All(0), Scalar.All(0), FloodFillFlags.Link4);
        // Sau floodFill: nen = 255, ruot vat the = 0, duong bien = 255

        using var interior = new Mat();
        Cv2.BitwiseNot(padded, interior);      // ruot vat the = 255

        using var full = new Mat();
        Cv2.BitwiseOr(interior, original, full);   // cong lai vong bien

        return new Mat(full, new Rect(1, 1, closedEdges.Cols, closedEdges.Rows)).Clone();
    }

    /// <summary>
    /// Giu component lon nhat VA lap lo ben trong cung mot luc:
    /// FindContours(External) chi tra ve bien ngoai, ve lai voi thickness = -1 la duoc mask dac.
    /// </summary>
    private static Mat LargestFilled(Mat mask)
    {
        using var work = mask.Clone();   // FindContours co the sua source
        Cv2.FindContours(work, out Point[][] cnts, out _,
                         RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        var res = new Mat(mask.Size(), MatType.CV_8UC1, Scalar.All(0));
        if (cnts == null || cnts.Length == 0) return res;

        var biggest = cnts.OrderByDescending(c => Cv2.ContourArea(c)).First();
        Cv2.DrawContours(res, new[] { biggest }, -1, Scalar.All(255), -1);
        return res;
    }

    /// <summary>Structuring element hinh dia tron (doi xung xoay hoan toan).</summary>
    private static Mat BuildDisk(int r)
    {
        var k = new Mat(2 * r + 1, 2 * r + 1, MatType.CV_8UC1, Scalar.All(0));
        Cv2.Circle(k, new Point(r, r), r, Scalar.All(255), -1);
        return k;
    }

    /// <summary>Opening voi dia ban kinh r. Downscale de chay nhanh khi r lon.</summary>
    private static Mat OpenWithDisk(Mat mask, int radius, double scale)
    {
        if (scale >= 0.999)
        {
            using var disk = BuildDisk(radius);
            var o = new Mat();
            Cv2.MorphologyEx(mask, o, MorphTypes.Open, disk);
            return o;
        }

        int rs = Math.Max(2, (int)Math.Round(radius * scale));

        using var small = new Mat();
        Cv2.Resize(mask, small, new Size(), scale, scale, InterpolationFlags.Area);
        Cv2.Threshold(small, small, 127, 255, ThresholdTypes.Binary);

        using var diskSmall = BuildDisk(rs);
        using var openedSmall = new Mat();
        Cv2.MorphologyEx(small, openedSmall, MorphTypes.Open, diskSmall);

        var opened = new Mat();
        Cv2.Resize(openedSmall, opened, mask.Size(), 0, 0, InterpolationFlags.Nearest);
        Cv2.Threshold(opened, opened, 127, 255, ThresholdTypes.Binary);
        return opened;
    }

    /// <summary>Chon ung vien tot nhat trong residual.</summary>
    private static Result SelectBest(Mat residual, Mat body, Size roiSize,
                                     double roiArea, Params p)
    {
        using var work = residual.Clone();
        Cv2.FindContours(work, out Point[][] cnts, out _,
                         RetrievalModes.External, ContourApproximationModes.ApproxNone);
        if (cnts == null || cnts.Length == 0) return null;

        double minArea = roiArea * p.MinAreaRatio;
        double maxArea = roiArea * p.MaxAreaRatio;

        Result best = null;
        double bestScore = double.MinValue;

        foreach (var c in cnts)
        {
            double area = Cv2.ContourArea(c);
            if (area < minArea || area > maxArea) continue;

            // --- minAreaRect: BAT BIEN XOAY. Khong duoc dung boundingRect de do kich thuoc ---
            RotatedRect rr = Cv2.MinAreaRect(c);
            double lo = Math.Min(rr.Size.Width, rr.Size.Height);
            double hi = Math.Max(rr.Size.Width, rr.Size.Height);
            if (lo < 1) continue;
            double aspect = hi / lo;
            if (aspect < p.MinAspect || aspect > p.MaxAspect) continue;

            // --- bo ung vien sat mep ROI (thuong la artifact do cat anh) ---
            Rect br = Cv2.BoundingRect(c);
            if (br.X <= p.BorderMargin || br.Y <= p.BorderMargin ||
                br.Right >= roiSize.Width - p.BorderMargin ||
                br.Bottom >= roiSize.Height - p.BorderMargin) continue;

            // --- phai dinh vao than vat the, khong phai manh troi noi ---
            if (!TouchesBody(c, body, roiSize, p.TouchDilate)) continue;

            // --- cham diem: uu tien vung to va nho ra sau ---
            double score = area * hi;
            if (score > bestScore)
            {
                bestScore = score;
                best = new Result
                {
                    Contour = c,
                    Area = area,
                    OrientedBox = rr,
                    BoundingBox = PadRect(br, p.BboxPadding, roiSize)
                };
            }
        }
        return best;
    }

    private static bool TouchesBody(Point[] contour, Mat body, Size size, int dilate)
    {
        using var m = new Mat(size, MatType.CV_8UC1, Scalar.All(0));
        Cv2.DrawContours(m, new[] { contour }, -1, Scalar.All(255), -1);

        using var k = Cv2.GetStructuringElement(
            MorphShapes.Ellipse, new Size(dilate * 2 + 1, dilate * 2 + 1));
        Cv2.Dilate(m, m, k);

        using var inter = new Mat();
        Cv2.BitwiseAnd(m, body, inter);
        return Cv2.CountNonZero(inter) > 0;
    }

    /// <summary>Noi rong rect va clamp dung cach (loi cu: dung Width - w thay vi x2 - x).</summary>
    private static Rect PadRect(Rect r, int pad, Size bounds)
    {
        int x = Math.Max(0, r.X - pad);
        int y = Math.Max(0, r.Y - pad);
        int x2 = Math.Min(bounds.Width, r.Right + pad);
        int y2 = Math.Min(bounds.Height, r.Bottom + pad);
        return new Rect(x, y, x2 - x, y2 - y);
    }

    // =================================================================================
    //  VE OVERLAY DEBUG
    // =================================================================================
    public static Mat DrawOverlay(Mat roi, Result r)
    {
        Mat vis = roi.Channels() == 1
            ? roi.CvtColor(ColorConversionCodes.GRAY2BGR)
            : roi.Clone();
        if (r == null) return vis;

        Cv2.DrawContours(vis, new[] { r.Contour }, -1, new Scalar(0, 255, 255), 2);
        Cv2.Rectangle(vis, r.BoundingBox, new Scalar(0, 255, 0), 2);

        Point2f[] pts = r.OrientedBox.Points();
        for (int i = 0; i < 4; i++)
            Cv2.Line(vis, (Point)pts[i], (Point)pts[(i + 1) % 4], new Scalar(255, 0, 255), 1);

        Cv2.PutText(vis, $"area={r.Area:F0}",
                    new Point(r.BoundingBox.X, Math.Max(12, r.BoundingBox.Y - 6)),
                    HersheyFonts.HersheySimplex, 0.5, new Scalar(0, 255, 0), 1);
        return vis;
    }
}


// =====================================================================================
//  CACH DUNG - thay cho doan code cu
// =====================================================================================
public static class ProtrusionUsage
{
    public static Rect? FindOnTopRight(Mat src, Point topRight, int roiW, int roiH)
    {
        // 1. Crop ROI + CLAMP (code cu thieu buoc nay -> crash khi topRight.X - roiW < 0)
        Rect roiRect = new Rect(topRight.X - roiW, topRight.Y + 50, roiW, roiH)
                       & new Rect(0, 0, src.Width, src.Height);
        if (roiRect.Width < 10 || roiRect.Height < 10) return null;

        using Mat roi = new Mat(src, roiRect);

        var prm = new ProtrusionDetector.Params
        {
            DiskRadius = 40,       // <-- chinh cai nay dau tien
            OpenDownScale = 0.5,
            MinAreaRatio = 0.0005,
            MaxAreaRatio = 0.10,
        };

        using var res = ProtrusionDetector.Detect(roi, prm,
                            (m, name) => Dbg.Show(m, name));   // bo debug thi truyen null

        if (res == null) return null;

        using var vis = ProtrusionDetector.DrawOverlay(roi, res);
        Dbg.Show(vis, "Done");

        // Doi bbox ve toa do anh goc
        return new Rect(roiRect.X + res.BoundingBox.X,
                        roiRect.Y + res.BoundingBox.Y,
                        res.BoundingBox.Width,
                        res.BoundingBox.Height);
    }

    /// <summary>
    /// Quet DiskRadius de tim gia tri dung. Chay 1 lan tren anh mau, nhin residual.
    /// </summary>
    public static void SweepRadius(Mat roi)
    {
        foreach (int r in new[] { 20, 30, 40, 50, 60 })
        {
            var prm = new ProtrusionDetector.Params { DiskRadius = r };
            using var res = ProtrusionDetector.Detect(roi, prm);
            Console.WriteLine(res == null
                ? $"r={r,3} : MISS"
                : $"r={r,3} : area={res.Area,8:F0}  size={res.OrientedBox.Size}");
        }
    }

    /// <summary>
    /// Test bat bien xoay. Neu Area on dinh qua moi goc (dao dong &lt; 20%) thi dat.
    /// </summary>
    public static void TestRotation(Mat sample, ProtrusionDetector.Params prm)
    {
        var center = new Point2f(sample.Width / 2f, sample.Height / 2f);
        for (int deg = 0; deg < 360; deg += 15)
        {
            using var M = Cv2.GetRotationMatrix2D(center, deg, 1.0);
            using var rot = new Mat();
            Cv2.WarpAffine(sample, rot, M, sample.Size(),
                           InterpolationFlags.Linear, BorderTypes.Replicate);
            using var res = ProtrusionDetector.Detect(rot, prm);
            Console.WriteLine($"{deg,3} deg : {(res == null ? "MISS" : res.Area.ToString("F0"))}");
        }
    }
}