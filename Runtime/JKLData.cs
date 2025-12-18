using System.Collections.Generic;
using UnityEngine;

namespace Droidworks.JKL
{
    public class JKLModel
    {
        public string Name;
        public List<Vector3> Vertices = new List<Vector3>();
        public List<JKLSurface> Surfaces = new List<JKLSurface>();
    }

    public class JKLSurface
    {
        public int MaterialIndex;
        public List<int> VertexIndices = new List<int>();
        // We'll skip UVs for now as per plan, but good to have the structure ready if needed.
        // public List<int> TextureVertexIndices = new List<int>(); 
    }
}
