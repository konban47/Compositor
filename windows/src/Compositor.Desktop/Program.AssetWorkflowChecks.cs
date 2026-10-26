using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using ImageMagick;
using SkiaSharp;

namespace Compositor.Desktop;

internal static partial class Program
{
    private static int AssetWorkflowChecks(string output)
    {
        BuildHeadless().SetupWithoutStarting();
        try{var window=new MainWindow();window.Show();window.AssetWorkflowSelfCheck(output);Console.WriteLine("PASS: assign profile preview/cancel/undo, shared presets, text controls, layer comps, timeline edits and project roundtrip.");return 0;}
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
public sealed partial class MainWindow
{
    internal void AssetWorkflowSelfCheck(string output)
    {
        void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        void Pump(){Dispatcher.UIThread.RunJobs();UpdateLayout();Dispatcher.UIThread.RunJobs();}
        void Finish(Task task){var end=DateTime.UtcNow.AddSeconds(30);while(!task.IsCompleted&&DateTime.UtcNow<end){Pump();Thread.Sleep(5);}Check(task.IsCompleted,"UI task timed out.");task.GetAwaiter().GetResult();Pump();}
        void Capture(Window window,string suffix){Pump();window.UpdateLayout();using var image=window.CaptureRenderedFrame();image!.Save(Path.ChangeExtension(output,suffix+".png"),new PngBitmapEncoderOptions());}
        T Tagged<T>(Control root,string tag) where T:Control=>root.GetVisualDescendants().OfType<T>().Single(c=>Equals(c.Tag,tag));
        void Click(Control root,string text)=>root.GetVisualDescendants().OfType<Button>().First(b=>b.Content?.ToString()==Localize.Text(text)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var pixels=new SKBitmap(Bitmaps.ColorInfo(150,100));pixels.Erase(new SKColor(145,95,65));var doc=new CanvasDocument(Guid.NewGuid(),500,320){Image=new(){ColorProfile=ColorProfiles.SRGB.ToByteArray()}};
        var layer=new ImageLayer(Guid.NewGuid(),ImportedImage.Create(pixels,"Sample"),new(50,80,150,100),"Sample");doc.Layers.Add(layer);AdoptImported(doc,"Workflow");Reselect(layer.ID);Pump();var asset=layer.Asset;var revision=_history.CurrentRevision;
        var task=AssignColorProfile();Pump();var assign=OwnedWindows.OfType<AssignProfileDialog>().Single();Tagged<RadioButton>(assign,"assign-profile-custom").IsChecked=true;
        var profiles=Tagged<ComboBox>(assign,"assign-profile-choice");profiles.SelectedIndex=profiles.Items.Cast<string>().ToList().FindIndex(n=>n.Contains("Adobe RGB"));Pump();
        Check(assign.Draft.ProfileEncoding is not null&&_canvas.PreviewDocument is not null,"Assignment did not preview.");Check(ReferenceEquals(doc.Layers[0].Asset,asset)&&_history.CurrentRevision==revision,"Profile preview mutated document/history.");
        Tagged<CheckBox>(assign,"assign-profile-preview").IsChecked=false;Check(_canvas.PreviewDocument is null,"Preview checkbox left assigned appearance.");Tagged<CheckBox>(assign,"assign-profile-preview").IsChecked=true;Capture(assign,"assign-profile");Click(assign,"Cancel");Finish(task);Check(doc.Image.ProfileEncoding is null&&_canvas.PreviewDocument is null,"Cancel did not restore assignment.");
        task=AssignColorProfile();Pump();assign=OwnedWindows.OfType<AssignProfileDialog>().Single();Tagged<RadioButton>(assign,"assign-profile-none").IsChecked=true;Click(assign,"OK");Finish(task);Check(doc.Image.ColorProfile is null&&ReferenceEquals(doc.Layers[0].Asset,asset),"Assignment did not update document metadata only.");Undo();Check(_document!.Image.ColorProfile is not null,"Assign profile undo failed.");
        task=PresetManager();Pump();var manager=OwnedWindows.OfType<PresetManagerDialog>().Single();var list=Tagged<ListBox>(manager,"preset-list");Check(list.ItemCount==12,"Default contour set missing.");Capture(manager,"presets");list.SelectedItem=list.Items[0];Click(manager,"Rename…");Pump();var rename=manager.OwnedWindows.OfType<FormDialog>().Single();Tagged<TextBox>(rename,"Name").Text="Custom contour";Click(rename,"OK");Pump();Check(PresetCatalog.Load("Contour")[0].Name=="Custom contour","Rename did not update shared store.");
        list.SelectedItems!.Clear();list.SelectedItems.Add(list.Items[0]);list.SelectedItems.Add(list.Items[1]);Click(manager,"Delete");Check(PresetCatalog.Load("Contour").Count==10,"Multi-delete failed.");
        var type=Tagged<ComboBox>(manager,"preset-type");type.SelectedIndex=0;Pump();var brushes=PresetCatalog.Load("Brush").Count;Check(brushes>=8,"Brush defaults not managed.");list.SelectedItem=list.Items[0];Click(manager,"Delete");Check(PresetCatalog.Load("Brush").Count==brushes-1,"Deleted brush reappeared.");Click(manager,"Done");Finish(task);
        SetTool(Tool.Type);_canvas.TextClicked?.Invoke(new SKPoint(30,25));this.KeyTextInput("Typography 字符");Refresh();OpenTypePanel("Character");Pump();FloatPanel(_dockPanels["Character"]);Pump();Capture(_dockPanels["Character"].Floating!,"character");
        ChangeType(s=>s.Tracking=2);Check(_text!.Style.Tracking==2,"Character control did not reach active text.");CommitText();OpenTypePanel("Paragraph");FloatPanel(_dockPanels["Paragraph"]);Pump();Capture(_dockPanels["Paragraph"].Floating!,"paragraph");
        OpenUtilityPanel("Layer Comps");_document!.LayerComps.Add(LayerComps.Capture(_document,"Layout A"));RefreshUtilityPanels();FloatPanel(_dockPanels["Layer Comps"]);Capture(_dockPanels["Layer Comps"].Floating!,"comps");
        OpenUtilityPanel("Timeline");Pump();Click(_dockPanels["Timeline"].Body,"Create");Pump();Check(_document!.Timeline is {Clips.Length:2},"Timeline creation did not track document layers.");
        var clip=_document.Timeline!.Clips[0];_timelineClip=clip.ID;_tracks!.ChangeClip!(clip with{Start=1,Duration=3});Check(_document.Timeline.Clips[0].Start==1,"Clip drag did not commit.");Undo();Check(_document!.Timeline!.Clips[0].Start==0,"Clip edit undo failed.");_timelineTime=2;_timelineClip=_document.Timeline.Clips[0].ID;AddTimelineKey();Check(_document.Timeline.Clips[0].Keys.Length==1,"Keyframe was not saved.");
        Capture(_dockPanels["Timeline"].Floating!,"timeline");using var copy=ProjectSnapshot.FromDocument(_document);ProjectStore.Validate(copy.Manifest);using var reopen=copy.ToDocument();Check(reopen.Timeline is not null&&reopen.LayerComps.Count==1,"Project lost comp or timeline.");
        _stopDocumentTimeline?.Invoke();
        var rateDialog = new TimelineRateDialog(_document.Timeline!.FrameRate); var rateTask = rateDialog.ShowDialog<bool>(this); Capture(rateDialog,"frame-rate"); Tagged<NumericUpDown>(rateDialog,"timeline-rate").Value=23.976m; Click(rateDialog,"OK"); Finish(rateTask); Check(rateDialog.Rate==23.976,"Frame rate input failed.");
        var panelDialog = new TimelinePanelDialog(_document,_document.Timeline); var panelTask=panelDialog.ShowDialog<bool>(this); Capture(panelDialog,"timeline-options"); Tagged<RadioButton>(panelDialog,"timeline-thumbnail-3").IsChecked=true; Click(panelDialog,"OK"); Finish(panelTask); Check(panelDialog.ThumbnailSize==3,"Thumbnail preference failed.");
        var exportDialog=new TimelineExportDialog(_document,_document.Timeline,"Workflow"); var exportTask=exportDialog.ShowDialog<bool>(this); Capture(exportDialog,"video-render"); Click(exportDialog,"Cancel"); Finish(exportTask);
        OpenUtilityPanel("Notes"); FloatPanel(_dockPanels["Notes"]); Capture(_dockPanels["Notes"].Floating!,"notes");
        _document.Marks.Add(new DocumentMark{Kind=MarkKind.Note,X=10,Y=10,Text="Review"}); RefreshUtilityPanels(); Check(Tagged<TextBox>(_dockPanels["Notes"].Body,"note-content").Text=="Review","Notes did not follow document annotations.");
        Capture(this,"final");
    }
}
