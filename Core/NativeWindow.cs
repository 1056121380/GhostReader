using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace GhostReader
{
    /// <summary>
    /// 轻量原生窗口基类：自注册窗口类 + WndProc 转发到实例。
    /// 不依赖 WinForms 控件，避免任务栏内嵌时被父窗口反向影响。
    /// </summary>
    internal class NativeWindow : IDisposable
    {
        private static readonly Dictionary<string, ushort> _classes = new Dictionary<string, ushort>();
        private static readonly Dictionary<IntPtr, NativeWindow> _map = new Dictionary<IntPtr, NativeWindow>();
        private static readonly WndProcDelegate _sharedProc;
        private static readonly object _sync = new object();

        protected IntPtr _hwnd;
        private GCHandle _selfHandle;
        private string _className;
        private bool _disposed;

        protected delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct CREATESTRUCT
        {
            public IntPtr lpCreateParams;
            public IntPtr hInstance;
            public IntPtr hMenu;
            public IntPtr hwndParent;
            public int cy, cx, y, x;
            public int style;
            public IntPtr lpszName;
            public IntPtr lpszClass;
            public int dwExStyle;
        }

        static NativeWindow()
        {
            _sharedProc = SharedWndProc;
        }

        public IntPtr Handle { get { return _hwnd; } }
        public bool IsCreated { get { return _hwnd != IntPtr.Zero; } }

        protected virtual IntPtr HandleMessage(uint msg, IntPtr wParam, IntPtr lParam)
        {
            return Win32.DefWindowProcW(_hwnd, msg, wParam, lParam);
        }

        private static IntPtr SharedWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == Win32.WM_NCCREATE)
            {
                try
                {
                    var cs = (CREATESTRUCT)Marshal.PtrToStructure(lParam, typeof(CREATESTRUCT));
                    var gch = GCHandle.FromIntPtr(cs.lpCreateParams);
                    var inst = (NativeWindow)gch.Target;
                    inst._hwnd = hWnd;
                    lock (_sync) _map[hWnd] = inst;
                }
                catch { }
            }

            NativeWindow inst2;
            lock (_sync) _map.TryGetValue(hWnd, out inst2);

            if (inst2 != null)
            {
                try { return inst2.HandleMessage(msg, wParam, lParam); }
                catch (Exception ex)
                {
                    try { FileLog.Write(ex); } catch { }
                    return Win32.DefWindowProcW(hWnd, msg, wParam, lParam);
                }
                finally
                {
                    if (msg == Win32.WM_NCDESTROY)
                    {
                        inst2._hwnd = IntPtr.Zero;
                        lock (_sync) _map.Remove(hWnd);
                        if (inst2._selfHandle.IsAllocated) inst2._selfHandle.Free();
                    }
                }
            }
            return Win32.DefWindowProcW(hWnd, msg, wParam, lParam);
        }

        private static ushort EnsureClass(string className)
        {
            lock (_sync)
            {
                ushort id;
                if (_classes.TryGetValue(className, out id)) return id;

                var wc = new Win32.WNDCLASSEX();
                wc.cbSize = (uint)Marshal.SizeOf(typeof(Win32.WNDCLASSEX));
                wc.style = 0x0003;                       // CS_HREDRAW | CS_VREDRAW
                wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_sharedProc);
                wc.hInstance = Win32.GetModuleHandleW(null);
                wc.lpszClassName = Marshal.StringToHGlobalUni(className);
                wc.hCursor = Win32.LoadCursorW(IntPtr.Zero, Win32.IDC_ARROW);
                wc.hbrBackground = IntPtr.Zero;
                id = Win32.RegisterClassExW(ref wc);
                Marshal.FreeHGlobal(wc.lpszClassName);
                if (id == 0)
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err == 1410) // class already exists
                    {
                        id = 1;
                    }
                    else
                    {
                        throw new InvalidOperationException("RegisterClassEx failed: " + err);
                    }
                }
                _classes[className] = id;
                return id;
            }
        }

        protected void CreateNative(string className, string title, uint style, uint exStyle,
            int x, int y, int width, int height, IntPtr parent)
        {            EnsureClass(className);
            _className = className;
            _selfHandle = GCHandle.Alloc(this);
            _hwnd = Win32.CreateWindowExW(exStyle, className, title, style,
                x, y, width, height, parent, IntPtr.Zero,
                Win32.GetModuleHandleW(null), GCHandle.ToIntPtr(_selfHandle));
            if (_hwnd == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                throw new InvalidOperationException("CreateWindowEx failed: " + err);
            }
        }

        public void Show() { if (_hwnd != IntPtr.Zero) Win32.ShowWindow(_hwnd, Win32.SW_SHOWNA); }
        public void Hide() { if (_hwnd != IntPtr.Zero) Win32.ShowWindow(_hwnd, Win32.SW_HIDE); }
        public bool Visible { get { return _hwnd != IntPtr.Zero && Win32.IsWindowVisible(_hwnd); } }

        public void SetStyleEx(int index, int value)
        {
            Win32.SetWindowLongPtrSafe(_hwnd, index, new IntPtr(value));
        }

        public int GetStyleEx(int index)
        {
            return Win32.GetWindowLongPtrSafe(_hwnd, index).ToInt32();
        }

        public void MoveWindow(int x, int y, int w, int h)
        {
            Win32.SetWindowPos(_hwnd, IntPtr.Zero, x, y, w, h,
                Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
        }

        public virtual void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_hwnd != IntPtr.Zero)
            {
                Win32.DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }
            if (_selfHandle.IsAllocated) _selfHandle.Free();
            lock (_sync)
            {
                if (_className != null) _className = null;
            }
        }
    }
}
