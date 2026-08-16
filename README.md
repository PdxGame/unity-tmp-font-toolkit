# TMP Font Toolkit（TMP 字体工具箱）

Unity 编辑器插件：一键生成并优化 TMP 中文字体资产。解决中文字体图集过大、生成流程繁琐、多平台压缩配置麻烦的问题。

- **原理参考**：UWA《从64MB降至5MB，TMP字体Atlas内存优化实战》+ 知乎《解决TextMeshPro中文字体Atlas占用过大的问题》
- **实测效果**：3500 字全量图集 16MB → 按需烘焙后 **0.6MB**（-96%），APK 整体 -20%

## 功能

| 功能 | 说明 |
|---|---|
| 字符集扫描 | 扫描场景（.unity 的 `m_text`）和 C# 脚本（字符串字面量），自动提取实际用字。支持当前场景 / 所有场景+脚本 / 自定义目录 |
| 三种字符来源 | 字符文件（任意文本格式）/ 扫描 / 手动输入 |
| 一键烘焙 | 源字体 + 字符集 → TMP 字体资产（Dynamic 烘焙 → Static 固化）|
| 图集导出 | 自动导出独立 PNG（内嵌纹理无法配置压缩）；**多图集逐张导出**（`_Atlas.png`、`_Atlas1.png`...）|
| 多平台压缩 | 同一图集按渠道配置压缩格式（PC DXT5 / Android ASTC 或 ETC2 / iOS ASTC / WebGL DXT5），实时预估体积 |
| 容量评估 | 手动触发干跑测试，用 TMP 真实打包结果判断字符集能否放入当前图集（非公式估算）|
| 设为默认字体 | 一键替换 TMP Settings 默认字体 |
| 替换场景字体 | 覆盖**所有场景（含未打开）**的 TMP 组件字体，文本级替换 + 确认弹窗 |
| Fallback 兜底 | 可选配置 LiberationSans 兜底，缺字不变方块 |
| 配置记忆 | 设置自动保存（EditorPrefs），下次打开恢复 |
| 悬停提示 | 所有控件带 Tooltip，鼠标悬停查看说明 |

## 使用

1. 菜单 `Tools > TMP Font Toolkit` 打开窗口
2. ① 选字符集来源（推荐"扫描场景+代码"）→ 开始扫描
3. ② 选源字体、图集尺寸、采样点、Padding
4. ③ 勾选目标平台渠道 → 点「评估容量」确认放得下 → 看体积预估
5. ⑤ 点「一键生成」（可选：再点「替换场景所有 TMP 字体」）

## 目录结构

```
Assets/TMPFontToolkit/
├── Editor/
│   ├── FontGeneratorWindow.cs   # 主窗口 UI + 按钮逻辑
│   ├── FontBaker.cs             # 烘焙核心 + 干跑测试 + 场景字体替换
│   ├── CharSetScanner.cs        # 场景 + 代码扫描
│   └── PlatformPresets.cs       # 平台压缩预设 + 体积估算
├── Fonts/
│   └── SmileySans.ttf           # 内置示例字体（得意黑，OFL 开源可商用）
├── Charsets/
│   └── GB2312_3500常用字.txt     # 标准 3500 常用字（《通用规范汉字表》一级字表）+ ASCII + 标点
└── README.md
```

## 内置字体

- **SmileySans.ttf（得意黑）**：内置示例字体，**OFL 1.1 开源许可，可自由使用/修改/分发（含商用）**
- 覆盖 9,240 字符（8,057 汉字，超过 GB2312 全量），测试任何文案都不会缺字
- 来源：https://github.com/atelier-anchor/smiley-sans （v2.0.1）
- 使用自己的字体时可直接删除 `Fonts/` 下文件，插件功能不受影响

## 参数说明

### 图集尺寸
字形图集的边长，决定能装多少字与纹理体积：
- **1024** ≈ 适合少量字符（<300 字），体积最小
- **2048 / 4096** ≈ 大量字符
- **8192**（慎用）≈ 单张装最多字，但部分老 GPU/移动设备不支持
- 太小会拆成多张图集（每张独立纹理/材质），太大浪费内存（体积 = 边长²）

