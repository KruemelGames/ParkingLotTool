using UnityEngine;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        // Drei native Abstürze endeten nach der Flächenvorschau ohne Stacktrace.
        // Vor jedem Bauabschnitt melden, auch vor der Übergabe an CS2.
        private void ProtokolliereBauschritt(string schritt)
        {
            Mod.log.Info($"PLT-Bauschritt Frame {UnityEngine.Time.frameCount}: BEGINN {schritt}");

            /*
             * UND IN DIE SPUR, DIE SOFORT AUF DER PLATTE LIEGT.
             *
             * Die Zeile darueber geht in den gewoehnlichen Logschreiber, und
             * der puffert. Bei einem nativen Absturz fehlen deshalb
             * ausgerechnet die letzten Zeilen - also die, die sagen, WO es
             * passiert ist. `ParkingLotSchrittmarke` schreibt durch.
             *
             * Hier und nicht an 25 einzelnen Stellen: die Bauschritte sind
             * schon die richtige Koernung, sie stehen ohnehin vor jedem
             * Abschnitt und vor der Uebergabe an CS2.
             */
            ParkingLotSchrittmarke.Setze("Bau: " + schritt);
        }
    }
}

