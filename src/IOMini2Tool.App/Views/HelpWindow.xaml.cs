using System.Windows;
using System.Windows.Controls;

namespace IOMini2Tool.Views;

public partial class HelpWindow : Window
{
    public HelpWindow(string topicId = HelpTopics.GeneralTopicId)
    {
        InitializeComponent();
        TopicList.ItemsSource = HelpTopics.All;
        ShowTopic(topicId);
    }

    public void ShowTopic(string topicId)
    {
        var topic = HelpTopics.Find(topicId);
        TopicList.SelectedItem = topic;
        TitleText.Text = topic.Title;
        BodyText.Text = topic.Body;
    }

    private void TopicList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TopicList.SelectedItem is HelpTopics.Topic topic)
        {
            TitleText.Text = topic.Title;
            BodyText.Text = topic.Body;
        }
    }
}
