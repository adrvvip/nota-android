// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The app's open windows and its main window, the same way on every platform: the desktop
// lifetime's on macOS / Windows / Linux, the single view's WindowHost on Android
// (Android/WindowShim.cs).

using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace Nota.App;

internal static class AppWindows
{
    public static Window? Main
    {
        get
        {
#if ANDROID
            return WindowHost.Current?.Main;
#else
            return (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
#endif
        }
    }

    public static IReadOnlyList<Window> All
    {
        get
        {
#if ANDROID
            return WindowHost.Current?.Windows ?? (IReadOnlyList<Window>)Array.Empty<Window>();
#else
            return (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows
                   ?? (IReadOnlyList<Window>)Array.Empty<Window>();
#endif
        }
    }
}
