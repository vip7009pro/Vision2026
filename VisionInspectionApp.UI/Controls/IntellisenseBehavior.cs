using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using VisionInspectionApp.UI.ViewModels;

namespace VisionInspectionApp.UI.Controls;

public static class IntellisenseBehavior
{
    public static readonly DependencyProperty EnableProperty =
        DependencyProperty.RegisterAttached(
            "Enable",
            typeof(bool),
            typeof(IntellisenseBehavior),
            new PropertyMetadata(false, OnEnableChanged));

    public static bool GetEnable(DependencyObject obj) => (bool)obj.GetValue(EnableProperty);
    public static void SetEnable(DependencyObject obj, bool value) => obj.SetValue(EnableProperty, value);

    public static readonly DependencyProperty NodesProperty =
        DependencyProperty.RegisterAttached(
            "Nodes",
            typeof(IEnumerable),
            typeof(IntellisenseBehavior),
            new PropertyMetadata(null));

    public static IEnumerable? GetNodes(DependencyObject obj) => (IEnumerable?)obj.GetValue(NodesProperty);
    public static void SetNodes(DependencyObject obj, IEnumerable? value) => obj.SetValue(NodesProperty, value);

    public sealed record IntellisenseItem(string DisplayText, string InsertText, string Description, string Icon, bool IsToolName);

    private sealed class PopupContext
    {
        public TextBox TextBox { get; }
        public Popup Popup { get; }
        public ListBox ListBox { get; }
        public int ReplaceStartIndex { get; set; }
        public int ReplaceLength { get; set; }

        public PopupContext(TextBox tb, Popup p, ListBox lb)
        {
            TextBox = tb;
            Popup = p;
            ListBox = lb;
        }
    }

    private static readonly Dictionary<TextBox, PopupContext> Contexts = new();

