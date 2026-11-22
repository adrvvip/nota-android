#!/bin/bash
# SPDX-License-Identifier: AGPL-3.0-only
# Emulator smoke run for the Android build (CI): install, launch, let it settle, then keep
# the app's log and screenshots in ci-logs/.
set -x
out=ci-logs
adb logcat -c
adb install -r Nota-android-x64.apk > $out/5-install.log 2>&1
adb shell monkey -p app.nota.android -c android.intent.category.LAUNCHER 1 >> $out/5-install.log 2>&1
sleep 25
adb exec-out screencap -p > $out/shot-1.png
sleep 30
adb exec-out screencap -p > $out/shot-2.png
adb shell pidof app.nota.android > $out/5-pid.txt 2>&1
adb logcat -d > $out/6-logcat-all.log 2>&1
grep -E "mono|monodroid|Avalonia|DOTNET|nota|Nota|AndroidRuntime|FATAL|DEBUG  |libc " $out/6-logcat-all.log | tail -n 2500 > $out/6-logcat.log
rm -f $out/6-logcat-all.log
exit 0
