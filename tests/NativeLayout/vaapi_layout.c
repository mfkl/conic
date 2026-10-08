/* Checks the libva layouts mirrored in src/Shared/Linux/VaApi.cs against the
 * installed libva headers. Compile-time only: any mismatch fails the build.
 *
 *   cc -std=c11 -I <libva include dir> tests/NativeLayout/vaapi_layout.c -o vaapi-layout && ./vaapi-layout
 */
#include <assert.h>
#include <stddef.h>
#include <stdio.h>
#include <va/va.h>
#include <va/va_vpp.h>
#include <va/va_drmcommon.h>

#define SIZE(type, expected) static_assert(sizeof(type) == (expected), "size mismatch: " #type)
#define OFFSET(type, field, expected) static_assert(offsetof(type, field) == (expected), "offset mismatch: " #type "." #field)
#define VALUE(name, expected) static_assert((name) == (expected), "value mismatch: " #name)

static_assert(sizeof(void *) == 8, "64-bit only");

SIZE(VAGenericValue, 16);
OFFSET(VAGenericValue, value, 8);
SIZE(VASurfaceAttrib, 24);
OFFSET(VASurfaceAttrib, flags, 4);
OFFSET(VASurfaceAttrib, value, 8);
SIZE(VARectangle, 8);

SIZE(VADRMPRIMESurfaceDescriptor, 312);
OFFSET(VADRMPRIMESurfaceDescriptor, objects, 16);
OFFSET(VADRMPRIMESurfaceDescriptor, num_layers, 80);
OFFSET(VADRMPRIMESurfaceDescriptor, layers, 84);
SIZE(((VADRMPRIMESurfaceDescriptor *)0)->objects[0], 16);
SIZE(((VADRMPRIMESurfaceDescriptor *)0)->layers[0], 56);

SIZE(VAProcColorProperties, 8);
SIZE(VAProcPipelineParameterBuffer, 224);
OFFSET(VAProcPipelineParameterBuffer, surface, 0);
OFFSET(VAProcPipelineParameterBuffer, surface_region, 8);
OFFSET(VAProcPipelineParameterBuffer, surface_color_standard, 16);
OFFSET(VAProcPipelineParameterBuffer, output_region, 24);
OFFSET(VAProcPipelineParameterBuffer, output_background_color, 32);
OFFSET(VAProcPipelineParameterBuffer, output_color_standard, 36);
OFFSET(VAProcPipelineParameterBuffer, pipeline_flags, 40);
OFFSET(VAProcPipelineParameterBuffer, filter_flags, 44);
OFFSET(VAProcPipelineParameterBuffer, filters, 48);
OFFSET(VAProcPipelineParameterBuffer, num_filters, 56);
OFFSET(VAProcPipelineParameterBuffer, forward_references, 64);
OFFSET(VAProcPipelineParameterBuffer, num_forward_references, 72);
OFFSET(VAProcPipelineParameterBuffer, backward_references, 80);
OFFSET(VAProcPipelineParameterBuffer, num_backward_references, 88);
OFFSET(VAProcPipelineParameterBuffer, rotation_state, 92);
OFFSET(VAProcPipelineParameterBuffer, blend_state, 96);
OFFSET(VAProcPipelineParameterBuffer, mirror_state, 104);
OFFSET(VAProcPipelineParameterBuffer, additional_outputs, 112);
OFFSET(VAProcPipelineParameterBuffer, num_additional_outputs, 120);
OFFSET(VAProcPipelineParameterBuffer, input_surface_flag, 124);
OFFSET(VAProcPipelineParameterBuffer, output_surface_flag, 128);
OFFSET(VAProcPipelineParameterBuffer, input_color_properties, 132);
OFFSET(VAProcPipelineParameterBuffer, output_color_properties, 140);
OFFSET(VAProcPipelineParameterBuffer, processing_mode, 148);
OFFSET(VAProcPipelineParameterBuffer, output_hdr_metadata, 152);

VALUE(VA_RT_FORMAT_YUV420, 0x1);
VALUE(VA_RT_FORMAT_YUV420_10, 0x100);
VALUE(VA_RT_FORMAT_RGB32, 0x20000);
VALUE(VA_FOURCC_NV12, 0x3231564E);
VALUE(VA_FOURCC_P010, 0x30313050);
VALUE(VA_FOURCC_BGRA, 0x41524742);
VALUE(VA_FOURCC_RGBA, 0x41424752);
VALUE(VA_FOURCC_BGRX, 0x58524742);
VALUE(VAProfileNone, -1);
VALUE(VAEntrypointVideoProc, 10);
VALUE(VAProcPipelineParameterBufferType, 41);
VALUE(VA_PROC_PIPELINE_FAST, 0x2);
VALUE(VA_FILTER_SCALING_HQ, 0x200);
VALUE(VAProcColorStandardBT601, 1);
VALUE(VAProcColorStandardBT709, 2);
VALUE(VAProcColorStandardSRGB, 8);
VALUE(VAProcColorStandardBT2020, 12);
VALUE(VA_SURFACE_ATTRIB_MEM_TYPE_DRM_PRIME_2, 0x40000000);
VALUE(VA_EXPORT_SURFACE_READ_ONLY, 0x1);
VALUE(VA_EXPORT_SURFACE_SEPARATE_LAYERS, 0x4);
VALUE(VA_EXPORT_SURFACE_COMPOSED_LAYERS, 0x8);
VALUE(VASurfaceAttribPixelFormat, 1);
VALUE(VA_SURFACE_ATTRIB_SETTABLE, 2);
VALUE(VAGenericValueTypeInteger, 1);
VALUE(VA_INVALID_ID, 0xFFFFFFFF);

int main(void)
{
    printf("VA-API %s layouts match src/Shared/Linux/VaApi.cs.\n", VA_VERSION_S);
    return 0;
}
