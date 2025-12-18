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
            CmpPalette palette = FindAndLoadPalette(ctx.assetPath);
            if (palette != null) Debug.Log($"[JKLImporter] Palette Loaded (Colors: {palette.Colors.Length})");
            else Debug.LogError("[JKLImporter] Palette NOT found! Textures will fail.");

            // 3. Load Materials & Textures
            var loadedMats = new Dictionary<int, (Material mat, int w, int h)>();
            
            // Helper to get render pipeline shader
            Shader defaultShader = GetDefaultShader();
            bool isURP = IsURP();
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
                    unityMat = CreateTransparentMaterial(jklMat.Name, defaultShader, isURP);
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
                    string jmatPath = FindFile(ctx.assetPath, jklMat.Name, jmatName);
                    
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
                            
                            // CRITICAL: Add texture to asset context so it is saved!
                            ctx.AddObjectToAsset($"tex_{i}", texture);

                            // Transparency
                            if (texData.Transparent)
                            {
                                SetupCutoutMaterial(unityMat, isURP);
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
            // Position
            verts.Add(model.Vertices[vIdx]);

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

        private CmpPalette FindAndLoadPalette(string assetPath)
        {
            try
            {
                string dir = Path.GetDirectoryName(assetPath);
                
                // Strategy: Search up parents, looking for "mission.cmp" in standard locations
                // or any ".cmp" if close by.
                
                // 1. Recursive search for "mission.cmp" starting from closest "mission" parent
                string searchDir = dir;
                while(!string.IsNullOrEmpty(searchDir))
                {
                    // Check local "cmp" folder
                    string localCmpDir = Path.Combine(searchDir, "cmp");
                    if (Directory.Exists(localCmpDir))
                    {
                         var cmps = Directory.GetFiles(localCmpDir, "*.cmp");
                         if(cmps.Length > 0) return CmpParser.Parse(cmps[0]);
                    }

                    // Check for "mission.cmp" recursively if inside a folder named "mission" (common in JK extraction)
                    if (Path.GetFileName(searchDir).Equals("mission", System.StringComparison.OrdinalIgnoreCase))
                    {
                         var found = Directory.GetFiles(searchDir, "mission.cmp", SearchOption.AllDirectories);
                         if (found.Length > 0) return CmpParser.Parse(found[0]);
                    }
                    
                    searchDir = Path.GetDirectoryName(searchDir);
                    if (searchDir.Contains("Assets") == false) break; // Don't go outside Assets
                }
                
                // 2. Fallback: Check local dir
                var localFiles = Directory.GetFiles(dir, "*.cmp");
                if (localFiles.Length > 0) return CmpParser.Parse(localFiles[0]);

            }
            catch(System.Exception e)
            {
                Debug.LogError($"[JKLImporter] Palette Search Error: {e}");
            }
            return null;
        }

        private string FindFile(string assetPath, string jklName, string jmatName)
        {
            string baseDir = Path.GetDirectoryName(assetPath);
            string parentDir = Path.GetDirectoryName(baseDir);

            // Paths from Blender Addon (ImportSithJKL.py)
            // base_dir
            // base_dir/mat
            // base_dir/3do/mat
            // parent/mat
            // parent/3do/mat
            
            var paths = new List<string>();
            paths.Add(baseDir);
            paths.Add(Path.Combine(baseDir, "mat"));
            paths.Add(Path.Combine(baseDir, "3do", "mat"));
            
            if(!string.IsNullOrEmpty(parentDir))
            {
                paths.Add(Path.Combine(parentDir, "mat"));
                paths.Add(Path.Combine(parentDir, "3do", "mat"));
            }

            // Allow loose checking
            foreach(var p in paths)
            {
                if (!Directory.Exists(p)) continue;
                
                // Check for jmatName (likely lowercase via Path.ChangeExtension) and jklName (original)
                var files = Directory.GetFiles(p);
                foreach(var f in files)
                {
                    string fname = Path.GetFileName(f);
                    if (fname.Equals(jmatName, System.StringComparison.OrdinalIgnoreCase) ||
                        fname.Equals(jklName, System.StringComparison.OrdinalIgnoreCase))
                    {
                        // Debug.Log($"[JKLImporter] Found {jmatName} at {f}");
                        return f;
                    }
                }
            }
             
            // Debug.LogWarning($"[JKLImporter] Not Found: {jmatName}. Searched: {string.Join(", ", paths)}");
            return null;
        }

        // --- Shader Helpers ---

        private Material CreateTransparentMaterial(string name, Shader shader, bool isURP)
        {
            var mat = new Material(shader);
            mat.name = name;
            
            // Generate 1x1 Transparent Texture to ensure shader is happy
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, new Color(1, 1, 1, 0)); // Fully Transparent
            tex.Apply();
            tex.name = name + "_TransTex";
            
            // We can't easier add to asset context from here without passing ctx.
            // For now, let's just create it. Ideally it should be added to ctx in the main loop.
            // But since this helper is called from main loop, we can just return mat and let main loop handle texture? 
            // No, main loop expects mat. 
            // Let's rely on standard shader properties.
            
            if (isURP)
            {
                mat.SetFloat("_Surface", 1); // Transparent
                mat.SetFloat("_Blend", 0); // Alpha
                mat.SetColor("_BaseColor", new Color(1, 1, 1, 0.0f)); // 0 Alpha
                mat.SetTexture("_BaseMap", tex);
                mat.SetTexture("_MainTex", tex);
                
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = 3000;
            }
            else
            {
                mat.SetFloat("_Mode", 3); // Transparent
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.DisableKeyword("_ALPHABLEND_ON");
                mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = 3000;
                mat.color = new Color(1,1,1, 0.0f);
                mat.mainTexture = tex;
            }
            return mat;
        }

        private void SetupCutoutMaterial(Material mat, bool isURP)
        {
            if (isURP)
            {
                mat.SetFloat("_AlphaClip", 1);
                mat.SetFloat("_Cutoff", 0.5f);
            }
            else
            {
                mat.SetFloat("_Mode", 1); // Cutout
                mat.EnableKeyword("_ALPHATEST_ON");
                mat.renderQueue = 2450;
            }
        }

        private Shader GetDefaultShader()
        {
             if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null)
            {
                var pipe = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.GetType().Name;
                if (pipe.Contains("Universal")) return Shader.Find("Universal Render Pipeline/Lit");
                if (pipe.Contains("HDRenderPipeline")) return Shader.Find("HDRP/Lit");
            }
            return Shader.Find("Standard");
        }

        private bool IsURP()
        {
             if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null)
             {
                 return UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.GetType().Name.Contains("Universal");
             }
             return false;
        }
    }
}
