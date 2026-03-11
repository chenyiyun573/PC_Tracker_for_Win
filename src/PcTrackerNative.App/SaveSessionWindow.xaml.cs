using System.Windows;
using PcTrackerNative.App.Models;

namespace PcTrackerNative.App;

public partial class SaveSessionWindow : Window
{
    private readonly string _defaultDescription;

    public SaveSessionWindow(TrackingMode mode, SessionOutcome outcome, string defaultDescription)
    {
        InitializeComponent();
        _defaultDescription = defaultDescription;
        SummaryTextBlock.Text = $"Save {mode} session ({outcome})";
        DescriptionTextBox.Text = defaultDescription;
        DifficultyComboBox.ItemsSource = Enum.GetValues(typeof(TaskDifficulty));
        DifficultyComboBox.SelectedItem = TaskDifficulty.Medium;
    }

    public SessionSaveInfo BuildSaveInfo(SessionOutcome outcome)
    {
        return new SessionSaveInfo
        {
            Outcome = outcome,
            FinalDescription = string.IsNullOrWhiteSpace(DescriptionTextBox.Text) ? _defaultDescription : DescriptionTextBox.Text.Trim(),
            Difficulty = DifficultyComboBox.SelectedItem is TaskDifficulty difficulty ? difficulty : TaskDifficulty.Medium,
            Notes = NotesTextBox.Text.Trim(),
        };
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void DiscardButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
