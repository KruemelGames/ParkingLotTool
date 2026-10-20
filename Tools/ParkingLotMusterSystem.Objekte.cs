using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    internal sealed partial class ParkingLotMusterSystem
    {
        internal delegate Entity ObjektErzeuger(ref TerrainHeightData hoehen);
        internal sealed class Objekt
        {
            public float2 Position;
            public Entity Vorbild;
            public int Alter = -1;
            public ObjektErzeuger Erzeuge;
        }
        internal sealed class Objektplan
        {
            public OberflaechenMuster.Bereich Ausschnitt;
            public Objekt[] Objekte;
            public int Verkleinerungen;
        }
        private EchteVorschauArt _art;
        private Objektplan _objektplan;
        private EntityQuery _objekte;
        private object _pflegeObjektLayout;
        private long _pflegeObjektRevision;

        /**
         * GESTAFFELT STATT ALLES IN EINEM BILD (Nutzer 20.10.).
         *
         * Bis zu 256 Baeume kamen in einem einzigen Bild; schaltete der Nutzer
         * neue Arten ein, die noch nirgends auf der Karte standen, musste CS2
         * deren Modelle und alle Instanzen auf einmal anlegen - deutliche
         * Ruckler. Jetzt zuerst je Darstellungsklon EIN Exemplar (damit CS2
         * die Modelle schon laedt), danach hoechstens `ObjekteJeBild` je Bild.
         * `_soll` ist von Anfang an die volle Zahl; die Pruefungen in OnUpdate
         * warten damit von selbst, bis alles steht.
         */
        private const int ObjekteJeBild = 32;
        private Entity[] _bereit;
        private int[] _reihenfolge;
        private int _ausgegeben;

        internal void PflegeObjekte(object layout, EchteVorschauArt art, long revision,
            Func<Objektplan> berechnen, bool abbrechen)
        {
            if (!_ladebereit) { Warte("Ladebereinigung steht aus"); return; }
            if (_reihenfolge != null && !abbrechen && _wunsch && _art==art && ReferenceEquals(layout,_pflegeObjektLayout)
                && revision==_pflegeObjektRevision && _ausgabeStand==_wunschStand && _neuester.IstAktuell(_wunschStand))
            {
                try { GibAus(ObjekteJeBild); }
                catch (Exception e) { Fehler(art + ": " + e.Message); }
                return;
            }
            if (!abbrechen && _wunsch && _art==art && ReferenceEquals(layout,_pflegeObjektLayout)
                && revision==_pflegeObjektRevision
                && (_fehler!=null || _ausgabeStand==_wunschStand || !_neuester.IstAktuell(_wunschStand))) return;
            _pflegeObjektLayout=layout; _pflegeObjektRevision=revision;
            string wahl = art + "|" + revision;
            long stand = _neuester.Merke(layout,wahl);
            if (abbrechen) _neuester.Stoppe();
            if (!_neuester.IstAktuell(stand))
            { _wunsch=true; EntferneAbgebrochenenSatz(); return; }
            if (_wunsch && _ausgabeStand == stand && _wahl == wahl) return;
            try
            {
                if (!_wunsch || _wunschStand != stand)
                {
                    if (_wunsch) Bereinige(); _objektplan=null; _plan=null; _fehler=null;
                    _anforderungBeginn=Stopwatch.GetTimestamp(); _anforderungBild=UnityEngine.Time.frameCount;
                }
                _wunsch=true; _wahl=wahl; _wunschStand=stand; _art=art;
                if (_fehler != null) return;
                if (_objektplan == null)
                {
                    var uhr=Stopwatch.StartNew();
                    _objektplan=berechnen(); _schnittMs=uhr.Elapsed.TotalMilliseconds;
                }
                var klone=World.GetOrCreateSystemManaged<ParkingLotMusterobjektPrefabSystem>();
                var bereit=new Entity[_objektplan.Objekte.Length];
                bool warten=false;
                for (int i=0;i<bereit.Length;i++)
                {
                    var o=_objektplan.Objekte[i];
                    bereit[i]=klone.FordereAn(o.Vorbild,o.Alter,out bool fehler);
                    if (fehler) { Fehler(art + ": Darstellungsklon nicht sicher verfuegbar"); return; }
                    if (bereit[i] == Entity.Null) warten=true;
                }
                if (warten)
                {
                    World.GetExistingSystemManaged<ParkingLotToolSystem>()?.MusterMaske(null);
                    Warte(art + ": Darstellungsklone noch nicht fertig"); return;
                }
                if (!_neuester.IstAktuell(stand)) return;
                var ausgabe=Stopwatch.StartNew(); _beginn=Stopwatch.GetTimestamp();
                Bereinige(); _soll=bereit.Length;
                // Erst je Darstellungsklon ein Exemplar, dann der Rest in Planfolge.
                var erste=new List<int>(); var rest=new List<int>(); var gesehen=new HashSet<Entity>();
                for (int i=0;i<bereit.Length;i++) (gesehen.Add(bereit[i]) ? erste : rest).Add(i);
                _bereit=bereit; _reihenfolge=erste.Concat(rest).ToArray(); _ausgegeben=0;
                _ausgabeStand=stand; _bild=UnityEngine.Time.frameCount;
                if (_reihenfolge.Length > 0) GibAus(Math.Max(1,Math.Min(erste.Count,ObjekteJeBild)));
                else _reihenfolge=null;
                _warteGeometrie=_warteSichtbar=_soll>0;
                _bild=UnityEngine.Time.frameCount; _bildzeitVorher=UnityEngine.Time.unscaledDeltaTime*1000;
                World.GetExistingSystemManaged<ParkingLotToolSystem>()?.MusterMaske(
                    _soll>0 ? _objektplan.Ausschnitt : (OberflaechenMuster.Bereich?)null,art);
                _wartet=null;
                Melde($"Satz {stand} angefordert: {art}, {_soll} Objekte ({gesehen.Count} Modelle zuerst, dann {ObjekteJeBild} je Bild); "
                    + $"verkleinert {_objektplan.Verkleinerungen}; "
                    + $"Auswahl {_schnittMs:F3} ms, erste Definitionen+Maskenwechsel {ausgabe.Elapsed.TotalMilliseconds:F3} ms");
            }
            catch (Exception e) { Fehler(art + ": " + e.Message); }
        }

        private void GibAus(int anzahl)
        {
            var hoehen=_terrain.GetHeightData(waitForPending:true);
            int ende=Math.Min(_ausgegeben+anzahl,_reihenfolge.Length);
            for (int k=_ausgegeben;k<ende;k++)
            {
                int i=_reihenfolge[k];
                var definition=_objektplan.Objekte[i].Erzeuge(ref hoehen);
                if (definition == Entity.Null) throw new InvalidOperationException("Ungueltige Objekthoehe; kein Teilsatz.");
                EntityManager.AddComponent<ParkingLotMusterflaeche>(definition);
                var daten=EntityManager.GetComponentData<CreationDefinition>(definition);
                if (daten.m_Owner != Entity.Null || daten.m_Original != Entity.Null || daten.m_Attached != Entity.Null
                    || EntityManager.HasComponent<OwnerDefinition>(definition)
                    || EntityManager.HasComponent<Owner>(definition) || EntityManager.HasComponent<ParkingLotPartRelation>(definition))
                    throw new InvalidOperationException("Objektdefinition hat einen fremden Bezug.");
                daten.m_Prefab=_bereit[i]; daten.m_Flags=CreationFlags.Permanent;
                EntityManager.SetComponentData(definition,daten);
                ParkingLotToolSystem.NurDiesesBild(EntityManager,definition);
            }
            _ausgegeben=ende;
            if (_ausgegeben < _reihenfolge.Length) return;
            Melde($"Satz {_ausgabeStand}: alle {_reihenfolge.Length} Objekte ausgegeben nach "
                + $"{UnityEngine.Time.frameCount-_bild} Bildern");
            _reihenfolge=null; _bereit=null;
        }
    }
}
