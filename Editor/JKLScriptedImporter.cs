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
                Material unityMat = null;
                int w = 64, h = 64; // Default size

                // Try to find material file
                // The FindFile method signature has changed in the provided diff.
                // Assuming the new FindFile returns (path, width, height)
                var (matFile, foundWidth, foundHeight) = FindFile(jklMat.Name, ctx.assetPath);
                
                if (!string.IsNullOrEmpty(matFile))
                {
                    var mParser = new Droidworks.JKL.MatParser(matFile, palette);
                    var textures = mParser.Parse();
                    
                    if (textures.Count > 0)
                    {
                        var texData = textures[0];
                        w = texData.Width; // Update w/h for UV normalization
                        h = texData.Height;
                        
                        // Create Texture
                        var texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
                        texture.name = texData.Name;
                        texture.SetPixelData(texData.Pixels, 0);
                        texture.filterMode = FilterMode.Point;
                        texture.wrapMode = TextureWrapMode.Repeat;
                        texture.Apply();
                        
                        // Create Material
                        unityMat = new Material(defaultShader);
                        unityMat.name = jklMat.Name;
                        
                        if (isURP)
                        {
                            unityMat.SetTexture("_BaseMap", texture);
                            unityMat.SetColor("_BaseColor", Color.white);
                            unityMat.SetTexture("_MainTex", texture);
                        }
                        else
                        {
                            unityMat.mainTexture = texture;
                            unityMat.color = Color.white;
                        }
                        
                        // Retro Matte Look
                        DisableShininess(unityMat, isURP);
                        
                        // Add texture to asset
                        ctx.AddObjectToAsset($"tex_{i}", texture);

                        // Transparency
                        if (texData.Transparent)
                        {
                            SetupCutoutMaterial(unityMat, isURP);
                        }
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
                // Logic Update:
                // -1 implies "No Material" or "Clip/Sky".
                // We should NOT map it to 0, because Mat 0 might be a valid texture (e.g. cratemag.mat).
                // Instead, we will handle key -1 explicitly as an invisible material.
                
                int effectiveIndex = s.MaterialIndex;
                if (effectiveIndex < -1 || effectiveIndex >= model.Materials.Count)
                {
                    // If truly out of bounds (<-1 or >Count), map to -1 (Invisible)
                    effectiveIndex = -1;
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
            // For true invisibility in URP, Unlit is safer than Lit as it ignores all lighting/reflections
            Shader transparentShader = isURP ? Shader.Find("Universal Render Pipeline/Unlit") : shader;
            if (transparentShader == null) transparentShader = shader; // Fallback
            
            var mat = new Material(transparentShader);
            mat.name = name;
            
            // Generate 1x1 Transparent Texture
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, new Color(1, 1, 1, 0)); 
            tex.Apply();
            tex.name = name + "_TransTex";
            
            if (isURP)
            {
                mat.SetFloat("_Surface", 1); // Transparent
                mat.SetFloat("_Blend", 0); // Alpha
                mat.SetColor("_BaseColor", new Color(1, 1, 1, 0.0f));
                mat.SetTexture("_BaseMap", tex);
                
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = 3000;
                
                // No need to disable shininess for Unlit, but doesn't hurt to be safe against Shader Graph variants
                mat.SetFloat("_Smoothness", 0.0f); 
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
                
                DisableShininess(mat, false);
            }
            return mat;
        }

        private void DisableShininess(Material mat, bool isURP)
        {
            if (isURP)
            {
                mat.SetFloat("_Smoothness", 0.0f);
                mat.SetFloat("_SpecularHighlights", 0.0f);
                mat.SetFloat("_EnvironmentReflections", 0.0f);
                mat.DisableKeyword("_SPECULARAGLOSSMAP");
                mat.DisableKeyword("_SPECULARHIGHLIGHTS_OFF"); 
                mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF"); // Force Off?
                mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            }
            else
            {
                mat.SetFloat("_Glossiness", 0.0f);
                mat.SetFloat("_SpecularHighlights", 0.0f);
                mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                mat.SetFloat("_GlossyReflections", 0.0f);
                mat.EnableKeyword("_GLOSSYREFLECTIONS_OFF");
            }
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
