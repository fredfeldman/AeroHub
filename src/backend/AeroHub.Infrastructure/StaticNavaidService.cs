using AeroHub.Contracts;
using AeroHub.Core;

namespace AeroHub.Infrastructure;

public sealed class StaticNavaidService : INavaidService
{
    private const double CenterLat = 33.104167;
    private const double CenterLon = -96.708333;
    private const double EarthRadiusMiles = 3958.8;

    private readonly List<RawNavaid> _seedNavaids =
    [
        // DFW Metroplex VORs / VORTACs
        new("CVE", "Cowboy VOR/DME", NavaidType.VorDme, 32.889167, -96.903889, 450, 116.20, "KDFW", "High/Low Enroute VOR/DME"),
        new("DFW", "Maverick VOR/DME", NavaidType.VorDme, 32.870833, -97.040000, 560, 115.70, "KDFW", "High/Low Enroute VOR/DME"),
        new("TTT", "Scurry VORTAC", NavaidType.Vortac, 32.716389, -96.388889, 480, 113.10, "KDAL", "High/Low Enroute VORTAC"),
        new("BYP", "Bonham VORTAC", NavaidType.Vortac, 33.536111, -96.242778, 700, 114.60, null, "High/Low Enroute VORTAC"),
        new("SLR", "Sulphur Springs VORTAC", NavaidType.Vortac, 33.199167, -95.541389, 488, 109.00, "KSLR", "High/Low Enroute VORTAC"),
        new("DUA", "Durant VOR/DME", NavaidType.VorDme, 33.982222, -96.386944, 681, 114.10, "KDUA", "Low Enroute VOR/DME"),
        new("UKW", "Bowie VORTAC", NavaidType.Vortac, 33.535833, -97.821389, 1100, 117.20, "0F2", "High/Low Enroute VORTAC"),
        new("PRX", "Paris VORTAC", NavaidType.Vortac, 33.543889, -95.534722, 540, 113.60, "KPRX", "High/Low Enroute VORTAC"),
        new("TNN", "Acton VORTAC", NavaidType.Vortac, 32.434722, -97.662500, 980, 110.60, null, "High/Low Enroute VORTAC"),
        new("CQY", "Cedar Creek VORTAC", NavaidType.Vortac, 32.228889, -96.220833, 400, 114.80, "F25", "High/Low Enroute VORTAC"),

        // Regional VORs / VORTACs within ~200 miles
        new("ADM", "Ardmore VORTAC", NavaidType.Vortac, 34.211389, -97.168333, 920, 116.70, "KADM", "High/Low Enroute VORTAC"),
        new("TYR", "Tyler VOR/DME", NavaidType.VorDme, 32.355833, -95.340000, 540, 114.20, "KTYR", "High/Low Enroute VOR/DME"),
        new("ACT", "Waco VORTAC", NavaidType.Vortac, 31.662222, -97.269167, 510, 115.30, "KACT", "High/Low Enroute VORTAC"),
        new("SPS", "Wichita Falls VORTAC", NavaidType.Vortac, 33.980833, -98.593611, 1020, 112.70, "KSPS", "High/Low Enroute VORTAC"),
        new("MLC", "McAlester VORTAC", NavaidType.Vortac, 34.849167, -95.918611, 820, 112.00, "KMLC", "High/Low Enroute VORTAC"),
        new("GGG", "Gregg County VORTAC", NavaidType.Vortac, 32.381111, -94.653611, 330, 112.90, "KGGG", "High/Low Enroute VORTAC"),
        new("TXK", "Texarkana VORTAC", NavaidType.Vortac, 33.513333, -94.073889, 390, 116.30, "KTXK", "High/Low Enroute VORTAC"),
        new("OKC", "Will Rogers VORTAC", NavaidType.Vortac, 35.353056, -97.602778, 1280, 114.10, "KOKC", "High/Low Enroute VORTAC"),
        new("BWD", "Brownwood VORTAC", NavaidType.Vortac, 31.798611, -98.983333, 1380, 108.20, "KBWD", "High/Low Enroute VORTAC"),
        new("LFK", "Lufkin VORTAC", NavaidType.Vortac, 31.385000, -94.716667, 310, 112.10, "KLFK", "High/Low Enroute VORTAC"),
        new("EIC", "Belcher VORTAC", NavaidType.Vortac, 32.771944, -93.801667, 190, 117.40, "KSHV", "High/Low Enroute VORTAC"),
        new("CLL", "College Station VORTAC", NavaidType.Vortac, 30.605278, -96.421667, 310, 115.90, "KCLL", "High/Low Enroute VORTAC"),
        new("ABI", "Abilene VORTAC", NavaidType.Vortac, 32.481111, -99.697500, 1800, 113.70, "KABI", "High/Low Enroute VORTAC"),
        new("PWE", "Pioneer VORTAC", NavaidType.Vortac, 35.803889, -97.620278, 1140, 113.20, "KPNC", "High/Low Enroute VORTAC"),
        new("FSM", "Fort Smith VORTAC", NavaidType.Vortac, 35.247222, -94.270000, 430, 110.40, "KFSM", "High/Low Enroute VORTAC"),

        // Local Approach ILS / NDB / Marker Beacons (< 45 miles)
        new("I-TKI", "ILS 18 Localizer", NavaidType.IlsLocalizer, 33.177778, -96.590556, 585, 109.75, "KTKI", "McKinney Runway 18 Approach Guidance"),
        new("TKI", "McKinney NDB", NavaidType.Ndb, 33.178333, -96.586667, 585, 0.332, "KTKI", "Non-Directional Beacon / LOM"),
        new("I-ADS", "ILS 16 Localizer", NavaidType.IlsLocalizer, 32.968611, -96.836389, 644, 110.10, "KADS", "Addison Runway 16 Approach Guidance"),
        new("I-DAL", "ILS 13R Localizer", NavaidType.IlsLocalizer, 32.847222, -96.851667, 487, 111.50, "KDAL", "Dallas Love Field Runway 13R Approach"),
        new("I-RVT", "ILS 31L Localizer", NavaidType.IlsLocalizer, 32.851111, -96.840833, 487, 110.15, "KDAL", "Dallas Love Field Runway 31L Approach"),
        new("I-DFW", "ILS 17L Localizer", NavaidType.IlsLocalizer, 32.896944, -97.027222, 603, 110.30, "KDFW", "DFW Runway 17L Approach Guidance"),
        new("I-EWA", "ILS 35L Localizer", NavaidType.IlsLocalizer, 32.880000, -97.030000, 603, 111.90, "KDFW", "DFW Runway 35L Approach Guidance"),
        new("HQZ", "Mesquite NDB", NavaidType.Ndb, 32.748056, -96.531667, 447, 0.245, "KHQZ", "Non-Directional Beacon"),
        new("FTW", "Meacham NDB", NavaidType.Ndb, 32.818889, -97.355556, 710, 0.365, "KFTW", "Non-Directional Beacon")
    ];

