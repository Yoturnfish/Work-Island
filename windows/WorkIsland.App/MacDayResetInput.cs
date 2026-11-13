using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace WorkIsland.App;

// Mirrors mac-source DayResetTimeInput: fixed colon, two fields, and guarded commits.
public sealed class MacDayResetInput : UserControl
{
    private int minutes;
    private bool loading;
    private int previousHourLength;
    public TextBox HourField { get; }
    public TextBox MinuteField { get; }
    public event EventHandler? MinutesChanged;
    public int Minutes
    {
        get => minutes;
        set
        {
            minutes = Math.Clamp(value, 0, 1439);
            if (!IsKeyboardFocusWithin) LoadValue();
        }
    }

    public MacDayResetInput()
    {
        Width = 96; Height = 24; Focusable = false;
        var grid = new Grid { Margin = new Thickness(7, 0, 7, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        HourField = MakeField("小时", TextAlignment.Right);
        MinuteField = MakeField("分钟", TextAlignment.Left);
        Grid.SetColumn(MinuteField, 2);
        grid.Children.Add(HourField); grid.Children.Add(MinuteField);
        var colon = new TextBlock { Text = ":", FontFamily = new FontFamily("Consolas"), FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(189, 255, 255, 255)),
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
            IsHitTestVisible = false };
        Grid.SetColumn(colon, 1); grid.Children.Add(colon);
        Content = new Border { CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromArgb(41, 255, 255, 255)), Child = grid };
        HourField.TextChanged += FieldChanged; MinuteField.TextChanged += FieldChanged;
        HourField.KeyDown += FieldKeyDown; MinuteField.KeyDown += FieldKeyDown;
        LostKeyboardFocus += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (!IsKeyboardFocusWithin) FinishEditing();
        }));
        LoadValue();
    }

    private static TextBox MakeField(string label, TextAlignment alignment)
    {
        var field = new TextBox { Style = new Style(typeof(TextBox)), Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), Padding = new Thickness(0), Foreground = Brushes.White,
            CaretBrush = Brushes.White, FontFamily = new FontFamily("Consolas"), FontSize = 12,
            VerticalContentAlignment = VerticalAlignment.Center, TextAlignment = alignment,
            FocusVisualStyle = null, AcceptsReturn = false };
        AutomationProperties.SetName(field, label);
        return field;
    }

    private void FieldChanged(object sender, TextChangedEventArgs e)
    {
        if (loading) return;
        var field = (TextBox)sender;
        var sanitized = string.Concat(field.Text.Where(char.IsDigit).Take(2));
        if (field.Text != sanitized)
        {
            loading = true; field.Text = sanitized; field.CaretIndex = sanitized.Length; loading = false;
        }
        if (HourField.Text.Length == 2 && MinuteField.Text.Length == 2 && TryParse(out var value)) SetIfChanged(value);
        if (field == HourField)
        {
            var advance = previousHourLength < 2 && sanitized.Length == 2 && HourField.IsKeyboardFocused;
            previousHourLength = sanitized.Length;
            if (advance) { MinuteField.Focus(); MinuteField.SelectAll(); }
        }
    }

    private void FieldKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (sender == HourField && (HourField.Text.Length != 0 || MinuteField.Text.Length != 0))
        { MinuteField.Focus(); MinuteField.SelectAll(); }
        else { Keyboard.ClearFocus(); FinishEditing(); }
        e.Handled = true;
    }

    private bool TryParse(out int value)
    {
        value = 0;
        if (!int.TryParse(HourField.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var hour)
            || !int.TryParse(MinuteField.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var minute)
            || minute is < 0 or > 59 || hour is < 0 or > 24 || hour == 24 && minute != 0) return false;
        value = hour == 24 ? 0 : hour * 60 + minute;
        return true;
    }

    public void FinishEditing()
    {
        if (HourField.Text.Length == 0 && MinuteField.Text.Length == 0) SetIfChanged(0);
        else if (TryParse(out var value)) SetIfChanged(value);
        LoadValue();
    }

    // A rejected persisted change must also replace the focused draft. The
    // ordinary Minutes setter intentionally leaves active editing untouched.
    internal void RestoreMinutes(int value)
    {
        minutes=Math.Clamp(value,0,1439);LoadValue();
    }

    private void SetIfChanged(int value)
    {
        if (minutes == value) return;
        minutes = value; MinutesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void LoadValue()
    {
        loading = true;
        HourField.Text = minutes == 0 ? "24" : (minutes / 60).ToString("00", CultureInfo.InvariantCulture);
        MinuteField.Text = (minutes % 60).ToString("00", CultureInfo.InvariantCulture);
        previousHourLength = HourField.Text.Length;
        loading = false;
    }
}
