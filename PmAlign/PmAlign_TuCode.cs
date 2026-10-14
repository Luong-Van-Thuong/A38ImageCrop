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
    public static class PmAlign_TuCode
    {
        public static PmModel train_(Mat anhMau, RectXoay roi, IEnumerable<RectXoay> mask,
                                PmCfg cfg, Action<string>? log = null)
        {
            using Mat roiMat = new Mat((int)roi.Rong, (int)roi.Cao, MatType.CV_8UC3);

            PmModel model = new();
            model.Muc = new List<MucPm>();
            model.Cao = anhMau.Height;
            model.Rong = anhMau.Width;
            model.Roi = roi;
            //using Mat anhMo = new();
            using Mat xam = new();
            if (anhMau.Channels() == 3)
                Cv2.CvtColor(anhMau, xam, ColorConversionCodes.BGR2GRAY);
            else
                anhMau.CopyTo(xam);
            List<DiemModel> lDiemModel = new();



            // Chay trong so muc 
            double w = anhMau.Width;
            double h = anhMau.Height;
            double cx = 0, cy = 0;
            double cx0 = 0, cy0 = 0;
            Mat anhTang = xam.Clone();
            Mat anhDisplay = anhMau.Clone();
            for (int muc = 0; muc < Math.Max(1, cfg.SoMuc); muc++)
            {

                //Cv2.GaussianBlur(anhTang, anhMo, new Size(3, 3), 2, 2);
                //Dbg.Show(anhMo, "Anh mo", true);

                using Mat sobelX = new();
                Cv2.Sobel(anhTang, sobelX, MatType.CV_32F, 1, 0, 3);
                Dbg.Show(sobelX, "SobelX");

                using Mat sobelY = new();
                Cv2.Sobel(anhTang, sobelY, MatType.CV_32F, 0, 1, 3);
                Dbg.Show(sobelY, "SobelY");

                using Mat cannyImg = new();
                Cv2.Canny(anhTang, cannyImg, 30, 150);
                Dbg.Show(cannyImg, "anh canny");


                double tiLe = 1.0 / (1 << muc);
                //var kt = new Size(Math.Max(4, (int)Math.Round(w * tiLe)),
                //                  Math.Max(4, (int)Math.Round(h * tiLe)));
                w = anhTang.Width;
                h = anhTang.Height;
                var kt = new Size(Math.Max(4, (int)Math.Round(w * tiLe)),
                                  Math.Max(4, (int)Math.Round(h * tiLe)));

                //using var anh = new Mat();
                //Cv2.Resize(anhTang, anh, kt, 0, 0, InterpolationFlags.Area);
                //Dbg.Show(anh, "Resize xam");
                // Bắt đầu xử lý trong Roi

                RectXoay roi_ = new RectXoay(Math.Round(roi.Cx * tiLe), Math.Round(roi.Cy * tiLe), Math.Round(roi.Rong * tiLe), Math.Round(roi.Cao * tiLe), roi.GocDo);

                Point[] viTriGoc_ = HinhBaoQuanhVat(roi_);
                // Lấy kích thước của ảnh sau khi resize để tạo ra ảnh roi hình
                using Mat anhRoiHinh_ = new(anhTang.Size(), MatType.CV_8UC1, Scalar.Black);
                Cv2.FillConvexPoly(anhRoiHinh_, viTriGoc_, Scalar.White);
                Dbg.Show(anhRoiHinh_, "AnhRoiHinh");

                Rect hop_ = Cv2.BoundingRect(anhRoiHinh_);
                hop_ = hop_.Intersect(new Rect(0, 0, anhTang.Width, anhTang.Height));

                using Mat anhCannyResize = new();
                Cv2.Resize(cannyImg, anhCannyResize, anhTang.Size(), 0, 0, InterpolationFlags.Area);
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
                if (muc == 0)
                {
                    if (pts == null || pts.Count == 0) return null;
                }
                else
                {
                    if (pts == null || pts.Count == 0)
                    {
                        return model;
                    }
                }

                //model.Muc.Add(new MucPm());

                if (muc == 0)
                {
                    cx0 = (double)pts.Average(x => x.x);
                    cy0 = (double)pts.Average(y => y.y);
                    cx = cx0;
                    cy = cy0;
                }
                else
                {
                    cx = cx0 * tiLe;
                    cy = cy0 * tiLe;
                }


                // Chuyển về tọa độ theo tâm điểm 
                foreach (var pt in pts)
                {
                    lDiemModel.Add(new DiemModel((float)(pt.x - cx), (float)(pt.y - cy), pt.ux, pt.uy));

                }

                // Vẽ 
                using Mat visa = anhDisplay.Clone();
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
                double tongDxy = 0, tongDxy2 = 0;
                foreach (var diem in lDiemModel)
                {
                    double dxy = Math.Sqrt(diem.X * diem.X + diem.Y * diem.Y);
                    double dxy2 = diem.X * diem.Gy - diem.Y * diem.Gx;
                    dxy2 = dxy2 * dxy2;
                    tongDxy2 += dxy2;
                    tongDxy += dxy;
                    if (maxR < dxy)
                        maxR = dxy;
                }
                double motDoBangNPixel = lDiemModel.Count == 0 ? 0 : Math.Sqrt(tongDxy2 / lDiemModel.Count) * Math.PI / 180;
                double motDoBangPixel = 1 / motDoBangNPixel;

                MucPm mucPm = new();
                mucPm.Cao = (int)anhTang.Height;
                mucPm.Rong = (int)anhTang.Width;
                mucPm.TiLe = (float)tiLe;
                mucPm.Diem = [.. lDiemModel];
                mucPm.BanKinh = (float)maxR;
                mucPm.DonBayXoay = (float)motDoBangNPixel;
                mucPm.Muc = muc;
                mucPm.xMuc = cx;
                mucPm.yMuc = cy;
                model.Muc.Add(mucPm);


                //model.Muc[muc].Diem = [.. lDiemModel];
                //model.Muc[muc].BanKinh = (float)maxR;
                //model.Muc[muc].DonBayXoay = (float)motDoBangNPixel;
                //model.Muc[muc].Rong  = (int)anhTang.Width;
                //model.Muc[muc].Cao = (int)anhTang.Height;
                //model.Muc[muc].TiLe = (float)tiLe;
                //model.Muc[muc].Muc = muc;
                lDiemModel.Clear();

                Cv2.PyrDown(anhTang, anhTang);
                Cv2.PyrDown(anhDisplay, anhDisplay);
                Dbg.Show(anhTang, "Anh nho");
            }

            return model;
        }
        // Ham tao vung tim kiem hinh chu nhat o vung train 
        public static Point[] HinhBaoQuanhVat(RectXoay rr)
        {
            Point[] kq = new Point[4];
            var r = new RotatedRect(new Point(rr.Cx, rr.Cy), new Size2f(rr.Rong, rr.Cao), (float)rr.GocDo);
            Point2f[] p = r.Points();

            for (int i = 0; i < 4; i++)
            {
                kq[i] = new Point((int)Math.Round(p[i].X), (int)Math.Round(p[i].Y));
            }
            return kq;
        }

        // 1) Hình học: 4 góc THỰC, không làm tròn gì cả
        public static Point2f[] GocThuc(RectXoay rr)
        {
            var r = new RotatedRect(new Point2f((float)rr.Cx, (float)rr.Cy),   // Point2f, không phải Point
                                    new Size2f((float)rr.Rong, (float)rr.Cao),
                                    (float)rr.GocDo);
            return r.Points();
        }

        // Hàm run model kiểm tra ảnh đầu vào với model đã train
        public static List<KetQuaPm> run_(Mat anh, PmModel model, RectXoay vungTim, PmCfg cfg,
                                     Action<string>? log = null, PmModel _model = null)
        {
            List<KetQuaPm> ketQuaList = new();
            // Nếu không tìm thấy giá trị nào trong model thì trả về kết quả null luôn
            if (_model == null || _model.Muc.Count == 0)
                return ketQuaList;

            KetQuaPm ketQuaPm = new();
            using Mat anhXam = anh.Clone();
            if(anhXam.Channels() == 3)
                Cv2.CvtColor(anh, anhXam, ColorConversionCodes.BGR2GRAY);
            int soMuc = model.Muc.Count;

            // Chay trong so muc 
            double w = anh.Width;
            double h = anh.Height;
            double cx = 0, cy = 0;
            double cx0 = 0, cy0 = 0;
            
            Mat anhDisplay = anh.Clone();

            for (int muc = soMuc - 1; muc >= 0; muc--)
            {
                Mat anhTang = anhXam.Clone();
                for (int mucAnh = 0;mucAnh < muc; mucAnh++)
                {
                    Cv2.PyrDown(anhTang, anhTang);
                }    

                using Mat sobelX = new();
                Cv2.Sobel(anhTang, sobelX, MatType.CV_32F, 1, 0, 3);
                //Dbg.Show(sobelX, "SobelX");

                using Mat sobelY = new();
                Cv2.Sobel(anhTang, sobelY, MatType.CV_32F, 0, 1, 3);
                //Dbg.Show(sobelY, "SobelY");

                using Mat cannyImg = new();
                Cv2.Canny(anhTang, cannyImg, 30, 100);
                Dbg.Show(cannyImg, "anh canny");


                double tiLe = 1.0 / (1 << muc);
                //var kt = new Size(Math.Max(4, (int)Math.Round(w * tiLe)),
                //                  Math.Max(4, (int)Math.Round(h * tiLe)));
                w = anhTang.Width;
                h = anhTang.Height;
                var kt = new Size(Math.Max(4, (int)Math.Round(w * tiLe)),
                                  Math.Max(4, (int)Math.Round(h * tiLe)));
                var pts_ = new List<(int x, int y, double ux, double uy)>();
                var ux = new Mat(anhXam.Size(), MatType.CV_32F, Scalar.All(0));
                var uy = new Mat(anhXam.Size(), MatType.CV_32F, Scalar.All(0));
                for (int y = 0; y < h; y++)
                {
                    for(int x = 0; x < w; x++)
                    {
                       
                        if (cannyImg.At<byte>(y, x) == 0) continue;
                        double gx = sobelX.At<float>(y, x);
                        double gy = sobelY.At<float>(y, x);
                        double gxy = Math.Sqrt(gx * gx + gy * gy);
                        if (gxy < 1e-3f)
                        {
                            continue;
                        } 
                        else
                        {
                            ux.Set(y, x, gx);
                            uy.Set(y, x, gy);
                            pts_.Add(((int)x, (int)y, (double)(gx / gxy), (double)(gy / gxy)));
                        }
                    }
                }

               // Dbg.Show(ux, "ux");
               // Dbg.Show(uy, "uy");
               //// Cv2.PyrDown(anhTang, anhTang);
               // Cv2.PyrDown(anhXam, anhTang);
               // Cv2.PyrDown(anhDisplay, anhDisplay);
            }


            return ketQuaList;
        }

        //private static List<Point> ChamMotDiem(PmModel _model = null)
        //{
        //    List<Point> lPoints = new();
        //    if(_model == null) return lPoints;


        //}
    }
}
