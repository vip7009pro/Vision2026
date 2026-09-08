using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using VisionInspectionApp.UI.Services;

namespace VisionInspectionApp.UI.Views.Documentation;

public partial class DocumentationViewerWindow : Window
{
    private readonly List<DocumentItem> _allDocs;
    private readonly string _docsDir;
    private DocumentItem? _currentDoc;
    private string? _lastGeneratedHtmlPath;

    public DocumentationViewerWindow(string? initialDocId = null)
    {
        InitializeComponent();

        _docsDir = DocumentationService.ResolveDocsDirectory();
        _allDocs = DocumentationService.GetAvailableDocuments();

        // Nạp danh sách tài liệu lên ListBox
        LstDocuments.ItemsSource = _allDocs;

        // Chọn tài liệu ban đầu
        DocumentItem? targetDoc = null;
        if (!string.IsNullOrWhiteSpace(initialDocId))
        {
            targetDoc = _allDocs.FirstOrDefault(d => d.Id.Equals(initialDocId, StringComparison.OrdinalIgnoreCase));
        }

        targetDoc ??= _allDocs.FirstOrDefault();

        if (targetDoc != null)
        {
            LstDocuments.SelectedItem = targetDoc;
        }
    }

    private void LstDocuments_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LstDocuments.SelectedItem is not DocumentItem doc) return;

        _currentDoc = doc;
        TxtActiveDocTitle.Text = $"{doc.Icon} {doc.Title}";
        TxtDocSubtitle.Text = doc.Description;

        DisplayDocument(doc);
    }

    private void DisplayDocument(DocumentItem doc)
    {
        try
        {
            var markdown = DocumentationService.LoadDocumentMarkdown(doc);
            
            // Xác định theme sáng hay tối hiện tại
            bool isDark = true;
            try
            {
                var currentTheme = System.Windows.Application.Current?.Resources["WindowBackgroundBrush"]?.ToString();
                if (currentTheme != null && currentTheme.Contains("#FFFAFAFA", StringComparison.OrdinalIgnoreCase))
                {
                    isDark = false;
                }
            }
            catch { }

            var html = DocumentationService.ConvertMarkdownToHtml(markdown, _docsDir, isDark);

            // Ghi ra file tạm thời để WebBrowser nạp ảnh local và render ổn định
            var tempFolder = Path.Combine(Path.GetTempPath(), "VisionInspectionDocs");
            if (!Directory.Exists(tempFolder))
            {
                Directory.CreateDirectory(tempFolder);
            }

            var tempFile = Path.Combine(tempFolder, $"{doc.Id}.html");
            File.WriteAllText(tempFile, html, Encoding.UTF8);
            _lastGeneratedHtmlPath = tempFile;

            DocWebBrowser.Navigate(new Uri(tempFile));
        }
        catch (Exception ex)
        {
            DocWebBrowser.NavigateToString($"<html><body style='font-family:Segoe UI;padding:20px;color:red;'><h3>Lỗi tải tài liệu:</h3><p>{ex.Message}</p></body></html>");
        }
    }

    private void TxtSearchFilter_TextChanged(object sender, TextChangedEventArgs e)
    {
        var filter = TxtSearchFilter.Text?.Trim().ToLowerInvariant() ?? "";
        BtnClearSearch.Visibility = string.IsNullOrEmpty(filter) ? Visibility.Collapsed : Visibility.Visible;

        if (string.IsNullOrEmpty(filter))
        {
            LstDocuments.ItemsSource = _allDocs;
        }
        else
        {
            var filtered = _allDocs.Where(d =>
                d.Title.ToLowerInvariant().Contains(filter) ||
                d.Description.ToLowerInvariant().Contains(filter) ||
                d.Category.ToLowerInvariant().Contains(filter)
            ).ToList();

            LstDocuments.ItemsSource = filtered;
            if (filtered.Count > 0 && !filtered.Contains(_currentDoc))
            {
                LstDocuments.SelectedIndex = 0;
            }
        }
    }

    private void BtnClearSearch_Click(object sender, RoutedEventArgs e)
    {
        TxtSearchFilter.Text = "";
    }

    private void BtnOpenInBrowser_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastGeneratedHtmlPath) || !File.Exists(_lastGeneratedHtmlPath))
        {
            if (_currentDoc != null)
            {
                DisplayDocument(_currentDoc);
            }
        }

        if (!string.IsNullOrWhiteSpace(_lastGeneratedHtmlPath) && File.Exists(_lastGeneratedHtmlPath))
        {
            try
            {
                Process.Start(new ProcessStartInfo(_lastGeneratedHtmlPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Không thể mở trình duyệt: {ex.Message}", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void BtnOpenDocsFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Directory.Exists(_docsDir))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", _docsDir) { UseShellExecute = true });
            }
            else
            {
                MessageBox.Show($"Thư mục tài liệu chưa được tạo tại: {_docsDir}", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Không thể mở thư mục: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnPrint_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            dynamic doc = DocWebBrowser.Document;
            doc?.parentWindow?.print();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Không thể gọi hộp thoại in: {ex.Message}\nBạn có thể nhấn nút 'Mở Trình Duyệt' rồi bấm Ctrl+P để in tài liệu.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
