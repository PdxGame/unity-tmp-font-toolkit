using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

// TMP Font Toolkit - 字体烘焙核心
// 封装经过验证的完整流程：
//   1. CreateFontAsset(Dynamic) + TryAddCharacters 烘焙字形（Static 模式会拒绝添加）
//   2. 切换 AtlasPopulationMode.Static（字形固化在图集）
//   3. 创建材质并保存为子资产（CreateFontAsset 不会持久化材质！）
//   4. 图集导出为独立 PNG（内嵌纹理无法配置压缩）
//   5. 按平台预设设置纹理压缩（PC/Android/iOS...）
//   6. 可选：设为 TMP 默认字体
// 参考：UWA《从64MB降至5MB，TMP字体Atlas内存优化实战》+ 知乎《解决TMP中文字体Atlas占用过大》
namespace TMPFontToolkit
{
    public static class FontBaker
    {
        public class BakeSettings
        {
            public Font sourceFont;                 // 源字体
            public string chars;                    // 字符集（去重后）
            public int samplingPointSize = 48;      // 采样点
            public int atlasPadding = 9;            // 字形间距
            public int atlasSize = 1024;            // 图集边长
            public string outputAssetPath;          // 字体资产输出路径 (Assets/...)
            public string atlasPngPath;             // 图集 PNG 输出路径
            public bool setAsDefault;               // 设为 TMP 默认字体
        }

        // 生成字体资产，返回生成后的 TMP_FontAsset
        public static TMP_FontAsset Bake(BakeSettings s, System.Action<string> log)
        {
            log("烘焙 " + s.chars.Length + " 字符 -> " + s.atlasSize + "x" + s.atlasSize + " 图集 ...");

            // 1. Dynamic 创建 + 烘焙
            TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(
                s.sourceFont, s.samplingPointSize, s.atlasPadding, GlyphRenderMode.SDFAA,
                s.atlasSize, s.atlasSize, AtlasPopulationMode.Dynamic, true);

            if (fontAsset == null)
            {
                log("错误：CreateFontAsset 失败（检查源字体导入设置）");
                return null;
            }

            bool ok = fontAsset.TryAddCharacters(s.chars, out string missingChars);
            log("TryAddCharacters: " + ok + ", missing: " + missingChars.Length);
            if (missingChars.Length > 0 && missingChars.Length < 100)
                log("缺字: " + missingChars);

            // 2. 转 Static（字形固化）
            fontAsset.atlasPopulationMode = AtlasPopulationMode.Static;

            // 3. 创建材质并保存（CreateFontAsset 不持久化材质 → MissingReference 根源）
            Texture2D atlasTex = fontAsset.atlasTextures[0];
            Shader sdfShader = Shader.Find("TextMeshPro/Mobile/Distance Field");
            if (sdfShader == null) { log("错误：找不到 TMP SDF shader"); return null; }
            Material mat = new Material(sdfShader);
            mat.name = "SDF Material";
            mat.SetTexture("_MainTex", atlasTex);
            mat.SetFloat("_TextureWidth", s.atlasSize);
            mat.SetFloat("_TextureHeight", s.atlasSize);
            mat.SetFloat("_GradientScale", s.atlasPadding + 1);
            mat.SetFloat("_WeightNormal", fontAsset.normalStyle);
            mat.SetFloat("_WeightBold", fontAsset.boldStyle);

            // 4. 保存资产（字体 + 材质子资产）
            AssetDatabase.CreateAsset(fontAsset, s.outputAssetPath);
            AssetDatabase.AddObjectToAsset(mat, fontAsset);
            AssetDatabase.SaveAssets();
            log("字体资产已保存: " + s.outputAssetPath);

            // 5. 图集导出为独立 PNG（内嵌纹理无法配置压缩）。
            //    多图集：逐张导出为 _Atlas0.png, _Atlas1.png ...，全部挂到字体资产。
            for (int i = 0; i < fontAsset.atlasTextures.Length; i++)
            {
                Texture2D srcTex = fontAsset.atlasTextures[i];
                if (srcTex == null) continue;

                string pngPath = i == 0
                    ? s.atlasPngPath
                    : s.atlasPngPath.Replace(".png", i + ".png");

                Texture2D readable = new Texture2D(srcTex.width, srcTex.height, srcTex.format, false);
                Graphics.CopyTexture(srcTex, readable);
                byte[] png = readable.EncodeToPNG();
                Object.DestroyImmediate(readable);
                File.WriteAllBytes(Path.Combine(Application.dataPath, pngPath.Replace("Assets/", "")), png);
                AssetDatabase.ImportAsset(pngPath);
                AssetDatabase.SaveAssets();

                Texture2D pngTex = AssetDatabase.LoadAssetAtPath<Texture2D>(pngPath);
                fontAsset.atlasTextures[i] = pngTex;
                if (i == 0 && fontAsset.material != null)
                    fontAsset.material.SetTexture("_MainTex", pngTex);

                // 移除内嵌纹理（避免重复打包）
                AssetDatabase.RemoveObjectFromAsset(srcTex);
                AssetDatabase.SaveAssets();
                log("图集已导出: " + pngPath);
            }
            EditorUtility.SetDirty(fontAsset);
            AssetDatabase.SaveAssets();

            // 6. 平台压缩设置由 PlatformApplier 完成（在窗口里选择渠道）
            return fontAsset;
        }

