using System.Collections.Generic;
using UnityEngine;

namespace Droidworks.JKL
{
    public class JKLModel
    {
        public string Name;
        public List<Vector3> Vertices = new List<Vector3>();
        public List<Vector2> TextureVertices = new List<Vector2>(); // UVs in pixels
        public List<JKLMaterial> Materials = new List<JKLMaterial>();
        public List<JKLSurface> Surfaces = new List<JKLSurface>();

        public Dictionary<string, JKLTemplate> Templates = new Dictionary<string, JKLTemplate>();
        public List<JKLThing> Things = new List<JKLThing>();
    }

    public class JKLMaterial
    {
        public string Name; // Filename
        public float XTile;
        public float YTile;
    }

    public class JKLSurface
    {
        public int MaterialIndex;
        public List<int> VertexIndices = new List<int>();
        public List<int> TextureVertexIndices = new List<int>(); 
    }

    public class JKLTemplate
    {
        public string Name;
        public string BasedOn;
        public string Model3D;
        public Dictionary<string, string> Params = new Dictionary<string, string>();
    }

    public class JKLThing
    {
        public int Index;
        public string TemplateName;
        public string Name;
        public Vector3 Position;
        public Vector3 Rotation; // Pitch, Yaw, Roll
        public int SectorIndex;
        public string Extras;
    }
}
