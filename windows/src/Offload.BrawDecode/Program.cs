// braw-decode for Windows — minimal Blackmagic RAW decoder for the Offload proxy pipeline (same CLI as the Mac tool).
//
//   braw-decode --info [--gen5] [--scale S] clip.braw
//       one JSON line: width, height (processed image at scale S), sourceWidth, sourceHeight, fps, frames,
//       timecode, camera, and when the SDK reports them gamma, gamut, colorScienceGen (with --gen5 the effective
//       values and colorScienceGenOriginal)
//   braw-decode [--gen5] [--scale S] [--start N] [--count N] clip.braw
//       raw interleaved RGB 16-bit little endian frames on stdout (ffmpeg: -f rawvideo -pix_fmt rgb48le)
//
//   S = full | half | quarter | eighth
//   --gen5: a Film Gen 4 clip is decoded with colour science Gen 5 (ověří až vzorek .braw Gen 4); when the SDK does
//       not take it, a warning on stderr and the clip is decoded as recorded (exit code unchanged)
//
// Exit codes: 0 OK (also when the reader closed the pipe early), 2 usage, 3 library/codec missing,
// 4 clip cannot be opened, 5 a frame failed.
// Frames come out in the clip's own gamma/gamut; the LUT is applied later by ffmpeg.
// BlackmagicRawAPI.dll is loaded at runtime (Offload.Core.BrawLibrary: next to this exe, SDK, Player, Resolve).

using Offload.BrawDecode;

// Binary stdout over the raw handle: a closed pipe is an error the decoder sees (never Console.Out / OpenStandardOutput).
using var stdout = BinaryStdout.Open();
// stderr in UTF-8 like the Mac tool (Offload's ToolProcess reads it so), not the console's OEM code page: a clip path
// with diacritics must come back intact in the error message.
using var stderr = new StreamWriter(Console.OpenStandardError(), new System.Text.UTF8Encoding(false)) { AutoFlush = true };
return Decoder.Run(args, stdout, stderr);
