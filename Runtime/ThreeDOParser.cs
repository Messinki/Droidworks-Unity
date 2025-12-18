using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using UnityEngine;

namespace Droidworks.ThreeDO
{
    public class ThreeDOModel
    {
        public string Name;
        public List<string> Materials = new List<string>();
        public List<ThreeDOMesh> Meshes = new List<ThreeDOMesh>();
        public List<ThreeDONode> Nodes = new List<ThreeDONode>();
    }

    public class ThreeDOMesh
    {
        public string Name;
        public List<Vector3> Vertices = new List<Vector3>();
        public List<Vector2> UVs = new List<Vector2>();
        public List<ThreeDOFace> Faces = new List<ThreeDOFace>();
    }

    public class ThreeDOFace
    {
        public int MaterialIndex;
        public List<int> VertexIndices = new List<int>();
        public List<int> UVIndices = new List<int>();
        public Vector3 Normal;
    }

    public class ThreeDONode
    {
        public int Index;
        public string Name;
        public int MeshIndex;
        public int ParentIndex;
        public Vector3 Position;
        public Vector3 Rotation;
        public Vector3 Pivot;
        
        public int SiblingIndex;
        public int FirstChildIndex;
    }

    public class ThreeDOParser
    {
        private string _content;
        private int _cursor;
        private int _length;

        public ThreeDOModel Parse(string filePath)
        {
            if (!File.Exists(filePath)) throw new FileNotFoundException(filePath);

            _content = File.ReadAllText(filePath);
            _length = _content.Length;
            _cursor = 0;

            var model = new ThreeDOModel();
            model.Name = Path.GetFileNameWithoutExtension(filePath);

            while (true)
            {
                string token = GetToken();
                if (string.IsNullOrEmpty(token)) break;

                if (token.Equals("3DO", StringComparison.OrdinalIgnoreCase))
                {
                    // Version
                    GetFloat(); 
                }
                else if (token.Equals("MODELRESOURCE", StringComparison.OrdinalIgnoreCase))
                {
                    ParseResource(model);
                }
                else if (token.Equals("GEOMETRYDEF", StringComparison.OrdinalIgnoreCase))
                {
                    ParseGeometry(model);
                }
                else if (token.Equals("HIERARCHYDEF", StringComparison.OrdinalIgnoreCase))
                {
                    ParseHierarchy(model);
                }
            }
            return model;
        }

        private void ParseResource(ThreeDOModel model)
        {
             string t = GetToken();
             if (t.Equals("MATERIALS", StringComparison.OrdinalIgnoreCase))
             {
                 int count = GetInt();
                 for(int i=0; i<count; i++)
                 {
                     int idx = GetInt(); // Index
                     // "0: matname"
                     // Tokenizer checks: if 0 ends with :, nice.
                     // GetInt handles trailing colon.
                     
                     // Next is name.
                     // Might be consumed if colon was separate?
                     // My GetInt consumes token.
                     // If file has "0: matname", GetInt reads "0". Next is "matname".
                     // If file has "0 : matname", GetInt reads "0". Next is ":".
                     
                     // Let's peek? Or simply loop.
                     string name = GetToken();
                     if (name == ":") name = GetToken();
                     
                     model.Materials.Add(name);
                 }
             }
        }

        private void ParseGeometry(ThreeDOModel model)
        {
            // RADIUS -> INSERT OFFSET -> GEOSETS
            while(true)
            {
                string t = GetToken();
                if (string.IsNullOrEmpty(t)) break;
                if (t.Equals("GEOSETS", StringComparison.OrdinalIgnoreCase))
                {
                    int count = GetInt();
                    for(int i=0; i<count; i++) ParseGeoset(model);
                    break;
                }
                else if (t.Equals("RADIUS", StringComparison.OrdinalIgnoreCase)) GetFloat();
                else if (t.Equals("INSERT", StringComparison.OrdinalIgnoreCase))
                {
                    GetToken(); // OFFSET
                    GetVector3();
                }
            }
        }

        private void ParseGeoset(ThreeDOModel model)
        {
            GetToken(); // GEOSET
            GetInt(); // Index
            GetToken(); // MESHES
            int count = GetInt();
            for(int i=0; i<count; i++) ParseMesh(model);
        }

