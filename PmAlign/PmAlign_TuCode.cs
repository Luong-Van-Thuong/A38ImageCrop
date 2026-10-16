using A38.ImageCrop;
using A38.ImageCrop.PmAlign;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Threading.Tasks;
using System.Transactions;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Converters;
using System.Xml;

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
                Cv2.Canny(anhTang, cannyImg, 30, 100);
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
                                     Action<string>? log = null)
        {
            List<KetQuaPm> ketQuaList = new();
            // Nếu không tìm thấy giá trị nào trong model thì trả về kết quả null luôn
            if (model == null || model.Muc.Count == 0)
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
            
            //Mat anhDisplay = anh.Clone();
            List<MucAnh> lPointRawRun = new();
            KetQuaPm kqPM = new();
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
                var ux = new Mat(anhTang.Size(), MatType.CV_32F, Scalar.All(0));
                var uy = new Mat(anhTang.Size(), MatType.CV_32F, Scalar.All(0));
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
                            ux.Set(y, x, 0);
                            uy.Set(y, x, 0);
                            continue;
                        } 
                        else
                        {
                            ux.Set(y, x, gx/gxy);
                            uy.Set(y, x, gy/gxy);
                            pts_.Add(((int)x, (int)y, (double)(gx / gxy), (double)(gy / gxy)));
                        }
                    }
                }
                double soBuocXoay = model.Muc[muc].BuocGocDo;
                // Đổi Góc sang radian
                double theTa = soBuocXoay * Math.PI / 180;
                //double buocXoay = 2 * Math.PI / theTa;
                // Nếu đây là tầng chạy thô đầu tiên thì kiểm tra toàn bộ bộ các điểm và xoay đủ từ 0 2Pi radian 
                DiemAnh diemGoc = new();
                if (lPointRawRun == null || lPointRawRun.Count == 0)
                {
                    for (double xoay = 0; xoay < 2*Math.PI; xoay += theTa)
                    {
                        for (int y = 0; y < h; y++)
                        {
                            for (int x = 0; x < w; x++)
                            {
                                var pointRawRung = ChamMotDiem(model, x, y, xoay, muc, ux, uy);
                                lPointRawRun.Add(new(muc,pointRawRung));
                            }
                        }
                    }
                }
                else // Lấy vị trí tìm từ lần chạy trước đó để tìm kiếm tinh không quyết toàn bộ ảnh nữa
                {
                    if (lPointRawRun != null && lPointRawRun.Count > 0)
                    {

                        
                        int mucRawBefor = muc + 1;
                        var lMucAnhRawBefor = lPointRawRun.Where(x => x.MucAnh_ == mucRawBefor).ToList();
                        int cLPointRawRun = lMucAnhRawBefor.Count();

                        if (cLPointRawRun > 0 )
                        {
                            for (int i = 0; i < cLPointRawRun; i++)
                            {
                                if(lMucAnhRawBefor[i].diemAnh_.tongDiem > 0.8 )
                                {
                                    double xTinh = lMucAnhRawBefor[i].diemAnh_.X0 * 2;
                                    double yTinh = lMucAnhRawBefor[i].diemAnh_.Y0 * 2;
                                    double gocXoay_ = lMucAnhRawBefor[i].diemAnh_.gocXoay;
                                    // Phần này cần code tiếp để chạy cho lần tinh chỉnh ở mức sau 
                                    var diemTinh = ChamMotDiem(model, xTinh, yTinh, gocXoay_, muc, ux, uy);
                                    lPointRawRun.Add(new(muc, diemTinh));   
                                }
                            }
                            if (muc == 0)
                            {
                                double max = 0;

                                foreach (var x in lPointRawRun)
                                {
                                    if (x.MucAnh_ == 0)
                                    {
                                        if (x.diemAnh_.tongDiem > max)
                                        {
                                            max = x.diemAnh_.tongDiem;
                                            kqPM.X = x.diemAnh_.X0;
                                            kqPM.Y = x.diemAnh_.Y0;
                                            //diemGoc = x.diemAnh_;

                                        }
                                    }
                                }
                            }
                        }
                    }
                    
                }
                anhTang.Dispose();
                ux.Dispose();
                uy.Dispose();
                //anhDisplay.Dispose();
            }
            ketQuaList.Add(kqPM);
            Cv2.Circle(anh, new Point((int)ketQuaList[0].X, (int)ketQuaList[0].Y), 1, Scalar.Lime, 1, LineTypes.Link8);
            
            return ketQuaList;
        }

        private static DiemAnh ChamMotDiem(PmModel _model = null, double x = 0, double y = 0, double theTa = 0, int muc = 0, Mat ux = null, Mat uy = null)
        {
            
            DiemAnh mucAnh_ = new();
            if (_model == null) return mucAnh_;
            int soLuong = _model.Muc[muc].Diem.Count();

            double s = Math.Sin(theTa);
            double c = Math.Cos(theTa);
            int diemModel = 0;
            double w = ux.Width;
            double h = ux.Height;      
            double tongDiem = 0;
            int soLuongTongDiem = 0;
            for (int i = 0; i < soLuong; i++)
            {
                // Xoay toàn bộ điểm và vector gradient tại điểm đó 
                double xRaw = _model.Muc[muc].Diem[i].X;
                double yRaw = _model.Muc[muc].Diem[i].Y;
                // Tim vi tri cua vat sau khi xoay
                double conX_ = c * xRaw - s * yRaw;
                double conY_ = s * xRaw + c * yRaw;
                // Dịch vật về hệ tọa độ theo điểm x,y
                int px = (int)Math.Round(conX_ + x);
                int py = (int)Math.Round(conY_ + y);
                // Kiểm tra xem vị trí có nằm trong khoảng của hình không 
                if (px < 0 || py < 0 || px >= w || py >= h)
                    continue;
                // Xoay vector gradient 
                double gxRaw = _model.Muc[muc].Diem[i].Gx;
                double gyRaw = _model.Muc[muc].Diem[i].Gy;
                double gx = c * gxRaw - s * gyRaw;
                double gy = s * gxRaw + c * gyRaw;
                // Hướng của vector theo chiều  ux và uy trên ảnh run
                double ux_ = ux.At<float>(py, px);
                double uy_ = uy.At<float>(py, px);
                // Tích vô hướng của vector tại vị trí px,py khi chạy và khi train
                tongDiem += gx * ux_ + gy * uy_;
                //soLuongTongDiem++;

            }
            mucAnh_.X0 = x;
            mucAnh_.Y0 = y;
            mucAnh_.tongDiem = tongDiem/ soLuong;
            mucAnh_.gocXoay = theTa;
            return mucAnh_;


        }
        public class DiemAnh
        {
            public double X0;
            public double Y0;
            public double tongDiem;
            public double gocXoay;
        }
        public class MucAnh
        {
            public int MucAnh_;
            public DiemAnh diemAnh_;
            
            public MucAnh(int mucAnh_, DiemAnh diemAnh_)
            {
                this.MucAnh_ = mucAnh_;
                this.diemAnh_ = diemAnh_;
            }
        }
    }
}
