using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace GhostReader
{
    /// <summary>
    /// EPUB 解析：读取 container.xml → OPF → spine 顺序，逐个解出 XHTML/HTML 正文。
    /// EPUB 本质是 zip + XML，不需要额外依赖。
    /// </summary>
    internal static class EpubLoader
    {
        private static readonly Regex RxScript =
            new Regex(@"<script[^>]*>[\s\S]*?</script>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxStyle =
            new Regex(@"<style[^>]*>[\s\S]*?</style>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxHeading =
            new Regex(@"<h[1-6][^>]*>([\s\S]*?)</h[1-6]>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxBlockEnd =
            new Regex(@"</(p|div|li|h[1-6]|blockquote|section|article|tr)>|<br\s*/?>",
                      RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxTag =
            new Regex(@"<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex RxWs =
            new Regex(@"[ \t\u00A0]+", RegexOptions.Compiled);

        private static readonly Dictionary<string, string> Entities = BuildEntities();

        private static Dictionary<string, string> BuildEntities()
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                {"&amp;", "&"}, {"&lt;", "<"}, {"&gt;", ">"}, {"&quot;", "\""}, {"&apos;", "'"},
                {"&nbsp;", " "}, {"&hellip;", "…"}, {"&mdash;", "—"}, {"&ndash;", "–"},
                {"&ldquo;", "“"}, {"&rdquo;", "”"}, {"&lsquo;", "‘"}, {"&rsquo;", "’"},
                {"&middot;", "·"}, {"&times;", "×"}, {"&copy;", "©"},
            };
            // 数字实体 &#123; &#x1F600;
            return d;
        }

        public static Book Load(string path, Action<string> onStatus)
        {
            var book = new Book { FilePath = path, Title = Path.GetFileNameWithoutExtension(path) };
            var headingLines = new List<int>();

            using (var zip = ZipFile.OpenRead(path))
            {
                // 1) container.xml → OPF 路径
                var containerEntry = zip.GetEntry("META-INF/container.xml");
                if (containerEntry == null) { onStatus?.Invoke("EPUB 结构异常：缺少 container.xml"); return null; }

                XDocument container;
                using (var s = containerEntry.Open()) container = XDocument.Load(s);

                XNamespace opfNs = "urn:oasis:names:tc:opendocument:xmlns:container";
                var rootfile = container.Descendants(opfNs + "rootfile").FirstOrDefaultSafe();
                string opfPath = rootfile?.Attribute("full-path")?.Value;
                if (string.IsNullOrEmpty(opfPath))
                {
                    // 回退：直接找 .opf
                    var opfEntry = zip.Entries.FirstOrDefault(e =>
                        e.FullName.EndsWith(".opf", StringComparison.OrdinalIgnoreCase));
                    if (opfEntry == null) { onStatus?.Invoke("EPUB 结构异常：找不到 OPF"); return null; }
                    opfPath = opfEntry.FullName;
                }

                // 2) OPF → manifest + spine
                var opfZipEntry = zip.GetEntry(opfPath);
                if (opfZipEntry == null) { onStatus?.Invoke("EPUB 结构异常：找不到 OPF 条目"); return null; }

                XDocument opf;
                using (var s = opfZipEntry.Open()) opf = XDocument.Load(s);

                string opfDir = GetDirectory(opfPath);

                XNamespace dcNs = "http://purl.org/dc/elements/1.1/";
                var titleEl = opf.Descendants(dcNs + "title").FirstOrDefaultSafe();
                if (titleEl != null && !string.IsNullOrWhiteSpace(titleEl.Value))
                    book.Title = titleEl.Value.Trim();

                var manifest = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var item in opf.Descendants().Where(e => e.Name.LocalName == "item"))
                {
                    string id = item.Attribute("id")?.Value;
                    string href = item.Attribute("href")?.Value;
                    if (id == null || href == null) continue;
                    manifest[id] = href;
                }

                var spineIds = new List<string>();
                foreach (var itemref in opf.Descendants().Where(e => e.Name.LocalName == "itemref"))
                {
                    string idref = itemref.Attribute("idref")?.Value;
                    if (idref != null && manifest.ContainsKey(idref)) spineIds.Add(idref);
                }
                if (spineIds.Count == 0) spineIds.AddRange(manifest.Keys);

                // 3) 逐章抽取正文
                int added = 0;
                foreach (var id in spineIds)
                {
                    string href = manifest[id];
                    string entryPath = NormalizePath(Combine(opfDir, href));
                    var entry = zip.GetEntry(entryPath);
                    if (entry == null) continue;

                    string html;
                    using (var s = entry.Open())
                    using (var reader = new StreamReader(s, DetectEncoding(entry), true))
                        html = reader.ReadToEnd();

                    string plain = HtmlToPlainText(html);

                    // 章节标题：优先取 <h1>~<h6>
                    string chapterTitle = null;
                    foreach (Match m in RxHeading.Matches(html))
                    {
                        string t = Clean(m.Groups[1].Value);
                        if (!string.IsNullOrWhiteSpace(t)) { chapterTitle = t; break; }
                    }

                    if (chapterTitle != null)
                    {
                        headingLines.Add(book.Lines.Count);
                        book.Lines.Add(chapterTitle);
                        added++;
                    }

                    foreach (var para in plain.Split('\n'))
                    {
                        string p = para.Trim();
                        if (p.Length == 0) continue;
                        if (p == chapterTitle) continue;   // 标题已单独记录
                        book.Lines.Add(p);
                        added++;
                    }
                }

                if (added == 0) { onStatus?.Invoke("EPUB 内没有可读文本"); return null; }
            }

            book.CharCount = 0;
            foreach (var l in book.Lines) book.CharCount += l.Length;
            book.DetectedEncoding = "EPUB";
            book.HeadingLines = headingLines;
            BookLoader.DetectChaptersInto(book, headingLines);
            return book;
        }

        private static Encoding DetectEncoding(ZipArchiveEntry entry)
        {
            try
            {
                using (var s = entry.Open())
                {
                    var buf = new byte[1024];
                    int n = s.Read(buf, 0, buf.Length);
                    if (n >= 3 && buf[0] == 0xEF && buf[1] == 0xBB && buf[2] == 0xBF)
                        return new UTF8Encoding(false);
                    if (n >= 2 && buf[0] == 0xFF && buf[1] == 0xFE)
                        return Encoding.Unicode;
                    if (n >= 2 && buf[0] == 0xFE && buf[1] == 0xFF)
                        return Encoding.BigEndianUnicode;
                }
            }
            catch { }
            return new UTF8Encoding(false);
        }

        private static string HtmlToPlainText(string html)
        {
            if (string.IsNullOrEmpty(html)) return "";
            string s = RxScript.Replace(html, " ");
            s = RxStyle.Replace(s, " ");
            s = RxBlockEnd.Replace(s, "\n");
            s = RxTag.Replace(s, "");
            s = DecodeEntities(s);
            s = RxWs.Replace(s, " ");
            var sb = new StringBuilder(s.Length);
            foreach (var line in s.Split('\n'))
            {
                string t = line.Trim();
                if (t.Length > 0) sb.Append(t).Append('\n');
            }
            return sb.ToString();
        }

        private static string Clean(string html)
        {
            string s = RxTag.Replace(html, "");
            s = DecodeEntities(s);
            return RxWs.Replace(s, " ").Trim();
        }

        private static string DecodeEntities(string s)
        {
            if (s.IndexOf('&') < 0) return s;
            foreach (var kv in Entities) s = s.Replace(kv.Key, kv.Value);

            // 数字实体
            s = Regex.Replace(s, "&#x([0-9a-fA-F]+);", m =>
            {
                int cp;
                if (int.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.HexNumber,
                        null, out cp) && cp > 0 && cp <= 0x10FFFF)
                    return char.ConvertFromUtf32(cp);
                return m.Value;
            });
            s = Regex.Replace(s, "&#([0-9]+);", m =>
            {
                int cp;
                if (int.TryParse(m.Groups[1].Value, out cp) && cp > 0 && cp <= 0x10FFFF)
                    return char.ConvertFromUtf32(cp);
                return m.Value;
            });
            return s;
        }

        private static string GetDirectory(string path)
        {
            int i = path.LastIndexOf('/');
            return i < 0 ? "" : path.Substring(0, i + 1);
        }

        private static string Combine(string dir, string href)
        {
            if (string.IsNullOrEmpty(dir)) return href;
            if (href.StartsWith("/")) return href.TrimStart('/');
            return dir + href;
        }

        private static string NormalizePath(string p)
        {
            var parts = new List<string>();
            foreach (var seg in p.Replace('\\', '/').Split('/'))
            {
                if (seg == "" || seg == ".") continue;
                if (seg == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); continue; }
                parts.Add(seg);
            }
            return string.Join("/", parts);
        }
    }

    internal static class LinqExtensions
    {
        public static T FirstOrDefaultSafe<T>(this System.Collections.Generic.IEnumerable<T> src)
            where T : class
        {
            foreach (var item in src) return item;
            return null;
        }
    }
}
