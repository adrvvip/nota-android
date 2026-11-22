// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Android entry points (Avalonia 12): the Application class builds the Avalonia app the way
// Program.BuildAvaloniaApp does on the desktop; the activity hosts its single view.

#if ANDROID
using System;
using global::Android.Content.PM;
using global::Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace Nota.App.Droid;

[global::Android.App.Application(Label = "Nota", Icon = "@drawable/icon")]
public sealed class AndroidApp : AvaloniaAndroidApplication<App>
{
    private AndroidApp(IntPtr javaReference, JniHandleOwnership transfer) : base(javaReference, transfer) { }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        => base.CustomizeAppBuilder(builder)
            .WithNotaFonts()
            .LogToTrace()
            .WithImeLogNoiseFiltered();

    public override void OnCreate()
    {
        // The engine and the app keep their data in the app's private files folder.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOTA_DATA_DIR")) && FilesDir?.AbsolutePath is { } files)
            Environment.SetEnvironmentVariable("NOTA_DATA_DIR", System.IO.Path.Combine(files, "nota"));
        base.OnCreate();
    }
}

[global::Android.App.Activity(
    Label = "Nota",
    Theme = "@style/NotaTheme",
    Icon = "@drawable/icon",
    MainLauncher = true,
    Exported = true,
    LaunchMode = LaunchMode.SingleTask,
    ScreenOrientation = ScreenOrientation.SensorLandscape,
    WindowSoftInputMode = global::Android.Views.SoftInput.AdjustResize,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode
                           | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density
                           | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.Navigation)]
public sealed class MainActivity : AvaloniaMainActivity
{
    [Obsolete]
    public override void OnBackPressed()
    {
        // Back closes the topmost floating window first; with only the main window left it
        // leaves the app as usual.
        if (WindowHost.Current?.HandleBack() == true) return;
        base.OnBackPressed();
    }
}
#endif
