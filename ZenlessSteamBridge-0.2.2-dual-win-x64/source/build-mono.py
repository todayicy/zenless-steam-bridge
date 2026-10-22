#!/usr/bin/env python3
"""Cross-compile the Windows .NET Framework EXEs using Mono, preserving UAC manifests."""
import argparse
import os
from pathlib import Path
import shutil
import struct
import subprocess
import xml.etree.ElementTree as ET


def manifest_resource(data):
    def header(size, resource_type, name, flags, language):
        return struct.pack("<IIHHHHIHHII", size, 32, 0xFFFF, resource_type,
                           0xFFFF, name, 0, flags, language, 0, 0)
    return (header(0, 0, 0, 0, 0) + header(len(data), 24, 1, 0x30, 0x409)
            + data + b"\0" * (-len(data) % 4))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mono", default=shutil.which("mono"))
    parser.add_argument("--compiler", help="Path to mcs.exe; defaults to mcs on PATH")
    parser.add_argument("--config", help="Mono runtime config, for relocatable toolchains")
    parser.add_argument("--framework-reference-dir", default=os.environ.get("NET48_REFERENCE_DIR"), help="Microsoft .NET Framework 4.8 reference directory; required to prevent Mono-only API calls")
    parser.add_argument("--test", action="store_true")
    args = parser.parse_args()
    if not args.framework_reference_dir:
        parser.error("Provide --framework-reference-dir (or NET48_REFERENCE_DIR) from Microsoft.NETFramework.ReferenceAssemblies.net48. See README.md.")
    references = Path(args.framework_reference_dir).resolve()
    framework_list = references / "RedistList" / "FrameworkList.xml"
    if not framework_list.is_file() or ET.parse(framework_list).getroot().get("Name") != ".NET Framework 4.8":
        parser.error("The reference directory must contain Microsoft's .NET Framework 4.8 RedistList/FrameworkList.xml")
    libraries = ["mscorlib", "System", "System.Core", "System.Windows.Forms", "System.Drawing", "System.Web.Extensions", "System.Management"]
    for library in libraries:
        if not (references / (library + ".dll")).is_file():
            parser.error("Missing Framework reference assembly: " + library)
    runner = [args.mono] if args.mono else []
    if args.config:
        runner += ["--config", str(Path(args.config).resolve())]
    compiler = runner + [str(Path(args.compiler).resolve())] if args.compiler else [shutil.which("mcs") or "mcs"]
    root = Path(__file__).resolve().parent
    dist, obj = root / "dist", root / "obj" / "mono"
    dist.mkdir(exist_ok=True)
    obj.mkdir(parents=True, exist_ok=True)
    sources = sorted(map(str, (root / "src").glob("*.cs")))
    common = ["-nologo", "-noconfig", "-nostdlib+", "-platform:x64", "-optimize+", "-codepage:utf8"]
    common += ["-r:" + str(references / (library + ".dll")) for library in libraries]
    common += ["-resource:" + str(root / "assets" / "zenless.png") + ",ZenlessSteamBridge.ZenlessArt", "-resource:" + str(root / "assets" / "starrail.png") + ",ZenlessSteamBridge.StarRailArt"]
    license_arg = "-resource:" + str(root / "LICENSE") + ",ZenlessSteamBridge.License"
    for name, manifest, extra in (
        ("ZenlessBridge.exe", "bridge.manifest", ["-define:BRIDGE_FRONTEND"]),
        ("ZenlessSteamBridge.exe", "app.manifest", ["-resource:" + str(dist / "ZenlessBridge.exe") + ",ZenlessSteamBridge.Frontend"]),
    ):
        resource = obj / (manifest + ".res")
        resource.write_bytes(manifest_resource((root / manifest).read_bytes()))
        subprocess.run(compiler + common + ["-target:winexe", "-win32res:" + str(resource),
                       license_arg, "-out:" + str(dist / name)] + extra + sources, check=True)
        print("Built", dist / name, flush=True)
    if args.test:
        if not runner:
            parser.error("--test requires mono on PATH or --mono")
        tests = dist / "CoreTests.exe"
        subprocess.run(compiler + common + ["-target:exe", "-main:ZenlessSteamBridge.CoreTests",
                       "-out:" + str(tests)] + sources + sorted(map(str, (root / "tests").glob("*.cs"))), check=True)
        for executable in [dist / "ZenlessSteamBridge.exe", dist / "ZenlessBridge.exe"]:
            subprocess.run(runner + [str(tests), "--api-compat", str(executable)], check=True)
        subprocess.run(runner + [str(tests)], check=True)
        subprocess.run(runner + [str(tests), "--payload", str(dist / "ZenlessSteamBridge.exe"), str(dist / "ZenlessBridge.exe")], check=True)


if __name__ == "__main__":
    main()
