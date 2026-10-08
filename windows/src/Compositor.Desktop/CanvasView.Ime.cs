using System.Globalization;
using Avalonia;
using Avalonia.Input.TextInput;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media;

namespace Compositor.Desktop;

public sealed partial class CanvasView
{
    private CanvasInputMethod? _inputMethod;
    private string _preedit = "";
    private int _preeditCursor;
    internal string PreeditText => _preedit;

    private void InitializeInputMethod()
    {
        _inputMethod = new CanvasInputMethod(this);
        TextInputMethodClientRequested += (_, args) =>
        {
            if (!TextEditing) return;
            args.Client = _inputMethod;
            args.Handled = true;
        };
    }

    private void RequeryInputMethod()
    {
        _preedit = "";
        RaiseEvent(new TextInputMethodClientRequeryRequestedEventArgs { RoutedEvent = InputMethod.TextInputMethodClientRequeryRequestedEvent });
        _inputMethod?.CaretChanged();
    }

    private Rect InputCaret
    {
        get
        {
            if (TextCaret is not { } caret) return new Rect(0, 0, 1, 20);
            var top = ToScreen(new SkiaSharp.SKPoint(caret.Left, caret.Top));
            var bottom = ToScreen(new SkiaSharp.SKPoint(caret.Right, caret.Bottom));
            return new Rect(top.X, top.Y, Math.Max(1, bottom.X - top.X), Math.Max(16, bottom.Y - top.Y));
        }
    }

    private void DrawPreedit(DrawingContext context)
    {
        if (!TextEditing || _preedit.Length == 0) return;
        var caret = InputCaret;
        var font = new Typeface(Localize.UiFont);
        var size = Math.Clamp(caret.Height, 16, 48);
        var text = new FormattedText(_preedit, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, font, size, Brushes.White);
        var point = new Point(caret.X, caret.Y);
        context.DrawRectangle(Brushes.Black, null, new Rect(point, new Size(text.Width + 4, text.Height + 2)));
        context.DrawText(text, point);
        context.DrawLine(new Pen(Brushes.White), new Point(point.X, point.Y + text.Height), new Point(point.X + text.Width, point.Y + text.Height));
        var prefix = new FormattedText(_preedit[..Math.Clamp(_preeditCursor, 0, _preedit.Length)], CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, font, size, Brushes.White);
        context.DrawLine(new Pen(Brushes.White), new Point(point.X + prefix.Width, point.Y), new Point(point.X + prefix.Width, point.Y + text.Height));
    }

    private sealed class CanvasInputMethod(CanvasView owner) : TextInputMethodClient
    {
        public override Visual TextViewVisual => owner;
        public override bool SupportsPreedit => true;
        // The text session currently handles its own caret and undo. Do not advertise an unsupported
        // surrounding-text replacement API to Windows TSF; committed input arrives through TextInput.
        public override bool SupportsSurroundingText => false;
        public override string SurroundingText => "";
        public override Rect CursorRectangle => owner.InputCaret;
        public override TextSelection Selection { get; set; }
        public override void SetPreeditText(string? text) => SetPreeditText(text, text?.Length);
        public override void SetPreeditText(string? text, int? cursorPos)
        {
            owner._preedit = text ?? "";
            owner._preeditCursor = Math.Clamp(cursorPos ?? owner._preedit.Length, 0, owner._preedit.Length);
            owner.InvalidateVisual();
            RaiseCursorRectangleChanged();
        }
        public void CaretChanged() => RaiseCursorRectangleChanged();
    }
}
