using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GhostReader
{
    /// <summary>
    /// 宿主窗口（隐藏）：只用来接收全局热键消息与拖放通知。
    /// </summary>
    internal sealed class HostWindow : NativeWindow
    {
        private const string CLASS = "GhostReaderHost";

        public event Action<int> HotkeyPressed;
        /// <summary>Explorer 重启（任务栏被重建）时触发，需要重新挂载任务栏面板。</summary>
        public event Action TaskbarRecreated;

        private uint _taskbarCreatedMsg;
        private uint _externalCommandMsg;

        public void Create(Settings cfg)
        {
            CreateNative(CLASS, ProfileIdentity.HostTitle, 0, 0, 0, 0, 0, 0, IntPtr.Zero);
            _taskbarCreatedMsg = Win32.RegisterWindowMessageW("TaskbarCreated");
            _externalCommandMsg = Win32.RegisterWindowMessageW("GhostReaderCommandV2");
            // 注册全局热键
            RegisterHotkeys(cfg);
        }

        private readonly Dictionary<int, uint> _hotkeyMap = new Dictionary<int, uint>();
        private int _hotkeyId = 1;

        /// <summary>一个热键槽位的注册结果：用户设置的组合 + 实际生效的组合。</summary>
        public sealed class HotkeySlot
        {
            public string Role;
            public string Requested;
            public string Effective;
            public bool Failed { get { return string.IsNullOrEmpty(Effective); } }
            public bool AutoChanged { get { return !Failed && Effective != Requested; } }
        }

        public readonly List<HotkeySlot> HotkeySlots = new List<HotkeySlot>();

        public void RegisterHotkeys(Settings cfg)
        {
            foreach (int id in _hotkeyMap.Keys) Win32.UnregisterHotKey(_hwnd, id);
            _hotkeyMap.Clear(); _hotkeyId = 1; FailedHotkeys.Clear(); HotkeySlots.Clear();
            // 顺序即热键 id 顺序，Program.OnHotkey 按 id 分派，改动时勿调整顺序
            RegisterSlot("toggle", cfg.HotkeyToggle);
            RegisterSlot("next", cfg.HotkeyNext);
            RegisterSlot("prev", cfg.HotkeyPrev);
            RegisterSlot("exit", cfg.HotkeyExit);
            RegisterSlot("settings", cfg.HotkeySettings);
            RegisterSlot("through", cfg.HotkeyThrough);
            RegisterSlot("autoscroll", cfg.HotkeyAutoScroll);
        }

        public readonly List<string> FailedHotkeys = new List<string>();

        /// <summary>
        /// 注册单个热键。被别的程序占用时不会就此罢工：依次尝试
        /// “加 Shift”“去掉 Alt”“退到 F21–F24”等备选组合，
        /// 只要有一个能注册成功就继续用，并在设置里如实显示真正生效的键。
        ///
        /// 关键点：热键 id 在重试前就固定下来 —— id 决定 Program 里的动作分派，
        /// 如果每试一次就换 id，角色映射会整体错位。
        /// </summary>
        private void RegisterSlot(string role, string spec)
        {
            var slot = new HotkeySlot { Role = role, Requested = spec };
            int id = _hotkeyId++;
            foreach (string candidate in HotKeySpec.Variants(spec))
            {
                if (!HotKeySpec.Parse(candidate, out uint mods, out uint vk)) continue;
                if (Win32.RegisterHotKey(_hwnd, id, mods | Win32.MOD_NOREPEAT, vk))
                {
                    _hotkeyMap[id] = vk;
                    slot.Effective = candidate;
                    break;
                }
                Win32.UnregisterHotKey(_hwnd, id);   // 注册失败不会占用 id，但注销一次更保险
            }
            if (slot.Failed) FailedHotkeys.Add(string.IsNullOrWhiteSpace(spec) ? "（空快捷键）" : spec);
            HotkeySlots.Add(slot);
        }

        /// <summary>把自动降级后真正生效的组合写回配置，返回是否有改动。</summary>
        public bool ApplyEffectiveHotkeys(Settings cfg)
        {
            bool changed = false;
            foreach (var slot in HotkeySlots)
            {
                if (!slot.AutoChanged) continue;
                switch (slot.Role)
                {
                    case "toggle": changed |= Set(ref cfg.HotkeyToggle, slot.Effective); break;
                    case "next": changed |= Set(ref cfg.HotkeyNext, slot.Effective); break;
                    case "prev": changed |= Set(ref cfg.HotkeyPrev, slot.Effective); break;
                    case "exit": changed |= Set(ref cfg.HotkeyExit, slot.Effective); break;
                    case "settings": changed |= Set(ref cfg.HotkeySettings, slot.Effective); break;
                    case "through": changed |= Set(ref cfg.HotkeyThrough, slot.Effective); break;
                    case "autoscroll": changed |= Set(ref cfg.HotkeyAutoScroll, slot.Effective); break;
                }
            }
            return changed;
        }

        private static bool Set(ref string field, string value)
        {
            if (field == value) return false;
            field = value; return true;
        }

        public override void Dispose()
        {
            foreach (int id in _hotkeyMap.Keys) Win32.UnregisterHotKey(_hwnd, id);
            _hotkeyMap.Clear(); base.Dispose();
        }

        protected override IntPtr HandleMessage(uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (_externalCommandMsg != 0 && msg == _externalCommandMsg)
            {
                int command = wParam.ToInt32();
                if (command == 1 || command == 5 || command == 8) HotkeyPressed?.Invoke(command);
                return IntPtr.Zero;
            }
            if (msg == Win32.WM_HOTKEY)
            {
                if (HotkeyPressed != null) HotkeyPressed(wParam.ToInt32());
                return IntPtr.Zero;
            }
            if (_taskbarCreatedMsg != 0 && msg == _taskbarCreatedMsg)
            {
                if (TaskbarRecreated != null) TaskbarRecreated();
                return IntPtr.Zero;
            }
            return base.HandleMessage(msg, wParam, lParam);
        }
    }

    internal static class HotKeySpec
    {
        public static bool Parse(string spec, out uint mods, out uint vk)
        {
            mods = 0;
            vk = 0;
            if (string.IsNullOrEmpty(spec)) return false;
            var parts = spec.Split('+');
            int keys = 0;
            foreach (var raw in parts)
            {
                string p = raw.Trim();
                if (p.Length == 0) return false;
                switch (p.ToLowerInvariant())
                {
                    case "ctrl": case "control": mods |= Win32.MOD_CONTROL; continue;
                    case "alt": mods |= Win32.MOD_ALT; continue;
                    case "shift": mods |= Win32.MOD_SHIFT; continue;
                    case "win": mods |= Win32.MOD_WIN; continue;
                }
                if (++keys > 1) return false;
                if (p.Length == 1 && char.IsAsciiLetterOrDigit(p[0]))
                { vk = char.ToUpperInvariant(p[0]); continue; }
                if (p.StartsWith("F", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(p.Substring(1), out int fn) && fn >= 1 && fn <= 24)
                { vk = (uint)(0x70 + fn - 1); continue; }
                switch (p.ToLowerInvariant())
                {
                    case "up": vk = 0x26; break;
                    case "down": vk = 0x28; break;
                    case "left": vk = 0x25; break;
                    case "right": vk = 0x27; break;
                    case "space": vk = 0x20; break;
                    case "enter": case "return": vk = 0x0D; break;
                    case "pgup": case "pageup": vk = 0x21; break;
                    case "pgdn": case "pagedown": vk = 0x22; break;
                    case "esc": case "escape": vk = 0x1B; break;
                    default: return false;
                }
            }
            return mods != 0 && vk != 0;
        }

        /// <summary>把修饰键 + 虚拟键码还原成可读组合，例如 “Ctrl+Alt+Shift+X”。</summary>
        public static string Format(uint mods, uint vk)
        {
            var parts = new List<string>();
            if ((mods & Win32.MOD_CONTROL) != 0) parts.Add("Ctrl");
            if ((mods & Win32.MOD_ALT) != 0) parts.Add("Alt");
            if ((mods & Win32.MOD_SHIFT) != 0) parts.Add("Shift");
            if ((mods & Win32.MOD_WIN) != 0) parts.Add("Win");
            parts.Add(KeyName(vk));
            return string.Join("+", parts.ToArray());
        }

        public static string KeyName(uint vk)
        {
            if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x70 + 1).ToString();
            if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
            if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();
            switch (vk)
            {
                case 0x26: return "Up";
                case 0x28: return "Down";
                case 0x25: return "Left";
                case 0x27: return "Right";
                case 0x20: return "Space";
                case 0x0D: return "Enter";
                case 0x21: return "PageUp";
                case 0x22: return "PageDown";
                case 0x1B: return "Esc";
            }
            return "0x" + vk.ToString("X2");
        }

        /// <summary>
        /// 组合被占用时的备选序列（第一个是用户设置的原组合）。
        /// 越往后越不容易和别的软件撞车：加 Shift → 去掉 Alt → F21–F24。
        /// </summary>
        public static List<string> Variants(string spec)
        {
            var list = new List<string>();
            if (!Parse(spec, out uint mods, out uint vk)) return list;
            list.Add(spec);
            if ((mods & Win32.MOD_SHIFT) == 0)
                list.Add(Format(mods | Win32.MOD_SHIFT, vk));
            if ((mods & Win32.MOD_ALT) != 0)
                list.Add(Format((mods & ~Win32.MOD_ALT) | Win32.MOD_SHIFT, vk));
            foreach (uint fn in new uint[] { 0x84, 0x85, 0x86, 0x87 })   // F21–F24
                list.Add(Format(Win32.MOD_CONTROL | Win32.MOD_ALT | Win32.MOD_SHIFT, fn));
            return list;
        }
    }
}
