using System.Windows;
using System.Windows.Controls;
using Sleepers.Services;

namespace Sleepers.Views;

public partial class HistoryView : UserControl
{
    public HistoryView()
    {
        InitializeComponent();
        List.ItemsSource = AppServices.History.Entries;
    }

    void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is HistoryEntry { Success: true } h) UiHelpers.OpenFile(h.OutputPath);
    }

    void Folder_Click(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is HistoryEntry { Success: true } h) UiHelpers.ShowInFolder(h.OutputPath);
    }

    void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Clear the conversion history? Your files are not affected.", "SLEEPERS",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            AppServices.History.Clear();
    }
}
