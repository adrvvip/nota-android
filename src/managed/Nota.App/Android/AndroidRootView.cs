// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The Android view's root: keeps Nota clear of the status bar, the navigation bar and the
// camera cutout (safe area), and fits the desktop layout to the screen. Nota is laid out for
// a desktop window of at least ~1280 × 720; a phone in landscape is ~850 dp wide, so the
// whole interface is scaled down until that desktop width fits — the same screens as on the
// PC, smaller. A tablet (≥ 1280 dp) keeps 100 %. Preferences → Interface scale still applies
// on top, as on the desktop.

#if ANDROID
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

internal sealed class AndroidRootView : Border
{
    private const double DesktopWidth = 1100, DesktopHeight = 600;
    private const double MinFit = 0.5;

    private readonly LayoutTransformControl _fit;

    public AndroidRootView(WindowHost host)
    {
        _fit = new LayoutTransformControl { Child = host };
        Child = _fit;
        this.Bind(BackgroundProperty, this.GetResourceObservable("Brush.BgApp"));
        SizeChanged += (_, _) => Refit();
    }

    /// <summary>The scale the desktop layout is drawn at on this screen (1 on a tablet).</summary>
    public static double Fit { get; private set; } = 1;

    private Thickness _safePx;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        top.ScalingChanged += (_, _) => Refit();
        // Back closes the topmost floating window first, as a desktop's Esc / close would.
        top.BackRequested += (_, args) => { if (WindowHost.Current?.HandleBack() == true) args.Handled = true; };
        if (top.InsetsManager is { } insets)
        {
            insets.DisplayEdgeToEdgePreference = true;
            insets.SafeAreaChanged += (_, _) => { _safePx = insets.SafeAreaPadding; Refit(); };
            _safePx = insets.SafeAreaPadding;
        }
        Refit();
    }

    private void Refit()
    {
        // The safe area comes in device pixels; layout is in DIPs, and the scaling is only
        // known once the view has a surface — so it is converted here, every time.
        double k = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        if (k <= 0) k = 1;
        var pad = new Thickness(_safePx.Left / k, _safePx.Top / k, _safePx.Right / k, _safePx.Bottom / k);
        if (pad != Padding) Padding = pad;
        double w = Bounds.Width - Padding.Left - Padding.Right;
        double h = Bounds.Height - Padding.Top - Padding.Bottom;
        if (w <= 0 || h <= 0) return;
        double fit = Math.Clamp(Math.Min(w / DesktopWidth, h / DesktopHeight), MinFit, 1);
        fit = Math.Round(fit, 2);
        if (Math.Abs(fit - Fit) < 0.005 && _fit.LayoutTransform is not null) return;
        Fit = fit;
        Console.WriteLine($"Nota fit: view {Bounds.Width:0}x{Bounds.Height:0} pad {Padding} -> {fit}");
        _fit.LayoutTransform = fit >= 0.995 ? null : new ScaleTransform(fit, fit);
    }
}
#endif
