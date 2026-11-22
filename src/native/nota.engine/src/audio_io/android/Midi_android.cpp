// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MIDI on Android (USB / Bluetooth MIDI devices through android.media.midi). The device
// side lives in the app (Android/AndroidMidi.cs): it watches MidiManager, opens every
// device and reports it here with nota_android_midi_device_added/_removed; incoming bytes
// arrive with nota_android_midi_receive (on Android's MIDI thread), and outgoing bytes leave
// through the sender the app registers. Everything above — MidiInput / MidiOutput /
// MidiConfig, the uid blocklist, MPE, MIDI learn, control surfaces — is the same as on the
// desktop: a device's uid is the id the app gives it, its name the product name.

#include "audio_io/MidiConfig.h"
#include "audio_io/MidiInput.h"
#include "audio_io/MidiOutput.h"
#include "audio_io/MidiParser.h"
#include "nota/nota_engine.h"

#include <algorithm>
#include <atomic>
#include <memory>
#include <mutex>
#include <string>
#include <vector>

namespace nota {
namespace {

struct Device {
    std::string uid;
    std::string name;
    bool hasInput = false;  // sends to Nota (a keyboard's output port)
    bool hasOutput = false; // takes bytes from Nota
};

std::mutex gDevMx;
std::vector<Device> gDevices;
std::atomic<uint32_t> gGeneration{1};

using SendFn = int32_t (*)(const char* uid, const uint8_t* data, int32_t len);
std::atomic<SendFn> gSend{nullptr};

struct OpenPort {
    std::string uid;
    int32_t source = 0;
    MidiParser parser;
};

// Open MidiInput instances (normally one, the engine's).
std::mutex gInMx;
std::vector<MidiInput::Impl*> gOpen;

} // namespace

struct MidiInput::Impl {
    MidiInput::MessageCallback cb;
    MidiInput::SysexCallback sysex;
    std::vector<std::unique_ptr<OpenPort>> ports;
    std::vector<MidiInput::PortInfo> info;
};

MidiInput::MidiInput() : impl_(new Impl()) {}
MidiInput::~MidiInput() {
    close();
    delete impl_;
}

bool MidiInput::open(MessageCallback cb, const std::vector<std::string>& disabledUids) {
    return open(std::move(cb), nullptr, disabledUids);
}

bool MidiInput::open(MessageCallback cb, SysexCallback sysex, const std::vector<std::string>& disabledUids) {
    close();
    std::vector<Device> devices;
    {
        std::lock_guard lock(gDevMx);
        devices = gDevices;
    }
    std::lock_guard lock(gInMx);
    impl_->cb = std::move(cb);
    impl_->sysex = std::move(sysex);
    uids_.clear();
    for (const auto& d : devices) {
        if (!d.hasInput) continue;
        if (std::find(disabledUids.begin(), disabledUids.end(), d.uid) != disabledUids.end()) continue;
        auto port = std::make_unique<OpenPort>();
        port->uid = d.uid;
        port->source = std::min<int32_t>(static_cast<int32_t>(impl_->ports.size()), kMaxSources - 1);
        if (static_cast<int32_t>(uids_.size()) < kMaxSources) uids_.push_back(d.uid);
        PortInfo info;
        info.source = port->source;
        info.uid = d.uid;
        info.name = d.name;
        if (d.hasOutput) info.outputUid = d.uid;
        impl_->info.push_back(std::move(info));
        impl_->ports.push_back(std::move(port));
    }
    gOpen.push_back(impl_);
    return true;
}

void MidiInput::close() {
    std::lock_guard lock(gInMx);
    gOpen.erase(std::remove(gOpen.begin(), gOpen.end(), impl_), gOpen.end());
    impl_->ports.clear();
    impl_->info.clear();
}

const std::vector<MidiInput::PortInfo>& MidiInput::ports() const { return impl_->info; }

uint32_t MidiInput::portsGeneration() { return gGeneration.load(std::memory_order_acquire); }

// ---- output ------------------------------------------------------------------------------

struct MidiOutput::Impl {
    std::string uid;
};

MidiOutput::MidiOutput() : impl_(new Impl()) {}
MidiOutput::~MidiOutput() {
    close();
    delete impl_;
}

bool MidiOutput::open(const std::string& uid) {
    std::lock_guard lock(gDevMx);
    for (const auto& d : gDevices)
        if (d.uid == uid && d.hasOutput) {
            impl_->uid = uid;
            return true;
        }
    return false;
}

bool MidiOutput::send(const uint8_t* data, int32_t len) {
    auto fn = gSend.load(std::memory_order_acquire);
    if (impl_->uid.empty() || !fn || !data || len <= 0) return false;
    return fn(impl_->uid.c_str(), data, len) != 0;
}

void MidiOutput::close() { impl_->uid.clear(); }

// ---- config ------------------------------------------------------------------------------

std::vector<MidiDeviceInfo> enumerateMidiInputs() {
    std::vector<MidiDeviceInfo> out;
    std::lock_guard lock(gDevMx);
    for (const auto& d : gDevices)
        if (d.hasInput) out.push_back({d.uid, d.name});
    return out;
}

std::string midiUidForEndpoint(uint32_t endpoint) {
    std::lock_guard lock(gDevMx);
    uint32_t i = 0;
    for (const auto& d : gDevices)
        if (d.hasInput && i++ == endpoint) return d.uid;
    return {};
}

std::string midiEndpointName(uint32_t) { return {}; }

} // namespace nota

// ---- the app's side (Android/AndroidMidi.cs) ---------------------------------------------

extern "C" NOTA_API void nota_android_midi_device_added(const char* uid, const char* name, int32_t has_input,
                                                         int32_t has_output) {
    if (!uid || !*uid) return;
    {
        std::lock_guard lock(nota::gDevMx);
        auto& list = nota::gDevices;
        list.erase(std::remove_if(list.begin(), list.end(), [&](const nota::Device& d) { return d.uid == uid; }),
                   list.end());
        list.push_back({uid, name && *name ? name : uid, has_input != 0, has_output != 0});
    }
    nota::gGeneration.fetch_add(1, std::memory_order_acq_rel);
}

extern "C" NOTA_API void nota_android_midi_device_removed(const char* uid) {
    if (!uid) return;
    {
        std::lock_guard lock(nota::gDevMx);
        auto& list = nota::gDevices;
        list.erase(std::remove_if(list.begin(), list.end(), [&](const nota::Device& d) { return d.uid == uid; }),
                   list.end());
    }
    nota::gGeneration.fetch_add(1, std::memory_order_acq_rel);
}

extern "C" NOTA_API void nota_android_midi_set_sender(int32_t (*send)(const char*, const uint8_t*, int32_t)) {
    nota::gSend.store(send, std::memory_order_release);
}

// Android's MIDI thread: bytes from device `uid` (a whole MIDI stream chunk; running status
// and SysEx split across calls are handled per port).
extern "C" NOTA_API void nota_android_midi_receive(const char* uid, const uint8_t* data, int32_t len) {
    if (!uid || !data || len <= 0) return;
    std::lock_guard lock(nota::gInMx);
    for (auto* impl : nota::gOpen) {
        for (auto& p : impl->ports) {
            if (p->uid != uid) continue;
            const int32_t source = p->source;
            p->parser.feed(
                data, static_cast<size_t>(len),
                [&](int status, int ch, int d1, int d2) {
                    if (impl->cb) impl->cb(source, status, ch, d1, d2);
                },
                [&](const uint8_t* s, int32_t n) {
                    if (impl->sysex) impl->sysex(source, s, n);
                });
        }
    }
}
