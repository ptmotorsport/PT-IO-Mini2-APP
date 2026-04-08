using System.Windows;
using System.Windows.Threading;

namespace IOMini2Tool;

public partial class App : Application
{
	protected override void OnStartup(StartupEventArgs e)
	{
		DispatcherUnhandledException += OnDispatcherUnhandledException;
		TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
		AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

		base.OnStartup(e);
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