using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using UnityEngine;

[RequireComponent(typeof(Rocket))]
public sealed class RocketAssemblyController : MonoBehaviour
{
    // Raised from 7 to fit Starship's full-stack engine count (33 on Super
    // Heavy + 6 on Ship = 39) as a real preset. Sandbox socket-count buttons
    // still stop at 7; presets are the only path to a higher count.
    public const int MaxSockets = 40;

    [Serializable] public class Layout
    {
        public int sockets = 1;
        public string fuelType="RP-1";
        public float fillFraction=1;
        public bool frameInstalled = true;
        public bool gimballed;
        public bool fuelInstalled = true, oxidizerInstalled = true;
        public float fuelCapacity = 100f, oxidizerCapacity = 180f;
        public float fuelDiameter = 3.7f, oxidizerDiameter = 3.7f;
        public Vector2[] angles = new Vector2[MaxSockets];
        // A fresh custom build starts with one F-1 on its single mount, so
        // it can fly as-is (TWR ~2.2 with the default tanks).
        public string[] engineIds = DefaultEngineIds();
        public EngineParameters[] parameters = new EngineParameters[MaxSockets];
    }
    private const string DefaultEngineId="F1";
    private static string[] DefaultEngineIds(){var ids=new string[MaxSockets];ids[0]=DefaultEngineId;return ids;}

    private RocketFlightModel flight;
    // Set by automated editor checks so keystrokes typed elsewhere while
    // the editor has focus can't steer or throttle a test flight.
    public static bool IgnorePilotInput;
    private float commandedThrottle=1;
    // Set by an explicit Space/Shutdown; while set, raising the throttle
    // doesn't relight the engine (see ApplyThrottle).
    private bool pilotShutdown;
    // Seconds to ramp from 0% to 100% throttle while Shift/Ctrl is held.
    private const float ThrottleRampPerSecond=1f;
    private bool showFormulas;
    private int editingSlot=-1;
    private string massInput,thrustInput,ispInput,diameterInput,ratioInput;
    private EngineCatalog catalog;
    private Rocket rocket;
    private AssemblyViewCamera view;
    [SerializeField, HideInInspector] private Transform cluster;
    [SerializeField, HideInInspector] private Layout layout = new Layout();
    // Set by LoadPreset when a real vehicle (RocketPresets) is selected -
    // hides the sandbox editing UI so the fixed configuration cannot be
    // changed piece by piece, per the request that presets be non-editable.
    [SerializeField, HideInInspector] private bool locked;
    [SerializeField, HideInInspector] private string activePresetName;
    // An imported body model already IS the rocket's outer surface - the
    // schematic tank cylinders that normally show through the generated
    // hull would just poke through/duplicate it, so they're hidden (not
    // removed - propellant mass/capacity tracking is unaffected either way).
    [SerializeField, HideInInspector] private string activeBodyModelKey;
    private readonly Stack<string> undo = new Stack<string>();
    private readonly List<Transform> sockets = new List<Transform>();
    [SerializeField, HideInInspector] private Material frameMaterial, fuelMaterial, oxidizerMaterial;
    [SerializeField, HideInInspector] private Transform tankStack;
    private RocketMountFrame mountFrame;
    private int category;
    private bool menuReported;
    private string fuelVolumeText="100", oxidizerVolumeText="180", fuelDiameterText="3.7", oxidizerDiameterText="3.7";
    private float stackHeight;
    private Vector2 panelScroll, assemblyScroll;
    private static RocketAssemblyController active;
    public bool FrameInstalled => layout.frameInstalled;
    public bool Gimballed => layout.gimballed;
    public ProceduralPropellantTank FuelTank { get; private set; }
    public ProceduralPropellantTank OxidizerTank { get; private set; }
    private AssemblySelectable selection;
    private AssemblySelectable.PartKind? selectedKind;
    private readonly List<AssemblySelectable> selectableParts=new List<AssemblySelectable>();
    public AssemblySelectable SelectedPart => selection;
    private int selectedEngine;
    private int selectedSocket;
    private string notice = "Select an engine, then click an attachment point.";
    private Vector2 catalogScroll;
    private float footprint = 3.7f;
    private float lowestEngine;
    public int SocketCount => layout.frameInstalled ? layout.sockets : 0;
    public int InstalledCount
    {
        get { var n=0; for(var i=0;i<SocketCount;i++) if(FindEngine(layout.engineIds[i])!=null)n++; return n; }
    }
    public static bool IsPointerOverPanel()
    {
        if(active==null || !active.enabled)return false;
        var point=new Vector2(Input.mousePosition.x,Screen.height-Input.mousePosition.y);
        return point.y<PanelTop || LeftPanelRect.Contains(point);
    }

    private const float PanelTop=130f;
    private const float PanelWidth=320f;
    private const float PanelInset=24f;
    private static Rect LeftPanelRect
    {
        get
        {
            var safe=Screen.safeArea;
            var top=Mathf.Max(PanelTop,Screen.height-safe.yMax+PanelInset);
            return new Rect(safe.xMin+PanelInset,top,PanelWidth,
                Mathf.Max(100f,Screen.height-top-safe.yMin-PanelInset));
        }
    }
    private static Rect RightPanelRect
    {
        get
        {
            var safe=Screen.safeArea;
            var top=Mathf.Max(PanelTop,Screen.height-safe.yMax+PanelInset);
            return new Rect(safe.xMax-PanelInset-PanelWidth,top,PanelWidth,
                Mathf.Max(100f,Screen.height-top-safe.yMin-PanelInset));
        }
    }

    [NonSerialized] private bool initialized;
    private void OnEnable()
    {
        if(Application.isPlaying)InitializeAssembly();
    }
    private void Start() => InitializeAssembly();
    private void InitializeAssembly()
    {
        if(initialized)return;
        initialized=true;
        active=this;
        catalog=Resources.Load<EngineCatalog>("EngineCatalog");
        rocket=GetComponent<Rocket>();
        view=FindFirstObjectByType<AssemblyViewCamera>();
        var surfaceFrame=GameObject.Find("Kenya Surface Frame");
        if(surfaceFrame!=null)foreach(var collider in surfaceFrame.GetComponentsInChildren<Collider>())collider.contactOffset=.00002f;
        flight=GetComponent<RocketFlightModel>();
        if(flight==null)flight=gameObject.AddComponent<RocketFlightModel>();
        // A scene saved before MaxSockets grew from 7 still has shorter
        // arrays - grow them in place instead of discarding saved data.
        GrowToMaxSockets(ref layout.parameters);
        GrowToMaxSockets(ref layout.engineIds);
        GrowToMaxSockets(ref layout.angles);
        if(catalog==null || catalog.engines==null || catalog.engines.Length==0)
        { notice="Engine catalog not found."; enabled=false; return; }
        if(frameMaterial==null)frameMaterial=new Material(Shader.Find("Standard")) {color=new Color(.24f,.29f,.33f)};
        frameMaterial.SetFloat("_Metallic",.65f);
        if(fuelMaterial==null)fuelMaterial=new Material(frameMaterial){color=new Color(.88f,.65f,.28f)};
        if(oxidizerMaterial==null)oxidizerMaterial=new Material(frameMaterial){color=new Color(.55f,.78f,.88f)};
        // Scenes saved before the default engine existed start with an empty
        // mount - which can't launch at all. Give the starting rocket its F-1.
        if(!locked && InstalledCount==0 && layout.frameInstalled && layout.sockets>=1 && FindEngine(DefaultEngineId)!=null)
            layout.engineIds[0]=DefaultEngineId;
        SyncFields();
        Rebuild();
        Debug.Log("ROCKET_ASSEMBLY_READY: component catalog loaded; fuel and oxidizer tanks built.");
    }

    public EngineParameters GetParameters(int slot)
    {
        if(slot<0 || slot>=SocketCount || FindEngine(layout.engineIds[slot])==null)return null;
        // Unity serializes the parameters array with empty (all-zero) entries
        // rather than nulls, so a mount whose engine wasn't put there through
        // InstallEngine can hold a blank entry - treat it as missing and use
        // the engine's reference data. (User-edited parameters are always
        // valid; SetParameters rejects anything else.)
        if(layout.parameters[slot]==null || !layout.parameters[slot].Valid)
            layout.parameters[slot]=EnginePerformance.Reference(layout.engineIds[slot]);
        return layout.parameters[slot];
    }
    public void SetParameters(int slot,EngineParameters values)
    {
        if(rocket.Launched || GetParameters(slot)==null || values==null || !values.Valid)throw new ArgumentException("Invalid engine parameters");
        Remember();layout.parameters[slot]=values.Copy();layout.parameters[slot].dataStatus="User-defined experiment";editingSlot=-1;Rebuild();
    }
    private void ParameterEditor()
    {
        var p=GetParameters(selectedSocket);if(p==null)return;
        GUILayout.Space(10);GUILayout.Label("BASE PARAMETERS · slot "+(selectedSocket+1));
        if(editingSlot!=selectedSocket)
        {
            editingSlot=selectedSocket;massInput=p.dryMass.ToString(CultureInfo.InvariantCulture);
            thrustInput=(p.vacuumThrust/1000).ToString(CultureInfo.InvariantCulture);ispInput=p.vacuumIsp.ToString(CultureInfo.InvariantCulture);
            diameterInput=p.exitDiameter.ToString(CultureInfo.InvariantCulture);ratioInput=p.mixtureRatio.ToString(CultureInfo.InvariantCulture);
        }
        GUILayout.Label("Dry mass, kg");massInput=GUILayout.TextField(massInput);
        GUILayout.Label("Vacuum thrust, kN");thrustInput=GUILayout.TextField(thrustInput);
        GUILayout.Label("Vacuum specific impulse, s");ispInput=GUILayout.TextField(ispInput);
        GUILayout.Label("Exit diameter per nozzle, m");diameterInput=GUILayout.TextField(diameterInput);
        GUILayout.Label("Mixture ratio O/F");ratioInput=GUILayout.TextField(ratioInput);
        if(GUILayout.Button("Apply engine parameters"))
        {
            var candidate=p.Copy();
            try
            {
                candidate.dryMass=float.Parse(massInput.Replace(',','.'),CultureInfo.InvariantCulture);
                candidate.vacuumThrust=float.Parse(thrustInput.Replace(',','.'),CultureInfo.InvariantCulture)*1000;
                candidate.vacuumIsp=float.Parse(ispInput.Replace(',','.'),CultureInfo.InvariantCulture);
                candidate.exitDiameter=float.Parse(diameterInput.Replace(',','.'),CultureInfo.InvariantCulture);
                candidate.mixtureRatio=float.Parse(ratioInput.Replace(',','.'),CultureInfo.InvariantCulture);
                SetParameters(selectedSocket,candidate);notice="Engine physics updated.";
            }
            catch(Exception){notice="Enter finite positive engine parameters.";}
        }
        var mode=GUILayout.Toolbar(p.optimization=="Vacuum"?1:0,new[]{"Sea-level","Vacuum"});
        if((mode==1)!=(p.optimization=="Vacuum")){var changed=p.Copy();changed.optimization=mode==1?"Vacuum":"Sea-level";SetParameters(selectedSocket,changed);}
        var allow=GUILayout.Toggle(p.allowGimbal,"Allow gimballed mounting");
        if(allow!=p.allowGimbal){var changed=p.Copy();changed.allowGimbal=allow;SetParameters(selectedSocket,changed);}
        GUILayout.Label(p.fuel+" / LOX · "+p.nozzleCount+" nozzle(s)");
        GUILayout.Label("Minimum throttle: "+(p.minimumThrottle*100).ToString("F0")+"%");
        GUILayout.Label(p.dataStatus,new GUIStyle(GUI.skin.label){wordWrap=true});
        GUILayout.Label(p.source,new GUIStyle(GUI.skin.label){wordWrap=true});
        var result=EnginePerformance.Evaluate(p,commandedThrottle,flight.Pressure);
        if(result.valid)
        {
            GUILayout.Label("CALCULATED RESULTS");
            GUILayout.Label("Thrust: "+(result.thrust/1000).ToString("F1")+" kN · Isp: "+result.isp.ToString("F1")+" s");
            GUILayout.Label("Mass flow: "+result.massFlow.ToString("F2")+" kg/s");
            GUILayout.Label("Engine TWR: "+(result.thrust/(p.dryMass*EnginePerformance.G0)).ToString("F2"));
            if(showFormulas)GUILayout.Label("Flow = "+commandedThrottle.ToString("F2")+" × "+p.vacuumThrust.ToString("F0")+" / (9.80665 × "+p.vacuumIsp.ToString("F1")+") = "+result.massFlow.ToString("F2")+" kg/s",new GUIStyle(GUI.skin.label){wordWrap=true});
        }
    }
    private void FlightControls()
    {
        if(flight==null)return;
        if(!rocket.EngineEnabled)flight.Prepare(commandedThrottle);
        GUILayout.Label("FLIGHT CONTROLS");
        if(mountFrame!=null && mountFrame.CanGimbal)
        {
            GUILayout.Label("Engine gimbal deflection (average):",new GUIStyle(GUI.skin.label){wordWrap=true});
            DrawGimbalIndicator("Pitch",flightGimbal.x);
            DrawGimbalIndicator("Yaw",flightGimbal.y);
            GUILayout.Label(flightGimbal.sqrMagnitude<.0001f?"Gimbals centered":"Gimbals deflected");
        }
        else
        {
            GUILayout.Label("This frame is fixed - no pitch/yaw steering. Return to assembly and " +
                "enable \"Allow gimballed mounting\" on the frame to steer.",
                new GUIStyle(GUI.skin.label){wordWrap=true});
        }
        GUILayout.Label("Shift/Ctrl: throttle up/down. Z: full throttle. X: cut throttle. Space: toggle ignition. G: stage. W/S/A/D: pitch/yaw. T: SAS. R: re-entry attitude (the attitude the heat shield was built for). H: heat-zone map.",new GUIStyle(GUI.skin.label){wordWrap=true});
        GUILayout.Label(new GUIContent(sasEnabled?"SAS: on - keys set turn rate, release to hold attitude":"SAS: off - keys deflect the engines directly",
            "The flight computer steers by gimballing the engines (each within its real range and speed). No control with the engines off."));
        var reentryOn=GUILayout.Toggle(reentryAttitude,new GUIContent(" Re-entry attitude (R)","Engines off: RCS turns the vehicle to the attitude its heat shield was built for - the orbiter at 40° angle of attack, the Ship belly-first at 65°, a booster engines-first - and holds it there."));
        if(reentryOn!=reentryAttitude)SetReentryAttitude(reentryOn);
        var heatingView=GetComponent<ReentryHeating>();
        if(heatingView!=null)heatingView.ShowZones=GUILayout.Toggle(heatingView.ShowZones,new GUIContent(" Heat-zone map (H)","Draws each heat-protected area on the vehicle in its own colour (bare structure striped), shading yellow then red as it nears its rating."));
        GUILayout.Label("Throttle: "+(commandedThrottle*100).ToString("F0")+"%");
        var previousThrottle=commandedThrottle;
        commandedThrottle=GUILayout.HorizontalSlider(commandedThrottle,0f,1);
        TimeWarpControls();
        ApplyThrottle(previousThrottle);
        GUILayout.BeginHorizontal();
        if(GUILayout.Button("Shutdown")){rocket.StopEngine();pilotShutdown=true;}
        GUILayout.EndHorizontal();
        if(rocket.Launched && GUILayout.Button("Return to assembly"))
        {
            TimeWarp.Request(1);pilotShutdown=false;rocket.ReturnToAssembly();
            // A preset may have dropped stages: reload it whole.
            if(activePresetIndex>=0){var index=activePresetIndex;locked=false;LoadPreset(index);}
            else Rebuild();
            view?.ShowAssembly();
        }
        GUILayout.Label("Mass: "+(flight.TotalMass/1000).ToString("F2")+" t · TWR: "+flight.TWR.ToString("F2"));
        GUILayout.Label((rocket.EngineEnabled?"Thrust: ":"Available thrust: ")+(flight.Thrust/1000).ToString("F1")+" kN");

        if(flight.HasFlightProgram)
        {
            var program="T+"+RocketFlightModel.Clock(flight.MissionTime);
            if(flight.MecoDone)program+=" · MECO - first stage complete";
            else
            {
                if(flight.ProgramThrottle<.999f)program+=" · throttle limited to "+(flight.ProgramThrottle*100).ToString("F0")+"% (Max Q)";
                if(flight.MecoTime>0)program+=" · MECO in "+RocketFlightModel.Clock(Mathf.Max(0,flight.MecoTime-flight.MissionTime));
            }
            GUILayout.Label(new GUIContent(program,"The real first-stage flight program: throttle schedule, engine shutdowns and main engine cutoff (MECO), timed from liftoff."));
        }
        GUILayout.Label(new GUIContent("Altitude: "+flight.Altitude.ToString("F1")+" m", "Rocket root height above the spherical planet surface."));
        var trajectory=GetComponent<TrajectoryDisplay>();
        if(trajectory!=null && trajectory.HasOrbit)
        {
            GUILayout.Label(new GUIContent("Apoapsis: "+TrajectoryDisplay.Km(trajectory.ApoapsisAltitude)+
                (trajectory.TimeToApoapsis>0?" in "+TrajectoryDisplay.Clock(trajectory.TimeToApoapsis):"")+
                " · Periapsis: "+TrajectoryDisplay.Km(trajectory.PeriapsisAltitude),
                "Predicted coasting path from here (engines off), with drag below 100 km: highest point ahead, and the orbit's lowest point."));
            GUILayout.Label(trajectory.Escaping?"Escape trajectory":
                trajectory.InOrbit?"In orbit · period "+TrajectoryDisplay.Clock(trajectory.OrbitalPeriod):
                trajectory.WillImpact?"Suborbital · impact in "+TrajectoryDisplay.Clock(trajectory.TimeToImpact):"Suborbital");
        }
        GUILayout.Label("Speed: "+flight.Speed.ToString("F1")+" m/s · Mach "+flight.Mach.ToString("F2"));
        ReentryGUI();
        MoonGUI(trajectory);
        GUILayout.Label(new GUIContent("Orbital speed: "+flight.OrbitalSpeed.ToString("F0")+" m/s", "Speed relative to the stars: ground speed plus Earth's rotation (~463 m/s eastward at the equator). ~7,800 m/s holds a low orbit."));
        GUILayout.Label("Pressure: "+(flight.Pressure/1000).ToString("F1")+" kPa · Air: "+(flight.AirTemperature-273.15).ToString("F0")+" °C");
        GUILayout.Label(new GUIContent("Vertical speed: "+flight.VerticalSpeed.ToString("+0.0;-0.0;0.0")+" m/s", "Radial velocity relative to the planet: positive ascending, negative descending."));
        GUILayout.Label("Horizontal speed: "+flight.HorizontalSpeed.ToString("F1")+" m/s");
        GUILayout.Label(new GUIContent("Q: "+(flight.DynamicPressure/1000).ToString("F1")+" kPa · AoA: "+flight.AngleOfAttack.ToString("F1")+"°",
            "Dynamic pressure (½ρv²) - how hard the air pushes - and angle of attack between the vehicle's axis and the airflow. High Q and AoA make the air fight the steering; Max Q is usually ~1 min after liftoff."));
        GUILayout.Label(new GUIContent("Drag: "+(flight.Drag/1000).ToString("F1")+" kN · Cd "+flight.CurrentDragCoefficient.ToString("F2"), "Aerodynamic drag opposing velocity; falls off with altitude as the air thins, and the drag coefficient rises steeply around Mach 1."));
        GUILayout.Label("Fuel: "+flight.FuelRemaining.ToString("F1")+" kg ("+flight.FuelType+")");
        GUILayout.Label("LOX: "+flight.OxidizerRemaining.ToString("F1")+" kg");
        if(flight.SolidBoosterCount>0)
            GUILayout.Label(flight.SolidBoosterCount+" × "+flight.SolidTitle+": "+(flight.SolidPropellant/1000).ToString("F0")+" t propellant"+
                (flight.SolidPropellant<=0?" (burnt out)":flight.SolidBurning?" · "+(flight.SolidThrust/1e6).ToString("F1")+" MN":""));
        GUILayout.Label("Flow: "+flight.FuelFlow.ToString("F2")+" + "+flight.OxidizerFlow.ToString("F2")+" kg/s");
        GUILayout.Label(flight.Status,new GUIStyle(GUI.skin.label){wordWrap=true});
        showFormulas=GUILayout.Toggle(showFormulas,"Show formulas");
        if(showFormulas)GUILayout.Label(FlightFormulas,new GUIStyle(GUI.skin.label){wordWrap=true,richText=true});
        GUILayout.Space(10);
    }

