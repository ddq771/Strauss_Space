using UnityEngine;

/// <summary>
/// The opening menu, drawn over the live scene: choose to build a rocket
/// (the sandbox assembly) or to launch a real one; for a real rocket, pick
/// it from the line-up, then set its payload - with liftoff mass, TWR and
/// Δv updating live - and go to the pad. More rockets added to
/// RocketPresets.All show up here automatically.
/// </summary>
public sealed class LaunchMenu : MonoBehaviour
{
    private enum Screen { Start, Rockets, Payload }
    public static bool Open { get; set; } = true;
    private Screen screen = Screen.Start;
    private int chosen = -1, hovered = -1;
    private RocketAssemblyController assembly;
    private RocketFlightModel flight;

    private GUIStyle sliderTrack, sliderThumb;
    private GUIStyle title, subtitle, cardTitle, cardText, small, big, back, stat, statLabel;
    private Texture2D white;
    private static readonly Color Accent = new Color(.32f, .72f, 1f);
    private static readonly Color Panel = new Color(.07f, .09f, .13f, .92f);
    private static readonly Color PanelHover = new Color(.11f, .15f, .22f, .96f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Attach()
    {
        var controller = FindFirstObjectByType<RocketAssemblyController>();
        if (controller != null && controller.GetComponent<LaunchMenu>() == null) controller.gameObject.AddComponent<LaunchMenu>();
        Open = true;
    }

    private void Awake()
    {
        assembly = GetComponent<RocketAssemblyController>();
        flight = GetComponent<RocketFlightModel>();
    }

    /// <summary>Back to the opening screen (from the assembly panel).</summary>
    public void Show() { Open = true; screen = Screen.Start; }

    /// <summary>Straight to the payload screen for the loaded real rocket.</summary>
    public void ShowPayload(int presetIndex) { Open = true; chosen = presetIndex; screen = Screen.Payload; }

    private void Styles()
    {
        if (title != null) return;
        white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply();
        title = new GUIStyle(GUI.skin.label) { fontSize = 46, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        title.normal.textColor = Color.white;
        subtitle = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter, wordWrap = true };
        subtitle.normal.textColor = new Color(.7f, .78f, .88f);
        cardTitle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, wordWrap = true };
        cardTitle.normal.textColor = Color.white;
        cardText = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
        cardText.normal.textColor = new Color(.78f, .83f, .9f);
        small = new GUIStyle(cardText) { fontSize = 12 };
        small.normal.textColor = new Color(.6f, .67f, .76f);
        stat = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
        stat.normal.textColor = Color.white;
        statLabel = new GUIStyle(small) { alignment = TextAnchor.UpperLeft };
        big = new GUIStyle(GUI.skin.button) { fontSize = 18, fontStyle = FontStyle.Bold };
        back = new GUIStyle(GUI.skin.button) { fontSize = 14 };
        sliderTrack = new GUIStyle(GUI.skin.horizontalSlider) { fixedHeight = 10, margin = new RectOffset(4, 4, 8, 8) };
        sliderThumb = new GUIStyle(GUI.skin.horizontalSliderThumb) { fixedHeight = 22, fixedWidth = 16 };
    }

    private void Fill(Rect r, Color c) { var old = GUI.color; GUI.color = c; GUI.DrawTexture(r, white); GUI.color = old; }

    // A clickable card: panel, accent bar, hover highlight.
    private bool Card(Rect r, int id, System.Action content)
    {
        var over = r.Contains(Event.current.mousePosition);
        if (over) hovered = id;
        Fill(r, over ? PanelHover : Panel);
        Fill(new Rect(r.x, r.y, 4, r.height), over ? Accent : Accent * new Color(1, 1, 1, .45f));
        GUILayout.BeginArea(new Rect(r.x + 22, r.y + 18, r.width - 40, r.height - 30));
        content();
        GUILayout.EndArea();
        return GUI.Button(r, GUIContent.none, GUIStyle.none);
    }