    public IReadOnlyList<NavaidSnapshot> GetNavaids(double? maxDistanceMiles = 200)
    {
        var limit = maxDistanceMiles ?? 200.0;

        return _seedNavaids
            .Select(raw =>
            {
                var distance = CalculateDistanceMiles(CenterLat, CenterLon, raw.Lat, raw.Lon);
                var bearing = CalculateBearingDegrees(CenterLat, CenterLon, raw.Lat, raw.Lon);
                return new NavaidSnapshot(
                    raw.Identifier,
                    raw.Name,
                    raw.Type,
                    raw.Lat,
                    raw.Lon,
                    raw.ElevationFeet,
                    raw.FrequencyMHz,
                    raw.AssociatedAirport,
                    Math.Round(distance, 1),
                    Math.Round(bearing, 1),
                    raw.UsageNotes);
            })
            .Where(item => item.DistanceMilesFromCenter <= limit)
            .OrderBy(item => item.DistanceMilesFromCenter)
            .ToList();
    }

    private static double CalculateDistanceMiles(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return EarthRadiusMiles * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double CalculateBearingDegrees(double lat1, double lon1, double lat2, double lon2)
    {
        var dLon = ToRadians(lon2 - lon1);
        var y = Math.Sin(dLon) * Math.Cos(ToRadians(lat2));
        var x = Math.Cos(ToRadians(lat1)) * Math.Sin(ToRadians(lat2)) -
                Math.Sin(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) * Math.Cos(dLon);
        return (Math.Atan2(y, x) * 180.0 / Math.PI + 360.0) % 360.0;
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;

    private sealed record RawNavaid(
        string Identifier,
        string Name,
        NavaidType Type,
        double Lat,
        double Lon,
        double? ElevationFeet,
        double FrequencyMHz,
        string? AssociatedAirport,
        string? UsageNotes);
}
