// braw-decode — minimal Blackmagic RAW decoder for the offload proxy pipeline (same CLI as the Windows braw-decode.exe).
//
//   braw-decode --info [--scale S] clip.braw
//       one JSON line: width, height (of the processed image at scale S), sourceWidth, sourceHeight, fps, frames,
//       timecode, camera, and when the SDK reports them gamma, gamut, colorScienceGen, then sensorRate (the rate frame 0
//       was captured at, IBlackmagicRawFrame::GetSensorRate, 6 decimals like fps — last key, as Windows 0.8.0)
//   braw-decode [--scale S] [--start N] [--count N] clip.braw
//       writes frames to stdout as raw interleaved RGB 16-bit little endian (ffmpeg: -f rawvideo -pix_fmt rgb48le)
//
//   S = full | half | quarter | eighth
//
// Exit codes (as on Windows): 0 OK (also when the reader closed the pipe early), 2 usage, 3 library/codec missing,
// 4 clip cannot be opened, 5 a frame failed. Every exit-3/4 line names the library in use: an old one cannot open
// clips from newer cameras, and Offload's note at the clip says so. stderr is UTF-8 (paths come through as given).
// Frames are decoded in the clip's own gamma/gamut (as recorded); the LUT is applied later by ffmpeg.
// Build: Tools/braw-decode/build.sh (links against BlackmagicRawAPI.framework at runtime via the dispatch loader).

#include "BlackmagicRawAPI.h"

#include <CoreFoundation/CoreFoundation.h>
#include <mach-o/dyld.h>
#include <algorithm>
#include <atomic>
#include <cerrno>
#include <climits>
#include <csignal>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <libgen.h>
#include <string>
#include <unistd.h>
#include <vector>

static const char* kFramework = "BlackmagicRawAPI.framework";

// Keep in sync with OffloadKit BrawLibrary.searchDirs (BrawLibraryTests checks the folders are here).
static const char* kLibrarySearchPaths[] = {
    // next to this executable (bundled), then the SDK, then the free Blackmagic RAW Player, then DaVinci Resolve
    "@exe",
    "/Applications/Blackmagic RAW/Blackmagic RAW SDK/Mac/Libraries",
    "/Applications/Blackmagic RAW/Blackmagic RAW Player.app/Contents/Frameworks",
    "/Applications/DaVinci Resolve/DaVinci Resolve.app/Contents/Frameworks",
};

static const char* kUsage = "usage: braw-decode [--info] [--scale full|half|quarter|eighth] [--start N] [--count N] clip.braw";

// The framework that was loaded (named in every exit-3/4 line).
static std::string gLibrary = kFramework;

static std::string toStd(CFStringRef s) {
    if (!s) return "";
    char buf[1024];
    if (CFStringGetCString(s, buf, sizeof buf, kCFStringEncodingUTF8)) return buf;
    return "";
}

static std::string jsonEscape(const std::string& s) {
    std::string o;
    for (char c : s) {
        if (c == '"' || c == '\\') o += '\\';
        if ((unsigned char)c >= 0x20) o += c;
    }
    return o;
}

struct FrameSlot {
    std::vector<uint8_t> data;
    uint32_t width = 0, height = 0;
    bool ok = false;
};

class Callback : public IBlackmagicRawCallback {
public:
    BlackmagicRawResolutionScale scale = blackmagicRawResolutionScaleFull;
    std::vector<FrameSlot>* slots = nullptr;
    uint64_t base = 0;
    std::atomic<int> errors{0};
    // The rate frame 0 was captured at (--info); 0 when the SDK does not say.
    std::atomic<float> sensorRate{0};

