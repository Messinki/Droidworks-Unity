using System;
using System.IO;
using UnityEngine;

namespace Droidworks.JKL
{
    public class CmpPalette
    {
        public Color32[] Colors; // 256 colors
    }

    public class CmpParser
    {
        public static CmpPalette Parse(string filePath)
        {
            if (!File.Exists(filePath)) return null;

            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            using (var br = new BinaryReader(fs))
            {
                // Header "CMP "
                var sig = br.ReadBytes(4);
                string sigStr = System.Text.Encoding.ASCII.GetString(sig);
                if (sigStr != "CMP ") return null;

                uint version = br.ReadUInt32();
                uint hasAlpha = br.ReadUInt32();
                
                // Skip Reserved (52 bytes)
                br.BaseStream.Seek(52, SeekOrigin.Current);

                var palette = new CmpPalette();
                palette.Colors = new Color32[256];

                for (int i = 0; i < 256; i++)
                {
                    byte r = br.ReadByte();
                    byte g = br.ReadByte();
                    byte b = br.ReadByte();
                    palette.Colors[i] = new Color32(r, g, b, 255);
                }

                // Only 255 is usually transparent in Sith engine? 
                // Or index 0?
                // The Python script assumed opaque for basics, implemented transparent logic later explicitly.
                // We'll stick to opaque for now, handling transparency in MatParser if needed.
                
                return palette;
            }
        }
    }
}
