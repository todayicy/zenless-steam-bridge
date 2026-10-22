# Zenless Steam Bridge · 绝区零 + 星穹铁道双启动版

基于 [SoledadAki/endfield-steam-bridge](https://github.com/SoledadAki/endfield-steam-bridge) v0.2.4 双游戏选择界面，改为选择启动 **绝区零** 或 **崩坏：星穹铁道**。

## 使用

1. 登录 Steam，将《绝区零》添加入库（AppID **4162040**）。无需先下载 Steam 国际服。
2. 用米哈游启动器安装、更新游戏，确认对应客户端可正常进入，再退出游戏。
3. 以管理员身份运行 `ZenlessSteamBridge.exe`。
4. 分别选择绝区零的 **ZenlessZoneZero.exe** 和星穹铁道的 **StarRail.exe**。至少设置一款；未设置的游戏在选择界面中禁用。
5. 首次点击“载入双游戏”；已有旧版则点击“修复启动入口”，等待 Steam 重启。
6. 从 Steam《绝区零》开始游戏，出现双卡片窗口后选择要启动的游戏。关闭窗口或按 Esc 会取消，不启动游戏。

“预览选择界面”只预览界面，不会启动游戏。

**统一通过 Steam《绝区零》入口启动，两款游戏的 Steam 游玩时长与截图均归在《绝区零》名下。** 本版本不提供星穹铁道独立 Steam 入口。

## 0.2.2 启动入口修正

旧版只写入 Steam 安装清单并复制前端，没有处理当前账号已有的启动选项，也没有检查 Steam 启动入口。配置页显示“载入完成”不能证明卡片程序实际被 Steam 执行。

本版会备份当前账号的 `localconfig.vdf`，只修改《绝区零》的 `LaunchOptions`，明确设置为：

```text
"选择界面 ZenlessBridge.exe 的完整路径" %command%
```

Steam 启动稳定目录中的选择界面，选择后由辅助程序直接启动相应游戏 EXE；传入的官方启动命令不再执行。写入后重新读取启动项，并检查组件哈希、目录标记和安装清单，全部通过才显示文件校验成功。程序仍需在真实 Windows + Steam 环境确认实际联动。

修正管理界面的 96 DPI 缩放基准；文字、按钮使用自动行高，窗口较小时可滚动。

保留 0.2.1 的 `String.Split` 兼容修复：使用 .NET Framework 支持的 Char[] 重载，Linux 构建强制使用微软官方 .NET Framework 4.8 参考程序集，并审查生成的调用。

## 从上一版升级

退出游戏，以管理员身份运行本版 `ZenlessSteamBridge.exe`。确认两款游戏路径后，直接点击 **“修复启动入口”**。它会更新已安装的选择界面和辅助程序、修复当前账号的 Steam 启动选项，同时保留原始撤回备份。无需先撤回旧版。

更改客户端路径后，也点击“修复启动入口”应用设置。修复失败时回滚到操作前的组件、配置和启动项。

如果仍出现官方启动器：点击“重新检测”，查看“启动入口”一栏；“复制 Steam 启动项”可用于核对 Steam → 绝区零 → 属性 → 通用 → 启动选项。点击 **“导出诊断”** 可以提供实际启动项、组件版本、安装记录及最近一次选择界面运行记录。诊断文件不包含完整 Steam 账号配置。

## 图片与界面

使用原项目双游戏版的卡片渲染、悬停/焦点、最小化、关闭和键盘选择方式。图片替换为米哈游启动器公开提供的绝区零、星穹铁道官方宣传图，内嵌于程序，无需联网下载。

图片原始地址、获取日期和文件哈希见源码中的 `../assets/SOURCES.json`；图片版权归相应权利人，不属于代码 MIT 授权范围。为兼容 .NET Framework 解码，保留原画面与分辨率无损转为 PNG。

## 启动与还原

- 按所选客户端自身区服启动，不修改官服/B服/国际服渠道、账号或游戏配置。
- 直接运行本机游戏 EXE，游戏更新仍用米哈游启动器。
- Steam 的统一 AppID 为 `4162040`；启动选择通过请求记录传给管理员辅助程序，辅助程序重新验证目标文件和游戏类型。
- 保留原 Steam 安装清单与原启动项备份，安装失败会回滚；撤回时只还原本工具设置的启动项，保留用户之后自行改动的设置。
- Steam 停止游戏时，只处理本次会话所记录、路径与创建时间匹配的游戏进程。
- 通过目录标记避免误删；遇到外部安装记录改动或标记不匹配时停止还原。
- 状态与备份：`%LOCALAPPDATA%\ZenlessSteamBridge`。撤回成功后保留历史备份。

Steam 验证或自动更新可能覆盖桥接文件。可用“重新检测”检查，再用“修复启动入口”修复。载入、修复和撤回都会正常退出并重启 Steam，保持它原有运行权限。启动项仅适用于载入时的 Steam 账号；切换账号会提示重新处理。

## 环境与验证范围

Windows 10/11 x64，.NET Framework 4.8。成品不需要 Python、Node.js 或 Mono。

已交叉编译 Windows x64 程序，并运行核心逻辑、嵌入组件及双卡片界面测试。详细结果见 `VALIDATION.txt`。**尚未完成 Windows + Steam + 真实游戏客户端的端到端实测**：真实游戏启动、Steam 计时、停止按钮、Shift+Tab 叠加层和 F12 截图需要在本机确认。

## 源码构建

Windows PowerShell，进入源码目录：

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
powershell -ExecutionPolicy Bypass -File .\test.ps1
powershell -ExecutionPolicy Bypass -File .\package.ps1
```

也可使用 `ZenlessSteamBridge.csproj`。打包脚本先运行测试，输出含成品、说明、图片和源码的 ZIP。

已安装 Mono 的 Linux 还需要微软官方 [Microsoft.NETFramework.ReferenceAssemblies.net48 1.0.3](https://www.nuget.org/packages/Microsoft.NETFramework.ReferenceAssemblies.net48/1.0.3)。下载并解压该 NuGet 包，将 `build/.NETFramework/v4.8` 作为参考程序集目录：

```bash
python3 build-mono.py --framework-reference-dir /path/to/net48/build/.NETFramework/v4.8 --test
```

也可用环境变量 `NET48_REFERENCE_DIR` 指定目录。脚本会拒绝缺少 Framework 4.8 参考程序集或误用 Mono 实现程序集的构建。

管理程序 UAC 清单为 `requireAdministrator`，Steam 前端为 `asInvoker`。图片同时嵌入两者，确保 Steam 中也能打开选择界面。源码测试程序支持 `--ui <管理程序路径>`，需要可用的图形显示环境。

## 来源

原项目 MIT 许可证保留于 LICENSE。双游戏界面基线：`24f126a671a17be27ecc6ad0c5a681070dbe6b02`（2026-10-22，v0.2.4）。本版本为本次独立修改，未发布到原作者仓库。
