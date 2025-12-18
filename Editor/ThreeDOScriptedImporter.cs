using UnityEngine;
using UnityEditor.AssetImporters;
using System.IO;
using System.Collections.Generic;
using Droidworks.ThreeDO;
using Droidworks.JKL; 
using Droidworks.JKL.Editor;

namespace Droidworks.ThreeDO.Editor
{
    [ScriptedImporter(1, "3do")]
    public class ThreeDOScriptedImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            var parser = new ThreeDOParser();
            ThreeDOModel model = null;
            try
            {
                 model = parser.Parse(ctx.assetPath);
            }
            catch(System.Exception e)
            {
                ctx.LogImportError($"Failed to parse 3DO: {e}");
                return;
            }

            // Load Palette
            var palette = ImporterUtils.FindAndLoadPalette(ctx.assetPath);

            // Shader Setup
            Shader defaultShader = ImporterUtils.GetDefaultShader();
            bool isURP = ImporterUtils.IsURP();

            // Load Materials
            var mats = new Dictionary<int, (Material mat, int w, int h)>();
            for(int i=0; i<model.Materials.Count; i++)
            {
                string matName = model.Materials[i];
                string jmatName = Path.ChangeExtension(matName, ".jmat");

                string path = ImporterUtils.FindFile(ctx.assetPath, matName, jmatName);
                Material unityMat = null;
                int w=64, h=64;

                if (!string.IsNullOrEmpty(path))
                {
                    var matParser = new MatParser(palette);
                    List<MatTexture> textures = null;
                    try { textures = matParser.Parse(path); }
                    catch(System.Exception e) { Debug.LogWarning($"[3DOImporter] Failed to parse mat {path}: {e}"); }

                    if (textures != null && textures.Count > 0)
                    {
                        var t = textures[0];
                        w = t.Width; 
                        h = t.Height;
                        
                        var tex2D = new Texture2D(w, h, TextureFormat.RGBA32, false);
                        tex2D.name = t.Name;
                        tex2D.filterMode = FilterMode.Point;
                        tex2D.SetPixels32(t.Pixels);
                        tex2D.Apply();
                        
                        ctx.AddObjectToAsset($"tex_{i}", tex2D);

                        unityMat = new Material(defaultShader); 
                        unityMat.name = t.Name;

                        if (isURP)
                        {
                            unityMat.SetTexture("_BaseMap", tex2D);
                            unityMat.SetColor("_BaseColor", Color.white);
                            unityMat.SetTexture("_MainTex", tex2D);
                        }
                        else
                        {
                            unityMat.mainTexture = tex2D;
                            unityMat.color = Color.white;
                        }

                        ImporterUtils.DisableShininess(unityMat, isURP);
                        
                        if (t.Transparent)
                        {
                            ImporterUtils.SetupCutoutMaterial(unityMat, isURP);
                        }
                    }
                }
                
                if (unityMat == null)
                {
                    unityMat = new Material(defaultShader);
                    unityMat.name = matName + "_MISSING";
                    unityMat.color = Color.magenta;
                    if(isURP) unityMat.SetColor("_BaseColor", Color.magenta);
                }
                
                ctx.AddObjectToAsset($"mat_{i}", unityMat);
                mats[i] = (unityMat, w, h);
            }

            // Create Root
            GameObject rootVal = new GameObject(model.Name);
            
            // 1. Create All Nodes first (to support arbitrary parent indices)
            var nodeGOs = new GameObject[model.Nodes.Count];
            for(int i=0; i<model.Nodes.Count; i++)
            {
                var node = model.Nodes[i];
                var go = new GameObject(node.Name);
                nodeGOs[i] = go;
            }
            
