using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    public enum BayKind
    {
        Perimeter,
        InnerPerimeter,
        Inner,
        Extra,
    }

    public enum BayRole
    {
        Normal,
        Disabled,
        Electric,
    }

    /**
     * WAS FUER EINE ZUFAHRT DAS IST.
     *
     * Bis zum 2026-08-27 gab es nur eine Sorte, und `Entrance` brauchte
     * deshalb kein Merkmal dafuer. Jetzt sind es vier, und sie unterscheiden
     * sich in Breite, Netzprefab und Fahrtrichtung.
     *
     * `Zufahrt` steht bewusst an erster Stelle: der Standardwert eines nicht
     * gesetzten Feldes ist damit die alte Sorte. Spielstaende und
     * Bauprotokolle von vorher lesen sich so unveraendert weiter, ohne dass
     * irgendwo eine Umrechnung noetig waere.
     */
    public enum Zufahrtsart
    {
        /** Zweispurig, beide Richtungen - die urspruengliche Sorte. */
        Zufahrt,
        /** Einspurig hinein: von der Strasse in den Parkplatz. */
        Einfahrt,
        /** Einspurig hinaus: vom Parkplatz auf die Strasse. */
        Ausfahrt,
        /** Nur Fussweg, kein Auto. Am schmalsten. */
        Fussweg,
        /**
         * Wie `Zufahrt`, aber als unsichtbare GASSE an die Stadtstrasse
         * gebaut statt als unsichtbarer Weg danebengelegt.
         *
         * Der Unterschied ist einzig, dass CS2 eine Strasse an einer
         * Strasse TEILT und dabei einen Knoten setzt - und nur ein Knoten
         * oeffnet den Bordstein. Gemessen am 2026-09-17: unsichtbarer
         * Gassenklon an einer 16-m-Strasse, Knoten mit drei Kanten, die
         * urspruengliche Kante ersetzt.
         *
         * Geometrisch ist sie mit `Zufahrt` identisch. Nur `ParkingLotNetBuilder`
         * unterscheidet sie, und nur beim aeusseren Stueck.
         */
        Gasse,
        /**
         * Einspurige Gasse HINEIN: von der Stadtstrasse in den Parkplatz.
         *
         * Wie `Gasse`, aber aus der Vanilla-"Alley Oneway" gebaut und damit
         * gerichtet. Die Richtung entsteht aus der Reihenfolge der
         * Endpunkte - dieselbe Regel wie bei `Einfahrt`.
         *
         * HINTEN ANGEHAENGT, und das muss so bleiben: der Bauzettel
         * speichert die Art als Zahl.
         */
        GasseEin,
        /** Einspurige Gasse HINAUS: vom Parkplatz auf die Stadtstrasse. */
        GasseAus,
    }

    /**
     * DIE FRAGEN AN EINE ZUFAHRTSART, JEDE AN GENAU EINER STELLE.
     *
     * Ohne diese Klasse stand dieselbe Aufzaehlung im Netzbau, in der
     * Vorflaeche, bei den Pfeilen und in der Zugangspruefung - und beim
     * Anlegen der einspurigen Gassen am 2026-09-18 wurden davon drei von
     * vier vergessen. Wer eine Art hinzufuegt, aendert von hier an nur noch
     * die Antworten hier.
     */
    internal static class Zufahrtsarten
    {
        /** Alle Gassenarten - sie werden als geteilte Strasse gebaut. */
        internal static bool IstGasse(Zufahrtsart art)
            => art == Zufahrtsart.Gasse || art == Zufahrtsart.GasseEin
               || art == Zufahrtsart.GasseAus;

        /** Eine Fahrtrichtung statt zweier. */
        internal static bool Einspurig(Zufahrtsart art)
            => art == Zufahrtsart.Einfahrt || art == Zufahrtsart.Ausfahrt
               || art == Zufahrtsart.GasseEin || art == Zufahrtsart.GasseAus;

        /**
         * Faehrt vom Parkplatz zur Strasse.
         *
         * Entscheidet ueberall die Reihenfolge der Endpunkte: der Kurs im
         * Netzbau, die Richtung des Fahrbahnpfeils.
         */
        internal static bool FaehrtHinaus(Zufahrtsart art)
            => art == Zufahrtsart.Ausfahrt || art == Zufahrtsart.GasseAus;

        /** Name fuers Log. */
        internal static string Name(Zufahrtsart art)
        {
            switch (art)
            {
                case Zufahrtsart.Einfahrt: return "Einfahrt";
                case Zufahrtsart.Ausfahrt: return "Ausfahrt";
                case Zufahrtsart.Fussweg: return "Fussweg";
                case Zufahrtsart.Gasse: return "Gasse";
                case Zufahrtsart.GasseEin: return "Gasse rein";
                case Zufahrtsart.GasseAus: return "Gasse raus";
                default: return "Zufahrt";
            }
        }
    }

    public sealed class Entrance
    {
        /** Gemessener Gesamtquerschnitt des einspurigen Vanilla-Weges. */
        internal const double EinspurigeBreite = 4.0;
        /** Gemessener Gesamtquerschnitt des reinen Vanilla-Fusswegs. */
        internal const double Fusswegbreite = 2.0;

        public int Edge { get; set; }
        public double Along { get; set; }
        public string Corner { get; set; }
        public Zufahrtsart Art { get; set; } = Zufahrtsart.Zufahrt;

        /**
         * Hat der Nutzer diesen Zugang gesetzt? Standard ja - alles, was aus
         * den Einstellungen kommt, ist gesetzt. Nur der ringlose Plan haengt
         * eigene Randfusswege an und markiert die als nicht gesetzt.
         */
        internal bool Gesetzt { get; set; } = true;

        /** Faehrt hier ueberhaupt ein Auto? Der Fussweg ist die Ausnahme. */
        internal bool FuerAutos => Art != Zufahrtsart.Fussweg;

        /** Einspurig sind Ein- und Ausfahrt und die gerichteten Gassen. */
        internal bool Einspurig => Zufahrtsarten.Einspurig(Art);

        /** Alle Gassenarten - sie werden als geteilte Strasse gebaut. */
        internal bool IstGasse => Zufahrtsarten.IstGasse(Art);

        /**
         * Die Flaeche folgt dem gemessenen Netzquerschnitt. `normaleBreite`
         * bleibt ein Parameter, damit die bestaetigte alte Zufahrt weiterhin
         * exakt dem Ai-Regler folgt.
         */
        internal double Breite(double normaleBreite, double gassenbreite = 0)
        {
            /*
             * JEDE GASSE ZUERST, AUCH DIE GERICHTETE.
             *
             * Zwei Gruende, und beide zaehlen:
             *
             * Sonst faenge `Einspurig` die gerichteten Gassen ab und gaebe
             * ihnen die Breite einer gewoehnlichen Einfahrt - sie sind aber
             * echte Strassen und bringen ihr Mass vom Prefab mit.
             *
             * Und alle drei teilen sich EINE Zahl. Getrennte Werte koennen
             * auseinanderlaufen, und das sieht man sofort: liegt der Belag
             * schmaler als die Fahrbahn, fahren die Autos ueber das Gras.
             * Seit alle drei aus derselben "Alley" entstehen, gibt es
             * keinen Grund fuer einen zweiten Wert.
             */
            if (IstGasse && gassenbreite > 0) return gassenbreite;
            if (Einspurig) return EinspurigeBreite;
            if (Art == Zufahrtsart.Fussweg) return Fusswegbreite;
            return normaleBreite;
        }

        internal Entrance Clone() => new Entrance
        {
            Edge = Edge, Along = Along, Corner = Corner, Art = Art,
        };
    }

    public sealed class LayoutSection
    {
        public double L { get; internal set; }
        public int Bays { get; internal set; }
        public double Cap0 { get; internal set; }
        public double Cap1 { get; internal set; }
        public string End0 { get; internal set; }
        public string End1 { get; internal set; }
    }

    /** Welcher Teilbereichslauf Buchten und Fahrwege beigesteuert hat. */
    public sealed class LayoutPassInfo
    {
        public bool Half { get; internal set; }
        public double Deg { get; internal set; }
        public double Phase { get; internal set; }
        public int PartIndex { get; internal set; }
        public double Area { get; internal set; }
        public int Stalls { get; internal set; }
        public int Aisles { get; internal set; }
        public int Crossings { get; internal set; }
        public int RawCrossings { get; internal set; }
    }

    /** Herkunft einer logischen Verbindung am erzeugenden Teilbereich. */
    public sealed class LayoutCrossRouteInfo
    {
        public float2[] Line { get; internal set; } = Array.Empty<float2>();
        public int PartIndex { get; internal set; }
        public double PartArea { get; internal set; }
        public int PartCrossings { get; internal set; }
        public int TargetIndex { get; internal set; }
        public double Deg { get; internal set; }
    }

    public sealed class SpecialStallCounts
    {
        public int Behindert { get; internal set; }
        public int Elektro { get; internal set; }
    }

    /**
     * Eine vom Werkzeug gesetzte Bezugsrichtung fuer eine Teilflaeche.
     *
     * Der Anker ist das Merkmal. Eine laufende Nummer waere nach der
     * naechsten Polygonaenderung bedeutungslos, weil die Zerlegung dabei neu
     * entsteht. Der Winkel ist bereits der von der Bezugslinie gelieferte
     * Rueckfall; Modus und Quer-Drehung werden weiterhin ausschliesslich in
     * <see cref="ParkingGeometry.Reihenwinkel"/> daraus abgeleitet.
     */
    public sealed class TeilflaechenAusrichtung
    {
        public float2 Anker { get; set; }
        public double Winkel { get; set; }

        internal TeilflaechenAusrichtung Clone() => new TeilflaechenAusrichtung
        {
            Anker = Anker,
            Winkel = Winkel,
        };
    }

    /** Gemessene Ausgabe eines inneren Teilflaechenrasters. */
    public sealed class TeilflaechenBauInfo
    {
        public int Index { get; internal set; }
        public double Winkel { get; internal set; }
        public int Innenbuchten { get; internal set; }
        public bool EigeneZuweisung { get; internal set; }
    }

    public sealed class LayoutSettings
    {
        public double Es { get; set; } = 1;
        public double Ai { get; set; } = 7;
        public double Cw { get; set; } = 3;
        public double Sl { get; set; } = 5.5;
        public double Sw { get; set; } = 3;
        public double Md { get; set; } = 2.5;
        /**
         * Breite des Belags auf JEDER Zufahrtsgasse, in Metern.
         *
         * Eine Zahl fuer alle drei Arten - siehe `Entrance.Breite`.
         *
         * Das Werkzeug misst die Gasse am Prefab, zieht die Bordsteinluft ab
         * und setzt das Ergebnis hier ein. Die 5,5 sind nur der Rueckfall
         * fuer den Fall, dass das Prefab noch nicht bereit ist; der
         * Rechenkern selbst darf das Spiel nicht fragen.
         */
        public double Gassenbreite { get; set; } = 5.5;

        public double Cr { get; set; } = 34;
        public bool Qk { get; set; } = true;
        public bool Randstrassen { get; set; } = true;
        public bool Auto { get; set; } = true;
        public double Angle { get; set; }
        public string AngleMode { get; set; }

        /**
         * Die vom Nutzer gewaehlte Bezugsrichtung in Grad, oder `null`.
         *
         * Ist sie gesetzt, ersetzt sie die laengste Kante als Nullpunkt - und
         * zwar fuer den GANZEN Parkplatz. Ohne sie holt sich jede Teilflaeche
         * ihre eigene laengste Kante; das bleibt der Normalfall.
         *
         * Gespeichert wird der WINKEL, nicht die Kantennummer. Nummern
         * ueberleben das Loeschen eines Punktes nicht - daran sind in diesem
         * Projekt schon die Zugaenge gescheitert -, und der Nutzer hat
         * ausdruecklich entschieden: verschwindet die Kante, bleibt die
         * Richtung, bis er sie selbst verwirft.
         */
        public double? Ausrichtwinkel { get; set; }
        /**
         * Geordnete Zuweisungen des Werkzeugs. Leer bedeutet den bisherigen
         * Bauweg Zeichen fuer Zeichen unveraendert. Der erste Eintrag ist die
         * Vorgabe fuer alle Teilflaechen ohne eigenen Treffer.
         */
        public TeilflaechenAusrichtung[] TeilflaechenAusrichtungen { get; set; }
            = Array.Empty<TeilflaechenAusrichtung>();

        /**
         * Die von Hand gezogenen Trennschnitte.
         *
         * Ist auch nur einer gesetzt, gilt ALLEIN er - die automatische
         * Zerlegung wird dann nicht mehr befragt. Siehe
         * `TeilflaechenAusSchnitten`.
         */
        /**
         * Die gezogenen Baulandflaechen.
         *
         * Sie gehen in den PLAN, nicht in eine Nachkorrektur: ihre Kanten
         * werden Rasterlinien, bevor eine Zelle eingeordnet wird.
         */
        public ParkingGeometry.Zoningflaeche[] Zoningflaechen { get; set; }
            = Array.Empty<ParkingGeometry.Zoningflaeche>();

        /**
         * Die Polygonlinien, an denen Randzoning entsteht.
         *
         * Gespeichert werden die beiden ENDPUNKTE der Linie, nicht ihre
         * Nummer. Die Nummerierung des Umrisses ueberlebt keine Bearbeitung -
         * das hat schon die Zuordnung der Ausrichtungen gelehrt
         * ([[plt-zuordnung-ueber-merkmal]]); zwei Punkte tun es.
         *
         * WAS DARAUS WIRD: die Randstrasse in diesem Abschnitt wird zur
         * Zoning-Strasse. Bauland entsteht nur AUSSERHALB des Polygons - der
         * Nutzer am 2026-09-03: *"Nur nach aussen, denn der User kann innen
         * ZF nutzen."* Nach innen bleibt der Parkplatz, wie er ist.
         */
        public ParkingGeometry.RandzoningLinie[] Randzoning { get; set; }
            = Array.Empty<ParkingGeometry.RandzoningLinie>();

        /**
         * Welches Vanilla-Strassenprefab die Zoning-Strasse nachbildet.
         *
         * Gebaut wird nie dieses Prefab selbst, sondern ein unsichtbarer
         * Klon davon. Der Name entscheidet nur ueber Vorbild und Breite.
         * Am 2026-09-02 gemessen: Gasse und Schotterstrasse sind beide
         * 8,00 m breit, die Geometrie aendert sich also nicht mit der Wahl.
         */
        public string Zoningstrasse { get; set; } = "Alley";

        public Teilflaechenschnitt[] Teilflaechenschnitte { get; set; }
            = Array.Empty<Teilflaechenschnitt>();
        public Entrance[] Entrances { get; set; } = Array.Empty<Entrance>();
        /**
         * Der Browser-Generator behaelt seine Automatik. Das Werkzeug setzt
         * diesen Schalter dagegen auf false, damit auch ein leeres Array
         * wirklich "noch keine Zufahrt" bedeutet und nicht heimlich wieder
         * die laengsten Kanten waehlt.
         */
        public bool AutomaticEntrances { get; set; } = true;

        /**
         * Versatz gegen den exakten Kantenwinkel, in Grad. Normalfall null.
         *
         * `edge` setzt die Reihenrichtung EXAKT auf den Winkel der laengsten
         * Arealkante. Dann laufen Reihe und Kante parallel - und ein
         * Reststreifen zwischen beiden ist ueber seine ganze Laenge GLEICH
         * breit. Trifft es unguenstig und er ist ein Zehntelmillimeter breit,
         * dann ist er das ueber hundert Meter, und die Streifenzerlegung
         * schneidet ihn an jeder Bucht erneut durch.
         *
         * Gemessen am Nutzerbau vom 2026-08-19 (298 Buchten, Reihenwinkel
         * 93,330 gegen Kantenwinkel 93,327 Grad): 2407 Grasringe, davon 12
         * unter CS2s Mindestkante. Mit 0,01 Grad Versatz: 16 Ringe, keiner zu
         * duenn, gleiche Buchtenzahl.
         *
         * Pauschal darf der Versatz nicht sein - 0,01 Grad brach zwei der vier
         * festen Faelle, 0,002 Grad noch einen. Deshalb nur als AUSNAHME, wenn
         * das Ergebnis sie braucht; siehe BuildImpl.
         */
        internal double KantenVersatz { get; set; }

        /**
         * WELCHER RECHENWEG.
         *
         * `false` ist der bisherige: Belag bauen, Gruen als "Grundstueck minus
         * Belag" schneiden, und die 21 Reparaturfunktionen raeumen hinterher
         * auf. Gemessen an der schraegen L-Form des Nutzers braucht er 12,5
         * MINUTEN reine Rechenzeit; nur das 7-s-Budget bricht ihn vorher ab,
         * weshalb dort im Spiel gar keine Vorschau erscheint.
         *
         * `true` ist der Zellenweg: das Areal wird in konvexe Teile zerlegt,
         * je Teil im Reihenkoordinatensystem als Raster aus Baendern und
         * Zellen gebaut, und Material ist ein Etikett je Zelle statt ein
         * Schnittergebnis. Weil Nachbarzellen IDENTISCHE Punkte teilen,
         * entfaellt jede Toleranz - und damit die Reparatur.
         *
         * In drei Machbarkeitsstudien gemessen (Tests/Zellenversuch):
         * dieselbe Form 15,4 ms, 385 Buchten, 0,00 % ungedeckt, 0 Toleranzen.
         *
         * Beide Wege stehen nebeneinander, umschaltbar im Panel. Nichts wird
         * geloescht, solange der neue nicht durchgehend besser ist.
         */
        internal bool Zellen { get; set; }

        /**
         * Beide Flaechenarten benutzen dasselbe Prefab.
         *
         * Dann ist die Aufteilung in Belag und Gruen sinnlos: es kaeme
         * ueberall dasselbe Material heraus, nur in 18 bis 41 Einzelstuecken
         * statt in einem. Ansage des Nutzers am 2026-08-22: *"wir koennen
         * statt mehrere kleine Flaechen einfach eine grosse machen. Das ist
         * mal eine Ausnahme."*
         *
         * Gesetzt wird die Fahne im UI-System, wo die beiden Prefabnamen
         * bekannt sind - die Geometrie kennt nur die Folge.
         */
        internal bool EineFlaeche { get; set; }

        internal bool NoNotch { get; set; }
        internal bool Single { get; set; }
        internal bool NoHalf { get; set; }

        internal LayoutSettings Clone()
        {
            return new LayoutSettings
            {
                Es = Es,
                Ai = Ai,
                Cw = Cw,
                Sl = Sl,
                Sw = Sw,
                Md = Md,
                Cr = Cr,
                Qk = Qk,
                Randstrassen = Randstrassen,
                Auto = Auto,
                Angle = Angle,
                AngleMode = AngleMode,
                Ausrichtwinkel = Ausrichtwinkel,
                TeilflaechenAusrichtungen = TeilflaechenAusrichtungen?
                    .Where(x => x != null).Select(x => x.Clone()).ToArray()
                    ?? Array.Empty<TeilflaechenAusrichtung>(),
                Teilflaechenschnitte = Teilflaechenschnitte?
                    .Where(x => x != null).Select(x => x.Clone()).ToArray()
                    ?? Array.Empty<Teilflaechenschnitt>(),
                /*
                 * WER HIER EIN FELD VERGISST, BAUT EINEN STUMMEN FEHLER.
                 *
                 * Genau das ist mir am 2026-09-02 passiert: das Feld war
                 * angelegt, durchgereicht und ausgewertet - aber `Clone`
                 * kannte es nicht, und der Zellenkern bekam eine leere
                 * Liste. Im Spiel sah es aus, als taete die Baulandflaeche
                 * gar nichts. Gemessen: 348 Buchten mit und ohne Flaeche,
                 * in allen sechs Faellen identisch.
                 */
                Zoningflaechen = Zoningflaechen?
                    .Where(x => x != null).Select(x => x.Clone()).ToArray()
                    ?? Array.Empty<ParkingGeometry.Zoningflaeche>(),
                // MITKOPIEREN - genau hier ist die Baulandflaeche einmal
                // stumm verlorengegangen, siehe der Kommentar darueber.
                Randzoning = Randzoning?
                    .Where(x => x != null).Select(x => x.Clone()).ToArray()
                    ?? Array.Empty<ParkingGeometry.RandzoningLinie>(),
                Zoningstrasse = Zoningstrasse,
                Entrances = Entrances?.Select(x => x?.Clone()).ToArray() ?? Array.Empty<Entrance>(),
                AutomaticEntrances = AutomaticEntrances,
                KantenVersatz = KantenVersatz,
                Zellen = Zellen,
                EineFlaeche = EineFlaeche,
                NoNotch = NoNotch,
                Single = Single,
                NoHalf = NoHalf,
            };
        }

        /**
         * GEMESSENE CS2-Masse, Stand 2026-08-01, aus dem Laufzeit-Dump:
         * `Invisible Road Path - 2xTwoway 2xPerpendicular` besteht aus
         * 2 x 3,0 m Fahrgasse + 2 x 0,5 m Gehstreifen + 2 x 5,5 m Parken
         * = 18,00 m. `m_DefaultWidth` meldet irrefuehrend nur 7,00 m, weil es
         * bei unsichtbaren Wegen nur den befahrbaren Kern meint.
         *
         * 18,00 m ist der MINDESTABSTAND, nicht der vorgeschriebene. md=0
         * loeschte am Rechteck gemessen 487 m2 Gruen fuer 26 Buchten mehr;
         * deshalb bleibt md=2,5. Die Bucht ist gemessen 3,0 x 5,5 m mit
         * SlotInterval 3,0, also ohne Fuge. es und cr gibt CS2 nicht vor.
         */
        public static LayoutSettings Cs2 => new LayoutSettings
        {
            Es = 1,
            Ai = 7,
            Cw = 3,
            Sl = 5.9,
            Sw = 3,
            Md = 2.5,
            Cr = 34,
            Qk = true,
            Auto = true,
            Angle = 0,
            /*
             * DER ZELLENWEG IST DER RECHENWEG.
             *
             * Bis zum 2026-09-01 stand hier nichts, `Zellen` blieb also
             * `false` - und damit hat der HAUPTLAUF des Paritaetstests den
             * alten Rechenweg gemessen, waehrend der Mod mit
             * `Engine = "zellen"` ausgeliefert wird. Die Zahlen, an denen
             * jede Aenderung geprueft wurde, beschrieben einen Weg, den
             * niemand faehrt. Genau deshalb blieb der Winkelfehler vom
             * 2026-08-31 im Test unsichtbar und fiel erst dem Nutzer auf.
             *
             * Auch die Rueckfaelle im Mod (`?? LayoutSettings.Cs2`) zeigen
             * damit auf denselben Weg wie der ausgelieferte Standard.
             */
            Zellen = true,
        };

        // Masse im Frame sind Fuss: Bucht 9 x 18 ft, Fahrgasse etwa 24 ft,
        // Setback 10 ft.
        public static LayoutSettings ReferenceFrame => new LayoutSettings
        {
            Es = 3,
            Ai = 7.3,
            Cw = 3,
            Sl = 5.5,
            Sw = 2.7,
            Md = 4,
            Cr = 60,
            Qk = true,
            Auto = true,
            Angle = 0,
        };

        /**
         * Zweispuriges Gegenstueck `Alley - Double Sided Parking`, 24,00 m:
         * 2 x 3,0 Fahrbahn + 2 x 1,0 Schulter + 2 x 8,0 Parkstreifen.
         * Der Streifen ist 8,0 m tief, die Bucht nur 6,5 m. Die 1,5 m
         * Ueberschuss braucht die Schraegvariante:
         * 6,5 * sin 67 + 2,9 * cos 67 = 7,11 m.
         */
        public static LayoutSettings Cs2TwoLane => new LayoutSettings
        {
            Es = 1,
            Ai = 8,
            Cw = 3,
            Sl = 8,
            Sw = 3,
            Md = 0,
            Cr = 34,
            Qk = true,
            Auto = true,
            Angle = 0,
        };
    }

    /**
     * Ein Fahrweg-Stueck der Netzfassung. `Kind` ist "perimeter", "aisle",
     * "cross" oder "entrance" und entscheidet nur noch ueber die Wegbreite.
     */
    /**
     * Ein Ablehnungsgrund mit Anzahl und erster Fundstelle. Beantwortet die
     * Frage "warum ist hier keine Bucht?" mit einer Zahl statt einer Vermutung.
     */
    public readonly struct RejectInfo
    {
        public RejectInfo(string reason, int count, float2 first)
        {
            Reason = reason;
            Count = count;
            First = first;
        }

        public string Reason { get; }
        public int Count { get; }
        public float2 First { get; }
    }

    public readonly struct NetSegment
    {
        public NetSegment(string kind, float2 a, float2 b,
                          Zufahrtsart art = Zufahrtsart.Zufahrt)
        {
            Kind = kind;
            A = a;
            B = b;
            Art = art;
        }

        public string Kind { get; }
        public float2 A { get; }
        public float2 B { get; }
        public Zufahrtsart Art { get; }
    }

    public sealed class ParkingLayout
    {
        public float2[][] Bay { get; internal set; } = Array.Empty<float2[]>();
        public BayKind[] BayKind { get; internal set; } = Array.Empty<BayKind>();
        public BayRole[] BayRole { get; internal set; } = Array.Empty<BayRole>();
        /**
         * Je Eintrag die beiden Buchten, die sich eine Ladesaeule teilen.
         *
         * Die Paarung muss mitgereicht werden, statt sie spaeter aus der Lage
         * zu erraten: liegen vier Elektroplaetze am Stueck, sind (0,1)+(2,3)
         * und (1,2) geometrisch nicht zu unterscheiden - die Saeule stuende
         * dann falsch.
         */
        public int2[] ElectricPair { get; internal set; } = Array.Empty<int2>();
        public float2[][] Cap { get; internal set; } = Array.Empty<float2[]>();
        public string[] CapKind { get; internal set; } = Array.Empty<string>();
        public float2[][] Median { get; internal set; } = Array.Empty<float2[]>();
        public float2[][] Green { get; internal set; } = Array.Empty<float2[]>();
        public float2[][] Fill { get; internal set; } = Array.Empty<float2[]>();
        public float2[][] FillHole { get; internal set; } = Array.Empty<float2[]>();
        public float2[][] CrossPavement { get; internal set; } = Array.Empty<float2[]>();
        public float2[][] GrassSurface { get; internal set; } = Array.Empty<float2[]>();
        public float2[][] AsphaltSurface { get; internal set; } = Array.Empty<float2[]>();

        /**
         * Der Boden unter den Zoning-Parzellen - eine eigene Liste.
         *
         * Nicht in `GrassSurface` mit hinein: der Nutzer waehlt dafuer eine
         * eigene Flaeche, und wer die beiden zusammenwirft, kann sie beim
         * Bauen nicht mehr trennen.
         */
        public float2[][] ZoningSurface { get; internal set; } = Array.Empty<float2[]>();

        /**
         * Der Belag der Zoning-Strasse.
         *
         * Getrennt von `AsphaltSurface`, weil er ein anderes Prefab braucht:
         * unsere Flaechen sind Decals auf der Ebene `Terrain` und werden auf
         * einer Strasse unsichtbar - auch auf einer unsichtbaren. Gebaut
         * wird er mit demselben `Terrain | Roads`-Klon wie die Vorflaeche.
         */
        public float2[][] ZoningRoadSurface { get; internal set; }
            = Array.Empty<float2[]>();

        /**
         * WELCHE ZONING-STRASSEN SIND DIE RANDZONING-STRASSEN?
         *
         * Die Achsen der Kurse, die aus einem Randzoning-Abschnitt entstanden
         * sind - in Weltkoordinaten, in der Reihenfolge, in der sie gebaut
         * werden.
         *
         * WARUM DAS OFFENSTEHEN MUSS. Bis zum 2026-09-10 erkannte das Werkzeug
         * eine RZ-Strasse an ihrer LAGE: parallel zur gewaehlten Kante und
         * hoechstens 15 m davon entfernt (`RandzoningEnthaelt`). Solange die
         * Strasse eigens erzeugt wurde, stimmte das immer - sie lief 10,4 m
         * parallel zur Kante.
         *
         * Seit die naechstliegende FAHRGASSE die RZ-Strasse ist, stimmt es
         * nicht mehr: an einer schraegen Kante steht sie rund 34 Grad zu ihr.
         * Der Parallel-Test schlug fehl, die Strasse galt nicht als
         * Randzoning, und die Regel "Innenseite immer aus" griff nicht. Der
         * Nutzer sah es sofort: *"Auffaellig, dass die Tiles nach innen gehen
         * statt aussen."*
         *
         * Raten ersetzt man nicht durch besseres Raten. Der Plan weiss es;
         * hier steht es.
         *
         * `Innen` ist der Einheitsvektor VON der Strasse IN den Parkplatz -
         * also die Seite, auf der KEINE Kacheln liegen sollen.
         *
         * Auch das war vorher geraten, naemlich ueber die Polygonmitte:
         * *"liegt der Schwerpunkt links oder rechts von der Fahrtrichtung?"*
         * Bei EINER Strasse entlang einer Kante geht das immer gut. Bei einer
         * TREPPE nicht mehr - eine Stufe kann so weit innen liegen, dass der
         * Schwerpunkt auf der falschen Seite von ihr steht, und dann zont sie
         * nach innen in den Parkplatz hinein. Der Nutzer im Spiel: *"Nicht
         * alle Tiles gehen nach aussen, da findet keine ordentliche Pruefung
         * statt."*
         *
         * Der Abschnitt weiss es ohne Raten: `Innen` steht senkrecht auf der
         * Gasse und zeigt von der bedienten Kante weg.
         */
        public (float2 A, float2 B, float2 Innen)[] RandzoningRoad
        { get; internal set; }
            = Array.Empty<(float2 A, float2 B, float2 Innen)>();
        /**
         * Konstruktive Materialgrenzen hinter der Ein-Flächen-Ausgabe.
         *
         * Sind beide Prefabs gleich, darf CS2 eine einzige Fläche bekommen.
         * Sobald nur ein Setzschalter an ist, werden dagegen diese beiden
         * unveränderten Listen gebraucht; sonst würde ein Schalter ins Leere
         * zeigen und der andere das ganze Areal beherrschen.
         */
        internal float2[][] GrassSurfaceByRole { get; set; }
        internal float2[][] AsphaltSurfaceByRole { get; set; }

        /**
         * DIE FLAECHEN, AUF DIE BEPFLANZUNG GEHOERT.
         *
         * Nicht zu verwechseln mit dem, was `SurfacesForPlacement` liefert.
         * Die beantwortet "was soll BELEGT werden" und gibt bei
         * abgeschalteter Dekoration mit Recht nichts zurueck.
         *
         * Genau daran hing die Bepflanzung bis zum 2026-09-15 mit. Der
         * Nutzer: *"Wenn ich die Dekoration-Flaeche ausschalte geht die
         * Vegetation nicht mehr, das ist natuerlich Mist weil trotzdem
         * Sachen gesetzt werden sollen."*
         *
         * Er hat recht: die Gruenflaechen sind weiterhin da, sie bekommen nur
         * keinen Belag. Ein Baum braucht keinen Belag unter sich.
         *
         * `GrassSurfaceByRole` ist nur gefuellt, wenn beide Belaege dasselbe
         * Prefab sind und CS2 deshalb EINE Flaeche bekommt - dann trennt erst
         * diese Liste wieder nach Rolle. Sonst ist `GrassSurface` schon rein
         * nach Rolle. Deshalb der Rueckfall.
         */
        public float2[][] GrassForVegetation
            => GrassSurfaceByRole ?? GrassSurface;

        internal void SurfacesForPlacement(
            bool road,
            bool decoration,
            out float2[][] grass,
            out float2[][] asphalt)
        {
            if (road && decoration)
            {
                grass = GrassSurface;
                asphalt = AsphaltSurface;
                return;
            }

            grass = decoration
                ? GrassSurfaceByRole ?? GrassSurface
                : Array.Empty<float2[]>();
            asphalt = road
                ? AsphaltSurfaceByRole ?? AsphaltSurface
                : Array.Empty<float2[]>();
        }
        public float2[][] PerimeterQuad { get; internal set; } = Array.Empty<float2[]>();
        public float2[][] EntranceQuad { get; internal set; } = Array.Empty<float2[]>();
        /**
         * Die Art je Zufahrtsrechteck, gleiche Reihenfolge wie `EntranceQuad`.
         *
         * Leer heisst "alles Zufahrt" - so verhaelt sich der klassische
         * Rechenkern, der die Arten nicht kennt. Die Vorschau faerbt dann wie
         * vorher, statt auf eine fehlende Zuordnung hereinzufallen.
         */
        public int[] EntranceQuadArt { get; internal set; } = Array.Empty<int>();
        public float2[][] AisleLine { get; internal set; } = Array.Empty<float2[]>();
        public float2[][] AisleQuad { get; internal set; } = Array.Empty<float2[]>();
        public float2[][] CrossLine { get; internal set; } = Array.Empty<float2[]>();
        /** Logische Querungen vor der vorgeschriebenen Teilung an hoeheren Fahrwegen. */
        public float2[][] CrossRouteLine { get; internal set; } = Array.Empty<float2[]>();
        public float2[][] CrossQuad { get; internal set; } = Array.Empty<float2[]>();
        public float2[][] PerimeterLine { get; internal set; } = Array.Empty<float2[]>();
        public float2[][] EntranceLine { get; internal set; } = Array.Empty<float2[]>();

        /**
         * Die Fahrwege fuer das STRASSENNETZ, an jeder Kreuzung geteilt und
         * bis zur Mittellinie des getroffenen Weges gefuehrt. Nur hieraus
         * baut `ParkingLotNetBuilder` seine Kurse. Die Linien darueber
         * bleiben die Grundlage fuer den BELAG und enden weiterhin an der
         * Fahrbahnkante.
         */
        public NetSegment[] NetLine { get; internal set; } = Array.Empty<NetSegment>();

        /**
         * Je Bauteil: `Reason` ist der Bauteilname, `Count` die Zahl der
         * Versuche, `First.x` die Zahl der gesetzten. Ein eigener Typ waere
         * sauberer, aber diese Ausgabe liest nur das Log.
         */
        public RejectInfo[] RejectTotals { get; internal set; } = Array.Empty<RejectInfo>();

        /** Die Ablehnungsgruende, haeufigster zuerst. */
        public RejectInfo[] Rejects { get; internal set; } = Array.Empty<RejectInfo>();
        public Entrance[] Entrances { get; internal set; } = Array.Empty<Entrance>();
        public float2[] Ring { get; internal set; } = Array.Empty<float2>();
        public LayoutSection[] Sections { get; internal set; } = Array.Empty<LayoutSection>();
        public LayoutPassInfo[] PassInfo { get; internal set; } = Array.Empty<LayoutPassInfo>();
        public LayoutCrossRouteInfo[] CrossRouteInfo { get; internal set; } =
            Array.Empty<LayoutCrossRouteInfo>();
        public int Stalls { get; internal set; }
        public int PerimeterStalls { get; internal set; }
        public int InnerPerimeterStalls { get; internal set; }
        public int InnerStalls { get; internal set; }
        public int ExtraStalls { get; internal set; }
        public SpecialStallCounts SpecialStalls { get; internal set; } = new SpecialStallCounts();
        public double Angle { get; internal set; }
        public int Aisles { get; internal set; }
        public int Parts { get; internal set; }
        public int NotchAisles { get; internal set; }
        public int Crossings { get; internal set; }
        /** Winkel und innere Buchten je formabhaengiger Teilflaeche. */
        public TeilflaechenBauInfo[] Teilflaechen { get; internal set; }
            = Array.Empty<TeilflaechenBauInfo>();
        /** Zahl der konstruktiv entlang einer Teilflaechennaht gebauten Wege. */
        public int TeilflaechenVerbindungen { get; internal set; }
        public string[] Warnings { get; internal set; } = Array.Empty<string>();
    }

    public static partial class ParkingGeometry
    {
        // Das Mikrometer Toleranz ist nicht kosmetisch: die Randbuchten werden so
        // gesetzt, dass ihre Aussenkante EXAKT auf der Setback-Linie liegt. Ohne
        // Toleranz entscheidet dort Gleitkomma-Rauschen in der 14. Stelle. Gemessen am
        // Parallelogramm: eine Diagonale verfehlte um 1,15e-14 m und verlor alle 24
        // Buchten, die Gegendiagonale lag zufaellig auf der anderen Seite und behielt
        // ihre 17.
        public const float FitEps = 1e-6f;

        // Einrastziele gelten bis fuenf Grad als kollinear; Modell und Browser teilen
        // exakt dieselbe Schranke.
        private static readonly double EntranceSnapCos = Math.Cos(5 * Math.PI / 180);
        // Wie spitz darf eine konvexe Ecke sein, damit die Zufahrt dort einrasten
        // darf? Bei 30 Grad wird die schraege Zufahrt 1/sin = 2x so lang.
        private static readonly double MinCornerSnapSin = Math.Sin(30 * Math.PI / 180);
        private static readonly double ParallelCos = Math.Cos(20 * Math.PI / 180);
        // WIDERLEGT: Abstand null bei Lotpunkt AUF dem Segment heisst, dass zwei
        // Strassen aufeinanderliegen. Gemessen lag die L-Form sonst 9,3 m darin.
        private const double MergeTol = 0;
        // Mit 30 Grad kostete die Regel den Browser-Fall 30 Buchten (219 -> 189)
        // und liess trotzdem 6,3 m Ueberlappung stehen.
        private const double MaxPass = 2;
        private static readonly double MinJunctionSin = Math.Sin(15 * Math.PI / 180);
        // Wieviel Buchtenzahl darf die knotenaermere Loesung kosten? Gemessen.
        private const double StallTolerance = 0.05;
        // Referenz 08s: drei Plaetze trennen den 1er- vom 4er-Lauf (9,0 m).
        private const int MinInnerRingRun = 3;
        // Im L-Fall verlieren drei 90-Grad-Buchten gegen die von 7 auf 10
        // wachsende Reihe.
        private const int MinTransverseInnerRingRun = 4;
        private const double MinTransverseRowAngle = 75;
        // Mess-Sweep: falsche Treffer 8,53-19,77 Grad, naechste echte Querung 45 Grad.
        private const double MinTransverseCapAngle = 45;
        // Bei 0 Grad greift die Regel auch an den flachen Knicken von Referenz 08s
        // und kostet dort 9 Buchten.
        private const double MinNotchTurn = 60;

        private static string NormalizeCapKind(string kind)
        {
            if (kind == "cut") return "clip";
            return kind == "cross" || kind == "ring" || kind == "clip"
                || kind == "entrance" || kind == "aisle" ? kind : "clip";
        }

        private static void PushCap(WorkLayout output, double2[] cap, string kind)
        {
            output.Cap.Add(cap);
            output.CapKind.Add(NormalizeCapKind(kind));
        }

        /** Zwischenzeiten je Bauphase auf stderr. Nur fuer die Fehlersuche. */
        public static bool PhaseLog;
        /** Wie oft ein spitzes Fahrgassen-Ende rechtwinklig gekappt wurde. */
        /**
         * Wie oft der Zufahrtsstrahl die Ringachse NICHT getroffen hat und
         * ersatzweise der naechste Ringpunkt genommen wurde.
         *
         * Das passiert an Ecken: dort landet der Schnittpunkt genau auf einem
         * Ringpunkt, und durch Rundung liegt der Kantenparameter auf beiden
         * angrenzenden Kanten knapp ausserhalb. Frueher warf das und der Bau
         * schlug fehl; jetzt traegt der Ersatz, aber er ist ein BEFUND.
         * Springt er regelmaessig an, stimmt die Konstruktion davor nicht.
         */
        internal static int RingtrefferErsatz;
        /** Spur der letzten Sonderplatzverteilung, fuer die Fehlersuche. */
        internal static readonly System.Collections.Generic.List<string>
            SonderplatzSpur = new System.Collections.Generic.List<string>();
        internal static double RingtrefferAbstand;

        /**
         * ZWEITER VERSUCH OHNE UHR: GEMESSEN UND VERWORFEN (2026-08-20).
         *
         * Die Idee war, einen Zeitabbruch mit einem Lauf ohne Zeitbegrenzung
         * aufzufangen - im Werkzeug laeuft der Bau nebenlaeufig, ein langer
         * Lauf haette also nur die Vorschau verzoegert.
         *
         * Gemessen an der schraegen L-Form des Nutzers
         * (-65.7,-31;120,-31;120,45;76.2,52.4;60,90;-65.7,90): der Lauf ohne
         * Uhr war nach ZEHN MINUTEN noch nicht fertig. Die Uhr ist also nicht
         * zu streng - sie ist das Einzige, was diesen Ausreisser stoppt. Ein
         * Wiederholungslauf ohne sie haette aus einer fehlenden Vorschau eine
         * dauerhaft rechnende gemacht.
         *
         * Der richtige Weg ist deshalb NICHT "nochmal ohne Uhr", sondern
         * "weniger verfeinern": der Abbruch muss dort, wo er auftritt, in ein
         * gruoberes Ergebnis muenden statt in eine Ausnahme. Solange das nicht
         * gebaut ist, gilt weiter der innere Rueckfall von
         * BuildMaterialSurfaces.
         */
        public static ParkingLayout Build(float2[] site, LayoutSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            /*
             * DER ZELLENWEG IST SEIT DEM 2026-09-01 DER EINZIGE.
             *
             * Hier stand `if (settings.Zellen) return BuildZellen(...)` und
             * darunter der alte Rechenweg. Der war ein Ueberbleibsel aus der
             * Portierung vom Browser-Prototyp; ausgeliefert wird
             * `Engine = "zellen"`, gebaut hat damit niemand mehr.
             *
             * Schlimmer noch: der Paritaetstest hat AUSSCHLIESSLICH den alten
             * Weg gemessen (`LayoutSettings.Cs2` liess `Zellen` auf `false`).
             * Der Winkelfehler vom 2026-08-31 blieb dadurch im Test
             * unsichtbar und fiel erst dem Nutzer im Spiel auf. Ein Waechter,
             * der einen unbenutzten Weg bewacht, gibt eine Sicherheit vor,
             * die es nicht gibt.
             */
            return BuildZellen(site, settings);
        }

        /** Wie viele Materialflaechen unter CS2s Mindestkante liegen. */
        /**
         * DER BAU DARF NIE AN DER MATERIALPHASE STERBEN.
         *
         * Die Materialuhr (7 s, siehe MaterialSurfaceRepair) bricht ab, wenn
         * eine Form zu lange braucht. `BuildMaterialSurfaces` faengt das
         * INNEN ab und liefert dann unreparierte Rueckfallflaechen - aber nur,
         * wenn der Abbruch INNERHALB seines try zuschlaegt. Davor liegen
         * Aufbauschritte (SlabFill fuer den Buchtenbelag, der Belagabzug),
         * und dort fliegt die Ausnahme ungefangen bis nach oben.
         *
         * Ergebnis im Spiel: gar keine Vorschau. Der Nutzer meldete am
         * 2026-08-20, dass groessere L-Formen oft ueberhaupt nicht angezeigt
         * werden - das ist dieser Fall. Gemessen an seiner schraegen L-Form
         * (-65.7,-31;120,-31;120,45;76.2,52.4;60,90;-65.7,90): einmal 405
         * Buchten mit unreparierten Flaechen, einmal gar nichts, je nachdem
         * an welcher Stelle die Uhr zuschlug.
         *
         * WARUM NICHT EINFACH DIE UHR ABSCHALTEN und neu rechnen: sie
         * schuetzt vor dem Einfrieren. Am 2026-08-17 blieb `Build` bei einem
         * Fuenfeck mit 16.430 m2 haengen. Ein zweiter Versuch ohne Uhr koennte
         * dort wieder haengen, und ein eingefrorenes Spiel ist schlimmer als
         * ein unfertiger Parkplatz.
         *
         * Deshalb der Mittelweg: bleibt nichts uebrig, geht das Layout OHNE
         * Materialflaechen hinaus. Buchten, Fahrwege und Zufahrten sind da,
         * nur Gras und Belag fehlen. Sichtbar und unvollstaendig schlaegt
         * unsichtbar.
         */
        private static void SichereMaterialflaechen(
            WorkLayout work, LayoutSettings settings)
        {
            try
            {
                BuildMaterialSurfaces(work, settings);
            }
            catch (Exception ausnahme)
            {
                work.GrassSurface = new List<double2[]>();
                work.AsphaltSurface = new List<double2[]>();
                var hinweis = "Materialphase abgebrochen: " + ausnahme.Message
                    + "; das Layout geht ohne Gras- und Belagflaechen hinaus.";
                if (!work.Warnings.Contains(hinweis)) work.Warnings.Add(hinweis);
                System.Diagnostics.Trace.TraceWarning(hinweis);
                if (PhaseLog)
                {
                    Console.Error.WriteLine("      [Notausgang] " + hinweis);
                    Console.Error.Flush();
                }
            }
        }

        private static int UnbaubareFlaechen(WorkLayout work)
        {
            var zahl = 0;
            foreach (var ring in work.GrassSurface.Concat(work.AsphaltSurface))
            {
                var hals = MinimumSurfaceFeature(new List<double2[]> { ring });
                if (hals != null && hals.Distance < SurfaceMinEdge) zahl++;
            }
            return zahl;
        }


        /**
         * Ein Stueck der Netzfassung. `Kind` bestimmt spaeter nur noch die
         * Wegbreite ("cross" schmal, alles andere breit); die Geometrie steht
         * schon fest, inklusive der Teilung an jeder Kreuzung.
         */
        /**
         * WARUM ETWAS NICHT GEBAUT WURDE.
         *
         * Der Nutzer sieht im Spiel nur das Ergebnis - eine Bucht, die fehlt,
         * hinterlaesst keine Spur. Am 2026-08-17 kostete genau das eine Stunde:
         * die ganze aeussere Randreihe fehlte, und erst eine handgebaute Sonde
         * in den `continue`-Zweigen zeigte, dass 124 von 134 Buchten an
         * "liegt im Ring" scheiterten.
         *
         * Diese Zaehlung macht das dauerhaft sichtbar. Sie zaehlt nur, sie
         * entscheidet nichts - faellt sie aus, aendert sich am Layout nichts.
         */
        internal sealed class RejectLog
        {
            // Je BAUTEIL getrennt - Randbuchten und Innenbuchten in einen Topf
            // zu werfen macht die Meldung wertlos.
            internal readonly Dictionary<string, RejectTotal> Totals =
                new Dictionary<string, RejectTotal>(StringComparer.Ordinal);
            internal readonly Dictionary<string, RejectReason> Reasons =
                new Dictionary<string, RejectReason>(StringComparer.Ordinal);

            private RejectTotal Topf(string bauteil)
            {
                if (!Totals.TryGetValue(bauteil, out var t))
                    Totals[bauteil] = t = new RejectTotal();
                return t;
            }

            internal void Try(string bauteil) => Topf(bauteil).Attempted++;

            internal void Ok(string bauteil) => Topf(bauteil).Placed++;

            internal void Note(string bauteil, string grund, double2 wo)
            {
                var key = bauteil + ": " + grund;
                if (!Reasons.TryGetValue(key, out var eintrag))
                    Reasons[key] = eintrag = new RejectReason();
                eintrag.Count++;
                // Die ERSTE Fundstelle merken - damit laesst sich im Spiel
                // hinfliegen und nachsehen.
                if (eintrag.Count == 1) eintrag.First = wo;
            }
        }

        internal sealed class RejectTotal
        {
            internal int Attempted;
            internal int Placed;
        }

        internal sealed class RejectReason
        {
            internal int Count;
            internal double2 First;
        }


        internal sealed class Line2
        {
            internal Line2(double2 a, double2 b) { A = a; B = b; }
            internal double2 A;
            internal double2 B;
        }

        private sealed class WorkLayout
        {
            internal List<double2[]> Bay = new List<double2[]>();
            internal List<BayKind> BayKind = new List<BayKind>();
            internal List<BayRole> BayRole = new List<BayRole>();
            internal List<int2> ElectricPair = new List<int2>();
            internal List<double2[]> Median = new List<double2[]>();
            internal List<double2[]> Cap = new List<double2[]>();
            internal List<string> CapKind = new List<string>();
            internal List<double2[]> Green = new List<double2[]>();
            internal List<double2[]> Fill = new List<double2[]>();
            /**
             * Verworfene Reste der Restfuellung: zu duenn zum Bauen und ohne
             * verbindende Wirkung.
             *
             * Am Nutzerbau vom 2026-08-17 (437 Buchten) waren 176 der 198
             * Restfuellungsteile schmaler als CS2s Mindestkante 0,375 m -
             * Haarrisse parallel zur Reihenrichtung, zusammen 39,3 m2 von
             * 2147 m2. Als eigene Flaechen baut CS2 die dickeren davon als
             * Streifen und verwirft die duenneren; genau das meldete der
             * Nutzer als "Streifen, dazwischen leer".
             *
             * Diese Liste wird nirgends weiterverwendet - sie haelt fest, was
             * absichtlich wegfaellt, damit die Menge messbar bleibt.
             */
            internal List<double2[]> FillThin = new List<double2[]>();
            internal List<double2[]> FillHole = new List<double2[]>();
            internal List<double2[]> CrossPavement = new List<double2[]>();
            internal List<double2[]> GrassSurface = new List<double2[]>();
            internal List<double2[]> AsphaltSurface = new List<double2[]>();
            internal List<double2[]> PerimeterQuad = new List<double2[]>();
            internal List<double2[]> EntranceQuad = new List<double2[]>();
            /** Art je Rechteck; leer heisst "alles Zufahrt". */
            internal List<int> EntranceQuadArt = new List<int>();
            internal List<Line2> PerimeterLine = new List<Line2>();
            internal List<Line2> EntranceLine = new List<Line2>();
            internal List<Line2> AisleLine = new List<Line2>();
            internal List<double2[]> AisleQuad = new List<double2[]>();
            internal List<Line2> CrossLine = new List<Line2>();
            internal List<Line2> CrossRouteLine = new List<Line2>();
            internal readonly RejectLog Rejects = new RejectLog();
            internal List<double2[]> CrossQuad = new List<double2[]>();
            internal List<Entrance> Entrances = new List<Entrance>();
            internal double2[] Ring = Array.Empty<double2>();
            internal List<LayoutSection> Sections = new List<LayoutSection>();
            internal List<LayoutPassInfo> PassInfo = new List<LayoutPassInfo>();
            internal int Stalls;
            internal int PerimeterStalls;
            internal int InnerPerimeterStalls;
            internal int InnerStalls;
            internal int ExtraStalls;
            internal SpecialStallCounts SpecialStalls = new SpecialStallCounts();
            internal List<string> Warnings = new List<string>();
            internal bool SilentMaterialWarnings;
            internal bool AllowThinGrassTransfer;
            /** Dieser Lauf wurde nach verworfenen Verbindungsstrassen neu aufgebaut. */
        }


        private sealed class CrossPolicy
        {
        }


        private sealed class RingParts
        {
            internal double2[] Points;
            internal int[] First;
            internal int[] Last;
        }

        private sealed class Crossing
        {
            internal double T;
            internal double Dn;
            internal double DnRaw;
        }







        private sealed class EntranceFit
        {
            internal double Along;
            internal double2 Direction;
            internal double Projection;
            internal double Length;
        }

        private sealed class Road
        {
            internal Line2 Segment;
            internal double2 U;
            internal bool SupportsRow;
            internal string Kind;
            internal double2[] Quad;
        }

        private sealed class SpatialIndex
        {
            private readonly double _cell;
            private readonly Dictionary<(int, int), List<double2[]>> _map =
                new Dictionary<(int, int), List<double2[]>>();

            internal SpatialIndex(double cell) { _cell = cell; }

            private HashSet<(int, int)> Keys(double2[] q)
            {
                var keys = new HashSet<(int, int)>();
                foreach (var p in q)
                    keys.Add(((int)Math.Floor(p.x / _cell), (int)Math.Floor(p.y / _cell)));
                return keys;
            }

            internal void Add(double2[] q)
            {
                foreach (var key in Keys(q))
                {
                    if (!_map.TryGetValue(key, out var list))
                        _map[key] = list = new List<double2[]>();
                    list.Add(q);
                }
            }

            internal bool Hits(double2[] q)
            {
                var seen = new HashSet<double2[]>(ReferenceEqualityComparer<double2[]>.Instance);
                foreach (var p in q)
                    for (var dx = -1; dx <= 1; dx++)
                        for (var dy = -1; dy <= 1; dy++)
                        {
                            var key = ((int)Math.Floor(p.x / _cell) + dx,
                                       (int)Math.Floor(p.y / _cell) + dy);
                            if (!_map.TryGetValue(key, out var list)) continue;
                            foreach (var other in list)
                                if (seen.Add(other) && QuadsOverlap(q, other)) return true;
                        }
                return false;
            }
        }

        private sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T> where T : class
        {
            internal static readonly ReferenceEqualityComparer<T> Instance =
                new ReferenceEqualityComparer<T>();
            public bool Equals(T x, T y) => ReferenceEquals(x, y);
            public int GetHashCode(T obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
