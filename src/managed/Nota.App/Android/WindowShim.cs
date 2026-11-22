// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Android has one view and no OS windows: Avalonia.Controls.Window cannot be created there.
// Nota's views are written against Window, so on Android this file supplies a Window of
// its own in the Nota.App namespace — C# looks a name up in the enclosing namespace before
// the using directives, so every `Window` in Nota.App (and below) means this one. It is a
// plain control that WindowHost shows full-size (the main window) or as a floating panel
// over it (dialogs, tool windows), with the members of Avalonia's Window that Nota uses.

#if ANDROID
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using AvWindowState = Avalonia.Controls.WindowState;

namespace Nota.App;

/// <summary>Stands in for <see cref="Avalonia.Controls.WindowClosingEventArgs"/>, whose
/// constructor is not public.</summary>
public class WindowClosingEventArgs : CancelEventArgs
{
    public WindowClosingEventArgs(WindowCloseReason reason, bool isProgrammatic)
    {
        CloseReason = reason;
        IsProgrammatic = isProgrammatic;
    }

    public WindowCloseReason CloseReason { get; }
    public bool IsProgrammatic { get; }
}

/// <summary>Window.Screens on Android: the one screen the app's view is on.</summary>
public sealed class WindowScreens
{
    private readonly Window _window;
    internal WindowScreens(Window window) => _window = window;

    public Screen? Primary => TopLevel.GetTopLevel(_window)?.Screens?.Primary;
    public IReadOnlyList<Screen> All => TopLevel.GetTopLevel(_window)?.Screens?.All ?? Array.Empty<Screen>();
    public int ScreenCount => All.Count;
    public Screen? ScreenFromWindow(Window _) => Primary;
    public Screen? ScreenFromPoint(PixelPoint _) => Primary;
    public Screen? ScreenFromVisual(Visual _) => Primary;
}