    private void OnGUI()
    {
        if (!Open || assembly == null) return;
        Styles();
        GUI.depth = -10;
        // Darken the scene behind.
        Fill(new Rect(0, 0, UnityEngine.Screen.width, UnityEngine.Screen.height), new Color(0, 0, 0, .55f));
        var w = UnityEngine.Screen.width; var h = UnityEngine.Screen.height;
        switch (screen)
        {
            case Screen.Start: StartScreen(w, h); break;
            case Screen.Rockets: RocketScreen(w, h); break;
            case Screen.Payload: PayloadScreen(w, h); break;
        }
    }

    private void StartScreen(float w, float h)
    {
        GUI.Label(new Rect(0, h * .14f, w, 60), "STRAUSS SPACE", title);
        GUI.Label(new Rect(w * .2f, h * .14f + 60, w * .6f, 30), "A real-scale rocket simulator: the real Earth, Moon and Sun, real engines, real staging.", subtitle);
        var cw = Mathf.Min(380, (w - 120) / 2); var ch = 220f; var y = h * .40f;
        var left = new Rect(w / 2 - cw - 20, y, cw, ch); var right = new Rect(w / 2 + 20, y, cw, ch);
        if (Card(left, 0, () =>
        {
            GUILayout.Label("BUILD A ROCKET", cardTitle);
            GUILayout.Space(8);
            GUILayout.Label("Start from an empty pad: choose engines, a mount frame and propellant tanks, and fly what you make.", cardText);
            GUILayout.FlexibleSpace();
            GUILayout.Label("Sandbox ▸", small);
        }))
        {
            if (assembly.IsPresetLoaded) assembly.ExitPreset();
            Open = false;
        }
        if (Card(right, 1, () =>
        {
            GUILayout.Label("LAUNCH A REAL ROCKET", cardTitle);
            GUILayout.Space(8);
            GUILayout.Label("Fly a historic or current launcher with its real engines, propellant, staging timeline and payload.", cardText);
            GUILayout.FlexibleSpace();
            GUILayout.Label(RocketPresets.All.Length + " rockets ▸", small);
        }))
            screen = Screen.Rockets;
    }

    private void RocketScreen(float w, float h)
    {
        GUI.Label(new Rect(0, h * .07f, w, 50), "CHOOSE A ROCKET", new GUIStyle(title) { fontSize = 34 });
        var n = RocketPresets.All.Length;
        var columns = Mathf.Clamp(Mathf.FloorToInt((w - 80) / 330), 1, 3);
        var cw = Mathf.Min(330, (w - 80 - (columns - 1) * 20) / columns);
        var rows = Mathf.CeilToInt(n / (float)columns);
        var top = h * .07f + 70;
        // Shrink the cards to fit however many rockets there are.
        var ch = Mathf.Clamp((h - top - 70) / rows - 18, 96f, 168f);
        var startX = (w - (columns * cw + (columns - 1) * 20)) / 2;
        for (var i = 0; i < n; i++)
        {
            var p = RocketPresets.All[i];
            var r = new Rect(startX + (i % columns) * (cw + 20), top + (i / columns) * (ch + 18), cw, ch);
            var index = i;
            if (Card(r, 10 + i, () =>
            {
                GUILayout.Label(p.name, cardTitle);
                GUILayout.Label(Summary(p), small);
                if (ch > 120) { GUILayout.Space(6); GUILayout.Label(string.IsNullOrEmpty(p.description) ? "" : p.description, cardText); }
                else GUILayout.Label(string.IsNullOrEmpty(p.description) ? "" : p.description, small);
            }))
            {
                chosen = index;
                assembly.LoadPreset(index);
                assembly.FocusRocket();
                screen = Screen.Payload;
            }
        }
        var by = top + rows * (ch + 18) + 6;
        if (GUI.Button(new Rect(w / 2 - 80, Mathf.Min(by, h - 56), 160, 36), "◂ Back", back)) screen = Screen.Start;
    }

    private static string Summary(RocketPresets.Preset p)
    {
        var height = p.bodyHeight + p.noseHeight + p.engineHeight;
        var stages = p.Staged ? p.upperStages.Length + 1 : 1;
        var frontal = Mathf.PI * p.bodyDiameter * p.bodyDiameter / 4;
        return height.ToString("F0") + " m × " + p.bodyDiameter.ToString("0.#") + " m (" + frontal.ToString("F0") + " m² frontal) · " + stages + (stages == 1 ? " stage" : " stages") +
               " · up to " + (p.payloadMax / 1000).ToString(p.payloadMax < 10000 ? "F1" : "F0") + " t to LEO";
    }