### 采样点（Sampling Point Size）
决定最大清晰字号 ≈ **采样点 × 2**：
- **32** = 最大 ~60px 字号（HUD 小字）
- **48** = 最大 ~96px（常规 UI）
- **64** = 最大 ~128px（大标题）
- **90** = 最大 ~180px（超大标题）
- 采样越高图集占用越大（字形占格 ≈ 采样 × 1.4）

> 提示：90pt 采样下单张 1024 图集仅容纳 ~60 字——高采样是为"少而大"的标题设计的。正文/UI 用低采样可装更多字。建议按用途分字体（标题 90pt 小字集 / 正文 48pt 大字集）。

### Padding
字形之间的间距像素，防止 SDF 边缘互相渗色：
- **5** = 紧凑，小字号够用
- **9** = 标准推荐
- **14** = 保守，大字号/描边用

### 平台压缩格式
| 格式 | 适用 | 每像素 | 说明 |
|---|---|---|---|
| DXT5 | PC / WebGL | 1 字节 | PC GPU 标准 |
| ASTC 6x6 | Android / iOS | 0.44 字节 | 主流移动 GPU 支持，体积小画质好，**推荐** |
| ETC2 | Android | 0.5 字节 | 全设备兼容（含 2013 年前老设备）|

> 同一图集可同时勾选多平台，Unity 打包时自动按目标平台选用对应压缩格式，无需重复配置。

## 字符集说明

- **ASCII（数字/字母/符号）与常用中文标点自动包含**，无需写入字符文件
- 字符文件支持任意文本格式（.txt / .json / .csv 等 TextAsset）
- 插件附带标准字表：`Charsets/GB2312_3500常用字.txt`（教育部《通用规范汉字表》一级字表）
- 推荐工作流：先用"扫描场景+代码"收集实际用字（最小图集），再用「追加预留字」补充未来文案；需要全量覆盖时用 3500 字表

## 踩坑记录（实现经验）

- **TMP `CreateFontAsset` 以 `Static` 模式创建时，`TryAddCharacters` 直接拒绝**（"Unable to add characters... because its AtlasPopulationMode is set to Static"）→ 必须**先用 Dynamic 烘焙，再转 Static**
- **`CreateFontAsset` 创建的材质不会持久化**（`material: {fileID: 0}`）→ 需手动 `AddObjectToAsset`，否则运行时 MissingReferenceException
- **TMP 图集纹理是内嵌子资源**（NativeFormatImporter），**无法配置纹理压缩** → 必须导出为独立 PNG（TextureImporter）后按平台设置压缩
- **容量判断不要用公式估算**：TMP 打包受字体 em 尺寸/字形宽度影响，线性公式必然不准。用**干跑测试**（Dynamic 创建 → TryAddCharacters → 统计缺字与图集数 → 销毁）拿真实结果
- **多图集**：字符超单张容量时 TMP 自动扩展多张（`atlasTextures` 数组），**必须逐张导出 PNG 并全部挂到字体资产**，只导第一张会丢字形
- 干跑测试开销大（创建/销毁字体资产 ~1-2 秒），**只能手动触发 + 缓存**，绝不能放 OnGUI 每帧执行（会把编辑器卡死）
- `GlyphRenderMode` 在 `UnityEngine.TextCore.LowLevel` 命名空间；`TryAddCharacters` 的 out 参数是 `string`（不是 `uint[]`）
- 场景 TMP 组件字体被置空后会 fallback 到 LiberationSans 并**序列化保存**（显示为"已赋值"），不再跟随默认字体 → 需要「替换场景所有 TMP 字体」按钮显式替换

## 兼容性

- Unity 2022.3 LTS（TMP 3.0.7 验证）；**支持 Unity 6**（TMP 3.2 API 完全向后兼容，无破坏性变更）
- Unity 2021+ 应可用（TMP 3.x API 稳定）
- 纯 Editor 脚本（`Editor/` 目录），不影响运行时
- 无第三方依赖，拷贝 `TMPFontToolkit` 文件夹即用

## License

MIT
