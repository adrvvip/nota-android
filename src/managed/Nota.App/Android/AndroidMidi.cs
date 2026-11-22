// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// USB (and other) MIDI devices on Android, through android.media.midi. Every device the
// system reports is opened; its output ports (what a keyboard plays) are connected to the
// engine (nota_android_midi_receive) and its first input port takes what Nota sends back
// (control-surface feedback). The engine then lists the device in Settings → MIDI like a
// desktop MIDI port (src/audio_io/android/Midi_android.cpp): switching it off, MPE, MIDI
// learn and controller profiles work as on the desktop.

#if ANDROID
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Android.Content;
using Android.Media.Midi;
using Android.OS;

namespace Nota.App;

internal static class AndroidMidi
{
    private const string Lib = "nota_engine";

    [DllImport(Lib)] private static extern void nota_android_midi_device_added(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string uid, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int hasInput, int hasOutput);
    [DllImport(Lib)] private static extern void nota_android_midi_device_removed([MarshalAs(UnmanagedType.LPUTF8Str)] string uid);
    [DllImport(Lib)] private static extern void nota_android_midi_receive([MarshalAs(UnmanagedType.LPUTF8Str)] string uid, IntPtr data, int len);
    [DllImport(Lib)] private static extern void nota_android_midi_set_sender(IntPtr send);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SendFn(IntPtr uid, IntPtr data, int len);

    private sealed class Open
    {
        public required string Uid;
        public required MidiDevice Device;
        public readonly List<MidiOutputPort> Outs = new();
        public MidiInputPort? In;
    }

    private static readonly object Gate = new();
    private static readonly Dictionary<int, Open> ById = new();
    private static readonly Dictionary<string, Open> ByUid = new();
    private static MidiManager? _manager;
    private static Handler? _handler;
    private static SendFn? _send;   // kept alive: native holds its pointer

    public static void Start(Context ctx)
    {
        if (_manager is not null) return;
        try
        {
            if (ctx.GetSystemService(Context.MidiService) is not MidiManager mm) return;
            _manager = mm;
            var thread = new HandlerThread("NotaMidi", -19)   /* THREAD_PRIORITY_URGENT_AUDIO */;
            thread.Start();
            _handler = new Handler(thread.Looper!);
            _send = Send;
            nota_android_midi_set_sender(Marshal.GetFunctionPointerForDelegate(_send));
            mm.RegisterDeviceCallback(new Callback(), _handler);
#pragma warning disable CA1422 // GetDevices: still the way to list every transport
            foreach (var info in mm.GetDevices() ?? Array.Empty<MidiDeviceInfo>()) Add(info);
#pragma warning restore CA1422
        }
        catch (Exception e)
        {
            Console.WriteLine("Nota MIDI: " + e);
        }
    }

    private sealed class Callback : MidiManager.DeviceCallback
    {
        public override void OnDeviceAdded(MidiDeviceInfo? device) { if (device is not null) Add(device); }
        public override void OnDeviceRemoved(MidiDeviceInfo? device) { if (device is not null) Remove(device.Id); }
    }

    private static string NameOf(MidiDeviceInfo info)
    {
        var p = info.Properties;
        string? name = p?.GetString(MidiDeviceInfo.PropertyName);
        if (string.IsNullOrWhiteSpace(name))
        {
            string manufacturer = p?.GetString(MidiDeviceInfo.PropertyManufacturer) ?? "";
            string product = p?.GetString(MidiDeviceInfo.PropertyProduct) ?? "";
            name = $"{manufacturer} {product}".Trim();
        }
        return string.IsNullOrWhiteSpace(name) ? $"MIDI device {info.Id}" : name;
    }

    private static void Add(MidiDeviceInfo info)
    {
        if (_manager is null) return;
        lock (Gate) if (ById.ContainsKey(info.Id)) return;
        // A device's own name is its uid, as on Windows / Linux: Settings → MIDI keeps its
        // on/off choice across reconnects. Two of the same model get #2, #3 ….
        string name = NameOf(info);
        _manager.OpenDevice(info, new Opened(info, name), _handler);
    }

    private sealed class Opened(MidiDeviceInfo info, string name) : Java.Lang.Object, MidiManager.IOnDeviceOpenedListener
    {
        public void OnDeviceOpened(MidiDevice? device)
        {
            if (device is null) return;
            string uid = name;
            lock (Gate)
            {
                for (int n = 2; ByUid.ContainsKey(uid); n++) uid = $"{name} #{n}";
                var open = new Open { Uid = uid, Device = device };
                // A device's *output* ports carry what it plays; its *input* ports take bytes.
                for (int i = 0; i < info.OutputPortCount; i++)
                {
                    var port = device.OpenOutputPort(i);
                    if (port is null) continue;
                    port.Connect(new Receiver(uid));
                    open.Outs.Add(port);
                }
                if (info.InputPortCount > 0) open.In = device.OpenInputPort(0);
                ById[info.Id] = open;
                ByUid[uid] = open;
            }
            nota_android_midi_device_added(uid, uid, info.OutputPortCount > 0 ? 1 : 0, info.InputPortCount > 0 ? 1 : 0);
        }
    }

    private static void Remove(int id)
    {
        Open? open;
        lock (Gate)
        {
            if (!ById.Remove(id, out open)) return;
            ByUid.Remove(open.Uid);
        }
        nota_android_midi_device_removed(open.Uid);
        try
        {
            foreach (var p in open.Outs) p.Close();
            open.In?.Close();
            open.Device.Close();
        }
        catch { /* already gone */ }
    }

    private sealed class Receiver(string uid) : MidiReceiver
    {
        public override void OnSend(byte[]? msg, int offset, int count, long timestamp)
        {
            if (msg is null || count <= 0) return;
            var h = GCHandle.Alloc(msg, GCHandleType.Pinned);
            try { nota_android_midi_receive(uid, h.AddrOfPinnedObject() + offset, count); }
            finally { h.Free(); }
        }
    }

    private static int Send(IntPtr uidPtr, IntPtr data, int len)
    {
        string? uid = Marshal.PtrToStringUTF8(uidPtr);
        MidiInputPort? port;
        lock (Gate) port = uid is not null && ByUid.TryGetValue(uid, out var o) ? o.In : null;
        if (port is null || len <= 0) return 0;
        var bytes = new byte[len];
        Marshal.Copy(data, bytes, 0, len);
        try { port.Send(bytes, 0, len); return 1; }
        catch { return 0; }
    }
}
#endif
