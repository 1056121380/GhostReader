using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace GhostReader
{
    /// <summary>
    /// 分层渲染表面：把内容画进 32bpp DIB（预乘 alpha），再用 UpdateLayeredWindow 贴到屏幕上。
    /// 这是"背景完全透明 + 不透明度独立可调"的正确实现方式。
    /// WinForms 的 Form.Opacity 与 TransparencyKey 会互相覆盖，无法满足需求，故不使用。
    /// </summary>
    internal sealed class LayeredSurface : IDisposable
    {
        private IntPtr _screenDc;
        private IntPtr _memDc;
        private IntPtr _hBitmap;
        private IntPtr _oldBitmap;
        private IntPtr _bits;
        private Bitmap _managed;
        private Graphics _g;
        private int _width, _height;
        private int _stride;
        private bool _disposed;

        public int Width { get { return _width; } }
        public int Height { get { return _height; } }
        public Graphics Graphics { get { return _g; } }
        /// <summary>全局不透明度倍率（0~1），直接作用于每个像素的 alpha 与预乘色值。</summary>
        public double Opacity { get; set; }

        public LayeredSurface()
        {
            Opacity = 1.0;
            _screenDc = Win32.GetDC(IntPtr.Zero);
            _memDc = Win32.CreateCompatibleDC(_screenDc);
        }

        public void Resize(int width, int height)
        {
            if (width < 1) width = 1;
            if (height < 1) height = 1;
            if (width == _width && height == _height) return;

            if (_g != null) { _g.Dispose(); _g = null; }
            if (_managed != null) { _managed.Dispose(); _managed = null; }
            if (_oldBitmap != IntPtr.Zero) { Win32.SelectObject(_memDc, _oldBitmap); _oldBitmap = IntPtr.Zero; }
            if (_hBitmap != IntPtr.Zero) { Win32.DeleteObject(_hBitmap); _hBitmap = IntPtr.Zero; }
            _bits = IntPtr.Zero;

            _width = width;
            _height = height;
            _stride = width * 4;

            var bmi = new Win32.BITMAPINFO();
            bmi.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(Win32.BITMAPINFOHEADER));
            bmi.bmiHeader.biWidth = width;
            bmi.bmiHeader.biHeight = -height;         // 负数 = 自上而下
            bmi.bmiHeader.biPlanes = 1;
            bmi.bmiHeader.biBitCount = 32;
            bmi.bmiHeader.biCompression = 0;          // BI_RGB

            _hBitmap = Win32.CreateDIBSection(_screenDc, ref bmi, 0, out _bits, IntPtr.Zero, 0);
            if (_hBitmap == IntPtr.Zero)
                throw new InvalidOperationException("CreateDIBSection failed");

            _oldBitmap = Win32.SelectObject(_memDc, _hBitmap);

            _managed = new Bitmap(width, height, _stride, PixelFormat.Format32bppPArgb, _bits);
            _g = Graphics.FromImage(_managed);
            _g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            _g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            _g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        }

        /// <summary>
        /// 清空为全透明（0,0,0,0）。
        /// 用 GDI+ 的 Clear 走标准路径：实测在 Format32bppPArgb 表面上
        /// Clear(Color.FromArgb(0,0,0,0)) 能正确清零且 GDI+ 内部缓存保持同步。
        /// （外部 memset 虽然也能清零像素，但会让 GDI+ 的表面缓存与实际内存脱节。）
        /// </summary>
        public void Clear()
        {
            if (_g == null) return;
            try
            {
                _g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                _g.Clear(Color.FromArgb(0, 0, 0, 0));
                _g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
            }
            catch (ArgumentException)
            {
                // Graphics 已被释放（ArgumentException: Parameter is not valid）：重建再清
                if (_managed != null)
                {
                    _g = Graphics.FromImage(_managed);
                    _g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    _g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                    _g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    _g.Clear(Color.FromArgb(0, 0, 0, 0));
                    _g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
                }
            }
        }

        /// <summary>
        /// 把每个像素的不透明度按 Opacity 缩放。
        /// DIB 内存为预乘 alpha，因此 RGB 与 A 同比例缩放即可保持预乘关系。
        /// </summary>
        public void ApplyOpacity()
        {
            if (_bits == IntPtr.Zero) return;
            double op = Opacity;
            if (op < 0) op = 0;
            if (op > 1) op = 1;
            if (op >= 0.999) return;

            int total = _stride * _height;
            byte[] buf = new byte[total];
            Marshal.Copy(_bits, buf, 0, total);

            for (int i = 0; i < total; i += 4)
            {
                int a = buf[i + 3];
                if (a == 0) { buf[i] = 0; buf[i + 1] = 0; buf[i + 2] = 0; continue; }
                int na = (int)Math.Round(a * op);
                if (na > 255) na = 255;
                if (na != a)
                {
                    double f = (double)na / a;
                    buf[i] = (byte)Math.Round(buf[i] * f);
                    buf[i + 1] = (byte)Math.Round(buf[i + 1] * f);
                    buf[i + 2] = (byte)Math.Round(buf[i + 2] * f);
                    buf[i + 3] = (byte)na;
                }
            }
            Marshal.Copy(buf, 0, _bits, total);
        }

        /// <summary>读取表面某点的 BGRA 像素（用于自检）。</summary>
        public int[] ProbeAlpha(int x, int y)
        {
            int[] outv = new int[4];
            if (_bits == IntPtr.Zero) return outv;
            if (x < 0 || y < 0 || x >= _width || y >= _height) return outv;
            IntPtr p = IntPtr.Add(_bits, y * _stride + x * 4);
            outv[0] = Marshal.ReadByte(p, 0);
            outv[1] = Marshal.ReadByte(p, 1);
            outv[2] = Marshal.ReadByte(p, 2);
            outv[3] = Marshal.ReadByte(p, 3);
            return outv;
        }

        /// <summary>把表面贴到目标窗口（UpdateLayeredWindow）。</summary>
        public void Present(IntPtr hwnd, int screenX, int screenY)
        {
            if (_hBitmap == IntPtr.Zero) return;

            Win32.POINT src = new Win32.POINT { X = 0, Y = 0 };
            Win32.POINT dst = new Win32.POINT { X = screenX, Y = screenY };
            Win32.SIZE size = new Win32.SIZE { CX = _width, CY = _height };
            var blend = new Win32.BLENDFUNCTION
            {
                BlendOp = 0,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = 1            // AC_SRC_ALPHA
            };
            if (!Win32.UpdateLayeredWindow(hwnd, _screenDc, ref dst, ref size,
                _memDc, ref src, 0, ref blend, Win32.ULW_ALPHA))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "UpdateLayeredWindow failed");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_g != null) { _g.Dispose(); _g = null; }
            if (_managed != null) { _managed.Dispose(); _managed = null; }
            if (_oldBitmap != IntPtr.Zero) { Win32.SelectObject(_memDc, _oldBitmap); _oldBitmap = IntPtr.Zero; }
            if (_hBitmap != IntPtr.Zero) { Win32.DeleteObject(_hBitmap); _hBitmap = IntPtr.Zero; }
            if (_memDc != IntPtr.Zero) { Win32.DeleteDC(_memDc); _memDc = IntPtr.Zero; }
            if (_screenDc != IntPtr.Zero) { Win32.ReleaseDC(IntPtr.Zero, _screenDc); _screenDc = IntPtr.Zero; }
        }
    }
}
