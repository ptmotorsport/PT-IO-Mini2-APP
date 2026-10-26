using System.Windows;
using System.Windows.Controls;
using IOMini2Tool.ViewModels;

namespace IOMini2Tool.Views;

public partial class ConfigPage : Page
{
    public ConfigPage()
    {
        InitializeComponent();
    }

    private void OutputFrequency_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: OutputConfigItem item } && DataContext is ConfigViewModel vm)
        {
            vm.OutputFrequencyChangedCommand.Execute(item);
        }
    }

    private void OutputDuty_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: OutputConfigItem item } && DataContext is ConfigViewModel vm)
        {
            vm.OutputDutyChangedCommand.Execute(item);
        }
    }

    private void OutputActiveLevel_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: OutputConfigItem item } && DataContext is ConfigViewModel vm)
        {
            vm.OutputActiveChangedCommand.Execute(item);
        }
    }

    private void HelpButton_Click(object sender, RoutedEventArgs e)
    {
        new HelpWindow().Show();
    }
}
