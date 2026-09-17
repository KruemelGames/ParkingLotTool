using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /**
     * MISST, WAS EIN ZUFAHRTSSTUMMEL AN DER STADTSTRASSE AUSLOEST.
     *
     * Anlass ist die Frage aus dem Reddit-Faden: unsere Zufahrt laesst den
     * Bordstein der Stadtstrasse durchlaufen, eine Gasse durchbricht ihn.
     * Der Nutzer hat von Hand gemessen, dass eine Gasse knapp unter einer
     * Zonenkachel lang sein darf - darunter steht "Ungueltige Form". Was er
     * im Spiel NICHT sehen kann, sind die Dinge, an denen die Entscheidung
     * haengt:
     *
     *  - Wird die Stadtstrasse wirklich GETEILT? Nur eine Teilung erzeugt
     *    den Knoten, und nur der Knoten oeffnet den Bordstein.
     *  - Ab welcher Laenge genau? Das ist die Zahl, die entscheidet, ob eine
     *    Zufahrt in einen schmalen Parkplatz ueberhaupt passt.
     *  - Was kostet die Teilung an Zonenkacheln? Jede Teilung kostet eine
     *    Kachelreihe (siehe cs2-zonenblock-je-kante).
     *
     * Und eine Frage kann ueberhaupt nur Code beantworten: ob ein
     * UNSICHTBARES Strassenprefab denselben Knoten erzeugt. Dafuer gibt es
     * kein Vanilla-Prefab, wohl aber unseren Zoningstrassen-Klon.
     *
     * WARUM INTERVALLHALBIERUNG UND KEINE LEITER. Der erste Entwurf hat
     * feste Laengen durchprobiert - 2, 3, 4, 6, 8, 12 m. Der Nutzer hat
     * sofort gesehen, was daran nicht stimmt: seine eigene Handmessung lag
     * bei "knapp unter 8 m", und genau dort hatte die Leiter 2 m Luecke. Das
     * Ergebnis waere "zwischen 6 und 8" gewesen, also keine Zahl. Jetzt wird
     * halbiert, bis die Schranken 5 cm auseinanderliegen.
     *
     * DER ENTSCHEIDENDE TRICK: fuer all das muss nichts gebaut werden.
     * `CourseSplitSystem` entscheidet ueber die Teilung in der Temp-Phase,
     * und eine Teilung legt Temp-Entities an, deren `Temp.m_Original` auf
     * die Stadtstrasse zeigt. Die Messung liest also den Vorschauzustand und
     * raeumt ihn wieder ab - der Spielstand bleibt unangetastet.
     *
     * Alt+F misst.
     * Alt+Shift+F misst und BAUT danach vier Stummel zum Hinsehen, jeden in
     * der gerade gemessenen Mindestlaenge; nur dafuer wird der Spielstand
     * veraendert.
     *
     * Warum CS2 die Gasse teilt und unseren Weg nicht, steht im Dekompilat:
     * `CourseSplitSystem.CheckCourseIntersections` steigt aus, sobald sich
     * `m_MergeLayers` beider Prefabs nicht schneiden UND `NetUtils.CanConnect`
     * falsch ist. Pathway gegen Road ist genau dieser Fall - deshalb haengt
     * sich unsere Zufahrt heute ueber `LocalConnect` an, ohne Knoten.
     */
    public sealed partial class ParkingLotToolSystem
    {
        private const string BsPrefix = "PLT-Bordsteinsonde:";

        /** Bilder zwischen Anlegen und Ablesen eines Temp-Falls. */
        private const int BsTempFrames = 4;

        /** Bilder nach dem Apply, bevor Knoten und Bloecke stehen. */
        private const int BsBauFrames = 24;

        /** Suchreichweite fuer die Stadtstrasse unter dem Zeiger. */
        private const float BsStrassenreichweite = 80f;

        /** Umkreis, in dem Zonenkacheln gezaehlt werden. */
        private const float BsKachelradius = 90f;

        /**
         * Die sicher lange Seite der Halbierung.
         *
         * Nicht groesser gewaehlt, obwohl es die Suche kaum kostet: je
         * weiter der Stummel in die Landschaft reicht, desto eher scheitert
         * er an etwas anderem als seiner Laenge - ein Haus, ein Baum, ein
         * Hang. Dann waere die obere Schranke gar keine.
         */
        private const float BsObereGrenze = 16f;

        /** Feiner als 5 cm ist fuer eine Zufahrt ohne Bedeutung. */
        private const float BsGenauigkeit = 0.05f;

        /** Notbremse; 16 m auf 5 cm brauchen rechnerisch neun Schritte. */
        private const int BsHoechstschritte = 14;

        private enum BsVariante
        {
            /** Was die Zufahrt heute ist: `Invisible Road Path - 1xTwoway`. */
            Weg,
            /** Unser unsichtbarer Strassenklon aus der Gasse. */
            KlonUnsichtbar,
            /** Die sichtbare Vanilla-Gasse als Vergleichsfall. */
            GasseSichtbar,
        }

        /** Wonach in dieser Messreihe die kuerzeste Laenge gesucht wird. */
        private enum BsKriterium
        {
            /** CS2 nimmt den Kurs ueberhaupt an. */
            Angenommen,
            /** CS2 nimmt ihn an UND teilt dabei die Stadtstrasse. */
            Teilt,
        }

        private enum BsPhase
        {
            Idle,
            /**
             * Das Werkzeug wurde gerade erst geoeffnet; `_letzteWeltposition`
             * steht noch nicht. Diese Phase gibt als einzige FALSCH zurueck -
             * ein wahrer Wert wuerde den Frame reservieren und genau die
             * Stelle ueberspringen, die die Position setzt.
             */
            ZeigerWarten,
            KlonWarten,
            MessAnlegen,
            MessAblesen,
            BauVorbereiten,
            BauAnlegen,
            BauAblesen,
            BauWarten,
            Fertig,
        }

        /**
         * Eine Messreihe sucht EINE Zahl: die kleinste Laenge, bei der ihr
         * Kriterium erfuellt ist.
         *
         * `Schlecht` ist die groesste bekannte Laenge, die es nicht tut,
         * `Gut` die kleinste bekannte, die es tut. Die gesuchte Grenze liegt
         * immer dazwischen.
         */
        private struct BsReihe
        {
            internal BsVariante Variante;
            /**
             * Wahr: der Stummel beginnt auf der MITTELLINIE der Stadtstrasse
             * - so setzt der Nutzer seine Gasse.
             * Falsch: er beginnt am Fahrbahnrand - so bauen wir heute, damit
             * der Knoten eine Sackgasse bleibt und `LocalConnect` greift.
             */
            internal bool AbMitte;
            internal BsKriterium Kriterium;
            internal float Schlecht;
            internal float Gut;
            internal bool ObereGrenzeGeprueft;
            internal int Schritte;
            /** Der Grund, an dem die obere Schranke gescheitert ist. */
            internal string Befund;
        }

        /** Ein Stummel, der wirklich stehenbleibt. */
        private struct BsBaufall
        {
            internal BsVariante Variante;
            internal bool AbMitte;
            internal float Laenge;
            /** Meter entlang der Strasse; trennt die Baufaelle voneinander. */
            internal float Versatz;
            internal string Anlass;
        }

        private BsPhase _bsPhase;
        private int _bsFrame;
        private bool _bsMitBau;
        private Entity _bsDefinition;
        private Entity _bsPrefab;
        private Entity _bsStrasse;
        private float3 _bsA;
        private float3 _bsB;
        private float3 _bsKontakt;
        private float _bsStrassenbreite;
        private int _bsKachelnVorher;
        private float _bsTestlaenge;
        private int _bsReiheIndex;
        private int _bsBauIndex;
        private int _bsMessungen;
        private readonly List<BsReihe> _bsReihen = new List<BsReihe>();
        private readonly List<BsBaufall> _bsBau = new List<BsBaufall>();
        private readonly List<string> _bsZeilen = new List<string>();

        /** Alt+F beziehungsweise Alt+Shift+F. */
        internal void StarteBordsteinsonde(bool mitBau)
        {
            if (_bsPhase != BsPhase.Idle)
            {
                BsMelde("laeuft bereits.");
                return;
            }
            /*
             * NUR AUF LEEREM TISCH.
             *
             * `applyMode` gilt fuer ALLE Temp-Entities des Werkzeugs. Mit
             * einer gezeichneten Vorschau wuerde das Clear nach jedem
             * Messfall sie loeschen - und das Apply der Bauphase wuerde einen
             * ganzen Parkplatz bauen, den niemand bestellt hat.
             */
            if (_points.Count != 0 || _closed || _buildStage != BuildStage.Idle
                || _avPhase != AvPhase.Idle || _zpPhase != ZpPhase.Idle)
            {
                BsMelde("Abbruch - es laeuft noch etwas (Vorschau, Bau, "
                    + "Versorgung oder Zoningsonde). Erst mit Rechtsklick "
                    + "leeren, dann Alt+F.");
                return;
            }
            _bsMitBau = mitBau;
            _bsFrame = 0;
            _bsPhase = BsPhase.ZeigerWarten;
            _uiSystem?.SetStatus(ParkingLotTexte.T(
                "Bordsteinsonde: Maus auf die freie Seite neben die Strasse "
                    + "halten.",
                "Curb probe: hold the mouse on the open side of the road."));
            BsLog("START angefordert; warte auf eine gueltige Zeigerposition.");
        }

        /**
         * Wartet darauf, dass der Zeiger einmal den Boden trifft.
         *
         * Gibt absichtlich FALSCH zurueck: sonst steigt OnUpdate hier aus und
         * erreicht die Stelle nie, die `_letzteWeltposition` setzt.
         */
        private bool BsWarteAufZeiger()
        {
            if (_letzteWeltpositionGueltig)
            {
                BsBeginne();
                return false;
            }
            if (++_bsFrame <= 600) return false;
            BsMelde("Abbruch - der Zeiger hat in 10 Sekunden nicht den Boden "
                + "getroffen. Maus auf freies Gelaende halten und Alt+F "
                + "erneut druecken.");
            _bsPhase = BsPhase.Idle;
            return false;
        }

        /** Der eigentliche Start, sobald Zeiger und Strasse stehen. */
        private void BsBeginne()
        {
            if (!BsSucheStadtstrasse())
            {
                BsMelde($"Abbruch - im Umkreis von {BsStrassenreichweite:F0} m "
                    + "liegt keine Stadtstrasse. Der Zeiger muss neben einer "
                    + "Strasse stehen, die NICHT uns gehoert.");
                _bsPhase = BsPhase.Idle;
                return;
            }

            var mitBau = _bsMitBau;
            _bsZeilen.Clear();
            _bsBau.Clear();
            BsBaueMessreihen();
            _bsReiheIndex = 0;
            _bsBauIndex = 0;
            _bsMessungen = 0;
            _bsFrame = 0;
            _bsDefinition = Entity.Null;
            _bsKachelnVorher = BsZaehleKacheln();

            BsLog($"START ({(mitBau ? "MESSEN UND BAUEN" : "nur messen")}): "
                + $"Stadtstrasse {BsEntity(_bsStrasse)} "
                + $"'{PrefabAssetName(BsPrefabVon(_bsStrasse))}', Breite "
                + $"{_bsStrassenbreite:F2} m, Kontaktpunkt "
                + $"({_bsKontakt.x:F1}/{_bsKontakt.z:F1}); {_bsReihen.Count} "
                + $"Messreihen; {_bsKachelnVorher} gueltige Zonenkachel(n) im "
                + $"Umkreis von {BsKachelradius:F0} m.");
            BsLog($"Gesucht wird je Reihe die KUERZESTE Laenge durch "
                + $"Intervallhalbierung zwischen 0 und {BsObereGrenze:F0} m, "
                + $"bis die Schranken {BsGenauigkeit * 100f:F0} cm "
                + "auseinanderliegen.");
            if (!mitBau)
                BsLog("Es wird NICHTS gebaut - jeder Versuch wird als Temp "
                    + "angelegt, abgelesen und wieder verworfen.");
            _uiSystem?.SetStatus(ParkingLotTexte.T(
                mitBau ? "Bordsteinsonde misst und baut ..."
                       : "Bordsteinsonde misst ...",
                mitBau ? "Curb probe measuring and building ..."
                       : "Curb probe measuring ..."));

            _bsPhase = BsPhase.KlonWarten;
        }

        /**
         * Sechs Reihen: drei Varianten, jede von der Mittellinie und vom
         * Fahrbahnrand aus.
         *
         * Je Reihe werden ZWEI Grenzen gesucht, und sie sind nicht dasselbe:
         * eine Laenge kann angenommen werden, ohne dass CS2 dabei die
         * Stadtstrasse teilt. Fuer den Bordstein zaehlt nur die zweite.
         */
        private void BsBaueMessreihen()
        {
            _bsReihen.Clear();
            foreach (BsVariante variante in Enum.GetValues(typeof(BsVariante)))
                foreach (var abMitte in new[] { true, false })
                    foreach (BsKriterium kriterium
                             in Enum.GetValues(typeof(BsKriterium)))
                        _bsReihen.Add(new BsReihe
                        {
                            Variante = variante,
                            AbMitte = abMitte,
                            Kriterium = kriterium,
                            Schlecht = 0f,
                            Gut = 0f,
                            ObereGrenzeGeprueft = false,
                            Schritte = 0,
                            Befund = null,
                        });
        }

        /** Ein wahrer Rueckgabewert reserviert diesen Frame fuer die Sonde. */
        private bool PflegeBordsteinsonde()
        {
            switch (_bsPhase)
            {
                case BsPhase.ZeigerWarten:
                    return BsWarteAufZeiger();
                case BsPhase.KlonWarten:
                    return BsWarteAufKlon();
                case BsPhase.MessAnlegen:
                    return BsLegeMessungAn();
                case BsPhase.MessAblesen:
                    if (++_bsFrame < BsTempFrames) return true;
                    return BsLiesMessungAb();
                case BsPhase.BauVorbereiten:
                    return BsBereiteBauVor();
                case BsPhase.BauAnlegen:
                    return BsLegeBauAn();
                case BsPhase.BauAblesen:
                    if (++_bsFrame < BsTempFrames) return true;
                    return BsLiesBauAb();
                case BsPhase.BauWarten:
                    if (++_bsFrame < BsBauFrames) return true;
                    return BsMesseBau();
                case BsPhase.Fertig:
                    BsSchreibeBericht();
                    _bsPhase = BsPhase.Idle;
                    return false;
                default:
                    return false;
            }
        }

        /**
         * Der unsichtbare Klon entsteht in PrefabUpdate und ist erst einen
         * Zyklus spaeter benutzbar - dieselbe Wartezeit wie bei der
         * Zoningsonde. Ohne sie waere der erste Fall der Archetypfehler vom
         * 2026-08-30.
         */
        private bool BsWarteAufKlon()
        {
            if (TryResolveZoningRoad("Alley", out _))
            {
                _bsPhase = BsPhase.MessAnlegen;
                _bsFrame = 0;
                return true;
            }
            if (++_bsFrame <= 300) return true;
            BsMelde("Abbruch - der unsichtbare Gassenklon war nach 300 "
                + "Frames nicht benutzbar. Grund siehe 'PLT-Zoningstrasse'.");
            _bsPhase = BsPhase.Idle;
            return false;
        }

        // ------------------------------------------------------------------
        // Intervallhalbierung
        // ------------------------------------------------------------------

        /**
         * Die naechste zu messende Laenge dieser Reihe.
         *
         * Falsch heisst: die Reihe ist fertig. Das gilt fuer den
         * Normalabschluss ebenso wie fuer den Fall, dass schon die obere
         * Schranke ihr Kriterium nicht erfuellt - dann gibt es gar keine
         * Grenze zu suchen.
         */
        private static bool BsNaechsteLaenge(BsReihe reihe, out float laenge)
        {
            if (!reihe.ObereGrenzeGeprueft)
            {
                laenge = BsObereGrenze;
                return true;
            }
            laenge = 0f;
            if (reihe.Gut <= 0f) return false;
            if (reihe.Gut - reihe.Schlecht <= BsGenauigkeit) return false;
            if (reihe.Schritte >= BsHoechstschritte) return false;
            laenge = (reihe.Gut + reihe.Schlecht) * 0.5f;
            return true;
        }

        private static BsReihe BsNimmErgebnis(BsReihe reihe, float laenge,
                                              bool erfuellt, string befund)
        {
            if (!reihe.ObereGrenzeGeprueft)
            {
                reihe.ObereGrenzeGeprueft = true;
                reihe.Gut = erfuellt ? laenge : 0f;
                reihe.Schlecht = 0f;
                if (!erfuellt) reihe.Befund = befund;
                return reihe;
            }
            if (erfuellt) reihe.Gut = laenge;
            else reihe.Schlecht = laenge;
            reihe.Schritte++;
            return reihe;
        }

        private bool BsLegeMessungAn()
        {
            while (_bsReiheIndex < _bsReihen.Count)
            {
                var reihe = _bsReihen[_bsReiheIndex];
                if (BsNaechsteLaenge(reihe, out _bsTestlaenge)) break;
                _bsZeilen.Add(BsReihenbefund(reihe));
                _bsReiheIndex++;
            }
            if (_bsReiheIndex >= _bsReihen.Count)
            {
                _bsPhase = BsPhase.BauVorbereiten;
                return true;
            }

            var aktuell = _bsReihen[_bsReiheIndex];
            if (!BsPrefabFuer(aktuell.Variante, out _bsPrefab))
            {
                _bsZeilen.Add(BsReihenname(aktuell) + ": Prefab fehlt");
                _bsReiheIndex++;
                return true;
            }
            if (!BsLegeKursAn(aktuell.Variante, aktuell.AbMitte,
                    _bsTestlaenge, 0f))
            {
                _bsZeilen.Add(BsReihenname(aktuell)
                    + ": kein Platz auf der Strasse");
                _bsReiheIndex++;
                return true;
            }

            _bsPhase = BsPhase.MessAblesen;
            _bsFrame = 0;
            return true;
        }

        /**
         * Liest ab, was CS2 aus dem Kurs gemacht hat.
         *
         * Drei Fragen, drei Messungen: gibt es ueberhaupt eine Temp-Kante mit
         * unserem Prefab und laesst CS2 den Apply zu (angenommen), haengt an
         * ihr ein Fehlersymbol (Problem), und gibt es eine Temp-Entity, deren
         * `Temp.m_Original` die Stadtstrasse ist (Teilung).
         */
        private bool BsLiesMessungAb()
        {
            var reihe = _bsReihen[_bsReiheIndex];
            var unsere = BsUnsereTemps(out var angenommen);
            var teilt = BsTeiltStadtstrasse();
            var fehler = BsFehlertexte(unsere);
            var erfuellt = reihe.Kriterium == BsKriterium.Angenommen
                ? angenommen
                : angenommen && teilt;
            var befund = (angenommen ? "angenommen" : "abgelehnt")
                + ", geteilt=" + (teilt ? "ja" : "nein")
                + (fehler.Count == 0 ? "" : ", " + string.Join(", ", fehler));

            _bsMessungen++;
            BsLog($"  {BsReihenname(reihe)} @ {_bsTestlaenge:F2} m: {befund}");
            // Ein Lauf dauert ein paar Sekunden. Ohne mitlaufende Zahl sieht
            // das aus wie ein Haenger.
            _uiSystem?.SetStatus(ParkingLotTexte.T(
                $"Bordsteinsonde: Reihe {_bsReiheIndex + 1} von "
                    + $"{_bsReihen.Count}, {_bsMessungen} Messungen",
                $"Curb probe: series {_bsReiheIndex + 1} of "
                    + $"{_bsReihen.Count}, {_bsMessungen} measurements"));

            _bsReihen[_bsReiheIndex] = BsNimmErgebnis(reihe, _bsTestlaenge,
                erfuellt, befund);
            BsVerwirfFall();
            _bsPhase = BsPhase.MessAnlegen;
            return true;
        }

        // ------------------------------------------------------------------
        // Bauphase
        // ------------------------------------------------------------------

        /**
         * Stellt die vier Stummel zusammen, die stehenbleiben.
         *
         * Gebaut wird in der GEMESSENEN Mindestlaenge und nicht in einer
         * runden Zahl: genau das ist die Laenge, mit der eine Zufahrt in
         * einen schmalen Parkplatz passen muesste, und nur an ihr ist zu
         * sehen, ob der Bordstein dabei noch aufgeht.
         */
        private bool BsBereiteBauVor()
        {
            if (!_bsMitBau)
            {
                _bsPhase = BsPhase.Fertig;
                return true;
            }

            var versatz = -30f;
            // Links die Nullmessung: unsere heutige Zufahrt, so wie sie
            // heute gebaut wird - vom Fahrbahnrand aus.
            BsFuegeBaufallAn(BsVariante.Weg, abMitte: false,
                BsKriterium.Angenommen, ref versatz, "wie heute gebaut");
            foreach (BsVariante variante in Enum.GetValues(typeof(BsVariante)))
                BsFuegeBaufallAn(variante, abMitte: true, BsKriterium.Teilt,
                    ref versatz, "kuerzeste teilende Laenge");

            if (_bsBau.Count == 0)
            {
                BsLog("Kein Baufall: keine Reihe hat eine Grenze geliefert.");
                _bsPhase = BsPhase.Fertig;
                return true;
            }
            _bsPhase = BsPhase.BauAnlegen;
            return true;
        }

        /**
         * Haengt einen Baufall an, wenn seine Reihe eine Grenze gefunden hat.
         *
         * Aufgerundet auf 5 cm: die obere Schranke der Halbierung ist die
         * kleinste Laenge, die NACHWEISLICH funktioniert hat. Abrunden
         * wuerde auf der ungeprueften Seite landen.
         */
        private void BsFuegeBaufallAn(BsVariante variante, bool abMitte,
            BsKriterium kriterium, ref float versatz, string anlass)
        {
            foreach (var reihe in _bsReihen)
            {
                if (reihe.Variante != variante || reihe.AbMitte != abMitte
                    || reihe.Kriterium != kriterium) continue;
                if (reihe.Gut <= 0f) return;
                _bsBau.Add(new BsBaufall
                {
                    Variante = variante,
                    AbMitte = abMitte,
                    Laenge = math.ceil(reihe.Gut * 20f) / 20f,
                    Versatz = versatz,
                    Anlass = anlass,
                });
                versatz += 20f;
                return;
            }
        }

        private bool BsLegeBauAn()
        {
            if (_bsBauIndex >= _bsBau.Count)
            {
                _bsPhase = BsPhase.Fertig;
                return true;
            }

            var fall = _bsBau[_bsBauIndex];
            if (!BsPrefabFuer(fall.Variante, out _bsPrefab)
                || !BsLegeKursAn(fall.Variante, fall.AbMitte, fall.Laenge,
                    fall.Versatz))
            {
                _bsZeilen.Add("BAU " + BsBaufallname(fall)
                    + ": kein Platz oder Prefab fehlt");
                _bsBauIndex++;
                return true;
            }

            _bsPhase = BsPhase.BauAblesen;
            _bsFrame = 0;
            return true;
        }

        private bool BsLiesBauAb()
        {
            var fall = _bsBau[_bsBauIndex];
            BsUnsereTemps(out var angenommen);
            if (!angenommen)
            {
                _bsZeilen.Add("BAU " + BsBaufallname(fall)
                    + ": beim Bauversuch doch abgelehnt - nicht gebaut");
                BsVerwirfFall();
                _bsBauIndex++;
                _bsPhase = BsPhase.BauAnlegen;
                return true;
            }

            applyMode = ApplyMode.Apply;
            _bsPhase = BsPhase.BauWarten;
            _bsFrame = 0;
            return true;
        }

        /**
         * Nach dem Apply: was wirklich dasteht.
         *
         * Der Knoten ist der Beweis. Eine Zufahrt ueber `LocalConnect` laesst
         * die Stadtstrasse in Ruhe - ihr eigenes Ende traegt dann genau EINE
         * Kante. Eine Teilung macht daraus einen Knoten mit drei Kanten, und
         * genau der zeichnet die Rampe im Bordstein.
         */
        private bool BsMesseBau()
        {
            var fall = _bsBau[_bsBauIndex];
            var kanten = BsKantenAmKnoten(_bsA, out var knoten);
            var lebt = EntityManager.Exists(_bsStrasse)
                && !EntityManager.HasComponent<Deleted>(_bsStrasse);
            var kacheln = BsZaehleKacheln();

            _bsZeilen.Add("BAU " + BsBaufallname(fall)
                + ": Knoten " + BsEntity(knoten) + " mit " + kanten
                + " Kante(n); urspruengliche Stadtstrasse "
                + (lebt ? "lebt noch" : "ist ersetzt (geteilt)")
                + "; Zonenkacheln " + _bsKachelnVorher + " -> " + kacheln
                + " (" + (kacheln - _bsKachelnVorher) + ").");
            _bsKachelnVorher = kacheln;

            BsVerwirfDefinition();
            _bsBauIndex++;
            _bsPhase = BsPhase.BauAnlegen;
            _bsFrame = 0;
            return true;
        }

        // ------------------------------------------------------------------
        // Kurs anlegen und verwerfen
        // ------------------------------------------------------------------

        private bool BsLegeKursAn(BsVariante variante, bool abMitte,
                                  float laenge, float versatz)
        {
            if (!BsStrecke(abMitte, laenge, versatz, out _bsA, out _bsB))
                return false;

            var kurve = NetUtils.StraightCurve(_bsA, _bsB);
            _bsDefinition = EntityManager.CreateEntity();
            EntityManager.AddComponentData(_bsDefinition, new CreationDefinition
            {
                m_Prefab = _bsPrefab,
                m_RandomSeed = Environment.TickCount,
            });
            EntityManager.AddComponent<Updated>(_bsDefinition);
            EntityManager.AddComponentData(_bsDefinition, new NetCourse
            {
                m_Curve = kurve,
                m_Length = math.distance(_bsA, _bsB),
                m_FixedIndex = -1,
                m_Elevation = float2.zero,
                m_StartPosition = BsCoursePos(_bsA,
                    NetUtils.GetNodeRotation(MathUtils.StartTangent(kurve)), true),
                m_EndPosition = BsCoursePos(_bsB,
                    NetUtils.GetNodeRotation(MathUtils.EndTangent(kurve)), false),
            });
            return true;
        }

        /** Temp weg, Definition weg - der Spielstand bleibt, wie er war. */
        private void BsVerwirfFall()
        {
            applyMode = ApplyMode.Clear;
            BsVerwirfDefinition();
        }

        private void BsVerwirfDefinition()
        {
            if (_bsDefinition != Entity.Null && EntityManager.Exists(_bsDefinition))
                EntityManager.DestroyEntity(_bsDefinition);
            _bsDefinition = Entity.Null;
        }

        private void BsSchreibeBericht()
        {
            BsLog($"BEFUND nach {_bsMessungen} Einzelmessung(en):");
            foreach (var zeile in _bsZeilen) BsLog("  " + zeile);
            if (!_bsMitBau)
                BsLog("Alt+Shift+F baut zusaetzlich vier Stummel nebeneinander "
                    + "zum Hinsehen, jeden in der eben gemessenen "
                    + "Mindestlaenge. Das VERAENDERT den Spielstand - nur auf "
                    + "einem Wegwerfstand druecken.");
            BsMelde($"fertig, {_bsMessungen} Messung(en)"
                + (_bsBauIndex > 0 ? $" und {_bsBauIndex} Stummel gebaut" : "")
                + ". Der Befund steht im Log.");
        }

        /**
         * Die eine Zahl, um die es der Reihe ging - mit ihrer Unsicherheit.
         *
         * Genannt werden beide Schranken. "5,31 m (bei 5,26 m nicht mehr)"
         * sagt, was gemessen ist; eine einzelne Zahl wuerde eine Genauigkeit
         * behaupten, die die Halbierung nicht hat.
         */
        private static string BsReihenbefund(BsReihe reihe)
        {
            var name = BsReihenname(reihe);
            if (!reihe.ObereGrenzeGeprueft)
                return name + ": nicht gemessen";
            if (reihe.Gut <= 0f)
                return name + $": auch bei {BsObereGrenze:F0} m nicht erfuellt"
                    + (string.IsNullOrEmpty(reihe.Befund)
                        ? string.Empty : " (" + reihe.Befund + ")");
            if (reihe.Schlecht <= 0f)
                return name + $": schon bei {reihe.Gut:F2} m erfuellt, "
                    + "kuerzer wurde nicht geprueft";
            return name + $": ab {reihe.Gut:F2} m "
                + $"(bei {reihe.Schlecht:F2} m nicht mehr), "
                + $"{reihe.Schritte} Halbierungen";
        }

        // ------------------------------------------------------------------
        // Messungen
        // ------------------------------------------------------------------

        /**
         * Unsere Temp-Kanten dieses Falls.
         *
         * Erkannt an Prefab UND an beiden Endpunkten - allein am Prefab waere
         * eine offene Parkplatzvorschau mit denselben unsichtbaren Wegen
         * nicht zu unterscheiden.
         */
        private List<Entity> BsUnsereTemps(out bool angenommen)
        {
            var treffer = new List<Entity>();
            var query = GetEntityQuery(
                ComponentType.ReadOnly<Temp>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.ReadOnly<Game.Net.Curve>(),
                ComponentType.Exclude<Deleted>());
            using var kandidaten = query.ToEntityArray(Allocator.Temp);
            foreach (var e in kandidaten)
            {
                if (EntityManager.GetComponentData<PrefabRef>(e).m_Prefab != _bsPrefab)
                    continue;
                var b = EntityManager.GetComponentData<Game.Net.Curve>(e).m_Bezier;
                var hin = math.distance(b.a, _bsA) + math.distance(b.d, _bsB);
                var her = math.distance(b.a, _bsB) + math.distance(b.d, _bsA);
                if (math.min(hin, her) > 4f) continue;
                treffer.Add(e);
            }
            angenommen = treffer.Count > 0 && GetAllowApply();
            return treffer;
        }

        /**
         * Wird die Stadtstrasse geteilt?
         *
         * Eine Teilung ersetzt die bestehende Kante durch zwei neue. CS2
         * legt dafuer Temp-Entities an, deren `Temp.m_Original` auf das
         * Original zeigt - und das schon in der Vorschau, lange vor dem
         * Apply. Genau deshalb kann diese Sonde messen, ohne zu bauen.
         */
        private bool BsTeiltStadtstrasse()
        {
            var query = GetEntityQuery(
                ComponentType.ReadOnly<Temp>(),
                ComponentType.Exclude<Deleted>());
            using var temps = query.ToEntityArray(Allocator.Temp);
            foreach (var e in temps)
                if (EntityManager.GetComponentData<Temp>(e).m_Original == _bsStrasse)
                    return true;
            return false;
        }

        /**
         * Die Fehlersymbole an unseren Temp-Entities, mit Klartext.
         *
         * Der Typ steckt nicht am Fehler-Entity, sondern am Prefab des
         * Hinweissymbols (`ToolErrorData.m_Error`) - dieselbe Kette wie in
         * `AvMeldeBaufehler`.
         */
        private List<string> BsFehlertexte(List<Entity> unsere)
        {
            var texte = new List<string>();
            using var fehler = m_ErrorQuery.ToEntityArray(Allocator.Temp);
            if (fehler.Length == 0) return texte;

            var typen = new Dictionary<Entity, ErrorType>();
            using (var prefabs = GetEntityQuery(
                       ComponentType.ReadOnly<ToolErrorData>(),
                       ComponentType.ReadOnly<PrefabData>())
                   .ToEntityArray(Allocator.Temp))
                foreach (var prefab in prefabs)
                    typen[prefab] = EntityManager
                        .GetComponentData<ToolErrorData>(prefab).m_Error;

            var unsereMenge = new HashSet<Entity>(unsere);
            foreach (var e in fehler)
            {
                var original = EntityManager.HasComponent<Temp>(e)
                    ? EntityManager.GetComponentData<Temp>(e).m_Original
                    : Entity.Null;
                var wem = unsereMenge.Contains(e) ? "unser Stummel"
                    : original == _bsStrasse ? "die Stadtstrasse"
                    : "fremd";
                if (!EntityManager.HasBuffer<Game.Notifications.IconElement>(e))
                {
                    var ohne = "Fehler ohne Symbol (" + wem + ")";
                    if (!texte.Contains(ohne)) texte.Add(ohne);
                    continue;
                }
                foreach (var symbol in EntityManager
                             .GetBuffer<Game.Notifications.IconElement>(e, true))
                {
                    if (!EntityManager.HasComponent<PrefabRef>(symbol.m_Icon)) continue;
                    var prefab = EntityManager
                        .GetComponentData<PrefabRef>(symbol.m_Icon).m_Prefab;
                    var name = typen.TryGetValue(prefab, out var typ)
                        ? typ.ToString() : PrefabAssetName(prefab);
                    var text = name + " (" + wem + ")";
                    if (!texte.Contains(text)) texte.Add(text);
                }
            }
            return texte;
        }

        /**
         * Wieviele Kanten haengen am Knoten dieses Punktes?
         *
         * Eins heisst Sackgasse - kein Knoten in der Stadtstrasse, der
         * Bordstein bleibt geschlossen. Drei heisst Einmuendung.
         */
        private int BsKantenAmKnoten(float3 punkt, out Entity knoten)
        {
            knoten = Entity.Null;
            var best = 3f;
            var query = GetEntityQuery(
                ComponentType.ReadOnly<Game.Net.Node>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());
            using var knotenliste = query.ToEntityArray(Allocator.Temp);
            foreach (var k in knotenliste)
            {
                var p = EntityManager.GetComponentData<Game.Net.Node>(k).m_Position;
                var d = math.distance(p.xz, punkt.xz);
                if (d >= best) continue;
                best = d;
                knoten = k;
            }
            if (knoten == Entity.Null
                || !EntityManager.HasBuffer<ConnectedEdge>(knoten)) return 0;
            return EntityManager.GetBuffer<ConnectedEdge>(knoten, true).Length;
        }

        /**
         * Gueltige Zonenkacheln im Umkreis.
         *
         * Gezaehlt wird `ValidArea`, nicht die nominelle Blockgroesse - genau
         * dort verliert der Nutzer seine Kachelreihe, und nur diese Zahl ist
         * mit `PLT-Zoningbloecke` vergleichbar.
         */
        private int BsZaehleKacheln()
        {
            var summe = 0;
            var query = GetEntityQuery(
                ComponentType.ReadOnly<Game.Zones.Block>(),
                ComponentType.ReadOnly<Game.Zones.ValidArea>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());
            using var bloecke = query.ToEntityArray(Allocator.Temp);
            foreach (var b in bloecke)
            {
                var block = EntityManager.GetComponentData<Game.Zones.Block>(b);
                if (math.distance(block.m_Position.xz, _bsKontakt.xz)
                    > BsKachelradius) continue;
                var gueltig = EntityManager
                    .GetComponentData<Game.Zones.ValidArea>(b).m_Area;
                summe += math.max(0, gueltig.y - gueltig.x)
                    * math.max(0, gueltig.w - gueltig.z);
            }
            return summe;
        }

        // ------------------------------------------------------------------
        // Geometrie und Prefabs
        // ------------------------------------------------------------------

        /**
         * Sucht die naechste Stadtstrasse unter dem Zeiger.
         *
         * `Owner` schliesst unsere eigenen Kanten aus: an einer Strasse, die
         * uns gehoert, waere die Frage sinnlos.
         */
        private bool BsSucheStadtstrasse()
        {
            _bsStrasse = Entity.Null;
            var zeiger = _letzteWeltposition;
            var best = BsStrassenreichweite;
            var query = GetEntityQuery(
                ComponentType.ReadOnly<Game.Net.Edge>(),
                ComponentType.ReadOnly<Game.Net.Curve>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Owner>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());
            using var kanten = query.ToEntityArray(Allocator.Temp);
            foreach (var e in kanten)
            {
                var prefab = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
                if (!EntityManager.HasComponent<RoadData>(prefab)) continue;
                var bogen = EntityManager.GetComponentData<Game.Net.Curve>(e).m_Bezier;
                var d = MathUtils.Distance(bogen.xz, zeiger.xz, out _);
                if (d >= best) continue;
                best = d;
                _bsStrasse = e;
            }
            if (_bsStrasse == Entity.Null) return false;

            var kurve = EntityManager.GetComponentData<Game.Net.Curve>(_bsStrasse).m_Bezier;
            MathUtils.Distance(kurve.xz, zeiger.xz, out var t);
            _bsKontakt = MathUtils.Position(kurve, t);
            var strassenprefab = BsPrefabVon(_bsStrasse);
            _bsStrassenbreite = EntityManager.HasComponent<NetGeometryData>(strassenprefab)
                ? EntityManager.GetComponentData<NetGeometryData>(strassenprefab)
                    .m_DefaultWidth
                : 8f;
            return true;
        }

        /**
         * Anfang und Ende eines Stummels auf der Stadtstrasse.
         *
         * Er steht senkrecht auf der Strasse und zeigt auf die Seite, auf der
         * der Zeiger stand. Die Hoehe kommt vom Gelaende; ohne sie legt CS2
         * den Kurs auf Meereshoehe.
         */
        private bool BsStrecke(bool abMitte, float laenge, float versatz,
                               out float3 a, out float3 b)
        {
            a = default;
            b = default;
            if (!EntityManager.HasComponent<Game.Net.Curve>(_bsStrasse)) return false;

            var kurve = EntityManager.GetComponentData<Game.Net.Curve>(_bsStrasse);
            var strassenlaenge = math.max(1f, kurve.m_Length);
            MathUtils.Distance(kurve.m_Bezier.xz, _letzteWeltposition.xz, out var t0);
            var t = math.clamp(t0 + versatz / strassenlaenge, 0.08f, 0.92f);
            var mitte = MathUtils.Position(kurve.m_Bezier, t);

            // Tangente aus zwei nahen Punkten - das kommt ohne eine
            // Annahme ueber die Ableitungsfunktion der Bezierklasse aus.
            var vor = MathUtils.Position(kurve.m_Bezier, math.max(0f, t - 0.02f));
            var nach = MathUtils.Position(kurve.m_Bezier, math.min(1f, t + 0.02f));
            var tangente = nach.xz - vor.xz;
            if (math.lengthsq(tangente) < 1e-6f) return false;
            tangente = math.normalize(tangente);
            var normale = new float2(-tangente.y, tangente.x);
            // Auf die Seite des Zeigers drehen.
            if (math.dot(_letzteWeltposition.xz - mitte.xz, normale) < 0f)
                normale = -normale;

            var startAbstand = abMitte ? 0f : _bsStrassenbreite * 0.5f;
            var startXz = mitte.xz + normale * startAbstand;
            var endeXz = startXz + normale * laenge;

            var hoehen = _terrainSystem.GetHeightData(waitForPending: true);
            a = new float3(startXz.x, 0f, startXz.y);
            b = new float3(endeXz.x, 0f, endeXz.y);
            a.y = Game.Simulation.TerrainUtils.SampleHeight(ref hoehen, a);
            b.y = Game.Simulation.TerrainUtils.SampleHeight(ref hoehen, b);
            return math.all(math.isfinite(a)) && math.all(math.isfinite(b));
        }

        private bool BsPrefabFuer(BsVariante variante, out Entity prefab)
        {
            switch (variante)
            {
                case BsVariante.Weg:
                    return TryResolvePathPrefab("Invisible Road Path - 1xTwoway",
                        out prefab);
                case BsVariante.KlonUnsichtbar:
                    return TryResolveZoningRoad("Alley", out prefab);
                default:
                    return ZpTryRoadPrefab("Alley", out prefab);
            }
        }

        private static CoursePos BsCoursePos(float3 position, quaternion rotation,
                                             bool erster) => new CoursePos
        {
            m_Entity = Entity.Null,
            m_Position = position,
            m_Rotation = rotation,
            m_CourseDelta = erster ? 0f : 1f,
            m_Elevation = float2.zero,
            m_Flags = erster ? CoursePosFlags.IsFirst : CoursePosFlags.IsLast,
            m_ParentMesh = -1,
            m_SplitPosition = 0f,
        };

        private Entity BsPrefabVon(Entity netz)
            => EntityManager.HasComponent<PrefabRef>(netz)
                ? EntityManager.GetComponentData<PrefabRef>(netz).m_Prefab
                : Entity.Null;

        private static string BsReihenname(BsReihe reihe)
            => reihe.Variante + " "
               + (reihe.AbMitte ? "ab Mittellinie" : "ab Fahrbahnrand")
               + ", kuerzeste Laenge "
               + (reihe.Kriterium == BsKriterium.Angenommen
                   ? "die CS2 annimmt"
                   : "die die Strasse teilt");

        private static string BsBaufallname(BsBaufall fall)
            => fall.Variante + " "
               + (fall.AbMitte ? "ab Mittellinie" : "ab Fahrbahnrand")
               + $" {fall.Laenge:F2} m ({fall.Anlass})";

        private static string BsEntity(Entity e) => e == Entity.Null
            ? "Entity.Null" : "#" + e.Index + "." + e.Version;

        private static void BsLog(string text) => Mod.log.Info(BsPrefix + " " + text);

        /**
         * Dieselbe Zeile ins Log UND in die Statuszeile des Panels.
         *
         * Ohne die zweite Haelfte sieht ein Lauf aus wie nichts: die Sonde
         * baut absichtlich nichts, und die Temp-Kurse flackern zu kurz, um
         * sie als Rueckmeldung zu lesen.
         */
        private void BsMelde(string text)
        {
            BsLog(text);
            _uiSystem?.SetStatus(ParkingLotTexte.T(
                "Bordsteinsonde: " + text,
                "Curb probe: " + text));
        }
    }
}