    void ReadComplete(IBlackmagicRawJob* readJob, HRESULT result, IBlackmagicRawFrame* frame) override {
        IBlackmagicRawJob* decodeJob = nullptr;
        void* user = nullptr;
        readJob->GetUserData(&user);
        if (result == S_OK && frame && (uintptr_t)user == 0) {
            float rate = 0;
            if (frame->GetSensorRate(&rate) == S_OK && rate > 0) sensorRate = rate;
        }
        if (result == S_OK) result = frame->SetResourceFormat(blackmagicRawResourceFormatRGBU16);
        if (result == S_OK) result = frame->SetResolutionScale(scale);
        if (result == S_OK) result = frame->CreateJobDecodeAndProcessFrame(nullptr, nullptr, &decodeJob);
        if (result == S_OK) {
            decodeJob->SetUserData(user);
            result = decodeJob->Submit();
        }
        if (result != S_OK) {
            fprintf(stderr, "braw-decode: read/decode setup failed (0x%08X)\n", (unsigned)result);
            errors++;
            if (decodeJob) decodeJob->Release();
        }
        readJob->Release();
    }

    void ProcessComplete(IBlackmagicRawJob* job, HRESULT result, IBlackmagicRawProcessedImage* img) override {
        void* user = nullptr;
        job->GetUserData(&user);
        uint64_t index = (uint64_t)(uintptr_t)user;
        if (result == S_OK && slots && index >= base && index - base < slots->size()) {
            FrameSlot& slot = (*slots)[index - base];
            uint32_t size = 0;
            void* res = nullptr;
            img->GetWidth(&slot.width);
            img->GetHeight(&slot.height);
            img->GetResourceSizeBytes(&size);
            img->GetResource(&res);
            // The SDK pads the buffer; stdout must carry exactly width*height RGB48 pixels.
            size_t frameBytes = (size_t)slot.width * slot.height * 6;
            if (size > frameBytes) size = (uint32_t)frameBytes;
            slot.data.assign((uint8_t*)res, (uint8_t*)res + size);
            slot.ok = true;
        } else {
            fprintf(stderr, "braw-decode: process failed (0x%08X)\n", (unsigned)result);
            errors++;
        }
        job->Release();
    }

    void DecodeComplete(IBlackmagicRawJob*, HRESULT) override {}
    void TrimProgress(IBlackmagicRawJob*, float) override {}
    void TrimComplete(IBlackmagicRawJob*, HRESULT) override {}
    void SidecarMetadataParseWarning(IBlackmagicRawClip*, CFStringRef, uint32_t, CFStringRef) override {}
    void SidecarMetadataParseError(IBlackmagicRawClip*, CFStringRef, uint32_t, CFStringRef) override {}
    void PreparePipelineComplete(void*, HRESULT) override {}

    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID, LPVOID*) override { return E_NOTIMPL; }
    ULONG STDMETHODCALLTYPE AddRef() override { return 0; }
    ULONG STDMETHODCALLTYPE Release() override { return 0; }
};

// The folder of this executable with symlinks resolved — where "@exe" looks (as the SDK's dispatch does).
static std::string exeDir() {
    char path[PATH_MAX], resolved[PATH_MAX];
    uint32_t size = sizeof path;
    if (_NSGetExecutablePath(path, &size) != 0 || !realpath(path, resolved)) return ".";
    return dirname(resolved);
}

static IBlackmagicRawFactory* loadFactory() {
    for (const char* p : kLibrarySearchPaths) {
        IBlackmagicRawFactory* f = nullptr;
        std::string dir;
        if (strcmp(p, "@exe") == 0) {
            dir = exeDir();
            f = CreateBlackmagicRawFactoryInstanceFromExeRelativePath(CFSTR("."));
        } else {
            dir = p;
            CFStringRef path = CFStringCreateWithCString(nullptr, p, kCFStringEncodingUTF8);
            f = CreateBlackmagicRawFactoryInstanceFromPath(path);
            CFRelease(path);
        }
        if (f) {
            gLibrary = dir + "/" + kFramework;
            return f;
        }
    }
    return nullptr;
}

// Ask the created object for IBlackmagicRawFactory (QueryInterface is IUnknown's first slot on every object): a
// framework of another SDK generation whose object lacks it is a clear exit 3, never a call into the wrong slot.
// Takes over (and releases) the created reference. Same as Windows BrawRuntime.FromCreated.
static IBlackmagicRawFactory* asFactory(IBlackmagicRawFactory* created) {
    IBlackmagicRawFactory* factory = nullptr;
    HRESULT r = created->QueryInterface(IID_IBlackmagicRawFactory, (LPVOID*)&factory);
    created->Release();
    if (r != S_OK || !factory) {
        if (factory) factory->Release();
        fprintf(stderr, "braw-decode: %s: the created object is not an IBlackmagicRawFactory (0x%08X)\n", gLibrary.c_str(),
                (unsigned)r);
        return nullptr;
    }
    return factory;
}

