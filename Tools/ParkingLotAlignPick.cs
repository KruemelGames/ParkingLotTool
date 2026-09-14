using System.Collections.Generic;
using Unity.Mathematics;
using static ParkingLotTool.Tools.ParkingLotTexte;

namespace ParkingLotTool.Tools
{
    /**
     * „An Polygonlinie ausrichten" - die Bezugsrichtung von Hand waehlen.
     *
     * Normalerweise holt sich jede Teilflaeche ihre eigene laengste Kante als
     * Nullpunkt. Wer eine Linie waehlt, setzt stattdessen EINE Richtung fuer
     * den ganzen Parkplatz; `Kante` heisst dann `Normal`, `Quer` steht wie
     * gehabt senkrecht dazu, und `Fest` faellt weg - die beiden sind
     * entweder/oder.
     *
     * ZWEI ENTSCHEIDUNGEN DES NUTZERS, die den Aufbau bestimmen:
     *
     *   - Der Modus wird erst aktiv, wenn wirklich eine Linie gewaehlt wurde.
     *     Bricht man die Auswahl ab, bleibt alles, wie es war. Deshalb aendert
     *     `BeginAusrichtWahl` NICHTS ausser dem Wartezustand.
     *   - Gemerkt wird die RICHTUNG, nicht die Kantennummer. Verschwindet die
     *     Kante, bleibt der Winkel bestehen, bis der Nutzer ihn selbst
     *     verwirft. Eine Nummer haette das nicht ueberlebt - genau daran sind
     *     in diesem Projekt schon die Zugaenge gescheitert.
     */
    public sealed partial class ParkingLotToolSystem
    {
        /**
         * Der Ablauf hat mehrere Runden: Flaeche, Linie, wieder Flaeche ...
         * bis der Nutzer beendet. Bei nur EINER Teilflaeche entfaellt die
         * Flaechenwahl - dann gibt es nichts zu unterscheiden.
         */
        internal enum Ausrichtschritt { Aus, Trennen, Flaeche, Linie }

        private Ausrichtschritt _ausrichtwahl = Ausrichtschritt.Aus;

        /**
         * In welchem Schritt das Ausrichten steht - UND WAS DIE OBERFLAECHE
         * DAVON ZEIGT. Beides in einem, absichtlich.
         *
         * SOFTLOCK AM 2026-09-01. Der Nutzer: *"Ich habe gerade ein Problem,
         * dass ich 'done splitting' nicht ausbekomme - irgendwie habe ich das
         * softlocked."*
         *
         * Vorher hingen zwei Anzeigen an diesem Zustand, und jede Stelle
         * musste sie von Hand nachfuehren. `BestaetigeAusrichtWahl` fuehrte
         * nur eine der beiden nach: der Schritt sprang auf `Aus`, die
         * Trennmodus-Anzeige blieb an. Der Knopf hiess weiter "Trennung
         * fertig", und sein Aufruf `BeendeTrennmodus` steigt bei
         * `!TrennmodusAktiv` sofort wieder aus - er tat also nichts. Esc half
         * ebensowenig, weil `AbortAusrichtWahl` bei `!AusrichtWahlAktiv`
         * genauso aussteigt. Kein Weg mehr heraus ausser Werkzeug zu.
         *
         * Der Fehler war nicht die vergessene Zeile, sondern dass es sie
         * geben konnte. Beide Anzeigen sind jetzt ABGELEITET: wer den Schritt
         * setzt, setzt sie mit, und keine Stelle kann es mehr vergessen.
         */
        internal Ausrichtschritt Ausrichtwahl
        {
            get => _ausrichtwahl;
            private set
            {
                _ausrichtwahl = value;
                _uiSystem?.SetAusrichtWahl(value != Ausrichtschritt.Aus);
                _uiSystem?.SetTrennmodus(value == Ausrichtschritt.Trennen);
            }
        }

        /** Wartet das Werkzeug gerade auf eine Auswahl? */
        internal bool AusrichtWahlAktiv => Ausrichtwahl != Ausrichtschritt.Aus;

        /** Die Teilflaeche, fuer die gerade eine Linie gesucht wird. */
        internal int AusrichtFlaeche { get; private set; } = -1;

