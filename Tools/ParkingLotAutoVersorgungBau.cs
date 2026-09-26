using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private enum AvPhase { Idle, LotWarten, TempWarten, ApplyWarten, Nachmessen }
        private sealed class AvKurs
        {
            internal string Name;
            internal Entity Prefab;
            internal readonly List<Entity> Definitionen = new List<Entity>();
            internal List<float3> Punkte;
            internal float3 Start, Ende;
            internal Versorgungstrasse Trasse;
            /** Haengen BEIDE Enden? Entscheidet, ob dieser Kurs bleibt. */
            internal bool Angeschlossen;
            internal readonly List<Entity> Kanten = new List<Entity>();
            /**
             * Die senkrechten Stuecke, mit denen CS2 unsere Leitung an das
             * hoeher liegende Rohr der Stadt anschliesst.
             *
             * Getrennt von `Kanten`, weil an dieser Liste die Abnahme haengt.
             * Gebraucht werden sie nur zum Markieren - siehe
             * MarkiereVersorgungsleitungen - damit sie beim Abriss mitgehen.
             */
            internal readonly List<Entity> Anschlussstuecke = new List<Entity>();
            internal long StromMax, FrischMax, AbwasserMax;
        }

        /*
         * HIER STAND `_avStartdefinitionen`.
         *
         * Die Liste hielt die Auswahl-Definitionen auf fremden Knoten, um sie
         * spaeter wieder zu zerstoeren. Seit diese Definitionen gar nicht mehr
         * entstehen (siehe ParkingLotAutoVersorgungStart, dort steht die
         * Begruendung samt Dekompilat), ist sie immer leer - und eine Schleife
         * ueber eine immer leere Liste liest sich beim naechsten Mal wie eine
         * geltende Regel.
         */
        private AvPhase _avPhase;
        private AvPhase _avGemeldetePhase = AvPhase.Idle;
        private int _avFrame, _avTempStabil = -1;
        private uint _avSimStart, _avSimLetzteMessung;
        private string _avLetzterBefund;

        private bool _avHatPlan;
        private readonly List<Versorgungstrasse> _avAusstehend = new List<Versorgungstrasse>();
        private readonly List<AvKurs> _avGebaut = new List<AvKurs>();
        private Entity _avTraeger;
        private int _avNochOffeneNetze;
        private readonly List<Versorgungstrasse> _avTrassen = new List<Versorgungstrasse>();
        private readonly List<AvKurs> _avKurse = new List<AvKurs>();
        private const float AutoVersorgungTiefe = -10f;
        private const int AutoVersorgungLotFrames = 240;
        private const int AutoVersorgungWartenFrames = 60;
        private const int AutoVersorgungMessenFrames = 120;
        private const int AutoVersorgungMessfrist = 3600;

        /*
         * HIER STAND DAS WARTEN AUF CS2S FLUSS-GRAPHEN.
         *
         * Es war noetig, solange ein eigenes Netz nur dann Ziel sein durfte,
         * wenn CS2 es selbst schon am Stadtnetz fuehrte. Am 2026-09-16 kostete
         * das 8,2 von 9,2 Sekunden eines Laufs. Seit die Planung nach
         * Zugehoerigkeit fragt statt nach dem Graphen (siehe `AvTeile`), gibt
         * es nichts mehr abzuwarten: was wir gebaut haben, wissen wir selbst.
         * Bestaetigt wird es weiterhin - von der Netzabdeckung am Ende.
         */

        internal void MerkeAutoVersorgung(Entity traeger,
            Entity altesLot = default, Entity alterTraeger = default)
        {
            if (_avPhase == AvPhase.Nachmessen) MesseVersorgungsfluss(true);
            AvEntferneDefinitionen();
            _avKurse.Clear();
            _avHatPlan = false;
            _avNetzstand.Clear();
            _avVerbindungen.Clear();
            _avFremdleitungen.Clear();
            _avAusstehend.Clear();
            _avGebaut.Clear();
            _avLetzterGesamtApplyBefund = null;
            _avTrassen.Clear();
            _avPhase = AvPhase.Idle;
            AvMerkeAbriss(altesLot, alterTraeger);
            _avGesamtzeit = System.Diagnostics.Stopwatch.StartNew();
            if (Mod.Optionen != null && !Mod.Optionen.AutomatischVersorgung) return;
            if (traeger == Entity.Null) return;
            _avTraeger = traeger;
            _avPhase = AvPhase.LotWarten;
            _avFrame = UnityEngine.Time.frameCount;
        }

        // Ein Vanilla-Apply je Netz: ein Fehler verwirft exakt dessen zwei
        // Kurse. Bereits gebaute und noch ausstehende Netze bleiben erhalten.
        private void StarteAutoVersorgung(Entity traeger)
        {
            var planzeit = System.Diagnostics.Stopwatch.StartNew();
            var strom = FindeVersorgungsprefab(true);
            var wasser = FindeVersorgungsprefab(false);
            if (strom == Entity.Null || wasser == Entity.Null)
            {
                AvFehler("mindestens 1 der 2 benoetigten Leitungsprefabs fehlt");
                return;
            }
            _avStrombreite = EntityManager.GetComponentData<NetGeometryData>(strom).m_DefaultWidth;
            _avStromprefab = strom;
            _avWasserprefab = wasser;
            _avWasserbreite = EntityManager.GetComponentData<NetGeometryData>(wasser).m_DefaultWidth;
            _avAchsabstand = VersorgungskursPruefung.Achsabstand(_avStrombreite, _avWasserbreite);
            AvErfasseStadtpfade();
            // Erst wissen, was im Weg liegt - dann die Huellen bauen.
            SammleFremdleitungen(_avAlleEigenen);
            _avHindernisse = AvHindernisse(_avAlleEigenen, math.max(_avStrombreite, _avWasserbreite) / 2);
            _avAusstehend.Clear();
            _avAusstehend.AddRange(WaehleVersorgungstrassen(traeger));
            Mod.log.Info($"PLT-Autoversorgung ZEIT: Planung {planzeit.Elapsed.TotalMilliseconds:F1} ms; "
                + $"seit Bauauftrag {_avGesamtzeit?.Elapsed.TotalMilliseconds:F1} ms.");
            _avHatPlan = _avAusstehend.Count > 0;
            _avTrassen.Clear();
            if (_avAusstehend.Count == 0) { AvNaechsteTrasse(); return; }
            _avTrassen.Add(_avAusstehend[0]);
            _avAusstehend.RemoveAt(0);
            foreach (var prefab in new[] { strom, wasser })
            {
                var lokal = EntityManager.GetComponentData<LocalConnectData>(prefab);
                var daten = EntityManager.GetComponentData<NetData>(prefab);
                Mod.log.Info($"PLT-Autoversorgung PREFAB '{PrefabAssetName(prefab)}': "
                    + $"LocalConnect-Suchweite {lokal.m_SearchDistance:F2}, "
                    + $"Hoehenfenster {lokal.m_HeightRange.min:F2}/{lokal.m_HeightRange.max:F2}, "
                    + $"Layer {lokal.m_Layers}, ConnectLayers {daten.m_ConnectLayers}.");
            }
            var sb = EntityManager.GetComponentData<NetGeometryData>(strom).m_DefaultWidth;
            var wb = EntityManager.GetComponentData<NetGeometryData>(wasser).m_DefaultWidth;
            var abstand = VersorgungskursPruefung.Achsabstand(sb, wb);
            Mod.log.Info($"PLT-Autoversorgung ABSTAND: Strombreite {sb:F2} m, "
                + $"Wasserbreite {wb:F2} m, Achsabstand {abstand:F2} m, "
                + $"Randabstand {abstand - (sb + wb) * 0.5f:F2} m. "
                + "Fremdobjekte und materialisierte Geometrie prueft Vanilla vor Apply.");
            foreach (var trasse in _avTrassen)
            {
                foreach (var istStrom in new[] { true, false })
                {
                    var weg = istStrom ? trasse.Stromweg : trasse.Wasserweg;
                    var punkte = new List<float3>();
                    var laenge = 0f;
                    for (var i = 1; i < weg.Count; i++) laenge += math.distance(weg[i - 1], weg[i]);
                    var entlang = 0f;
                    for (var i = 0; i < weg.Count; i++)
                    {
                        if (i > 0) entlang += math.distance(weg[i - 1], weg[i]);
                        punkte.Add(new float3(weg[i].x,
                            math.lerp(trasse.Start.y, trasse.Ziel.y, entlang / laenge) + AutoVersorgungTiefe, weg[i].y));
                    }
                    _avKurse.Add(new AvKurs { Name = trasse.Herkunft + (istStrom ? " Strom" : " Wasser"),
                        Prefab = istStrom ? strom : wasser, Trasse = trasse, Punkte = punkte,
                        Start = punkte[0], Ende = punkte[punkte.Count - 1] });
                }
            }
            foreach (var trasse in _avTrassen)
            {
                AvMeldeStartknoten(trasse);
                AvMeldeZielstrasse(trasse);
            }
            foreach (var kurs in _avKurse)
                for (var i = 1; i < kurs.Punkte.Count; i++) LegeVersorgungskurs(kurs, i);
            _avPhase = AvPhase.TempWarten;
            _avFrame = UnityEngine.Time.frameCount;
            _avTempStabil = -1;
            _avLetzterBefund = null;
        }

        /**
         * Schreibt jeden Phasenwechsel in die Schrittspur.
         *
         * Nur beim Wechsel. Eine Marke je Bild wuerde die Spur in einer
         * Sekunde vollschreiben und nebenbei die Platte beschaeftigen - die
         * Warnung im Kopf von ParkingLotSchrittmarke meint genau das.
         */
        private void MeldePhase()
        {
            if (_avPhase == _avGemeldetePhase) return;
            _avGemeldetePhase = _avPhase;
            ParkingLotSchrittmarke.Setze("Versorgung: Phase " + _avPhase);
        }

        private void LegeVersorgungskurs(AvKurs kurs, int abschnitt)
        {
            // Hier ist CS2 am 2026-09-14 abgestuerzt, und am 2026-09-10 in
            // derselben Ecke beim Loeschen. Die Spur soll das beim naechsten
            // Mal von selbst sagen.
            ParkingLotSchrittmarke.Setze(
                $"Versorgung: Kurs anlegen [{kurs.Name}, Teil {abschnitt}]");
            var start = kurs.Punkte[abschnitt - 1];
            var ende = kurs.Punkte[abschnitt];
            var kurve = NetUtils.StraightCurve(start, ende);
            var d = EntityManager.CreateEntity();
            kurs.Definitionen.Add(d);
            EntityManager.AddComponentData(d, new CreationDefinition {
                m_Prefab = kurs.Prefab, m_RandomSeed = Environment.TickCount });
            EntityManager.AddComponent<Updated>(d);
            EntityManager.AddComponentData(d, new NetCourse {
                m_Curve = kurve, m_Length = math.distance(start, ende),
                m_FixedIndex = -1, m_Elevation = new float2(AutoVersorgungTiefe),
                m_StartPosition = AvCoursePos(start,
                    NetUtils.GetNodeRotation(MathUtils.StartTangent(kurve)), true),
                m_EndPosition = AvCoursePos(ende,
                    NetUtils.GetNodeRotation(MathUtils.EndTangent(kurve)), false) });
            Mod.log.Info($"PLT-Autoversorgung KURS [{kurs.Name}, Teil {abschnitt}/{kurs.Punkte.Count - 1}]: Definition {d}, "
                + $"{math.distance(start, ende):F2} m, "
                + $"Welt-Y {start.y:F2}/{ende.y:F2}, Elevation -10/-10, "
                + $"Prefab '{PrefabAssetName(kurs.Prefab)}'. Ausgangsfluss neue Leitung = 0.");
        }

        private static CoursePos AvCoursePos(float3 p, quaternion r, bool erste)
            => new CoursePos { m_Entity = Entity.Null, m_Position = p, m_Rotation = r,
                m_CourseDelta = erste ? 0f : 1f, m_Elevation = new float2(AutoVersorgungTiefe),
                m_Flags = erste ? CoursePosFlags.IsFirst : CoursePosFlags.IsLast,
                m_ParentMesh = -1 };

        // true reserviert den Werkzeugdurchlauf bis einschliesslich Apply/Clear.
        private bool PflegeAutoVersorgung()
        {
            MeldePhase();
            if (_avPhase == AvPhase.Idle) return false;
            var frame = UnityEngine.Time.frameCount;
            var vergangen = frame - _avFrame;
            if (_avPhase == AvPhase.Nachmessen)
            {
                PflegeAutoVersorgungsmessung();
                return false;
            }
            if (m_ToolSystem.activeTool != this)
            {
                if (_avPhase == AvPhase.ApplyWarten) AvNaechsteTrasse();
                else AvFehler("Werkzeug vor Leitungsbau/Apply verlassen");
                return false;
            }
            if (_avPhase == AvPhase.ApplyWarten)
            {
                // 0 Temp-Kanten war am 05.09. kein Dauerhaftigkeitsbeleg:
                // zwei Bauten endeten mit 0/2 dauerhaften Kursen. Jede gemerkte
                // Entity zaehlen, bevor Definitionen oder der Zyklus wechseln.
                var zustand = AvMesseApply("Werkzeug");
                if (!AvTempQuery().IsEmptyIgnoreFilter)
                {
                    if (vergangen > AutoVersorgungLotFrames)
                    {
                        Mod.log.Warn($"PLT-Autoversorgung: Apply nach {vergangen} Frames noch offen; "
                            + $"{_avNochOffeneNetze} weitere Netze koennen nicht starten. "
                            + "Werkzeug wird freigegeben; Abnahme der angewandten Kurse bleibt offen.");
                        // Angewandt ist angewandt - kein zweiter Anschluss,
                        // auch wenn der Zyklus hier abbricht.
                        foreach (var trasse in _avTrassen) AvMerkeAngewandt(trasse);
                        _avAusstehend.Clear();
                        AvEntferneDefinitionen();
                        AvNaechsteTrasse();
                        return false;
                    }
                    return true;
                }
                /*
                 * DER APPLY IST GELAUFEN - EGAL WIE ER AUSGING.
                 *
                 * Dieses Netz bekommt nie einen zweiten Anschluss. Auch dann
                 * nicht, wenn die Dauerhaftigkeit unbestaetigt bleibt: eine
                 * zweite Leitung auf Verdacht waere schlimmer als eine, deren
                 * Abnahme offen ist.
                 */
                foreach (var trasse in _avTrassen) AvMerkeAngewandt(trasse);
                if (zustand != VersorgungsapplyZustand.Dauerhaft)
                    Mod.log.Warn("PLT-Autoversorgung: Temp-Zyklus beendet, aber Dauerhaftigkeit "
                        + "NICHT bestaetigt; Einzelzustand siehe APPLY-NACHWEIS. Kein blinder Neubau.");
                else
                    /*
                     * ERST JETZT DIE ZUORDNUNG SCHREIBEN.
                     *
                     * Eine Temp-Kante zu markieren waere sinnlos - sie kann
                     * noch verworfen werden, und dann truege eine Leiche den
                     * Namen dieses Parkplatzes. `Dauerhaft` ist der erste
                     * Zeitpunkt, an dem die Kante wirklich in der Welt steht.
                     *
                     * Ohne diese Zeile weiss nach dem naechsten Speichern
                     * niemand mehr, dass die Leitung zu diesem Parkplatz
                     * gehoert - und beim Abriss bliebe sie stehen.
                     */
                    MarkiereVersorgungsleitungen(AvZugehoerigesLot(), _avTraeger);
                AvEntferneDefinitionen();
                AvNaechsteTrasse();
                // Auch der Abschlussdurchlauf gehoert noch dieser Transaktion.
                return true;
            }
            if (_avPhase == AvPhase.LotWarten)
            {
                // Kein Leitungskurs im laufenden Parkplatz-Umbau. Die alten
                // Leitungen muessen auch physisch verschwunden sein (nicht nur Deleted).
                if (IsEditing) return false;
                if (!AvAbrissFertig())
                {
                    if (vergangen > AutoVersorgungLotFrames)
                    {
                        _avHatPlan = false;
                        AvFehler("alter Leitungsabriss noch offen; kein Neubau "
                            + "auf alte Leitungen. Offen: " + AvAbrissRest());
                    }
                    return true;
                }
                // Nach Apply/Clear mindestens die naechste Barriere abwarten.
                // Danach entscheiden Abriss und Temp-Entities unten, nicht 60
                // pauschale Bilder. Der Flussgraph wird separat nachgemessen.
                if (_avHatPlan && vergangen < 2) return true;
                if (_buildStage != BuildStage.Idle || GassenknotenLaeuft || SammleUnsereKanten(_avTraeger).Count == 0
                    || !AvTempQuery().IsEmptyIgnoreFilter)
                {
                    if (vergangen > AutoVersorgungLotFrames)
                    {
                        Mod.log.Warn($"PLT-Autoversorgung: {_avNochOffeneNetze} weitere Netze "
                            + "ohne freien Bauzyklus nicht begonnen.");
                        _avAusstehend.Clear();
                        AvFehler($"nach {vergangen} Frames noch kein freier Bauzyklus mit dauerhaften Strassen");
                    }
                    return false;
                }
                StarteAutoVersorgung(_avTraeger);
                return _avPhase == AvPhase.TempWarten;
            }
            var bereit = AvTempKantenDa();
            if (!bereit)
            {
                _avTempStabil = -1;
                if (vergangen <= AutoVersorgungWartenFrames) return true;
                AvFehler($"nach {vergangen} Frames fehlen vollstaendige Leitungskurse; siehe ECS-Messung");
                return true;
            }
            // Ein weiterer Werkzeugdurchlauf laesst die Validierungsbarriere
            // der ersten vollstaendigen Materialisierung wirksam werden.
            if (_avTempStabil < 0) { _avTempStabil = frame; return true; }
            if (frame <= _avTempStabil) return true;
            var fehler = m_ErrorQuery.CalculateEntityCount();
            if (fehler > 0 || !GetAllowApply())
            {
                AvMeldeBaufehler();
                AvFehler($"Vanilla-Baupruefung: {fehler} Fehler-Entities, "
                    + "Apply nicht zugelassen (u.a. Kollisionen/Fremdleitungen); keine Fehlerumgehung");
                return true;
            }
            if (!AvPruefeLeitungskollisionen())
            {
                AvFehler("Leitungsberuehrung vor Apply; Details siehe Messung");
                return true;
            }
            if (!AvPruefeTempAnschluesse())
            {
                AvFehler("fehlender Anschluss vor Apply; Details siehe Messung");
                return true;
            }
            /*
             * DIESELBE FRAGE WIE IN DER PLANUNG, KURZ VOR DEM APPLY.
             *
             * Hier stand `AvZielHatStadtpfad`, und das haette genau die neuen
             * Leitungen von Zone zu Zone abgewiesen - deren Ziel haengt ja noch
             * nicht an der Stadt. Geprueft wird deshalb, was die Planung
             * geprueft hat: lebt das Ziel noch, und gehoert es weiterhin zu
             * einem anderen Teil.
             */
            AvErfasseStadtpfade();
            var teile = AvErmittleTeile(SammleVersorgungsgruppen(
                SammleUnsereKanten(_avTraeger).FindAll(KanteNimmtVersorgung)));
            foreach (var t in _avTrassen)
            {
                var meins = t.Startnetz == null ? -1
                    : AvGruppeAmPunkt(teile.Gruppen, t.Start);
                if (AvZielErlaubt(t.Zielkante, meins < 0 ? -1 : teile.Finde(meins), teile))
                    continue;
                AvFehler("Ziel vor Apply nicht mehr zulaessig (geloescht oder "
                    + "inzwischen selbst mit uns verbunden); 0 Kurse angewandt");
                return true;
            }
            AvMeldeTempOhneOriginal("direkt vor dem Apply", null);
            // Die Identitaeten bleiben beim Vanilla-Create erhalten: ApplyNetSystem
            // entfernt Temp und setzt Created, Applied, Updated auf derselben Entity.
            applyMode = ApplyMode.Apply;
            _avGebaut.AddRange(_avKurse);
            _avPhase = AvPhase.ApplyWarten;
            _avFrame = frame;
            _avLetzterApplyBefund = null;
            Mod.log.Info($"PLT-Autoversorgung APPLY: {_avKurse.Count}/{_avKurse.Count} "
                + "Kurse vollstaendig, Vanilla-Fehler 0. Dauerhaftigkeit und Fluss noch NICHT bestaetigt.");
            return true;
        }

        // ToolSystem deaktiviert das alte Werkzeug (Enabled=false). Deshalb
        // ruft auch das staendig aktive Aktivierungssystem diese reine Messung.
        internal void PflegeAutoVersorgungsmessung()
        {
            if (_avPhase != AvPhase.Nachmessen) return;
            var frame = World.GetOrCreateSystemManaged<Game.Simulation.SimulationSystem>().frameIndex;
            if (frame - _avSimLetzteMessung < AutoVersorgungMessenFrames) return;
            _avSimLetzteMessung = frame;
            var fertig = frame - _avSimStart >= AutoVersorgungMessfrist;
            if (MesseVersorgungsfluss(fertig) || fertig) _avPhase = AvPhase.Idle;
        }

        private void AvEntferneDefinitionen()
        {
            foreach (var kurs in _avKurse)
                foreach (var d in kurs.Definitionen)
                    if (EntityManager.Exists(d)) EntityManager.DestroyEntity(d);
        }

        /**
         * Setzt einen abgebrochenen Versorgungslauf fort.
         *
         * Aufgerufen beim Oeffnen des Werkzeugs. Bedingungen: es lief
         * ueberhaupt etwas (`_avTraeger` steht), der Traeger lebt noch, gerade
         * laeuft kein Zyklus (`Idle`), und es ist noch etwas zu tun - ein nie
         * begonnenes oder ein gescheitertes Netz.
         *
         * `_avVerbindungen` bleibt stehen - daraus leitet die naechste Planung
         * ab, welche Teile schon zusammenhaengen. Ein Teil, das die Stadt
         * erreicht, wird uebersprungen; innerhalb eines Teils wird nie noch
         * einmal verbunden.
         *
         * `_avNetzstand` wird dagegen geleert - verbrauchte Anlaeufe und
         * gesperrte Ziele gelten nur fuer einen Lauf. Zwischen zwei Oeffnungen
         * des Werkzeugs baut der Nutzer: eine Strasse mehr, eine fremde
         * Leitung weniger, und derselbe Weg ist frei.
         */
        private void NimmOffeneVersorgungWiederAuf()
        {
            if (Mod.Optionen != null && !Mod.Optionen.AutomatischVersorgung) return;
            if (_avTraeger == Unity.Entities.Entity.Null) return;
            if (!EntityManager.Exists(_avTraeger)) { _avTraeger = Unity.Entities.Entity.Null; return; }
            if (_avPhase != AvPhase.Idle) return;
            var gescheitert = _avNetzstand.Count;
            if (_avNochOffeneNetze <= 0 && gescheitert == 0) return;

            Mod.log.Info("PLT-Autoversorgung: Werkzeug wieder geoeffnet, "
                + $"{_avNochOffeneNetze} offene(s) Netz(e) werden fortgesetzt"
                + (gescheitert > 0
                    ? $"; {gescheitert} Kante(n) aus gescheiterten Netzen bekommen "
                        + "einen neuen Anlauf, weil sich die Lage seit dem Fehlschlag "
                        + "geaendert haben kann."
                    : "."));
            _avNetzstand.Clear();
            _avHatPlan = true;
            _avPhase = AvPhase.LotWarten;
            _avFrame = UnityEngine.Time.frameCount;
            _avGesamtzeit = System.Diagnostics.Stopwatch.StartNew();
        }

        /**
         * NENNT DIE VANILLA-FEHLER BEIM NAMEN.
         *
         * `m_ErrorQuery.CalculateEntityCount()` liefert eine Zahl. Im
         * Bauzettel vom 2026-09-16 stand "3 Fehler-Entities" - und damit war
         * nicht zu sagen, ob sich zwei Leitungen beruehren, ob ein Rohr in
         * einem fremden liegt oder ob CS2 die Kurve zu eng fand. Jede Theorie
         * darueber waere geraten gewesen.
         *
         * CS2 weiss es. `ValidationSystem` haengt dem schuldigen Objekt ein
         * Hinweissymbol an; dessen Prefab traegt `ToolErrorData.m_Error`, also
         * genau den `ErrorType`. Diese Zuordnung lesen wir hier zurueck.
         *
         * Kommt das Symbol erst einen Frame spaeter, bleiben Ort und
         * Zugehoerigkeit - eine halbe Auskunft ist immer noch mehr als eine
         * Zahl.
         */
        /**
         * WELCHE TEMP-ENTITY HAT IHR ORIGINAL VERLOREN?
         *
         * `GetAllowApply()` ist bei null Fehler-Entities falsch, wenn
         * `OriginalDeletedSystem` IRGENDEINE Temp-Entity ohne `Deleted` findet,
         * deren `Temp.m_Original` nicht mehr existiert - es nennt aber keine.
         * Und `ApplyNetSystem` verarbeitet beim naechsten Apply alle Temp-Netze
         * global. Ein solcher Rest ist der naechste Verdacht fuer die nativen
         * Abstuerze beim Bearbeiten (Codex, 2026-09-25). Hier wird er benannt:
         * bei der Ablehnung und unmittelbar vor jedem Apply.
         */
        private void AvMeldeTempOhneOriginal(string anlass, HashSet<Entity> unsere)
        {
            using var temp = GetEntityQuery(ComponentType.ReadOnly<Temp>(),
                ComponentType.Exclude<Deleted>()).ToEntityArray(Allocator.Temp);
            var zahl = 0;
            var beispiele = new List<string>();
            foreach (var e in temp)
            {
                var original = EntityManager.GetComponentData<Temp>(e).m_Original;
                if (original == Entity.Null || EntityManager.Exists(original)) continue;
                zahl++;
                if (beispiele.Count >= 6) continue;
                var art = EntityManager.HasComponent<Edge>(e) ? "Kante"
                    : EntityManager.HasComponent<Game.Net.Node>(e) ? "Knoten"
                    : EntityManager.HasComponent<Game.Areas.Area>(e) ? "Flaeche"
                    : EntityManager.HasComponent<Game.Objects.Object>(e) ? "Objekt" : "sonst";
                var prefab = "?";
                if (EntityManager.HasComponent<PrefabRef>(e))
                    try { prefab = _prefabSystem.GetPrefabName(
                        EntityManager.GetComponentData<PrefabRef>(e).m_Prefab); }
                    catch { }
                beispiele.Add($"{e} {art} '{prefab}' Original {original}"
                    + (unsere != null && unsere.Contains(e) ? " (unser Kurs)" : ""));
            }
            if (zahl == 0 && anlass == "direkt vor dem Apply") return;
            var text = $"PLT-Autoversorgung TEMP OHNE ORIGINAL {anlass}: {zahl} von "
                + $"{temp.Length} Temp-Entities"
                + (beispiele.Count > 0 ? ": " + string.Join("; ", beispiele) : "");
            if (zahl > 0) Mod.log.Warn(text); else Mod.log.Info(text);
            if (zahl > 0)
                ParkingLotSchrittmarke.Setze($"Versorgung: {zahl} Temp ohne Original {anlass}");
        }

        private void AvMeldeBaufehler()
        {
            var typen = new Dictionary<Entity, ErrorType>();
            using (var fehlerprefabs = GetEntityQuery(
                ComponentType.ReadOnly<ToolErrorData>(),
                ComponentType.ReadOnly<PrefabData>()).ToEntityArray(Allocator.Temp))
                foreach (var prefab in fehlerprefabs)
                    typen[prefab] = EntityManager
                        .GetComponentData<ToolErrorData>(prefab).m_Error;

            var unsereKurse = new HashSet<Entity>();
            foreach (var kurs in _avKurse)
            {
                foreach (var k in kurs.Kanten) unsereKurse.Add(k);
                foreach (var k in kurs.Anschlussstuecke) unsereKurse.Add(k);
                foreach (var d in kurs.Definitionen) unsereKurse.Add(d);
            }
            var unsereStrassen = new HashSet<Entity>(SammleUnsereKanten(_avTraeger));

            using var betroffene = m_ErrorQuery.ToEntityArray(Allocator.Temp);
            if (betroffene.Length == 0)
            {
                Mod.log.Warn("PLT-Autoversorgung BAUFEHLER: kein Fehler-Entity, aber "
                    + "GetAllowApply() ist falsch - CS2 verweigert den Apply aus einem "
                    + "anderen Grund (geloeschtes Original).");
                AvMeldeTempOhneOriginal("bei der Ablehnung", unsereKurse);
                return;
            }
            foreach (var e in betroffene)
            {
                var original = EntityManager.HasComponent<Temp>(e)
                    ? EntityManager.GetComponentData<Temp>(e).m_Original : Entity.Null;
                var wem = unsereKurse.Contains(e) || unsereKurse.Contains(original)
                    ? "UNSERE Leitung"
                    : unsereStrassen.Contains(e) || unsereStrassen.Contains(original)
                        ? "UNSERE Strasse"
                        : "FREMD";
                var art = "kein Hinweissymbol (noch nicht erzeugt)";
                var beruehrung = "";
                if (EntityManager.HasBuffer<Game.Notifications.IconElement>(e))
                {
                    var namen = new List<string>();
                    foreach (var symbol in EntityManager
                        .GetBuffer<Game.Notifications.IconElement>(e, true))
                    {
                        if (!EntityManager.HasComponent<PrefabRef>(symbol.m_Icon)) continue;
                        var prefab = EntityManager
                            .GetComponentData<PrefabRef>(symbol.m_Icon).m_Prefab;
                        namen.Add(typen.TryGetValue(prefab, out var typ)
                            ? typ.ToString() : PrefabAssetName(prefab));
                        /*
                         * DER ORT DES SYMBOLS IST DER BERUEHRPUNKT.
                         *
                         * `ErrorData.m_Position` ist die Mitte der
                         * Schnittmenge beider Huellen - genau die Stelle, an
                         * der es klemmt. Sie ueberlebt nur im Symbol; die
                         * `ErrorData` selbst liegt in einer Queue und ist
                         * danach weg. Ohne sie steht in der Zeile nur die
                         * Mitte der ganzen Kante, und die kann 20 m daneben
                         * liegen.
                         */
                        if (beruehrung.Length == 0
                            && EntityManager.HasComponent<Game.Notifications.Icon>(symbol.m_Icon))
                        {
                            var ort = EntityManager
                                .GetComponentData<Game.Notifications.Icon>(symbol.m_Icon).m_Location;
                            beruehrung = $", Beruehrung ({ort.x:F1}/{ort.z:F1}, Y {ort.y:F1})";
                        }
                    }
                    if (namen.Count > 0) art = string.Join(", ", namen);
                }
                Mod.log.Warn($"PLT-Autoversorgung BAUFEHLER: {e} ({wem}), Original {original}, "
                    + $"Prefab '{AvFehlerprefab(e)}', Ort {AvFehlerort(e)}"
                    + AvFehlertiefe(e) + beruehrung + $", Art: {art}.");
            }
        }

        /**
         * Auf welcher Tiefe liegt dieses Netz?
         *
         * Unsere Leitungen liegen fest auf -10. Steht in der Zeile, dass das
         * Hindernis woanders liegt und wir trotzdem kollidieren, ist Ausweichen
         * in der Tiefe die billigere Antwort als jeder Umweg - und das soll man
         * sehen koennen, statt es zu vermuten.
         */
        private string AvFehlertiefe(Entity e)
        {
            if (!EntityManager.HasComponent<Game.Net.Elevation>(e)) return "";
            var h = EntityManager.GetComponentData<Game.Net.Elevation>(e).m_Elevation;
            return $", Tiefe {h.x:F1}/{h.y:F1}";
        }

        private string AvFehlerprefab(Entity e)
            => EntityManager.HasComponent<PrefabRef>(e)
                ? PrefabAssetName(EntityManager.GetComponentData<PrefabRef>(e).m_Prefab)
                : "-";

        private string AvFehlerort(Entity e)
        {
            float3 p;
            if (EntityManager.HasComponent<Curve>(e))
                p = MathUtils.Position(
                    EntityManager.GetComponentData<Curve>(e).m_Bezier, 0.5f);
            else if (EntityManager.HasComponent<Game.Net.Node>(e))
                p = EntityManager.GetComponentData<Game.Net.Node>(e).m_Position;
            else if (EntityManager.HasComponent<Game.Objects.Transform>(e))
                p = EntityManager.GetComponentData<Game.Objects.Transform>(e).m_Position;
            else return "unbekannt";
            return $"({p.x:F1}/{p.z:F1}, Y {p.y:F1})";
        }

        private void AvNaechsteTrasse()
        {
            // Dieser Zyklus ist vorbei. Was gemerkt werden musste, ist gemerkt
            // (siehe `AvMerkeAngewandt` und `AvMerkeFehlschlag`); ab hier darf
            // die Liste nicht mehr aussehen, als wuerde noch etwas gebaut.
            _avTrassen.Clear();
            _avKurse.Clear();
            _avFrame = UnityEngine.Time.frameCount;
            _avTempStabil = -1;
            // Ohne weitere Netze entfaellt der leere 60-Frame-Zyklus.
            // Die getrennte Flussmessung prueft weiterhin die Netzabdeckung.
            if (_avNochOffeneNetze == 0) _avHatPlan = false;
            if (_avHatPlan && m_ToolSystem.activeTool == this)
            {
                _avPhase = AvPhase.LotWarten;
                return;
            }
            if (_avHatPlan)
            {
                /*
                 * NICHT VERLOREN, NUR VERTAGT.
                 *
                 * Bis zum 2026-09-16 endete der Lauf hier endgueltig. Jetzt
                 * bleibt `_avNochOffeneNetze` stehen, und
                 * `NimmOffeneVersorgungWiederAuf` macht beim naechsten
                 * Oeffnen des Werkzeugs weiter. Der Nutzer sieht es auch:
                 * die Zeile sagt, was noch aussteht und was es braucht.
                 */
                Mod.log.Warn($"PLT-Autoversorgung: Werkzeug gewechselt; {_avNochOffeneNetze} "
                    + "noch nicht begonnene Netze. Bereits angewandte Kurse werden weiter "
                    + "gemessen; der Rest wird beim naechsten Oeffnen des Werkzeugs "
                    + "fortgesetzt.");
                _uiSystem?.SetStatus(ParkingLotTexte.T(
                    _avNochOffeneNetze + " Versorgungsnetz(e) noch offen - beim "
                    + "nächsten Öffnen des Werkzeugs wird weitergebaut.",
                    _avNochOffeneNetze + " supply network(s) still open - they "
                    + "will be finished the next time the tool is opened."));
                _avAusstehend.Clear();
            }
            _avKurse.AddRange(_avGebaut);
            Mod.log.Info($"PLT-Autoversorgung ZEIT: Bauphase beendet nach "
                + $"{_avGesamtzeit?.Elapsed.TotalMilliseconds:F1} ms, {_avGebaut.Count} angewandte Kurse; "
                + "Versorgungsfluss wird separat geprueft.");
            _avPhase = _avKurse.Count > 0 ? AvPhase.Nachmessen : AvPhase.Idle;
            _avSimStart = _avSimLetzteMessung = World.GetOrCreateSystemManaged<
                Game.Simulation.SimulationSystem>().frameIndex;
        }

        private void AvFehler(string grund)
        {
            /*
             * ZUERST MERKEN, DANN AUFRAEUMEN.
             *
             * `AvNaechsteTrasse` am Ende dieser Methode kann sofort neu
             * planen. Stuende die Merkung danach, plante die naechste Runde
             * dasselbe Netz mit demselben Ergebnis.
             *
             * Gemerkt wird nur, was noch KEINE Leitung hat: alle Wege hierher
             * liegen vor dem Apply.
             */
            foreach (var trasse in _avTrassen) AvMerkeFehlschlag(trasse);
            AvEntferneDefinitionen();
            if (_avPhase == AvPhase.TempWarten && m_ToolSystem.activeTool == this)
                applyMode = ApplyMode.Clear;
            Mod.log.Warn("PLT-Autoversorgung: " + grund
                + $". Aktuelle Trasse vollstaendig verworfen; {_avGebaut.Count} bereits angewandte Kurse, "
                + $"{_avNochOffeneNetze} weitere Netze. Der Parkplatz bleibt gebaut.");
            AvNaechsteTrasse();
        }
        /**
         * Sucht das Prefab fuer Kabel beziehungsweise Rohr.
         *
         * Gesucht wird ueber die Anschlussdaten UND den Namen. Der Name allein
         * waere zerbrechlich, die Anschlussdaten allein nicht eindeutig - es
         * gibt mehrere Strom- und Wasserleitungen. Beides zusammen trifft
         * genau die zwei, die der Nutzer selbst benutzt.
         */
        private Entity FindeVersorgungsprefab(bool strom)
        {
            var name = strom ? "Low-voltage Ground Cable" : "Combined Small Pipe";
            var query = GetEntityQuery(
                ComponentType.ReadOnly<NetData>(),
                ComponentType.ReadOnly<PrefabData>());
            using var prefabs = query.ToEntityArray(Allocator.Temp);
            for (var i = 0; i < prefabs.Length; i++)
            {
                var p = prefabs[i];
                var passt = strom
                    ? EntityManager.HasComponent<ElectricityConnectionData>(p)
                    : EntityManager.HasComponent<WaterPipeConnectionData>(p);
                if (!passt) continue;
                if (EntityManager.HasComponent<RoadData>(p)) continue;
                if (PrefabAssetName(p) == name
                    && EntityManager.HasComponent<NetGeometryData>(p)
                    && EntityManager.HasComponent<LocalConnectData>(p)) return p;
            }
            Mod.log.Warn($"PLT-Autoversorgung: exaktes Prefab '{name}' mit Geometrie/LocalConnect fehlt.");
            return Entity.Null;
        }

        /** Der Assetname eines PREFABS (nicht einer Instanz). */
        private string PrefabAssetName(Entity prefab)
            => _prefabSystem != null
                && _prefabSystem.TryGetPrefab<PrefabBase>(prefab, out var asset)
                && asset != null ? asset.name : null;
    }
}
