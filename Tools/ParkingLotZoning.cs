using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;
using static ParkingLotTool.Tools.ParkingLotTexte;

namespace ParkingLotTool.Tools
{
    /**
     * DAS ZONING-RECHTECK - setzen, verschieben, loeschen.
     *
     * Der Nutzer zieht im geschlossenen Umriss ein Rechteck. Was er zieht,
     * sind NUR die Parzellen; die Zoning-Strasse kommt spaeter aussen dazu.
     * Seine Ansage vom 2026-09-01:
     *
     *   "Das Rechteck soll nach dem Erstellen verschoben werden. Nach dem
     *    Erstellen soll die Zoning-Flaeche ausgewaehlt werden (hover) koennen
     *    und wieder geloescht werden mit Rechtsklick."
     *
     * DIESE DATEI BAUT NOCH NICHTS IM SPIEL. Sie fuehrt die Flaechen nur als
     * Entwurf und zeigt sie an. Ob CS2 auf unseren Zellen ueberhaupt Gebaeude
     * wachsen laesst, misst zuerst der Sondenlauf; erst danach entstehen
     * Strasse und Zonenblock. Andersherum haetten wir Geometrie fuer etwas
     * gebaut, das vielleicht gar nicht traegt.
     *
     * WARUM DER WINKEL NICHT DER DES PARKPLATZES IST: er kommt aus denselben
     * vier Modi (Kante, Quer, Fest, ausgerichtete Linie), aber mit eigenen
     * Werten. Der Nutzer wollte die Parzellen ausdruecklich frei drehen
     * koennen - eine Zeile Hausfronten muss nicht parallel zu den Buchtreihen
     * stehen.
     */
    public sealed partial class ParkingLotToolSystem
    {
        private readonly List<ParkingGeometry.Zoningflaeche> _zoningflaechen
            = new List<ParkingGeometry.Zoningflaeche>();

        /** Laeuft gerade ein Zug? Dann ist das hier die Vorschau. */
        private ParkingGeometry.Zoningflaeche _zoningZug;
        private float2 _zoningZugStart;

        /** Die Flaeche unter dem Zeiger, sonst -1. */
        private int _zoningHover = -1;

        /**
         * Die AUSGEWAEHLTE Flaeche - sie wird verschoben, nicht die unter
         * dem Zeiger.
         *
         * Ansage des Nutzers: *"Die Flaeche muss ausgewaehlt werden koennen,
         * um sie zu verschieben. Standard sollte immer die erste Flaeche
         * sein."* Deshalb 0 und nicht -1: solange es eine Flaeche gibt, ist
         * eine gewaehlt.
         */
        private int _zoningAuswahl;

        /** Wartet das Werkzeug auf den Klick auf eine Polygonlinie? */
        private bool _zoningLinienwahl;

        internal int ZoningAuswahl =>
            _zoningflaechen.Count == 0 ? -1
            : math.clamp(_zoningAuswahl, 0, _zoningflaechen.Count - 1);

        internal bool ZoningLinienwahl => _zoningLinienwahl;

        /**
         * Die gewaehlte Bezugslinie DER PARZELLEN - eigene, nicht die des
         * Parkplatzes.
         *
         * Der Nutzer wollte die vier Modi des Reihenwinkels auch hier, aber
         * mit eigenen Werten. Eine gemeinsame Bezugslinie waere die halbe
         * Trennung gewesen: der Winkelmodus haette sich getrennt eingestellt
         * und der Bezug nicht.
         */
        internal double? ZoningAusrichtwinkel { get; private set; }

        /** Die Flaeche, die gerade verschoben wird, sonst -1. */
        private int _zoningSchiebt = -1;
        private float2 _zoningGriff;

        /**
         * Die zuletzt gewaehlte Rasterstelle, absolut.
         *
         * Sie bricht den Gleichstand zwischen zwei fast gleich guten
         * Stellen - ohne sie flackert die Flaeche im Zeigerrauschen.
         * Gilt je Zugvorgang; beim Anfassen wird sie geloescht.
         */
        private float2 _zoningRastZiel;
        private bool _zoningRastGesetzt;

        /**
         * Das letzte beruhigte Mausziel, absolut.
         *
         * Der Gelaenderaycast wanderte bei stiller Maus im gemessenen Lauf um
         * 1,7 cm. Unterhalb von 5 cm ist das deshalb kein neuer Zugwunsch,
         * sondern Messrauschen. Der feste Griff bleibt davon unberuehrt.
         */
        private float2 _zoningLetztesMausziel;
        private bool _zoningMauszielGesetzt;

        internal IReadOnlyList<ParkingGeometry.Zoningflaeche> Zoningflaechen
            => _zoningflaechen;

        internal ParkingGeometry.Zoningflaeche ZoningVorschau => _zoningZug;
        internal int ZoningHover => _zoningHover;

        /** Der Modus, in dem die Klicks den Parzellen gehoeren. */
        internal bool ZoningModus { get; private set; }

        /**
         * Der Winkel, in dem neue Flaechen entstehen.
         *
         * Dieselbe Ableitung wie beim Reihenwinkel des Parkplatzes - Bezug
         * ist die ausgerichtete Linie, sonst die laengste Kante, dazu 0, 90
         * oder der Reglerwert. Sie steht in `ParkingGeometry.Reihenwinkel`
         * und wird hier NICHT nachgebaut: genau daran ist der Reihenwinkel
         * am 2026-09-01 zerbrochen, als zwei Rechenwege dieselbe Ableitung
         * je einmal geschrieben hatten und sich uneinig wurden.
         */
        /**
         * MIT "KANTE" LIEGEN DIE PARZELLEN AUF DEM BUCHTRASTER.
         *
         * Der Nutzer hat das vermutet und nachgefragt - es stimmt, und zwar
         * weil hier dieselbe Funktion rechnet wie fuer die Buchtreihen:
         * ohne gewaehlte Linie ist der Bezug beider die LAENGSTE KANTE.
         *
         * Eine Stelle koennte die beiden auseinandertreiben, und deshalb
         * steht sie hier: `Reihenwinkel` addiert am Ende `KantenVersatz`.
         * Den setzt im Livepfad NIEMAND - er ist ein aufgegebener Versuch
         * (0,01 Grad brachen zwei feste Faelle) und immer 0. Bekommt er
         * jemals einen Wert, muss er auch hier hinein, sonst kippen die
         * Parzellen um denselben Betrag gegen die Buchten.
         */
        internal double ZoningWinkel => ParkingGeometry.Reihenwinkel(
            new LayoutSettings
            {
                AngleMode = ZoningWinkelmodus,
                Angle = ZoningReglerwinkel,
                Ausrichtwinkel = ZoningAusrichtwinkel,
            },
            _closed && _points.Count >= 3
                ? ParkingGeometry.LaengsteKante(_points
                    .Select(p => new double2(p.x, p.y)).ToArray())
                : 0);

        /**
         * WO BAULAND ENTSTEHEN SOLL - innen, aussen oder beides.
         *
         * Ansage des Nutzers: *"Ich muss vorher dem User auch fragen ob er
         * nur innerhalb oder ausserhalb oder innerhalb und ausserhalb
         * zoning will. Denn das beeinflusst auch die Generation des Parking
         * Lots, denn wir muessen ja auch ausserhalb der Strasse Platz fuer
         * Zoning machen, damit nicht die Tiles auf Strassen von uns
         * liegen."* Standard ist innen.
         *
         * Die Wahl gilt fuer den ganzen Parkplatz, nicht je Flaeche - sie
         * wird VOR dem Ziehen getroffen, weil sie bestimmt, wieviel Platz
         * jede Flaeche belegt.
         */
        /**
         * ABGELEITET, nicht nachgefuehrt.
         *
         * Bis zum 2026-09-21 war das ein eigener Schalter, der neben den
         * Tiefen herlief. Zwei Quellen fuer dieselbe Frage sind genau die
         * Sorte Fehler, die sich beim Zurueckstellen zeigt: Tiefe auf 0 und
         * der Schalter stand weiter auf "aussen". Jetzt gibt es nur die
         * Tiefen, und "aussen gibt es" heisst genau "eine ist groesser 0".
         */
        internal ParkingGeometry.Zoningseite ZoningSeite =>
            _zoningflaechen.Any(f => f?.Aussentiefen != null
                && f.Aussentiefen.Any(t => t > 1e-6))
                ? ParkingGeometry.Zoningseite.Beides
                : ParkingGeometry.Zoningseite.Innen;

        /**
         * WIE TIEF EIN BAND WIRD, WENN MAN EINE SEITE ANKLICKT.
         *
         * Eine Vorwahl, keine Einstellung an einer Flaeche. Der Nutzer am
         * 2026-09-21: *"es braucht nicht Seite 1-4 fuer Tile-Tiefe sondern
         * einfach nur ein voreinstellen fuers klicken."* Welche Seite
         * gemeint ist, sagt der Klick im Seitenmodus - dort steht der Zeiger
         * ja schon auf genau einer Seite genau einer Strasse.
         *
         * 1 bis 6, weil CS2 ab einer Strasse nicht tiefer zont
         * (`ZoneUtils.MAX_ZONE_DEPTH`). Ein "0" braucht es nicht: ein
         * zweiter Klick auf dieselbe Seite nimmt das Band wieder weg.
         */
        internal int ZoningTiefeVorwahl { get; private set; } = 2;