    // The physics acting on the vehicle in flight, as the simulation applies it.
    private const string FlightFormulas=
        "<b>Atmosphere (US Standard Atmosphere 1976)</b>\n"+
        "Altitude h = |r| − R_earth\n"+
        "Geopotential H = r0·h / (r0 + h)\n"+
        "Temperature T = Tb + L·(H − Hb)\n"+
        "Pressure p = pb·(Tb / T)^(g0M / R*L)\n"+
        "   isothermal layer: p = pb·e^(−g0M·ΔH / R*Tb)\n"+
        "Density ρ = p / (287.05·T)\n"+
        "Speed of sound a = √(1.4·287.05·T), Mach = v / a\n\n"+
        "<b>Propulsion</b>\n"+
        "Exit area A = Σ π·D² / 4\n"+
        "Mass flow mdot = throttle·F_vac / (g0·Isp_vac)\n"+
        "Thrust F = throttle·F_vac − p·A\n"+
        "Fuel flow = mdot / (1 + O/F), LOX flow = mdot·(O/F) / (1 + O/F)\n"+
        "Solid motor F = frac(t)·F_peak − p·A, mdot = frac(t)·F_peak / (g0·Isp)\n"+
        "TWR = F / (m·g_local)\n\n"+
        "<b>Mass</b>\n"+
        "dm/dt = −mdot\n"+
        "Centre of mass = Σ mi·xi / Σ mi\n"+
        "Inertia I = m·(3r² + h²) / 12, I_roll = m·r² / 2\n"+
        "Δv = Isp·g0·ln(m0 / m1)\n\n"+
        "<b>Gravity</b>\n"+
        "Earth g = G·M / r²\n"+
        "Moon (tidal) a = μ_M·(r_m − r) / |r_m − r|³ − μ_M·r_m / |r_m|³\n\n"+
        "<b>Rotating Earth</b>\n"+
        "a = −2ω × v − ω × (ω × r), ω = 2π / 86164.09 s\n"+
        "Orbital speed = |v + ω × r|\n\n"+
        "<b>Aerodynamics</b>\n"+
        "Dynamic pressure q = ½·ρ·v²\n"+
        "Drag D = q·[Cd·f(M)·A·cos α + 1.2·A_side·sin²α]\n"+
        "   f(M): 1 below Mach 0.6, 1.9× at Mach 1.05–1.1, 1.0 by Mach 5\n"+
        "Normal force N = q·A·C_Nα·sin α (at the centre of pressure)\n"+
        "Pitch damping τ = −q·A·L²·C_mq·ω / (2v)\n\n"+
        "<b>Thrust vectoring</b>\n"+
        "Gimbal torque τ = L·F·sin θ\n"+
        "SAS: α = ωn²·error − 2ζωn·ω, θ = asin(I·α / (L·F))\n\n"+
        "<b>Aerodynamic heating</b>\n"+
        "Sutton-Graves q = 1.7415e-4·√(ρ / rn)·v³\n"+
        "Skin C·dT/dt = q·cos^1.5 θ − ε·σ·(T^4 − T_air^4)\n\n"+
        "<b>Motion</b>\n"+
        "a = ΣF / m, α = Στ / I";

    private static void DrawGimbalIndicator(string label,float angle)
    {
        GUILayout.Label(label+": "+angle.ToString("+0.0;-0.0;0.0")+"°");
        var rect=GUILayoutUtility.GetRect(80f,22f,GUILayout.ExpandWidth(true));
        if(Event.current.type==EventType.Repaint)
        {
            var color=GUI.color;
            GUI.color=new Color(.4f,.45f,.5f);
            GUI.DrawTexture(new Rect(rect.x+6,rect.center.y-2,rect.width-12,4),Texture2D.whiteTexture);
            GUI.color=Color.white;
            GUI.DrawTexture(new Rect(rect.center.x-1,rect.y+2,2,18),Texture2D.whiteTexture);
            var fraction=Mathf.InverseLerp(-RocketMountFrame.PreviewAngleLimit,RocketMountFrame.PreviewAngleLimit,angle);
            var x=Mathf.Lerp(rect.x+6,rect.xMax-6,fraction);
            GUI.color=Mathf.Abs(angle)<.01f?new Color(.3f,1f,.5f):new Color(1f,.7f,.2f);
            GUI.DrawTexture(new Rect(x-5,rect.y+3,10,16),Texture2D.whiteTexture);
            GUI.color=color;
        }
        GUILayout.BeginHorizontal();GUILayout.Label("−10°");GUILayout.FlexibleSpace();GUILayout.Label("0°");GUILayout.FlexibleSpace();GUILayout.Label("+10°");GUILayout.EndHorizontal();
    }

    private readonly Dictionary<string,EngineCatalog.Entry> modelLessEngines=new Dictionary<string,EngineCatalog.Entry>();
    public EngineCatalog.Entry FindEngine(string id)
    {
        if(catalog==null || catalog.engines==null || string.IsNullOrEmpty(id))return null;
        foreach(var engine in catalog.engines) if(engine.id==id)return engine;
        // Engines a preset's body model carries itself (e.g. the Saturn V's
        // J-2s) need performance data but no catalog model: a model-less
        // entry, never offered in the sandbox engine list.
        if(EnginePerformance.Reference(id)==null)return null;
        if(!modelLessEngines.TryGetValue(id,out var entry))
            modelLessEngines[id]=entry=new EngineCatalog.Entry{id=id,title=id,height=3f,diameter=2f};
        return entry;
    }

    private void Remember()
    { undo.Push(JsonUtility.ToJson(layout)); }

    private static void GrowToMaxSockets<T>(ref T[] array)
    {
        if(array!=null && array.Length==MaxSockets)return;
        var grown=new T[MaxSockets];
        if(array!=null)Array.Copy(array,grown,Mathf.Min(array.Length,MaxSockets));
        array=grown;
    }

    public void SetSocketCount(int count)
    {
        // The sandbox UI only offers 1/3/5/7, but presets (Falcon 9's 9,
        // Starship's 39) need the full range up to MaxSockets.
        if(count<1 || count>MaxSockets)throw new ArgumentOutOfRangeException(nameof(count));
        if(layout.sockets==count)return;
        Remember();
        layout.sockets=count;
        selectedSocket=Mathf.Min(selectedSocket,count-1);
        Rebuild();
        notice="Frame updated. Hidden slots are preserved; add more mounts to restore them.";
    }

    public void InstallEngine(int socket,string id)
    {
        if(socket<0 || socket>=SocketCount || FindEngine(id)==null)throw new ArgumentException("Unknown socket or engine");
        Remember();
        layout.engineIds[socket]=id;
        layout.parameters[socket]=EnginePerformance.Reference(id);
        editingSlot=-1;
        selectedSocket=socket;
        selectedKind=AssemblySelectable.PartKind.Engine;
        Rebuild();
        if(InstalledCount==1){layout.fuelType=GetParameters(socket).fuel;flight.SetFuel(layout.fuelType);}
        notice=FindEngine(id).title+" installed in slot "+(socket+1)+".";
    }

    public void RemoveEngine(int socket)
    {
        if(socket<0 || socket>=SocketCount || FindEngine(layout.engineIds[socket])==null)return;
        selectedSocket=socket;
        Remember(); layout.engineIds[socket]=null; layout.parameters[socket]=null;editingSlot=-1; Rebuild();
        notice="Engine removed. The attachment point is available.";
    }

    public void UndoChange()
    {
        if(undo.Count==0)return;
        layout=JsonUtility.FromJson<Layout>(undo.Pop());
        editingSlot=-1;
        selectedSocket=Mathf.Min(selectedSocket,layout.sockets-1);
        SyncFields(); Rebuild(); notice="Last change undone.";
    }

    public string GetEngineId(int socket) => layout.engineIds[socket];
    public Vector3 GetSocketPosition(int index) => sockets[index].position;

    private readonly struct Ring { public readonly float radius; public readonly int count;
        public Ring(float radius,int count){this.radius=radius;this.count=count;} }

    /// <summary>
    /// Splits `remaining` engines across as many concentric rings as needed
    /// around a centre engine. Each ring's radius is the larger of (a) what
    /// even angular spacing needs for its own population, at the given
    /// engine-to-engine clearance, or (b) enough room past the previous
    /// ring's radius that the two rings' engines cannot overlap radially.
    /// Ring capacity grows by 6 per ring outward (8, 14, 20, ...) - matches
    /// Falcon 9's real 8-engine octaweb ring exactly, and gives large
    /// presets like Starship's 39-engine cluster a plausible multi-ring
    /// spread rather than one absurdly wide ring.
    /// </summary>
    private static List<Ring> PlanRings(int remaining,float spacing)
    {
        var rings=new List<Ring>();
        var previousRadius=0f;
        var ringIndex=0;
        while(remaining>0)
        {
            var capacity=8+6*ringIndex;
            var count=Mathf.Min(remaining,capacity);
            var evenSpacingRadius=spacing/(2f*Mathf.Sin(Mathf.PI/count));
            var radius=Mathf.Max(evenSpacingRadius,previousRadius+spacing);
            rings.Add(new Ring(radius,count));
            previousRadius=radius;
            remaining-=count;
            ringIndex++;
        }
        return rings;
    }

    private static void FindRingSlot(List<Ring> rings,int indexAmongRingEngines,out float radius,out int indexOnRing,out int countOnRing)
    {
        var offset=indexAmongRingEngines;
        foreach(var ring in rings)
        {
            if(offset<ring.count){radius=ring.radius;indexOnRing=offset;countOnRing=ring.count;return;}
            offset-=ring.count;
        }
        // Unreachable as long as callers only index within SocketCount-1.
        radius=rings.Count>0?rings[^1].radius:0f;indexOnRing=0;countOnRing=1;
    }