        // 设置平台纹理压缩
        public static void ApplyPlatformCompression(string atlasPngPath, PlatformPreset preset, int maxTextureSize, System.Action<string> log)
        {
            TextureImporter importer = AssetImporter.GetAtPath(atlasPngPath) as TextureImporter;
            if (importer == null) { log("错误：PNG 不是 TextureImporter"); return; }

            // 默认平台：不压缩（编辑器可读），仅目标平台压缩
            TextureImporterPlatformSettings def = importer.GetPlatformTextureSettings("DefaultTexturePlatform");
            def.overridden = true;
            def.format = TextureImporterFormat.RGBA32;
            def.maxTextureSize = maxTextureSize;
            importer.SetPlatformTextureSettings(def);

            TextureImporterPlatformSettings ps = importer.GetPlatformTextureSettings(preset.platformKey);
            ps.overridden = true;
            ps.format = preset.format;
            ps.maxTextureSize = maxTextureSize;
            importer.SetPlatformTextureSettings(ps);

            importer.SaveAndReimport();
            log("已设置 " + preset.name + " 压缩: " + preset.format);
        }

        // 干跑测试：Dynamic 创建 → TryAddCharacters → 统计缺字与图集数 → 销毁（不保存）
        // 返回 null 表示测试失败（如字体加载问题）
        public class ProbeResult
        {
            public int missingChars;
            public int atlasCount;
        }

        public static ProbeResult DryRunProbe(Font font, string chars, int sample, int padding, int atlasSize)
        {
            try
            {
                TMP_FontAsset probe = TMP_FontAsset.CreateFontAsset(
                    font, sample, padding, GlyphRenderMode.SDFAA, atlasSize, atlasSize,
                    AtlasPopulationMode.Dynamic, true);
                if (probe == null) return null;
                probe.TryAddCharacters(chars, out string missing);
                var result = new ProbeResult
                {
                    missingChars = missing.Length,
                    atlasCount = probe.atlasTextures != null ? probe.atlasTextures.Length : 1
                };
                Object.DestroyImmediate(probe);
                return result;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[TMPFontToolkit] DryRunProbe failed: " + e.Message);
                return null;
            }
        }

        // 设为 TMP 默认字体（只改 TMP Settings，不影响场景已有组件）
        public static void SetAsDefaultFont(TMP_FontAsset fontAsset, System.Action<string> log)
        {
            TMP_Settings settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>("Assets/TextMesh Pro/Resources/TMP Settings.asset");
            if (settings == null)
            {
                log("警告：未找到 TMP Settings.asset，跳过默认字体设置");
                return;
            }
            SerializedObject so = new SerializedObject(settings);
            so.FindProperty("m_defaultFontAsset").objectReferenceValue = fontAsset;
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            log("已设为 TMP 默认字体");
        }

        // 批量替换场景中所有 TMP 组件的字体。
        // 覆盖所有场景（含未打开的）：文本级解析 .unity 文件，替换 TMP 组件的 m_fontAsset 引用。
        public static void ReplaceSceneFonts(TMP_FontAsset fontAsset, System.Action<string> log)
        {
            // 1. 当前已打开场景：遍历实例替换（保存后序列化到文件）
            int fixedCount = 0;
            var allTMP = Object.FindObjectsOfType<TMPro.TextMeshProUGUI>(true);
            foreach (var tmp in allTMP)
            {
                if (tmp.font != fontAsset)
                {
                    tmp.font = fontAsset;
                    tmp.ForceMeshUpdate();
                    fixedCount++;
                }
            }
            if (fixedCount > 0)
            {
                UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
                UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
            }

            // 2. 所有场景文件（含未打开的）：文本级替换 m_fontAsset 引用
            string scenesDir = Path.Combine(Application.dataPath, "Scenes");
            string oldGuid = GetFontGuid(fontAsset);
            string fallbackGuid = GetFontGuidByName("LiberationSans SDF");
            int fileFixed = 0;

            if (Directory.Exists(scenesDir) && !string.IsNullOrEmpty(oldGuid))
            {
                foreach (string file in Directory.GetFiles(scenesDir, "*.unity", SearchOption.AllDirectories))
                {
                    string content = File.ReadAllText(file, Encoding.UTF8);
                    string modified = content;

                    // TMP 组件结构: m_fontAsset: {fileID: X, guid: YYY, type: 2}
                    // 只替换旧默认字体（LiberationSans）的 guid → 新字体 guid（fileID 保留原值）；
                    // 其他字体不动（保留多字体场景）
                    if (!string.IsNullOrEmpty(fallbackGuid))
                    {
                        modified = System.Text.RegularExpressions.Regex.Replace(modified,
                            "(m_fontAsset: \\{fileID: (\\d+), guid: )" + fallbackGuid + "(, type: 2\\})",
                            "${1}" + oldGuid + "${3}");
                    }

                    if (modified != content)
                    {
                        File.WriteAllText(file, modified, new UTF8Encoding(true));
                        fileFixed++;
                        log("已替换场景文件: " + Path.GetFileName(file));
                    }
                }
            }

            AssetDatabase.SaveAssets();
            log("替换完成：已打开场景 " + fixedCount + " 个组件，场景文件 " + fileFixed + " 个");
        }

        // 获取字体资产的 guid
        static string GetFontGuid(TMP_FontAsset fontAsset)
        {
            string path = AssetDatabase.GetAssetPath(fontAsset);
            if (string.IsNullOrEmpty(path)) return null;
            string metaPath = path + ".meta";
            if (!File.Exists(metaPath)) return null;
            string meta = File.ReadAllText(metaPath, Encoding.UTF8);
            var m = System.Text.RegularExpressions.Regex.Match(meta, "guid: ([0-9a-f]{32})");
            return m.Success ? m.Groups[1].Value : null;
        }

        // 按名字找字体资产的 guid（用于定位旧默认字体）
        static string GetFontGuidByName(string fontName)
        {
            string[] guids = AssetDatabase.FindAssets(fontName + " t:TMP_FontAsset");
            foreach (string g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                if (path != null && path.Contains(fontName))
                    return g;
            }
            return null;
        }
    }
}
