using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;

namespace QuinSlate.Ui.Components;

/// <summary>
/// Keeps keyboard focus in the editor when the user clicks the panel's own chrome.
/// </summary>
/// <remarks>
/// A click on chrome with nothing interactive under it — the strips around the WebView2, the empty
/// stretch of the tab strip, the title bar — still moves focus: XAML walks up to the nearest
/// focusable ancestor, which is the TabView template's ScrollViewer, or to nothing at all. Either
/// way the WebView2 is told it lost focus, Chromium blurs the document, and the caret disappears
/// although the user only clicked dead chrome. Those two targets are cancelled here.
/// A move to a real control goes through untouched: the caption buttons, the tab rename box, the
/// context menu and every other focusable element take focus on a click exactly as before, and so
/// does keyboard navigation. Only a <see cref="ScrollViewer"/> inside the chrome element handed to
/// the constructor counts — flyout and menu presenters carry template ScrollViewers of their own,
/// in their own popup trees, and those must keep taking focus normally.
/// </remarks>
public sealed class EditorFocusGuard
{
    private readonly UIElement editor;
    private readonly DependencyObject chrome;

    /// <summary>
    /// Guards keyboard focus on <paramref name="editor"/> for as long as it lives.
    /// <paramref name="chrome"/> is the subtree whose containers are treated as dead chrome —
    /// the panel's tab strip.
    /// </summary>
    public EditorFocusGuard(UIElement editor, DependencyObject chrome)
    {
        if (editor == null)
        {
            throw new ArgumentNullException(nameof(editor));
        }

        if (chrome == null)
        {
            throw new ArgumentNullException(nameof(chrome));
        }

        this.editor = editor;
        this.chrome = chrome;
        this.editor.LosingFocus += OnEditorLosingFocus;
    }

    private void OnEditorLosingFocus(UIElement sender, LosingFocusEventArgs e)
    {
        if (e.InputDevice == FocusInputDeviceKind.Keyboard || IsChromeContainer(e.NewFocusedElement) == false)
        {
            return;
        }

        e.TryCancel();
    }

    private bool IsChromeContainer(DependencyObject element)
    {
        if (element == null)
        {
            return true;
        }

        if (element is ScrollViewer == false)
        {
            return false;
        }

        // The tab strip's own ScrollViewer arrives here detached — VisualTreeHelper.GetParent on it
        // is null, so the subtree test alone would never match it — but a later SDK template may
        // well hand it over parented, so both shapes count. Neither matches a flyout or menu
        // presenter's ScrollViewer: those are parented inside their own popup tree, and keep taking
        // focus normally.
        return VisualTreeHelper.GetParent(element) == null || IsInChrome(element);
    }

    private bool IsInChrome(DependencyObject element)
    {
        for (DependencyObject current = element; current != null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, chrome))
            {
                return true;
            }
        }

        return false;
    }
}