        /**
         * Der Rand einer Flaeche - IMMER nur die Zoning-Strasse.
         *
         * Bis zum 2026-09-21 wurde die Aussentiefe hier aufaddiert. Das war
         * der Fehler: der Zellenkern stuft alles innerhalb von `Rand` als
         * Fahrbahn ein, also wurde aus dem aeusseren Bauland Asphalt. Die
         * Tiefe steht jetzt in `Aussentiefen` an der Flaeche selbst.
         */
        internal double ZoningRand => ParkingGeometry.ZoningStrassenbreite;

        /** Die Vorwahl fuer den naechsten Klick auf eine Aussenseite. */
        internal void SetzeZoningAussentiefe(int parzellen)
        {
            var neu = math.clamp(parzellen, 1, 6);
            if (ZoningTiefeVorwahl == neu) return;
            ZoningTiefeVorwahl = neu;
            _uiSystem?.SetZoningAussentiefe(neu);
        }

        internal int ZoningAussentiefe => ZoningTiefeVorwahl;

        /**
         * Gibt der angeklickten Aussenseite ein Band - oder nimmt es weg.
         *
         * Zurueck kommt, was passiert ist, damit der Aufrufer es melden
         * kann: 0 heisst "war innen, nichts zu tun", sonst die neue Tiefe
         * in Kacheln (0 = abgeraeumt).
         */
        /**
         * ZWEI PUNKTE, NICHT EINER.
         *
         * `aufStrasse` liegt auf der angeklickten Strassenkante und sagt,
         * WELCHE Seite welcher Flaeche gemeint ist. `zeiger` sagt nur, ob
         * innen oder aussen geklickt wurde.
         *
         * Erst stand hier nur der Zeiger, mit der Strassenbreite als
         * Toleranz. Das ging schief: die Seitensuche laesst den Zeiger bis
         * `ZoningSeitenreichweite` (24 m) entfernt stehen, weil man auf die
         * Kacheln neben der Strasse zielt und nicht auf den Asphalt. Weiter
         * als 8 m weg fand die Suche keine Flaeche, setzte kein Band - und
         * der Nutzer sah, dass sich nach dem Klick nichts ruehrte.
         */
        internal int SchalteAussenband(float2 aufStrasse, float2 zeiger,
            out bool getroffen)
        {
            getroffen = false;
            if (!ParkingGeometry.ZoningSeiteBei(_zoningflaechen, aufStrasse,
                    out var index, out var seite,
                    (float)ParkingGeometry.ZoningStrassenbreite))
                return 0;
            var f = _zoningflaechen[index];
            if (!ParkingGeometry.ZoningSeiteIstAussen(f, seite, zeiger))
                return 0;

            getroffen = true;
            f.Aussentiefen ??= new double[4];

            /*
             * AENDERN GEHT VOR ABSCHALTEN.
             *
             * Ein Klick auf eine Seite, die schon ein Band hat, raeumte es
             * frueher immer ab - auch dann, wenn im Panel inzwischen eine
             * ANDERE Tiefe stand. Wer von 2 auf 4 stellt und draufklickt,
             * meint aber 4, nicht "weg". Ansage des Nutzers am 2026-09-21:
             * *"wenn ich outer band aendere und erneut auf eine linie klicke
             * die bereits belegt ist soll die aenderung erst kommen, also
             * aenderung vor deaktivieren."*
             *
             * Abgeraeumt wird deshalb nur noch bei GLEICHER Tiefe - der
             * zweite Klick mit unveraenderter Vorwahl.
             */
            var neu = ZoningTiefeVorwahl * ParkingGeometry.Zoningparzelle;
            var gleich = Math.Abs(f.Aussentiefen[seite] - neu) < 1e-6;
            var vorher = f.Aussentiefen[seite];
            f.Aussentiefen[seite] = gleich ? 0.0 : neu;
            // Anfang der Messkette zum Zoningzettel: hier entsteht der Wert,
            // in `WriteBuildReceipt` wird er geschrieben, beim Umbau wieder
            // gelesen. Drei Zeilen im Log, und man sieht, wo er abreisst.
            Mod.log.Info("PLT-Aussenband GESCHALTET: Flaeche " + index
                + ", Seite " + seite + ": " + vorher.ToString("0.##",
                    System.Globalization.CultureInfo.InvariantCulture)
                + " -> " + f.Aussentiefen[seite].ToString("0.##",
                    System.Globalization.CultureInfo.InvariantCulture)
                + " m (Vorwahl " + ZoningTiefeVorwahl + " Kacheln).");
            _layoutDirty = _closed;
            _geometryRevision++;
            return gleich ? 0 : ZoningTiefeVorwahl;
        }

        /*
         * HIER STAND `UebernimmZoningRand`.
         *
         * Die Funktion schrieb einen neuen Rand in ALLE Flaechen und wies
         * die zurueck, die damit nicht mehr in den Umriss passten. Seit dem
         * 2026-09-21 ist der Rand immer die Strassenbreite, also gibt es
         * nichts mehr nachzutragen; und das aeussere Bauland darf ohnehin
         * nicht nachtraeglich in fremde Flaechen laufen. Es wird einzeln
         * angeklickt, siehe `SchalteAussenband`.
         */

        /** "edge" | "quer" | "fixed" - dieselben Namen wie beim Parkplatz. */
        internal string ZoningWinkelmodus { get; private set; } = "edge";

        /** Der Reglerwert; zaehlt nur bei "fixed". */
        internal double ZoningReglerwinkel { get; private set; }

        internal void SetzeZoningWinkelmodus(string modus)
        {
            if (string.IsNullOrEmpty(modus) || ZoningWinkelmodus == modus) return;
            ZoningWinkelmodus = modus;
            /*
             * "Fest" verwirft die Linie - dieselbe Regel wie beim
             * Reihenwinkel. Die beiden sind entweder/oder: "Kante" und
             * "Quer" sind die 0- und 90-Grad-Seite DESSELBEN Bezugs, "Fest"
             * ist der Gegenmodus dazu. Bliebe die Linie stehen, waere der
             * Reglerwert relativ zu ihr - und der Nutzer haette bei 0 Grad
             * nicht das, was "Kante" zeigt.
             */
            if (modus == "fixed") SetzeZoningAusrichtwinkel(null);
            _zoningLinienwahl = false;
            _uiSystem?.SetZoningLinienwahl(false);
            DreheZoningflaechen();
            _geometryRevision++;
        }

        /**
         * DER WINKEL GILT FUER ALLE FLAECHEN, NICHT NUR FUER NEUE.
         *
         * Das war mein Denkfehler: der Winkel floss nur in `ZoningAusZug`,
         * also beim Ziehen. Eine gesetzte Flaeche behielt ihren
         * gespeicherten Wert, und der Regler tat scheinbar nichts. Der
         * Nutzer: *"Das Drehen funktioniert nicht, ich habe auf Fixed
         * gestellt und Winkel eingestellt, die Flaeche hat sich nicht
         * gedreht."*
         *
         * NUR DIE GEWAEHLTE FLAECHE DREHT SICH. Zuerst folgten ihr alle,
         * mit der Begruendung "der Winkel ist eine Einstellung". Der Nutzer
         * hat das im Spiel sofort als Fehler gemeldet: *"Beim Aendern des
         * Winkels wurden 2 Flaechen statt die ausgewaehlte veraendert."* Er
         * hat recht - die Auswahl gibt es genau dafuer, eine einzelne
         * Flaeche anzufassen. Der Regler gilt also fuer die gewaehlte und
         * fuer die naechste gezogene; die uebrigen bleiben, wie sie sind.
         *
         * Gedreht wird um die Mitte, damit sie an ihrem Platz bleibt.
         *
         * EINE DREHUNG LOESCHT NICHTS. Passt eine Flaeche im neuen Winkel
         * nicht mehr in den Umriss, behaelt sie ihren alten und wird
         * gemeldet. Zuerst hatte ich sie hier verworfen - dieselbe Regel wie
         * beim Verschieben einer Polygonlinie. Das ist aber etwas anderes:
         * dort zieht der Nutzer dem Parkplatz den Boden weg, hier probiert
         * er nur einen Winkel aus. Ein Regler, der beim Ausprobieren Arbeit
         * auffrisst, ist ein schlechter Regler.
         *
         * Dass dabei Flaechen verschiedene Winkel tragen koennen, ist
         * sichtbar und damit ehrlich - man sieht sofort, welche nicht
         * mitgegangen ist.
         */
        private void DreheZoningflaechen()
        {
            if (_zoningflaechen.Count == 0) return;
            var winkel = ZoningWinkel;
            var geaendert = false;
            var verweigert = 0;
            var i = ZoningAuswahl;
            if (i >= 0
                && System.Math.Abs(_zoningflaechen[i].Winkel - winkel) >= 1e-9)
            {
                var gedreht =
                    ParkingGeometry.ZoningGedreht(_zoningflaechen[i], winkel);
                // Auch beim Drehen darf nichts ineinanderlaufen.
                if (ZoningLiegtImUmriss(gedreht)
                    && !ZoningUeberlapptAndere(gedreht, i))
                {
                    _zoningflaechen[i] = gedreht;
                    geaendert = true;
                }
                else verweigert++;
            }
            if (geaendert) _layoutDirty = _closed;
            if (verweigert > 0)
                _uiSystem?.SetStatus(T(
                    "Die Fläche bleibt im alten Winkel: gedreht passt sie "
                        + "nicht in den Umriss.",
                    "The patch keeps its old angle: rotated it would not "
                        + "fit inside."));
            if (geaendert) _geometryRevision++;
        }

