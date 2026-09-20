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
        public string[] engineIds = new string[MaxSockets];
        public EngineParameters[] parameters = new EngineParameters[MaxSockets];
    }

    private RocketFlightModel flight;
    private float commandedThrottle=1;
    private static readonly float[] FlightSpeeds = { 1f, 2f, 5f, 10f };
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
        SyncFields();
        Rebuild();
        Debug.Log("ROCKET_ASSEMBLY_READY: component catalog loaded; fuel and oxidizer tanks built.");
    }

    public EngineParameters GetParameters(int slot)
    {
        if(slot<0 || slot>=SocketCount || FindEngine(layout.engineIds[slot])==null)return null;
        layout.parameters[slot]??=EnginePerformance.Reference(layout.engineIds[slot]);
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
            GUILayout.Label("Up/Down or W/S: Pitch. Left/Right or A/D: Yaw. Release to center.",new GUIStyle(GUI.skin.label){wordWrap=true});
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
        GUILayout.Label("Shift/Ctrl: throttle up/down. Z: full throttle. X: cut throttle. Space: toggle ignition.",new GUIStyle(GUI.skin.label){wordWrap=true});
        GUILayout.Label("Throttle: "+(commandedThrottle*100).ToString("F0")+"%");
        commandedThrottle=GUILayout.HorizontalSlider(commandedThrottle,.01f,1);
        GUILayout.Label("Simulation speed: ×"+Time.timeScale.ToString("F0"));
        GUILayout.BeginHorizontal();
        foreach (var speed in FlightSpeeds)
        {
            var previousColor = GUI.backgroundColor;
            GUI.backgroundColor = Mathf.Approximately(Time.timeScale, speed)
                ? new Color(1f, .72f, .3f) : Color.white;
            if (GUILayout.Button("×"+speed.ToString("F0"))) Time.timeScale = speed;
            GUI.backgroundColor = previousColor;
        }
        GUILayout.EndHorizontal();
        if(rocket.EngineEnabled)rocket.SetThrottle(commandedThrottle);
        GUILayout.BeginHorizontal();
        if(GUILayout.Button("Shutdown"))rocket.StopEngine();
        GUILayout.EndHorizontal();
        if(rocket.Launched && GUILayout.Button("Return to assembly")){Time.timeScale=1f;rocket.ReturnToAssembly();Rebuild();view?.ShowAssembly();}
        GUILayout.Label("Mass: "+(flight.TotalMass/1000).ToString("F2")+" t · TWR: "+flight.TWR.ToString("F2"));
        GUILayout.Label((rocket.EngineEnabled?"Thrust: ":"Available thrust: ")+(flight.Thrust/1000).ToString("F1")+" kN");
        GUILayout.Label(new GUIContent("Altitude: "+flight.Altitude.ToString("F1")+" m", "Rocket root height above the spherical planet surface."));
        GUILayout.Label("Speed: "+flight.Speed.ToString("F1")+" m/s · Pressure: "+(flight.Pressure/1000).ToString("F1")+" kPa");
        GUILayout.Label(new GUIContent("Vertical speed: "+flight.VerticalSpeed.ToString("+0.0;-0.0;0.0")+" m/s", "Radial velocity relative to the planet: positive ascending, negative descending."));
        GUILayout.Label("Horizontal speed: "+flight.HorizontalSpeed.ToString("F1")+" m/s");
        GUILayout.Label(new GUIContent("Drag: "+(flight.Drag/1000).ToString("F1")+" kN", "Aerodynamic drag opposing velocity; falls off with altitude as the air thins."));
        GUILayout.Label("Fuel: "+flight.FuelRemaining.ToString("F1")+" kg ("+flight.FuelType+")");
        GUILayout.Label("LOX: "+flight.OxidizerRemaining.ToString("F1")+" kg");
        GUILayout.Label("Flow: "+flight.FuelFlow.ToString("F2")+" + "+flight.OxidizerFlow.ToString("F2")+" kg/s");
        GUILayout.Label(flight.Status,new GUIStyle(GUI.skin.label){wordWrap=true});
        showFormulas=GUILayout.Toggle(showFormulas,"Show formulas");
        if(showFormulas)GUILayout.Label("A = sum(pi × D² / 4)\nF = throttle × Fvac − p × A\nFlow = throttle × Fvac / (g0 × IspVac)\nFuel flow = Flow / (1 + O/F)\nLOX flow = Flow − Fuel flow\nTWR = total thrust / (vehicle mass × local gravity)\nGravity = G × planet mass / distance²\nForce direction = mount orientation\nTorque = offset from COM × force\nAltitude = max(0, distance to planet center − planet radius) [m]\nAtmosphere: p = 101325 × exp(−altitude / 8500)\nAir density = p / (287.05 × 288.15)\nDrag = 0.5 × density × speed² × Cd × body cross-section, opposing velocity\nLinear throttle; attached-flow approximation.",new GUIStyle(GUI.skin.label){wordWrap=true});
        GUILayout.Space(10);
    }

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

    public EngineCatalog.Entry FindEngine(string id)
    {
        if(catalog==null || catalog.engines==null || string.IsNullOrEmpty(id))return null;
        foreach(var engine in catalog.engines) if(engine.id==id)return engine;
        return null;
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
        cluster=new GameObject("Engine Cluster").transform;
        cluster.SetParent(transform,false);
        cluster.localPosition=Vector3.up*rocket.AssemblyMountLocalY;
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
        if(layout.frameInstalled)Beam("Central Mount",Vector3.zero,new Vector3(0,-.25f,0),1.4f);
        for(var i=0;i<SocketCount;i++)
        {
            var p=Vector3.down*.3f;
            var onRing=layout.sockets==3 || i>0;
            if(onRing)
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
            AddSelectable(cluster.gameObject,AssemblySelectable.PartKind.Frame,0,frameSurfaces.ToArray());
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
        var heightBelowPivot=-rocket.AssemblyMountLocalY+(.4f+lowestEngine+ (layout.gimballed ? footprint*.1f : 0))*PlanetBody.WorldUnitsPerMeter;
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
    private void UpdateFlightGimbals()
    {
        if(mountFrame==null || !mountFrame.CanGimbal)return;
        var input=Vector2.zero;
        if(Application.isFocused && GUIUtility.keyboardControl==0)
        {
            input.x=((Input.GetKey(KeyCode.UpArrow)||Input.GetKey(KeyCode.W))?1f:0f)-((Input.GetKey(KeyCode.DownArrow)||Input.GetKey(KeyCode.S))?1f:0f);
            input.y=((Input.GetKey(KeyCode.LeftArrow)||Input.GetKey(KeyCode.A))?1f:0f)-((Input.GetKey(KeyCode.RightArrow)||Input.GetKey(KeyCode.D))?1f:0f);
        }
        var desired=Vector2.ClampMagnitude(input,1f)*RocketMountFrame.PreviewAngleLimit;
        flightGimbal=Vector2.MoveTowards(flightGimbal,desired,20f*Time.deltaTime);
        for(var i=0;i<SocketCount;i++)
        {
            var parameters=GetParameters(i);
            // A gimballed engine below the centre of mass swings the nose the
            // OPPOSITE way from its own tilt - same reason real rocket TVC
            // steers by kicking the tail away from the turn. Feeding
            // flightGimbal straight into the mount tilted the engine toward
            // the key pressed, so the nose swung backwards from what W/S/A/D
            // suggest. Negate it here so the commanded direction matches the
            // nose's actual response, not the engine's.
            mountFrame.SetAngle(i,parameters!=null && parameters.allowGimbal?-flightGimbal:Vector2.zero);
        }
    }

    private void Update()
    {
        if(rocket==null || catalog==null)return;
        if(rocket.Launched)
        {
            UpdateFlightGimbals();
            UpdateFlightThrottle();
            return;
        }
        flightGimbal=Vector2.zero;
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
        if(!Application.isFocused || GUIUtility.keyboardControl!=0 || !Input.GetKeyDown(KeyCode.Space))return false;
        if(rocket.EngineEnabled)rocket.StopEngine();
        else
        {
            rocket.StartEngine(commandedThrottle);
            if(!rocket.Launched)notice=flight.Status;
        }
        return true;
    }

    // KSP-style throttle keys: hold Shift/Ctrl to ramp, Z for full throttle,
    // X to cut it. commandedThrottle is the same field the GUI slider reads
    // and writes, so the slider and keyboard always agree on the value.
    private void UpdateFlightThrottle()
    {
        HandleEngineToggleKey();
        if(!Application.isFocused || GUIUtility.keyboardControl!=0)return;
        if(Input.GetKeyDown(KeyCode.Z))commandedThrottle=1f;
        else if(Input.GetKeyDown(KeyCode.X))commandedThrottle=0f;
        else
        {
            var up=Input.GetKey(KeyCode.LeftShift)||Input.GetKey(KeyCode.RightShift);
            var down=Input.GetKey(KeyCode.LeftControl)||Input.GetKey(KeyCode.RightControl);
            if(up!=down)commandedThrottle=Mathf.Clamp01(commandedThrottle+(up?1f:-1f)*ThrottleRampPerSecond*Time.deltaTime);
        }
        if(rocket.EngineEnabled)rocket.SetThrottle(commandedThrottle);
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
        SetSocketCount(preset.engineCount);
        for(var i=0;i<preset.engineCount;i++)InstallEngine(i,preset.engineId);
        layout.fuelType=preset.fuelType;
        flight.SetFuel(preset.fuelType);
        SetTank(false,true,preset.fuelCapacity,preset.fuelDiameter);
        SetTank(true,true,preset.oxidizerCapacity,preset.oxidizerDiameter);
        flight.SetFill(1f);
        rocket.ConfigureShape(preset.bodyDiameter,preset.bodyHeight,preset.noseHeight,preset.engineHeight,preset.hullColor);
        rocket.SetBodyModel(preset.bodyModelKey);
        // The imported model already looks like the real vehicle - the
        // schematic tank recolor is only useful for the generated hull.
        if(string.IsNullOrEmpty(preset.bodyModelKey))SetTankColors(preset.hullColor,preset.hullColor);
        else SetTankColors(DefaultFuelColor,DefaultOxidizerColor);
        flight.SetDragCoefficient(preset.dragCoefficient);
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
        tankStack.localPosition=Vector3.up*rocket.AssemblyMountLocalY;
        tankStack.localScale=Vector3.one*PlanetBody.WorldUnitsPerMeter;
        stackHeight=0; FuelTank=null; OxidizerTank=null;
        if(layout.fuelInstalled)FuelTank=AddTank(false);
        if(layout.oxidizerInstalled)OxidizerTank=AddTank(true);
        var nose=transform.Find("NoseCone");
        if(nose!=null)nose.localPosition=Vector3.up*(rocket.AssemblyMountLocalY+stackHeight*PlanetBody.WorldUnitsPerMeter);
        // Conservative collision envelope follows the rebuilt component stack.
        var capsule=GetComponent<CapsuleCollider>();
        if(capsule!=null)
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

    private void OnGUI()
    {
        if(catalog==null)return;
        if(!menuReported && Event.current.type==EventType.Repaint)
        {menuReported=true;Debug.Log("ROCKET_ASSEMBLY_MENU_RENDERED");}
        GUI.enabled=true;
        GUILayout.BeginArea(LeftPanelRect,GUI.skin.box);
        if(rocket.Launched)
        {
            assemblyScroll=GUILayout.BeginScrollView(assemblyScroll);
            FlightControls();
            GUILayout.EndScrollView();GUILayout.EndArea();
            return;
        }
        GUILayout.Label("ROCKET COMPONENTS");
        GUILayout.Label("Space also launches at the current throttle.",new GUIStyle(GUI.skin.label){wordWrap=true});
        if(GUILayout.Button("Launch",GUILayout.Height(34)))
        {
            rocket.StartEngine(commandedThrottle);
            assemblyScroll=Vector2.zero;
            if(!rocket.Launched)notice=flight.Status;
            GUILayout.EndArea();
            GUIUtility.ExitGUI();
        }
        panelScroll=GUILayout.BeginScrollView(panelScroll);
        if(locked)
        {
            GUILayout.Label("REAL ROCKET: "+activePresetName.ToUpperInvariant());
            GUILayout.Label("This is a fixed configuration - engines, tanks and body are locked. Use \"Custom build\" to edit an assembly by hand instead.",new GUIStyle(GUI.skin.label){wordWrap=true});
            if(GUILayout.Button("Custom build",GUILayout.Height(32)))ExitPreset();
        }
        else
        {
            GUILayout.Label("REAL ROCKETS");
            GUILayout.Label("Pick one for a fixed, realistic configuration, or build your own below.",new GUIStyle(GUI.skin.label){wordWrap=true});
            for(var i=0;i<RocketPresets.All.Length;i++)
                if(GUILayout.Button(RocketPresets.All[i].name,GUILayout.Height(30)))LoadPreset(i);
            GUILayout.Space(12);
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
        if (Application.isPlaying && active == this) Time.timeScale = 1f;
        if(active==this)active=null;
        if(frameMaterial!=null)Destroy(frameMaterial);
        if(fuelMaterial!=null)Destroy(fuelMaterial);
        if(oxidizerMaterial!=null)Destroy(oxidizerMaterial);
    }
}
