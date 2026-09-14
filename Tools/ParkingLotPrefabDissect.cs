using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using static ParkingLotTool.Tools.ParkingLotTexte;

namespace ParkingLotTool.Tools
{
    /**
     * DER PREFAB-SEZIERER.
     *
     * Auftrag des Nutzers am 2026-08-25: der erzeugte Parkplatz soll eine
     * ECHTE Parkanlage werden - CS2 soll ihn mit Kapazitaet und Auslastung
     * kennen und die Infoansicht "Parken" soll ihn zeigen. Keine Wirtschaft.
     *
     * Bevor irgendetwas gebaut wird, muss klar sein, WORAUS eine
     * Vanilla-Parkanlage ueberhaupt besteht. Genau das nimmt diese Hilfe
     * auseinander, und zwar im laufenden Spiel - nicht im Dekompilat.
     *
     * WARUM NICHT AUS DEM DEKOMPILAT. Das Dekompilat sagt, welche Komponenten
     * eine Prefab-Klasse ANMELDET. Es sagt nicht, was am Ende tatsaechlich am
     * Prefab und am gebauten Ding haengt - dazwischen liegen
     * `GetPrefabComponents`, `GetArchetypeComponents`, Vererbung ueber
     * mehrere Prefab-Klassen und alles, was andere Mods dazutun. Zweimal ist
     * an dieser Luecke schon ein Gebaeude-Anlauf gescheitert
     * (siehe Projektnotiz `streetblock-building-controller`).
     *
     * Der Abzug beantwortet drei Fragen:
     *   1. Welche Bauteile traegt ein Vanilla-Parkplatz-Prefab, mit Werten?
     *   2. Welche ECS-Komponenten hat seine Prefab-Entity, und welche hat
     *      eine GEBAUTE Parkanlage in der Welt?
     *   3. Was davon fehlt unserer eigenen Flaeche `PLT Parkplatzflaeche`?
     *
     * Frage 3 ist der eigentliche Punkt: die Differenzliste ist die
     * Arbeitsliste.
     */
    public sealed partial class ParkingLotToolSystem
    {
        private EntityQuery _parkanlagenPrefabQuery;
        private EntityQuery _parkanlagenWeltQuery;

        private void InitializePrefabDissect()
        {
            // Prefab-Entities: sie tragen PrefabData UND die Parkanlagendaten.
            _parkanlagenPrefabQuery = GetEntityQuery(
                ComponentType.ReadOnly<ParkingFacilityData>(),
                ComponentType.ReadOnly<PrefabData>());
            // Gebaute Parkanlagen in der Welt. Die Laufzeitkomponente liegt in
            // Game.Buildings, nicht in Game.Prefabs - das ist leicht zu
            // verwechseln und war beim Suchen der erste Irrweg.
            _parkanlagenWeltQuery = GetEntityQuery(
                ComponentType.ReadOnly<Game.Buildings.ParkingFacility>());
        }

