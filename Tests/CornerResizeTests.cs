using GhostReader;
using System.Drawing;
using System.Reflection;
internal static class CornerResizeTests
{
 internal static void Run(Action<bool,string> check)
 {
 var original=new Rectangle(100,100,400,300);var minimum=new Size(160,60);var maximum=new Size(800,600);
 var cases=new[]{
 (ResizeCorner.TopLeft,new Point(102,102),new Rectangle(120,115,380,285)),
 (ResizeCorner.TopRight,new Point(498,102),new Rectangle(100,115,420,285)),
 (ResizeCorner.BottomLeft,new Point(102,398),new Rectangle(120,100,380,315)),
 (ResizeCorner.BottomRight,new Point(498,398),new Rectangle(100,100,420,315))};
 foreach(var item in cases){check(CornerResize.Hit(original,item.Item2,18)==item.Item1,"corner hit "+item.Item1);check(CornerResize.Calculate(original,item.Item1,new Point(20,15),minimum,maximum)==item.Item3,"opposite anchor preserved for "+item.Item1);}
 check(CornerResize.Hit(original,new Point(300,250),18)==ResizeCorner.None&&CornerResize.Hit(original,new Point(99,99),18)==ResizeCorner.None,"body center and outside do not accidentally resize");
 check(CornerResize.Calculate(original,ResizeCorner.TopLeft,new Point(int.MaxValue,int.MaxValue),minimum,maximum)==new Rectangle(340,340,160,60),"minimum size clamps without crossing the fixed opposite corner");
 check(CornerResize.Calculate(original,ResizeCorner.BottomRight,new Point(int.MaxValue,int.MaxValue),minimum,maximum)==new Rectangle(100,100,800,600),"oversized drag clamps safely without integer overflow");
 check(CornerResize.Calculate(original,ResizeCorner.BottomRight,new Point(40,0),minimum,maximum).Size==new Size(440,300),"resize is independent width and height rather than forced aspect ratio");
 foreach(bool compact in new[]{false,true})foreach(var corner in new[]{ResizeCorner.TopLeft,ResizeCorner.TopRight,ResizeCorner.BottomLeft,ResizeCorner.BottomRight}){
 using var state=new AppState();var cfg=new Settings();using var reader=new ReaderWindow(state,cfg,compact);reader.Create(200,200,400,200);
 int x=corner==ResizeCorner.TopLeft||corner==ResizeCorner.BottomLeft?202:598;
 int y=corner==ResizeCorner.TopLeft||corner==ResizeCorner.TopRight?202:398;
 var point=new Point(x,y);reader.BeginPointer(point);reader.UpdatePointer(new Point(x+20,y+15));reader.EndPointer(new Point(x+20,y+15));
 Win32.GetWindowRect(reader.Handle,out var r);var actual=Rectangle.FromLTRB(r.Left,r.Top,r.Right,r.Bottom);
 var expected=CornerResize.Calculate(new Rectangle(200,200,400,200),corner,new Point(20,15),new Size(compact?100:160,compact?20:60),new Size(10000,10000));
 check(actual==expected&&reader.LastInteractionWasResize&&!reader.IsInteracting,(compact?"mini":"desktop")+" native corner interaction "+corner);
 }
 using(var state=new AppState()){
 var book=new Book{FilePath=Path.Combine(Settings.ConfigDir,"first-resize.txt")};for(int i=0;i<30;i++)book.Lines.Add("Line "+i+" 正文");state.OpenBook(book);state.SetAnchor(new ReadingPosition());
 var cfg=new Settings{TaskbarWidth=300,TaskbarFreePosition=true,TaskbarX=200,TaskbarY=300};using var panel=new TaskbarPanel(state,cfg);panel.Attach();
 var p=new Point(panel.Window.GetX()+panel.Window.WindowWidth-2,panel.Window.GetY()+panel.Window.WindowHeight-2);
 panel.Window.BeginPointer(p);panel.Window.UpdatePointer(new Point(p.X,p.Y+120));panel.Window.EndPointer(new Point(p.X,p.Y+120));
 check(cfg.TaskbarCustomSize&&panel.Window.PreviewText.Contains('\n'),"first mini resize redraws multi-line body immediately on release");
 }
 using(var state=new AppState()){
 var screen=System.Windows.Forms.Screen.PrimaryScreen.Bounds;var cfg=new Settings{TaskbarCustomSize=true,TaskbarWidth=420,TaskbarHeight=180,TaskbarFreePosition=true,TaskbarX=screen.Left+80,TaskbarY=screen.Top+200};
 using var panel=new TaskbarPanel(state,cfg);panel.Attach();int fixedRight=panel.Window.GetX()+panel.Window.WindowWidth;
 var p=new Point(panel.Window.GetX()+1,panel.Window.GetY()+1);panel.Window.BeginPointer(p);panel.Window.UpdatePointer(new Point(p.X-200,p.Y));panel.Window.EndPointer(new Point(p.X-200,p.Y));
 check(panel.Window.GetX()>=screen.Left&&panel.Window.GetX()+panel.Window.WindowWidth==fixedRight,"screen-edge mini resizing keeps the opposite corner fixed after release");
 }
 var excessive=new Settings{WinW=10000,WinH=10000,TaskbarWidth=10000,TaskbarHeight=10000};excessive.Normalize();
 check(excessive.WinW<=System.Windows.Forms.SystemInformation.VirtualScreen.Width&&excessive.WinH<=System.Windows.Forms.SystemInformation.VirtualScreen.Height,"persisted oversized dimensions cannot allocate beyond display bounds");
 using(var state=new AppState()){
 var book=new Book{FilePath=Path.Combine(Settings.ConfigDir,"resize-fixture.txt")};for(int i=0;i<40;i++)book.Lines.Add("Line "+i+" 四角缩放测试正文");state.OpenBook(book);state.SetAnchor(new ReadingPosition());
 var cfg=new Settings{TaskbarCustomSize=true,TaskbarWidth=400,TaskbarHeight=180,TaskbarFreePosition=true,TaskbarX=300,TaskbarY=300};using var panel=new TaskbarPanel(state,cfg);panel.Attach();
 var point=new Point(panel.Window.GetX()+398,panel.Window.GetY()+178);
 panel.Window.BeginPointer(point);panel.Window.UpdatePointer(new Point(point.X+30,point.Y+40));panel.Window.EndPointer(new Point(point.X+30,point.Y+40));
 panel.Position();panel.Reattach();var saved=Settings.Load();
 check(panel.Window.WindowWidth==430&&panel.Window.WindowHeight==220&&saved.TaskbarWidth==430&&saved.TaskbarHeight==220&&saved.TaskbarCustomSize,"mini custom width and height persist through maintenance, reattach and disk save");
 panel.Redraw();check(panel.Window.PreviewText.Contains('\n'),"enlarged custom mini height displays multiple readable lines");
 int manual=0;state.ManualNavigationRequested+=()=>manual++;var anchor=state.GetAnchor();
 var corner=new Point(panel.Window.GetX()+1,panel.Window.GetY()+1);panel.Window.BeginPointer(corner);panel.Window.EndPointer(corner);
 check(manual==0&&state.GetAnchor().Offset==anchor.Offset,"clicking a resize corner without movement never flips a page");
 }
 }
}
