using System;
using System.Collections.Generic;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class Program
{
    private static void PruefeVersorgungsFehlermuster(Action<bool, string> pruefe)
    {
        // Bilanz der zwei Kurse aus Muster A, keine Nachbildung des ECS-Apply.
        var offen = VersorgungsapplyZustand.Offen;
        var dauerhaft = VersorgungsapplyZustand.Dauerhaft;
        var verloren = VersorgungsapplyZustand.Verloren;
        void Bilanz(int soll, int fest, int temp, int deleted, int fehlt,
            VersorgungsapplyZustand erwartet, string name)
            => pruefe(VersorgungsapplyPruefung.Zustand(soll, fest, temp, deleted, fehlt) == erwartet, "A: " + name);
        Bilanz(2, 0, 2, 0, 0, offen, "vor Apply: 2 Temp sind kein Erfolg");
        Bilanz(2, 1, 1, 0, 0, offen, "halber Apply bleibt offen");
        Bilanz(2, 2, 0, 0, 0, dauerhaft, "Gegenbeispiel: beide Entities dauerhaft");
        Bilanz(2, 0, 0, 0, 2, verloren, "0 Temp, aber beide verschwunden");
        Bilanz(2, 0, 0, 2, 0, verloren, "0 Temp, beide Deleted");
        Bilanz(2, 1, 0, 0, 1, verloren, "eine von zwei verloren");
        Bilanz(2, 0, 1, 1, 0, verloren, "Deleted bei noch laufendem Temp-Zyklus");
        Bilanz(0, 0, 0, 0, 0, offen, "Nichts-Tun besteht nicht");
        Bilanz(2, 0, 0, 0, 0, offen, "leere Abfrage ersetzt keine Entity-Bilanz");
        Bilanz(4, 2, 2, 0, 0, offen, "Netz 1 fest, Netz 2 noch temporaer");
        Bilanz(4, 4, 0, 0, 0, dauerhaft, "zwei aufeinanderfolgende Netze erhalten");
        Bilanz(4, 2, 0, 0, 2, verloren, "Verlust von Netz 1 beim zweiten Zyklus");

        // Synthetische endliche Zielstrasse x=10, y=0..10, Gesamtfangradius 1 m.
        // Am unteren Ende ragt Strom um 1,5 m heraus, am oberen Wasser.
        // 72 Drehungen und versetzte Weltkoordinaten vermeiden einen Achsen-Sonderfall.
        for (var winkel = 0; winkel < 360; winkel += 5)
        {
            var w = winkel * math.PI / 180;
            float2 Drehe(float2 p) => new float2(p.x * math.cos(w) - p.y * math.sin(w),
                p.x * math.sin(w) + p.y * math.cos(w)) + new float2(-1052.6f, -32.9f);
            var a = Drehe(new float2(10, 0));
            var b = Drehe(new float2(10, 10));
            bool Tor(float2 p, bool strom) => VersorgungskursPruefung.Anschluss(
                Versorgungsweg.Streckenabstand(p, p, a, b), 1, 0.5f, 0.25f, true);
            foreach (var y in new[] { 0f, 10f, 5f })
            {
                var weg = new List<float2> { Drehe(new float2(0, y)), Drehe(new float2(10, y)) };
                var strom = Versorgungsweg.Versetze(weg, -1.5f);
                var wasser = Versorgungsweg.Versetze(weg, 1.5f);
                pruefe(Tor(weg[1], true), $"B: Mittellinie trifft bei y={y}, Winkel {winkel}");
                pruefe(Tor(strom[1], true) == (y != 0) && Tor(wasser[1], false) == (y != 10),
                    $"B: wechselndes fehlendes Zielende bei y={y}, Winkel {winkel}");
                pruefe(Versorgungsweg.Spuren(weg, 0.5f, 0.5f, new List<Versorgungsweg.Hindernis>(),
                    null, 8, out _, out _, Tor) == (y == 5),
                    $"B: beide Spuren vor Auswahl pruefen bei y={y}, Winkel {winkel}");
                // Reale Logbreiten 1,50/3,50 m, Suchweite 0: schon eine nur
                // 1,50 m breite Zielstrasse faengt beide um 1,50 m versetzten Enden.
                // Der schmale Kunstfall oben ist deshalb KEIN Replay des Spielfehlers.
                pruefe(Versorgungsweg.Spuren(weg, 1.5f, 3.5f, new List<Versorgungsweg.Hindernis>(),
                    null, 8, out _, out _, (p, s) => VersorgungskursPruefung.Anschluss(
                        Versorgungsweg.Streckenabstand(p, p, a, b), 1.5f, s ? 1.5f : 3.5f, 0, true)),
                    $"B: Logbreiten treffen auch am Strassenende bei y={y}, Winkel {winkel}");
            }
            // Ein abgelehntes nahes Ziel darf das weiter entfernte gueltige nicht verdecken.
            var suche = Versorgungsweg.Suche(new List<float2> { Drehe(new float2(0, 0)) },
                new List<Versorgungsweg.Hindernis>(), _ => null,
                _ => new[] { new Versorgungsweg.Ziel { Punkt = a, Index = 0 },
                    new Versorgungsweg.Ziel { Punkt = Drehe(new float2(10, 5)), Index = 1 } },
                (weg, ziel) => Versorgungsweg.Spuren(weg, 0.5f, 0.5f,
                    new List<Versorgungsweg.Hindernis>(), null, 8, out _, out _, Tor));
            pruefe(suche.Punkte != null && suche.Ziel == 1,
                $"B: vorhandene vollstaendige Alternative wird gebaut, Winkel {winkel}");
        }
        pruefe(VersorgungskursPruefung.Anschluss(1.00001f, 1, 0.5f, 0.25f, true), "B: Epsilon am Fangradius");
        pruefe(!VersorgungskursPruefung.Anschluss(1.01f, 1, 0.5f, 0.25f, true), "B: ausserhalb Fangradius");
        pruefe(!VersorgungskursPruefung.Anschluss(0, 1, 0.5f, 0.25f, false), "B: Geometrie ersetzt keine Layer");
    }
}
