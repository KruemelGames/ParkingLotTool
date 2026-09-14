using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Game.Objects;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using static ParkingLotTool.Tools.ParkingLotTexte;

namespace ParkingLotTool.Tools
{
    /**
     * DER TRAEGERTEST.
     *
     * Eine einzige Frage, und sie entscheidet ueber den ganzen naechsten
     * Umbau: **Bleibt ein Aufkleber liegen, wenn er an einer nackten
     * Traeger-Entity haengt statt an einer Flaeche oder einem Netz?**
     *
     * Warum das die Frage ist. Zwei Wege sind am Code gescheitert:
     *
     *   - `Owner` = Netzkante. Der Klick kaeme damit bis zu unserer Flaeche
     *     durch (`DefaultToolSystem` laeuft die Kette hoch, bis ein Gebaeude
     *     kommt oder kein Owner mehr da ist). Aber `SubObjectSystem` haelt so
     *     ein Kind fuer ein vom Netz-Prefab DEKLARIERTES Unterobjekt, und weil
     *     die unsichtbaren Wege keine deklarieren, raeumt
     *     `RemoveUnusedOldSubObjects` es beim naechsten `Updated` der Kante weg.
     *
     *   - `Attached` = Netzkante ohne Owner. Entkommt dem Loeschpfad, aber
     *     `AttachPositionSystem` darf jedes `Updated + Attached`-Objekt
     *     versetzen.
     *
     * Der Ausweg steht in genau diesem System, Zeile 153-170: hat der Parent
     * WEDER `Node` NOCH `Composition`, kehrt es unveraendert zurueck. Eine
     * Traeger-Entity ohne `Transform`, ohne `Node`, ohne `Edge` und ohne
     * `Area` faellt durch alle vier Raster von `SubObjectSystem` und durch das
     * von `AttachPositionSystem`.
     *
     * DAS IST BISHER EINE HYPOTHESE AUS GELESENEM CODE. Was der Code nicht
     * sagt: ob eine so nackte Entity das Speichern und Laden uebersteht und ob
     * ihr `SubNet`-Puffer der Anbindung der Parkspuren genuegt. Beides
     * beantwortet nur das laufende Spiel - deshalb dieser Test und nicht
     * gleich der Umbau.
     *
     * Der Test aendert NICHTS am Bauverfahren. Er haengt ein paar vorhandene
     * Aufkleber eines bereits gebauten Parkplatzes um und schaut zu.
     */
    public sealed partial class ParkingLotToolSystem
    {
        /** Wieviele Aufkleber der Test umhaengt. Klein halten - es ist ein Versuch. */
        private const int TraegerProben = 5;

        private static string TraegerAkte => System.IO.Path.Combine(
            UnityEngine.Application.persistentDataPath, "Logs",
            "ParkingLotTool-traegertest.txt");

        /**
         * EIN KNOPF, ZWEI SCHRITTE.
         *
         * Erst standen hier zwei Knoepfe nebeneinander, "anlegen" und
         * "pruefen". Nutzerurteil am 2026-08-25: *"Das UI ist bisschen weird
         * und unverstaendlich, warum 2 Buttons?"* Zu Recht - die beiden
         * gehoeren zu EINEM Vorgang, und welcher gerade dran ist, weiss das
         * Programm besser als der Nutzer.
         *
         * Der Zustand liegt in der Akte auf der Platte, nicht im Speicher.
         * Das ist kein Zufall: zwischen Start und Abschluss soll der Nutzer
         * SPEICHERN UND NEU LADEN, und ein Feld im Speicher waere danach weg.
         */
        internal void SchalteTraegertest()
        {
            if (TraegertestLaeuft()) PruefeTraegertest();
            else StarteTraegertest();
        }

        private DateTime _traegerAkteStand;
        private bool _traegerLaeuftZwischenspeicher;
        private bool _traegerGeprueft;

