using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Compositor.Core.IO;
namespace Compositor.Desktop;
internal static partial class Program
{
    private static int ColorChecks(string output)
    {
        BuildHeadless().SetupWithoutStarting();
        try { var window = new MainWindow(); window.Show(); window.ColorSelfCheck(output); Console.WriteLine("PASS: color settings engines/policies, cancel and commit, gray profile curve, OCIO config/role/depth and validation."); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
public sealed partial class MainWindow
{
    internal void ColorSelfCheck(string output)
    {
        static void Check(bool result, string message) { if (!result) throw new InvalidOperationException(message); }
        void Pump() { Dispatcher.UIThread.RunJobs(); UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        void Wait(Func<bool> ready) { var end = DateTime.UtcNow.AddSeconds(30); while (!ready() && DateTime.UtcNow < end) { Pump(); Thread.Sleep(15); } Check(ready(), "Color UI timed out"); Pump(); }
        T Find<T>(Window window, string tag) where T : Control => window.GetVisualDescendants().OfType<T>().First(c => Equals(c.Tag, tag));
        void Click(Window window, string name) { var button = window.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == Localize.Text(name)); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(); }
        void Shot(Window window, string name) { Pump(); window.UpdateLayout(); using var image = window.CaptureRenderedFrame(); image!.Save(Path.ChangeExtension(output, name + ".png"), new PngBitmapEncoderOptions()); }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        var original = EditorPreferences.Current.Copy(); var pending = ColorSettings(); Pump(); var dialog = OwnedWindows.OfType<ColorSettingsDialog>().Single();
        Find<ComboBox>(dialog, "Engine:").SelectedIndex = (int)ColorEngine.MicrosoftIcm; Pump(); Check(!Find<CheckBox>(dialog, "Use Black Point Compensation").IsEnabled, "ICM claimed unsupported BPC");
        Find<ComboBox>(dialog, "Engine:").SelectedIndex = (int)ColorEngine.AceCompatible; Pump();
        Find<CheckBox>(dialog, "Desaturate Monitor Colors:").IsChecked = true; Check(ColorDefaults.Settings.DesaturateMonitor, "Preview did not apply"); Shot(dialog, "ColorSettings");
        Click(dialog, "Cancel"); Wait(() => pending.IsCompleted); pending.GetAwaiter().GetResult(); Check(ColorDefaults.Settings.DesaturateMonitor == original.ColorSettings.DesaturateMonitor, "Cancel kept preview");
        pending = ColorSettings(); Pump(); dialog = OwnedWindows.OfType<ColorSettingsDialog>().Single(); Find<ComboBox>(dialog, "Intent:").SelectedIndex = (int)ColorIntent.Relative; Click(dialog, "OK"); Wait(() => pending.IsCompleted); pending.GetAwaiter().GetResult(); Check(EditorPreferences.Load().ColorSettings.Intent == ColorIntent.Relative, "Settings were not persisted");
        var gray = new GrayProfileDialog(true); var grayTask = gray.ShowDialog<bool>(this); Pump(); Shot(gray, "DotGain"); Click(gray, "OK"); Wait(() => grayTask.IsCompleted); Check(grayTask.Result && IccProfiles.Valid(gray.ProfileBytes), "Custom dot gain did not generate ICC");
        var ocio = new OpenColorIoDialog(new() { Enabled = true }); var ocioTask = ocio.ShowDialog<bool>(this); Wait(() => ocio.Ready); Shot(ocio, "OpenColorIO");
        Find<ComboBox>(ocio, "Document Depth:").SelectedIndex = 3; Click(ocio, "OK"); Wait(() => ocioTask.IsCompleted); Check(ocioTask.Result && ocio.Draft.Depth == 32, "OCIO did not validate or preserve depth");
        EditorPreferences.Commit(original); ApplyPreferences(); Close();
    }
}
