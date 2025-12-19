using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Droidworks.JKL
{
    public class JKLParser
    {
        private JKLTokenizer _tokenizer;

        public JKLModel Parse(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"JKL file not found: {filePath}");
            }

            _tokenizer = new JKLTokenizer(filePath);

            var model = new JKLModel();
            model.Name = Path.GetFileNameWithoutExtension(filePath);

            while (true)
            {
                string token = _tokenizer.GetToken();
                if (string.IsNullOrEmpty(token)) break;

                // Section detection
                // JKL has "SECTION: ..."
                if (token.Equals("SECTION", StringComparison.OrdinalIgnoreCase))
                {
                    string t2 = _tokenizer.GetToken(); // Could be ':' or the section name
                    string sectionName = "";
                    if (t2 == ":")
                    {
                        sectionName = _tokenizer.GetToken();
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
                    string sub = _tokenizer.GetToken();
                    if (sub.Equals("vertices", StringComparison.OrdinalIgnoreCase))
                    {
                        ParseVertices(model);
                    }
                    else if (sub.Equals("texture", StringComparison.OrdinalIgnoreCase))
                    {
                        string sub2 = _tokenizer.GetToken();
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
                    else if (sub.Equals("templates", StringComparison.OrdinalIgnoreCase))
                    {
                        ParseTemplates(model);
                    }
                    else if (sub.Equals("things", StringComparison.OrdinalIgnoreCase))
                    {
                        ParseThings(model);
                    }
                }
            }

            return model;
        }

        private void ParseVertices(JKLModel model)
        {
            int count = _tokenizer.GetInt();
            for (int i = 0; i < count; i++)
            {
                _tokenizer.GetInt(); // index
                // Tokenizer handles ':' separator automatically if present

                float x = _tokenizer.GetFloat();
                float y = _tokenizer.GetFloat();
                float z = _tokenizer.GetFloat();
                
                // RAW Sith Coordinates (Right-Handed Z-up)
                // We will convert to Unity space in the Importer
                model.Vertices.Add(new Vector3(x, y, z)); 
            }
        }

        private void ParseTextureVertices(JKLModel model)
        {
            int count = _tokenizer.GetInt();
            for (int i = 0; i < count; i++)
            {
                _tokenizer.GetInt(); // index

                float u = _tokenizer.GetFloat();
                float v = _tokenizer.GetFloat();
                
                model.TextureVertices.Add(new Vector2(u, v));
            }
        }

        private void ParseMaterials(JKLModel model)
        {
            int count = _tokenizer.GetInt();
            for (int i = 0; i < count; i++)
            {
                // Peek to see if we hit "end" (rare but possible in some formats)
                string token = _tokenizer.PeekToken();
                if (token != null && token.Equals("end", StringComparison.OrdinalIgnoreCase)) 
                {
                    _tokenizer.GetToken(); // consume 'end'
                    break;
                }

                _tokenizer.GetInt(); // 0
                
                // CONSUME COLON IF PRESENT
                string next = _tokenizer.PeekToken();
                if (next == ":")
                {
                    _tokenizer.GetToken(); 
                }
                
                string file = _tokenizer.GetToken();
                float xTile = _tokenizer.GetFloat();
                float yTile = _tokenizer.GetFloat();

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
            int count = _tokenizer.GetInt(); // 0: mat ...

            for (int i = 0; i < count; i++)
            {
                _tokenizer.GetInt(); // index

                int matIdx = _tokenizer.GetInt(); 

                // Skip 7 fields
                for (int k = 0; k < 7; k++) _tokenizer.GetToken();

                int nMsgVerts = _tokenizer.GetInt(); 

                var surface = new JKLSurface();
                surface.MaterialIndex = matIdx;

                for (int k = 0; k < nMsgVerts; k++)
                {
                    // v,tv 
                    // Tokenizer will split "0,1" into "0", ",", "1"
                    
                    int vIdx = _tokenizer.GetInt();
                    
                    // Check for comma
                    string next = _tokenizer.PeekToken();
                    int tvIdx = 0;
                    
                    if (next == ",")
                    {
                        _tokenizer.GetToken(); // consume comma
                        tvIdx = _tokenizer.GetInt();
                    }
                    
                    surface.VertexIndices.Add(vIdx);
                    surface.TextureVertexIndices.Add(tvIdx);
                }
                
                // Skip Intensities 
                for (int k = 0; k < nMsgVerts; k++) _tokenizer.GetFloat();

                model.Surfaces.Add(surface);
            }
        }
        private void ParseTemplates(JKLModel model)
        {
            int count = _tokenizer.GetInt();
            // Debug.Log($"Parsing {count} Templates");
            
            for (int i = 0; i < count; i++)
            {
                string name = _tokenizer.GetToken();
                string basedOn = _tokenizer.GetToken();
                string paramsStr = _tokenizer.GetRestOfLine();

                var tmpl = new JKLTemplate { Name = name, BasedOn = basedOn };

                // Parse params simple kv parsing
                var parts = paramsStr.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var p in parts)
                {
                    if (p.Contains("="))
                    {
                        var kv = p.Split(new char[] { '=' }, 2);
                        if (kv.Length == 2)
                        {
                            string key = kv[0].ToLower().Trim();
                            string val = kv[1].Trim();
                            tmpl.Params[key] = val;
                            if (key == "model3d") tmpl.Model3D = val;
                        }
                    }
                }
                model.Templates[name] = tmpl;
            }
        }

        private void ParseThings(JKLModel model)
        {
            int count = _tokenizer.GetInt();
            // Debug.Log($"Parsing {count} Things (Reported)");
            
            // Loop up to count, but check for early break or 'end'
            // The JKL parser often over-reports count by 200 or so.
            for (int i = 0; i < count; i++)
            {
                // Peek to check for termination
                string next = _tokenizer.PeekToken();
                if (string.IsNullOrEmpty(next) || next.Equals("end", StringComparison.OrdinalIgnoreCase) || next.Equals("SECTION", StringComparison.OrdinalIgnoreCase))
                {
                    if(next != null && next.Equals("end", StringComparison.OrdinalIgnoreCase)) _tokenizer.GetToken(); // Consume 'end'
                    break;
                }

                string idxStr = _tokenizer.GetToken();
                // Double check if get_token returned end (if peek didn't catch it?)
                if (idxStr.Equals("end", StringComparison.OrdinalIgnoreCase)) break;

                int idx = i;
                if (int.TryParse(idxStr, out int pIdx)) idx = pIdx;

                string tmplName = "";
                
                // Handle optional colon
                if (_tokenizer.PeekToken() == ":")
                {
                    _tokenizer.GetToken(); // consume :
                    tmplName = _tokenizer.GetToken();
                }
                else
                {
                    tmplName = _tokenizer.GetToken();
                }

                string name = _tokenizer.GetToken();
                
                float x = _tokenizer.GetFloat();
                float y = _tokenizer.GetFloat();
                float z = _tokenizer.GetFloat();
                
                float p = _tokenizer.GetFloat();
                float yaw = _tokenizer.GetFloat();
                float r = _tokenizer.GetFloat();
                
                int sector = _tokenizer.GetInt();
                
                string extras = _tokenizer.GetRestOfLine();

                var thing = new JKLThing
                {
                    Index = idx,
                    TemplateName = tmplName,
                    Name = name,
                    Position = new Vector3(x, y, z),
                    Rotation = new Vector3(p, yaw, r),
                    SectorIndex = sector,
                    Extras = extras
                };
                
                model.Things.Add(thing);
            }
        }
    }
}
