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
using static ProjectAlign.PmAlign.PmAlign_TuCode;

namespace ProjectAlign.PmAlign
{
    public static class PmAlign_TuCode
    {
        public static PmModel train_(Mat anhMau, RectXoay roi, IEnumerable<RectXoay> mask,
                                PmCfg cfg, Action<string>? log = null)
        {
            var swTrain = System.Diagnostics.Stopwatch.StartNew();   // [DO] đồng hồ tổng train_
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
                var swMuc = System.Diagnostics.Stopwatch.StartNew();   // [DO] đồng hồ cho 1 tầng train

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

                log?.Invoke($"[DO] train L{muc}: {anhTang.Width}x{anhTang.Height} | nDiem={mucPm.Diem.Length} | R={mucPm.BanKinh:F1}px | buocGoc={mucPm.BuocGocDo:F2}° | {swMuc.Elapsed.TotalMilliseconds:F1} ms");   // [DO]
                Cv2.PyrDown(anhTang, anhTang);
                Cv2.PyrDown(anhDisplay, anhDisplay);
                Dbg.Show(anhTang, "Anh nho");
            }

            log?.Invoke($"[DO] TONG train_: {swTrain.Elapsed.TotalMilliseconds:F1} ms");   // [DO]
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
            var dh = System.Diagnostics.Stopwatch.StartNew();
            var sw = System.Diagnostics.Stopwatch.StartNew();   // [DO] đồng hồ cho từng đoạn trong 1 tầng
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
            KetQuaPm kqPM = null;
            for (int muc = soMuc - 1; muc >= 0; muc--)
            {
                sw.Restart();   // [DO]
                Mat anhTang = anhXam.Clone();

                for (int mucAnh = 0;mucAnh < muc; mucAnh++)
                {
                    Cv2.PyrDown(anhTang, anhTang);
                }
                double tPyr = sw.Elapsed.TotalMilliseconds; sw.Restart();   // [DO] thời gian dựng ảnh tầng (PyrDown)

                using Mat anhMo = new();
                Cv2.GaussianBlur(anhTang, anhMo, new Size(5, 5), 0);

                using Mat sobelX = new();
                Cv2.Sobel(anhMo, sobelX, MatType.CV_32F, 1, 0, 3);
                //Dbg.Show(sobelX, "SobelX");

                using Mat sobelY = new();
                Cv2.Sobel(anhMo, sobelY, MatType.CV_32F, 0, 1, 3);
                //Dbg.Show(sobelY, "SobelY");

                using Mat cannyImg = new();
                Cv2.Canny(anhMo, cannyImg, 30, 100);
                Dbg.Show(cannyImg, "anh canny");

                double tLoc = sw.Elapsed.TotalMilliseconds; sw.Restart();   // [DO] Blur + Sobel x2 + Canny (+ Dbg.Show nếu đang bật)
                

                double tiLe = 1.0 / (1 << muc);
                //var kt = new Size(Math.Max(4, (int)Math.Round(w * tiLe)),
                //                  Math.Max(4, (int)Math.Round(h * tiLe)));
                w = anhTang.Width;
                h = anhTang.Height;

                int n = (int)(w * h);
                float[] sxArr = new float[n];
                float[] syArr = new float[n];
                byte[] cannyArr = new byte[n];
                System.Runtime.InteropServices.Marshal.Copy(sobelX.Data, sxArr, 0, n);
                System.Runtime.InteropServices.Marshal.Copy(sobelY.Data, syArr, 0, n);
                System.Runtime.InteropServices.Marshal.Copy(cannyImg.Data, cannyArr, 0, n);

                //var kt = new Size(Math.Max(4, (int)Math.Round(w * tiLe)),
                //                  Math.Max(4, (int)Math.Round(h * tiLe)));

                float[] uxArr = new float[n];
                float[] uyArr = new float[n];
                for (int i = 0; i < n; i++)
                {
                    if (cannyArr[i] == 0) continue;
                    double gxy = Math.Sqrt(sxArr[i] * sxArr[i] + syArr[i] * syArr[i]);
                    if (gxy < (1e-3))
                        continue;
                    uxArr[i] = (float)(sxArr[i] / gxy);
                    uyArr[i] = (float)(syArr[i] / gxy);
                }
                double tGrad = sw.Elapsed.TotalMilliseconds; sw.Restart();   // [DO] Marshal.Copy + vòng sqrt tạo ux/uy
                int nDiemDo = model.Muc[muc].Diem.Length;   // [DO] số điểm model ở tầng này
                    
                double soBuocXoay = model.Muc[muc].BuocGocDo;
                
                // Đổi Góc sang radian
                double theTa = soBuocXoay * Math.PI / 180;
                double tMin = -cfg.GocDenDo * Math.PI / 180;   // đổi dấu nên hai đầu đảo chỗ
                double tMax = -cfg.GocTuDo * Math.PI / 180;
                int heSo = 1 << muc;

                //double buocXoay = 2 * Math.PI / theTa;
                // Nếu đây là tầng chạy thô đầu tiên thì kiểm tra toàn bộ bộ các điểm và xoay đủ từ 0 2Pi radian 
                DiemAnh diemGoc = new();
                if (muc == soMuc -1)
                {
                    for (double xoay = tMin; xoay < tMax; xoay += theTa)
                    {
                        BangXoay bang = XoayModel(model, muc, xoay);
                        for (int y = 0; y < h; y++)
                        {
                            for (int x = 0; x < w; x++)
                            {
                                if (vungTim.HopLe && !vungTim.Chua(x * heSo, y * heSo)) continue;

                                //var pointRawRung = ChamMotDiem(model, x, y, xoay, muc, ux, uy);
                                var pointRawRung = ChamMotDiem(bang, x, y, uxArr, uyArr, (int)w, (int)h);
                                if (pointRawRung!=null && pointRawRung.tongDiem < cfg.DiemToiThieu) continue;
                                lPointRawRun.Add(new(muc, pointRawRung));
                            }
                        }
                    }
                    double tChamTho = sw.Elapsed.TotalMilliseconds;   // [DO] thời gian vét cạn tầng thô
                    int soGocDo = (int)Math.Ceiling((tMax - tMin) / theTa);   // [DO] số góc đã thử
                    long soTuTheDo = (long)w * (long)h * soGocDo;   // [DO] số tư thế tối đa (vungTim có thể bỏ bớt)
                    int soTruocNMS = lPointRawRun.Count;   // [DO] số ứng viên lọt ngưỡng trước NMS
                    sw.Restart();   // [DO]
                    List<DiemAnh> lDiemAnhNMS = new();
                    lDiemAnhNMS = lPointRawRun.Select(x => x.diemAnh_).ToList();
                    double khoangPx = Math.Max(2, 0.5 * model.Muc[muc].BanKinh);
                    var lPoint = LocTrungNMS(lDiemAnhNMS, khoangPx, cfg.SoUngVienDinh);
                    lPointRawRun.Clear();
                    foreach (var p in lPoint)
                    {
                        lPointRawRun.Add(new(muc, p));
                    }

                    double tNMS = sw.Elapsed.TotalMilliseconds;   // [DO]
                    log?.Invoke($"[DO] run L{muc} {w}x{h} nDiem={nDiemDo} | PyrDown {tPyr:F1} | Blur+Sobel+Canny {tLoc:F1} | ux/uy {tGrad:F1} | Cham {tChamTho:F1} ms ({soGocDo} goc, <= {soTuTheDo} tu the, <= {soTuTheDo * nDiemDo * 9 / 1e6:F0}M phep cham) | {soTruocNMS} ung vien truoc NMS | NMS {tNMS:F1} ms");   // [DO]
                    log?.Invoke($"L{muc}: cham diem {dh.ElapsedMilliseconds} ms | {lPointRawRun.Count(p => p.MucAnh_ == muc)} ung vien");
                }
                else // Lấy vị trí tìm từ lần chạy trước đó để tìm kiếm tinh không quyết toàn bộ ảnh nữa
                {
                    if (lPointRawRun != null && lPointRawRun.Count > 0)
                    {
                        int mucRawBefor = muc + 1;
                        var lMucAnhRawBefor = lPointRawRun.Where(x => x.MucAnh_ == mucRawBefor).ToList();
                        int cLPointRawRun = lMucAnhRawBefor.Count();
                        double buocTren = model.Muc[muc + 1].BuocGocDo * Math.PI / 180;
                        int k = (int)Math.Ceiling(buocTren / theTa);
                        if (cLPointRawRun > 0)
                        {

                            for (int i = 0; i < cLPointRawRun; i++)
                            {
                                DiemAnh diemTot = null;                                
                                double xTinh = lMucAnhRawBefor[i].diemAnh_.X0 * 2;
                                double yTinh = lMucAnhRawBefor[i].diemAnh_.Y0 * 2;
                                double gocXoay_ = lMucAnhRawBefor[i].diemAnh_.gocXoay;
                           
                                double max = 0;
                                for(int k_ = -k; k_ <=k; k_++)
                                {
                                    double gocXoay_1 = gocXoay_ + k_ * theTa;
                                    BangXoay bang = XoayModel(model, muc, gocXoay_1);  // xoay 1 lần cho góc gocXoay_1
                                    for (int dy_ = -2; dy_<=2; dy_++)
                                    {
                                        for(int dx_ = -2; dx_ <=2; dx_++)
                                        {
                                            var d = ChamMotDiem(bang, (int)xTinh + dx_, (int)yTinh + dy_, uxArr, uyArr, (int)w, (int)h);
                                            if (diemTot == null || d.tongDiem > diemTot.tongDiem) diemTot = d;
                                        }  
                                    }    
  
                                }
                                if (diemTot != null && diemTot.tongDiem < cfg.DiemToiThieu) continue;
                                lPointRawRun.Add(new(muc, diemTot));
                               
                            }
                            long soTuTheTinh = (long)cLPointRawRun * (2 * k + 1) * 25;   // [DO] số tư thế tầng tinh
                            log?.Invoke($"[DO] run L{muc} {w}x{h} nDiem={nDiemDo} | PyrDown {tPyr:F1} | Blur+Sobel+Canny {tLoc:F1} | ux/uy {tGrad:F1} | Cham {sw.Elapsed.TotalMilliseconds:F1} ms ({cLPointRawRun} ung vien x {2 * k + 1} goc x 25 vi tri = {soTuTheTinh} tu the, {soTuTheTinh * nDiemDo * 9 / 1e6:F0}M phep cham)");   // [DO]
                           
                           
                        }
                        else
                        {
                            var error = "Tầng trước không tìm được đối tượng nào";
                        }
                    }

                }
                anhTang.Dispose();
                
                //anhDisplay.Dispose();
            }