        /**
         * Laeuft gerade einer - UND passt er zu dieser Welt?
         *
         * Die zweite Haelfte fehlte und hat den Lauf vom 2026-08-25 16:38
         * gekostet. Nutzerbeobachtung danach: *"Was komisch war, nach dem Bau
         * oder evtl vorher schon lief der Lauf bereits."* Genau so war es: die
         * Akte der vorigen Sitzung hatte keine Abschlusszeile, also bot der
         * Knopf "abschliessen" an, bevor in dieser Sitzung ueberhaupt etwas
         * gestartet war. Wer ihn drueckt, vergleicht zwei verschiedene Welten.
         *
         * Deshalb zaehlt ein Test nur dann als laufend, wenn die Kennung des
         * Parkplatzes noch stimmt. Sonst bietet der Knopf "starten" an - und
         * ein Start ueberschreibt die Akte ohnehin.
         *
         * GEPUFFERT, weil das Panel jeden Frame fragt: neu gelesen wird nur,
         * wenn sich die Datei geaendert hat.
         */
        internal bool TraegertestLaeuft()
        {
            if (!System.IO.File.Exists(TraegerAkte))
            {
                _traegerGeprueft = true;
                return _traegerLaeuftZwischenspeicher = false;
            }
            var stand = System.IO.File.GetLastWriteTimeUtc(TraegerAkte);
            if (_traegerGeprueft && stand == _traegerAkteStand)
                return _traegerLaeuftZwischenspeicher;
            _traegerAkteStand = stand;
            _traegerGeprueft = true;

            var offen = true;
            foreach (var zeile in System.IO.File.ReadAllLines(TraegerAkte))
                if (zeile.StartsWith("ABGESCHLOSSEN", StringComparison.Ordinal))
                    offen = false;
            if (!offen || LiesSollwerte().Count == 0)
                return _traegerLaeuftZwischenspeicher = false;

            var kennung = LiesKennung();
            if (kennung < 0) return _traegerLaeuftZwischenspeicher = true;
            var flaeche = FindeGebauteEigeneFlaeche(FindeEigenesPrefab());
            var jetzt = flaeche != Entity.Null
                && EntityManager.HasBuffer<Game.Net.SubNet>(flaeche)
                ? EntityManager.GetBuffer<Game.Net.SubNet>(flaeche, true).Length
                : -1;
            return _traegerLaeuftZwischenspeicher = jetzt == kennung;
        }

        /** Die Zeile, die im Panel steht. */
        internal string Traegerstand()
        {
            if (TraegertestLaeuft())
                return T("Laeuft. Jetzt hovern, eine Strasse daneben bauen, "
                         + "speichern, neu laden - dann abschliessen.",
                         "Running. Now hover, build a road next to it, save, "
                         + "reload - then finish.");
            if (!System.IO.File.Exists(TraegerAkte)) return string.Empty;
            var letzte = System.IO.File.ReadAllLines(TraegerAkte)
                .LastOrDefault(z => z.Contains("URTEIL:"));
            return letzte == null ? string.Empty : letzte.Trim();
        }

        private void StarteTraegertest()
        {
            try
            {
                var bericht = LegeTraegerAn();
                SchreibeTraegerdatei(bericht);
                _uiSystem?.SetReportPath(TraegerAkte);
                _debugTooltipSystem?.Show(T(
                    "Traegertest angelegt - siehe Logs-Ordner.",
                    "Carrier test created - see the Logs folder."));
            }
            catch (Exception ausnahme)
            {
                Mod.log.Warn("PLT-Traegertest fehlgeschlagen: " + ausnahme);
                _debugTooltipSystem?.Show(T(
                    "Traegertest fehlgeschlagen - siehe Log.",
                    "Carrier test failed - see the log."));
            }
        }

        private void PruefeTraegertest()
        {
            try
            {
                var bericht = VergleicheTraeger();
                SchreibeTraegerdatei(bericht, anhaengen: true);
                // Erst diese Zeile beendet den Vorgang. Sie steht in der Akte
                // und nicht im Speicher, damit sie das Laden ueberlebt.
                SchreibeTraegerdatei("ABGESCHLOSSEN "
                    + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                    + Environment.NewLine, anhaengen: true);
                _uiSystem?.SetReportPath(TraegerAkte);
                _debugTooltipSystem?.Show(T(
                    "Traegertest geprueft - siehe Logs-Ordner.",
                    "Carrier test checked - see the Logs folder."));
            }
            catch (Exception ausnahme)
            {
                Mod.log.Warn("PLT-Traegerpruefung fehlgeschlagen: " + ausnahme);
                _debugTooltipSystem?.Show(T(
                    "Pruefung fehlgeschlagen - siehe Log.",
                    "Check failed - see the log."));
            }
        }

