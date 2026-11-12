"""
Compiles the project's own scripts outside the Unity editor, for a quick check when the editor is busy or closed.

    py -3 -I Tools/check_compile.py

Takes the csproj files Unity generated (for their defines and references), swaps the explicit file list for a glob
over the script folders (so new files are included), points project references at Library/ScriptAssemblies, and runs
`dotnet build`. Output goes to Temp/, which Unity ignores. Not a substitute for a Unity compile (no IL post-processing).
"""
import os
import re
import subprocess
import sys

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
TARGETS = [
    ("SurgicalFoundations.Runtime.csproj", "zz_check_runtime.csproj", "Assets\\_Project\\Scripts\\Runtime\\**\\*.cs", False),
    ("SurgicalFoundations.Editor.csproj", "zz_check_editor.csproj", "Assets\\_Project\\Scripts\\Editor\\**\\*.cs", True),
]


def make(src, dst, glob, uses_checked_runtime):
    s = open(os.path.join(ROOT, src), encoding="utf-8-sig").read()
    s = re.sub(r'\s*<Compile Include="[^"]*" />', "", s)
    refs = re.findall(r'<ProjectReference Include="([^"]+)\.csproj"', s)
    s = re.sub(r'<ProjectReference Include="[^"]+">.*?</ProjectReference>', "", s, flags=re.S)
    s = re.sub(r'<ProjectReference Include="[^"]+" />', "", s)
    items = ""
    for r in refs:
        path = "Library\\ScriptAssemblies\\" + r + ".dll"
        if uses_checked_runtime and r == "SurgicalFoundations.Runtime":
            path = "Temp\\bincheck\\zz_check_runtime\\SurgicalFoundations.Runtime.dll"  # the one just built
        items += '\n    <Reference Include="%s"><HintPath>%s</HintPath><Private>false</Private></Reference>' % (r, path)
    s = s.replace("</Project>", '  <ItemGroup>\n    <Compile Include="%s" />%s\n  </ItemGroup>\n</Project>' % (glob, items))
    name = dst[:-7]
    s = s.replace("Temp\\obj\\$(MSBuildProjectName)", "Temp\\objcheck\\" + name)
    s = s.replace("Temp\\bin\\Debug\\", "Temp\\bincheck\\" + name + "\\")
    open(os.path.join(ROOT, dst), "w", encoding="utf-8").write(s)


def main():
    bad = 0
    for src, dst, glob, dep in TARGETS:
        make(src, dst, glob, dep)
        p = subprocess.run(["dotnet", "build", dst, "-nologo", "-v", "q"], cwd=ROOT, capture_output=True, text=True)
        lines = sorted({re.sub(r"\s*\[.*$", "", l) for l in p.stdout.splitlines() if ": error " in l})
        print("%s: %s" % (src, "OK" if not lines and p.returncode == 0 else "%d error(s)" % len(lines)))
        for l in lines:
            print("  " + l.replace(ROOT + os.sep, ""))
        if p.returncode != 0 and not lines:
            print(p.stdout[-1500:])
        bad += len(lines) or (p.returncode != 0)
        os.remove(os.path.join(ROOT, dst))
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
