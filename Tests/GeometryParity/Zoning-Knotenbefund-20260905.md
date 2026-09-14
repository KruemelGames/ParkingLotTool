# Zoning-Knotenbefund vom 05.09.2026

Die Kreuzung fehlt bereits im damaligen Plan. Der gespeicherte Abzug
`ParkingLotTool-debug-20260905-015213-534.json`, `Preview.Layout.NetLine`,
enthaelt 5 Zoningkanten. Das Mod-Log meldet dazu 5 von 5 erzeugte Kurse,
5 Temp-Kanten und 2 Netze. Zwischen diesem Plan und Temp geht keine Kante
verloren. Der heutige Testplan mit 6 Kanten stammt aus neuerem Quelltext.

Die damalige DLL wurde laut Abzug um 01:48:16 gebaut. Vor dem heutigen
Build waren lokale Release-DLL und installierte DLL SHA-256-identisch:
`C48E7643114B794D9DE476D254B9CF52F3463F1B36656F8D95D62A40AD98DA17`.
Ihre dekompilierte Ringkonstruktion ruft `flaeche.Strassenring()` auf.
Im Arbeitsstand steht bereits `ZoningRingMitRandachse(...)` (Dateien von
01:59/02:01). Diese Aenderung war also noch nicht ausgeliefert.

Die alte Konstruktion entfernt die parallele Ringseite als Doppel, ohne
die Querarme aus der gemeinsamen Randachse zu konstruieren. Im exportierten
Float-Plan endet der Querarm 0,158 mm neben der Randachse. Die Verlaengerung
ignoriert Abstaende unter 1 mm; `ZellenSchneideStrassen` findet keinen
Segment-Schnitt und teilt die RZ-Kante nicht. Die alte Naehemessung meldete
trotzdem einen Zusammenhang. Der ungerundete Reihenwinkel ist entscheidend:
88,03525058 Grad reproduziert den Fehler, 88,04 Grad allein verdeckt ihn.

Mutation: Nur den Aufruf auf `Strassenring()` zurueckgesetzt, danach wieder
hergestellt. 01:51 ergibt 8 Kanten, 1 Netz, 4 Strassenueberlappungen;
01:52 ergibt 5 Kanten, 2 Netze, 0 Kreuzungen, 1 Strassenueberlappung.
Der Zoningnetz-Lauf wird rot (Exit 1, 3 fehlgeschlagene Varianten).

Mit gemeinsamer Achskonstruktion ergeben beide Baue, jeweils mit gerundetem
und ungerundetem Winkel: 6 Kanten, 1 Netz, 1 exakte Kreuzung,
0 Strassenueberlappungen, kuerzeste Kante 16 m. Die schon vorhandene
Korrektur wurde beibehalten. Neu sind die ungerundeten Zufahrtspositionen
aus den Debug-Abzuegen im Test, Pruefungen auf exakte Kreuzung und mindestens
1 m Kantenlaenge sowie die Modul-ID der geladenen Assembly im Mod-Log.

Alle 5 Pflichtlaeufe bestanden: 17 bekannte Meldungen ohne neue Abweichung;
Zoningflaeche, Zoningnetz und Ueberlappung sauber; 300000 Rastfaelle mit
0 Fehlern und 900000 stabile Folgebilder. Logs: `artifacts/zoning-release/`.

Offen: C# Release kompiliert mit 2 Warnungen, aber der Postprozessor bricht
mit Code -1 ab: benutzerspezifisches `CSII_UNITYVERSION` ist in dieser
Umgebung leer. Der Build verwendete ein Ausgabeziel im Workspace, da der
Spiel-Modordner hier nicht beschreibbar ist. Keine neue Version ausgeliefert.
Die Abnahme nach Apply im selben Nutzerbau (1 Netz und 0 Strasse-auf-Strasse)
ist damit noch nicht gemessen. Kein Commit, keine neue Bausperre.
