using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

// TMP Font Toolkit - 字符集扫描器
// 扫描场景（.unity 的 m_text 字段）和 C# 脚本（字符串字面量）提取实际用到的字符。
// 纯文本读取，不需要打开场景，毫秒级完成。
namespace TMPFontToolkit
{
    public enum ScanScope
    {
        CurrentScene,      // 只扫当前打开的场景
        AllScenesAndScripts, // 扫 Assets/Scenes + Assets/Scripts 全部
        CustomFolder       // 自定义目录
    }

    public class ScanResult
    {
        public string source;        // 来源描述（场景/脚本路径）
        public int charCount;        // 该来源贡献的字符数
        public HashSet<char> chars;  // 该来源的字符集合
    }

    public static class CharSetScanner
    {
        // 扫描入口：返回各来源的字符统计 + 合并后的字符集
        public static List<ScanResult> Scan(ScanScope scope, string customFolder, string projectAssetsPath)
        {
            var results = new List<ScanResult>();
            var sceneFiles = new List<string>();
            var scriptFiles = new List<string>();

            switch (scope)
            {
                case ScanScope.CurrentScene:
                    var active = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
                    if (!string.IsNullOrEmpty(active.path))
                        sceneFiles.Add(Path.Combine(projectAssetsPath, active.path.Replace("Assets/", "")));
                    break;
                case ScanScope.AllScenesAndScripts:
                    sceneFiles.AddRange(FindFiles(Path.Combine(projectAssetsPath, "Scenes"), "*.unity"));
                    scriptFiles.AddRange(FindFiles(Path.Combine(projectAssetsPath, "Scripts"), "*.cs", true));
                    break;
                case ScanScope.CustomFolder:
                    if (!string.IsNullOrEmpty(customFolder) && Directory.Exists(customFolder))
                    {
                        sceneFiles.AddRange(FindFiles(customFolder, "*.unity", true));
                        scriptFiles.AddRange(FindFiles(customFolder, "*.cs", true));
                    }
                    break;
            }

            // 扫描场景（m_text 字段，含 \u 转义解码）
            foreach (var f in sceneFiles)
            {
                var chars = ScanSceneFile(f);
                results.Add(new ScanResult
                {
                    source = Path.GetFileName(f),
                    charCount = chars.Count,
                    chars = chars
                });
            }

            // 扫描脚本（字符串字面量，跳过注释行）
            foreach (var f in scriptFiles)
            {
                var chars = ScanScriptFile(f);
                results.Add(new ScanResult
                {
                    source = Path.GetFileName(f),
                    charCount = chars.Count,
                    chars = chars
                });
            }

            return results;
        }

        // 扫描单个场景文件：提取 m_text: "..." 并解码 \uXXXX
        static HashSet<char> ScanSceneFile(string path)
        {
            var result = new HashSet<char>();
            try
            {
                string content = File.ReadAllText(path, Encoding.UTF8);
                var matches = Regex.Matches(content, "m_text: \"([^\"]*)\"");
                foreach (Match m in matches)
                {
                    string decoded = Regex.Replace(m.Groups[1].Value, "\\\\u([0-9a-fA-F]{4})",
                        mm => ((char)Convert.ToInt32(mm.Groups[1].Value, 16)).ToString());
                    foreach (char c in decoded)
                        AddIfCjkOrUseful(result, c);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[TMPFontToolkit] 扫描场景失败 " + path + ": " + e.Message);
            }
            return result;
        }

        // 扫描单个脚本文件：提取 "..." 字符串字面量里的字符（跳过注释行）
        static HashSet<char> ScanScriptFile(string path)
        {
            var result = new HashSet<char>();
            try
            {
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                foreach (string line in lines)
                {
                    string t = line.Trim();
                    if (t.StartsWith("//") || t.StartsWith("*") || t.StartsWith("/*"))
                        continue;
                    var matches = Regex.Matches(line, "\"([^\"]*)\"");
                    foreach (Match m in matches)
                    {
                        foreach (char c in m.Groups[1].Value)
                            AddIfCjkOrUseful(result, c);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[TMPFontToolkit] 扫描脚本失败 " + path + ": " + e.Message);
            }
            return result;
        }

        // 只收录 CJK 汉字/标点/全角符号（数字字母走 ASCII，不占图集）
        static void AddIfCjkOrUseful(HashSet<char> set, char c)
        {
            int code = c;
            bool isCJK = (code >= 0x4E00 && code <= 0x9FFF)          // 汉字
                      || (code >= 0x3000 && code <= 0x303F)          // CJK 标点
                      || (code >= 0xFF00 && code <= 0xFFEF)          // 全角符号
                      || code == 0x00B7 || code == 0x2026 || code == 0x2014 // · … —
                      || code == 0x2018 || code == 0x2019            // ‘ ’
                      || code == 0x201C || code == 0x201D;           // “ ”
            if (isCJK)
                set.Add(c);
        }

        // 合并所有来源的字符
        public static HashSet<char> MergeAll(List<ScanResult> results)
        {
            var merged = new HashSet<char>();
            foreach (var r in results)
                foreach (var c in r.chars)
                    merged.Add(c);
            return merged;
        }

        static List<string> FindFiles(string dir, string pattern, bool recursive = false)
        {
            var list = new List<string>();
            if (!Directory.Exists(dir)) return list;
            try
            {
                list.AddRange(Directory.GetFiles(dir, pattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[TMPFontToolkit] 遍历目录失败 " + dir + ": " + e.Message);
            }
            return list;
        }
    }
}