public class Window : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<Window, string?>(nameof(Title), "Window");
    public static readonly StyledProperty<object?> IconProperty =
        AvaloniaProperty.Register<Window, object?>(nameof(Icon));
    public static readonly StyledProperty<AvWindowState> WindowStateProperty =
        AvaloniaProperty.Register<Window, AvWindowState>(nameof(WindowState));
    public static readonly StyledProperty<WindowStartupLocation> WindowStartupLocationProperty =
        AvaloniaProperty.Register<Window, WindowStartupLocation>(nameof(WindowStartupLocation));
    public static readonly StyledProperty<SizeToContent> SizeToContentProperty =
        AvaloniaProperty.Register<Window, SizeToContent>(nameof(SizeToContent));
    public static readonly StyledProperty<bool> CanResizeProperty =
        AvaloniaProperty.Register<Window, bool>(nameof(CanResize), true);
    public static readonly StyledProperty<bool> TopmostProperty =
        AvaloniaProperty.Register<Window, bool>(nameof(Topmost));
    public static readonly StyledProperty<bool> ShowInTaskbarProperty =
        AvaloniaProperty.Register<Window, bool>(nameof(ShowInTaskbar), true);
    public static readonly StyledProperty<bool> ExtendClientAreaToDecorationsHintProperty =
        AvaloniaProperty.Register<Window, bool>(nameof(ExtendClientAreaToDecorationsHint));
    public static readonly StyledProperty<double> ExtendClientAreaTitleBarHeightHintProperty =
        AvaloniaProperty.Register<Window, double>(nameof(ExtendClientAreaTitleBarHeightHint), -1);
    public static readonly StyledProperty<WindowDecorations> WindowDecorationsProperty =
        AvaloniaProperty.Register<Window, WindowDecorations>(nameof(WindowDecorations), WindowDecorations.Full);
    public static readonly StyledProperty<IReadOnlyList<WindowTransparencyLevel>> TransparencyLevelHintProperty =
        AvaloniaProperty.Register<Window, IReadOnlyList<WindowTransparencyLevel>>(nameof(TransparencyLevelHint),
            Array.Empty<WindowTransparencyLevel>());

    private readonly List<Window> _owned = new();
    private TaskCompletionSource<object?>? _dialog;
    private bool _shown;
    private bool _closing;

    public Window()
    {
        Focusable = true;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        Screens = new WindowScreens(this);
    }

    // No ControlTheme exists for this type: wear ContentControl's, which presents Content.
    protected override Type StyleKeyOverride => typeof(ContentControl);

    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public object? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public AvWindowState WindowState { get => GetValue(WindowStateProperty); set => SetValue(WindowStateProperty, value); }
    public WindowStartupLocation WindowStartupLocation { get => GetValue(WindowStartupLocationProperty); set => SetValue(WindowStartupLocationProperty, value); }
    public SizeToContent SizeToContent { get => GetValue(SizeToContentProperty); set => SetValue(SizeToContentProperty, value); }
    public bool CanResize { get => GetValue(CanResizeProperty); set => SetValue(CanResizeProperty, value); }
    public bool Topmost { get => GetValue(TopmostProperty); set => SetValue(TopmostProperty, value); }
    public bool ShowInTaskbar { get => GetValue(ShowInTaskbarProperty); set => SetValue(ShowInTaskbarProperty, value); }
    public bool ExtendClientAreaToDecorationsHint { get => GetValue(ExtendClientAreaToDecorationsHintProperty); set => SetValue(ExtendClientAreaToDecorationsHintProperty, value); }
    public double ExtendClientAreaTitleBarHeightHint { get => GetValue(ExtendClientAreaTitleBarHeightHintProperty); set => SetValue(ExtendClientAreaTitleBarHeightHintProperty, value); }
    public WindowDecorations WindowDecorations { get => GetValue(WindowDecorationsProperty); set => SetValue(WindowDecorationsProperty, value); }
    public IReadOnlyList<WindowTransparencyLevel> TransparencyLevelHint { get => GetValue(TransparencyLevelHintProperty); set => SetValue(TransparencyLevelHintProperty, value); }

    public PixelPoint Position { get; set; }
    public WindowScreens Screens { get; }
    public Window? Owner { get; private set; }
    public IReadOnlyList<Window> OwnedWindows => _owned;
    public bool IsActive { get; internal set; }
    public object? PlatformImpl => null;

    public Size ClientSize => Bounds.Size;
    public double RenderScaling => TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
    public double DesktopScaling => RenderScaling;
    public IStorageProvider StorageProvider => TopLevel.GetTopLevel(this)!.StorageProvider;
    public IClipboard? Clipboard => TopLevel.GetTopLevel(this)?.Clipboard;
    public ILauncher Launcher => TopLevel.GetTopLevel(this)!.Launcher;
    public IFocusManager? FocusManager => TopLevel.GetTopLevel(this)?.FocusManager;
    public IInsetsManager? InsetsManager => TopLevel.GetTopLevel(this)?.InsetsManager;
    public TopLevel? Host => TopLevel.GetTopLevel(this);
    public bool ShowActivated { get; set; } = true;

    public static readonly RoutedEvent<RoutedEventArgs> WindowOpenedEvent =
        RoutedEvent.Register<Window, RoutedEventArgs>("WindowOpened", RoutingStrategies.Direct);

    /// <summary>Where Avalonia wants a TopLevel, a Nota window gives the view it lives in.</summary>
    public static implicit operator TopLevel?(Window? w) => w is null ? null : TopLevel.GetTopLevel(w);

    public static TopLevel? GetTopLevel(Visual? visual) => TopLevel.GetTopLevel(visual);

    public void RequestAnimationFrame(Action<TimeSpan> action)
    {
        if (TopLevel.GetTopLevel(this) is { } top) top.RequestAnimationFrame(action);
        else Avalonia.Threading.Dispatcher.UIThread.Post(() => action(TimeSpan.FromTicks(Environment.TickCount64 * TimeSpan.TicksPerMillisecond)),
            Avalonia.Threading.DispatcherPriority.Render);
    }

    public event EventHandler? Opened;
    public event EventHandler<WindowClosingEventArgs>? Closing;
    public event EventHandler? Closed;
    public event EventHandler? Activated;
    public event EventHandler? Deactivated;

    protected virtual void OnOpened(EventArgs e) => Opened?.Invoke(this, e);
    protected virtual void OnClosing(WindowClosingEventArgs e) => Closing?.Invoke(this, e);
    protected virtual void OnClosed(EventArgs e) => Closed?.Invoke(this, e);

    public void Show() => ShowCore(null, modal: false);
    public void Show(Window owner) => ShowCore(owner, modal: false);

    public Task ShowDialog(Window owner) => ShowDialog<object?>(owner);

    public async Task<TResult> ShowDialog<TResult>(Window owner)
    {
        _dialog = new TaskCompletionSource<object?>();
        ShowCore(owner, modal: true);
        var r = await _dialog.Task;
        return r is TResult t ? t : default!;
    }

    private void ShowCore(Window? owner, bool modal)
    {
        if (_shown)
        {
            IsVisible = true;
            Activate();
            return;
        }
        Owner = owner;
        owner?._owned.Add(this);
        _shown = true;
        WindowHost.Current?.Present(this, modal);
        RaiseEvent(new RoutedEventArgs(WindowOpenedEvent, this));
        OnOpened(EventArgs.Empty);
        Activate();
    }

    public void Hide()
    {
        WindowHost.Current?.SetFrameVisible(this, false);
    }

    public void Activate()
    {
        WindowHost.Current?.BringToFront(this);
        foreach (var w in WindowHost.Current?.Windows ?? Enumerable.Empty<Window>())
        {
            bool active = ReferenceEquals(w, this);
            if (w.IsActive == active) continue;
            w.IsActive = active;
            if (active) w.Activated?.Invoke(w, EventArgs.Empty);
            else w.Deactivated?.Invoke(w, EventArgs.Empty);
        }
        Focus();
    }

    public void Close() => Close(null);

    public void Close(object? dialogResult)
    {
        if (!_shown || _closing) return;
        var args = new WindowClosingEventArgs(WindowCloseReason.WindowClosing, isProgrammatic: true);
        OnClosing(args);
        if (args.Cancel) return;
        _closing = true;
        foreach (var child in _owned.ToArray()) child.Close();
        Owner?._owned.Remove(this);
        WindowHost.Current?.Remove(this);
        _shown = false;
        _closing = false;
        IsActive = false;
        OnClosed(EventArgs.Empty);
        _dialog?.TrySetResult(dialogResult);
        _dialog = null;
        Owner?.Activate();
    }

    /// <summary>Close as if the user pressed the panel's close button (cancellable).</summary>
    internal void RequestClose() => Close(null);

    public void BeginMoveDrag(PointerPressedEventArgs e) => WindowHost.Current?.BeginMoveDrag(this, e);
    public void BeginResizeDrag(WindowEdge edge, PointerPressedEventArgs e) { }

    /// <summary>The Nota window a visual is in (its nearest Window ancestor, or itself).</summary>
    public static Window? Of(Visual? v)
    {
        for (var cur = v; cur is not null; cur = cur.GetVisualParent())
            if (cur is Window w) return w;
        return WindowHost.Current?.Main;
    }
}

