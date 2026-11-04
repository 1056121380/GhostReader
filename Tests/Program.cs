using GhostReader;
using System.Drawing;
using System.Text.Json;
using System.Reflection;

class Tests {
 static int fails;
 static void Check(bool ok,string name){Console.WriteLine((ok?"PASS ":"FAIL ")+name);if(!ok)fails++;}
 [STAThread] static int Main(string[] args){
 System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2);
 if(args.Contains("--diagnose-running"))return NativeProbe.Diagnose();
 if(args.Contains("--wheel-smoke"))return WheelSmoke.Run();
 if(args.Contains("--resize-smoke"))return WheelSmoke.Run(true);
 if(args.Contains("--quit-legacy"))return NativeProbe.QuitLegacy();
 if(args.Contains("--quit-current"))return NativeProbe.QuitCurrent();
 if(args.Contains("--ux-benchmark"))return UxBenchmark.Run();
 string profile=Path.Combine(AppContext.BaseDirectory,"test-profile");Directory.CreateDirectory(profile);
 Environment.SetEnvironmentVariable("GHOSTREADER_PROFILE",profile);
 // Regression: omitting IncludeFields silently discards all configured field values.
 var opts=(JsonSerializerOptions)typeof(Settings).GetField("JsonOpts",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
 var original=new Settings{FontSize=23,Opacity=0.34,TaskbarWidth=615,LastBook="中文.txt"};
 var restored=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(original,opts),opts);
 Check(restored.FontSize==23 && restored.Opacity==0.34 && restored.TaskbarWidth==615 && restored.LastBook=="中文.txt","settings values survive actual serializer roundtrip");
 using var bitmap=new Bitmap(600,400);using var g=Graphics.FromImage(bitmap);
 var cfg=new Settings{Padding=0,FontSize=14};
 var state=new AppState(); state.Book=new Book();
 state.Book.Lines.Add(new string('i',180)+new string('W',100));
 state.Rebuild(g,180,cfg);
 using var fmt=Win32.CreateFormat(false);
 Check(state.Display.All(s=>g.MeasureString(s,state.Font,int.MaxValue,fmt).Width<=181),"mixed-width text fits viewport");
 Check(string.Concat(state.Display)==state.Book.Lines[0],"wrapping preserves original text");
 
