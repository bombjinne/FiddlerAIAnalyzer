using Fiddler;
using Markdig;
using mshtml;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Printing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Threading;

namespace FiddlerAIAnalyzerPlugin
{


    public partial class AIChatView : UserControl
    {
        
        private bool _isSending = false;
        private PluginSettings _currentSettings;
        private static readonly Brush UserMessageBackground = new SolidColorBrush(Color.FromArgb(30, 30, 30, 30)); // 浅灰色，半透明效果
        private static readonly Brush AiMessageBackground = new SolidColorBrush(Color.FromArgb(30, 0, 122, 204));   // 浅蓝色，半透明效果
        private static readonly Brush MessageBorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200)); // 边框颜色
        private const double MessageBorderThickness = 1.0; // 边框粗细


        private void InputBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !e.IsRepeat)
            {
                if (e.KeyboardDevice.Modifiers == ModifierKeys.Shift)
                {
                    // Shift + Enter: 插入换行
                    InputBox.AppendText("\n");
                    e.Handled = true; // 阻止默认行为
                }
                else
                {
                    // 普通 Enter: 发送消息
                    OnSendButtonClick(null, null); // 触发发送按钮逻辑
                    e.Handled = true; // 阻止默认行为（插入换行）
                }
            }
        }
        private string GetSelectedRequestContent()
        {
            var selectedSessions = FiddlerApplication.UI.GetSelectedSessions(int.MaxValue);

            if (selectedSessions == null || selectedSessions.Length == 0)
            {
                return null;
            }

            var content = new StringBuilder();
            content.AppendLine($"共选中 {selectedSessions.Length} 个请求：\n");

            for (int i = 0; i < selectedSessions.Length; i++)
            {
                var session = selectedSessions[i];
                content.AppendLine($"--- 请求 #{i + 1} ---");

                // ======== 处理请求部分 ========
                if (session.oRequest != null)
                {
                    // URL
                    if (_currentSettings.IncludeUrl)
                    {
                        content.AppendLine($"URL: {session.fullUrl}");
                    }

                    // 请求头
                    if (_currentSettings.IncludeRequestHeaders && session.oRequest.headers != null)
                    {
                        content.AppendLine("请求头:");
                        foreach (var header in session.oRequest.headers)
                        {
                            content.AppendLine($"  {header.Name}: {header.Value}");
                        }
                    }

                    // 请求体
                    if (_currentSettings.IncludeRequestBody &&
                        session.RequestBody != null &&
                        session.RequestBody.Length > 0)
                    {
                        string contentType = "";
                        if (session.oRequest.headers != null)
                        {
                            contentType = session.oRequest.headers["Content-Type"] ?? "";
                        }

                        if (!_currentSettings.ExcludeBinaryContent || !IsBinaryContentType(contentType))
                        {
                            try
                            {
                                var bodyText = Encoding.UTF8.GetString(session.RequestBody);
                                content.AppendLine("请求体:");
                                content.AppendLine(bodyText);
                            }
                            catch
                            {
                                content.AppendLine("[解析请求体出错]");
                            }
                        }
                        else if (_currentSettings.ExcludeBinaryContent)
                        {
                            content.AppendLine("[请求体为二进制内容，已根据设置忽略]");
                        }
                    }
                }
                else
                {
                    content.AppendLine("[请求信息不可用]");
                }

                // ======== 处理响应部分 ========
                if (session.oResponse != null)
                {
                    // 响应头
                    if (_currentSettings.IncludeResponseHeaders && session.oResponse.headers != null)
                    {
                        content.AppendLine("响应头:");
                        foreach (var header in session.oResponse.headers)
                        {
                            content.AppendLine($"  {header.Name}: {header.Value}");
                        }
                    }

                    // 响应体
                    if (_currentSettings.IncludeResponseBody &&
                        session.ResponseBody != null &&
                        session.ResponseBody.Length > 0)
                    {
                        string contentType = "";
                        if (session.oResponse.headers != null)
                        {
                            contentType = session.oResponse.headers["Content-Type"] ?? "";
                        }

                        if (!_currentSettings.ExcludeBinaryContent || !IsBinaryContentType(contentType))
                        {
                            try
                            {
                                var bodyText = Encoding.UTF8.GetString(session.ResponseBody);
                                content.AppendLine("响应体:");
                                content.AppendLine(bodyText);
                            }
                            catch
                            {
                                content.AppendLine("[解析响应体出错]");
                            }
                        }
                        else if (_currentSettings.ExcludeBinaryContent)
                        {
                            content.AppendLine("[响应体为二进制内容，已根据设置忽略]");
                        }
                    }
                }
                else
                {
                    content.AppendLine("[响应信息不可用]");
                }

                content.AppendLine(); // 分隔符
            }

            return content.ToString();
        }




        private bool IsBinaryContentType(string contentType)
        {
            if (string.IsNullOrWhiteSpace(contentType))
                return false;

            var lowerContentType = contentType.ToLowerInvariant();
            return lowerContentType.StartsWith("image/") ||
                   lowerContentType.StartsWith("audio/") ||
                   lowerContentType.StartsWith("video/") ||
                   lowerContentType.Contains("application/octet-stream") ||
                   lowerContentType.Contains("application/pdf"); // PDF 有时也被视为二进制
        }

        public async Task StreamQwenAPIResponse(
    string prompt,
    string apiKey,
    Action<string> onTextReceived,   
    Action<string> onError)
        {
            var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Post, "https://dashscope.aliyuncs.com/api/v1/services/aigc/text-generation/generation");

            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            request.Headers.Add("X-DashScope-SSE", "enable");

            var jsonPayload = new
            {
                model = "qwen3-max",
                input = new
                {
                    messages = new[]
                    {
                new { role = "user", content = prompt }
            }
                },
                parameters = new
                {
                    stream = true
                }
            };

            string json = Newtonsoft.Json.JsonConvert.SerializeObject(jsonPayload);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();

                    using (var stream = await response.Content.ReadAsStreamAsync())
                    using (var reader = new System.IO.StreamReader(stream))
                    {
                        string line;
                        while ((line = await reader.ReadLineAsync()) != null)
                        {
                            if (line.StartsWith("data:"))
                            {
                                string payload = line.Substring(5).Trim(); // 移除 "data:"
                                if (string.IsNullOrEmpty(payload) || payload == "[DONE]")
                                    continue;

                                try
                                {
                                    var jObject = JObject.Parse(payload);

                                    // 🔥 关键修改：优先尝试 message（DashScope 当前实际返回），fallback 到 delta
                                    JToken contentToken = jObject["output"]?["choices"]?[0]?["message"]?["content"]
                                                       ?? jObject["output"]?["choices"]?[0]?["delta"]?["content"];

                                    if (contentToken?.Type == JTokenType.String)
                                    {
                                        string text = contentToken.ToString();
                                        if (!string.IsNullOrEmpty(text))
                                        {
                                            onTextReceived?.Invoke(text);
                                        }
                                    }

                                    // 可选：检测结束（但通常最后一个包 content 为空，可忽略）
                                    // var finishReason = jObject["output"]?["choices"]?[0]?["finish_reason"]?.ToString();
                                    // if (finishReason == "stop") break;
                                }
                                catch (Exception ex)
                                {
                                    onError?.Invoke($"[JSON解析失败: {ex.Message}]");
                                }
                            }
                            // 忽略其他行如 "id:", "event:", ":HTTP_STATUS/200" 等
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                onError?.Invoke($"[网络请求失败: {ex.Message}]");
            }
        }


        /// <summary>
        /// 将 Markdown 文本转换为 HTML
        /// </summary>
        private string ConvertMarkdownToHtml(string markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown))
                return "";

            var pipeline = new MarkdownPipelineBuilder()
                .UseAdvancedExtensions() // 启用表格、删除线、任务列表等扩展
                .UseBootstrap() // 可选：使用 Bootstrap 样式
                .Build();

            string html = Markdown.ToHtml(markdown, pipeline);

            string styledHtml = $@"
