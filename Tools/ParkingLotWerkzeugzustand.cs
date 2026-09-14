using ParkingLotTool.Geometry;

namespace ParkingLotTool.Tools
{
    /**
     * DER REITER ERREICHT ENDLICH DAS WERKZEUG.
     *
     * Ansage des Nutzers am 2026-09-09: *"Also z.B. bin ich im Reiter
     * 'Parkplaetze', kann ich weiter bauen, was sinnlos ist. Waehrend ich
     * gerade Zugaenge baue, dann kann ich die Polygonpunkte verschieben."*
     *
     * `OnUpdate` verteilte die Klicks bis dahin ueber eine feste Kette und
     * fragte den Reiter nirgends. Wer in der Kette zuerst stand, bekam den
     * Klick; alles darunter blieb offen. Diese Datei ist die Bruecke zur
     * Tabelle in `Geometry/Werkzeugzustand.cs` - dort steht, WAS erlaubt ist,
     * und dort kann das Testprojekt es pruefen. Hier steht nur, WO das
     * Werkzeug gerade ist.
     */
    public sealed partial class ParkingLotToolSystem
    {
        private Werkzeugreiter _reiter = Werkzeugreiter.Entwurf;

        internal Werkzeugreiter AktuellerReiter => _reiter;

        /**
         * Der Modus wird ABGELEITET, nicht gefuehrt.
         *
         * Ein zweiter, mitgefuehrter Zustand waere die naechste Falle: er
         * kann von den vorhandenen Fahnen abweichen, und dann streiten zwei
         * Wahrheiten um denselben Klick. Dieselbe Lehre wie beim Ausrichten,
         * wo zwei gespiegelte Anzeigen den Nutzer am 2026-09-01 festgesetzt
         * haben.
         *
         * Die Reihenfolge ist die der Klickkette: wer den Klick zuerst
         * braucht, steht oben.
         */
        internal Werkzeugmodus AktuellerModus
        {
            get
            {
                if (MarkerMode) return Werkzeugmodus.Meldemarke;
                if (ZoningSeitenModus) return Werkzeugmodus.Zoningseite;
                if (ZoningModus) return Werkzeugmodus.Zoningflaeche;
                if (AusrichtWahlAktiv) return Werkzeugmodus.Ausrichten;
                if (EntranceModeAktiv) return Werkzeugmodus.Zugang;
                return Werkzeugmodus.Grund;
            }
        }

        /** Darf der naechste Klick das tun? */
        internal bool DarfArbeiten(Weltarbeit arbeit)
            => Werkzeugzustand.Erlaubt(_reiter, AktuellerModus, arbeit);

        /** Der Panel-Reitername als Zustand. */
        internal static Werkzeugreiter ReiterAus(string name)
        {
            switch (name)
            {
                case "zoning": return Werkzeugreiter.Zoning;
                case "liste": return Werkzeugreiter.Liste;
                case "debug": return Werkzeugreiter.Debug;
                case "report": return Werkzeugreiter.Melden;
                default: return Werkzeugreiter.Entwurf;
            }
        }

        /**
         * KEIN MODUS UEBERLEBT EINEN REITERWECHSEL.
         *
         * Das galt bisher nur fuer die zwei Zoning-Modi, und die Begruendung
         * dafuer stand schon im Panel: *"Ein Modus, der einen Reiter
         * ueberlebt, ist eine Falle: der naechste Linksklick geht dann
         * irgendwo hin, wo der Nutzer gerade gar nicht arbeitet."* Genau das
         * gilt fuer den Zugangsmodus und das Ausrichten ebenso.
         *
         * Der Melde-Modus wird weiter im Panel abgeraeumt: dort haengen die
         * Markierungen dran, die mit ihm verschwinden sollen.
         */
        internal void SetzeReiter(Werkzeugreiter reiter)
        {
            if (_reiter == reiter) return;
            _reiter = reiter;

            if (AusrichtWahlAktiv
                && !Werkzeugzustand.ReiterErlaubt(reiter, Weltarbeit.Ausrichtwahl))
                AbortAusrichtWahl("Reiterwechsel");

            if (EntranceModeAktiv
                && !Werkzeugzustand.ReiterErlaubt(reiter, Weltarbeit.Zugang))
                SetEntranceModeFromPanel(false);

            // Der Zoning-Reiter bringt sein Werkzeug mit; jeder andere legt
            // beide ab. Das tat bisher `SetTab` selbst - jetzt an einer
            // Stelle, damit die Regel nicht wieder auseinanderlaeuft.
            if (ZoningSeitenModus
                && !Werkzeugzustand.ReiterErlaubt(reiter, Weltarbeit.Zoningseite))
                SetzeZoningSeitenModus(false);
            var zoningAn = Werkzeugzustand.ModusBeimReiter(reiter)
                == Werkzeugmodus.Zoningflaeche;
            if (ZoningModus != zoningAn) SetzeZoningModus(zoningAn);
        }
    }
}
