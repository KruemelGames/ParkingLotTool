from pathlib import Path
import subprocess

root = Path(__file__).resolve().parent.parent
cases = [
    ("abstand", "Geometry/Zellen/Ringbandplanung.cs",
     "foreach (var rz in randzoning ?? Array.Empty<Randzoningabschnitt>())",
     "foreach (var rz in Array.Empty<Randzoningabschnitt>())", "rzanschluss"),
    ("zettelmessung", "Geometry/ZoningGassenabstand.cs",
     'if (r.Kind != "zoning") continue;', 'if (r.Kind != "mutiert") continue;', "rzanschluss"),
    ("rastergrenze", "Geometry/Zellen/Layout.cs",
     'fragmente = TeileAlle(fragmente, teiler,\n                    linienregister.FreieGerade(a, b, Linienart.Zoningkante, "RZ-Abschnitt"));',
     'fragmente = TeileAlleSegment(fragmente, teiler,\n                    linienregister.FreieGerade(a, b, Linienart.Zoningkante, "RZ-Abschnitt"), a, b);', "rzstufen")]
for name, rel, old, new, test in cases:
    path = root / rel
    original = path.read_bytes()
    text = original.decode("utf-8-sig").replace("\r\n", "\n")
    assert text.count(old) == 1, name
    try:
        path.write_text(text.replace(old, new), encoding="utf-8")
        result = subprocess.run(["dotnet", "run", "-c", "Release", "--project", "Tests/GeometryParity/CSharp/GeometryParity.csproj", "--", "--" + test], cwd=root, capture_output=True, text=True)
        (root / "artifacts/drei" / ("mutation-" + name + ".txt")).write_text(result.stdout + result.stderr, encoding="utf-8")
        assert result.returncode == 1 and "FEHLER:" in result.stdout, name + " wurde nicht erkannt"
        line = "Mutation " + name + ": Exit 1, vom unverringerten Test erkannt; Original wiederhergestellt."
        print(line, flush=True)
    finally:
        path.write_bytes(original)
    with (root / "bericht-drei.md").open("a", encoding="utf-8") as report:
        report.write("\n" + line + "\n")

