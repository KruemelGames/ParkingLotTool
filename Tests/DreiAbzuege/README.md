# Drei Abzuege: isolierter Messstand

Produktionsdateien werden nur gelesen. `Instrumentiere.py` kopiert `Geometry/`
nach `Kopie/` und fuegt dort Messausgaben sowie den begrenzten Entwurf ein.
`Kopie/`, `bin/` und `obj/` sind erzeugte Dateien. Der Entwurf gilt nur fuer die
hier vermessene Folge: buendiges Praefix, danach mindestens zwei versetzte
Enden. Er ist kein allgemeiner Ersatz fuer RinglosEndwege.

Aus dem Repository-Hauptverzeichnis, PowerShell:

```powershell
python Tests\DreiAbzuege\Instrumentiere.py
$env:PLT_DREI_ENTWURF = '0'
$env:PLT_DREI_MUTATION = ''
dotnet run -c Release --project Tests\DreiAbzuege\DreiAbzuege.csproj
$env:PLT_DREI_ENTWURF = '1'
dotnet run -c Release --no-build --project Tests\DreiAbzuege\DreiAbzuege.csproj
$env:PLT_DREI_MUTATION = 'eck'
dotnet run -c Release --no-build --project Tests\DreiAbzuege\DreiAbzuege.csproj
# Muss Exitcode 1 liefern: Ecke in der Fahrgasse.
$env:PLT_DREI_MUTATION = 'stummel'
dotnet run -c Release --no-build --project Tests\DreiAbzuege\DreiAbzuege.csproj
# Muss Exitcode 1 liefern: Diagonalstreifen fehlt.
Remove-Item Env:PLT_DREI_ENTWURF, Env:PLT_DREI_MUTATION
```

Die Originaldateien werden aus `$env:USERPROFILE\AppData\LocalLow\Colossal Order\Cities Skylines II\Logs` gelesen.
`original-eingaben.json` archiviert die vollstaendigen Eingaben und SHA256 der
Originaldateien. Keine von Hand gerundeten Polygonkoordinaten.

`messung.txt` ist der Produktionsstand, `entwurf.txt` der begrenzte Entwurf,
`mutation-*.txt` sind die Gegenproben. Die uebrigen Laufprotokolle und
`pflicht-ergebnisse.json` gelten ausschliesslich fuer den unveraenderten
Produktionsstand. Fuer ihre Reproduktion erst GeometryParity in Release
laufen lassen, dann `python Tests\DreiAbzuege\Pflichtlaeufe.py`.
Es wird kein Hauptprojekt gebaut und nichts ausgeliefert.