        internal void SetzeZoningAusrichtwinkel(double? grad)
        {
            ZoningAusrichtwinkel = grad;
            _uiSystem?.SetZoningAusrichtwinkel(grad);
            DreheZoningflaechen();
            _geometryRevision++;
        }

        /** Startet oder beendet die Wahl der Bezugslinie. */
        internal void SetzeZoningLinienwahl(bool an)
        {
            if (an && !_closed) return;
            _zoningLinienwahl = an;
            _uiSystem?.SetZoningLinienwahl(an);
            _uiSystem?.SetStatus(an
                ? T("Jetzt eine Polygonlinie anklicken.",
                    "Now click a line of the outline.")
                : T("Linienwahl beendet.", "Line pick finished."));
        }

        internal void SetzeZoningReglerwinkel(double grad)
        {
            if (System.Math.Abs(ZoningReglerwinkel - grad) < 1e-9) return;
            ZoningReglerwinkel = grad;
            DreheZoningflaechen();
            _geometryRevision++;
        }

        internal void SetzeZoningflaechen(
            IReadOnlyList<ParkingGeometry.Zoningflaeche> satz)
        {
            _zoningflaechen.Clear();
            if (satz == null) return;
            foreach (var f in satz)
                _zoningflaechen.Add(new ParkingGeometry.Zoningflaeche
                {
                    Ecke = f.Ecke,
                    Spalten = f.Spalten,
                    Reihen = f.Reihen,
                    Winkel = f.Winkel,
                    // Der Rand gehoert dazu. Fehlte er, fiele das aeussere
                    // Bauland bei jedem Rueckgaengig auf den Standardwert -
                    // derselbe Fehler wie beim Verschieben und Drehen.
                    Rand = f.Rand,
                    Aussentiefen = f.Aussentiefen?.Clone() as double[],
                });
        }

        /**
         * Alles vergessen - STILL.
         *
         * Kein Statustext: das hier laeuft beim Zuruecksetzen des ganzen
         * Werkzeugs, und ein "Zoning beendet" waere dort die falsche
         * Meldung ueber den falschen Vorgang.
         */
        internal void VergissZoningflaechen()
        {
            _zoningflaechen.Clear();
            // Die Handschaltungen gehoeren zu diesen Flaechen. Blieben sie
            // stehen, traefen sie beim naechsten Parkplatz zufaellig eine
            // Kante, die genauso liegt - und niemand wuesste, warum dort
            // eine Seite aus ist.
            _zoningSeitenPlan.Clear();
            _randzoning.Clear();
            _zoningZug = null;
            _zoningHover = -1;
            _zoningSchiebt = -1;
            _zoningSchiebtVorher = null;
            _uiSystem?.SetZoningZug(string.Empty);
            if (ZoningModus)
            {
                ZoningModus = false;
                _uiSystem?.SetZoningModus(false);
            }
            _uiSystem?.SetZoningZahlen(0, 0);
        }

        /**
         * Schaltet den Zoning-Modus.
         *
         * Er schliesst sich mit dem Zugangsmodus aus - beide wollen denselben
         * Linksklick, und zwei Modi gleichzeitig sind genau die Sorte
         * Doppeldeutigkeit, die den Nutzer heute schon einmal festgesetzt
         * hat. Deshalb schaltet der eine den anderen aus.
         */
        internal void SetzeZoningModus(bool an)
        {
            /*
             * OHNE GESCHLOSSENEN UMRISS GIBT ES NICHTS ZU ZONEN.
             *
             * Die Parzellen liegen IM Parkplatz - ohne Umriss gaebe es kein
             * Innen, und man zoege Rechtecke in die leere Landschaft. Der
             * Nutzer hat genau danach gefragt: *"Der Reiter wird erst aktiv,
             * wenn ein Polygon gezeichnet wurde, so wie vereinbart?"* Er
             * wurde es nicht; jetzt schon, und zwar an drei Stellen - hier,
             * am Reiterknopf und beim Zuruecksetzen des Werkzeugs. Eine
             * gesperrte Oberflaeche allein reicht nicht: die Bindung kann
             * ausgeloest werden, ohne dass jemand den Knopf sieht.
             */
            if (an && !_closed)
            {
                _uiSystem?.SetStatus(T(
                    "Erst das Polygon schließen, dann Zoning-Flächen setzen.",
                    "Close the outline first, then place zoning patches."));
                return;
            }
            if (ZoningModus == an) return;
            ZoningModus = an;
            if (an) SetEntranceModeFromPanel(false);
            // Beide brauchen denselben Linksklick. Wer das Setzen einschaltet,
            // meint nicht mehr den Seitenschalter.
            if (an) SetzeZoningSeitenModus(false);
            _zoningZug = null;
            _zoningSchiebt = -1;
            _uiSystem?.SetZoningModus(an);
            _uiSystem?.SetStatus(an
                ? T("Zoning: Rechteck ziehen. Rechtsklick auf eine Fläche "
                        + "löscht sie.",
                    "Zoning: drag a rectangle. Right-click a patch to delete it.")
                : T("Zoning beendet.", "Zoning finished."));
        }

        /** Welche Flaeche liegt unter dem Zeiger? -1, wenn keine. */
        private int ZoningUnterZeiger()
        {
            if (!_hasHover) return -1;
            var zeiger = new float2(_hoverPosition.x, _hoverPosition.z);
            // RUECKWAERTS: die zuletzt gesetzte liegt oben. Bei zwei
            // ueberlappenden Flaechen trifft der Klick die, die man sieht.
            for (var i = _zoningflaechen.Count - 1; i >= 0; i--)
                if (ParkingGeometry.ZoningEnthaelt(_zoningflaechen[i], zeiger))
                    return i;
            return -1;
        }

        /**
         * Nimmt die Klicks im Zoning-Modus.
         *
         * Rueckgabe `true` heisst: verbraucht - das Polygon bekommt diesen
         * Klick nicht mehr zu sehen. Ohne das zoege derselbe Linksklick auch
         * noch eine Ecke des Umrisses herum.
         */
        /**
         * HAELT DER NUTZER DIE MAUS - AUCH MIT SHIFT?
         *
         * CS2 maskiert die unmodifizierte Bindung, sobald ein Modifier
         * gehalten wird; `applyAction` meldet dann "losgelassen", obwohl die
         * Taste unten ist. Der Nutzer sah es als *"Shift ziehen funktioniert
         * nicht, die ZF bleibt stehen anstatt ohne Snapping zu
         * verschieben."*
         *
         * Am 2026-08-20 stand derselbe Fehler schon einmal im Weg, damals
         * mit Strg beim Einfuegen eines Polygonpunkts. Die Taste zu wechseln
         * hilft nicht - es liegt am Modifier an sich.
         *
         * Deshalb hier, und nur hier, direkt an der Maus vorbei: eng
         * begrenzt auf den Fall, der den Modifier braucht, damit der
         * Normalfall weiter ueber das Spielsystem laeuft.
         */
        private static bool ZoningZugHaelt(bool ausAktion)
        {
            if (ausAktion) return true;
            if (!ShiftGehalten()) return false;
            var maus = UnityEngine.InputSystem.Mouse.current;
            return maus != null && maus.leftButton.isPressed;
        }

