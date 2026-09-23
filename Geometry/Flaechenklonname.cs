using System;
using System.Collections.Generic;

namespace ParkingLotTool.Geometry
{
    /**
     * WIE UNSERE FLAECHENKLONE HEISSEN - UND ZURUECK.
     *
     * Der Name ist nicht Kosmetik. Ein Spielstand merkt sich eine Flaeche
     * ueber den NAMEN ihres Prefabs. Gibt es beim Laden keinen Klon mit
     * genau diesem Namen, wird das Teil "obsolet": es behaelt seine Form,
     * verliert aber sein Material. Der Nutzer sieht dann blasse Flaechen
     * ohne Gras - genau das meldete ein Tester am 2026-09-23 (124 von 124
     * Teilen tot).
     *
     * Deshalb gilt hier dreierlei:
     *
     * 1. EIN NAME, EIN KLON. Bis zum 2026-09-24 hiessen Vorflaeche (-90)
     *    und Zoningbelag (-94) desselben Vorbilds beide
     *    "PLT Zoningbelag (X)" - die Unterscheidung fragte nur "Aufschlag
     *    gleich 0?", und seit die Vorflaeche am 2026-09-18 auf -90 ging,
     *    war sie nicht mehr 0. Im Log standen danach zwei FERTIGE Klone
     *    gleichen Namens (Batch 27 und 29). Welcher davon beim Laden
     *    zurueckkam, war nicht festgelegt.
     *
     * 2. ALTE NAMEN BLEIBEN GUELTIG. Spielstaende verweisen auf sie.
     *    "PLT Vorflaeche (X)" bleibt Aufschlag 0, "PLT Zoningbelag (X)"
     *    bleibt der Zoningbelag. Nur der Fall, der vorher kollidierte,
     *    bekommt einen neuen Namen. Ein Vorflaechenteil aus einem alten
     *    Spielstand kommt dadurch als Zoningbelag zurueck: zwei Stufen
     *    andere Zeichenreihenfolge, aber mit Material.
     *
     * 3. DER NAME IST UMKEHRBAR. Ein totes Teil verraet ueber
     *    `PrefabSystem.GetPrefabName` den Namen, den es gesucht hat. Daraus
     *    liest `TryLese` Vorbild, Aufschlag und Raeumwirkung zurueck - das
     *    ist die Grundlage der Reparatur beim Laden. `--klonnamen` prueft
     *    Hin- und Rueckweg fuer jede Variante.
     */
    public static class Flaechenklonname
    {
        /** Zeichenprioritaet von Vorflaeche und Asphaltbelag. */
        public const int Vorflaeche = -90;

        /** Zeichenprioritaet von Zoningbelag, Parzellenboden und Gruen. */
        public const int Zoning = -94;

        /**
         * Jede Kombination aus Prioritaet und Raeumwirkung, die ein Bau
         * anfordern kann - gelesen aus `ParkingLotAreaPreview`, wo Vorflaeche,
         * Zoningbelag, Parzellenboden, Dekobelag und Asphaltbelag entstehen.
         * Jede Flaeche kann jede Rolle bekommen, weil der Nutzer frei waehlt.
         */
        public static readonly (int Aufschlag, bool Raeumt)[] Bauvarianten =
        {
            (Vorflaeche, false),
            (Zoning, false),
            (Zoning, true),
            (Vorflaeche, true),
        };

        /**
         * Flaechen, die das Spiel mitbringt, die aber kein Material sind -
         * namentlich bekannt, gemessen am 2026-08-21 (29 Flaechen, davon 16
         * brauchbar). Dazu kommen die Platzhalter, erkennbar am Namensende.
         */
        public static readonly string[] KeineMaterialien =
        {
            "Clip Surface", "Missing Area", "Surface Area",
        };

        private const string Vorsilbe = "PLT ";
        private const string Raeum = "PLT Raeumbelag (";
        private const string Vor = "PLT Vorflaeche (";
        private const string Zon = "PLT Zoningbelag (";

        public static bool IstEigener(string name)
            => name != null && name.StartsWith(Vorsilbe, StringComparison.Ordinal);

        /** Kommt ein eingebautes Prefab mit diesem Namen als Material in Frage? */
        public static bool IstMaterial(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (IstEigener(name)) return false;
            if (name.EndsWith("Placeholder", StringComparison.Ordinal)) return false;
            return Array.IndexOf(KeineMaterialien, name) < 0;
        }

        public static string Name(string vorbild, int aufschlag, bool raeumt)
        {
            if (raeumt) return Raeum + vorbild + ", " + aufschlag + ")";
            if (aufschlag == 0) return Vor + vorbild + ")";
            if (aufschlag == Zoning) return Zon + vorbild + ")";
            return Vor + vorbild + ", " + aufschlag + ")";
        }

        /**
         * Der Rueckweg. Das Vorbild darf selbst Kommas und Klammern tragen -
         * Mod-Flaechen heissen etwa "G87 Vanilla Asphalt Pavement G87 VA
         * Surface ORM Surface". Gelesen wird deshalb von hinten.
         */
        public static bool TryLese(string name, out string vorbild,
            out int aufschlag, out bool raeumt)
        {
            vorbild = null;
            aufschlag = 0;
            raeumt = false;
            if (name == null || !name.EndsWith(")", StringComparison.Ordinal))
                return false;

            if (name.StartsWith(Raeum, StringComparison.Ordinal))
            {
                raeumt = true;
                return MitZahl(name, Raeum.Length, out vorbild, out aufschlag);
            }
            if (name.StartsWith(Zon, StringComparison.Ordinal))
            {
                vorbild = name.Substring(Zon.Length, name.Length - Zon.Length - 1);
                aufschlag = Zoning;
                return vorbild.Length > 0;
            }
            if (name.StartsWith(Vor, StringComparison.Ordinal))
            {
                // "PLT Vorflaeche (X, -90)" gegen "PLT Vorflaeche (X)". Ein
                // Vorbild, das selbst auf ", <Zahl>" endet, gibt es im Spiel
                // nicht; der Test haelt diese Annahme fest.
                if (MitZahl(name, Vor.Length, out vorbild, out aufschlag))
                    return true;
                vorbild = name.Substring(Vor.Length, name.Length - Vor.Length - 1);
                aufschlag = 0;
                return vorbild.Length > 0;
            }
            return false;
        }

        private static bool MitZahl(string name, int start, out string vorbild,
            out int aufschlag)
        {
            vorbild = null;
            aufschlag = 0;
            var komma = name.LastIndexOf(", ", StringComparison.Ordinal);
            if (komma < start) return false;
            var zahl = name.Substring(komma + 2, name.Length - komma - 3);
            if (!int.TryParse(zahl, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out aufschlag)) return false;
            vorbild = name.Substring(start, komma - start);
            return vorbild.Length > 0;
        }

        /** Fuer den Test: jede Variante eines Vorbilds bekommt einen eigenen Namen. */
        public static IEnumerable<string> AlleNamen(string vorbild)
        {
            yield return Name(vorbild, 0, false);
            foreach (var (a, r) in Bauvarianten) yield return Name(vorbild, a, r);
        }
    }
}
