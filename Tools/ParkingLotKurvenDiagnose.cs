using System;
using System.Collections.Generic;
using System.Diagnostics;
using Colossal.Mathematics;
using Game.Simulation;
using ParkingLotTool.Geometry;
using Unity.Entities;
using Unity.Mathematics;
using static ParkingLotTool.Tools.ParkingLotPreviewStyle;
using static ParkingLotTool.Tools.ParkingLotTexte;
using Kurven = ParkingLotTool.Geometry.Strassenkurve;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private struct Kurvenquelle { internal Entity Road; internal int Seite,Teil; internal Bezier4x3 Kurve; }
        private readonly List<Kurvenquelle> _kurvenQuellen=new List<Kurvenquelle>();
        private readonly List<(Entity Road,float3 Punkt,float3 Anfang)> _kurvenKnotenquellen=new List<(Entity,float3,float3)>();
        private Entity _kurvenFangstrasse;
        private readonly Dictionary<float2,Kurven.Fang> _kurvenPunkte=new Dictionary<float2,Kurven.Fang>();
        private readonly Dictionary<float2,Kurven.Grund> _kurvenPunktfehler=new Dictionary<float2,Kurven.Grund>();
        private readonly List<Kurven.Ergebnis> _kurvenFest=new List<Kurven.Ergebnis>();
        private Kurven.Fang _kurvenHover;
        private Kurven.Grund _kurvenFanggrund;
        private float3 _kurvenRoh,_kurvenFangRoh,_kurvenFanglage;
        private bool _kurvenSammeln,_kurvenFangInit,_kurvenAktiv;
        private long _kurvenFangStart;
        private double _kurvenFangMs,_kurvenVersatz;
        private int _kurvenFangRevision=-1,_kurvenFestRevision=-1,_kurvenSettings=-1,_kurvenFangmaske;
        private bool _kurvenFestZu,_kurvenRandstrasse;
        private (Kurven.Fang,Kurven.Fang,int,double,Kurven.Grund) _kurvenPaar;
        private Kurven.Ergebnis _kurvenVorschlag;
        private bool _kurvenPaarInit;
        private string _kurvenMeldung, _kurvenAusnahme;
        private readonly StrassenkurvenLog _kurvenLog=new StrassenkurvenLog();
        private readonly Dictionary<Kurven.Ergebnis,double> _kurvenZeiten=new Dictionary<Kurven.Ergebnis,double>();
        private int _kurvenInnen;
        // Nur mit "Strassenkurve folgen": ohne den Schalter passiert nichts, also auch keine
        // gruene Linie (Nutzer, 19.10.). Die Linie selbst haengt zusaetzlich an Kurvendiagnose.
        private bool KurvendiagnoseAn => Mod.Optionen?.StrassenkurveFolgen==true
            && !_bauarbeiter && !IsEditing;

        private void BeginKurvenfang(float3 raw)
        {
            _kurvenSammeln=KurvendiagnoseAn && (!_kurvenFangInit || !math.all(raw==_kurvenFangRoh)
                || _kurvenFangRevision!=_geometryRevision || _kurvenFangmaske!=(int)GetActualSnap());
            _kurvenRoh=raw;
            if(!_kurvenSammeln)return;
            BeginKurveneckenfang();
            _kurvenFangStart=Stopwatch.GetTimestamp();_kurvenQuellen.Clear();_kurvenKnotenquellen.Clear();_kurvenHover=default;
            _kurvenFanggrund=Kurven.Grund.KeinFang;
        }
        private void MerkeKurvenquelle(Entity road,int seite,int teil,Bezier4x3 curve)
        {
            if(!_kurvenSammeln || !math.all(math.isfinite(curve.a)) || !math.all(math.isfinite(curve.d))
                || math.distancesq(curve.a,curve.d)<.01f)return;
            if(MathUtils.Distance(curve.xz,_kurvenRoh.xz,out var t)>=SnapDistance)return;
            if(math.lengthsq(math.normalizesafe(MathUtils.Tangent(curve,t).xz))<.5f)return;
            _kurvenQuellen.Add(new Kurvenquelle {Road=road,Seite=seite,Teil=teil,Kurve=curve});
        }
        private void MerkeKurvenstrasse(Entity e) { if(_kurvenSammeln)_kurvenFangstrasse=e; }
        private void MerkeKurvenknotenquelle(Line3.Segment linie,float t)
        {
            if(_kurvenSammeln)_kurvenKnotenquellen.Add((_kurvenFangstrasse,MathUtils.Position(linie,t),linie.a));
        }
        private void BestaetigeKurvenfang(float3 position)
        {
            if(!KurvendiagnoseAn)return;
            if(!_kurvenSammeln)
            {
                if(!math.all(position==_kurvenFanglage)){_kurvenHover=default;_kurvenFangInit=false;}
                return;
            }
            try
            {
                _kurvenHover=default;_kurvenFanggrund=LastSnap==SnapKind.Crossing && _kurvenQuellen.Count>0?Kurven.Grund.Tangente:Kurven.Grund.KeinFang;
                var ecke = LastSnap == SnapKind.Crossing ? KurveneckeBei(position) : null;
                if (ecke != null)
                {
                    _kurvenHover = ecke.AlsFang();
                    _kurvenFanggrund = Kurven.Grund.Keiner;
                    return;
                }
                Entity naechste=Entity.Null;double abstand=double.PositiveInfinity;
                foreach(var q in _kurvenQuellen)
                {
                    var dist=MathUtils.Distance(q.Kurve.xz,position.xz,out var t);
                    if(dist<abstand){abstand=dist;naechste=q.Road;}
                    if(dist>Kurven.Fangabstand)continue;
                    var p=MathUtils.Position(q.Kurve,t);
                    if(math.distance(p,position)>Kurven.Fangabstand)continue;
                    var k=LeseKurvenfangkante(q.Road);if(k==null)continue;
                    if(!QuellenSeite(k,q,out var seite) || !Kurven.BestaetigeFang(k,seite,q.Teil,t,position,out var f))continue;
                    if(_kurvenHover.Vorhanden && (_kurvenHover.Kante!=f.Kante || _kurvenHover.Seite!=f.Seite))
                    {_kurvenHover=default;_kurvenFanggrund=Kurven.Grund.Mehrdeutig;return;}
                    _kurvenHover=f;_kurvenFanggrund=Kurven.Grund.Keiner;
                }
                if(!_kurvenHover.Vorhanden && LastSnap==SnapKind.Crossing)
                {
                    _kurvenHover=SchnittAufKurve(position);
                    if(_kurvenHover.Vorhanden)_kurvenFanggrund=Kurven.Grund.Keiner;
                }
                // Auch ein Schnitt (Hilfslinie trifft die begradigte Endkappe einer
                // Sackgasse, Nutzer 20.10.) liegt auf dieser Verlaengerung.
                if(!_kurvenHover.Vorhanden && (LastSnap==SnapKind.RoadEdge || LastSnap==SnapKind.Crossing))
                {
                    _kurvenHover=KnotenVerlaengerung(position,LastSnap==SnapKind.Crossing);
                    if(_kurvenHover.Vorhanden)_kurvenFanggrund=Kurven.Grund.Keiner;
                }
                if(!_kurvenHover.Vorhanden && LastSnap==SnapKind.RoadEdge)
                {
                    foreach(var q in _kurvenKnotenquellen)
                        if(math.distance(q.Punkt,position)<=Kurven.Fangabstand){naechste=q.Road;break;}
                    if(naechste!=Entity.Null)_kurvenFanggrund=KurvenKnotengrund(naechste,position);
                }
            }
            catch(Exception e){_kurvenHover=default;Kurvenfehler(e);}
            finally
            {
                _kurvenFangInit=true;_kurvenFangRoh=_kurvenRoh;_kurvenFanglage=position;
                _kurvenFangRevision=_geometryRevision;_kurvenFangmaske=(int)GetActualSnap();
                _kurvenFangMs=(Stopwatch.GetTimestamp()-_kurvenFangStart)*1000.0/Stopwatch.Frequency;
            }
        }
        /**
         * Hilfslinie trifft Bordstein: der Fang schneidet wie CS2 mit der
         * TANGENTE des Randes, an einer Kurve liegt der Punkt daher knapp
         * neben dem echten Rand. Innerhalb der Pfeilhoehe gilt der
         * Lotfusspunkt auf der Kurve - dieselbe Toleranz wie die Abtastung.
         */
        private Kurven.Fang SchnittAufKurve(float3 position)
        {
            Kurven.Fang fund = default;
            foreach (var q in _kurvenQuellen)
            {
                var dist = MathUtils.Distance(q.Kurve.xz, position.xz, out var t);
                if (dist > Kurven.Pfeilhoehe) continue;
                var k = LeseKurvenfangkante(q.Road);
                if (k == null || !QuellenSeite(k, q, out var seite)
                    || !Kurven.BestaetigeFang(k, seite, q.Teil, t, MathUtils.Position(q.Kurve, t), out var f)) continue;
                if (fund.Vorhanden && (fund.Kante != f.Kante || fund.Seite != f.Seite)) return default;
                fund = f;
            }
            return fund;
        }
        /**
         * Fang auf einem begradigten Knotenstueck (Ecke, Strassenende): das
         * Stueck beginnt am Randende und laeuft auf dessen Tangente weiter.
         * Anker ist genau dieses Randende - eindeutig, sonst kein Fang.
         */
        private Kurven.Fang KnotenVerlaengerung(float3 position, bool schnitt = false)
        {
            Kurven.Fang anker = default;
            foreach (var q in _kurvenKnotenquellen)
            {
                if (schnitt)
                {
                    // Der Schnittpunkt muss AUF dem begradigten Stueck liegen, hinter dessen Anfang.
                    var richtung = math.normalizesafe(q.Punkt.xz - q.Anfang.xz);
                    var weg = position.xz - q.Anfang.xz;
                    if (math.lengthsq(richtung) < .5f || math.dot(weg, richtung) < 0f
                        || math.abs(richtung.x * weg.y - richtung.y * weg.x) > Kurven.Pfeilhoehe) continue;
                }
                else if (math.distance(q.Punkt, position) > Kurven.Fangabstand) continue;
                var k = LeseKurvenfangkante(q.Road);
                if (k == null) continue;
                for (int seite = -1; seite <= 1; seite += 2)
                {
                    var teile = k.Seite(seite);
                    if (teile == null || teile.Length == 0) continue;
                    for (int ende = 0; ende < 2; ende++)
                    {
                        int teil = ende == 0 ? 0 : teile.Length - 1;
                        var p = teile[teil].Kurve.Punkt(ende);
                        if (math.distance(p, q.Anfang) > Kurven.Fangabstand) continue;
                        var f = new Kurven.Fang { Kante = k.Id, Seite = seite, Teil = teil, T = ende, Punkt = p };
                        if (anker.Vorhanden && !anker.Equals(f)) return default;
                        anker = f;
                    }
                }
            }
            var ecke = Strassenecke.Verlaengerung(position, anker);
            return ecke == null ? default : ecke.AlsFang();
        }
        // Knotenhaelften kommen ohne Seite an; sie haengen dort am Randzug, wo ihre Kurve liegt.
        private static bool QuellenSeite(Kurven.Kante k, Kurvenquelle q, out int seite)
        {
            seite = q.Seite;
            if (seite != 0) return true;
            var c = Kurvenform(q.Kurve);
            for (int s = -1; s <= 1; s += 2)
            {
                var teile = k.Seite(s);
                if (teile == null) continue;
                foreach (var teil in teile)
                {
                    if (teil.Quelle != q.Teil) continue;
                    var o = teil.Umgekehrt ? teil.Kurve.Umgekehrt() : teil.Kurve;
                    if (math.distance(o.A, c.A) < .001f && math.distance(o.B, c.B) < .001f
                        && math.distance(o.C, c.C) < .001f && math.distance(o.D, c.D) < .001f)
                    {
                        seite = s;
                        return true;
                    }
                }
            }
            return false;
        }
        private void MerkeKurvenpunkt(float2 p)
        {
            if(KurvendiagnoseAn && _kurvenHover.Vorhanden && math.distance(_kurvenHover.Punkt.xz,p)<=Kurven.Pfeilhoehe)_kurvenPunkte[p]=_kurvenHover;
            else _kurvenPunkte.Remove(p);
            if(KurvendiagnoseAn && !_kurvenHover.Vorhanden && (LastSnap==SnapKind.RoadEdge || (LastSnap==SnapKind.Crossing && _kurvenQuellen.Count>0)))
                _kurvenPunktfehler[p]=_kurvenFanggrund;
            else _kurvenPunktfehler.Remove(p);
        }
        private void VergissKurvendiagnose()
        {
            VergissKurvenform();
            BeginKurveneckenfang();
            _kurvenPunkte.Clear();_kurvenPunktfehler.Clear();_kurvenFest.Clear();_kurvenVorschlag=null;_kurvenPaarInit=false;
            _kurvenFangInit=false;_kurvenFestRevision=-1;_kurvenHover=default;_kurvenLog.Leere();
            _kurvenSettings=-1;_kurvenZeiten.Clear();_kurvenNetz.Clear();_kurvenKnoten.Clear();
            _kurvenAktiv=false;_kurvenMeldung=null;_uiSystem?.SetKurvenhinweis(null);
        }
        private Kurven.Ergebnis VerfolgeKurvenpaar(Kurven.Fang a,Kurven.Fang b,int innen)
        {
            long start=Stopwatch.GetTimestamp();
            LadeKurvennetz(a,b);
            var e=Kurven.Verfolge(a,b,_kurvenNetz,_kurvenKnoten,_kurvenVersatz,_kurvenRandstrasse?innen:0);
            _kurvenZeiten[e]=(Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency;
            return e;
        }
        private int Kurveninnenseite()
        {
            if(!_closed)return 0;
            double sum=0;var o=_points[0];
            for(int i=1;i+1<_points.Count;i++)
            {var a=_points[i]-o;var b=_points[i+1]-o;sum+=(double)a.x*b.y-(double)a.y*b.x;}
            return sum>0?1:sum<0?-1:0;
        }
        private void PflegeKurvendiagnose()
        {
            bool aktiv=KurvendiagnoseAn && _buildStage==BuildStage.Idle && !_speicherLaeuft
                && _points.Count>0 && _dragPoint<0 && _dragEdge<0 && Werkzeugzustand.ZeigtUmrisshilfe(_reiter,AktuellerModus);
            if(!aktiv)
            {
                if(_kurvenAktiv){_uiSystem?.SetKurvenhinweis(null);_kurvenMeldung=null;}
                _kurvenAktiv=false;_kurvenPaarInit=false;return;
            }
            _kurvenAktiv=true;
            if(_kurvenSettings!=_seenSettingsRevision)
            {
                var s=_uiSystem?.CurrentSettings()??LayoutSettings.Cs2;
                _kurvenVersatz=s.Es+s.Sl+s.Ai/2;_kurvenRandstrasse=s.Randstrassen;
                _kurvenSettings=_seenSettingsRevision;_kurvenFestRevision=-1;_kurvenPaarInit=false;
            }
            if(_kurvenFestRevision!=_geometryRevision || _kurvenFestZu!=_closed)
            {
                _kurvenFest.Clear();_kurvenZeiten.Clear();_kurvenFestRevision=_geometryRevision;_kurvenFestZu=_closed;
                _kurvenInnen=Kurveninnenseite();_kurvenPaarInit=false;
                int n=_closed?_points.Count:_points.Count-1;
                for(int i=0;i<n;i++)
                {
                    int j=(i+1)%_points.Count;
                    Kurven.Ergebnis fest=null;
                    if(_kurvenPunkte.TryGetValue(_points[i],out var a) && _kurvenPunkte.TryGetValue(_points[j],out var b))
                        fest=VerfolgeKurvenpaar(a,b,_kurvenInnen);
                    else if(_kurvenPunktfehler.TryGetValue(_points[i],out var fehler) || _kurvenPunktfehler.TryGetValue(_points[j],out fehler))
                        fest=new Kurven.Ergebnis {Grund=fehler};
                    if(fest!=null){_kurvenFest.Add(fest);MeldeKurvenpaar(i,fest);}
                }
            }
            Kurven.Fang anfang=default;
            if(!_closed)_kurvenPunkte.TryGetValue(_points[_points.Count-1],out anfang);
            var ende=_hasHover && !_closed && LastSnap!=SnapKind.None?_kurvenHover:default;
            // Schliessen: der Zeiger rastet am ersten Punkt ein, dessen Fang gilt.
            if(!_closed && CanCloseAtCursor() && _kurvenPunkte.TryGetValue(_points[0],out var ersterFang))ende=ersterFang;
            var fanggrund=LastSnap==SnapKind.None?Kurven.Grund.KeinFang:_kurvenFanggrund;
            var key=(anfang,ende,_kurvenInnen,_kurvenVersatz,fanggrund);
            if(!_kurvenPaarInit || !_kurvenPaar.Equals(key))
            {
                _kurvenPaar=key;_kurvenPaarInit=true;
                if(_kurvenVorschlag!=null)_kurvenZeiten.Remove(_kurvenVorschlag);
                _kurvenVorschlag=anfang.Vorhanden && ende.Vorhanden?VerfolgeKurvenpaar(anfang,ende,_kurvenInnen):null;
                // Schon gefolgter Bordstein (U-Strasse schliessen): die Kante wird gerade, ohne Meldung.
                if(_kurvenVorschlag!=null && _kurvenVorschlag.Grund==Kurven.Grund.Keiner && FolgtSchonKurve(_kurvenVorschlag))
                    _kurvenVorschlag=new Kurven.Ergebnis {Grund=Kurven.Grund.Gerade,Von=anfang,Bis=ende};
                var maus=_kurvenVorschlag;
                if(maus==null && _hasHover && (anfang.Vorhanden || ende.Vorhanden))
                    maus=new Kurven.Ergebnis {Grund=anfang.Vorhanden?fanggrund:Kurven.Grund.KeinFang,Von=anfang,Bis=ende};
                if(maus==null && !_closed && _kurvenPunktfehler.TryGetValue(_points[_points.Count-1],out var punktfehler))
                    maus=new Kurven.Ergebnis {Grund=punktfehler};
                if(maus!=null)MeldeKurvenpaar(-1,maus);
                // Die Statuszeile spricht nur ueber den naechsten Klick. Eine schon
                // gesetzte Kante (etwa eine gerade an einer Kreuzung) blieb sonst
                // dort stehen und ueberdeckte alles Weitere (Nutzer, 19.10.).
                // Fehler gesetzter Kurven meldet KurvenformFehler beim Bau.
                var ergebnis=_kurvenVorschlag??maus;
                var grund=ergebnis==null?Kurven.Grund.KeinFang:ergebnis.Grund;
                string meldung=ergebnis==null?null:Kurvenmeldung(grund);
                if(meldung!=_kurvenMeldung){_kurvenMeldung=meldung;_uiSystem?.SetKurvenhinweis(meldung);}
            }
        }
        private void MeldeKurvenpaar(int quelle,Kurven.Ergebnis e)
        {
            if(!_kurvenLog.Neu(quelle,e))return;
            double ms=_kurvenZeiten.TryGetValue(e,out var zeit)?zeit:0;
            var von=e.Von;var bis=e.Bis;
            string herkunft=quelle<0?"Maus":"Kante "+quelle;
            ParkingLotHintergrundDiagnose.Sicher(()=>Mod.log.Info($"PLT-Kurven: [{herkunft}] {KurvenEntity(von.Kante)}/{von.Seite}/{von.Teil}@{von.T:F3} -> {KurvenEntity(bis.Kante)}/{bis.Seite}/{bis.Teil}@{bis.T:F3}; Abweichung {e.Abweichung:F3} m, Radius {e.Radius:F2} m, Winkel {e.Winkel*180/Math.PI:F1}, Stuecke {e.Stuecke}; Fang {_kurvenFangMs:F3} ms, Verfolgung {ms:F3} ms; {e.Grund}."));
            if(e.Grund==Kurven.Grund.Luecke)
            {
                var m=e.Luecke;
                if(!m.Vorhanden){m.KantenendeZumAngebot=double.PositiveInfinity;m.AngebotZumZiel=double.PositiveInfinity;}
                ParkingLotHintergrundDiagnose.Sicher(()=>Mod.log.Info($"PLT-Kurven-Luecke: [{herkunft}] Knoten {KurvenEntity(m.Knoten)}, Messung {m.Vorhanden}, Angebote {m.Angebote}; Kantenende->bestes Angebot {m.KantenendeZumAngebot:F3} m, Angebot->Ziel {m.AngebotZumZiel:F3} m."));
            }
        }
        private void ZeichneKurvendiagnose(ParkingLotPreviewBuffer buffer)
        {
            try
            {
                PflegeKurvenform();
                ZeichneZiehkurven(buffer);
                ZeichneKantenzugkurven(buffer);
                ZeichneKurvenzug(buffer);
                PflegeKurvendiagnose();if(!_kurvenAktiv || Mod.Optionen?.Kurvendiagnose!=true)return;
                foreach(var e in _kurvenFest)ZeichneKurvenbogen(buffer,e);
                if(_kurvenVorschlag!=null)ZeichneKurvenbogen(buffer,_kurvenVorschlag);
            }
            catch(Exception e){Kurvenfehler(e);}
        }
        private static void ZeichneKurvenbogen(ParkingLotPreviewBuffer buffer,Kurven.Ergebnis e)
        {
            // Was folgt, zeigt schon die Strichlinie entlang der Kurve; hier nur
            // noch die Absage (rot), und ohne Ring an jedem Abtastpunkt - die
            // Kurve ist ein Ganzes, keine Punktreihe (Nutzer, 19.10.).
            if (e.OhneFolgen || e.Folgt) return;
            var farbe=e.Folgt?KurvenJa:KurvenNein;
            if(e.Punkte.Length==0)
            {
                if(e.Von.Vorhanden && math.all(math.isfinite(e.Von.Punkt)))buffer.DrawRing(farbe,e.Von.Punkt,ActivePointDiameter,HandleOutlineWidth);
                if(e.Bis.Vorhanden && math.all(math.isfinite(e.Bis.Punkt)))buffer.DrawRing(farbe,e.Bis.Punkt,ActivePointDiameter,HandleOutlineWidth);
            }
            for(int i=0;i<e.Punkte.Length;i++)
            {
                if(i>0)buffer.DrawLine(farbe,new Line3.Segment(e.Punkte[i-1],e.Punkte[i]),PreviewLineWidth);
            }
        }
        private void Kurvenfehler(Exception e)
        {
            if(_kurvenAusnahme==e.Message)return;
            _kurvenAusnahme=e.Message;
            ParkingLotHintergrundDiagnose.Sicher(()=>Mod.log.Warn("PLT-Kurven: Diagnose ausgelassen: "+e.Message));
        }
        private static string Kurvenmeldung(Kurven.Grund g)
        {
            switch(g)
            {
                case Kurven.Grund.Keiner:return T("kurven.folgt");
                case Kurven.Grund.Gerade:return null;
                case Kurven.Grund.Kreuzung:return T("kurven.kreuzung");
                case Kurven.Grund.Kreisverkehr:return T("kurven.kreisverkehr");
                case Kurven.Grund.Hoehe:return T("kurven.hoehe");
                case Kurven.Grund.Seitenwechsel:return T("kurven.seitenwechsel");
                case Kurven.Grund.Luecke:return T("kurven.luecke");
                case Kurven.Grund.Tangentensprung:return T("kurven.tangentensprung");
                case Kurven.Grund.Mehrdeutig:return T("kurven.mehrdeutig");
                case Kurven.Grund.Budget:return T("kurven.budget");
                case Kurven.Grund.ZuEng:return T("kurven.zuEng");
                case Kurven.Grund.ZuKurz:return T("kurven.zuKurz");
                case Kurven.Grund.Ungueltig:return T("kurven.ungueltig");
                case Kurven.Grund.Nullsehne:return T("kurven.nullsehne");
                case Kurven.Grund.Tangente:return T("kurven.tangente");
                case Kurven.Grund.EckeAndereStrasse:return T("kurven.eckeAndereStrasse");
                case Kurven.Grund.EckenTangente:return T("kurven.eckenTangente");
                default:return T("kurven.keinFang");
            }
        }
    }
}
