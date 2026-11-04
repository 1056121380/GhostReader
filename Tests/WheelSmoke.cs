using GhostReader;
using System.Drawing;
using System.Windows.Forms;

internal static class WheelSmoke
{
    internal static int Run(bool resize=false)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);Application.EnableVisualStyles();
        using var state=new AppState();
        var book=new Book{FilePath=Path.Combine(Settings.ConfigDir,"wheel-fixture.txt"),Title="Wheel fixture"};
        for(int i=0;i<200;i++)book.Lines.Add($"Line {i:000}: GhostReader native wheel route. 透明区域滚轮测试正文。");
        state.OpenBook(book);state.SetAnchor(new ReadingPosition());
        var cfg=new Settings{ClickThrough=!resize,TaskbarCustomSize=resize,TaskbarHeight=180,TaskbarFreePosition=resize,TaskbarX=1100,TaskbarY=500,BackgroundOpacity=0,TextColor=Color.Black.ToArgb(),ShowInTaskbar=true,TaskbarOffset=600,TaskbarBackgroundOpacity=.3,TaskbarTextOpacity=1};
        using var reader=new ReaderWindow(state,cfg);using var mini=new TaskbarPanel(state,cfg);
        using var form=new Form{Text="GhostReader Wheel Smoke",StartPosition=FormStartPosition.Manual,Location=new Point(100,100),ClientSize=new Size(800,400),AutoScaleMode=AutoScaleMode.None};
        var stats=new Label{Left=16,Top=12,Width=760,Height=70};form.Controls.Add(stats);
        var close=new Button{Left=16,Top=340,Width=130,Height=34,Text="退出测试"};close.Click+=(s,e)=>form.Close();form.Controls.Add(close);
        var through=new CheckBox{Left=240,Top=344,Width=170,Text="鼠标穿透",Checked=cfg.ClickThrough};form.Controls.Add(through);
        through.CheckedChanged+=(s,e)=>{cfg.ClickThrough=through.Checked;reader.ApplyStyles();mini.Window.ApplyStyles();};
        using var maintenance=new System.Windows.Forms.Timer{Interval=500};maintenance.Tick+=(s,e)=>mini.Position();
        WheelInputRouter router=null;int movements=0;
        void Update(){var anchor=state.GetAnchor();stats.Text=$"Movements={movements}; Source={anchor.Paragraph}:{anchor.Offset}; ForegroundIsBackdrop={Win32.GetForegroundWindow()==form.Handle}; Hook={router?.IsHookInstalled}";}
        state.ManualNavigationRequested+=()=>movements++;
        state.Changed+=()=>{reader.Redraw();mini.Redraw();Update();};
        reader.GeometryChanged+=()=>{cfg.WinX=reader.GetX();cfg.WinY=reader.GetY();cfg.WinW=reader.WindowWidth;cfg.WinH=reader.WindowHeight;cfg.Save();Update();};
        form.Shown+=(s,e)=>{reader.Create(form.Left+20,form.Top+110,600,190);reader.Show();mini.Attach();router=new WheelInputRouter(reader,mini.Window,cfg,()=>false);maintenance.Start();Update();};
        try{Application.Run(form);}finally{router?.Dispose();}
        File.WriteAllText(Path.Combine(Settings.ConfigDir,"wheel-smoke-result.txt"),stats.Text);
        return 0;
    }
}
