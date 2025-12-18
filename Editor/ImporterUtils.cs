using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using Droidworks.JKL;

namespace Droidworks.JKL.Editor
{
    public static class ImporterUtils
    {
        public static CmpPalette FindAndLoadPalette(string assetPath)
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
                Debug.LogError($"[ImporterUtils] Palette Search Error: {e}");
            }
            return null;
        }

        public static string FindFile(string assetPath, string jklName, string jmatName)
        {
            string baseDir = Path.GetDirectoryName(assetPath);
            string parentDir = Path.GetDirectoryName(baseDir);

            // Paths from Blender Addon (ImportSithJKL.py)
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
                
                var files = Directory.GetFiles(p);
                foreach(var f in files)
                {
                    string fname = Path.GetFileName(f);
                    // Check intersection of names provided
                    if ((!string.IsNullOrEmpty(jmatName) && fname.Equals(jmatName, System.StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(jklName) && fname.Equals(jklName, System.StringComparison.OrdinalIgnoreCase)))
                    {
                        return f;
                    }
                }
            }
             
            return null;
        }

        public static Material CreateTransparentMaterial(string name, Shader shader, bool isURP)
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

        public static void DisableShininess(Material mat, bool isURP)
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

        public static void SetupCutoutMaterial(Material mat, bool isURP)
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

        public static Shader GetDefaultShader()
        {
             if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null)
            {
                var pipe = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.GetType().Name;
                if (pipe.Contains("Universal")) return Shader.Find("Universal Render Pipeline/Lit");
                if (pipe.Contains("HDRenderPipeline")) return Shader.Find("HDRP/Lit");
            }
            return Shader.Find("Standard");
        }

        public static bool IsURP()
        {
             if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null)
             {
                 return UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.GetType().Name.Contains("Universal");
             }
             return false;
        }
    }
}
