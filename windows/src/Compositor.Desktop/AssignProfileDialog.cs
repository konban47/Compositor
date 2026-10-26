using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Compositor.Core.Format;
using Compositor.Core.IO;

namespace Compositor.Desktop;

internal sealed class AssignProfileDialog : DialogWindow
{
    internal ImageSettings Draft { get; private set; }
    internal AssignProfileDialog(ImageSettings original, ColorManagementSettings settings, Action<ImageSettings?> preview)
    {
        Draft = original; Title = Localize.Text("Assign Profile"); Width = 820; Height = 240; MinWidth = 650; MinHeight = 230;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new Grid { ColumnDefinitions = new("*,135"), Margin = new Thickness(16) };
        var fields = new StackPanel { Spacing = 12, Margin = new Thickness(12) };
        fields.Children.Add(new TextBlock { Text = Localize.Text("Assign Profile") + ":" });
        var none = new RadioButton { GroupName = "assign-profile", Content = Localize.Text("Do Not Color Manage This Document"), Tag = "assign-profile-none" };
        var workingBytes = IccProfiles.Resolve(settings.ProfileName(original.Mode), settings);
        var working = new RadioButton { GroupName = "assign-profile", Content = Localize.Text("Working Space") + ": " + settings.ProfileName(original.Mode), Tag = "assign-profile-working" };
        var custom = new RadioButton { GroupName = "assign-profile", Content = Localize.Text("Profile"), Tag = "assign-profile-custom" };
        var catalog = IccProfiles.Catalog(settings, ProfileAssignment.Model(original)).ToList();
        var current = ProfileAssignment.Current(original);
        if (current is not null && !catalog.Any(p => p.Bytes.AsSpan().SequenceEqual(current))) catalog.Insert(0, new(IccProfiles.Name(current, "Embedded Profile"), ProfileAssignment.Model(original), current));
        var choice = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "assign-profile-choice" };
        void Choices(byte[]? selected) { choice.ItemsSource = catalog.Select(p => p.Name).Append(Localize.Text("Custom ICC Profile…")).ToArray(); choice.SelectedIndex = Math.Max(0, catalog.FindIndex(p => selected is not null && p.Bytes.AsSpan().SequenceEqual(selected))); }
        Choices(current ?? workingBytes);
        var row = new Grid { ColumnDefinitions = new("145,*") }; row.Children.Add(custom); Grid.SetColumn(choice, 1); row.Children.Add(choice);
        fields.Children.Add(none); fields.Children.Add(working); fields.Children.Add(row);
        var error = new TextBlock { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap, Tag = "assign-profile-error" }; fields.Children.Add(error);
        root.Children.Add(new Border { BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1), Child = fields });
        var actions = new StackPanel { Spacing = 12, Margin = new Thickness(16, 8, 0, 0) }; Grid.SetColumn(actions, 1); root.Children.Add(actions);
        var ok = FormDialog.ActionButton("OK", () => Close(true)); var cancel = FormDialog.ActionButton("Cancel", () => Close(false));
        var check = new CheckBox { Content = Localize.Text("Preview"), IsChecked = true, Tag = "assign-profile-preview" };
        actions.Children.Add(ok); actions.Children.Add(cancel); actions.Children.Add(check); Content = root;
        var busy = false;
        void Update()
        {
            if (busy) return;
            choice.IsEnabled = custom.IsChecked == true;
            try
            {
                var target = none.IsChecked == true ? null : working.IsChecked == true ? workingBytes : catalog.ElementAtOrDefault(choice.SelectedIndex)?.Bytes;
                Draft = ProfileAssignment.Assign(original, target, settings); error.Text = ""; ok.IsEnabled = true;
                preview(check.IsChecked == true ? Draft : null);
            }
            catch (Exception e) { error.Text = Localize.Text(e.Message); ok.IsEnabled = false; preview(null); }
        }
        none.IsChecked = current is null; working.IsChecked = current is not null && current.AsSpan().SequenceEqual(workingBytes); custom.IsChecked = none.IsChecked != true && working.IsChecked != true;
        foreach (var radio in new[] { none, working, custom }) radio.IsCheckedChanged += (_, _) => Update();
        check.IsCheckedChanged += (_, _) => Update();
        choice.SelectionChanged += async (_, _) =>
        {
            if (busy) return;
            if (choice.SelectedIndex == catalog.Count)
            {
                busy = true;
                try
                {
                    var files = await StorageProvider.OpenFilePickerAsync(new() { Title = Localize.Text("ICC Profile"), FileTypeFilter = [new("ICC Profile") { Patterns = ["*.icc", "*.icm"] }] });
                    if (files.FirstOrDefault()?.TryGetLocalPath() is { } path)
                    {
                        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("ICC profile is too large.");
                        var bytes = await File.ReadAllBytesAsync(path); _ = ProfileAssignment.Assign(original, bytes, settings);
                        catalog.Add(new(IccProfiles.Name(bytes, Path.GetFileNameWithoutExtension(path)), ProfileAssignment.Model(original), bytes)); Choices(bytes);
                    }
                    else Choices(ProfileAssignment.Current(Draft));
                }
                catch (Exception e) { Choices(ProfileAssignment.Current(Draft)); error.Text = Localize.Text(e.Message); return; }
                finally { busy = false; }
            }
            Update();
        };
        Opened += (_, _) => Update(); Closed += (_, _) => preview(null);
        KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Escape) Close(false); };
    }
}
