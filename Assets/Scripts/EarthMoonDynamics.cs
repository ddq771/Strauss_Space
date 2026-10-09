using System;

/// <summary>
/// Coasting flight under Earth's and the Moon's gravity, for the trajectory
/// prediction and rails time warp - the same forces the physics applies
/// (PlanetBody's gravity, MoonBody's pull).
///
/// Frame: Earth-centred, non-rotating, aligned with the Earth-fixed scene
/// at the moment the track was made (so "now" positions convert to the
/// scene directly). Earth is itself pulled toward the Moon, so the Moon's
/// effect on the rocket here is the difference of the two pulls (the tidal
/// term) - as in MoonBody.FixedUpdate.
/// </summary>
public static class EarthMoonDynamics
{
    /// <summary>
    /// The Moon's path over a stretch of time, sampled from the ephemeris
    /// (MoonBody.Position) and interpolated (cubic Hermite), so integrating
    /// thousands of steps doesn't evaluate the series thousands of times.
    /// </summary>
    public sealed class MoonTrack
    {
        private readonly Double3[] positions, velocities;
        private readonly double step;

        public MoonTrack(DateTime start, double rotation, double spanSeconds, double stepSeconds = 600)
        {
            step = Math.Max(1, Math.Min(stepSeconds, spanSeconds / 4));
            var count = (int)Math.Ceiling(spanSeconds / step) + 3;
            positions = new Double3[count];
            velocities = new Double3[count];
            for (var i = 0; i < count; i++) positions[i] = MoonBody.OffsetFromEarth(start.AddSeconds(i * step), rotation);
            for (var i = 0; i < count; i++)
            {
                // Velocity from the ephemeris itself, a second either side.
                var t = start.AddSeconds(i * step);
                velocities[i] = (MoonBody.OffsetFromEarth(t.AddSeconds(1), rotation) - MoonBody.OffsetFromEarth(t.AddSeconds(-1), rotation)) / 2;
            }
            Span = (count - 1) * step;
        }

        public double Span { get; }

        /// <summary>The Moon's centre (m from Earth's) t seconds after the start.</summary>
        public Double3 Position(double t)
        {
            t = Math.Max(0, Math.Min(t, Span));
            var i = Math.Min((int)(t / step), positions.Length - 2);
            var u = (t - i * step) / step;
            var u2 = u * u; var u3 = u2 * u;
            return positions[i] * (2 * u3 - 3 * u2 + 1) + velocities[i] * ((u3 - 2 * u2 + u) * step)
                 + positions[i + 1] * (-2 * u3 + 3 * u2) + velocities[i + 1] * ((u3 - u2) * step);
        }

        public Double3 Velocity(double t)
        {
            const double h = 1;
            return (Position(t + h) - Position(t - h)) / (2 * h);
        }
    }

    public static double MuMoon => MoonBody.GravitationalParameter;

    /// <summary>Gravitational acceleration (m/s²) at r with the Moon at rm.</summary>
    public static Double3 Acceleration(Double3 r, Double3 rm, double muEarth)
    {
        var rl = r.Length;
        var a = r * (-muEarth / (rl * rl * rl));
        var d = rm - r;
        var dl = d.Length; var rml = rm.Length;
        return a + d * (MuMoon / (dl * dl * dl)) - rm * (MuMoon / (rml * rml * rml));
    }

    /// <summary>A step size that resolves the orbit around whichever body is closer in its own terms.</summary>
    public static double SuggestedStep(Double3 r, Double3 v, Double3 rm, Double3 vm, double muEarth, double earthRadius)
    {
        var rl = r.Length;
        var d = (r - rm).Length;
        var earth = .01 * Math.Sqrt(rl * rl * rl / muEarth);
        var moon = .01 * Math.Sqrt(d * d * d / MuMoon);
        var dt = Math.Min(earth, moon);
        // Close to a surface, small enough to catch the crossing cleanly.
        var moonAltitude = d - MoonBody.RadiusMeters;
        var closing = Math.Max(1, (v - vm).Length);
        if (moonAltitude < 200000) dt = Math.Min(dt, Math.Max(.25, moonAltitude / closing * .1));
        return Math.Max(.25, Math.Min(dt, 600));
    }

    /// <summary>One RK4 step of dt seconds from time t.</summary>
    public static void Step(ref Double3 r, ref Double3 v, double t, double dt, MoonTrack moon, double muEarth)
    {
        var m0 = moon.Position(t); var mh = moon.Position(t + dt / 2); var m1 = moon.Position(t + dt);
        var k1r = v; var k1v = Acceleration(r, m0, muEarth);
        var k2r = v + k1v * (dt / 2); var k2v = Acceleration(r + k1r * (dt / 2), mh, muEarth);
        var k3r = v + k2v * (dt / 2); var k3v = Acceleration(r + k2r * (dt / 2), mh, muEarth);
        var k4r = v + k3v * dt; var k4v = Acceleration(r + k3r * dt, m1, muEarth);
        r = r + (k1r + k2r * 2 + k3r * 2 + k4r) * (dt / 6);
        v = v + (k1v + k2v * 2 + k3v * 2 + k4v) * (dt / 6);
    }

    /// <summary>Height (m) above the Moon's mean surface.</summary>
    public static double MoonAltitude(Double3 r, Double3 rm) => (r - rm).Length - MoonBody.RadiusMeters;
}
