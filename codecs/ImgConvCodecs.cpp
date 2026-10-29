// ImgConvCodecs.dll — optional codecs for ImgConv, built from libjxl (statically linked):
//   * jpegli: Google's JPEG encoder — noticeably smaller JPGs at the same visual quality
//   * JPEG XL decoding — open .jxl files without any Microsoft Store extension
// ImgConv.exe works without this DLL and falls back to the built-in Windows codecs.
#include <csetjmp>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <vector>

#include "lib/jpegli/encode.h"
#include <jxl/decode.h>

#define API extern "C" __declspec(dllexport)

API void ic_free(void* p) { free(p); }

// ---------- jpegli ----------

struct ErrorJump
{
    jpeg_error_mgr mgr;
    jmp_buf jump;
};

static void OnError(j_common_ptr cinfo) { longjmp(reinterpret_cast<ErrorJump*>(cinfo->err)->jump, 1); }
static void OnMessage(j_common_ptr) {}

// Encodes 24-bit BGR rows (GDI+ Format24bppRgb layout) to JPEG. Returns 0 on success.
// The result is allocated with malloc; release it with ic_free.
API int ic_jpegli_encode(const uint8_t* bgr, int width, int height, int stride, int quality,
                         uint8_t** out, unsigned long* outSize)
{
    *out = nullptr;
    *outSize = 0;
    if (!bgr || width <= 0 || height <= 0) return 1;

    jpeg_compress_struct cinfo;
    ErrorJump err;
    unsigned char* buf = nullptr;
    unsigned long size = 0;
    std::vector<uint8_t> row(static_cast<size_t>(width) * 3);

    cinfo.err = jpegli_std_error(&err.mgr);
    err.mgr.error_exit = OnError;
    err.mgr.output_message = OnMessage;
    if (setjmp(err.jump))
    {
        jpegli_destroy_compress(&cinfo);
        free(buf);
        return 2;
    }

    jpegli_create_compress(&cinfo);
    jpegli_mem_dest(&cinfo, &buf, &size);
    cinfo.image_width = width;
    cinfo.image_height = height;
    cinfo.input_components = 3;
    cinfo.in_color_space = JCS_RGB;
    jpegli_set_defaults(&cinfo);
    jpegli_set_quality(&cinfo, quality, TRUE);
    jpegli_start_compress(&cinfo, TRUE);
    while (cinfo.next_scanline < cinfo.image_height)
    {
        const uint8_t* src = bgr + static_cast<size_t>(cinfo.next_scanline) * stride;
        for (int x = 0; x < width; x++) // BGR -> RGB
        {
            row[x * 3 + 0] = src[x * 3 + 2];
            row[x * 3 + 1] = src[x * 3 + 1];
            row[x * 3 + 2] = src[x * 3 + 0];
        }
        JSAMPROW r = row.data();
        jpegli_write_scanlines(&cinfo, &r, 1);
    }
    jpegli_finish_compress(&cinfo);
    jpegli_destroy_compress(&cinfo);

    *out = buf;
    *outSize = size;
    return 0;
}

// ---------- JPEG XL ----------

// Decodes a .jxl file to 32-bit BGRA (GDI+ Format32bppArgb layout), orientation applied.
// Returns 0 on success; the pixels are allocated with malloc — release with ic_free.
API int ic_jxl_decode(const uint8_t* data, size_t size, uint8_t** bgra, int* width, int* height)
{
    *bgra = nullptr;
    *width = *height = 0;
    JxlDecoder* dec = JxlDecoderCreate(nullptr);
    if (!dec) return 1;

    int rc = 2;
    uint8_t* pixels = nullptr;
    JxlBasicInfo info = {};
    const JxlPixelFormat format = { 4, JXL_TYPE_UINT8, JXL_NATIVE_ENDIAN, 0 };

    if (JxlDecoderSubscribeEvents(dec, JXL_DEC_BASIC_INFO | JXL_DEC_FULL_IMAGE) == JXL_DEC_SUCCESS &&
        JxlDecoderSetInput(dec, data, size) == JXL_DEC_SUCCESS)
    {
        JxlDecoderCloseInput(dec);
        for (;;)
        {
            JxlDecoderStatus st = JxlDecoderProcessInput(dec);
            if (st == JXL_DEC_BASIC_INFO)
            {
                if (JxlDecoderGetBasicInfo(dec, &info) != JXL_DEC_SUCCESS) break;
            }
            else if (st == JXL_DEC_NEED_IMAGE_OUT_BUFFER)
            {
                size_t need = 0;
                if (JxlDecoderImageOutBufferSize(dec, &format, &need) != JXL_DEC_SUCCESS) break;
                pixels = static_cast<uint8_t*>(malloc(need));
                if (!pixels || JxlDecoderSetImageOutBuffer(dec, &format, pixels, need) != JXL_DEC_SUCCESS) break;
            }
            else if (st == JXL_DEC_FULL_IMAGE)
            {
                rc = 0; // first frame is enough
                break;
            }
            else break; // error, need more input, or success without an image
        }
    }
    JxlDecoderDestroy(dec);

    if (rc != 0) { free(pixels); return rc; }

    // Orientation is applied by the decoder, so width/height may be swapped.
    const bool swap = info.orientation >= JXL_ORIENT_TRANSPOSE;
    const int w = static_cast<int>(swap ? info.ysize : info.xsize);
    const int h = static_cast<int>(swap ? info.xsize : info.ysize);
    for (size_t i = 0, n = static_cast<size_t>(w) * h * 4; i < n; i += 4) // RGBA -> BGRA
    {
        uint8_t t = pixels[i];
        pixels[i] = pixels[i + 2];
        pixels[i + 2] = t;
    }
    *bgra = pixels;
    *width = w;
    *height = h;
    return 0;
}
