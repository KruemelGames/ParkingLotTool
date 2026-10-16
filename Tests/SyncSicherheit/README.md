# Sync-Sicherheit: Register 1a

`pwsh -NoProfile -File Tests/SyncSicherheit/register-pflichtlaeufe.ps1`
startet die bisherigen Pflichtlaeufe einschliesslich Sprache, UI und
Speicherformate sowie die alten und neuen Sync-Mutationen. Nur Testprojekte
werden kompiliert; kein Mod-Build, kein Deploy und kein Spielstart.
Protokolle: `.codex-build/register-1a/`.
Einzellauf: `dotnet run -c Release --project Tests/SyncSicherheit/SyncSicherheit.csproj`.

Der produktive Registerkern kommt aus Geometry/Auftragsregister.cs.
Eingebunden sind auch der echte Weltadapter, die asynchrone Sync-Uebergabe,
Sync-Antwort/Datenstand, Fahrwegsteuerung, Fahrwegabschluss/-Identitaetspruefung
und Strassenannahme/-Abschluss. Hinzu kommen Bestandsannahme/Task-Lesen,
Bestandswerkzeugabschluss, Flaechenannahme/-Abschluss und die echten
Hintergrundannahme-, Gate-, Fehler-, Rueckweg- und Abschlussmethoden.
Native ECS-, Werkzeug- und UI-Zugriffe werden
durch Container ersetzt. Test-Main faengt alle Ausnahmen ab und liefert Exit 1.

Geprueft werden insbesondere:

- Annahme vor Start; falscher Worker und falsches Lot; verlorene Uebergabe.
- Synchrone Ablehnung und synchrone Antwort bei bereits bestehender Sync-Zuordnung.
- Alte Generation und alte Antwort an ein neues Lot derselben Entity-ID.
- Reentranter und nachtraeglicher Doppelabschluss, Workerfreigabe.
- Ersatz-Lot und gemeinsamer Nutzerfortschritt ueber interne Migrationen hinweg.
- Datenstand fehlt / 0 bis 15; Normalbau, Repair und Wiederaufnahme gegen Migration 14/15.
- Fehler vor dem Schreiben und Diagnosefehler nach bestaetigter Wirkung.
- Spaeter Normalbauabschluss nach Uebernahme durch eine Migration.
- Keine Bereinigung veralteter Fahrweg-/Bestands-Entity-IDs in der neuen Welt.
- Schritte 10/11/12/13 bei fehlendem Datenstand und allen Staenden 0..15.
- Alte Task-Ergebnisse, Gate-Rueckrufe und fremde aktuelle Auftragskennungen.
- Drei Bauversuche mit einer Kennung, bisherigen Pausen und einem Abschluss.
- Rueckweg, NeubauFaellig und OffenerErsatz; Fehler vor/nach der Uebernahme.
- Freigabe-/Diagnoseausnahmen; ein Rueckweg bestaetigt keine Migration.

Die sechs Registermutationen entfernen Generation, Abschlussbegrenzung,
Workerfreigabe, Annahmebestaetigung oder die Antwortidentitaet. Jede muss mit
Exit 1 an einer Testaussage scheitern; Compilerfehler gelten nicht als Treffer.
Die bestehenden Hardening-Mutationen bleiben aktiv; ihr Strassen-Ziel wurde
auf den neuen registrierten Rueckmeldeaufruf umgestellt.

Alle fuenf Worker benutzen das Register; die Entity-Rueckmeldung ohne
Kennung und der Dispatcher-Rueckfall ohne Register sind entfernt.
worker-mutationen.ps1 prueft zusaetzlich neun Fehler: falsche Bestandskennung,
altes Task-Ergebnis, fehlende Freigabe bei Bestand/Flaechen/Hintergrund,
fehlende Flaechengeneration, alter Gate-Rueckruf, vorzeitiger Abschluss einer
Wiederholung und Migrationserfolg durch einen blossen Rueckweg.
Historische Leser bleiben kompatibel; NebenarbeitOffen schreibt jetzt V2 und liest weiter V1.
Die native Aufrufreihenfolge beim Laden, Apply und Cohtml-Darstellung
brauchen weiterhin Ingame-Abnahme. Es gibt keine neuen Fristen oder Abbrueche.

## Speichersicherheit 9-11
nebenarbeit-mutationen.ps1 prueft 16 weitere Fehler gegen produktive
Herkunfts-/Schreibmethoden, Vor-Apply-Pruefung, Nachweis-Sicherung/-Laden,
Markerquittung, Nebenarbeits-Aufnahme/-Fehler und neue Speicherformate.
Abgedeckt: Nachbarkanten, Original/Cancel/Delete, falscher Seed, fremdes Schild
an gleicher Lage/Kante, mehrdeutige Herkunft, senkrechte Anschlussstuecke,
fehlender Nachweisteil, Edit-Uebernahme, Generationswechsel und werfender Logger.
Die echte Bushalt-Soll/Ist-Zuordnung prueft alle Positionen, auch nahe Punkte
und doppelte Schilder an nur einer Stelle.
Native Erzeugung, Temp-Umwandlung und Save-Entity-Remapping bleiben Ingame.
Der Pflichtlauf umfasst zusaetzlich Versorgungsabsturz und Versorgungsuebernahme.
