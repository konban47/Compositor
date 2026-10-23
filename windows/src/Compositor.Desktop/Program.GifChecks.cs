using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Compositor.Core.Document;
using Compositor.Core.IO;
using Compositor.Core.Model;
using ImageMagick;
using SkiaSharp;
namespace Compositor.Desktop;
internal static partial class Program
{
    private static int GifChecks(string output)
    {
        BuildHeadless().SetupWithoutStarting();
        try{var window=new MainWindow();window.Show();window.GifSelfCheck(output);Console.WriteLine("PASS: GIF timeline navigation, frame edits and undo, duplicate/delete/reorder/timing, playback isolation, save/reopen/export and automatic tool-rail columns.");return 0;}
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
public sealed partial class MainWindow
{
    internal void GifSelfCheck(string output)
    {
        static void Check(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
        void Pump(){Dispatcher.UIThread.RunJobs();UpdateLayout();Dispatcher.UIThread.RunJobs();}
        void Finish(Task task){var end=DateTime.UtcNow.AddSeconds(30);while(!task.IsCompleted&&DateTime.UtcNow<end){Pump();Thread.Sleep(5);}Check(task.IsCompleted,"Timed out");task.GetAwaiter().GetResult();Pump();}
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);var path=Path.ChangeExtension(output,"gif");
        using(var frames=new MagickImageCollection()){foreach(var color in new[]{MagickColors.Red,MagickColors.Blue,MagickColors.Lime})frames.Add(new MagickImage(color,160,120){AnimationDelay=10,AnimationIterations=0});frames.Write(path,MagickFormat.Gif);}
        Width=1600;Height=1000;Pump();AdoptImported(ImageImporter.NewDocument(ImageImporter.Decode(path)),"New");OpenUtilityPanel("Timeline");Pump();var layer=PropertyLayer!;Check(layer.Gif?.Frames.Count==3,"GIF did not import as one animated layer");
        var revision=_history.CurrentRevision;Finish(NavigateGif(1));Check(PropertyLayer!.Asset!.Image.GetPixel(50,50)==SKColors.Blue&&_history.CurrentRevision==revision,"Navigation changed history or wrong frame");
        SetTool(Tool.Brush);_options.Brush=new(Diameter:12,Green:1,Painting:new());OptionsChanged();Painted([new SKPoint(50.5f,50.5f)]);Check(PropertyLayer!.Gif!.Edited.Count==1,"Brush failed to edit selected frame");Undo();Pump();Check(PropertyLayer!.Gif!.Edited.Count==0,"Undo lost original GIF state");
        var timeline=_utilityPanels["Timeline"];
        void Click(string text){timeline.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Content,Localize.Text(text))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();}
        Click("Duplicate Frame");Check(PropertyLayer!.Gif!.Frames.Count==4,"Duplicate failed");Click("Move Frame Left");Click("Delete Frame");Check(PropertyLayer!.Gif!.Frames.Count==3,"Delete failed");
        timeline.GetVisualDescendants().OfType<NumericUpDown>().Single(n=>Equals(n.Tag,"gif-delay")).Value=.25m;Check(PropertyLayer!.Gif!.Delay(PropertyLayer.GifFrameIndex)==.25,"Delay did not persist");
        revision=_history.CurrentRevision;var asset=PropertyLayer.Asset;Click("Play");Finish(_gifPlaybackFrame!());var wait=DateTime.UtcNow.AddMilliseconds(350);while(DateTime.UtcNow<wait){Pump();Thread.Sleep(5);}Check(_canvas.PreviewDocument is not null&&PropertyLayer.Asset==asset&&_history.CurrentRevision==revision,$"Playback failed: preview={_canvas.PreviewDocument is not null}, asset={PropertyLayer.Asset==asset}, revision={_history.CurrentRevision==revision}, visible={_dockPanels.GetValueOrDefault("Timeline")?.Visible}, status={_message}");Click("Stop");Check(_canvas.PreviewDocument is null,"Stop left playback active");
        Finish(NavigateGif(2)); asset=PropertyLayer!.Asset;revision=_history.CurrentRevision;
        PropertyLayer.Gif=PropertyLayer.Gif! with { TimingMode="Same for all",UniformDelay=10 };Refresh();Pump();
        var play=timeline.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Content,Localize.Text("Play")));
        var stop=timeline.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Content,Localize.Text("Stop")));
        // Restart before allowing the first decode continuation to publish its preview.
        play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var until=DateTime.UtcNow.AddSeconds(10);while(_canvas.PreviewDocument is null&&DateTime.UtcNow<until){Pump();Finish(_gifPlaybackFrame!());Thread.Sleep(5);}
        Check(_canvas.PreviewDocument?.Layers.Single().GifFrameIndex==2,"Restart did not begin at the selected frame");
        Click("Stop");Finish(_gifPlaybackFrame!());Check(_canvas.PreviewDocument is null&&PropertyLayer.Asset==asset&&_history.CurrentRevision==revision,"Restart/stop mutated the edit state or republished a retired preview");
        var package=Path.ChangeExtension(output,"comp");ProjectStore.Save(ProjectSnapshot.FromDocument(_document!),package);using(var saved=ProjectStore.Load(package)){Check(saved.Animations.Count==1,"Save lost GIF");}
        using(var exported=ExportImage.Encode(_document!,new(ExportFormat.Gif,160,120))){Check(new GifSource(exported.Bytes).Delays.Count==3,"Animated export lost frames");}
        Click("Delete Frame");Click("Delete Frame");revision=_history.CurrentRevision;Click("Delete Frame");Check(PropertyLayer!.Gif!.Frames.Count==1&&_history.CurrentRevision==revision,"Last-frame deletion must preserve the animation");Undo();Undo();Pump();
        _rail.SetColumns(false);_rail.SetAvailableHeight(400);Check(_rail.TwoColumns,"Short window did not use two columns");_rail.SetAvailableHeight(2000);Check(!_rail.TwoColumns,"Tall window did not restore one column");
        Pump();using var shot=this.CaptureRenderedFrame();shot!.Save(output,new PngBitmapEncoderOptions());
    }
}
