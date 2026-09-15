using Game.Areas;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /**
     * EINEN GEBAUTEN PARKPLATZ ANKLICKEN UND MELDEN.
     *
     * WOZU. Im Auswahlfenster eines Parkplatzes steht seit langem "Report
     * this lot". Das setzt aber voraus, dass man ihn ausgewaehlt bekommt -
     * und solange unser Werkzeug offen ist, verbraucht es den Klick selbst.
     * Ein Tester, der beim Arbeiten etwas Krummes sieht, muesste also erst
     * das Werkzeug schliessen, den Parkplatz anklicken, melden und wieder
     * aufmachen. Ansage des Nutzers am 2026-09-15: *"Bitte noch einen Button
     * einfuegen fuer 'report a Parking lot' wo man einfach nen Parkplatz
     * anklickt und das gleiche passiert."*
     *
     * WIE GETROFFEN WIRD - und warum nicht per Strahl. Das Werkzeug schickt
     * seinen Strahl mit `TypeMask.Terrain` los; er liefert einen Punkt auf
     * dem Boden, keine Entity. Diese Maske umzustellen haette Folgen fuer
     * jeden anderen Klick des Werkzeugs. Der Punkt reicht aber voellig: ein
     * PLT-Parkplatz ist eine Flaeche mit Knotenpuffer, und "liegt dieser
     * Punkt darin" ist dieselbe Frage wie bei den Teilflaechen.
     *
     * Gemeldet wird danach ueber GENAU denselben Weg wie aus dem
     * Auswahlfenster - `FordereLotAbzug` plus das Einpacken einen Durchgang
     * spaeter. Zwei Wege zum selben Ergebnis waeren zwei Wege, die
     * auseinanderlaufen koennen.
     */
    public sealed partial class ParkingLotToolSystem
    {
        private EntityQuery _meldeLots;
        private bool _meldeLotWahl;
        private Entity _meldeLotUnterZeiger = Entity.Null;

        internal bool MeldeLotWahlAktiv => _meldeLotWahl;
        internal Entity MeldeLotUnterZeiger => _meldeLotUnterZeiger;

        private void InitialisiereMeldewahl()
        {
            _meldeLots = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<ParkingLotCarrierReference>(),
                    ComponentType.ReadOnly<Area>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Game.Common.Deleted>(),
                    ComponentType.ReadOnly<Game.Tools.Temp>(),
                },
            });
        }

        /** Vom Panel aus: an und aus. */
        internal void SchalteMeldeLotWahl(bool an)
        {
            _meldeLotWahl = an;
            _meldeLotUnterZeiger = Entity.Null;
            _uiSystem?.SetMeldeLotWahl(an);
            _uiSystem?.SetStatus(an
                ? ParkingLotTexte.T(
                    "Einen gebauten Parkplatz anklicken. Rechtsklick bricht ab.",
                    "Click a parking lot you built. Right-click to cancel.")
                : ParkingLotTexte.T("Abgebrochen.", "Cancelled."));
        }

        /**
         * Sucht je Durchgang den Parkplatz unter dem Zeiger.
         *
         * Billig: ein paar Dutzend Flaechen mit je einer Handvoll Knoten. Die
         * Vorpruefung ueber das Huellrechteck spart den Rest.
         */
        private void PflegeMeldeLotWahl()
        {
            if (!_meldeLotWahl) { _meldeLotUnterZeiger = Entity.Null; return; }
            _meldeLotUnterZeiger = Entity.Null;
            if (!_hasHover) return;
            var punkt = new float2(_hoverPosition.x, _hoverPosition.z);

            using var lots = _meldeLots.ToEntityArray(Allocator.Temp);
            for (var i = 0; i < lots.Length; i++)
            {
                var lot = lots[i];
                if (!EntityManager.HasBuffer<Node>(lot)) continue;
                var knoten = EntityManager.GetBuffer<Node>(lot, true);
                if (knoten.Length < 3) continue;

                var min = new float2(float.MaxValue);
                var max = new float2(float.MinValue);
                for (var k = 0; k < knoten.Length; k++)
                {
                    var p = knoten[k].m_Position.xz;
                    min = math.min(min, p);
                    max = math.max(max, p);
                }
                if (punkt.x < min.x || punkt.x > max.x
                    || punkt.y < min.y || punkt.y > max.y) continue;

                var innen = false;
                for (int a = 0, b = knoten.Length - 1; a < knoten.Length; b = a++)
                {
                    var pa = knoten[a].m_Position.xz;
                    var pb = knoten[b].m_Position.xz;
                    if (pa.y > punkt.y != pb.y > punkt.y
                        && punkt.x < (pb.x - pa.x) * (punkt.y - pa.y)
                            / (pb.y - pa.y) + pa.x)
                        innen = !innen;
                }
                if (!innen) continue;
                _meldeLotUnterZeiger = lot;
                return;
            }
        }

        /**
         * Verbraucht den Klick, solange die Wahl laeuft.
         *
         * Rueckgabe `true` heisst: das Polygon bekommt diesen Klick nicht mehr
         * zu sehen. Ohne das wuerde derselbe Linksklick auch noch einen Punkt
         * setzen - derselbe Fallstrick wie beim Ausrichten.
         */
        private bool HandleMeldeLotWahl(bool secondaryPressed,
                                        bool escapePressed)
        {
            if (!_meldeLotWahl) return false;
            if (secondaryPressed || escapePressed)
            {
                SchalteMeldeLotWahl(false);
                return true;
            }
            var maus = UnityEngine.InputSystem.Mouse.current;
            if (maus == null || !maus.leftButton.wasPressedThisFrame)
                return true;
            if (_meldeLotUnterZeiger == Entity.Null)
            {
                _uiSystem?.SetStatus(ParkingLotTexte.T(
                    "Kein Parkplatz unter dem Zeiger.",
                    "No parking lot under the cursor."));
                return true;
            }
            var lot = _meldeLotUnterZeiger;
            _meldeLotWahl = false;
            _meldeLotUnterZeiger = Entity.Null;
            _uiSystem?.SetMeldeLotWahl(false);
            // DERSELBE WEG wie aus dem Auswahlfenster - siehe
            // ParkingLotUISystem.MeldeGewaehltenParkplatz.
            _uiSystem?.MeldeParkplatz(lot);
            return true;
        }
    }
}
