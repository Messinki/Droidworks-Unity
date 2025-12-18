using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Droidworks.ThreeDO
{
    public class ThreeDOParser
    {
        private string _content;
        private int _cursor;
        private int _length;
        private ThreeDOModel _model;

        public ThreeDOParser(string filePath)
        {
            if (File.Exists(filePath))
            {
                _content = File.ReadAllText(filePath);
                _cursor = 0;
                _length = _content.Length;
            }
        }
        
        public ThreeDOParser(string content, string name)
        {
            _content = content;
            _cursor = 0;
            _length = _content.Length;
        }

        public ThreeDOModel Parse()
        {
            if (string.IsNullOrEmpty(_content)) return null;

            _model = new ThreeDOModel();
            
            // Basic loop
            while(true)
            {
                string token = GetToken();
                if (string.IsNullOrEmpty(token)) break;

                string upper = token.ToUpper();

                if (upper == "3DO")
                {
                    GetFloat(); // Version
                }
                else if (upper == "MODELRESOURCE")
                {
                    ParseResource();
                }
                else if (upper == "GEOMETRYDEF")
                {
                    ParseGeometry();
                }
                else if (upper == "HIERARCHYDEF")
                {
                    ParseHierarchy();
                }
            }

            return _model;
        }

        private void ParseResource()
        {
            // Expect MATERIALS
            string t = GetToken();
            if (t != null && t.ToUpper() == "MATERIALS")
            {
                int count = GetInt();
                for (int i = 0; i < count; i++)
                {
                    GetInt(); // Index
                    ConsumeColon();
                    string val = GetToken();
                    _model.Materials.Add(val);
                }
            }
        }

        private void ParseGeometry()
        {
            while(true)
            {
                // Peek next token without consuming if possible? 
                // We don't have Peek. But we can GetToken and dispatch.
                // If it's not a keyword, it's problematic if we are inside a loop with a break condition.
                // But structure is strict.
                
                int startCursor = _cursor;
                string t = GetToken();
                if (string.IsNullOrEmpty(t)) break;
                string u = t.ToUpper();

                if (u == "RADIUS") GetFloat();
                else if (u == "INSERT")
                {
                    GetToken(); // OFFSET
                    GetVector3();
                }
                else if (u == "GEOSETS")
                {
                    int count = GetInt();
                    for(int i=0; i<count; i++) ParseGeoset();
                    break; // Done with geometry def after geosets usually
                }
                else if (u == "HIERARCHYDEF")
                {
                    // Oops, we consumed header for next section. Backtrack or handle?
                    // Backtracking is hard.
                    // But usually GeometryDef ends exactly when Geosets end.
                    // If we are here, we might have over-read. 
                    // Let's rely on Geosets loop finishing.
                    // If 'break' was hit after Geosets loop, we return.
                }
            }
        }

        private void ParseGeoset()
        {
            GetToken(); // GEOSET
            GetInt(); // Index
            GetToken(); // MESHES
            int count = GetInt();
            for(int i=0; i<count; i++) ParseMesh();
        }

        private void ParseMesh()
        {
            GetToken(); // MESH
            int idx = GetInt();
            GetToken(); // NAME
            string name = GetToken();
            
            var mesh = new ThreeDOMesh();
            mesh.Name = name;
            mesh.Index = idx;
            
            int numVerts = 0;
            int numFaces = 0;

            while(true)
            {
                string t = GetToken();
                if (string.IsNullOrEmpty(t)) break;
                string u = t.ToUpper();

                if (u == "RADIUS") GetFloat();
                else if (u == "GEOMETRYMODE") GetInt();
                else if (u == "LIGHTINGMODE") GetInt();
                else if (u == "TEXTUREMODE") GetInt();
                else if (u == "VERTICES")
                {
                    numVerts = GetInt();
                    for(int i=0; i<numVerts; i++)
                    {
                        GetInt(); // idx
                        ConsumeColon(); 
                        mesh.Vertices.Add(GetVector3());
                        GetFloat(); // intensity
                    }
                }
                else if (u == "TEXTURE") // TEXTURE VERTICES
                {
                    GetToken(); // VERTICES
                    int count = GetInt();
                    for(int i=0; i<count; i++)
                    {
                        GetInt(); // idx
                        ConsumeColon();
                        // 3DO UVs are 2 floats (u v)
                        float u_val = GetFloat();
                        float v_val = GetFloat();
                        mesh.UVs.Add(new Vector2(u_val, v_val)); 
                    }
                }
                else if (u == "VERTEX") // VERTEX NORMALS
                {
                    GetToken(); // NORMALS
                    // Usually matches numVerts, NO count token
                    for(int i=0; i<numVerts; i++)
                    {
                        GetInt();
                        ConsumeColon();
                        GetVector3(); 
                    }
                }
                else if (u == "FACES")
                {
                    numFaces = GetInt();
                    for(int i=0; i<numFaces; i++)
                    {
                        GetInt(); // idx
                        ConsumeColon();
                        
                        var face = new ThreeDOFace();
                        face.MaterialIndex = GetInt();
                        GetInt(); // type
                        GetInt(); // geo
                        GetInt(); // light
                        GetInt(); // tex
                        GetFloat(); // extra
                        
                        int vCount = GetInt();
                        face.VertexIndices = new int[vCount];
                        face.UVIndices = new int[vCount];
                        
                        for(int v=0; v<vCount; v++)
                        {
                           face.VertexIndices[v] = GetInt();
                           GetToken(); // comma or just whitespace separator? 
                           // Format is usually: v1,uv1 v2,uv2...
                           // The comma IS a token in my tokenizer.
                           // So we check if next token is comma, if so skip it?
                           // Or GetInt() reads v1. Next GetToken() is comma.
                           // Next GetInt() reads uv1.
                           
                           face.UVIndices[v] = GetInt();
                           // Wait, if next was comma, GetInt() would fail if not stripping.
                           // Logic:
                           // v_idx = GetInt();
                           // t = GetToken(); if(t==",") ...
                           // uv_idx = GetInt();
                        }
                        mesh.Faces.Add(face);
                    }
                }
                else if (u == "FACE") // FACE NORMALS
                {
                    GetToken(); // NORMALS
                    for(int i=0; i<numFaces; i++)
                    {
                        GetInt();
                        ConsumeColon();
                        GetVector3();
                    }
                    break; // End of mesh
                }
            }
            _model.Meshes.Add(mesh);
        }
        
        private void ParseHierarchy()
        {
            GetToken(); // HIERARCHY
            GetToken(); // NODES
            int count = GetInt();
            
            for(int i=0; i<count; i++)
            {
                var n = new ThreeDONode();
                n.Index = GetInt();
                ConsumeColon();
                
                GetInt(); // flags
                GetInt(); // type
                n.MeshIndex = GetInt();
                n.ParentIndex = GetInt();
                n.ChildIndex = GetInt();
                n.SiblingIndex = GetInt();
                GetInt(); // num children
                
                n.Position = GetVector3();
                n.Rotation = GetVector3(); // Pitch, Yaw, Roll
                n.Pivot = GetVector3();
                n.Name = GetToken();
                
                _model.Nodes.Add(n);
            }
        }

        // --- Helpers ---

        private string GetToken()
        {
            SkipWhitespace();
            if (_cursor >= _length) return null;

            if (_content[_cursor] == '"')
            {
                _cursor++;
                int start = _cursor;
                while (_cursor < _length && _content[_cursor] != '"') _cursor++;
                string s = _content.Substring(start, _cursor - start);
                _cursor++;
                return s;
            }

            int sIdx = _cursor;
            while (_cursor < _length && !char.IsWhiteSpace(_content[_cursor]))
            {
                char c = _content[_cursor];
                if (c == ':' || c == ',')
                {
                    if (_cursor == sIdx)
                    {
                        _cursor++;
                        return c.ToString();
                    }
                    else break;
                }
                _cursor++;
            }
            return _content.Substring(sIdx, _cursor - sIdx);
        }

        private void SkipWhitespace()
        {
            while (_cursor < _length)
            {
                char c = _content[_cursor];
                if (char.IsWhiteSpace(c))
                {
                    _cursor++;
                }
                else if (c == '#')
                {
                    while (_cursor < _length && _content[_cursor] != '\n') _cursor++;
                }
                else break;
            }
        }
        
        private void ConsumeColon()
        {
            int start = _cursor;
            // Peek
            SkipWhitespace();
            if (_cursor < _length && _content[_cursor] == ':')
            {
                _cursor++;
            }
        }
        
        private int GetInt()
        {
            string t = GetToken();
            if (t == ":") t = GetToken(); 
            if (t == ",") t = GetToken(); // Handle comma too
            if (int.TryParse(t, out int result)) return result;
            return 0;
        }
        
        private float GetFloat()
        {
            string t = GetToken();
            if (t == ":") t = GetToken();
             if (t == ",") t = GetToken();
            if (float.TryParse(t, out float result)) return result;
            return 0f;
        }
        
        private Vector3 GetVector3()
        {
            float x = GetFloat();
            float y = GetFloat();
            float z = GetFloat();
            return new Vector3(x, y, z);
        }
    }
}
