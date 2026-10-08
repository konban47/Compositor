using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Compositor.Core.Document;
using Compositor.Core.IO;
using SkiaSharp;

namespace Compositor.Desktop;

internal static partial class Program
{
    private static int WindowsChecks(string output)
    {
        BuildHeadless().SetupWithoutStarting();
        var folder = Path.Combine(Path.GetTempPath(), "compositor-windows-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var project = Path.Combine(folder, "中文项目.comp");
            using (var doc = Demo()) ProjectStore.Save(ProjectSnapshot.FromDocument(doc), project);
            var window = new MainWindow();
            window.Show();
            window.WindowsSelfCheck(project, folder);
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame();
            frame?.Save(output, new PngBitmapEncoderOptions());
            Console.WriteLine("PASS: Windows commands, IME composition/commit, Chinese text, localized controls, async save, dirty-close cancellation, AI subject mask, canvas-only mode.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { Directory.Delete(folder, recursive: true); }
    }
}

public sealed partial class MainWindow
{
    internal void WindowsSelfCheck(string path, string folder)
    {
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static void Finish(Task task)
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (!task.IsCompleted && DateTime.UtcNow < deadline) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
            if (!task.IsCompleted) throw new TimeoutException("The UI operation did not complete in 30 seconds.");
            task.GetAwaiter().GetResult();
        }
        Open(path);
        UpdateLayout();
        if (Localize.IsChinese)
        {
            Check(_mainMenu!.Items.OfType<MenuItem>().First().Header!.ToString()!.StartsWith("文件"), "File menu was not translated.");
            Check(Localize.Text("Saved 测试.comp") == "已保存 测试.comp", "Translated status template lost the filename.");
            Check(Localize.Text("_Hue/Saturation…") == "色相／饱和度…", "Photoshop terminology is missing.");
        }
        var dimensions = (_document!.Width, _document.Height);
        RotateCanvas(true); Check(_document.Width == dimensions.Height, "Rotate Canvas did not run.");
        Undo(); Check(_document.Width == dimensions.Width, "Rotate Canvas did not undo.");
        ToggleCanvasOnly(); Check(_canvasOnly && _chrome.All(c => !c.IsVisible), "Canvas only left chrome visible.");
        ToggleCanvasOnly(); Check(!_canvasOnly && _mainMenu!.IsVisible, "Canvas only did not restore chrome.");

        SetTool(Tool.Type);
        _canvas.TextClicked?.Invoke(new SKPoint(20, 30));
        Check(_canvas.TextEditing, "The Type tool did not start text input.");
        var request = new TextInputMethodClientRequestedEventArgs { RoutedEvent = InputElement.TextInputMethodClientRequestedEvent };
        _canvas.RaiseEvent(request);
        Check(request.Client is { SupportsPreedit: true }, "The canvas did not supply a Windows input-method client.");
        request.Client!.SetPreeditText("zhongwen", 4);
        Check(_canvas.PreeditText == "zhongwen" && _text!.Content.Length == 0, "IME preedit was committed prematurely.");
        Check(request.Client.CursorRectangle.Height >= 16, "IME candidate position has no usable height.");
        this.KeyTextInput("中文图层蒙版");
        Dispatcher.UIThread.RunJobs();
        Check(_text!.Content == "中文图层蒙版" && _canvas.PreeditText.Length == 0, "IME commit lost Chinese text.");
        CommitText();
        Check(_document.Layers.Any(l => l.Text?.Style.Content == "中文图层蒙版"), "Committed Chinese text was not in the document.");

        var savePath = Path.Combine(folder, "保存与撤销.comp");
        var save = WriteTab(_open, savePath);
        Change("New Blank Layer", doc => LayerPlacement.AddBlank(doc, null) is not null);
        Finish(save);
        Check(save.Result && _history.IsModified, "An async save incorrectly marked later edits as saved.");
        using (var saved = ProjectStore.Load(savePath).ToDocument())
            Check(saved.Layers.Any(l => l.Text?.Style.Content == "中文图层蒙版"), "Chinese text was lost during async save.");

        Close();
        Dispatcher.UIThread.RunJobs();
        var confirm = OwnedWindows.OfType<ConfirmDialog>().Single();
        var cancel = confirm.GetVisualDescendants().OfType<Button>().Single(b => b.IsCancel);
        cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Check(IsVisible, "Canceling the unsaved-change dialog closed the editor.");

        var paletteTask = FindCommand();
        Dispatcher.UIThread.RunJobs();
        var palette = OwnedWindows.OfType<CommandPalette>().Single();
        var query = palette.GetVisualDescendants().OfType<TextBox>().Single();
        query.Text = Localize.IsChinese ? "顺时针" : "Clockwise";
        Dispatcher.UIThread.RunJobs();
        Check(palette.GetVisualDescendants().OfType<ListBox>().Single().ItemCount >= 1, "Command search cannot find localized commands.");
        palette.Close(); Finish(paletteTask);

        Reselect(_document.Layers.First(l => l.Asset is not null && l.Text is null).ID);
        SetTool(Tool.Object);
        Finish(DetectSubject(removeBackground: true));
        Check(_document.Layers.First(l => l.ID == Selected).Mask is not null, "The AI background action did not create a mask.");
        Undo();
        SetTool(Tool.Move);
        _canvas.Fit();
    }
}
