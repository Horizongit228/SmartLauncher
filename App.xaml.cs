using System.Configuration;
using System.Data;
using System;
using SmartLauncher.UI.Services;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace SmartLauncher.UI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private const string InstanceMutexName =
        @"Local\SmartLauncher.0C2DA260-87A7-49A8-8BD4-F3F79718CB57";

    private const string ActivationEventName =
        @"Local\SmartLauncher.Activate.0C2DA260-87A7-49A8-8BD4-F3F79718CB57";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _activationEvent;
    private RegisteredWaitHandle? _activationRegistration;
    private bool _ownsInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _instanceMutex =
            new Mutex(
                initiallyOwned: true,
                InstanceMutexName,
                out bool isFirstInstance);

        _ownsInstanceMutex = isFirstInstance;
        _activationEvent =
            new EventWaitHandle(
                initialState: false,
                EventResetMode.AutoReset,
                ActivationEventName);

        if (!isFirstInstance)
        {
            _activationEvent.Set();
            Shutdown();
            return;
        }

        AppLogService.Initialize();
        DispatcherUnhandledException +=
            App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException +=
            CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException +=
            TaskScheduler_UnobservedTaskException;

        base.OnStartup(e);

        var window = new MainWindow();
        MainWindow = window;

        _activationRegistration =
            ThreadPool.RegisterWaitForSingleObject(
                _activationEvent,
                (_, timedOut) =>
                {
                    if (timedOut)
                    {
                        return;
                    }

                    Dispatcher.BeginInvoke(
                        DispatcherPriority.Send,
                        new Action(
                            window.RestoreAndActivate));
                },
                state: null,
                Timeout.Infinite,
                executeOnlyOnce: false);

        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLogService.Info(
            $"Smart Launcher exited with code {e.ApplicationExitCode}.");

        _activationRegistration?.Unregister(null);
        _activationRegistration = null;

        _activationEvent?.Dispose();
        _activationEvent = null;

        if (_ownsInstanceMutex)
        {
            try
            {
                _instanceMutex?.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The mutex was already released during shutdown.
            }
        }

        _instanceMutex?.Dispose();
        _instanceMutex = null;

        base.OnExit(e);
    }

    private void App_DispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        AppLogService.Error(
            "Unhandled UI exception.",
            e.Exception);

        System.Windows.MessageBox.Show(
            "Smart Launcher столкнулся с ошибкой и сохранил подробности в журнале:\n"
            + AppLogService.CurrentLogPath
            + "\n\n"
            + e.Exception.Message,
            "Ошибка Smart Launcher",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
        Shutdown(-1);
    }

    private static void CurrentDomain_UnhandledException(
        object sender,
        UnhandledExceptionEventArgs e)
    {
        AppLogService.Error(
            "Unhandled application exception.",
            e.ExceptionObject as Exception);
    }

    private static void TaskScheduler_UnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs e)
    {
        AppLogService.Error(
            "Unobserved task exception.",
            e.Exception);
        e.SetObserved();
    }
}

