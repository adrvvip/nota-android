#!/bin/bash
# SPDX-License-Identifier: AGPL-3.0-only
# Emulator smoke run for the Android build (CI): install (permissions granted up front),
# launch, let it settle, then keep the app's log and screenshots in ci-logs/.
set -x
out=ci-logs
pkg=app.nota.android
adb logcat -c
adb install -r -g Nota-android-x64.apk > $out/5-install.log 2>&1
adb shell appops set $pkg MANAGE_EXTERNAL_STORAGE allow >> $out/5-install.log 2>&1
adb shell monkey -p $pkg -c android.intent.category.LAUNCHER 1 >> $out/5-install.log 2>&1
sleep 20
adb exec-out screencap -p > $out/shot-1.png
sleep 25
adb exec-out screencap -p > $out/shot-2.png
pid=$(adb shell pidof $pkg | tr -d '\r')
echo "pid=$pid" > $out/5-pid.txt
adb logcat -d > $out/6-logcat-all.log 2>&1
if [ -n "$pid" ]; then grep -E "\( *$pid\)| $pid +[0-9]+ " $out/6-logcat-all.log | tail -n 3000 > $out/6-logcat.log; fi
grep -E "AndroidRuntime|FATAL|DEBUG  |MonoDroid|mono-rt|DOTNET" $out/6-logcat-all.log | tail -n 800 > $out/7-crash.log
rm -f $out/6-logcat-all.log
exit 0
