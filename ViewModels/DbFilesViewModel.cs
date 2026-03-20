using System.IO;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using S7WpfApp.Services;

namespace S7WpfApp.ViewModels;

/// <summary>
/// DB 文件管理页面 ViewModel
/// </summary>
public partial class DbFilesViewModel : ObservableObject
{
    private readonly IDbFileService _dbFileService;
    private readonly TiaDbParser _tiaParser = new();
    private readonly ISymbolService _symbolService;

    /// <summary>
    /// PlcDB 文件夹路径（程序目录下）
    /// </summary>
    private static readonly string PlcDbFolder =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PlcDB");

    [ObservableProperty]
    private string _title = "DB 文件管理";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _status = "";

    /// <summary>
    /// 已保存的 DB 文件列表
    /// </summary>
    public ObservableCollection<DbFileEntry> DbFiles { get; } = new();

    public DbFilesViewModel(IDbFileService dbFileService, ISymbolService symbolService)
    {
        _dbFileService = dbFileService;
        _symbolService = symbolService;
    }

    /// <summary>
    /// 加载 DB 文件列表
    /// </summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        await _dbFileService.LoadAsync();
        RefreshList();
    }

    private void RefreshList()
    {
        DbFiles.Clear();
        foreach (var entry in _dbFileService.GetAll())
        {
            DbFiles.Add(entry);
        }
    }

    /// <summary>
    /// 重新导入 DB 文件
    /// </summary>
    [RelayCommand]
    private async Task ReimportAsync(DbFileEntry? entry)
    {
        if (entry == null) return;


        // 检查文件是否存在
        if (!File.Exists(entry.FilePath))
        {
            var result = await UIHelper.DisplayConfirm(
                "文件不存在",
                $"文件 \"{entry.FileName}\" 不存在。\n是否要选择新文件？",
                "选择新文件",
                "取消");

            if (result)
            {
                await SelectNewFileAsync(entry);
            }
            return;
        }

        await ReimportFileAsync(entry);
    }

    /// <summary>
    /// 选择新文件替换
    /// </summary>
    [RelayCommand]
    private async Task SelectNewFileAsync(DbFileEntry? entry)
    {
        if (entry == null) return;

        try
        {
            var pickedPath = UIHelper.PickFile("选择文件", "DB文件|*.db;*.txt;*.awl|所有文件|*.*"); var result = pickedPath != null ? new { FullPath = pickedPath, FileName = System.IO.Path.GetFileName(pickedPath) } : null;

            if (result == null) return;

            // 复制到 PlcDB 文件夹
            var localPath = CopyToPlcDbFolder(result.FullPath, result.FileName);
            entry.FilePath = localPath;
            entry.FileName = result.FileName;

            await ReimportFileAsync(entry);
        }
        catch (Exception ex)
        {
            Status = $"选择文件失败: {ex.Message}";
        }
    }

    /// <summary>
    /// 重新导入文件
    /// </summary>
    private async Task ReimportFileAsync(DbFileEntry entry)
    {

        try
        {
            IsBusy = true;
            Status = $"正在重新导入 DB{entry.DbNumber}...";

            var content = await File.ReadAllTextAsync(entry.FilePath);
            var tags = _tiaParser.Parse(content, entry.DbNumber);

            if (tags != null && tags.Count > 0)
            {
                // 更新条目信息
                entry.DbName = _tiaParser.ParsedDbName;
                entry.VariableCount = tags.Count;
                entry.LastUpdateTime = DateTime.Now;

                // 更新符号地址
                int updatedCount = _symbolService.UpdateAddressesFromParsedTags(entry.DbNumber, tags);

                _dbFileService.AddOrUpdate(entry);
                await _dbFileService.SaveAsync();

                RefreshList();

                if (updatedCount > 0)
                {
                    await UIHelper.DisplayAlert("成功",
                        $"已重新导入 DB{entry.DbNumber}\n更新了 {updatedCount} 个符号地址", "确定");
                }
                else
                {
                    await UIHelper.DisplayAlert("成功",
                        $"已重新导入 DB{entry.DbNumber}\n共 {tags.Count} 个变量", "确定");
                }

                Status = $"已重新导入 DB{entry.DbNumber}";
            }
            else
            {
                Status = "解析失败或文件为空";
            }
        }
        catch (Exception ex)
        {
            Status = $"导入失败: {ex.Message}";
            await UIHelper.DisplayAlert("错误", $"导入失败: {ex.Message}", "确定");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 删除 DB 文件条目
    /// </summary>
    [RelayCommand]
    private async Task DeleteAsync(DbFileEntry? entry)
    {
        if (entry == null) return;

        var confirm = await UIHelper.DisplayConfirm(
            "确认删除",
            $"确定要删除 DB{entry.DbNumber} ({entry.FileName}) 吗？\n（不会删除实际文件）",
            "删除",
            "取消");

        if (!confirm) return;

        _dbFileService.Remove(entry.Id);
        await _dbFileService.SaveAsync();
        RefreshList();

        Status = $"已删除 DB{entry.DbNumber}";
    }

    /// <summary>
    /// 导入新 DB 文件
    /// </summary>
    [RelayCommand]
    private async Task ImportNewAsync()
    {
        try
        {
            var pickedPath = UIHelper.PickFile("选择文件", "DB文件|*.db;*.txt;*.awl|所有文件|*.*"); var result = pickedPath != null ? new { FullPath = pickedPath, FileName = System.IO.Path.GetFileName(pickedPath) } : null;

            if (result == null) return;

            IsBusy = true;
            Status = $"正在解析: {result.FileName}";

            var content = await File.ReadAllTextAsync(result.FullPath);

            // 检测 DB 编号
            int detectedDbNumber = VariableParserService.DetectDbNumber(result.FullPath, content);

            var inputDbNumber = await UIHelper.DisplayPrompt(
                "确认 DB 编号",
                $"检测到可能的 DB 编号: {detectedDbNumber}\n请确认或修改：",
                placeholder: "DB 编号",
                initialValue: detectedDbNumber.ToString());

            if (string.IsNullOrWhiteSpace(inputDbNumber))
            {
                IsBusy = false;
                return;
            }

            if (!int.TryParse(inputDbNumber, out var dbNumber))
            {
                dbNumber = detectedDbNumber;
            }

            var tags = _tiaParser.Parse(content, dbNumber);

            if (tags != null && tags.Count > 0)
            {
                // 复制到 PlcDB 文件夹
                var localPath = CopyToPlcDbFolder(result.FullPath, result.FileName);

                var entry = new DbFileEntry
                {
                    FilePath = localPath,
                    FileName = result.FileName,
                    DbNumber = dbNumber,
                    DbName = _tiaParser.ParsedDbName,
                    VariableCount = tags.Count
                };

                _dbFileService.AddOrUpdate(entry);
                await _dbFileService.SaveAsync();

                // 更新符号地址
                int updatedCount = _symbolService.UpdateAddressesFromParsedTags(dbNumber, tags);

                RefreshList();

                Status = $"已导入 DB{dbNumber} - {tags.Count} 个变量";

                if (updatedCount > 0)
                {
                    await UIHelper.DisplayAlert("成功",
                        $"已导入 DB{dbNumber}\n共 {tags.Count} 个变量，更新了 {updatedCount} 个符号地址", "确定");
                }
            }
            else
            {
                Status = "解析失败或文件为空";
            }
        }
        catch (Exception ex)
        {
            Status = $"导入失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }



    /// <summary>
    /// 将 DB 文件复制到程序目录的 PlcDB 文件夹，返回本地路径
    /// </summary>
    private static string CopyToPlcDbFolder(string sourcePath, string fileName)
    {
        Directory.CreateDirectory(PlcDbFolder);
        var destPath = Path.Combine(PlcDbFolder, fileName);

        // 如果源文件和目标文件不同则复制（覆盖已有）
        var fullSource = Path.GetFullPath(sourcePath);
        var fullDest = Path.GetFullPath(destPath);
        if (!string.Equals(fullSource, fullDest, StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(sourcePath, destPath, overwrite: true);
        }

        return destPath;
    }
}
