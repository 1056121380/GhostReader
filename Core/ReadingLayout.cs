using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;

namespace GhostReader
{
    internal sealed class ReadingPosition
    {
        public int Paragraph { get; set; }
        public int Offset { get; set; }
        public double Fraction { get; set; }
        public ReadingPosition Copy() => new ReadingPosition { Paragraph = Paragraph, Offset = Offset, Fraction = Fraction };
    }

    internal sealed class ReadingLine
    {
        public string Text;
        public int Paragraph, Offset;
        public ReadingPosition Position => new ReadingPosition { Paragraph = Paragraph, Offset = Offset };
    }

    internal sealed class ReadingLayout
    {
        public readonly List<ReadingLine> Lines = new List<ReadingLine>();
        private double[] _paragraphStarts = Array.Empty<double>();
        private int[] _paragraphLengths = Array.Empty<int>();
        private double Absolute(ReadingPosition position)
        {
            if (_paragraphLengths.Length == 0) return 0;
            int paragraph = Math.Clamp(position.Paragraph, 0, _paragraphLengths.Length - 1);
            return _paragraphStarts[paragraph] + Math.Clamp(position.Offset, 0, _paragraphLengths[paragraph]) + Math.Clamp(double.IsFinite(position.Fraction) ? position.Fraction : 0, 0, .999999999);
        }
        public double CoordinateOf(ReadingPosition position, bool includeTail = false)
        {
            if (Lines.Count == 0) return 0;
            int index = IndexOf(position);
            if (index == Lines.Count - 1 && !includeTail) return index;
            double start = Absolute(Lines[index].Position);
            double end = index + 1 < Lines.Count ? Absolute(Lines[index + 1].Position) : _paragraphStarts[^1] - 1;
            return index + Math.Clamp((Absolute(position) - start) / Math.Max(1, end - start), 0, 1);
        }
        public ReadingPosition PositionAt(double coordinate, bool includeTail = false)
        {
            if (Lines.Count == 0) return new ReadingPosition();
            coordinate = Math.Clamp(double.IsFinite(coordinate) ? coordinate : 0, 0, includeTail ? Lines.Count : Lines.Count - 1);
            int index = Math.Min(Lines.Count - 1, (int)Math.Floor(coordinate));
            double start = Absolute(Lines[index].Position);
            double end = index + 1 < Lines.Count ? Absolute(Lines[index + 1].Position) : includeTail ? _paragraphStarts[^1] - 1 : start;
            double absolute = start + (end - start) * (coordinate - index);
            int lo = 0, hi = _paragraphLengths.Length - 1, paragraph = 0;
            while (lo <= hi)
            {
                int middle = lo + (hi - lo) / 2;
                if (_paragraphStarts[middle] <= absolute) { paragraph = middle; lo = middle + 1; }
                else hi = middle - 1;
            }
            double offset = Math.Max(0, absolute - _paragraphStarts[paragraph]);
            int whole = Math.Min(_paragraphLengths[paragraph], (int)Math.Floor(offset));
            return new ReadingPosition { Paragraph = paragraph, Offset = whole, Fraction = Math.Clamp(offset - whole, 0, .999999999) };
        }
        public void Build(Book book, Graphics g, Font font, int width)
        {
            Lines.Clear();
            _paragraphStarts = book == null ? Array.Empty<double>() : new double[book.Lines.Count + 1];
            _paragraphLengths = book == null ? Array.Empty<int>() : new int[book.Lines.Count];
            if (book == null) return;
            for (int i = 0; i < book.Lines.Count; i++)
            {
                _paragraphLengths[i] = book.Lines[i].Length;
                _paragraphStarts[i + 1] = _paragraphStarts[i] + book.Lines[i].Length + 1;
            }
            width = Math.Max(1, width);
            using var fmt = Win32.CreateFormat(false);
            for (int paragraph = 0; paragraph < book.Lines.Count; paragraph++)
            {
                string text = book.Lines[paragraph];
                int[] boundaries = StringInfo.ParseCombiningCharacters(text);
                if (boundaries.Length == 0)
                {
                    Lines.Add(new ReadingLine { Text = "", Paragraph = paragraph });
                    continue;
                }
                int start = 0;
                while (start < boundaries.Length)
                {
                    int left = start + 1, right = boundaries.Length, best = start + 1;
                    while (left <= right)
                    {
                        int mid = left + (right - left) / 2;
                        int end = mid < boundaries.Length ? boundaries[mid] : text.Length;
                        float measured = g.MeasureString(text.Substring(boundaries[start], end - boundaries[start]), font, int.MaxValue, fmt).Width;
                        if (measured <= width) { best = mid; left = mid + 1; }
                        else right = mid - 1;
                    }
                    int stop = best < boundaries.Length ? boundaries[best] : text.Length;
                    Lines.Add(new ReadingLine { Text = text.Substring(boundaries[start], stop - boundaries[start]), Paragraph = paragraph, Offset = boundaries[start] });
                    start = best;
                }
            }
        }
        public int IndexOf(ReadingPosition position)
        {
            int lo = 0, hi = Lines.Count - 1, best = 0;
            while (lo <= hi)
            {
                int mid = lo + (hi - lo) / 2;
                var line = Lines[mid];
                if (line.Paragraph < position.Paragraph || (line.Paragraph == position.Paragraph && line.Offset <= position.Offset))
                { best = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            return best;
        }
    }
}