<style>
    body {{
        font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
        font-size: 14px;
        line-height: 1.6;
        color: #333;
        background-color: #fff;
        margin: 10px;
    }}
    pre {{
        background-color: #f6f8fa;
        border-radius: 6px;
        padding: 16px;
        overflow: auto;
        font-size: 85%;
        line-height: 1.45;
        border: 1px solid #d0d7de;
    }}
    code {{
        background-color: rgba(110,118,129,0.1);
        padding: 0.2em 0.4em;
        border-radius: 6px;
        font-size: 85%;
    }}
    pre code {{
        background-color: transparent;
        padding: 0;
    }}
    blockquote {{
        margin: 0;
        padding: 0 1em;
        color: #6a737d;
        border-left: 0.25em solid #dfe2e5;
    }}
    h1, h2, h3, h4, h5, h6 {{
        margin-top: 24px;
        margin-bottom: 16px;
        font-weight: 600;
        line-height: 1.25;
    }}
    /* 确保代码块内的滚动条可见 */
    ::-webkit-scrollbar {{
        width: 8px;
        height: 8px;
    }}
    ::-webkit-scrollbar-track {{
        background: #f1f1f1; 
    }}
    ::-webkit-scrollbar-thumb {{
        background: #c1c1c1; 
        border-radius: 4px;
    }}
    ::-webkit-scrollbar-thumb:hover {{
        background: #a8a8a8; 
    }}
