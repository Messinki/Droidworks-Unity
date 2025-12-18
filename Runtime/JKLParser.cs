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
            // Format: Count \n index: x y z
            int count = GetInt();
            
            for (int i = 0; i < count; i++)
            {
                // Index might be "0:" or "0" then ":"
                string idxToken = GetToken();
                if (!idxToken.EndsWith(":"))
                {
                    // Check if next token is colon
                    // But our GetToken might skip whitespace.
                    // The standard JKL is "0: 1.0 2.0 3.0"
                    // If GetToken returns "0", the next one should be ":"
                    // We can just consume it if it is a colon.
                    PeekTokenAndConsume(":"); 
                }

                float x = GetFloat();
                float y = GetFloat();
                float z = GetFloat();
                
                // JKL is typically Z-up? Unity is Y-up.
                // Standard conversion: Unity (x, y, z) = JKL (x, z, y) usually?
                // Or maybe (x, -z, y)? 
                // Let's stick to raw coordinates first, then adjust in Importer if needed.
                // The Python script did: Vector3(x, y, z).to_blender() -> (x, y, z).
                // Blender is Z-up. Unity is Y-up.
                // So (x, y, z) in JKL (Z-up) -> (x, z, y) in Unity.
                
                model.Vertices.Add(new Vector3(x, z, y)); 
            }
        }

        private void ParseSurfaces(JKLModel model)
        {
            // Format: Count \n 0: mat flags ... 
            int count = GetInt();

            for (int i = 0; i < count; i++)
            {
                 // Index
                string idxToken = GetToken();
                if (!idxToken.EndsWith(":")) PeekTokenAndConsume(":");

                int matIdx = GetInt(); // Material Index

                // Skip 7 fields: surf, face, geo, light, tex, adjoin, extra
                for (int k = 0; k < 7; k++) GetToken();

                int nMsgVerts = GetInt(); // Number of vertices in face

                var surface = new JKLSurface();
                surface.MaterialIndex = matIdx;

                for (int k = 0; k < nMsgVerts; k++)
                {
                    // Vertex format in surface: vertIdx, texVertIdx
                    // Can be "0, 0" or "0,0"
                    // GetInt stops at comma?
                    
                    // My simple GetToken splits by whitespace. 
                    // If "0,0" is one token, we need to split it.
                    string pair = GetToken();
                    string[] parts = pair.Split(',');
                    
                    int vIdx = int.Parse(parts[0]);
                    // int tvIdx = int.Parse(parts[1]); // Ignore texture idx for now

                    surface.VertexIndices.Add(vIdx);
                }
                
                // Skip Intensities (nMsgVerts floats)
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