    private static void OnEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox textBox) return;

        if ((bool)e.NewValue)
        {
            textBox.Loaded += OnTextBoxLoaded;
            textBox.Unloaded += OnTextBoxUnloaded;
            textBox.PreviewKeyDown += OnTextBoxPreviewKeyDown;
            textBox.TextChanged += OnTextBoxTextChanged;
            textBox.SelectionChanged += OnTextBoxSelectionChanged;
            textBox.LostFocus += OnTextBoxLostFocus;
            if (textBox.IsLoaded)
            {
                EnsurePopupCreated(textBox);
            }
        }
        else
        {
            textBox.Loaded -= OnTextBoxLoaded;
            textBox.Unloaded -= OnTextBoxUnloaded;
            textBox.PreviewKeyDown -= OnTextBoxPreviewKeyDown;
            textBox.TextChanged -= OnTextBoxTextChanged;
            textBox.SelectionChanged -= OnTextBoxSelectionChanged;
            textBox.LostFocus -= OnTextBoxLostFocus;
            RemovePopup(textBox);
        }
    }

    private static void OnTextBoxLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb) EnsurePopupCreated(tb);
    }

    private static void OnTextBoxUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb) RemovePopup(tb);
    }

    private static void EnsurePopupCreated(TextBox textBox)
    {
        if (Contexts.ContainsKey(textBox)) return;

        var listBox = new ListBox
        {
            MaxHeight = 200,
            MinWidth = 240,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E1E1E")),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DCDCDC")),
            BorderThickness = new Thickness(0),
            Focusable = false
        };

        var template = new DataTemplate(typeof(IntellisenseItem));
        var factory = new FrameworkElementFactory(typeof(StackPanel));
        factory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        factory.SetValue(StackPanel.MarginProperty, new Thickness(4, 3, 4, 3));

        var iconText = new FrameworkElementFactory(typeof(TextBlock));
        iconText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Icon"));
        iconText.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 6, 0));
        factory.AppendChild(iconText);

        var nameText = new FrameworkElementFactory(typeof(TextBlock));
        nameText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("DisplayText"));
        nameText.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
        nameText.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4EC9B0")));
        nameText.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 6, 0));
        factory.AppendChild(nameText);

        var descText = new FrameworkElementFactory(typeof(TextBlock));
        descText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Description"));
        descText.SetValue(TextBlock.FontSizeProperty, 11.0);
        descText.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9CDCFE")));
        descText.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        factory.AppendChild(descText);

        template.VisualTree = factory;
        listBox.ItemTemplate = template;

        var border = new Border
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#252526")),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#007ACC")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(2),
            Child = listBox,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 10, ShadowDepth = 3, Opacity = 0.5 }
        };

        var popup = new Popup
        {
            PlacementTarget = textBox,
            Placement = PlacementMode.Relative,
            StaysOpen = false,
            AllowsTransparency = true,
            Child = border
        };

        var ctx = new PopupContext(textBox, popup, listBox);
        Contexts[textBox] = ctx;

        listBox.MouseLeftButtonUp += (s, ev) => CommitSelection(ctx);
    }

    private static void RemovePopup(TextBox textBox)
    {
        if (Contexts.TryGetValue(textBox, out var ctx))
        {
            ctx.Popup.IsOpen = false;
            ctx.ListBox.ItemsSource = null;
            Contexts.Remove(textBox);
        }
    }

    private static void OnTextBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && Contexts.TryGetValue(tb, out var ctx))
        {
            tb.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!ctx.ListBox.IsKeyboardFocusWithin)
                {
                    ctx.Popup.IsOpen = false;
                    ctx.ListBox.ItemsSource = null;
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    private static void OnTextBoxSelectionChanged(object sender, RoutedEventArgs e)
    {
    }

    private static void OnTextBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            EvaluateIntellisense(tb);
        }
    }

    private static void OnTextBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb || !Contexts.TryGetValue(tb, out var ctx) || !ctx.Popup.IsOpen)
            return;

        if (e.Key == Key.Down)
        {
            if (ctx.ListBox.Items.Count > 0)
            {
                ctx.ListBox.SelectedIndex = (ctx.ListBox.SelectedIndex + 1) % ctx.ListBox.Items.Count;
                ctx.ListBox.ScrollIntoView(ctx.ListBox.SelectedItem);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            if (ctx.ListBox.Items.Count > 0)
            {
                ctx.ListBox.SelectedIndex = (ctx.ListBox.SelectedIndex - 1 + ctx.ListBox.Items.Count) % ctx.ListBox.Items.Count;
                ctx.ListBox.ScrollIntoView(ctx.ListBox.SelectedItem);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter || e.Key == Key.Tab)
        {
            CommitSelection(ctx);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ctx.Popup.IsOpen = false;
            ctx.ListBox.ItemsSource = null;
            e.Handled = true;
        }
    }

    private static void CommitSelection(PopupContext ctx)
    {
        try
        {
            if (ctx.ListBox.SelectedItem is not IntellisenseItem item) return;

            var tb = ctx.TextBox;
            var text = tb.Text ?? string.Empty;
            var start = Math.Clamp(ctx.ReplaceStartIndex, 0, text.Length);
            var len = Math.Clamp(ctx.ReplaceLength, 0, text.Length - start);

            var insertText = item.InsertText;
            if (item.IsToolName)
            {
                insertText = item.InsertText + ".";
            }

            var newText = text.Remove(start, len).Insert(start, insertText);
            tb.Text = newText;
            tb.CaretIndex = Math.Min(start + insertText.Length, newText.Length);
            ctx.Popup.IsOpen = false;
            ctx.ListBox.ItemsSource = null;

            var binding = tb.GetBindingExpression(TextBox.TextProperty);
            binding?.UpdateSource();

            if (item.IsToolName)
            {
                EvaluateIntellisense(tb);
            }
        }
        catch
        {
            ctx.Popup.IsOpen = false;
            ctx.ListBox.ItemsSource = null;
        }
    }

    private static IEnumerable<ToolGraphNodeViewModel>? GetAvailableNodes(TextBox tb)
    {
        var boundNodes = GetNodes(tb) as IEnumerable<ToolGraphNodeViewModel>;
        if (boundNodes != null) return boundNodes;

        DependencyObject current = tb;
        while (current != null)
        {
            if (current is FrameworkElement fe && fe.DataContext is ToolEditorViewModel vm)
            {
                return vm.Nodes;
            }
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static void EvaluateIntellisense(TextBox tb)
    {
        try
        {
            if (!Contexts.TryGetValue(tb, out var ctx)) return;

            var text = tb.Text ?? string.Empty;
            var caret = tb.CaretIndex;
            if (caret <= 0 || caret > text.Length)
            {
                ctx.Popup.IsOpen = false;
                ctx.ListBox.ItemsSource = null;
                return;
            }

            var sub = text.Substring(0, caret);
            if (string.IsNullOrEmpty(sub))
            {
                ctx.Popup.IsOpen = false;
                ctx.ListBox.ItemsSource = null;
                return;
            }

            var nodes = GetAvailableNodes(tb)?.ToList() ?? new List<ToolGraphNodeViewModel>();

            // Check Trigger 1: Dot after tool name (e.g. "Caliper1.", "{Caliper1.", "EP.Edge1.", "Caliper1.V")
            var dotIndex = sub.LastIndexOf('.');
            if (dotIndex >= 0 && dotIndex < sub.Length)
            {
                var textBeforeDot = sub.Substring(0, dotIndex);
                var textAfterDot = dotIndex + 1 < sub.Length ? sub.Substring(dotIndex + 1) : string.Empty;

                var toolToken = ExtractToolToken(textBeforeDot);
                if (!string.IsNullOrEmpty(toolToken))
                {
                    var matchedNode = ResolveNodeByToken(toolToken, nodes);
                    var props = GetPropertiesForNode(matchedNode, toolToken);

                    var filtered = props.Where(p => string.IsNullOrEmpty(textAfterDot) || p.DisplayText.StartsWith(textAfterDot, StringComparison.OrdinalIgnoreCase)).ToList();

                    if (filtered.Count > 0)
                    {
                        ctx.ListBox.ItemsSource = filtered;
                        ctx.ListBox.SelectedIndex = 0;
                        ctx.ReplaceStartIndex = dotIndex + 1;
                        ctx.ReplaceLength = textAfterDot.Length;

                        ShowPopupAtCaret(ctx);
                        return;
                    }
                }
            }

            // Check Trigger 2: Tool Name typing (e.g. "{" or "Cal" or after "{")
            var wordStart = sub.Length - 1;
            while (wordStart >= 0 && wordStart < sub.Length && (char.IsLetterOrDigit(sub[wordStart]) || sub[wordStart] == '_' || sub[wordStart] == '{'))
            {
                if (sub[wordStart] == '{')
                {
                    wordStart++;
                    break;
                }
                wordStart--;
            }

            if (wordStart < 0)
            {
                wordStart = 0;
            }
            else if (wordStart < sub.Length && sub[wordStart] != '{' && !char.IsLetterOrDigit(sub[wordStart]) && sub[wordStart] != '_')
            {
                wordStart++;
            }

            if (wordStart >= 0 && wordStart < sub.Length)
            {
                var typedWord = sub.Substring(wordStart);
                if (typedWord.StartsWith("{")) typedWord = typedWord.Substring(1);

                if (!string.IsNullOrWhiteSpace(typedWord) && typedWord.Length >= 1)
                {
                    var matchingNodes = nodes
                        .Where(n => !string.IsNullOrWhiteSpace(n.RefName) && n.RefName.StartsWith(typedWord, StringComparison.OrdinalIgnoreCase))
                        .Select(n => new IntellisenseItem(n.RefName, n.RefName, $"Tool {n.Type}", "🔧", IsToolName: true))
                        .ToList();

                    var globalItems = new List<IntellisenseItem>
                    {
                        new("TotalPass", "TotalPass", "Kết quả tổng Pass (bool: true/false)", "⚡", IsToolName: false),
                        new("TotalFail", "TotalFail", "Kết quả tổng NG (bool: true/false)", "⚡", IsToolName: false),
                        new("TotalPassBit", "TotalPassBit", "Bit tổng Pass (1/0)", "⚡", IsToolName: false),
                        new("TotalFailBit", "TotalFailBit", "Bit tổng NG (1/0)", "⚡", IsToolName: false),
                        new("PassCount", "PassCount", "Số lượng công cụ Pass", "⚡", IsToolName: false),
                        new("FailCount", "FailCount", "Số lượng công cụ NG", "⚡", IsToolName: false)
                    };

                    matchingNodes.AddRange(globalItems.Where(g => g.DisplayText.StartsWith(typedWord, StringComparison.OrdinalIgnoreCase)));

                    if (matchingNodes.Count > 0)
                    {
                        ctx.ListBox.ItemsSource = matchingNodes;
                        ctx.ListBox.SelectedIndex = 0;
                        ctx.ReplaceStartIndex = wordStart;
                        ctx.ReplaceLength = typedWord.Length;

                        ShowPopupAtCaret(ctx);
                        return;
                    }
                }
            }

            ctx.Popup.IsOpen = false;
            ctx.ListBox.ItemsSource = null;
        }
        catch
        {
            if (Contexts.TryGetValue(tb, out var c))
            {
                c.Popup.IsOpen = false;
                c.ListBox.ItemsSource = null;
            }
        }
    }

    private static string ExtractToolToken(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var end = s.Length - 1;
        while (end >= 0 && char.IsWhiteSpace(s[end])) end--;
        if (end < 0 || end >= s.Length) return string.Empty;

        var start = end;
        while (start >= 0 && start < s.Length && (char.IsLetterOrDigit(s[start]) || s[start] == '_' || s[start] == '.' || s[start] == '-'))
        {
            start--;
        }
        start++;
        if (start < 0 || start > end || start >= s.Length) return string.Empty;
        var len = end - start + 1;
        if (len <= 0 || start + len > s.Length) return string.Empty;

        var token = s.Substring(start, len).TrimStart('{', '$');
        return token;
    }

    private static ToolGraphNodeViewModel? ResolveNodeByToken(string token, List<ToolGraphNodeViewModel> nodes)
    {
        var name = token;
        var parts = token.Split('.');
        if (parts.Length > 1)
        {
            name = parts.Last();
        }

        return nodes.FirstOrDefault(n => string.Equals(n.RefName, name, StringComparison.OrdinalIgnoreCase));
    }

    private static List<IntellisenseItem> GetPropertiesForNode(ToolGraphNodeViewModel? node, string toolToken)
    {
        var type = node?.Type ?? string.Empty;

        if (string.IsNullOrEmpty(type))
        {
            if (toolToken.StartsWith("Origin", StringComparison.OrdinalIgnoreCase)) type = "Origin";
            else if (toolToken.StartsWith("Point", StringComparison.OrdinalIgnoreCase)) type = "Point";
            else if (toolToken.StartsWith("Dist", StringComparison.OrdinalIgnoreCase)) type = "Distance";
            else if (toolToken.StartsWith("Ang", StringComparison.OrdinalIgnoreCase)) type = "Angle";
            else if (toolToken.StartsWith("Line", StringComparison.OrdinalIgnoreCase)) type = "Line";
            else if (toolToken.StartsWith("Code", StringComparison.OrdinalIgnoreCase)) type = "CodeDetection";
            else if (toolToken.StartsWith("Blob", StringComparison.OrdinalIgnoreCase)) type = "BlobDetection";
            else if (toolToken.StartsWith("Circle", StringComparison.OrdinalIgnoreCase)) type = "Point";
        }

        if (string.Equals(type, "Origin", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "Point", StringComparison.OrdinalIgnoreCase))
        {
            return new List<IntellisenseItem>
            {
                new("X", "X", "Tọa độ X (mm nếu calib / px)", "⚡", false),
                new("Y", "Y", "Tọa độ Y (mm nếu calib / px)", "⚡", false),
                new("AngleDeg", "AngleDeg", "Góc xoay (độ)", "⚡", false),
                new("Angle", "Angle", "Góc xoay (độ)", "⚡", false),
                new("Pass", "Pass", "Kết quả OK/NG (bool: true/false)", "⚡", false),
                new("Score", "Score", "Điểm số khớp Pattern/Origin (0.0 -> 1.0)", "⚡", false),
                new("X_mm", "X_mm", "Tọa độ X (mm)", "⚡", false),
                new("Y_mm", "Y_mm", "Tọa độ Y (mm)", "⚡", false),
                new("X_px", "X_px", "Tọa độ X (pixel)", "⚡", false),
                new("Y_px", "Y_px", "Tọa độ Y (pixel)", "⚡", false)
            };
        }

        if (string.Equals(type, "Distance", StringComparison.OrdinalIgnoreCase))
        {
            return new List<IntellisenseItem>
            {
                new("Value", "Value", "Khoảng cách (mm nếu calib / px)", "⚡", false),
                new("Distance", "Distance", "Khoảng cách (mm nếu calib / px)", "⚡", false),
                new("Pass", "Pass", "Kết quả kiểm tra OK/NG (bool: true/false)", "⚡", false),
                new("Value_mm", "Value_mm", "Khoảng cách (mm)", "⚡", false),
                new("Value_px", "Value_px", "Khoảng cách (pixel)", "⚡", false)
            };
        }

        if (string.Equals(type, "Angle", StringComparison.OrdinalIgnoreCase))
        {
            return new List<IntellisenseItem>
            {
                new("AngleDeg", "AngleDeg", "Góc nghiêng (độ)", "⚡", false),
                new("Value", "Value", "Góc nghiêng (độ)", "⚡", false),
                new("Pass", "Pass", "Kết quả kiểm tra OK/NG (bool: true/false)", "⚡", false)
            };
        }

        if (string.Equals(type, "Line", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "Caliper", StringComparison.OrdinalIgnoreCase))
        {
            return new List<IntellisenseItem>
            {
                new("Found", "Found", "Trạng thái phát hiện đường thẳng (bool: true/false)", "⚡", false),
                new("Pass", "Pass", "Kết quả OK/NG (bool: true/false)", "⚡", false)
            };
        }

        if (string.Equals(type, "CodeDetection", StringComparison.OrdinalIgnoreCase))
        {
            return new List<IntellisenseItem>
            {
                new("Text", "Text", "Chuỗi văn bản mã đọc được (Barcode/QR)", "⚡", false),
                new("Pass", "Pass", "Kết quả đọc mã OK/NG (bool: true/false)", "⚡", false),
                new("Found", "Found", "Trạng thái phát hiện mã (bool: true/false)", "⚡", false)
            };
        }

        if (string.Equals(type, "ImageOutput", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "OutputImage", StringComparison.OrdinalIgnoreCase))
        {
            return new List<IntellisenseItem>
            {
                new("Text", "Text", "Đường dẫn file ảnh đã xuất (Absolute Path)", "⚡", false),
                new("SavedFilePath", "SavedFilePath", "Đường dẫn file ảnh đã xuất (Absolute Path)", "⚡", false),
                new("Pass", "Pass", "Trạng thái lưu ảnh thành công (bool: true/false)", "⚡", false),
                new("Found", "Found", "Trạng thái lưu ảnh thành công (bool: true/false)", "⚡", false)
            };
        }

        if (string.Equals(type, "SurfaceCompare", StringComparison.OrdinalIgnoreCase))
        {
            return new List<IntellisenseItem>
            {
                new("Pass", "Pass", "Kết quả so sánh bề mặt OK/NG", "⚡", false),
                new("Count", "Count", "Số lượng vùng lỗi phát hiện", "⚡", false),
                new("Score", "Score", "Diện tích vùng lỗi lớn nhất (px)", "⚡", false),
                new("MaxArea", "MaxArea", "Diện tích vùng lỗi lớn nhất (px)", "⚡", false)
            };
        }

        if (string.Equals(type, "BlobDetection", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "Blob", StringComparison.OrdinalIgnoreCase))
        {
            return new List<IntellisenseItem>
            {
                new("Value", "Value", "Số lượng blob phát hiện (Count)", "⚡", false),
                new("Pass", "Pass", "Kết quả đánh giá OK/NG (bool: true/false)", "⚡", false)
            };
        }

        if (string.Equals(type, "Condition", StringComparison.OrdinalIgnoreCase))
        {
            return new List<IntellisenseItem>
            {
                new("Pass", "Pass", "Kết quả logic của Condition (bool: true/false)", "⚡", false)
            };
        }

        if (string.Equals(type, "Text", StringComparison.OrdinalIgnoreCase))
        {
            return new List<IntellisenseItem>
            {
                new("Text", "Text", "Nội dung văn bản hiển thị", "⚡", false),
                new("Pass", "Pass", "Trạng thái OK/NG", "⚡", false)
            };
        }

        if (string.Equals(type, "Preprocess", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "ImageSource", StringComparison.OrdinalIgnoreCase))
        {
            return new List<IntellisenseItem>
            {
                new("Pass", "Pass", "Trạng thái xử lý ảnh OK/NG", "⚡", false)
            };
        }

        // Distance / LineLineDistance / PointLineDistance / SegmentLineDistance / CircleFinder / Diameter / Angle / EdgePair / EdgePairDetect / LinePairDetection
        return new List<IntellisenseItem>
        {
            new("Value", "Value", "Giá trị đo đạc (mm/px/độ/bán kính)", "⚡", false),
            new("Pass", "Pass", "Kết quả đánh giá OK/NG (bool: true/false)", "⚡", false),
            new("Found", "Found", "Trạng thái tìm thấy đối tượng (bool: true/false)", "⚡", false)
        };
    }

    private static void ShowPopupAtCaret(PopupContext ctx)
    {
        var tb = ctx.TextBox;
        try
        {
            var caretRect = tb.GetRectFromCharacterIndex(Math.Min(tb.CaretIndex, tb.Text.Length));
            if (!caretRect.IsEmpty)
            {
                ctx.Popup.HorizontalOffset = Math.Max(0, caretRect.Left);
                ctx.Popup.VerticalOffset = caretRect.Bottom + 4;
            }
            else
            {
                ctx.Popup.HorizontalOffset = 10;
                ctx.Popup.VerticalOffset = tb.ActualHeight;
            }
        }
        catch
        {
            ctx.Popup.HorizontalOffset = 10;
            ctx.Popup.VerticalOffset = tb.ActualHeight;
        }

        ctx.Popup.IsOpen = true;
    }
}
