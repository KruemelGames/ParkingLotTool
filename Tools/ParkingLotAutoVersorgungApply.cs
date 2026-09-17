using Game;
using Game.Common;
using Game.Net;
using Game.Tools;
using ParkingLotTool.Geometry;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private string _avLetzterApplyBefund;
        private string _avLetzterGesamtApplyBefund;

        internal void MesseAutoVersorgungNachToolOutput()
        {
            if (_avPhase == AvPhase.ApplyWarten) AvMesseApply("nach ToolOutputBarrier");
            // Auch waehrend Netz 2 gebaut wird, die Entities von Netz 1 verfolgen.
            // Die alte Messung liess zwischen Apply und 120 Simulationsframes eine Luecke.
            if (_avPhase != AvPhase.Idle && _avGebaut.Count > 0)
                AvMesseApply("alle angewandten Netze nach ToolOutputBarrier", true);
        }

        private VersorgungsapplyZustand AvMesseApply(string zeitpunkt, bool gesamt = false)
        {
            var erwartet = 0; var dauerhaft = 0; var temporaer = 0;
            var geloescht = 0; var verschwunden = 0;
            var details = new System.Collections.Generic.List<string>();
            foreach (var kurs in gesamt ? _avGebaut : _avKurse)
                foreach (var e in kurs.Kanten)
                {
                    erwartet++;
                    string status;
                    if (!EntityManager.Exists(e)) { verschwunden++; status = "verschwunden"; }
                    else if (EntityManager.HasComponent<Deleted>(e)) { geloescht++; status = "Deleted"; }
                    else if (EntityManager.HasComponent<Temp>(e))
                    {
                        temporaer++;
                        var temp = EntityManager.GetComponentData<Temp>(e);
                        status = $"Temp ({temp.m_Flags}, Original {temp.m_Original})";
                    }
                    else if (EntityManager.HasComponent<Edge>(e)) { dauerhaft++; status = "dauerhaft"; }
                    else { verschwunden++; status = "ohne Edge"; }
                    details.Add($"{kurs.Name}: {e} {status}");
                }
            var bilanz = $"dauerhaft {dauerhaft}/{erwartet}, Temp {temporaer}, "
                + $"Deleted {geloescht}, verschwunden {verschwunden}; " + string.Join("; ", details);
            if (bilanz != (gesamt ? _avLetzterGesamtApplyBefund : _avLetzterApplyBefund))
            {
                Mod.log.Info($"PLT-Autoversorgung APPLY-NACHWEIS ({zeitpunkt}, "
                    + $"Frame {UnityEngine.Time.frameCount}, Phase {_avPhase}, "
                    + $"ApplyMode Tool/Vanilla {applyMode}/{m_ToolSystem.applyMode}): {bilanz}.");
                if (gesamt) _avLetzterGesamtApplyBefund = bilanz;
                else _avLetzterApplyBefund = bilanz;
            }
            return VersorgungsapplyPruefung.Zustand(erwartet, dauerhaft, temporaer, geloescht, verschwunden);
        }
    }

    // SystemOrder.cs:694-697 fuehrt ToolOutputBarrier vor PostTool aus.
    // Ein Messpunkt dort trennt Verlust beim Apply von spaeterem Verlust;
    // die bisherigen 120 Simulationsframes bis zur Flussmessung konnten das nicht.
    public sealed partial class ParkingLotAutoVersorgungApplySystem : GameSystemBase
    {
        [Preserve]
        protected override void OnUpdate()
        {
            using var uhr = ParkingLotMessung.Miss(
                ParkingLotMessung.Sys.Versorgung);
            World.GetExistingSystemManaged<ParkingLotToolSystem>()
                ?.MesseAutoVersorgungNachToolOutput();
        }
    }
}
