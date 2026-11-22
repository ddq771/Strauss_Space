using System;
using System.Collections.Generic;

/// <summary>
/// Upper-ascent guidance: linear-tangent steering to a circular orbit, the
/// method behind the Saturn V's iterative guidance mode and the Shuttle's
/// powered explicit guidance (simplified to the plane of the orbit).
///
/// The idea: for a rocket pushing in a vacuum, the most fuel-efficient way to
/// reach a given height, vertical speed and horizontal speed is to tilt the
/// thrust so that the tangent of its pitch changes linearly with time. Over
/// one burn that is very nearly the same as letting the vertical share of the
/// thrust, sin γ, change linearly, which is what is used here:
///
///     sin γ(t) = A + B·t        (t = seconds from the last solution)
///
/// so the rest of the climb is fixed by just two numbers, A and B, plus the
/// time left to engine cutoff, T. Those three are chosen so that at cutoff
///   * the height is the target height,
///   * the vertical speed is 0, and
///   * the horizontal speed is circular-orbit speed there,
/// i.e. the rocket is exactly in the circular target orbit.
///
/// How a solution is found (Solve), given the rocket's state now and a list
/// of the burns still to come (Burn - every remaining stage, not only the one
/// burning now, which is what lets it plan for a weak upper stage):
///  1. Predict: fly the plan forward in a quick 2-D simulation (Predict) with
///     the present A, B and T - thrust from the burn list (F / m, the mass
///     falling as the propellant goes), gravity μ/r², and the "centrifugal"
///     v_h²/r that horizontal speed gives back. Along the way it adds up the
///     integrals the two equations below need.
///  2. Correct A and B: the vertical speed and height at cutoff are LINEAR in
///     A and B (the thrust's vertical part is a(t)·(A + B·t)), so with the
///     gravity parts taken from the prediction they are two equations in two
///     unknowns, solved directly:
///         A·L + B·J = 0 − v_z − G_v               (vertical speed → 0)
///         A·S + B·Q = r_target − r − v_z·T − G_r   (height → target)
///     L = ∫a dt and J = ∫a·t dt are the thrust's speed-adding integrals; S =
///     ∫(T − t)·a dt and Q = ∫(T − t)·t·a dt are the same for height (a push
///     at time t moves the rocket for the T − t seconds still to go); G_v and
///     G_r are gravity's net effect on vertical speed and height.
///  3. Correct T: the horizontal condition uses angular momentum h = r·v_h
///     (it only changes by thrust: dh/dt = r·a·cos γ). If the prediction ends
///     short of the target's h = r_t·√(μ/r_t), T is stretched by the missing
///     h ÷ the rate h is gaining at cutoff (a Newton step), and vice versa.
///  4. Repeat until T stops changing (usually 1-3 rounds, since each new
///     solution starts from the last one).
/// It is solved again every second from the real state, so drag, steering
/// lag and the simplifications here keep being corrected. In the last few
/// seconds the equations become touchy (T → 0 divides everything), so A and B
/// are frozen and the caller's normal "in orbit" test ends the burn.
///
/// If no solution exists - the remaining burns can't reach the orbit, or the
/// numbers run away - Valid is false and the caller falls back to its
/// simpler climb law.
///
/// Plain C# (no Unity types) so it can be tested on its own.
/// </summary>
public sealed class AscentGuidance
{
    /// <summary>One stretch of the plan: an unpowered gap (staging) of 'gap'
    /// seconds, then 'duration' seconds at constant thrust (N) and mass flow
    /// (kg/s), starting at 'mass' kg.</summary>
    public struct Burn
    {
        public double gap, thrust, flow, duration, mass;
    }

    // Pitch limits for the solution (sin γ). Slightly below the horizon is
    // allowed (a lofted rocket may need to pitch a little down late on), but
    // never steeply; never steeper than ~72° up.
    public const double SinMin = -.2, SinMax = .95;
    private const double ResolveEvery = 1;    // s between solutions
    private const double FreezeBelow = 8;     // s to go: stop re-solving
    private const int MaxRounds = 8;          // corrections per solution

    public bool Valid { get; private set; }
    public double A { get; private set; }
    public double B { get; private set; }
    /// <summary>Seconds to cutoff at the last solution.</summary>
    public double T { get; private set; }
    /// <summary>Why the last solution failed (for the panel / tests).</summary>
    public string Problem { get; private set; } = "";

    private double solvedAt = double.NaN;     // clock time of the last solution

    /// <summary>Seconds left to cutoff, by the clock 'now'.</summary>
    public double TimeToGo(double now) => double.IsNaN(solvedAt) ? double.NaN : T - (now - solvedAt);

    /// <summary>The commanded sin γ at clock time 'now': the line A + B·t,
    /// t measured from the last solution (but not beyond the planned cutoff
    /// T, so a burn running a little long holds the final pitch rather than
    /// following the line off), kept within the limits.</summary>
    public double SinPitch(double now)
    {
        var s = A + B * Math.Min(now - solvedAt, T);
        return Math.Max(SinMin, Math.Min(SinMax, s));
    }

