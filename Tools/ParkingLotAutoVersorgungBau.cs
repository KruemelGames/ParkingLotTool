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

        internal void MerkeAutoVersorgung(Entity traeger,
            Entity altesLot = default, Entity alterTraeger = default)
        {
            if (_avPhase == AvPhase.Nachmessen) MesseVersorgungsfluss(true);
            AvEntferneDefinitionen();
            _avKurse.Clear();
            _avHatPlan = false;
            _avVersucht.Clear();
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
                        _avAusstehend.Clear();
                        AvEntferneDefinitionen();
                        AvNaechsteTrasse();
                        return false;
                    }
                    return true;
                }
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
                // 60 Werkzeugframes geben den Graphsystemen nach Apply Zeit.
                if (_avHatPlan && vergangen < AutoVersorgungWartenFrames) return true;
                if (_buildStage != BuildStage.Idle || SammleUnsereKanten(_avTraeger).Count == 0
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
            AvErfasseStadtpfade();
            if (!_avTrassen.TrueForAll(t => AvZielHatStadtpfad(t.Zielkante)))
            {
                AvFehler("Stadtpfad des Ziels vor Apply verloren; 0 Kurse angewandt");
                return true;
            }
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

        private void AvNaechsteTrasse()
        {
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
                Mod.log.Warn($"PLT-Autoversorgung: Werkzeug gewechselt; {_avNochOffeneNetze} "
                    + "noch nicht begonnene Netze. Bereits angewandte Kurse werden weiter gemessen.");
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