        /**
         * Eine Zuweisung: Winkel plus der Punkt, an dem sie haengt.
         *
         * Der Punkt ist das Merkmal, nicht die Nummer der Teilflaeche - die
         * Zerlegung wird bei jeder Polygonaenderung neu gerechnet, und eine
         * Nummer haette dieselbe Falle wie die Kantennummer bei den
         * Zugaengen.
         */
        internal sealed class Ausrichtzuweisung
        {
            /** Der Punkt in der Teilflaeche, an dem die Zuweisung haengt. */
            internal float2 Anker;
            /** Die beiden Enden der gewaehlten Linie, zuletzt gesehen. */
            internal float2 LinieA;
            internal float2 LinieB;
            /** Der zuletzt gueltige Winkel - der Rueckfall. */
            internal double Winkel;
        }

        /**
         * DIE LINIE FUEHRT, NICHT DIE ZAHL.
         *
         * Ansage des Nutzers: *"Wenn eine Linie einen anderen Winkel bekommt
         * durch das Bewegen der Polygonpunkte, muss der neue Winkel genommen
         * werden."* Gleichzeitig gilt weiter: verschwindet die Linie, bleibt
         * der letzte Winkel stehen, bis er selbst verworfen wird.
         *
         * Beides zusammen heisst: gespeichert wird die LINIE, und der Winkel
         * ist nur ihr Rueckfall.
         *
         * Wiedergefunden wird sie ohne Metergrenze: zu jedem gemerkten Ende
         * wird der NAECHSTE heutige Polygonpunkt gesucht. Sind die beiden
         * verschieden und benachbart, ist das die Linie - dann zaehlt ihr
         * aktueller Winkel, und die gemerkten Enden werden nachgezogen.
         * Sind sie es nicht, gibt es die Kante nicht mehr, und der letzte
         * Winkel bleibt. Eine geratene Toleranz braucht es dafuer nicht.
         */
        private void AktualisiereAusrichtungen()
        {
            if (_ausrichtungen.Count == 0 || _points.Count < 2) return;
            foreach (var zuweisung in _ausrichtungen)
            {
                var a = NaechsterPunkt(zuweisung.LinieA);
                var b = NaechsterPunkt(zuweisung.LinieB);
                if (a < 0 || b < 0 || a == b) continue;
                var benachbart = (a + 1) % _points.Count == b
                    || (b + 1) % _points.Count == a;
                if (!benachbart) continue;

                // Die Reihenfolge festhalten, sonst kippt der Winkel um 180
                // Grad, sobald die zwei Punkte die Rollen tauschen.
                var von = (a + 1) % _points.Count == b ? _points[a] : _points[b];
                var nach = (a + 1) % _points.Count == b ? _points[b] : _points[a];
                var richtung = nach - von;
                if (math.lengthsq(richtung) < 1e-6f) continue;

                zuweisung.LinieA = von;
                zuweisung.LinieB = nach;
                zuweisung.Winkel = math.degrees(
                    math.atan2(richtung.y, richtung.x));
            }
        }

        private int NaechsterPunkt(float2 gesucht)
        {
            var beste = -1;
            var abstand = float.MaxValue;
            for (var i = 0; i < _points.Count; i++)
            {
                var d = math.distancesq(_points[i], gesucht);
                if (d >= abstand) continue;
                abstand = d;
                beste = i;
            }
            return beste;
        }

        private readonly List<Ausrichtzuweisung> _ausrichtungen
            = new List<Ausrichtzuweisung>();

        internal IReadOnlyList<Ausrichtzuweisung> Ausrichtungen => _ausrichtungen;

        /** Setzt den ganzen Satz - fuer Rueckgaengig und den Bauzettel. */
        internal void SetzeAusrichtungen(IReadOnlyList<Ausrichtzuweisung> satz)
        {
            _ausrichtungen.Clear();
            if (satz != null)
                foreach (var z in satz)
                    _ausrichtungen.Add(new Ausrichtzuweisung
                    {
                        Anker = z.Anker,
                        LinieA = z.LinieA,
                        LinieB = z.LinieB,
                        Winkel = z.Winkel,
                    });
            _uiSystem?.SetAusrichtwinkel(Ausrichtwinkel);
        }

        /**
         * Ein einzelner Winkel ohne Flaechenbezug - so liest der Bauzettel
         * der Fassung 2 seine Ausrichtung zurueck. Er kennt nur EINEN Wert;
         * er wird zur Vorgabe fuer alle Teilflaechen.
         */
        internal void SetzeAusrichtwinkel(double? grad,
            float2 linieA = default, float2 linieB = default)
        {
            _ausrichtungen.Clear();
            if (grad.HasValue)
                _ausrichtungen.Add(new Ausrichtzuweisung
                {
                    Anker = float2.zero,
                    LinieA = linieA,
                    LinieB = linieB,
                    Winkel = grad.Value,
                });
            _uiSystem?.SetAusrichtwinkel(Ausrichtwinkel);
        }

