$ErrorActionPreference = 'Stop'
# Die drei Methoden werden bei JEDEM Test-Build aus dem aktuellen Quelltext
# gelesen. Kein gepflegtes Duplikat der Werkzeugrechnung als Messgeraet.
function Methode([string] $datei, [string] $signatur) {
    $inhalt = [IO.File]::ReadAllText((Join-Path $PSScriptRoot "..\..\Tools\$datei"))
    $anfang = $inhalt.IndexOf($signatur, [StringComparison]::Ordinal)
    if ($anfang -lt 0) { throw "Methode fehlt: $signatur" }
    $klammer = $inhalt.IndexOf('{', $anfang)
    $tiefe = 1
    $ende = $klammer + 1
    while ($tiefe -gt 0 -and $ende -lt $inhalt.Length) {
        if ($inhalt[$ende] -eq '{') { $tiefe++ }
        if ($inhalt[$ende] -eq '}') { $tiefe-- }
        $ende++
    }
    if ($tiefe -ne 0) { throw "Methodengrenze fehlt: $signatur" }
    return $inhalt.Substring($anfang, $ende - $anfang)
}
$kopf = @'
// Automatisch gelesene Methoden; Unity-Lebenszyklus und UI sind nicht geladen.
using System;
using System.Linq;
using System.Collections.Generic;
using Unity.Mathematics;
using ParkingLotTool.Geometry;
internal sealed class Werkzeugprobe {
    List<float2> _points;
    List<ParkingGeometry.RandzoningLinie> _randzoning;
    bool _closed = true;
    const float EntranceLineEpsilon = 0.0001f;
    sealed class UI { internal void SetStatus(string s) {} }
    UI _uiSystem = null;
    static string T(string de,string en) => de;
    internal static Entrance Zugang(float2[] f,Entrance alt,float2 pos,LayoutSettings e) {
        var w = new Werkzeugprobe { _points=f.ToList() };
        return w.TryReprojectEntrance(alt,pos,e,out var neu) ? neu : null;
    }
    internal static ParkingGeometry.RandzoningLinie[] Rand(float2[] f,ParkingGeometry.RandzoningLinie[] r) {
        var w = new Werkzeugprobe { _points=f.ToList(), _randzoning=r.ToList() };
        w.RandzoningFolgeDemUmriss();return w._randzoning.ToArray();
    }
    static class Mod { internal static readonly Logger log=new Logger(); }
    sealed class Logger { internal void Info(string s) {} }
'@
$methoden = @(
    (Methode 'ParkingLotEntranceMaintenance.cs' 'private bool TryReprojectEntrance('),
    (Methode 'ParkingLotEntrances.cs' 'private bool TryNormalEntranceBounds('),
    (Methode 'ParkingLotRandzoning.cs' 'private void RandzoningFolgeDemUmriss()')
)
$ordner = Join-Path $PSScriptRoot 'obj'
New-Item -ItemType Directory -Force -Path $ordner | Out-Null
[IO.File]::WriteAllText((Join-Path $ordner 'Werkzeugprobe.g.cs'), $kopf + "`n" + ($methoden -join "`n") + "`n}")