    private void PayloadScreen(float w, float h)
    {
        if (chosen < 0) { screen = Screen.Rockets; return; }
        var p = RocketPresets.All[chosen];
        var pw = Mathf.Min(560, w - 60); var ph = 460f;
        var r = new Rect((w - pw) / 2, Mathf.Max(30, h * .5f - ph / 2), pw, ph);
        Fill(r, Panel);
        Fill(new Rect(r.x, r.y, r.width, 4), Accent);
        GUILayout.BeginArea(new Rect(r.x + 28, r.y + 22, r.width - 56, r.height - 40));
        GUILayout.Label(p.name.ToUpperInvariant() + " · PAYLOAD", cardTitle);
        GUILayout.Label(Summary(p), small);
        GUILayout.Space(14);

        var max = Mathf.Max(p.payloadMax, p.payloadMass);
        GUILayout.Label("Payload: " + (p.payloadMass / 1000).ToString("F1") + " t   (max " + (max / 1000).ToString("F1") + " t)", stat);
        // One wide slider, 0 to the rocket's real maximum, in 0.1 t steps.
        GUILayout.Space(6);
        var value = GUILayout.HorizontalSlider(p.payloadMass, 0f, max, sliderTrack, sliderThumb, GUILayout.Height(22));
        if (Mathf.Abs(value - p.payloadMass) >= 50f) assembly.SetPayload(Mathf.Round(value / 100f) * 100f);
        GUILayout.BeginHorizontal();
        GUILayout.Label("0 t", small);
        GUILayout.FlexibleSpace();
        GUILayout.Label((max / 1000).ToString("F1") + " t", small);
        GUILayout.EndHorizontal();
        GUILayout.Space(10);

        // First-stage propellant load, 0-100% in 1% steps (upper stages
        // always go up full).
        GUILayout.Label("Fuel: " + (flight.Fill * 100).ToString("F0") + "%   (" + (flight.LiquidPropellant / 1000).ToString("N0") + " t of propellant)", stat);
        GUILayout.Space(6);
        var fill = GUILayout.HorizontalSlider(flight.Fill, 0f, 1f, sliderTrack, sliderThumb, GUILayout.Height(22));
        fill = Mathf.Round(fill * 100f) / 100f;
        if (Mathf.Abs(fill - flight.Fill) >= .005f) flight.SetFill(fill);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Empty", small);
        GUILayout.FlexibleSpace();
        GUILayout.Label("Full", small);
        GUILayout.EndHorizontal();
        GUILayout.Space(16);

        // Live figures for this load.
        flight.Prepare(1f);
        GUILayout.BeginHorizontal();
        Stat("Liftoff mass", (flight.TotalMass / 1000).ToString("N0") + " t");
        Stat("Thrust", (flight.Thrust / 1e6).ToString("F1") + " MN");
        var twr = flight.TWR;
        Stat("TWR", twr.ToString("F2"), twr < 1 ? new Color(1f, .45f, .4f) : Color.white);
        Stat("Δv (vac)", assembly.TotalDeltaV.ToString("N0") + " m/s");
        GUILayout.EndHorizontal();
        GUILayout.Space(6);
        GUILayout.Label(twr < 1 ? "Too heavy to lift off: lower the payload or the fuel." :
            "Payload rides on the last stage to the end: more payload, less Δv for every stage and a slower climb.", cardText);
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("◂ Rockets", back, GUILayout.Height(40), GUILayout.Width(140))) screen = Screen.Rockets;
        GUILayout.FlexibleSpace();
        GUI.enabled = twr >= 1;
        if (GUILayout.Button("TO THE LAUNCH PAD ▸", big, GUILayout.Height(40), GUILayout.Width(260))) Open = false;
        GUI.enabled = true;
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    private void Stat(string label, string value) => Stat(label, value, Color.white);
    private void Stat(string label, string value, Color color)
    {
        GUILayout.BeginVertical(GUILayout.Width(118));
        GUILayout.Label(label, statLabel);
        stat.normal.textColor = color;
        GUILayout.Label(value, stat);
        stat.normal.textColor = Color.white;
        GUILayout.EndVertical();
    }
}