struct Options {
    bool info = false;
    BlackmagicRawResolutionScale scale = blackmagicRawResolutionScaleFull;
    uint64_t start = 0;
    uint64_t count = UINT64_MAX;   // UINT64_MAX = to the end of the clip
    const char* path = nullptr;
};

static bool parseScale(const char* s, BlackmagicRawResolutionScale& out) {
    if (!strcmp(s, "full")) out = blackmagicRawResolutionScaleFull;
    else if (!strcmp(s, "half")) out = blackmagicRawResolutionScaleHalf;
    else if (!strcmp(s, "quarter")) out = blackmagicRawResolutionScaleQuarter;
    else if (!strcmp(s, "eighth")) out = blackmagicRawResolutionScaleEighth;
    else return false;
    return true;
}

// Digits only (strtoull alone takes "-1", "12abc" and "").
static bool parseNumber(const char* s, uint64_t& out) {
    if (!*s) return false;
    for (const char* c = s; *c; c++)
        if (*c < '0' || *c > '9') return false;
    errno = 0;
    out = strtoull(s, nullptr, 10);
    return errno == 0;
}

// Strict like the Windows decoder: Offload never passes anything else, so a typo fails loudly (exit 2).
static bool parseArgs(int argc, char** argv, Options& o) {
    for (int i = 1; i < argc; i++) {
        const char* a = argv[i];
        const char* value = i + 1 < argc ? argv[i + 1] : nullptr;
        if (!strcmp(a, "--info")) {
            o.info = true;
        } else if (!strcmp(a, "--scale")) {
            if (!value || !parseScale(value, o.scale)) return false;
            i++;
        } else if (!strcmp(a, "--start")) {
            if (!value || !parseNumber(value, o.start)) return false;
            i++;
        } else if (!strcmp(a, "--count")) {
            if (!value || !parseNumber(value, o.count)) return false;
            i++;
        } else if (!strncmp(a, "--", 2)) {
            return false;
        } else {
            o.path = a;
        }
    }
    return o.path != nullptr;
}

// false when the reader went away (EPIPE — SIGPIPE is ignored, see main).
static bool writeAll(const uint8_t* p, size_t n) {
    while (n > 0) {
        ssize_t w = write(STDOUT_FILENO, p, n);
        if (w < 0) {
            if (errno == EINTR) continue;
            return false;
        }
        p += w;
        n -= (size_t)w;
    }
    return true;
}

// ffmpeg has what it wanted (a thumbnail needs one frame) and closed the pipe: a success, not a failure.
[[noreturn]] static void readerGone() {
    fprintf(stderr, "braw-decode: stdout closed by the reader, stopping\n");
    _exit(0);
}

static std::string fourcc(uint32_t v) {
    char s[5] = {(char)(v >> 24), (char)(v >> 16), (char)(v >> 8), (char)v, 0};
    return s;
}

static void attributeWarning(const char* key, uint32_t attribute, const char* what) {
    fprintf(stderr, "braw-decode: %s: clip attribute '%s' %s\n", key, fourcc(attribute).c_str(), what);
}

// Gamma and gamut are strings. An attribute the SDK does not return is left out silently; one of another variant
// type is a warning on stderr and left out too — never a failure of --info (Windows ruling T6-attr).
static bool attributeText(IBlackmagicRawClipProcessingAttributes* attrs, BlackmagicRawClipProcessingAttribute attribute,
                          const char* key, std::string& out) {
    Variant v;
    VariantInit(&v);
    bool ok = false;
    if (attrs->GetClipAttribute(attribute, &v) == S_OK) {
        if (v.vt == blackmagicRawVariantTypeString) {
            out = toStd(v.bstrVal);
            ok = true;
        } else if (v.vt != blackmagicRawVariantTypeEmpty) {
            attributeWarning(key, attribute, "is a number, expected a string");
        }
    }
    VariantClear(&v);
    return ok;
}

