using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;

// TMP Font Toolkit - 主窗口
// 菜单: Tools > TMP Font Toolkit
// 功能: 字符集扫描/编辑 → 字体烘焙 → 平台压缩 → 设为默认字体
namespace TMPFontToolkit
{
    public class FontGeneratorWindow : EditorWindow
    {
        // ---- 字符集 ----
        int charSourceMode = 0;          // 0=字符文件 1=扫描 2=手动
        TextAsset charFile;
        ScanScope scanScope = ScanScope.AllScenesAndScripts;
        string customFolder = "";
        string manualChars = "";
        List<ScanResult> scanResults = new List<ScanResult>();
        HashSet<char> scannedChars = new HashSet<char>();
        bool scanned = false;
        string extraChars = "";          // 扫描后手动追加的预留字

        // ---- 烘焙参数 ----
        Font sourceFont;
        int[] atlasSizes = { 512, 1024, 2048, 4096, 8192 };
        int atlasSizeIndex = 1;          // 默认 1024
        int[] sampleSizes = { 32, 48, 64, 90 };
        int sampleSizeIndex = 1;         // 默认 48
        int[] paddings = { 5, 9, 14 };
        int paddingIndex = 1;            // 默认 9
        string outputName = "";              // 空 = 自动按源字体命名 {fontName}_SDF
        string outputFolder = "Assets/Art/Font";
        bool setAsDefault = true;
        bool useFallback = false;

        // ---- 平台渠道 ----
        bool[] platformEnabled = { true, true, false, false }; // PC, Android(ASTC), Android(ETC2), iOS, WebGL? 见下
        // 渠道列表: PC / Android ASTC / Android ETC2 / iOS / WebGL
        int[] platformFormatChoice = { 0, 0, 0, 0, 0 }; // 0=ASTC 6x6 1=ASTC 8x8（Android 可选）
        bool[] platformChecks = { true, true, false, true, false }; // PC, Android, iOS, WebGL 默认

        // ---- UI 状态 ----
        Vector2 logScroll;
        List<string> logs = new List<string>();

        // 手动评估缓存：每字符源模式独立一份（点「评估容量」才干跑，结果按模式缓存）
        string[] probeCacheKey = new string[3];
        FontBaker.ProbeResult[] probeCache = new FontBaker.ProbeResult[3];
        int[] suggestedAtlas = new int[3];

        static string BuildProbeKey(int mode, string chars, int charCount, int sample, int padding, int atlasSize)
        {
            return "p|" + mode + "|" + charCount + "|" + sample + "|" + padding + "|" + atlasSize;
        }

        [MenuItem("Tools/TMP Font Toolkit")]
        public static void Open()
        {
            var w = GetWindow<FontGeneratorWindow>();
            w.titleContent = new GUIContent("TMP 字体工具箱");
            w.minSize = new Vector2(460, 620);
            w.Show();
        }

        void OnEnable()
        {
            // 恢复上次配置
            outputName = EditorPrefs.GetString("TMPFontToolkit.outputName", "");
            outputFolder = EditorPrefs.GetString("TMPFontToolkit.outputFolder", outputFolder);
            atlasSizeIndex = EditorPrefs.GetInt("TMPFontToolkit.atlasSize", atlasSizeIndex);
            sampleSizeIndex = EditorPrefs.GetInt("TMPFontToolkit.sampleSize", sampleSizeIndex);
            paddingIndex = EditorPrefs.GetInt("TMPFontToolkit.padding", paddingIndex);
            setAsDefault = EditorPrefs.GetBool("TMPFontToolkit.setDefault", setAsDefault);
            useFallback = EditorPrefs.GetBool("TMPFontToolkit.fallback", useFallback);
            charSourceMode = EditorPrefs.GetInt("TMPFontToolkit.charSource", charSourceMode);
        }

        void OnGUI()
        {
            EditorGUILayout.Space(4);
            DrawCharsetSection();
            DrawBakeSection();
            DrawPlatformSection();
            DrawOptionsSection();
            DrawActionSection();
            DrawLogSection();
        }

