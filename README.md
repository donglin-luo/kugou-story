# 听见 · 酷狗歌曲故事

一个 Windows 桌面伴随插件：自动识别酷狗正在播放的歌曲，**后台检索网页、读取原文、生成并核对故事**，用 **100 字以内**讲述歌曲背后的故事，在可拖动、可置顶的小窗中展示。与酷狗并行运行，不改动酷狗文件。全部用 **C# / .NET 8 / WPF** 实现，不使用 Python，也不需要 Node.js 才能运行。当前版本 **1.1.2**，默认小窗为 420 × 450，升级时会同步扩大此前保存的小尺寸，为完整介绍预留空间。

## 直接使用

从 [GitHub Releases 下载最新免安装版](https://github.com/donglin-luo/kugou-story/releases/latest)，解压压缩包后双击 `KugouStory.exe`。免安装包包含 .NET 运行环境，无需安装开发工具。

1. 双击 `KugouStory.exe`。开发目录中的免安装版本位于 `dist/KugouStory-win-x64/`，也可以双击根目录的 `run.cmd`。
2. 点击右上角 **⚙**，填入自己的 **DeepSeek API Key**，点击“保存并应用”。可先用“测试连接”检查完整检索与生成流程，测试也会调用搜索、读取和模型 API。升级会保留此前已保存的本机 Key。
3. 在酷狗中播放歌曲。约 1～3 秒完成稳定识别，后台会显示搜索、读取、核对的进度；完成后显示介绍和“已对照资料 · N 个来源”。暂停时保留当前介绍；换歌时自动取消上一首的请求。

右上角 **“取消置顶 / 置顶”** 按钮直接切换窗口是否保持在其他窗口上方，托盘右键菜单也有同一功能，切换立即保存，重启后保留。标题区可拖动；右下角可调整大小；“−”或 Esc 收起到系统托盘；“×”退出。双击托盘图标恢复小窗，右键可设置、手动输入歌曲或恢复自动识别。设置中可调透明度。点击 **“来源”** 查看实际使用的网页、核对摘录，并打开原文。

默认使用 DeepSeek 官方 `https://api.deepseek.com` 的 `deepseek-flash` 模型，并使用同一 Key 调用官方 Search / Fetch 接口，无需再申请搜索 Key。此版本的自动联网流程要求使用 DeepSeek 官方接口；不支持把其他服务商或本机模型的 Key 用作联网检索。API Key 由用户自行申请，搜索、网页读取和模型调用按 DeepSeek 的规则计费。

## 识别与内容

- 首选 Windows 系统媒体会话，只读取来源标识包含 `kugou` 或“酷狗”的歌曲，不会跟随其他播放器。
- 回退读取 KuGou 进程的歌曲窗口标题，例如 `张艾嘉 - 光阴的故事 - 酷狗音乐`。此路径无法确认播放/暂停状态，会在小窗标注。
- 连续两次识别到相同歌曲才发起请求；切歌会取消旧请求并阻止旧结果覆盖当前歌曲。
- 按歌名、歌手、专辑及模型缓存核对后的介绍、来源链接与摘录，30 天内重复播放可复用，最多缓存 200 首。旧版未检索的介绍不再复用。“换一篇 / 重新检索”会重新走完整后台流程。
- 并行搜索两组关键词，合并重复 URL、按发布方去重，最多读取五个网页、保留四份原文。某网页读取失败时，可使用搜索返回的原始正文，绝不把搜索摘要当成原文。原文不可用时显示可重试的网络/资料读取状态。
- 程序为原文片段建立编号。第一轮模型依据片段写故事；第二轮独立请求对照原文核对并修正同名作品、版本、年份、创作人物及传闻。每项事实需选择来源和片段编号，程序检查编号有效并直接提取对应的真实原文，不要求模型抄写引文。检查不通过或字数超过100字时自动修正一次，最终再强制限制字数。
- 具体轶事缺少可靠资料时，围绕有依据的制作合作、创作及首发背景叙述，不再用“暂无法确认幕后故事”充当介绍。完全无关的来源仍会被拒绝。AI 对语义的核对可能存在误差，可通过“来源”查看上下文；“已对照资料”表示执行了上述自动流程，不代表人工审定。
- 提示词要求 90～100 字，程序再按 Unicode 文本元素强制限制到 **100 字以内，含标点和省略号**。超长时优先保留完整句子。
- 不录音、不抓取音频。向 DeepSeek 发送歌名、歌手、专辑、搜索关键词及公开网页内容。Key 使用 Windows DPAPI 加密，仅当前 Windows 账户可解密；不写入代码、日志或歌曲缓存。

配置及缓存位于 `%LOCALAPPDATA%\KugouStory\`：`settings.json` 为配置，`stories.json` 为本地故事缓存。退出程序后可删除这两个文件来重置。

## 系统与兼容性

- Windows 10 2004 或更新版本，推荐 Windows 11，x64。
- `dist` 中的免安装包包含 .NET 运行环境，目标电脑不必另装 .NET。源码运行需要 .NET SDK 8 或更新版本。
- 2026-10-30 已在本机 Windows 11、酷狗 20.1.51 的实际播放中识别到《光阴的故事》/张艾嘉，系统媒体会话与窗口标题两种路径均可读取；已使用本机配置验证真实搜索、读取、模型核对及摘录校验。
- 酷狗各版本暴露的信息可能不同；若无法识别，先播放一首歌并等待几秒。若酷狗以管理员身份运行，可将两者改为相同权限运行。仍不可用时可从托盘菜单手动输入歌曲。
- 未提供酷狗程序内嵌式 DLL、自动安装或开机自启功能。本工具是独立悬浮窗程序。

## 源码运行与打包

```powershell
dotnet build KugouStory.sln -c Release
dotnet run --project tests/KugouStory.Tests -c Release
dotnet run --project src/KugouStory -c Release
powershell -ExecutionPolicy Bypass -File tools/publish.ps1
```

打包输出 `dist/KugouStory-win-x64/KugouStory.exe` 和 `dist/KugouStory-win-x64.zip`。首次打包会从 NuGet 下载微软 .NET 运行组件，需要网络。开发依赖只用微软 .NET / Windows SDK，无第三方 UI 或 AI SDK 包。

`tools/MediaProbe` 是只读诊断程序，输出当前 Windows 媒体会话、酷狗窗口标题，不读取密钥。运行：`dotnet run --project tools/MediaProbe`。

`tests/KugouStory.Tests` 是无第三方测试框架的自动化检查，使用模拟 HTTP 响应验证字数、Unicode、标题解析、防抖、接口参数、错误和取消请求，以及检索→读取→生成→独立核对的流程、移动/桌面来源去重、拒绝捏造引文与无效引用、原文不可用的处理；不会消耗真实 API 额度。

`tools/StorySmoke` 使用本机已保存的 DeepSeek Key 做真实完整检索测试，不输出密钥，会消耗 API 额度。运行 `dotnet run --project tools/StorySmoke -c Release -- --save` 可将核对后的测试歌曲介绍写入本机缓存。

## 接口依据

- [微软系统媒体会话 API](https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssessionmanager)
- [DeepSeek 接口与模型名称](https://api-docs.deepseek.com/zh-cn/)
- [DeepSeek Chat Completions](https://api-docs.deepseek.com/api/create-chat-completion/)
- [DeepSeek Search / Fetch API](https://api-docs.deepseek.com/guides/search/)
