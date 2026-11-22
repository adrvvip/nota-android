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
        // The engine and the app keep their settings in the app's private files folder.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOTA_DATA_DIR")) && FilesDir?.AbsolutePath is { } files)
            Environment.SetEnvironmentVariable("NOTA_DATA_DIR", System.IO.Path.Combine(files, "nota"));
        AndroidAccess.UpdateUserHome(this);
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
    protected override void OnCreate(global::Android.OS.Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        AndroidAccess.AskOnce(this);
    }

    protected override void OnResume()
    {
        base.OnResume();
        AndroidAccess.UpdateUserHome(this);   // "All files access" may have just been granted
    }

}

/// <summary>Storage and microphone access. Nota works on files by path like on the desktop,
/// which on Android 11+ takes "All files access"; without it everything stays in the app's
/// own folder on shared storage (Android/data/app.nota.android/files).</summary>
internal static class AndroidAccess
{
    public static bool HasAllFiles(global::Android.Content.Context ctx)
        => OperatingSystem.IsAndroidVersionAtLeast(30)
            ? global::Android.OS.Environment.IsExternalStorageManager
            : ctx.CheckSelfPermission(global::Android.Manifest.Permission.WriteExternalStorage) == Permission.Granted;

    public static void UpdateUserHome(global::Android.Content.Context ctx)
    {
        string? home = HasAllFiles(ctx)
            ? global::Android.OS.Environment.ExternalStorageDirectory?.AbsolutePath
            : ctx.GetExternalFilesDir(null)?.AbsolutePath;
        home ??= ctx.FilesDir?.AbsolutePath;
        if (home is not null) Environment.SetEnvironmentVariable("NOTA_USER_HOME", home);
    }

    public static void AskOnce(global::Android.App.Activity activity)
    {
        var wanted = new System.Collections.Generic.List<string>();
        if (activity.CheckSelfPermission(global::Android.Manifest.Permission.RecordAudio) != Permission.Granted)
            wanted.Add(global::Android.Manifest.Permission.RecordAudio);
        if (!OperatingSystem.IsAndroidVersionAtLeast(30) && !HasAllFiles(activity))
        {
            wanted.Add(global::Android.Manifest.Permission.ReadExternalStorage);
            wanted.Add(global::Android.Manifest.Permission.WriteExternalStorage);
        }
        if (wanted.Count > 0) activity.RequestPermissions(wanted.ToArray(), 1);

        if (!OperatingSystem.IsAndroidVersionAtLeast(30) || HasAllFiles(activity)) return;
        var prefs = activity.GetSharedPreferences("nota", global::Android.Content.FileCreationMode.Private);
        if (prefs is null || prefs.GetBoolean("asked_all_files", false)) return;
        prefs.Edit()?.PutBoolean("asked_all_files", true)?.Apply();
        new global::Android.App.AlertDialog.Builder(activity)
            .SetTitle("Allow access to your files")!
            .SetMessage("Nota opens and saves projects, samples and exports as files, like on a computer. "
                        + "Allow \"All files access\" so it can use your Music, Documents and Download folders. "
                        + "Without it, Nota keeps everything in its own folder.")!
            .SetPositiveButton("Allow", (_, _) =>
            {
                var intent = new global::Android.Content.Intent(
                    global::Android.Provider.Settings.ActionManageAppAllFilesAccessPermission,
                    global::Android.Net.Uri.Parse("package:" + activity.PackageName));
                try { activity.StartActivity(intent); }
                catch
                {
                    activity.StartActivity(new global::Android.Content.Intent(
                        global::Android.Provider.Settings.ActionManageAllFilesAccessPermission));
                }
            })!
            .SetNegativeButton("Not now", (_, _) => { })!
            .Show();
    }
}
#endif