    private void Rebuild()
    {
        if(cluster!=null) { cluster.gameObject.SetActive(false); Destroy(cluster.gameObject); }
        sockets.Clear();
        selectableParts.Clear();
        selection=null;
        // A preset's body model carries its own engines and produces the
        // thrust itself: no engine models, frame beams or adapter arms are
        // built, only invisible thrust points (still on the gimbal mount, so
        // steering works) at the base of the body. Keeps each stage's thrust
        // with the stage's own geometry for future staging.
        var bodyThrust=!string.IsNullOrEmpty(activeBodyModelKey);
        cluster=new GameObject(bodyThrust?"Body Thrust":"Engine Cluster").transform;
        cluster.SetParent(transform,false);
        cluster.localPosition=Vector3.up*(bodyThrust?rocket.ActiveBaseLocalY:rocket.AssemblyMountLocalY);
        cluster.localScale=Vector3.one*PlanetBody.WorldUnitsPerMeter;
        mountFrame=null;
        var largestDiameter=1.0f;
        lowestEngine=0;
        for(var i=0;i<SocketCount;i++)
        {
            var e=FindEngine(layout.engineIds[i]);
            if(e==null)continue;
            largestDiameter=Mathf.Max(largestDiameter,e.diameter);
            lowestEngine=Mathf.Max(lowestEngine,e.height);
        }
        // Clearance for any permitted gimbal angle, even with unlike engines.
        var spacing=largestDiameter+.25f+(layout.gimballed ? 2*lowestEngine*Mathf.Sin(RocketMountFrame.PreviewAngleLimit*Mathf.Deg2Rad) : 0);
        // sockets==3 keeps its own layout (a triangle, no centre engine) -
        // everything else is a centre engine plus however many concentric
        // rings it takes to fit the rest without any engine overlapping its
        // neighbours (needed once presets go past the 7-socket sandbox cap,
        // e.g. Starship's 39-engine cluster).
        var ringPlan= layout.sockets==3 ? null : PlanRings(layout.sockets-1,spacing);
        var outerRadius = layout.sockets==3 ? spacing/Mathf.Sqrt(3) : (ringPlan.Count>0 ? ringPlan[^1].radius : 0f);
        footprint=Mathf.Max(3.7f,2*outerRadius+largestDiameter);
        if(bodyThrust){lowestEngine=0;footprint=rocket.BodyDiameter;}
        if(layout.frameInstalled && !bodyThrust)Beam("Central Mount",Vector3.zero,new Vector3(0,-.25f,0),1.4f);
        var bodyPositions=bodyThrust?BodyThrustPositions():null;
        for(var i=0;i<SocketCount;i++)
        {
            var p=bodyThrust?Vector3.zero:Vector3.down*.3f;
            var onRing=layout.sockets==3 || i>0;
            if(bodyPositions!=null)p=bodyPositions[i];
            else if(onRing)
            {
                float radius; int indexOnRing, countOnRing;
                if(layout.sockets==3){radius=outerRadius;indexOnRing=i;countOnRing=3;}
                else FindRingSlot(ringPlan,i-1,out radius,out indexOnRing,out countOnRing);
                var angle=indexOnRing*2*Mathf.PI/countOnRing;
                p+=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*radius;
            }
            var socket=new GameObject("Mount "+(i+1)).transform;
            socket.SetParent(cluster,false); socket.localPosition=p;
            sockets.Add(socket);
            if(bodyThrust)continue;
            Beam("Adapter Arm",new Vector3(0,-.15f,0),p,.15f);
            Beam("Socket Plate",p+Vector3.up*.08f,p,.5f);
            var entry=FindEngine(layout.engineIds[i]);
            if(entry!=null)
            {
                var instance=Instantiate(entry.prefab,socket,false);
                instance.name=entry.title;
                instance.transform.localPosition=Vector3.zero;
                instance.transform.localRotation=Quaternion.identity;
                instance.transform.localScale=Vector3.one;
                AddSelectable(instance,AssemblySelectable.PartKind.Engine,i,instance.GetComponentsInChildren<Renderer>());
            }
        }
        if(layout.frameInstalled)
        {
            var frameSurfaces=new List<Renderer>();
            foreach(Transform child in cluster)
            {
                var surface=child.GetComponent<Renderer>();if(surface!=null)frameSurfaces.Add(surface);
            }
            if(!bodyThrust)AddSelectable(cluster.gameObject,AssemblySelectable.PartKind.Frame,0,frameSurfaces.ToArray());
            mountFrame=cluster.gameObject.AddComponent<RocketMountFrame>();
            mountFrame.Configure(layout.gimballed,sockets.ToArray());
            for(var i=0;i<SocketCount;i++)mountFrame.SetAngle(i,GetParameters(i)!=null && !GetParameters(i).allowGimbal?Vector2.zero:layout.angles[i]);
        }
        RebuildTanks();
        if(selectedKind.HasValue)
            foreach(var part in selectableParts)
                if(part.Kind==selectedKind.Value && (part.Kind!=AssemblySelectable.PartKind.Engine || part.Slot==selectedSocket))
                {selection=part;selection.Highlight(true);break;}
        if(selection==null)selectedKind=null;
        HideLegacy();
        Dock();
        flight?.Configure(layout.fuelType,layout.fillFraction);
        flight?.AssemblyChanged();
    }

    // Thrust points for a preset body, laid out symmetrically so thrust
    // passes through the axis and steering doesn't twist the vehicle:
    // when only some engines gimbal (the Ship: 3 sea-level Raptors steer,
    // 3 Raptor Vacuums are fixed) the steering engines take an inner ring
    // and the fixed ones an outer ring between them, as on the real
    // vehicle; a pair sits either side of the axis (the Shuttle's OMS).
    // Uniform sets keep the centre + rings layout (null).
    private Vector3[] BodyThrustPositions()
    {
        var n=SocketCount;
        var result=new Vector3[n];
        var steering=new List<int>();var fixedEngines=new List<int>();
        for(var i=0;i<n;i++)
        {
            var e=GetParameters(i);
            if(e!=null && e.allowGimbal && e.gimbalRange>0)steering.Add(i);else fixedEngines.Add(i);
        }
        var diameter=Mathf.Max(1f,rocket.BodyDiameter);
        void Ring(List<int> group,float radius,float phase)
        {
            if(group.Count==1){result[group[0]]=Vector3.zero;return;}
            for(var k=0;k<group.Count;k++)
            {
                var a=phase+k*2*Mathf.PI/group.Count;
                result[group[k]]=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*radius;
            }
        }
        if(steering.Count>0 && fixedEngines.Count>0)
        {
            Ring(steering,diameter*.16f,0);
            Ring(fixedEngines,diameter*.34f,Mathf.PI/Mathf.Max(1,fixedEngines.Count));
            return result;
        }
        if(n==2){var all=new List<int>{0,1};Ring(all,diameter*.3f,0);return result;}
        return null;   // uniform set: the usual centre + rings layout
    }

    private void Beam(string name,Vector3 a,Vector3 b,float width)
    {
        var obj=GameObject.CreatePrimitive(PrimitiveType.Cube);
        obj.name=name;
        obj.transform.SetParent(cluster,false);
        obj.transform.localPosition=(a+b)*.5f;
        obj.transform.localRotation=(b-a).sqrMagnitude>.00001f ? Quaternion.FromToRotation(Vector3.up,b-a) : Quaternion.identity;
        obj.transform.localScale=new Vector3(width,Mathf.Max(.08f,(b-a).magnitude),width);
        obj.GetComponent<Renderer>().sharedMaterial=frameMaterial;
        Destroy(obj.GetComponent<Collider>());
    }

    private void Dock()
    {
        if(rocket.Launched)return;
        var mount=GameObject.Find("Rocket Assembly Root");
        if(mount==null)return;
        var heightBelowPivot=!string.IsNullOrEmpty(activeBodyModelKey)
            ? -rocket.BodyBaseLocalY // the body's own engine bells rest on the pad
            : -rocket.AssemblyMountLocalY+(.4f+lowestEngine+ (layout.gimballed ? footprint*.1f : 0))*PlanetBody.WorldUnitsPerMeter;
        transform.position=mount.transform.position+mount.transform.up*heightBelowPivot;
        transform.rotation=mount.transform.rotation;
    }

    private void AddSelectable(GameObject obj,AssemblySelectable.PartKind kind,int slot,Renderer[] surfaces)
    {
        var part=obj.AddComponent<AssemblySelectable>();part.Configure(kind,slot,surfaces);selectableParts.Add(part);
    }

    public void SelectPart(AssemblySelectable part)
    {
        GUIUtility.keyboardControl=0;
        if(selection!=null)selection.Highlight(false);
        selection=part;selectedKind=part!=null?(AssemblySelectable.PartKind?)part.Kind:null;
        if(part!=null)
        {
            if(part.Kind==AssemblySelectable.PartKind.Engine)selectedSocket=part.Slot;
            part.Highlight(true);notice="Selected "+part.gameObject.name+". Press Delete to remove.";
        }
    }

    public void SelectEngine(int slot)
    {
        foreach(var part in selectableParts)
            if(part.Kind==AssemblySelectable.PartKind.Engine && part.Slot==slot){SelectPart(part);return;}
        SelectPart(null);
    }

    public void DeleteSelection()
    {
        if(selection==null || rocket.Launched)return;
        var kind=selection.Kind;var slot=selection.Slot;
        SelectPart(null);
        switch(kind)
        {
            case AssemblySelectable.PartKind.Engine:RemoveEngine(slot);break;
            case AssemblySelectable.PartKind.Frame:SetFrame(false,false);break;
            case AssemblySelectable.PartKind.FuelTank:SetTank(false,false,layout.fuelCapacity,layout.fuelDiameter);break;
            case AssemblySelectable.PartKind.OxidizerTank:SetTank(true,false,layout.oxidizerCapacity,layout.oxidizerDiameter);break;
        }
        notice="Component removed. Undo to restore it.";
    }

    private Vector2 flightGimbal;
    // --- Staging ------------------------------------------------------------
    // A staged preset flies its stages in order: when the current stage's
    // burn ends (its programmed cutoff, or running dry) it separates after
    // the next stage's separationDelay - its parts of the body model fall
    // away as a DroppedStage with its mass - and the next stage lights after
    // its ignitionDelay, with its own engines, propellant and flight program,
    // thrusting from its own base. Mid-burn drops (strap-ons, SRBs) fall away
    // at their times. The last stage may be relit after its cutoff.
    private int activePresetIndex=-1;
    private int stageIndex;            // 0 = first stage
    private float separationAt=-1, ignitionAt=-1;
    private bool separated, finalRelightOffered;
    // Operator control: with auto-staging off nothing separates or drops on
    // its own - the operator stages (G or the Stage button), as a flight
    // crew / range would command it. G also stages early with auto on.
    private bool autoStaging=true, stagePromptShown;
    private float stageStartPropellant;
    public bool AutoStaging=>autoStaging;
    private readonly HashSet<int> dropsDone=new HashSet<int>();
    public int StageIndex=>stageIndex;
    public int StageCount=>activePresetIndex>=0 && RocketPresets.All[activePresetIndex].Staged ? RocketPresets.All[activePresetIndex].upperStages.Length+1 : 1;
    [Serializable] private class StageExtentFile { public float height_m; public StageExtent[] stages; }
    [Serializable] private class StageExtent { public string name; public float bottom_m, top_m; }

    private void ResetStaging(int presetIndex)
    {
        activePresetIndex=presetIndex;stageIndex=0;separationAt=ignitionAt=-1;separated=false;finalRelightOffered=false;
        stagePromptShown=false;stageStartPropellant=-1;
        dropsDone.Clear();
        DroppedStage.ClearAll();
        GetComponent<ReentryHeating>()?.ResetHeat();tileAxisStage=-1;reentryAttitude=false;
    }

    public string StageName(int index)
    {
        if(activePresetIndex<0)return null;
        var p=RocketPresets.All[activePresetIndex];
        if(!p.Staged)return null;
        return index==0?(p.firstStageName??"First stage"):p.upperStages[index-1].name;
    }

    private void UpdateStaging()
    {
        if(activePresetIndex<0 || flight.Crashed)return;
        var p=RocketPresets.All[activePresetIndex];
        if(!p.Staged)return;
        var now=flight.MissionTime;

        // Mid-burn drops of the current stage.
        var drops=stageIndex==0?p.firstStageDrops:p.upperStages[stageIndex-1].drops;
        if(drops!=null && !separated)
            for(var i=0;i<drops.Length;i++)
                if(!dropsDone.Contains(i) && flight.StageBurnTime>=drops[i].time)
                {
                    // No fairing fitted: nothing to jettison, and its mass was never on board.
                    if(p.fairingOff && RocketPresets.Preset.IsFairingDrop(drops[i])){dropsDone.Add(i);continue;}
                    if(autoStaging)Drop(drops,i,now);
                    else if(!stagePromptShown){stagePromptShown=true;notice=DropName(drops[i])+" burnt out - press G to stage.";}
                }

        if(stageIndex>=p.upperStages.Length)
        {
            // Last stage: after its programmed cutoff it may be relit (Space).
            if(flight.MecoDone && !finalRelightOffered)
            {
                finalRelightOffered=true;
                flight.AllowRelight();
                notice=StageName(stageIndex)+" cutoff at T+"+RocketFlightModel.Clock(now)+" - press Space to relight.";
            }
            return;
        }
        var next=p.upperStages[stageIndex];
        var spent=flight.MecoDone || flight.FuelRemaining<=0 || flight.OxidizerRemaining<=0;
        if(!separated && separationAt<0 && spent && !rocket.EngineEnabled)
        {
            if(autoStaging)separationAt=now+next.separationDelay;
            else if(!stagePromptShown){stagePromptShown=true;notice=StageName(stageIndex)+" burn complete - press G to separate and light "+next.name+".";}
        }
        if(!separated && separationAt>=0 && now>=separationAt)Separate(p,next,now);
        if(separated && now>=ignitionAt)Ignite(next,now);
    }

    private void Drop(RocketPresets.StageDrop[] drops,int i,float now)
    {
        dropsDone.Add(i);
        Detach(drops[i].modelStages,drops[i].mass+(drops[i].solids?SolidCasingMass():0),"Dropped "+string.Join(", ",drops[i].modelStages));
        if(drops[i].solids)flight.JettisonSolids();
        else flight.ShedDryMass(drops[i].mass);
        stagePromptShown=false;
        notice=DropName(drops[i])+" separated at T+"+RocketFlightModel.Clock(now)+".";
        Banner(DropName(drops[i]).ToUpperInvariant()+" SEPARATION");
    }

    private static string DropKind(RocketPresets.StageDrop drop)
    {
        var n=string.Join(" ",drop.modelStages);
        if(n.Contains("Fairing"))return "payload fairing";
        if(n.Contains("LAS"))return "launch abort system";
        if(n.Contains("LES"))return "escape tower";
        if(n.Contains("Shroud"))return "nose shroud";
        if(n.Contains("Interstage"))return "interstage ring";
        return "strap-on boosters";
    }

    private static string DropName(RocketPresets.StageDrop drop)=>string.Join(", ",drop.modelStages).Replace("_"," ");

    private RocketPresets.StageDrop[] CurrentDrops(RocketPresets.Preset p)=>stageIndex==0?p.firstStageDrops:p.upperStages[stageIndex-1].drops;

    private int NextDropIndex(RocketPresets.Preset p)
    {
        var drops=CurrentDrops(p);
        if(drops==null || separated)return -1;
        for(var i=0;i<drops.Length;i++)if(!dropsDone.Contains(i))return i;
        return -1;
    }

    /// <summary>What the next press of Stage does, or null if nothing is left to stage.</summary>
    public string NextStageAction
    {
        get
        {
            if(activePresetIndex<0 || !rocket.Launched || flight.Crashed)return null;
            var p=RocketPresets.All[activePresetIndex];
            if(!p.Staged || separated)return null;
            var drop=NextDropIndex(p);
            if(drop>=0)return "Drop "+DropName(CurrentDrops(p)[drop]);
            if(stageIndex>=p.upperStages.Length)return null;
            return "Separate "+StageName(stageIndex)+", light "+p.upperStages[stageIndex].name;
        }
    }

