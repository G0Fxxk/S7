using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using S7WpfApp.Services;
using S7WpfApp.ViewModels;
using S7WpfApp.Views;

namespace S7WpfApp;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 全局异常捕获
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            base.OnStartup(e);

            // 确保应用数据目录存在
            var appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "S7WpfApp");
            Directory.CreateDirectory(appDataDir);

            var services = new ServiceCollection();

            // 注册服务
            services.AddSingleton<IPlcService, PlcService>();
            services.AddSingleton<IAuthService, AuthService>();
            services.AddSingleton<IBindingService, BindingService>();
            services.AddSingleton<IDbBlockService, DbBlockService>();
            services.AddSingleton<ISymbolService, SymbolService>();
            services.AddSingleton<IDbFileService, DbFileService>();
            services.AddSingleton<IAxisConfigService, AxisConfigService>();
            services.AddSingleton<IRecipeLogService, RecipeLogService>();
            services.AddSingleton<IRecipeService, RecipeService>();
            services.AddSingleton<IPlcDataReadWriteService, PlcDataReadWriteService>();
            services.AddSingleton<IControlPanelConfigService, ControlPanelConfigService>();

            // 注册日志框架
            services.AddLogging(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Debug);
                builder.AddDebug();  // 输出到 VS 调试窗口
            });

            // 注册 ViewModels
            services.AddTransient<MainViewModel>();
            services.AddTransient<ShellViewModel>();
            // Singleton：主 Tab 页 ViewModel，切换页签时需保留已导入的 DB 结构、变量列表和自动刷新状态
            services.AddSingleton<DbParserViewModel>();
            // Singleton：内部有 Timer 和事件订阅，Transient 会导致资源泄漏
            services.AddSingleton<ControlPanelViewModel>();
            services.AddTransient<DbFilesViewModel>();
            services.AddTransient<AxisControlViewModel>();
            // Singleton：内部有事件订阅和日志状态
            services.AddSingleton<RecipeViewModel>();
            services.AddTransient<MultiTrendViewModel>();

            // 注册 Views (Windows)
            services.AddTransient<MainWindow>();
            services.AddTransient<SymbolManageWindow>();
            services.AddTransient<AxisControlWindow>();
            services.AddTransient<AxisConfigWindow>();
            services.AddTransient<TrendWindow>();
            services.AddTransient<MultiTrendWindow>();

            // 注册 Views (UserControls)
            services.AddTransient<ConnectionView>();
            services.AddTransient<ControlPanelView>();
            services.AddTransient<DbFilesView>();
            services.AddTransient<DbParserView>();
            services.AddTransient<RecipeView>();

            Services = services.BuildServiceProvider();

            // 手动创建主窗口（捕获构造异常）
            try
            {
                var mainWindow = Services.GetRequiredService<MainWindow>();
                mainWindow.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"创建主窗口失败:\n{ex.Message}\n\n{ex.InnerException?.Message}\n\n{ex.StackTrace}",
                    "S7WpfApp 错误", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }

            // 异步初始化（不阻塞 UI 线程）
            _ = Task.Run(async () =>
            {
                try
                {
                    var symbolService = Services.GetRequiredService<ISymbolService>();
                    await symbolService.LoadAsync();

                    var dbFileService = Services.GetRequiredService<IDbFileService>();
                    await dbFileService.LoadAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"初始化服务失败: {ex}");
                }
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"启动失败:\n{ex}", "S7WpfApp 错误", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show($"未处理的异常:\n{e.Exception.Message}\n\n{e.Exception.StackTrace}",
            "S7WpfApp 错误", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            MessageBox.Show($"致命错误:\n{ex.Message}\n\n{ex.StackTrace}",
                "S7WpfApp 错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"未观察的任务异常: {e.Exception}");
        e.SetObserved();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 释放所有 DI 容器中的 IDisposable 服务
        if (Services is IDisposable disposable)
        {
            disposable.Dispose();
        }
        base.OnExit(e);
    }
}
