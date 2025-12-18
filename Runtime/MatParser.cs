using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Droidworks.JKL
{
    public class MatTexture
    {
        public string Name;
        public int Width;
        public int Height;
        public Color32[] Pixels; // RGBA
        public bool Transparent;
    }

    public class MatParser
    {
        private CmpPalette _palette;

        public MatParser(CmpPalette palette)
        {
            _palette = palette;
        }

        public List<MatTexture> Parse(string filePath)
        {
            var textures = new List<MatTexture>();

            if (!File.Exists(filePath)) return textures;

            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                using (var br = new BinaryReader(fs))
                {
                    // Header "MAT "
                    var sig = br.ReadBytes(4);
                    string sigStr = System.Text.Encoding.ASCII.GetString(sig);
                    if (sigStr != "MAT ") return textures;

                    float version = br.ReadSingle();
                    int typeVal = br.ReadInt32(); // 2 is Texture, others might be color
                    int recordCount = br.ReadInt32(); // Number of logical records (cells in a texture animation?)
                    int textureCount = br.ReadInt32(); // Total textures (mips?)

                    // Python script skip:
                    // ColorFormat (14 ints = 56 bytes)
                    // We need BPP from here.
                    // int[14] cf_data
                    int[] cfData = new int[14];
                    for (int k = 0; k < 14; k++) cfData[k] = br.ReadInt32();

                    int bpp = cfData[1]; // Bits per pixel (8 or 16)

                    // Skip Records
                    // Records describe how textures are used (e.g. animation frames). 
                    // We verify Logic from Python:
                    // if typeVal == 2: skip 40 bytes/rec
                    // else: skip 24 bytes/rec
                    
                    for (int k = 0; k < recordCount; k++)
                    {
                        if (typeVal == 2) br.BaseStream.Seek(40, SeekOrigin.Current);
                        else br.BaseStream.Seek(24, SeekOrigin.Current);
                    }

                    if (typeVal != 2) return textures; // Only handle Texture Mats for now

                    // Parse Textures (Mipmaps/Frames)
                    // We usually only care about the first one (LOD0)
                    for (int k = 0; k < textureCount; k++)
                    {
                        // Mipmap Header (6 ints = 24 bytes)
                        int width = br.ReadInt32();
                        int height = br.ReadInt32();
                        int transparentType = br.ReadInt32(); // 0=None, 1=Trans (color 0), 2=Trans (last color?)
                        int pad1 = br.ReadInt32();
                        int pad2 = br.ReadInt32();
                        int mips = br.ReadInt32();

                        int reqSize = (width * height * bpp) / 8;
                        byte[] pixelData = br.ReadBytes(reqSize);

                        // Only process the first texture found (usually highest res)
                        // Actually, if it's an animation (CELs), we might want them suitable for Unity?
                        // For now, load first texture as "The Texture".
                        
                        if (textures.Count == 0)
                        {
                             var tex = new MatTexture();
                             tex.Name = Path.GetFileNameWithoutExtension(filePath);
                             tex.Width = width;
                             tex.Height = height;
                             tex.Transparent = (transparentType != 0);
                             tex.Pixels = DecodePixels(pixelData, width, height, bpp, transparentType);
                             textures.Add(tex);
                        }

                        // Skip remaining mipmaps for this texture block
                        // Mips are stored sequentially after the main data?
                        // The Python script says:
                        // for m in range(1, levels): ... seek ...
                        
                        for (int m = 1; m < mips; m++)
                        {
                            int w = width >> m;
                            int h = height >> m;
                            if (w < 1) w = 1;
                            if (h < 1) h = 1;
                            int sz = (w * h * bpp) / 8;
                            br.BaseStream.Seek(sz, SeekOrigin.Current);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to parse MAT {filePath}: {e}");
            }

            return textures;
        }

        private Color32[] DecodePixels(byte[] data, int w, int h, int bpp, int transType)
        {
            Color32[] pixels = new Color32[w * h];

            // NOTE:
            // JKL Textures are often stored Column-Major or with a specific stride?
            // Python: np.frombuffer... no transpose. 
            // So probably Row-Major (standard).
            // However, Unity Texture2D.SetPixels expects Row-Major starting from Bottom-Left?
            // Usually file formats are Top-Left. We might need to flip Y.
            // Let's decode linearly first, then FlipY can be done during assignment or here.
            // Python script V-coord was `1.0 - v`, implying texture is "upright" in blender UV space (0,0 BL).
            // If we load Top-Left data into Unity (Bottom-Left), image is upside down.
            // So we generally need to flip Y.

            if (bpp == 8)
            {
                // Indexed
                if (_palette == null)
                {
                    // Grayscale fallback
                    for (int i = 0; i < data.Length; i++)
                    {
                        byte val = data[i];
                        pixels[i] = new Color32(val, val, val, 255);
                    }
                }
                else
                {
                    for (int i = 0; i < data.Length; i++)
                    {
                        int idx = data[i];
                        pixels[i] = _palette.Colors[idx];
                        
                        // Transparency
                        if (transType != 0 && idx == 0) // Index 0 is transparent?
                        {
                            // Typically Sith engine uses index 0 for transparency
                            pixels[i] = new Color32(0, 0, 0, 0);
                        }
                    }
                }
            }
            else if (bpp == 16)
            {
                // RGB565 or ARGB1555?
                // Python: 
                // r = ((arr & 0xF800) >> 11) << 3
                // g = ((arr & 0x07E0) >> 5) << 2
                // b = (arr & 0x001F) << 3
                // This is RGB565.
                
                for (int i = 0; i < w * h; i++)
                {
                    ushort val = BitConverter.ToUInt16(data, i * 2);
                    
                    byte r = (byte)(((val & 0xF800) >> 11) << 3);
                    byte g = (byte)(((val & 0x07E0) >> 5) << 2);
                    byte b = (byte)((val & 0x001F) << 3);
                    
                    // Alpha? 16-bit usually no alpha in this logic. 
                    // Unless ARGB1555. 
                    // Let's stick to RGB565 for now as per Python script.
                    
                    pixels[i] = new Color32(r, g, b, 255);
                    
                    // If transType is set, color 0 (black 0x0000) might be transparent?
                    if (transType != 0 && val == 0)
                    {
                         pixels[i] = new Color32(0, 0, 0, 0);
                    }
                }
            }

            // Flip Y (Top-Left to Bottom-Left for Unity)
            Color32[] flipped = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    flipped[(h - 1 - y) * w + x] = pixels[y * w + x];
                }
            }

            return flipped;
        }
    }
}
