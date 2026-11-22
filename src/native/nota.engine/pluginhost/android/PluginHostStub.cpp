// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Android build of the pluginhost module: there are no VST3 / AU plug-ins to host on
// Android, so JUCE is not compiled at all. This file supplies the bridge factories
// (PluginHostBridge.h) and the nota_pluginhost_* C ABI with "no plug-ins" answers, so
// the core engine and the app run unchanged with only the built-in devices.

#include "nota/nota_engine.h"

#include "bridge/PluginHostBridge.h"

#include <cstdint>
#include <memory>

namespace nota {

std::shared_ptr<Instrument> createPluginInstrument(int32_t, double, int32_t) { return nullptr; }
std::shared_ptr<Device> createPluginEffect(int32_t, double, int32_t) { return nullptr; }
void setPluginAudioWorkgroup(void*) {}

} // namespace nota

extern "C" NOTA_API NotaResult nota_pluginhost_open_test_window(void) { return NOTA_ERR_UNKNOWN; }
extern "C" NOTA_API int32_t nota_pluginhost_pump(void) { return 0; }

extern "C" NOTA_API int32_t nota_pluginhost_scan(const char*) { return 0; }
extern "C" NOTA_API int32_t nota_pluginhost_scan_ex(const char*, const char*, int32_t) { return 0; }
extern "C" NOTA_API int32_t nota_pluginhost_scan_progress(int32_t* done, int32_t* total) {
    if (done) *done = 0;
    if (total) *total = 0;
    return 0;
}
extern "C" NOTA_API const char* nota_pluginhost_scan_current(void) { return ""; }
extern "C" NOTA_API int32_t nota_pluginhost_scan_failed_count(void) { return 0; }
extern "C" NOTA_API int32_t nota_pluginhost_plugin_count(void) { return 0; }
extern "C" NOTA_API const char* nota_pluginhost_plugin_desc(int32_t) { return ""; }
extern "C" NOTA_API const char* nota_pluginhost_plugin_id(int32_t) { return ""; }
extern "C" NOTA_API int32_t nota_pluginhost_index_of_id(const char*) { return -1; }
extern "C" NOTA_API NotaResult nota_pluginhost_add_scan_path(const char*) { return NOTA_ERR_UNKNOWN; }
extern "C" NOTA_API NotaResult nota_pluginhost_remove_scan_path(int32_t) { return NOTA_ERR_INVALID_ARG; }
extern "C" NOTA_API int32_t nota_pluginhost_scan_path_count(void) { return 0; }
extern "C" NOTA_API const char* nota_pluginhost_scan_path(int32_t) { return ""; }
extern "C" NOTA_API int32_t nota_pluginhost_default_scan_path_count(const char*) { return 0; }
extern "C" NOTA_API const char* nota_pluginhost_default_scan_path(const char*, int32_t) { return ""; }
extern "C" NOTA_API int32_t nota_pluginhost_restore_default_scan_paths(const char*) { return 0; }
extern "C" NOTA_API int32_t nota_pluginhost_is_scan_path_removed(const char*) { return 0; }