// The colour science generation is a u16; any integer type is taken.
static bool attributeNumber(IBlackmagicRawClipProcessingAttributes* attrs, BlackmagicRawClipProcessingAttribute attribute,
                            const char* key, int& out) {
    Variant v;
    VariantInit(&v);
    bool ok = false;
    if (attrs->GetClipAttribute(attribute, &v) == S_OK) {
        ok = true;
        switch (v.vt) {
        case blackmagicRawVariantTypeU8: out = (int)(v.uiVal & 0xFF); break;
        case blackmagicRawVariantTypeS16: out = v.iVal; break;
        case blackmagicRawVariantTypeU16: out = v.uiVal; break;
        case blackmagicRawVariantTypeS32: out = v.intVal; break;
        case blackmagicRawVariantTypeU32:
            if (v.uintVal > (uint32_t)INT_MAX) {
                attributeWarning(key, attribute, "is out of range");
                ok = false;
            } else {
                out = (int)v.uintVal;
            }
            break;
        case blackmagicRawVariantTypeEmpty: ok = false; break;
        case blackmagicRawVariantTypeString:
            attributeWarning(key, attribute, "is a string, expected a number");
            ok = false;
            break;
        default:
            attributeWarning(key, attribute, "has an unexpected variant type");
            ok = false;
            break;
        }
    }
    VariantClear(&v);
    return ok;
}