        /**
         * Schritt 1: Traeger anlegen und ein paar Aufkleber umhaengen.
         *
         * BEWUSST NACKT. Der Traeger bekommt nur `PrefabRef` (damit
         * `LaneConnectionSystem` ueberhaupt etwas zu lesen hat), einen leeren
         * `SubObject`-Puffer und eine Kopie der `SubNet`-Eintraege des
         * Parkplatzes. Er bekommt AUSDRUECKLICH KEIN `Transform`, `Node`,
         * `Edge` oder `Area` - genau daran haengt die ganze Hypothese.
         */
        private string LegeTraegerAn()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Parking Lot Tool - Traegertest");
            sb.AppendLine("angelegt " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();

            var flaeche = FindeGebauteEigeneFlaeche(FindeEigenesPrefab());
            if (flaeche == Entity.Null)
                throw new InvalidOperationException(
                    "Kein gebauter PLT-Parkplatz gefunden. Erst einen bauen.");

            var traeger = EntityManager.CreateEntity();
            EntityManager.AddComponentData(traeger,
                EntityManager.GetComponentData<PrefabRef>(flaeche));
            EntityManager.AddBuffer<Game.Objects.SubObject>(traeger);
            var netze = EntityManager.AddBuffer<Game.Net.SubNet>(traeger);
            if (EntityManager.HasBuffer<Game.Net.SubNet>(flaeche))
            {
                var quelle = EntityManager.GetBuffer<Game.Net.SubNet>(flaeche, true);
                for (var i = 0; i < quelle.Length; i++) netze.Add(quelle[i]);
            }

            /*
             * DIE KENNUNG DES PARKPLATZES.
             *
             * Ohne sie war der Test am 2026-08-25 16:38 wertlos: der Nutzer
             * hatte CS2 neu gestartet und einen NEUEN Parkplatz gebaut, die
             * Akte trug aber noch die Sollwerte des alten. Verglichen wurden
             * damit zwei verschiedene Welten - und weil der Suchradius 30 m
             * betrug, fand die Pruefung brav die Nachbarbucht 2,2 m weiter
             * und meldete "VERSCHOBEN". Ein falsches Ergebnis, das wie ein
             * echtes aussah.
             *
             * Die Zahl der SubNet-Eintraege plus die Lage des ersten
             * Aufklebers identifizieren den Parkplatz gut genug, um beim
             * naechsten Mal zu MERKEN, dass es ein anderer ist.
             */
            sb.AppendLine($"  KENNUNG {netze.Length}");
            sb.AppendLine($"  Parkplatz:  Entity {flaeche.Index}");
            sb.AppendLine($"  Traeger:    Entity {traeger.Index}, "
                + $"{netze.Length} SubNet-Eintraege kopiert");
            sb.AppendLine("  Traeger traegt bewusst KEIN Transform/Node/Edge/Area.");
            sb.AppendLine();

            var proben = new List<Entity>();
            if (EntityManager.HasBuffer<Game.Objects.SubObject>(flaeche))
            {
                var kinder = EntityManager.GetBuffer<Game.Objects.SubObject>(flaeche, true);
                for (var i = 0; i < kinder.Length && proben.Count < TraegerProben; i++)
                {
                    var kind = kinder[i].m_SubObject;
                    if (!EntityManager.HasComponent<Game.Objects.Attached>(kind)) continue;
                    if (!EntityManager.HasComponent<Game.Objects.Transform>(kind)) continue;
                    proben.Add(kind);
                }
            }
            if (proben.Count == 0)
                throw new InvalidOperationException(
                    "Keine angehaengten Aufkleber am Parkplatz gefunden.");

            sb.AppendLine($"  {proben.Count} Aufkleber umgehaengt:");
            foreach (var probe in proben)
            {
                var lage = EntityManager.GetComponentData<Game.Objects.Transform>(probe);
                var alt = EntityManager.GetComponentData<Game.Objects.Attached>(probe);
                EntityManager.SetComponentData(probe, new Game.Objects.Attached
                {
                    m_Parent = traeger,
                    m_OldParent = Entity.Null,
                    m_CurvePosition = 0f,
                });
                // `Updated` ist der Ausloeser, um den es geht: erst damit
                // laufen SubObjectSystem und AttachPositionSystem ueber das
                // Objekt. Ohne ihn wuerde der Test nichts messen.
                if (!EntityManager.HasComponent<Game.Common.Updated>(probe))
                    EntityManager.AddComponent<Game.Common.Updated>(probe);

                sb.AppendLine($"    {PrefabnameVon(probe)}  Entity {probe.Index}  "
                    + $"vorher Parent {alt.m_Parent.Index}");
                sb.AppendLine("      SOLL " + Lage(lage));
            }
            sb.AppendLine();
            sb.AppendLine("  Jetzt: hovern, eine Strasse daneben bauen, speichern,");
            sb.AppendLine("  neu laden - und dann 'Traegertest pruefen' druecken.");
            return sb.ToString();
        }

