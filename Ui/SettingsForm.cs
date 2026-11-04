using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace GhostReader
{
    internal sealed class SettingsForm : Form
    {
        private readonly Settings _cfg;
        private readonly Settings _draft;
        private readonly List<TextBox> _hotkeys = new List<TextBox>();
        private bool _ready;
        private readonly System.Windows.Forms.Timer _previewTimer;
        private bool _previewPending;
        public event Action<Settings> PreviewChanged;
        public SettingsForm(Settings cfg,IReadOnlyList<string> hotkeyWarnings=null)
        {
            _cfg = cfg; _draft = cfg.Clone(); _draft.Normalize();
            _previewTimer=new System.Windows.Forms.Timer{Interval=80};
            _previewTimer.Tick+=(s,e)=>{_previewTimer.Stop();if(_previewPending){_previewPending=false;PreviewChanged?.Invoke(_draft.Clone());}};
            Text = "GhostReader · 外观与阅读模式";
            Font = new Font("Microsoft YaHei UI", 9f);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(620, 660); MinimumSize = new Size(550, 540);
            StartPosition = FormStartPosition.CenterScreen;
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10, 8, 10, 4) };
            var save = new Button { Text = "保存", Width = 90, Height = 30 };
            var cancel = new Button { Text = "取消", Width = 90, Height = 30, DialogResult = DialogResult.Cancel };
            save.Click += (s, e) => Save(); buttons.Controls.Add(save); buttons.Controls.Add(cancel);
            var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(12, 7) };
            tabs.TabPages.Add(BuildAppearance(false)); tabs.TabPages.Add(BuildAppearance(true)); tabs.TabPages.Add(BuildAutoScroll()); tabs.TabPages.Add(BuildBehavior());
            Controls.Add(tabs); Controls.Add(buttons); AcceptButton = save; CancelButton = cancel;
            if(hotkeyWarnings!=null&&hotkeyWarnings.Count>0)
            {
                var warning=new Label{Name="HotkeyWarning",Dock=DockStyle.Top,Height=66,Padding=new Padding(12,8,12,4),ForeColor=Color.FromArgb(125,73,0),BackColor=Color.FromArgb(255,245,218),Text="热键调整："+string.Join("、",hotkeyWarnings.Select(key=>string.IsNullOrWhiteSpace(key)?"（空快捷键）":key))+"\n标注“自动改用”表示原组合被别的程序占用，程序已换用备选组合；可在‘隐蔽与快捷键’页改成你要的组合。"};
                Controls.Add(warning);
            }
            _ready = true;
        }
        private FlowLayoutPanel Content(TabPage tab)
        {
            var content = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(14) };
            tab.Controls.Add(content); return content;
        }
        private TabPage BuildAppearance(bool compact)
        {
            var tab = new TabPage(compact ? "任务栏正文" : "桌面小窗"); var body = Content(tab);
            var help = new Label { AutoSize = true, MaximumSize = new Size(520, 0), Text = compact ? "细长正文浮层，可拖到屏幕任意位置；也可选择停靠任务栏。" : "无边框小窗：拖动正文移动，拖动四边或四角缩放。背景与文字独立透明。", Margin = new Padding(0, 0, 0, 12) };
            body.Controls.Add(help);
            AddCheck(body, compact ? "启用任务栏正文" : "启用桌面小窗", compact ? _draft.TaskbarMode : _draft.DesktopMode, value => { if(compact) _draft.TaskbarMode=value; else _draft.DesktopMode=value; });
            var style = _draft.Appearance(compact);
            AddSlider(body, "背景不透明度", 0, 100, (int)Math.Round(style.BackgroundOpacity * 100), "%", value => { if(compact) _draft.TaskbarBackgroundOpacity=value/100.0; else _draft.BackgroundOpacity=value/100.0; });
            AddSlider(body, "文字不透明度", 0, 100, (int)Math.Round(style.TextOpacity * 100), "%", value => { if(compact) _draft.TaskbarTextOpacity=value/100.0; else _draft.TextOpacity=value/100.0; });
            AddSlider(body, "字号", 6, compact ? 32 : 48, (int)Math.Round(style.FontSize), " pt", value => { if(compact) _draft.TaskbarFontSize=value; else _draft.FontSize=value; });
            var fonts = new ComboBox { Width = 300, DropDownStyle = ComboBoxStyle.DropDownList };
            using(var installed = new System.Drawing.Text.InstalledFontCollection())
                fonts.Items.AddRange(installed.Families.Select(f => f.Name).Distinct().OrderBy(n => n).Cast<object>().ToArray());
            int selected = fonts.FindStringExact(style.FontFamily); fonts.SelectedIndex = selected >= 0 ? selected : 0;
            fonts.SelectedIndexChanged += (s,e) => { if(compact) _draft.TaskbarFontFamily=fonts.Text; else _draft.FontFamily=fonts.Text; Changed(); };
            Row(body, "字体", fonts);
            var colors = new FlowLayoutPanel { Width=360, Height=36, Margin=Padding.Empty };
            var foreground = new Button { Text="文字颜色…", Width=135, Height=28, BackColor=style.TextColor, ForeColor=style.TextColor.GetBrightness()>.5?Color.Black:Color.White };
            var background = new Button { Text="背景颜色…", Width=135, Height=28, BackColor=style.BackgroundColor, ForeColor=style.BackgroundColor.GetBrightness()>.5?Color.Black:Color.White };
            foreground.Click += (s,e) => { using var d = new ColorDialog { Color=_draft.Appearance(compact).TextColor, FullOpen=true }; if(d.ShowDialog(this)==DialogResult.OK) { if(compact) _draft.TaskbarTextColor=d.Color.ToArgb(); else _draft.TextColor=d.Color.ToArgb(); foreground.BackColor=d.Color; foreground.ForeColor=d.Color.GetBrightness()>.5?Color.Black:Color.White; Changed(); } };
            background.Click += (s,e) => { using var d = new ColorDialog { Color=_draft.Appearance(compact).BackgroundColor, FullOpen=true }; if(d.ShowDialog(this)==DialogResult.OK) { if(compact) _draft.TaskbarBackColor=d.Color.ToArgb(); else _draft.BackColor=d.Color.ToArgb(); background.BackColor=d.Color; background.ForeColor=d.Color.GetBrightness()>.5?Color.Black:Color.White; Changed(); } };
            colors.Controls.Add(foreground); colors.Controls.Add(background); Row(body,"颜色",colors);
            if (compact)
            {
                AddNumber(body,"浮层宽度（像素）",100,10000,_draft.TaskbarWidth,v=>_draft.TaskbarWidth=v);
                AddCheck(body,"使用自定义尺寸（拖动边缘或四角后自动启用）",_draft.TaskbarCustomSize,v=>_draft.TaskbarCustomSize=v);
                AddNumber(body,"自定义高度",20,10000,_draft.TaskbarHeight,v=>_draft.TaskbarHeight=v);
                var placement=new ComboBox {Width=300,DropDownStyle=ComboBoxStyle.DropDownList};
                placement.Items.AddRange(new object[]{"停靠任务栏","自由位置（可任意拖动）"});placement.SelectedIndex=_draft.TaskbarFreePosition?1:0;
                placement.SelectedIndexChanged+=(s,e)=>{_draft.TaskbarFreePosition=placement.SelectedIndex==1;Changed();};Row(body,"摆放模式",placement);
                AddNumber(body,"自由位置 X",-100000,100000,_draft.TaskbarX,v=>_draft.TaskbarX=v);
                AddNumber(body,"自由位置 Y",-100000,100000,_draft.TaskbarY,v=>_draft.TaskbarY=v);
                AddCheck(body,"自动避开任务栏图标（推荐：不再压住开始按钮）",_draft.TaskbarAutoDock,v=>_draft.TaskbarAutoDock=v);
                AddNumber(body,"停靠时距左侧（关闭自动避让后生效）",0,10000,_draft.TaskbarOffset,v=>_draft.TaskbarOffset=v);
            }
            else
            {
                AddSlider(body,"行距",100,250,(int)Math.Round(_draft.LineSpacing*100),"%",v=>_draft.LineSpacing=v/100.0);
                AddNumber(body,"内边距",0,60,_draft.Padding,v=>_draft.Padding=v);
                AddNumber(body,"窗口宽度",160,10000,_draft.WinW,v=>_draft.WinW=v);
                AddNumber(body,"窗口高度",60,10000,_draft.WinH,v=>_draft.WinH=v);
                AddCheck(body,"显示底部章节提示",_draft.ShowStatus,v=>_draft.ShowStatus=v);
            }
            var sample = new Panel { Width=520, Height=100, Margin=new Padding(0,12,0,0) };
            sample.Paint += (s,e) =>
            {
                for(int y=0;y<sample.Height;y+=12)for(int x=0;x<sample.Width;x+=12)
                { using var b = new SolidBrush(((x/12+y/12)%2)==0?Color.FromArgb(215,218,222):Color.FromArgb(242,243,245)); e.Graphics.FillRectangle(b,x,y,12,12); }
                var current=_draft.Appearance(compact);
                using var bg=new SolidBrush(current.BackgroundBrushColor); e.Graphics.FillRectangle(bg,sample.ClientRectangle);
                using var font=current.CreateFont(); using var fg=new SolidBrush(current.TextBrushColor);
                e.Graphics.DrawString("风吹过书页，故事悄然继续。",font,fg,new RectangleF(12,15,496,80));
            };
            PreviewChanged += c=>sample.Invalidate(); body.Controls.Add(sample);
            return tab;
        }
        private TabPage BuildAutoScroll()
        {
            var tab=new TabPage("自动滚动");var body=Content(tab);
            body.Controls.Add(new Label {AutoSize=true,MaximumSize=new Size(520,0),Margin=new Padding(0,0,0,14),Text="以连续像素平滑移动正文，不是定时跳页。向上滑动继续阅读，向下滑动回看。"});
            AddCheck(body,"启用自动滚动",_draft.AutoScrollEnabled,v=>_draft.AutoScrollEnabled=v);
            AddSlider(body,"速度",1,120,(int)Math.Round(_draft.AutoScrollPixelsPerSecond)," px/s",v=>_draft.AutoScrollPixelsPerSecond=v);
            var direction=new ComboBox {Width=300,DropDownStyle=ComboBoxStyle.DropDownList};
            direction.Items.AddRange(new object[]{"文字向上滑动 · 继续阅读","文字向下滑动 · 向前回看"});direction.SelectedIndex=_draft.AutoScrollDirection<0?1:0;
            direction.SelectedIndexChanged+=(s,e)=>{_draft.AutoScrollDirection=direction.SelectedIndex==1?-1:1;Changed();};Row(body,"方向",direction);
            body.Controls.Add(new Label {AutoSize=true,MaximumSize=new Size(520,0),Margin=new Padding(0,16,0,0),Text="建议从 6–15 像素/秒开始。桌面端与小条共享阅读位置，行宽不同会各自连续重排。\n\n右键菜单、托盘或自动滚动热键可开始/暂停。隐藏全部、打开设置/目录/导入框或拖动时暂时暂停；恢复后继续，不补跳。手动翻阅会关闭自动滚动。到书首或末尾时自动停止。"});
            return tab;
        }
        private TabPage BuildBehavior()
        {
            var tab=new TabPage("隐蔽与快捷键"); var body=Content(tab);
            AddCheck(body,"桌面小窗始终置顶",_draft.AlwaysOnTop,v=>_draft.AlwaysOnTop=v);
            AddCheck(body,"鼠标穿透（Ctrl+Alt+T 随时恢复）",_draft.ClickThrough,v=>_draft.ClickThrough=v);
            AddCheck(body,"鼠标穿透时，悬停阅读区域仍可滚轮阅读",_draft.WheelWhenClickThrough,v=>_draft.WheelWhenClickThrough=v);
            AddCheck(body,"鼠标移出阅读区域时隐藏全部",_draft.AutoHideOnLeave,v=>_draft.AutoHideOnLeave=v);
            AddCheck(body,"下次启动隐藏阅读区域",_draft.StartHidden,v=>_draft.StartHidden=v);
            AddCheck(body,"隐藏托盘图标",_draft.HideTrayIcon,v=>_draft.HideTrayIcon=v);
            body.Controls.Add(new Label { AutoSize=true,MaximumSize=new Size(520,0),Margin=new Padding(0,0,0,10),ForeColor=Color.FromArgb(150,80,20),
                Text="隐藏后托盘里就没有它了。找回方式：\n· "+_draft.HotkeyToggle+"          呼出阅读区域\n· "+_draft.HotkeySettings+"          打开设置（取消勾选即可恢复托盘图标）\n· "+_draft.HotkeyExit+"  直接彻底退出" });
            body.Controls.Add(new Label { AutoSize=true,MaximumSize=new Size(520,0),Margin=new Padding(0,12,0,12),Text="即使文字与背景均为 0%，也可用设置热键恢复。设置窗口打开时仍可一键隐藏阅读区域，其余快捷键暂停；新热键保存后生效。热键冲突时将给出提示。" });
            string[] names={"隐藏 / 恢复全部","下一页 / 下一行","上一页 / 上一行","退出程序","打开设置","切换鼠标穿透","自动滚动开关"};
            string[] values={_draft.HotkeyToggle,_draft.HotkeyNext,_draft.HotkeyPrev,_draft.HotkeyExit,_draft.HotkeySettings,_draft.HotkeyThrough,_draft.HotkeyAutoScroll};
            foreach(var pair in names.Zip(values,(name,value)=>(name,value)))
            { var input=new TextBox { Width=280,Text=pair.value };_hotkeys.Add(input);Row(body,pair.name,input); }
            body.Controls.Add(new Label { AutoSize=true,MaximumSize=new Size(520,0),Text="支持 Ctrl / Alt / Shift / Win + 字母、数字、方向键、F1–F24。隐藏不等于防截屏或进程隐藏。" });
            return tab;
        }
        private void AddCheck(FlowLayoutPanel body,string title,bool value,Action<bool> setter)
        {
            var box=new CheckBox { Text=title,Checked=value,AutoSize=true,Margin=new Padding(0,5,0,7) };
            box.CheckedChanged+=(s,e)=>{setter(box.Checked);Changed();};body.Controls.Add(box);
        }
        private void Row(FlowLayoutPanel body,string title,Control control)
        {
            var row=new Panel { Width=530,Height=Math.Max(38,control.Height+4),Margin=new Padding(0,2,0,2) };
            var label=new Label { Text=title,Left=0,Top=6,Width=140,AutoSize=false };
            control.Left=146;control.Top=0;row.Controls.Add(label);row.Controls.Add(control);body.Controls.Add(row);
        }
        private void AddSlider(FlowLayoutPanel body,string title,int min,int max,int value,string unit,Action<int> setter)
        {
            var panel=new Panel {Width=370,Height=38};
            var slider=new TrackBar {Left=0,Width=282,Height=34,Minimum=min,Maximum=max,Value=Math.Clamp(value,min,max),TickStyle=TickStyle.None,SmallChange=1,LargeChange=5};
            var label=new Label {Left=286,Top=5,Width=80,Text=slider.Value+unit};
            slider.ValueChanged+=(s,e)=>{label.Text=slider.Value+unit;setter(slider.Value);Changed();};
            panel.Controls.Add(slider);panel.Controls.Add(label);Row(body,title,panel);
        }
        private void AddNumber(FlowLayoutPanel body,string title,int min,int max,int value,Action<int> setter)
        {
            var number=new NumericUpDown {Width=150,Minimum=min,Maximum=max,Value=Math.Clamp(value,min,max)};
            number.ValueChanged+=(s,e)=>{setter((int)number.Value);Changed();};Row(body,title,number);
        }
        private void Changed()
        {
            if(!_ready)return;_previewPending=true;if(!_previewTimer.Enabled)_previewTimer.Start();
        }
        protected override void Dispose(bool disposing)
        {
            if(disposing){_previewPending=false;_previewTimer.Stop();_previewTimer.Dispose();}
            base.Dispose(disposing);
        }
        private void Save()
        {
            var seen=new HashSet<string>();
            foreach(var input in _hotkeys)
            {
                if(!HotKeySpec.Parse(input.Text,out uint mods,out uint key) || !seen.Add(mods+":"+key))
                { MessageBox.Show(this,"热键无效或重复："+input.Text,"GhostReader");input.Focus();return; }
            }
            _draft.HotkeyToggle=_hotkeys[0].Text;_draft.HotkeyNext=_hotkeys[1].Text;_draft.HotkeyPrev=_hotkeys[2].Text;
            _draft.HotkeyExit=_hotkeys[3].Text;_draft.HotkeySettings=_hotkeys[4].Text;_draft.HotkeyThrough=_hotkeys[5].Text;_draft.HotkeyAutoScroll=_hotkeys[6].Text;
            _cfg.CopyFrom(_draft);_cfg.Save();DialogResult=DialogResult.OK;Close();
        }
    }
}