            var lTang0 = lPointRawRun.Where(x => x.MucAnh_ == 0)
                                     .Select(x => x.diemAnh_)
                                     .Where(d => d.tongDiem >= cfg.DiemToiThieu)   // ngưỡng nhận
                                     .OrderByDescending(d => d.tongDiem)           // kq[0] = điểm cao nhất
                                     .Take(cfg.SoKetQua);                          // số kết quả tối đa
            foreach (var diemTot in lTang0)
            {
                kqPM = new();
                kqPM.X = diemTot.X0;
                kqPM.Y = diemTot.Y0;
                double gocDo = -diemTot.gocXoay * 180 / Math.PI;
                kqPM.GocDo = ((gocDo + 180) % 360 + 360) % 360 - 180;
                kqPM.Diem = diemTot.tongDiem;
                kqPM.DiemXep = diemTot.tongDiem;
                ketQuaList.Add(kqPM);
            }
            log?.Invoke($"[DO] TONG run_: {dh.Elapsed.TotalMilliseconds:F1} ms");   // [DO]
            return ketQuaList;
        }

        public static List<DiemAnh> LocTrungNMS(List<DiemAnh> ds, double khoangPx, int soGiu)
        {
            List<DiemAnh> lDiemAnh = new();
            List<DiemAnh> giu = new();
            var diem = ds.OrderByDescending(x => x.tongDiem).ToList();
            foreach (var d in diem)
            {
                bool trung = false;
                foreach (var g in giu)
                {
                    double dx = g.X0 - d.X0; double dy = g.Y0 - d.Y0;
                    double dxy = dx * dx + dy * dy;
                    //double gocR = GocLec(d.gocXoay, g.gocXoay);
                    if (dxy < khoangPx * khoangPx)
                    {
                        trung = true;
                        break;
                    }
                }
                if (!trung)
                {
                    giu.Add(d);
                }
                lDiemAnh = giu;
                if (giu.Count >= soGiu)
                    return lDiemAnh;
            }
            return lDiemAnh;
        }

