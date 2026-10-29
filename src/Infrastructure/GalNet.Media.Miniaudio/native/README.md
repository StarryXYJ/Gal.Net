# Vendored miniaudio

`miniaudio.h`: https://github.com/mackron/miniaudio/blob/0.11.23/miniaudio.h

SHA256: `7e4f3f13c8fe66df2080ac3dd12a89193e3c2463cb7f067c798abd7331cd8ee6`.

The upstream Public Domain / MIT No Attribution license is included at the end of the header. The wrapper is GalNet code. Windows builds use MSVC x64 with the static CRT; the generated DLL is copied transitively to output/publish. End users do not need a compiler. Only Windows x64 is currently supported by this adapter. Decoder support: WAV, MP3, Ogg Vorbis, FLAC. Unsupported formats fail explicitly.

`stb_vorbis.c` comes from https://github.com/nothings/stb/blob/f0569113c93ad095470c54bf34a17b36646bbbb5/stb_vorbis.c (Public Domain / MIT license included in the source). SHA256: `4c7cb2ff1f7011e9d67950446b7eb9ca044f2e464d76bfbb0b84dd2e23e65636`.
