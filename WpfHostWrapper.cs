using System.Windows.Forms;
using System.Windows.Forms.Integration;

namespace FiddlerAIAnalyzerPlugin
{
    public class WpfHostWrapper : UserControl
    {
        public WpfHostWrapper(System.Windows.UIElement wpfContent)
        {
            // 创建 ElementHost
            var elementHost = new ElementHost
            {
                Dock = DockStyle.Fill,         // ✅ 关键：占满整个控件
                Child = wpfContent
            };

            // 设置自身也能拉伸
            this.Dock = DockStyle.Fill;        // ✅ 关键：自身也要 Fill
            this.AutoSize = false;             // ✅ 关键：禁止自动收缩

            // 添加到 Controls 中
            this.Controls.Add(elementHost);
        }
    }
}