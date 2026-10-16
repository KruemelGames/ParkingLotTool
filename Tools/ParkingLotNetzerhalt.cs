using System;
using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Entities;
using Unity.Mathematics;
using SubNet = Game.Net.SubNet;

namespace ParkingLotTool.Tools
{
    internal static class ParkingLotNetzerhalt
    {
        // SubNet ist auch Vanilla-Loeschkaskade. Der neue Lot darf bis zur
        // Uebernahme auf erhaltene Gassen fuer Parkspuren verweisen; bei
        // Ruecknahme (auch nach Laden) muss dieser geliehene Bezug zuerst weg.
        internal static void EntferneGelieheneVerweise(EntityManager em, Entity alt, Entity neu, Entity traeger)
        {
            if (!em.HasBuffer<ParkingLotErhaltenerKurs>(alt)) return;
            var kanten = new HashSet<Entity>();
            foreach (var r in em.GetBuffer<ParkingLotErhaltenerKurs>(alt,true)) kanten.Add(r.Bestand.Kante);
            foreach (var owner in new[] {neu,traeger})
                if (em.HasBuffer<SubNet>(owner))
                {
                    var b = em.GetBuffer<SubNet>(owner);
                    for (int i=b.Length-1;i>=0;i--) if (kanten.Contains(b[i].m_SubNet)) b.RemoveAt(i);
                }
        }

        internal static bool Verbunden(EntityManager em, Entity node, Entity edge)
        {
            if (!ParkingLotNetzRueckweg.Lebt(em,node) || !em.HasComponent<Node>(node)
                || !em.HasBuffer<ConnectedEdge>(node)) return false;
            foreach (var c in em.GetBuffer<ConnectedEdge>(node,true)) if (c.m_Edge == edge) return true;
            return false;
        }

        internal static bool Pruefe(EntityManager em, ParkingLotRueckwegkurs alt)
        {
            var e = alt.Kante;
            if (!ParkingLotNetzRueckweg.Lebt(em,e) || em.HasComponent<Temp>(e)
                || !em.HasComponent<Edge>(e) || !em.HasComponent<Curve>(e)
                || !em.HasComponent<PrefabRef>(e) || !em.HasComponent<Owner>(e)) return false;
            var k = em.GetComponentData<Edge>(e);
            bool verbunden = Verbunden(em,k.m_Start,e) && Verbunden(em,k.m_End,e);
            bool knoten = k.m_Start == alt.Start && k.m_End == alt.Ende;
            float d = HintergrundAbgleich.Kurvenabstand(ParkingLotKursabgleich.Form(em.GetComponentData<Curve>(e).m_Bezier),
                ParkingLotKursabgleich.Form(alt.Kurs.m_Curve));
            if (verbunden)
            {
                d = math.max(d,math.distance(em.GetComponentData<Node>(k.m_Start).m_Position,alt.Kurs.m_StartPosition.m_Position));
                d = math.max(d,math.distance(em.GetComponentData<Node>(k.m_End).m_Position,alt.Kurs.m_EndPosition.m_Position));
            }
            bool upgrade = em.HasComponent<Upgraded>(e) == alt.HatUpgrade
                && (!alt.HatUpgrade || em.GetComponentData<Upgraded>(e).Equals(alt.Upgrade));
            return Netzerhalt.Unveraendert(em.GetComponentData<PrefabRef>(e).m_Prefab == alt.Prefab,
                true,knoten,verbunden,upgrade,d) && em.GetComponentData<Owner>(e).m_Owner == alt.Besitzer;
        }
    }

    public sealed partial class ParkingLotToolSystem
    {
        private readonly Dictionary<(string Kind,int Index),List<Entity>> _erhalteneKursketten = new Dictionary<(string,int),List<Entity>>();
        private readonly Dictionary<int,Gassenkurs> _hintergrundGassenkurse = new Dictionary<int,Gassenkurs>();
        private int _nichtBaubareHintergrundGassen;

