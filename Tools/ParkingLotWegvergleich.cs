using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /**
     * SO GEBAUT - SO LIEGT ES JETZT (2026-10-09).
     *
     * Anlass: Issue #10. Auf den Screenshots liefen die Wege eines
     * Parkplatzes in Buendeln zu einem einzigen Punkt, quer ueber ein
     * Gebaeude. Das kann ein Fehler unseres Baus sein - oder jemand hat mit
     * Move It oder einem anderen Mod nachtraeglich Knoten verschoben. Der
     * Nutzer: "wenn ein User nachtraeglich am Parkplatz rumbaut ... bin ich
     * nicht der Verursacher primaer und somit hat das eine andere
     * Priorisierung."
     *
     * Bis hier konnte der Bericht das nicht unterscheiden: der Bauzettel im
     * Spielstand haelt nur die EINGABEN (Umriss, Einstellungen), und der
     * daraus neu gerechnete Plan stammt aus der Version, die gerade laeuft.
     * Was damals wirklich gebaut wurde, stand nirgends.
     *
     * Jetzt schreibt der Bau einen FINGERABDRUCK seiner Wege ins
     * Bauprotokoll (eine Datei neben den Logs, NICHT in den Spielstand):
     * Anfang und Ende jeder Kante des Traegers mit Prefabnamen, auf den
     * Zentimeter. Ein Bericht ueber einen gebauten Parkplatz vergleicht das
     * mit den Kanten, die jetzt am Traeger haengen. Unabhaengig davon
     * meldet er Knoten unserer Wege, die weit ausserhalb des Umrisses
     * liegen - dorthin baut der Mod nie, das geht auch ohne Fingerabdruck.
     */
    public sealed partial class ParkingLotToolSystem
    {
        /** Gleich heisst: beide Enden liegen hoechstens so weit auseinander. */
        private const float WegToleranz = 0.05f;

        /** Weiter ausserhalb des Umrisses legt der Bau keinen Knoten - auch die Zufahrt nicht. */
        private const float WegAusserhalbGrenze = 10f;

        /** Hoechstens so viele Einzelfaelle in den Bericht - er soll klein bleiben. */
        private const int WegHoechstzahlEinzelfaelle = 40;

        /** Der gebaute Parkplatz, ueber den der laufende Bericht geht. */
        private Entity _vergleichLot = Entity.Null;

        /** Die Klartextzeilen fuer die Zusammenfassung des laufenden Berichts. */
        private string _wegvergleichText;

        internal sealed class DebugWegvergleich
        {
            public string Stand { get; set; }
            public int Gebaut { get; set; }
            public int Jetzt { get; set; }
            public int Unveraendert { get; set; }
            public int VerschobenOderEntfernt { get; set; }
            public int Neu { get; set; }
            public int WegeAusserhalb { get; set; }
            public string[] Einzelfaelle { get; set; }
            public string Note { get; set; }
        }

        private readonly struct GebauterWeg
        {
            internal GebauterWeg(float2 a, float2 b, string prefab)
            {
                A = a;
                B = b;
                Prefab = prefab;
            }

            internal float2 A { get; }
            internal float2 B { get; }
            internal string Prefab { get; }

            internal bool Gleich(GebauterWeg o)
                => string.Equals(Prefab, o.Prefab, StringComparison.Ordinal)
                   && ((math.distance(A, o.A) <= WegToleranz && math.distance(B, o.B) <= WegToleranz)
                       || (math.distance(A, o.B) <= WegToleranz && math.distance(B, o.A) <= WegToleranz));

            internal string Text()
                => $"{Prefab} ({A.x.ToString("F2", CultureInfo.InvariantCulture)}/"
                   + $"{A.y.ToString("F2", CultureInfo.InvariantCulture)}) -> "
                   + $"({B.x.ToString("F2", CultureInfo.InvariantCulture)}/"
                   + $"{B.y.ToString("F2", CultureInfo.InvariantCulture)})";
        }

        /** Die Kanten, die jetzt am Traeger haengen - Temp beim Bau, dauerhaft danach. */
        private List<GebauterWeg> WegeDesTraegers(Entity traeger)
        {
            var wege = new List<GebauterWeg>();
            if (traeger == Entity.Null || !EntityManager.Exists(traeger)
                || !EntityManager.HasBuffer<Game.Net.SubNet>(traeger)) return wege;
            var puffer = EntityManager.GetBuffer<Game.Net.SubNet>(traeger, true);
            var kanten = new List<Entity>(puffer.Length);
            for (var i = 0; i < puffer.Length; i++) kanten.Add(puffer[i].m_SubNet);
            foreach (var kante in kanten)
            {
                if (!EntityManager.Exists(kante) || EntityManager.HasComponent<Deleted>(kante)
                    || !EntityManager.HasComponent<Game.Net.Edge>(kante)) continue;
                var edge = EntityManager.GetComponentData<Game.Net.Edge>(kante);
                if (!EntityManager.Exists(edge.m_Start) || !EntityManager.Exists(edge.m_End)
                    || !EntityManager.HasComponent<Game.Net.Node>(edge.m_Start)
                    || !EntityManager.HasComponent<Game.Net.Node>(edge.m_End)) continue;
                var a = EntityManager.GetComponentData<Game.Net.Node>(edge.m_Start).m_Position.xz;
                var b = EntityManager.GetComponentData<Game.Net.Node>(edge.m_End).m_Position.xz;
                var name = "?";
                if (EntityManager.HasComponent<PrefabRef>(kante) && _prefabSystem != null
                    && _prefabSystem.TryGetPrefab<PrefabBase>(
                        EntityManager.GetComponentData<PrefabRef>(kante).m_Prefab, out var p)
                    && p != null)
                    name = p.name;
                wege.Add(new GebauterWeg(a, b, name));
            }
            return wege;
        }

        /** Der Fingerabdruck fuers Bauprotokoll: ,"wege":[[ax,az,bx,bz,"Prefab"],...] */
        private string WegeFuersProtokoll(Entity traeger)
        {
            var wege = WegeDesTraegers(traeger);
            // Kein leerer Fingerabdruck: er hiesse spaeter "alles neu".
            if (wege.Count == 0) return string.Empty;
            var text = new StringBuilder(",\"wege\":[");
            for (var i = 0; i < wege.Count; i++)
            {
                if (i > 0) text.Append(',');
                var w = wege[i];
                text.Append('[').Append(Num(w.A.x)).Append(',').Append(Num(w.A.y)).Append(',')
                    .Append(Num(w.B.x)).Append(',').Append(Num(w.B.y)).Append(",\"")
                    .Append(Escape(w.Prefab)).Append("\"]");
            }
            return text.Append(']').ToString();
        }

        /** Der juengste Fingerabdruck dieses Umrisses aus dem Bauprotokoll; `null` ohne. */
        private static List<GebauterWeg> GebauteWegeAusProtokoll(float2[] umriss)
        {
            try
            {
                var pfad = JournalPath();
                if (!File.Exists(pfad) || umriss == null || umriss.Length < 3) return null;
                var kennung = "\"kennung\":\"" + LotId(umriss) + "\"";
                // Die JUENGSTE Zeile dieses Umrisses zaehlt - hat sie keinen
                // Fingerabdruck, gibt es keinen. Ein aelterer Bau desselben
                // Umrisses wuerde sonst als "veraendert" gemeldet.
                string treffer = null;
                foreach (var zeile in File.ReadLines(pfad))
                    if (zeile.Contains(kennung)) treffer = zeile;
                if (treffer == null || !treffer.Contains("\"wege\":")) return null;
                var json = Newtonsoft.Json.Linq.JObject.Parse(treffer);
                var wege = new List<GebauterWeg>();
                foreach (var w in json["wege"] ?? new Newtonsoft.Json.Linq.JArray())
                    wege.Add(new GebauterWeg(
                        new float2((float)w[0], (float)w[1]),
                        new float2((float)w[2], (float)w[3]),
                        (string)w[4]));
                return wege;
            }
            catch (Exception fehler)
            {
                Mod.log.Warn("PLT-Wegvergleich: Bauprotokoll nicht lesbar: " + fehler.Message);
                return null;
            }
        }

        /**
         * Fuer die Automatik der Wegpruefung: wurden die Wege seit dem Bau
         * veraendert? Ohne Fingerabdruck (gebaut vor ihm) heisst das nein -
         * dann stammen die Wege aus unserem Bau.
         */
        internal bool WegeSeitBauVeraendert(Entity lot)
        {
            if (lot == Entity.Null || !EntityManager.Exists(lot)
                || !EntityManager.HasComponent<ParkingLotCarrierReference>(lot)) return false;
            if (!ParkingLotBaukontextLeser.TryRead(EntityManager, lot, out var kontext, out _, melden: false)) return false;
            var gebaut = GebauteWegeAusProtokoll(kontext.Punkte.Select(p => p.xz).ToArray());
            if (gebaut == null) return false;
            var offen = WegeDesTraegers(EntityManager.GetComponentData<ParkingLotCarrierReference>(lot).Carrier);
            if (offen.Count != gebaut.Count) return true;
            foreach (var g in gebaut)
            {
                var i = offen.FindIndex(j => j.Gleich(g));
                if (i < 0) return true;
                offen.RemoveAt(i);
            }
            return false;
        }

        /** Vergleicht den Fingerabdruck mit dem jetzigen Stand und schreibt den Klartext. */
        private DebugWegvergleich VergleicheWege(Entity lot, float2[] umriss)
        {
            _wegvergleichText = null;
            if (lot == Entity.Null || !EntityManager.Exists(lot)
                || !EntityManager.HasComponent<ParkingLotCarrierReference>(lot)) return null;
            var jetzt = WegeDesTraegers(
                EntityManager.GetComponentData<ParkingLotCarrierReference>(lot).Carrier);
            var gebaut = GebauteWegeAusProtokoll(umriss);

            var einzeln = new List<string>();
            var ausserhalb = 0;
            /*
             * NICHT NUR DIE KNOTEN - AUCH DER WEG DAZWISCHEN.
             *
             * Issue #10 hatte alle Knoten im Umriss, und trotzdem liefen
             * Randstrassenwege quer ueber den Innenhof einer U-Form: der Ring
             * war im schmalen Arm zu einem Punkt zusammengeklappt, und von
             * dort spannten gerade Kanten zum anderen Arm. Geprueft wird
             * deshalb an Anfang, Ende und drei Stellen dazwischen; eine Kante
             * zaehlt einmal.
             */
            foreach (var w in jetzt)
            {
                var groesster = 0f;
                foreach (var t in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
                    groesster = math.max(groesster, AbstandAusserhalb(math.lerp(w.A, w.B, t), umriss));
                if (groesster <= WegAusserhalbGrenze) continue;
                ausserhalb++;
                if (einzeln.Count < WegHoechstzahlEinzelfaelle)
                    einzeln.Add($"Weg bis {groesster.ToString("F1", CultureInfo.InvariantCulture)} m ausserhalb des Umrisses: {w.Text()}");
            }

            var ergebnis = new DebugWegvergleich { Jetzt = jetzt.Count, WegeAusserhalb = ausserhalb };
            var text = new StringBuilder();
            text.AppendLine("PATHS OF THIS LOT SINCE IT WAS BUILT");
            if (gebaut == null)
            {
                ergebnis.Stand = "kein Fingerabdruck";
                ergebnis.Note = "Gebaut vor dem Fingerabdruck im Bauprotokoll (oder auf einem anderen Rechner); "
                    + "nur die Lage zum Umriss ist pruefbar.";
                text.AppendLine($"  {jetzt.Count} path pieces now. Built with an older version, so there is no record "
                    + "of how they were built - only the outline check below is possible.");
            }
            else
            {
                var offenJetzt = new List<GebauterWeg>(jetzt);
                var unveraendert = 0;
                var weg = new List<GebauterWeg>();
                foreach (var g in gebaut)
                {
                    var i = offenJetzt.FindIndex(j => j.Gleich(g));
                    if (i >= 0) { offenJetzt.RemoveAt(i); unveraendert++; }
                    else weg.Add(g);
                }
                ergebnis.Gebaut = gebaut.Count;
                ergebnis.Unveraendert = unveraendert;
                ergebnis.VerschobenOderEntfernt = weg.Count;
                ergebnis.Neu = offenJetzt.Count;
                ergebnis.Stand = weg.Count == 0 && offenJetzt.Count == 0 ? "unveraendert" : "veraendert";
                foreach (var g in weg.Take(WegHoechstzahlEinzelfaelle / 2))
                    einzeln.Add("gebaut, jetzt nicht mehr da: " + g.Text());
                foreach (var n in offenJetzt.Take(WegHoechstzahlEinzelfaelle / 2))
                    einzeln.Add("jetzt da, so nicht gebaut: " + n.Text());
                text.AppendLine($"  built {gebaut.Count}, now {jetzt.Count}: {unveraendert} unchanged, "
                    + $"{weg.Count} moved or removed, {offenJetzt.Count} new or moved.");
                text.AppendLine(ergebnis.Stand == "unveraendert"
                    ? "  => The paths are exactly as the mod built them."
                    : "  => The paths were changed after building (e.g. Move It, another mod, or a road built across).");
            }
            text.AppendLine(ausserhalb == 0
                ? $"  No path runs more than {WegAusserhalbGrenze:F0} m outside the outline."
                : $"  {ausserhalb} path piece(s) run more than {WegAusserhalbGrenze:F0} m outside the outline - "
                    + "the mod never plans paths there (moved afterwards, or a planning error).");
            text.AppendLine();
            ergebnis.Einzelfaelle = einzeln.ToArray();
            _wegvergleichText = text.ToString();
            Mod.log.Info("PLT-Wegvergleich Lot " + lot.Index + ": " + ergebnis.Stand + ", gebaut "
                + ergebnis.Gebaut + ", jetzt " + ergebnis.Jetzt + ", unveraendert " + ergebnis.Unveraendert
                + ", verschoben/entfernt " + ergebnis.VerschobenOderEntfernt + ", neu " + ergebnis.Neu
                + ", Wege ausserhalb " + ausserhalb + ".");
            return ergebnis;
        }

        /** 0 innerhalb, sonst der Abstand zum Umriss. */
        private static float AbstandAusserhalb(float2 p, float2[] umriss)
        {
            if (umriss == null || umriss.Length < 3) return 0;
            var innen = false;
            var naechster = float.MaxValue;
            for (int i = 0, j = umriss.Length - 1; i < umriss.Length; j = i++)
            {
                var a = umriss[i];
                var b = umriss[j];
                if ((a.y > p.y) != (b.y > p.y)
                    && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                    innen = !innen;
                var ab = b - a;
                var t = math.clamp(math.dot(p - a, ab) / math.max(math.lengthsq(ab), 1e-9f), 0f, 1f);
                naechster = math.min(naechster, math.distance(p, a + ab * t));
            }
            return innen ? 0 : naechster;
        }
    }
}
