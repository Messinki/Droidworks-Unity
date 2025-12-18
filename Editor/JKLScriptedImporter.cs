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

            // 1. Find and Load Colormap (Try "mission.cmp" in standard paths)
            CmpPalette palette = FindAndLoadPalette(ctx.assetPath);
            if (palette == null) Debug.LogError($"[JKLImporter] Failed to find palette for {ctx.assetPath}");
            else Debug.Log($"[JKLImporter] Loaded palette (Colors: {palette.Colors.Length})");

            // 2. Load Materials
            // Map JKL Material Index -> (Material, TextureWidth, TextureHeight)
            var loadedMats = new Dictionary<int, (Material, int, int)>();
            
            for (int i = 0; i < model.Materials.Count; i++)
            {
                var jklMat = model.Materials[i];
                string jmatName = Path.ChangeExtension(jklMat.Name, ".jmat");
                
                // Search for .jmat file
                string jmatPath = FindFile(ctx.assetPath, jklMat.Name, jmatName);
                
                Material unityMat = null;
                int w = 64, h = 64;

                // Special Case: Material 0 is often Sky/Clip (Transparent)
                // If using logic similar to Blender addon:
                if (i == 0)
                {
                    unityMat = new Material(GetDefaultShader());
                    unityMat.name = jklMat.Name;
                    // Make transparent
                    unityMat.SetFloat("_Mode", 3); // Transparent
                    unityMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                    unityMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    unityMat.SetInt("_ZWrite", 0);
                    unityMat.DisableKeyword("_ALPHATEST_ON");
                    unityMat.DisableKeyword("_ALPHABLEND_ON");
                    unityMat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                    unityMat.renderQueue = 3000;
                    
                    if (IsURP())
                    {
                         unityMat.SetFloat("_Surface", 1); // Transparent
                         unityMat.SetFloat("_Blend", 0); // Alpha
                         unityMat.SetColor("_BaseColor", new Color(1, 1, 1, 0.2f));
                    }
                    else
                    {
                        unityMat.color = new Color(1, 1, 1, 0.2f);
                    }
                }
                else if (!string.IsNullOrEmpty(jmatPath))
                {
                    var matParser = new MatParser(palette);
                    var textures = matParser.Parse(jmatPath);

                    if (textures.Count > 0)
                    {
                        var texData = textures[0];
                        w = texData.Width;
                        h = texData.Height;
                        
                        var texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
                        texture.name = texData.Name;
                        texture.SetPixels32(texData.Pixels);
                        texture.Apply();
                        
                        unityMat = new Material(GetDefaultShader());
                        unityMat.name = texData.Name;
                        
                        // Fix for URP Texture Assignment
                        if (IsURP())
                        {
                            unityMat.SetTexture("_BaseMap", texture);
                            unityMat.color = Color.white; // Ensure tint is white
                        }
                        else
                        {
                            unityMat.mainTexture = texture;
                        }
                        
                        // Handle Transparency (if flagged in MAT)
                        if (texData.Transparent)
                        {
                            if (IsURP())
                            {
                                unityMat.SetFloat("_AlphaClip", 1);
                                unityMat.SetFloat("_Cutoff", 0.5f);
                            }
                            else
                            {
                                unityMat.SetFloat("_Mode", 1); // Cutout
                                unityMat.EnableKeyword("_ALPHATEST_ON");
                                unityMat.renderQueue = 2450;
                            }
                        }
                    }
                }
                else
                {
                    Debug.LogWarning($"[JKLImporter] Could not find .jmat file for material: {jklMat.Name} -> {jmatName}");
                }

                if (unityMat == null)
                {
                    unityMat = new Material(GetDefaultShader());
                    unityMat.name = jklMat.Name + "_Missing";
                    if (IsURP()) unityMat.SetColor("_BaseColor", Color.magenta);
                    else unityMat.color = Color.magenta;
                }
                
                ctx.AddObjectToAsset($"mat_{i}", unityMat);
                loadedMats[i] = (unityMat, w, h);
            }

            // Create Mesh
            var mesh = new Mesh();
            mesh.name = model.Name;
            
            // Vertices are already shared, but Surface vertices are indices into them.
            // Unity Mesh structure requires: Vertices, Normals, UVs.
            // But JKL surfaces share geometric vertices but have Unique UVs per face-vertex.
            // So we CANNOT simply set mesh.vertices = model.Vertices.
            // We must UNROLL the mesh so that every triangle vertex has its own UV.

            List<Vector3> newVertices = new List<Vector3>();
            List<Vector2> newUVs = new List<Vector2>();
            
            // Submeshes for Materials
            // We need to group triangles by material.
            // Map: MaterialIndex -> List of Indices
            
            // 1. Group Surfaces by MaterialIndex
            var usedMatIndices = new HashSet<int>();
            foreach (var s in model.Surfaces) usedMatIndices.Add(s.MaterialIndex);
            
            List<int> sortedMatIndices = new List<int>(usedMatIndices);
            sortedMatIndices.Sort();

            List<Material> rendererMaterials = new List<Material>();
            
            // Store triangles for later assignment
            var submeshTriangles = new List<List<int>>();

            for (int subMeshIdx = 0; subMeshIdx < sortedMatIndices.Count; subMeshIdx++)
            {
                int matIdx = sortedMatIndices[subMeshIdx];
                var triIndices = new List<int>();
                submeshTriangles.Add(triIndices);
                
                // Get corresponding Unity Material
                if (loadedMats.ContainsKey(matIdx))
                    rendererMaterials.Add(loadedMats[matIdx].Item1);
                else
                    rendererMaterials.Add(null); 

                // Get Texture Dimensions for UV Normalization
                int texW = 64, texH = 64;
                if (loadedMats.ContainsKey(matIdx))
                {
                    texW = loadedMats[matIdx].Item2;
                    texH = loadedMats[matIdx].Item3;
                }

                // Process all surfaces with this material
                foreach (var surf in model.Surfaces)
                {
                    if (surf.MaterialIndex != matIdx) continue;
                    if (surf.VertexIndices.Count < 3) continue;

                    // Fan Triangulation (0, i, i+1)
                    int v0_idx = surf.VertexIndices[0];
                    int uv0_idx = surf.TextureVertexIndices[0];
                    
                    int idx0 = AddVertex(v0_idx, uv0_idx, model, newVertices, newUVs, texW, texH);

                    for (int i = 1; i < surf.VertexIndices.Count - 1; i++)
                    {
                        int v1_idx = surf.VertexIndices[i];
                        int uv1_idx = surf.TextureVertexIndices[i];
                        int idx1 = AddVertex(v1_idx, uv1_idx, model, newVertices, newUVs, texW, texH);
                        
                        int v2_idx = surf.VertexIndices[i + 1];
                        int uv2_idx = surf.TextureVertexIndices[i + 1];
                        int idx2 = AddVertex(v2_idx, uv2_idx, model, newVertices, newUVs, texW, texH);
                        
                        triIndices.Add(idx0);
                        triIndices.Add(idx1);
                        triIndices.Add(idx2);
                    }
                }
            }

            // Assign to Mesh
            // CRITICAL: Set Vertices BEFORE SetTriangles
            mesh.SetVertices(newVertices);
            mesh.SetUVs(0, newUVs);
            
            mesh.subMeshCount = sortedMatIndices.Count;
            for (int i = 0; i < submeshTriangles.Count; i++)
            {
               mesh.SetTriangles(submeshTriangles[i], i);
            }

            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            ctx.AddObjectToAsset("mesh", mesh);

            // Create GameObject
            var go = new GameObject(model.Name);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = rendererMaterials.ToArray();

            ctx.AddObjectToAsset("main", go);
            ctx.SetMainObject(go);
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
                var pipe = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.GetType().Name;
                return pipe.Contains("Universal");
            }
            return false;
        }

        private int AddVertex(int vIdx, int uvIdx, JKLModel model, List<Vector3> verts, List<Vector2> uvs, int w, int h)
        {
            // Simple Add. Optimization: Use Dictionary cache to reuse vertices.
            // For now, simple duplicate is fine for level geometry.
            
            verts.Add(model.Vertices[vIdx]);
            
            if (uvIdx >= 0 && uvIdx < model.TextureVertices.Count)
            {
                Vector2 pixUV = model.TextureVertices[uvIdx];
                // Normalize: u = pix/w, v = pix/h
                // JKL V is top-down? Unity Bottom-Up.
                // Assuming we flipped texture pixels Y, we might need to flip V?
                // Or if JKL UV (0,0) is top-left, and Unity (0,0) is bottom-left.
                // Image decoded with Flip Y -> (0,0) Unity is Top-Left of original Image.
                // So (0,0) JKL UV matches (0,0) Unity UV.
                // It should be fine.
                
                uvs.Add(new Vector2(pixUV.x / w, pixUV.y / h));
            }
            else
            {
                uvs.Add(Vector2.zero);
            }
            
            return verts.Count - 1;
        }

        private CmpPalette FindAndLoadPalette(string assetPath)
        {
            // 1. Try standard relative lookups first (fast)
            string dir = Path.GetDirectoryName(assetPath);
            for (int i=0; i<4; i++)
            {
                if (string.IsNullOrEmpty(dir)) break;
                
                string[] cmps = Directory.GetFiles(dir, "*.cmp");
                if (cmps.Length > 0) return CmpParser.Parse(cmps[0]);
                
                if (Directory.Exists(Path.Combine(dir, "cmp")))
                {
                     cmps = Directory.GetFiles(Path.Combine(dir, "cmp"), "*.cmp");
                     if (cmps.Length > 0) return CmpParser.Parse(cmps[0]);
                }
                dir = Path.GetDirectoryName(dir);
            }

            // 2. Fallback: Recursive search for "mission.cmp" in the project found in "mission" folder
            // This covers the user's case: mission/3do/misc/cmp/mission.cmp
            // We'll search starting from the JKL's root parent (assuming "mission" is in path)
            // or just search up to Assets.
            
            dir = Path.GetDirectoryName(assetPath);
            while (!string.IsNullOrEmpty(dir))
            {
                // If we are in "mission" folder or equivalent, deep search
                if (Path.GetFileName(dir).Equals("mission", System.StringComparison.OrdinalIgnoreCase))
                {
                    var found = Directory.GetFiles(dir, "mission.cmp", SearchOption.AllDirectories);
                    if (found.Length > 0) return CmpParser.Parse(found[0]);
                }
                
                // Also just check strict "mission.cmp" recursively if we are at Assets root?
                // Too slow. Let's rely on the above loop eventually checking "mission" folder if JKL is inside it.
                
                dir = Path.GetDirectoryName(dir);
                 // Stop if we leave Assets
                 if (dir.EndsWith("Assets")) break;
            }
            
            return null;
        }
        
        private string FindFile(string assetPath, string originalName, string targetName)
        {
            // Search relative to assetPath
            string dir = Path.GetDirectoryName(assetPath);
            
            // Candidate paths to check
            var candidates = new List<string>();
            candidates.Add(dir);
            candidates.Add(Path.Combine(dir, "mat"));
            
            // Parent check (for ../mat)
            string parent = Path.GetDirectoryName(dir);
            if (!string.IsNullOrEmpty(parent))
            {
                candidates.Add(Path.Combine(parent, "mat"));
                candidates.Add(Path.Combine(parent, "3do", "mat"));
            }

            foreach (var path in candidates)
            {
                if (!Directory.Exists(path)) 
                {
                    // Debug.Log($"[JKLImporter] Candidate path does not exist: {path}");
                    continue;
                }

                // Case-Insensitive Search
                // targetName is e.g. "00t_wall.jmat"
                // file might be "00t_wall.jmat" or "00T_WALL.jmat"
                
                var files = Directory.GetFiles(path); // This might be slow if folder is huge
                foreach (var f in files)
                {
                    string fname = Path.GetFileName(f);
                    if (fname.Equals(targetName, System.StringComparison.OrdinalIgnoreCase))
                        return f;
                        
                    // Also verify against original name just in case rename didn't happen
                    // e.g. 00T_WALL.MAT (if we didn't rename it?)
                    if (fname.Equals(originalName, System.StringComparison.OrdinalIgnoreCase))
                        return f;
                }
            }
            
            // Debug failure
            Debug.LogWarning($"[JKLImporter] FindFile Failed for {targetName}. Checked: {string.Join(", ", candidates)}");
             
             return null;
        }
    }
}