/// <summary>The file pickers (LinuxFileDialogs) called on a Nota window.</summary>
public static class WindowPickers
{
    public static Task<IReadOnlyList<IStorageFile>> PickFilesAsync(this Window w, FilePickerOpenOptions options)
        => TopLevel.GetTopLevel(w)!.PickFilesAsync(options);
    public static Task<IStorageFile?> PickSaveFileAsync(this Window w, FilePickerSaveOptions options)
        => TopLevel.GetTopLevel(w)!.PickSaveFileAsync(options);
    public static Task<IReadOnlyList<IStorageFolder>> PickFoldersAsync(this Window w, FolderPickerOpenOptions options)
        => TopLevel.GetTopLevel(w)!.PickFoldersAsync(options);
}

/// <summary>The single view Android shows: the main window full-size, every other window as
/// a panel floating above it (modal ones over a dim backdrop).</summary>
public sealed class WindowHost : Panel
{
    public static WindowHost? Current { get; private set; }

    private readonly Panel _mainLayer = new();
    private readonly Canvas _floatLayer = new();
    private readonly Dictionary<Window, Control> _frames = new();
    private readonly List<Window> _order = new();

    public WindowHost()
    {
        Current = this;
        Children.Add(_mainLayer);
        Children.Add(_floatLayer);
        _floatLayer.SizeChanged += (_, _) => { foreach (var w in _order) Place(w); };
    }

