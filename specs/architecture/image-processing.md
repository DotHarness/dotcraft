# Image processing

| Field | Value |
|---|---|
| Version | 0.8.3 |
| Status | Living |
| Date | 2026-10-10 |
| Parent | [Runtime module boundaries](runtime-module-boundaries.md) |
| Related Specs | [Tool architecture](tools-architecture.md), [Remote screen view](../features/remote-screen-view.md) |

This specification owns image normalization and screenshot encoding behavior. It defines the
shared image capability and the policies applied by its consumers.

## Responsibility boundaries

The shared image capability owns format recognition, validation, decoding, encoding, resizing,
file metadata and resource limits. It is platform-independent and provider-independent, with no
third-party image library, system codec or external process dependency.

Agents owns model input limits, output format selection and failure placeholders. Core owns
screenshot size and quality selection and integrates tool images and image-generation references.
Remote screen view owns frame limits, retries and capture availability. Assembly dependencies
follow the parent specification.

## Model image flow

1. Check the encoded input against the 64 MiB input limit and recognize its format from its bytes.
   A declared MIME type must not override the detected format.
2. Decode and validate the default image or first frame. Recognition alone does not establish
   validity and must not permit unvalidated bytes to reach the model.
3. Select dimensions that preserve the aspect ratio without enlargement. The longest side must
   not exceed 2048 pixels, and the grid of 32-pixel patches must contain at most 2500 patches.
   Both dimensions must remain positive; a 2048 by 2048 image becomes 1600 by 1600.
4. Preserve the original file when the format permits it and resizing is unnecessary; otherwise
   resize and encode according to the format contract below.
5. Return the encoded image with its detected MIME type and original and output dimensions.
   Preserve application-level content properties without mutating the input.

| Input | Without resizing | With resizing |
|---|---|---|
| PNG | Preserve the validated original file | RGBA8 PNG |
| JPEG | Preserve the validated original file | JPEG, quality 85, 4:4:4 chroma sampling |
| WebP | Preserve the original file after validating its first frame | Static lossless RGBA8 WebP |
| GIF | First frame as RGBA8 PNG | Resized first frame as RGBA8 PNG |
| BMP | RGBA8 PNG | Resized RGBA8 PNG |

Unsupported formats and encoding modes must fail explicitly. MIME-only recognition may return
`application/octet-stream` for unknown content.

## Frames, pixels and metadata

PNG and APNG use the default image. GIF uses its first frame at the declared offset on a
transparent-black canvas, honoring transparency without applying the file's background color.
WebP uses its first frame at the declared canvas offset and honors its blend behavior. Its initial
canvas is transparent black, or opaque black without alpha; the animation background-color hint
does not replace it. Preserving a PNG or WebP file retains the entire file, including later
animation frames; re-encoding produces a static image.

Resizing uses Triangle filtering and preserves 16-bit PNG channel precision until resizing is
complete. Image processing preserves stored orientation and does not automatically rotate pixels
or perform ICC color conversion.

Re-encoding retains EXIF, including orientation, and RGB ICC profiles unchanged. Other file metadata is
discarded. Unreadable optional metadata may be omitted; required image data must validate, and a
failure to write retained metadata makes encoding fail.

## Screenshot flow

1. Capture supplies desktop pixels and selects the width limit and JPEG quality.
2. Image processing preserves the captured orientation and color channels, reducing width only
   when necessary and retaining the aspect ratio.
3. Encode JPEG with 4:2:0 chroma sampling and quality constrained to 1 through 100.
4. Capture reports an encoding failure through its capture result. Remote screen view applies
   its own frame-size retries and availability rules.

## Resource and failure contract

Each operation has a 512 MiB working-memory limit, including decoded pixels, metadata and encoded
output. Malformed dimensions, truncated data and decompression beyond resource limits must fail
without unbounded allocation or non-terminating processing. Results own their encoded data and
remain valid independently of the caller's input buffer.

Failures distinguish unsupported content, corrupt images, resource limits and encoding errors.
Invalid operation parameters remain caller errors. Model adaptation maps an image failure to a
processing-failure or size-limit placeholder; one failed image must not discard other tool content.
