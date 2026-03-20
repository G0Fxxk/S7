using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace S7WpfApp.Views;

/// <summary>
/// 导出项包装类（用于对话框中的通道选择列表）
/// </summary>
public class ExportChannelItem
{
    public string Name { get; set; } = "";
    public bool IsExportSelected { get; set; } = true;
    public int OriginalIndex { get; set; }
}

/// <summary>
/// 趋势导出对话框
/// </summary>
public partial class TrendExportDialog : Window
{
    public List<ExportChannelItem> ChannelItems { get; } = new();

    // 导出结果
    public bool UseTimeRange => UseTimeRangeCheck.IsChecked == true;
    public DateTime? ExportStartTime { get; private set; }
    public DateTime? ExportEndTime { get; private set; }
    public bool IncludeChart => IncludeChartCheck.IsChecked == true;
    public bool IncludeData => IncludeDataCheck.IsChecked == true;
    public List<int> SelectedChannelIndices => ChannelItems
        .Where(c => c.IsExportSelected).Select(c => c.OriginalIndex).ToList();

    /// <summary>
    /// 导出格式: "pdf" 或 "csv"
    /// </summary>
    public string ExportFormat { get; private set; } = "pdf";

    public TrendExportDialog(IEnumerable<string> channelNames, DateTime? firstSampleTime, DateTime? lastSampleTime)
    {
        InitializeComponent();

        int idx = 0;
        foreach (var name in channelNames)
        {
            ChannelItems.Add(new ExportChannelItem { Name = name, IsExportSelected = true, OriginalIndex = idx++ });
        }
        ChannelCheckList.ItemsSource = ChannelItems;

        // 默认时间范围
        StartTimeBox.Text = (firstSampleTime ?? DateTime.Now.AddMinutes(-10)).ToString("yyyy-MM-dd HH:mm:ss");
        EndTimeBox.Text = (lastSampleTime ?? DateTime.Now).ToString("yyyy-MM-dd HH:mm:ss");
    }

    private void OnTimeRangeCheckChanged(object sender, RoutedEventArgs e)
    {
        if (TimeRangePanel == null) return; // InitializeComponent 期间防护
        TimeRangePanel.IsEnabled = UseTimeRangeCheck.IsChecked == true;
    }

    private void OnSelectAllChanged(object sender, RoutedEventArgs e)
    {
        if (ChannelCheckList == null) return; // InitializeComponent 期间防护
        bool selectAll = SelectAllCheck.IsChecked == true;
        foreach (var item in ChannelItems)
            item.IsExportSelected = selectAll;

        // 刷新 UI
        ChannelCheckList.ItemsSource = null;
        ChannelCheckList.ItemsSource = ChannelItems;
    }

    private bool TryParseTimeRange()
    {
        if (!UseTimeRange) return true;

        if (DateTime.TryParse(StartTimeBox.Text, out var start) && DateTime.TryParse(EndTimeBox.Text, out var end))
        {
            if (end <= start)
            {
                MessageBox.Show("结束时间必须大于起始时间。", "时间范围错误");
                return false;
            }
            ExportStartTime = start;
            ExportEndTime = end;
            return true;
        }
        MessageBox.Show("时间格式无效，请使用 yyyy-MM-dd HH:mm:ss 格式。", "格式错误");
        return false;
    }

    private void OnExportPdfClick(object sender, RoutedEventArgs e)
    {
        if (!TryParseTimeRange()) return;
        if (SelectedChannelIndices.Count == 0)
        {
            MessageBox.Show("请至少选择一个通道。", "提示");
            return;
        }
        ExportFormat = "pdf";
        DialogResult = true;
    }

    private void OnExportCsvClick(object sender, RoutedEventArgs e)
    {
        if (!TryParseTimeRange()) return;
        if (SelectedChannelIndices.Count == 0)
        {
            MessageBox.Show("请至少选择一个通道。", "提示");
            return;
        }
        ExportFormat = "csv";
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