        // Zoning und Sync-Gassen verwenden dieselbe Kettenpruefung. Die
        // gespeicherte Hoehe bleibt stehen: das Layout hat nur XZ-Kurse;
        // die anschliessende Abnahme misst alle vier 3D-Kontrollpunkte.
        private bool FindeNetzerhalt(int index, NetSegment piece, float2 von, float2 nach,
            Entity prefab, Entity stadt, Entity[] kandidaten, HashSet<Entity> ziel, bool merken, string art = null)
        {
            var pruefung = new List<Netzerhalt.Teil<Entity>>();
            foreach (var e in kandidaten)
            {
                if (ziel.Contains(e) || !ParkingLotNetzRueckweg.Lebt(EntityManager,e)
                    || EntityManager.HasComponent<Temp>(e) || !EntityManager.HasComponent<Edge>(e)
                    || !EntityManager.HasComponent<Curve>(e) || !EntityManager.HasComponent<PrefabRef>(e)
                    || !EntityManager.HasComponent<Owner>(e)
                    || !ParkingLotBesitz.GehoertZu(EntityManager,EntityManager.GetComponentData<Owner>(e).m_Owner,_editLot)) continue;
                var c = EntityManager.GetComponentData<Curve>(e).m_Bezier;
                var k = EntityManager.GetComponentData<Edge>(e);
                bool seiten = true;
                if (piece.Kind == "zoning" && EntscheideZoningseiten(c.a.xz,c.d.xz,false,
                    out bool links,out bool rechts,out _)) seiten = LiestSeite(e,true) == links && LiestSeite(e,false) == rechts;
                pruefung.Add(new Netzerhalt.Teil<Entity> { Kante=e,Start=k.m_Start,Ende=k.m_End,
                    Kurve=ParkingLotKursabgleich.Form(c),
                    PrefabGleich=EntityManager.GetComponentData<PrefabRef>(e).m_Prefab == prefab,
                    Verbunden=ParkingLotNetzerhalt.Verbunden(EntityManager,k.m_Start,e)
                        && ParkingLotNetzerhalt.Verbunden(EntityManager,k.m_End,e),SeitenGleich=seiten });
            }
            var teile = Netzerhalt.FindeKette(von,nach,stadt,Zufahrtsarten.FaehrtHinaus(piece.Art),Entity.Null,pruefung);
            if (teile.Count == 0) return false;
            var kanten = new List<Entity>();
            foreach (var p in teile)
            {
                kanten.Add(p.Kante); ziel.Add(p.Kante); ziel.Add(p.Start); ziel.Add(p.Ende);
            }
            if (merken) _erhalteneKursketten.Add((art ?? piece.Kind,index),kanten);
            return true;
        }

        private IEnumerable<int> PlaneNetzerhaltSchritte()
        {
            _hintergrundRoutenpunkte.Clear();
            _erhalteneKursketten.Clear(); _hintergrundGassenkurse.Clear(); _nichtBaubareHintergrundGassen = 0;
            foreach (int n in SammleErhalteneZoningteileSchritte(_erhalteneNetzteile,true)) yield return n;
            _zoningErhalten = _erhalteneNetzteile.Count > 0;
            yield return 0;
            // Der normale Edit hat bislang keinen Gassenerhalt; dessen
            // Bau-/Apply-Verhalten bleibt in Runde 8 unveraendert.
            if (!_bauarbeiter) yield break;
            var kandidaten = ParkingLotNetzRueckweg.Besitzteile(EntityManager,_editLot);
            TryChooseDrivablePath(_areaPreviewSettings.Ai,out var breit,out _);
            TryChooseDrivablePath(_areaPreviewSettings.Cw,out var schmal,out _);
            var bericht = new List<string>();
            var hoehen = new Dictionary<(long,long),float>();
            for (int i = 0; i < _areaPreviewLayout.NetLine.Length; i++)
            {
                var piece = _areaPreviewLayout.NetLine[i];
                if (piece.Kind == "zoning") continue;
                if (piece.Kind != "entrance" || !Zufahrtsarten.IstGasse(piece.Art))
                {
                    foreach (var kurs in InnereNetzkurse(piece,breit,schmal))
                    {
                        if (kurs.Prefab != Entity.Null)
                            FindeNetzerhalt(i,piece,kurs.A,kurs.B,kurs.Prefab,Entity.Null,
                                kandidaten,_erhalteneNetzteile,true,kurs.Kind);
                        yield return 0;
                    }
                    continue;
                }
                float breite = (float)new Entrance { Art=piece.Art }.Breite(_areaPreviewSettings.Ai,_areaPreviewSettings.Gassenbreite);
                if (PlaneGassenkurs(piece,i,breite,hoehen,bericht,out var p))
                {
                    _hintergrundGassenkurse.Add(i,p);
                    MerkeAnschlussrouten(p.Anschluss.Entity);
                    bool stadt = EntityManager.HasComponent<Node>(p.Anschluss.Entity) && Stadtarme(p.Anschluss.Entity) > 0;
                    bool erhalten = stadt && FindeNetzerhalt(i,piece,p.Von,p.Nach,p.Prefab,p.Anschluss.Entity,
                        kandidaten,_erhalteneNetzteile,true,"entrance-gasse");
                    ParkingLotNetzRueckweg.Melde($"Netzerhalt Zufahrt {i}: Kurs {p.Von}->{p.Nach}, Klon {p.Prefab}, Stadtanschluss {p.Anschluss.Entity}, Stadtarme {Stadtarme(p.Anschluss.Entity)}; erhalten={erhalten}.");
                }
                else
                {
                    _nichtBaubareHintergrundGassen++;
                    ParkingLotNetzRueckweg.Melde($"Zufahrt {i}: Plan nicht baubar; " + string.Join("; ",bericht));
                }
                yield return 0;
            }
            ParkingLotNetzRueckweg.Melde($"Netzerhalt: {_erhalteneKursketten.Count} unveraenderte Sollkurse, {_erhalteneNetzteile.Count} Kanten/Knoten bleiben stehen; Neubau nur bei Kurs-/Prefabwechsel oder unvollstaendigem Anschluss.");
        }

