using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using SubNet = Game.Net.SubNet;
using SubLane = Game.Net.SubLane;
using CarLane = Game.Net.CarLane;
using Colossal.Mathematics;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        // Separater ECS-Arbeitszustand fuer dieselben partiellen Bau-Funktionen.
        // Kein ToolBase.OnCreate, kein Eintrag in ToolSystem.tools, keine
        // Eingabeaktionen, kein Panel, kein Aufruf von Tool-OnUpdate/Apply/Clear.
        private bool _bauarbeiter;
        private int _hintergrundStufeBBild = -1;
        [ThreadStatic] private static bool _erzeugeBauarbeiter;
        private ParkingLotBaukontext _baukontext;
        private VegetationOptions _bauvegetation;
        private VegetationAsset[] _baupflanzen;
        private EntityQuery _dauerhafteFlaechen;
        private readonly HashSet<Entity> _vorhandeneLots = new HashSet<Entity>();
        private readonly HashSet<Entity> _eigeneDauerteile = new HashSet<Entity>();
        private readonly HintergrundTempo _hintergrundTempo = new HintergrundTempo();
        internal void HintergrundBildzeit(double ms) => _hintergrundTempo.MeldeBild(ms);
        private bool _hintergrundPlanVorbereitet;
        private double _hintergrundRechenMs;
        private int _hintergrundRechenthread, _hintergrundSpielthread;

        internal static ParkingLotToolSystem Bauarbeiter(World world)
        {
            var werkzeug = world.GetOrCreateSystemManaged<ParkingLotToolSystem>();
            // World.CreateSystemManaged erzeugt eine zweite Instanz; die erste
            // bleibt im Typ-Lookup. AddSystemManaged wuerde den doppelten Typ
            // ablehnen (Unity.Entities.World, dekompiliert am 02.10.2026).
            ParkingLotToolSystem arbeiter;
            _erzeugeBauarbeiter = true;
            try { arbeiter = world.CreateSystemManaged<ParkingLotToolSystem>(); }
            finally { _erzeugeBauarbeiter = false; }
            arbeiter._lotOwnerPrefab = werkzeug._lotOwnerPrefab;
            return arbeiter;
        }

        private void InitialisiereBauarbeiter()
        {
            Enabled = false;
            _terrainSystem = World.GetOrCreateSystemManaged<TerrainSystem>();
            // 11:51:34: beide Vorflaechen meldeten "Kein Netz-Suchbaum".
            // Diese Dienste sind Bauabhaengigkeiten, kein Debug-/Werkzeugeinstieg.
            _netSearchSystem = World.GetOrCreateSystemManaged<Game.Net.SearchSystem>();
            _areaSearchSystem = World.GetOrCreateSystemManaged<Game.Areas.SearchSystem>();
            _overlay = new ParkingLotOverlay();
            InitializeAreaPreview(); InitializeNetBuilder(); InitializeBayObjects();
            InitializeEntranceArrows(); InitializeLotOwner(); InitializeBusStops();
            InitialisiereZoningSeiten(); InitialisiereZoningSeitenSpeicher(); InitializeEditing();
            _dauerhafteFlaechen = GetEntityQuery(ComponentType.ReadOnly<Game.Areas.Area>(),
                ComponentType.ReadOnly<Game.Areas.Node>(), ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Temp>(), ComponentType.Exclude<Deleted>());
        }

        /** Die Layout-Einstellungen eines gebauten Parkplatzes aus seinem Bauzettel. */
        internal static LayoutSettings LayoutEinstellungen(ParkingLotBaukontext k)
        {
            var settings = k.Zettel.ToLayoutSettings(k.Zugaenge);
            settings.Zoningstrasse = k.Zoningstrasse;
            settings.Zoningflaechen = k.Zonen;
            settings.Randzoning = k.Randzoning.ToArray();
            settings.BusStops = k.Bushalte.ToArray(); settings.Teilflaechenschnitte = k.Schnitte;
            settings.TeilflaechenAusrichtungen = (k.Ausrichtungen ?? Array.Empty<Ausrichtzuweisung>())
                .Select(a => new TeilflaechenAusrichtung { Anker = a.Anker, Winkel = a.Winkel }).ToArray();
            settings.Ausrichtwinkel = double.IsNaN(k.Zettel.Ausrichtwinkel) ? (double?)null : k.Zettel.Ausrichtwinkel;
            // Aus den gespeicherten Kurvenzetteln, genau wie beim Setzen - Edit und Sync rechnen dieselben Laeufe.
            settings.Kurvenlaeufe = Kurvenzettel.Laeufe(k.Punkte.Select(p => p.xz).ToArray(), k.Kurven);
            return settings;
        }

        internal Task<ParkingLayout> BereiteHintergrund(Entity lot, int auftrag)
        {
            if (!_bauarbeiter) throw new InvalidOperationException("Kein isolierter Bauarbeiter.");
            if (!ParkingLotBaukontextLeser.TryRead(EntityManager, lot, out _baukontext, out var grund))
                throw new InvalidOperationException(grund);
            // Reparatur fehlender Assets: dieselben Bauwerte, nur andere Flaechen/Arten.
            var ersatz = ParkingLotFehlendeAssetsSystem.ErsatzFuer(lot);
            if (ersatz != null) _baukontext = _baukontext.MitFlaechen(ersatz.Strasse, ersatz.Deko, ersatz.Zoning);
            _definitionsmodus = ParkingLotDefinitionsmodus.Permanent;
            _definitionsauftrag = auftrag;
            _editLot = lot; _lotOwner = _lotCarrier = Entity.Null;
            _replacementNewLot = _replacementNewCarrier = Entity.Null;
            _eigeneDauerteile.Clear(); _vorhandeneLots.Clear();
            using (var areas = _dauerhafteFlaechen.ToEntityArray(Allocator.Temp))
                foreach (var area in areas) _vorhandeneLots.Add(area);
            _points.Clear(); _worldPoints.Clear(); _entrances.Clear(); _busStops.Clear();
            _points.AddRange(_baukontext.Punkte.Select(p => p.xz));
            _worldPoints.AddRange(_baukontext.Punkte); _entrances.AddRange(_baukontext.Zugaenge);
            _busStops.AddRange(_baukontext.Bushalte);
            _zoningflaechen.Clear(); _zoningflaechen.AddRange(_baukontext.Zonen ?? Array.Empty<ParkingGeometry.Zoningflaeche>());
            _randzoning.Clear(); _randzoning.AddRange(_baukontext.Randzoning);
            _zoningSeitenPlan.Clear(); _zoningSeitenPlan.AddRange(_baukontext.Seitenplan);
            _bauvegetation = ersatz?.Vegetation ?? VegetationVon(lot);
            // Ohne Laternenzettel (vor 2026-10-04 gebaut) der Standard des Spielers - wie Sync-Schritt 11.
            _baulaternen = LaternenVon(lot) ?? World.GetOrCreateSystemManaged<ParkingLotUISystem>().LaternenStandardOptionen;
            // Nur der unveraenderliche Assetkatalog; KEINE Panelwahl.
            _baupflanzen = World.GetOrCreateSystemManaged<ParkingLotUISystem>().VegetationAssets;
            _editParkingFeeKnown = EntityManager.HasComponent<ParkingLotEconomyData>(lot);
            _editParkingFee = _editParkingFeeKnown ? EntityManager.GetComponentData<ParkingLotEconomyData>(lot).ParkingFee : 0;
            _editBuildingEconomyEnabled = HasEnabledCompanion(lot);
            _replacementEconomyTransferred = false;
            _closed = true;
            LadeZoningBedienwerte(_baukontext.Zettel);
            var k = _baukontext;
            var settings = LayoutEinstellungen(k);
            settings.Brueckengrundrisse = Brueckengrundrisse(k.Punkte.Select(p => p.xz).ToArray());
            _areaPreviewSettings = settings;
            _alteZoningkurse = null; _erhalteneNetzteile.Clear(); _zoningErhalten = false;
            _erhalteneKursketten.Clear(); _hintergrundGassenkurse.Clear();
            var polygon = k.Punkte.Select(p => p.xz).ToArray();
            _hintergrundSpielthread = System.Threading.Thread.CurrentThread.ManagedThreadId;
            return Task.Run(() =>
            {
                var uhr = Stopwatch.StartNew();
                _hintergrundRechenthread = System.Threading.Thread.CurrentThread.ManagedThreadId;
                var layout = ParkingGeometry.Build(polygon,settings);
                // Gelb wird auch hier nicht freigeraeumt (ParkingLotZoningBand).
                var mitWirkung = BandwirkungFuerLauf(settings.Zoningflaechen, layout,
                    settings.Ai, settings.Cw, polygon);
                if (mitWirkung != null)
                {
                    settings.Zoningflaechen = mitWirkung;
                    layout = ParkingGeometry.Build(polygon, settings);
                }
                _hintergrundRechenMs = uhr.Elapsed.TotalMilliseconds;
                return layout;
            });
        }

        internal bool HintergrundPrefabs(ParkingLayout layout)
        {
            _areaPreviewLayout = layout;
            _altesZoningprefab = _baukontext.Zoningstrasse;
            if (!_hintergrundPlanVorbereitet)
            {
                _vorbereitungportion ??= new HintergrundPortion(HintergrundVorbereitungsschritte(layout),_hintergrundTempo);
                var uhr = Stopwatch.StartNew();
                _vorbereitungportion.Weiter(() => uhr.Elapsed.TotalMilliseconds);
                if (!_vorbereitungportion.Fertig) return false;
            }
            SyncAreaPreview(prefabsOnly: true);
            bool netze = ResolvePathPrefabs();
            foreach (var stueck in layout.NetLine)
            {
                if (stueck.Kind == "zoning") netze &= TryResolveZoningRoad(_areaPreviewSettings.Zoningstrasse, out _);
                if (Zufahrtsarten.IstGasse(stueck.Art)) netze &= TryResolveZufahrtsgasse(stueck.Art, out _);
            }
            return _areaPreviewPrefabsReady && netze && TryResolveLotOwnerPrefab(out _);
        }

        private HintergrundPortion _abrissportion;
        internal bool HintergrundAbrissFertig => _abrissportion?.Fertig == true;
        internal int HintergrundAbriss()
        {
            if (_abrissportion == null) _abrissportion = new HintergrundPortion(EntferneAlteNetzeSchritte(),_hintergrundTempo);
            var uhr = Stopwatch.StartNew();
            return _abrissportion.Weiter(() => uhr.Elapsed.TotalMilliseconds);
        }
        internal bool HintergrundTerrainFertig() => GelaendeNachAbrissFertig();
        internal Entity HintergrundLot => _lotOwner;
        internal Entity HintergrundTraeger => _lotCarrier;

        internal int HintergrundStufeA()
        {
            _areaTransferRecords.Clear();
            var height = _editHeightSnapshot.isCreated ? _editHeightSnapshot : _terrainSystem.GetHeightData(true);
            if (!TryResolveLotOwnerPrefab(out var prefab)) return 0;
            var prefabName = ParkingLotPrefabname.Von(EntityManager, prefab);
            if (prefabName.IsEmpty) throw new InvalidOperationException("Stufe A ohne speicherbare PrefabID; keine Definition ausgegeben.");
            int n = CreateLotOwnerDefinition(ref height);
            if (n == 1)
            {
                var r = _areaTransferRecords[0];
                EntityManager.AddComponentData(_editLot,new ParkingLotStufeAPrefabID { PrefabName = prefabName, Prefab = r.Prefab });
                var b = EntityManager.AddBuffer<ParkingLotStufeAKnoten>(_editLot);
                foreach (var p in r.SentNodes) b.Add(new ParkingLotStufeAKnoten { Position = p });
            }
            return n;
        }

        internal bool HintergrundBesitzerDa()
        {
            if (_lotOwner != Entity.Null) return true;
            if (_areaTransferRecords.Count != 1) return false;
            var record = _areaTransferRecords[0];
            using var areas = _dauerhafteFlaechen.ToEntityArray(Allocator.Temp);
            var treffer = new List<Entity>();
            foreach (var a in areas)
                if (!_vorhandeneLots.Contains(a) && EntityManager.GetComponentData<PrefabRef>(a).m_Prefab == record.Prefab
                    && AreaNodesMatch(record.SentNodes, EntityManager.GetBuffer<Game.Areas.Node>(a, true))) treffer.Add(a);
            if (treffer.Count != 1) return false;
            AuditMaterializedArea(record,treffer[0]);
            if (record.GeometryAccepted != true) return false;
            _lotOwner = treffer[0];
            EnsureOwnerBuffers(_lotOwner);
            if (!CreateLotCarrier()) throw new InvalidOperationException("Neuer Traeger fehlt.");
            EnsureOwnerBuffers(_lotCarrier);
            EntityManager.AddComponentData(_lotCarrier, new Owner(_lotOwner));
            _eigeneDauerteile.Add(_lotOwner); _eigeneDauerteile.Add(_lotCarrier);
            // Die Ergebnisidentitaet liegt am alten Lot; nach Laden wird ein
            // unvollstaendiger Ersatz abgerissen und der Rueckweg aufgenommen.
            EntityManager.AddComponentData(_editLot, new ParkingLotOffenerErsatz { Lot = _lotOwner, Traeger = _lotCarrier });
            ParkingLotNetzRueckweg.Melde($"Stufe A materialisiert: Lot {_lotOwner.Index}, Traeger {_lotCarrier.Index}; 6 Besitzerpuffer vorhanden.");
            return true;
        }

        private HintergrundPortion _kinderportion;
        private bool _kinderRichtig;
        private string _kindermessung;
        private readonly Dictionary<PartTransferRecord,Entity> _hintergrundObjekttreffer = new Dictionary<PartTransferRecord,Entity>();
        internal bool HintergrundKinderpruefungFertig => _kinderportion?.Fertig == true;
        internal bool HintergrundKinderDa(out string messung)
        {
            if (_kinderportion == null || _kinderportion.Fertig)
            { _kinderportion?.Dispose(); _kinderportion = new HintergrundPortion(HintergrundKinderschritte(),_hintergrundTempo); }
            var uhr = Stopwatch.StartNew();
            _kinderportion.Weiter(() => uhr.Elapsed.TotalMilliseconds);
            messung = _kindermessung;
            return _kinderportion.Fertig && _kinderRichtig;
        }
        private IEnumerable<int> HintergrundKinderschritte()
        {
            foreach (int n in SammleHintergrundteileSchritte()) yield return n;
            foreach (int n in BerechneHintergrundObjektbodenSchritte()) yield return n;
            _hintergrundObjekttreffer.Clear();
            _fehlendeHintergrundobjekte.Clear(); _fehlendeHintergrundkurse.Clear(); _fehlendeErhalteneKurse.Clear();
            var areas = _eigeneDauerteile.Where(e => EntityManager.HasComponent<Game.Areas.Area>(e)).ToList();
            var flaechenindex = new HintergrundLageindex<Entity>();
            foreach (var a in areas)
            {
                foreach (var n in EntityManager.GetBuffer<Game.Areas.Node>(a,true)) flaechenindex.Fuege(n.m_Position.xz,a);
                yield return 0;
            }
            var netze = _eigeneDauerteile.Where(e => EntityManager.HasComponent<Edge>(e)).ToArray();
            var objektindex = new Dictionary<Entity,HintergrundLageindex<Entity>>();
            foreach (var e in _eigeneDauerteile)
            {
                yield return 0;
                if (!EntityManager.HasComponent<Game.Objects.Transform>(e) || !EntityManager.HasComponent<PrefabRef>(e)) continue;
                var prefab = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
                if (!objektindex.TryGetValue(prefab,out var index)) objektindex.Add(prefab,index = new HintergrundLageindex<Entity>());
                index.Fuege(EntityManager.GetComponentData<Game.Objects.Transform>(e).m_Position.xz,e);
            }
            int flaechen = 0, objekte = 0, kurse = 0;
            var benutzt = new HashSet<Entity>();
            foreach (var r in _areaTransferRecords)
                foreach (var a in flaechenindex.Nahe(r.SentNodes[0].xz))
                {
                    yield return 0;
                    if (!benutzt.Contains(a) && EntityManager.GetComponentData<PrefabRef>(a).m_Prefab == r.Prefab
                        && AreaNodesMatch(r.SentNodes, EntityManager.GetBuffer<Game.Areas.Node>(a, true)))
                    {
                        AuditMaterializedArea(r, a);
                        if (r.GeometryAccepted != true) continue;
                        benutzt.Add(a); flaechen++; break;
                    }
                }
            foreach (var r in _objectRecords)
            {
                yield return 0;
                bool gefunden = false;
                foreach (var e in objektindex.TryGetValue(r.Prefab,out var index) ? index.Nahe(r.From.xz) : Array.Empty<Entity>())
                    if (!benutzt.Contains(e) && EntityManager.HasComponent<Game.Objects.Transform>(e)
                        && EntityManager.GetComponentData<PrefabRef>(e).m_Prefab == r.Prefab
                        && EntityManager.HasComponent<Owner>(e) && EntityManager.GetComponentData<Owner>(e).m_Owner == _lotCarrier
                        && EntityManager.HasComponent<Game.Objects.Attached>(e)
                        && EntityManager.GetComponentData<Game.Objects.Attached>(e).m_Parent == _lotCarrier
                        && HintergrundAbgleich.Objektlage(EntityManager.GetComponentData<Game.Objects.Transform>(e).m_Position,
                            r.From,_hintergrundObjektboden[r].Position,_hintergrundObjektboden[r].Erlaubt))
                    {
                        benutzt.Add(e); objekte++; gefunden = true; _hintergrundObjekttreffer[r] = e;
                        if (!EntityManager.HasComponent<ParkingLotPartRelation>(e))
                            EntityManager.AddComponentData(e, new ParkingLotPartRelation { Lot = _lotOwner, Carrier = _lotCarrier });
                        ApplyVegetationAge(e, r.Prefab); break;
                    }
                if (!gefunden) _fehlendeHintergrundobjekte.Add(r);
            }
            foreach (var r in _netRecords)
            {
                yield return 0;
                bool innenhoeheVanilla = r.Kurs.HasValue
                    && ParkingLotKursabgleich.InnenhoeheVanilla(EntityManager,r.Prefab,r.Kurs.Value,_lotOwner)
                    && ParkingLotKursabgleich.InnenhoeheNachGeneratoren(EntityManager,r.Prefab,r.Kurs.Value,_lotOwner);
                var teile = r.Kurs.HasValue ? ParkingLotKursabgleich.Sammle(EntityManager,netze,
                    r.Kurs.Value.m_Curve,r.Prefab,_lotOwner,benutzt,innenhoeheVanilla) : new List<ParkingLotKursabgleich.Teil>();
                bool hoehen = r.Kurs.HasValue;
                for (int s = 0; s <= 16 && hoehen; s++)
                {
                    var punkt = MathUtils.Position(r.Kurs.Value.m_Curve,s/16f);
                    var projiziert = new List<float3>();
                    foreach (var teil in teile)
                    {
                        var c = EntityManager.GetComponentData<Curve>(teil.Kante).m_Bezier;
                        MathUtils.Distance(c.xz,punkt.xz,out float t);
                        projiziert.Add(MathUtils.Position(c,t));
                    }
                    hoehen &= HintergrundKurspruefung.LageGedeckt(punkt,projiziert,innenhoeheVanilla && s > 0 && s < 16);
                }
                if (hoehen && ParkingLotKursabgleich.Kette(teile,out var start,out var ende)
                    && HintergrundSollanschluss(r,start,ende))
                { kurse++; foreach (var teil in teile) benutzt.Add(teil.Kante); }
                else _fehlendeHintergrundkurse.Add(r);
            }
            int erhalten = 0;
            foreach (var kette in _erhalteneKursketten)
            {
                bool da = ErhalteneKursketteDa(kette.Value);
                if (da) erhalten++;
                else ParkingLotNetzRueckweg.Melde($"Erhaltener Soll-Kurs {kette.Key}: Originalkante/Prefab/Owner/3D-Kurve/Knoten/ConnectedEdge veraendert oder fehlend; Abnahme offen.");
                yield return 0;
            }
            int soll = _netRecords.Count + _erhalteneKursketten.Count + _nichtBaubareHintergrundGassen;
            _kindermessung = $"Besitzerpruefung: Kurse mit 17 Lageproben (5 cm; Enden 3D, innen XZ nur bei Vanilla-Y) {kurse+erhalten}/{soll} (neu {kurse}/{_netRecords.Count}, erhalten {erhalten}/{_erhalteneKursketten.Count}, nicht baubar {_nichtBaubareHintergrundGassen}), Flaechen {flaechen}/{_areaTransferRecords.Count}, Objekte {objekte}/{_objectRecords.Count}.";
            _kinderRichtig = Netzerhalt.Vollstaendig(soll,erhalten,kurse,_netRecords.Count) && flaechen == _areaTransferRecords.Count && objekte == _objectRecords.Count;
        }

        private IEnumerable<int> SammleHintergrundteileSchritte()
        {
            // Die nativen Puffer der zwei eigenen Besitzer ersetzen einen
            // Welt-Owner-Scan bei jeder Abnahme (1757: 2098 Buchten).
            var teile = new HashSet<Entity>();
            foreach (var owner in new[] {_lotOwner,_lotCarrier})
            {
                if (!EntityManager.Exists(owner)) continue;
                if (EntityManager.HasBuffer<Game.Areas.SubArea>(owner))
                    foreach (var a in EntityManager.GetBuffer<Game.Areas.SubArea>(owner,true)) teile.Add(a.m_Area);
                if (EntityManager.HasBuffer<Game.Objects.SubObject>(owner))
                    foreach (var a in EntityManager.GetBuffer<Game.Objects.SubObject>(owner,true)) teile.Add(a.m_SubObject);
                if (EntityManager.HasBuffer<SubNet>(owner))
                    foreach (var a in EntityManager.GetBuffer<SubNet>(owner,true))
                    {
                        teile.Add(a.m_SubNet);
                        if (!EntityManager.HasComponent<Edge>(a.m_SubNet)) continue;
                        var e = EntityManager.GetComponentData<Edge>(a.m_SubNet);
                        teile.Add(e.m_Start); teile.Add(e.m_End);
                    }
            }
            foreach (var e in teile)
            {
                if (ParkingLotNetzRueckweg.Lebt(EntityManager,e) && !EntityManager.HasComponent<Temp>(e)
                    && EntityManager.HasComponent<Owner>(e))
                {
                    var o = EntityManager.GetComponentData<Owner>(e).m_Owner;
                    if (o == _lotOwner || o == _lotCarrier) _eigeneDauerteile.Add(e);
                }
                yield return 0;
            }
            _eigeneDauerteile.RemoveWhere(e => !ParkingLotNetzRueckweg.Lebt(EntityManager,e) || EntityManager.HasComponent<Temp>(e));
        }

        internal bool HintergrundWirtschaftBereit()
        {
            if (_editParkingFeeKnown && !_replacementEconomyTransferred)
            {
                if (!_editEconomySystem.TryInitializeReplacement(_lotOwner,_editParkingFee)) return false;
                _replacementEconomyTransferred = true;
            }
            return !_editBuildingEconomyEnabled || HasEnabledCompanion(_lotOwner);
        }

        internal void HintergrundUebernahme()
        {
            if (Mod.Aus("hintergrund-vor-uebernahme")) throw new InvalidOperationException("Fehlerprobe vor Uebernahme.");
            var k = _baukontext; var r = k.Zettel;
            var q = new Bauzettelquelle { Settings = _areaPreviewSettings, Punkte = k.Punkte,
                MedianWidth = r.MedianWidth, GreenMedian = r.GreenMedian, CrossBays = r.CrossBays,
                SurfaceRoadOn = r.SurfaceRoadOn, SurfaceDecorationOn = r.SurfaceDecorationOn,
                SurfaceApronOn = r.SurfaceApronOn, BayIcons = r.BayIcons,
                ZoningWinkelmodus = Winkelmodus.Dekodiere(r.ZoningWinkelmodus),
                ZoningReglerwinkel = r.ZoningReglerwinkel, ZoningAussentiefeVorwahl = r.ZoningAussentiefeVorwahl,
                ZoningAusrichtwinkel = double.IsNaN(r.ZoningAusrichtwinkel) ? (double?)null : r.ZoningAusrichtwinkel,
                Ausrichtwinkel = _areaPreviewSettings.Ausrichtwinkel, Ausrichtungen = k.Ausrichtungen,
                Trennschnitte = k.Schnitte, Zoningflaechen = k.Zonen, Seitenplan = k.Seitenplan,
                Kurven = k.Kurven,
                Randzoning = k.Randzoning, FlaecheStrasse = k.FlaecheStrasse,
                FlaecheDekoration = k.FlaecheDekoration, FlaecheZoning = k.FlaecheZoning };
            if (!SchreibeBauzettel(_lotOwner,q)) throw new InvalidOperationException("Neuer Bauzettel fehlt.");
            _nebenarbeitLot = _lotOwner;
            _nebenarbeitGeneration = World.GetOrCreateSystemManaged<ParkingLotAuftragsSystem>().Register.Generation;
            ParkingLotNebenarbeitDaten.Merke(EntityManager, _lotOwner, Mod.Optionen == null || Mod.Optionen.AutomatischVersorgung);
            if (!ParkingLotBaukontextLeser.TryRead(EntityManager,_lotOwner,out var gelesen,out var grund,melden:false))
                throw new InvalidOperationException("Neuer Bauzettel unvollstaendig: " + grund);
            ParkingLotNetzRueckweg.Melde($"Neuer Bauzettel geschrieben/gelesen: Lot {_lotOwner.Index}, {gelesen.Punkte.Length} Punkte, {gelesen.Zugaenge.Length} Zugaenge, {gelesen.Bushalte.Count} Bushalte.");
            NameLotOwner(_areaPreviewLayout.Stalls);
            AppendBuildJournal(_areaPreviewLayout,_areaPreviewSettings,k.Punkte,OwnerDisplayName(_lotOwner),
                _eigeneDauerteile.Count(e => EntityManager.HasComponent<Edge>(e) || EntityManager.HasComponent<Game.Net.Node>(e)),
                _objectRecords.Count,_areaTransferRecords.Count(a => a.Kind == "Grass"),_areaTransferRecords.Count(a => a.Kind == "Asphalt"));
            BeginReplacementCommit(_lotOwner,_lotCarrier);
            if (!PollReplacementCommit()) throw new InvalidOperationException("Ersatzuebernahme nicht abgeschlossen.");
        }

        private HintergrundPortion _ruecknahmeportion;
        internal bool HintergrundRuecknahmeFertig => _ruecknahmeportion?.Fertig == true;
        internal int HintergrundRuecknahme()
        {
            if (_ruecknahmeportion == null) _ruecknahmeportion = new HintergrundPortion(HintergrundRuecknahmeschritte(),_hintergrundTempo);
            var uhr = Stopwatch.StartNew();
            return _ruecknahmeportion.Weiter(() => uhr.Elapsed.TotalMilliseconds);
        }
        private IEnumerable<int> HintergrundRuecknahmeschritte()
        {
            // Abriss wird am Gate ausgefuehrt, vor Modification1/2. Die ganze
            // neue Besitzerkette faellt; ALTES Lot und erhaltene Netzstrassen leben.
            if (_lotOwner == Entity.Null && _areaTransferRecords.Count == 1) HintergrundBesitzerDa();
            ParkingLotNetzerhalt.EntferneGelieheneVerweise(EntityManager,_editLot,_lotOwner,_lotCarrier);
            foreach (int n in ParkingLotErsatzabriss.Schritte(EntityManager,_editLot,_lotOwner,_lotCarrier)) yield return n;
            foreach (int n in RestoreHiddenPartsSchritte()) yield return n;
            VerwerfeEdithoehen();
        }
    }
}