    // The operator's Stage command: the next mid-burn drop (strap-ons,
    // SRBs) if one is left, otherwise cut the current stage's engines,
    // separate it and light the next one after its real ullage / ignition
    // delay. Staging early leaves the unburnt propellant on the spent stage.
    public void StageNow()
    {
        if(NextStageAction==null)return;
        var p=RocketPresets.All[activePresetIndex];
        var now=flight.MissionTime;
        var drop=NextDropIndex(p);
        if(drop>=0){Drop(CurrentDrops(p),drop,now);return;}
        if(rocket.EngineEnabled)rocket.StopEngine();
        stagePromptShown=false;
        Separate(p,p.upperStages[stageIndex],now);
    }

    private void HandleStageKey()
    {
        if(Application.isFocused && !IgnorePilotInput && GUIUtility.keyboardControl==0 && Input.GetKeyDown(KeyCode.G))StageNow();
    }

    // Fraction of the current stage's propellant left (0-1).
    public float StagePropellantFraction
    {
        get
        {
            var left=(float)flight.LiquidPropellant;
            if(stageStartPropellant<0 || left>stageStartPropellant)stageStartPropellant=left;
            return stageStartPropellant>0?left/stageStartPropellant:0f;
        }
    }

    // --- KSP-style staging stack (bottom right of the flight screen) ------
    // One block per stage still on the vehicle, the next one to go at the
    // bottom (as on the real stack): engines, propellant gauge and status.
    // Strap-on / SRB drops show as their own block above the core stage
    // they belong to. A banner calls out each separation and ignition.
    private string stageBanner; private float stageBannerUntil;
    private GUIStyle stackTitle, stackText, bannerStyle;
    private static Texture2D stackTex;

    private void Banner(string text){stageBanner=text;stageBannerUntil=Time.unscaledTime+3.5f;}

    private static void Fill(Rect r,Color c)
    {
        if(stackTex==null){stackTex=new Texture2D(1,1);stackTex.SetPixel(0,0,Color.white);stackTex.Apply();}
        var old=GUI.color;GUI.color=c;GUI.DrawTexture(r,stackTex);GUI.color=old;
    }

    private void StagingStackGUI()
    {
        if(activePresetIndex<0)return;
        var p=RocketPresets.All[activePresetIndex];
        if(!p.Staged)return;
        stackTitle??=new GUIStyle(GUI.skin.label){fontSize=13,fontStyle=FontStyle.Bold};
        stackText??=new GUIStyle(GUI.skin.label){fontSize=11};
        bannerStyle??=new GUIStyle(GUI.skin.label){fontSize=26,fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleCenter};

        // Blocks, top (last stage) to bottom (next event).
        var blocks=new List<(string title,string detail,float fill,int state,bool next)>();
        for(var k=StageCount-1;k>=stageIndex;k--)
        {
            if(k==stageIndex && separated)continue;
            var engines=k==0?null:p.upperStages[k-1].engines;
            var engineText=engines!=null?EngineSummary(engines):(p.engineCount+"× "+p.engineId);
            if(k>stageIndex)
            {
                var spec=p.upperStages[k-1];
                var waiting=k==stageIndex+1 && separated?"igniting in "+Mathf.Max(0,ignitionAt-flight.MissionTime).ToString("F1")+" s":
                    ((spec.fuelMass+spec.oxidizerMass)/1000f).ToString("N0")+" t propellant";
                blocks.Add(("STAGE "+(k+1)+" · "+StageName(k)+"   Δv "+StageDeltaV(k).ToString("N0")+" m/s",engineText+" · "+waiting,1f,k==stageIndex+1&&separated?1:0,false));
            }
            else
            {
                var burn=flight.MecoTime>0&&!flight.MecoDone?" · cutoff in "+RocketFlightModel.Clock(Mathf.Max(0,flight.MecoTime-flight.StageBurnTime)):"";
                blocks.Add(("STAGE "+(k+1)+" · "+StageName(k)+"   Δv "+StageDeltaV(k).ToString("N0")+" m/s",engineText+" · "+(!rocket.Launched?"ready":rocket.EngineEnabled?"BURNING":"off")+burn,
                    StagePropellantFraction,rocket.EngineEnabled?1:2,false));
            }
            if(k==stageIndex)
            {
                var drops=CurrentDrops(p);
                if(drops!=null)
                    for(var i=drops.Length-1;i>=0;i--)
                        if(!dropsDone.Contains(i))
                            blocks.Add(("  ↳ "+DropName(drops[i]),(drops[i].solids?"solid boosters":DropKind(drops[i]))+" · drop at "+RocketFlightModel.Clock(drops[i].time),
                                drops[i].solids?flight.SolidBoosterCount>0?1f:0f:1f,3,false));
            }
        }
        const float w=330,h=46,gap=4;
        var action=NextStageAction;
        var height=blocks.Count*(h+gap)+82;
        var x=Screen.width-w-16;var y=Screen.height-height-16;
        Fill(new Rect(x-6,y-6,w+12,height+12),new Color(0,0,0,.55f));
        stackTitle.normal.textColor=Color.white;
        GUI.Label(new Rect(x,y,w,20),"STAGING"+(autoStaging?" · auto":" · manual")+"     Total Δv "+TotalDeltaV.ToString("N0")+" m/s (vac)",stackTitle);
        y+=24;
        for(var b=0;b<blocks.Count;b++)
        {
            var (title,detail,fill,state,_)=blocks[b];
            var isNext=b==blocks.Count-1;
            var accent=state==1?new Color(.35f,.9f,.45f):state==3?new Color(1f,.7f,.3f):state==2?new Color(.9f,.8f,.3f):new Color(.55f,.6f,.7f);
            var r=new Rect(x,y,w,h);
            Fill(r,new Color(.12f,.14f,.18f,.9f));
            Fill(new Rect(r.x,r.y,4,r.height),accent);
            if(isNext && action!=null && Mathf.Repeat(Time.unscaledTime,1f)<.5f && (!autoStaging || stagePromptShown))
                Fill(new Rect(r.x,r.y,r.width,2),new Color(1f,.85f,.3f));
            stackTitle.normal.textColor=accent;
            GUI.Label(new Rect(r.x+10,r.y+2,w-14,20),title,stackTitle);
            stackText.normal.textColor=new Color(.85f,.87f,.9f);
            GUI.Label(new Rect(r.x+10,r.y+19,w-14,18),detail,stackText);
            Fill(new Rect(r.x+10,r.y+38,w-20,4),new Color(.25f,.27f,.3f));
            Fill(new Rect(r.x+10,r.y+38,(w-20)*Mathf.Clamp01(fill),4),accent*new Color(1,1,1,.9f));
            y+=h+gap;
        }
        GUI.enabled=action!=null;
        if(GUI.Button(new Rect(x,y+2,w,30),action!=null?"STAGE (G) ▸ "+action:"STAGE (G)"))StageNow();
        GUI.enabled=true;
        autoStaging=GUI.Toggle(new Rect(x,y+34,w,20),autoStaging," Auto-staging (real timeline)");

        if(stageBanner!=null && Time.unscaledTime<stageBannerUntil)
        {
            var a=Mathf.Clamp01((stageBannerUntil-Time.unscaledTime)/.8f);
            bannerStyle.normal.textColor=new Color(1f,.85f,.4f,a);
            GUI.Label(new Rect(0,Screen.height*.18f,Screen.width,40),stageBanner,bannerStyle);
        }
    }

    // --- Delta-v ---------------------------------------------------------
    // Tsiolkovsky per stage, Δv = Isp·g0·ln(m0/m1), with vacuum Isp (the
    // thrust-weighted mean of the stage's engines; solids mixed in by
    // propellant mass). The stage burning now uses the live mass and what
    // is left in its tanks; later stages carry everything above them.
    // Mid-burn drops (strap-ons, SRB casings) are left on - slightly
    // conservative, like KSP's readout.
    private const double G0=9.80665;

    private static double StageIsp(string[] engines)
    {
        double thrust=0,flow=0;
        foreach(var id in engines)
        {
            var e=EnginePerformance.Reference(id);
            if(e==null || e.vacuumIsp<=0)continue;
            thrust+=e.vacuumThrust;flow+=e.vacuumThrust/e.vacuumIsp;
        }
        return flow>0?thrust/flow:0;
    }

    private string[] FirstStageEngines(RocketPresets.Preset p)
    {
        var list=new string[Mathf.Max(1,p.engineCount)];
        for(var i=0;i<list.Length;i++)list[i]=p.engineId;
        return list;
    }

    /// <summary>Vacuum Δv (m/s) of stage k from now, or 0 if it's spent.</summary>
    public double StageDeltaV(int k)
    {
        if(activePresetIndex<0)return 0;
        var p=RocketPresets.All[activePresetIndex];
        if(k<stageIndex || (k==stageIndex && separated))return 0;
        if(k==stageIndex)
        {
            var engines=k==0?FirstStageEngines(p):p.upperStages[k-1].engines;
            double liquid=flight.LiquidPropellant,solid=flight.SolidPropellant;
            var isp=StageIsp(engines);
            var solidMotor=EnginePerformance.Solid(p.solidBoosterId);
            if(k==0 && solid>0 && solidMotor!=null)isp=(liquid*isp+solid*solidMotor.vacuumIsp)/Math.Max(1,liquid+solid);
            double m0=flight.TotalMass,m1=m0-liquid-(k==0?solid:0);
            return m1>0&&m0>m1?isp*G0*Math.Log(m0/m1):0;
        }
        // A later stage: everything from it up.
        var spec=p.upperStages[k-1];
        double above=p.payloadMass;
        for(var j=k-1;j<p.upperStages.Length;j++)above+=p.StageDry(j+1)+p.upperStages[j].fuelMass+p.upperStages[j].oxidizerMass;
        var end=above-spec.fuelMass-spec.oxidizerMass;
        return end>0?StageIsp(spec.engines)*G0*Math.Log(above/end):0;
    }

    public double TotalDeltaV
    {
        get{double sum=0;for(var k=0;k<StageCount;k++)sum+=StageDeltaV(k);return sum;}
    }

    // --- Payload ----------------------------------------------------------
    // Cargo on top of the last stage. For a real rocket it can go from 0 up
    // to what that vehicle could lift to low Earth orbit; for a custom build
    // up to what its engines can still lift off with (TWR 1). It is plain
    // mass riding to the end, so it changes everything downstream: liftoff
    // weight and TWR, acceleration, every stage's Δv, and the masses left
    // after each separation.
    private float customPayload;

    private void PayloadControls()
    {
        float current,max;
        if(activePresetIndex>=0)
        {
            var p=RocketPresets.All[activePresetIndex];
            current=p.payloadMass;max=Mathf.Max(p.payloadMax,p.payloadMass);
        }
        else
        {
            current=customPayload;
            // Thrust/g minus everything else at liftoff = the most it can lift.
            max=Mathf.Max(0f,(float)(flight.Thrust/9.80665-(flight.TotalMass-customPayload)));
        }
        GUILayout.Label(new GUIContent("Payload: "+(current/1000).ToString("F1")+" t  (max "+(max/1000).ToString("F1")+" t)",
            activePresetIndex>=0?"The most this vehicle could carry to low Earth orbit.":"The most this assembly can lift off with (thrust-to-weight 1)."));
        if(max<=0)return;
        var chosen=Mathf.Round(GUILayout.HorizontalSlider(current,0f,max)/100f)*100f;
        chosen=Mathf.Min(chosen,max);
        if(Mathf.Abs(chosen-current)>=1f)SetPayload(chosen);
    }

    public void SetPayload(float kilograms)
    {
        if(rocket.Launched)return;
        if(activePresetIndex>=0)
        {
            // Preset is a struct: write back into the array, not a copy.
            ref var p=ref RocketPresets.All[activePresetIndex];
            p.payloadMass=Mathf.Clamp(kilograms,0f,Mathf.Max(p.payloadMax,p.payloadMass));
            flight.SetPresetDryMass(p.LiftoffDryMass);
        }
        else
        {
            customPayload=Mathf.Max(0f,kilograms);
            flight.SetPresetDryMass(customPayload);
        }
    }

    // --- Re-entry ---------------------------------------------------------
    /// <summary>Thermal protection of the stack flying now (the stage burning, or the one about to light).</summary>
    public RocketPresets.HeatProtection CurrentHeatProtection
    {
        get
        {
            if(activePresetIndex<0)return default;
            var p=RocketPresets.All[activePresetIndex];
            var k=separated?stageIndex+1:stageIndex;
            var heat=k==0?p.firstStageHeat:k-1<p.upperStages.Length?p.upperStages[k-1].heat:default;
            heat.windwardLocal=TileAxis();
            return heat;
        }
    }

    // Which side of the vehicle the heat-shield tiles are on, from the body
    // model's tile meshes still attached (rocket-local, across the axis).
    private Vector3 cachedTileAxis;private int tileAxisStage=-1;
    private Vector3 TileAxis()
    {
        var key=stageIndex*2+(separated?1:0);
        if(key==tileAxisStage)return cachedTileAxis;
        tileAxisStage=key;cachedTileAxis=Vector3.back;
        var sum=Vector3.zero;var count=0;
        foreach(var r in rocket.GetComponentsInChildren<Renderer>())
            if(r.name.IndexOf("Tiles",StringComparison.OrdinalIgnoreCase)>=0){sum+=transform.InverseTransformPoint(r.bounds.center);count++;}
        if(count>0)
        {
            var offset=sum/count;offset.y=0;
            if(offset.sqrMagnitude>1e-12f)cachedTileAxis=offset.normalized;
        }
        return cachedTileAxis;
    }

    public void SetReentryAttitude(bool on)
    {
        reentryAttitude=on;holdingAttitude=false;
        notice=on?"Re-entry attitude: turning to "+(CurrentHeatProtection.HasShield?"the attitude the heat shield was built for":"nose-first")+" (RCS, engines off).":"Re-entry attitude off - holding attitude.";
    }

    // The Moon: the predicted encounter, then altitude and speeds over its
    // ground, the touchdown limits on the way down, and lift-off on it.
    private void MoonGUI(TrajectoryDisplay trajectory)
    {
        var moon=MoonBody.Instance;
        if(moon==null)return;
        if(moon.Landed)
        {
            var weight=flight.TotalMass*MoonBody.SurfaceGravity;
            GUILayout.Label(new GUIContent("On the Moon · "+MoonBody.Coordinates(moon.RocketLatitude,moon.RocketLongitude)+
                "\nLunar weight "+(weight/1000).ToString("N1")+" kN: throttle up past it to lift off.",
                "Standing on the Moon's mean surface, carried round with it."),new GUIStyle(GUI.skin.label){wordWrap=true});
            return;
        }
        if(trajectory!=null && !moon.RocketNear)
        {
            if(trajectory.WillImpactMoon)GUILayout.Label("Moon impact in "+TrajectoryDisplay.Clock(trajectory.TimeToMoonImpact));
            else if(trajectory.MoonEncounter && trajectory.TimeToMoonPeriapsis>0)
                GUILayout.Label("Moon encounter · closest "+TrajectoryDisplay.Km(trajectory.MoonPeriapsisAltitude)+" in "+TrajectoryDisplay.Clock(trajectory.TimeToMoonPeriapsis));
        }
        if(!moon.RocketNear)return;
        var style=new GUIStyle(GUI.skin.label){wordWrap=true,richText=true};
        var altitude=moon.RocketAltitude;
        var text="Moon altitude "+(altitude<10000?altitude.ToString("N0")+" m":TrajectoryDisplay.Km(altitude))+
            " · vertical "+moon.RocketVerticalSpeed.ToString("+0.0;-0.0;0.0")+" m/s · horizontal "+moon.RocketHorizontalSpeed.ToString("F1")+" m/s";
        if(trajectory!=null)
        {
            if(trajectory.WillImpactMoon)text+="\nMoon impact in "+TrajectoryDisplay.Clock(trajectory.TimeToMoonImpact);
            else if(trajectory.TimeToMoonPeriapsis>0)text+="\nMoon Pe "+TrajectoryDisplay.Km(trajectory.MoonPeriapsisAltitude)+" in "+TrajectoryDisplay.Clock(trajectory.TimeToMoonPeriapsis);
        }
        if(altitude<5000)
        {
            // Touchdown limits, green when inside them.
            string Check(bool ok,string what)=>"<color="+(ok?"#7dff8a":"#ff6a5a")+">"+what+"</color>";
            var tilt=Vector3.Angle(transform.up,(transform.position-moon.transform.position).normalized);
            text+="\nTouchdown: "+Check(-moon.RocketVerticalSpeed<=MoonBody.SafeVerticalSpeed,"≤"+MoonBody.SafeVerticalSpeed.ToString("F0")+" m/s down")+" · "+
                Check(moon.RocketHorizontalSpeed<=MoonBody.SafeHorizontalSpeed,"≤"+MoonBody.SafeHorizontalSpeed.ToString("F0")+" m/s across")+" · "+
                Check(tilt<=MoonBody.SafeTiltDegrees,"≤"+MoonBody.SafeTiltDegrees.ToString("F0")+"° tilt ("+tilt.ToString("F0")+"°)");
        }
        GUILayout.Label(new GUIContent(text,"Height of the rocket's base above the Moon's mean surface, and its speed relative to the Moon's ground (which turns with it)."),style);
    }