        private bool HandleZoning(bool primaerGedrueckt, bool primaerGehalten,
                                  bool sekundaerGedrueckt, bool escGedrueckt)
        {
            /*
             * DIE LINIENWAHL BRAUCHT DEN SETZEN-MODUS NICHT.
             *
             * Ansage des Nutzers: *"Es sollte direkt beim Erstellen/Ziehen
             * der eingestellte Modus angepasst werden. Das heisst Align
             * sowie die anderen Modi sollten auch moeglich sein, BEVOR
             * ueberhaupt eine ZF erstellt/gezogen wurde."* Die drei
             * Winkelmodi konnten das schon - sie wirken auf den naechsten
             * Zug. Die Linienwahl hing dagegen am Setzen-Modus, weil sie
             * unterhalb dieses Tores stand: man musste erst "Flaechen
             * setzen" druecken, um eine Bezugslinie waehlen zu duerfen.
             * Jetzt steht sie darueber.
             */
            if (_zoningLinienwahl && _closed)
            {
                if (escGedrueckt)
                {
                    SetzeZoningLinienwahl(false);
                    return true;
                }
                if (sekundaerGedrueckt) { SetzeZoningLinienwahl(false); return true; }
                if (!primaerGedrueckt) return true;
                if (_hoverEdge < 0 || _points.Count < 2)
                {
                    _uiSystem?.SetStatus(T("Keine Linie unter dem Zeiger.",
                        "No line under the cursor."));
                    return true;
                }
                var la = _points[_hoverEdge];
                var lb = _points[(_hoverEdge + 1) % _points.Count];
                var richtung = lb - la;
                if (math.lengthsq(richtung) < 1e-6f)
                {
                    _uiSystem?.SetStatus(T("Diese Linie hat keine Länge.",
                        "That line has no length."));
                    return true;
                }
                SetzeZoningAusrichtwinkel(
                    math.degrees(math.atan2(richtung.y, richtung.x)));
                SetzeZoningLinienwahl(false);
                _uiSystem?.SetStatus(T(
                    "Parzellen folgen jetzt dieser Linie.",
                    "Parcels now follow that line."));
                return true;
            }

            if (!ZoningModus) return false;
            // Der Umriss kann sich unter dem laufenden Modus aufloesen -
            // Rueckgaengig bis vor das Schliessen genuegt dafuer.
            if (!_closed) { SetzeZoningModus(false); return false; }

            if (escGedrueckt)
            {
                SetzeZoningModus(false);
                return true;
            }

            _zoningHover = _zoningSchiebt >= 0 ? _zoningSchiebt
                : _zoningZug != null ? -1 : ZoningUnterZeiger();

            if (!_hasHover)
            {
                /*
                 * KEIN BODEN UNTER DEM ZEIGER - dann liegt dort auch keine
                 * Flaeche, und der Rechtsklick bedeutet dasselbe wie ueber
                 * freiem Grund: raus aus dem Modus.
                 */
                if (sekundaerGedrueckt)
                {
                    if (_zoningZug != null) { _zoningZug = null; return true; }
                    SetzeZoningModus(false);
                }
                return true;
            }
            var zeiger = new float2(_hoverPosition.x, _hoverPosition.z);

            // RECHTSKLICK LOESCHT - aber nur auf einer Flaeche. Sonst ist er
            // der gewohnte Schritt zurueck und gehoert nicht uns.
            if (sekundaerGedrueckt)
            {
                if (_zoningZug != null) { _zoningZug = null; return true; }
                /*
                 * IMMER VERBRAUCHT, auch wenn nichts darunter liegt.
                 *
                 * Ansage des Nutzers: *"Rechtsklick sollte nicht mehr gelten,
                 * also 'undo Polygonpunkte' im Bauen, wenn ich Zoning betreten
                 * habe."* Vorher fiel ein Rechtsklick neben eine Flaeche
                 * durch und nahm den letzten POLYGONPUNKT zurueck - im
                 * Zoning-Modus zerlegte man also den Umriss, ohne es zu
                 * wollen.
                 *
                 * UEBER FREIEM GRUND VERLAESST ER DEN MODUS. Ansage des
                 * Nutzers am 2026-09-09: *"Was bei Toggle Road Sides der
                 * Rechtsklick macht, sollte auch bei 'Place Patches'
                 * funktionieren."* Dort gilt seit demselben Tag: was unter
                 * dem Zeiger liegt, geht weg - liegt nichts da, geht der
                 * Modus. Beide Weltmodi tun jetzt dasselbe.
                 *
                 * Verbraucht bleibt er in jedem Fall; nur der Umriss darf
                 * ihn nie zu sehen bekommen.
                 */
                if (_zoningHover < 0) { SetzeZoningModus(false); return true; }
                var vorher = CaptureUndoState();
                _zoningflaechen.RemoveAt(_zoningHover);
                _zoningHover = -1;
                NachZoningaenderung(vorher, "Zoning-Fläche gelöscht");
                return true;
            }

            // VERSCHIEBEN GEHT VOR ZIEHEN. Wer auf einer Flaeche drueckt,
            // will sie bewegen und keine zweite darueberlegen.
            if (_zoningSchiebt >= 0)
            {
                if (ZoningZugHaelt(primaerGehalten))
                {
                    var f = _zoningflaechen[_zoningSchiebt];
                    /*
                     * DER GRIFF IST EIN FESTER ABSTAND, KEIN ANKER.
                     *
                     * `_zoningGriff` haelt seit dem Anfassen den Abstand
                     * zwischen Zeiger und Flaechenecke. Gewuenscht ist
                     * damit immer die Ecke, die unter demselben Punkt des
                     * Zeigers liegt - unabhaengig davon, was zwischendurch
                     * abgelehnt wurde.
                     *
                     * Beide frueheren Fassungen hatten je einen Mangel:
                     * den Griff nur um den ausgefuehrten Teil nachziehen
                     * liess den Rest auflaufen (*"bleibt stecken"*), ihn
                     * dem Zeiger nachfuehren loeste die Bindung (*"dadurch
                     * ist die Flaeche nicht mehr am Cursor gebunden"*). Ein
                     * fester Abstand hat keines von beidem: der Zeiger darf
                     * hinauslaufen, die Flaeche bleibt an der Kante, und
                     * beim Zurueckkommen sitzt sie wieder genau unter dem
                     * Griffpunkt.
                     */
                    var rastenAn = !ShiftGehalten();
                    var mausziel = zeiger + _zoningGriff;
                    if (rastenAn && _zoningMauszielGesetzt
                        && math.distance(mausziel, _zoningLetztesMausziel) < 0.05f)
                        mausziel = _zoningLetztesMausziel;
                    else if (rastenAn)
                    {
                        _zoningLetztesMausziel = mausziel;
                        _zoningMauszielGesetzt = true;
                    }
                    else _zoningMauszielGesetzt = false;
                    var wunsch = mausziel - f.Ecke;

                    /*
                     * ERREICHBARE ROHPOSITION, DANN RASTZIEL - genau einmal.
                     *
                     * Nachbarin und Flaechengroesse legen Rasterpunkte und
                     * Kontaktlinien fest. Der Mauswunsch waehlt daraus, aber
                     * er darf weder die Gitterphase noch den Fangbereich an
                     * einer unerreichbaren Stelle ausserhalb des Umrisses
                     * festlegen. Deshalb wird zuerst der rohe Wunsch auf den
                     * erlaubten Bewegungsraum begrenzt und DORT gerastert.
                     *
                     * Anders als der bisherige zweite Durchgang ist das kein
                     * Rueckfall: jeder Bildlauf hat denselben Rechenweg. Die
                     * Begrenzung laeuft nur einmal; ein gefundenes Rasterziel
                     * ist bereits durch denselben Pruefer zugelassen und wird
                     * nicht noch einmal halbiert. Der absolute Mauspunkt und
                     * der feste Griff bleiben die einzigen Eingaben - kein
                     * gerasteter Zuwachs kann ins naechste Bild rueckkoppeln.
                     */
                    var grob = ErlaubterVersatz(
                        f, wunsch, _zoningSchiebt, rastenAn);
                    var ziel = rastenAn
                        ? RasteAnNachbarn(
                            f, wunsch, grob, _zoningSchiebt)
                        : grob;
                    var erlaubt = ziel;

                    /*
                     * DAS ZIEL GEHOERT MIT INS LOG.
                     *
                     * Ohne es sind zwei voellig verschiedene Lagen nicht zu
                     * unterscheiden, und beide schreiben "erlaubt 0.0":
                     *
                     *   ziel 0,0 m -> die Flaeche STEHT schon auf dem
                     *                 Rastziel. Kein Weg noetig, alles gut.
                     *   ziel 4,5 m -> ein Ziel war da und wurde verweigert.
                     *                 DAS ist Reibung.
                     *
                     * Am 2026-09-04 habe ich genau das verwechselt und aus
                     * "87 % blockiert" einen Rueckschritt gelesen, wo in
                     * Wahrheit das Rasten sauber gehalten hat. Eine Messung,
                     * die zwei Gegenteile gleich aussehen laesst, ist keine.
                     */
                    if (ParkingGeometry.LiveAn
                        && math.lengthsq(wunsch) > 1e-6f)
                        ParkingGeometry.Live("  zoning schieben | wunsch "
                            + ParkingLotLiveLog.Zahl(math.length(wunsch), 3)
                            + " m | ziel "
                            + ParkingLotLiveLog.Zahl(math.length(ziel), 3)
                            + " m | erlaubt "
                            + ParkingLotLiveLog.Zahl(math.length(erlaubt), 3)
                            + " m"
                            + (math.lengthsq(ziel) > 2.5e-3f
                                && math.lengthsq(erlaubt) <= 2.5e-3f
                                ? "  <-- VERWEIGERT" : string.Empty));
                    if (!math.all(erlaubt == float2.zero))
                    {
                        _zoningflaechen[_zoningSchiebt] =
                            ParkingGeometry.ZoningVerschoben(f, erlaubt);
                    }
                    /*
                     * DER GRIFF FOLGT IMMER DEM ZEIGER - auch wenn nichts
                     * oder nur eine Achse ausgefuehrt wurde.
                     *
                     * Vorher wurde er nur um den ausgefuehrten Teil
                     * nachgezogen, mit der Begruendung, sonst spraenge die
                     * Flaeche beim Wegziehen von der Wand. Der abgelehnte
                     * Rest staute sich damit aber auf: wer 20 m gegen den
                     * Rand schob, musste erst 20 m zurueckfahren, bevor sich
                     * ueberhaupt wieder etwas ruehrte. Befund des Nutzers:
                     * *"wenn ich die ZF verschiebe und an den Rand komme,
                     * dann bleibt es stecken."*
                     *
                     * Der Griff wird dabei NICHT veraendert - er ist der
                     * feste Abstand vom Anfassen, siehe oben.
                     */
                    return true;
                }
                var vorher2 = _zoningSchiebtVorher;
                _zoningSchiebt = -1;
                _zoningRastGesetzt = false;
                _zoningMauszielGesetzt = false;
                _zoningSchiebtVorher = null;
                NachZoningaenderung(vorher2, "Zoning-Fläche verschoben");
                return true;
            }

            if (_zoningZug != null)
            {
                if (primaerGehalten)
                {
                    var kandidat = ParkingGeometry.ZoningAusZug(
                        _zoningZugStart, zeiger, ZoningWinkel,
                        out var breiteAus, out var tiefeAus);
                    // GAR NICHT ERST HINAUSLASSEN. Passt der naechste
                    // Schritt nicht mehr in den Umriss, bleibt der letzte
                    // gueltige stehen - das Rechteck waechst bis an die
                    // Kante und dort nicht weiter.
                    // Der Zug waechst bis an die Kante - und bis an die
                    // naechste Flaeche. Ueberlappen ist auch beim Ziehen
                    // verboten, sonst entstuende der Konflikt gleich mit.
                    if (ZoningLiegtImUmriss(kandidat)
                        && !ZoningUeberlapptAndere(kandidat, -1))
                    {
                        _zoningZug = kandidat;
                        MeldeZug(_zoningZug, breiteAus, tiefeAus);
                    }
                    return true;
                }
                var fertig = _zoningZug;
                _zoningZug = null;
                _uiSystem?.SetZoningZug(string.Empty);
                var vorher3 = CaptureUndoState();
                fertig.Rand = ZoningRand;
                // Baender bekommt die Flaeche nicht beim Setzen, sondern
                // durch einen Klick auf ihre Aussenseite im Seitenmodus.
                _zoningflaechen.Add(fertig);
                _zoningAuswahl = _zoningflaechen.Count - 1;
                _uiSystem?.SetZoningAuswahl(_zoningAuswahl);
                NachZoningaenderung(vorher3,
                    "Zoning-Fläche " + fertig.Spalten + "x" + fertig.Reihen);
                return true;
            }

            if (primaerGedrueckt)
            {
                var getroffen = ZoningUnterZeiger();
                /*
                 * MESSPUNKT, kein Schmuck. Der Nutzer meldete, eine
                 * gesetzte Flaeche lasse sich nicht mehr verschieben, und
                 * ich konnte es statisch nicht nachvollziehen. Statt weiter
                 * zu raten sagt der naechste Versuch mit Live-Log, ob der
                 * Druck ueberhaupt ankommt und ob er eine Flaeche trifft.
                 */
                if (ParkingGeometry.LiveAn)
                    ParkingGeometry.Live("  zoning druck | flaechen "
                        + _zoningflaechen.Count + " | getroffen " + getroffen
                        + " | auswahl " + ZoningAuswahl
                        + " | zeiger " + ParkingLotLiveLog.Zahl(zeiger.x)
                        + "/" + ParkingLotLiveLog.Zahl(zeiger.y));
                if (getroffen >= 0)
                {
                    // Ein Druck auf eine Flaeche WAEHLT sie und beginnt
                    // zugleich das Verschieben. Zwei Bedienschritte fuer
                    // dasselbe Ziel waeren einer zuviel.
                    _zoningAuswahl = getroffen;
                    _uiSystem?.SetZoningAuswahl(getroffen);
                    _zoningSchiebtVorher = CaptureUndoState();
                    _zoningSchiebt = getroffen;
                    // Der ABSTAND wird gemerkt, nicht die Zeigerposition.
                    _zoningGriff = _zoningflaechen[getroffen].Ecke - zeiger;
                    // Neuer Zug, neue Entscheidung: die alte Rasterstelle
                    // gehoert zum vorigen Vorgang.
                    _zoningRastGesetzt = false;
                    _zoningMauszielGesetzt = false;
                    return true;
                }
                /*
                 * WIE WEIT DER ZEIGER VON DER NAECHSTEN FLAECHE WEG WAR.
                 *
                 * Befund des Nutzers: *"Manchmal werden auch weitere ZFs
                 * erstellt von einer in die andere hinein."* Die Vermutung
                 * dazu: was er als zur Flaeche gehoerig SIEHT (ihre Kacheln)
                 * ist groesser als das, was als Flaeche ZAEHLT (ihr
                 * gezogenes Rechteck) - ein Klick auf eine Kachel daneben
                 * beginnt dann einen neuen Zug.
                 *
                 * Diese Zeile misst genau das: getroffen wurde nichts, aber
                 * wie nah war die naechste Flaeche? Sind es regelmaessig
                 * wenige Meter, ist die Vermutung bestaetigt.
                 */
                if (ParkingGeometry.LiveAn && _zoningflaechen.Count > 0)
                {
                    var naechste = float.PositiveInfinity;
                    foreach (var f in _zoningflaechen)
                        naechste = math.min(naechste, ParkingGeometry.ZoningRechteckAbstand(
                            ParkingGeometry.ZoningEcken(f),
                            new[] { zeiger, zeiger, zeiger, zeiger }));
                    ParkingGeometry.Live("  zoning neuer zug | naechste "
                        + "Flaeche " + ParkingLotLiveLog.Zahl(naechste)
                        + " m entfernt");
                }
                var erste = ParkingGeometry.ZoningAusZug(
                    zeiger, zeiger, ZoningWinkel, out _, out _);
                if (!ZoningLiegtImUmriss(erste)
                    || ZoningUeberlapptAndere(erste, -1))
                {
                    // Schon die kleinste Parzelle passt hier nicht. Lieber
                    // gar nicht anfangen als einen Zug beginnen, der nie
                    // etwas ergeben kann.
                    _uiSystem?.SetStatus(T(
                        "Hier ist kein Platz für eine Parzelle.",
                        "No room for a parcel here."));
                    return true;
                }
                _zoningZugStart = zeiger;

                _zoningZug = erste;
                MeldeZug(_zoningZug, false, false);
                return true;
            }
            return true;
        }

