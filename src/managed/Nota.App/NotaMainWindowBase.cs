// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MainWindow.axaml's root type. On the desktop it is Avalonia's Window; on Android, where
// no OS windows exist, `Window` names the stand-in in Android/WindowShim.cs, so the same
// XAML and code run inside the single Android view.

namespace Nota.App;

public class NotaMainWindowBase : Window
{
}
