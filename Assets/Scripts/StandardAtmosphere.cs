using System;

/// <summary>
/// US Standard Atmosphere 1976: temperature, pressure, density and speed of
/// sound against altitude, layer by layer - temperature falls 6.5 K/km
/// through the troposphere to -56.5 °C at 11 km, holds, then warms through
/// the stratosphere and cools again in the mesosphere; pressure follows
/// from hydrostatic balance within each layer, and density from the ideal
/// gas law at the real local temperature. Above the model's 86 km top the
/// 86 km temperature is held and pressure keeps decaying exponentially.
/// Altitudes are geometric metres above sea level (converted internally to
/// geopotential height, as the standard defines its layers).
/// </summary>
public static class StandardAtmosphere
{
    private const double EarthRadiusKm = 6356.766;     // the standard's r0
    private const double GasConstantAir = 287.053;     // J/(kg·K)
    private const double HeatCapacityRatio = 1.4;
    private const double HydrostaticConstant = 34.163195; // g0·M/R*, K/km

    // Geopotential base height (km), base temperature (K), lapse rate
    // (K/km) and base pressure (Pa) of each layer.
    private static readonly double[] BaseKm = { 0, 11, 20, 32, 47, 51, 71, 84.852 };
    private static readonly double[] BaseK = { 288.15, 216.65, 216.65, 228.65, 270.65, 270.65, 214.65, 186.946 };
    private static readonly double[] Lapse = { -6.5, 0, 1.0, 2.8, 0, -2.8, -2.0, 0 };
    private static readonly double[] BasePa = { 101325, 22632.06, 5474.889, 868.0187, 110.9063, 66.93887, 3.956420, 0.3733836 };

    private static int Layer(double geopotentialKm, out double heightInLayer)
    {
        var layer = BaseKm.Length - 1;
        while (layer > 0 && geopotentialKm < BaseKm[layer]) layer--;
        heightInLayer = geopotentialKm - BaseKm[layer];
        return layer;
    }

    private static double Geopotential(double altitudeMetres)
    {
        var km = Math.Max(0, altitudeMetres) / 1000;
        return EarthRadiusKm * km / (EarthRadiusKm + km);
    }

    public static double TemperatureKelvin(double altitudeMetres)
    {
        var layer = Layer(Geopotential(altitudeMetres), out var dh);
        return BaseK[layer] + Lapse[layer] * dh;
    }

    public static double Pressure(double altitudeMetres)
    {
        var layer = Layer(Geopotential(altitudeMetres), out var dh);
        var t = BaseK[layer] + Lapse[layer] * dh;
        return Lapse[layer] == 0
            ? BasePa[layer] * Math.Exp(-HydrostaticConstant * dh / BaseK[layer])
            : BasePa[layer] * Math.Pow(BaseK[layer] / t, HydrostaticConstant / Lapse[layer]);
    }

    /// <summary>ρ = p / (R·T), kg/m³.</summary>
    public static double Density(double pressure, double temperatureKelvin) =>
        pressure / (GasConstantAir * temperatureKelvin);

    /// <summary>a = √(γ·R·T), m/s - 340 m/s at sea level, 295 m/s at 11 km.</summary>
    public static double SpeedOfSound(double temperatureKelvin) =>
        Math.Sqrt(HeatCapacityRatio * GasConstantAir * temperatureKelvin);
}
