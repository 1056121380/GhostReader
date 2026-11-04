using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GhostReader
{
    internal static class FileLog
    {
        public static void Write(Exception ex)
        {
            try { File.AppendAllText(Path.Combine(Settings.ConfigDir,"crash.log"),DateTimeOffset.Now+" "+ex+Environment.NewLine); } catch { }
        }
    }
    internal static class Program
    {
        [STAThread] private static int Main(string[] args)
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if(args.Length>0 && args[0].Equals("--selftest",StringComparison.OrdinalIgnoreCase)) return SelfTest.Run(args);
            using var mutex=new Mutex(true,ProfileIdentity.MutexName,out bool created);
            if(!created)
            {
                IntPtr host = Win32.FindWindowW("GhostReaderHost", ProfileIdentity.HostTitle);
                int command = args.Contains("--settings") ? 5 : args.Contains("--hidden") ? 8 : 1;
                if(host != IntPtr.Zero && Win32.PostMessageW(host, Win32.RegisterWindowMessageW("GhostReaderCommandV2"), new IntPtr(command), IntPtr.Zero)) return 0;
                MessageBox.Show("GhostReader 已在运行。\nCtrl+Alt+M 显示/隐藏；Ctrl+Alt+S 打开设置。","GhostReader");return 0;
            }
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException+=(s,e)=>FileLog.Write(e.Exception);
            AppDomain.CurrentDomain.UnhandledException+=(s,e)=>{if(e.ExceptionObject is Exception ex)FileLog.Write(ex);};
            try { using var app=new GhostApp(args.Contains("--hidden"));return app.Run(args); }
            catch(Exception ex){FileLog.Write(ex);MessageBox.Show("启动失败："+ex.Message,"GhostReader");return 1;}
        }
    }
    internal sealed class GhostApp : ApplicationContext
    {
        private readonly Settings _cfg;
        private readonly AppState _state;
        private readonly HostWindow _host;
        private readonly ReaderWindow _reader;
        private readonly TaskbarPanel _panel;
        private readonly NotifyIcon _tray;
        private readonly ContextMenuStrip _menu;
        private readonly System.Windows.Forms.Timer _maintenance;
        private readonly System.Windows.Forms.Timer _autoTimer;
        private long _lastAutoTick;
        private bool _readingDialogOpen;
        private ToolStripMenuItem _miDesktop, _miTaskbar, _miAuto, _miSpeed, _miReverse;
        private bool _trayMenuOpen;
        private string _lastHotkeyNotice="";
        private readonly Icon _icon;
        private readonly WheelInputRouter _wheel;
        private bool _hidden,_quitting,_loading,_settingsOpen,_disposed;
        public GhostApp(bool startHidden=false)
        {
            _cfg=Settings.Load();_state=new AppState();_host=new HostWindow();
            _host.HotkeyPressed+=OnHotkey;_host.TaskbarRecreated+=()=>{if(!_hidden&&_cfg.TaskbarMode)_panel?.Reattach();};
            _host.Create(_cfg);
            // 热键被别的程序占用时已自动改用备选组合，把真正生效的键写回配置并保存，
            // 免得下次启动又撞车、设置页显示的也一直不是实际生效的键。
            if(_host.ApplyEffectiveHotkeys(_cfg))_cfg.Save();
            _reader=new ReaderWindow(_state,_cfg);_reader.Create(_cfg.WinX,_cfg.WinY,_cfg.WinW,_cfg.WinH);
            _panel=new TaskbarPanel(_state,_cfg);
            Wire(_reader,false);Wire(_panel.Window,true);
            _reader.GeometryChanged+=()=>{_cfg.WinX=_reader.GetX();_cfg.WinY=_reader.GetY();_cfg.WinW=_reader.WindowWidth;_cfg.WinH=_reader.WindowHeight;_cfg.Save();};
            _state.ManualNavigationRequested+=StopAutoScroll;
            _state.Changed+=()=>{if(_reader.Visible)_reader.Redraw();_panel.Redraw();};
            _menu=new ContextMenuStrip();
            _menu.Items.Add("隐藏 / 恢复全部阅读区域",null,(s,e)=>ToggleVisibility());
            _miDesktop = new ToolStripMenuItem("显示桌面端", null, (s,e)=>SetViewVisibility(false,!_reader.Visible));
            _miTaskbar = new ToolStripMenuItem("显示任务栏小条", null, (s,e)=>SetViewVisibility(true,!_panel.VisiblePanel));
            _menu.Items.Add(_miDesktop); _menu.Items.Add(_miTaskbar);
            _menu.Items.Add("只保留桌面端",null,(s,e)=>ShowOnlyView(false));
            _menu.Items.Add("只保留任务栏小条",null,(s,e)=>ShowOnlyView(true));
            _menu.Items.Add("找回小条并移回任务栏",null,(s,e)=>{_panel.Dock();SetViewVisibility(true,true);});
            _miAuto = new ToolStripMenuItem("自动滚动",null,(s,e)=>ToggleAutoScroll());
            _menu.Items.Add(_miAuto);
            _miSpeed=new ToolStripMenuItem("滚动速度");
            _miSpeed.DropDownItems.Add("慢一点",null,(s,e)=>AdjustScrollSpeed(-1));
            _miSpeed.DropDownItems.Add("快一点",null,(s,e)=>AdjustScrollSpeed(1));
            foreach(double speed in new[]{6d,12d,24d,48d}){double value=speed;_miSpeed.DropDownItems.Add(value+" px/s",null,(s,e)=>SetScrollSpeed(value));}
            _miReverse=new ToolStripMenuItem("切换滚动方向",null,(s,e)=>ReverseAutoScroll());
            _menu.Items.Add(_miSpeed);_menu.Items.Add(_miReverse);
            _menu.Opening+=(s,e)=>{_trayMenuOpen=true;RefreshMenuState();};
            _menu.Closed+=(s,e)=>{_trayMenuOpen=false;UpdateAutoTimerState();};
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add("导入 TXT / EPUB…",null,(s,e)=>OpenFile());
            _menu.Items.Add("章节目录…",null,(s,e)=>ShowChapters());
            _menu.Items.Add("外观与模式设置…",null,(s,e)=>ShowSettings());
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add("切换鼠标穿透",null,(s,e)=>ToggleThrough());
            _menu.Items.Add("将桌面小窗移回主屏",null,(s,e)=>RecoverDesktop());
            _menu.Items.Add("隐藏托盘图标（更隐蔽）",null,(s,e)=>ToggleTrayIcon());
            _menu.Items.Add("退出",null,(s,e)=>ExitApp());
            _icon=MakeIcon();_tray=new NotifyIcon {Icon=_icon,Text="GhostReader · Ctrl+Alt+M 隐藏 / 恢复",Visible=!_cfg.HideTrayIcon,ContextMenuStrip=_menu};
            _tray.DoubleClick+=(s,e)=>ToggleVisibility();
            _tray.BalloonTipClicked+=(s,e)=>ShowSettings();
            _hidden=_cfg.StartHidden||startHidden;
            RecoverOffscreen();ApplyVisibility();
            _wheel=new WheelInputRouter(_reader,_panel.Window,_cfg,()=>_settingsOpen||_readingDialogOpen||_loading||_quitting||_trayMenuOpen||_reader.ContextMenuOpen||_panel.Window.ContextMenuOpen);
            _maintenance=new System.Windows.Forms.Timer {Interval=500};
            _maintenance.Tick+=(s,e)=>{_state.SaveProgress();if(!_hidden&&_cfg.TaskbarMode)_panel.Position();};_maintenance.Start();
            _lastAutoTick = System.Diagnostics.Stopwatch.GetTimestamp();
            _autoTimer = new System.Windows.Forms.Timer { Interval = 16 };
            _autoTimer.Tick += (s,e) =>
            {
                long now = System.Diagnostics.Stopwatch.GetTimestamp();
                double elapsed = (now - _lastAutoTick) / (double)System.Diagnostics.Stopwatch.Frequency;
                _lastAutoTick = now; StepAutoScroll(elapsed);
            };
            UpdateAutoTimerState();
        }
        private void Wire(ReaderWindow window,bool compact)
        {
            window.ImportRequested+=OpenFile;window.SettingsRequested+=ShowSettings;window.ChapterListRequested+=ShowChapters;
            window.ToggleRequested+=ToggleVisibility;window.HideRequested+=HideAll;window.ExitRequested+=ExitApp;
            window.OwnHideRequested+=()=>SetViewVisibility(compact,false);
            window.OnlyThisRequested+=()=>ShowOnlyView(compact);
            window.AutoScrollRequested+=ToggleAutoScroll;
            window.SpeedChangeRequested+=AdjustScrollSpeed;window.ReverseScrollRequested+=ReverseAutoScroll;
            window.InteractionStateChanged+=UpdateAutoTimerState;
            if(compact)window.DockRequested+=()=>_panel.Dock();
        }
        public int Run(string[] args)
        {
            var startup=new System.Windows.Forms.Timer {Interval=100};
            startup.Tick+=async(s,e)=>
            {
                startup.Stop();startup.Dispose();
                string path=args.FirstOrDefault(a=>!a.StartsWith("--")&&File.Exists(a));
                bool explicitFile=path!=null;
                if(path==null && File.Exists(_cfg.LastBook))path=_cfg.LastBook;
                if(path!=null)await LoadBookAsync(path,explicitFile);
                if(args.Contains("--settings"))ShowSettings();
                ReportHotkeys();
            };
            startup.Start();Application.Run(this);return 0;
        }
        private void ApplyVisibility()
        {
            if(!_hidden&&_cfg.DesktopMode)_reader.Show();else _reader.Hide();
            if(!_hidden&&_cfg.TaskbarMode)_panel.Attach();else _panel.Detach();
            RefreshMenuState();
        }
        private void SetViewVisibility(bool compact,bool visible)
        {
            if (visible && _hidden) { _cfg.DesktopMode=false; _cfg.TaskbarMode=false; _hidden=false; }
            if(compact)_cfg.TaskbarMode=visible;else _cfg.DesktopMode=visible;
            _cfg.Save(); ApplyVisibility();
        }
        private void ShowOnlyView(bool compact)
        {
            _cfg.DesktopMode=!compact; _cfg.TaskbarMode=compact; _hidden=false;
            _cfg.Save(); ApplyVisibility();
        }
        private void ToggleVisibility()
        {
            _hidden=!_hidden;
            if(!_hidden&&!_cfg.DesktopMode&&!_cfg.TaskbarMode){_cfg.DesktopMode=true;_cfg.Save();}
            ApplyVisibility();
        }
        private void RefreshMenuState()
        {
            if(_miDesktop==null)return;
            _miDesktop.Checked=_reader.Visible;_miTaskbar.Checked=_panel.VisiblePanel;
            _miAuto.Checked=_cfg.AutoScrollEnabled;_miAuto.Enabled=_state.Book!=null||_cfg.AutoScrollEnabled;
            _miAuto.Text=_cfg.AutoScrollEnabled?"暂停自动滚动":"开始自动滚动";
            _miSpeed.Text="速度："+_cfg.AutoScrollPixelsPerSecond.ToString("0.#")+" px/s";
            _miReverse.Text=_cfg.AutoScrollDirection>0?"改为向下滚动 · 回看":"改为向上滚动 · 继续阅读";
            UpdateAutoTimerState();
        }
        private bool CanAnimate => _cfg.AutoScrollEnabled&&!_hidden&&!_settingsOpen&&!_readingDialogOpen&&!_loading&&!_quitting&&!_trayMenuOpen&&_state.Book!=null&&(_reader.Visible||_panel.VisiblePanel)&&!_reader.IsInteracting&&!_panel.Window.IsInteracting;
        private void UpdateAutoTimerState()
        {
            if(_autoTimer==null)return;
            bool run=CanAnimate;if(run==_autoTimer.Enabled)return;
            if(run){_lastAutoTick=System.Diagnostics.Stopwatch.GetTimestamp();_autoTimer.Start();}
            else _autoTimer.Stop();
        }
        private void SetScrollSpeed(double speed)
        {
            _cfg.AutoScrollPixelsPerSecond=Math.Clamp(speed,1,120);_cfg.Save();RefreshMenuState();
        }
        private void AdjustScrollSpeed(int direction)
        {
            double current=_cfg.AutoScrollPixelsPerSecond;
            SetScrollSpeed(direction>0?Math.Max(current+1,Math.Round(current*1.25)):Math.Min(current-1,Math.Round(current/1.25)));
        }
        private void ReverseAutoScroll(){_cfg.AutoScrollDirection=_cfg.AutoScrollDirection>0?-1:1;_cfg.Save();RefreshMenuState();}
        private void ToggleAutoScroll()
        {
            if(_state.Book==null&&!_cfg.AutoScrollEnabled){_tray.ShowBalloonTip(2500,"GhostReader","请先导入书籍，再开始自动滚动。",ToolTipIcon.Info);return;}
            _cfg.AutoScrollEnabled=!_cfg.AutoScrollEnabled;_cfg.Save();RefreshMenuState();
        }
        private void StopAutoScroll()
        {
            if(!_cfg.AutoScrollEnabled)return;
            _cfg.AutoScrollEnabled=false;_cfg.Save();RefreshMenuState();
        }
        private void StepAutoScroll(double seconds)
        {
            if(!CanAnimate){UpdateAutoTimerState();return;}
            ReaderWindow view = _reader.Visible ? _reader : _panel.VisiblePanel ? _panel.Window : null;
            if(view==null)return;
            var limit=view.LastReadablePosition;
            if(_reader.Visible&&_panel.VisiblePanel)
            {
                var other=_panel.Window.LastReadablePosition;
                if(other.Paragraph>limit.Paragraph||(other.Paragraph==limit.Paragraph&&other.Offset>limit.Offset))limit=other;
            }
            if(!view.AdvanceAutoScrollTo(seconds,limit))StopAutoScroll();
        }
        private void HideAll(){_hidden=true;ApplyVisibility();}
        private void OnHotkey(int id)
        {
            if(_quitting)return;
            if(id==1){ToggleVisibility();return;}
            if(id==8){HideAll();return;}
            if(_settingsOpen)return;
            switch(id)
            {
                case 1:ToggleVisibility();break;
                case 2:ActiveView().Navigate(1,true);break;
                case 3:ActiveView().Navigate(-1,true);break;
                case 4:ExitApp();break;
                case 5:ShowSettings();break;
                case 6:ToggleThrough();break;
                case 7:ToggleAutoScroll();break;
            }
        }
        private ReaderWindow ActiveView()=>_cfg.DesktopMode&&!_hidden?_reader:_panel.Window;
        private void ToggleThrough(){_cfg.ClickThrough=!_cfg.ClickThrough;_cfg.Save();RefreshAppearance();}
        private void RefreshAppearance()
        {
            _reader.Resize(_cfg.WinW,_cfg.WinH);_reader.ApplyStyles();_reader.RebuildLayout();
            if(_panel.Window.IsCreated){_panel.Window.ApplyStyles();_panel.Window.RebuildLayout();}
            ApplyVisibility();
        }
        private void ShowSettings()
        {
            if(_settingsOpen)return;_settingsOpen=true;UpdateAutoTimerState();
            var snapshot=_cfg.Clone();
            try
            {
                using var form=new SettingsForm(_cfg,HotkeyNotices());
                form.PreviewChanged+=candidate=>{_cfg.CopyFrom(candidate);RefreshAppearance();};
                if(form.ShowDialog()!=DialogResult.OK)_cfg.CopyFrom(snapshot);
                _host.RegisterHotkeys(_cfg);
                if(_host.ApplyEffectiveHotkeys(_cfg))_cfg.Save();
                RefreshAppearance();UpdateTrayVisibility();ReportHotkeys();
            }
            finally{_settingsOpen=false;UpdateAutoTimerState();}
        }
        /// <summary>设置窗口顶部的热键提示：区分“已自动改用”和“彻底注册不了”。</summary>
        private string[] HotkeyNotices()
        {
            return _host.HotkeySlots
                .Where(s=>s.AutoChanged||s.Failed)
                .Select(s=>s.Failed?s.Requested:s.Requested+"（已被占用，自动改用 "+s.Effective+"）")
                .ToArray();
        }
        private void UpdateTrayVisibility()
        {
            // 隐藏托盘图标时，只要“找回用”的热键（显示 / 设置 / 退出）有一个没注册成功，
            // 就必须保留托盘图标 —— 否则既没图标也没快捷键，程序就真的找不回来了。
            bool recoveryKeysOk=_host.HotkeySlots.Count>0&&_host.HotkeySlots
                .Where(s=>s.Role=="toggle"||s.Role=="settings"||s.Role=="exit")
                .All(s=>!s.Failed);
            _tray.Visible=!_cfg.HideTrayIcon||!recoveryKeysOk||_host.FailedHotkeys.Count>0;
        }
        /// <summary>
        /// 隐藏托盘图标前必须先说清楚怎么找回来 —— 否则托盘里没有图标、
        /// 阅读区又被隐藏时，屏幕上什么都不剩，只剩杀进程一条路。
        /// </summary>
        private void ToggleTrayIcon()
        {
            if(!_cfg.HideTrayIcon)
            {
                DialogResult answer=MessageBox.Show(
                    "确定要隐藏托盘图标吗？\n\n隐藏后托盘里就找不到它了，但仍可用下面任一方式找回并退出：\n"+
                    "  · "+_cfg.HotkeyToggle+"          呼出阅读区域\n"+
                    "  · "+_cfg.HotkeySettings+"          打开设置（取消勾选即可恢复托盘图标）\n"+
                    "  · "+_cfg.HotkeyExit+"  直接彻底退出\n\n"+
                    "想恢复托盘图标：用上面的设置快捷键打开设置，取消勾选“隐藏托盘图标”。",
                    "GhostReader",MessageBoxButtons.OKCancel,MessageBoxIcon.Information);
                if(answer!=DialogResult.OK)return;
            }
            _cfg.HideTrayIcon=!_cfg.HideTrayIcon;_cfg.Save();UpdateTrayVisibility();
        }
        private void ReportHotkeys()
        {
            var failed=_host.HotkeySlots.Where(s=>s.Failed).Select(s=>s.Requested).ToList();
            var auto=_host.HotkeySlots.Where(s=>s.AutoChanged).ToList();
            if(failed.Count==0&&auto.Count==0){_lastHotkeyNotice="";return;}
            var parts=new List<string>();
            if(auto.Count>0)parts.Add("已被占用，已自动改用 "+string.Join("、",auto.Select(s=>s.Effective)));
            if(failed.Count>0)parts.Add("无法注册："+string.Join("、",failed));
            string notice=string.Join("；",parts.ToArray());
            _tray.Visible=true;
            if(notice==_lastHotkeyNotice)return;
            _lastHotkeyNotice=notice;
            _tray.ShowBalloonTip(5000,"GhostReader · 热键已自动调整",notice+"。可在设置里改回。",ToolTipIcon.Warning);
        }
        private async void OpenFile()
        {
            using var dialog=new OpenFileDialog {Title="导入小说",Filter="小说文件 (*.txt;*.epub)|*.txt;*.epub|TXT 文本 (*.txt)|*.txt|EPUB (*.epub)|*.epub"};
            if(File.Exists(_cfg.LastBook))dialog.InitialDirectory=Path.GetDirectoryName(_cfg.LastBook);
            DialogResult result;
            _readingDialogOpen=true;UpdateAutoTimerState();
            try { result=dialog.ShowDialog(); } finally { _readingDialogOpen=false;UpdateAutoTimerState(); }
            if(result==DialogResult.OK)await LoadBookAsync(dialog.FileName,true);
        }
        private async Task LoadBookAsync(string path,bool show)
        {
            if(_loading)return;_loading=true;UpdateAutoTimerState();
            try
            {
                string error=null;
                var book=await Task.Run(()=>BookLoader.Load(Path.GetFullPath(path),msg=>error=msg));
                if(_quitting)return;
                if(book==null||book.Lines.Count==0)
                {MessageBox.Show("文件为空或无法读取。\n"+error,"GhostReader",MessageBoxButtons.OK,MessageBoxIcon.Warning);return;}
                _state.OpenBook(book);_cfg.LastBook=book.FilePath;_cfg.Save();
                _reader.RebuildLayout();if(_panel.Window.IsCreated)_panel.Window.RebuildLayout();
                if(show)_hidden=false;ApplyVisibility();
                _tray.Text=("GhostReader · "+book.Title).Substring(0,Math.Min(63,("GhostReader · "+book.Title).Length));
            }
            catch(Exception ex){FileLog.Write(ex);if(!_quitting)MessageBox.Show("导入失败："+ex.Message,"GhostReader");}
            finally{_loading=false;UpdateAutoTimerState();}
        }
        private void ShowChapters()
        {
            if(_state.Book==null){MessageBox.Show("请先导入 TXT 或 EPUB。","GhostReader");return;}
            using var form=new ChapterForm(_state);
            _readingDialogOpen=true;UpdateAutoTimerState();
            try { form.ShowDialog(); } finally { _readingDialogOpen=false;UpdateAutoTimerState(); }
        }
        private void RecoverOffscreen()
        {
            var rect=new Rectangle(_cfg.WinX,_cfg.WinY,_cfg.WinW,_cfg.WinH);
            if(!Screen.AllScreens.Any(s=>Rectangle.Intersect(s.WorkingArea,rect).Width>=60&&Rectangle.Intersect(s.WorkingArea,rect).Height>=30))RecoverDesktop();
        }
        private void RecoverDesktop()
        {
            var area=Screen.PrimaryScreen.WorkingArea;_cfg.WinX=area.Left+40;_cfg.WinY=area.Top+40;
            _reader.MoveTo(_cfg.WinX,_cfg.WinY);_cfg.Save();
        }
        private void ExitApp(){if(_quitting)return;_quitting=true;HideAll();ExitThread();}
        protected override void Dispose(bool disposing)
        {
            if(disposing&&!_disposed)
            {
                _disposed=true;_quitting=true;_maintenance.Stop();_maintenance.Dispose();_autoTimer.Stop();_autoTimer.Dispose();
                _cfg.WinX=_reader.GetX();_cfg.WinY=_reader.GetY();_cfg.Save();
                _state.RememberCurrent();_state.SaveProgress();
                _tray.Visible=false;_tray.Dispose();_menu.Dispose();_icon.Dispose();
                _wheel.Dispose();_panel.Dispose();_reader.Dispose();_host.Dispose();_state.Dispose();
            }
            base.Dispose(disposing);
        }
        private static Icon MakeIcon()
        {
            using var bitmap=new Bitmap(32,32);using var g=Graphics.FromImage(bitmap);
            g.Clear(Color.Transparent);using var bg=new SolidBrush(Color.FromArgb(35,40,48));g.FillEllipse(bg,1,1,30,30);
            using var fg=new SolidBrush(Color.FromArgb(225,231,238));g.FillRectangle(fg,8,9,16,15);
            using var spine=new Pen(Color.FromArgb(35,40,48),2);g.DrawLine(spine,16,9,16,24);
            IntPtr handle=bitmap.GetHicon();try{using var icon=Icon.FromHandle(handle);return(Icon)icon.Clone();}finally{Win32.DestroyIcon(handle);}
        }
    }
}
