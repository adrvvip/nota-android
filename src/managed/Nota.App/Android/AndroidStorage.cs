// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Android's pickers (Storage Access Framework) answer with content:// URIs; Nota works on
// file paths everywhere, as on the desktop. With "All files access" granted, a document on
// shared storage or an SD card is turned back into its path and handed on as a plain file
// item (TryGetLocalPath works); a file from elsewhere (Drive, a media provider) is copied
// into the app's cache first, so it can still be opened.

#if ANDROID
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Nota.App;

internal static class AndroidStorage
{
    public static async Task<T> LocalizeAsync<T>(TopLevel top, T result)
    {
        object? mapped = result switch
        {
            IReadOnlyList<IStorageFile> files => await MapFiles(top, files),
            IReadOnlyList<IStorageFolder> folders => await MapFolders(top, folders),
            IStorageFile file => await MapFile(top, file, copyIfForeign: false),
            _ => result,
        };
        return mapped is T t ? t : result;
    }

    private static async Task<IReadOnlyList<IStorageFile>> MapFiles(TopLevel top, IReadOnlyList<IStorageFile> files)
    {
        var list = new List<IStorageFile>(files.Count);
        foreach (var f in files) list.Add(await MapFile(top, f, copyIfForeign: true));
        return list;
    }

    private static async Task<IReadOnlyList<IStorageFolder>> MapFolders(TopLevel top, IReadOnlyList<IStorageFolder> folders)
    {
        var list = new List<IStorageFolder>(folders.Count);
        foreach (var f in folders)
        {
            if (f.TryGetLocalPath() is null && PathOf(f.Path) is { } p && Directory.Exists(p)
                && await top.StorageProvider.TryGetFolderFromPathAsync(new Uri("file://" + p)) is { } local)
                list.Add(local);
            else list.Add(f);
        }
        return list;
    }

    private static async Task<IStorageFile> MapFile(TopLevel top, IStorageFile f, bool copyIfForeign)
    {
        if (f.TryGetLocalPath() is not null) return f;
        if (PathOf(f.Path) is { } p && File.Exists(p)
            && await top.StorageProvider.TryGetFileFromPathAsync(new Uri("file://" + p)) is { } local)
            return local;
        if (!copyIfForeign) return f;
        try
        {
            // Not on a storage volume: a copy in the cache keeps it openable by path.
            string dir = Path.Combine(Path.GetTempPath(), "picked", Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(dir);
            string target = Path.Combine(dir, string.IsNullOrWhiteSpace(f.Name) ? "file" : f.Name);
            await using (var src = await f.OpenReadAsync())
            await using (var dst = File.Create(target))
                await src.CopyToAsync(dst);
            return await top.StorageProvider.TryGetFileFromPathAsync(new Uri("file://" + target)) ?? f;
        }
        catch { return f; }
    }

    /// <summary>The file path of an external-storage document or tree URI, if it has one.</summary>
    internal static string? PathOf(Uri uri)
    {
        if (uri.Scheme == "file") return uri.LocalPath;
        if (uri.Scheme != "content") return null;
        string s = Uri.UnescapeDataString(uri.AbsolutePath);   // /document/primary:Music/a.wav, /tree/…/document/…
        int doc = s.LastIndexOf("/document/", StringComparison.Ordinal);
        string? id = doc >= 0 ? s[(doc + "/document/".Length)..]
                   : s.StartsWith("/tree/", StringComparison.Ordinal) ? s["/tree/".Length..] : null;
        if (id is null) return null;
        if (id.StartsWith("raw:", StringComparison.Ordinal)) return id[4..];   // Downloads
        if (uri.Host != "com.android.externalstorage.documents") return null;
        int colon = id.IndexOf(':');
        if (colon < 0) return null;
        string volume = id[..colon], rest = id[(colon + 1)..];
        string root = volume == "primary"
            ? global::Android.OS.Environment.ExternalStorageDirectory?.AbsolutePath ?? "/storage/emulated/0"
            : "/storage/" + volume;
        return rest.Length == 0 ? root : Path.Combine(root, rest);
    }
}
#endif
