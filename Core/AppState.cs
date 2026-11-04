using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.Json;

namespace GhostReader
{
    internal sealed class AppState : IDisposable
    {
        public Book Book;
        public readonly List<string> Display = new List<string>();
        public readonly List<int> DisplayOrigin = new List<int>();
        public readonly List<string> DisplayChapter = new List<string>();
        public readonly ReadingLayout Layout = new ReadingLayout();
        public int ScrollLine;
        public int VisibleCount = 20, LineHeight = 20;
        public Font Font = new Font("Microsoft YaHei UI", 14f);
        public bool NeedsRebuild;
        private ReadingPosition _anchor = new ReadingPosition();
        private readonly Dictionary<string, ReadingPosition> _progress = new Dictionary<string, ReadingPosition>(StringComparer.OrdinalIgnoreCase);
        private bool _dirty;
        private string ProgressPath => Path.Combine(Settings.ConfigDir, "progress-v2.json");
        public event Action Changed;
        public event Action ManualNavigationRequested;
        public AppState()
        {
            try
            {
                if (File.Exists(ProgressPath))
                {
                    var saved = JsonSerializer.Deserialize<Dictionary<string, ReadingPosition>>(File.ReadAllText(ProgressPath));
                    if (saved != null) foreach (var pair in saved) if (pair.Value != null) _progress[pair.Key] = pair.Value;
                }
            }
            catch { }
        }
        public void OpenBook(Book book)
        {
            RememberCurrent();
            Book = book;
            string key = Path.GetFullPath(book.FilePath);
            SetAnchor(_progress.TryGetValue(key, out var saved) ? saved : new ReadingPosition(), false);
            NeedsRebuild = true;
        }
        public ReadingPosition GetAnchor() => _anchor.Copy();
        public void SetAnchor(ReadingPosition position, bool notify = true)
        {
            int paragraph = Book == null || Book.Lines.Count == 0 ? 0 : Math.Clamp(position.Paragraph, 0, Book.Lines.Count - 1);
            int offset = Book == null || Book.Lines.Count == 0 ? 0 : Math.Clamp(position.Offset, 0, Book.Lines[paragraph].Length);
            _anchor = new ReadingPosition { Paragraph = paragraph, Offset = offset, Fraction = double.IsFinite(position.Fraction) ? Math.Clamp(position.Fraction, 0, .999999999) : 0 };
            ScrollLine = Layout.IndexOf(_anchor);
            RememberCurrent();
            if (notify) RaiseChanged();
        }
        public void RememberCurrent()
        {
            if (Book == null || string.IsNullOrEmpty(Book.FilePath)) return;
            _progress[Path.GetFullPath(Book.FilePath)] = _anchor.Copy();
            _dirty = true;
        }
        public void SaveProgress()
        {
            if (!_dirty) return;
            try { Settings.AtomicWrite(ProgressPath, JsonSerializer.Serialize(_progress)); _dirty = false; }
            catch { }
        }
        // Compatibility with the original command and layout interfaces.
        public int GetProgress(string path) => ScrollLine;
        public void SetProgress(string path, int value) { RememberCurrent(); }
        public int TotalLines => Display.Count;
        public void RaiseChanged() => Changed?.Invoke();
        public int CurrentChapterIndex => ChapterIndexFor(_anchor.Paragraph);
        private int ChapterIndexFor(int paragraph)
        {
            if(Book==null)return -1;
            int lo=0,hi=Book.Chapters.Count-1,result=-1;
            while(lo<=hi){int mid=lo+(hi-lo)/2;if(Book.Chapters[mid].StartLine<=paragraph){result=mid;lo=mid+1;}else hi=mid-1;}
            return result;
        }
        public string ChapterOf(int displayIndex)
        {
            if(Book==null)return "";
            int line=displayIndex>=0&&displayIndex<DisplayOrigin.Count?DisplayOrigin[displayIndex]:_anchor.Paragraph;
            int chapter=ChapterIndexFor(line);return chapter<0?Book.Title:Book.Chapters[chapter].Title;
        }
        public void Rebuild(Graphics g, int widthPx, Settings cfg)
        {
            Font.Dispose(); Font = cfg.Appearance(false).CreateFont();
            Layout.Build(Book, g, Font, Math.Max(1, widthPx - cfg.Padding * 2));
            Display.Clear(); DisplayOrigin.Clear(); DisplayChapter.Clear();
            foreach (var line in Layout.Lines) { Display.Add(line.Text); DisplayOrigin.Add(line.Paragraph); }
            for (int i = 0; i < Display.Count; i++) DisplayChapter.Add(ChapterOf(i));
            ScrollLine = Layout.IndexOf(_anchor);
            LineHeight = Math.Max(1, (int)Math.Ceiling(Font.GetHeight(g) * cfg.LineSpacing));
            NeedsRebuild = false;
        }
        public void MoveInLayout(ReadingLayout layout, int delta)
        {
            ManualNavigationRequested?.Invoke();
            if (layout.Lines.Count == 0) return;
            int index = (int)Math.Clamp((long)layout.IndexOf(_anchor) + delta, 0, layout.Lines.Count - 1);
            SetAnchor(layout.Lines[index].Position);
        }
        public void ScrollBy(int delta) => MoveInLayout(Layout, delta);
        public void ScrollTo(int line)
        {
            ManualNavigationRequested?.Invoke();
            if (Layout.Lines.Count != 0) SetAnchor(Layout.Lines[Math.Clamp(line, 0, Layout.Lines.Count - 1)].Position);
        }
        public void PageDown() => ScrollBy(Math.Max(1, VisibleCount));
        public void PageUp() => ScrollBy(-Math.Max(1, VisibleCount));
        public bool GoToChapter(string title)
        {
            if (Book == null) return false;
            int index = Book.Chapters.FindIndex(c => c.Title == title);
            return GoToChapter(index);
        }
        public bool GoToChapter(int index)
        {
            if (Book == null || index < 0 || index >= Book.Chapters.Count) return false;
            ManualNavigationRequested?.Invoke();
            SetAnchor(new ReadingPosition { Paragraph = Book.Chapters[index].StartLine }); return true;
        }
        public string ChapterProgress() => ChapterOf(ScrollLine);
        public void Dispose() { RememberCurrent(); SaveProgress(); Font.Dispose(); }
    }
}
