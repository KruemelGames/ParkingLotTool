using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using ParkingLotTool.Geometry;
using Unity.Entities;
using Unity.Mathematics;
using Kurven = ParkingLotTool.Geometry.Strassenkurve;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private readonly Dictionary<long, Kurven.Kante> _kurvenFangnetz = new Dictionary<long, Kurven.Kante>();
        private readonly List<Strassenecke.Linienanker> _kurvenLinien = new List<Strassenecke.Linienanker>();
        private readonly List<string> _kurvenLinienGruende = new List<string>();
        private Strassenecke.Linienanker _kurvenLinienanker;
        private string _kurvenLinienGrund;
        private Kurven.Kante _kurvenLinienkante;
        private Kurven.Bezier _kurvenLinienkurve;
        private bool _kurvenLinieIstKnoten;
        // Alle bestaetigten Bordsteinecken dieses Fangs. Welche gemeint ist,
        // entscheidet erst die Endposition: ein Hilfslinienschnitt vom
        // vorigen Punkt kann dieselbe Ecke mit besserer Prioritaet treffen.
        private readonly List<Strassenecke> _kurvenEcken = new List<Strassenecke>();

        private void BeginKurveneckenfang()
        {
            _kurvenFangnetz.Clear();
            _kurvenLinien.Clear();
            _kurvenLinienGruende.Clear();
            _kurvenLinienkante = null;
            _kurvenLinienanker = default;
            _kurvenEcken.Clear();
        }

        private Kurven.Kante LeseKurvenfangkante(Entity e)
        {
            long id = KurvenId(e);
            if (_kurvenFangnetz.TryGetValue(id, out var k)) return k;
            k = LeseKurvenkante(e);
            if (k != null) _kurvenFangnetz.Add(id, k);
            return k;
        }

        private void MerkeKurvenlinienquelle(Bezier4x3 kurve, bool knoten)
        {
            if (!_kurvenSammeln) return;
            _kurvenLinienkante = null;
            try
            {
                _kurvenLinienkante = LeseKurvenfangkante(_kurvenFangstrasse);
                _kurvenLinienkurve = Kurvenform(kurve);
                _kurvenLinieIstKnoten = knoten;
            }
            catch (Exception e) { Kurvenfehler(e); }
        }

        private void BeginKurvenfanglinie(SnapLine linie)
        {
            if (!_kurvenSammeln) return;
            try
            {
                _kurvenLinienGrund = "keine Strasse";
                _kurvenLinienanker = _kurvenLinienkante == null ? default : Strassenecke.Anker(_kurvenLinienkante,
                    _kurvenLinienkurve, _kurvenLinieIstKnoten, linie.Position, linie.Direction, out _kurvenLinienGrund);
            }
            catch (Exception e) { _kurvenLinienanker = default; _kurvenLinienGrund = "Fehler"; Kurvenfehler(e); }
        }

        private void BeginKurveneckenschnitt(float2 prioritaet) { }

        private void MerkeKurveneckenschnitt(int index, SnapLine linie, SnapLine andere,
            float3 position, SnapCandidate best)
        {
            if (!_kurvenSammeln || index >= _kurvenLinien.Count) return;
            try
            {
                if (Strassenecke.Bestaetige(position, _kurvenLinienanker, _kurvenLinien[index],
                    _kurvenFangnetz, out var ecke))
                {
                    if (!_kurvenEcken.Contains(ecke)) _kurvenEcken.Add(ecke);
                }
                else MeldeEckenschnitt(_kurvenLinienanker, _kurvenLinienGrund,
                    _kurvenLinien[index], index < _kurvenLinienGruende.Count ? _kurvenLinienGruende[index] : null);
            }
            catch (Exception e) { Kurvenfehler(e); }
        }

        private string _eckenschnittLog;

        // Einmal je Zustand: welche Bordsteinlinien sich schnitten und warum keine Ecke daraus wurde.
        private void MeldeEckenschnitt(Strassenecke.Linienanker a, string warumA, Strassenecke.Linienanker b, string warumB)
        {
            if (!a.Eins.Vorhanden && !b.Eins.Vorhanden) return;
            // Eine Hilfslinie (Achse, frueherer Punkt) ist nie eine Ecke - das ist kein Befund.
            if ((!a.Eins.Vorhanden && warumA == "keine Strasse") || (!b.Eins.Vorhanden && warumB == "keine Strasse")) return;
            string Anker(Strassenecke.Linienanker l, string warum) => !l.Eins.Vorhanden ? "keiner (" + (warum ?? "?") + ")"
                : $"{KurvenEntity(l.Eins.Kante)}/{l.Eins.Seite}/{l.Eins.Teil}@{l.Eins.T:F0}"
                  + (l.Zwei.Vorhanden ? $"+{KurvenEntity(l.Zwei.Kante)}/{l.Zwei.Seite}/{l.Zwei.Teil}@{l.Zwei.T:F0}" : "");
            var zeile = $"PLT-Kurven-Ecke: Schnitt ohne Ecke, Linie A {Anker(a, warumA)}, Linie B {Anker(b, warumB)}.";
            if (zeile == _eckenschnittLog) return;
            _eckenschnittLog = zeile;
            ParkingLotHintergrundDiagnose.Sicher(() => Mod.log.Info(zeile));
        }

        private Strassenecke KurveneckeBei(float3 position)
        {
            Strassenecke beste = null;
            float abstand = (float)Kurven.Fangabstand;
            foreach (var ecke in _kurvenEcken)
            {
                float d = math.distance(ecke.Punkt.xz, position.xz);
                if (d > abstand) continue;
                if (beste != null && d == abstand && !beste.Equals(ecke)) return null;
                beste = ecke;
                abstand = d;
            }
            return beste;
        }

        private void MerkeKurvenfanglinie(SnapLine linie)
        {
            if (!_kurvenSammeln) return;
            _kurvenLinien.Add(_kurvenLinienanker);
            _kurvenLinienGruende.Add(_kurvenLinienkante == null ? "keine Strasse" : _kurvenLinienGrund);
            _kurvenLinienkante = null;
        }
    }
}