    /// <summary>True when Update would solve again now: never solved, or the
    /// last solution is ResolveEvery old - unless the ending is frozen (a
    /// valid solution with under FreezeBelow seconds left). Lets the caller
    /// skip building the burn list on the steps in between.</summary>
    public bool Due(double now)
    {
        if (double.IsNaN(solvedAt)) return true;
        var elapsed = now - solvedAt;
        return elapsed >= ResolveEvery && !(Valid && T - elapsed < FreezeBelow);
    }

    /// <summary>
    /// Brings the solution up to date. How: does nothing unless Due (see
    /// there; a failed solution is retried once a second too, not every
    /// step). Otherwise, if the last solution was valid, moves its time
    /// origin to now (A + B·elapsed becomes the new A, T shrinks by
    /// elapsed) and runs Solve from there. Arguments: clock time (s), μ (m³/s²), radius r (m),
    /// vertical speed vz (m/s, up +), horizontal inertial speed vh (m/s,
    /// along the target plane), target radius (m), and the burns to come.
    /// Returns Valid.
    /// </summary>
    public bool Update(double now, double mu, double r, double vz, double vh, double rTarget, IReadOnlyList<Burn> burns)
    {
        if (!Due(now)) return Valid;
        if (!double.IsNaN(solvedAt) && Valid)
        {
            var elapsed = now - solvedAt;
            A += B * elapsed;
            T -= elapsed;
        }
        solvedAt = now;
        Valid = Solve(mu, r, vz, vh, rTarget, burns);
        return Valid;
    }

    /// <summary>Forgets the solution (a new flight).</summary>
    public void Reset()
    {
        Valid = false; A = B = T = 0; solvedAt = double.NaN; Problem = "";
    }

    // The iteration described in the class summary.
    private bool Solve(double mu, double r0, double vz0, double vh0, double rT, IReadOnlyList<Burn> burns)
    {
        var planEnd = 0.0;                    // s until the last burn ends
        foreach (var b in burns) planEnd += b.gap + b.duration;
        if (planEnd <= 0) { Problem = "nothing left to burn"; return false; }
        var hTarget = rT * Math.Sqrt(mu / rT); // angular momentum of the orbit

        // No usable previous solution: start from a gentle climb and a T from
        // the speed still needed - the time the plan takes to deliver it.
        if (!Valid || T <= 0 || double.IsNaN(T))
        {
            A = .3; B = 0;
            var need = Math.Sqrt(mu / rT) - vh0 + Math.Max(0, -vz0) + 200;
            T = TimeForDeltaV(burns, need, planEnd);
        }

        var roundsAtEnd = 0;                  // rounds in a row T hit the plan's end
        var shortAtEnd = false;               // the last round ended there short of orbit
        for (var round = 0; round < MaxRounds; round++)
        {
            var p = Predict(mu, r0, vz0, vh0, A, B, T, burns);

            // Step 2: the two linear equations for A and B (Cramer's rule).
            var det = p.L * p.Q - p.J * p.S;
            if (Math.Abs(det) < 1e-9) { Problem = "no thrust to steer with"; return false; }
            var rhsV = -vz0 - p.Gv;
            var rhsR = rT - r0 - vz0 * T - p.Gr;
            A = (rhsV * p.Q - p.J * rhsR) / det;
            B = (p.L * rhsR - p.S * rhsV) / det;

            // Step 3: stretch or shorten T to close the angular-momentum gap.
            // A Newton step: d(h)/dT at cutoff = r·a·cos γ there.
            var hEnd = p.rEnd * p.vhEnd;
            var rate = p.rEnd * p.aEnd * Math.Sqrt(Math.Max(0, 1 - p.sinEnd * p.sinEnd));
            double dT;
            if (rate > 1e-3) dT = (hTarget - hEnd) / rate;
            else dT = hTarget > hEnd ? 5 : -5;          // ended in a staging gap: nudge out of it
            dT = Math.Max(-.5 * T, Math.Min(Math.Max(60, .5 * T), dT));
            T += dT;
            // Wants more burn than the plan holds: try it at the full length
            // (A and B still improve each round, so an early shortfall may
            // only be the rough starting guess). Only once the steering has
            // been re-solved there and the burn still ends short of orbit
            // (twice running) is the orbit out of reach.
            if (T > planEnd)
            {
                T = planEnd;
                shortAtEnd = hEnd < hTarget * .995;
                if (++roundsAtEnd >= 2 && shortAtEnd) { Problem = "not enough Δv for this orbit"; return false; }
            }
            else { roundsAtEnd = 0; shortAtEnd = false; }
            if (T < 1) { Problem = "already past the cutoff"; return false; }
            if (Math.Abs(dT) < .05) break;
        }
        // Out of rounds while still pinned at the plan's end and short: no orbit.
        if (shortAtEnd) { Problem = "not enough Δv for this orbit"; return false; }
        if (double.IsNaN(A) || double.IsNaN(B) || Math.Abs(A) > 1.5) { Problem = "no sensible steering"; return false; }
        Problem = "";
        return true;
    }

    // Everything one prediction produces: the state at cutoff and the integrals.
    private struct Prediction
    {
        public double rEnd, vzEnd, vhEnd, aEnd, sinEnd;
        public double L, J, S, Q, Gv, Gr;
    }