        /**
         * Was gerade unter dem Zeiger entsteht - waehrend des Ziehens.
         *
         * Die Bedienung sagt: *"Waehrend du ziehst, zeigt das Panel
         * 3 x 3 Parzellen - 24 x 24 m."* Die Meter stehen dabei, weil
         * Parzellen allein keine Groesse sind, die man im Gelaende
         * abschaetzen kann.
         */
        private void MeldeZug(ParkingGeometry.Zoningflaeche f,
                              bool breiteGekappt, bool tiefeGekappt)
        {
            var meter = ParkingGeometry.Zoningparzelle;
            var text = f.Spalten + " × " + f.Reihen + " "
                + T("Parzellen", "parcels") + " · "
                + (f.Spalten * meter).ToString("0") + " × "
                + (f.Reihen * meter).ToString("0") + " m";
            if (breiteGekappt || tiefeGekappt)
                text += "  —  " + T(
                    "mehr geht nicht: CS2 baut höchstens "
                        + ParkingGeometry.ZoningMaxBreite + " × "
                        + ParkingGeometry.ZoningMaxTiefe + " Parzellen",
                    "that is the limit: CS2 builds at most "
                        + ParkingGeometry.ZoningMaxBreite + " by "
                        + ParkingGeometry.ZoningMaxTiefe + " parcels");
            _uiSystem?.SetZoningZug(text);
        }

