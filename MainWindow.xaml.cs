using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using S7WpfApp.ViewModels;
using S7WpfApp.Views;

namespace S7WpfApp;

public partial class MainWindow : Window
{
    public ShellViewModel ShellVm { get; }
    public ConnectionView ConnectionView { get; }
    public DbParserView DbParserView { get; }
    public ControlPanelView ControlPanelView { get; }
    public DbFilesView DbFilesView { get; }
    public RecipeView RecipeView { get; }

    public MainWindow(
        ShellViewModel shellVm,
        ConnectionView connectionView,
        DbParserView dbParserView,
        ControlPanelView controlPanelView,
        DbFilesView dbFilesView,
        RecipeView recipeView)
    {
        ShellVm = shellVm;
        ConnectionView = connectionView;
        DbParserView = dbParserView;
        ControlPanelView = controlPanelView;
        DbFilesView = dbFilesView;
        RecipeView = recipeView;

        DataContext = this;
        InitializeComponent();

        // 根据屏幕工作区域自适应窗口大小（占屏幕 80%），确保不同分辨率下正常显示
        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(workArea.Width * 0.8, 1350);
        Height = Math.Min(workArea.Height * 0.85, 850);

        // 从文件加载图标
        try
        {
            var icoPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo.ico");
            if (System.IO.File.Exists(icoPath))
            {
                Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri(icoPath));
            }
        }
        catch { /* 图标加载失败不影响程序运行 */ }
    }

    private async void OnAdminClick(object sender, RoutedEventArgs e)
    {
        if (ShellVm.IsAdmin)
        {
            await ShellVm.LogoutCommand.ExecuteAsync(null);
        }
        else
        {
            await ShellVm.LoginCommand.ExecuteAsync(null);
        }
    }
}