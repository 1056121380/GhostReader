using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace GhostReader
{
    /// <summary>
    /// 任务栏空闲段探测。
    ///
    /// 1.5 的任务栏正文是独立分层浮层（不改动 Explorer），位置只靠一个
    /// “距左侧偏移”数字决定，默认 80 —— 正好压在开始按钮和搜索框上。
    ///
    /// 算空闲段不能靠子窗口边界：任务列表窗口 MSTaskListWClass 本身一直铺到
    /// 通知区（实测 48..1628），真正空白是在它内部（最后一个任务按钮右侧）。
    /// 也不能靠“背景色”：Win10 的任务栏是半透明毛玻璃 + 横向渐变，
    /// 整条任务栏有近 3000 种颜色，没有众数色可用。
    ///
    /// 这里用垂直方向边缘检测：纯背景列上下相邻像素几乎不变（渐变很缓），
    /// 而任何图标、文字、按钮边框都会在相邻行之间产生明显的亮度跳变。
    /// 逐列判断“有没有东西”，再把连续空列合并成空闲段。
    ///
    /// 像素抓取用一次 BitBlt 完成（1920×34，耗时 ~1ms）。绝不能用逐点 GetPixel ——
    /// 本机实测每次 GetPixel 约 16ms，扫一遍会卡死 UI。
    /// </summary>
    internal static class TaskbarDock
    {
        public sealed class Gap
        {
            /// <summary>屏幕坐标下的左边界。</summary>
            public int Left;
            /// <summary>屏幕坐标下的右边界（不含）。</summary>
            public int Right;
            public int Width { get { return Right - Left; } }
        }

        public sealed class Slot
        {
            /// <summary>相对任务栏左边缘的偏移。</summary>
            public int Offset;
            /// <summary>该空隙实际能容纳的宽度。</summary>
            public int AvailableWidth;
            public bool Found;
        }

        private const string TrayClass = "TrayNotifyWnd";
        /// <summary>相邻两行的三通道差值之和超过它，就认为这一列“有东西”。</summary>
        private const int VerticalEdgeThreshold = 48;
        /// <summary>短于这个宽度的空隙不拿去停靠（小缝隙塞不下字）。</summary>
        private const int MinimumGapWidth = 80;

        /// <summary>找出任务栏上的空闲段，按宽度从大到小排列。</summary>
        public static List<Gap> FindGaps() { return FindGaps(Rectangle.Empty); }

        /// <summary>
        /// 找出任务栏上的空闲段，按宽度从大到小排列。
        /// <paramref name="ignore"/>：小条自身当前占据的屏幕区域，扫描时整段跳过。
        /// 不跳过的话会自己把自己当成“占位”，导致小条在任务栏上左右乱跳。
        /// </summary>
        public static List<Gap> FindGaps(Rectangle ignore)
        {
            var gaps = new List<Gap>();
            IntPtr taskbar = Win32.FindWindowW("Shell_TrayWnd", null);
            if (taskbar == IntPtr.Zero) return gaps;
            if (!Win32.GetWindowRect(taskbar, out var tbRect)) return gaps;
            var bar = Rectangle.FromLTRB(tbRect.Left, tbRect.Top, tbRect.Right, tbRect.Bottom);

            // 通知区（托盘图标 + 时钟）的左边界：小条不能越过它
            int trayLeft = int.MaxValue;
            Win32.EnumChildWindows(taskbar, (h, l) =>
            {
                var name = new StringBuilder(256);
                Win32.GetClassNameW(h, name, 256);
                if (name.ToString() == TrayClass && Win32.GetWindowRect(h, out var r))
                    trayLeft = Math.Min(trayLeft, r.Left);
                return true;
            }, IntPtr.Zero);

            foreach (var run in ScanFreeRuns(bar, ignore)) gaps.Add(run);

            if (trayLeft != int.MaxValue)
                for (int i = gaps.Count - 1; i >= 0; i--)
                    if (gaps[i].Right > trayLeft) gaps[i].Right = Math.Min(gaps[i].Right, trayLeft);

            gaps.RemoveAll(g => g.Width < MinimumGapWidth);
            gaps.Sort((a, b) => b.Width.CompareTo(a.Width));
            return gaps;
        }

        /// <summary>
        /// 为指定宽度挑一个能停靠的空隙。
        /// 完全放得下就用原宽度；都放不下就取最宽的一段并压缩到它的宽度。
        /// 一个都找不到时返回 Found=false，调用方回退到手工偏移。
        /// </summary>
        public static Slot FindSlot(int stripWidth, int taskbarLeft) { return FindSlot(stripWidth, taskbarLeft, Rectangle.Empty); }

        public static Slot FindSlot(int stripWidth, int taskbarLeft, Rectangle ignore)
        {
            var slot = new Slot { AvailableWidth = 0, Offset = 0, Found = false };
            var gaps = FindGaps(ignore);
            if (gaps.Count == 0) return slot;

            foreach (var gap in gaps)
            {
                if (gap.Width >= stripWidth)
                {
                    slot.Found = true;
                    slot.Offset = gap.Left - taskbarLeft;
                    slot.AvailableWidth = gap.Width;
                    return slot;
                }
            }

            var widest = gaps[0];
            if (widest.Width >= 100)
            {
                slot.Found = true;
                slot.Offset = widest.Left - taskbarLeft;
                slot.AvailableWidth = widest.Width;
            }
            return slot;
        }

        /// <summary>
        /// 抓任务栏一条带，逐列做垂直边缘检测，把连续空列合并成空闲段。
        /// </summary>
        private static List<Gap> ScanFreeRuns(Rectangle bar, Rectangle ignore)
        {
            var result = new List<Gap>();
            int width = bar.Width;
            int height = bar.Height;
            if (width <= 0 || height <= 0) return result;

            // 上下各留 3px：任务栏顶端有一道高光描边，会让每一列都算出强跳变
            int top = bar.Top + 3;
            int bandHeight = height - 6;
            if (bandHeight < 4) { top = bar.Top; bandHeight = height; }
            if (bandHeight < 2) return result;

            byte[] pixels;
            if (!GrabBand(bar.Left, top, width, bandHeight, out pixels)) return result;

            var busy = new bool[width];
            for (int x = 0; x < width; x++)
            {
                // 小条自己占据的列直接跳过，避免把自己的文字算成"占用"
                if (ignore.Width > 0)
                {
                    int screenX = bar.Left + x;
                    if (screenX >= ignore.Left && screenX < ignore.Right) continue;
                }
                for (int y = 0; y + 1 < bandHeight; y++)
                {
                    int o = (y * width + x) * 4;
                    int o2 = ((y + 1) * width + x) * 4;
                    int diff = Math.Abs(pixels[o] - pixels[o2])
                             + Math.Abs(pixels[o + 1] - pixels[o2 + 1])
                             + Math.Abs(pixels[o + 2] - pixels[o2 + 2]);
                    if (diff > VerticalEdgeThreshold) { busy[x] = true; break; }
                }
            }

            int runStart = -1;
            for (int x = 0; x <= width; x++)
            {
                bool free = x < width && !busy[x];
                if (free && runStart < 0) runStart = x;
                if (!free && runStart >= 0)
                {
                    result.Add(new Gap { Left = bar.Left + runStart, Right = bar.Left + x });
                    runStart = -1;
                }
            }
            return result;
        }

        /// <summary>一次 BitBlt 抓一段屏幕像素（BGRX，32 位）。</summary>
        private static bool GrabBand(int x, int y, int width, int height, out byte[] pixels)
        {
            pixels = null;
            IntPtr screen = IntPtr.Zero, mem = IntPtr.Zero, dib = IntPtr.Zero, old = IntPtr.Zero;
            try
            {
                screen = Win32.GetDC(IntPtr.Zero);
                mem = Win32.CreateCompatibleDC(screen);
                var info = new Win32.BITMAPINFO();
                info.bmiHeader.biSize = 40;
                info.bmiHeader.biWidth = width;
                info.bmiHeader.biHeight = -height;      // 负数 = 自上而下
                info.bmiHeader.biPlanes = 1;
                info.bmiHeader.biBitCount = 32;
                info.bmiHeader.biCompression = 0;        // BI_RGB
                IntPtr bits;
                dib = Win32.CreateDIBSection(mem, ref info, 0, out bits, IntPtr.Zero, 0);
                if (dib == IntPtr.Zero || bits == IntPtr.Zero) return false;
                old = Win32.SelectObject(mem, dib);
                if (!Win32.BitBlt(mem, 0, 0, width, height, screen, x, y, 0x00CC0020)) return false;
                pixels = new byte[width * height * 4];
                Marshal.Copy(bits, pixels, 0, pixels.Length);
                return true;
            }
            catch { pixels = null; return false; }
            finally
            {
                if (old != IntPtr.Zero && mem != IntPtr.Zero) Win32.SelectObject(mem, old);
                if (dib != IntPtr.Zero) Win32.DeleteObject(dib);
                if (mem != IntPtr.Zero) Win32.DeleteDC(mem);
                if (screen != IntPtr.Zero) Win32.ReleaseDC(IntPtr.Zero, screen);
            }
        }

        /// <summary>诊断信息：任务栏位置与检测到的空闲段。</summary>
        public static string Describe()
        {
            IntPtr taskbar = Win32.FindWindowW("Shell_TrayWnd", null);
            if (taskbar == IntPtr.Zero) return "Shell_TrayWnd 未找到";
            if (!Win32.GetWindowRect(taskbar, out var tbRect)) return "GetWindowRect 失败";
            var bar = Rectangle.FromLTRB(tbRect.Left, tbRect.Top, tbRect.Right, tbRect.Bottom);
            var sb = new StringBuilder();
            sb.Append("taskbar " + bar.Width + "x" + bar.Height + " at " + bar.Left + "," + bar.Top);
            var gaps = FindGaps();
            sb.Append(" 空闲段 " + gaps.Count + " 个：");
            foreach (var g in gaps) sb.Append(" [" + g.Left + ".." + g.Right + " w" + g.Width + "]");
            return sb.ToString();
        }
    }
}