        /**
         * Schritt 2: Was ist aus den Aufklebern geworden?
         *
         * Verglichen wird gegen die SOLL-Zeilen aus Schritt 1. Nach einem
         * Neuladen stimmen die Entity-Nummern nicht mehr; deshalb wird ueber
         * die Position gesucht und nicht ueber die Nummer.
         */
        private string VergleicheTraeger()
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("--- Pruefung " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                + " ---");

            var soll = LiesSollwerte();
            if (soll.Count == 0)
            {
                sb.AppendLine("  Keine Sollwerte gefunden - erst starten.");
                return sb.ToString();
            }

            // Ist es ueberhaupt noch derselbe Parkplatz?
            var jetztFlaeche = FindeGebauteEigeneFlaeche(FindeEigenesPrefab());
            var jetztKennung = jetztFlaeche != Entity.Null
                && EntityManager.HasBuffer<Game.Net.SubNet>(jetztFlaeche)
                ? EntityManager.GetBuffer<Game.Net.SubNet>(jetztFlaeche, true).Length
                : -1;
            var sollKennung = LiesKennung();
            if (sollKennung >= 0 && jetztKennung != sollKennung)
            {
                sb.AppendLine($"  ABGEBROCHEN: Der Parkplatz ist ein anderer als beim "
                    + $"Start (SubNet {jetztKennung} statt {sollKennung}).");
                sb.AppendLine("  Ein Vergleich waere wertlos. Bitte den Test in EINER "
                    + "Sitzung fahren: bauen, starten, belasten, speichern, laden, "
                    + "abschliessen - ohne den Parkplatz neu zu bauen.");
                return sb.ToString();
            }

            var query = GetEntityQuery(
                ComponentType.ReadOnly<Game.Objects.Transform>(),
                ComponentType.ReadOnly<Game.Objects.Attached>(),
                ComponentType.Exclude<Game.Tools.Temp>(),
                ComponentType.Exclude<Game.Common.Deleted>());
            using var alle = query.ToEntityArray(Allocator.TempJob);

            var groessteAbweichung = 0.0;
            foreach (var eintrag in soll)
            {
                // Naechstes angehaengtes Objekt zur gemerkten Stelle suchen.
                var beste = Entity.Null;
                var bester = double.MaxValue;
                for (var i = 0; i < alle.Length; i++)
                {
                    var lage = EntityManager.GetComponentData<Game.Objects.Transform>(alle[i]);
                    var d = math.distance(lage.m_Position, eintrag.Position);
                    if (d < bester) { bester = d; beste = alle[i]; }
                }

                /*
                 * ENG SUCHEN, NICHT WEIT.
                 *
                 * Buchten stehen alle 3,0 m. Mit den urspruenglichen 30 m
                 * Radius fand die Pruefung immer IRGENDEINEN Aufkleber und
                 * meldete dessen Abstand als "Verschiebung" - am 2026-08-25
                 * 2,21 m, was genau die Nachbarbucht war. 0,30 m ist enger
                 * als jeder Buchtabstand: entweder es ist derselbe Aufkleber
                 * oder es gibt keinen.
                 */
                if (beste == Entity.Null || bester > 0.30)
                {
                    sb.AppendLine($"  {eintrag.Name}: NICHT GEFUNDEN "
                        + $"(naechster liegt {bester:F2} m entfernt) - der "
                        + "urspruengliche Aufkleber ist weg oder weit weg.");
                    groessteAbweichung = double.MaxValue;
                    continue;
                }

                var jetzt = EntityManager.GetComponentData<Game.Objects.Transform>(beste);
                var parent = EntityManager.GetComponentData<Game.Objects.Attached>(beste)
                    .m_Parent;
                var parentLebt = EntityManager.Exists(parent);
                var parentNackt = parentLebt
                    && !EntityManager.HasComponent<Game.Objects.Transform>(parent)
                    && !EntityManager.HasComponent<Game.Net.Node>(parent)
                    && !EntityManager.HasComponent<Game.Net.Edge>(parent)
                    && !EntityManager.HasComponent<Game.Areas.Area>(parent);

                groessteAbweichung = Math.Max(groessteAbweichung, bester);
                sb.AppendLine($"  {eintrag.Name}  Entity {beste.Index}");
                sb.AppendLine($"    Abstand zum Sollwert: {bester:F4} m");
                sb.AppendLine($"    IST  {Lage(jetzt)}");
                sb.AppendLine($"    Parent {parent.Index}: "
                    + (!parentLebt ? "EXISTIERT NICHT MEHR"
                        : parentNackt ? "lebt, weiterhin nackt"
                        : "lebt, ist aber NICHT mehr nackt"));
                if (parentLebt)
                    sb.AppendLine("    Parent-Puffer: SubNet "
                        + (EntityManager.HasBuffer<Game.Net.SubNet>(parent)
                            ? EntityManager.GetBuffer<Game.Net.SubNet>(parent, true)
                                .Length.ToString()
                            : "FEHLT")
                        + ", SubObject "
                        + (EntityManager.HasBuffer<Game.Objects.SubObject>(parent)
                            ? EntityManager.GetBuffer<Game.Objects.SubObject>(parent, true)
                                .Length.ToString()
                            : "FEHLT"));
            }

