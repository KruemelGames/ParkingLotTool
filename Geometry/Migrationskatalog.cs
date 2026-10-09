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
     * Regeln (MIGRATION-PLAN.md): ein Schritt schreibt NIE direkt Geometrie
     * oder loescht selbst Entities. Noetiger Netzwechsel wird als asynchroner
     * Auftrag an den regulaeren Edit/Neubau gegeben; er meldet erst Erfolg, wenn
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
            // Bleibt als Nummer stehen: Parkplaetze tragen den erreichten
            // Stand als Zahl, ein entfernter Schritt verschoebe alle danach.
            new Schritt(4, "GassenknotenEben",
                "Entfaellt (2026-09-26): die sinkenden Gassen kamen vom Edit, "
                + "der ins Gelaendeloch der alten Gasse baute. Das behebt der "
                + "Edit selbst; ein Neubau per Sync ist nicht noetig."),
            new Schritt(5, "ObjekteNichtAnFlaeche",
                "Objekte, die an der Parkplatzflaeche statt am Traeger "
                + "haengen (Ladesaeulen bis 2026-09-28), kommen an den "
                + "Traeger; an der Flaeche verstreut CS2 sie zufaellig."),
            new Schritt(6, "BaeumeAlternNicht",
                "Baeume bestehender Parkplaetze kommen auf eine Stufe ihrer "
                + "Altersauswahl zurueck und altern nicht mehr, sofern der "
                + "Zettel nichts anderes sagt (Standard seit 2026-10-02)."),
            new Schritt(7, "Fahrwege25MitKosten",
                "Stillgelegt (2026-10-02): der Neubau ueber das sichtbare "
                + "Werkzeug war vom Nutzer abbrechbar und verlor dabei Wege. "
                + "Neue und selbst bearbeitete Parkplaetze fahren 25 km/h; "
                + "der Hintergrund-Neubau folgt als eigener Schritt."),
            new Schritt(8, "Fahrwege25ImHintergrund",
                "Stillgelegt (2026-10-04): der Permanent-Neubau im Hintergrund kam nach neun "
                + "Runden nicht zuverlaessig durch. Ersetzt durch Schritt 9."),
            new Schritt(9, "Fahrwege25Tauschen",
                "Alte unsichtbare Vanilla-Fahrwege bekommen per Vanilla-Ersetzen (Temp + Apply) "
                + "den 25-km/h-Klon. Lage, Knoten, Anschluesse und Besitzer bleiben; Gassen und "
                + "Zoningstrassen frischen ihre Spuren selbst auf."),
            new Schritt(10, "PflanzenNachWuchs",
                "Pflanzen bestehender Parkplaetze werden neu gesetzt, wenn ihre Arten Buesche mit "
                + "Altersstufen enthalten (TreeData, unter 5 m): sie wurden bis 2026-10-04 wie Baeume "
                + "gepflanzt. Gleiche Einstellungen (Arten, Dichte, Alter, Seed), Grün aus dem "
                + "nachgerechneten Layout; Tausch per Temp + Apply."),
            new Schritt(11, "LaternenNachruesten",
                "Parkplaetze ohne Laternen und ohne Laternenzettel bekommen Laternen nach dem "
                + "Standard des Spielers. Pflanzen im Freiraum einer Laterne weichen (Nutzer: die "
                + "Laterne ist wichtiger); Abgleich der Buchtaufkleber gegen geaenderte Geometrie."),
            new Schritt(12, "LaternenNachZettel",
                "Parkplaetze, deren Laternenzettel Laternen verlangt, die aber keine einzige haben, "
                + "bekommen sie nach diesem Zettel. Grund (2026-10-06): das Wiederherstellen des "
                + "Bauplans schrieb bis 1.0.5 die Panelwahl als Zettel, ohne Laternen zu bauen - "
                + "Schritt 11 hielt sich dann fuer erledigt."),
            new Schritt(13, "FlaechenFuerCs2",
                "Flaechen, die CS2 verworfen hat (Entity ohne Dreiecke, im Spiel nackter Boden), werden "
                + "durch Stuecke aus ihren eigenen Ecken ersetzt, die CS2 annimmt (2026-10-08). Grund: "
                + "CS2 rueckt jede Flaeche 0,1 m ein; ein Detail unter etwa 0,2 m liess die ganze Flaeche "
                + "fallen. Kein Neubau; Prefab, Besitzer und Hoehen bleiben."),
        };

        /** Der Stand, den ein heute gebauter Parkplatz hat. */
        public static int Aktuell => Schritte.Length;
    }
}
