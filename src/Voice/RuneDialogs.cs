using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Wpf = System.Windows.Controls;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;

namespace Rune.Voice;

public partial class LauncherWindow
{
    internal Window CreateRuneDialog(string title, FrameworkElement body, double width = 540)
    {
        var dialog = new Window { Icon = Icon,
            Owner = this, Title = title, Width = width, SizeToContent = SizeToContent.Height,
            MaxHeight = SystemParameters.WorkArea.Height - 32, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, AllowsTransparency = true,
            Background = Brushes.Transparent, Foreground = (Brush)FindResource("Bone"), Resources = Resources,
            FontFamily = FontFamily, FontSize = 14, ShowInTaskbar = false
        };
        var surface = new Wpf.Border {
            Margin = new Thickness(12), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(95, 80, 51)),
            Background = new LinearGradientBrush(Color.FromRgb(32, 30, 24), Color.FromRgb(18, 18, 15), 90),
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.45 }
        };
        var layout = new Wpf.Grid(); layout.RowDefinitions.Add(new() { Height = GridLength.Auto }); layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var caption = new Wpf.Grid { Background = Brushes.Transparent, Margin = new Thickness(24, 16, 12, 12) };
        caption.ColumnDefinitions.Add(new()); caption.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        caption.Children.Add(new Wpf.TextBlock { Text = title, FontFamily = new FontFamily("Georgia"), FontSize = 23, Foreground = (Brush)FindResource("Bone"), VerticalAlignment = VerticalAlignment.Center });
        var close = new Wpf.Button { Content = "×", Width = 36, Height = 36, Style = (Style)FindResource("CloseChromeButton"), ToolTip = "Close", FontSize = 21 };
        System.Windows.Automation.AutomationProperties.SetName(close, "Close " + title);
        Wpf.Grid.SetColumn(close, 1); caption.Children.Add(close); close.Click += (_, _) => dialog.Close();
        caption.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource is not Wpf.Button && e.LeftButton == MouseButtonState.Pressed) dialog.DragMove(); };
        var scroll = new Wpf.ScrollViewer { Content = body, VerticalScrollBarVisibility = Wpf.ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = Wpf.ScrollBarVisibility.Disabled, MaxHeight = Math.Max(180, SystemParameters.WorkArea.Height - 160), Margin = new Thickness(24, 0, 24, 24) };
        Wpf.Grid.SetRow(scroll, 1); layout.Children.Add(caption); layout.Children.Add(scroll); surface.Child = layout; dialog.Content = surface;
        dialog.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; dialog.Close(); } };
        return dialog;
    }

    private Wpf.Button DialogButton(string text, bool danger = false) => new() { Content = text, Style = (Style)FindResource(danger ? "DangerButton" : "RuneButton"), MinWidth = 100, Margin = new Thickness(8, 0, 0, 0) };
    private Wpf.TextBlock DialogText(string text, bool muted = false) => new() { Text = text, TextWrapping = TextWrapping.Wrap, LineHeight = 20, Foreground = (Brush)FindResource(muted ? "Muted" : "Bone") };

    internal Window CreateVoiceVolumeWindow()
    {
        var panel = new Wpf.StackPanel();
        panel.Children.Add(DialogText("Choose how you hear your companions.", true));
        var card = new Wpf.Border { Background = new SolidColorBrush(Color.FromRgb(39, 37, 30)), CornerRadius = new CornerRadius(7), Padding = new Thickness(16), Margin = new Thickness(0, 20, 0, 20) };
        var speech = new Wpf.StackPanel();
        var toggle = new Wpf.CheckBox { Content = "Spoken replies", IsChecked = runtime.ShellVoiceReplies, FontSize = 15, FontWeight = FontWeights.SemiBold };
        System.Windows.Automation.AutomationProperties.SetName(toggle, "Spoken companion replies");
        speech.Children.Add(toggle);
        var help = DialogText("Turn off to free voice resources. Your microphone commands and written replies stay available.", true); help.Margin = new Thickness(42, 7, 0, 0); speech.Children.Add(help); card.Child = speech; panel.Children.Add(card);
        var labelRow = new Wpf.Grid(); labelRow.ColumnDefinitions.Add(new()); labelRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        labelRow.Children.Add(new Wpf.TextBlock { Text = "Companion volume", VerticalAlignment = VerticalAlignment.Center, FontSize = 14 });
        var value = new Wpf.TextBlock { Text = runtime.ShellVoiceVolume + "%", Foreground = (Brush)FindResource("Gold"), FontSize = 16, FontWeight = FontWeights.SemiBold }; Wpf.Grid.SetColumn(value, 1); labelRow.Children.Add(value); panel.Children.Add(labelRow);
        var slider = new Wpf.Slider { Minimum = 0, Maximum = 100, Value = runtime.ShellVoiceVolume, TickFrequency = 5, SmallChange = 5, LargeChange = 10, IsSnapToTickEnabled = true, IsMoveToPointEnabled = true, Margin = new Thickness(0, 12, 0, 8), Style = (Style)FindResource("DialogVolumeSlider") };
        System.Windows.Automation.AutomationProperties.SetName(slider, "Companion volume percentage"); panel.Children.Add(slider);
        panel.Children.Add(DialogText("Applies to the next reply or voice test. Game volume stays unchanged.", true));
        var footer = new Wpf.Grid { Margin = new Thickness(0, 24, 0, 0) }; footer.ColumnDefinitions.Add(new()); footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var saved = DialogText("Changes save automatically.", true); saved.VerticalAlignment = VerticalAlignment.Center; footer.Children.Add(saved);
        var done = DialogButton("Done"); done.IsDefault = true; Wpf.Grid.SetColumn(done, 1); footer.Children.Add(done); panel.Children.Add(footer);
        var window = CreateRuneDialog("Voice & volume", panel);
        toggle.Checked += (_, _) => runtime.ShellSetVoiceReplies(true); toggle.Unchecked += (_, _) => runtime.ShellSetVoiceReplies(false);
        slider.ValueChanged += (_, _) => { runtime.ShellSetVoiceVolume((int)slider.Value); value.Text = (int)slider.Value + "%"; };
        done.Click += (_, _) => window.Close();
        return window;
    }

    private MessageBoxResult ShowRuneMessage(string message, string title, MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None)
    {
        var panel = new Wpf.StackPanel(); panel.Children.Add(DialogText(message));
        var actions = new Wpf.StackPanel { Orientation = Wpf.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) }; panel.Children.Add(actions);
        var window = CreateRuneDialog(title, panel, 560); MessageBoxResult result = buttons == MessageBoxButton.YesNo ? MessageBoxResult.No : MessageBoxResult.OK;
        if (buttons == MessageBoxButton.YesNo) { var cancel = DialogButton("Cancel"); cancel.IsDefault = true; cancel.Click += (_, _) => window.Close(); actions.Children.Add(cancel); }
        var confirm = DialogButton(buttons == MessageBoxButton.YesNo ? "Continue" : "OK", image == MessageBoxImage.Warning && title.StartsWith("Remove"));
        confirm.IsDefault = buttons != MessageBoxButton.YesNo; confirm.Click += (_, _) => { result = buttons == MessageBoxButton.YesNo ? MessageBoxResult.Yes : MessageBoxResult.OK; window.Close(); }; actions.Children.Add(confirm);
        window.ShowDialog(); return result;
    }

    internal Window CreateProfileNameWindow(string title, string initial, out Wpf.TextBox input)
    {
        var panel = new Wpf.StackPanel(); panel.Children.Add(DialogText("Profile name", true));
        input = new Wpf.TextBox { Text = initial, Margin = new Thickness(0, 8, 0, 0), MinHeight = 38, MaxLength = 48 };
        System.Windows.Automation.AutomationProperties.SetName(input, "Profile name"); panel.Children.Add(input);
        var actions = new Wpf.StackPanel { Orientation = Wpf.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) }; panel.Children.Add(actions);
        var window = CreateRuneDialog(title, panel); var cancel = DialogButton("Cancel"); cancel.Click += (_, _) => window.Close(); actions.Children.Add(cancel);
        var ok = DialogButton("Continue"); ok.IsDefault = true; ok.IsEnabled = input.Text.Trim().Length > 0; actions.Children.Add(ok);
        var field = input; field.TextChanged += (_, _) => ok.IsEnabled = field.Text.Trim().Length > 0;
        ok.Click += (_, _) => window.DialogResult = true;
        window.Loaded += (_, _) => { field.Focus(); field.SelectAll(); };
        return window;
    }
}
