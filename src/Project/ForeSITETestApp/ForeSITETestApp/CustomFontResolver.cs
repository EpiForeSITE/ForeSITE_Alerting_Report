using PdfSharp.Fonts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ForeSITETestApp
{
    public class CustomFontResolver : IFontResolver
    {
        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            if (familyName.Equals("Georgia", StringComparison.OrdinalIgnoreCase))
            {
                if (isBold && isItalic) return new FontResolverInfo("Georgia#Bold-Italic");
                if (isBold) return new FontResolverInfo("Georgia#Bold");
                if (isItalic) return new FontResolverInfo("Georgia#Italic");
                return new FontResolverInfo("Georgia#"); // Regular
            }
            // Fallback to Arial if Georgia is not requested or fails
            if (isBold && isItalic) return new FontResolverInfo("Arial#Bold-Italic");
            if (isBold) return new FontResolverInfo("Arial#Bold");
            if (isItalic) return new FontResolverInfo("Arial#Italic");
            return new FontResolverInfo("Arial#");
        }

        public byte[] GetFont(string faceName)
        {
            string fontFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fonts");
            if (faceName.StartsWith("Georgia#"))
            {
                string fontFile = Path.Combine(fontFolder, "georgia.ttf");
                if (File.Exists(fontFile)) return File.ReadAllBytes(fontFile);
                // Fallback to bold if regular not found (adjust based on available files)
                fontFile = Path.Combine(fontFolder, "georgiab.ttf");
                if (File.Exists(fontFile)) return File.ReadAllBytes(fontFile);
            }
            // Fallback to Arial
            string arialFile = Path.Combine(fontFolder, "arial.ttf");
            if (File.Exists(arialFile)) return File.ReadAllBytes(arialFile);
            throw new FileNotFoundException($"Font '{faceName}' not found and no fallback available.");
        }
    }
}
