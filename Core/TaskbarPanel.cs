using System;
using System.Drawing;
using System.Windows.Forms;

namespace GhostReader
{
    /// <summary>Independent layered overlay: never reparents to or modifies Explorer.</summary>
    internal sealed class TaskbarPanel : IDisposable
    {
        private readonly Settings _cfg;
        public ReaderWindow Window { get; }
        public bool VisiblePanel => Window.Visible;
        public int PanelWidth { get => _cfg.TaskbarWidth; set => _cfg.TaskbarWidth = Math.Clamp(value, 100, 10000); }
        public TaskbarPanel(AppState state, Settings cfg)
        {
            _cfg = cfg; Window = new ReaderWindow(state, cfg, true);
            Window.GeometryChanged += RememberPosition;
        }
        public void RememberPosition()
        {
            _cfg.TaskbarFreePosition = true;
            if(Window.LastInteractionWasResize)
            {
                _cfg.TaskbarCustomSize=true;_cfg.TaskbarWidth=Window.WindowWidth;_cfg.TaskbarHeight=Window.WindowHeight;
            }
            _cfg.TaskbarX = Window.GetX(); _cfg.TaskbarY = Window.GetY();
            Position();
            if(Window.LastInteractionWasResize)Window.Redraw();
            _cfg.Save();
        }
        public void Dock()
        {
            _cfg.TaskbarFreePosition = false; Position(); _cfg.Save();
        }

        private Rectangle TaskbarBounds()
        {
            IntPtr taskbar = Win32.FindWindowW("Shell_TrayWnd", null);
            if (taskbar != IntPtr.Zero && Win32.GetWindowRect(taskbar, out var rect))
                return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            var screen = Screen.PrimaryScreen.Bounds;
            return new Rectangle(screen.Left, screen.Bottom - 40, screen.Width, 40);
        }
        public bool Attach()
        {
            if (!Window.IsCreated) Window.Create(0, 0, _cfg.TaskbarWidth, 40);
            Window.ApplyStyles(); Position(); Window.Show(); return true;
        }
        public bool Reattach() => Attach();
        public void Detach() => Window.Hide();
        public void Position()
        {
            if (!Window.IsCreated) return;
            Window.EnsureTopmost();
            if (Window.IsInteracting) return;
            var bounds = TaskbarBounds();
            bool vertical = bounds.Height > bounds.Width;
            var freeScreen = Screen.FromPoint(new Point(_cfg.TaskbarX, _cfg.TaskbarY)).Bounds;
            int maxWidth = _cfg.TaskbarFreePosition ? freeScreen.Width : vertical ? Screen.PrimaryScreen.Bounds.Width : bounds.Width;
            int width = Math.Clamp(_cfg.TaskbarWidth, 100, Math.Max(100, maxWidth - 16));
            using var font = _cfg.Appearance(true).CreateFont();
            using var measureBitmap=new Bitmap(1,1);
            using var measureGraphics=Graphics.FromImage(measureBitmap);
            int neededHeight = (int)Math.Ceiling(font.GetHeight(measureGraphics)) + 12;
            int height = _cfg.TaskbarCustomSize?Math.Clamp(_cfg.TaskbarHeight,20,Math.Max(20,freeScreen.Height)):Math.Max(neededHeight, vertical ? 36 : Math.Clamp(bounds.Height - 2, 20, 80));
            var screen = Screen.FromRectangle(bounds).Bounds;
            int x;
            if (vertical)
            {
                x = bounds.Left > screen.Left + screen.Width / 2 ? bounds.Left - width - 4 : bounds.Right + 4;
            }
            else
            {
                int offset = _cfg.TaskbarOffset;
                if (_cfg.TaskbarAutoDock)
                {
                    // 自动找任务栏上真正没被图标占用的空段，避免默认压在开始按钮/搜索框上。
                    // 扫描时把小条自身当前占据的区域排除掉，否则它会把自己的文字当成占用，
                    // 于是每 500ms 重新算一次位置，在任务栏上来回乱跳。
                    var selfRect = new Rectangle(Window.GetX(), Window.GetY(), Window.WindowWidth, Window.WindowHeight);
                    var slot = TaskbarDock.FindSlot(width, bounds.Left, selfRect);
                    if (slot.Found)
                    {
                        offset = slot.Offset;
                        if (slot.AvailableWidth < width) width = Math.Max(100, slot.AvailableWidth);
                    }
                }
                x = bounds.Left + Math.Clamp(offset, 0, Math.Max(0, bounds.Width - width));
            }
            x = Math.Clamp(x, screen.Left, Math.Max(screen.Left, screen.Right - width));
            int y = vertical ? bounds.Top + 80 : bounds.Top + (bounds.Height - height) / 2;
            y = Math.Clamp(y, screen.Top, Math.Max(screen.Top, screen.Bottom - height));
            if (_cfg.TaskbarFreePosition)
            {
                x = Math.Clamp(_cfg.TaskbarX, freeScreen.Left, Math.Max(freeScreen.Left, freeScreen.Right - width));
                y = Math.Clamp(_cfg.TaskbarY, freeScreen.Top, Math.Max(freeScreen.Top, freeScreen.Bottom - height));
                _cfg.TaskbarX = x; _cfg.TaskbarY = y;
            }
            bool changed = Window.GetX() != x || Window.GetY() != y || Window.WindowWidth != width || Window.WindowHeight != height;
            Window.Resize(width, height); Window.MoveTo(x, y);
            if (changed) Window.Redraw();
            Window.EnsureTopmost();
        }
        public void Redraw() { if (VisiblePanel) Window.Redraw(); }
        public void Dispose() => Window.Dispose();
    }
}
