using System.Windows;
using IOMini2Tool.ViewModels;

namespace IOMini2Tool;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        
        _viewModel = new MainWindowViewModel(ContentFrame);
        DataContext = _viewModel;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _viewModel.CommunicationService.Dispose();
        base.OnClosing(e);
    }
}