            // Und die Kernfrage direkt: gibt es ueberhaupt noch eine nackte
            // Traeger-Entity? Sie ist an ihren Puffern erkennbar.
            var traegerQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<Game.Net.SubNet>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Game.Objects.Transform>(),
                    ComponentType.ReadOnly<Game.Net.Node>(),
                    ComponentType.ReadOnly<Game.Net.Edge>(),
                    ComponentType.ReadOnly<Game.Areas.Area>(),
                },
            });
            using var nackte = traegerQuery.ToEntityArray(Allocator.TempJob);
            sb.AppendLine();
            sb.AppendLine($"  Nackte Traeger-Entities in der Welt: {nackte.Length}");

            sb.AppendLine();
            sb.AppendLine(groessteAbweichung == double.MaxValue
                ? "  URTEIL: GESCHEITERT - mindestens ein Aufkleber ist weg."
                : groessteAbweichung < 0.001
                    ? $"  URTEIL: BESTANDEN - groesste Abweichung {groessteAbweichung:F4} m."
                    : $"  URTEIL: VERSCHOBEN - groesste Abweichung {groessteAbweichung:F4} m.");
            return sb.ToString();
        }

        /** Die Kennung aus der Akte, oder -1. */
        private static int LiesKennung()
        {
            if (!System.IO.File.Exists(TraegerAkte)) return -1;
            foreach (var zeile in System.IO.File.ReadAllLines(TraegerAkte))
            {
                var t = zeile.Trim();
                if (!t.StartsWith("KENNUNG ", StringComparison.Ordinal)) continue;
                if (int.TryParse(t.Substring(8), out var wert)) return wert;
            }
            return -1;
        }

        private struct Sollwert
        {
            internal string Name;
            internal float3 Position;
        }

        /** Die SOLL-Zeilen aus der Akte zurueckgelesen. */
        private static List<Sollwert> LiesSollwerte()
        {
            var liste = new List<Sollwert>();
            if (!System.IO.File.Exists(TraegerAkte)) return liste;
            string name = null;
            foreach (var zeile in System.IO.File.ReadAllLines(TraegerAkte))
            {
                var t = zeile.Trim();
                if (t.Contains("Entity ") && t.Contains("vorher Parent"))
                    name = t.Split(' ')[0];
                else if (t.StartsWith("SOLL ", StringComparison.Ordinal) && name != null)
                {
                    var teile = t.Substring(5).Split(new[] { ' ', '/' },
                        StringSplitOptions.RemoveEmptyEntries);
                    if (teile.Length >= 3
                        && double.TryParse(teile[0], NumberStyles.Float,
                            CultureInfo.InvariantCulture, out var x)
                        && double.TryParse(teile[1], NumberStyles.Float,
                            CultureInfo.InvariantCulture, out var y)
                        && double.TryParse(teile[2], NumberStyles.Float,
                            CultureInfo.InvariantCulture, out var z))
                    {
                        liste.Add(new Sollwert
                        {
                            Name = name,
                            Position = new float3((float)x, (float)y, (float)z),
                        });
                    }
                    name = null;
                }
            }
            return liste;
        }

        private static string Lage(Game.Objects.Transform lage)
            => lage.m_Position.x.ToString("F4", CultureInfo.InvariantCulture)
               + " / " + lage.m_Position.y.ToString("F4", CultureInfo.InvariantCulture)
               + " / " + lage.m_Position.z.ToString("F4", CultureInfo.InvariantCulture);

        private void SchreibeTraegerdatei(string text, bool anhaengen = false)
        {
            var ordner = System.IO.Path.GetDirectoryName(TraegerAkte);
            System.IO.Directory.CreateDirectory(ordner);
            if (anhaengen) System.IO.File.AppendAllText(TraegerAkte, text,
                new UTF8Encoding(false));
            else System.IO.File.WriteAllText(TraegerAkte, text, new UTF8Encoding(false));
            _traegerGeprueft = false;   // beim naechsten Blick neu lesen
            Mod.log.Info("PLT-Traegertest geschrieben: " + TraegerAkte);
        }
    }
}
