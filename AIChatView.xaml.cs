using Fiddler;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using System.Windows.Threading;

namespace FiddlerAIAnalyzerPlugin
{


    public partial class AIChatView : UserControl
    {

        private bool _isSending = false;
        private PluginSettings _currentSettings;
        // 在您的类的字段区域添加以下静态只读字段
        private static readonly Brush UserMessageBackground = new SolidColorBrush(Color.FromArgb(30, 30, 30, 30)); // 浅灰色，半透明效果
        private static readonly Brush AiMessageBackground = new SolidColorBrush(Color.FromArgb(30, 0, 122, 204));   // 浅蓝色，半透明效果
        private static readonly Brush MessageBorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200)); // 边框颜色
        private const double MessageBorderThickness = 1.0; // 边框粗细


        // 获取当前选中的请求

        private void InputBox_KeyDown(object sender, KeyEventArgs e)
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
                        // 安全地获取 Content-Type
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
                        // 安全地获取 Content-Type
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
    Action<string> onTextReceived,   // 注意：参数名必须一致
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
        private async void OnSendButtonClick(object sender, RoutedEventArgs e)
        {
            // ✅ 检查核心控件是否存在
            if (MessagesRichTextBox?.Document == null)
            {
                FiddlerApplication.Log.LogString("[ERROR] MessagesRichTextBox.Document is null!");
                return;
            }

            if (_isSending)
            {
                // 注意：这条提示消息现在也是可选的了
                AddMessage("[提示] 正在处理上一条请求，请稍候...", isUser: false);
                return;
            }

            // 从UI收集最新设置
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
                userMessageToDisplay = $"{prompt}\n\n--- 已附带请求摘要 ---\n{GenerateRequestSummary()}";
            }
            else
            {
                fullPrompt = prompt;
                userMessageToDisplay = prompt;
            }

            // 1. 先添加用户消息到聊天记录 (这部分保持不变，但内部实现已修改为可选)
            AddMessage(userMessageToDisplay, isUser: true);


            var aiResponseParagraph = new Paragraph()
            {
                Margin = new Thickness(0, 5, 0, 5),
                Padding = new Thickness(8, 6, 8, 6), // 增加内边距
                                                     // --- 添加背景色和边框 ---
                Background = AiMessageBackground, // AI 消息背景色
                BorderBrush = MessageBorderBrush,
                BorderThickness = new Thickness(MessageBorderThickness),
            };
            // 添加一个初始的 Run，用于流式更新 AI 回复的实际文本内容
            var aiResponseRun = new Run("");

            // （可选）添加一个表示身份的小标签，这个 Run 可以有不同的样式
            var aiLabelRun = new Run("[AI] ") { Foreground = Brushes.Green, FontWeight = FontWeights.Bold }; // 标签颜色与 AddMessage 中一致
            aiResponseParagraph.Inlines.Add(aiLabelRun);

            aiResponseParagraph.Inlines.Add(aiResponseRun);

            MessagesRichTextBox.Document.Blocks.Add(aiResponseParagraph);
            MessagesRichTextBox.ScrollToEnd();

            _isSending = true; // 设置发送状态
                               // --- 修改结束 ---

            try
            {
                // --- 修改传参：传递 Paragraph 和 Run 给流式处理方法 ---
                // 调用流式处理方法，传入必要的对象以便它可以更新UI
                await StreamAndAppendResponse(fullPrompt, apiKey, aiResponseParagraph, aiResponseRun);
                // --- 修改结束 ---
            }
            catch (Exception ex)
            {
                // 确保在UI线程上更新UI
                Application.Current.Dispatcher.Invoke(() =>
                {
                    // --- 修改错误处理：将错误信息也作为可选文本添加 ---
                    // 错误信息也应该让用户能复制，所以添加到 Paragraph 中
                    aiResponseParagraph.Inlines.Add(new Run($"\n[严重错误: {ex.Message}]") { Foreground = Brushes.Red });
                    MessagesRichTextBox.ScrollToEnd();
                    // --- 修改结束 ---
                });
            }
            finally
            {
                // 确保在UI线程上重置状态
                MessagesRichTextBox.Dispatcher.Invoke(() =>
                {
                    _isSending = false;
                });
            }
        }


        // 新增辅助方法：在后台线程流式接收，并调度到 UI 更新 (修改参数)
        private async Task StreamAndAppendResponse(string prompt, string apiKey, Paragraph targetParagraph, Run targetRun)
        {
            try
            {
                StringBuilder accumulated = new StringBuilder();

                await StreamQwenAPIResponse(prompt, apiKey,
                    onTextReceived: (text) =>
                    {
                        accumulated.Append(text);

                        // 使用 UI Element 自带的 Dispatcher 更新 UI
                        MessagesRichTextBox.Dispatcher.Invoke(() =>
                        {
                            // --- 修改更新方式 ---
                            // 直接更新 Run 的文本内容
                            targetRun.Text = accumulated.ToString();
                            MessagesRichTextBox.ScrollToEnd();
                            // --- 修改结束 ---
                        });
                    },
                    onError: (error) =>
                    {
                        // 使用 UI Element 自带的 Dispatcher 更新 UI
                        MessagesRichTextBox.Dispatcher.Invoke(() =>
                        {
                            // --- 修改错误处理 ---
                            // 将错误信息也作为 Run 添加到 Paragraph 中
                            targetParagraph.Inlines.Add(new Run(error) { Foreground = Brushes.Red });
                            MessagesRichTextBox.ScrollToEnd();
                            // --- 修改结束 ---
                        });
                    });
            }
            finally
            {
                MessagesRichTextBox.Dispatcher.Invoke(() =>
                {
                    _isSending = false;
                });
            }
        }

        // 新增辅助方法：在后台线程流式接收，并调度到 UI 更新
        private async Task StreamAndAppendResponse(string prompt, string apiKey, TextBlock targetTextBlock)
        {
            try
            {
                StringBuilder accumulated = new StringBuilder();

                await StreamQwenAPIResponse(prompt, apiKey,
                    onTextReceived: (text) =>
                    {
                        accumulated.Append(text);

                        // 使用 UI Element 自带的 Dispatcher 更新 UI
                        MessagesRichTextBox.Dispatcher.Invoke(() =>
                        {
                            targetTextBlock.Text = accumulated.ToString();
                            MessagesRichTextBox.ScrollToEnd();
                        });
                    },
                    onError: (error) =>
                    {
                        // 使用 UI Element 自带的 Dispatcher 更新 UI
                        MessagesRichTextBox.Dispatcher.Invoke(() =>
                        {
                            targetTextBlock.Text += error;
                            MessagesRichTextBox.ScrollToEnd();
                        });
                    });
            }
            finally
            {
                // 使用 UI Element 自带的 Dispatcher 重置发送状态
                MessagesRichTextBox.Dispatcher.Invoke(() =>
                {
                    _isSending = false;
                });
            }
        }

        /// <summary>
        /// 生成一个简单的请求摘要，用于在聊天框中展示
        /// </summary>
        /// <returns></returns>
        private string GenerateRequestSummary()
        {
            var selectedSessions = FiddlerApplication.UI.GetSelectedSessions(1); // 只取一个就够了做摘要

            if (selectedSessions == null || selectedSessions.Length == 0)
            {
                return "[无选中请求]";
            }

            var session = selectedSessions[0];
            var sb = new StringBuilder();

            // 显示 URL 的一部分和总长度
            string shortUrl = session.fullUrl.Length > 100 ? session.fullUrl.Substring(0, 100) + "..." : session.fullUrl;
            sb.AppendLine($"URL: {shortUrl}");

            if (session.RequestBody != null)
                sb.AppendLine($"请求体大小: {session.RequestBody.Length} bytes");
            if (session.ResponseBody != null)
                sb.AppendLine($"响应体大小: {session.ResponseBody.Length} bytes");

            return sb.ToString();
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

        // === 其他方法（如 SendButtonClick、CallQwenAPI 等）===


        public AIChatView()
        {
            InitializeComponent();
            _currentSettings = PluginSettings.Load(); // 启动时加载配置
            ApplySettingsToUI(); // 更新界面

            AddMessage("你好！我是 AI 分析助手。请先在上方设置 API Key，可选中 Fiddler 中的一个或多个请求后提问。", isUser: false);
        }



        private void AddMessage(string message, bool isUser)
        {
            if (MessagesRichTextBox?.Document == null) return;

            var document = MessagesRichTextBox.Document;

            // 定义消息样式常量 (可以在类级别定义为静态只读以供复用)
            Brush userMessageBackground = new SolidColorBrush(Color.FromArgb(30, 0, 122, 204)); // 浅蓝色，半透明
            Brush aiMessageBackground = new SolidColorBrush(Color.FromArgb(30, 30, 30, 30));   // 浅灰色，半透明
            Brush messageBorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200));
            const double messageBorderThicknessValue = 1.0;
            Thickness messageMargin = new Thickness(0, 5, 0, 5);
            Thickness messagePadding = new Thickness(8, 6, 8, 6); // 内边距

            // ---- AI 消息 ----
            if (!isUser)
            {
                var aiParagraph = new Paragraph();

                // --- 添加背景色和边框 ---
                aiParagraph.Background = aiMessageBackground;
                aiParagraph.BorderBrush = messageBorderBrush;
                aiParagraph.BorderThickness = new Thickness(messageBorderThicknessValue);
                aiParagraph.Padding = messagePadding; // 添加内边距
                                                      // --- 结束添加样式 ---

                // 例如，添加一个表示身份的小标签
                var aiLabelRun = new Run("[AI] ") { Foreground = Brushes.Green, FontWeight = FontWeights.Bold };
                aiParagraph.Inlines.Add(aiLabelRun);

                // 添加实际消息内容 (这部分是可选择的)
                AddTextAsRuns(aiParagraph.Inlines, message, Brushes.Black, FontWeights.Normal);

                // 设置段落的外边距
                aiParagraph.Margin = messageMargin;

                document.Blocks.Add(aiParagraph);
                MessagesRichTextBox.ScrollToEnd();
                return;
            }

            // ---- 用户消息 ----

            // 1. 时间标签 (这部分可以做成不可选的装饰)
            var timeText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var userLabelPara = new Paragraph(new Run($"👤 用户 · {timeText}")
            {
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100))
            })
            {
                Margin = new Thickness(0, 5, 0, 2), // 段落间距
                TextAlignment = TextAlignment.Right // 右对齐用户标签
            };
            document.Blocks.Add(userLabelPara);

            // 2. 用户消息主体 (这部分是可选择的主要内容)
            var userParagraph = new Paragraph()
            {
                Margin = new Thickness(0, 0, 0, 5),
                TextAlignment = TextAlignment.Right // 右对齐内容
            };

            // --- 添加背景色和边框 ---
            userParagraph.Background = userMessageBackground;
            userParagraph.BorderBrush = messageBorderBrush;
            userParagraph.BorderThickness = new Thickness(messageBorderThicknessValue);
            userParagraph.Padding = messagePadding; // 添加内边距
                                                    // --- 结束添加样式 ---

            // 应用用户消息的内容
            AddTextAsRuns(userParagraph.Inlines, message, Brushes.Black, FontWeights.Normal);

            document.Blocks.Add(userParagraph);
            MessagesRichTextBox.ScrollToEnd();
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