#!/usr/bin/env python3
"""Inspect the actual mobile payload; building an APK alone is not runtime acceptance."""
import argparse
import hashlib
import json
from pathlib import Path
import struct
import zipfile


def digest(data):
    return hashlib.sha256(data).hexdigest()


def elf(data):
    if data[:4] != b"\x7fELF" or data[5] != 1:
        raise ValueError("Expected a little-endian ELF library")
    wide = data[4] == 2
    machine = struct.unpack_from("<H", data, 18)[0]
    phoff = struct.unpack_from("<Q" if wide else "<I", data, 32 if wide else 28)[0]
    size, count = struct.unpack_from("<HH", data, 54 if wide else 42)
    alignments = []
    for index in range(count):
        pos = phoff + index * size
        if struct.unpack_from("<I", data, pos)[0] == 1:
            alignment = struct.unpack_from("<Q" if wide else "<I", data, pos + (48 if wide else 28))[0]
            if alignment < 16384:
                raise ValueError(f"ELF LOAD alignment {alignment} is below 16 KB")
            alignments.append(alignment)
    if not alignments:
        raise ValueError("No ELF LOAD segments")
    return {"machine": machine, "loadAlignments": alignments, "sha256": digest(data)}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("apk", type=Path)
    parser.add_argument("content", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--abi", action="append", choices=("arm64-v8a", "x86_64"))
    args = parser.parse_args()
    report = {"apk": str(args.apk), "apkSha256": digest(args.apk.read_bytes()), "deviceTested": False, "native": {}}
    with zipfile.ZipFile(args.apk) as package:
        names = package.namelist()
        embedded = package.read("assets/game.galpak")
        if embedded != args.content.read_bytes():
            raise ValueError("Embedded game package differs from the verified export")
        report["contentSha256"] = digest(embedded)
        expected_abis = set(args.abi or ("arm64-v8a", "x86_64"))
        actual_abis = {name.split("/")[1] for name in names if name.startswith("lib/") and name.endswith(".so")}
        if actual_abis != expected_abis:
            raise ValueError(f"Expected ABIs {sorted(expected_abis)}, found {sorted(actual_abis)}")
        report["size"] = {
            "apkBytes": args.apk.stat().st_size,
            "uncompressedBytes": sum(info.file_size for info in package.infolist()),
            "nativeBytesByAbi": {
                abi: sum(info.file_size for info in package.infolist() if info.filename.startswith(f"lib/{abi}/"))
                for abi in sorted(expected_abis)
            },
        }
        for abi in sorted(expected_abis):
            machine = {"arm64-v8a": 183, "x86_64": 62}[abi]
            libs = [name for name in names if name.startswith(f"lib/{abi}/") and name.endswith(".so")]
            for required in ("libgalnet_miniaudio.so", "libvlc.so", "libc++_shared.so"):
                if f"lib/{abi}/{required}" not in libs:
                    raise ValueError(f"Missing {abi}/{required}")
            aot = [name for name in libs if "/libaot-" in name]
            if not any("GalNet.Sample.CrossPlatform.Android.dll" in name for name in aot):
                raise ValueError(f"Missing Mono AOT host for {abi}")
            for name in libs:
                if Path(name).name.lower().startswith("libsdl"):
                    raise ValueError("Desktop SDL must not enter the mobile package")
                info = elf(package.read(name))
                if info["machine"] != machine:
                    raise ValueError(f"Wrong architecture: {name}")
                report["native"][name] = info
            report.setdefault("aotCounts", {})[abi] = len(aot)
    args.output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({key: value for key, value in report.items() if key != "native"}, indent=2))


if __name__ == "__main__":
    main()
