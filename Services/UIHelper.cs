using System.Windows;
using Microsoft.Win32;

namespace S7WpfApp.Services;

/// <summary>
/// UI 辅助类 - 替代 MAUI 的 DisplayAlert、FilePicker 等
/// </summary>
public static class UIHelper
{
    /// <summary>
    /// 显示信息对话框
    /// </summary>
    public static Task DisplayAlert(string title, string message, string accept)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 显示确认对话框
    /// </summary>
    public static Task<bool> DisplayConfirm(string title, string message, string accept, string cancel)
    {
        var result = MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Question);
        return Task.FromResult(result == MessageBoxResult.OK);
    }

    /// <summary>
    /// 显示输入对话框（简易实现）
    /// </summary>
    public static Task<string?> DisplayPrompt(string title, string message, string accept = "确定", string cancel = "取消", string? placeholder = null, string? initialValue = null)
    {
        // WPF 没有内置 InputDialog，使用简易方案
        var dialog = new Window
        {
            Title = title,
            Width = 400,
            MinHeight = 180,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1a, 0x1a, 0x2e)),
            Owner = Application.Current.MainWindow
        };

        var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
        var label = new System.Windows.Controls.TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.White,
            Margin = new Thickness(0, 0, 0, 8)
        };
        var textBox = new System.Windows.Controls.TextBox
        {
            Text = initialValue ?? "",
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2a, 0x2a, 0x4a)),
            Foreground = System.Windows.Media.Brushes.White,
            Padding = new Thickness(8, 4, 8, 4),
            FontSize = 14
        };

        var btnPanel = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };

        string? result = null;
        var okBtn = new System.Windows.Controls.Button { Content = accept, Width = 80, Margin = new Thickness(0, 0, 8, 0) };
        okBtn.Click += (s, e) => { result = textBox.Text; dialog.Close(); };
        var cancelBtn = new System.Windows.Controls.Button { Content = cancel, Width = 80 };
        cancelBtn.Click += (s, e) => dialog.Close();

        btnPanel.Children.Add(okBtn);
        btnPanel.Children.Add(cancelBtn);
        panel.Children.Add(label);
        panel.Children.Add(textBox);
        panel.Children.Add(btnPanel);
        dialog.Content = panel;
        dialog.ShowDialog();

        return Task.FromResult(result);
    }

    /// <summary>
    /// 显示操作选择对话框
    /// </summary>
    public static Task<string?> DisplayActionSheet(string title, string cancel, string? destruction, params string[] buttons)
    {
        // 简易菜单选择
        var dialog = new Window
        {
            Title = title,
            Width = 350,
            Height = 60 + buttons.Length * 40 + (destruction != null ? 40 : 0),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1a, 0x1a, 0x2e)),
            Owner = Application.Current.MainWindow
        };

        string? result = null;
        var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16, 8, 16, 8) };

        foreach (var btn in buttons)
        {
            var b = new System.Windows.Controls.Button
            {
                Content = btn,
                Margin = new Thickness(0, 2, 0, 2),
                Padding = new Thickness(8, 4, 8, 4),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            b.Click += (s, e) => { result = btn; dialog.Close(); };
            panel.Children.Add(b);
        }

        if (destruction != null)
        {
            var db = new System.Windows.Controls.Button
            {
                Content = destruction,
                Foreground = System.Windows.Media.Brushes.Red,
                Margin = new Thickness(0, 8, 0, 2),
                Padding = new Thickness(8, 4, 8, 4)
            };
            db.Click += (s, e) => { result = destruction; dialog.Close(); };
            panel.Children.Add(db);
        }

        var cb = new System.Windows.Controls.Button
        {
            Content = cancel,
            Margin = new Thickness(0, 2, 0, 0),
            Padding = new Thickness(8, 4, 8, 4)
        };
        cb.Click += (s, e) => dialog.Close();
        panel.Children.Add(cb);

        dialog.Content = panel;
        dialog.ShowDialog();
        return Task.FromResult(result);
    }

    /// <summary>
    /// 打开文件对话框
    /// </summary>
    public static string? PickFile(string title, string filter = "所有文件|*.*")
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    /// <summary>
    /// 打开文件夹选择对话框
    /// </summary>
    public static string? PickFolder(string title)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title
        };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
    /// <summary>
    /// 保存文件对话框
    /// </summary>
    public static string? SaveFile(string title, string filter = "所有文件|*.*", string? defaultFileName = null)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            Filter = filter,
            FileName = defaultFileName ?? ""
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
