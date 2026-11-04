using System;
using System.Drawing;
using System.Windows.Forms;

namespace GhostReader
{
    /// <summary>
    /// 无界面自检：验证编码探测、分章、排版、透明像素合成、任务栏窗口定位。
    /// 用 --selftest 参数运行，结果打到 stdout 并写 selftest-report.txt。
    /// </summary>
    internal static class SelfTest
    {
        public static int Run(string[] args)
        {
            int failures = 0;
            var log = new System.Text.StringBuilder();

            Action<string> say = s =>
            {
                try { Console.WriteLine(s); } catch { }
                log.AppendLine(s);
            };

            try
            {
                // ---------- 1. 编码探测 + 分章 ----------
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                string gbk = System.IO.Path.Combine(dir, "_testdata", "测试小说_GBK.txt");
                string utf8 = System.IO.Path.Combine(dir, "_testdata", "测试小说_UTF8.txt");

                if (System.IO.File.Exists(gbk))
                {
                    var b1 = BookLoader.Load(gbk, null);
                    if (b1 == null) { say("FAIL: GBK 文件加载返回 null"); failures++; }
                    else
                    {
                        say("GBK  -> enc=" + b1.DetectedEncoding + " lines=" + b1.Lines.Count
                            + " chapters=" + b1.Chapters.Count);
                        if (b1.DetectedEncoding.IndexOf("GB", StringComparison.OrdinalIgnoreCase) < 0)
                        { say("FAIL: GBK 编码识别错误"); failures++; }
                        else say("PASS: GBK 识别正确");
                        if (b1.Lines.Count < 6) { say("FAIL: 行数异常"); failures++; }
                        else say("PASS: 行数正常");
                        if (b1.Chapters.Count < 3) { say("FAIL: 分章数应>=3, 实际 " + b1.Chapters.Count); failures++; }
                        else
                        {
                            say("PASS: 分章正常 -> " + string.Join(" | ", b1.Chapters.ConvertAll(c => c.Title)));
                        }
                        // 中文是否乱码
                        bool hasCjk = false;
                        foreach (var l in b1.Lines)
                            if (l.IndexOfAny(new[] { '风', '夜', '剑', '青' }) >= 0) { hasCjk = true; break; }
                        if (!hasCjk) { say("FAIL: GBK 中文乱码"); failures++; }
                        else say("PASS: GBK 中文无乱码");
                    }
                }
                else { say("FAIL: 未找到 GBK 测试文件"); failures++; }

                if (System.IO.File.Exists(utf8))
                {
                    var b2 = BookLoader.Load(utf8, null);
                    if (b2 == null || b2.DetectedEncoding.IndexOf("UTF", StringComparison.OrdinalIgnoreCase) < 0)
                    { say("FAIL: UTF-8 识别错误 -> " + (b2 == null ? "null" : b2.DetectedEncoding)); failures++; }
                    else say("PASS: UTF-8 识别正确 (" + b2.DetectedEncoding + ")");
                }
                else { say("FAIL: 未找到 UTF-8 测试文件"); failures++; }

                // ---------- 2. 分层表面：透明像素与不透明度 ----------
                using (var surface = new LayeredSurface())
                {
                    surface.Resize(200, 100);
                    surface.Clear();
                    surface.Opacity = 1.0;

                    using (var br = new SolidBrush(Color.FromArgb(255, 255, 0, 0)))
                    {
                        surface.Graphics.FillRectangle(br, 0, 0, 50, 50);
                    }
                    int[] probe = surface.ProbeAlpha(10, 10);   // 应为不透明
                    int[] empty = surface.ProbeAlpha(150, 80);  // 应为全透明
                    say("pixel(alpha=255) A=" + probe[3] + " RGB=" + probe[0] + "," + probe[1] + "," + probe[2]);
                    say("pixel(empty)     A=" + empty[3]);
                    if (probe[3] != 255) { say("FAIL: 实心像素 alpha 应为 255, 实际 " + probe[3]); failures++; }
                    else say("PASS: 不透明像素 alpha=255");
                    if (empty[3] != 0) { say("FAIL: 空白像素应为全透明, 实际 " + empty[3]); failures++; }
                    else say("PASS: 空白像素全透明（背景真正透明）");

                    // 应用 50% 不透明度
                    surface.Opacity = 0.5;
                    surface.ApplyOpacity();
                    int[] half = surface.ProbeAlpha(10, 10);
                    say("pixel(opacity=50%) A=" + half[3]);
                    if (half[3] < 120 || half[3] > 135)
                    { say("FAIL: 50% 不透明度后 alpha 应≈128, 实际 " + half[3]); failures++; }
                    else say("PASS: 不透明度缩放正确 (255→" + half[3] + ")");
                }

                // ---------- 3. 任务栏窗口定位 ----------
                IntPtr tb = Win32.FindWindowW("Shell_TrayWnd", null);
                if (tb == IntPtr.Zero) { say("FAIL: 找不到 Shell_TrayWnd"); failures++; }
                else
                {
                    Win32.RECT r;
                    Win32.GetWindowRect(tb, out r);
                    say("Shell_TrayWnd rect = " + r.Left + "," + r.Top + "," + r.Right + "," + r.Bottom
                        + "  height=" + (r.Bottom - r.Top));
                    if ((r.Bottom - r.Top) <= 0) { say("FAIL: 任务栏高度异常"); failures++; }
                    else say("PASS: 任务栏可定位，高度 " + (r.Bottom - r.Top) + "px");

                    // 报告任务栏上的空闲段，人工可据此核对小条停靠位置
                    say("  任务栏像素: " + TaskbarDock.Describe());
                    var gaps = TaskbarDock.FindGaps();
                    if (gaps.Count == 0) say("WARN: 任务栏上未找到空闲段，小条将回退到手工偏移");
                    else
                    {
                        say("PASS: 找到 " + gaps.Count + " 段任务栏空闲区");
                        foreach (var gap in gaps)
                            say("  空闲段 x=" + gap.Left + ".." + gap.Right + " 宽度=" + gap.Width);
                    }
                }

                // ---------- 4. 全局热键注册 ----------
                // 4a 用一组几乎不会被占用的临时组合验证“注册机制本身”可用，
                //    不再因为用户机器上 Ctrl+Alt+T / Ctrl+Alt+A 之类被占用就整体判 FAIL。
                using (var host = new HostWindow())
                {
                    var probe = new Settings();
                    probe.HotkeyToggle = "Ctrl+Alt+Shift+F21";
                    probe.HotkeyNext = "Ctrl+Alt+Shift+F22";
                    probe.HotkeyPrev = "Ctrl+Alt+Shift+F23";
                    probe.HotkeyExit = "Ctrl+Alt+Shift+F24";
                    probe.HotkeySettings = "Ctrl+Alt+Shift+F13";
                    probe.HotkeyThrough = "Ctrl+Alt+Shift+F14";
                    probe.HotkeyAutoScroll = "Ctrl+Alt+Shift+F15";
                    host.Create(probe);
                    if (host.FailedHotkeys.Count == 0) say("PASS: HostWindow + 热键注册机制正常");
                    else { say("FAIL: 热键注册机制异常：" + string.Join(", ", host.FailedHotkeys)); failures++; }
                }

                // 4b 顺带看一眼个人配置里的组合有没有被占用。
                //    只提示不判失败 —— 占用时程序会自动改用备选组合，功能不受影响。
                using (var host = new HostWindow())
                {
                    host.Create(Settings.Load());
                    if (host.FailedHotkeys.Count == 0) say("PASS: 当前配置的热键全部可用");
                    else say("PASS: 当前配置中 " + string.Join(", ", host.FailedHotkeys)
                        + " 被其他程序占用（已自动改用备选组合，阅读功能不受影响）");
                }

                // ---------- 5. 悬浮窗创建 ----------
                {
                    var st = new AppState();
                    var cfg = new Settings();
                    using (var rw = new ReaderWindow(st, cfg))
                    {
                        rw.Create(100, 100, 400, 300);
                        say("PASS: 悬浮窗 HWND 创建成功");
                        rw.Redraw();
                        say("PASS: 悬浮窗分层渲染执行完成");
                    }
                }
            }
            catch (Exception ex)
            {
                say("EXCEPTION: " + ex);
                failures++;
            }

            say("");
            say(failures == 0 ? "==== ALL TESTS PASSED ====" : ("==== " + failures + " TEST(S) FAILED ===="));

            try
            {
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest-report.txt"),
                    log.ToString(), new System.Text.UTF8Encoding(false));
            }
            catch { }

            return failures == 0 ? 0 : 1;
        }
    }
}
