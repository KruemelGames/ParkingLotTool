using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /**
     * DER SONDENTEST: was nimmt CS2 wirklich an?
     *
     * Bis heute rechnet der Mod gegen GESCHAETZTE Grenzen. Die 0,375 m
     * stammen aus dem Dekompilat (`m_SnapDistance / 2`) und sind belastbar -
     * aber ob CS2 bei einer EINSCHNUERUNG dieselbe Zahl anlegt, ob es einen
     * spitzesten Winkel gibt und ob die Umlaufrichtung zaehlt, hat nie jemand
     * gemessen. Am 2026-08-24 stand deshalb im Bericht der Flaechenstudie an
     * mehreren Stellen "unbekannt, wir vermuten dieselbe Zahl".
     *
     * Ein Umbau des Flaechenkerns gegen geschaetzte Grenzen waere unklug.
     * Also wird gemessen, und zwar im Spiel selbst.
     *
     * DER PRUEFSTEIN ist der Dreieckspuffer: nimmt CS2 eine Flaeche nicht an,
     * legt es sie zwar an, aber `Game.Areas.Triangle` bleibt leer und im Spiel
     * ist nackter Boden zu sehen. Genau das meldet der Fehlerbericht seit
     * jeher als "X Flaeche(n) ohne Dreiecke (unsichtbar)". Der Test braucht
     * also keine Sichtpruefung - er liest die Antwort selbst.
     *
     * SCHONEND, wie vom Nutzer verlangt: *"nicht dass du zb auf die Idee
     * kommst 100000 flächen innerhalb einer sekunden bauen zu wollen sondern
     * nach und nach und mit löschen bereits gesetzter und überprüfter
     * flächen."* Es liegt immer HOECHSTENS EINE Sondenflaeche in der Welt.
     *
     * Und es sind wenige: eine Schwelle findet man nicht durch Masse, sondern
     * durch HALBIEREN. 1,0 m geht, 0,001 m geht nicht - dazwischen halbieren,
     * bis der Umschlagpunkt steht. Rund 14 Sonden je Frage statt tausender.
     */
    public sealed partial class ParkingLotToolSystem
    {
        private enum Sondenschritt
        {
            Aus,
            Setzen,
            Warten,
            Auswerten,
            Loeschen,
            Fertig,
        }

        /**
         * Eine Frage an CS2.
         *
         * `Baue` erzeugt zu einem Parameterwert ein Polygon. Die Binaersuche
         * kennt die Bedeutung des Parameters nicht - sie halbiert nur zwischen
         * einem Wert, der angenommen wurde, und einem, der abgelehnt wurde.
         */
        private sealed class Sondenfrage
        {
            internal string Name;
            internal string Einheit;
            internal double Gut;
            internal double Schlecht;
            internal Func<double, float2[]> Baue;
            /** Keine Suche, nur eine einzelne Ja/Nein-Probe. */
            internal bool Einzelprobe;
        }

        /**
         * Wie lange auf CS2 gewartet wird.
         *
         * Zwischen dem Setzen und dem Dreieckspuffer liegen mehrere Systeme
         * (Definition -> Entity -> Triangulierung). 12 Frames sind grosszuegig
         * gewaehlt; lieber langsamer messen als eine noch nicht triangulierte
         * Flaeche als "abgelehnt" zu zaehlen. Bei 60 Bildern je Sekunde
         * kostet eine Sonde damit rund 0,25 s.
         */
        private const int SondeWartenFrames = 12;
        private const int SondeRundenMax = 14;

        /**
          * EIGENE ABFRAGE, WEIL DIE VORHANDENE `Temp` VERLANGT.
          *
          * Der erste Lauf am 2026-08-24 las `_tempAreaDebugQuery` - und die
          * hat `Temp` in ihrer All-Liste. Eine mit `ApplyMode.Apply` gesetzte
          * Flaeche ist aber gerade NICHT mehr Temp. Der Test hat also
          * moeglicherweise die Vorschau gemessen statt des fertigen Ergebnisses,
          * und die Zahlen widersprachen sich prompt: Frage 1 nannte 0,14 m als
          * Grenze fuer die kuerzeste Kante, Frage 2 nahm eine Kante von
          * 0,0008 m an. Ein Messgeraet, das sich selbst widerspricht, misst
          * nicht das, was auf dem Schild steht.
          */
        private EntityQuery _sondeFlaechenQuery;

        private void InitialisiereSonde()
        {
            _sondeFlaechenQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Areas.Area>(),
                    ComponentType.ReadOnly<Game.Areas.Node>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                None = new[] { ComponentType.ReadOnly<Deleted>() },
            });
        }

        /** Wie viele Punkte das gerade gesetzte Polygon hat - der Ausweis. */
        private int _sondePunkte;
        private int _sondeKnoten;
        private int _sondeDreiecke;

        private Sondenschritt _sondeSchritt = Sondenschritt.Aus;
        private int _sondeFrame;
        private float3 _sondeMitte;
        private List<Sondenfrage> _sondeFragen;
        private int _sondeFrageIndex;
        private double _sondeGut, _sondeSchlecht, _sondeWert;
        private int _sondeRunde;
        private Entity _sondeFlaeche = Entity.Null;
        private readonly List<string> _sondeErgebnisse = new List<string>();

        internal bool SondeLaeuft => _sondeSchritt != Sondenschritt.Aus
            && _sondeSchritt != Sondenschritt.Fertig;

        /**
         * Die Fragen. Jede ist ein Parameter, jede eine eigene Binaersuche.
         *
         * Die Polygone sind bewusst gross gehalten (20 m Grundmass): so ist
         * IMMER nur die eine gefragte Eigenschaft grenzwertig, und ein
         * abgelehntes Ergebnis kann keine zweite Ursache haben.
         */
        private static List<Sondenfrage> SondenfragenBauen() => new List<Sondenfrage>
        {
            new Sondenfrage
            {
                Name = "Kuerzeste Kante",
                Einheit = "m",
                Gut = 5.0,
                Schlecht = 0.0005,
                // Ein Viereck, dessen untere Kante genau `p` lang ist.
                Baue = p => new[]
                {
                    new float2(0f, 0f),
                    new float2((float)p, 0f),
                    new float2(20f, 20f),
                    new float2(0f, 20f),
                },
            },
            new Sondenfrage
            {
                /*
                 * DAS TRENNEXPERIMENT.
                 *
                 * Frage 1 nannte 0,1415 m, Frage 2 nahm eine Kante von
                 * 0,0008 m an - beide Male mit VOLLSTAENDIGER Knotenzahl, CS2
                 * hat also nichts vereinfacht. Der Unterschied kann demnach
                 * nicht die Kantenlaenge sein.
                 *
                 * Der Verdacht: es ist nicht die Kante, sondern das duenne
                 * DREIECK, das beim Triangulieren entsteht. In Frage 1 hat es
                 * die Flaeche 10*p; in Frage 2 entsteht gar keins, weil die
                 * Kerbe eine Aussparung ist und ringsum genug Platz bleibt.
                 *
                 * Diese Frage ist dieselbe Form wie Frage 1, nur doppelt so
                 * hoch. Damit trennen sich drei Erklaerungen in einer
                 * einzigen Messung:
                 *
                 *   Grenze bleibt bei 0,1415  ->  es ist eine LAENGE
                 *   Grenze faellt auf 0,0707  ->  es ist eine FLAECHE (10p -> 20p)
                 *   Grenze steigt auf 0,283   ->  es ist ein WINKEL (halbiert
                 *                                 sich bei doppelter Hoehe)
                 */
                Name = "Kuerzeste Kante bei doppelter Hoehe",
                Einheit = "m",
                Gut = 5.0,
                Schlecht = 0.0005,
                Baue = p => new[]
                {
                    new float2(0f, 0f),
                    new float2((float)p, 0f),
                    new float2(20f, 40f),
                    new float2(0f, 40f),
                },
            },
            new Sondenfrage
            {
                Name = "Engster Hals",
                Einheit = "m",
                Gut = 5.0,
                Schlecht = 0.0005,
                /*
                 * Zwei Bloecke, verbunden durch einen Hals der Breite `p`.
                 * ALLE Kanten bleiben dabei lang - fiele die Flaeche hier
                 * durch, waere es nachweislich die Einschnuerung und nicht
                 * eine kurze Kante. Genau diese Trennung fehlt uns heute.
                 */
                Baue = p => new[]
                {
                    new float2(0f, 0f),
                    new float2(20f, 0f),
                    new float2(20f, 20f),
                    new float2(12f, 20f),
                    new float2(12f, 20f + (float)p),
                    new float2(20f, 20f + (float)p),
                    new float2(20f, 40f),
                    new float2(0f, 40f),
                },
            },
            new Sondenfrage
            {
                Name = "Spitzester Winkel",
                Einheit = "Grad",
                Gut = 60.0,
                Schlecht = 0.05,
                /*
                 * Ein Keil mit der Spitze im Ursprung und dem Oeffnungswinkel
                 * `p`. Die Schenkel sind 40 m lang, damit bei kleinen Winkeln
                 * die Grundkante noch weit ueber der Mindestkante liegt: bei
                 * 1 Grad sind das 0,70 m. Faellt die Flaeche frueher durch,
                 * war es der Winkel und nicht die Kante.
                 */
                Baue = p =>
                {
                    var halb = math.radians((float)p * 0.5f);
                    return new[]
                    {
                        new float2(0f, 0f),
                        new float2(40f * math.sin(halb), 40f * math.cos(halb)),
                        new float2(-40f * math.sin(halb), 40f * math.cos(halb)),
                    };
                },
            },
            new Sondenfrage
            {
                Name = "Umlaufrichtung im Uhrzeigersinn",
                Einheit = "",
                Einzelprobe = true,
                // Ein einfaches Quadrat, aber andersherum umlaufend als sonst.
                // Steht seit Monaten als offene Frage in den Notizen.
                Baue = _ => new[]
                {
                    new float2(0f, 0f),
                    new float2(0f, 20f),
                    new float2(20f, 20f),
                    new float2(20f, 0f),
                },
            },
            new Sondenfrage
            {
                Name = "Viele Punkte (Kreis mit 200)",
                Einheit = "",
                Einzelprobe = true,
                // 173 Punkte laufen nachweislich. Wo die Grenze liegt, weiss
                // niemand; 200 ist der erste Schritt darueber.
                Baue = _ =>
                {
                    var punkte = new float2[200];
                    for (var i = 0; i < punkte.Length; i++)
                    {
                        var w = math.radians(360f * i / punkte.Length);
                        punkte[i] = new float2(
                            30f * math.cos(w), 30f * math.sin(w));
                    }
                    return punkte;
                },
            },
        };

        /**
         * Startet den Lauf an der Stelle, auf die der Nutzer gerade zeigt.
         *
         * Absichtlich unter dem Mauszeiger und nicht an einer festen
         * Kartenposition: dort weiss der Nutzer, dass Boden ist, und er sieht
         * die Sonden aufblitzen. Eine feste Position koennte im Wasser liegen.
         */
        internal void StarteSondenlauf()
        {
            /**
              * JEDER ABBRUCH GEHT AUCH INS LOG.
              *
              * Beim ersten Versuch am 2026-08-24 stand im Log KEINE einzige
              * Sondenzeile - weder Start noch Grund. Der Nutzer sah nur, dass
              * nichts passiert, und ich konnte es nicht nachvollziehen. Eine
              * Statuszeile allein reicht nicht: sie ist weg, sobald etwas
              * anderes sie ueberschreibt.
              */
            if (SondeLaeuft)
            {
                Melde(ParkingLotTexte.T(
                    "Der Sondenlauf läuft bereits.",
                    "The probe run is already in progress."));
                return;
            }
            if (!ResolveSurfacePrefabs())
            {
                Melde(ParkingLotTexte.T(
                    "Sondenlauf nicht möglich: die Flächen-Prefabs sind nicht auflösbar.",
                    "Probe run not possible: the surface prefabs cannot be resolved."));
                return;
            }
            /*
             * NICHT der Zeiger, sondern die letzte gueltige Weltposition.
             *
             * Zum Klicken muss der Zeiger das Gelaende verlassen - ein
             * Raycast in diesem Moment liefert also nie einen Bodenpunkt.
             * Genau daran ist der erste Versuch gescheitert.
             */
            if (!_letzteWeltpositionGueltig
                || !math.all(math.isfinite(_letzteWeltposition)))
            {
                Melde(ParkingLotTexte.T(
                    "Sondenlauf nicht möglich: fahre einmal mit der Maus über das "
                    + "Gelände, damit eine Stelle bekannt ist.",
                    "Probe run not possible: move the mouse over the terrain once "
                    + "so a spot is known."));
                return;
            }
            _sondeMitte = _letzteWeltposition;

            _sondeFragen = SondenfragenBauen();
            _sondeFrageIndex = 0;
            _sondeErgebnisse.Clear();
            BeginneFrage();
            Mod.log.Info($"PLT-Sondenlauf gestartet bei "
                + $"{_sondeMitte.x:F1} / {_sondeMitte.z:F1}, "
                + $"{_sondeFragen.Count} Fragen.");
            VeroeffentlicheSondenstand();
        }

        /** Statuszeile UND Log - ein stummer Abbruch ist kein Abbruch. */
        private void Melde(string text)
        {
            _uiSystem?.SetStatus(text);
            Mod.log.Info("PLT-Sondenlauf: " + text);
        }

        internal void BrichSondenlaufAb()
        {
            if (!SondeLaeuft) return;
            LoescheSondenflaeche();
            _sondeSchritt = Sondenschritt.Aus;
            Mod.log.Info("PLT-Sondenlauf abgebrochen.");
            VeroeffentlicheSondenstand();
        }

        private void BeginneFrage()
        {
            var frage = _sondeFragen[_sondeFrageIndex];
            _sondeRunde = 0;
            if (frage.Einzelprobe)
            {
                _sondeWert = 0;
            }
            else
            {
                _sondeGut = frage.Gut;
                _sondeSchlecht = frage.Schlecht;
                _sondeWert = (_sondeGut + _sondeSchlecht) * 0.5;
            }
            _sondeSchritt = Sondenschritt.Setzen;
        }

        /**
         * Der Zustandsautomat, ein Schritt je Frame.
         *
         * Bewusst KEINE Schleife: zwischen Setzen und Auswerten muss CS2
         * rechnen duerfen. Ein Durchlauf ohne Frames dazwischen wuerde jede
         * Flaeche als abgelehnt melden, weil sie noch nicht trianguliert ist.
         */
        private void PflegeSondenlauf()
        {
            if (!SondeLaeuft) return;
            switch (_sondeSchritt)
            {
                case Sondenschritt.Setzen:
                    SetzeSondenflaeche();
                    _sondeFrame = UnityEngine.Time.frameCount;
                    _sondeSchritt = Sondenschritt.Warten;
                    break;

                case Sondenschritt.Warten:
                    if (UnityEngine.Time.frameCount - _sondeFrame
                        < SondeWartenFrames) return;
                    _sondeSchritt = Sondenschritt.Auswerten;
                    break;

                case Sondenschritt.Auswerten:
                    WerteSondeAus();
                    _sondeSchritt = Sondenschritt.Loeschen;
                    break;

                case Sondenschritt.Loeschen:
                    LoescheSondenflaeche();
                    NaechsteSonde();
                    break;
            }
        }

        private void SetzeSondenflaeche()
        {
            try
            {
                var frage = _sondeFragen[_sondeFrageIndex];
                var polygon = frage.Baue(_sondeWert);
                _sondePunkte = polygon.Length;
                var heightData = _terrainSystem.GetHeightData(waitForPending: true);

                var definition = EntityManager.CreateEntity();
                EntityManager.AddComponentData(definition, new CreationDefinition
                {
                    m_Prefab = _grassSurfacePrefab,
                });
                EntityManager.AddComponent<Updated>(definition);
                var nodes = EntityManager.AddBuffer<Game.Areas.Node>(definition);
                nodes.ResizeUninitialized(polygon.Length + 1);
                for (var i = 0; i < polygon.Length; i++)
                {
                    var x = _sondeMitte.x + polygon[i].x;
                    var z = _sondeMitte.z + polygon[i].y;
                    var y = TerrainUtils.SampleHeight(
                        ref heightData, new float3(x, 0f, z));
                    nodes[i] = new Game.Areas.Node(
                        new float3(x, y, z), float.MinValue);
                }
                nodes[polygon.Length] = nodes[0];
                applyMode = ApplyMode.Apply;
            }
            catch (Exception ausnahme)
            {
                Mod.log.Error(ausnahme, "PLT-Sonde konnte nicht gesetzt werden.");
                _sondeSchritt = Sondenschritt.Loeschen;
            }
        }

        /**
         * Hat CS2 die Flaeche angenommen?
         *
         * Gesucht wird die juengste Flaeche unseres Gras-Prefabs in der Naehe
         * der Sondenmitte. `Triangle`-Puffer leer heisst: angelegt, aber nicht
         * gebaut - im Spiel bleibt nackter Boden.
         */
        private void WerteSondeAus()
        {
            var frage = _sondeFragen[_sondeFrageIndex];
            var angenommen = false;
            _sondeFlaeche = Entity.Null;
            try
            {
                /*
                  * DER AUSWEIS IST DIE PUNKTZAHL.
                  *
                  * Prefab und Naehe allein reichen nicht: der Nutzer hat
                  * moeglicherweise einen Parkplatz mit demselben Gras-Prefab
                  * in der Naehe stehen, und dann misst der Test dessen
                  * Flaechen statt unserer. Die Punktzahl unterscheidet die
                  * Sonden eindeutig - 4, 8, 3 und 200 Punkte - und keine
                  * unserer Parkplatzflaechen trifft sie zufaellig mit
                  * derselben Naehe.
                  */
                using var flaechen = _sondeFlaechenQuery.ToEntityArray(
                    Allocator.TempJob);
                var gefunden = 0;
                for (var i = 0; i < flaechen.Length; i++)
                {
                    var flaeche = flaechen[i];
                    if (!EntityManager.Exists(flaeche)) continue;
                    if (EntityManager.GetComponentData<PrefabRef>(flaeche).m_Prefab
                        != _grassSurfacePrefab) continue;
                    if (!EntityManager.HasBuffer<Game.Areas.Node>(flaeche)) continue;
                    var knoten = EntityManager.GetBuffer<Game.Areas.Node>(
                        flaeche, true);
                    // CS2 schliesst den Ring selbst; deshalb beide Laengen
                    // zulassen statt eine zu raten.
                    if (knoten.Length != _sondePunkte
                        && knoten.Length != _sondePunkte + 1) continue;
                    if (knoten.Length == 0) continue;
                    if (math.distance(knoten[0].m_Position.xz, _sondeMitte.xz)
                        > 200f) continue;
                    gefunden++;
                    _sondeFlaeche = flaeche;
                    /*
                     * "HAT DREIECKE" IST NICHT DASSELBE WIE "IST DAS, WAS WIR
                     * WOLLTEN".
                     *
                     * Der Lauf vom 2026-08-24 hat sich selbst widersprochen:
                     * Frage 1 nannte 0,14 m als Grenze fuer die kuerzeste
                     * Kante, Frage 2 nahm eine Kante von 0,0008 m klaglos an.
                     * Beide Male stimmte das Dreieckskriterium - aber in
                     * Frage 2 hatte CS2 die haarfeine Kerbe vermutlich einfach
                     * WEGGELASSEN und den Rest sauber gebaut. Die Flaeche war
                     * da, nur nicht die Form, nach der gefragt war.
                     *
                     * Deshalb werden ab jetzt drei Zustaende unterschieden.
                     * Die Knotenzahl verraet die Vereinfachung: verschwindet
                     * ein Merkmal, verschwinden auch seine Punkte.
                     */
                    var dreiecke = EntityManager.HasBuffer<Game.Areas.Triangle>(flaeche)
                        ? EntityManager.GetBuffer<Game.Areas.Triangle>(
                            flaeche, true).Length
                        : 0;
                    _sondeKnoten = knoten.Length;
                    _sondeDreiecke = dreiecke;
                    angenommen = dreiecke > 0;
                }

                /*
                 * KEINE FLAECHE GEFUNDEN IST KEIN "ABGELEHNT".
                 *
                 * Das ist der Unterschied zwischen "CS2 hat sie verworfen" und
                 * "mein Messgeraet hat sie nicht gefunden". Wer beides
                 * gleichsetzt, bekommt eine saubere Kurve aus lauter
                 * Messfehlern.
                 */
                if (gefunden == 0)
                {
                    Mod.log.Warn($"PLT-Sonde [{frage.Name}] "
                        + $"{_sondeWert.ToString("G6", CultureInfo.InvariantCulture)}: "
                        + "KEINE Flaeche gefunden - nicht auswertbar, "
                        + "Lauf abgebrochen.");
                    Melde(ParkingLotTexte.T(
                        "Sondenlauf abgebrochen: die gesetzte Fläche war nicht "
                        + "auffindbar. Die Messung wäre wertlos.",
                        "Probe run aborted: the placed surface could not be "
                        + "found. The measurement would be worthless."));
                    _sondeSchritt = Sondenschritt.Aus;
                    return;
                }
                if (gefunden > 1)
                    Mod.log.Warn($"PLT-Sonde [{frage.Name}]: {gefunden} Flaechen "
                        + "passen auf den Ausweis - die letzte wurde gewertet.");
            }
            catch (Exception ausnahme)
            {
                Mod.log.Warn("PLT-Sonde nicht auswertbar: " + ausnahme.Message);
                return;
            }

            if (frage.Einzelprobe)
            {
                Notiere(frage, (angenommen ? "ANGENOMMEN" : "ABGELEHNT")
                    + $" (Knoten {_sondeKnoten} von {_sondePunkte}, "
                    + $"Dreiecke {_sondeDreiecke})");
                return;
            }
            if (angenommen) _sondeGut = _sondeWert; else _sondeSchlecht = _sondeWert;
            // Knoten und Dreiecke IMMER mitschreiben: nur so ist hinterher zu
            // sehen, ob CS2 die Form gebaut oder vereinfacht hat.
            Mod.log.Info($"PLT-Sonde [{frage.Name}] "
                + $"{_sondeWert.ToString("G6", CultureInfo.InvariantCulture)} "
                + $"{frage.Einheit}: "
                + (angenommen ? "angenommen" : "abgelehnt")
                + $" (Knoten {_sondeKnoten} von {_sondePunkte} gesendet, "
                + $"Dreiecke {_sondeDreiecke})");
        }

        private void LoescheSondenflaeche()
        {
            if (_sondeFlaeche == Entity.Null) return;
            try
            {
                if (EntityManager.Exists(_sondeFlaeche)
                    && !EntityManager.HasComponent<Deleted>(_sondeFlaeche))
                    EntityManager.AddComponent<Deleted>(_sondeFlaeche);
            }
            catch (Exception ausnahme)
            {
                Mod.log.Warn("PLT-Sonde nicht loeschbar: " + ausnahme.Message);
            }
            _sondeFlaeche = Entity.Null;
        }

        private void NaechsteSonde()
        {
            var frage = _sondeFragen[_sondeFrageIndex];
            _sondeRunde++;
            var fertig = frage.Einzelprobe || _sondeRunde >= SondeRundenMax;
            if (!fertig)
            {
                _sondeWert = (_sondeGut + _sondeSchlecht) * 0.5;
                _sondeSchritt = Sondenschritt.Setzen;
                VeroeffentlicheSondenstand();
                return;
            }

            if (!frage.Einzelprobe)
                Notiere(frage, "Grenze zwischen "
                    + _sondeSchlecht.ToString("G4", CultureInfo.InvariantCulture)
                    + " und "
                    + _sondeGut.ToString("G4", CultureInfo.InvariantCulture)
                    + " " + frage.Einheit);

            _sondeFrageIndex++;
            if (_sondeFrageIndex >= _sondeFragen.Count)
            {
                _sondeSchritt = Sondenschritt.Fertig;
                Mod.log.Info("PLT-Sondenlauf fertig:\n  "
                    + string.Join("\n  ", _sondeErgebnisse));
                _uiSystem?.SetStatus(ParkingLotTexte.T(
                    "Sondenlauf fertig.", "Probe run finished."));
                VeroeffentlicheSondenstand();
                return;
            }
            BeginneFrage();
            VeroeffentlicheSondenstand();
        }

        private void Notiere(Sondenfrage frage, string ergebnis)
        {
            var zeile = frage.Name + ": " + ergebnis;
            _sondeErgebnisse.Add(zeile);
            Mod.log.Info("PLT-Sondenergebnis " + zeile);
        }

        /** Fortschritt und Ergebnisse ins Panel. */
        private void VeroeffentlicheSondenstand()
        {
            if (_uiSystem == null) return;
            var kopf = _sondeSchritt == Sondenschritt.Fertig
                ? ParkingLotTexte.T("Fertig.", "Finished.")
                : SondeLaeuft
                    ? ParkingLotTexte.T(
                        $"Frage {_sondeFrageIndex + 1} von {_sondeFragen.Count}: "
                        + $"{_sondeFragen[_sondeFrageIndex].Name}, Runde {_sondeRunde + 1}",
                        $"Question {_sondeFrageIndex + 1} of {_sondeFragen.Count}: "
                        + $"{_sondeFragen[_sondeFrageIndex].Name}, round {_sondeRunde + 1}")
                    : "";
            _uiSystem.SetzeSondenstand(kopf, _sondeErgebnisse);
        }
    }
}
