using AgentWiki.App;
using AgentWiki.App.Infrastructure;
using AgentWiki.Desktop.Services;
using AgentWiki.Desktop.ViewModels;
using AgentWiki.Desktop.Views;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace AgentWiki.Desktop;

public partial class App : Application
{
    private ServiceProvider? _services;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        // Override theme placeholders with a single real mono face for this OS.
        Resources["AwFont.Mono"] = AppFonts.Mono;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        AgentWikiLogging.Configure(verbose: false, enableConsoleSink: false);

        var collection = new ServiceCollection();
        collection.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(dispose: true);
        });
        collection.AddAgentWikiServices();
        collection.AddSingleton<UiSettingsStore>();
        collection.AddSingleton<ConfigEditorService>();
        collection.AddSingleton<MainViewModel>();
        collection.AddSingleton<DashboardViewModel>();
        collection.AddSingleton<GenerateViewModel>();
        collection.AddSingleton<UpdateViewModel>();
        collection.AddSingleton<SetupViewModel>();
        collection.AddSingleton<SettingsViewModel>();
        collection.AddSingleton<ProviderViewModel>();
        collection.AddSingleton<WikiBrowserViewModel>();
        collection.AddSingleton<LogsViewModel>();

        _services = collection.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = _services.GetRequiredService<MainViewModel>()
            };

            desktop.Exit += (_, _) =>
            {
                Log.CloseAndFlush();
                _services?.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
