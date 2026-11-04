using GhostReader;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
internal static class ReadingUxTests
{
 const BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Instance;
 static T Field<T>(object value,string name)=>(T)value.GetType().GetField(name,Flags).GetValue(value);
 static void Invoke(object value,string name,params object[] args)=>value.GetType().GetMethod(name,Flags).Invoke(value,args);
 internal static void Run(Action<bool,string> check)
 {
 var book=new Book{FilePath=Path.Combine(Settings.ConfigDir,"ux-fixture.txt"),Title="UX fixture"};
 for(int i=0;i<50;i++)book.Lines.Add(new string('夜',150));
 book.Chapters.Add(new Book.Chapter{Title="第一章",StartLine=0,EndLine=2});
 book.Chapters.Add(new Book.Chapter{Title="重复章节",StartLine=2,EndLine=4});
 book.Chapters.Add(new Book.Chapter{Title="特别的重复章节",StartLine=4,EndLine=50});
 new Settings{AutoScrollEnabled=false,DesktopMode=true,TaskbarMode=false,StartHidden=false}.Save();
 using(var app=new GhostApp()){
 var cfg=Field<Settings>(app,"_cfg");var state=Field<AppState>(app,"_state");var reader=Field<ReaderWindow>(app,"_reader");var timer=Field<System.Windows.Forms.Timer>(app,"_autoTimer");
 check(!timer.Enabled,"disabled auto reading has no 16ms animation timer running");
 state.OpenBook(book);reader.RebuildLayout();cfg.AutoScrollEnabled=true;Invoke(app,"RefreshMenuState");check(timer.Enabled,"enabled visible reading starts animation timer");
 Invoke(app,"HideAll");check(!timer.Enabled&&cfg.AutoScrollEnabled,"global hiding suspends timer without losing auto-reading preference");
 Invoke(app,"ToggleVisibility");check(timer.Enabled,"restoring visible reading resumes animation timer");
 var point=new Point(reader.GetX()+100,reader.GetY()+100);reader.BeginPointer(point);check(!timer.Enabled,"dragging pauses animation timer immediately");reader.UpdatePointer(new Point(point.X+10,point.Y+10));reader.EndPointer(new Point(point.X+10,point.Y+10));check(timer.Enabled&&cfg.AutoScrollEnabled,"finishing a drag resumes the prior auto-reading preference");
 var adjust=typeof(GhostApp).GetMethod("AdjustScrollSpeed",Flags);var reverse=typeof(GhostApp).GetMethod("ReverseAutoScroll",Flags);
 check(adjust!=null&&reverse!=null,"quick speed and reverse actions are available outside settings");
 if(adjust!=null&&reverse!=null){cfg.AutoScrollPixelsPerSecond=12;adjust.Invoke(app,new object[]{1});check(cfg.AutoScrollPixelsPerSecond==15,"quick faster control adjusts speed without opening settings");cfg.AutoScrollPixelsPerSecond=1;adjust.Invoke(app,new object[]{-1});check(cfg.AutoScrollPixelsPerSecond==1,"quick speed respects lower bound");cfg.AutoScrollPixelsPerSecond=120;adjust.Invoke(app,new object[]{1});check(cfg.AutoScrollPixelsPerSecond==120,"quick speed respects upper bound");cfg.AutoScrollDirection=1;reverse.Invoke(app,null);check(cfg.AutoScrollDirection==-1,"quick reverse flips reading direction");}
 }
 new Settings{AutoScrollEnabled=false,DesktopMode=true,TaskbarMode=false}.Save();
 using(var app=new GhostApp()){
 var host=Field<HostWindow>(app,"_host");host.FailedHotkeys.Clear();host.FailedHotkeys.Add("Ctrl+Alt+M");
 bool modal=false;using var finished=new System.Threading.ManualResetEventSlim(false);
 var watchdog=new System.Threading.Thread(()=>{while(!finished.Wait(10)){if(NativeProbe.CloseOwnMessageBox()){modal=true;break;}}});watchdog.IsBackground=true;watchdog.Start();
 try{Invoke(app,"ReportHotkeys");}finally{finished.Set();watchdog.Join(1000);}
 check(!modal,"hotkey conflicts never open a blocking native dialog");
 check(Field<NotifyIcon>(app,"_tray").Visible,"hotkey conflict notification retains tray recovery");
 Field<Settings>(app,"_cfg").HideTrayIcon=true;Field<NotifyIcon>(app,"_tray").Visible=false;host.FailedHotkeys.Clear();host.FailedHotkeys.Add("");Invoke(app,"ReportHotkeys");
 check(Field<NotifyIcon>(app,"_tray").Visible,"even an empty invalid shortcut retains a visible recovery tray");
 }
 var warningCtor=typeof(SettingsForm).GetConstructor(new[]{typeof(Settings),typeof(IReadOnlyList<string>)});
 check(warningCtor!=null,"settings can display current hotkey conflict details");
 if(warningCtor!=null){using var warningForm=(SettingsForm)warningCtor.Invoke(new object[]{new Settings(),new[]{"Ctrl+Alt+M"}});var label=warningForm.Controls.Find("HotkeyWarning",true).FirstOrDefault();check(label!=null&&label.Text.Contains("Ctrl+Alt+M"),"settings warning identifies occupied shortcut without a popup");}
 using(var form=new SettingsForm(new Settings())){
 int updates=0;double latest=0;form.PreviewChanged+=draft=>{updates++;latest=draft.FontSize;};var draft=Field<Settings>(form,"_draft");
 draft.FontSize=15;Invoke(form,"Changed");draft.FontSize=20;Invoke(form,"Changed");check(updates==0,"rapid appearance edits are coalesced rather than rebuilding immediately");
 var watch=System.Diagnostics.Stopwatch.StartNew();while(watch.ElapsedMilliseconds<180){Application.DoEvents();System.Threading.Thread.Sleep(5);}
 check(updates==1&&latest==20,"coalesced appearance preview applies the latest value");
 }
 int cancelledUpdates=0;var cancelledForm=new SettingsForm(new Settings());cancelledForm.PreviewChanged+=s=>cancelledUpdates++;Invoke(cancelledForm,"Changed");cancelledForm.Dispose();
 var cancelWatch=System.Diagnostics.Stopwatch.StartNew();while(cancelWatch.ElapsedMilliseconds<140){Application.DoEvents();System.Threading.Thread.Sleep(5);}
 check(cancelledUpdates==0,"closing settings cancels pending appearance preview");
 var hiddenConstructor=typeof(GhostApp).GetConstructor(new[]{typeof(bool)});
 check(hiddenConstructor!=null,"one-time hidden startup can preserve current runtime visibility");
 if(hiddenConstructor!=null){new Settings{StartHidden=false,DesktopMode=true,TaskbarMode=false}.Save();using var hiddenApp=(GhostApp)hiddenConstructor.Invoke(new object[]{true});check(!Field<ReaderWindow>(hiddenApp,"_reader").Visible&&!Field<TaskbarPanel>(hiddenApp,"_panel").VisiblePanel&&!Field<Settings>(hiddenApp,"_cfg").StartHidden,"hidden upgrade does not change saved startup preference or show a reader");}
 using(var host=new HostWindow()){host.Create(new Settings());int received=0;host.HotkeyPressed+=id=>received=id;Win32.SendMessageW(host.Handle,Win32.RegisterWindowMessageW("GhostReaderCommandV2"),new IntPtr(8),IntPtr.Zero);check(received==8,"explicit hide launch command is routed without toggling visibility");}
 using(var state=new AppState()){
 state.OpenBook(book);state.SetAnchor(new ReadingPosition{Paragraph=5});using var form=new ChapterForm(state);
 var list=Field<ListBox>(form,"_list");check(list.SelectedIndex==2,"chapter directory initially selects current original chapter");
 var search=form.Controls.Find("ChapterSearch",true).FirstOrDefault() as TextBox;check(search!=null,"chapter directory provides a search field");
 var locate=form.Controls.Find("LocateCurrentChapter",true).FirstOrDefault();form.PerformLayout();
 check(locate!=null&&locate.Right<=locate.Parent.ClientSize.Width&&locate.Left>=search.Right,"chapter current-location button remains visible beside search field");
 if(search!=null){search.Text="不存在";var before=state.GetAnchor();Invoke(form,"JumpTo");check(list.Items.Count==0&&state.GetAnchor().Paragraph==before.Paragraph,"empty chapter filter cannot jump or change progress");search.Text="特别";check(list.Items.Count==1,"chapter search filters names");Invoke(form,"JumpTo");check(state.GetAnchor().Paragraph==4,"filtered chapter jump preserves original chapter identity");}
 }
 using(var state=new AppState()){
 state.OpenBook(book);var cfg=new Settings{BackgroundOpacity=.3};using var reader=new ReaderWindow(state,cfg);reader.Create(200,200,400,200);reader.Redraw();var layout=Field<ReadingLayout>(reader,"_layout");var line=layout.Lines[0];
 cfg.BackgroundOpacity=.6;reader.ApplyStyles();reader.RebuildLayout();reader.Redraw();check(ReferenceEquals(line,layout.Lines[0]),"opacity-only changes reuse measured line wrapping");line=layout.Lines[0];reader.Resize(400,280);check(ReferenceEquals(line,layout.Lines[0]),"height-only resizing reuses measured line wrapping");
 int before=layout.Lines[0].Text.Length;cfg.FontSize+=5;reader.ApplyStyles();reader.RebuildLayout();check(layout.Lines[0].Text.Length<before,"font changes still invalidate wrapping correctly");
 var anchor=state.GetAnchor();line=layout.Lines[0];cfg.LineSpacing=2.4;reader.ApplyStyles();reader.RebuildLayout();check(ReferenceEquals(line,layout.Lines[0])&&state.GetAnchor().Offset==anchor.Offset,"line spacing reuses wrapping and preserves original reading position");
 int previousWidth=layout.Lines[0].Text.Length;reader.Resize(260,280);check(layout.Lines[0].Text.Length<previousWidth,"width changes still invalidate wrapping correctly");
 }
 }
}
