using System;
using System.Collections.Generic;

namespace ParkingLotTool.Geometry.Zellen
{
    internal sealed partial class Ringlosplan
    {
        internal static Bandplan EinseitigesBand(double minY, double maxY, double tiefe, double breite)
        {
            var baender = new List<Bandabschnitt>();
            var module = new List<Parkmodulabschnitt>();
            if (maxY - minY + 1e-6 < tiefe + breite)
                baender.Add(new Bandabschnitt(0, minY, maxY, Zellart.Restgruen));
            else
            {
                var anfang = (minY + maxY - tiefe - breite) / 2;
                if (anfang > minY) baender.Add(new Bandabschnitt(0, minY, anfang, Zellart.Restgruen));
                var reihe = new Bandabschnitt(1, anfang, anfang + tiefe, Zellart.Bucht, 0);
                var gasse = new Bandabschnitt(2, reihe.Ende, reihe.Ende + breite, Zellart.Fahrgasse);
                baender.Add(reihe); baender.Add(gasse);
                if (maxY > gasse.Ende) baender.Add(new Bandabschnitt(3, gasse.Ende, maxY, Zellart.Restgruen));
                module.Add(new Parkmodulabschnitt { Id = 0, ErsteReihe = reihe, Fahrgasse = gasse,
                    ZweiteReihe = new Bandabschnitt(4, gasse.Ende, gasse.Ende, Zellart.Bucht, 1) });
            }
            return new Bandplan { Baender = baender, Module = module };
        }
    }
}
