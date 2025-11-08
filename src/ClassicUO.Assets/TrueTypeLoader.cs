#region license

// Copyright (c) 2021, jaedan
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are met:
// 1. Redistributions of source code must retain the above copyright
//    notice, this list of conditions and the following disclaimer.
// 2. Redistributions in binary form must reproduce the above copyright
//    notice, this list of conditions and the following disclaimer in the
//    documentation and/or other materials provided with the distribution.
// 3. All advertising materials mentioning features or use of this software
//    must display the following acknowledgement:
//    This product includes software developed by andreakarasho - https://github.com/andreakarasho
// 4. Neither the name of the copyright holder nor the
//    names of its contributors may be used to endorse or promote products
//    derived from this software without specific prior written permission.
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS ''AS IS'' AND ANY
// EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
// WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
// DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER BE LIABLE FOR ANY
// DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
// (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
// LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
// ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
// (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
// SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

#endregion

using System;
using ClassicUO.Utility.Logging;
using FontStashSharp;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ClassicUO.Assets
{
    public class TrueTypeLoader
    {
        public const string EMBEDDED_FONT = "Roboto-Regular";

        private readonly Dictionary<string, FontSystem> _fonts = new();

        private TrueTypeLoader()
        {
        }

        private static TrueTypeLoader _instance;
        public static TrueTypeLoader Instance => _instance ??= new TrueTypeLoader();

        public byte[] ImGuiFont;

        public void Load()
        {
            var settings = new FontSystemSettings
            {
                FontResolutionFactor = 2,
                KernelWidth = 2,
                KernelHeight = 2
            };

            string fontPath = Path.Combine(AppContext.BaseDirectory, "Fonts");

            if (!Directory.Exists(fontPath))
                Directory.CreateDirectory(fontPath);

            // 首先加载嵌入的字体
            LoadEmbeddedFonts();

            // 然后加载运行时字体目录中的字体（支持.ttf和.otf）
            byte[] chineseFallbackFont = FindChineseFallbackFont();

            foreach (string fontFile in Directory.GetFiles(fontPath, "*.*")
                .Where(f => f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || 
                           f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    string fontName = Path.GetFileNameWithoutExtension(fontFile);
                    byte[] fontBytes = File.ReadAllBytes(fontFile);

#if DEBUG
                    Log.Trace($"Loading runtime font: {fontName} ({fontBytes.Length} bytes)");
#endif

                    var fontSystem = new FontSystem(settings);
                    fontSystem.AddFont(fontBytes);

                    // 如果有中文回退字体且当前字体不是中文字体，添加为回退
                    if (chineseFallbackFont != null && fontBytes != chineseFallbackFont)
                    {
                        try
                        {
                            fontSystem.AddFont(chineseFallbackFont);
#if DEBUG
                            Log.Trace($"  Added Chinese fallback font to {fontName}");
#endif
                        }
                        catch (Exception ex)
                        {
                            Log.Warn($"Failed to add Chinese fallback to {fontName}: {ex.Message}");
                        }
                    }

                    _fonts[fontName] = fontSystem;
                }
                catch (Exception ex)
                {
                    Log.Error($"Failed to load font {fontFile}: {ex.Message}");
                }
            }
        }

        private byte[] FindChineseFallbackFont()
        {
            // 在已加载的字体中查找中文字体
            string fontPath = Path.Combine(AppContext.BaseDirectory, "Fonts");
            
            foreach (string fontFile in Directory.GetFiles(fontPath, "*.*")
                .Where(f => f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || 
                           f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)))
            {
                string fontName = Path.GetFileNameWithoutExtension(fontFile).ToLower();
                FileInfo fi = new FileInfo(fontFile);
                
                // 检测中文字体（通过名称或文件大小）
                if (fontName.Contains("noto") || fontName.Contains("source") || 
                    fontName.Contains("chinese") || fontName.Contains("sc") ||
                    fontName.Contains("hans") || fontName.Contains("cjk") ||
                    fi.Length > 1000000) // 大于1MB的字体可能包含中文
                {
#if DEBUG
                    Log.Trace($"Found Chinese fallback font: {fontName} ({fi.Length} bytes)");
#endif
                    return File.ReadAllBytes(fontFile);
                }
            }
            
            return null;
        }

        private void LoadEmbeddedFonts()
        {
            var settings = new FontSystemSettings
            {
                FontResolutionFactor = 2,
                KernelWidth = 2,
                KernelHeight = 2
            };

            System.Reflection.Assembly assembly = this.GetType().Assembly;
            string fontAssetFolder = assembly.GetName().Name + ".fonts";
            // Get all embedded resource names
            string[] resourceNames = assembly.GetManifestResourceNames()
                                        .Where(name => name.StartsWith(fontAssetFolder))
                                        .ToArray();

            // 首先加载所有字体到字典中
            Dictionary<string, byte[]> fontBytes = new Dictionary<string, byte[]>();
            byte[] chineseFallbackFont = null;

            foreach (string resourceName in resourceNames)
            {
                Stream stream = assembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                    using (stream)
                    {
                        string[] rnameParts = resourceName.Split('.');
                        string fname = rnameParts[rnameParts.Length - 2];
                        
                        var memoryStream = new MemoryStream();
                        stream.CopyTo(memoryStream);
                        byte[] filebytes = memoryStream.ToArray();
                        
                        fontBytes[fname] = filebytes;
                        
                        // 检测中文字体（通常文件较大，>1MB）
                        if (fname.Contains("Noto") || fname.Contains("Source") || 
                            fname.Contains("Chinese") || fname.Contains("SC") ||
                            filebytes.Length > 1000000) // 大于1MB的字体可能包含中文
                        {
                            chineseFallbackFont = filebytes;
#if DEBUG
                            Log.Trace($"Detected Chinese font: {fname} ({filebytes.Length} bytes)");
#endif
                        }
                    }
            }

            // 现在创建FontSystem，为每个字体添加中文回退
            foreach (var kvp in fontBytes)
            {
                string fname = kvp.Key;
                byte[] filebytes = kvp.Value;

#if DEBUG
                Log.Trace($"Loading embedded font: {fname}");
#endif

                if (fname == EMBEDDED_FONT) //Special case for ImGui
                    ImGuiFont = filebytes;

                var fontSystem = new FontSystem(settings);
                fontSystem.AddFont(filebytes);
                
                // 如果有中文字体且当前字体不是中文字体，添加为回退
                if (chineseFallbackFont != null && filebytes != chineseFallbackFont)
                {
                    try
                    {
                        fontSystem.AddFont(chineseFallbackFont);
#if DEBUG
                        Log.Trace($"  Added Chinese fallback font to {fname}");
#endif
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"Failed to add Chinese fallback to {fname}: {ex.Message}");
                    }
                }
                
                _fonts[fname] = fontSystem;
            }
        }

        public SpriteFontBase GetFont(string name, float size)
        {
            if (_fonts.TryGetValue(name, out FontSystem font))
            {
                return font.GetFont(size);
            }

            if (_fonts.Count > 0)
                return _fonts.First().Value.GetFont(size);

            return null;
        }

        public SpriteFontBase GetFont(string name) => GetFont(name, 12);

        public string[] Fonts => _fonts.Keys.ToArray();
    }
}
