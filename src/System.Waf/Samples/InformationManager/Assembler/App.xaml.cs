﻿using Microsoft.Extensions.Configuration;
using NLog;
using NLog.Targets;
using NLog.Targets.Wrappers;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.Globalization;
using System.IO;
using System.Waf.Applications;
using System.Waf.Applications.Services;
using System.Windows;
using Waf.InformationManager.Assembler.Properties;
using Waf.InformationManager.Common.Applications.Services;

namespace Waf.InformationManager.Assembler;

public partial class App
{
    private static readonly (string loggerNamePattern, LogLevel minLevel)[] logSettings =
    [
        ("App", LogLevel.Info),
        ("InfoMan.Common.P", LogLevel.Warn),
        ("InfoMan.Common.A", LogLevel.Warn),
        ("InfoMan.Infra.P", LogLevel.Warn),
        ("InfoMan.Infra.A", LogLevel.Warn),
        ("InfoMan.Address.P", LogLevel.Warn),
        ("InfoMan.Address.A", LogLevel.Warn),
        ("InfoMan.Address.D", LogLevel.Warn),
        ("InfoMan.Email.P", LogLevel.Warn),
        ("InfoMan.Email.A", LogLevel.Warn),
        ("InfoMan.Email.D", LogLevel.Warn),
    ];

    private AggregateCatalog? catalog;
    private CompositionContainer? container;
    private IEnumerable<IModuleController> moduleControllers = [];

    public App()
    {
        LogManager.Setup().LoadConfiguration(c =>
        {
            c.Configuration.DefaultCultureInfo = CultureInfo.InvariantCulture;
            var layout = "${date:format=yyyy-MM-dd HH\\:mm\\:ss.ff} [${level:format=FirstCharacter}] ${processid} ${logger} ${message} ${exception}";
            var fileTarget = c.ForTarget("fileTarget").WriteTo(new FileTarget
            {
                FileName = AppInfo.LogFileName,
                Layout = layout,
                ConcurrentWrites = true,
                ArchiveAboveSize = 5_000_000,  // 5 MB
                MaxArchiveFiles = 1,
                ArchiveNumbering = ArchiveNumberingMode.Rolling
            }).WithAsync(AsyncTargetWrapperOverflowAction.Block);
            var traceTarget = c.ForTarget("traceTarget").WriteTo(new TraceTarget
            {
                Layout = layout,
                RawWrite = true
            }).WithAsync(AsyncTargetWrapperOverflowAction.Block);

            foreach (var (loggerNamePattern, minLevel) in logSettings)
            {
                c.ForLogger(loggerNamePattern).FilterMinLevel(minLevel).WriteTo(fileTarget).WriteTo(traceTarget);
            }
        });
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Log.App.Info("{0} {1} is starting; OS: {2}; .NET: {3}", ApplicationInfo.ProductName, ApplicationInfo.Version, Environment.OSVersion, Environment.Version);

#if (!DEBUG)
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, ea) => Log.App.Warn(ea.Exception, "UnobservedTaskException");
#endif
        AppConfig appConfig;
        try
        {
            var config = new ConfigurationBuilder().AddCommandLine(Environment.GetCommandLineArgs()).Build();
            appConfig = config.Get<AppConfig>() ?? new AppConfig();
        }
        catch (Exception ex)
        {
            Log.App.Error(ex, "Command line parsing error");
            appConfig = new AppConfig();
        }

        catalog = new AggregateCatalog();
        catalog.Catalogs.Add(new AssemblyCatalog(typeof(IMessageService).Assembly));   // WinApplicationFramework

        // Load module assemblies as well. See App.config file.
        var baseDir = AppContext.BaseDirectory;
        foreach (var x in Settings.Default.ModuleAssemblies)
        {
            catalog.Catalogs.Add(new AssemblyCatalog(Path.Combine(baseDir, x!)));
        }

        container = new(catalog, CompositionOptions.DisableSilentRejection);
        var batch = new CompositionBatch();
        batch.AddExportedValue(container);
        container.Compose(batch);

        InitializeCultures(appConfig);
        var presentationServices = container.GetExportedValues<IPresentationService>();
        foreach (var x in presentationServices) x.Initialize();

        moduleControllers = container.GetExportedValues<IModuleController>();
        foreach (var x in moduleControllers) x.Initialize();
        foreach (var x in moduleControllers) x.Run();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        foreach (var x in moduleControllers.Reverse()) x.Shutdown();
        container?.Dispose();
        catalog?.Dispose();
        Log.App.Info("{0} closed", ApplicationInfo.ProductName);
        base.OnExit(e);
    }

    private static void InitializeCultures(AppConfig appConfig)
    {
        try
        {
            if (!string.IsNullOrEmpty(appConfig.Culture)) Thread.CurrentThread.CurrentCulture = CultureInfo.DefaultThreadCurrentCulture = new CultureInfo(appConfig.Culture);
            if (!string.IsNullOrEmpty(appConfig.UICulture)) Thread.CurrentThread.CurrentUICulture = CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo(appConfig.UICulture);
        }
        catch (Exception ex)
        {
            Log.App.Error(ex, "The specified culture code is invalid");
        }
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception ?? throw new InvalidOperationException("Unknown exception object");
        Log.App.Error(ex, "UnhandledException; IsTerminating={0}", e.IsTerminating);

        var message = string.Format(CultureInfo.CurrentCulture, "Unknown application error\n\n{0}", ex);
        if (MainWindow?.IsVisible == true)
        {
            MessageBox.Show(message, ApplicationInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        else
        {
            MessageBox.Show(message, ApplicationInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Error, MessageBoxResult.OK, MessageBoxOptions.ServiceNotification);
        }
    }
}