            // 2. Setup Transform & Hierarchy
            for(int i=0; i<model.Nodes.Count; i++)
            {
                var node = model.Nodes[i];
                var go = nodeGOs[i];
                
                // Position (Swap Y/Z)
                go.transform.localPosition = new Vector3(node.Position.x, node.Position.z, node.Position.y);
                // Rotation
                // 3DO (Right Handed Z-up):
                // Pitch (X), Yaw (Z), Roll (Y) - Based on Assasin.3do and Blender Importer structure
                // Blender applies: RotZ(yaw) * RotX(pitch) * RotY(roll)
                
                // Unity (Left Handed Y-up):
                // Map Axes: 3DO-X -> Unity-X, 3DO-Z -> Unity-Y, 3DO-Y -> Unity-Z
                // Negate angles for coordinate system change
                
                float rX = -node.Rotation.x; // Pitch
                float rY = -node.Rotation.y; // Yaw
                float rZ = -node.Rotation.z; // Roll
                
                // Construct Rotation: Yaw(Y) * Pitch(X) * Roll(Z)
                Quaternion qPitch = Quaternion.AngleAxis(rX, Vector3.right);
                Quaternion qYaw = Quaternion.AngleAxis(rY, Vector3.up);
                Quaternion qRoll = Quaternion.AngleAxis(rZ, Vector3.forward);
                
                go.transform.localRotation = qYaw * qPitch * qRoll;

                // Hierarchy
                if (node.ParentIndex >= 0 && node.ParentIndex < nodeGOs.Length && node.ParentIndex != i)
                {
                    go.transform.SetParent(nodeGOs[node.ParentIndex].transform, false);
                }
                else
                {
                    go.transform.SetParent(rootVal.transform, false);
                }
                
                // Mesh
                if (node.MeshIndex >= 0 && node.MeshIndex < model.Meshes.Count)
                {
                    var meshDef = model.Meshes[node.MeshIndex];
                    
                    // PASS PIVOT to Mesh Builder
                    Vector3 pivotUnity = new Vector3(node.Pivot.x, node.Pivot.z, node.Pivot.y);
                    
                    var (unityMesh, unityMats) = BuildUnityMesh(meshDef, mats, pivotUnity);
                    unityMesh.name = $"{meshDef.Name}_{i}"; 
                    ctx.AddObjectToAsset($"mesh_{node.MeshIndex}_{i}", unityMesh); 
                    
                    var mf = go.AddComponent<MeshFilter>();
                    mf.sharedMesh = unityMesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterials = unityMats;
                }
            }
            
            ctx.AddObjectToAsset("root", rootVal);
            ctx.SetMainObject(rootVal);
        }

        private (Mesh, Material[]) BuildUnityMesh(ThreeDOMesh meshDef, Dictionary<int, (Material, int, int)> mats, Vector3 pivotOffset)
        {
            var mesh = new Mesh();
            var submeshes = new Dictionary<int, List<int>>();
            
            var newVerts = new List<Vector3>();
            var newUVs = new List<Vector2>();
            
            foreach(var face in meshDef.Faces)
            {
                int matIdx = face.MaterialIndex;
                if(!submeshes.ContainsKey(matIdx)) submeshes[matIdx] = new List<int>();
                var tris = submeshes[matIdx];
                
                float w = 64f, h = 64f;
                if(mats.ContainsKey(matIdx)) { w = mats[matIdx].Item2; h = mats[matIdx].Item3; }
                
                int[] currentIndices = new int[face.VertexIndices.Count];
                for(int k=0; k<face.VertexIndices.Count; k++)
                {
                    int vIdx = face.VertexIndices[k];
                    int uvIdx = (k < face.UVIndices.Count) ? face.UVIndices[k] : 0;
                    
                    Vector3 vRaw = meshDef.Vertices[vIdx];
                    Vector3 v = new Vector3(vRaw.x, vRaw.z, vRaw.y);
                    
                    // APPLY PIVOT OFFSET
                    v += pivotOffset; 
                    
                    Vector2 uvRaw = (uvIdx < meshDef.UVs.Count) ? meshDef.UVs[uvIdx] : Vector2.zero;
                    Vector2 uv = new Vector2(uvRaw.x / w, uvRaw.y / h);
                    
                    newVerts.Add(v);
                    newUVs.Add(uv);
                    currentIndices[k] = newVerts.Count - 1;
                }
                
                for(int k=1; k<currentIndices.Length-1; k++)
                {
                    tris.Add(currentIndices[0]);
                    tris.Add(currentIndices[k+1]); 
                    tris.Add(currentIndices[k]);
                }
            }
            
            mesh.SetVertices(newVerts);
            mesh.SetUVs(0, newUVs);
            
            var sortedKeys = new List<int>(submeshes.Keys);
            sortedKeys.Sort();

            mesh.subMeshCount = sortedKeys.Count;
            var outMats = new Material[sortedKeys.Count];
            
            for(int i=0; i<sortedKeys.Count; i++)
            {
                int matIdx = sortedKeys[i];
                mesh.SetTriangles(submeshes[matIdx], i);
                if (mats.ContainsKey(matIdx)) outMats[i] = mats[matIdx].Item1;
                else {/* fallback? */}
            }
            
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return (mesh, outMats);
        }
    }
}
