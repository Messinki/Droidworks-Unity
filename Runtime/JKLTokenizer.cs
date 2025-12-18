using System.Globalization;
using System.IO;
using UnityEngine;

namespace Droidworks.JKL
{
    public class JKLTokenizer
    {
        private string _content;
        private int _cursor;
        private int _length;

        public JKLTokenizer(string filePath)
        {
            _content = File.ReadAllText(filePath);
            _length = _content.Length;
            _cursor = 0;
        }

        public string PeekToken()
        {
            int savedCursor = _cursor;
            string token = GetToken();
            _cursor = savedCursor; // Restore
            return token;
        }

        public string GetToken()
        {
            SkipWhitespace();
            if (_cursor >= _length) return null;

            // Quoted String
            if (_content[_cursor] == '"')
            {
                _cursor++;
                int start = _cursor;
                while (_cursor < _length && _content[_cursor] != '"')
                {
                    _cursor++;
                }
                string token = _content.Substring(start, _cursor - start);
                _cursor++; // Skip closing quote
                return token;
            }

            // Normal Token or Punctuation
            int tokenStart = _cursor;
            while (_cursor < _length && !char.IsWhiteSpace(_content[_cursor]))
            {
                char c = _content[_cursor];
                if (c == ':' || c == ',')
                {
                    if (_cursor == tokenStart)
                    {
                        // Return the punctuation itself as a token
                        _cursor++;
                        return c.ToString();
                    }
                    else
                    {
                        // Return the token preceding the punctuation
                        break;
                    }
                }
                _cursor++;
            }

            return _content.Substring(tokenStart, _cursor - tokenStart);
        }

        public void SkipWhitespace()
        {
            while (_cursor < _length)
            {
                // Skip real whitespace
                while (_cursor < _length && char.IsWhiteSpace(_content[_cursor]))
                {
                    _cursor++;
                }

                // comments
                if (_cursor < _length && _content[_cursor] == '#')
                {
                    // Skip until newline
                    while (_cursor < _length && _content[_cursor] != '\n' && _content[_cursor] != '\r')
                    {
                        _cursor++;
                    }
                }
                else
                {
                    break;
                }
            }
        }

        public string GetRestOfLine()
        {
            int start = _cursor;
            while (_cursor < _length && _content[_cursor] != '\n' && _content[_cursor] != '\r')
            {
                _cursor++;
            }
            string line = _content.Substring(start, _cursor - start).Trim();
            
            // Consume unexpected newlines if any
            if (_cursor < _length && _content[_cursor] == '\r') _cursor++;
            if (_cursor < _length && _content[_cursor] == '\n') _cursor++;
            
            return line;
        }

        public int GetInt()
        {
            string t = GetToken();
            if (t == ":") t = GetToken(); // Skip separators if they appear
            
            if (int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out int val))
            {
                return val;
            }
            return 0;
        }

        public float GetFloat()
        {
            string t = GetToken();
            if (t == ":") t = GetToken();
            if (float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
            {
                return val;
            }
            return 0f;
        }

        public Vector3 GetVector3()
        {
            float x = GetFloat();
            float y = GetFloat();
            float z = GetFloat();
            return new Vector3(x, y, z);
        }
    }
}
