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
und Strassenannahme/-Abschluss. Native ECS-, Werkzeug- und UI-Zugriffe werden
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
- Keine Bereinigung veralteter Fahrweg-Entity-IDs in der neuen Welt.

Die sechs Registermutationen entfernen Generation, Abschlussbegrenzung,
Workerfreigabe, Annahmebestaetigung oder die Antwortidentitaet. Jede muss mit
Exit 1 an einer Testaussage scheitern; Compilerfehler gelten nicht als Treffer.
Die bestehenden Hardening-Mutationen bleiben aktiv; ihr Strassen-Ziel wurde
auf den neuen registrierten Rueckmeldeaufruf umgestellt.

Offen bleiben die Adapter fuer Bestands-, Flaechen- und Hintergrundarbeit.
Deren Task-Ergebnisse, Gate-Callbacks und Wiederholungen haben hier noch
keine Registerkennung. Nur der Wiederaufnahme-Zeitpunkt des Hintergrunds
liegt jetzt hinter der nativen Pufferwiederherstellung.
Die native Aufrufreihenfolge beim Laden, Apply und Cohtml-Darstellung
brauchen weiterhin Ingame-Abnahme. Es gibt keine neuen Fristen oder Abbrueche.