    /// <summary>A Moon landing, lift-off or crash: banner and notice.</summary>
    public void ReportMoon(string banner,string message)
    {
        notice=message;
        Banner(banner);
    }

    public void ReportBurnUp(string reason)
    {
        notice="Burned up on re-entry at T+"+RocketFlightModel.Clock(flight.MissionTime)+": "+reason+".";
        Banner("BURNED UP ON RE-ENTRY");
    }

    private void ReentryGUI()
    {
        var heating=GetComponent<ReentryHeating>();
        if(heating==null)return;
        var heated=heating.HeatFlux>=5000;
        if(!heated && !heating.ShowZones)return;
        var style=new GUIStyle(GUI.skin.label){wordWrap=true,richText=true};
        var text="";
        if(heated)
        {
            text="Re-entry heating: "+(heating.HeatFlux/1000).ToString("N0")+" kW/m²";
            var off=Mathf.Acos(Mathf.Clamp01(heating.AttitudeMatch))*Mathf.Rad2Deg;
            text+=off<10?" · on the entry attitude":" · "+off.ToString("F0")+"° off the entry attitude"+(reentryAttitude?"":" (R)");
        }
        else text="Heat zones (H to hide)";
        // One line per zone, its swatch matching the map (H), coloured as it nears its rating.
        var count=heating.ZoneNames.Length-1;
        for(var z=0;z<=count;z++)
        {
            var share=heating.ZoneLimitC[z]>0?heating.ZoneHottestC[z]/heating.ZoneLimitC[z]:0;
            var tone=share>.85?"#ff5a4d":share>.6?"#ffc04d":"#ffffff";
            text+="\n<color=#"+ColorUtility.ToHtmlStringRGB(ReentryHeating.ZoneColor(z,count))+">"+(z<count?"■":"▨")+"</color> <color="+tone+">"+heating.ZoneNames[z]+": "+
                heating.ZoneHottestC[z].ToString("N0")+" / "+heating.ZoneLimitC[z].ToString("N0")+" °C</color>";
        }
        if(heated && !heating.ShowZones)text+="\nH: show the heat zones on the vehicle";
        GUILayout.Label(new GUIContent(text,"Stagnation-point heat flux (Sutton-Graves), and the hottest point of each protected area and of the bare structure against what it's rated for. Over a rating, the vehicle burns up."),style);
    }

    private static string EngineSummary(string[] engines)
    {
        var counts=new Dictionary<string,int>();
        foreach(var e in engines){counts.TryGetValue(e,out var c);counts[e]=c+1;}
        var parts=new List<string>();
        foreach(var kv in counts)parts.Add((kv.Value>1?kv.Value+"× ":"")+kv.Key);
        return string.Join(" + ",parts);
    }

    private float SolidCasingMass()
    {
        var solid=EnginePerformance.Solid(RocketPresets.All[activePresetIndex].solidBoosterId);
        return solid!=null?(float)(solid.inertMass*flight.SolidBoosterCount):0f;
    }

    private void Separate(RocketPresets.Preset p,RocketPresets.StageSpec next,float now)
    {
        var spentParts=stageIndex==0?p.firstStageModelStages:p.upperStages[stageIndex-1].modelStages;
        var spentDry=p.StageDry(stageIndex);
        Detach(spentParts,spentDry+(float)flight.LiquidPropellant,(StageName(stageIndex)??"Stage")+" (spent)");
        if(flight.SolidBoosterCount>0)flight.JettisonSolids();

        // What's left: the next stage's propellant in the tanks, everything
        // else above it as dry mass.
        var above=p.payloadMass+p.StageDry(stageIndex+1);
        for(var j=stageIndex+1;j<p.upperStages.Length;j++)
            above+=p.StageDry(j+1)+p.upperStages[j].fuelMass+p.upperStages[j].oxidizerMass;
        flight.LoadStagePropellant(next.fuelType,next.fuelMass,next.oxidizerMass);
        flight.SetStageDryMass(above);

        // The stack is now from the next stage's base up.
        var extents=Resources.Load<TextAsset>("RocketBodies/"+activeBodyModelKey+"_stages");
        if(extents!=null)
        {
            var file=JsonUtility.FromJson<StageExtentFile>(extents.text);
            var spentSet=new HashSet<string>();
            for(var k=0;k<=stageIndex;k++)
            {
                var parts=k==0?p.firstStageModelStages:p.upperStages[k-1].modelStages;
                if(parts!=null)foreach(var part in parts)spentSet.Add(part);
            }
            if(p.firstStageDrops!=null)foreach(var d in p.firstStageDrops)foreach(var part in d.modelStages)spentSet.Add(part);
            float bottom=float.MaxValue,top=0;
            foreach(var e in file.stages)
                if(!spentSet.Contains(e.name)){bottom=Mathf.Min(bottom,e.bottom_m);top=Mathf.Max(top,e.top_m);}
            if(bottom<float.MaxValue)rocket.SetActiveStack(bottom,top,next.diameter);
        }

        // The next stage's engines (they light at ignitionAt).
        layout.sockets=next.engines.Length;
        for(var i=0;i<MaxSockets;i++){layout.engineIds[i]=i<next.engines.Length?next.engines[i]:null;layout.parameters[i]=i<next.engines.Length?EnginePerformance.Reference(next.engines[i]):null;gimbalAngles[i]=Vector2.zero;}
        layout.fuelType=next.fuelType;
        layout.fuelCapacity=next.fuelMass/Mathf.Max(1f,flight.FuelDensity);
        layout.oxidizerCapacity=next.oxidizerMass/RocketFlightModel.OxygenDensity;
        layout.fuelDiameter=layout.oxidizerDiameter=next.diameter;
        Rebuild();
        flight.StartStageProgram(next.burnSeconds,null,next.engineCutoffs);
        flight.StopProgramUntilIgnition();
        separated=true;
        ignitionAt=now+next.ignitionDelay;
        dropsDone.Clear();
        notice=(StageName(stageIndex)??"Stage")+" separated at T+"+RocketFlightModel.Clock(now)+".";
        Banner((StageName(stageIndex)??"Stage").ToUpperInvariant()+" SEPARATION");
    }

    private void Ignite(RocketPresets.StageSpec next,float now)
    {
        stageIndex++;
        separated=false;separationAt=ignitionAt=-1;stagePromptShown=false;stageStartPropellant=-1;
        flight.StartStageProgram(next.burnSeconds,null,next.engineCutoffs);
        pilotShutdown=false;
        commandedThrottle=1f;
        rocket.StartEngine(commandedThrottle);
        notice=next.name+" ignition at T+"+RocketFlightModel.Clock(now)+(rocket.EngineEnabled?".":" failed: "+flight.Status);
        Banner(next.name.ToUpperInvariant()+(rocket.EngineEnabled?" IGNITION":" IGNITION FAILED"));
    }

    // Moves named Stage_* groups of the body model into a falling DroppedStage.
    private void Detach(string[] modelStages,float mass,string label)
    {
        if(modelStages==null || modelStages.Length==0)return;
        var root=rocket.transform.Find("Imported Body/"+activeBodyModelKey);
        if(root==null)return;
        var parts=new List<Transform>();
        foreach(var name in modelStages){var t=root.Find("Stage_"+name);if(t!=null)parts.Add(t);}
        if(parts.Count==0)return;
        var body=rocket.GetComponent<Rigidbody>();
        var up=rocket.transform.up;
        var joined=string.Join(" ",modelStages);
        if(joined.Contains("Fairing") || joined.Contains("Shroud"))
        {
            // Payload fairing / shroud: each half is pushed sideways by the
            // separation springs and pyros and swings open about its base
            // hinge, then tumbles away - a clamshell opening, not a drop.
            foreach(var part in parts)
            {
                var outward=PartOutward(part);
                var spin=Vector3.Cross(up,outward)*-.35f;   // top tips outward
                DroppedStage.Create(label,new[]{part},mass/parts.Count,body,outward*4f+up*.5f,spin);
            }
            return;
        }
        if(joined.Contains("LES") || joined.Contains("LAS"))
        {
            // Escape tower: its jettison motor fires it off ahead and to one side.
            DroppedStage.Create(label,parts,mass,body,up*35f+rocket.transform.right*6f,rocket.transform.forward*.2f);
            return;
        }
        // Spent stages, strap-ons, interstage rings: retro-rockets / springs move them away aft.
        DroppedStage.Create(label,parts,mass,body,-up*2f);
    }

    // Hides the body model's fairing / nose shroud parts when the active
    // preset flies without one (its mass and drag are already handled by
    // StageDry and the drag coefficient); shows them otherwise.
    private void ApplyFairingVisibility()
    {
        if(activePresetIndex<0)return;
        var p=RocketPresets.All[activePresetIndex];
        var root=rocket.transform.Find("Imported Body/"+activeBodyModelKey);
        if(root==null)return;
        var stages=new List<RocketPresets.StageDrop[]>{p.firstStageDrops};
        if(p.upperStages!=null)foreach(var s in p.upperStages)stages.Add(s.drops);
        foreach(var drops in stages)
        {
            if(drops==null)continue;
            foreach(var d in drops)
            {
                if(!RocketPresets.Preset.IsFairingDrop(d))continue;
                foreach(var name in d.modelStages){var t=root.Find("Stage_"+name);if(t!=null)t.gameObject.SetActive(!p.fairingOff);}
            }
        }
    }

    // Direction from the vehicle's axis out to a detached part (world), for
    // pushing fairing halves apart; a single-piece shroud goes to one side.
    private Vector3 PartOutward(Transform part)
    {
        var renderers=part.GetComponentsInChildren<Renderer>();
        if(renderers.Length==0)return rocket.transform.right;
        var bounds=renderers[0].bounds;
        foreach(var r in renderers)bounds.Encapsulate(r.bounds);
        var local=rocket.transform.InverseTransformPoint(bounds.center);local.y=0;
        return local.sqrMagnitude>1e-10f?rocket.transform.TransformDirection(local.normalized):rocket.transform.right;
    }

    // Flight control. With SAS (the flight computer, T toggles) W/S and A/D
    // command a pitch / yaw rate; let go and it holds the attitude it had.
    // Either way it works the way real thrust vector control does: each
    // engine swivels only as far as its own gimbal range (Merlin ±5°, Raptor
    // ±15°, F-1 ±6°...) at its actuator's slew rate, so torque - and how
    // fast a heavy vehicle can turn - comes from thrust × lever arm, against
    // whatever the air is doing (RocketFlightModel.ApplyAeroTorques). With
    // the engines off there's no control at all: these vehicles have no
    // attitude thrusters. SAS off = the keys deflect the gimbals directly.
    private bool sasEnabled=true;
    private bool holdingAttitude;
    private Quaternion holdAttitude;
    private readonly Vector2[] gimbalAngles=new Vector2[MaxSockets];
    private const float SasMaxRate=5f;      // deg/s commanded by a held key
    private const float SasNaturalFrequency=.8f, SasDamping=.9f, SasRateGain=1.5f;
    public bool SasEnabled=>sasEnabled;

    // Keys are read every frame; the flight computer itself runs every
    // physics step (FixedUpdate), so at ×10 speed it still corrects ten
    // times as often as the frames - once per frame it lagged far enough
    // behind Starship's big gimbals to go unstable and tip it over.
    private Vector2 pilotInput;

    // --- Autopilot hooks (Autopilot.cs) ----------------------------------
    // With AutopilotSteering set, SAS holds AutopilotAttitude (engines under
    // thrust, RCS while coasting) instead of the attitude it latched itself.
    // How: UpdateFlightGimbals and ApplyAttitudeControl, where SAS would use
    // its own held attitude, copy AutopilotAttitude into holdAttitude each
    // physics step; the error between that and the present rotation then
    // drives the gimbals / RCS exactly as for a pilot's hold.
    [NonSerialized] public bool AutopilotSteering;
    [NonSerialized] public Quaternion AutopilotAttitude=Quaternion.identity;
    // The pilot is pressing a pitch/yaw key (ReadFlightInput's vector isn't zero).
    public bool PilotSteering=>pilotInput.sqrMagnitude>.0001f;
    /// <summary>A stage is separating or waiting to light: leave the engines
    /// alone. How: 'separated' is set from separation until the next stage
    /// ignites; 'separationAt' is set when a spent stage is waiting out its
    /// separation delay (both reset by Ignite).</summary>
    public bool StagingInProgress=>separated || separationAt>=0;
    // Turns SAS on (clearing the held attitude so it re-latches) and the
    // re-entry attitude hold off, so the autopilot's attitude is what's held.
    public void EngageSas(){if(!sasEnabled){sasEnabled=true;holdingAttitude=false;}reentryAttitude=false;}
    // Auto-staging on: UpdateStaging then separates and lights stages on the
    // real timeline by itself.
    public void SetAutoStaging(bool on)=>autoStaging=on;
    // The Launch button's routine (refuses, with a notice, if TWR < 1).
    public void Launch()=>TryLaunch();
    // The last message shown to the pilot (e.g. why a launch was refused).
    public string Notice=>notice;
    // Flying one of RocketPresets.All (a real vehicle), and which one.
    public bool IsPreset=>activePresetIndex>=0;
    public int PresetIndex=>activePresetIndex;
    /// <summary>Sets the throttle as the pilot would, lighting or shutting
    /// down the engines. How: stores it in commandedThrottle (the value the
    /// slider and keys share); at 0 it stops the engines; otherwise it
    /// clears a pilot's "Shutdown" and either changes the running engines'
    /// throttle or lights them (Rocket.StartEngine checks propellant etc.).</summary>
    public void SetAutopilotThrottle(float throttle)
    {
        commandedThrottle=Mathf.Clamp01(throttle);
        if(commandedThrottle<=0){if(rocket.EngineEnabled)rocket.StopEngine();return;}
        pilotShutdown=false;
        if(rocket.EngineEnabled)rocket.SetThrottle(commandedThrottle);
        else rocket.StartEngine(commandedThrottle);
    }
    /// <summary>The lowest throttle every installed engine can run at (1 =
    /// none can throttle). How: the largest minimumThrottle among the
    /// installed engines - below it at least one engine would be outside its
    /// working range and the engine model refuses to run.</summary>
    public float MinimumThrottle
    {
        get
        {
            var min=0f;
            for(var i=0;i<SocketCount;i++){var p=GetParameters(i);if(p!=null)min=Mathf.Max(min,p.minimumThrottle);}
            return min>0?min:1f;
        }
    }
    /// <summary>Vacuum thrust (N) and thrust-weighted vacuum Isp (s) of the
    /// installed engines. How: sums thrust, and sums mass flow as thrust/Isp
    /// for each engine; the combined Isp is total thrust / total (thrust/Isp)
    /// - what one engine giving the same thrust and flow would have.</summary>
    public void InstalledEngines(out double vacuumThrust,out double vacuumIsp)
    {
        double thrust=0,flow=0;
        for(var i=0;i<SocketCount;i++)
        {
            var p=GetParameters(i);
            if(p==null || p.vacuumIsp<=0)continue;
            thrust+=p.vacuumThrust;flow+=p.vacuumThrust/p.vacuumIsp;
        }
        vacuumThrust=thrust;vacuumIsp=flow>0?thrust/flow:0;
    }

