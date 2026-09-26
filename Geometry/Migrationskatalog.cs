namespace ParkingLotTool.Geometry
{
    /**
     * DIE SCHRITTE, MIT DENEN EIN PARKPLATZ AUF DEN STAND DIESER VERSION
     * NACHGERUESTET WIRD ("synchronisieren").
     *
     * Hier steht nur der Katalog - Nummer, Name, Zweck. Die Ausfuehrung liegt
     * in `Tools/ParkingLotSync.Schritte.cs`, weil sie Entities anfasst. Der
     * Test `--migrationen` prueft beides gegeneinander: Nummern lueckenlos ab
     * 1, Namen eindeutig, und zu jedem Namen genau eine Ausfuehrung.
     *
     * Regeln (MIGRATION-PLAN.md): ein Schritt aendert NIE Geometrie und
     * loescht NIE Entities; er ist wiederholbar; er meldet erst Erfolg, wenn
     * seine Nachpruefung die Wirkung sieht. Neue Schritte kommen HINTEN
     * dazu - die Nummer eines Schritts ist der Datenstand, den ein
     * Parkplatz nach ihm hat, und steht damit in Spielstaenden.
     */
    public static class Migrationskatalog
    {
        public sealed class Schritt
        {
            public int Nummer { get; }
            public string Name { get; }
            public string Zweck { get; }

            public Schritt(int nummer, string name, string zweck)
            {
                Nummer = nummer;
                Name = name;
                Zweck = zweck;
            }
        }

        public static readonly Schritt[] Schritte =
        {
            new Schritt(1, "PflanzenAmTraeger",
                "Pflanzen aus Spielstaenden vor dem 2026-09-14 bekommen den "
                + "Traeger als Besitzer; ohne ihn verdraengt CS2 sie laufend."),
            new Schritt(2, "ObjekteAmTraeger",
                "Aufkleber, Pfeile und Saeulen aus Spielstaenden vor dem "
                + "2026-09-24 bekommen den Traeger als Besitzer; ohne ihn "
                + "loescht sie der Vegetationspinsel."),
            new Schritt(3, "HaltestellenOhneBesitzer",
                "Bushaltestellen, die am 2026-09-24 kurz mit Besitzer gebaut "
                + "wurden, verlieren ihn; sonst sind sie im Linienwerkzeug "
                + "nicht anwaehlbar."),
            new Schritt(4, "GassenknotenEben",
                "Parkplaetze mit Zufahrtsgassen aus Staenden vor dem 2026-09-26 "
                + "werden ueber den Bearbeiten-Weg neu gebaut: am inneren "
                + "Gassenende hing ein nicht einebnender Weg, CS2 legte den "
                + "gemeinsamen Knoten aufs weggeschnittene Gelaende, und die "
                + "Gasse sank in den Boden."),
        };

        /** Der Stand, den ein heute gebauter Parkplatz hat. */
        public static int Aktuell => Schritte.Length;
    }
}