int main(int argc, char** argv) {
    // A reader that goes away must be a failed write we can see (EPIPE → exit 0 with a note), not a death by SIGPIPE.
    signal(SIGPIPE, SIG_IGN);

    Options o;
    if (!parseArgs(argc, argv, o)) {
        fprintf(stderr, "%s\n", kUsage);
        return 2;
    }

    IBlackmagicRawFactory* created = loadFactory();
    if (!created) {
        fprintf(stderr, "braw-decode: %s not found (install Blackmagic RAW Player, the Blackmagic RAW SDK or DaVinci Resolve)\n",
                kFramework);
        return 3;
    }
    IBlackmagicRawFactory* factory = asFactory(created);
    if (!factory) return 3;
    IBlackmagicRaw* codec = nullptr;
    IBlackmagicRawClip* clip = nullptr;
    HRESULT r = factory->CreateCodec(&codec);
    if (r != S_OK || !codec) {
        fprintf(stderr, "braw-decode: CreateCodec failed (0x%08X; library %s)\n", (unsigned)r, gLibrary.c_str());
        return 3;
    }

    // The configuration is read on the first OpenClip, so the pipeline is set before it (CPU).
    IBlackmagicRawConfiguration* config = nullptr;
    if (codec->QueryInterface(IID_IBlackmagicRawConfiguration, (LPVOID*)&config) == S_OK && config) {
        config->SetPipeline(blackmagicRawPipelineCPU, nullptr, nullptr);
        config->Release();
    }

    CFStringRef cfPath = CFStringCreateWithCString(nullptr, o.path, kCFStringEncodingUTF8);
    r = cfPath ? codec->OpenClip(cfPath, &clip) : E_FAIL;
    if (cfPath) CFRelease(cfPath);
    if (r != S_OK || !clip) {
        fprintf(stderr, "braw-decode: cannot open %s (0x%08X; library %s)\n", o.path, (unsigned)r, gLibrary.c_str());
        return 4;
    }

    uint32_t width = 0, height = 0;
    float fps = 0;
    uint64_t frames = 0;
    clip->GetWidth(&width);
    clip->GetHeight(&height);
    clip->GetFrameRate(&fps);
    clip->GetFrameCount(&frames);

    Callback cb;
    cb.scale = o.scale;
    r = codec->SetCallback(&cb);
    if (r != S_OK) {
        fprintf(stderr, "braw-decode: SetCallback failed (0x%08X; library %s)\n", (unsigned)r, gLibrary.c_str());
        return 3;
    }

    if (o.info) {
        // Decode frame 0 at the requested scale: the processed size is the truth, not width/scale.
        std::vector<FrameSlot> slots(1);
        cb.slots = &slots;
        cb.base = 0;
        IBlackmagicRawJob* job = nullptr;
        if (frames > 0 && clip->CreateJobReadFrame(0, &job) == S_OK) {
            job->SetUserData((void*)(uintptr_t)0);
            if (job->Submit() != S_OK) job->Release();
            codec->FlushJobs();
        }
        // What the clip was shot in — Offload picks the LUT by it (LutResolver.braw).
        std::string gamma, gamut;
        int gen = 0;
        bool hasGamma = false, hasGamut = false, hasGen = false;
        IBlackmagicRawClipProcessingAttributes* attrs = nullptr;
        if (clip->CloneClipProcessingAttributes(&attrs) == S_OK && attrs) {
            hasGamma = attributeText(attrs, blackmagicRawClipProcessingAttributeGamma, "gamma", gamma);
            hasGamut = attributeText(attrs, blackmagicRawClipProcessingAttributeGamut, "gamut", gamut);
            hasGen = attributeNumber(attrs, blackmagicRawClipProcessingAttributeColorScienceGen, "colorScienceGen", gen);
            attrs->Release();
        }
        CFStringRef tc = nullptr, cam = nullptr;
        clip->GetTimecodeForFrame(0, &tc);
        clip->GetCameraType(&cam);
        char head[256];
        snprintf(head, sizeof head, "{\"width\":%u,\"height\":%u,\"sourceWidth\":%u,\"sourceHeight\":%u,\"fps\":%.6f,\"frames\":%llu,",
                 slots[0].ok ? slots[0].width : width, slots[0].ok ? slots[0].height : height, width, height, fps,
                 (unsigned long long)frames);
        std::string json = head;
        json += "\"timecode\":\"" + jsonEscape(toStd(tc)) + "\",\"camera\":\"" + jsonEscape(toStd(cam)) + "\"";
        if (hasGamma) json += ",\"gamma\":\"" + jsonEscape(gamma) + "\"";
        if (hasGamut) json += ",\"gamut\":\"" + jsonEscape(gamut) + "\"";
        if (hasGen) json += ",\"colorScienceGen\":" + std::to_string(gen);
        float sensor = cb.sensorRate;
        if (sensor > 0) {
            char rate[64];
            snprintf(rate, sizeof rate, ",\"sensorRate\":%.6f", (double)sensor);
            json += rate;
        }
        json += "}\n";
        if (tc) CFRelease(tc);
        if (cam) CFRelease(cam);
        if (!writeAll((const uint8_t*)json.data(), json.size())) readerGone();
    } else {
        // Decode in small batches: jobs run in parallel inside the SDK, output stays in frame order.
        const uint64_t batch = 8;
        uint64_t end = std::min(frames, o.start + std::min(o.count, UINT64_MAX - o.start));
        std::vector<FrameSlot> slots(batch);
        cb.slots = &slots;
        for (uint64_t b = o.start; b < end; b += batch) {
            uint64_t n = std::min(batch, end - b);
            for (auto& s : slots) s.ok = false;
            cb.base = b;
            for (uint64_t i = 0; i < n; i++) {
                IBlackmagicRawJob* job = nullptr;
                if (clip->CreateJobReadFrame(b + i, &job) != S_OK) { cb.errors++; continue; }
                job->SetUserData((void*)(uintptr_t)(b + i));
                if (job->Submit() != S_OK) { job->Release(); cb.errors++; }
            }
            codec->FlushJobs();
            for (uint64_t i = 0; i < n; i++) {
                if (!slots[i].ok) {
                    fprintf(stderr, "braw-decode: frame %llu failed\n", (unsigned long long)(b + i));
                    _exit(5);
                }
                if (!writeAll(slots[i].data.data(), slots[i].data.size())) readerGone();
            }
        }
    }

    clip->Release();
    codec->Release();
    factory->Release();
    _exit(cb.errors > 0 ? 5 : 0);
}