        public class DiemAnh
        {
            public double X0;
            public double Y0;
            public double tongDiem;
            public double gocXoay; // tinh bằng rad, model đang xoay ở góc rad nào
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



        public class BangXoay
        {
            public double gocXoay;          // góc đã xoay (rad)
            public int[] oxXoay = [];       // vị trí x của từng điểm model sau khi xoay
            public int[] oyXoay = [];
            public float[] gxXoay = [];     // gradient x của từng điểm model sau khi xoay
            public float[] gyXoay = [];
        }

        // Gọi 1 lần cho mỗi góc: xoay toàn bộ điểm model
        private static BangXoay XoayModel(PmModel _model, int muc, double theTa)
        {
            var diemMuc = _model.Muc[muc].Diem;
            int nDiem = diemMuc.Length;
            double c = Math.Cos(theTa);
            double s = Math.Sin(theTa);
            BangXoay bang = new();
            bang.gocXoay = theTa;
            bang.oxXoay = new int[nDiem];
            bang.oyXoay = new int[nDiem];
            bang.gxXoay = new float[nDiem];
            bang.gyXoay = new float[nDiem];
            for (int i = 0; i < nDiem; i++)
            {
                // Vị trí sau khi xoay (đã làm tròn sẵn)
                bang.oxXoay[i] = (int)Math.Round(c * diemMuc[i].X - s * diemMuc[i].Y);
                bang.oyXoay[i] = (int)Math.Round(s * diemMuc[i].X + c * diemMuc[i].Y);
                // Hướng gradient sau khi xoay (cùng công thức với vị trí)
                bang.gxXoay[i] = (float)(c * diemMuc[i].Gx - s * diemMuc[i].Gy);
                bang.gyXoay[i] = (float)(s * diemMuc[i].Gx + c * diemMuc[i].Gy);
            }
            return bang;
        }

