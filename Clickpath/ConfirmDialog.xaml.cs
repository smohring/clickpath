using System.Windows;
using System.Windows.Media;

namespace Clickpath;

/// <summary>The app's own yes/no question, in the look of the main window and centred over it (instead of the system message box).</summary>
public partial class ConfirmDialog : Window
{
    ConfirmDialog(string message, string confirmLabel, string cancelLabel, bool danger)
    {
        InitializeComponent();
        Message.Text = message;
        ConfirmButton.Content = confirmLabel;
        CancelButton.Content = cancelLabel;

        var accent = Application.Current.TryFindResource("AccentTextFillColorPrimaryBrush") as Brush ?? Brushes.DodgerBlue;
        Symbol.Text = danger ? "" : "";
        Symbol.Foreground = danger ? new SolidColorBrush(Color.FromRgb(0xE8, 0x5C, 0x4A)) : accent;
        ConfirmButton.Style = (Style)FindResource(danger ? "DangerButton" : "AccentButtonStyle");

        // a destructive question defaults to "no", so Enter by accident deletes nothing
        (danger ? CancelButton : ConfirmButton).IsDefault = true;
        Loaded += (_, _) => (danger ? CancelButton : ConfirmButton).Focus();
    }

    /// <summary>Shows the question over <paramref name="owner"/> and returns whether the user confirmed.</summary>
    public static bool Ask(Window? owner, string message, string confirmLabel, string cancelLabel, bool danger = false)
    {
        var dialog = new ConfirmDialog(message, confirmLabel, cancelLabel, danger);
        if (owner is { IsVisible: true }) dialog.Owner = owner;
        else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return dialog.ShowDialog() == true;
    }

    void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;

    void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