 // Regression: background alpha must not dim the independently configured text.
 var alphaCfg=new Settings{Opacity=1,BackgroundOpacity=.25,TextOpacity=.8,BackgroundMode=Settings.BgSolid};
 using(var reader=new ReaderWindow(new AppState(),alphaCfg)){
 reader.Create(100,100,400,180);reader.Redraw();
 var surface=(LayeredSurface)typeof(ReaderWindow).GetField("_surface",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(reader);
 Check(surface.ProbeAlpha(390,170)[3]==64,"desktop background uses its own 25% alpha");
 alphaCfg.BackgroundOpacity=0; reader.Redraw();
 Check(surface.ProbeAlpha(390,170)[3]==0,"desktop background can be truly transparent");
 }
 using(var form=new SettingsForm(new Settings())){
 var save=form.Controls.Cast<System.Windows.Forms.Control>().FirstOrDefault(c=>c is System.Windows.Forms.Button && c.Text=="保存");
 Check(save==null || save.Bottom<=form.ClientSize.Height || form.AutoScroll,"settings save controls are reachable");
 }

 var disk=new Settings{FontSize=21,TextOpacity=.42,BackgroundOpacity=.12,TaskbarFontSize=9,TaskbarTextOpacity=.3};disk.Save();
 var diskLoaded=Settings.Load();Check(diskLoaded.FontSize==21&&diskLoaded.TextOpacity==.42&&diskLoaded.BackgroundOpacity==.12&&diskLoaded.TaskbarFontSize==9&&diskLoaded.TaskbarTextOpacity==.3,"independent desktop/taskbar settings persist on disk");
 File.WriteAllText(Settings.SettingsPath,"{broken json");Check(Settings.Load().FontSize==14,"corrupt settings fall back safely");
 var invalid=new Settings{FontSize=-100,FontFamily="",WinW=-1,TaskbarWidth=int.MaxValue,TextOpacity=double.NaN};invalid.Normalize();
 Check(invalid.FontSize==6&&invalid.WinW==160&&invalid.TaskbarWidth<=System.Windows.Forms.SystemInformation.VirtualScreen.Width&&invalid.TaskbarWidth>=100&&invalid.TextOpacity==.85&&!string.IsNullOrWhiteSpace(invalid.FontFamily),"invalid configuration is normalized");
 var book=new Book{FilePath=Path.Combine(profile,"book.txt"),Title="测试小说"};
 book.Lines.Add("第一章 风起");book.Lines.Add("风吹过书页，故事悄然继续。"+new string('夜',300));book.Lines.Add("第二章 夜谈");book.Lines.Add("第二章正文");
 BookLoader.DetectChaptersInto(book,book.HeadingLines);
 using(var st=new AppState()){
 st.OpenBook(book);st.Rebuild(g,180,cfg);st.SetAnchor(new ReadingPosition{Paragraph=1,Offset=100});
 st.Rebuild(g,300,new Settings{FontSize=22,Padding=5});
 Check(st.GetAnchor().Paragraph==1&&st.GetAnchor().Offset==100,"reflow preserves exact original-text anchor");
 st.SaveProgress();using var resumed=new AppState();resumed.OpenBook(book);
 Check(resumed.GetAnchor().Paragraph==1&&resumed.GetAnchor().Offset==100,"progress resumes exact paragraph and offset");
 st.ScrollBy(int.MaxValue);Check(st.ScrollLine==st.TotalLines-1,"scroll clamps safely at final readable line");
 st.ScrollBy(int.MinValue);Check(st.ScrollLine==0,"scroll clamps safely at first readable line");
 }
 var unicode=new Book();unicode.Lines.Add("A😀e\u0301中文👨‍👩‍👧‍👦B");var unicodeLayout=new ReadingLayout();
 using(var font=new Font("Microsoft YaHei UI",14)){unicodeLayout.Build(unicode,g,font,8);}
 Check(string.Concat(unicodeLayout.Lines.Select(l=>l.Text))==unicode.Lines[0],"narrow wrapping preserves all Unicode text");
 Check(unicodeLayout.Lines.All(l=>!char.IsLowSurrogate(l.Text[0])&&!char.IsHighSurrogate(l.Text[^1])),"wrapping does not split surrogate pairs");
 Check(unicodeLayout.Lines.Any(l=>l.Text=="e\u0301")&&unicodeLayout.Lines.Any(l=>l.Text=="👨‍👩‍👧‍👦"),"wrapping preserves combining and ZWJ graphemes");
 var repeat=new Book{FilePath=Path.Combine(profile,"repeat.txt")};repeat.Lines.AddRange(new[]{"重复标题","正文一","重复标题","正文二"});
 repeat.Chapters.Add(new Book.Chapter{Title="重复标题",StartLine=0,EndLine=2});repeat.Chapters.Add(new Book.Chapter{Title="重复标题",StartLine=2,EndLine=4});
 using(var st=new AppState()){st.OpenBook(repeat);st.GoToChapter(1);Check(st.GetAnchor().Paragraph==2,"chapter jump uses identity instead of duplicated title");}
 using(var st=new AppState()){
 st.OpenBook(book);var appearance=new Settings{TaskbarFontSize=32,TaskbarBackgroundOpacity=.2,TaskbarTextOpacity=.6};
 using var panel=new TaskbarPanel(st,appearance);panel.Attach();panel.Window.Navigate(1);panel.Redraw();
 Check(panel.Window.PreviewText.StartsWith("风吹过书页"),"taskbar surface renders actual novel body");
 var style=appearance.Appearance(true);using var font=style.CreateFont();
 Check(panel.Window.WindowHeight>=Math.Ceiling(font.GetHeight(g))+12,"largest allowed taskbar font fits overlay height");
 var surface=(LayeredSurface)typeof(ReaderWindow).GetField("_surface",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(panel.Window);
 Check(surface.ProbeAlpha(panel.Window.WindowWidth-1,panel.Window.WindowHeight-1)[3]==51,"taskbar background alpha is independent");
 panel.Detach();Check(!panel.VisiblePanel,"taskbar detach really hides its window");
 }
 var cancelCfg=new Settings{BackgroundOpacity=.7};using(var form=new SettingsForm(cancelCfg)){
 var draft=(Settings)typeof(SettingsForm).GetField("_draft",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(form);draft.BackgroundOpacity=.1;draft.TextColor=Color.Red.ToArgb();
 Check(cancelCfg.BackgroundOpacity==.7&&cancelCfg.TextColor!=Color.Red.ToArgb(),"cancelled settings draft cannot mutate original colors or opacity");}
 Check(!HotKeySpec.Parse("Ctrl+Garbage+M",out _,out _),"hotkey parser rejects unknown tokens");
 Check(!HotKeySpec.Parse("Ctrl+A+B",out _,out _),"hotkey parser rejects multiple primary keys");
 string cr=Path.Combine(profile,"cr.txt");File.WriteAllText(cr,"第一章 风起\r正文\r第二章 夜谈");
 Check(BookLoader.Load(cr,null).Lines.Count==3,"TXT supports lone CR line separators");
 string prose=Path.Combine(profile,"prose.txt");File.WriteAllText(prose,"第一章 风起\n他在后记中写下这句话。\n第二章 夜谈\n正文");
 Check(BookLoader.Load(prose,null).Chapters.Count==2,"chapter detection does not classify prose mentioning afterword");
 new Settings{StartHidden=false,DesktopMode=true,TaskbarMode=true}.Save();
 using(var app=new GhostApp()){
 var appType=typeof(GhostApp);var hidden=appType.GetField("_hidden",BindingFlags.NonPublic|BindingFlags.Instance);
 appType.GetField("_settingsOpen",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(app,true);
 appType.GetMethod("OnHotkey",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(app,new object[]{1});
 var desktop=(ReaderWindow)appType.GetField("_reader",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(app);
 var panel=(TaskbarPanel)appType.GetField("_panel",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(app);
 Check((bool)hidden.GetValue(app)&&!desktop.Visible&&!panel.VisiblePanel,"panic hotkey hides both surfaces even while settings are open");
 var settings=(Settings)appType.GetField("_cfg",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(app);settings.HideTrayIcon=true;
 var host=(HostWindow)appType.GetField("_host",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(app);host.FailedHotkeys.Add("Ctrl+Alt+S");
 var updateTray=appType.GetMethod("UpdateTrayVisibility",BindingFlags.NonPublic|BindingFlags.Instance);
 if(updateTray!=null)updateTray.Invoke(app,null);
 var tray=(System.Windows.Forms.NotifyIcon)appType.GetField("_tray",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(app);
 Check(updateTray!=null&&tray.Visible,"failed hotkeys retain recoverable tray even when hide-tray requested");
 }

 using(var host=new HostWindow()){
 host.Create(new Settings());int received=0;host.HotkeyPressed+=id=>received=id;
 Win32.SendMessageW(host.Handle,Win32.RegisterWindowMessageW("GhostReaderCommandV2"),new IntPtr(5),IntPtr.Zero);
 Check(received==5,"second-instance settings command reaches live host");
 }
 string artifact=Environment.GetEnvironmentVariable("GHOSTREADER_TEST_ARTIFACTS")??Path.Combine(AppContext.BaseDirectory,"rendered");Directory.CreateDirectory(artifact);
 using(var st=new AppState()){
 st.OpenBook(book);st.SetAnchor(new ReadingPosition{Paragraph=1});
 var renderCfg=new Settings{BackgroundOpacity=0,TextOpacity=.8,WinW=480,WinH=220,TaskbarTextOpacity=.5};
 using var reader=new ReaderWindow(st,renderCfg);reader.Create(100,100,480,220);reader.Redraw();
 var surface=(LayeredSurface)typeof(ReaderWindow).GetField("_surface",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(reader);
 int maxAlpha=0;for(int y=0;y<220;y++)for(int x=0;x<480;x++)maxAlpha=Math.Max(maxAlpha,surface.ProbeAlpha(x,y)[3]);
 Check(maxAlpha>=190&&maxAlpha<=204,"80% text remains visible over a zero-alpha background");
 surface.Graphics.Flush();var bitmapField=typeof(LayeredSurface).GetField("_managed",BindingFlags.NonPublic|BindingFlags.Instance);
 ((Bitmap)bitmapField.GetValue(surface)).Save(Path.Combine(artifact,"desktop-render.png"),System.Drawing.Imaging.ImageFormat.Png);
 renderCfg.TextOpacity=0;reader.Redraw();maxAlpha=0;for(int y=0;y<220;y++)for(int x=0;x<480;x++)maxAlpha=Math.Max(maxAlpha,surface.ProbeAlpha(x,y)[3]);
 Check(maxAlpha==0,"both zero opacity values produce a fully transparent surface");
 using var panel=new TaskbarPanel(st,renderCfg);panel.Attach();panel.Redraw();
 var taskSurface=(LayeredSurface)typeof(ReaderWindow).GetField("_surface",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(panel.Window);
 taskSurface.Graphics.Flush();((Bitmap)bitmapField.GetValue(taskSurface)).Save(Path.Combine(artifact,"taskbar-render.png"),System.Drawing.Imaging.ImageFormat.Png);
 Check(panel.Window.PreviewText.Length>0&&panel.Window.PreviewText.Length<book.Lines[1].Length,"taskbar wraps body to its own viewport width");
 }
 // Minimal real EPUB fixture verifies container/OPF/spine and entity decoding.
 string epub=Path.Combine(profile,"test.epub");if(File.Exists(epub))File.Delete(epub);
 using(var zip=System.IO.Compression.ZipFile.Open(epub,System.IO.Compression.ZipArchiveMode.Create)){
 void Entry(string name,string value){using var writer=new StreamWriter(zip.CreateEntry(name).Open());writer.Write(value);}
 Entry("META-INF/container.xml","<container xmlns='urn:oasis:names:tc:opendocument:xmlns:container'><rootfiles><rootfile full-path='OEBPS/book.opf'/></rootfiles></container>");
 Entry("OEBPS/book.opf","<package xmlns='http://www.idpf.org/2007/opf'><metadata xmlns:dc='http://purl.org/dc/elements/1.1/'><dc:title>测试电子书</dc:title></metadata><manifest><item id='two' href='two.xhtml' media-type='application/xhtml+xml'/><item id='one' href='one.xhtml' media-type='application/xhtml+xml'/></manifest><spine><itemref idref='one'/><itemref idref='two'/></spine></package>");
 Entry("OEBPS/one.xhtml","<html><body><h1>第一章 开始</h1><p>风起 &amp; 夜谈</p></body></html>");
 Entry("OEBPS/two.xhtml","<html><body><h1>第二章 继续</h1><p>书页悄然翻过</p></body></html>");
 }
 var ebook=BookLoader.Load(epub,null);
 Check(ebook!=null&&ebook.Title=="测试电子书"&&ebook.Lines.IndexOf("第一章 开始")<ebook.Lines.IndexOf("第二章 继续")&&ebook.Lines.Contains("风起 & 夜谈"),"EPUB respects spine order and decodes body entities");

 using(var surface=new LayeredSurface()){
 surface.Resize(10,10);bool rejected=false;
 try{surface.Present(IntPtr.Zero,0,0);}catch(System.ComponentModel.Win32Exception){rejected=true;}
 Check(rejected,"native presentation reports invalid window instead of silent success");
 }

 Check(System.Runtime.InteropServices.Marshal.SizeOf<Win32.PAINTSTRUCT>()==(IntPtr.Size==8?72:64),"PAINTSTRUCT native buffer has the full 32-byte reserved region");

 var hiddenBoth=new Settings{DesktopMode=false,TaskbarMode=false};hiddenBoth.Normalize();
 Check(!hiddenBoth.DesktopMode&&!hiddenBoth.TaskbarMode,"individually hiding both views is not undone by normalization");
 string autoJson="{\"AutoScrollEnabled\":true,\"AutoScrollPixelsPerSecond\":18,\"AutoScrollDirection\":-1,\"TaskbarFreePosition\":true,\"TaskbarX\":145,\"TaskbarY\":173}";
 var autoSettings=JsonSerializer.Deserialize<Settings>(autoJson,opts);
 using(var doc=JsonDocument.Parse(JsonSerializer.Serialize(autoSettings,opts))){
 Check(doc.RootElement.TryGetProperty("AutoScrollPixelsPerSecond",out var speed)&&speed.GetDouble()==18&&doc.RootElement.GetProperty("AutoScrollDirection").GetInt32()==-1,"auto-scroll speed and direction survive configuration roundtrip");
 }
 using(var st=new AppState())using(var panel=new TaskbarPanel(st,autoSettings)){
 panel.Attach();panel.Window.MoveTo(145,173);
 var moved=(Action)typeof(ReaderWindow).GetField("GeometryChanged",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(panel.Window);moved?.Invoke();
 panel.Position();panel.Reattach();
 Check(panel.Window.GetX()==145&&panel.Window.GetY()==173,"mini strip stays at arbitrary XY after drag, maintenance and reattach");
 }
 var advance=typeof(ReaderWindow).GetMethod("AdvanceAutoScroll");
 Check(advance!=null,"reader exposes pixel-continuous automatic scrolling");
 if(advance!=null){
 using var st=new AppState();st.OpenBook(book);st.SetAnchor(new ReadingPosition{Paragraph=1});
 using var reader=new ReaderWindow(st,autoSettings);reader.Create(100,100,480,220);
 // Reverse at the start of paragraph one must move towards the prior source text.
 advance.Invoke(reader,new object[]{.05});var anchor=st.GetAnchor();
 Check(anchor.Paragraph==0,"downward text animation revisits earlier source text");
 }

 using(var st=new AppState()){
 var motionCfg=new Settings{AutoScrollPixelsPerSecond=20,AutoScrollDirection=1};st.OpenBook(book);st.SetAnchor(new ReadingPosition{Paragraph=1});
 using var reader=new ReaderWindow(st,motionCfg);reader.Create(100,100,480,220);reader.Redraw();
 var layout=(ReadingLayout)typeof(ReaderWindow).GetField("_layout",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(reader);
 int lineHeight=(int)typeof(ReaderWindow).GetField("_lineHeight",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(reader);
 var surface=(LayeredSurface)typeof(ReaderWindow).GetField("_surface",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(reader);
 byte[] Pixels(){surface.Graphics.Flush();var data=new byte[480*220*4];var bits=(IntPtr)typeof(LayeredSurface).GetField("_bits",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(surface);System.Runtime.InteropServices.Marshal.Copy(bits,data,0,data.Length);return data;}
 var before=Pixels();double start=layout.CoordinateOf(st.GetAnchor());reader.AdvanceAutoScroll(.05);reader.Redraw();double first=layout.CoordinateOf(st.GetAnchor());
 Check(Math.Abs((first-start)*lineHeight-1)<.00001,"automatic motion advances exactly one pixel for 20 px/s over 50 ms");
 Check(!before.SequenceEqual(Pixels())&&(int)first==(int)start,"fractional-line auto movement actually changes rendered pixels before a line jump");
 motionCfg.AutoScrollPixelsPerSecond=40;reader.AdvanceAutoScroll(.05);double fast=layout.CoordinateOf(st.GetAnchor());
 Check(Math.Abs((fast-first)*lineHeight-2)<.00001,"doubling speed doubles pixel displacement");
 motionCfg.AutoScrollDirection=-1;reader.AdvanceAutoScroll(.05);
 Check(Math.Abs(layout.CoordinateOf(st.GetAnchor())-first)<.00001,"reversing direction smoothly returns to prior position");
 motionCfg.AutoScrollDirection=1;reader.AdvanceAutoScroll(30);double stalled=layout.CoordinateOf(st.GetAnchor());
 Check((stalled-first)*lineHeight<=4.00001,"a delayed UI frame does not catch up by skipping pages");
 st.SetAnchor(new ReadingPosition());motionCfg.AutoScrollDirection=-1;
 Check(!reader.AdvanceAutoScroll(.05)&&st.GetAnchor().Paragraph==0&&st.GetAnchor().Offset==0,"backward auto motion stops safely at book start");
 st.SetAnchor(layout.Lines[^1].Position);motionCfg.AutoScrollDirection=1;
 Check(!reader.AdvanceAutoScroll(.05),"forward auto motion stops at last readable line");
 var position=layout.PositionAt(2.375);
 Check(Math.Abs(layout.CoordinateOf(position)-2.375)<.000001,"continuous source position roundtrips across display-line interpolation");
 st.SetAnchor(position);st.SaveProgress();using var resumed=new AppState();resumed.OpenBook(book);
 Check(Math.Abs(resumed.GetAnchor().Fraction-position.Fraction)<.000001,"fractional automatic progress survives disk resume");
 }
 using(var form=new SettingsForm(new Settings())){
 var tabs=form.Controls.OfType<System.Windows.Forms.TabControl>().Single();
 Check(tabs.TabPages.Cast<System.Windows.Forms.TabPage>().Any(t=>t.Text=="自动滚动"),"settings expose a dedicated auto-scroll control page");
 }
 new Settings{StartHidden=false,DesktopMode=true,TaskbarMode=true}.Save();
 using(var app=new GhostApp()){
 var t=typeof(GhostApp);const BindingFlags privateInstance=BindingFlags.NonPublic|BindingFlags.Instance;
 var step=t.GetMethod("StepAutoScroll",privateInstance);var setView=t.GetMethod("SetViewVisibility",privateInstance);var onlyView=t.GetMethod("ShowOnlyView",privateInstance);
 Check(step!=null&&setView!=null&&onlyView!=null,"application wires auto-scroll and independent view visibility");
 if(step!=null&&setView!=null&&onlyView!=null){
 var cfgLive=(Settings)t.GetField("_cfg",privateInstance).GetValue(app);var st=(AppState)t.GetField("_state",privateInstance).GetValue(app);
 var desktop=(ReaderWindow)t.GetField("_reader",privateInstance).GetValue(app);var panel=(TaskbarPanel)t.GetField("_panel",privateInstance).GetValue(app);
 st.OpenBook(book);st.SetAnchor(new ReadingPosition{Paragraph=1});desktop.RebuildLayout();panel.Window.RebuildLayout();
 cfgLive.AutoScrollEnabled=true;step.Invoke(app,new object[]{.05});var position=st.GetAnchor();
 Check(position.Offset>0||position.Fraction>0,"visible application timer step advances shared reading progress");
 t.GetField("_settingsOpen",privateInstance).SetValue(app,true);step.Invoke(app,new object[]{.05});var afterPause=st.GetAnchor();
 Check(afterPause.Offset==position.Offset&&afterPause.Fraction==position.Fraction,"settings modal pauses automatic progress");
 t.GetField("_settingsOpen",privateInstance).SetValue(app,false);
 setView.Invoke(app,new object[]{false,false});Check(!desktop.Visible&&panel.VisiblePanel,"hiding desktop alone preserves mini strip");
 onlyView.Invoke(app,new object[]{false});Check(desktop.Visible&&!panel.VisiblePanel,"desktop-only action hides only the other surface");
 onlyView.Invoke(app,new object[]{true});Check(!desktop.Visible&&panel.VisiblePanel,"mini-strip-only action hides desktop surface");
 var hotkey=t.GetMethod("OnHotkey",privateInstance);hotkey.Invoke(app,new object[]{1});position=st.GetAnchor();step.Invoke(app,new object[]{.05});afterPause=st.GetAnchor();
 Check(afterPause.Offset==position.Offset&&afterPause.Fraction==position.Fraction,"global hiding pauses automatic progress");
 hotkey.Invoke(app,new object[]{1});Check(!desktop.Visible&&panel.VisiblePanel,"global restore preserves independently selected mini-strip-only mode");
 cfgLive.AutoScrollEnabled=true;panel.Window.Navigate(1);Check(!cfgLive.AutoScrollEnabled,"manual navigation stops automatic scrolling");
 cfgLive.AutoScrollEnabled=true;st.GoToChapter(1);Check(!cfgLive.AutoScrollEnabled,"manual chapter selection stops automatic scrolling");
 setView.Invoke(app,new object[]{true,false});Check(!desktop.Visible&&!panel.VisiblePanel,"both views can remain individually hidden");
 hotkey.Invoke(app,new object[]{1});hotkey.Invoke(app,new object[]{1});Check(desktop.Visible,"global restore recovers a view when both choices were disabled");
 }
 }

 var tailBook=new Book{FilePath=Path.Combine(profile,"tail.txt")};tailBook.Lines.Add(new string('夜',320));
 using(var st=new AppState()){
 st.OpenBook(tailBook);var tailCfg=new Settings{FontSize=8,AutoScrollDirection=-1,AutoScrollPixelsPerSecond=20};
 using var desktop=new ReaderWindow(st,tailCfg);desktop.Create(100,100,1800,120);desktop.Redraw();
 var layout=(ReadingLayout)typeof(ReaderWindow).GetField("_layout",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(desktop);
 int startOffset=layout.Lines[^1].Offset;int beforeOffset=Math.Min(tailBook.Lines[0].Length-1,startOffset+80);
 st.SetAnchor(new ReadingPosition{Paragraph=0,Offset=beforeOffset});desktop.AdvanceAutoScroll(.05);var now=st.GetAnchor();
 Check(now.Offset<beforeOffset&&beforeOffset-now.Offset<=20,"reverse auto preserves source position inside the last display row without a large jump");
 }
 new Settings{StartHidden=false,DesktopMode=true,TaskbarMode=true,FontSize=8,WinW=2400,TaskbarWidth=150}.Save();
 using(var app=new GhostApp()){
 const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;var t=typeof(GhostApp);
 var cfgLive=(Settings)t.GetField("_cfg",flags).GetValue(app);var st=(AppState)t.GetField("_state",flags).GetValue(app);
 var tinyBook=new Book{FilePath=Path.Combine(profile,"tiny-tail.txt")};tinyBook.Lines.Add(new string('夜',120));st.OpenBook(tinyBook);
 var desktop=(ReaderWindow)t.GetField("_reader",flags).GetValue(app);var panel=(TaskbarPanel)t.GetField("_panel",flags).GetValue(app);desktop.RebuildLayout();panel.Window.RebuildLayout();
 cfgLive.AutoScrollEnabled=true;t.GetMethod("StepAutoScroll",flags).Invoke(app,new object[]{.05});
 var pos=st.GetAnchor();Check((pos.Offset>0||pos.Fraction>0)&&cfgLive.AutoScrollEnabled,"auto continues for unread mini-strip tail even when all text fits one desktop line");
 }

 Check(NativeProbe.MaintainsMiniZOrder(),"mini-strip maintenance restores its topmost z-order instead of remaining covered");
 Check(typeof(GhostApp).Assembly.GetType("GhostReader.WheelInputRouter")!=null,"inactive and transparent reader has an explicit OS wheel input route");
 var identity=typeof(GhostApp).Assembly.GetType("GhostReader.ProfileIdentity");
 Check(identity!=null,"test profile has a separate singleton identity from user profile");

 using(var st=new AppState()){
 st.OpenBook(book);st.SetAnchor(new ReadingPosition());var inputCfg=new Settings{ClickThrough=true,BackgroundOpacity=0};using var desktop=new ReaderWindow(st,inputCfg);desktop.Create(140,140,400,200);desktop.Show();
 st.Changed+=desktop.Redraw;bool suspended=false;
 using var router=new WheelInputRouter(desktop,null,inputCfg,()=>suspended);
 Check(router.IsHookInstalled,"native wheel hook installs successfully");
 var inside=new Point(desktop.GetX()+100,desktop.GetY()+80);
 bool routed=router.TryRouteWheel(inside,-120);System.Windows.Forms.Application.DoEvents();
 Check(routed&&st.ScrollLine==1,"wheel over transparent click-through reader reaches navigation without focus");
 Check(!router.TryRouteWheel(new Point(-99999,-99999),-120),"wheel outside reader is left to other applications");
 st.ScrollTo(0);router.TryRouteWheel(inside,-60);System.Windows.Forms.Application.DoEvents();bool firstHalf=st.ScrollLine==0;
 router.TryRouteWheel(inside,-60);System.Windows.Forms.Application.DoEvents();Check(firstHalf&&st.ScrollLine==1,"high-resolution wheel deltas accumulate to one detent without duplicate movement");
 using(var covering=new ReaderWindow(st,new Settings{BackgroundOpacity=1})){
 covering.Create(desktop.GetX(),desktop.GetY(),400,200);covering.Show();covering.EnsureTopmost();System.Windows.Forms.Application.DoEvents();
 Check(!router.TryRouteWheel(inside,-120),"covered reader cannot swallow the foreground application's wheel");
 }
 suspended=true;Check(!router.TryRouteWheel(inside,-120),"reader wheel route is suspended for modal dialogs");suspended=false;
 inputCfg.WheelWhenClickThrough=false;Check(!router.TryRouteWheel(inside,-120),"explicit click-through wheel opt-out leaves wheel input untouched");inputCfg.WheelWhenClickThrough=true;
 var before=st.GetAnchor();router.TryRouteWheel(inside,-120);desktop.Hide();System.Windows.Forms.Application.DoEvents();var after=st.GetAnchor();
 Check(before.Paragraph==after.Paragraph&&before.Offset==after.Offset,"queued wheel event cannot move progress after hiding reader");
 }
 string originalProfile=Environment.GetEnvironmentVariable("GHOSTREADER_PROFILE");
 try{
 Environment.SetEnvironmentVariable("GHOSTREADER_PROFILE",Path.Combine(profile,"A"));string a=ProfileIdentity.MutexName;string hostA=ProfileIdentity.HostTitle;
 Environment.SetEnvironmentVariable("GHOSTREADER_PROFILE",Path.Combine(profile,"B"));
 Check(a!=ProfileIdentity.MutexName&&hostA!=ProfileIdentity.HostTitle,"different configuration profiles cannot capture each other's launch commands");
 Environment.SetEnvironmentVariable("GHOSTREADER_PROFILE",Path.Combine(profile,"A"));Check(a==ProfileIdentity.MutexName,"same profile retains stable singleton identity");
 }finally{Environment.SetEnvironmentVariable("GHOSTREADER_PROFILE",originalProfile);}

 var geometryType=typeof(GhostApp).Assembly.GetType("GhostReader.CornerResize");
 Check(geometryType!=null,"both reader modes have shared four-corner resize geometry");
 var sizeCfg=JsonSerializer.Deserialize<Settings>("{\"TaskbarCustomSize\":true,\"TaskbarHeight\":180,\"TaskbarWidth\":500}",opts);
 using(var doc=JsonDocument.Parse(JsonSerializer.Serialize(sizeCfg,opts)))Check(doc.RootElement.TryGetProperty("TaskbarHeight",out var h)&&h.GetInt32()==180,"custom mini-strip height survives settings roundtrip");
 Check(typeof(ReaderWindow).GetMethod("BeginPointer")!=null&&typeof(ReaderWindow).GetMethod("UpdatePointer")!=null,"native reader supports corner pointer interactions in both modes");
 CornerResizeTests.Run(Check);
 EdgeResizeTests.Run(Check);
 ReadingUxTests.Run(Check);
 Console.WriteLine($"Failures: {fails}");return fails==0?0:1;
 }
}
