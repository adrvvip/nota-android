// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The window a visual is in. On the desktop that is its TopLevel; on Android every Nota
// window lives inside the one view (Android/WindowShim.cs), so it is the nearest Window
// ancestor.

using Avalonia;
using Avalonia.Controls;

namespace Nota.App;

internal static class WindowOf
{
    public static Window? Find(Visual? v)
    {
#if ANDROID
        return Window.Of(v);
#else
        return v is null ? null : TopLevel.GetTopLevel(v) as Window;
#endif
    }
}