        /**
         * Die Vorgabe fuer alle nicht eigens zugewiesenen Teilflaechen: die
         * ERSTE Zuweisung. So gewollt - "die anderen Teilflaechen orientieren
         * sich an der, ausser sie werden extra nochmal ausgewaehlt".
         */
        internal double? Ausrichtwinkel
            => _ausrichtungen.Count == 0 ? (double?)null : _ausrichtungen[0].Winkel;

        internal void BeginAusrichtWahl()
        {
            /*
             * ENTWEDER ODER, und zwar in BEIDE Richtungen.
             *
             * Zoning schaltete den Zugangsmodus schon aus, aber nicht
             * umgekehrt - und das Ausrichten gar nicht. Zwei leuchtende
             * Modusknoepfe sind genau die Doppeldeutigkeit, die den Nutzer
             * heute schon einmal festgesetzt hat, und die Klicks bekaeme
             * ohnehin nur einer von beiden: `HandleAusrichtWahl` steht in
             * `OnUpdate` vor `HandleZoning`.
             */
            SetzeZoningModus(false);
            if (!_closed || _points.Count < 3)
            {
                _uiSystem?.SetStatus(T(
                    "Erst das Polygon schließen, dann eine Linie wählen.",
                    "Close the outline first, then pick a line."));
                return;
            }
            /*
             * ZUERST DER TRENNMODUS, wenn die Form ihn zulaesst.
             *
             * Ansage des Nutzers vom 2026-09-01: der Ausrichtknopf wird zum
             * "Trennung fertig", man KANN Schnitte ziehen, muss aber nicht.
             * Erst danach beginnt das eigentliche Ausrichten.
             *
             * Zulassen heisst: es muss ueberhaupt ein Schnitt moeglich sein.
             * Die beiden Punkte duerfen nicht benachbart sein, also braucht
             * es mindestens vier. Der Nutzer nannte "mehr als 5", sagte aber
             * im selben Atemzug, ein Viereck ginge auch und sein Beispiel
             * hatte fuenf - deshalb die Grenze dort, wo ein Schnitt
             * geometrisch moeglich wird, statt an einer gesetzten Zahl.
             */
            _trennAnfang = -1;
            AktualisiereTeilflaechen();
            if (TrennungMoeglich)
            {
                Ausrichtwahl = Ausrichtschritt.Trennen;
                AusrichtFlaeche = -1;
                _uiSystem?.SetStatus(T(
                    "Trennmodus: zwei Polygonpunkte verbinden, oder gleich "
                        + "„Trennung fertig“.",
                    "Split mode: connect two outline points, or just press "
                        + "Done splitting."));
                Mod.log.Info("PLT-Trennmodus: gestartet, " + _points.Count
                    + " Punkte, " + _trennschnitte.Count
                    + " vorhandene Schnitt(e).");
                return;
            }
            // Bei einer einzigen Teilflaeche gibt es nichts auszuwaehlen -
            // Rechteck, Dreieck, Raute. Dann geht es direkt zur Linie.
            AusrichtFlaeche = _teilflaechen.Count > 1 ? -1 : 0;
            Ausrichtwahl = _teilflaechen.Count > 1
                ? Ausrichtschritt.Flaeche : Ausrichtschritt.Linie;
            Mod.log.Info("PLT-Ausrichten: Auswahl gestartet, "
                + _teilflaechen.Count + " Teilflaeche(n); "
                + (_teilflaechen.Count > 1
                    ? "erst Flaeche waehlen." : "direkt zur Linie."));
        }

        internal void AbortAusrichtWahl(string grund)
        {
            if (!AusrichtWahlAktiv) return;
            Ausrichtwahl = Ausrichtschritt.Aus;
            AusrichtFlaeche = -1;
            _trennAnfang = -1;
            /*
             * Abbrechen laesst den vorigen Zustand voellig unberuehrt - auch
             * eine frueher gewaehlte Linie bleibt. Der Nutzer: "Align wird
             * auch erst aktiv wenn ich eine Linie ausgewaehlt habe, ansonsten
             * bleibt der vorherige Modi aktiv."
             */
            _uiSystem?.SetStatus(T("Auswahl abgebrochen.", "Selection cancelled."));
            Mod.log.Info("PLT-Ausrichten: Auswahl abgebrochen (" + grund + ").");
        }

