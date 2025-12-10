## Fiddler AI 分析插件

这个插件在 Fiddler 中提供 AI 协助的抓包分析体验，包括一键附带选中会话的请求/响应数据并调用大模型进行实时流式回复。项目的主要逻辑代码位于 AIChatView.xaml / AIChatView.xaml.cs 和插件入口 AIAnalyzerPlugin.cs。开发过程中参考了 CSDN 教程 [Fiddler 插件开发用 WPF 承载插件 UI](https://blog.csdn.net/Gdeer/article/details/102756017) 以及 GitHub 示例仓库 [JasonGrass/Fiddler.Plugin.SDK](https://github.com/JasonGrass/Fiddler.Plugin.SDK)，并借助 AI 辅助完成实现。

### 背景
经常用 Fiddler 抓包分析网络请求，手动梳理信息容易混乱，而来回复制给 AI 又很繁琐，所以做了这个插件来自动把选中会话交给模型分析。我不太懂代码，主要靠 AI 帮忙写出了一些shi山代码，但功能可用，自用版本分享出来就当是抛砖引玉，有个性需求的可以自行复刻和修改。

### 功能概览
- 在 Fiddler 中新增 AI分析 选项卡，WPF UI 通过 WinForms 容器嵌入。
- 选择会话后，可勾选要发送给 AI 的 URL/请求头/请求体/响应头/响应体，并可忽略二进制内容。
- 调用通义千问（DashScope）流式接口，实时将回复渲染到内置浏览器。
- Markdown 渲染支持代码块/表格/引用等，便于阅读和复制。

### 环境要求
- Windows + Fiddler 2.1.8.1 及以上（插件通过 [assembly: RequiredVersion("2.1.8.1")] 声明）。
- .NET Framework（与 Fiddler 兼容的版本，推荐 4.6 及以上）。

### 编译步骤
1) 在本机打开解决方案并恢复 NuGet 依赖（已使用 Markdig、Newtonsoft.Json 等包）。
2) 引入本地安装的fiddler.exe。
3) 生成解决方案，输出dll位于项目obj目录下的 Debug/Release目录。

### 部署步骤
编译完成后，将以下文件复制到 D:\Documents\Fiddler2\Scripts 目录（确保覆盖旧版本）：
- 本项目生成的主 DLL（例如 FiddlerAIAnalyzerPlugin.dll 及其输出目录内的其他插件 DLL）
- 依赖库：
  - System.Numerics.Vectors.dll
  - System.Runtime.CompilerServices.Unsafe.dll
  - System.Memory.dll
  - System.Buffers.dll
  - Markdig.dll
  - Jgrass.FiddlerPlugin.dll

复制后重启 Fiddler，即可在顶部标签页看到 AI分析。

### 使用说明
1) 在 AI分析 页填写 QWEN API Key，点击保存。
2) 在 Fiddler 中选中一个或多个会话，勾选要发送给 AI 的内容（URL/头/体等）。
3) 在输入框输入问题，按 Enter 或点击发送。若希望换行，使用 Shift+Enter。
4) 插件会流式展示回复，可随时复制查看。

插件界面概览：
<img width="2002" height="959" alt="ScreenShot_2025-12-10_160055_726" src="https://github.com/user-attachments/assets/db2d079f-7455-4b37-ba0a-882aaf7b6a89" />

### 常见问题
- **未看到标签页**：确认已将所有 DLL 放入 D:\Documents\Fiddler2\Scripts，并重启 Fiddler。
- **API 调用失败**：检查网络、API Key，或在日志中查看错误信息。
- **二进制内容过大**：可勾选 不发送二进制内容避免发送图片/音视频/PDF。


# AIAnalyzer