        private void ParseMesh(ThreeDOModel model)
        {
            GetToken(); // MESH
            GetInt(); // Index
            GetToken(); // NAME
            string name = GetToken(); // MeshName

            var mesh = new ThreeDOMesh { Name = name };
            
            int numVerts = 0;
            int numFaces = 0;

            while(true)
            {
                string t = GetToken();
                if (string.IsNullOrEmpty(t)) break;
                
                if (t.Equals("VERTICES", StringComparison.OrdinalIgnoreCase))
                {
                    numVerts = GetInt();
                    for(int i=0; i<numVerts; i++)
                    {
                        GetInt(); // idx
                        string sep = GetToken(); 
                        if (sep != ":") { /* assume separator? */ }
                        
                        mesh.Vertices.Add(GetVector3()); // x, y, z
                        GetFloat(); // Intensity
                    }
                }
                else if (t.Equals("TEXTURE", StringComparison.OrdinalIgnoreCase))
                {
                    GetToken(); // VERTICES
                    int count = GetInt();
                    for(int i=0; i<count; i++)
                    {
                        GetInt(); 
                        GetToken(); // :
                        float u = GetFloat();
                        float v = GetFloat();
                        mesh.UVs.Add(new Vector2(u, v));
                    }
                }
                else if (t.Equals("FACES", StringComparison.OrdinalIgnoreCase))
                {
                    numFaces = GetInt();
                    for(int i=0; i<numFaces; i++)
                    {
                        GetInt(); // idx
                        GetToken(); // :
                        
                        int matIdx = GetInt();
                        GetInt(); // type
                        GetInt(); // geo
                        GetInt(); // light
                        GetInt(); // tex
                        // extra might be float?
                        GetFloat(); 

                        int nVerts = GetInt();
                        var face = new ThreeDOFace { MaterialIndex = matIdx };
                        
                        for(int k=0; k<nVerts; k++)
                        {
                            int vIdx = GetInt();
                            face.VertexIndices.Add(vIdx);
                            
                            if (PeekChar() == ',')
                            {
                                GetToken(); // Consume comma
                                int uvIdx = GetInt();
                                face.UVIndices.Add(uvIdx);
                            }
                            else
                            {
                                face.UVIndices.Add(0);
                            }
                        }
                        mesh.Faces.Add(face);
                    }
                }
                else if (t.Equals("FACE", StringComparison.OrdinalIgnoreCase)) // FACE NORMALS
                {
                    GetToken(); // NORMALS
                    for(int i=0; i<numFaces; i++)
                    {
                         GetInt(); GetToken(); 
                         GetVector3(); // Normal
                    }
                    break; // End of mesh
                }
                else if (t.Equals("radius", StringComparison.OrdinalIgnoreCase)) GetFloat();
                else if (t.Equals("geometrymode", StringComparison.OrdinalIgnoreCase)) GetInt();
                else if (t.Equals("lightingmode", StringComparison.OrdinalIgnoreCase)) GetInt();
                else if (t.Equals("texturemode", StringComparison.OrdinalIgnoreCase)) GetInt();
                else if (t.Equals("vertex", StringComparison.OrdinalIgnoreCase)) // VERTEX NORMALS
                {
                     GetToken(); // NORMALS
                     for(int i=0; i<numVerts; i++)
                     {
                         GetInt(); GetToken(); GetVector3(); 
                     }
                }
            }
            model.Meshes.Add(mesh);
        }

        private void ParseHierarchy(ThreeDOModel model)
        {
            GetToken(); // HIERARCHY
            GetToken(); // NODES
            int count = GetInt();
            for(int i=0; i<count; i++)
            {
                int idx = GetInt();
                GetToken(); // :
                
                int flags = GetInt();
                int type = GetInt();
                int meshIdx = GetInt();
                int parentIdx = GetInt();
                int childIdx = GetInt();
                int siblingIdx = GetInt();
                int numChildren = GetInt();
                
                Vector3 pos = GetVector3();
                Vector3 rot = GetVector3();
                Vector3 piv = GetVector3();
                string name = GetToken();
                
                model.Nodes.Add(new ThreeDONode
                {
                    Index = idx,
                    Name = name,
                    MeshIndex = meshIdx,
                    ParentIndex = parentIdx,
                    SiblingIndex = siblingIdx,
                    FirstChildIndex = childIdx,
                    Position = pos,
                    Rotation = rot,
                    Pivot = piv
                });
            }
        }

        // --- Helpers ---

        private string GetToken()
        {
            SkipWhitespace();
            if (_cursor >= _length) return null;
            
            // Handle punctuation
            char first = _content[_cursor];
            if (first == ':' || first == ',')
            {
                _cursor++;
                return first.ToString();
            }

            int start = _cursor;
            while (_cursor < _length && !char.IsWhiteSpace(_content[_cursor]))
            {
                if (_content[_cursor] == ':' || _content[_cursor] == ',') break;
                _cursor++;
            }
            return _content.Substring(start, _cursor - start);
        }

        private char PeekChar()
        {
            SkipWhitespace();
            if (_cursor >= _length) return '\0';
            return _content[_cursor];
        }

        private void SkipWhitespace()
        {
            while (_cursor < _length)
            {
                char c = _content[_cursor];
                if (c == '#') { while (_cursor < _length && _content[_cursor] != '\n') _cursor++; }
                else if (char.IsWhiteSpace(c)) _cursor++;
                else break;
            }
        }

        private int GetInt()
        {
            string s = GetToken();
            if (s == ":") s = GetToken(); // Skip preceding colon if any
            
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return int.Parse(s.Substring(2), System.Globalization.NumberStyles.HexNumber);
            }
            return int.Parse(s);
        }

        private float GetFloat()
        {
            string s = GetToken();
            if (s == ":") s = GetToken();
            return float.Parse(s, CultureInfo.InvariantCulture);
        }

        private Vector3 GetVector3()
        {
            float x = GetFloat();
            float y = GetFloat();
            float z = GetFloat();
            return new Vector3(x, y, z); // Import raw, swap in Importer
        }
    }
}
