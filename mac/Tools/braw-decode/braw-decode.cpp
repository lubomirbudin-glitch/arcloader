// braw-decode — minimal Blackmagic RAW decoder for the offload proxy pipeline.
//
//   braw-decode --info [--scale S] clip.braw
//       prints JSON: width, height (of the processed image at scale S), sourceWidth, sourceHeight,
//       fps, frames, timecode, camera
//   braw-decode [--scale S] [--start N] [--count N] clip.braw
//       writes frames to stdout as raw interleaved RGB 16-bit little endian (ffmpeg: -f rawvideo -pix_fmt rgb48le)
//
//   S = full | half | quarter | eighth
//
// Frames are decoded in the clip's own gamma/gamut (as recorded); the LUT is applied later by ffmpeg.
// Build: Tools/braw-decode/build.sh (links against BlackmagicRawAPI.framework at runtime via the dispatch loader).

#include "BlackmagicRawAPI.h"

#include <CoreFoundation/CoreFoundation.h>
#include <atomic>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <mutex>
#include <string>
#include <unistd.h>
#include <vector>

static const char* kLibrarySearchPaths[] = {
    // next to this executable (bundled), then the SDK, then the free Blackmagic RAW Player
    "@exe",
    "/Applications/Blackmagic RAW/Blackmagic RAW SDK/Mac/Libraries",
    "/Applications/Blackmagic RAW/Blackmagic RAW Player.app/Contents/Frameworks",
    "/Applications/DaVinci Resolve/DaVinci Resolve.app/Contents/Libraries",
};

static std::string toStd(CFStringRef s) {
    if (!s) return "";
    char buf[256];
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

    void ReadComplete(IBlackmagicRawJob* readJob, HRESULT result, IBlackmagicRawFrame* frame) override {
        IBlackmagicRawJob* decodeJob = nullptr;
        void* user = nullptr;
        readJob->GetUserData(&user);
        if (result == S_OK) result = frame->SetResourceFormat(blackmagicRawResourceFormatRGBU16);
        if (result == S_OK) result = frame->SetResolutionScale(scale);
        if (result == S_OK) result = frame->CreateJobDecodeAndProcessFrame(nullptr, nullptr, &decodeJob);
        if (result == S_OK) {
            decodeJob->SetUserData(user);
            result = decodeJob->Submit();
        }
        if (result != S_OK) {
            fprintf(stderr, "braw-decode: read/decode setup failed (0x%08x)\n", (unsigned)result);
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
            fprintf(stderr, "braw-decode: process failed (0x%08x)\n", (unsigned)result);
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

static IBlackmagicRawFactory* loadFactory() {
    for (const char* p : kLibrarySearchPaths) {
        IBlackmagicRawFactory* f = nullptr;
        if (strcmp(p, "@exe") == 0) {
            f = CreateBlackmagicRawFactoryInstanceFromExeRelativePath(CFSTR("."));
        } else {
            CFStringRef path = CFStringCreateWithCString(nullptr, p, kCFStringEncodingUTF8);
            f = CreateBlackmagicRawFactoryInstanceFromPath(path);
            CFRelease(path);
        }
        if (f) return f;
    }
    return nullptr;
}

static BlackmagicRawResolutionScale parseScale(const char* s) {
    if (!strcmp(s, "half")) return blackmagicRawResolutionScaleHalf;
    if (!strcmp(s, "quarter")) return blackmagicRawResolutionScaleQuarter;
    if (!strcmp(s, "eighth")) return blackmagicRawResolutionScaleEighth;
    return blackmagicRawResolutionScaleFull;
}

static bool writeAll(const uint8_t* p, size_t n) {
    while (n > 0) {
        ssize_t w = write(STDOUT_FILENO, p, n);
        if (w < 0) {
            if (errno == EINTR) continue;
            return false;  // ffmpeg closed the pipe
        }
        p += w;
        n -= (size_t)w;
    }
    return true;
}

int main(int argc, char** argv) {
    bool info = false;
    const char* scaleArg = "full";
    uint64_t start = 0, count = UINT64_MAX;
    const char* path = nullptr;
    for (int i = 1; i < argc; i++) {
        if (!strcmp(argv[i], "--info")) info = true;
        else if (!strcmp(argv[i], "--scale") && i + 1 < argc) scaleArg = argv[++i];
        else if (!strcmp(argv[i], "--start") && i + 1 < argc) start = strtoull(argv[++i], nullptr, 10);
        else if (!strcmp(argv[i], "--count") && i + 1 < argc) count = strtoull(argv[++i], nullptr, 10);
        else path = argv[i];
    }
    if (!path) {
        fprintf(stderr, "usage: braw-decode [--info] [--scale full|half|quarter|eighth] [--start N] [--count N] clip.braw\n");
        return 2;
    }

    IBlackmagicRawFactory* factory = loadFactory();
    if (!factory) {
        fprintf(stderr, "braw-decode: BlackmagicRawAPI.framework not found (install Blackmagic RAW Player or SDK)\n");
        return 3;
    }
    IBlackmagicRaw* codec = nullptr;
    IBlackmagicRawClip* clip = nullptr;
    if (factory->CreateCodec(&codec) != S_OK) {
        fprintf(stderr, "braw-decode: CreateCodec failed\n");
        return 3;
    }
    CFStringRef cfPath = CFStringCreateWithCString(nullptr, path, kCFStringEncodingUTF8);
    HRESULT r = codec->OpenClip(cfPath, &clip);
    CFRelease(cfPath);
    if (r != S_OK) {
        fprintf(stderr, "braw-decode: cannot open %s\n", path);
        return 4;
    }

    uint32_t width = 0, height = 0;
    float fps = 0;
    uint64_t frames = 0;
    clip->GetWidth(&width);
    clip->GetHeight(&height);
    clip->GetFrameRate(&fps);
    clip->GetFrameCount(&frames);

    IBlackmagicRawConfiguration* config = nullptr;
    if (codec->QueryInterface(IID_IBlackmagicRawConfiguration, (LPVOID*)&config) == S_OK && config) {
        config->SetPipeline(blackmagicRawPipelineCPU, nullptr, nullptr);
        config->Release();
    }

    Callback cb;
    cb.scale = parseScale(scaleArg);
    codec->SetCallback(&cb);

    if (info) {
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
        CFStringRef tc = nullptr, cam = nullptr;
        clip->GetTimecodeForFrame(0, &tc);
        clip->GetCameraType(&cam);
        printf("{\"width\":%u,\"height\":%u,\"sourceWidth\":%u,\"sourceHeight\":%u,\"fps\":%.6f,\"frames\":%llu,"
               "\"timecode\":\"%s\",\"camera\":\"%s\"}\n",
               slots[0].ok ? slots[0].width : width, slots[0].ok ? slots[0].height : height, width, height, fps,
               (unsigned long long)frames, jsonEscape(toStd(tc)).c_str(), jsonEscape(toStd(cam)).c_str());
        if (tc) CFRelease(tc);
        if (cam) CFRelease(cam);
    } else {
        // Decode in small batches: jobs run in parallel inside the SDK, output stays in frame order.
        const uint64_t batch = 8;
        uint64_t end = count == UINT64_MAX ? frames : std::min(frames, start + count);
        std::vector<FrameSlot> slots(batch);
        cb.slots = &slots;
        for (uint64_t b = start; b < end; b += batch) {
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
                if (!writeAll(slots[i].data.data(), slots[i].data.size())) _exit(0);
            }
        }
    }

    clip->Release();
    codec->Release();
    factory->Release();
    fflush(stdout);
    _exit(cb.errors > 0 ? 5 : 0);
}
