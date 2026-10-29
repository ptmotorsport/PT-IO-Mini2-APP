using System.Windows;
using System.Windows.Threading;
using IOMini2Tool.Services;

namespace IOMini2Tool;

public partial class App : Application
{
	protected override async void OnStartup(StartupEventArgs e)
	{
		DispatcherUnhandledException += OnDispatcherUnhandledException;
		TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
		AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

		base.OnStartup(e);

		if (e.Args.Length > 0 && string.Equals(e.Args[0], "--elevated-driver-install", StringComparison.Ordinal))
		{
			var exitCode = 87;
			if (e.Args.Length == 4)
			{
				exitCode = await DriverInstallerService.RunElevatedInstallHelperAsync(e.Args[1], e.Args[2], e.Args[3]);
			}

			Environment.ExitCode = exitCode;
			Environment.Exit(exitCode);
		}

		new MainWindow().Show();
	}

	private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
	{
		MessageBox.Show(
			$"Unexpected error: {e.Exception.Message}\n\n{e.Exception.GetType().Name}\n{e.Exception.StackTrace}",
			"Application Error",
			MessageBoxButton.OK,
			MessageBoxImage.Error);

		e.Handled = true;
	}

	private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
	{
		e.SetObserved();

		MessageBox.Show(
			$"Background task error: {e.Exception.GetBaseException().Message}",
			"Background Error",
			MessageBoxButton.OK,
			MessageBoxImage.Warning);
	}

	private void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
	{
		if (e.ExceptionObject is Exception ex)
		{
			MessageBox.Show(
				$"Fatal error: {ex.Message}\n\n{ex.GetType().Name}\n{ex.StackTrace}",
				"Fatal Error",
				MessageBoxButton.OK,
				MessageBoxImage.Error);
		}
	}
}