        internal void SeziereParkanlagen()
        {
            try
            {
                var text = BaueSezierbericht();
                var folder = System.IO.Path.Combine(
                    UnityEngine.Application.persistentDataPath, "Logs");
                System.IO.Directory.CreateDirectory(folder);
                var path = System.IO.Path.Combine(folder,
                    "ParkingLotTool-prefabs-"
                    + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
                System.IO.File.WriteAllText(path, text, new UTF8Encoding(false));
                _uiSystem?.SetReportPath(path);
                Mod.log.Info("PLT-Prefababzug geschrieben: " + path);
                _debugTooltipSystem?.Show(T(
                    "Prefababzug geschrieben.", "Prefab dump written."));
            }
            catch (Exception ausnahme)
            {
                Mod.log.Warn("PLT: Prefababzug fehlgeschlagen: " + ausnahme);
                _debugTooltipSystem?.Show(T(
                    "Prefababzug fehlgeschlagen - siehe Log.",
                    "Prefab dump failed - see the log."));
            }
        }

        private string BaueSezierbericht()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Parking Lot Tool - Prefababzug Parkanlagen");
            sb.AppendLine("erstellt " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();

            using var prefabs = _parkanlagenPrefabQuery.ToEntityArray(Allocator.TempJob);
            sb.AppendLine("=== 1. UEBERSICHT ===");
            sb.AppendLine($"  Prefabs mit ParkingFacilityData: {prefabs.Length}");

            var namen = new List<(Entity Entity, string Name, string Typ)>();
            for (var i = 0; i < prefabs.Length; i++)
            {
                if (_prefabSystem == null
                    || !_prefabSystem.TryGetPrefab<PrefabBase>(prefabs[i], out var p)
                    || p == null) continue;
                namen.Add((prefabs[i], p.name, p.GetType().Name));
            }
            foreach (var gruppe in namen.GroupBy(n => n.Typ).OrderByDescending(g => g.Count()))
                sb.AppendLine($"    {gruppe.Count(),3}x {gruppe.Key}");
            sb.AppendLine();

            using var welt = _parkanlagenWeltQuery.ToEntityArray(Allocator.TempJob);
            sb.AppendLine($"  Gebaute Parkanlagen in der Welt: {welt.Length}");
            sb.AppendLine();

            /*
             * Zuerst die reinen Parkplaetze. "ParkingLot" im Namen trifft die
             * Vanilla-Reihe ParkingLot01..05; die sind das Vorbild, nicht ein
             * Krankenhaus mit angebautem Parkdeck.
             */
            var vorbilder = namen
                .OrderByDescending(n => n.Name.IndexOf("ParkingLot",
                    StringComparison.OrdinalIgnoreCase) >= 0)
                .ThenBy(n => n.Name, StringComparer.Ordinal)
                .ToList();

            sb.AppendLine($"=== 2. ZERLEGUNG ALLER {vorbilder.Count} VORBILDER ===");
            foreach (var vorbild in vorbilder)
            {
                ZerlegePrefab(sb, vorbild.Entity, vorbild.Name, vorbild.Typ);
                sb.AppendLine();
            }

            var eigenesVorab = FindeEigenesPrefab();
            /*
             * DIE EICHTABELLE FUER DIE WIRTSCHAFTSFORMEL.
             *
             * Der Nutzer will, dass Unterhalt, Personal und spaeter Baukosten
             * sich an der GROESSE richten statt feste Zahlen zu sein. Die
             * Faktoren muss niemand erfinden - CS2 hat sie, man muss sie nur
             * paarweise ablesen: Kapazitaet gegen Unterhalt.
             *
             * WARUM NICHT AUS DEN PREFABS: die Zahl der Aufkleber im Prefab
             * ist NICHT die Kapazitaet. Ein ParkingLotDecal04 traegt mehrere
             * Buchten, ein Decal01 eine. Rechnet man "Unterhalt je
             * Decal-Eintrag", streut das Ergebnis zwischen 51 und 9.333 - eine
             * Zahl, die nach Aussage aussieht und keine ist.
             *
             * Deshalb hier die ECHTE Kapazitaet aus dem gebauten Ding, ueber
             * denselben Weg, den auch das Auswahlfenster geht
             * (`VehicleUtils.GetParkingData`).
             */
            sb.AppendLine("=== 2b. EICHTABELLE: GROESSE GEGEN KOSTEN ===");
            sb.AppendLine("  Prefab                 Plaetze  belegt  Unterhalt  "
                + "Strom  Arb.   Laerm   je Platz");
            foreach (var entity in welt)
            {
                var name = PrefabnameVon(entity);
                var kapazitaet = 0;
                var geparkt = 0;
                var spuren = 0;
                var gebuehr = 0;
                try
                {
                    Game.Vehicles.VehicleUtils.GetParkingData(
                        this, entity, ref spuren, ref kapazitaet, ref geparkt,
                        ref gebuehr);
                }
                catch (Exception ausnahme)
                {
                    sb.AppendLine($"  {name,-22} nicht lesbar: {ausnahme.Message}");
                    continue;
                }

                // Die Kosten stehen am PREFAB, nicht am gebauten Ding.
                var unterhalt = 0;
                var strom = 0;
                var arbeit = 0;
                var laerm = 0;
                if (EntityManager.HasComponent<PrefabRef>(entity)
                    && _prefabSystem != null
                    && _prefabSystem.TryGetPrefab<PrefabBase>(
                        EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab,
                        out var prefab)
                    && prefab != null)
                {
                    foreach (var bauteil in prefab.components)
                    {
                        if (bauteil == null) continue;
                        foreach (var feld in Felder(bauteil.GetType()))
                        {
                            object wert;
                            try { wert = feld.GetValue(bauteil); } catch { continue; }
                            if (!(wert is int zahl)) continue;
                            if (feld.Name == "m_Upkeep") unterhalt = zahl;
                            else if (feld.Name == "m_ElectricityConsumption") strom = zahl;
                            else if (feld.Name == "m_Workplaces") arbeit = zahl;
                            else if (feld.Name == "m_NoisePollution") laerm = zahl;
                        }
                    }
                }

                var jePlatz = kapazitaet > 0
                    ? (unterhalt / (double)kapazitaet).ToString("F1")
                    : "-";
                sb.AppendLine($"  {name,-22} {kapazitaet,7} {geparkt,7} "
                    + $"{unterhalt,10} {strom,6} {arbeit,5} {laerm,7} {jePlatz,10}");
            }
            sb.AppendLine("  (Kapazitaet aus den ECHTEN Parkspuren, nicht aus "
                + "Prefab-Aufklebern.)");
            sb.AppendLine();

            sb.AppendLine("=== 3. UNSERE EIGENE FLAECHE ===");
            var eigenes = eigenesVorab;
            if (eigenes == Entity.Null)
                sb.AppendLine("  'PLT Parkplatzflaeche' nicht gefunden - erst einen "
                    + "Parkplatz bauen, damit das Prefab angemeldet ist.");
            else if (_prefabSystem.TryGetPrefab<PrefabBase>(eigenes, out var unser))
                ZerlegePrefab(sb, eigenes, unser.name, unser.GetType().Name);
            sb.AppendLine();

            var parkingLot01 = FindePrefabMitName("ParkingLot01");
            var parkingLot04 = FindePrefabMitName("ParkingLot04");
            var begleiterPrefab = FindePrefabMitName(
                ParkingLotBuildingEconomySystem.CompanionPrefabName);
            sb.AppendLine("=== 3b. PREFAB DES WIRTSCHAFTS-BEGLEITERS ===");
            SchreibeBegleiterPrefab(sb, begleiterPrefab);
            sb.AppendLine();

            /*
             * ALLE, NICHT EINE.
             *
             * Zwei Abzuege hintereinander waren hier unbrauchbar, und beide
             * Male lag es an einer Deckelung von mir: am 14:24 stand hier
             * unser EIGENER Parkplatz (er war seit dem Umbau schlicht der
             * erste in der Liste), am 14:43 nur der grosse Parkplatz - das
             * Parkhaus, das der Nutzer eigens dafuer gebaut hatte, fiel unter
             * den Tisch. Seine Antwort darauf, zu Recht: *"Wie gesagt, ich
             * habe das Parkhaus gebaut. Wenn du das nicht ausliest, kann ich
             * nicht dafuer."*
             *
             * Deshalb: jede gebaute Parkanlage kommt in den Abzug, unsere
             * eigene ausgenommen (die steht in 4c). Wird es zu viel, ist das
             * ein Luxusproblem gegenueber einem Abzug, der die Haelfte
             * verschweigt.
             */
            var fremdeListe = new List<Entity>();
            for (var i = 0; i < welt.Length; i++)
            {
                if (!EntityManager.HasComponent<PrefabRef>(welt[i])) continue;
                if (EntityManager.GetComponentData<PrefabRef>(welt[i]).m_Prefab
                    == eigenesVorab) continue;
                fremdeListe.Add(welt[i]);
            }

            sb.AppendLine("=== 4. ALLE GEBAUTEN VANILLA-PARKANLAGEN IN DER WELT ===");
            sb.AppendLine($"  {fremdeListe.Count} Stueck (ohne unsere eigenen).");
            if (fremdeListe.Count == 0)
                sb.AppendLine("  Keine im Spielstand. Setz einen Vanilla-Parkplatz "
                    + "und druecke noch einmal.");
            foreach (var fremde in fremdeListe)
            {
                sb.AppendLine();
                sb.AppendLine($"  --- {PrefabnameVon(fremde)}, Entity {fremde.Index} ---");
                SchreibeArchetyp(sb, fremde, "    ");
                SchreibeParkanlagendaten(sb, fremde, "    ");
                SchreibePuffer(sb, fremde, "    ");
            }
            sb.AppendLine();

            /*
             * DIE LUECKE VOM ERSTEN ABZUG.
             *
             * Abschnitt 2 und 3 zeigen die Komponenten der PREFAB-Entity. Was
             * eine gebaute Flaeche traegt, steht dort NICHT - das entscheidet
             * `AreaData.m_Archetype`, den `AreaPrefab.LateInitialize` einmal
             * anlegt. Genau dieser Archetyp ist aber die Frage: bekommt unsere
             * Flaeche `ParkingFacility` und `CarParkingFacility` oder nicht?
             *
             * Im Abzug vom 2026-08-25 14:16 fehlte er, und die Differenzliste
             * war dadurch irrefuehrend: sie zaehlte lauter Gebaeudekram auf
             * (WorkplaceData, PollutionData, ServiceUpkeepData), den wir gar
             * nicht wollen.
             */
            sb.AppendLine("=== 4b. DER ARCHETYP UNSERER FLAECHE ===");
            SchreibeAreaArchetyp(sb, eigenes, "  ");
            sb.AppendLine();

            sb.AppendLine("=== 4c. UNSER GEBAUTER PARKPLATZ IN DER WELT ===");
            var unsereFlaeche = FindeGebauteEigeneFlaeche(eigenes);
            if (unsereFlaeche == Entity.Null)
                sb.AppendLine("  Keiner im Spielstand. Bau einen PLT-Parkplatz "
                    + "und druecke noch einmal.");
            else
            {
                sb.AppendLine($"  Besitzer-Entity {unsereFlaeche.Index}:");
                SchreibeArchetyp(sb, unsereFlaeche, "    ");
                SchreibeParkanlagendaten(sb, unsereFlaeche, "    ");
                SchreibePuffer(sb, unsereFlaeche, "    ");
                SchreibeBuildingSicherheit(sb, unsereFlaeche, "    ");
                var traeger = EntityManager.HasComponent<
                        ParkingLotCarrierReference>(unsereFlaeche)
                    ? EntityManager.GetComponentData<ParkingLotCarrierReference>(
                        unsereFlaeche).Carrier
                    : Entity.Null;
                sb.AppendLine();
                sb.AppendLine(traeger == Entity.Null
                    ? "  Technischer Traeger: kein Verweis."
                    : $"  Technischer Traeger, Entity {traeger.Index}:");
                if (traeger != Entity.Null)
                {
                    SchreibeArchetyp(sb, traeger, "    ");
                    SchreibePuffer(sb, traeger, "    ");
                }
            }
            sb.AppendLine();

            sb.AppendLine("=== 4d. GEBAUTER WIRTSCHAFTS-BEGLEITER ===");
            var begleiter = FindeBegleiter(unsereFlaeche);
            SchreibeBegleiterInstanz(sb, begleiter);
            sb.AppendLine();

            sb.AppendLine("=== 5. BEGLEITER-DIFFERENZLISTE ===");
            SchreibeBegleiterDifferenzen(sb, parkingLot01, parkingLot04,
                begleiterPrefab, FindeGebauteParkanlage("ParkingLot01", welt),
                FindeGebauteParkanlage("ParkingLot04", welt), begleiter);
            sb.AppendLine();

            sb.AppendLine("=== 5c. WIRKT DIE PARKGEBUEHR AUF DIE WEGEWAHL? ===");
            SchreibeParkkosten(sb);
            sb.AppendLine();

            sb.AppendLine("=== 5b. ALTE FLAECHEN-DIFFERENZLISTE ===");
            SchreibeDifferenz(sb, vorbilder.FirstOrDefault().Entity, eigenes,
                welt.Length > 0 ? welt[0] : Entity.Null);
            return sb.ToString();
        }


        /**
         * DIE ZAHL, DIE ENTSCHEIDET, OB GEBUEHR UND KOMFORT UEBERHAUPT LENKEN.
         *
         * `PathUtils` (1232-1233) rechnet beides MULTIPLIKATIV in die Kosten
         * einer Parkbucht:
         *
         *     m_ParkingCost.m_Value.z *= parkingLane.m_ParkingFee;
         *     m_ParkingCost.m_Value.w *= (65535 - m_ComfortFactor) * ...;
         *
         * Steht z oder w im Prefab auf NULL, ist das Ergebnis null - dann
         * zahlen die Leute zwar, meiden den teuren Parkplatz aber nicht, und
         * der Komfort waere ebenso wirkungslos. Die Klassenvorbelegung in
         * `Game.Prefabs/CarPathfind.cs:22` lautet (10, 0, 0, 0); ob das
         * ausgelieferte Asset das ueberschreibt, sagt nur das laufende Spiel.
         *
         * Deshalb hier ausgelesen statt geraten.
         */
        private void SchreibeParkkosten(System.Text.StringBuilder sb)
        {
            var abfrage = GetEntityQuery(
                ComponentType.ReadOnly<Game.Prefabs.PathfindCarData>(),
                ComponentType.ReadOnly<PrefabData>());
            using var prefabs = abfrage.ToEntityArray(Allocator.Temp);
            if (prefabs.Length == 0)
            {
                sb.AppendLine("  Kein Prefab mit PathfindCarData gefunden.");
                return;
            }
            sb.AppendLine("  Lesehilfe: z ist der Faktor fuer die GEBUEHR, "
                + "w der fuer den KOMFORT.");
            sb.AppendLine("  Steht dort 0, hat die jeweilige Zahl keine "
                + "Lenkungswirkung - egal was wir setzen.");
            for (var i = 0; i < prefabs.Length; i++)
            {
                var name = _prefabSystem.TryGetPrefab<PrefabBase>(
                    prefabs[i], out var prefab) && prefab != null
                    ? prefab.name : "(ohne Namen)";
                var daten = EntityManager
                    .GetComponentData<Game.Prefabs.PathfindCarData>(prefabs[i]);
                var k = daten.m_ParkingCost.m_Value;
                sb.AppendLine($"    {name}: Parkkosten "
                    + $"x {k.x:F3}  y {k.y:F3}  z {k.z:F3}  w {k.w:F3}");
            }
        }

        /**
         * Der Archetyp, den eine gebaute Flaeche dieses Prefabs bekommt.
         *
         * Er steht in `AreaData.m_Archetype` und wird von
         * `AreaPrefab.LateInitialize` aus den Bauteilen zusammengesetzt -
         * nicht zu verwechseln mit den Komponenten der Prefab-Entity selbst.
         */
        private void SchreibeAreaArchetyp(StringBuilder sb, Entity prefab,
            string einzug)
        {
            if (prefab == Entity.Null
                || !EntityManager.HasComponent<AreaData>(prefab))
            {
                sb.AppendLine(einzug + "Kein AreaData - Prefab fehlt oder ist "
                    + "kein Areal.");
                return;
            }
            var daten = EntityManager.GetComponentData<AreaData>(prefab);
            var typen = daten.m_Archetype.GetComponentTypes(Allocator.Temp);
            var namen = new List<string>(typen.Length);
            for (var i = 0; i < typen.Length; i++)
                namen.Add(typen[i].GetManagedType()?.Name ?? typen[i].ToString());
            typen.Dispose();
            namen.Sort(StringComparer.Ordinal);
            sb.AppendLine($"{einzug}AreaData.m_Archetype ({namen.Count}):");
            sb.AppendLine(einzug + "  " + string.Join(", ", namen));
            var hatAnlage = namen.Contains("ParkingFacility");
            var hatAuto = namen.Contains("CarParkingFacility");
            sb.AppendLine($"{einzug}-> ParkingFacility: "
                + (hatAnlage ? "JA" : "NEIN")
                + " | CarParkingFacility: " + (hatAuto ? "JA" : "NEIN"));
            if (!hatAnlage)
                sb.AppendLine(einzug + "   Ohne diese zwei wird die Flaeche von "
                    + "ParkingFacilityAISystem nie angefasst.");
        }

        /** Eine gebaute Flaeche, die auf unser eigenes Prefab zeigt. */
        private Entity FindeGebauteEigeneFlaeche(Entity prefab)
        {
            if (prefab == Entity.Null) return Entity.Null;
            var query = GetEntityQuery(
                ComponentType.ReadOnly<Game.Areas.Area>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Game.Tools.Temp>(),
                ComponentType.Exclude<Game.Common.Deleted>());
            using var alle = query.ToEntityArray(Allocator.TempJob);
            for (var i = 0; i < alle.Length; i++)
                if (EntityManager.GetComponentData<PrefabRef>(alle[i]).m_Prefab
                    == prefab)
                    return alle[i];
            return Entity.Null;
        }

        private Entity FindeEigenesPrefab()
        {
            using var alle = _areaPrefabQuery.ToEntityArray(Allocator.TempJob);
            for (var i = 0; i < alle.Length; i++)
                if (_prefabSystem != null
                    && _prefabSystem.TryGetPrefab<PrefabBase>(alle[i], out var p)
                    && p != null
                    && string.Equals(p.name, "PLT Parkplatzflaeche",
                        StringComparison.Ordinal))
                    return alle[i];
            return Entity.Null;
        }

        /**
         * Ein Prefab von beiden Seiten: die BAUTEILE, die der Autor
         * zusammengesteckt hat, und der ARCHETYP, der daraus geworden ist.
         * Beides zusammen, weil die eine Seite die andere nicht verraet.
         */
        private void ZerlegePrefab(StringBuilder sb, Entity entity, string name, string typ)
        {
            sb.AppendLine($"  --- {name}  ({typ}) ---");
            if (!_prefabSystem.TryGetPrefab<PrefabBase>(entity, out var prefab))
            {
                sb.AppendLine("      nicht aufloesbar.");
                return;
            }

            sb.AppendLine($"      Bauteile ({prefab.components.Count}):");
            foreach (var bauteil in prefab.components)
            {
                if (bauteil == null) continue;
                SchreibeBauteil(sb, bauteil, "        ");
            }
            SchreibeArchetyp(sb, entity, "      ");
            SchreibeParkanlagendaten(sb, entity, "      ");
        }

        /**
         * EIN BAUTEIL, VOLLSTAENDIG - auch seine Listen.
         *
         * Die erste Fassung schrieb ein Feldarray als "[14]" hin. Genau das
         * hat der Nutzer am 2026-08-25 angestrichen: *"Nicht nachbauen,
         * sondern genau herausfinden, aus was allem ein Prefab besteht - und
         * nicht schaetzen und die Haelfte vergessen."* Bei `ParkingLot01`
         * standen damit 14 Unterobjekte, 6 Unternetze, 3 Unterareale und 11
         * Unterspuren als vier nackte Zahlen im Abzug. Das ist genau die
         * Haelfte, auf die es ankommt.
         *
         * Jetzt wird jede Liste aufgeklappt und jedes Element mit seinen
         * eigenen Feldern geschrieben. Prefab-Verweise erscheinen als Name,
         * weil die Frage immer lautet: WELCHES Ding haengt hier?
         *
         * Eine Ebene tief, nicht beliebig: ein Unterobjekt verweist auf ein
         * Prefab, das selbst wieder Unterobjekte hat. Das waere ein Baum ohne
         * Boden. Die zweite Ebene bekommt man, indem man das genannte Prefab
         * beim naechsten Lauf selbst seziert.
         */
        private static void SchreibeBauteil(StringBuilder sb, ComponentBase bauteil,
            string einzug)
        {
            var felder = Felder(bauteil.GetType());
            var einzeilig = new List<string>();
            var listen = new List<(string Name, Array Werte)>();

            foreach (var feld in felder)
            {
                object wert;
                try { wert = feld.GetValue(bauteil); }
                catch { continue; }
                if (wert is Array liste && liste.Length > 0
                    && !(wert is byte[]) && !(wert is char[]))
                {
                    listen.Add((feld.Name, liste));
                    continue;
                }
                einzeilig.Add(feld.Name + "=" + Kurz(wert));
            }

            sb.AppendLine(einzug + bauteil.GetType().Name
                + (einzeilig.Count == 0 ? string.Empty
                    : "  { " + string.Join(", ", einzeilig) + " }"));

            foreach (var liste in listen)
            {
                sb.AppendLine($"{einzug}  {liste.Name} ({liste.Werte.Length}):");
                for (var i = 0; i < liste.Werte.Length; i++)
                    sb.AppendLine($"{einzug}    [{i}] "
                        + Element(liste.Werte.GetValue(i)));
            }
        }

        private static List<FieldInfo> Felder(Type typ)
            => typ.GetFields(BindingFlags.Public | BindingFlags.Instance)
                  .Where(f => !f.IsStatic).ToList();

        /** Ein Listenelement mit allen eigenen Feldern, in einer Zeile. */
        private static string Element(object wert)
        {
            if (wert == null) return "null";
            if (wert is PrefabBase p) return "-> " + p.name;
            var typ = wert.GetType();
            if (typ.IsPrimitive || wert is string || typ.IsEnum)
                return Kurz(wert);
            var teile = new List<string>();
            foreach (var feld in Felder(typ))
            {
                object inhalt;
                try { inhalt = feld.GetValue(wert); }
                catch { continue; }
                teile.Add(feld.Name + "=" + Kurz(inhalt));
            }
            return teile.Count == 0 ? typ.Name
                : typ.Name + " { " + string.Join(", ", teile) + " }";
        }

        private static string Kurz(object wert)
        {
            if (wert == null) return "null";
            if (wert is PrefabBase p) return "-> " + p.name;
            if (wert is Array a) return "[" + a.Length + "]";
            var text = wert.ToString();
            return text.Length > 48 ? text.Substring(0, 45) + "..." : text;
        }

        /**
         * DIE PUFFER EINER GEBAUTEN ENTITY - und was NICHT gelesen wurde.
         *
         * Der zweite Teil ist der wichtigere. Ein Abzug, der nur zeigt, was
         * er lesen konnte, sieht vollstaendig aus und ist es nicht. Deshalb
         * werden am Ende ausdruecklich die Puffertypen genannt, die dieses
         * Werkzeug NICHT aufloest - dann ist die Luecke sichtbar statt
         * unsichtbar.
         */
        private void SchreibePuffer(StringBuilder sb, Entity entity, string einzug)
        {
            if (!EntityManager.Exists(entity)) return;
            var gelesen = new HashSet<string>(StringComparer.Ordinal);

            SchreibeKindpuffer<Game.Objects.SubObject>(sb, entity, einzug,
                "SubObject", k => k.m_SubObject, gelesen);
            SchreibeKindpuffer<Game.Net.SubNet>(sb, entity, einzug,
                "SubNet", k => k.m_SubNet, gelesen);
            SchreibeKindpuffer<Game.Areas.SubArea>(sb, entity, einzug,
                "SubArea", k => k.m_Area, gelesen);
            SchreibeKindpuffer<Game.Net.SubLane>(sb, entity, einzug,
                "SubLane", k => k.m_SubLane, gelesen);

            var typen = EntityManager.GetChunk(entity).Archetype
                .GetComponentTypes(Allocator.Temp);
            var offen = new List<string>();
            for (var i = 0; i < typen.Length; i++)
            {
                if (!typen[i].IsBuffer) continue;
                var name = typen[i].GetManagedType()?.Name ?? typen[i].ToString();
                if (!gelesen.Contains(name)) offen.Add(name);
            }
            typen.Dispose();
            offen.Sort(StringComparer.Ordinal);
            sb.AppendLine($"{einzug}NICHT aufgeloeste Puffer ({offen.Count}): "
                + (offen.Count == 0 ? "-" : string.Join(", ", offen)));
        }

        private void SchreibeKindpuffer<T>(StringBuilder sb, Entity entity,
            string einzug, string name, Func<T, Entity> kind,
            HashSet<string> gelesen) where T : unmanaged, IBufferElementData
        {
            gelesen.Add(typeof(T).Name);
            if (!EntityManager.HasBuffer<T>(entity))
            {
                sb.AppendLine($"{einzug}{name}: kein Puffer");
                return;
            }
            var puffer = EntityManager.GetBuffer<T>(entity, true);
            sb.AppendLine($"{einzug}{name} ({puffer.Length}):");

            // Nach Prefabnamen zusammenfassen - 144 Aufkleber einzeln zu
            // schreiben hilft niemandem, ihre Verteilung schon.
            var zaehler = new Dictionary<string, int>(StringComparer.Ordinal);
            var beispiel = new Dictionary<string, Entity>(StringComparer.Ordinal);
            for (var i = 0; i < puffer.Length; i++)
            {
                var e = kind(puffer[i]);
                var schluessel = PrefabnameVon(e);
                zaehler.TryGetValue(schluessel, out var n);
                zaehler[schluessel] = n + 1;
                if (!beispiel.ContainsKey(schluessel)) beispiel[schluessel] = e;
            }
            foreach (var eintrag in zaehler.OrderByDescending(x => x.Value))
            {
                sb.AppendLine($"{einzug}  {eintrag.Value,4}x {eintrag.Key}");
                SchreibeArchetyp(sb, beispiel[eintrag.Key], einzug + "      ");
            }
        }

        private string PrefabnameVon(Entity entity)
        {
            if (!EntityManager.Exists(entity)) return "(weg)";
            if (!EntityManager.HasComponent<PrefabRef>(entity)) return "(ohne PrefabRef)";
            var referenz = EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
            return _prefabSystem != null
                   && _prefabSystem.TryGetPrefab<PrefabBase>(referenz, out var p)
                   && p != null
                ? p.name
                : "(unbekannt)";
        }

        private void SchreibeArchetyp(StringBuilder sb, Entity entity, string einzug)
        {
            if (!EntityManager.Exists(entity))
            {
                sb.AppendLine(einzug + "Archetyp: Entity existiert nicht.");
                return;
            }
            var typen = EntityManager.GetChunk(entity).Archetype.GetComponentTypes(
                Allocator.Temp);
            var namen = new List<string>(typen.Length);
            for (var i = 0; i < typen.Length; i++)
                namen.Add(typen[i].GetManagedType()?.Name ?? typen[i].ToString());
            typen.Dispose();
            namen.Sort(StringComparer.Ordinal);
            sb.AppendLine($"{einzug}ECS-Komponenten ({namen.Count}):");
            sb.AppendLine(einzug + "  " + string.Join(", ", namen));
        }

        private void SchreibeParkanlagendaten(StringBuilder sb, Entity entity, string einzug)
        {
            if (EntityManager.HasComponent<ParkingFacilityData>(entity))
            {
                var daten = EntityManager.GetComponentData<ParkingFacilityData>(entity);
                sb.AppendLine($"{einzug}ParkingFacilityData: "
                    + $"ComfortFactor={daten.m_ComfortFactor:R}, "
                    + $"GarageMarkerCapacity={daten.m_GarageMarkerCapacity}, "
                    + $"RoadTypes={daten.m_RoadTypes}");
            }
            if (EntityManager.HasComponent<Game.Buildings.ParkingFacility>(entity))
            {
                var lauf = EntityManager.GetComponentData<
                    Game.Buildings.ParkingFacility>(entity);
                sb.AppendLine($"{einzug}Buildings.ParkingFacility (Laufzeit): {lauf}");
            }
        }

        /** Die vier Werte, von denen der Building-Anlauf nicht raten darf. */
        private void SchreibeBuildingSicherheit(StringBuilder sb, Entity entity,
            string einzug)
        {
            var subObjects = EntityManager.HasBuffer<Game.Objects.SubObject>(entity)
                ? EntityManager.GetBuffer<Game.Objects.SubObject>(entity, true).Length
                : -1;
            var subAreas = EntityManager.HasBuffer<Game.Areas.SubArea>(entity)
                ? EntityManager.GetBuffer<Game.Areas.SubArea>(entity, true).Length
                : -1;
            sb.AppendLine(einzug + "Building-Sicherheit: "
                + "Building=" + EntityManager.HasComponent<
                    Game.Buildings.Building>(entity)
                + ", Transform=" + EntityManager.HasComponent<
                    Game.Objects.Transform>(entity)
                + ", Owner=" + EntityManager.HasComponent<Game.Common.Owner>(entity)
                + ", SubObject=" + subObjects + ", SubArea=" + subAreas);
        }

        /**
         * DIE ARBEITSLISTE. Was traegt ein Vorbild, das wir nicht tragen?
         *
         * Getrennt nach Prefab-Seite und Weltseite, weil beides eigene
         * Konsequenzen hat: an der Prefab-Seite entscheidet sich, was CS2
         * beim Anmelden erzeugt, an der Weltseite, was die Simulation sieht.
         */
        private void SchreibeDifferenz(StringBuilder sb, Entity vorbild,
            Entity eigenes, Entity gebaut)
        {
            if (vorbild == Entity.Null || eigenes == Entity.Null)
            {
                sb.AppendLine("  Vergleich nicht moeglich - Vorbild oder eigenes "
                    + "Prefab fehlt.");
                return;
            }
            var beim = Komponenten(vorbild);
            var uns = Komponenten(eigenes);
            var fehlt = beim.Except(uns).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var extra = uns.Except(beim).OrderBy(x => x, StringComparer.Ordinal).ToList();

            sb.AppendLine("  Am Vorbild-PREFAB, aber nicht an unserem "
                + $"({fehlt.Count}):");
            sb.AppendLine("    " + (fehlt.Count == 0 ? "-" : string.Join(", ", fehlt)));
            sb.AppendLine($"  Nur an unserem ({extra.Count}):");
            sb.AppendLine("    " + (extra.Count == 0 ? "-" : string.Join(", ", extra)));

            if (gebaut == Entity.Null) return;
            var inWelt = Komponenten(gebaut);
            sb.AppendLine($"  Eine GEBAUTE Parkanlage traegt zusaetzlich "
                + "(gegenueber ihrem eigenen Prefab):");
            sb.AppendLine("    " + string.Join(", ",
                inWelt.Except(beim).OrderBy(x => x, StringComparer.Ordinal)));
        }

        private HashSet<string> Komponenten(Entity entity)
        {
            var menge = new HashSet<string>(StringComparer.Ordinal);
            if (!EntityManager.Exists(entity)) return menge;
            var typen = EntityManager.GetChunk(entity).Archetype.GetComponentTypes(
                Allocator.Temp);
            for (var i = 0; i < typen.Length; i++)
                menge.Add(typen[i].GetManagedType()?.Name ?? typen[i].ToString());
            typen.Dispose();
            return menge;
        }
    }
}
