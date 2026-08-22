using System;
using System.Collections.Generic;
using OpenCvSharp;

public class CapacitorInspector
{
    public enum DefectType
    {
        None,
        Missing,
        Tilt
    }

    public struct InspectionResult
    {
        public bool IsOk;
        public DefectType Defect;
        public double TiltAngleDeg;
        public int ValidEdgePointsCount;
        public Point2f LinePt1;
        public Point2f LinePt2;
        public string Message;
    }

    // Cấu hình thông số ngưỡng
    private const double MAX_TILT_ANGLE_DEG = 3.0; // Góc nghiêng tối đa cho OK
    private const int MIN_EDGE_POINTS_FOR_PRESENCE = 25; // Số điểm cạnh tối thiểu để xác nhận có tụ
    private const int MIN_HEIGHT_SPAN_PX = 30; // Chiều cao tối thiểu của cạnh phải (px)

    public InspectionResult Inspect(Mat srcMat, Rect searchRoi)
    {
        var result = new InspectionResult
        {
            IsOk = false,
            Defect = DefectType.None,
            TiltAngleDeg = 0
        };

        // 1. Cắt ROI và Grayscale
        using Mat roi = new Mat(srcMat, searchRoi);
        using Mat gray = new Mat();
        if (roi.Channels() > 1)
            Cv2.CvtColor(roi, gray, ColorConversionCodes.BGR2GRAY);
        else
            roi.CopyTo(gray);

        // 2. Tiền xử lý: GaussianBlur nhẹ
        using Mat blurred = new Mat();
        Cv2.GaussianBlur(gray, blurred, new Size(3, 3), 0.8);

        // 3. Sobel-X để bắt cạnh đứng
        using Mat sobelX = new Mat();
        Cv2.Sobel(blurred, sobelX, MatType.CV_16S, 1, 0, 3);
        using Mat absSobelX = new Mat();
        Cv2.ConvertScaleAbs(sobelX, absSobelX);

        // 4. Threshold tách biên cạnh
        using Mat edgeMask = new Mat();
        Cv2.Threshold(absSobelX, edgeMask, 60, 255, ThresholdTypes.Binary);

        // 5. Quét từ phải sang trái (Right-to-Left Scan) lấy điểm biên ngoài cùng
        var edgePoints = new List<Point2f>();
        int minY = int.MaxValue;
        int maxY = int.MinValue;

        for (int y = 5; y < edgeMask.Rows - 5; y++)
        {
            for (int x = edgeMask.Cols - 1; x >= 0; x--)
            {
                if (edgeMask.At<byte>(y, x) > 0)
                {
                    edgePoints.Add(new Point2f(x + searchRoi.X, y + searchRoi.Y));
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                    break;
                }
            }
        }

        result.ValidEdgePointsCount = edgePoints.Count;
        int heightSpan = (maxY >= minY) ? (maxY - minY) : 0;

        // BƯỚC 1: KIỂM TRA HIỆN DIỆN (PRESENCE / ABSENCE)
        if (edgePoints.Count < MIN_EDGE_POINTS_FOR_PRESENCE || heightSpan < MIN_HEIGHT_SPAN_PX)
        {
            result.IsOk = false;
            result.Defect = DefectType.Missing;
            result.Message = $"NG: Không có tụ / Thiếu tụ (Points: {edgePoints.Count}, Height: {heightSpan}px)";
            return result;
        }

        // BƯỚC 2: KIỂM TRA ĐỘ NGHIÊNG (TILT ANGLE)
        Line2D line = Cv2.FitLine(edgePoints, DistanceTypes.Huber, 0, 0.01, 0.01);
        double vx = line.Vx;
        double vy = line.Vy;

        if (vy < 0)
        {
            vx = -vx;
            vy = -vy;
        }

        // Tính góc lệch so với trục thẳng đứng Y
        double angleRad = Math.Atan2(vx, vy);
        result.TiltAngleDeg = Math.Abs(angleRad * (180.0 / Math.PI));

        if (result.TiltAngleDeg <= MAX_TILT_ANGLE_DEG)
        {
            result.IsOk = true;
            result.Defect = DefectType.None;
            result.Message = $"OK: Tụ thẳng ({result.TiltAngleDeg:F2}°)";
        }
        else
        {
            result.IsOk = false;
            result.Defect = DefectType.Tilt;
            result.Message = $"NG: Tụ nghiêng ({result.TiltAngleDeg:F2}° > {MAX_TILT_ANGLE_DEG}°)";
        }

        // Dựng 2 mút hiển thị kết quả lên GUI
        result.LinePt1 = new Point2f((float)(line.X1 - vx * 40), (float)(line.Y1 - vy * 40));
        result.LinePt2 = new Point2f((float)(line.X1 + vx * 40), (float)(line.Y1 + vy * 40));

        return result;
    }
}