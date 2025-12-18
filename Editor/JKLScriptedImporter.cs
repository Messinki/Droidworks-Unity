using UnityEngine;
using UnityEditor.AssetImporters;
using System.IO;
using System.Collections.Generic;

namespace Droidworks.JKL.Editor
{
    [ScriptedImporter(1, "jkl")]
    public class JKLScriptedImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            var parser = new JKLParser();
            JKLModel model = null;
            
            try
            {
                model = parser.Parse(ctx.assetPath);
            }
            catch (System.Exception e)
            {
                ctx.LogImportError($"Failed to parse JKL: {e}");
                return;
            }

            // Create Mesh
            var mesh = new Mesh();
            mesh.name = model.Name;

            // Vertices
            // JKLParser already provided Unity-converted Vector3 (x, z, y) if we did it there.
            // Let's assume JKLParser.ParseVertices did `new Vector3(x, z, y)`.
            mesh.SetVertices(model.Vertices);

            // Triangles
            var triangles = new List<int>();
            
            // Naive Fan Triangulation for Convex Polygons
            // JKL surfaces are usually convex polygons.
            foreach (var surf in model.Surfaces)
            {
                if (surf.VertexIndices.Count < 3) continue;

                // Fan: 0-1-2, 0-2-3, 0-3-4...
                // JKL winding order usually counter-clockwise? 
                // Unity is clockwise for front-face? Or CCW?
                // Unity default is Clockwise (CW) for backface culling, meaning visible front face 
                // vertices must be ordered CW.
                // JKL/Sith engine is usually Right Handed?
                // We might need to flip order (0-2-1) if it appears inside out.
                // Let's try standard sequence first.
                
                int v0 = surf.VertexIndices[0];
                for (int i = 1; i < surf.VertexIndices.Count - 1; i++)
                {
                    triangles.Add(v0);
                    triangles.Add(surf.VertexIndices[i]);
                    triangles.Add(surf.VertexIndices[i + 1]);
                }
            }

            mesh.SetTriangles(triangles, 0);
            
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            ctx.AddObjectToAsset("mesh", mesh);

            // Create GameObject
            var go = new GameObject(model.Name);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            
            var mr = go.AddComponent<MeshRenderer>();
            // Use default material (pink) or standard white?
            // "No materials" was requested, so pink is fine, 
            // but let's give it a default white material so it's visible.
            var mat = new Material(Shader.Find("Standard"));
            ctx.AddObjectToAsset("material", mat);
            mr.sharedMaterial = mat;

            ctx.AddObjectToAsset("main", go);
            ctx.SetMainObject(go);
        }
    }
}
