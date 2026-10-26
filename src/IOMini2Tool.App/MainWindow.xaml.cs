using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using IOMini2Tool.ViewModels;
using IOMini2Tool.Views;

namespace IOMini2Tool;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private HelpWindow? _helpWindow;

    public MainWindow()
    {
        InitializeComponent();
        
        _viewModel = new MainWindowViewModel(ContentFrame);
        DataContext = _viewModel;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F1) return;
        e.Handled = true;

        string topicId = HelpTopics.GeneralTopicId;
        var element = Mouse.DirectlyOver as DependencyObject;
        while (element != null)
        {
            var topic = HelpTopics.GetTopic(element);
            if (!string.IsNullOrEmpty(topic))
            {
                topicId = topic;
                break;
            }
            element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }

        if (_helpWindow == null || !_helpWindow.IsLoaded)
        {
            _helpWindow = new HelpWindow(topicId) { Owner = this };
            _helpWindow.Show();
        }
        else
        {
            _helpWindow.ShowTopic(topicId);
            _helpWindow.Activate();
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _viewModel.CommunicationService.Dispose();
        base.OnClosing(e);
    }
}
