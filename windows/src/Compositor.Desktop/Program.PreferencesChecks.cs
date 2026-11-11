using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;
internal static partial class Program
{
    private static int PreferencesChecks(string output)
    {
        BuildHeadless().SetupWithoutStarting();
        try { var window = new MainWindow(); window.Show(); window.PreferencesSelfCheck(output); Console.WriteLine("PASS: preference categories/search, every Photoshop page, Camera Raw preferences and workflow, advanced GPU draft/cancel, save/reload, recovery, layer commands, adjustment editing and cursor cache."); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
public sealed partial class MainWindow
{
    internal void PreferencesSelfCheck(string output)
    {
        void Check(bool yes,string message) { if (!yes) throw new InvalidOperationException(message); }
        void Pump() { Dispatcher.UIThread.RunJobs(); UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        void Finish(Task task) { for(var i=0;i<600 && !task.IsCompleted;i++){Pump();Thread.Sleep(10);} Check(task.IsCompleted,"Operation timed out."); task.GetAwaiter().GetResult(); Pump(); }
        void Click(Control owner,string text) { owner.GetVisualDescendants().OfType<Button>().Single(b=>b.Content?.ToString()==Localize.Text(text)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(); }
        void Capture(Window window,string suffix) { Pump(); using var frame=window.CaptureRenderedFrame(); frame!.Save(Path.ChangeExtension(output,suffix+".png"),new PngBitmapEncoderOptions()); }
        var pixels=Bitmaps.Allocate(Bitmaps.ColorInfo(160,100));pixels.Erase(SKColors.Coral);
        var doc=ImageImporter.NewDocument(ImportedImage.Create(pixels,"Layer"));AdoptImported(doc,"Import Image");Pump();
        var original=EditorPreferences.Current.Copy();
        var task=EditPreferences("Performance");Pump();var dialog=OwnedWindows.OfType<PreferencesDialog>().Single();
        Check(dialog.Category=="Performance","Requested category did not open.");
        Capture(dialog,"performance");
        Click(dialog,"Advanced Settings…");var advanced=dialog.OwnedWindows.OfType<FormDialog>().Single();
        advanced.GetVisualDescendants().OfType<CheckBox>().Single(c=>Equals(c.Tag,"GPU Compositing")).IsChecked=false;
        Capture(advanced,"gpu-advanced");Click(advanced,"OK");Check(!dialog.Draft.AcceleratedCanvas && EditorPreferences.Current.AcceleratedCanvas,"GPU draft leaked.");
        Click(dialog,"Cancel");Finish(task);Check(EditorPreferences.Current.AcceleratedCanvas,"Cancel committed GPU preference.");
        task=EditPreferences("Cursors");Pump();dialog=OwnedWindows.OfType<PreferencesDialog>().Single();
        dialog.GetVisualDescendants().OfType<RadioButton>().First(c=>Equals(c.Tag,"Precise")).IsChecked=true;
        var nav=dialog.GetVisualDescendants().OfType<ListBox>().Single();
        for(var i=0;i<PreferencesDialog.Categories.Length;i++){nav.SelectedIndex=i;Pump();Check(dialog.Category==PreferencesDialog.Categories[i],"Category navigation failed.");}
        var search=dialog.GetVisualDescendants().OfType<TextBox>().Single(t=>Equals(t.Tag,"Search Preferences"));search.Text="GPU";Pump();
        Check(dialog.GetVisualDescendants().OfType<CheckBox>().Any(c=>Equals(c.Tag,"Use Graphics Processor") && c.IsEffectivelyVisible),"Preference search did not include matching description.");
        search.Text="";nav.SelectedIndex=11;Capture(dialog,"cursors");Click(dialog,"OK");Finish(task);
        Check(_canvas.PreciseBrushCursor && EditorPreferences.Load().PreciseCursor,"Preference was not applied and persisted.");
        // Every page as Photoshop lays it out, then Camera Raw Preferences and its Workflow options on a developed picture.
        task=EditPreferences("General");Pump();dialog=OwnedWindows.OfType<PreferencesDialog>().Single();nav=dialog.GetVisualDescendants().OfType<ListBox>().Single();
        for(var i=0;i<PreferencesDialog.Categories.Length;i++){nav.SelectedIndex=i;Pump();Capture(dialog,$"page-{i:00}");}
        Check(dialog.GetVisualDescendants().OfType<CheckBox>().Any(c=>Equals(c.Tag,"Beep When Done")) && dialog.GetVisualDescendants().OfType<ComboBox>().Any(c=>Equals(c.Tag,"Full Screen Border")),"Photoshop preference rows are missing.");
        Check(dialog.GetVisualDescendants().OfType<CheckBox>().Where(c=>Equals(c.Tag,"Enable Generator")).All(c=>!c.IsEnabled && ToolTip.GetTip(c) is not null),"An unsupported preference is not explained.");
        Click(dialog,"Cancel");Finish(task);
        var rawTask=CameraRawPreferences();Pump();var rawPrefs=OwnedWindows.OfType<CameraRawPreferencesDialog>().Single();var rawNav=rawPrefs.GetVisualDescendants().OfType<ListBox>().Single();
        for(var i=0;i<CameraRawPreferencesDialog.Categories.Length;i++){rawNav.SelectedIndex=i;Pump();Check(rawPrefs.Category==CameraRawPreferencesDialog.Categories[i],"Camera Raw category navigation failed.");Capture(rawPrefs,$"raw-{i}");}
        rawNav.SelectedIndex=4;Pump();
        rawPrefs.GetVisualDescendants().OfType<CheckBox>().Single(c=>Equals(c.Tag,"Resize to Fit")).IsChecked=true;Pump();
        rawPrefs.GetVisualDescendants().OfType<ComboBox>().Single(c=>Equals(c.Tag,"Resize to Fit Mode")).SelectedIndex=2;Pump();
        rawPrefs.GetVisualDescendants().OfType<NumericUpDown>().Single(c=>Equals(c.Tag,"Workflow Width")).Value=80;Pump();
        rawPrefs.GetVisualDescendants().OfType<CheckBox>().Single(c=>Equals(c.Tag,"Open in Compositor as Smart Objects")).IsChecked=true;
        Capture(rawPrefs,"raw-workflow");Click(rawPrefs,"OK");Finish(rawTask);
        var workflow=EditorPreferences.Load().CameraRaw.Workflow;Check(workflow.Resize && workflow.FitMode==2 && workflow.Width==80 && workflow.SmartObject,"Camera Raw workflow was not kept.");
        var developed=RawDevelopDialog.Finish(ImportedImage.Create(Bitmaps.Allocate(Bitmaps.ColorInfo(400,200)),"Raw"),workflow);
        Check(developed.Width==80 && developed.Height==40,"Workflow did not resize the developed picture.");developed.Dispose();
        Check(RulerUnits.Pixels(6,72,200)==2 && Math.Abs(RulerUnits.Pixels(4,254,0)-10)<1e-9,"Ruler units are wrong.");
        var checker = Skin.Checker;
        task=EditPreferences("Guides, Grid and Slices");Pump();dialog=OwnedWindows.OfType<PreferencesDialog>().Single();
        dialog.GetVisualDescendants().OfType<ComboBox>().Single(c=>Equals(c.Tag,"Guide Style")).SelectedIndex=1;
        dialog.GetVisualDescendants().OfType<ComboBox>().Single(c=>Equals(c.Tag,"Grid Size")).SelectedIndex=3;Pump();
        var badFont=dialog.GetVisualDescendants().OfType<TextBox>().Single(c=>Equals(c.Tag,"Default Font"));badFont.Text="";Pump();
        Click(dialog,"OK");Check(!task.IsCompleted && dialog.Category=="Type","Invalid hidden field was accepted or not revealed.");
        badFont.Text="Arial";Pump();Click(dialog,"OK");Finish(task);
        Check(_canvas.GuideLineStyle==1 && EditorPreferences.Load().GuideStyle==1 && EditorPreferences.Load().CheckerSize==20,"Guide style did not apply and persist.");
        Check(ReferenceEquals(checker,Skin.Checker) && checker is Avalonia.Media.DrawingBrush { DestinationRect.Rect.Width:40 },"Existing checker references were not updated.");
        var invalid=original.Copy();invalid.CheckerLight="bad";invalid.GpuCacheMB=int.MaxValue;invalid.BrushSmoothing=double.NaN;invalid.Normalize();
        Check(invalid.CheckerLight=="#454545" && invalid.GpuCacheMB==2048 && invalid.BrushSmoothing==0,"Invalid settings were not normalized.");
        var layerId=doc.Layers[0].ID;Reselect(layerId);task=ExtendedImageAdjustment(ImageAdjustmentKind.BrightnessContrast,adjustmentLayer:true);Pump();
        var form=OwnedWindows.OfType<ImageAdjustmentDialog>().Single();form.GetVisualDescendants().OfType<ValueBox>().Single(n=>Equals(n.Tag,"Brightness value")).Value=37;Click(form,"OK");Finish(task);
        Check(doc.Layers.Last().Adjustment?.Extended?.Get("Brightness")==37,"Adjustment layer was not created.");
        task=EditAdjustment();Pump();form=OwnedWindows.OfType<ImageAdjustmentDialog>().Single();Check(form.GetVisualDescendants().OfType<ValueBox>().Single(n=>Equals(n.Tag,"Brightness value")).Value==37,"Reopening reset adjustment parameters.");Click(form,"Cancel");Finish(task);Undo();Pump();
        doc=_document!;Reselect(layerId);SetTool(Tool.Pen);Pump();
        var screen=_canvas.TranslatePoint(new Point(50,50),this)!.Value;this.MouseMove(screen);Pump();var builds=_canvas.SceneBuildCount;
        this.MouseMove(screen+new Vector(5,5));Pump();Check(_canvas.SceneBuildCount==builds,"Pointer-only movement rebuilt the display scene.");Check(_canvas.PenCursorEnabled,"Pen cursor not selected.");
        var menu=_mainMenu!.Items.OfType<MenuItem>().Single(m=>m.Tag as string=="Layer");menu.IsSubMenuOpen=true;Pump();Capture(this,"layer-menu");menu.IsSubMenuOpen=false;
        Check(menu.Items.OfType<MenuItem>().Any(m=>m.Tag as string=="Vector Mask"),"Vector mask menu missing.");
        VectorMaskCommand("Reveal");Check(doc.Layers[0].Mask?.VectorPath is not null,"Vector mask command failed.");Undo();Pump();doc=_document!;
        Edit("Recovery Check",()=>{doc.Layers[0].Name="Recoverable";return true;});Finish(SaveRecoveryCopies());
        var recovery=RecoveryPath(_open, doc.ID);Check(Directory.Exists(recovery),"Recovery copy was not written.");
        using(var recovered=ProjectStore.Load(recovery)) Check(recovered.Manifest.Layers[0].Name=="Recoverable","Recovery copy has stale contents.");
        Check(_history.IsModified,"Recovery marked the original as saved.");
        var pendingRecovery=new TaskCompletionSource<bool>();_open.Recovering=pendingRecovery.Task;
        _open.Path=Path.Combine(AppPaths.SettingsDirectory,"manual-during-recovery.comp");
        Finish(SaveTab(_open,false));Check(Directory.Exists(_open.Path) && !_history.IsModified,"Recovery swallowed a manual save.");
        pendingRecovery.SetResult(true);_open.Recovering=null;
        EditorPreferences.Commit(original);ApplyPreferences();Capture(this,"final");
    }
}
