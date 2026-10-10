using A38.ImageCrop;
using A38.ImageCrop.PmAlign;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Interop;

namespace ProjectAlign.PmAlign
{
    public class PmAlign_TuCode
    {
        public static PmModel train_(Mat anhMau, RectXoay roi, IEnumerable<RectXoay> mask,
                                PmCfg cfg, Action<string>? log = null)
        {
            using Mat roiMat = new Mat((int)roi.Rong, (int)roi.Cao, MatType.CV_8UC3);

            PmModel model = new();
            using Mat anhMo = new();
            using Mat xam = new();
            if (anhMau.Channels() == 3)
                Cv2.CvtColor(anhMau, xam, ColorConversionCodes.BGR2GRAY);
            else
                anhMau.CopyTo(xam);


            Cv2.GaussianBlur(xam, anhMo, new Size(3, 3), 2, 2);
            Dbg.Show(anhMo, "Anh mo", true);

            using Mat sobelX = new();
            Cv2.Sobel(anhMo, sobelX, MatType.CV_32F, 1, 0, 3);
            //Dbg.Show(sobelX, "SobelX");

            using Mat sobelY = new();
            Cv2.Sobel(anhMo, sobelY, MatType.CV_32F, 0, 1, 3);
            //Dbg.Show(sobelY, "SobelY");

            using Mat cannyImg = new();
            Cv2.Canny(anhMo, cannyImg, 30, 100);
            Dbg.Show(cannyImg, "anh canny");

            List<DiemModel> lDiemModel = new();

            // Bắt đầu xử lý trong Roi            
            Point[] viTriGoc = HinhBaoQuanhVat(roi);
            using Mat anhRoiHinh = new(anhMau.Size(), MatType.CV_8UC1, Scalar.Black);
            Cv2.FillConvexPoly(anhRoiHinh, viTriGoc, Scalar.White);
            Dbg.Show(anhRoiHinh, "AnhRoiHinh");

            Rect hop = Cv2.BoundingRect(anhRoiHinh);
            hop = hop.Intersect(new Rect(0, 0, anhMau.Width, anhMau.Height));

            // Chay trong so muc 
            double w = anhMau.Width;
            double h = anhMau.Height;
            for (int muc = 0; muc < Math.Max(1, cfg.SoMuc); muc++)
            {
                model.Muc.Add(new MucPm());
                model.Muc[muc].Muc = muc;

                double tiLe = 1.0 / (1 << muc);
                var kt = new Size(Math.Max(4, (int)Math.Round(w * tiLe)),
                                  Math.Max(4, (int)Math.Round(h * tiLe)));
                using var anh = new Mat();
                Cv2.Resize(xam, anh, kt, 0, 0, InterpolationFlags.Area);
                Dbg.Show(anh, "Resize xam");
                // Bắt đầu xử lý trong Roi
                
                RectXoay roi_ = new RectXoay(Math.Round(roi.Cx * tiLe), Math.Round(roi.Cy * tiLe), Math.Round(roi.Rong * tiLe), Math.Round(roi.Cao * tiLe), roi.GocDo);

                Point[] viTriGoc_ = HinhBaoQuanhVat(roi_);
                // Lấy kích thước của ảnh sau khi resize để tạo ra ảnh roi hình
                using Mat anhRoiHinh_ = new(anh.Size(), MatType.CV_8UC1, Scalar.Black);
                Cv2.FillConvexPoly(anhRoiHinh_, viTriGoc_, Scalar.White);
                Dbg.Show(anhRoiHinh_, "AnhRoiHinh");

                Rect hop_ = Cv2.BoundingRect(anhRoiHinh_);
                hop_ = hop_.Intersect(new Rect(0, 0, anh.Width, anh.Height));

                using Mat anhCannyResize = new();
                Cv2.Resize(cannyImg, anhCannyResize, anh.Size(), 0, 0, InterpolationFlags.Area);
                Dbg.Show(anhCannyResize, "AnhCannyResize");

                int yCuoi = hop_.Y + hop_.Height;
                int xCuoi = hop_.X + hop_.Width;

                var pts = new List<(int x, int y, float ux, float uy)>();
                for (int y = 0; y < yCuoi; y++)
                {
                    for (int x = 0; x < xCuoi; x++)
                    {
                        if (anhRoiHinh_.At<byte>(y, x) == 0) continue;
                        if (anhCannyResize.At<byte>(y, x) == 0) continue;
                        double dx = sobelX.At<float>(y, x);
                        double dy = sobelY.At<float>(y, x);
                        double dxy = Math.Sqrt(dx * dx + dy * dy);
                        if (dxy < (1e-3f)) continue;
                        //lDiemModel.Add(new DiemModel(x, y, (float)(dx / dxy), (float)(dy / dxy)));
                        pts.Add((x, y, (float)(dx / dxy), (float)(dy / dxy)));
                    }
                }

                if (pts == null || pts.Count == 0) return null;
                double cx = 0, cy = 0;
                if (muc == 0)
                {
                    cx = (double)pts.Average(x => x.x);
                    cy = (double)pts.Average(y => y.y);
                } else
                {
                    cx = cx * (1/tiLe);
                    cy = cy * (1/tiLe);
                }
             
                    
                // Chuyển về tọa độ theo tâm điểm 
                foreach (var pt in pts)
                {
                    lDiemModel.Add(new DiemModel((float)(pt.x - cx), (float)(pt.y - cy), pt.ux, pt.uy));
               
                }

                // Vẽ 
                using Mat visa = anhMau.Clone();
                //Cv2.CvtColor(anhMau, visa, ColorConversionCodes.BGR2GRAY);
                foreach (var diem in lDiemModel.Where((_, i) => i % 5 == 0))
                {
                    var a = new Point(diem.X + cx, diem.Y + cy);
                    var b = new Point(diem.X + cx + 8 * diem.Gx, diem.Y + cy + 8 * diem.Gy);
                    Cv2.ArrowedLine(visa, a, b, Scalar.Red, 1);
                }
                Cv2.Circle(visa, new Point(cx, cy), 3, Scalar.Lime, -1);
                Dbg.Show(visa, "show visa");

                double maxR = 0;
                // Tính bán kính của hình tròn để tính ra góc xoay cho mỗi pixel của mỗi tầng
                double tongDxy = 0;
                foreach (var diem in lDiemModel)
                {
                    double dxy = Math.Sqrt(diem.X * diem.X + diem.Y * diem.Y);
                    tongDxy += dxy;
                    if (maxR < dxy)
                        maxR = dxy;
                }
                double motDoBangNPixel = lDiemModel.Count == 0 ? 0 : Math.Sqrt(tongDxy / lDiemModel.Count) * Math.PI/ 180;
                double motDoBangPixel = 1/motDoBangNPixel;
                model.Muc[muc].Diem = [.. lDiemModel];
                model.Muc[muc].BanKinh = (float)maxR;
                model.Muc[muc].DonBayXoay = (float)motDoBangNPixel;
            }

            return model;
        }
        // Ham tao vung tim kiem hinh chu nhat o vung train 
        public static Point[] HinhBaoQuanhVat(RectXoay rr)
        {
            Point[] kq = new Point[4];
            var r = new RotatedRect(new Point(rr.Cx, rr.Cy), new Size2f(rr.Rong, rr.Cao ), (float)rr.GocDo);
            Point2f[] p = r.Points();
           
            for(int i = 0; i < 4; i++)
            {
                kq[i] = new Point((int)Math.Round(p[i].X),(int)Math.Round( p[i].Y));
            }
            return kq;
        }
    }
}
