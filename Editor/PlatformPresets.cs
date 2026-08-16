using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// TMP Font Toolkit - 平台压缩预设与体积估算
// 每个平台渠道定义：构建目标、纹理压缩格式、每像素字节数（用于估算）。
namespace TMPFontToolkit
{
    [Serializable]
    public class PlatformPreset
    {
        public string name;              // 显示名
        public BuildTarget buildTarget;  // 构建目标
        public string platformKey;       // TextureImporter 平台键
        public TextureImporterFormat format; // 压缩格式
        public float bytesPerPixel;      // 压缩后每像素字节数（估算用）
        public bool enabled;             // 窗口中的勾选状态

        public PlatformPreset(string name, BuildTarget target, string key, TextureImporterFormat format, float bpp, bool enabled = true)
        {
            this.name = name;
            this.buildTarget = target;
            this.platformKey = key;
            this.format = format;
            this.bytesPerPixel = bpp;
            this.enabled = enabled;
        }
    }

    public static class PlatformPresets
    {
        // 各渠道预设（bytesPerPixel 为压缩后每像素字节数）：
        //  - ASTC 6x6: 16 字节 / 36 像素 = 0.444 BPP
        //  - ASTC 8x8: 16 字节 / 64 像素 = 0.25 BPP
        //  - ETC2 RGBA8: 8 字节 / 16 像素 = 0.5 BPP
        //  - DXT5: 1 BPP
        //  - RGBA32: 4 BPP（不压缩）
        public static readonly PlatformPreset[] All = new PlatformPreset[]
        {
            new PlatformPreset("PC / Windows (DXT5)", BuildTarget.StandaloneWindows64, "Standalone", TextureImporterFormat.DXT5, 1.0f),
            new PlatformPreset("Android (ASTC 6x6)", BuildTarget.Android, "Android", TextureImporterFormat.ASTC_6x6, 0.444f),
            new PlatformPreset("Android (ETC2)", BuildTarget.Android, "Android", TextureImporterFormat.ETC2_RGBA8, 0.5f),
            new PlatformPreset("iOS (ASTC 6x6)", BuildTarget.iOS, "iPhone", TextureImporterFormat.ASTC_6x6, 0.444f),
            new PlatformPreset("WebGL (DXT5)", BuildTarget.WebGL, "WebGL", TextureImporterFormat.DXT5, 1.0f),
        };

        // 估算压缩后纹理大小（字节）。maxTextureSize 为图集边长。
        public static long EstimateSizeBytes(PlatformPreset preset, int atlasSize, int glyphCount)
        {
            // 图集面积 × 每像素字节数；字符数少时实际利用率低，但纹理按全尺寸存储
            long bytes = (long)atlasSize * atlasSize;
            return (long)(bytes * preset.bytesPerPixel);
        }

        // 估算为可读字符串
        public static string FormatSize(long bytes)
        {
            if (bytes >= 1024 * 1024) return string.Format("{0:F2} MB", bytes / (1024.0 * 1024.0));
            if (bytes >= 1024) return string.Format("{0:F1} KB", bytes / 1024.0);
            return bytes + " B";
        }
    }
}
