using InternetClawMachine.Games.GameHelpers;
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace InternetClawMachine
{
    public static class ClassExtensions
    {
        public static DateTime StartOfWeek(this DateTime dt, DayOfWeek startOfWeek)
        {
            var diff = (7 + (dt.DayOfWeek - startOfWeek)) % 7;
            return dt.AddDays(-1 * diff).Date;
        }

        public static decimal Map(this decimal value, decimal fromSource, decimal toSource, decimal fromTarget, decimal toTarget)
        {
            return (value - fromSource) / (toSource - fromSource) * (toTarget - fromTarget) + fromTarget;
        }

        public static int Map(this int value, int fromSource, int toSource, int fromTarget, int toTarget)
        {
            return (int)((((decimal)value - (decimal)fromSource) / ((decimal)toSource - (decimal)fromSource)) * (toTarget - fromTarget) + fromTarget);
        }

        public static float Map(this float value, float fromStart, float fromEnd, float toStart, float toEnd)
        {
            return (value - fromStart) / (fromEnd - fromStart) * (toEnd - toStart) + toStart;
        }
        public static short Map(this short value, float fromStart, float fromEnd, float toStart, float toEnd)
        {
            return (short)((value - fromStart) / (fromEnd - fromStart) * (toEnd - toStart) + toStart);
        }

        public static double ToRadians(this double val)
        {
            return Math.PI / 180 * val;
        }


        [DllImport("gdi32")]
        static extern int DeleteObject(IntPtr o);

        public static BitmapSource ToBitmapSource(this System.Drawing.Bitmap source)
        {
            IntPtr ip = source.GetHbitmap();
            BitmapSource bs = null;
            try
            {
                bs = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(ip,
                   IntPtr.Zero, Int32Rect.Empty,
                   System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            }
            finally
            {
                DeleteObject(ip);
            }

            return bs;
        }

        public static BitmapSource ToBitmapSource(this DirectBitmap source)
        {
            IntPtr ip = source.Bitmap.GetHbitmap();
            BitmapSource bs = null;
            try
            {
                bs = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(ip,
                   IntPtr.Zero, Int32Rect.Empty,
                   System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            }
            finally
            {
                DeleteObject(ip);
            }

            return bs;
        }


    }
}