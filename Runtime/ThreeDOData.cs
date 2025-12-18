using System.Collections.Generic;
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
        public int Index;
        public List<Vector3> Vertices = new List<Vector3>();
        public List<Vector2> UVs = new List<Vector2>();
        public List<ThreeDOFace> Faces = new List<ThreeDOFace>();
    }

    public class ThreeDOFace
    {
        public int MaterialIndex;
        public int[] VertexIndices;
        public int[] UVIndices; // Corresponding 1:1 with VertexIndices usually? Or separate? 
                                // 3DO format has pairs: v_idx, uv_idx for each vertex in the face.
    }

    public class ThreeDONode
    {
        public int Index;
        public string Name;
        public int MeshIndex;
        public int ParentIndex;
        public int ChildIndex; // First child
        public int SiblingIndex; // Next sibling
        public Vector3 Position;
        public Vector3 Rotation; // Pitch, Yaw, Roll
        public Vector3 Pivot;
    }
}