        /**
         * „Ausrichtung bestaetigen" - der Knopf neben dem Zuruecksetzen.
         *
         * Er verwirft nichts und uebernimmt nichts: jede gewaehlte Linie ist
         * in dem Moment schon uebernommen, in dem man sie anklickt. Er beendet
         * nur den Wartezustand. Genau deshalb gibt es ihn - bis zum
         * 2026-09-01 kam man aus der Auswahl NUR per Rechtsklick oder Esc
         * heraus, und das muss man erst einmal wissen. Der Nutzer: *"Wir haben
         * kein wirkliches Align bestaetigen, damit die Preview wieder
         * angezeigt wird."*
         */
        internal void BestaetigeAusrichtWahl()
        {
            if (!AusrichtWahlAktiv) return;
            var zuweisungen = _ausrichtungen.Count;
            Ausrichtwahl = Ausrichtschritt.Aus;
            AusrichtFlaeche = -1;
            _uiSystem?.SetStatus(zuweisungen == 0
                ? T("Beendet - es war keine Linie gewählt.",
                    "Finished - no line was picked.")
                : T("Ausrichtung übernommen (" + zuweisungen + ").",
                    "Alignment applied (" + zuweisungen + ")."));
            Mod.log.Info("PLT-Ausrichten: vom Nutzer bestaetigt, "
                + zuweisungen + " Zuweisung(en) stehen.");
        }

        /** Der grosse Knopf: zurueck zur laengsten Kante. */
        internal void ResetAusrichtung()
        {
            Ausrichtwahl = Ausrichtschritt.Aus;
            AusrichtFlaeche = -1;
            // Die Handschnitte gehoeren zur Ausrichtung: wer sie zuruecksetzt,
            // will wieder die Vorgabe - und die ist die automatische Zerlegung.
            var hatteSchnitte = _trennschnitte.Count;
            VergissTrennschnitte();
            if (Ausrichtwinkel == null && hatteSchnitte == 0)
            {
                _uiSystem?.SetStatus(T("Es war keine Linie gewählt.",
                    "No line was picked."));
                _uiSystem?.SetAusrichtwinkel(null);
                return;
            }
            var vorher = Ausrichtwinkel.Value;
            // Auch das Verwerfen ist ein Schritt - sonst waere es der
            // einzige Bedienvorgang ohne Rueckgaengig.
            var before = CaptureUndoState();
            _ausrichtungen.Clear();
            _uiSystem?.SetAusrichtwinkel(null);
            _geometryRevision++;
            _layoutDirty = _closed;
            CommitUndoState(before, "Ausrichtung zurückgesetzt");
            _uiSystem?.SetStatus(T("Ausrichtung zurückgesetzt: wieder längste Kante.",
                "Alignment reset: longest edge again."));
            Mod.log.Info("PLT-Ausrichten: zurueckgesetzt, vorher "
                + vorher.ToString("F2",
                    System.Globalization.CultureInfo.InvariantCulture)
                + " Grad. Wer wieder ausrichten will, waehlt neu.");
        }

        /**
         * Beim Zuruecksetzen des Werkzeugs faellt die Bezugslinie mit.
         *
         * Ohne das erbte der NAECHSTE Parkplatz still die Ausrichtung des
         * vorigen - man haette sie an einer Form gewaehlt und an einer ganz
         * anderen wiedergefunden. Und der Wartezustand ueberlebte das
         * Schliessen des Werkzeugs: beim naechsten Oeffnen haette der erste
         * Klick eine Linie gewaehlt statt einen Punkt zu setzen.
         *
         * Kein Rueckgaengig-Schritt: hier wird das ganze Polygon verworfen,
         * der Stapel ohnehin geleert.
         */
        private void VergissAusrichtung()
        {
            // OHNE Bedingung. Sie stand hier als Abkuerzung ("es ist ja eh
            // nichts aktiv") und war damit dieselbe Sorte Falle, die den
            // Nutzer am 2026-09-01 festgesetzt hat: eine Aufraeumstelle, die
            // sich selbst ueberspringt, raeumt nicht auf. Zuweisen kostet
            // nichts, die Bindungen melden ohnehin nur Aenderungen.
            Ausrichtwahl = Ausrichtschritt.Aus;
            AusrichtFlaeche = -1;
            VergissTrennschnitte();
            /*
             * DIE ZONING-FLAECHEN GEHOEREN ZUM UMRISS, nicht zum Werkzeug.
             * Ohne diese Zeile erbte der NAECHSTE Parkplatz still die
             * Parzellen des vorigen - gezogen an einer Form, wiedergefunden
             * an einer ganz anderen. Genau dieselbe Falle wie bei der
             * Bezugslinie eine Zeile darueber.
             */
            VergissZoningflaechen();
            if (_ausrichtungen.Count == 0) return;
            _ausrichtungen.Clear();
            _uiSystem?.SetAusrichtwinkel(null);
        }