        /**
         * Wieviel von einem gewuenschten Versatz ist erlaubt?
         *
         * Zuerst der ganze. Passt der nicht, wird er in seine beiden
         * Achsen zerlegt und jede einzeln versucht. Dadurch GLEITET die
         * Flaeche an der Umrisskante entlang, statt hart stehenzubleiben -
         * sonst muesste man beim Verschieben exakt parallel zur Kante
         * ziehen, was mit der Maus kaum gelingt.
         *
         * Die Flaechenachsen bleiben ein Kandidat. Zusaetzlich werden die
         * Richtungen der wirklichen Hinderniskanten versucht: Umriss,
         * Randzoning und andere ZF. Nur deren Tangente beschreibt an einer
         * schraeg zur Flaeche stehenden Wand ein echtes Gleiten.
         */
        /**
         * KEINE FLAECHE DARF EINE ANDERE UEBERLAPPEN.
         *
         * Geprueft werden die GEZOGENEN Rechtecke, nicht die freigehaltenen
         * Bereiche. Zuerst hatte ich die Korridore mitgeprueft - damit
         * hielten zwei Flaechen so viel Abstand, dass zwischen ihnen zwei
         * Strassen Platz gehabt haetten. Der Nutzer will es anders: *"Genau
         * wie am Rand des Parkplatzes duerfen die Zoningflaechen bis auf die
         * innere Zoningflaeche aneinander ran, nicht nur Strasse bis
         * Strasse."*
         *
         * Beruehren sich zwei Parzellenrechtecke, ist dort kein Platz mehr
         * fuer eine Strasse - und dann entfaellt sie dort, dieselbe Regel
         * wie am Parkplatzrand. Das entscheidet die Netzplanung, nicht diese
         * Pruefung.
         *
         * Trennachsentest ueber beide Rechteckrichtungen. Die Rechtecke sind
         * gedreht, ein Vergleich der Achsengrenzen genuegt also nicht.
         */
        private bool ZoningUeberlapptAndere(
            ParkingGeometry.Zoningflaeche f, int ausser)
        {
            var meine = ParkingGeometry.ZoningEcken(f);
            for (var i = 0; i < _zoningflaechen.Count; i++)
            {
                if (i == ausser) continue;
                var andere = ParkingGeometry.ZoningEcken(_zoningflaechen[i]);
                if (ParkingGeometry.ZoningRechteckeUeberlappen(meine, andere))
                    return true;
            }
            return false;
        }

        /**
         * Werkzeugadapter fuer das Tetris-Rasten im reinen Geometriekern.
         *
         * Nur gleich gedrehte Nachbarinnen teilen ein 8-m-Gitter. An einem
         * schraegen Parkplatzrand kann statt eines vollen Rasterpunkts eine
         * Kontaktlinie gewinnen: quer dazu ist der Abstand dann exakt 0/8/16
         * oder 24 m, entlang der Naht bleibt die ZF am erreichbaren Anker.
         * CS2-Wissen und Verbotsgruende bleiben hier im Werkzeug.
         */
        private static readonly float ZoningRastreichweite
            = (float)(3 * ParkingGeometry.Zoningparzelle);

        /** Eine Zulassungspruefung fuer Rastziel und Bewegung. */
        private bool ZoningZielErlaubt(
            ParkingGeometry.Zoningflaeche f, int ausser, float2[] umriss)
        {
            if (!_closed || umriss == null || umriss.Length < 3) return false;
            return ZoningLiegtImUmriss(f, umriss, Randgruentiefe, false)
                && HaeltRandzoningAbstand(f)
                && !ZufahrtTrifftFlaeche(f)
                && !ZoningUeberlapptAndere(f, ausser);
        }

        private float2 RasteAnNachbarn(
            ParkingGeometry.Zoningflaeche f,
            float2 mauswunsch,
            float2 erreichbarerVersatz,
            int ausser)
        {
            var umriss = _points.ToArray();
            bool Erlaubt(ParkingGeometry.Zoningflaeche kandidat)
                => ZoningZielErlaubt(kandidat, ausser, umriss);
            var nachbarn = new List<ParkingGeometry.Zoningflaeche>();
            for (var i = 0; i < _zoningflaechen.Count; i++)
                if (i != ausser) nachbarn.Add(_zoningflaechen[i]);

            var alt = _zoningRastGesetzt
                ? (float2?)_zoningRastZiel : null;
            var rastung = ParkingGeometry.ZoningAnNachbarnRasten(
                f, mauswunsch, erreichbarerVersatz, nachbarn, Erlaubt, alt,
                ZoningRastreichweite);

            if (rastung.Gefunden)
            {
                _zoningRastZiel = f.Ecke + rastung.Versatz;
                _zoningRastGesetzt = true;
            }
            else _zoningRastGesetzt = false;

            if (ParkingGeometry.LiveAn && rastung.NachbarnInReichweite > 0)
            {
                var (laengs, quer) = ParkingGeometry.ZoningRichtungen(f.Winkel);
                var text = rastung.Gefunden
                    ? (rastung.Achsen == 2 ? "voll" : "Kontaktlinie")
                        + " | ziel GUELTIG | Ankerkorrektur "
                        + ParkingLotLiveLog.Zahl(
                            math.sqrt(rastung.Korrekturquadrat), 3)
                        + " m | Mausabstand "
                        + ParkingLotLiveLog.Zahl(
                            math.sqrt(rastung.Mausabstandquadrat), 3) + " m"
                    : "KEIN gueltiges Rasterziel | Gleiten";
                ParkingGeometry.Live("  zoning rasten | anker u/v "
                    + ParkingLotLiveLog.Zahl(
                        math.dot(erreichbarerVersatz, laengs), 2) + "/"
                    + ParkingLotLiveLog.Zahl(
                        math.dot(erreichbarerVersatz, quer), 2)
                    + " m | " + text
                    + " | Nachbarn " + rastung.NachbarnInReichweite
                    + " | Kandidaten " + rastung.Angeboten
                    + ": zu weit " + rastung.ZuWeit
                    + ", verboten " + rastung.Verboten
                    + " | Pruefungen " + rastung.Pruefungen
                    + (rastung.HatVerbotene
                        ? " | bester verbotener Kandidat "
                            + ParkingLotLiveLog.Zahl(
                                math.sqrt(rastung.KleinsteVerbotene), 3)
                            + " m vom Anker: " + ZoningVerbotsgruende(
                                f, rastung.VerboteneEcke - f.Ecke,
                                ausser, umriss)
                        : string.Empty));
            }
            return rastung.Versatz;
        }

        /** Baut die echten Gleitbasen einmal fuer den reinen Begrenzungskern. */
        private List<ParkingGeometry.ZoningBewegungsachse> ZoningBewegungsachsen(
            ParkingGeometry.Zoningflaeche f, float2 wunsch, int ausser,
            float2[] umriss)
        {
            var achsen = new List<ParkingGeometry.ZoningBewegungsachse>();
            void Hinzu(float2 richtung, string name)
            {
                if (math.lengthsq(richtung) < 1e-9f) return;
                richtung = math.normalize(richtung);
                foreach (var alt in achsen)
                    if (math.abs(math.dot(alt.Richtung, richtung)) > 0.99999f)
                        return;
                achsen.Add(new ParkingGeometry.ZoningBewegungsachse(
                    richtung, name));
            }

            var (laengs, _) = ParkingGeometry.ZoningRichtungen(f.Winkel);
            Hinzu(laengs, "ZF");
            foreach (var linie in _randzoning)
                Hinzu(linie.B - linie.A, "Randzoning");

            var mitte = ParkingGeometry.ZoningMitte(f);
            var halbeDiagonale = 0.5f * math.length(new float2(
                (float)(f.Spalten * ParkingGeometry.Zoningparzelle),
                (float)(f.Reihen * ParkingGeometry.Zoningparzelle)));
            var reichweite = halbeDiagonale + math.length(wunsch) + 8f;
            for (var i = 0; i < umriss.Length; i++)
            {
                var a = umriss[i];
                var b = umriss[(i + 1) % umriss.Length];
                if (ParkingGeometry.ZoningPunktStrecke(mitte, a, b)
                    > reichweite) continue;
                Hinzu(b - a, "Umriss " + i);
            }
            for (var i = 0; i < _zoningflaechen.Count; i++)
            {
                if (i == ausser) continue;
                var (nachbarLaengs, _) = ParkingGeometry.ZoningRichtungen(
                    _zoningflaechen[i].Winkel);
                Hinzu(nachbarLaengs, "ZF " + i);
            }
            return achsen;
        }