    public Window? Main { get; private set; }
    public IReadOnlyList<Window> Windows => _order;

    internal void Present(Window w, bool modal)
    {
        if (_frames.ContainsKey(w)) return;
        if (Main is null)
        {
            Main = w;
            _mainLayer.Children.Add(w);
            _frames[w] = w;
            _order.Add(w);
            return;
        }

        var close = new Button
        {
            Content = "✕",
            Padding = new Thickness(16, 0),
            Height = 32,
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
        };
        close.Click += (_, _) => w.RequestClose();
        var title = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0),
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        title[!TextBlock.TextProperty] = w[!Window.TitleProperty];
        // The desktop's raised title band (NotaWindow), with the close button where Windows has it.
        var bar = new DockPanel { Height = 32 };
        bar.Bind(Panel.BackgroundProperty, bar.GetResourceObservable("Brush.SurfaceRaised"));
        title.Classes.Add("Heading");
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.Bind(TextBlock.ForegroundProperty, title.GetResourceObservable("Brush.TextPrimary"));
        close.Bind(Button.ForegroundProperty, close.GetResourceObservable("Brush.TextPrimary"));
        DockPanel.SetDock(close, Dock.Right);
        bar.Children.Add(close);
        bar.Children.Add(title);
        bar.PointerPressed += (_, e) => BeginMoveDrag(w, e);

        var dock = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        dock.Children.Add(bar);
        dock.Children.Add(w);

