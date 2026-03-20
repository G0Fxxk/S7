using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using S7WpfApp.Services;

namespace S7WpfApp.Views;

/// <summary>
/// 符号管理窗口 — 先显示分组，双击分组后显示该组符号
/// </summary>
public partial class SymbolManageWindow : Window
{
    private readonly ISymbolService _symbolService;
    private string? _currentCategory; // 当前正在查看的分组名

    /// <summary>
    /// 分组显示模型
    /// </summary>
    private class GroupItem
    {
        public string Name { get; set; } = "";
        public int Count { get; set; }
    }

    public SymbolManageWindow(ISymbolService symbolService)
    {
        InitializeComponent();
        _symbolService = symbolService;
        ShowGroups();
    }

    /// <summary>
    /// 显示分组列表
    /// </summary>
    private void ShowGroups()
    {
        _currentCategory = null;

        var symbols = _symbolService.GetAllSymbols().ToList();
        var groups = symbols
            .GroupBy(s => string.IsNullOrEmpty(s.Category) ? "未分组" : s.Category)
            .Select(g => new GroupItem { Name = g.Key, Count = g.Count() })
            .OrderBy(g => g.Name)
            .ToList();

        GroupList.ItemsSource = groups;
        GroupPanel.Visibility = Visibility.Visible;
        SymbolPanel.Visibility = Visibility.Collapsed;
        BackBtn.Visibility = Visibility.Collapsed;

        TitleText.Text = "符号分组";
        CountText.Text = $"共 {groups.Count} 个分组，{symbols.Count} 个符号";
        StatusText.Text = "双击分组查看符号";
    }

    /// <summary>
    /// 显示指定分组的符号
    /// </summary>
    private void ShowSymbols(string category)
    {
        _currentCategory = category;

        var queryCategory = category == "未分组" ? "" : category;
        var symbols = _symbolService.GetAllSymbols()
            .Where(s => (string.IsNullOrEmpty(s.Category) ? "" : s.Category) == queryCategory)
            .OrderBy(s => s.Name)
            .ToList();

        SymbolList.ItemsSource = symbols;
        GroupPanel.Visibility = Visibility.Collapsed;
        SymbolPanel.Visibility = Visibility.Visible;
        BackBtn.Visibility = Visibility.Visible;

        TitleText.Text = $"分组: {category}";
        CountText.Text = $"{symbols.Count} 个符号";
        StatusText.Text = $"正在查看 [{category}] 分组";
    }

    /// <summary>
    /// 双击分组 → 显示该组符号
    /// </summary>
    private void OnGroupDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (GroupList.SelectedItem is GroupItem group)
            ShowSymbols(group.Name);
    }

    /// <summary>
    /// 返回分组列表
    /// </summary>
    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        ShowGroups();
    }

    /// <summary>
    /// 删除符号
    /// </summary>
    private async void OnDeleteSymbolClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not SymbolEntry symbol) return;

        var confirm = await UIHelper.DisplayConfirm(
            "确认删除",
            $"确定要删除符号 \"{symbol.Name}\" 吗？",
            "删除",
            "取消");

        if (!confirm) return;

        _symbolService.RemoveSymbol(symbol.Name);
        await _symbolService.SaveAsync();
        StatusText.Text = $"已删除符号: {symbol.Name}";

        // 刷新当前视图
        if (_currentCategory != null)
            ShowSymbols(_currentCategory);
        else
            ShowGroups();
    }
}