        private float2 ErlaubterVersatz(
            ParkingGeometry.Zoningflaeche f, float2 wunsch, int ausser,
            bool rasterBefreiung)
        {
            var umriss = _points.ToArray();
            bool Erlaubt(ParkingGeometry.Zoningflaeche kandidat)
                => ZoningZielErlaubt(kandidat, ausser, umriss);
            var messung = ParkingGeometry.ZoningVersatzBegrenzen(
                f, wunsch, Erlaubt,
                () => ZoningBewegungsachsen(f, wunsch, ausser, umriss),
                rasterBefreiung,
                ParkingGeometry.ZoningMaxBreite
                    + ParkingGeometry.ZoningMaxTiefe);

            if (ParkingGeometry.LiveAn && messung.AusgangUngueltig)
            {
                ParkingGeometry.Live("  zoning befreien | "
                    + (messung.Befreiungsring > 0
                        ? "Ring " + messung.Befreiungsring + " | "
                        : string.Empty)
                    + messung.Pruefungen + " geprueft"
                    + (messung.BudgetErschoepft
                        ? " | Budget 1500 aufgebraucht" : string.Empty));
            }
            if (ParkingGeometry.LiveAn
                && !messung.AusgangUngueltig
                && messung.Restquadrat > 1e-8f)
            {
                var achse = math.lengthsq(messung.Basisachse) > 1e-9f
                    ? messung.Basisachse
                    : ParkingGeometry.ZoningRichtungen(f.Winkel).Laengs;
                var quer = new float2(-achse.y, achse.x);
                ParkingGeometry.Live("  zoning gleiten | ziel verboten "
                    + ZoningVerbotsgruende(f, wunsch, ausser, umriss)
                    + " | basis " + messung.Basis
                    + " | wunsch a/b "
                    + ParkingLotLiveLog.Zahl(math.dot(wunsch, achse), 3)
                    + "/" + ParkingLotLiveLog.Zahl(math.dot(wunsch, quer), 3)
                    + " m | erlaubt a/b "
                    + ParkingLotLiveLog.Zahl(
                        math.dot(messung.Versatz, achse), 3)
                    + "/" + ParkingLotLiveLog.Zahl(
                        math.dot(messung.Versatz, quer), 3)
                    + " m | rest "
                    + ParkingLotLiveLog.Zahl(math.sqrt(messung.Restquadrat), 3)
                    + " m | Pruefungen " + messung.Pruefungen);
            }
            return messung.Versatz;
        }

        /**
         * WARUM DAS ZIEL VERWEIGERT WURDE - und zwar immer.
         *
         * Vorher stieg diese Meldung beim haeufigsten Fall stumm aus: war das
         * Ziel ausserhalb des Umrisses, kehrte sie ohne eine Zeile zurueck.
         * Im Log stand deshalb nur, DASS nichts ging.
         *
         * Der Nutzer hat am 2026-09-04 berichtet, der Reibwiderstand trete
         * *"immer nur in eine Richtung"* auf. Der damalige Log enthielt nur
         * Betraege und konnte das Vorzeichen nicht belegen. Ob die Wand aus
         * Randgruen, RZ-Abstand, Zufahrt oder Nachbarin besteht, entscheidet
         * allein der Grund. Also nennt die neue Messung Regel, Basis und beide
         * vorzeichenbehafteten Komponenten statt beim ersten Nein aufzuhoeren.
         */
        private string ZoningVerbotsgruende(
            ParkingGeometry.Zoningflaeche f, float2 wunsch, int ausser,
            float2[] umriss)
        {
            var probe = ParkingGeometry.ZoningVerschoben(f, wunsch);
            var gruende = new List<string>();
            if (!ZoningLiegtImUmriss(
                    probe, umriss, Randgruentiefe, false))
                gruende.Add("Umriss/Randgruen");
            if (!HaeltRandzoningAbstand(probe)) gruende.Add("RZ-Abstand");
            if (ZufahrtTrifftFlaeche(probe)) gruende.Add("Zufahrt");
            if (ZoningUeberlapptAndere(probe, ausser)) gruende.Add("andere ZF");
            return gruende.Count == 0
                ? "KEIN Grund gefunden - das ist ein Fehler"
                : string.Join(" + ", gruende);
        }

        /**
         * DIE FLAECHE MUSS GANZ IM UMRISS LIEGEN - sonst faellt sie weg.
         *
         * Ansage des Nutzers: *"Wenn die Linie verschoben wird, ist es
         * moeglich, die Zoning-Flaeche ausserhalb des Polygons zu haben.
         * Wenn das passiert (auch nur ein Stueck), dann die Flaeche
         * loeschen. Ja, ich weiss, ist abfuck fuer den User, aber Zoning
         * sollte sowieso zum Schluss passieren."*
         *
         * Vier Ecken drin zu pruefen genuegt NICHT: bei einem konkaven
         * Umriss kann eine Polygonkante quer durch das Rechteck laufen,
         * waehrend alle vier Ecken innen liegen. Deshalb zusaetzlich: keine
         * Rechteckkante darf eine Polygonkante kreuzen.
         */
        /**
         * Das Randgruen bleibt frei - es ist die aeussere Kante des
         * Parkplatzes und geht der Zoning-Flaeche VOR.
         *
         * Ansage des Nutzers am 2026-09-02: *"Am besten machen wir auch das
         * Maximum von der Zoning-Flaeche bis zum Randgruen, nicht bis zur
         * Polygongrenze. Und wenn Aussen oder Beides angeklickt wurde, darf
         * auch ausserhalb des Polygons bzw. auf dem Rand nichts entstehen.
         * Also Rand geht sogar ueber ZF."*
         *
         * Randstrasse und Randbuchten duerfen dagegen weichen - sie machen
         * der Flaeche Platz und binden sich an die Zoning-Strasse an.
         */
        private double Randgruentiefe =>
            _uiSystem?.CurrentSettings()?.Es ?? 1.0;

        /**
         * DAS RANDZONING ZAEHLT MIT.
         *
         * Ohne Randzoning reicht eine Flaeche bis ans Randgruen. Wo welches
         * gesetzt ist, endet sie eine Kachel vor der RZ-Strasse - dort
         * entsteht das Bauland des Randzonings, und beide sollen sich EINE
         * Strasse teilen statt sich zu ueberlagern.
         *
         * Die Regel gilt damit in BEIDE Richtungen: `RaeumeFuerRandzoning`
         * schiebt eine bestehende Flaeche aus dem Weg, wenn Randzoning
         * dazukommt; diese Zeile haelt eine neue Flaeche fern, wenn das
         * Randzoning schon steht. Der Nutzer hat beide Faelle genannt, und
         * bis eben war nur der erste gebaut.
         */
        /**
         * WELCHE PRUEFUNG ABLEHNT, STEHT IM LIVE-LOG.
         *
         * Drei Gruende koennen eine Flaeche verbieten, und von aussen sehen
         * alle drei gleich aus: die Flaeche bewegt sich nicht. Genau daran
         * habe ich am 2026-09-04 zweimal die falsche Ursache vermutet. Wer
         * den Live-Log anhat, sieht ab jetzt den Namen der Pruefung.
         */
        private bool ZoningLiegtImUmriss(ParkingGeometry.Zoningflaeche f)
        {
            if (!_closed || _points.Count < 3) return Nein("kein Umriss");
            if (!ZoningLiegtImUmriss(f, _points.ToArray(), Randgruentiefe))
                return Nein("Umriss/Randgruen");
            if (!HaeltRandzoningAbstand(f)) return Nein("Randzoning-Abstand");
            // Und keine Zufahrt darf darunter liegen: ein Haus auf der
            // Einfahrt waere derselbe Fehler wie eine Bucht unter einem
            // Haus, nur andersherum.
            if (ZufahrtTrifftFlaeche(f)) return Nein("Zufahrt im Weg");
            return true;

            bool Nein(string grund)
            {
                if (ParkingGeometry.LiveAn)
                    ParkingGeometry.Live("  zoning verboten | " + grund
                        + " | ecke " + ParkingLotLiveLog.Zahl(f.Ecke.x)
                        + "/" + ParkingLotLiveLog.Zahl(f.Ecke.y)
                        + " | " + f.Spalten + "x" + f.Reihen);
                return false;
            }
        }

        /**
         * Dieselbe Pruefung gegen einen ANDEREN Umriss.
         *
         * Gebraucht wird sie, um einen Umriss zu pruefen, den es noch nicht
         * gibt: waehrend am Polygon gezogen wird, muss die Frage lauten
         * "waere das noch erlaubt?", nicht "war es erlaubt?".
         */
        private static bool ZoningLiegtImUmriss(
            ParkingGeometry.Zoningflaeche f, float2[] umriss,
            double randgruen = 0.0, bool protokoll = true)
        {
            if (umriss == null || umriss.Length < 3) return false;

            /*
             * GEPRUEFT WIRD DAS GEZOGENE RECHTECK - die PARZELLEN.
             *
             * Ich hatte das am 2026-09-02 kurzzeitig auf `EckenMitRand`
             * umgestellt, weil die Zoning-Strasse draussen landete. Das war
             * die falsche Haelfte: damit stoppt die Flaeche viel zu frueh
             * und der Nutzer verliert Bauland, das er haben will.
             *
             * Seine Regel von Anfang an: *"Ich moechte es so, dass die
             * Parzellen richtig an den Polygonrand koennen - da passt dann
             * auch keine Strasse mehr dazwischen, selbst die Zoning-Strasse
             * nicht."* Und nach dem Test: *"Ich meine die innere Flaeche,
             * nicht innere Flaeche + Strasse. Die innere Flaeche gilt als
             * Kontaktpunkt."*
             *
             * Nicht die Flaeche weicht der Strasse, sondern die STRASSE
             * weicht: sie wird gekappt, wo sie nicht mehr hineinpasst.
             * Das passiert in `ZoningRingstuecke`, nicht hier.
             */
            var ecken = ParkingGeometry.ZoningEcken(f);
            foreach (var ecke in ecken)
                if (!PointInPolygonInclusive(ecke, umriss))
                    return UmrissNein("Ecke ausserhalb", protokoll);
            for (var i = 0; i < 4; i++)
            {
                var a1 = ecken[i];
                var a2 = ecken[(i + 1) % 4];
                for (var k = 0; k < umriss.Length; k++)
                    if (StreckenKreuzen(a1, a2,
                            umriss[k], umriss[(k + 1) % umriss.Length]))
                        return UmrissNein("Kante kreuzt Umriss", protokoll);
            }

            /*
             * MINDESTABSTAND STATT VERSETZTEM POLYGON.
             *
             * Ein nach innen versetzter Umriss waere bei konkaven Formen
             * heikel - Ecken kippen um, Kanten verschwinden. Der Abstand
             * jeder Rechteckkante zu jeder Polygonkante beantwortet dieselbe
             * Frage ohne diese Fallen.
             */
            if (randgruen > 0)
            {
                var grenze = randgruen - 1e-3;
                for (var i = 0; i < 4; i++)
                {
                    var a1 = ecken[i];
                    var a2 = ecken[(i + 1) % 4];
                    for (var k = 0; k < umriss.Length; k++)
                        if (StreckenabstandQuadrat(a1, a2, umriss[k],
                                umriss[(k + 1) % umriss.Length])
                            < grenze * grenze)
                            return UmrissNein(
                                $"unter {randgruen:F1} m Randgruen an Kante {k}",
                                protokoll);
                }
            }
            return true;
        }

