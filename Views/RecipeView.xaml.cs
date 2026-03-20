using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using S7WpfApp.ViewModels;
using S7WpfApp.Models;

namespace S7WpfApp.Views;

/// <summary>
/// 配方管理视图
/// </summary>
public partial class RecipeView : UserControl
{
    private RecipeViewModel? _vm;

    public RecipeView(RecipeViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = _vm;
    }

    private async void OnCreateTable(object sender, RoutedEventArgs e)
    {
        if (_vm != null)
            await _vm.CreateTableCommand.ExecuteAsync(null);
    }

    private async void OnDeleteTable(object sender, RoutedEventArgs e)
    {
        if (_vm != null)
            await _vm.DeleteTableCommand.ExecuteAsync(null);
    }

    private async void OnAddVariable(object sender, RoutedEventArgs e)
    {
        if (_vm != null)
            await _vm.AddVariableCommand.ExecuteAsync(null);
    }

    private void OnRemoveVariable(object sender, RoutedEventArgs e)
    {
        if (_vm != null)
        {
            var selectedRow = VariableGrid.SelectedItem as RecipeVariableRow;
            _vm.RemoveVariableCommand.Execute(selectedRow);
        }
    }

    private async void OnCreateDataSet(object sender, RoutedEventArgs e)
    {
        if (_vm != null)
            await _vm.CreateDataSetCommand.ExecuteAsync(null);
    }

    private async void OnDeleteDataSet(object sender, RoutedEventArgs e)
    {
        if (_vm != null)
            await _vm.DeleteDataSetCommand.ExecuteAsync(null);
    }

    private async void OnReadFromPlc(object sender, RoutedEventArgs e)
    {
        if (_vm != null)
            await _vm.ReadFromPlcCommand.ExecuteAsync(null);
    }

    private async void OnWriteToPlc(object sender, RoutedEventArgs e)
    {
        if (_vm != null)
            await _vm.WriteToPlcCommand.ExecuteAsync(null);
    }

    private async void OnExportCsv(object sender, RoutedEventArgs e)
    {
        if (_vm != null)
            await _vm.ExportCsvCommand.ExecuteAsync(null);
    }

    private async void OnImportCsv(object sender, RoutedEventArgs e)
    {
        if (_vm != null)
            await _vm.ImportCsvCommand.ExecuteAsync(null);
    }

    private void OnSaveValues(object sender, RoutedEventArgs e)
    {
        _vm?.SaveValuesCommand.Execute(null);
    }

    private void OnOpenLogFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var logDir = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "S7WpfApp", "logs");
            System.IO.Directory.CreateDirectory(logDir);
            System.Diagnostics.Process.Start("explorer.exe", logDir);
        }
        catch (System.Exception ex)
        {
            MessageBox.Show($"打开日志文件夹失败: {ex.Message}", "错误");
        }
    }
}
