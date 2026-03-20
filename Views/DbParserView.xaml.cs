using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using S7WpfApp.ViewModels;
using S7WpfApp.Models;
using S7WpfApp.Services;

namespace S7WpfApp.Views;

/// <summary>
/// DB 解析器视图 — 导入/解析 TIA Portal DB 文件，显示变量列表，支持读写和趋势
/// </summary>
public partial class DbParserView : UserControl
{
    private readonly DbParserViewModel _vm;

    private readonly ISymbolService _symbolService;

    public DbParserView(DbParserViewModel vm, ISymbolService symbolService)
    {
        InitializeComponent();
        _vm = vm;
        _symbolService = symbolService;

        // 绑定状态更新
        _vm.PropertyChanged += (s, e) =>
        {
            Dispatcher.Invoke(() =>
            {
                if (e.PropertyName == nameof(_vm.StatusMessage)) StatusText.Text = _vm.StatusMessage;
                if (e.PropertyName == nameof(_vm.DbInfo)) DbInfoText.Text = _vm.DbInfo;
                if (e.PropertyName == nameof(_vm.Variables))
                {
                    VariablesList.ItemsSource = _vm.Variables.Where(v => v.IsVisible).ToList();
                }
            });
        };
    }

    private void RefreshList()
    {
        VariablesList.ItemsSource = _vm.Variables.Where(v => v.IsVisible).ToList();
    }

    private async void OnImportClick(object s, RoutedEventArgs e)
    {
        await _vm.ImportDbFileCommand.ExecuteAsync(null);
        RefreshList();
    }

    private async void OnReadAllClick(object s, RoutedEventArgs e)
    {
        await _vm.ReadAllCommand.ExecuteAsync(null);
        RefreshList();
    }

    private async void OnWriteClick(object s, RoutedEventArgs e)
    {
        _vm.WriteValue = WriteValueBox.Text;
        await _vm.WriteValueCommand.ExecuteAsync(null);
        RefreshList();
    }

    private void OnAutoRefreshClick(object s, RoutedEventArgs e)
        => _vm.ToggleAutoRefreshCommand.Execute(null);

    private void OnSelectAllClick(object s, RoutedEventArgs e)
    {
        _vm.SelectAllVariablesCommand.Execute(null);
        RefreshList();
    }

    private void OnDeselectAllClick(object s, RoutedEventArgs e)
    {
        _vm.DeselectAllVariablesCommand.Execute(null);
        RefreshList();
    }

    private void OnTrendClick(object s, RoutedEventArgs e)
    {
        if (_vm.SelectedVariable != null)
            _vm.OpenTrendPopupCommand.Execute(_vm.SelectedVariable);
    }

    private async void OnAddSymbolClick(object s, RoutedEventArgs e)
    {
        if (_vm.SelectedVariable != null)
            await _vm.AddToSymbolTableCommand.ExecuteAsync(_vm.SelectedVariable);
    }

    private void OnManageSymbolsClick(object s, RoutedEventArgs e)
    {
        var win = new SymbolManageWindow(_symbolService) { Owner = Application.Current.MainWindow };
        win.ShowDialog();
    }

    /// <summary>
    /// 行内展开/折叠图标点击
    /// </summary>
    private void OnExpandIconClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is System.Windows.FrameworkElement fe && fe.Tag is MonitorVariable v)
        {
            e.Handled = true;
            _vm.ToggleArrayExpandCommand.Execute(v);
            RefreshList();
        }
    }

    private void OnVariableSelected(object s, SelectionChangedEventArgs e)
    {
        if (VariablesList.SelectedItem is MonitorVariable v)
            _vm.SelectedVariable = v;
    }

    private void OnCheckboxClick(object s, RoutedEventArgs e) { /* CheckBox 已双向绑定 */ }
}
