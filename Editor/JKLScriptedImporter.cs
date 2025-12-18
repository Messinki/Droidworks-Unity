using UnityEngine;
using UnityEditor.AssetImporters;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace Droidworks.JKL.Editor
{
    [ScriptedImporter(1, "jkl")]
    public class JKLScriptedImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            Debug.Log($"[JKLImporter] Starting import for: {ctx.assetPath}");

            // 1. Parse JKL Model
            var parser = new JKLParser();
            JKLModel model = null;
            try
            {
                model = parser.Parse(ctx.assetPath);
                Debug.Log($"[JKLImporter] Parsed JKL: {model.Name}, Verts: {model.Vertices.Count}, Surfs: {model.Surfaces.Count}");
            }
            catch (System.Exception e)
            {
                ctx.LogImportError($"Failed to parse JKL: {e}");
                return;
            }

            // 2. Load Palette
            CmpPalette palette = ImporterUtils.FindAndLoadPalette(ctx.assetPath);
            if (palette != null) Debug.Log($"[JKLImporter] Palette Loaded (Colors: {palette.Colors.Length})");
            else Debug.LogError("[JKLImporter] Palette NOT found! Textures will fail.");

            // 3. Load Materials & Textures
            var loadedMats = new Dictionary<int, (Material mat, int w, int h)>();
            
            // Helper to get render pipeline shader
            Shader defaultShader = ImporterUtils.GetDefaultShader();
            bool isURP = ImporterUtils.IsURP();
            Debug.Log($"[JKLImporter] Render Pipeline: {(isURP ? "URP" : "Standard")}");

            for (int i = 0; i < model.Materials.Count; i++)
            {
                var jklMat = model.Materials[i];
                string jmatName = Path.ChangeExtension(jklMat.Name, ".jmat");
                
                Material unityMat = null;
                int w = 64, h = 64; // Default size

                // 3a. Special Case: Mat 0 (Sky/Clip) - Transparent
                if (i == 0)
                {
                    unityMat = ImporterUtils.CreateTransparentMaterial(jklMat.Name, defaultShader, isURP);
                    Debug.Log($"[JKLImporter] Mat 0 (Sky/Clip): {unityMat.name}");
                    
                    // Save the dummy texture we assigned to Mat 0
                    if (unityMat.mainTexture != null)
                    {
                         ctx.AddObjectToAsset($"tex_0", unityMat.mainTexture);
                    }
                }
                else
                {
                    // 3b. Find .jmat file
                    string jmatPath = ImporterUtils.FindFile(ctx.assetPath, jklMat.Name, jmatName);
                    
                    if (!string.IsNullOrEmpty(jmatPath))
                    {
                        // Parse JMAT
                        var matParser = new MatParser(palette);
                        List<MatTexture> textures = null;
                        try { textures = matParser.Parse(jmatPath); }
                        catch (System.Exception e) { Debug.LogError($"[JKLImporter] Failed to parse {jmatName}: {e}"); }

                        if (textures == null || textures.Count == 0)
                        {
                             Debug.LogWarning($"[JKLImporter] JMAT parsed 0 textures: {jmatPath}");
                        }

                        if (textures != null && textures.Count > 0)
                        {
                            var texData = textures[0];
                            w = texData.Width;
                            h = texData.Height;
                            
                            // Debug.Log($"[JKLImporter] Texture: {texData.Name} ({w}x{h}, Trans:{texData.Transparent}). First Px: {texData.Pixels[0]}");

                            // Create Texture
                            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
                            texture.name = texData.Name;
                            texture.filterMode = FilterMode.Point; 
                            texture.SetPixels32(texData.Pixels);
                            texture.Apply();

                            // Verify Shader
                            if (defaultShader == null) Debug.LogError("[JKLImporter] Shader is NULL!");

                            // Create Material
                            unityMat = new Material(defaultShader);
                            unityMat.name = texData.Name;
                            
                            // Assign Texture (Pipeline Dependent)
                            if (isURP)
                            {
                                unityMat.SetTexture("_BaseMap", texture);
                                unityMat.SetColor("_BaseColor", Color.white);
                                // Safety: Set _MainTex too, just in case
                                unityMat.SetTexture("_MainTex", texture);
                            }
                            else
                            {
                                unityMat.mainTexture = texture;
                                unityMat.color = Color.white;
                            }
                            
                            // Retro Matte Look
                            ImporterUtils.DisableShininess(unityMat, isURP);
                            
                            // CRITICAL: Add texture to asset context so it is saved!
                            ctx.AddObjectToAsset($"tex_{i}", texture);

                            // Transparency
                            if (texData.Transparent)
                            {
                                ImporterUtils.SetupCutoutMaterial(unityMat, isURP);
                            }
                        }
                    }
                    else
                    {
                         Debug.LogWarning($"[JKLImporter] Missing JMAT: {jklMat.Name} (looked for {jmatName})");
                    }
                }

                // Fallback
                if (unityMat == null)
                {
                    unityMat = new Material(defaultShader);
                    unityMat.name = $"{jklMat.Name}_MISSING";
                    if (isURP) unityMat.SetColor("_BaseColor", Color.magenta);
                    else unityMat.color = Color.magenta;
                }

                ctx.AddObjectToAsset($"mat_{i}", unityMat);
                loadedMats[i] = (unityMat, w, h);
            }

            // 4. Build Mesh
            var mesh = new Mesh();
            mesh.name = model.Name;

            var newVertices = new List<Vector3>();
            var newUVs = new List<Vector2>();
            
            var surfacesByMat = new Dictionary<int, List<Droidworks.JKL.JKLSurface>>();
            foreach(var s in model.Surfaces)
            {
                int effectiveIndex = s.MaterialIndex;
                if (effectiveIndex < 0 || effectiveIndex >= model.Materials.Count)
                {
                    // Fallback to Material 0 (Sky/Clip) to match Blender behavior (default logic)
                    // This prevents holes in the mesh for invalid surfaces
                    effectiveIndex = 0;
                }
                
                if(!surfacesByMat.ContainsKey(effectiveIndex)) surfacesByMat[effectiveIndex] = new List<Droidworks.JKL.JKLSurface>();
                surfacesByMat[effectiveIndex].Add(s);
            }

            var sortedMatIndices = surfacesByMat.Keys.ToList();
            sortedMatIndices.Sort();

            mesh.subMeshCount = sortedMatIndices.Count;
            var materialsForRenderer = new List<Material>();

            // Temporary lists for mesh construction
            // We can't set mesh.triangles until we set mesh.vertices!
            var finalTriangles = new List<List<int>>(); 

            for(int sub = 0; sub < sortedMatIndices.Count; sub++)
            {
                int matIdx = sortedMatIndices[sub];
                var surfaces = surfacesByMat[matIdx];
                
                // Mat info
                var matInfo = loadedMats.ContainsKey(matIdx) ? loadedMats[matIdx] : (null, 64, 64);
                materialsForRenderer.Add(matInfo.mat);
                int tw = matInfo.w;
                int th = matInfo.h;

                var subTriangles = new List<int>();

                foreach(var surf in surfaces)
                {
                    // Triangulate Fan (0, 1, 2), (0, 2, 3)...
                    if (surf.VertexIndices.Count < 3) continue;

                    // Get/Add Base Vertex 0
                    int idx0 = AddVertex(surf.VertexIndices[0], surf.TextureVertexIndices[0], model, newVertices, newUVs, tw, th);

                    for(int k=1; k < surf.VertexIndices.Count - 1; k++)
                    {
                        int idx1 = AddVertex(surf.VertexIndices[k], surf.TextureVertexIndices[k], model, newVertices, newUVs, tw, th);
                        int idx2 = AddVertex(surf.VertexIndices[k+1], surf.TextureVertexIndices[k+1], model, newVertices, newUVs, tw, th);

                        subTriangles.Add(idx0);
                        subTriangles.Add(idx2);
                        subTriangles.Add(idx1);
                    }
                }
                finalTriangles.Add(subTriangles);
            }

            // Assign Geometry (Strict Order for Unity)
            mesh.SetVertices(newVertices);
            mesh.SetUVs(0, newUVs);
            
            for(int i=0; i<finalTriangles.Count; i++)
            {
                mesh.SetTriangles(finalTriangles[i], i);
            }

            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            
            ctx.AddObjectToAsset("mesh", mesh);

            // 5. Create GameObject
            var go = new GameObject(Path.GetFileNameWithoutExtension(ctx.assetPath));
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = materialsForRenderer.ToArray();

            ctx.AddObjectToAsset("main", go);
            ctx.SetMainObject(go);
            
            Debug.Log("[JKLImporter] Import Complete.");
        }

        // --- Helper Methods ---

        private int AddVertex(int vIdx, int uvIdx, JKLModel model, List<Vector3> verts, List<Vector2> uvs, int w, int h)
        {
            // Position (Convert from Sith Z-up to Unity Y-up)
            verts.Add(ImporterUtils.SithToUnityPosition(model.Vertices[vIdx]));

            // UV
            if (uvIdx >= 0 && uvIdx < model.TextureVertices.Count)
            {
                Vector2 raw = model.TextureVertices[uvIdx];
                // Normalization: raw / dimension
                uvs.Add(new Vector2(raw.x / w, raw.y / h));
            }
            else
            {
                uvs.Add(Vector2.zero);
            }

            return verts.Count - 1;
        }
    }
}
