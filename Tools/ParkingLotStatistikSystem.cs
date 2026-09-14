using System;
using System.Collections.Generic;
using Game;
using Game.Citizens;
using Game.Common;
using Game.Tools;
using Unity.Entities;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /**
     * Tastet die eigenen Parkplaetze regelmaessig ab und fuehrt Strichliste.
     *
     * WARUM UEBERHAUPT ABTASTEN. CS2 haelt keine Historie je Parkplatz. Ein
     * einzelner Blick beantwortet „wer kommt hier her" nicht: um drei Uhr
     * nachts stehen fuenf Autos da, und die haetten dann die Mehrheit. Erst
     * viele Blicke ergeben eine Aussage.
     *
     * WAS DAS KOSTET. Eine Probe geht einmal ueber alle Parkspuren der Stadt,
     * um die eigenen herauszufiltern; danach nur noch ueber die eigenen. Das
     * ist derselbe Durchlauf, den `ParkingLotComfortSystem` fuer seinen
     * Bericht ohnehin faehrt, und er laeuft hier nur alle
     * `ProbeAlleFrames` Bilder.
     */
    public sealed partial class ParkingLotStatistikSystem : GameSystemBase
    {
        /** Rund neun Sekunden bei sechzig Bildern - haeufig genug fuer ein
         *  Tagesprofil, selten genug, um im Bildratenprofil nicht aufzufallen. */
        internal const int ProbeAlleFrames = 512;

        /** So viele Zielgebaeude behaelt ein Parkplatz in der Rangliste. */
        internal const int ZieleHoechstens = 16;

        /** Tage im Ring. Sieben plus einer, der gerade gefuellt wird. */
        internal const int TagringLaenge = 8;

        internal const int StundenImTag = 24;

        /** Unter so vielen Proben sagt eine Karte noch gar nichts. */
        internal const int ProbenFuerAussage = 5;

        private ParkingLotComfortSystem _comfort;
        private Game.Simulation.SimulationSystem _simulation;
        private Game.Simulation.TimeSystem _zeit;
        private EntityQuery _lotQuery;

        private readonly List<Entity> _spuren = new List<Entity>();
        private readonly List<Entity> _spurLots = new List<Entity>();
        private readonly Dictionary<Entity, Lotlauf> _laeufe =
            new Dictionary<Entity, Lotlauf>();
        private readonly List<Entity> _abgeraeumt = new List<Entity>();

        private int _frames;
        private bool _zeitfehlerGemeldet;

        /**
         * Ist das hier die allererste Probe, seit der Spielstand offen ist?
         *
         * Daran haengt das Baudatum. Bei der ersten Probe steht jeder
         * Parkplatz schon da - wie lange, weiss niemand. Taucht dagegen
         * SPAETER einer auf, der vorher nicht dabei war, ist er wirklich
         * gerade gebaut worden, und der Frame stimmt.
         */
        private bool _ersteProbeDerSitzung = true;

        /**
         * Was zwischen zwei Proben im Kopf bleibt.
         *
         * Steht NICHT im Spielstand: welche Autos gerade da stehen, ist im
         * naechsten Moment ohnehin anders. Gebraucht wird es nur, um
         * Neuankuenfte von Dauerparkern zu unterscheiden und um beim
         * Wegfahren die Standzeit auszurechnen.
         */
        private sealed class Lotlauf
        {
            internal HashSet<Entity> Stehende = new HashSet<Entity>();
            internal Dictionary<Entity, uint> Ankunft =
                new Dictionary<Entity, uint>();

            /**
             * Die erste Probe nach dem Laden zaehlt KEINE Ankuenfte.
             *
             * Sonst gaelten nach jedem Ladevorgang alle zweihundert
             * abgestellten Autos schlagartig als frisch angekommen, und der
             * Tageswert schoesse ins Nichts.
             */
            internal bool Geimpft;
        }

        /** Ein Messwert im Entstehen, gesammelt ueber alle Spuren eines Lots. */
        private sealed class Probe
        {
            internal int Belegt;
            internal int Personen;
            internal int Einkaufen;
            internal int Arbeiten;
            internal int NachHause;
            internal int Freizeit;
            internal int Besichtigen;
            internal int Sonstige;
            internal int Touristen;
            internal int Kind;
            internal int Jugend;
            internal int Erwachsen;
            internal int Senior;
            internal int Hochschule;
            internal int ZufriedenSumme;
            internal int ZufriedenAnzahl;
            internal int WegSummeDezimeter;
            internal int WegAnzahl;
            internal int Steckengeblieben;
            internal readonly Dictionary<Entity, int> Ziele =
                new Dictionary<Entity, int>();
            internal readonly HashSet<Entity> Stehende = new HashSet<Entity>();
        }

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _comfort = World.GetOrCreateSystemManaged<ParkingLotComfortSystem>();
            _simulation =
                World.GetOrCreateSystemManaged<Game.Simulation.SimulationSystem>();
            _zeit = World.GetOrCreateSystemManaged<Game.Simulation.TimeSystem>();

            _lotQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<ParkingLotEconomyData>(),
                    ComponentType.ReadOnly<ParkingLotCarrierReference>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });
        }

        /**
         * Nach jedem Ladevorgang faengt die Beobachtung von vorne an.
         *
         * Welche Autos vor dem Speichern dastanden, weiss niemand mehr. Ohne
         * diesen Schnitt zaehlte die naechste Probe den halben Parkplatz als
         * Neuankunft - und der Tageswert waere Unsinn.
         */
        [Preserve]
        protected override void OnGameLoadingComplete(
            Colossal.Serialization.Entities.Purpose zweck, GameMode modus)
        {
            base.OnGameLoadingComplete(zweck, modus);
            VergissLaufendeBeobachtung();
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (++_frames < ProbeAlleFrames) return;
            _frames = 0;
            if (_lotQuery.IsEmptyIgnoreFilter) return;
            Tasten();
        }

        private void Tasten()
        {
            var jetzt = _simulation?.frameIndex ?? 0u;
            if (!VersucheZeit(out var stunde, out var tag)) return;

            using var lots = _lotQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
            var proben = new Dictionary<Entity, Probe>(lots.Length);
            for (var i = 0; i < lots.Length; i++) proben[lots[i]] = new Probe();

            _comfort.SammleUnsereParkspuren(_spuren, _spurLots);
            for (var i = 0; i < _spuren.Count; i++)
            {
                if (!proben.TryGetValue(_spurLots[i], out var probe)) continue;
                LiesSpur(_spuren[i], probe);
            }

            foreach (var paar in proben)
                Verbuche(paar.Key, paar.Value, jetzt, stunde, tag);

            RaeumeVerschwundeneLots(proben);
            _ersteProbeDerSitzung = false;
        }

        /**
         * Spielstunde und Spieltag.
         *
         * Beides kommt aus `TimeSystem`. Schlaegt das fehl - etwa in einem
         * halb geladenen Zustand -, wird die Probe verworfen statt geraten:
         * ein falsch einsortierter Stundenwert bliebe fuer immer im Profil.
         */
        private bool VersucheZeit(out int stunde, out int tag)
        {
            stunde = 0;
            tag = 0;
            if (_zeit == null) return false;
            try
            {
                var jetzt = _zeit.GetCurrentDateTime();
                stunde = jetzt.Hour;
                tag = jetzt.Year * 366 + jetzt.DayOfYear;
                return true;
            }
            catch (Exception fehler)
            {
                if (!_zeitfehlerGemeldet)
                {
                    _zeitfehlerGemeldet = true;
                    Mod.log.Warn("PLT-Statistik: keine Spielzeit lesbar, "
                        + "Proben werden ausgelassen. " + fehler.Message);
                }
                return false;
            }
        }

        private void LiesSpur(Entity spur, Probe probe)
        {
            if (!EntityManager.HasBuffer<Game.Net.LaneObject>(spur)) return;
            var objekte = EntityManager.GetBuffer<Game.Net.LaneObject>(spur, true);
            for (var i = 0; i < objekte.Length; i++)
            {
                var fahrzeug = objekte[i].m_LaneObject;
                if (fahrzeug == Entity.Null || !EntityManager.Exists(fahrzeug))
                    continue;
                // Nur wirklich abgestellte Autos. Der Puffer traegt zeitweise
                // auch Fahrzeuge, die gerade erst einbiegen.
                if (!EntityManager.HasComponent<Game.Vehicles.ParkedCar>(fahrzeug))
                    continue;

                probe.Belegt++;
                probe.Stehende.Add(fahrzeug);
                LiesHalter(fahrzeug, probe);
            }
        }

        /**
         * Vom abgestellten Auto zum Menschen dahinter.
         *
         * Der Zweck am Buerger sagt NICHT, wann jemand losfaehrt, sondern was
         * in Laufweite liegt: steht das Auto hier und der Halter hat den
         * Zweck „zur Arbeit", dann arbeitet er hier in der Naehe. Das ist der
         * Punkt, an dem ich mich beim Planen zuerst vertan hatte; die
         * Zuordnung steht ausformuliert in PLAN-Infokarten.md.
         */
        private void LiesHalter(Entity fahrzeug, Probe probe)
        {
            if (!EntityManager.HasComponent<Game.Vehicles.PersonalCar>(fahrzeug))
                return;
            var halter = EntityManager
                .GetComponentData<Game.Vehicles.PersonalCar>(fahrzeug).m_Keeper;
            if (halter == Entity.Null || !EntityManager.Exists(halter)) return;

            probe.Personen++;

            if (EntityManager.HasComponent<TravelPurpose>(halter))
                ZaehleZweck(EntityManager
                    .GetComponentData<TravelPurpose>(halter).m_Purpose, probe);
            else
                probe.Sonstige++;

            if (EntityManager.HasComponent<Citizen>(halter))
            {
                var buerger = EntityManager.GetComponentData<Citizen>(halter);
                switch (buerger.GetAge())
                {
                    case CitizenAge.Child: probe.Kind++; break;
                    case CitizenAge.Teen: probe.Jugend++; break;
                    case CitizenAge.Elderly: probe.Senior++; break;
                    default: probe.Erwachsen++; break;
                }
                // 3 und 4 sind WellEducated und HighlyEducated - in CS2 die
                // beiden Stufen mit Hochschule.
                if (buerger.GetEducationLevel() >= 3) probe.Hochschule++;
                probe.ZufriedenSumme += buerger.Happiness;
                probe.ZufriedenAnzahl++;
            }

            if (EntityManager.HasComponent<HouseholdMember>(halter))
            {
                var haushalt = EntityManager
                    .GetComponentData<HouseholdMember>(halter).m_Household;
                if (haushalt != Entity.Null && EntityManager.Exists(haushalt)
                    && EntityManager.HasComponent<TouristHousehold>(haushalt))
                    probe.Touristen++;
            }

            LiesZiel(fahrzeug, halter, probe);
        }

        private void ZaehleZweck(Purpose zweck, Probe probe)
        {
            switch (zweck)
            {
                case Purpose.Shopping:
                case Purpose.CompanyShopping:
                    probe.Einkaufen++;
                    break;
                case Purpose.GoingToWork:
                case Purpose.Working:
                    probe.Arbeiten++;
                    break;
                case Purpose.GoingHome:
                case Purpose.Sleeping:
                case Purpose.WaitingHome:
                    probe.NachHause++;
                    break;
                case Purpose.Leisure:
                case Purpose.Relaxing:
                    probe.Freizeit++;
                    break;
                case Purpose.Sightseeing:
                case Purpose.VisitAttractions:
                    probe.Besichtigen++;
                    break;
                case Purpose.PathFailed:
                    /*
                     * WAS DAS WIRKLICH HEISST.
                     *
                     * Nicht „jemand hat den Parkplatz nicht gefunden" - so
                     * hatte ich es zuerst geplant, und so ist es NICHT
                     * lesbar: ein gescheiterter Weg haengt am Buerger, nicht
                     * am Ziel, das er nicht erreicht hat. Wer hier gezaehlt
                     * wird, hat auf diesem Platz geparkt und kommt von hier
                     * aus nicht weiter. Auch das ist ein echter Mangel -
                     * aber ein anderer.
                     */
                    probe.Steckengeblieben++;
                    probe.Sonstige++;
                    break;
                default:
                    probe.Sonstige++;
                    break;
            }
        }

        private void LiesZiel(Entity fahrzeug, Entity halter, Probe probe)
        {
            if (!EntityManager.HasComponent<CurrentBuilding>(halter)) return;
            var ziel = EntityManager
                .GetComponentData<CurrentBuilding>(halter).m_CurrentBuilding;
            if (ziel == Entity.Null || !EntityManager.Exists(ziel)) return;
            if (!EntityManager.HasComponent<Game.Objects.Transform>(ziel)) return;
            // Unsere eigene Flaeche ist kein Ziel; sie ist der Ausgangspunkt.
            if (EntityManager.HasComponent<ParkingLotCarrierReference>(ziel))
                return;

            probe.Ziele.TryGetValue(ziel, out var bisher);
            probe.Ziele[ziel] = bisher + 1;

            if (!EntityManager.HasComponent<Game.Objects.Transform>(fahrzeug))
                return;
            var vonWo = EntityManager
                .GetComponentData<Game.Objects.Transform>(fahrzeug).m_Position;
            var nachWo = EntityManager
                .GetComponentData<Game.Objects.Transform>(ziel).m_Position;
            var meter = Unity.Mathematics.math.distance(
                new Unity.Mathematics.float2(vonWo.x, vonWo.z),
                new Unity.Mathematics.float2(nachWo.x, nachWo.z));
            probe.WegSummeDezimeter += (int)(meter * 10f);
            probe.WegAnzahl++;
        }

        // ------------------------------------------------------------------
        // Verbuchen
        // ------------------------------------------------------------------

        private void Verbuche(Entity lot, Probe probe, uint jetzt,
                              int stunde, int tag)
        {
            if (!EntityManager.Exists(lot)) return;

            var statistik = HoleStatistik(lot, jetzt, out var ersteProbe);
            var kapazitaet = EntityManager
                .GetComponentData<ParkingLotEconomyData>(lot).Capacity;

            var lauf = HoleLauf(lot);
            var angekommen = ZaehleAnkuenfte(lauf, probe, jetzt,
                out var standzeitMinuten, out var abgefahren);

            statistik.Proben++;
            statistik.AutosGesamt += angekommen;

            var belegtPromille = kapazitaet > 0
                ? Unity.Mathematics.math.clamp(
                    (probe.Belegt * 1000) / kapazitaet, 0, 1000)
                : 0;
            statistik.BelegtPromille = ParkingLotStatistik.Glaette(
                statistik.BelegtPromille, belegtPromille, ersteProbe);
            statistik.BelegtJetzt = probe.Belegt;
            if (probe.Belegt > statistik.BelegtHoechst)
            {
                statistik.BelegtHoechst = probe.Belegt;
                statistik.BelegtHoechstFrame = jetzt;
            }

            /*
             * DIE ANTEILE HAENGEN AN DEN PERSONEN, NICHT AN DEN BUCHTEN.
             *
             * Nenner ist die Zahl der Halter, die wir wirklich lesen konnten.
             * Nimmt man stattdessen die Belegung, sinken alle Anteile, sobald
             * ein Auto ohne lesbaren Halter dasteht - und die Karte behauptet
             * dann, „wenige" kaemen zum Einkaufen, obwohl es alle waren.
             */
            var n = probe.Personen;
            if (n > 0)
            {
                statistik.ZweckEinkaufen = ParkingLotStatistik.Glaette(
                    statistik.ZweckEinkaufen, Anteil(probe.Einkaufen, n), ersteProbe);
                statistik.ZweckArbeiten = ParkingLotStatistik.Glaette(
                    statistik.ZweckArbeiten, Anteil(probe.Arbeiten, n), ersteProbe);
                statistik.ZweckNachHause = ParkingLotStatistik.Glaette(
                    statistik.ZweckNachHause, Anteil(probe.NachHause, n), ersteProbe);
                statistik.ZweckFreizeit = ParkingLotStatistik.Glaette(
                    statistik.ZweckFreizeit, Anteil(probe.Freizeit, n), ersteProbe);
                statistik.ZweckBesichtigen = ParkingLotStatistik.Glaette(
                    statistik.ZweckBesichtigen, Anteil(probe.Besichtigen, n), ersteProbe);
                statistik.ZweckSonstige = ParkingLotStatistik.Glaette(
                    statistik.ZweckSonstige, Anteil(probe.Sonstige, n), ersteProbe);
                statistik.TouristPromille = ParkingLotStatistik.Glaette(
                    statistik.TouristPromille, Anteil(probe.Touristen, n), ersteProbe);
                statistik.AlterKind = ParkingLotStatistik.Glaette(
                    statistik.AlterKind, Anteil(probe.Kind, n), ersteProbe);
                statistik.AlterJugend = ParkingLotStatistik.Glaette(
                    statistik.AlterJugend, Anteil(probe.Jugend, n), ersteProbe);
                statistik.AlterErwachsen = ParkingLotStatistik.Glaette(
                    statistik.AlterErwachsen, Anteil(probe.Erwachsen, n), ersteProbe);
                statistik.AlterSenior = ParkingLotStatistik.Glaette(
                    statistik.AlterSenior, Anteil(probe.Senior, n), ersteProbe);
                statistik.BildungPromille = ParkingLotStatistik.Glaette(
                    statistik.BildungPromille, Anteil(probe.Hochschule, n), ersteProbe);
                statistik.KeinWegPromille = ParkingLotStatistik.Glaette(
                    statistik.KeinWegPromille,
                    Anteil(probe.Steckengeblieben, n), ersteProbe);
            }

            if (probe.ZufriedenAnzahl > 0)
                statistik.Zufriedenheit = ParkingLotStatistik.Glaette(
                    statistik.Zufriedenheit,
                    probe.ZufriedenSumme / probe.ZufriedenAnzahl, ersteProbe);

            if (probe.WegAnzahl > 0)
                statistik.FusswegDezimeter = ParkingLotStatistik.Glaette(
                    statistik.FusswegDezimeter,
                    probe.WegSummeDezimeter / probe.WegAnzahl, ersteProbe);

            if (abgefahren > 0)
                statistik.StandzeitMinuten = ParkingLotStatistik.Glaette(
                    statistik.StandzeitMinuten, standzeitMinuten,
                    statistik.StandzeitMinuten == 0);

            MerkeGebuehrenwechsel(lot, ref statistik, jetzt);
            EntityManager.SetComponentData(lot, statistik);

            SchreibeStunde(lot, stunde, belegtPromille);
            SchreibeTag(lot, tag, angekommen);
            SchreibeZiele(lot, probe);
        }

        private static int Anteil(int teil, int ganzes)
            => ganzes <= 0 ? 0
               : Unity.Mathematics.math.clamp((teil * 1000) / ganzes, 0, 1000);

        private ParkingLotStatistik HoleStatistik(Entity lot, uint jetzt,
                                                  out bool ersteProbe)
        {
            if (EntityManager.HasComponent<ParkingLotStatistik>(lot))
            {
                var vorhanden = EntityManager
                    .GetComponentData<ParkingLotStatistik>(lot);
                ersteProbe = vorhanden.Proben == 0;
                return vorhanden;
            }

            ersteProbe = true;
            var frisch = new ParkingLotStatistik
            {
                Version = ParkingLotStatistik.AktuelleVersion,
                /*
                 * BEI ALTBESTAND BLEIBT DAS BAUDATUM UNBEKANNT.
                 *
                 * Bei der ersten Probe nach dem Laden steht jeder Parkplatz
                 * schon da; der aktuelle Frame waere gelogen. Lieber sagt die
                 * Karte „gebaut: unbekannt", als ein Datum zu erfinden, an dem
                 * nichts passiert ist. Wer spaeter dazukommt, ist dagegen
                 * nachweislich neu.
                 */
                GebautFrame = _ersteProbeDerSitzung ? 0u : jetzt,
                LetzteGebuehr = -1,
            };
            EntityManager.AddComponentData(lot, frisch);
            return frisch;
        }

        private Lotlauf HoleLauf(Entity lot)
        {
            if (_laeufe.TryGetValue(lot, out var vorhanden)) return vorhanden;
            var neu = new Lotlauf();
            _laeufe[lot] = neu;
            return neu;
        }

        /**
         * Wer ist neu dazugekommen, wer ist weg - und wie lange stand er.
         *
         * Die erste Probe nach dem Laden impft nur: alles, was dasteht, gilt
         * als bereits bekannt. Ohne diesen Schritt zaehlte jeder Ladevorgang
         * den halben Parkplatz als Neuankunft.
         */
        private int ZaehleAnkuenfte(Lotlauf lauf, Probe probe, uint jetzt,
                                    out int standzeitMinuten, out int abgefahren)
        {
            standzeitMinuten = 0;
            abgefahren = 0;

            if (!lauf.Geimpft)
            {
                lauf.Geimpft = true;
                foreach (var fahrzeug in probe.Stehende)
                {
                    lauf.Stehende.Add(fahrzeug);
                    lauf.Ankunft[fahrzeug] = jetzt;
                }
                return 0;
            }

            var angekommen = 0;
            foreach (var fahrzeug in probe.Stehende)
            {
                if (lauf.Stehende.Contains(fahrzeug)) continue;
                angekommen++;
                lauf.Ankunft[fahrzeug] = jetzt;
            }

            var standzeitSumme = 0L;
            _abgeraeumt.Clear();
            foreach (var fahrzeug in lauf.Stehende)
            {
                if (probe.Stehende.Contains(fahrzeug)) continue;
                _abgeraeumt.Add(fahrzeug);
                if (!lauf.Ankunft.TryGetValue(fahrzeug, out var seit)) continue;
                if (jetzt <= seit) continue;
                standzeitSumme += FramesInMinuten(jetzt - seit);
                abgefahren++;
            }
            for (var i = 0; i < _abgeraeumt.Count; i++)
            {
                lauf.Stehende.Remove(_abgeraeumt[i]);
                lauf.Ankunft.Remove(_abgeraeumt[i]);
            }
            foreach (var fahrzeug in probe.Stehende) lauf.Stehende.Add(fahrzeug);

            if (abgefahren > 0)
                standzeitMinuten = (int)(standzeitSumme / abgefahren);
            return angekommen;
        }

        /**
         * Simulationsframes in Spielminuten.
         *
         * `TimeSystem.kTicksPerDay` ist die einzige belastbare Groesse dafuer:
         * so viele Frames dauert ein Spieltag, also 1440 Spielminuten.
         */
        private static long FramesInMinuten(uint frames)
            => (long)frames * 1440L / Game.Simulation.TimeSystem.kTicksPerDay;

        private void MerkeGebuehrenwechsel(Entity lot,
                                           ref ParkingLotStatistik statistik,
                                           uint jetzt)
        {
            var gebuehr = EntityManager
                .GetComponentData<ParkingLotEconomyData>(lot).ParkingFee;
            if (gebuehr == statistik.LetzteGebuehr) return;

            // Der allererste gelesene Stand ist keine Aenderung des Nutzers.
            if (statistik.LetzteGebuehr >= 0)
            {
                statistik.GebuehrFrame = jetzt;
                statistik.BelegtVorGebuehr = statistik.BelegtPromille;
            }
            statistik.LetzteGebuehr = gebuehr;
        }

        private void SchreibeStunde(Entity lot, int stunde, int belegtPromille)
        {
            var puffer = EntityManager.HasBuffer<ParkingLotStundenwert>(lot)
                ? EntityManager.GetBuffer<ParkingLotStundenwert>(lot)
                : EntityManager.AddBuffer<ParkingLotStundenwert>(lot);
            while (puffer.Length < StundenImTag)
                puffer.Add(new ParkingLotStundenwert());
            if (stunde < 0 || stunde >= puffer.Length) return;

            var wert = puffer[stunde];
            wert.BelegtPromille = ParkingLotStatistik.Glaette(
                wert.BelegtPromille, belegtPromille, wert.Proben == 0);
            wert.Proben++;
            puffer[stunde] = wert;
        }

        private void SchreibeTag(Entity lot, int tag, int angekommen)
        {
            var puffer = EntityManager.HasBuffer<ParkingLotTagwert>(lot)
                ? EntityManager.GetBuffer<ParkingLotTagwert>(lot)
                : EntityManager.AddBuffer<ParkingLotTagwert>(lot);

            for (var i = 0; i < puffer.Length; i++)
            {
                if (puffer[i].Tag != tag) continue;
                var treffer = puffer[i];
                treffer.Autos += angekommen;
                puffer[i] = treffer;
                return;
            }

            if (puffer.Length < TagringLaenge)
            {
                puffer.Add(new ParkingLotTagwert { Tag = tag, Autos = angekommen });
                return;
            }

            // Ring ist voll: der aelteste Tag weicht.
            var aeltester = 0;
            for (var i = 1; i < puffer.Length; i++)
                if (puffer[i].Tag < puffer[aeltester].Tag) aeltester = i;
            puffer[aeltester] = new ParkingLotTagwert
            {
                Tag = tag,
                Autos = angekommen,
            };
        }

        /**
         * Zielrangliste pflegen.
         *
         * Alte Gewichte werden bei jeder Probe leicht abgesenkt, bevor die
         * neuen Treffer dazukommen. Sonst haette ein Supermarkt, der vor
         * hundert Spieltagen abgerissen wurde, fuer immer den ersten Platz.
         */
        private void SchreibeZiele(Entity lot, Probe probe)
        {
            var puffer = EntityManager.HasBuffer<ParkingLotZielwert>(lot)
                ? EntityManager.GetBuffer<ParkingLotZielwert>(lot)
                : EntityManager.AddBuffer<ParkingLotZielwert>(lot);

            for (var i = puffer.Length - 1; i >= 0; i--)
            {
                var wert = puffer[i];
                wert.Gewicht -= 1 + wert.Gewicht / 32;
                if (wert.Gewicht <= 0
                    || wert.Gebaeude == Entity.Null
                    || !EntityManager.Exists(wert.Gebaeude))
                {
                    puffer.RemoveAt(i);
                    continue;
                }
                puffer[i] = wert;
            }

            foreach (var paar in probe.Ziele)
            {
                var gefunden = false;
                for (var i = 0; i < puffer.Length; i++)
                {
                    if (puffer[i].Gebaeude != paar.Key) continue;
                    var wert = puffer[i];
                    wert.Gewicht += paar.Value * 8;
                    puffer[i] = wert;
                    gefunden = true;
                    break;
                }
                if (gefunden) continue;

                if (puffer.Length >= ZieleHoechstens)
                {
                    var schwaechster = 0;
                    for (var i = 1; i < puffer.Length; i++)
                        if (puffer[i].Gewicht < puffer[schwaechster].Gewicht)
                            schwaechster = i;
                    if (puffer[schwaechster].Gewicht >= paar.Value * 8) continue;
                    puffer.RemoveAt(schwaechster);
                }
                puffer.Add(new ParkingLotZielwert
                {
                    Gebaeude = paar.Key,
                    Gewicht = paar.Value * 8,
                });
            }
        }

        /** Abgerissene Parkplaetze fallen auch aus dem Arbeitsspeicher. */
        private void RaeumeVerschwundeneLots(Dictionary<Entity, Probe> proben)
        {
            _abgeraeumt.Clear();
            foreach (var paar in _laeufe)
                if (!proben.ContainsKey(paar.Key)) _abgeraeumt.Add(paar.Key);
            for (var i = 0; i < _abgeraeumt.Count; i++)
                _laeufe.Remove(_abgeraeumt[i]);
        }

        /**
         * Nach dem Laden wissen wir nicht mehr, welche Autos schon dastanden.
         *
         * Also alle Laeufe verwerfen; die naechste Probe impft neu.
         */
        internal void VergissLaufendeBeobachtung()
        {
            _laeufe.Clear();
            _frames = 0;
            _ersteProbeDerSitzung = true;
        }
    }
}