        var frame = new Border
        {
            Child = dock,
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            BorderThickness = new Thickness(1),

            BoxShadow = BoxShadows.Parse("0 8 32 0 #80000000"),
        };
        frame[!Border.BackgroundProperty] = w[!TemplatedControl.BackgroundProperty];
        frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("Brush.Seam"));

        Control top = frame;
        if (modal)
        {
            var dim = new Panel { Background = new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0)) };
            dim.Children.Add(frame);
            top = dim;
            dim.SizeChanged += (_, _) => Place(w);
        }

        _frames[w] = top;
        _order.Add(w);
        _floatLayer.Children.Add(top);
        Place(w);
    }

    private Border? FrameOf(Window w) => _frames.TryGetValue(w, out var c)
        ? c as Border ?? (c as Panel)?.Children.OfType<Border>().FirstOrDefault()
        : null;

    private void Place(Window w)
    {
        if (ReferenceEquals(w, Main) || !_frames.TryGetValue(w, out var top)) return;
        var area = _floatLayer.Bounds.Size;
        if (area.Width <= 0 || area.Height <= 0) area = Bounds.Size;
        if (top is Panel dim)
        {
            dim.Width = area.Width;
            dim.Height = area.Height;
        }
        var frame = FrameOf(w);
        if (frame is null) return;

        bool fill = w.WindowState is AvWindowState.Maximized or AvWindowState.FullScreen;
        double maxW = Math.Max(200, area.Width - 24), maxH = Math.Max(200, area.Height - 24);
        if (fill)
        {
            frame.Width = area.Width;
            frame.Height = area.Height;
            frame.CornerRadius = default;
            SetPos(top, frame, 0, 0);
            return;
        }

        double width = double.IsNaN(w.Width) || w.SizeToContent is SizeToContent.Width or SizeToContent.WidthAndHeight
            ? double.NaN : Math.Min(w.Width, maxW);
        double height = double.IsNaN(w.Height) || w.SizeToContent is SizeToContent.Height or SizeToContent.WidthAndHeight
            ? double.NaN : Math.Min(w.Height + 32, maxH);
        frame.Width = width;
        frame.Height = height;
        frame.MaxWidth = maxW;
        frame.MaxHeight = maxH;
        frame.Measure(new Size(maxW, maxH));
        var size = frame.DesiredSize;
        double x = Math.Max(12, (area.Width - size.Width) / 2), y = Math.Max(12, (area.Height - size.Height) / 2);
        SetPos(top, frame, x, y);
    }

    private static void SetPos(Control top, Border frame, double x, double y)
    {
        if (top is Panel dim && !ReferenceEquals(top, frame))
        {
            frame.HorizontalAlignment = HorizontalAlignment.Left;
            frame.VerticalAlignment = VerticalAlignment.Top;
            frame.Margin = new Thickness(x, y, 0, 0);
            Canvas.SetLeft(dim, 0);
            Canvas.SetTop(dim, 0);
        }
        else
        {
            Canvas.SetLeft(frame, x);
            Canvas.SetTop(frame, y);
        }
    }

    internal void Remove(Window w)
    {
        if (!_frames.Remove(w, out var top)) return;
        _order.Remove(w);
        if (ReferenceEquals(w, Main)) { _mainLayer.Children.Remove(w); Main = null; return; }
        _floatLayer.Children.Remove(top);
        // Detach the window from its frame, so it can be shown again later.
        if (w.GetVisualParent() is DockPanel dock) dock.Children.Remove(w);
    }

    internal void SetFrameVisible(Window w, bool visible)
    {
        if (_frames.TryGetValue(w, out var top)) top.IsVisible = visible;
    }

    internal void BringToFront(Window w)
    {
        if (ReferenceEquals(w, Main) || !_frames.TryGetValue(w, out var top)) return;
        top.IsVisible = true;
        if (_floatLayer.Children.IndexOf(top) == _floatLayer.Children.Count - 1) return;
        _floatLayer.Children.Remove(top);
        _floatLayer.Children.Add(top);
    }

    private Window? _dragging;
    private Point _dragStart;
    private Point _frameStart;

    internal void BeginMoveDrag(Window w, PointerPressedEventArgs e)
    {
        if (ReferenceEquals(w, Main) || FrameOf(w) is not { } frame) return;
        if (_frames[w] is Panel) return;   // modal: stays centred
        _dragging = w;
        _dragStart = e.GetPosition(this);
        _frameStart = new Point(Canvas.GetLeft(frame), Canvas.GetTop(frame));
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging is null || FrameOf(_dragging) is not { } frame) return;
        var d = e.GetPosition(this) - _dragStart;
        Canvas.SetLeft(frame, Math.Clamp(_frameStart.X + d.X, -frame.Bounds.Width + 80, Bounds.Width - 80));
        Canvas.SetTop(frame, Math.Clamp(_frameStart.Y + d.Y, 0, Bounds.Height - 40));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging is null) return;
        _dragging = null;
        e.Pointer.Capture(null);
    }

    /// <summary>The Back button: closes the topmost floating window; false when only the main
    /// window is left.</summary>
    public bool HandleBack()
    {
        var top = _order.LastOrDefault(w => !ReferenceEquals(w, Main));
        if (top is null) return false;
        top.RequestClose();
        return true;
    }
}
#endif