        // ① 字符集
        void DrawCharsetSection()
        {
            EditorGUILayout.LabelField(new GUIContent("① 字符集", "选择字符集来源：字符文件 / 扫描项目 / 手动输入"), EditorStyles.boldLabel);
            charSourceMode = GUILayout.Toolbar(charSourceMode, new[] { "字符文件", "扫描场景+代码", "手动输入" });

            if (charSourceMode == 0)
            {
                charFile = (TextAsset)EditorGUILayout.ObjectField(
                    new GUIContent("字符文件", "选择包含要烘焙字符的文本资产（任意文本格式）。文件里的所有字符都会进入字体，ASCII 数字/字母与常用标点自动包含"),
                    charFile, typeof(TextAsset), false);
            }
            else if (charSourceMode == 1)
            {
                EditorGUILayout.BeginHorizontal();
                scanScope = (ScanScope)EditorGUILayout.EnumPopup(
                    new GUIContent("扫描范围", "当前场景：只扫打开的\n所有场景+脚本：扫 Assets/Scenes 和 Assets/Scripts 全部（含未打开的）\n自定义目录：手动指定文件夹"), scanScope);
                EditorGUILayout.EndHorizontal();
                if (scanScope == ScanScope.CustomFolder)
                {
                    EditorGUILayout.BeginHorizontal();
                    customFolder = EditorGUILayout.TextField("自定义目录", customFolder);
                    if (GUILayout.Button("浏览", GUILayout.Width(50)))
                    {
                        string dir = EditorUtility.OpenFolderPanel("选择扫描目录", Application.dataPath, "");
                        if (!string.IsNullOrEmpty(dir)) customFolder = dir;
                    }
                    EditorGUILayout.EndHorizontal();
                }
                if (GUILayout.Button(new GUIContent("开始扫描", "扫描场景 TMP 文本和代码字符串字面量，提取实际用到的字符（毫秒级）")))
                {
                    RunScan();
                }
                if (scanned)
                {
                    EditorGUILayout.Space(2);
                    EditorGUILayout.LabelField("扫描结果:", EditorStyles.miniBoldLabel);
                    foreach (var r in scanResults)
                    {
                        if (r.charCount > 0)
                            EditorGUILayout.LabelField("  " + r.source + " → " + r.charCount + " 字");
                    }
                    EditorGUILayout.LabelField("合计 " + scannedChars.Count + " 字（去重）");
                    extraChars = EditorGUILayout.TextField(
                        new GUIContent("追加预留字", "扫描结果之外手动补充的字符（如未来 UI 文案用到的字），会合并进字符集"), extraChars);
                }
            }
            else
            {
                manualChars = EditorGUILayout.TextArea(manualChars, GUILayout.Height(80));
                EditorGUILayout.HelpBox("手动输入要烘焙的字符，ASCII 与常用标点自动包含", MessageType.None);
            }
            EditorGUILayout.Space(6);
        }

        void RunScan()
        {
            scanResults = CharSetScanner.Scan(scanScope, customFolder, Application.dataPath);
            scannedChars = CharSetScanner.MergeAll(scanResults);
            scanned = true;
            Log("扫描完成: " + scannedChars.Count + " 个字符");
        }

        // 当前有效字符集（去重，含 ASCII + 中文标点）
        string GetEffectiveChars()
        {
            var set = new HashSet<char>();
            // ASCII 可打印
            for (int c = 0x20; c <= 0x7E; c++) set.Add((char)c);
            // 中文标点
            foreach (char c in "，。！？；：、（）【】《》“”‘’…—·") set.Add(c);

            if (charSourceMode == 0 && charFile != null)
            {
                foreach (char c in charFile.text) { if (c != '\r' && c != '\n') set.Add(c); }
            }
            else if (charSourceMode == 1 && scanned)
            {
                foreach (char c in scannedChars) set.Add(c);
                foreach (char c in extraChars) set.Add(c);
            }
            else if (charSourceMode == 2)
            {
                foreach (char c in manualChars) { if (c != '\r' && c != '\n') set.Add(c); }
            }
            return new string(set.ToArray());
        }