</style>
{html}";

            return styledHtml;
        }

        /// <summary>
        /// 将消息添加到聊天记录中并更新 WebBrowser 显示（线程安全）
        /// </summary>
        private void AddMessage(string message, bool isUser)
        {
            if (MessagesWebBrowser?.Document is IHTMLDocument3 doc)
            {
                var chatContainer = doc.getElementById("chat-container");
                if (chatContainer == null) return;

                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                string roleLabel = isUser ? "👤 用户" : "🤖 AI";
                string className = isUser ? "user-message" : "ai-message";

                string contentHtml = ConvertMarkdownToHtml(message);
                string messageHtml = $@"
<div class=""message {className}"">
    <div class=""message-header"">{roleLabel} · {timestamp}</div>
    <div class=""message-content"">{contentHtml}</div>
</div>";

                chatContainer.insertAdjacentHTML("beforeend", messageHtml);
            }
        }

        private void UpdateStreamingMessage(string content, string placeholderId)
        {
            if (MessagesWebBrowser?.Document is IHTMLDocument3 doc)
            {
                var el = doc.getElementById(placeholderId);
                if (el != null)
                {
                    IHTMLElement contentDiv = null;
                    for (int i = 0; i < el.children.length; i++)
                    {
                        var child = el.children.item(i) as IHTMLElement;
                        if (child != null && child.className == "message-content")
                        {
                            contentDiv = child;
                            break;
                        }
                    }
                    if (contentDiv != null)
                    {
                        string html = ConvertMarkdownToHtml(string.IsNullOrEmpty(content) ? "正在生成回答..." : content);
                        try
                        {
                            contentDiv.innerHTML = html;
                        }
                        catch { /* 忽略异常 */ }
                    }
                }
            }
        }


        /// <summary>
        /// 生成一个简单的请求摘要，用于在聊天框中展示 (保持不变)
        /// </summary>
        private string GenerateRequestSummary()
        {
            var selectedSessions = FiddlerApplication.UI.GetSelectedSessions(1);
            if (selectedSessions == null || selectedSessions.Length == 0)
            {
                return "[无选中请求]";
            }

            var session = selectedSessions[0];
            var sb = new StringBuilder();
            string shortUrl = session.fullUrl.Length > 100 ? session.fullUrl.Substring(0, 100) + "..." : session.fullUrl;
            sb.AppendLine($"URL: {shortUrl} <br>");
            if (session.RequestBody != null)
                sb.AppendLine($"请求体大小: {session.RequestBody.Length} bytes <br>");
            if (session.ResponseBody != null)
                sb.AppendLine($"响应体大小: {session.ResponseBody.Length} bytes");
            return sb.ToString();
        }

        private async void OnSendButtonClick(object sender, RoutedEventArgs e)
        {
            Button sendButton = sender as Button ?? (Button)this.FindName("SendButton");
            if (sendButton != null) sendButton.IsEnabled = false;

            try
            {
                if (_isSending)
                {
                    AddMessage("[提示] 正在处理上一条请求，请稍候...", isUser: false);
                    return;
                }

                CollectSettingsFromUI();

                string prompt = InputBox.Text.Trim();
                if (string.IsNullOrEmpty(prompt))
                {
                    AddMessage("请输入问题", isUser: false);
                    return;
                }

                InputBox.Clear();

                string apiKey = ApiKeyBox.Password;
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    AddMessage("[错误] 请先设置有效的 API Key", isUser: false);
                    return;
                }

                string requestContent = GetSelectedRequestContent();
                string fullPrompt;
                string userMessageToDisplay;

                if (requestContent != null)
                {
                    fullPrompt = $"{prompt}\n\n以下是相关的 HTTP 请求和响应信息，请基于此进行分析：\n\n{requestContent}";
                    userMessageToDisplay = $"{prompt}<br>--- 已附带请求摘要 ---<br>{GenerateRequestSummary()}";
                }
                else
                {
                    fullPrompt = prompt;
                    userMessageToDisplay = prompt;
                }

                AddMessage(userMessageToDisplay, isUser: true);

                _isSending = true;

                // 生成唯一 ID
                string placeholderId = $"ai-streaming-{Guid.NewGuid():N}";

                // 创建占位符并插入 DOM
                if (MessagesWebBrowser?.Document is IHTMLDocument3 doc)
                {
                    var chatContainer = doc.getElementById("chat-container");
                    if (chatContainer != null)
                    {
                        string placeholderHtml = $@"
<div class='message ai-message' id='{placeholderId}'>
    <div class='message-header'>🤖 AI · 正在思考...</div>
    <div class='message-content'><p>正在生成回答...</p></div>
</div>";
                        chatContainer.insertAdjacentHTML("beforeend", placeholderHtml);
                    }
                }

                // 启动流式请求
                _ = Task.Run(async () =>
                {
                    var aiResponseBuffer = new StringBuilder();
                    Exception streamError = null;

                    await StreamQwenAPIResponse(
                        fullPrompt,
                        apiKey,
                        onTextReceived: (text) =>
                        {
                            aiResponseBuffer.Append(text);
                            if (!this.Dispatcher.HasShutdownStarted)
                            {
                                this.Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    UpdateStreamingMessage(aiResponseBuffer.ToString(), placeholderId);
                                }));
                            }
                        },
                        onError: (error) =>
                        {
                            streamError = new Exception(error);
                            if (!this.Dispatcher.HasShutdownStarted)
                            {
                                this.Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    _isSending = false;
                                    ReplaceStreamingMessage($"[错误] {error}", placeholderId);
                                }));
                            }
                        });

                    if (!this.Dispatcher.HasShutdownStarted)
                    {
                        this.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            _isSending = false;
                            ReplaceStreamingMessage(aiResponseBuffer.ToString(), placeholderId);
                        }));
                    }
                });
            }
            catch (Exception ex)
            {
                FiddlerApplication.Log.LogString($"Error in OnSendButtonClick: {ex}");
                _isSending = false;
                if (!this.Dispatcher.HasShutdownStarted)
                {
                    this.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        AddMessage($"[严重错误] {ex.Message}", isUser: false);
                    }));
                }
            }
            finally
            {
                if (sendButton != null) sendButton.IsEnabled = true;
            }
        }


        private void ReplaceStreamingMessage(string finalContent, string placeholderId)
        {
            if (MessagesWebBrowser?.Document is IHTMLDocument3 doc)
            {
                // 找到并移除占位符
                IHTMLElement placeholder = doc.getElementById(placeholderId);
                if (placeholder != null)
                {
                    IHTMLDOMNode node = placeholder as IHTMLDOMNode;
                    if (node?.parentNode != null)
                    {
                        node.parentNode.removeChild(node);
                    }
                }

                // 添加最终 AI 消息
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                string displayContent = string.IsNullOrEmpty(finalContent) ? "<p>无内容返回。</p>" : ConvertMarkdownToHtml(finalContent);

                string finalMessageHtml = $@"
<div class=""message ai-message"">
    <div class=""message-header"">🤖 AI · {timestamp}</div>
    <div class=""message-content"">{displayContent}</div>
</div>";

                // 插入新消息
                var chatContainer = doc.getElementById("chat-container");
                if (chatContainer != null)
                {
                    chatContainer.insertAdjacentHTML("beforeend", finalMessageHtml);
                }
            }
        }


        /// <summary>
        /// 将内存中的配置应用到界面控件
        /// </summary>
        private void ApplySettingsToUI()
        {
            ApiKeyBox.Password = _currentSettings.ApiKey;

            CbUrl.IsChecked = _currentSettings.IncludeUrl;
            CbReqHeaders.IsChecked = _currentSettings.IncludeRequestHeaders;
            CbReqBody.IsChecked = _currentSettings.IncludeRequestBody;
            CbRespHeaders.IsChecked = _currentSettings.IncludeResponseHeaders;
            CbRespBody.IsChecked = _currentSettings.IncludeResponseBody;
            CbNoBinary.IsChecked = _currentSettings.ExcludeBinaryContent;
        }

        /// <summary>
        /// 从界面控件收集当前设置
        /// </summary>
        private void CollectSettingsFromUI()
        {
            _currentSettings.ApiKey = ApiKeyBox.Password;

            _currentSettings.IncludeUrl = CbUrl.IsChecked == true;
            _currentSettings.IncludeRequestHeaders = CbReqHeaders.IsChecked == true;
            _currentSettings.IncludeRequestBody = CbReqBody.IsChecked == true;
            _currentSettings.IncludeResponseHeaders = CbRespHeaders.IsChecked == true;
            _currentSettings.IncludeResponseBody = CbRespBody.IsChecked == true;
            _currentSettings.ExcludeBinaryContent = CbNoBinary.IsChecked == true;
        }

        /// <summary>
        /// 点击“保存”按钮时触发
        /// </summary>
        private void OnSaveApiKeyClick(object sender, RoutedEventArgs e)
        {
            CollectSettingsFromUI();       // 从界面获取最新值
            _currentSettings.Save();       // 保存到文件
            MessageBox.Show("配置已保存！");
        }



        public AIChatView()
        {
            InitializeComponent();
            _currentSettings = PluginSettings.Load();
            ApplySettingsToUI();

            // 初始化空聊天容器
            string initHtml = @"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <title>Chat</title>
    <style>
        body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; font-size: 14px; line-height: 1.6; color: #333; background-color: #fff; margin: 10px; }
        .message { margin: 10px 0; padding: 4px 8px; border-radius: 8px; border: 1px solid #ddd; }
        .user-message { background-color: #e3f2fd; text-align: right; }
        .ai-message { background-color: #f5f5f5; }
        .message-header { font-size: 12px; color: #666; margin-bottom: 5px; }
        .message-content p { margin: 0; line-height: 1.5; }
        pre { background-color: #f6f8fa; border-radius: 6px; padding: 16px; overflow: auto; font-size: 85%; line-height: 1.45; border: 1px solid #d0d7de; }
        code { background-color: rgba(110,118,129,0.1); padding: 0.2em 0.4em; border-radius: 6px; font-size: 85%; }
        pre code { background-color: transparent; padding: 0; }
        blockquote { margin: 0; padding: 0 1em; color: #6a737d; border-left: 0.25em solid #dfe2e5; }
        h1, h2, h3, h4, h5, h6 { margin-top: 24px; margin-bottom: 16px; font-weight: 600; line-height: 1.25; }
        ::-webkit-scrollbar { width: 8px; height: 8px; }
        ::-webkit-scrollbar-track { background: #f1f1f1; }
        ::-webkit-scrollbar-thumb { background: #c1c1c1; border-radius: 4px; }
        ::-webkit-scrollbar-thumb:hover { background: #a8a8a8; }
    </style>
</head>
<body>
    <div id='chat-container'></div>
</body>
</html>";

            MessagesWebBrowser.NavigateToString(initHtml);

            // 等待加载完成后显示欢迎语
            MessagesWebBrowser.LoadCompleted += (s, e) =>
            {
                AddMessage("你好！我是 AI 分析助手。请先在上方设置 API Key，可选中 Fiddler 中的一个或多个请求后提问。", isUser: false);
            };
        }




        // 辅助方法：将字符串按代码块分割，并添加到 Inlines 集合中
        private void AddTextAsRuns(InlineCollection inlines, string text, Brush foreground, FontWeight fontWeight)
        {
            if (text.Contains("```"))
            {
                var parts = text.Split(new[] { "```" }, StringSplitOptions.None);
                for (int i = 0; i < parts.Length; i++)
                {
                    if (i % 2 == 0)
                    {
                        // 普通文本部分
                        if (!string.IsNullOrEmpty(parts[i]))
                        {
                            var run = new Run(parts[i]) { Foreground = foreground, FontWeight = fontWeight };
                            inlines.Add(run);
                        }
                    }
                    else
                    {
                        // 代码块部分 - 使用 InlineUIContainer 包裹只读 TextBox
                        var codeBox = new TextBox
                        {
                            Text = parts[i],
                            IsReadOnly = true,
                            FontFamily = new FontFamily("Consolas"),
                            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#f0f0f0")),
                            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ddd")),
                            Margin = new Thickness(0, 2, 0, 2), // 内联元素的 Margin
                            Padding = new Thickness(4),
                            TextWrapping = TextWrapping.NoWrap,
                            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                            BorderThickness = new Thickness(1)
                        };

                        // 尝试设置最大宽度，使其适应 RichTextBox
                        codeBox.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                        codeBox.Arrange(new Rect(codeBox.DesiredSize));

                        var container = new InlineUIContainer(codeBox);
                        // 可能需要调整 BaselineAlignment 使它对齐更好
                        container.BaselineAlignment = BaselineAlignment.Center;
                        inlines.Add(container);
                    }
                }
            }
            else
            {
                // 纯文本消息
                var run = new Run(text) { Foreground = foreground, FontWeight = fontWeight };
                inlines.Add(run);
            }
        }



        public static T FindVisualChild<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj == null) return null;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = VisualTreeHelper.GetChild(depObj, i);
                if (child != null && child is T t)
                    return t;

                var childItem = FindVisualChild<T>(child);
                if (childItem != null)
                    return childItem;
            }
            return null;
        }
    }
}