using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GhostReader
{
    /// <summary>
    /// 小说文本装载：编码探测（UTF-8 / UTF-8-BOM / UTF-16LE / GB18030 / Big5）+ 分章切分 + 段落化。
    /// </summary>
    internal sealed class Book
    {
        public string FilePath = "";
        public string Title = "";
        public List<Chapter> Chapters = new List<Chapter>();
        /// <summary>全文按"排版行"切分后的行数组。</summary>
        public List<string> Lines = new List<string>();
        public int CharCount;
        public string DetectedEncoding = "";

        public sealed class Chapter
        {
            public string Title;
            public int StartLine;   // 在 Lines 中的起始行
            public int EndLine;
            public override string ToString() { return Title; }
        }

        /// <summary>EPUB 解析出的显式章节标题行号。</summary>
        public List<int> HeadingLines = new List<int>();
    }

    internal static class BookLoader
    {
        private static readonly string[] ChapterPatterns =
        {
            @"^\s*第\s*[0-9零一二三四五六七八九十百千万两]+\s*[章回节卷篇幕折]",
            @"^\s*第\s*[0-9零一二三四五六七八九十百千万两]+\s*[部篇]\s*[:：、\s]",
            @"^\s*(Chapter|CHAPTER|Part|PART|Book|BOOK)\s+[0-9IVXLCivxlc]+",
            @"^\s*[0-9]{1,4}\s*[、.．]\s*\S",
            @"^\s*(?:楔子|序章|序言|引子|前言|后记|尾声|番外|终章|大结局)(?:$|[\s:：、　])",
        };

        private static readonly System.Text.RegularExpressions.Regex[] ChapterRegexes =
            BuildRegexes();

        private static System.Text.RegularExpressions.Regex[] BuildRegexes()
        {
            var list = new List<System.Text.RegularExpressions.Regex>();
            foreach (var p in ChapterPatterns)
            {
                list.Add(new System.Text.RegularExpressions.Regex(p,
                    System.Text.RegularExpressions.RegexOptions.Compiled));
            }
            return list.ToArray();
        }

        /// <summary>读取并解析。返回 null 表示失败。</summary>
        public static Book Load(string path, Action<string> onStatus)
        {
            string ext = Path.GetExtension(path ?? "").ToLowerInvariant();
            if (ext == ".epub")
            {
                try { return EpubLoader.Load(path, onStatus); }
                catch (Exception ex)
                {
                    onStatus?.Invoke("EPUB 解析失败：" + ex.Message);
                    return null;
                }
            }

            byte[] raw;
            try { raw = File.ReadAllBytes(path); }
            catch (Exception ex) { onStatus?.Invoke("读取失败：" + ex.Message); return null; }

            string text;
            string enc;
            Decode(raw, out text, out enc);
            onStatus?.Invoke("编码：" + enc + "，字符数 " + text.Length);

            var book = new Book
            {
                FilePath = path,
                Title = Path.GetFileNameWithoutExtension(path),
                DetectedEncoding = enc,
                CharCount = text.Length
            };

            SplitLines(text, book);
            DetectChaptersInto(book, book.HeadingLines);
            return book;
        }

        /// <summary>编码探测：BOM 优先，其次 UTF-8 严格校验，最后 GB18030 兜底（兼容 GBK/Big5）。</summary>
        private static void Decode(byte[] raw, out string text, out string enc)
        {
            // UTF-8 BOM
            if (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF)
            {
                text = new UTF8Encoding(false).GetString(raw, 3, raw.Length - 3);
                enc = "UTF-8 (BOM)";
                return;
            }
            // UTF-16 LE BOM
            if (raw.Length >= 2 && raw[0] == 0xFF && raw[1] == 0xFE)
            {
                text = Encoding.Unicode.GetString(raw, 2, raw.Length - 2);
                enc = "UTF-16LE";
                return;
            }
            // UTF-16 BE BOM
            if (raw.Length >= 2 && raw[0] == 0xFE && raw[1] == 0xFF)
            {
                text = Encoding.BigEndianUnicode.GetString(raw, 2, raw.Length - 2);
                enc = "UTF-16BE";
                return;
            }

            // 无 BOM：先试严格 UTF-8
            try
            {
                var strict = new UTF8Encoding(false, true);
                text = strict.GetString(raw);
                enc = "UTF-8";
                return;
            }
            catch (DecoderFallbackException)
            {
                // 不是合法 UTF-8
            }

            // 兜底：GB18030（向下兼容 GBK/GB2312），若出现大量替换字符则尝试 Big5
            text = DecodeWith(raw, 54936); // 54936 = GB18030
            if (CountReplacement(text) > raw.Length / 200)
            {
                var alt = DecodeWith(raw, 950); // Big5
                if (CountReplacement(alt) < CountReplacement(text))
                {
                    text = alt;
                    enc = "Big5";
                    return;
                }
            }
            enc = "GB18030/GBK";
        }

        private static string DecodeWith(byte[] raw, int codePage)
        {
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                var enc = Encoding.GetEncoding(codePage, EncoderFallback.ReplacementFallback,
                                                  DecoderFallback.ReplacementFallback);
                return enc.GetString(raw);
            }
            catch { return Encoding.Default.GetString(raw); }
        }

        private static int CountReplacement(string s)
        {
            int n = 0;
            foreach (var ch in s) if (ch == '\uFFFD') n++;
            return n;
        }

        /// <summary>把全文切成排版行：先按 \r\n / \n 拆段落，再把超长段落按固定宽度硬折（保持"行"概念稳定）。</summary>
        private static void SplitLines(string text, Book book)
        {
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            var sb = new StringBuilder(text.Length);
            foreach (var ch in text)
            {
                if (ch == '\r') continue;
                if (ch == '\n') { sb.Append('\n'); continue; }
                if (ch == '\t') { sb.Append("    "); continue; }
                if (ch == '\0') continue;
                sb.Append(ch);
            }

            string[] paras = sb.ToString().Split('\n');
            foreach (var p in paras)
            {
                string line = p.TrimEnd();
                if (line.Length == 0) continue;      // 丢空行，屏幕更省
                book.Lines.Add(line);
            }
        }

        /// <summary>
        /// 分章：优先采用 EPUB 的显式标题行；否则用正则识别"第N章""Chapter N"等。
        /// </summary>
        public static void DetectChaptersInto(Book book, List<int> explicitHeadings)
        {
            int n = book.Lines.Count;
            var headingSet = new HashSet<int>();
            if (explicitHeadings != null)
            {
                foreach (var h in explicitHeadings)
                    if (h >= 0 && h < n) headingSet.Add(h);
            }

            for (int i = 0; i < n; i++)
            {
                string s = book.Lines[i];
                bool isHeading = headingSet.Contains(i);
                if (!isHeading)
                {
                    if (s.Length > 60) continue;          // 太长不像标题
                    foreach (var rx in ChapterRegexes)
                    {
                        if (rx.IsMatch(s)) { isHeading = true; break; }
                    }
                }
                if (!isHeading) continue;
                book.Chapters.Add(new Book.Chapter { Title = s.Trim(), StartLine = i, EndLine = n });
            }

            // 计算每章结束行
            for (int i = 0; i < book.Chapters.Count; i++)
            {
                int end = (i + 1 < book.Chapters.Count) ? book.Chapters[i + 1].StartLine : n;
                book.Chapters[i].EndLine = end;
            }
            if (book.Chapters.Count == 0)
            {
                book.Chapters.Add(new Book.Chapter { Title = "全文", StartLine = 0, EndLine = n });
            }
        }
    }
}
