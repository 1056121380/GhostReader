using GhostReader;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

internal static class NativeProbe {
 [DllImport("user32.dll")] internal static extern IntPtr GetTopWindow(IntPtr parent);
 [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr hwnd,uint command);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd,StringBuilder name,int count);
 [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
 internal static bool CloseOwnMessageBox(){
 for(var w=GetTopWindow(IntPtr.Zero);w!=IntPtr.Zero;w=GetWindow(w,2)){
 GetWindowThreadProcessId(w,out uint pid);if(pid!=(uint)Environment.ProcessId)continue;
 var name=new StringBuilder(64);GetClassName(w,name,64);if(name.ToString()=="#32770"){Win32.PostMessageW(w,Win32.WM_CLOSE,IntPtr.Zero,IntPtr.Zero);return true;}
 }
 return false;
 }
 internal static bool Above(IntPtr upper,IntPtr lower){
 for(var w=GetTopWindow(IntPtr.Zero);w!=IntPtr.Zero;w=GetWindow(w,2)){if(w==upper)return true;if(w==lower)return false;}return false;
 }
 internal static bool MaintainsMiniZOrder(){
 bool result=false;Exception failure=null;
 var thread=new System.Threading.Thread(()=>{
 try{
 using var state=new AppState();using var panel=new TaskbarPanel(state,new Settings{TaskbarBackgroundOpacity=1,TaskbarFreePosition=true,TaskbarX=145,TaskbarY=173});
 using var cover=new ReaderWindow(state,new Settings{BackgroundOpacity=1});using var context=new ApplicationContext();using var timer=new System.Windows.Forms.Timer{Interval=40};int phase=0;
 timer.Tick+=(s,e)=>{try{switch(phase++){
 case 0:panel.Attach();break;
 case 1:cover.Create(145,173,440,100);cover.Show();break;
 case 2:panel.Position();break;
 default:result=Above(panel.Window.Handle,cover.Handle);timer.Stop();context.ExitThread();break;
 }}catch(Exception ex){failure=ex;timer.Stop();context.ExitThread();}};
 timer.Start();Application.Run(context);
 }catch(Exception ex){failure=ex;}
 });thread.SetApartmentState(System.Threading.ApartmentState.STA);thread.IsBackground=true;thread.Start();
 if(!thread.Join(4000))return false;
 if(failure!=null)Console.WriteLine("Native z-order test: "+failure.Message);
 return result;
 }
 internal static int QuitCurrent(){
 var host=Win32.FindWindowW("GhostReaderHost",ProfileIdentity.HostTitle);
 if(host==IntPtr.Zero){Console.WriteLine("Current profile host not running");return 0;}
 Console.WriteLine("Requesting current profile reader shutdown");
 return Win32.PostMessageW(host,Win32.WM_HOTKEY,new IntPtr(4),IntPtr.Zero)?0:1;
 }
 internal static int QuitLegacy(){
 var host=Win32.FindWindowW("GhostReaderHost","GhostReaderHost");
 if(host==IntPtr.Zero){Console.WriteLine("Legacy host not running");return 0;}
 Console.WriteLine("Requesting legacy GhostReader normal shutdown");
 return Win32.PostMessageW(host,Win32.WM_HOTKEY,new IntPtr(4),IntPtr.Zero)?0:1;
 }
 internal static int Diagnose(){
 Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
 var taskbar=Win32.FindWindowW("Shell_TrayWnd",null);
 foreach(var pair in new[]{("desktop","GhostReaderReaderWindow"),("mini","GhostReaderOverlay"),("taskbar","Shell_TrayWnd")}){
 var h=Win32.FindWindowW(pair.Item2,null);Win32.GetWindowRect(h,out var r);
 Console.WriteLine($"{pair.Item1}: handle={h} visible={Win32.IsWindowVisible(h)} rect={r.Left},{r.Top},{r.Right},{r.Bottom} ex={Win32.GetWindowLongPtrSafe(h,Win32.GWL_EXSTYLE).ToInt64():X} aboveTaskbar={Above(h,taskbar)}");
 }
 int count=0;for(var w=GetTopWindow(IntPtr.Zero);w!=IntPtr.Zero;w=GetWindow(w,2)){
 var name=new StringBuilder(256);GetClassName(w,name,256);
 if(name.ToString().Contains("GhostReader")||name.ToString()=="Shell_TrayWnd")Console.WriteLine($"Z[{count}]: {name} {w}");count++;
 }
 return 0;
 }
}
