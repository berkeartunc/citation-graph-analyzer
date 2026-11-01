using System.Globalization;
using System.Text;

namespace CitationGraph.Core
{
    // A small forward-only JSON reader. It reads the text character by character
    // and only supports what ArticleJsonReader needs: strings, numbers, arrays,
    // objects and skipping values that are not used.
    // It is a struct, so it is passed with "ref" to keep one shared position.
    internal struct MiniJsonReader
    {
        private readonly string _s;
        private int _i;

        public MiniJsonReader(string s)
        {
            _s = s ?? throw new ArgumentNullException(nameof(s));
            _i = 0;
        }

        public char Peek() => _i < _s.Length ? _s[_i] : '\0';

        public char PeekNonWhitespace()
        {
            int j = _i;
            while (j < _s.Length && char.IsWhiteSpace(_s[j])) j++;
            return j < _s.Length ? _s[j] : '\0';
        }

        public void SkipWhitespace()
        {
            while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
        }

        public bool TryConsume(char ch)
        {
            SkipWhitespace();
            if (Peek() == ch)
            {
                _i++;
                return true;
            }
            return false;
        }

        public void Expect(char ch)
        {
            SkipWhitespace();
            if (Peek() != ch)
                throw new FormatException($"JSON hatası: '{ch}' bekleniyordu ama '{Peek()}' geldi (konum {_i}).");
            _i++;
        }

        public string ReadString()
        {
            SkipWhitespace();
            if (Peek() != '"')
                throw new FormatException($"JSON hatası: metin bekleniyordu (konum {_i}).");
            _i++; // opening quote

            var sb = new StringBuilder();
            while (_i < _s.Length)
            {
                char c = _s[_i++];
                if (c == '"')
                    return sb.ToString();

                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }

                if (_i >= _s.Length)
                    throw new FormatException("JSON hatası: kaçış karakteri yarım kaldı.");

                char e = _s[_i++];
                sb.Append(e switch
                {
                    '"' => '"',
                    '\\' => '\\',
                    '/' => '/',
                    'b' => '\b',
                    'f' => '\f',
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    'u' => ReadUnicode4(),
                    _ => e
                });
            }

            throw new FormatException("JSON hatası: metin kapanmadan dosya bitti.");
        }

        // Reads the 4 hex digits after "\u"
        private char ReadUnicode4()
        {
            if (_i + 4 > _s.Length)
                throw new FormatException("JSON hatası: eksik unicode kaçışı.");

            int code = int.Parse(_s.AsSpan(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            _i += 4;
            return (char)code;
        }

        // Reads a string, number, true/false or null as text. Null becomes "".
        // Used for fields like id and year that can be written in different ways.
        public string ReadStringOrNumberAsString()
        {
            SkipWhitespace();
            char c = Peek();

            if (c == '"') return ReadString();
            if (c == '-' || char.IsDigit(c)) return ReadNumberRaw();

            if (StartsWith("null")) { _i += 4; return ""; }
            if (StartsWith("true")) { _i += 4; return "true"; }
            if (StartsWith("false")) { _i += 5; return "false"; }

            return ReadBareToken();
        }

        // Reads an array of strings. Items that are not strings are skipped.
        // If the value is not an array, it is skipped and an empty list is returned.
        public List<string> ReadStringArrayOrEmpty()
        {
            SkipWhitespace();
            if (Peek() != '[')
            {
                SkipValue();
                return new List<string>();
            }

            Expect('[');

            var list = new List<string>();
            if (TryConsume(']')) return list;

            while (true)
            {
                SkipWhitespace();
                if (Peek() == '"')
                    list.Add(ReadString());
                else
                    SkipValue();

                if (TryConsume(']')) break;
                Expect(',');
            }

            return list;
        }

        public void SkipValue()
        {
            SkipWhitespace();
            char c = Peek();

            if (c == '{') SkipObject();
            else if (c == '[') SkipArray();
            else if (c == '"') ReadString();
            else ReadBareToken(); // number, true, false or null
        }

        private void SkipObject()
        {
            Expect('{');
            if (TryConsume('}')) return;

            while (true)
            {
                ReadString();
                Expect(':');
                SkipValue();
                if (TryConsume('}')) break;
                Expect(',');
            }
        }

        private void SkipArray()
        {
            Expect('[');
            if (TryConsume(']')) return;

            while (true)
            {
                SkipValue();
                if (TryConsume(']')) break;
                Expect(',');
            }
        }

        private string ReadNumberRaw()
        {
            int start = _i;
            if (Peek() == '-') _i++;

            while (_i < _s.Length && char.IsDigit(_s[_i])) _i++;

            if (_i < _s.Length && _s[_i] == '.')
            {
                _i++;
                while (_i < _s.Length && char.IsDigit(_s[_i])) _i++;
            }

            if (_i < _s.Length && (_s[_i] == 'e' || _s[_i] == 'E'))
            {
                _i++;
                if (_i < _s.Length && (_s[_i] == '+' || _s[_i] == '-')) _i++;
                while (_i < _s.Length && char.IsDigit(_s[_i])) _i++;
            }

            return _s.Substring(start, _i - start);
        }

        // Reads until the next separator. Used for true, false, null and unexpected tokens.
        private string ReadBareToken()
        {
            SkipWhitespace();
            int start = _i;

            while (_i < _s.Length)
            {
                char c = _s[_i];
                if (char.IsWhiteSpace(c) || c == ',' || c == ']' || c == '}' || c == ':')
                    break;
                _i++;
            }

            return _s.Substring(start, _i - start);
        }

        private bool StartsWith(string token) =>
            string.CompareOrdinal(_s, _i, token, 0, token.Length) == 0;
    }
}
