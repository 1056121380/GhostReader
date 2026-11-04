using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace GhostReader
{
    /// <summary>Routes wheel and mini-strip drag presses over visible reader rectangles. No input recording.</summary>
    internal sealed class WheelInputRouter : NativeWindow
    {
        private const uint RoutedWheel = Win32.WM_APP + 27;
        private readonly ReaderWindow _desktop, _mini;
        private readonly Settings _cfg;
        private readonly Func<bool> _suspended;
        private readonly HookProc _callback;
        private IntPtr _hook;
        private ReaderWindow _pressedReader;
        private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
        [StructLayout(LayoutKind.Sequential)]
        private struct MouseData { public Win32.POINT Point; public uint Data, Flags, Time; public UIntPtr Extra; }
        [DllImport("user32.dll",SetLastError=true)] private static extern IntPtr SetWindowsHookExW(int id,HookProc callback,IntPtr module,uint thread);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Win32.POINT point);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window,uint flags);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window,uint command);
        public bool IsHookInstalled => _hook != IntPtr.Zero;
        public WheelInputRouter(ReaderWindow desktop,ReaderWindow mini,Settings cfg,Func<bool> suspended)
        {
            _desktop=desktop;_mini=mini;_cfg=cfg;_suspended=suspended;
            CreateNative("GhostReaderWheelRouter","",0,0,0,0,0,0,new IntPtr(-3));
            _callback=OnMouse;
            _hook=SetWindowsHookExW(14,_callback,Win32.GetModuleHandleW(null),0);
            if(_hook==IntPtr.Zero)FileLog.Write(new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"Mouse-wheel hook installation failed"));
        }
        private IntPtr OnMouse(int code,IntPtr message,IntPtr data)
        {
            if(code>=0)
            {
                uint kind=(uint)message.ToInt64();
                if(kind==Win32.WM_MOUSEWHEEL||kind==Win32.WM_LBUTTONDOWN||kind==Win32.WM_LBUTTONUP)
                {
                    try
                    {
                        var mouse=Marshal.PtrToStructure<MouseData>(data);
                        var point=new Point(mouse.Point.X,mouse.Point.Y);
                        bool handled=kind==Win32.WM_MOUSEWHEEL?TryRouteWheel(point,(short)(mouse.Data>>16)):TryRouteMiniButton(point,kind);
                        if(handled)return new IntPtr(1);
                    }
                    catch { /* Routing failure must not suppress input to other applications. */ }
                }
            }
            return CallNextHookEx(_hook,code,message,data);
        }
        public bool TryRouteWheel(Point screenPoint,int delta)
        {
            if(delta==0||_suspended()||(_cfg.ClickThrough&&!_cfg.WheelWhenClickThrough))return false;
            ReaderWindow target=Contains(_mini,screenPoint)?_mini:Contains(_desktop,screenPoint)?_desktop:null;
            if(target==null)return false;
            return Win32.PostMessageW(_hwnd,RoutedWheel,new IntPtr(delta),target.Handle);
        }
        public bool TryRouteMiniButton(Point point,uint message)
        {
            if(message==Win32.WM_LBUTTONDOWN)
            {
                if(_suspended()||_cfg.ClickThrough)return false;
                var target=Contains(_mini,point)?_mini:Contains(_desktop,point)&&_desktop.CornerAt(point)!=ResizeCorner.None?_desktop:null;
                if(target==null)return false;
                if(PostButton(target,point,message)){_pressedReader=target;return true;}
                return false;
            }
            if(message==Win32.WM_LBUTTONUP&&_pressedReader!=null)
            {
                var target=_pressedReader;_pressedReader=null;
                return PostButton(target,point,message);
            }
            return false;
        }
        private bool PostButton(ReaderWindow target,Point point,uint message)
        {
            if(!target.IsCreated)return false;
            int x=point.X-target.GetX(),y=point.Y-target.GetY();
            return Win32.PostMessageW(target.Handle,message,new IntPtr(message==Win32.WM_LBUTTONDOWN?1:0),new IntPtr((y&0xffff)<<16|(x&0xffff)));
        }
        private static bool Contains(ReaderWindow reader,Point point)
        {
            if(reader==null||!reader.Visible||!Win32.GetWindowRect(reader.Handle,out var rect)||
                point.X<rect.Left||point.X>=rect.Right||point.Y<rect.Top||point.Y>=rect.Bottom)return false;
            // Transparent pixels hit an underlying window. It is eligible only if that hit
            // window is BELOW the reader, never a foreground application covering it.
            var hit=WindowFromPoint(new Win32.POINT{X=point.X,Y=point.Y});
            var root=GetAncestor(hit,2);
            if(root==reader.Handle)return true;
            int count=0;
            for(var above=GetWindow(reader.Handle,3);above!=IntPtr.Zero&&count++<512;above=GetWindow(above,3))
                if(above==root)return false;
            return true;
        }
        protected override IntPtr HandleMessage(uint message,IntPtr wParam,IntPtr lParam)
        {
            if(message==RoutedWheel)
            {
                // Recheck visibility/modal state because the queued message can outlive a hide action.
                if(!_suspended()&&(!_cfg.ClickThrough||_cfg.WheelWhenClickThrough))
                {
                    var target=_mini!=null&&_mini.Handle==lParam?_mini:_desktop!=null&&_desktop.Handle==lParam?_desktop:null;
                    if(target!=null&&target.Visible)target.HandleWheelDelta(wParam.ToInt32());
                }
                return IntPtr.Zero;
            }
            return base.HandleMessage(message,wParam,lParam);
        }
        public override void Dispose()
        {
            if(_hook!=IntPtr.Zero){UnhookWindowsHookEx(_hook);_hook=IntPtr.Zero;}
            base.Dispose();
        }
    }
}
