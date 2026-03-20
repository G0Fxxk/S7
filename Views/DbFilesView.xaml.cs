using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using S7WpfApp.ViewModels;
using S7WpfApp.Services;

namespace S7WpfApp.Views;

/// <summary>
/// DB 文件管理视图 — 导入/重导入/删除 DB 文件，双击可跳转到 DB 解析器
/// </summary>
public partial class DbFilesView : UserControl
{
    private readonly DbFilesViewModel _vm;

    private readonly DbParserViewModel _parserVm;

    public DbFilesView(DbFilesViewModel vm, DbParserViewModel parserVm)
    {
        InitializeComponent();
        _vm = vm;
        _parserVm = parserVm;
        FilesList.ItemsSource = _vm.DbFiles;

        // 初始加载
        _ = _vm.LoadCommand.ExecuteAsync(null);
    }

    private async void OnImportClick(object s, RoutedEventArgs e)
        => await _vm.ImportNewCommand.ExecuteAsync(null);

    private async void OnRefreshClick(object s, RoutedEventArgs e)
        => await _vm.LoadCommand.ExecuteAsync(null);

    private async void OnReimportClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is DbFileEntry entry)
            await _vm.ReimportCommand.ExecuteAsync(entry);
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is DbFileEntry entry)
            await _vm.DeleteCommand.ExecuteAsync(entry);
    }

    /// <summary>
    /// 双击列表项 → 切换到 DB 解析器标签页并加载该文件
    /// </summary>
    private async void OnFileDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FilesList.SelectedItem is not DbFileEntry entry) return;

        // 获取 DbParserViewModel 并加载文件
        await _parserVm.ImportDbFileFromPathAsync(entry.FilePath, entry.DbNumber);

        // 切换到 DB 解析器标签页（index 1）
        var mainWindow = Application.Current.MainWindow;
        if (mainWindow != null)
        {
            var tabControl = FindTabControl(mainWindow);
            if (tabControl != null)
                tabControl.SelectedIndex = 1; // DB 解析器
        }
    }

    /// <summary>
    /// 递归查找 MainWindow 中的 TabControl
    /// </summary>
    private static TabControl? FindTabControl(DependencyObject parent)
    {
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is TabControl tc) return tc;
            var result = FindTabControl(child);
            if (result != null) return result;
        }
        return null;
    }
}
