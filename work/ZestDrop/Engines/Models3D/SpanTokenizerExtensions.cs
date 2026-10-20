using System;

namespace ZestDrop.Engines.Models3D;

internal static class SpanTokenizerExtensions
{
    public static SpanSplitAnyEnumerator SplitAny(this ReadOnlySpan<char> span, char sep1, char sep2)
    {
        return new SpanSplitAnyEnumerator(span, sep1, sep2);
    }

    public ref struct SpanSplitAnyEnumerator
    {
        private ReadOnlySpan<char> _span;
        private readonly char _sep1;
        private readonly char _sep2;
        private Range _current;
        private int _offset;

        public SpanSplitAnyEnumerator(ReadOnlySpan<char> span, char sep1, char sep2)
        {
            _span = span;
            _sep1 = sep1;
            _sep2 = sep2;
            _current = default;
            _offset = 0;
        }

        public readonly Range Current => _current;

        public readonly SpanSplitAnyEnumerator GetEnumerator() => this;

        public bool MoveNext()
        {
            if (_span.IsEmpty)
                return false;

            int index = -1;
            for (int i = 0; i < _span.Length; i++)
            {
                char c = _span[i];
                if (c == _sep1 || c == _sep2)
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                _current = new Range(_offset, _offset + _span.Length);
                _offset += _span.Length;
                _span = ReadOnlySpan<char>.Empty;
                return true;
            }

            _current = new Range(_offset, _offset + index);
            _offset += index + 1;
            _span = _span.Slice(index + 1);
            return true;
        }
    }
}
