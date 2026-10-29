/* Thin opaque C ABI. All decoder I/O is called by a background worker, never the device callback. */
#define STB_VORBIS_HEADER_ONLY
#include "stb_vorbis.c"
#define MINIAUDIO_IMPLEMENTATION
#include "miniaudio.h"
#undef STB_VORBIS_HEADER_ONLY
#include "stb_vorbis.c"

#ifdef _WIN32
#define GN_API __declspec(dllexport)
#else
#define GN_API __attribute__((visibility("default")))
#endif

/* ABI 2 uses UTF-8 paths and a context-bearing, AOT-safe render callback. */
GN_API int gn_abi_version(void) { return 2; }
typedef void (*gn_render)(void*, float*, unsigned);
typedef struct { ma_device device; gn_render render; void* context; } gn_device;
static void gn_callback(ma_device* device, void* output, const void* input, ma_uint32 frames)
{
    gn_device* owner = (gn_device*)device->pUserData;
    owner->render(owner->context, (float*)output, frames * 2);
    (void)input;
}
GN_API void* gn_device_create(gn_render render, void* context)
{
    gn_device* device = (gn_device*)calloc(1, sizeof(gn_device));
    if (!device || !render) { free(device); return NULL; }
    device->render = render;
    device->context = context;
    ma_device_config config = ma_device_config_init(ma_device_type_playback);
    config.playback.format = ma_format_f32;
    config.playback.channels = 2;
    config.sampleRate = 48000;
    config.periodSizeInFrames = 480;
    config.dataCallback = gn_callback;
    config.pUserData = device;
    if (ma_device_init(NULL, &config, &device->device) != MA_SUCCESS) { free(device); return NULL; }
    return device;
}
GN_API int gn_device_start(gn_device* device) { return ma_device_start(&device->device); }
GN_API void gn_device_destroy(gn_device* device)
{
    if (!device) return;
    ma_device_uninit(&device->device);
    free(device);
}
typedef struct { ma_decoder decoder; double duration; } gn_decoder;
GN_API void* gn_decoder_create(const char* path)
{
    gn_decoder* decoder = (gn_decoder*)calloc(1, sizeof(gn_decoder));
    ma_decoder_config config = ma_decoder_config_init(ma_format_f32, 2, 48000);
    ma_result result;
    FILE* file = NULL;
    if (!decoder || !path) { free(decoder); return NULL; }
#ifdef _WIN32
    /* miniaudio's narrow Windows fopen uses the ANSI code page, not UTF-8. */
    int count = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, path, -1, NULL, 0);
    wchar_t* wide = count > 0 ? (wchar_t*)malloc((size_t)count * sizeof(wchar_t)) : NULL;
    if (!wide) { free(decoder); return NULL; }
    if (!MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, path, -1, wide, count)) { free(wide); free(decoder); return NULL; }
    result = ma_decoder_init_file_w(wide, &config, &decoder->decoder);
    if (result == MA_SUCCESS) file = _wfopen(wide, L"rb");
    free(wide);
#else
    result = ma_decoder_init_file(path, &config, &decoder->decoder);
    if (result == MA_SUCCESS) file = fopen(path, "rb");
#endif
    if (result != MA_SUCCESS) { free(decoder); return NULL; }
    ma_uint64 length = 0;
    if (ma_decoder_get_length_in_pcm_frames(&decoder->decoder, &length) == MA_SUCCESS) decoder->duration = (double)length / 48000;
    /* miniaudio's callback-based Vorbis adapter cannot report length. Probe using a separate seekable file. */
    if (decoder->duration == 0 && file) {
        stb_vorbis* vorbis = stb_vorbis_open_file(file, 0, NULL, NULL);
        if (vorbis) {
            stb_vorbis_info info = stb_vorbis_get_info(vorbis);
            if (info.sample_rate) decoder->duration = (double)stb_vorbis_stream_length_in_samples(vorbis) / info.sample_rate;
            stb_vorbis_close(vorbis);
        }
    }
    if (file) fclose(file);
    return decoder;
}
GN_API int gn_decoder_read(gn_decoder* decoder, float* output, unsigned frames)
{
    ma_uint64 read = 0;
    ma_result result = ma_decoder_read_pcm_frames(&decoder->decoder, output, frames, &read);
    return result == MA_SUCCESS || result == MA_AT_END ? (int)read : -1;
}
GN_API double gn_decoder_duration(gn_decoder* decoder)
{
    return decoder->duration;
}
GN_API void gn_decoder_destroy(gn_decoder* decoder)
{
    if (!decoder) return;
    ma_decoder_uninit(&decoder->decoder);
    free(decoder);
}