        // Gọi cho mỗi (x, y): KHÔNG xoay nữa, chỉ dịch tới (x, y) rồi đọc mảng
        private static DiemAnh ChamMotDiem(BangXoay bang, int x, int y, float[] uxArr, float[] uyArr, int W, int H)
        {
            int nDiem = bang.oxXoay.Length;
            double tongDiem = 0;
            for (int i = 0; i < nDiem; i++)
            {
                double tot = 0;
                for(int dy = -1; dy <=1; dy++)
                {
                    for(int dx = -1; dx <= 1; dx++)
                    {
                        int px = x + bang.oxXoay[i] + dx;          // chỉ dịch, không xoay
                        int py = y + bang.oyXoay[i] + dy;
                        if (px < 0 || py < 0 || px >= W || py >= H) continue;
                        int k = py * W + px;                   // vị trí pixel (px, py) trong mảng 1 chiều
                        double v = bang.gxXoay[i] * uxArr[k] + bang.gyXoay[i] * uyArr[k];
                        if (v > tot) tot = v;
                    }    
                }

                tongDiem += tot;
            }
            DiemAnh mucAnh_ = new();
            mucAnh_.X0 = x;
            mucAnh_.Y0 = y;
            mucAnh_.tongDiem = tongDiem / nDiem;
            mucAnh_.gocXoay = bang.gocXoay;
            return mucAnh_;
        }



        

    }
}