    private void ReadFlightInput()
    {
        if(IgnorePilotInput)return;   // tests drive pilotInput themselves
        if(Application.isFocused && !IgnorePilotInput && GUIUtility.keyboardControl==0 && Input.GetKeyDown(KeyCode.T))
        { sasEnabled=!sasEnabled; holdingAttitude=false; }
        if(Application.isFocused && !IgnorePilotInput && GUIUtility.keyboardControl==0 && Input.GetKeyDown(KeyCode.R))
            SetReentryAttitude(!reentryAttitude);
        var input=Vector2.zero;
        if(Application.isFocused && !IgnorePilotInput && GUIUtility.keyboardControl==0)
        {
            input.x=((Input.GetKey(KeyCode.UpArrow)||Input.GetKey(KeyCode.W))?1f:0f)-((Input.GetKey(KeyCode.DownArrow)||Input.GetKey(KeyCode.S))?1f:0f);
            input.y=((Input.GetKey(KeyCode.LeftArrow)||Input.GetKey(KeyCode.A))?1f:0f)-((Input.GetKey(KeyCode.RightArrow)||Input.GetKey(KeyCode.D))?1f:0f);
        }
        pilotInput=Vector2.ClampMagnitude(input,1f);
    }

    private void FixedUpdate()
    {
        if(rocket==null || catalog==null)return;
        // A lifting body (the orbiter) gets its hypersonic lift and wing drag.
        var heat=CurrentHeatProtection;
        flight.SetEntryAerodynamics(heat.ToLocal(RocketPresets.HeatZone.Windward),heat.liftToDrag,heat.planformArea);
        if(rocket.Launched)UpdateFlightGimbals();
    }

    private float rollCommand;            // degrees of differential gimbal
    private const float SasRollRate=1.5f;  // 1/s: roll-rate damping gain

    // δ (degrees) of tangential gimbal that gives roll torque 'torque'.
    private float RollDeflection(float torque,float thrust)
    {
        float leverSum=0,all=0;
        for(var i=0;i<SocketCount;i++)
        {
            var e=GetParameters(i);
            if(e==null)continue;
            all+=e.vacuumThrust;
        }
        if(all<=0)return 0;
        for(var i=0;i<SocketCount;i++)
        {
            var e=GetParameters(i);
            if(e==null || !e.allowGimbal || e.gimbalRange<=0)continue;
            var r=sockets[i].localPosition;
            leverSum+=thrust*e.vacuumThrust/all*new Vector2(r.x,r.z).magnitude*PlanetBody.WorldUnitsPerMeter;
        }
        if(leverSum<=1e-9f)return 0;
        return Mathf.Clamp(torque/leverSum*Mathf.Rad2Deg,-3f,3f);
    }

    private float GimballedThrustFraction()
    {
        float all=0,steering=0;
        for(var i=0;i<SocketCount;i++)
        {
            var e=GetParameters(i);
            if(e==null)continue;
            all+=e.vacuumThrust;
            if(e.allowGimbal && e.gimbalRange>0)steering+=e.vacuumThrust;
        }
        return all>0?Mathf.Max(.05f,steering/all):1f;
    }

    // --- Attitude control with the engines off ---------------------------
    // RCS thrusters (and the Ship's flaps) turn the vehicle while it coasts,
    // within the stage's angular-acceleration limit - the same SAS: keys set
    // a turn rate, release to hold. R (re-entry attitude) points the heat
    // shield into the oncoming air (the base shield = engines first; no
    // shield = nose first) and holds it there.
    private bool reentryAttitude;
    public bool ReentryAttitude=>reentryAttitude;

    public float CurrentRcs
    {
        get
        {
            if(activePresetIndex<0)return .5f;
            var p=RocketPresets.All[activePresetIndex];
            var k=separated?stageIndex+1:stageIndex;
            if(k==0)return p.firstStageRcs;
            var r=k-1<p.upperStages.Length?p.upperStages[k-1].rcsDegPerSec2:0;
            return r>0?r:.6f;
        }
    }

    // Extra control from aerodynamic surfaces, per kPa of dynamic pressure.
    private float CurrentAeroControl
    {
        get
        {
            if(activePresetIndex<0)return 0;
            var p=RocketPresets.All[activePresetIndex];
            var k=separated?stageIndex+1:stageIndex;
            return k==0?p.firstStageAeroControl:k-1<p.upperStages.Length?p.upperStages[k-1].aeroControlPerKPa:0;
        }
    }

    private void ApplyAttitudeControl()
    {
        var body=rocket.GetComponent<Rigidbody>();
        if(body.isKinematic || flight.Crashed || TimeWarp.OnRails)return;
        var powered=(rocket.EngineEnabled || flight.SolidBurning) && flight.Thrust>0;
        var dynamicPressureKPa=(float)(.5*flight.AirDensity*flight.Speed*flight.Speed/1000);
        var limit=(CurrentRcs+CurrentAeroControl*dynamicPressureKPa)*Mathf.Deg2Rad;
        var omega=transform.InverseTransformDirection(body.angularVelocity);
        if(powered)
        {
            // Under thrust the engines steer pitch and yaw, but a single
            // central engine can't stop a roll - real upper stages do that
            // with roll thrusters (the S-IVB's APS, Centaur's RCS, Falcon's
            // cold gas). Damp any spin about the long axis.
            var rcs=Mathf.Max(CurrentRcs,.6f)*Mathf.Deg2Rad;
            var roll=Mathf.Clamp(-omega.y*2f,-rcs,rcs);
            if(Mathf.Abs(omega.y)>1e-5f)body.AddRelativeTorque(Vector3.Scale(body.inertiaTensor,new Vector3(0,roll,0)),ForceMode.Force);
            return;
        }
        if(limit<=0)return;
        var input=pilotInput;
        Vector3 alpha;
        if(input.sqrMagnitude>.0001f)
        {
            holdingAttitude=false;reentryAttitude=false;
            alpha=sasEnabled?(new Vector3(input.x,0,input.y)*SasMaxRate*Mathf.Deg2Rad-omega)*SasRateGain
                            :new Vector3(input.x,0,input.y)*limit;
        }
        else if(sasEnabled || reentryAttitude || AutopilotSteering)
        {
            if(AutopilotSteering){holdAttitude=AutopilotAttitude;holdingAttitude=true;}
            else if(reentryAttitude)
            {
                // The attitude the stage was built to enter at: that side
                // toward the direction of motion (it meets the air first) -
                // the orbiter's 40° angle of attack, the Ship's 65°, a
                // booster engines-first.
                var heat=CurrentHeatProtection;
                var entry=heat.ToLocal(heat.EntryDirection);
                var velocity=flight.GroundVelocity;
                if(velocity.sqrMagnitude>1)
                {
                    var motion=velocity.normalized;
                    // Rolled windward side down, so the belly meets the air
                    // below it and a lifting body's lift points up.
                    var windward=heat.ToLocal(RocketPresets.HeatZone.Windward);
                    var lee=-(windward-Vector3.Dot(windward,entry)*entry);
                    var up=flight.RadialUp;var upAcross=up-Vector3.Dot(up,motion)*motion;
                    holdAttitude=lee.sqrMagnitude>1e-4f && upAcross.sqrMagnitude>1e-4f
                        ? Quaternion.LookRotation(motion,upAcross)*Quaternion.Inverse(Quaternion.LookRotation(entry,lee))
                        : Quaternion.FromToRotation(transform.TransformDirection(entry),motion)*transform.rotation;
                    holdingAttitude=true;
                }
            }
            if(!holdingAttitude){holdAttitude=transform.rotation;holdingAttitude=true;}
            (Quaternion.Inverse(transform.rotation)*holdAttitude).ToAngleAxis(out var angle,out var axis);
            if(angle>180)angle-=360;
            var error=axis*(angle*Mathf.Deg2Rad);
            if(float.IsNaN(error.x))error=Vector3.zero;
            // Stiffness matched to the authority available: a 10° error
            // asks for the full limit (weak RCS in vacuum = gentle, flaps
            // biting in thick air = stiff enough to beat the aero torque).
            var w=Mathf.Clamp(Mathf.Sqrt(limit/.175f),.3f,3f);const float zeta=.9f;
            alpha=error*(w*w)-omega*(2*zeta*w);
        }
        else return;
        alpha=new Vector3(Mathf.Clamp(alpha.x,-limit,limit),Mathf.Clamp(alpha.y,-limit,limit),Mathf.Clamp(alpha.z,-limit,limit));
        body.AddRelativeTorque(Vector3.Scale(body.inertiaTensor,alpha),ForceMode.Force);
    }

    private void UpdateFlightGimbals()
    {
        ApplyAttitudeControl();
        if(mountFrame==null || !mountFrame.CanGimbal)return;
        var input=pilotInput;
        var body=rocket.GetComponent<Rigidbody>();
        var thrust=(float)flight.Thrust*PlanetBody.WorldUnitsPerMeter;   // world force units
        var powered=(rocket.EngineEnabled || flight.SolidBurning) && thrust>0 && !body.isKinematic;

        // Commanded deflection, in degrees: x tilts thrust about the local X
        // axis (pitch), y about local Z (yaw). A deflection of +θ gives a
        // torque of −L·T·sin θ about that axis (thrust acts below the
        // centre of mass), which is why the signs below are negated.
        Vector2 command;
        rollCommand=0;
        if(!powered){ command=Vector2.zero; holdingAttitude=false; }
        else if(sasEnabled)
        {
            var omega=transform.InverseTransformDirection(body.angularVelocity);
            Vector3 alpha;
            if(input.sqrMagnitude>.0001f)
            {
                holdingAttitude=false;
                var rate=new Vector3(input.x,0,input.y)*SasMaxRate*Mathf.Deg2Rad;
                alpha=(rate-omega)*SasRateGain;
            }
            else
            {
                if(AutopilotSteering){holdAttitude=AutopilotAttitude;holdingAttitude=true;}
                if(!holdingAttitude){holdAttitude=transform.rotation;holdingAttitude=true;}
                (Quaternion.Inverse(transform.rotation)*holdAttitude).ToAngleAxis(out var angle,out var axis);
                if(angle>180)angle-=360;
                var error=axis*(angle*Mathf.Deg2Rad);
                if(float.IsNaN(error.x))error=Vector3.zero;
                alpha=error*(SasNaturalFrequency*SasNaturalFrequency)-omega*(2*SasDamping*SasNaturalFrequency);
            }
            var inertia=body.inertiaTensor;
            var lever=Mathf.Max(1e-6f,Vector3.Distance(body.worldCenterOfMass,cluster.position));
            // Only the engines that swivel steer: the Ship's three Raptor
            // Vacuums (and any fixed mount) add thrust but no control
            // torque, so count just the gimballing share.
            var authority=lever*thrust*GimballedThrustFraction();
            command=new Vector2(
                Mathf.Asin(Mathf.Clamp(-inertia.x*alpha.x/authority,-1f,1f))*Mathf.Rad2Deg,
                Mathf.Asin(Mathf.Clamp(-inertia.z*alpha.z/authority,-1f,1f))*Mathf.Rad2Deg);
            // Roll: there are no roll thrusters, so - like the real vehicles -
            // the off-axis engines swivel in opposite directions around the
            // ring (each along its own tangent) to stop any spin.
            rollCommand=RollDeflection(inertia.y*(-omega.y*SasRollRate),thrust);
        }
        else command=-input*RocketMountFrame.PreviewAngleLimit*2;

        // Each engine follows within its own range, at its own slew rate.
        var shown=Vector2.zero;var count=0;
        for(var i=0;i<SocketCount;i++)
        {
            var parameters=GetParameters(i);
            var range=parameters!=null && parameters.allowGimbal?parameters.gimbalRange:0f;
            var rate=parameters!=null?parameters.gimbalRate:0f;
            var target=command;
            if(rollCommand!=0 && range>0)
            {
                // Tangential deflection for engine i at r=(x,z) from the
                // axis: (θ,φ)=-δ·(x,z)/|r| gives roll torque +T·δ·|r|.
                var r=sockets[i].localPosition;var radius=new Vector2(r.x,r.z).magnitude;
                if(radius>.01f)target+=-rollCommand*new Vector2(r.x,r.z)/radius;
            }
            target=Vector2.ClampMagnitude(target,range);
            gimbalAngles[i]=Vector2.MoveTowards(gimbalAngles[i],target,rate*Time.deltaTime);
            mountFrame.SetAngle(i,gimbalAngles[i],range);
            if(range>0){shown+=gimbalAngles[i];count++;}
        }
        // Shown on the flight panel in the old convention (+ = key direction).
        flightGimbal=count>0?-shown/count:Vector2.zero;
    }

    private void Update()
    {
        if(rocket==null || catalog==null)return;
        if(Application.isFocused && GUIUtility.keyboardControl==0 && Input.GetKeyDown(KeyCode.H) && !LaunchMenu.Open)
        {
            var heating=GetComponent<ReentryHeating>();
            if(heating!=null)heating.ShowZones=!heating.ShowZones;
        }
        if(rocket.Launched)
        {
            HandleStageKey();
            UpdateStaging();
            ReadFlightInput();
            UpdateFlightThrottle();
            return;
        }
        flightGimbal=Vector2.zero;
        if(LaunchMenu.Open)return;
        if(Input.GetKeyDown(KeyCode.Delete) && GUIUtility.keyboardControl==0 && !locked)DeleteSelection();
        if(HandleEngineToggleKey())return;
        // A locked preset can still be launched (above) but its parts
        // cannot be clicked, selected or swapped in the 3D view.
        if(locked || !Input.GetMouseButtonDown(0) || IsPointerOverPanel())return;
        GUIUtility.keyboardControl=0;
        var camera=view!=null?view.GetComponent<Camera>():Camera.main;if(camera==null)return;
        SelectAtRay(camera.ScreenPointToRay(Input.mousePosition),camera.farClipPlane);
    }

    // Space toggles ignition at the current commanded throttle - the keyboard
    // equivalent of the Launch/Shutdown buttons, usable both to ignite from
    // the pad and to re-light mid-flight after a shutdown.
    private bool HandleEngineToggleKey()
    {
        if(!Application.isFocused || IgnorePilotInput || GUIUtility.keyboardControl!=0 || !Input.GetKeyDown(KeyCode.Space))return false;
        if(rocket.EngineEnabled){rocket.StopEngine();pilotShutdown=true;}
        else if(!rocket.Launched)TryLaunch();
        else
        {
            pilotShutdown=false;
            rocket.StartEngine(commandedThrottle);
        }
        return true;
    }

