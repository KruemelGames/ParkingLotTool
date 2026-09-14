using System.Collections.Generic;
using Game.City;
using ParkingLotTool.Geometry;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    /**
     * Die Zahlen hinter den Infokarten - eingesammelt und zurechtgelegt.
     *
     * Getrennt von `ParkingLotListeUISystem.cs`, weil das dort sonst deutlich
     * ueber achthundert Zeilen ginge. Bindungen und Ausloeser stehen drueben,
     * hier steht nur Rechnen und Ablesen.
     */
    public sealed partial class ParkingLotListeUISystem
    {
        /**
         * Ab diesem Belegungsunterschied gilt ein anderer Platz als „leer".
         *
         * 400 Promille sind vierzig Prozentpunkte. Weniger waere kein Rat,
         * sondern Rauschen: zwei Plaetze bei 70 und 80 Prozent sind beide voll.
         */
        private const int LeererNachbarAbstand = 400;

        /** Ein Infoblock, wie er ueber die Bindung geht. */
        private struct Infoblock
        {
            internal Infoart Art;
            internal int Zahl1;
            internal int Zahl2;
            internal int Zahl3;
            internal int Wort;
            internal string ZielSchluessel;
            internal string ZielName;
        }

        private readonly List<Entity> _zielSortiert = new List<Entity>();

        // ------------------------------------------------------------------
        // Einsammeln
        // ------------------------------------------------------------------

        private void SammleWerte()
        {
            _werte.Clear();
            var stadtZufriedenheit = StadtZufriedenheit();

            for (var i = 0; i < _lots.Count; i++)
                _werte[_lots[i]] = LiesLot(_lots[i], stadtZufriedenheit);

            SetzeVergleichswerte();
        }

        private Infowerte LiesLot(Entity lot, int stadtZufriedenheit)
        {
            // Kapazitaet 0 heisst „noch nicht gemessen", nicht „kein Platz".
            // Die Karte zeigt dann eine Null und fuellt sich von selbst.
            var wirtschaft = EntityManager
                .HasComponent<ParkingLotEconomyData>(lot)
                ? EntityManager.GetComponentData<ParkingLotEconomyData>(lot)
                : default(ParkingLotEconomyData);
            var werte = new Infowerte
            {
                Kapazitaet = wirtschaft.Capacity,
                ZufriedenheitStadt = stadtZufriedenheit,
                Zufahrten = ZufahrtenVon(lot),
            };

            if (EntityManager.HasComponent<ParkingLotStatistik>(lot))
            {
                var s = EntityManager.GetComponentData<ParkingLotStatistik>(lot);
                werte.Proben = s.Proben;
                werte.BelegtPromille = s.BelegtPromille;
                werte.ZweckEinkaufen = s.ZweckEinkaufen;
                werte.ZweckArbeiten = s.ZweckArbeiten;
                werte.ZweckNachHause = s.ZweckNachHause;
                werte.ZweckFreizeit = s.ZweckFreizeit;
                werte.ZweckBesichtigen = s.ZweckBesichtigen;
                werte.TouristPromille = s.TouristPromille;
                werte.AlterKind = s.AlterKind;
                werte.AlterJugend = s.AlterJugend;
                werte.AlterErwachsen = s.AlterErwachsen;
                werte.AlterSenior = s.AlterSenior;
                werte.BildungPromille = s.BildungPromille;
                werte.Zufriedenheit = s.Zufriedenheit;
                werte.FusswegDezimeter = s.FusswegDezimeter;
                werte.KeinWegPromille = s.KeinWegPromille;
                werte.AutosGesamt = s.AutosGesamt;
                werte.StandzeitMinuten = s.StandzeitMinuten;
                werte.GebautBekannt = s.GebautFrame != 0u;
                werte.GebuehrenwirkungBekannt =
                    s.GebuehrFrame != 0u && s.BelegtVorGebuehr > 0;
            }

            if (EntityManager.HasBuffer<ParkingLotZielwert>(lot))
                werte.ZieleBekannt =
                    EntityManager.GetBuffer<ParkingLotZielwert>(lot, true).Length;

            /*
             * „GEBAEUDE IN LAUFWEITE" IST DIE ZAHL DER ECHTEN ZIELE.
             *
             * Zuerst wollte ich das raeumlich zaehlen - alles im Umkreis von
             * zweihundert Metern. Das waere teurer UND schwaecher: ein
             * Lagerhaus hinter einer Mauer steht im Umkreis, wird aber nie
             * betreten. Gezaehlt wird deshalb, wohin die Gaeste wirklich
             * gehen. Der Satz heisst darum auch „bedient", nicht „liegt bei".
             */
            werte.GebaeudeInLaufweite = werte.ZieleBekannt;

            if (EntityManager.HasBuffer<ParkingLotStundenwert>(lot))
            {
                var stunden =
                    EntityManager.GetBuffer<ParkingLotStundenwert>(lot, true);
                for (var i = 0; i < stunden.Length; i++)
                    if (stunden[i].Proben > 0) werte.StundenMitWerten++;
            }

            if (EntityManager.HasBuffer<ParkingLotTagwert>(lot))
            {
                var tage = EntityManager.GetBuffer<ParkingLotTagwert>(lot, true);
                werte.TageImRing = tage.Length;
                for (var i = 0; i < tage.Length; i++)
                    werte.AutosLetzteWoche += tage[i].Autos;
            }

            return werte;
        }

        /**
         * Was nur im Vergleich aller Plaetze feststeht.
         *
         * Rang, teuerster und guenstigster Platz je Bucht, und ob es einen
         * deutlich leereren Nachbarn gibt.
         */
        private void SetzeVergleichswerte()
        {
            if (_lots.Count == 0) return;

            var teuerster = Entity.Null;
            var guenstigster = Entity.Null;
            var teuerstenWert = int.MinValue;
            var guenstigstenWert = int.MaxValue;
            var leersteBelegung = int.MaxValue;

            for (var i = 0; i < _lots.Count; i++)
            {
                var werte = _werte[_lots[i]];
                if (werte.Kapazitaet > 0
                    && EntityManager
                        .HasComponent<ParkingLotEconomyData>(_lots[i]))
                {
                    var wirtschaft = EntityManager
                        .GetComponentData<ParkingLotEconomyData>(_lots[i]);
                    var jeBucht = wirtschaft.Upkeep / werte.Kapazitaet;
                    if (jeBucht > teuerstenWert)
                    { teuerstenWert = jeBucht; teuerster = _lots[i]; }
                    if (jeBucht < guenstigstenWert)
                    { guenstigstenWert = jeBucht; guenstigster = _lots[i]; }
                }
                if (werte.Proben >= Infoauswahl.ProbenFuerAussage
                    && werte.BelegtPromille < leersteBelegung)
                    leersteBelegung = werte.BelegtPromille;
            }

            // Rang nach Kapazitaet: 1 ist der groesste Platz der Stadt.
            var nachGroesse = new List<Entity>(_lots);
            nachGroesse.Sort(delegate (Entity a, Entity b)
            {
                var ka = _werte[a].Kapazitaet;
                var kb = _werte[b].Kapazitaet;
                if (ka != kb) return kb.CompareTo(ka);
                return a.Index.CompareTo(b.Index);
            });

            for (var rang = 0; rang < nachGroesse.Count; rang++)
            {
                var lot = nachGroesse[rang];
                var werte = _werte[lot];
                werte.RangInStadt = rang + 1;
                werte.LotsInStadt = _lots.Count;
                // Einen einzelnen Parkplatz „den teuersten der Stadt" zu
                // nennen waere albern - dafuer braucht es Vergleich.
                werte.TeuersterJeBucht = _lots.Count > 1 && lot == teuerster;
                werte.GuenstigsterJeBucht = _lots.Count > 1
                    && lot == guenstigster && guenstigster != teuerster;
                werte.LeererNachbarBekannt =
                    leersteBelegung != int.MaxValue
                    && werte.BelegtPromille - leersteBelegung
                       >= LeererNachbarAbstand;
                _werte[lot] = werte;
            }
        }

        private int ZufahrtenVon(Entity lot)
        {
            if (!EntityManager.HasBuffer<ParkingLotBuildEntrance>(lot)) return 0;
            return EntityManager
                .GetBuffer<ParkingLotBuildEntrance>(lot, true).Length;
        }

        /**
         * Stadtweite Zufriedenheit, exakt so gerechnet wie am Buerger.
         *
         * `Citizen.Happiness` ist `(WellBeing + Health) / 2`. Damit der
         * Vergleich ehrlich bleibt, wird der Stadtwert aus genau denselben
         * beiden Statistiken gebildet - nicht aus einem beliebigen anderen
         * Zufriedenheitsmass, das nur aehnlich heisst.
         */
        private int StadtZufriedenheit()
        {
            var quelle = _stadtwerte;
            if (quelle == null) return 0;
            var wohl = quelle.Wert(StatisticType.Wellbeing);
            var gesund = quelle.Wert(StatisticType.Health);
            if (wohl <= 0 && gesund <= 0) return 0;
            return (wohl + gesund) / 2;
        }

        // ------------------------------------------------------------------
        // Einen Block fuellen
        // ------------------------------------------------------------------

        private Infoblock BereiteBlock(Entity lot, Infoart art, Infowerte werte)
        {
            var block = new Infoblock
            {
                Art = art,
                ZielSchluessel = string.Empty,
                ZielName = string.Empty,
            };
            if (art == Infoart.Keine) return block;

            switch (art)
            {
                case Infoart.ZielRang:
                    FuelleZiel(lot, werte, ref block);
                    break;

                case Infoart.Zweck:
                    block.Wort = (int)Infoauswahl.StaerksterZweck(
                        werte, out var zweckAnteil);
                    block.Zahl1 = (int)Infokarten.MengenstufeVon(
                        zweckAnteil / 1000.0);
                    break;

                case Infoart.Wohnparkplatz:
                    block.Zahl1 = (int)Infokarten.MengenstufeVon(
                        werte.ZweckNachHause / 1000.0);
                    break;

                case Infoart.Touristen:
                    block.Zahl1 = (int)Infokarten.MengenstufeVon(
                        werte.TouristPromille / 1000.0);
                    break;

                case Infoart.Alter:
                    block.Wort = (int)Infoauswahl.StaerksteAltersgruppe(
                        werte, out var alterAnteil);
                    block.Zahl1 = (int)Infokarten.MengenstufeVon(
                        alterAnteil / 1000.0);
                    break;

                case Infoart.Bildung:
                    // „x von 10" liest sich besser als „61 Prozent".
                    block.Zahl1 = (werte.BildungPromille + 50) / 100;
                    break;

                case Infoart.Zufriedenheit:
                    block.Wort =
                        werte.Zufriedenheit >= werte.ZufriedenheitStadt ? 0 : 1;
                    block.Zahl1 = werte.Zufriedenheit;
                    block.Zahl2 = werte.ZufriedenheitStadt;
                    break;

                case Infoart.Fussweg:
                    block.Zahl1 = (werte.FusswegDezimeter + 5) / 10;
                    block.Wort = (int)Infokarten.WegstufeVon(block.Zahl1);
                    FuelleZiel(lot, werte, ref block, 0);
                    break;

                case Infoart.Laufweite:
                    block.Zahl1 = werte.GebaeudeInLaufweite;
                    break;

                case Infoart.Erreichbar:
                    ZaehleZielarten(lot, ref block);
                    break;

                case Infoart.Tagesprofil:
                    FuelleTagesprofil(lot, ref block);
                    break;

                case Infoart.Spitze:
                    FuelleSpitze(lot, werte, ref block);
                    break;

                case Infoart.Dauerlast:
                    block.Zahl1 = werte.TageImRing;
                    block.Zahl2 = werte.BelegtPromille / 10;
                    break;

                case Infoart.Leerstand:
                    block.Zahl1 = UnityEngine.Mathf.Max(0,
                        werte.Kapazitaet
                        - (werte.Kapazitaet * werte.BelegtPromille) / 1000);
                    break;

                case Infoart.Durchsatz:
                    block.Zahl1 = werte.AutosLetzteWoche;
                    block.Zahl2 = werte.TageImRing;
                    block.Zahl3 = werte.Kapazitaet > 0 && werte.TageImRing > 0
                        ? werte.AutosLetzteWoche
                          / (werte.Kapazitaet * werte.TageImRing)
                        : 0;
                    break;

                case Infoart.Standzeit:
                    block.Zahl1 = werte.StandzeitMinuten / 60;
                    block.Zahl2 = werte.StandzeitMinuten % 60;
                    break;

                case Infoart.Rang:
                    block.Zahl1 = werte.RangInStadt;
                    block.Zahl2 = werte.LotsInStadt;
                    break;

                case Infoart.Kosten:
                    block.Wort = werte.TeuersterJeBucht ? 0 : 1;
                    break;

                case Infoart.Bilanz:
                    FuelleBilanz(lot, werte, ref block);
                    break;

                // --- Maengel ---
                case Infoart.KeinWeg:
                    block.Zahl1 = (int)Infokarten.MengenstufeVon(
                        werte.KeinWegPromille / 1000.0);
                    break;

                case Infoart.Zufahrt:
                    block.Zahl1 = werte.Kapazitaet;
                    break;

                case Infoart.Ungleichgewicht:
                    FuelleLeerenNachbarn(lot, ref block);
                    break;

                case Infoart.Gebuehrenwirkung:
                    FuelleGebuehrenwirkung(lot, werte, ref block);
                    break;
            }
            return block;
        }

        /**
         * Ein Ziel aus der Rangliste - bei jeder Runde ein anderes.
         *
         * `rang` -1 heisst: nach Rundennummer durchwechseln. Sonst zeigte ein
         * Platz auf ewig denselben Supermarkt, obwohl die Liste neun Ziele
         * kennt. Der Fussweg dagegen bezieht sich immer auf das Hauptziel,
         * sonst passte die Entfernung nicht zum genannten Ort.
         */
        private void FuelleZiel(Entity lot, Infowerte werte,
                                ref Infoblock block, int rang = -1)
        {
            SortiereZiele(lot);
            if (_zielSortiert.Count == 0) return;

            var gewaehlt = rang >= 0
                ? UnityEngine.Mathf.Min(rang, _zielSortiert.Count - 1)
                : _rundennummer % _zielSortiert.Count;
            var ziel = _zielSortiert[gewaehlt];

            block.ZielSchluessel = SchluesselVon(ziel);
            block.ZielName = NameVon(ziel);
            block.Zahl1 = (int)Infokarten.MengenstufeVon(
                AnteilAmZiel(lot, ziel));
            if (rang >= 0)
                block.Zahl1 = (werte.FusswegDezimeter + 5) / 10;
        }

        private void SortiereZiele(Entity lot)
        {
            _zielSortiert.Clear();
            if (!EntityManager.HasBuffer<ParkingLotZielwert>(lot)) return;
            var puffer = EntityManager.GetBuffer<ParkingLotZielwert>(lot, true);
            for (var i = 0; i < puffer.Length; i++)
            {
                if (puffer[i].Gebaeude == Entity.Null) continue;
                if (!EntityManager.Exists(puffer[i].Gebaeude)) continue;
                _zielSortiert.Add(puffer[i].Gebaeude);
            }
            var gewichte = new Dictionary<Entity, int>();
            for (var i = 0; i < puffer.Length; i++)
                gewichte[puffer[i].Gebaeude] = puffer[i].Gewicht;
            _zielSortiert.Sort(delegate (Entity a, Entity b)
            {
                var ga = gewichte.ContainsKey(a) ? gewichte[a] : 0;
                var gb = gewichte.ContainsKey(b) ? gewichte[b] : 0;
                if (ga != gb) return gb.CompareTo(ga);
                return a.Index.CompareTo(b.Index);
            });
        }

        private double AnteilAmZiel(Entity lot, Entity ziel)
        {
            if (!EntityManager.HasBuffer<ParkingLotZielwert>(lot)) return 0.0;
            var puffer = EntityManager.GetBuffer<ParkingLotZielwert>(lot, true);
            var gesamt = 0;
            var dieses = 0;
            for (var i = 0; i < puffer.Length; i++)
            {
                gesamt += puffer[i].Gewicht;
                if (puffer[i].Gebaeude == ziel) dieses = puffer[i].Gewicht;
            }
            return gesamt > 0 ? (double)dieses / gesamt : 0.0;
        }

        /** Wie viele Laeden, Bueros und Wohnhaeuser die Gaeste ansteuern. */
        private void ZaehleZielarten(Entity lot, ref Infoblock block)
        {
            SortiereZiele(lot);
            for (var i = 0; i < _zielSortiert.Count; i++)
            {
                var ziel = _zielSortiert[i];
                if (EntityManager
                    .HasComponent<Game.Buildings.CommercialProperty>(ziel))
                    block.Zahl1++;
                else if (EntityManager
                    .HasComponent<Game.Buildings.OfficeProperty>(ziel))
                    block.Zahl2++;
                else if (EntityManager
                    .HasComponent<Game.Buildings.ResidentialProperty>(ziel))
                    block.Zahl3++;
            }
        }

        private void FuelleTagesprofil(Entity lot, ref Infoblock block)
        {
            if (!EntityManager.HasBuffer<ParkingLotStundenwert>(lot)) return;
            var stunden = EntityManager
                .GetBuffer<ParkingLotStundenwert>(lot, true);

            var mittel = 0;
            var gezaehlt = 0;
            for (var i = 0; i < stunden.Length; i++)
            {
                if (stunden[i].Proben == 0) continue;
                mittel += stunden[i].BelegtPromille;
                gezaehlt++;
            }
            if (gezaehlt == 0) return;
            mittel /= gezaehlt;

            // Erste Stunde ueber dem Mittel, letzte Stunde darueber.
            var voll = -1;
            var leer = -1;
            for (var i = 0; i < stunden.Length; i++)
            {
                if (stunden[i].Proben == 0) continue;
                if (stunden[i].BelegtPromille <= mittel) continue;
                if (voll < 0) voll = i;
                leer = i;
            }
            block.Zahl1 = voll < 0 ? 0 : voll;
            block.Zahl2 = leer < 0 ? 0 : (leer + 1) % 24;
        }

        private void FuelleSpitze(Entity lot, Infowerte werte,
                                  ref Infoblock block)
        {
            if (!EntityManager.HasBuffer<ParkingLotStundenwert>(lot)) return;
            var stunden = EntityManager
                .GetBuffer<ParkingLotStundenwert>(lot, true);
            var beste = -1;
            for (var i = 0; i < stunden.Length; i++)
            {
                if (stunden[i].Proben == 0) continue;
                if (beste < 0
                    || stunden[i].BelegtPromille > stunden[beste].BelegtPromille)
                    beste = i;
            }
            if (beste < 0) return;
            block.Zahl1 = beste;
            block.Zahl2 =
                (werte.Kapazitaet * stunden[beste].BelegtPromille) / 1000;
            block.Zahl3 = werte.Kapazitaet;
        }

        private void FuelleBilanz(Entity lot, Infowerte werte,
                                  ref Infoblock block)
        {
            block.Zahl2 = werte.AutosGesamt;
            if (!EntityManager.HasComponent<ParkingLotStatistik>(lot)) return;
            var s = EntityManager.GetComponentData<ParkingLotStatistik>(lot);
            var jetzt = _simulation != null ? _simulation.frameIndex : 0u;
            if (s.GebautFrame == 0u || jetzt <= s.GebautFrame) return;
            block.Zahl1 = (int)((jetzt - s.GebautFrame)
                / (uint)Game.Simulation.TimeSystem.kTicksPerDay);
        }

        private void FuelleLeerenNachbarn(Entity lot, ref Infoblock block)
        {
            var leerster = Entity.Null;
            var leersteBelegung = int.MaxValue;
            for (var i = 0; i < _lots.Count; i++)
            {
                if (_lots[i] == lot) continue;
                var andere = _werte[_lots[i]];
                if (andere.Proben < Infoauswahl.ProbenFuerAussage) continue;
                if (andere.BelegtPromille >= leersteBelegung) continue;
                leersteBelegung = andere.BelegtPromille;
                leerster = _lots[i];
            }
            if (leerster == Entity.Null) return;
            block.ZielSchluessel = SchluesselVon(leerster);
            block.ZielName = NameVon(leerster);
            block.Zahl1 = leersteBelegung / 10;
        }

        private void FuelleGebuehrenwirkung(Entity lot, Infowerte werte,
                                            ref Infoblock block)
        {
            if (!EntityManager.HasComponent<ParkingLotStatistik>(lot)) return;
            var s = EntityManager.GetComponentData<ParkingLotStatistik>(lot);
            var jetzt = _simulation != null ? _simulation.frameIndex : 0u;
            if (s.GebuehrFrame == 0u || jetzt <= s.GebuehrFrame) return;

            block.Zahl1 = (int)((jetzt - s.GebuehrFrame)
                / (uint)Game.Simulation.TimeSystem.kTicksPerDay);
            if (s.BelegtVorGebuehr > 0)
            {
                var wandel = ((s.BelegtPromille - s.BelegtVorGebuehr) * 100)
                    / s.BelegtVorGebuehr;
                block.Zahl2 = UnityEngine.Mathf.Abs(wandel);
                // Wort: 0 = mehr Autos, 1 = weniger, 2 = unveraendert.
                block.Wort = wandel > 2 ? 0 : (wandel < -2 ? 1 : 2);
            }
            else
            {
                block.Wort = 2;
            }
        }
    }

    /** Kapselt die stadtweiten Statistiken, damit sie ersetzbar bleiben. */
    internal interface ICityStatistikQuelle
    {
        int Wert(StatisticType art);
    }

    internal sealed class CityStatistikQuelle : ICityStatistikQuelle
    {
        private readonly Game.Simulation.CityStatisticsSystem _system;

        internal CityStatistikQuelle(World welt)
        {
            _system = welt?.GetOrCreateSystemManaged<
                Game.Simulation.CityStatisticsSystem>();
        }

        public int Wert(StatisticType art)
            => _system != null ? _system.GetStatisticValue(art) : 0;
    }
}
