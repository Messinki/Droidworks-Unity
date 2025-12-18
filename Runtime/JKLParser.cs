using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Droidworks.JKL
{
    public class JKLParser
    {
        private string _content;
        private int _cursor;
        private int _length;

        public JKLModel Parse(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"JKL file not found: {filePath}");
            }

            _content = File.ReadAllText(filePath);
            _length = _content.Length;
            _cursor = 0;

            var model = new JKLModel();
            model.Name = Path.GetFileNameWithoutExtension(filePath);

            while (true)
            {
                string token = GetToken();
                if (string.IsNullOrEmpty(token)) break;

                // Section detection
                // JKL has "SECTION: ..."
                if (token.Equals("SECTION", StringComparison.OrdinalIgnoreCase))
                {
                    string t2 = GetToken(); // Could be ':' or the section name
                    string sectionName = "";
                    if (t2 == ":")
                    {
                        sectionName = GetToken();
                    }
                    else
                    {
                        sectionName = t2;
                    }
                    
                    // We don't explicitly need to do anything with SECTION headers 
                    // because we scan for "World vertices" etc anyway.
                }
                
                // Top level keywords often appearing inside sections
                // e.g. "World vertices 123"
                if (token.Equals("World", StringComparison.OrdinalIgnoreCase))
                {
                    string sub = GetToken();
                    if (sub.Equals("vertices", StringComparison.OrdinalIgnoreCase))
                    {
                        ParseVertices(model);
                    }
                    else if (sub.Equals("texture", StringComparison.OrdinalIgnoreCase))
                    {
                        string sub2 = GetToken();
                        if (sub2.Equals("vertices", StringComparison.OrdinalIgnoreCase))
                        {
                            ParseTextureVertices(model);
                        }
                    }
                    else if (sub.Equals("materials", StringComparison.OrdinalIgnoreCase))
                    {
                        ParseMaterials(model);
                    }
                    else if (sub.Equals("surfaces", StringComparison.OrdinalIgnoreCase))
                    {
                        ParseSurfaces(model);
                    }
                }
            }

            return model;
        }

        private void ParseVertices(JKLModel model)
        {
            int count = GetInt();
            for (int i = 0; i < count; i++)
            {
                string idxToken = GetToken();
                if (!idxToken.EndsWith(":")) PeekTokenAndConsume(":");

                float x = GetFloat();
                float y = GetFloat();
                float z = GetFloat();
                
                // RAW Sith Coordinates (Right-Handed Z-up)
                // We will convert to Unity space in the Importer
                model.Vertices.Add(new Vector3(x, y, z)); 
            }
        }

        private void ParseTextureVertices(JKLModel model)
        {
            int count = GetInt();
            for (int i = 0; i < count; i++)
            {
                string idxToken = GetToken();
                if (!idxToken.EndsWith(":")) PeekTokenAndConsume(":");

                float u = GetFloat();
                float v = GetFloat();
                
                model.TextureVertices.Add(new Vector2(u, v));
            }
        }

        private void ParseMaterials(JKLModel model)
        {
            int count = GetInt();
            for (int i = 0; i < count; i++)
            {
                // Index check
                string token = GetToken();
                if (token.Equals("end", StringComparison.OrdinalIgnoreCase)) break;

                // Handle index 0:
                if (!token.EndsWith(":")) PeekTokenAndConsume(":");
                
                string file = GetToken();
                float xTile = GetFloat();
                float yTile = GetFloat();

                model.Materials.Add(new JKLMaterial 
                { 
                    Name = file, 
                    XTile = xTile, 
                    YTile = yTile 
                });
            }
        }

        private void ParseSurfaces(JKLModel model)
        {
            int count = GetInt(); // 0: mat ...

            for (int i = 0; i < count; i++)
            {
                string idxToken = GetToken();
                if (!idxToken.EndsWith(":")) PeekTokenAndConsume(":");

                int matIdx = GetInt(); 

                // Skip 7 fields
                for (int k = 0; k < 7; k++) GetToken();

                int nMsgVerts = GetInt(); 

                var surface = new JKLSurface();
                surface.MaterialIndex = matIdx;

                for (int k = 0; k < nMsgVerts; k++)
                {
                    // v,tv 
                    string pair = GetToken();
                    string[] parts = pair.Split(',');
                    
                    int vIdx = int.Parse(parts[0]);
                    int tvIdx = (parts.Length > 1) ? int.Parse(parts[1]) : 0;

                    surface.VertexIndices.Add(vIdx);
                    surface.TextureVertexIndices.Add(tvIdx);
                }
                
                // Skip Intensities 
                for (int k = 0; k < nMsgVerts; k++) GetFloat();

                model.Surfaces.Add(surface);
            }
        }

        // --- Tokenizer Helpers ---

        private string GetToken()
        {
            SkipWhitespace();
            if (_cursor >= _length) return null;

            int start = _cursor;
            while (_cursor < _length && !char.IsWhiteSpace(_content[_cursor]))
            {
                _cursor++;
            }
            return _content.Substring(start, _cursor - start);
        }

        private void SkipWhitespace()
        {
            while (_cursor < _length)
            {
                char c = _content[_cursor];
                // Skip comments # ...
                if (c == '#')
                {
                    while (_cursor < _length && _content[_cursor] != '\n') _cursor++;
                }
                else if (char.IsWhiteSpace(c))
                {
                    _cursor++;
                }
                else
                {
                    break;
                }
            }
        }
        
        private void PeekTokenAndConsume(string expected)
        {
            // Simple hack: read next token, if matches consume, else backtrack?
            // Actually JKL is rigid enough. 
            // If we are at "0", next char might be ":"
            // My GetToken splits at whitespace. So "0:" is one token? Yes.
            // If "0 :", then "0", ":"
            // Let's rely on ParseVertices handling "0:" or "0".
            // Implement simple check:
            int savedCursor = _cursor;
            string t = GetToken();
            if (t != expected)
            {
                _cursor = savedCursor; // Backtrack
            }
        }

        private int GetInt()
        {
            string s = GetToken();
            if (s != null && s.EndsWith(":")) s = s.Substring(0, s.Length - 1);
            return int.Parse(s);
        }

        private float GetFloat()
        {
            string s = GetToken();
            // Handle scientific notation etc if needed, but float.Parse usually fine
            return float.Parse(s, CultureInfo.InvariantCulture);
        }
    }
}