    // Simulation speed buttons: ×1-×10 run the physics faster; ×100/×1000
    // warp on rails (see TimeWarp) - only coasting above 100 km in flight,
    // or on the pad to fast-forward the clock.
    private void TimeWarpControls()
    {
        GUILayout.Label("Simulation speed: ×"+TimeWarp.Rate.ToString("N0")+(TimeWarp.OnRails?(rocket.Launched?" (on rails)":" (clock)"):""));
        // Rows - physics speeds, then rails warp in two rows - so the buttons
        // fit the panel width without squashing.
        GUILayout.BeginHorizontal();
        foreach (var rate in TimeWarp.Rates)
        {
            if (Mathf.Approximately(rate, TimeWarp.RailsFrom) || Mathf.Approximately(rate, 100000))
            {
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label(Mathf.Approximately(rate, TimeWarp.RailsFrom)?"Warp:":"", GUILayout.Width(44));
            }
            var previousColor = GUI.backgroundColor;
            GUI.backgroundColor = Mathf.Approximately(TimeWarp.Rate, rate)
                ? new Color(1f, .72f, .3f) : Color.white;
            // Short labels for the big rates (×10k, ×100k, ×1M) so a row of
            // them still fits the 320 px panel.
            var label=rate>=1e6f?"×"+(rate/1e6f).ToString("0")+"M":rate>=1e4f?"×"+(rate/1e3f).ToString("0")+"k":"×"+rate.ToString("N0");
            if (GUILayout.Button(label)) TimeWarp.Request(rate);
            GUI.backgroundColor = previousColor;
        }
        GUILayout.EndHorizontal();
        if(!string.IsNullOrEmpty(TimeWarp.Message))
            GUILayout.Label(TimeWarp.Message,new GUIStyle(GUI.skin.label){wordWrap=true,normal={textColor=new Color(1f,.75f,.4f)}});
    }

    // Launch from the pad (Launch button / Space). Two ways this used to do
    // nothing with no explanation: a throttle left at 0% from an earlier
    // flight (lifts off at full throttle instead), and a rocket too heavy for
    // its engines, whose clamps released onto a pad it could never leave
    // (refused, saying why).
    private void TryLaunch()
    {
        if(rocket.Launched)return;
        pilotShutdown=false;
        if(TimeWarp.OnRails)TimeWarp.Request(1);   // the pad clock warp ends at launch
        if(commandedThrottle<=.01f)commandedThrottle=1f;
        if(flight.Prepare(commandedThrottle) && flight.TWR<1)
        {
            notice="Too heavy to lift off: TWR "+flight.TWR.ToString("F2")+" at "+(commandedThrottle*100).ToString("F0")+
                "% throttle ("+(flight.Thrust/1e6).ToString("F2")+" MN vs "+(flight.TotalMass/1000).ToString("N0")+" t). Add or upgrade engines, or carry less propellant.";
            return;
        }
        GetComponent<ReentryHeating>()?.ResetHeat();
        rocket.StartEngine(commandedThrottle);
        if(!rocket.Launched)notice=flight.Status;
    }

    // KSP-style throttle keys: hold Shift/Ctrl to ramp, Z for full throttle,
    // X to cut it. commandedThrottle is the same field the GUI slider reads
    // and writes, so the slider and keyboard always agree on the value.
    private void UpdateFlightThrottle()
    {
        HandleEngineToggleKey();
        if(!Application.isFocused || IgnorePilotInput || GUIUtility.keyboardControl!=0)return;
        var previousThrottle=commandedThrottle;
        if(Input.GetKeyDown(KeyCode.Z))commandedThrottle=1f;
        else if(Input.GetKeyDown(KeyCode.X))commandedThrottle=0f;
        else
        {
            var up=Input.GetKey(KeyCode.LeftShift)||Input.GetKey(KeyCode.RightShift);
            var down=Input.GetKey(KeyCode.LeftControl)||Input.GetKey(KeyCode.RightControl);
            if(up!=down)commandedThrottle=Mathf.Clamp01(commandedThrottle+(up?1f:-1f)*ThrottleRampPerSecond*Time.deltaTime);
        }
        ApplyThrottle(previousThrottle);
    }

    // Cutting the throttle to zero (X, Ctrl, the slider) or below an
    // engine's minimum switches the engine off in Rocket. Raising it again
    // relights it, KSP-style - previously only Space could, so Z/Shift
    // looked dead after a cut. An explicit Space/Shutdown stays off.
    private void ApplyThrottle(float previousThrottle)
    {
        if(rocket.EngineEnabled)rocket.SetThrottle(commandedThrottle);
        else if(!pilotShutdown && commandedThrottle>previousThrottle && commandedThrottle>0)
            rocket.StartEngine(commandedThrottle);
    }

    public void SelectAtRay(Ray ray,float maxDistance)
    {
        AssemblySelectable nearest=null;var distance=maxDistance;
        foreach(var part in selectableParts)
        {
            if(part==null || !part.gameObject.activeInHierarchy)continue;
            if(part.IntersectRay(ray,out var hitDistance) && hitDistance<distance)
            {nearest=part;distance=hitDistance;}
        }
        SelectPart(nearest);
    }

    private void LateUpdate()
    {
        if(cluster==null)return;
        HideLegacy();
        Dock();
    }

    public void FocusCluster()
    {
        if(view!=null && cluster!=null)
            view.FocusAssemblyPart(cluster.TransformPoint(new Vector3(0,-lowestEngine*.45f,0)),Mathf.Max(footprint,lowestEngine));
    }

    private string SavePath => Path.Combine(Application.persistentDataPath,"rocket-assembly.json");
    public void SaveLayout(string path=null)
    {
        try { File.WriteAllText(path ?? SavePath,JsonUtility.ToJson(layout,true)); notice="Assembly saved."; }
        catch(Exception e) { notice="Could not save assembly: "+e.Message; }
    }
    public void LoadLayout(string path=null)
    {
        try
        {
            if(!File.Exists(path ?? SavePath)){notice="No saved assembly found.";return;}
            var loaded=JsonUtility.FromJson<Layout>(File.ReadAllText(path ?? SavePath));
            // Saves from before MaxSockets grew past 7 are still valid -
            // grow their arrays in place rather than rejecting the file.
            if(loaded==null || loaded.engineIds==null || loaded.engineIds.Length<1 || loaded.engineIds.Length>MaxSockets ||
               loaded.sockets<1 || loaded.sockets>MaxSockets || loaded.sockets>loaded.engineIds.Length)
                throw new InvalidDataException("Invalid assembly file format.");
            foreach(var id in loaded.engineIds)
                if(!string.IsNullOrEmpty(id) && FindEngine(id)==null)throw new InvalidDataException("Unknown engine.");
            if(loaded.angles==null || loaded.angles.Length!=loaded.engineIds.Length ||
               !ProceduralPropellantTank.ValidDimensions(loaded.fuelCapacity,loaded.fuelDiameter) ||
               !ProceduralPropellantTank.ValidDimensions(loaded.oxidizerCapacity,loaded.oxidizerDiameter))
                throw new InvalidDataException("Invalid component parameters.");
            foreach(var angle in loaded.angles)if(float.IsNaN(angle.x)||float.IsInfinity(angle.x)||float.IsNaN(angle.y)||float.IsInfinity(angle.y))
                throw new InvalidDataException("Invalid mount angle.");
            if(loaded.fuelType!="RP-1" && loaded.fuelType!="LH2" && loaded.fuelType!="Methane")throw new InvalidDataException("Invalid propellant type.");
            if(float.IsNaN(loaded.fillFraction)||loaded.fillFraction<0||loaded.fillFraction>1)throw new InvalidDataException("Invalid fill fraction.");
            if(loaded.parameters==null)loaded.parameters=new EngineParameters[loaded.engineIds.Length];
            if(loaded.parameters.Length!=loaded.engineIds.Length)throw new InvalidDataException("Invalid engine parameters.");
            GrowToMaxSockets(ref loaded.engineIds);
            GrowToMaxSockets(ref loaded.angles);
            GrowToMaxSockets(ref loaded.parameters);
            for(var i=0;i<loaded.parameters.Length;i++)
            {
                // JsonUtility materializes null array entries as empty objects.
                // Empty parameter cards belong to empty mounts and must not
                // invalidate an otherwise valid saved assembly.
                if(string.IsNullOrEmpty(loaded.engineIds[i]))
                {
                    loaded.parameters[i]=null;
                    continue;
                }
                if(loaded.parameters[i]==null)loaded.parameters[i]=EnginePerformance.Reference(loaded.engineIds[i]);
                if(loaded.parameters[i]==null || !loaded.parameters[i].Valid)
                    throw new InvalidDataException("Invalid engine parameters in slot "+(i+1)+".");
            }
            Remember();layout=loaded;selectedSocket=0;editingSlot=-1;SyncFields();Rebuild();notice="Assembly loaded.";
        }
        catch(Exception e){notice="Could not load assembly: "+e.Message;}
    }

    public string ActivePresetName => activePresetName;
    public bool IsLocked => locked;

    // Fuel/oxidizer tanks are visually the biggest part of the rocket (the
    // hull is mostly hidden behind/around them) and normally use a fixed
    // orange/light-blue schematic color pair so they're easy to tell apart
    // while hand-building. A preset instead paints the whole vehicle one
    // real color, like the actual rockets it's approximating.
    private static readonly Color DefaultFuelColor=new(.88f,.65f,.28f);
    private static readonly Color DefaultOxidizerColor=new(.55f,.78f,.88f);
    private void SetTankColors(Color fuel,Color oxidizer)
    {
        if(fuelMaterial!=null)fuelMaterial.color=fuel;
        if(oxidizerMaterial!=null)oxidizerMaterial.color=oxidizer;
    }

    /// <summary>
    /// Configures the assembly to match a real vehicle (RocketPresets) and
    /// locks the sandbox editing UI - see the class doc comment on
    /// RocketPresets for exactly what is and isn't a faithful match.
    /// </summary>
    public void LoadPreset(int index)
    {
        if(rocket.Launched || index<0 || index>=RocketPresets.All.Length)return;
        var preset=RocketPresets.All[index];
        undo.Clear();
        ResetStaging(index);
        // Clear every slot first - switching directly from one preset (or a
        // sandbox build) to another must not leave stale engines behind in
        // slots the new preset doesn't get around to overwriting, and must
        // not leave the previous vehicle's fuel type in place while engines
        // are installed one at a time (InstallEngine only auto-syncs fuel
        // type from the *first* engine of an otherwise-empty assembly).
        for(var i=0;i<MaxSockets;i++){layout.engineIds[i]=null;layout.parameters[i]=null;}
        layout.frameInstalled=true;
        layout.gimballed=true;
        // Set before the install/tank loop below so its Rebuild() calls
        // already hide the tank stack, instead of flashing it visible
        // then hiding it once SetBodyModel runs at the end.
        activeBodyModelKey=preset.bodyModelKey;
        // Shape first: every Rebuild() below positions the engine cluster,
        // tanks and dock height from rocket.AssemblyMountLocalY, which is
        // derived from the body dimensions. Resizing afterwards (with no
        // further Rebuild) left the engines at the previous vehicle's mount
        // height - mid-body on a taller preset model.
        rocket.ConfigureShape(preset.bodyDiameter,preset.bodyHeight,preset.noseHeight,preset.engineHeight,preset.hullColor);
        rocket.SetBodyModel(null);   // rebuilt fresh: staging detaches its parts
        rocket.SetBodyModel(preset.bodyModelKey);
        SetSocketCount(preset.engineCount);
        for(var i=0;i<preset.engineCount;i++)InstallEngine(i,preset.engineId);
        if(!string.IsNullOrEmpty(preset.coreEngineId))layout.parameters[0]=EnginePerformance.Reference(preset.coreEngineId);
        flight.SetSolidBoosters(preset.solidBoosterId,preset.solidBoosterCount);
        flight.SetPresetDryMass(preset.LiftoffDryMass);
        flight.SetFlightProgram(preset.mecoSeconds,preset.throttleProgram,preset.engineCutoffs);
        flight.SetAerodynamics(preset.centerOfPressure,preset.normalForceSlope);
        layout.fuelType=preset.fuelType;
        flight.SetFuel(preset.fuelType);
        SetTank(false,true,preset.fuelCapacity,preset.fuelDiameter);
        SetTank(true,true,preset.oxidizerCapacity,preset.oxidizerDiameter);
        flight.SetFill(1f);
        // The imported model already looks like the real vehicle - the
        // schematic tank recolor is only useful for the generated hull.
        if(string.IsNullOrEmpty(preset.bodyModelKey))SetTankColors(preset.hullColor,preset.hullColor);
        else SetTankColors(DefaultFuelColor,DefaultOxidizerColor);
        flight.SetDragCoefficient(preset.dragCoefficient*(preset.fairingOff?1.2f:1f));
        ApplyFairingVisibility();
        activePresetName=preset.name;
        locked=true;
        selectedSocket=0;
        undo.Clear();
        FocusRocket();
        notice=preset.name+" loaded and locked. Use \"Custom build\" to return to the sandbox.";
    }

    /// <summary>Leaves a locked preset and resets to a blank sandbox assembly.</summary>
    public void ExitPreset()
    {
        if(rocket.Launched || !locked)return;
        locked=false;
        activePresetName=null;
        activeBodyModelKey=null;
        undo.Clear();
        layout=new Layout();
        rocket.ConfigureShape(3.7f,35f,8f,4f,new Color(0.85f,0.86f,0.88f));
        rocket.SetBodyModel(null);
        ResetStaging(-1);
        flight.SetSolidBoosters(null,0);
        customPayload=0;flight.SetPresetDryMass(0);
        flight.SetFlightProgram(0,null,null);
        flight.SetAerodynamics(0,0);
        SetTankColors(DefaultFuelColor,DefaultOxidizerColor);
        flight.SetDragCoefficient(0.5f);
        selectedSocket=0;
        SyncFields();
        Rebuild();
        FocusRocket();
        notice="Back to a blank custom assembly.";
    }

    public void SetFrame(bool installed,bool gimballed)
    {
        Remember(); layout.frameInstalled=installed; layout.gimballed=gimballed; Rebuild();
        notice=installed ? "Frame installed. Attach engines to its mounts." : "Frame and engines removed. Undo to restore the assembly.";
    }

    public void SetTank(bool oxidizer,bool installed,float volume,float diameter)
    {
        if(!ProceduralPropellantTank.ValidDimensions(volume,diameter))throw new ArgumentOutOfRangeException(nameof(volume));
        Remember();
        if(oxidizer){layout.oxidizerInstalled=installed;layout.oxidizerCapacity=volume;layout.oxidizerDiameter=diameter;}
        else{layout.fuelInstalled=installed;layout.fuelCapacity=volume;layout.fuelDiameter=diameter;}
        SyncFields(); Rebuild();
    }

    public void SetGimbal(int socket,Vector2 degrees)
    {
        if(!layout.gimballed || !layout.frameInstalled || socket<0 || socket>=SocketCount)return;
        if(GetParameters(socket)!=null && !GetParameters(socket).allowGimbal)return;
        if(float.IsNaN(degrees.x)||float.IsInfinity(degrees.x)||float.IsNaN(degrees.y)||float.IsInfinity(degrees.y))return;
        Remember(); layout.angles[socket]=Vector2.ClampMagnitude(degrees,RocketMountFrame.PreviewAngleLimit);
        mountFrame.SetAngle(socket,layout.angles[socket]);
    }