        /**
         * WELCHER DER DREI TEILE ABGELEHNT HAT.
         *
         * Der Live-Log sagte bisher nur "Umriss/Randgruen" - das sind aber
         * drei verschiedene Regeln, und sie fuehren zu drei verschiedenen
         * Ursachen. Nach dem Lauf des Nutzers vom 2026-09-04 war klar, DASS
         * diese Pruefung ablehnt; welche der drei, war es nicht.
         */
        private static bool UmrissNein(string grund, bool protokoll = true)
        {
            if (protokoll && ParkingGeometry.LiveAn)
                ParkingGeometry.Live("    umriss nein | " + grund);
            return false;
        }

        /** Kleinster quadrierter Abstand zwischen zwei Strecken. */
        private static double StreckenabstandQuadrat(
            float2 a1, float2 a2, float2 b1, float2 b2)
        {
            if (StreckenKreuzen(a1, a2, b1, b2)) return 0.0;
            return math.min(
                math.min(PunktStreckeQuadrat(a1, b1, b2),
                    PunktStreckeQuadrat(a2, b1, b2)),
                math.min(PunktStreckeQuadrat(b1, a1, a2),
                    PunktStreckeQuadrat(b2, a1, a2)));
        }

        private static double PunktStreckeQuadrat(float2 p, float2 a, float2 b)
        {
            var ab = b - a;
            var laenge = math.lengthsq(ab);
            if (laenge < 1e-12f) return math.distancesq(p, a);
            var t = math.clamp(math.dot(p - a, ab) / laenge, 0f, 1f);
            return math.distancesq(p, a + ab * t);
        }

        /**
         * WUERDE DIESER UMRISS ALLE PARZELLEN NOCH TRAGEN?
         *
         * Der zweite der beiden Faelle, die der Nutzer verhindert haben
         * will: *"Wir haben 2 Faelle, die verhindert werden muessen: das
         * Verschieben der Flaeche und das Aendern des Polygons selbst durch
         * Linien/Polygon-Punkte."* Der Zug am Polygon wird also abgelehnt,
         * solange er eine Parzelle hinausschieben wuerde - der Umriss
         * klemmt dann an dieser Kante, statt die Flaeche zu verlieren.
         */
        internal bool ZoningVertraegtUmriss(float2[] umriss)
        {
            if (_zoningflaechen.Count == 0) return true;
            for (var i = 0; i < _zoningflaechen.Count; i++)
                if (!ZoningLiegtImUmriss(_zoningflaechen[i], umriss,
                        Randgruentiefe))
                {
                    if (ParkingGeometry.LiveAn)
                        ParkingGeometry.Live("  zoning sperrt umriss | "
                            + "flaeche " + i + " laege draussen");
                    return false;
                }
            return true;
        }

        /** Der Umriss mit EINEM ersetzten Punkt - ohne ihn anzufassen. */
        internal bool ZoningVertraegtPunkt(int index, float2 neu)
        {
            if (_zoningflaechen.Count == 0) return true;
            var probe = _points.ToArray();
            if (index < 0 || index >= probe.Length) return true;
            probe[index] = neu;
            return ZoningVertraegtUmriss(probe);
        }

        /**
         * Wirft alle Flaechen weg, die nicht mehr ganz im Umriss liegen.
         *
         * Laeuft nach JEDER Umrissaenderung, nicht nur nach dem Verschieben
         * eines Punktes: auch Ausstuelpen, Kanten schieben und Rueckgaengig
         * koennen den Umriss unter einer Parzelle wegziehen.
         */
        private void PruefeZoningflaechenImUmriss()
        {
            if (_zoningflaechen.Count == 0) return;
            var weg = 0;
            for (var i = _zoningflaechen.Count - 1; i >= 0; i--)
            {
                if (ZoningLiegtImUmriss(_zoningflaechen[i])) continue;
                _zoningflaechen.RemoveAt(i);
                weg++;
            }
            if (ParkingGeometry.LiveAn)
                ParkingGeometry.Live("  zoning umrisspruefung | "
                    + _zoningflaechen.Count + " geblieben, " + weg
                    + " verworfen");
            if (weg == 0) return;
            _zoningZug = null;
            _zoningSchiebt = -1;
            _zoningHover = -1;
            _zoningAuswahl = 0;
            _layoutDirty = _closed;
            _geometryRevision++;
            var parzellen = _zoningflaechen.Sum(f => f.Parzellen);
            _uiSystem?.SetZoningZahlen(_zoningflaechen.Count, parzellen);
            _uiSystem?.SetZoningAuswahl(ZoningAuswahl);
            _uiSystem?.SetStatus(T(
                weg + " Zoning-Fläche(n) gelöscht: nicht mehr ganz im Umriss.",
                weg + " zoning patch(es) deleted: no longer fully inside."));
            Mod.log.Info("PLT-Zoning: " + weg
                + " Flaeche(n) verworfen, weil der Umriss sich darunter "
                + "veraendert hat.");
        }

        /**
         * Einmal je Bild: hat sich der Umriss geaendert?
         *
         * Ein eigener Stand statt `_geometryRevision`, weil die auch bei
         * jeder Zoning-Aenderung hochzaehlt - dann pruefte sich die
         * Pruefung selbst und liefe bei jedem Verschieben mit.
         */
        private int _zoningUmrissstand = int.MinValue;

        internal void ZoningFolgeDemUmriss()
        {
            var stand = _closed ? 17 : 31;
            for (var i = 0; i < _points.Count; i++)
                stand = stand * 31
                    + _points[i].x.GetHashCode() * 7
                    + _points[i].y.GetHashCode();
            if (stand == _zoningUmrissstand) return;
            _zoningUmrissstand = stand;
            // AUFGEBROCHEN heisst WEG. Ohne geschlossenen Umriss gibt es
            // kein Innen, also auch keine Parzellen - der Nutzer wollte das
            // ausdruecklich so.
            if (!_closed)
            {
                if (_zoningflaechen.Count != 0 || _randzoning.Count != 0 || ZoningModus)
                    VergissZoningflaechen();
                return;
            }
            RandzoningFolgeDemUmriss();
            PruefeZoningflaechenImUmriss();
        }

        /** Der Schnappschuss vom Beginn eines Verschiebens. */
        private ParkingLotUndoSnapshot _zoningSchiebtVorher;

        private void NachZoningaenderung(
            ParkingLotUndoSnapshot vorher, string was)
        {
            /*
             * OHNE DAS RECHNET DIE VORSCHAU NICHT NEU.
             *
             * `_geometryRevision` sagt "die Geometrie hat sich geaendert",
             * aber erst `_layoutDirty` beauftragt `StartBuildIfNeeded` mit
             * einem neuen Lauf. Bei den Trennschnitten steht die Zeile seit
             * jeher (`NachTrennaenderung`), hier fehlte sie - und der Nutzer
             * sah genau das: Flaeche gesetzt, Panel zaehlt 30 Parzellen,
             * Parkplatz unveraendert. Alles gerechnet, nur nie angestossen.
             */
            _layoutDirty = _closed;
            _geometryRevision++;
            CommitUndoState(vorher, was);
            var parzellen = _zoningflaechen.Sum(f => f.Parzellen);
            _uiSystem?.SetZoningZahlen(_zoningflaechen.Count, parzellen);
            _uiSystem?.SetStatus(T(
                _zoningflaechen.Count + " Zoning-Fläche(n), "
                    + parzellen + " Parzellen.",
                _zoningflaechen.Count + " zoning patch(es), "
                    + parzellen + " parcels."));
            Mod.log.Info("PLT-Zoning: " + was + "; jetzt "
                + _zoningflaechen.Count + " Flaeche(n), " + parzellen
                + " Parzelle(n).");
        }
    }
}