        // ② 烘焙参数
        void DrawBakeSection()
        {
            EditorGUILayout.LabelField(new GUIContent("② 烘焙参数", "字体烘焙的核心参数：源字体、图集尺寸、采样点、Padding"), EditorStyles.boldLabel);
            Font newFont = (Font)EditorGUILayout.ObjectField(
                new GUIContent("源字体", "要烘焙的 TTF/OTF 字体文件。字形将按此字体渲染进图集"), sourceFont, typeof(Font), false);
            if (newFont != sourceFont)
            {
                // 换了字体：若输出名是空的或还是旧字体的自动名，则按新字体重命名
                if (string.IsNullOrEmpty(outputName) || outputName == "自动命名")
                    outputName = SanitizeName(newFont.name) + "_SDF";
                sourceFont = newFont;
            }
            string[] atlasLabels = { "512 x 512", "1024 x 1024", "2048 x 2048", "4096 x 4096", "8192 x 8192（慎用，老设备不支持）" };
            atlasSizeIndex = EditorGUILayout.Popup(
                new GUIContent("图集尺寸", "字形图集的边长。决定能装多少字与纹理体积：\n1024 ≈ 适合少量字符（<300字）\n2048/4096 ≈ 大量字符\n8192 ≈ 超大图集，单张装最多字，但部分老 GPU/移动设备不支持\n太小会拆成多张图集，太大浪费内存（体积 = 边长²）"), atlasSizeIndex, atlasLabels);
            sampleSizeIndex = EditorGUILayout.Popup(
                new GUIContent("采样点", "字形渲染精度，决定最大清晰字号 ≈ 采样点 × 2：\n32 = 最大 ~60px 字号（HUD 小字）\n48 = 最大 ~96px（常规 UI）\n64 = 最大 ~128px（大标题）\n90 = 最大 ~180px（超大标题）\n采样越高图集占用越大"), sampleSizeIndex, System.Array.ConvertAll(sampleSizes, x => x.ToString()));
            paddingIndex = EditorGUILayout.Popup(
                new GUIContent("Padding", "字形之间的间距像素，防止 SDF 边缘互相渗色：\n5 = 紧凑，小字号够用\n9 = 标准推荐\n14 = 保守，大字号/描边用"), paddingIndex, System.Array.ConvertAll(paddings, x => x.ToString()));
            if (string.IsNullOrEmpty(outputName))
                EditorGUILayout.HelpBox("输出名称留空将自动使用源字体名 + _SDF", MessageType.Info);
            outputName = EditorGUILayout.TextField(new GUIContent("输出名称（留空自动）", "生成的字体资产名。留空时自动为 源字体名_SDF（如 SmileySans_SDF）"), outputName);
            EditorGUILayout.BeginHorizontal();
            outputFolder = EditorGUILayout.TextField(new GUIContent("输出目录", "字体资产与图集 PNG 的输出文件夹（Assets 相对路径）"), outputFolder);
            if (GUILayout.Button("浏览", GUILayout.Width(50)))
            {
                string dir = EditorUtility.OpenFolderPanel("选择输出目录", Application.dataPath, "");
                if (!string.IsNullOrEmpty(dir))
                {
                    // 转为 Assets 相对路径
                    if (dir.StartsWith(Application.dataPath))
                        outputFolder = "Assets" + dir.Substring(Application.dataPath.Length);
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox("生成的文件将放入子目录: " + outputFolder + "/" + (sourceFont != null ? SanitizeName(sourceFont.name) : "{字体名}") + "/", MessageType.None);
            EditorGUILayout.Space(6);
        }

        // ③ 平台压缩 + 预估
        void DrawPlatformSection()
        {
            EditorGUILayout.LabelField(new GUIContent("③ 平台压缩 + 体积预估", "为不同发布平台配置纹理压缩格式。同一图集可同时配置多套，打包时自动按目标平台选用。\nDXT5 = PC/WebGL\nASTC 6x6 = 安卓/iOS 主流（体积小画质好）\nETC2 = 安卓全兼容（老设备）"), EditorStyles.boldLabel);

            int atlasSize = atlasSizes[atlasSizeIndex];
            int sample = sampleSizes[sampleSizeIndex];
            int padding = paddings[paddingIndex];
            string chars = GetEffectiveChars();
            int charCount = chars.Length;
            int mode = charSourceMode;

            // 手动评估：点按钮才干跑测试；结果按模式独立缓存
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("评估容量（当前 " + atlasSize + " × 采样 " + sample + "）", "用 TMP 真实打包测试当前字符集能否放入当前图集，返回实际图集张数与缺字数（不保存任何文件，耗时约 1-2 秒）"), GUILayout.Width(260)))
            {
                if (charCount == 0) { Log("错误：字符集为空，无法评估"); }
                else if (sourceFont == null) { Log("错误：请先选择源字体"); }
                else
                {
                    probeCacheKey[mode] = BuildProbeKey(mode, chars, charCount, sample, padding, atlasSize);
                    probeCache[mode] = FontBaker.DryRunProbe(sourceFont, chars, sample, padding, atlasSize);
                    suggestedAtlas[mode] = atlasSize;
                    if (probeCache[mode] != null && (probeCache[mode].missingChars > 0 || probeCache[mode].atlasCount > 1))
                    {
                        int need = atlasSize;
                        while (need < 8192)
                        {
                            var p2 = FontBaker.DryRunProbe(sourceFont, chars, sample, padding, need * 2);
                            if (p2 != null && p2.missingChars == 0 && p2.atlasCount <= 1) { need *= 2; break; }
                            need *= 2;
                        }
                        suggestedAtlas[mode] = need;
                    }
                    Log("评估完成: " + probeCache[mode].atlasCount + " 张图集, 缺 " + probeCache[mode].missingChars + " 字");
                }
            }
            if (GUILayout.Button("清除评估", GUILayout.Width(90)))
            {
                probeCacheKey[mode] = "";
                probeCache[mode] = null;
            }
            EditorGUILayout.EndHorizontal();

            // 过期检测：当前参数与该模式评估时的快照不同则提示重新评估（各模式独立）
            string currentKey = sourceFont != null ? BuildProbeKey(mode, chars, charCount, sample, padding, atlasSize) : "";
            bool stale = probeCache[mode] != null && probeCacheKey[mode] != currentKey;
            bool hasContent = charCount > 0;

            var probe = (stale || !hasContent) ? null : probeCache[mode];
            int atlasCount = (probe != null && probe.atlasCount > 0) ? probe.atlasCount : 1;
            if (stale && hasContent)
            {
                EditorGUILayout.HelpBox("⚠️ 参数已变更，显示的评估结果已过期 —— 请重新点击「评估容量」", MessageType.Warning);
            }
            else if (probe != null)
            {
                string msg = string.Format(
                    "当前 {0} 图集 × 采样 {1}：{2} 字 → {3}，实际需要 {4} 张图集",
                    atlasSize, sample, charCount,
                    probe.missingChars == 0 ? "✅ 全部放入" : "⚠️ 缺 " + probe.missingChars + " 字",
                    probe.atlasCount);
                if (probe.missingChars > 0 || probe.atlasCount > 1)
                    EditorGUILayout.HelpBox(msg + " → 建议图集 " + suggestedAtlas[mode] + "（单张放下）或保持多图集", MessageType.Warning);
                else
                    EditorGUILayout.HelpBox(msg, MessageType.Info);
            }
            else if (hasContent)
            {
                EditorGUILayout.HelpBox("点击「评估容量」查看当前配置能否放下 " + charCount + " 字", MessageType.Info);
            }
            // 当前模式未设置字符：保持安静

            string[] channelNames = { "PC (DXT5)", "Android (ASTC 6x6)", "Android (ETC2)", "iOS (ASTC 6x6)", "WebGL (DXT5)" };
            // 平台列表（PC/Android ASTC/Android ETC2/iOS/WebGL）
            var presets = new[] { PlatformPresets.All[0], PlatformPresets.All[1], PlatformPresets.All[2], PlatformPresets.All[3], PlatformPresets.All[4] };

            for (int i = 0; i < presets.Length; i++)
            {
                EditorGUILayout.BeginHorizontal();
                platformChecks[i] = EditorGUILayout.Toggle(platformChecks[i], GUILayout.Width(18));
                EditorGUILayout.LabelField(new GUIContent(channelNames[i], GetPlatformTooltip(i)), GUILayout.Width(170));
                // 预估体积 = 单张体积 × 评估出的图集张数（未评估则单张估算）
                long est = PlatformPresets.EstimateSizeBytes(presets[i], atlasSize, 0) * atlasCount;
                string suffix = probe != null ? (" × " + atlasCount + " 张 = " + PlatformPresets.FormatSize(est)) : " 预估(单张)";
                EditorGUILayout.LabelField("→ " + PlatformPresets.FormatSize(est) + suffix);
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.Space(6);
        }

        static string GetPlatformTooltip(int i)
        {
            switch (i)
            {
                case 0: return "PC/Windows：DXT5 压缩，1 字节/像素";
                case 1: return "Android：ASTC 6x6 压缩（2013年后设备均支持），体积小画质好，推荐";
                case 2: return "Android：ETC2 压缩（全设备兼容），比 ASTC 略大略差，仅老设备需要";
                case 3: return "iOS：ASTC 6x6 压缩，iPhone/iPad 全支持";
                case 4: return "WebGL：DXT5 压缩";
                default: return "";
            }
        }

        // ④ 选项
        void DrawOptionsSection()
        {
            EditorGUILayout.LabelField(new GUIContent("④ 选项", "生成后的附加操作"), EditorStyles.boldLabel);
            setAsDefault = EditorGUILayout.Toggle(new GUIContent("设为 TMP 默认字体", "生成后自动替换 TMP Settings 的默认字体（影响之后新建的 TMP 文本）"), setAsDefault);
            useFallback = EditorGUILayout.Toggle(new GUIContent("配置 Fallback 兜底 (LiberationSans)", "把 TMP 内置 LiberationSans 设为兜底字体：遇到字符集外的字时自动用它显示，不会变方块"), useFallback);
            EditorGUILayout.Space(6);
        }

        // ⑤ 操作按钮
        void DrawActionSection()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("一键生成", "完整流程：烘焙字形 → 导出图集 PNG → 设置勾选的平台压缩 → 可选设为默认字体/Fallback"), GUILayout.Height(32)))
                RunFullBake();
            if (GUILayout.Button(new GUIContent("设为默认字体", "仅替换 TMP Settings 的默认字体，不影响场景已有文本"), GUILayout.Height(32)))
                RunSetDefault();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("替换场景所有 TMP 字体", "把 Assets/Scenes 下所有场景（含未打开的）中所有 TMP 文本组件的字体替换为生成的字体（有确认弹窗）"), GUILayout.Height(32)))
                RunReplaceSceneFonts();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(6);
        }

        // ⑥ 日志
        void DrawLogSection()
        {
            EditorGUILayout.LabelField("⑥ 日志", EditorStyles.boldLabel);
            logScroll = EditorGUILayout.BeginScrollView(logScroll, GUILayout.Height(140));
            foreach (var l in logs)
                EditorGUILayout.LabelField(l);
            EditorGUILayout.EndScrollView();
        }

        void Log(string msg)
        {
            logs.Add("[" + System.DateTime.Now.ToString("HH:mm:ss") + "] " + msg);
            logScroll.y = float.MaxValue;
            Debug.Log("[TMPFontToolkit] " + msg);
        }

        // ---- 动作实现 ----

        void RunFullBake()
        {
            if (sourceFont == null) { Log("错误：请选择源字体"); return; }
            if (string.IsNullOrEmpty(outputName))
                outputName = SanitizeName(sourceFont.name) + "_SDF";
            string chars = GetEffectiveChars();
            if (chars.Length == 0) { Log("错误：字符集为空"); return; }

            // 每个字体一个子目录：{输出目录}/{字体名}/，避免多字体平铺混乱
            string subDir = SanitizeName(sourceFont.name);
            string assetPath = outputFolder + "/" + subDir + "/" + outputName + ".asset";
            string pngPath = outputFolder + "/" + subDir + "/" + outputName + "_Atlas.png";

            // 确保目录存在
            string fullDir = Path.Combine(Application.dataPath, (outputFolder + "/" + subDir).Replace("Assets/", ""));
            Directory.CreateDirectory(fullDir);
            Log("输出目录: " + outputFolder + "/" + subDir);

            // 清理旧资产
            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath) != null)
                AssetDatabase.DeleteAsset(assetPath);
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(pngPath) != null)
                AssetDatabase.DeleteAsset(pngPath);
            AssetDatabase.SaveAssets();

            // 烘焙
            var settings = new FontBaker.BakeSettings
            {
                sourceFont = sourceFont,
                chars = chars,
                samplingPointSize = sampleSizes[sampleSizeIndex],
                atlasPadding = paddings[paddingIndex],
                atlasSize = atlasSizes[atlasSizeIndex],
                outputAssetPath = assetPath,
                atlasPngPath = pngPath,
                setAsDefault = setAsDefault
            };
            var fontAsset = FontBaker.Bake(settings, Log);
            if (fontAsset == null) { Log("错误：烘焙失败"); return; }

            // 平台压缩（所有图集：_Atlas.png, _Atlas1.png, _Atlas2.png ...）
            var presets = new[] { PlatformPresets.All[0], PlatformPresets.All[1], PlatformPresets.All[2], PlatformPresets.All[3], PlatformPresets.All[4] };
            bool anyApplied = false;
            for (int i = 0; i < presets.Length; i++)
            {
                if (platformChecks[i])
                {
                    // 压缩全部图集
                    for (int atlasIdx = 0; ; atlasIdx++)
                    {
                        string p = atlasIdx == 0 ? pngPath : pngPath.Replace(".png", atlasIdx + ".png");
                        if (AssetDatabase.LoadAssetAtPath<Texture2D>(p) == null) break;
                        FontBaker.ApplyPlatformCompression(p, presets[i], atlasSizes[atlasSizeIndex], Log);
                    }
                    anyApplied = true;
                }
            }
            if (!anyApplied) Log("提示：未勾选任何平台压缩");

            // Fallback
            if (useFallback)
            {
                TMP_FontAsset fb = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                    "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
                if (fb != null)
                {
                    if (fontAsset.fallbackFontAssetTable == null)
                        fontAsset.fallbackFontAssetTable = new List<TMP_FontAsset>();
                    fontAsset.fallbackFontAssetTable.Clear();
                    fontAsset.fallbackFontAssetTable.Add(fb);
                    EditorUtility.SetDirty(fontAsset);
                    AssetDatabase.SaveAssets();
                    Log("已配置 Fallback: LiberationSans SDF");
                }
            }

            // 默认字体
            if (setAsDefault)
            {
                FontBaker.SetAsDefaultFont(fontAsset, Log);
            }

            // 保存配置
            SavePrefs();
            Log("✅ 全部完成！字体: " + assetPath);
        }

        void RunSetDefault()
        {
            if (sourceFont == null) { Log("错误：请先选择源字体（用于定位字体资产）"); return; }
            string subDir = SanitizeName(sourceFont.name);
            string assetPath = outputFolder + "/" + subDir + "/" + outputName + ".asset";
            var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (fa == null) { Log("错误：找不到 " + assetPath + "，请先生成"); return; }
            FontBaker.SetAsDefaultFont(fa, Log);
        }

        // 替换场景所有 TMP 组件的字体（全部强制换成生成的字体）
        void RunReplaceSceneFonts()
        {
            if (sourceFont == null) { Log("错误：请先选择源字体（用于定位字体资产）"); return; }
            string subDir = SanitizeName(sourceFont.name);
            string assetPath = outputFolder + "/" + subDir + "/" + outputName + ".asset";
            var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (fa == null) { Log("错误：找不到 " + assetPath + "，请先生成"); return; }
            if (!EditorUtility.DisplayDialog("替换场景字体",
                    "将把当前所有场景中所有 TMP 文本组件的字体替换为 " + fa.name + "，确定？",
                    "确定", "取消"))
                return;
            FontBaker.ReplaceSceneFonts(fa, Log);
        }

        void SavePrefs()
        {
            EditorPrefs.SetString("TMPFontToolkit.outputName", outputName);
            EditorPrefs.SetString("TMPFontToolkit.outputFolder", outputFolder);
            EditorPrefs.SetInt("TMPFontToolkit.atlasSize", atlasSizeIndex);
            EditorPrefs.SetInt("TMPFontToolkit.sampleSize", sampleSizeIndex);
            EditorPrefs.SetInt("TMPFontToolkit.padding", paddingIndex);
            EditorPrefs.SetBool("TMPFontToolkit.setDefault", setAsDefault);
            EditorPrefs.SetBool("TMPFontToolkit.fallback", useFallback);
            EditorPrefs.SetInt("TMPFontToolkit.charSource", charSourceMode);
        }

        // 去掉字体名里不适合做资产名的字符
        static string SanitizeName(string name)
        {
            var chars = name.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray();
            string s = new string(chars);
            return string.IsNullOrEmpty(s) ? "Font" : s;
        }
    }
}
