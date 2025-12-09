using Fiddler;
using Jgrass.FiddlerPlugin;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Windows.Forms.Integration;

[assembly: RequiredVersion("2.1.8.1")] // 确保 Fiddler 版本兼容

namespace FiddlerAIAnalyzerPlugin
{
    public class AIAnalyzerPlugin : FiddlerPluginApplication
    {
        public override IFiddlerViewProvider GetFiddlerViewProvider()
        {
            return new AIViewProvider();
        }

        // 可选：重写 OnLoad 做初始化（记得调 base.OnLoad()）
        public override void OnLoad()
        {
            base.OnLoad(); // 必须调用，否则 UI 不加载
            FiddlerApplication.Log.LogString("[AI分析] 插件已加载");
        }

        public override void OnBeforeUnload()
        {
            FiddlerApplication.Log.LogString("[AI分析] 插件正在卸载");
            base.OnBeforeUnload();
        }
    }

    public class AIViewProvider : IFiddlerViewProvider
    {
        public IList<FiddlerTabPage> BuildFiddlerTabPages()
        {
            var wpfControl = new AIChatView(); // 你的 WPF 控件
            var winFormsWrapper = new WpfHostWrapper(wpfControl); // 包装成 WinForms UserControl

            return new List<FiddlerTabPage>
    {
        new FiddlerTabPage("AI分析", winFormsWrapper) // ✅ 现在类型匹配！
    };
        }
    }
}