        private IEnumerable<(string Kind,float2 A,float2 B,Entity Prefab)> InnereNetzkurse(
            NetSegment p,Entity breit,Entity schmal)
        {
            if (p.Kind == "entrance" && p.Art == Zufahrtsart.Fussweg)
            {
                TryResolvePedestrianPath(out var fuss,gesetzterZugang:true);
                yield return ("entrance-pedestrian",p.A,p.B,fuss);
            }
            else if (p.Kind == "entrance" && (p.Art == Zufahrtsart.Einfahrt || p.Art == Zufahrtsart.Ausfahrt))
            {
                if (!TryResolvePathPrefab(OnewayPathName,out var auto)) yield break;
                bool hinaus = Zufahrtsarten.FaehrtHinaus(p.Art);
                var a = hinaus ? p.B : p.A; var b = hinaus ? p.A : p.B;
                yield return (hinaus ? "entrance-out" : "entrance-in",a,b,auto);
                if (!TryResolvePedestrianPath(out var fuss)) yield break;
                var v = b-a; float laenge = math.length(v);
                if (laenge < 1f) yield break;
                var normal = new float2(-v.y,v.x)/laenge;
                float offset = (OnewayPathWidth-PedestrianPathWidth)/2f;
                yield return ("entrance-ped-right",a-normal*offset,b-normal*offset,fuss);
                yield return ("entrance-ped-left",a+normal*offset,b+normal*offset,fuss);
            }
            else yield return (p.Kind,p.A,p.B,p.Kind == "cross" ? schmal : breit);
        }

        private int Stadtarme(Entity n)
        {
            int zahl = 0;
            if (!EntityManager.HasBuffer<ConnectedEdge>(n)) return 0;
            foreach (var c in EntityManager.GetBuffer<ConnectedEdge>(n,true))
                if (ParkingLotNetzRueckweg.Lebt(EntityManager,c.m_Edge) && EntityManager.HasComponent<Edge>(c.m_Edge)
                    && EntityManager.HasComponent<Road>(c.m_Edge) && !EntityManager.HasComponent<Owner>(c.m_Edge))
                { var e = EntityManager.GetComponentData<Edge>(c.m_Edge); if (e.m_Start == n || e.m_End == n) zahl++; }
            return zahl;
        }

        private bool ErhalteneKursketteDa(List<Entity> kanten)
        {
            if (!EntityManager.HasBuffer<ParkingLotErhaltenerKurs>(_editLot)) return false;
            foreach (var e in kanten)
            {
                bool gefunden = false;
                foreach (var r in EntityManager.GetBuffer<ParkingLotErhaltenerKurs>(_editLot,true))
                    if (r.Bestand.Kante == e)
                    {
                        gefunden = ParkingLotNetzerhalt.Pruefe(EntityManager,r.Bestand);
                        if (!gefunden) _fehlendeErhalteneKurse.Add(r.Bestand);
                        break;
                    }
                if (!gefunden) return false;
            }
            return kanten.Count > 0;
        }
    }
}
