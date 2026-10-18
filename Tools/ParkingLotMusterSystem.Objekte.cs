using System;
using System.Diagnostics;
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

        internal void PflegeObjekte(object layout, EchteVorschauArt art, long revision,
            Func<Objektplan> berechnen, bool abbrechen)
        {
            if (!_ladebereit) { Warte("Ladebereinigung steht aus"); return; }
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
                var hoehen=_terrain.GetHeightData(waitForPending:true);
                for (int i=0;i<bereit.Length;i++)
                {
                    var definition=_objektplan.Objekte[i].Erzeuge(ref hoehen);
                    if (definition == Entity.Null) throw new InvalidOperationException("Ungueltige Objekthoehe; kein Teilsatz.");
                    EntityManager.AddComponent<ParkingLotMusterflaeche>(definition);
                    var daten=EntityManager.GetComponentData<CreationDefinition>(definition);
                    if (daten.m_Owner != Entity.Null || daten.m_Original != Entity.Null || daten.m_Attached != Entity.Null
                        || EntityManager.HasComponent<OwnerDefinition>(definition)
                        || EntityManager.HasComponent<Owner>(definition) || EntityManager.HasComponent<ParkingLotPartRelation>(definition))
                        throw new InvalidOperationException("Objektdefinition hat einen fremden Bezug.");
                    daten.m_Prefab=bereit[i]; daten.m_Flags=CreationFlags.Permanent;
                    EntityManager.SetComponentData(definition,daten);
                    ParkingLotToolSystem.NurDiesesBild(EntityManager,definition);
                }
                _ausgabeStand=stand; _warteGeometrie=_warteSichtbar=_soll>0;
                _bild=UnityEngine.Time.frameCount; _bildzeitVorher=UnityEngine.Time.unscaledDeltaTime*1000;
                World.GetExistingSystemManaged<ParkingLotToolSystem>()?.MusterMaske(
                    _soll>0 ? _objektplan.Ausschnitt : (OberflaechenMuster.Bereich?)null,art);
                _wartet=null;
                Melde($"Satz {stand} angefordert: {art}, {_soll} Objekte; verkleinert {_objektplan.Verkleinerungen}; "
                    + $"Auswahl {_schnittMs:F3} ms, Definition+Maskenwechsel {ausgabe.Elapsed.TotalMilliseconds:F3} ms");
            }
            catch (Exception e) { Fehler(art + ": " + e.Message); }
        }
    }
}