    /// <summary>
    /// Flies the plan T seconds forward in 2-D (radius r, vertical speed vz,
    /// horizontal speed vh) with sin γ = A + B·t (limited), and returns the
    /// end state and the integrals for Solve. How: N equal steps (~1.5 s,
    /// 50-250 of them), each a midpoint (RK2) step: derivatives at the
    /// start, a half step, derivatives there, the full step with those. The
    /// accelerations are
    ///     dvz/dt = a·sin γ − μ/r² + vh²/r     (thrust up, gravity, "centrifugal")
    ///     dvh/dt = a·cos γ − vz·vh/r          (thrust along; the second term
    ///                                          keeps r·vh honest as r grows)
    ///     dr/dt  = vz
    /// and the integrals are summed at each step's midpoint time tm, with
    /// weight a(tm)·dt: L += a, J += a·tm, S += a·(T − tm), Q += a·tm·(T − tm);
    /// gravity's part g = −μ/r² + vh²/r goes into Gv += g, Gr += g·(T − tm).
    /// </summary>
    private static Prediction Predict(double mu, double r, double vz, double vh, double A, double B, double T,
                                      IReadOnlyList<Burn> burns)
    {
        var p = new Prediction();
        var n = (int)Math.Max(50, Math.Min(250, T / 1.5));
        var dt = T / n;
        for (var i = 0; i < n; i++)
        {
            var t0 = i * dt;
            var tm = t0 + dt / 2;
            // Midpoint state.
            Derivatives(mu, r, vz, vh, Accel(burns, t0), Sin(A + B * t0), out var dr1, out var dvz1, out var dvh1);
            var rm = r + dr1 * dt / 2; var vzm = vz + dvz1 * dt / 2; var vhm = vh + dvh1 * dt / 2;
            var am = Accel(burns, tm); var sm = Sin(A + B * tm);
            Derivatives(mu, rm, vzm, vhm, am, sm, out var dr2, out var dvz2, out var dvh2);
            r += dr2 * dt; vz += dvz2 * dt; vh += dvh2 * dt;

            // Integrals at the midpoint.
            var g = -mu / (rm * rm) + vhm * vhm / rm;
            p.L += am * dt; p.J += am * tm * dt;
            p.S += am * (T - tm) * dt; p.Q += am * tm * (T - tm) * dt;
            p.Gv += g * dt; p.Gr += g * (T - tm) * dt;
        }
        p.rEnd = r; p.vzEnd = vz; p.vhEnd = vh;
        // Thrust and pitch just before cutoff (for the Newton step on T).
        p.aEnd = Accel(burns, Math.Max(0, T - 1e-6));
        p.sinEnd = Sin(A + B * T);
        return p;
    }

    private static double Sin(double s) => Math.Max(SinMin, Math.Min(SinMax, s));

    private static void Derivatives(double mu, double r, double vz, double vh, double a, double s,
                                    out double dr, out double dvz, out double dvh)
    {
        var c = Math.Sqrt(Math.Max(0, 1 - s * s));
        dr = vz;
        dvz = a * s - mu / (r * r) + vh * vh / r;
        dvh = a * c - vz * vh / r;
    }

    /// <summary>
    /// Thrust acceleration (m/s²) t seconds from now, from the plan. How:
    /// walks the burns in order, subtracting each gap and duration from t;
    /// inside a gap it is 0; inside a burn it is thrust ÷ the mass then -
    /// the burn's starting mass less flow × the seconds into the burn. Past
    /// the end of the plan, 0.
    /// </summary>
    public static double Accel(IReadOnlyList<Burn> burns, double t)
    {
        foreach (var b in burns)
        {
            if (t < b.gap) return 0;
            t -= b.gap;
            if (t < b.duration) return b.thrust / Math.Max(1, b.mass - b.flow * t);
            t -= b.duration;
        }
        return 0;
    }

    /// <summary>
    /// Seconds from now until the plan has delivered 'deltaV' m/s (capped at
    /// the plan's end). How: each burn delivers the rocket equation's
    /// v_e·ln(m0/m1) with exhaust speed v_e = thrust/flow; whole burns are
    /// added up until the one in which the total is reached, then that
    /// burn's time is found by inverting the equation: the mass where the
    /// remaining Δv is reached is m0·e^(−Δv/v_e), burnt at 'flow' kg/s.
    /// </summary>
    public static double TimeForDeltaV(IReadOnlyList<Burn> burns, double deltaV, double cap)
    {
        var t = 0.0;
        foreach (var b in burns)
        {
            t += b.gap;
            if (b.flow <= 0 || b.thrust <= 0) { t += b.duration; continue; }
            var ve = b.thrust / b.flow;
            var m1 = b.mass - b.flow * b.duration;
            var dv = ve * Math.Log(b.mass / Math.Max(1, m1));
            if (dv >= deltaV) return Math.Min(cap, t + (b.mass - b.mass * Math.Exp(-deltaV / ve)) / b.flow);
            deltaV -= dv;
            t += b.duration;
        }
        return cap;
    }
}
