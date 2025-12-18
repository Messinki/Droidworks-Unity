using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.AssetImporters;
using Droidworks.ThreeDO;
using Droidworks.JKL; // For parsers

namespace Droidworks.ThreeDO.Editor
{
    [ScriptedImporter(1, "3do")]
    public class ThreeDOScriptedImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            var parser = new ThreeDOParser(ctx.assetPath);
            var model = parser.Parse();
            
            if (model == null) return;

            var rootGo = new GameObject(model.Name);
            
            // Materials
            // We need to load palette again? 
            // Finding CMP... similar logic to JKL.
            // For now, let's just make placeholders or reuse JKLScriptedImporter logic.
            // We can extract helper FindFile/FindPalette?
            // Or just minimal: White material.
            // TODO: Extract Material Loading Logic to a Helper Class.
            
            var mats = new Material[model.Materials.Count];
            for(int i=0; i<model.Materials.Count; i++)
            {
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.name = model.Materials[i];
                mat.color = Color.white;
                ctx.AddObjectToAsset($"mat_{i}", mat);
                mats[i] = mat;
            }
            
            // Nodes & Meshes
            var gameObjects = new Dictionary<int, GameObject>();
            
            // First pass: create all node objects
            foreach(var node in model.Nodes)
            {
                var go = new GameObject(node.Name);
                gameObjects[node.Index] = go;
                
                // Transform
                // 3DO pos/rot are local or global? Hierarchy implies local relative to parent.
                // Position is Vector3
                go.transform.localPosition = node.Position;
                go.transform.localRotation = Quaternion.Euler(node.Rotation); // Pitch Yaw Roll? Check order.
                
                // Mesh
                if (node.MeshIndex >= 0 && node.MeshIndex < model.Meshes.Count)
                {
                    var meshData = model.Meshes[node.MeshIndex];
                    var mesh = BuildMesh(meshData);
                    mesh.name = $"{model.Name}_{node.Name}_Mesh";
                    ctx.AddObjectToAsset($"mesh_{node.Index}", mesh);
                    
                    var mf = go.AddComponent<MeshFilter>();
                    mf.sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    
                    // Assign Materials
                    // Mesh faces reference material indices.
                    // We need submeshes.
                    // My simple BuildMesh below doesn't handle submeshes yet.
                    
                    // Assign default if single submesh
                    if (mats.Length > 0) mr.sharedMaterial = mats[0];
                }
            }
            
            // Second pass: hierarchy
            foreach(var node in model.Nodes)
            {
                var go = gameObjects[node.Index];
                if (node.ParentIndex >= 0 && gameObjects.ContainsKey(node.ParentIndex))
                {
                    go.transform.SetParent(gameObjects[node.ParentIndex].transform, false);
                }
                else
                {
                    go.transform.SetParent(rootGo.transform, false);
                }
            }

            ctx.AddObjectToAsset("main", rootGo);
            ctx.SetMainObject(rootGo);
        }

        private Mesh BuildMesh(ThreeDOMesh meshData)
        {
            var mesh = new Mesh();
            var verts = meshData.Vertices;
            var tris = new List<int>();
            
            // Simple triangulation: Fan
            // Also UVs
            
            var newVerts = new List<Vector3>();
            var newUVs = new List<Vector2>();
            
            foreach(var face in meshData.Faces)
            {
                // Triangulate
                int v0_idx = newVerts.Count;
                
                for(int i=0; i<face.VertexIndices.Length; i++)
                {
                    int vi = face.VertexIndices[i];
                    int ui = face.UVIndices[i];
                    
                    newVerts.Add(meshData.Vertices[vi]);
                    // UV? Need to look up in mesh.UVs if separate list?
                    // ThreeDOMesh has UVs list.
                    if (ui >= 0 && ui < meshData.UVs.Count)
                        newUVs.Add(meshData.UVs[ui]);
                    else
                        newUVs.Add(Vector2.zero);
                }
                
                // Fan indices: 0, 1, 2; 0, 2, 3; ...
                for(int i=1; i<face.VertexIndices.Length-1; i++)
                {
                    tris.Add(v0_idx);
                    tris.Add(v0_idx + i);
                    tris.Add(v0_idx + i + 1);
                }
            }
            
            mesh.SetVertices(newVerts);
            mesh.SetUVs(0, newUVs);
            mesh.SetTriangles(tris, 0);
            
            mesh.RecalculateNormals();
            return mesh;
        }
    }
}
