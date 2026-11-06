# CodeSnap — 屏幕代码实时识别 + 一键输入悬浮窗

识别屏幕上形如 `HJK874`、`K9D7M2`、`IIE352` 等「字母+数字」代码，结果显示在置顶悬浮窗；按热键即可把代码自动输入到鼠标所指的输入框。

典型场景：游戏房间码 / 邀请码、抖音聊天、微信、Discord 等。

## 特性

- 使用 Windows 自带 OCR 引擎，离线运行，无云端服务
- 置顶小悬浮窗，可拖动到任意位置
- `Ctrl+Alt+F8`：在鼠标位置点击后粘贴/键入代码（可选自动回车）
- `Ctrl+Alt+F10`：框选要识别的屏幕区域
- 三种扫描范围：固定选区（最快）、鼠标周围、全屏
- 多帧多数票，避免闪烁和误报
- 屏幕出现新码时自动切换识别结果
- `Corrections` 修正表：处理 Windows OCR 的固定误读（例如把 `II` 读成 `N`/`H`/`u`）
- 免安装、无后台服务、无遥测

## 环境要求

- Windows 10 / 11（x64）
- **PowerShell 7.4+**（`pwsh.exe`）；不支持 Windows PowerShell 5.1
- Windows OCR 语言包（英文和/或中文），运行 `Check-Ocr.bat` 可自检
- `lib/` 中已附带两个必要的 .NET / WinRT 投影 DLL

> 程序主体是一个 C# 源文件，由 PowerShell 在启动时即时编译（约 1–2 秒），无需安装 .NET SDK 或 Visual Studio。

## 快速开始

1. 下载或克隆本仓库。
2. 双击 **`Check-Ocr.bat`**，看到 `识别结果: [HJK874]` 即环境正常；否则先安装 Windows OCR 语言功能。
3. 双击 **`CodeSnap.bat`**，右上角出现悬浮窗。
4. 让代码显示在屏幕上，按 **`Ctrl+Alt+F10`** 拖拽框选代码所在的小块区域。
   连续 1–2 帧一致后，悬浮窗显示识别结果。
5. 把鼠标移到要输入的输入框上，按 **`Ctrl+Alt+F8`**（或点悬浮窗的“发送”按钮）。
   悬浮窗会临时隐藏，在鼠标位置点击后粘贴/键入代码。

## 热键（均为 Ctrl+Alt 组合）

| 热键 | 功能 |
|---|---|
| `F6` | 保存诊断图与 OCR 原文到 `debug/` |
| `F7` | 切换扫描范围：选区 → 鼠标周围 → 全屏 |
| `F8` | 把已识别代码输入到鼠标位置 |
| `F9` | 标记 / 清除自动输出目标 |
| `F10` | 拖拽框选识别区域 |
| `F11` | 暂停 / 继续扫描 |
| `F12` | 退出 |

## 配置

配置保存在程序目录的 `config.json`（保存设置时生成），可参考 [`config.example.json`](config.example.json)。

| 键 | 默认 | 说明 |
|---|---|---|
| `Mode` | `full` | `region` / `cursor` / `full` |
| `RegionX/Y/W/H` | – | 固定扫描区域（物理像素） |
| `Regex` | `^[A-Z0-9]{4,12}$` | 命中规则 |
| `StrictShape` | `false` | `true` 时仅匹配“字母开头+数字结尾”风格 |
| `MinLetters` / `MinDigits` | `2` / `2` | 候选码中字母、数字的最少个数 |
| `Zoom` | `2` | 小区域送 OCR 前放大倍数（1–4） |
| `StableFrames` | `2` | 连续多少帧一致才确认 |
| `MissToClear` | `5` | 连续多少帧没有结果后清空当前码 |
| `OutputType` | `true` | `true`=逐字键入；`false`=剪贴板粘贴（Ctrl+V） |
| `AutoSend` | `false` | 识别到新码后自动发送到标记目标 |
| `AutoEnter` | `false` | 输入/粘贴后自动回车 |
| `ClickDelayMs` | `130` | 点击目标后的等待时间 |
| `TypeDelayMs` | `12` | 逐字输入间隔 |
| `ScanIntervalMs` | `10` | 扫描帧间空闲时间 |
| `OverrideCode` | `""` | 调试用：固定输出某个码（正常留空） |
| `Corrections` | `{}` | OCR 误读 → 正确码 的修正表 |

### 微信等聊天窗口推荐配置

```json
"OutputType": false,
"AutoEnter": true
```

剪贴板粘贴可绕过中文输入法（拼音）对字母的干扰，通常是把 ASCII 代码输入聊天框最可靠的方式；仅在希望插入后立即发送时再开启 `AutoEnter`。

## OCR 局限

Windows OCR 对连续的大写 `I` 有固定缺陷：`IIE352` 常被读成 `NE352`、`HE352` 或 `UE352`。这是引擎本身的限制，不是截图质量问题（即使用大号、清晰的字体渲染也能复现）。可用 `Corrections` 修正：

```json
"Corrections": {
  "NE352": "IIE352",
  "HE352": "IIE352",
  "UE352": "IIE352"
}
```

`0/O`、`1/I`、`2/Z` 等其它混淆同理。

## 隐私

所有处理都在本地完成，无网络访问、无遥测、无云端 OCR。

`codesnap.log` 与 `debug/` 目录可能包含从屏幕捕获的文字（包括聊天内容），**两者均已在 `.gitignore` 中忽略，请勿上传**。`config.json` 可能包含窗口坐标，同样已忽略。

## 第三方组件

`lib/Microsoft.Windows.SDK.NET.dll` 与 `lib/WinRT.Runtime.dll` 为 Microsoft / CsWinRT 的 MIT 许可组件，详见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。

## 许可证

MIT，详见 [LICENSE](LICENSE)。

---

English documentation: [README.md](README.md)