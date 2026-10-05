// braw-decode for Windows — minimal Blackmagic RAW decoder for the Offload proxy pipeline (same CLI as the Mac tool).
//
//   braw-decode --info [--scale S] clip.braw
//       one JSON line: width, height (processed image at scale S), sourceWidth, sourceHeight, fps, frames,
//       timecode, camera, and when the SDK reports them gamma, gamut, colorScienceGen
//   braw-decode [--scale S] [--start N] [--count N] clip.braw
//       raw interleaved RGB 16-bit little endian frames on stdout (ffmpeg: -f rawvideo -pix_fmt rgb48le)
//
//   S = full | half | quarter | eighth
//
// Exit codes: 0 OK, 2 usage, 3 library/codec missing, 4 clip cannot be opened, 5 a frame failed, 6 stdout closed.
// Frames come out in the clip's own gamma/gamut; the LUT is applied later by ffmpeg.
// BlackmagicRawAPI.dll is loaded at runtime (Offload.Core.BrawLibrary: next to this exe, SDK, Player, Resolve).

using Offload.BrawDecode;

// Binary stdout over the raw handle: a closed pipe is an error the decoder sees (never Console.Out / OpenStandardOutput).
using var stdout = BinaryStdout.Open();
return Decoder.Run(args, stdout, Console.Error);