    public Vector3 GetThrustDirection(int socket) => sockets[socket].up;
    public Vector3 ResultantThrustDirection
    {
        get { var sum=Vector3.zero; for(var i=0;i<SocketCount;i++)if(FindEngine(layout.engineIds[i])!=null)sum+=sockets[i].up;
            return InstalledCount>0 ? sum/InstalledCount : Vector3.zero; }
    }

    private void SyncFields()
    {
        fuelVolumeText=layout.fuelCapacity.ToString(CultureInfo.InvariantCulture);
        oxidizerVolumeText=layout.oxidizerCapacity.ToString(CultureInfo.InvariantCulture);
        fuelDiameterText=layout.fuelDiameter.ToString(CultureInfo.InvariantCulture);
        oxidizerDiameterText=layout.oxidizerDiameter.ToString(CultureInfo.InvariantCulture);
    }

    private void RebuildTanks()
    {
        if(tankStack!=null){tankStack.gameObject.SetActive(false);Destroy(tankStack.gameObject);}
        tankStack=new GameObject("Tank Stack").transform; tankStack.SetParent(transform,false);
        tankStack.localPosition=Vector3.up*(string.IsNullOrEmpty(activeBodyModelKey)?rocket.AssemblyMountLocalY:rocket.ActiveBaseLocalY);
        tankStack.localScale=Vector3.one*PlanetBody.WorldUnitsPerMeter;
        stackHeight=0; FuelTank=null; OxidizerTank=null;
        if(layout.fuelInstalled)FuelTank=AddTank(false);
        if(layout.oxidizerInstalled)OxidizerTank=AddTank(true);
        var nose=transform.Find("NoseCone");
        if(nose!=null)nose.localPosition=Vector3.up*(rocket.AssemblyMountLocalY+stackHeight*PlanetBody.WorldUnitsPerMeter);
        // Conservative collision envelope follows the rebuilt component stack.
        var capsule=GetComponent<CapsuleCollider>();
        // A body model's collider comes from Rocket.ConfigureShape (the full
        // vehicle); only the sandbox stack needs it fitted to its parts.
        if(capsule!=null && string.IsNullOrEmpty(activeBodyModelKey))
        {
            var bottom=-.4f-lowestEngine; var top=stackHeight+8;
            capsule.center=Vector3.up*(rocket.AssemblyMountLocalY+(bottom+top)*.5f*PlanetBody.WorldUnitsPerMeter);
            capsule.radius=Mathf.Max(footprint,Mathf.Max(layout.fuelInstalled?layout.fuelDiameter:0,layout.oxidizerInstalled?layout.oxidizerDiameter:0))*.5f*PlanetBody.WorldUnitsPerMeter;
            capsule.height=(top-bottom)*PlanetBody.WorldUnitsPerMeter;
        }
        var hideTanks=!string.IsNullOrEmpty(activeBodyModelKey);
        foreach(var renderer in tankStack.GetComponentsInChildren<Renderer>(true))renderer.enabled=!hideTanks;
    }

    private ProceduralPropellantTank AddTank(bool oxidizer)
    {
        var obj=new GameObject(oxidizer?"Oxidizer tank":"Fuel tank"); obj.transform.SetParent(tankStack,false);
        obj.transform.localPosition=Vector3.up*stackHeight;
        var tank=obj.AddComponent<ProceduralPropellantTank>();
        tank.Configure(oxidizer?ProceduralPropellantTank.Contents.Oxidizer:ProceduralPropellantTank.Contents.Fuel,
            oxidizer?layout.oxidizerCapacity:layout.fuelCapacity,oxidizer?layout.oxidizerDiameter:layout.fuelDiameter,
            oxidizer?oxidizerMaterial:fuelMaterial);
        AddSelectable(obj,oxidizer?AssemblySelectable.PartKind.OxidizerTank:AssemblySelectable.PartKind.FuelTank,0,obj.GetComponentsInChildren<Renderer>());
        stackHeight+=tank.Height;return tank;
    }

    private void HideLegacy()
    {
        foreach(var name in new[]{"Engine","Body"}){var child=transform.Find(name);if(child!=null)child.gameObject.SetActive(false);}
    }

    public void FocusRocket()
    {
        if(view!=null && cluster!=null)view.FocusAssemblyPart(cluster.TransformPoint(Vector3.up*(stackHeight+8-lowestEngine)*.5f),Mathf.Max(stackHeight+8+lowestEngine,footprint));
    }

    private void TankEditor(bool oxidizer)
    {
        GUILayout.Label(oxidizer?"OXIDIZER":"FUEL");
        GUILayout.Label("Capacity, m³");
        if(oxidizer)oxidizerVolumeText=GUILayout.TextField(oxidizerVolumeText);else fuelVolumeText=GUILayout.TextField(fuelVolumeText);
        GUILayout.Label("Diameter, m");
        if(oxidizer)oxidizerDiameterText=GUILayout.TextField(oxidizerDiameterText);else fuelDiameterText=GUILayout.TextField(fuelDiameterText);
        var tank=oxidizer?OxidizerTank:FuelTank;
        GUILayout.Label(tank!=null ? "Height: "+tank.Height.ToString("F2")+" m" : "No tank installed");
        if(GUILayout.Button(tank==null?"Install tank":"Apply dimensions"))
        {
            float v,d;
            if(float.TryParse((oxidizer?oxidizerVolumeText:fuelVolumeText).Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out v) &&
               float.TryParse((oxidizer?oxidizerDiameterText:fuelDiameterText).Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out d) &&
               ProceduralPropellantTank.ValidDimensions(v,d))
            {SetTank(oxidizer,true,v,d);FocusRocket();notice="Tank dimensions and attached component positions updated.";}
            else notice="Enter positive dimensions: capacity 0.01–100000 m³, diameter 0.2–30 m, height 0.05–500 m.";
        }
        GUILayout.Space(12);
    }

    public bool IsPresetLoaded=>activePresetIndex>=0;

    private GUIStyle backStyle;

    private void OnGUI()
    {
        if(catalog==null || LaunchMenu.Open)return;
        if(!menuReported && Event.current.type==EventType.Repaint)
        {menuReported=true;Debug.Log("ROCKET_ASSEMBLY_MENU_RENDERED");}
        GUI.enabled=true;
        if(activePresetIndex>=0)StagingStackGUI();
        // A real rocket chosen from the launch menu: no side panel on the
        // pad, just a small way back to the menu and the launch control
        // (everything else was set there).
        if(locked && !rocket.Launched)
        {
            backStyle??=new GUIStyle(GUI.skin.button){fontSize=13};
            if(GUI.Button(new Rect(24,PanelTop,96,30),"◂ Back",backStyle))GetComponent<LaunchMenu>()?.ShowPayload(activePresetIndex);
            if(GUI.Button(new Rect(128,PanelTop,150,30),"LAUNCH (Space)",backStyle))TryLaunch();
            return;
        }
        GUILayout.BeginArea(LeftPanelRect,GUI.skin.box);
        if(rocket.Launched)
        {
            assemblyScroll=GUILayout.BeginScrollView(assemblyScroll);
            FlightControls();
            GUILayout.EndScrollView();GUILayout.EndArea();
            return;
        }
        if(GUILayout.Button("◂ Main menu",GUILayout.Height(26))){GetComponent<LaunchMenu>()?.Show();GUILayout.EndArea();GUIUtility.ExitGUI();}
        GUILayout.Label("ROCKET COMPONENTS");
        GUILayout.Label("Space also launches at the current throttle.",new GUIStyle(GUI.skin.label){wordWrap=true});
        if(GUILayout.Button("Launch",GUILayout.Height(34)))
        {
            TryLaunch();
            assemblyScroll=Vector2.zero;
            GUILayout.EndArea();
            GUIUtility.ExitGUI();
        }
        panelScroll=GUILayout.BeginScrollView(panelScroll);
        // Liftoff figures at full throttle, solid boosters included.
        if(flight.Prepare(1f) || flight.Thrust>0)
            GUILayout.Label("Liftoff: "+(flight.TotalMass/1000).ToString("N0")+" t · "+(flight.Thrust/1e6).ToString("F1")+" MN · TWR "+flight.TWR.ToString("F2"),
                new GUIStyle(GUI.skin.label){fontStyle=FontStyle.Bold,wordWrap=true});
        PayloadControls();
        TimeWarpControls();
        if(locked)
        {
            GUILayout.Label("REAL ROCKET: "+activePresetName.ToUpperInvariant());
            GUILayout.Label("This is a fixed configuration - engines, tanks and body are locked. Use \"Custom build\" to edit an assembly by hand instead.",new GUIStyle(GUI.skin.label){wordWrap=true});
            if(GUILayout.Button("Change payload / rocket",GUILayout.Height(32)))GetComponent<LaunchMenu>()?.ShowPayload(activePresetIndex);
            if(GUILayout.Button("Custom build",GUILayout.Height(32)))ExitPreset();
        }
        else
        {
            GUILayout.Label("CUSTOM BUILD",new GUIStyle(GUI.skin.label){fontStyle=FontStyle.Bold});
            category=GUILayout.Toolbar(category,new[]{"Engines","Frames","Tanks"});
            if(category==0)
            {
                for(var i=0;i<catalog.engines.Length;i++)
                {
                    GUI.backgroundColor=i==selectedEngine ? new Color(.25f,.75f,1f) : Color.white;
                    if(GUILayout.Button(catalog.engines[i].title,GUILayout.Height(33))) {selectedEngine=i;FocusCluster();}
                }
                GUI.backgroundColor=Color.white;
                ParameterEditor();
                GUILayout.Label("Select an engine → click an empty mount to install. Click a component to select it. Press Delete to remove it.",new GUIStyle(GUI.skin.label){wordWrap=true});
            }
            if(category==1)
            {
                if(GUILayout.Button("Install fixed frame",GUILayout.Height(32)))SetFrame(true,false);
                if(GUILayout.Button("Install gimballed frame",GUILayout.Height(32)))SetFrame(true,true);
                GUILayout.Label("Number of mounts"); GUILayout.BeginHorizontal();
                foreach(var count in new[]{1,3,5,7})if(GUILayout.Button((layout.sockets==count?"• ":"")+count))SetSocketCount(count);
                GUILayout.EndHorizontal();
                GUILayout.Label("Each engine has its own mount. The frame expands to fit the engines.",new GUIStyle(GUI.skin.label){wordWrap=true});
            }
            if(category==2){
                GUILayout.Label("Tank fuel");
                foreach(var fuel in new[]{"RP-1","LH2","Methane"})if(GUILayout.Button((flight.FuelType==fuel?"• ":"")+fuel)){Remember();layout.fuelType=fuel;flight.SetFuel(fuel);}
                GUILayout.Label("Initial fill: "+(flight.Fill*100).ToString("F0")+"%");
                var fill=GUILayout.HorizontalSlider(flight.Fill,0,1);if(Mathf.Abs(fill-flight.Fill)>.001f){Remember();layout.fillFraction=fill;flight.SetFill(fill);}
                TankEditor(false);TankEditor(true);GUILayout.Label("Capacity determines geometry. Remaining propellant is a separate quantity.",new GUIStyle(GUI.skin.label){wordWrap=true});}
            GUILayout.Space(12);
        }
        if(GUILayout.Button("Focus engines",GUILayout.Height(30)))FocusCluster();
        if(GUILayout.Button("Show entire rocket",GUILayout.Height(30)))FocusRocket();
        if(!locked)
        {
            GUILayout.BeginHorizontal();if(GUILayout.Button("Save"))SaveLayout();if(GUILayout.Button("Load"))LoadLayout();GUILayout.EndHorizontal();
            if(GUILayout.Button("Undo change"))UndoChange();
        }
        GUILayout.Label(notice,new GUIStyle(GUI.skin.label){wordWrap=true});
        GUILayout.Space(12);
        GUILayout.Label("ASSEMBLY");
        if(!locked)
        {
            GUILayout.Label(selection!=null ? "Selected: "+selection.gameObject.name+" · Delete to remove" : "Click a component to select it.",new GUIStyle(GUI.skin.label){wordWrap=true});
        }
        GUILayout.Label(!layout.frameInstalled?"No frame installed":layout.gimballed?"Gimballed frame":"Fixed frame");
        GUILayout.Label("Engines: "+InstalledCount+" / "+SocketCount);
        if(!locked)
        {
            for(var i=0;i<SocketCount;i++)
            {
                var entry=FindEngine(layout.engineIds[i]);
                if(GUILayout.Button((selectedSocket==i?"▸ ":"")+(i+1)+". "+(entry?.title??"Empty"),GUILayout.Height(30)))
                {selectedSocket=i;SelectEngine(i);}
            }
            if(layout.frameInstalled)
            {
                if(GUILayout.Button("Install in slot "+(selectedSocket+1),GUILayout.Height(32))){InstallEngine(selectedSocket,catalog.engines[selectedEngine].id);FocusCluster();}
                if(layout.gimballed)
                {
                    var angle=layout.angles[selectedSocket];
                    GUILayout.Label("Mount angle "+(selectedSocket+1)+" (max 10°)");
                    GUILayout.Label("Pitch: "+angle.x.ToString("F1")+"°");
                    var x=GUILayout.HorizontalSlider(angle.x,-10,10);
                    GUILayout.Label("Yaw: "+angle.y.ToString("F1")+"°");
                    var z=GUILayout.HorizontalSlider(angle.y,-10,10);
                    if(Mathf.Abs(x-angle.x)>.01f || Mathf.Abs(z-angle.y)>.01f)SetGimbal(selectedSocket,new Vector2(x,z));
                    if(GUILayout.Button("Reset engine angle"))SetGimbal(selectedSocket,Vector2.zero);
                }
            }
        }
        GUILayout.Space(10);
        GUILayout.Label("Fuel: "+(FuelTank!=null?FuelTank.Capacity+" m³":"no tank"));
        GUILayout.Label("Oxidizer: "+(OxidizerTank!=null?OxidizerTank.Capacity+" m³":"no tank"));
        GUILayout.Label("Drag scales with altitude-based air density and speed squared; propellant densities are constant.",new GUIStyle(GUI.skin.label){wordWrap=true});
        GUILayout.EndScrollView();GUILayout.EndArea();
        var camera=view!=null ? view.GetComponent<Camera>() : Camera.main;
        if(!locked && camera!=null && cluster!=null && Vector3.Distance(camera.transform.position,cluster.position)<.5f)
            for(var i=0;i<sockets.Count;i++)
            {
                var p=camera.WorldToScreenPoint(sockets[i].position);if(p.z<=0)continue;
                var point=new Vector2(p.x,Screen.height-p.y);
                if(LeftPanelRect.Contains(point) || point.y<PanelTop)continue;
                GUI.backgroundColor=FindEngine(layout.engineIds[i])==null?new Color(.3f,1f,.55f):new Color(1f,.7f,.25f);
                if(GUI.Button(new Rect(point.x-17,point.y-17,34,34),(i+1).ToString()))
                {
                    selectedSocket=i;
                    if(FindEngine(layout.engineIds[i])==null)InstallEngine(i,catalog.engines[selectedEngine].id);
                    else SelectEngine(i);
                    FocusCluster();
                }
            }
        GUI.backgroundColor=Color.white;GUI.enabled=true;
    }

    private void OnDestroy()
    {
        if (Application.isPlaying && active == this) TimeWarp.Request(1);
        if(active==this)active=null;
        if(frameMaterial!=null)Destroy(frameMaterial);
        if(fuelMaterial!=null)Destroy(fuelMaterial);
        if(oxidizerMaterial!=null)Destroy(oxidizerMaterial);
    }
}
