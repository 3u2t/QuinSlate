using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Serilog;
using System;

namespace QuinSlate.Ui.Components;

/// <summary>
/// Logs where a mouse press lands on the editor element, in DIPs relative to that element, so it
/// can be compared with the <c>clientX</c>/<c>clientY</c> the page reports for the same press
/// (<c>WebEditor/build/src/clickProbe.js</c>). Diagnostic only — it observes and never handles.
/// </summary>
/// <remarks>
/// The two numbers must match: a DIP and a CSS pixel are the same unit on both sides of the
/// WebView2 boundary once its rasterization scale is applied, and the element's own margins are
/// outside the page. A difference is the WebView2's rendering and its input path disagreeing about
/// where the control is, which the page cannot detect on its own because everything it sees is
/// already shifted. See <c>Docs/Investigations/07-EDITOR-CLICK-CARET-LINE-OFFSET.md</c>.
/// Coordinates, sizes and indices only: nothing here can carry buffer text.
/// </remarks>
public sealed class EditorPointerProbe
{
    /// <summary>
    /// Presses logged per burst. The counter resets on every resize, because a resize is when the
    /// leading hypothesis says the offset appears: a hard per-session cap would be spent on the
    /// first forty ordinary clicks and log nothing for the maximize that triggered the bug.
    /// </summary>
    private const int MaxProbeEntriesPerBurst = 40;

    /// <summary>A resize drag raises a continuous stream; only the settled size is worth a line.</summary>
    private const int GeometrySettleMs = 400;

    private readonly UIElement editor;

    private DispatcherTimer geometryTimer;
    private int pressIndex;
    private int entriesInBurst;

    /// <summary>Starts observing presses on <paramref name="editor"/> for as long as it lives.</summary>
    public EditorPointerProbe(UIElement editor)
    {
        if (editor == null)
        {
            throw new ArgumentNullException(nameof(editor));
        }

        this.editor = editor;

        // handledEventsToo: the WebView2 marks the press handled as it forwards it to Chromium, so
        // a plain event subscription would never see it.
        this.editor.AddHandler(
            UIElement.PointerPressedEvent, new PointerEventHandler(OnPointerPressed), true);

        var element = editor as FrameworkElement;
        if (element != null)
        {
            // Not deferred to the first press: an unconditional line at load is what proves the
            // probe is attached at all. Without it, a capture in which the handler never fires
            // (the WebView2 swallowing pointer input is one of the things under test) would be
            // indistinguishable from one in which the user never clicked.
            element.Loaded += OnEditorLoaded;
            element.SizeChanged += OnEditorSizeChanged;
        }
    }

    private void OnEditorLoaded(object sender, RoutedEventArgs e)
    {
        LogGeometry();
    }

    private void OnEditorSizeChanged(object sender, SizeChangedEventArgs e)
    {
        entriesInBurst = 0;

        if (geometryTimer == null)
        {
            geometryTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(GeometrySettleMs),
            };
            geometryTimer.Tick += OnGeometryTimerTick;
        }

        geometryTimer.Stop();
        geometryTimer.Start();
    }

    private void OnGeometryTimerTick(object sender, object e)
    {
        geometryTimer.Stop();
        LogGeometry();
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (entriesInBurst >= MaxProbeEntriesPerBurst)
        {
            return;
        }

        entriesInBurst++;
        pressIndex++;

        Windows.Foundation.Point position = e.GetCurrentPoint(editor).Position;
        Log.ForContext<EditorPointerProbe>().Information(
            "Click probe (host) #{PressIndex}: x={X:F1} y={Y:F1} element DIPs.",
            pressIndex,
            position.X,
            position.Y);
    }

    private void LogGeometry()
    {
        var element = editor as FrameworkElement;
        if (element == null)
        {
            return;
        }

        double scale = element.XamlRoot != null ? element.XamlRoot.RasterizationScale : 0;
        Log.ForContext<EditorPointerProbe>().Information(
            "Editor geometry (host): editor {Width:F1}x{Height:F1} DIPs at rasterization scale {Scale:F3}.",
            element.ActualWidth,
            element.ActualHeight,
            scale);
    }
}