        /**
         * Nimmt die Klicks, solange gewaehlt wird.
         *
         * Rueckgabe `true` heisst: verbraucht - das Polygon bekommt diesen
         * Klick nicht mehr zu sehen. Ohne das wuerde derselbe Linksklick auch
         * noch einen Punkt ziehen.
         */
        private bool HandleAusrichtWahl(bool secondaryPressed, bool escapePressed)
        {
            if (!AusrichtWahlAktiv) return false;
            // Der Trennmodus hat seine eigene Rechtsklick-Bedeutung
            // (Zuruecknehmen statt Beenden) und muss deshalb vor der
            // gemeinsamen Abbruchbehandlung stehen.
            if (Ausrichtwahl == Ausrichtschritt.Trennen)
                return HandleTrennmodus(secondaryPressed, escapePressed);
            if (secondaryPressed || escapePressed)
            {
                AbortAusrichtWahl(escapePressed ? "Esc" : "Rechtsklick");
                return true;
            }
            /*
             * DIREKT VON DER MAUS, NICHT UEBER DIE ProxyAction.
             *
             * Erster Anlauf: `applyAction.WasPressedThisFrame()`. Der Klick
             * kam nie an - der Nutzer meldete "das Klicken auf eine Linie
             * bewirkt nichts". Dasselbe steht seit dem 2026-08 im Kopf von
             * `ParkingLotEntrances`: "CS2 maskierte ProxyAction-Klick kam
             * aber nicht bis zum Werkzeug." Der Rest des Werkzeugs liest
             * deshalb laengst `Mouse.current` - diese Stelle jetzt auch.
             */
            var maus = UnityEngine.InputSystem.Mouse.current;
            if (maus == null || !maus.leftButton.wasPressedThisFrame)
                return true;

            /*
             * ERSTE RUNDE: DIE TEILFLAECHE.
             *
             * Getroffen wird ueber das grobe Rechteck, nicht ueber die echte
             * Zerlegungsform - so hat der Nutzer es gewollt, und ein spitz
             * zulaufendes Teil waere sonst kaum anklickbar.
             */
            if (Ausrichtwahl == Ausrichtschritt.Flaeche)
            {
                if (!_hasHover) return true;
                var treffer = TeilflaecheUnter(
                    new float2(_hoverPosition.x, _hoverPosition.z));
                if (treffer < 0)
                {
                    _uiSystem?.SetStatus(T("Keine Teilfläche unter dem Zeiger.",
                        "No sub-area under the cursor."));
                    return true;
                }
                AusrichtFlaeche = treffer;
                Ausrichtwahl = Ausrichtschritt.Linie;
                _uiSystem?.SetStatus(T("Jetzt die Linie wählen.",
                    "Now pick the line."));
                Mod.log.Info("PLT-Ausrichten: Teilflaeche " + treffer
                    + " gewaehlt, warte auf Linie.");
                return true;
            }

            if (_hoverEdge < 0 || _hoverEdge >= _points.Count)
            {
                _uiSystem?.SetStatus(T("Keine Linie unter dem Zeiger.",
                    "No line under the cursor."));
                return true;
            }

            var a = _points[_hoverEdge];
            var b = _points[(_hoverEdge + 1) % _points.Count];
            var richtung = b - a;
            if (math.lengthsq(richtung) < 1e-6f)
            {
                _uiSystem?.SetStatus(T("Diese Linie hat keine Länge.",
                    "That line has no length."));
                return true;
            }

            var before = CaptureUndoState();
            var grad = math.degrees(math.atan2(richtung.y, richtung.x));
            /*
             * DIE NUMMER FESTHALTEN, BEVOR SIE ZURUECKGESETZT WIRD.
             *
             * Weiter unten faellt `AusrichtFlaeche` auf -1, weil die naechste
             * Runde wieder bei der Flaechenwahl beginnt. Beide Logzeilen
             * standen danach und meldeten deshalb immer "-1" - am 2026-09-01
             * im Live-Log des Nutzers als scheinbarer Fehler aufgeschlagen:
             * es sah aus, als haenge die Zuweisung an gar keiner Flaeche.
             * Der Anker daneben war die ganze Zeit richtig; nur der Bericht
             * war es nicht.
             */
            var gewaehlteFlaeche = AusrichtFlaeche;
            var anker = AusrichtFlaeche >= 0 && AusrichtFlaeche < _teilflaechen.Count
                ? _teilflaechen[AusrichtFlaeche].Anker
                : float2.zero;
            // Eine zweite Zuweisung an dieselbe Flaeche ersetzt die erste.
            /*
             * EINE ERSETZUNG BEHAELT IHREN PLATZ.
             *
             * Die ERSTE Zuweisung ist die Vorgabe fuer alle Teilflaechen,
             * die der Nutzer nicht anfasst. Der erste Anlauf entfernte den
             * alten Eintrag und haengte den neuen ans Ende - weist man
             * dieselbe Flaeche ein zweites Mal zu, waere die Vorgabe damit
             * still auf eine ANDERE Flaeche gewandert. Codex hat beim Lesen
             * genau danach gefragt; die Antwort ist: die erste Flaeche bleibt
             * Traegerin der Vorgabe, egal wie oft man sie aendert.
             */
            var neuerEintrag = new Ausrichtzuweisung
            {
                Anker = anker,
                LinieA = a,
                LinieB = b,
                Winkel = grad,
            };
            var vorhanden = _ausrichtungen.FindIndex(z =>
                GleicherAnker(z.Anker, anker));
            if (vorhanden >= 0) _ausrichtungen[vorhanden] = neuerEintrag;
            else _ausrichtungen.Add(neuerEintrag);
            _uiSystem?.SetAusrichtwinkel(Ausrichtwinkel);
            _geometryRevision++;
            _layoutDirty = _closed;
            CommitUndoState(before, "Bezugslinie gewählt");

            /*
             * ZURUECK AUF ANFANG - und zwar IMMER.
             *
             * "Erst Fläche auswählen, dann Linie, dann wieder Fläche, dann
             * Linie usw., bis bestätigt oder abgebrochen wird." Der Ablauf
             * endet also nur auf Ansage: "Fertig", Rechtsklick oder Esc.
             *
             * Bis zum 2026-09-01 beendete die einzelne Teilflaeche sich nach
             * der ersten Linie selbst. Das war gedacht als "es gibt nichts
             * mehr zu waehlen" - seit die Vorschau waehrend des Ausrichtens
             * laeuft, stimmt das nicht mehr: man waehlt eine Linie, SIEHT das
             * Ergebnis und will die naechste probieren. Genau dabei fiel man
             * vorher aus dem Modus heraus.
             */
            if (_teilflaechen.Count > 1)
            {
                AusrichtFlaeche = -1;
                Ausrichtwahl = Ausrichtschritt.Flaeche;
                _uiSystem?.SetStatus(T(
                    "Übernommen. Nächste Fläche wählen, oder „Fertig“.",
                    "Applied. Pick the next area, or press Done."));
            }
            else
            {
                // Eine einzige Teilflaeche: die Flaechenwahl entfaellt, die
                // Runde beginnt gleich wieder bei der Linie.
                _uiSystem?.SetStatus(T(
                    "Übernommen. Andere Linie wählen, oder „Fertig“.",
                    "Applied. Pick a different line, or press Done."));
            }
            ParkingLotLiveLog.Zeile("align linie | teil " + gewaehlteFlaeche
                + " | winkel " + ParkingLotLiveLog.Zahl(grad)
                + " | zuweisungen " + _ausrichtungen.Count
                + " | teilflaechen " + _teilflaechen.Count);
            Mod.log.Info("PLT-Ausrichten: Linie " + _hoverEdge + " gewaehlt, "
                + grad.ToString("F2",
                    System.Globalization.CultureInfo.InvariantCulture)
                + " Grad fuer Teilflaeche " + gewaehlteFlaeche + "; "
                + _ausrichtungen.Count + " Zuweisung(en) gesamt.");
            return true;
        }
    }
}
