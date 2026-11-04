using GhostReader;
using System.Drawing;
internal static class EdgeResizeTests
{
 internal static void Run(Action<bool,string> check)
 {
 var rectangle=new Rectangle(100,100,400,300);
 var cases=new[]{
 ("Left",new Point(101,250),new Rectangle(120,100,380,300)),
 ("Right",new Point(499,250),new Rectangle(100,100,420,300)),
 ("Top",new Point(300,101),new Rectangle(100,115,400,285)),
 ("Bottom",new Point(300,399),new Rectangle(100,100,400,315))};
 foreach(var item in cases){
 check(CornerResize.Hit(rectangle,item.Item2,18).ToString()==item.Item1,"edge hit "+item.Item1);
 if(!Enum.TryParse<ResizeCorner>(item.Item1,out var edge))continue;
 var result=CornerResize.Calculate(rectangle,edge,new Point(20,15),new Size(160,60),new Size(800,600));
 check(result==item.Item3,"single-axis geometry and opposite edge fixed for "+item.Item1);
 foreach(bool compact in new[]{false,true}){
 using var state=new AppState();using var reader=new ReaderWindow(state,new Settings(),compact);reader.Create(200,200,400,300);
 var start=new Point(item.Item2.X+100,item.Item2.Y+100);reader.BeginPointer(start);reader.UpdatePointer(new Point(start.X+20,start.Y+15));reader.EndPointer(new Point(start.X+20,start.Y+15));
 Win32.GetWindowRect(reader.Handle,out var r);var expected=new Rectangle(item.Item3.X+100,item.Item3.Y+100,item.Item3.Width,item.Item3.Height);
 check(Rectangle.FromLTRB(r.Left,r.Top,r.Right,r.Bottom)==expected&&reader.LastInteractionWasResize,(compact?"mini":"desktop")+" native edge interaction "+item.Item1);
 }
 }
 check(CornerResize.Hit(new Rectangle(100,100,100,20),new Point(100,110),18).ToString()=="Left","thin mini retains a horizontal edge handle between corners");
 }
}
