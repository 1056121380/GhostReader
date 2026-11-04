using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace GhostReader
{
    /// <summary>Non-activating per-pixel-alpha reading surface. Each view owns its layout.</summary>
    internal sealed class ReaderWindow : NativeWindow
    {
        private readonly AppState _state;
        private readonly Settings _cfg;
        private readonly bool _compact;
        private LayeredSurface _surface;
        private Font _font;
        private ReadingLayout _layout = new ReadingLayout();
        private Book _layoutBook;
        private (Book Book,string Family,double Size,int Width,float DpiX,float DpiY)? _wrapKey;
        private string _fontFamily;
        private double _fontSize;
        private bool _contextMenuOpen;
        private bool _layoutDirty = true;
        private int _lineHeight = 20, _visibleCount = 1;
        private bool _dragging, _moved, _tracking;
        private ResizeCorner _resizeCorner;
        public bool LastInteractionWasResize { get; private set; }
        private Win32.POINT _dragOrigin;
        private int _dragX, _dragY, _dragW, _dragH;
        private int _wheelRemainder;
        public event Action ImportRequested, ExitRequested, SettingsRequested, ChapterListRequested, ToggleRequested, HideRequested, GeometryChanged;
        public event Action OwnHideRequested, OnlyThisRequested, AutoScrollRequested, DockRequested;
        public event Action InteractionStateChanged;
        public event Action<int> SpeedChangeRequested;
        public event Action ReverseScrollRequested;
        public bool ContextMenuOpen => _contextMenuOpen;
        public bool IsInteracting => _dragging||_contextMenuOpen;
        public ResizeCorner CornerAt(Point point)
        {
            int dpi=_hwnd==IntPtr.Zero?96:(int)Win32.GetDpiForWindow(_hwnd);
            return CornerResize.Hit(new Rectangle(GetX(),GetY(),WindowWidth,WindowHeight),point,Math.Max(14,(int)Math.Round(14*dpi/96.0)));
        }
        public int WindowWidth = 560, WindowHeight = 440;
        public string PreviewText { get; private set; } = "";
        public ReaderWindow(AppState state, Settings cfg, bool compact = false)
        { _state = state; _cfg = cfg; _compact = compact; }
        public void Create(int x, int y, int w, int h)
        {
            WindowWidth = Math.Max(100, w); WindowHeight = Math.Max(20, h);
            CreateNative(_compact ? "GhostReaderOverlay" : "GhostReaderReaderWindow",
                _compact ? "GhostReader 任务栏正文" : "GhostReader 桌面阅读",
                Win32.WS_POPUP, Win32.WS_EX_LAYERED | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE,
                x, y, WindowWidth, WindowHeight, IntPtr.Zero);
            ApplyStyles();
        }
        public void ApplyStyles()
        {
            if (_hwnd == IntPtr.Zero) return;
            uint ex = Win32.WS_EX_LAYERED | Win32.WS_EX_NOACTIVATE | (_cfg.ShowInTaskbar ? Win32.WS_EX_APPWINDOW : Win32.WS_EX_TOOLWINDOW);
            if (_cfg.ClickThrough) ex |= Win32.WS_EX_TRANSPARENT;
            Win32.SetWindowLongPtrSafe(_hwnd, Win32.GWL_EXSTYLE, new IntPtr(ex));
            Win32.SetWindowPos(_hwnd, new IntPtr(_compact || _cfg.AlwaysOnTop ? -1 : -2), 0, 0, 0, 0,
                Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
            _layoutDirty = true;
        }
        public new void Show() { base.Show(); Redraw();if(_compact||_cfg.AlwaysOnTop)EnsureTopmost(); }
        public int GetX() { return Win32.GetWindowRect(_hwnd, out var r) ? r.Left : 0; }
        public int GetY() { return Win32.GetWindowRect(_hwnd, out var r) ? r.Top : 0; }
        public void EnsureTopmost()
        {
            if(_hwnd==IntPtr.Zero)return;
            uint flags=Win32.SWP_NOMOVE|Win32.SWP_NOSIZE|Win32.SWP_NOACTIVATE;
            Win32.SetWindowPos(_hwnd,new IntPtr(-2),0,0,0,0,flags);
            Win32.SetWindowPos(_hwnd,new IntPtr(-1),0,0,0,0,flags);
            Win32.SetWindowPos(_hwnd,IntPtr.Zero,0,0,0,0,flags);
        }
        public void MoveTo(int x, int y)
        {
            Win32.SetWindowPos(_hwnd, IntPtr.Zero, x, y, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
        }
        public void Resize(int w, int h)
        {
            int nw = Math.Clamp(w,_compact?100:160,10000), nh = Math.Clamp(h,_compact?20:60,10000);
            if (WindowWidth == nw && WindowHeight == nh) return;
            WindowWidth = nw; WindowHeight = nh; _layoutDirty = true;
            Win32.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, nw, nh, Win32.SWP_NOMOVE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
            Redraw();
        }
        public void RebuildLayout()
        {
            if (_surface == null) _surface = new LayeredSurface();
            _surface.Resize(WindowWidth, WindowHeight);
            var appearance = _cfg.Appearance(_compact);
            if(_font==null||_fontFamily!=appearance.FontFamily||_fontSize!=appearance.FontSize)
            {
                _font?.Dispose();_font=appearance.CreateFont();_fontFamily=appearance.FontFamily;_fontSize=appearance.FontSize;
            }
            int width=Math.Max(1,WindowWidth-appearance.Padding*2);
            var key=(_state.Book,appearance.FontFamily,appearance.FontSize,width,_surface.Graphics.DpiX,_surface.Graphics.DpiY);
            if(!_wrapKey.HasValue||!_wrapKey.Value.Equals(key))
            {
                if(_compact)_layout.Build(_state.Book,_surface.Graphics,_font,width);
                else{_state.Rebuild(_surface.Graphics,WindowWidth,_cfg);_layout=_state.Layout;}
                _wrapKey=key;
            }
            _lineHeight=Math.Max(1,(int)Math.Ceiling(_font.GetHeight(_surface.Graphics)*appearance.LineSpacing));
            int statusHeight=!_compact&&_cfg.ShowStatus?_lineHeight:0;
            _visibleCount=Math.Max(1,(WindowHeight-appearance.Padding*2-statusHeight)/_lineHeight);
            if(!_compact){_state.VisibleCount=_visibleCount;_state.LineHeight=_lineHeight;}
            _layoutBook=_state.Book;_layoutDirty=false;
        }
        public void Redraw()
        {
            if (_hwnd == IntPtr.Zero) return;
            if (_layoutDirty || _layoutBook != _state.Book || _surface == null) RebuildLayout();
            var appearance = _cfg.Appearance(_compact);
            _surface.Clear(); _surface.Opacity = 1;
            var g = _surface.Graphics;
            g.ResetClip();
            using (var background = new SolidBrush(appearance.BackgroundBrushColor))
                g.FillRectangle(background, 0, 0, WindowWidth, WindowHeight);
            using var format = Win32.CreateFormat(false);
            using var brush = new SolidBrush(appearance.TextBrushColor);
            int pad = appearance.Padding;
            if (_state.Book == null || _layout.Lines.Count == 0)
            {
                PreviewText = _compact ? "右键导入 TXT / EPUB" : "右键导入 TXT / EPUB\nCtrl+Alt+S 设置 · Ctrl+Alt+M 一键隐藏";
                using var hintFont = new Font("Microsoft YaHei UI", _compact ? 10 : 11);
                g.DrawString(PreviewText, hintFont, brush, new RectangleF(pad, pad, Math.Max(1, WindowWidth - pad * 2), Math.Max(1, WindowHeight - pad * 2)));
            }
            else
            {
                double coordinate = _layout.CoordinateOf(_state.GetAnchor());
                int start = (int)Math.Floor(coordinate);
                float phase = (float)(coordinate - start);
                var preview = new System.Text.StringBuilder();
                int statusHeight = !_compact && _cfg.ShowStatus ? _lineHeight : 0;
                int rows=_compact&&!_cfg.TaskbarCustomSize?1:_visibleCount;
                float textTop = _compact ? Math.Max(0,(WindowHeight - rows*_lineHeight)/2f) : pad;
                int clipHeight = _compact ? Math.Min(WindowHeight,rows*_lineHeight) : Math.Max(1, WindowHeight - pad * 2 - statusHeight);
                g.SetClip(new RectangleF(0, textTop, WindowWidth, clipHeight));
                int drawCount = rows + 2;
                for (int i = 0; i < drawCount && start + i < _layout.Lines.Count; i++)
                {
                    string text = _layout.Lines[start + i].Text;
                    float y = textTop + (i - phase) * _lineHeight;
                    if (y >= textTop + clipHeight) break;
                    if (i > 0) preview.AppendLine(); preview.Append(text);
                    g.DrawString(text, _font, brush, pad, y, format);
                }
                PreviewText = preview.ToString();
                g.ResetClip();
                if (!_compact && _cfg.ShowStatus)
                {
                    using var statusBrush = new SolidBrush(Color.FromArgb((int)(appearance.TextBrushColor.A * .55), appearance.TextColor));
                    g.DrawString(_state.ChapterProgress(), _font, statusBrush, pad, WindowHeight - pad - _lineHeight, format);
                }
            }
            _surface.Present(_hwnd, GetX(), GetY());
            if((_compact||_cfg.AlwaysOnTop)&&(Win32.GetWindowLongPtrSafe(_hwnd,Win32.GWL_EXSTYLE).ToInt64()&8)==0)EnsureTopmost();
        }
        public ReadingPosition LastReadablePosition
        {
            get
            {
                if (_layoutDirty || _layoutBook != _state.Book) RebuildLayout();
                return _layout.Lines.Count == 0 ? new ReadingPosition() : _layout.Lines[^1].Position;
            }
        }
        public bool AdvanceAutoScroll(double seconds) => AdvanceAutoScrollTo(seconds, LastReadablePosition);
        public bool AdvanceAutoScrollTo(double seconds, ReadingPosition forwardLimit)
        {
            if (_dragging) return true;
            if (_layoutDirty || _layoutBook != _state.Book) RebuildLayout();
            if (_layout.Lines.Count == 0) return false;
            // The tail sentinel preserves positions within the last row even when another
            // narrower visible surface needs to keep scrolling after this view is full.
            double current = _layout.CoordinateOf(_state.GetAnchor(), true);
            double limit = _layout.CoordinateOf(forwardLimit, true);
            double speed = double.IsFinite(_cfg.AutoScrollPixelsPerSecond) ? Math.Clamp(_cfg.AutoScrollPixelsPerSecond, 1, 120) : 12;
            double elapsed = double.IsFinite(seconds) ? Math.Clamp(seconds, 0, .1) : 0;
            int direction = _cfg.AutoScrollDirection < 0 ? -1 : 1;
            if (direction > 0 && current >= limit) return false;
            if (direction < 0 && current <= 0) return false;
            double next = direction > 0 ? Math.Min(limit, current + speed * elapsed / _lineHeight)
                : Math.Max(0, current - speed * elapsed / _lineHeight);
            if (Math.Abs(next - current) > 1e-12) _state.SetAnchor(_layout.PositionAt(next, true));
            return direction > 0 ? next < limit : next > 0;
        }
        public void Navigate(int direction, bool page = false)
        {
            if (_layoutDirty || _layoutBook != _state.Book) RebuildLayout();
            _state.MoveInLayout(_layout, direction * (page && !_compact ? _visibleCount : 1));
        }
        private static bool SetCornerCursor(ResizeCorner corner)
        {
            if(corner==ResizeCorner.None)return false;
            int cursor=corner switch {
                ResizeCorner.Left or ResizeCorner.Right=>32644,
                ResizeCorner.Top or ResizeCorner.Bottom=>32645,
                ResizeCorner.TopLeft or ResizeCorner.BottomRight=>32642,
                _=>32643
            };
            Win32.SetCursor(Win32.LoadCursorW(IntPtr.Zero,cursor));return true;
        }
        public void BeginPointer(Point point)
        {
            if(_cfg.ClickThrough)return;
            if(_compact)EnsureTopmost();
            _dragging=true;_moved=false;LastInteractionWasResize=false;
            _dragOrigin=new Win32.POINT{X=point.X,Y=point.Y};_dragX=GetX();_dragY=GetY();_dragW=WindowWidth;_dragH=WindowHeight;
            _resizeCorner=CornerAt(point);SetCornerCursor(_resizeCorner);Win32.SetCapture(_hwnd);InteractionStateChanged?.Invoke();
        }
        public void UpdatePointer(Point point)
        {
            if(!_dragging){SetCornerCursor(CornerAt(point));return;}
            int dx=point.X-_dragOrigin.X,dy=point.Y-_dragOrigin.Y;
            _moved|=_resizeCorner==ResizeCorner.None?Math.Abs(dx)>3||Math.Abs(dy)>3:dx!=0||dy!=0;
            if(!_moved)return;
            if(_resizeCorner!=ResizeCorner.None)
            {
                Size minimum=new Size(_compact?100:160,_compact?20:60);
                var screen=System.Windows.Forms.Screen.FromRectangle(new Rectangle(_dragX,_dragY,_dragW,_dragH)).Bounds;
                var virtualBounds=System.Windows.Forms.SystemInformation.VirtualScreen;
                Size maximum=_compact?new Size(Math.Max(100,screen.Width-16),Math.Max(20,screen.Height)):new Size(Math.Max(160,virtualBounds.Width),Math.Max(60,virtualBounds.Height));
                var rect=CornerResize.Calculate(new Rectangle(_dragX,_dragY,_dragW,_dragH),_resizeCorner,new Point(dx,dy),minimum,maximum);
                if(_compact)rect=CornerResize.LimitToScreen(new Rectangle(_dragX,_dragY,_dragW,_dragH),rect,_resizeCorner,screen);
                bool changed=WindowWidth!=rect.Width||WindowHeight!=rect.Height;
                WindowWidth=rect.Width;WindowHeight=rect.Height;_layoutDirty|=changed;
                Win32.SetWindowPos(_hwnd,IntPtr.Zero,rect.X,rect.Y,rect.Width,rect.Height,Win32.SWP_NOZORDER|Win32.SWP_NOACTIVATE);
                if(changed)Redraw();SetCornerCursor(_resizeCorner);
            }
            else MoveTo(_dragX+dx,_dragY+dy);
            if(_compact)EnsureTopmost();
        }
        private void FinishGeometry()
        {
            LastInteractionWasResize=_moved&&_resizeCorner!=ResizeCorner.None;
            _dragging=false;_resizeCorner=ResizeCorner.None;
            if(_moved)GeometryChanged?.Invoke();
            InteractionStateChanged?.Invoke();
        }
        public void EndPointer(Point point)
        {
            if(!_dragging)return;
            bool wasResize=_resizeCorner!=ResizeCorner.None;
            bool wasMoved=_moved;
            FinishGeometry();Win32.ReleaseCapture();
            if(!wasMoved&&!wasResize)Navigate(point.X-GetX()<WindowWidth/2?-1:1,true);
            Win32.SetCursor(Win32.LoadCursorW(IntPtr.Zero,Win32.IDC_ARROW));
        }
        public void HandleWheelDelta(int delta)
        {
            _wheelRemainder=(int)Math.Clamp((long)_wheelRemainder+delta,-12000,12000);
            while(Math.Abs(_wheelRemainder)>=120)
            {
                int sign=_wheelRemainder>0?1:-1;
                _wheelRemainder-=sign*120;Navigate(-sign);
            }
        }
        protected override IntPtr HandleMessage(uint msg, IntPtr wParam, IntPtr lParam)
        {
            switch (msg)
            {
                case Win32.WM_PAINT:
                    Win32.BeginPaint(_hwnd, out var ps); Win32.EndPaint(_hwnd, ref ps); return IntPtr.Zero;
                case Win32.WM_ERASEBKGND: return new IntPtr(1);
                case Win32.WM_LBUTTONDOWN:
                    int downX=(short)(lParam.ToInt64()&0xffff),downY=(short)((lParam.ToInt64()>>16)&0xffff);
                    BeginPointer(new Point(GetX()+downX,GetY()+downY));return IntPtr.Zero;
                case Win32.WM_MOUSEMOVE:
                    if (!_tracking)
                    {
                        var track = new Win32.TRACKMOUSEEVENT { cbSize = (uint)Marshal.SizeOf<Win32.TRACKMOUSEEVENT>(), dwFlags = 2, hwndTrack = _hwnd };
                        Win32.TrackMouseEvent(ref track); _tracking = true;
                    }
                    Win32.GetCursorPos(out var mousePoint);
                    UpdatePointer(new Point(mousePoint.X,mousePoint.Y));return IntPtr.Zero;
                case Win32.WM_LBUTTONUP:
                    Win32.GetCursorPos(out var upPoint);EndPointer(new Point(upPoint.X,upPoint.Y));return IntPtr.Zero;
                case 0x0215:
                    if(_dragging)FinishGeometry();return IntPtr.Zero;
                case Win32.WM_SETCURSOR:
                    Win32.GetCursorPos(out var cursorPoint);
                    if(SetCornerCursor(_dragging?_resizeCorner:CornerAt(new Point(cursorPoint.X,cursorPoint.Y))))return new IntPtr(1);
                    break;
                case Win32.WM_MOUSEWHEEL:
                    int delta = (short)((wParam.ToInt64() >> 16) & 0xffff);
                    HandleWheelDelta(delta);
                    return IntPtr.Zero;
                case Win32.WM_MOUSELEAVE:
                    _tracking = false;
                    if (_cfg.AutoHideOnLeave && !_dragging) HideRequested?.Invoke();
                    return IntPtr.Zero;
                case Win32.WM_RBUTTONUP: ShowContextMenu(); return IntPtr.Zero;
                case Win32.WM_DPICHANGED: _layoutDirty = true; Redraw(); return IntPtr.Zero;
                case Win32.WM_KEYDOWN:
                    if (wParam.ToInt32() == 0x22 || wParam.ToInt32() == 0x20) Navigate(1, true);
                    if (wParam.ToInt32() == 0x21) Navigate(-1, true);
                    return IntPtr.Zero;
            }
            return base.HandleMessage(msg, wParam, lParam);
        }
        private void ShowContextMenu()
        {
            IntPtr menu = Win32.CreatePopupMenu();
            _contextMenuOpen=true;InteractionStateChanged?.Invoke();
            try
            {
                Win32.AppendMenuW(menu, Win32.MF_STRING, 1, "导入 TXT / EPUB…");
                Win32.AppendMenuW(menu, Win32.MF_STRING, 2, "章节目录…");
                Win32.AppendMenuW(menu, Win32.MF_STRING, 4, "外观与模式设置…  Ctrl+Alt+S");
                Win32.AppendMenuW(menu, Win32.MF_STRING, 14, (_cfg.AutoScrollEnabled ? "暂停自动滚动  " : "开始自动滚动  ") + _cfg.HotkeyAutoScroll);
                Win32.AppendMenuW(menu, Win32.MF_STRING, 21, "滚动慢一点（当前 "+_cfg.AutoScrollPixelsPerSecond.ToString("0.#")+" px/s）");
                Win32.AppendMenuW(menu, Win32.MF_STRING, 22, "滚动快一点");
                Win32.AppendMenuW(menu, Win32.MF_STRING, 23, _cfg.AutoScrollDirection>0?"改为向下滚动 · 回看":"改为向上滚动 · 继续阅读");
                Win32.AppendMenuW(menu, Win32.MF_STRING, 12, "仅隐藏这个阅读区域");
                Win32.AppendMenuW(menu, Win32.MF_STRING, 13, "只保留这个阅读区域");
                if (_compact) Win32.AppendMenuW(menu, Win32.MF_STRING, 15, "将小条移回任务栏");
                Win32.AppendMenuW(menu, Win32.MF_SEPARATOR, 0, null);
                Win32.AppendMenuW(menu, Win32.MF_STRING, 3, "隐藏全部阅读区域  Ctrl+Alt+M");
                Win32.AppendMenuW(menu, Win32.MF_STRING, 9, "退出");
                Win32.GetCursorPos(out var point); Win32.SetForegroundWindow(_hwnd);
                int command = Win32.TrackPopupMenu(menu, Win32.TPM_RIGHTBUTTON | Win32.TPM_RETURNCMD, point.X, point.Y, 0, _hwnd, IntPtr.Zero);
                switch (command)
                {
                    case 1: ImportRequested?.Invoke(); break;
                    case 2: ChapterListRequested?.Invoke(); break;
                    case 3: ToggleRequested?.Invoke(); break;
                    case 4: SettingsRequested?.Invoke(); break;
                    case 9: ExitRequested?.Invoke(); break;
                    case 12: OwnHideRequested?.Invoke(); break;
                    case 13: OnlyThisRequested?.Invoke(); break;
                    case 14: AutoScrollRequested?.Invoke(); break;
                    case 15: DockRequested?.Invoke(); break;
                    case 21: SpeedChangeRequested?.Invoke(-1); break;
                    case 22: SpeedChangeRequested?.Invoke(1); break;
                    case 23: ReverseScrollRequested?.Invoke(); break;
                }
            }
            finally { Win32.DestroyMenu(menu);_contextMenuOpen=false;InteractionStateChanged?.Invoke(); }
        }
        public override void Dispose()
        {
            base.Dispose(); _surface?.Dispose(); _font?.Dispose();
        }
    }
}
