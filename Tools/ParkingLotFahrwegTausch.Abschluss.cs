using System;
using Game.Common;
using Game.Prefabs;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotFahrwegTauschWerkzeug
    {
        // Rueckmeldung ausserhalb des Catchs: ein werfender Empfaenger wird nie erneut aufgerufen.
        private void PruefeUndBeende()
        {
            bool ok;
            string text;
            try { ok = Pruefe(out text); }
            catch (Exception x) { ok = false; text = "Pruefung fehlgeschlagen: " + x.Message; }
            Beende(ok, text);
        }

        private bool Pruefe(out string text)
        {
            int getauscht = 0, besitzOk = 0;
            foreach (var (kante, ziel, besitzer) in _plan)
            {
                if (!EntityManager.Exists(kante) || EntityManager.HasComponent<Deleted>(kante)) continue;
                if (EntityManager.GetComponentData<PrefabRef>(kante).m_Prefab == ziel) getauscht++;
                if (EntityManager.HasComponent<Owner>(kante)
                    && EntityManager.GetComponentData<Owner>(kante).m_Owner == besitzer) besitzOk++;
            }
            text = $"{getauscht}/{_plan.Count} Wege mit neuem Prefab, Besitzer erhalten {besitzOk}/{_plan.Count}";
            return getauscht == _plan.Count && besitzOk == _plan.Count;
        }

        private void Beende(bool ok, string text)
        {
            if (_stufe == Stufe.Frei) return;
            var lot = _lot;
            // Freigabe vor Bereinigung: eine defekte Definition darf den Worker nicht sperren.
            _stufe = Stufe.Frei;
            _lot = Entity.Null;
            _plan.Clear();
            var fehler = 0;
            string ersterFehler = null;
            foreach (var d in _definitionen)
            {
                try { if (EntityManager.Exists(d)) EntityManager.DestroyEntity(d); }
                catch (Exception x) { fehler++; ersterFehler ??= d + ": " + x.Message; }
            }
            _definitionen.Clear();
            if (fehler > 0)
            {
                ok = false;
                text += $"; Bereinigung von {fehler} Definition(en) fehlgeschlagen: {ersterFehler}";
            }
            try
            {
                Mod.log.Info($"PLT-Fahrwegtausch: Lot {lot.Index}: {(ok ? "fertig" : "abgebrochen")} - {text}.");
                ParkingLotSchrittmarke.Aenderung("Fahrwegtausch: Lot " + lot.Index + (ok ? " fertig" : " abgebrochen"));
            }
            catch (Exception x)
            {
                try { Mod.log.Warn("PLT-Fahrwegtausch: Abschlussdiagnose fehlgeschlagen: " + x); }
                catch (Exception) { /* Auch der Logger darf die Rueckmeldung nicht verhindern. */ }
            }
            Fertig?.Invoke(lot, ok, text);
        }
    }
}
