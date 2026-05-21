using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// On-screen overlay UI yang menampilkan data buoy dari DatabaseManager.
/// Setiap parameter bisa di-expand untuk menampilkan grafik historis.
/// Klik baris parameter untuk toggle graph.
/// 
/// Attach ke GameObject yang sama atau berbeda dengan DatabaseManager.
/// </summary>
public class BuoyDataOverlayUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DatabaseManager databaseManager;

    [Header("Display Settings")]
    [Tooltip("Tampilkan overlay UI")]
    [SerializeField] private bool showOverlay = true;

    [Tooltip("Lebar panel")]
    [SerializeField] private float panelWidth = 400f;

    [Tooltip("Tinggi graph saat expanded")]
    [SerializeField] private float graphHeight = 100f;

    // ─── Parameter Definitions ───────────────────────────────────
    private class ParamDef
    {
        public string key;
        public string label;
        public string unit;
        public Color color;
        public Func<BuoyData, float> getter;
        public Func<BuoyData, string> formatter;
        public bool expanded;
    }

    private List<ParamDef> paramDefs;
    private Dictionary<string, Texture2D> graphTextures = new Dictionary<string, Texture2D>();
    private int lastDataHash;

    // ─── Styles (lazy init) ──────────────────────────────────────
    private GUIStyle headerStyle, statusStyle, labelStyle, valueStyle, arrowStyle, graphInfoStyle;
    private bool stylesInit;

    private void Start()
    {
        InitParamDefs();
        if (databaseManager == null)
            databaseManager = FindObjectOfType<DatabaseManager>();
    }

    private void OnDestroy()
    {
        foreach (var tex in graphTextures.Values)
            if (tex != null) Destroy(tex);
    }

    private void InitParamDefs()
    {
        Color paramColor = new Color(0.7f, 0.75f, 0.8f);

        paramDefs = new List<ParamDef>
        {
            new ParamDef { key="WVHT", label="Wave Height", unit="m",
                color=paramColor,
                getter=d=>d.WVHT,
                formatter=d=>$"{d.WVHT:F2} m" },
            new ParamDef { key="WSPD", label="Wind Speed", unit="kn",
                color=paramColor,
                getter=d=>(float)d.WSPD,
                formatter=d=>$"{d.WSPD} kn ({d.WindSpeedMs:F1} m/s)" },
            new ParamDef { key="WDIR", label="Wind Direction", unit="°",
                color=paramColor,
                getter=d=>d.WindDirectionDeg,
                formatter=d=>$"{d.WDIR} ({d.WindDirectionDeg:F0}°)" },
            new ParamDef { key="GST", label="Gust Speed", unit="kn",
                color=paramColor,
                getter=d=>(float)d.GST,
                formatter=d=>$"{d.GST} kn ({d.GustSpeedMs:F1} m/s)" },
            new ParamDef { key="TEMP", label="Temperature", unit="°C",
                color=paramColor,
                getter=d=>d.TEMP,
                formatter=d=>$"{d.TEMP:F0} °C" },
            new ParamDef { key="HUMID", label="Humidity", unit="%",
                color=paramColor,
                getter=d=>d.HUMID,
                formatter=d=>$"{d.HUMID:F0} %" },
            new ParamDef { key="CDIR", label="Current Dir", unit="°",
                color=paramColor,
                getter=d=>d.CurrentDirectionDeg,
                formatter=d=>$"{d.CDIR} ({d.CurrentDirectionDeg:F0}°)" },
            new ParamDef { key="CSPD", label="Current Speed", unit="m/s",
                color=paramColor,
                getter=d=>d.CSPD,
                formatter=d=>$"{d.CSPD:F2} m/s" },
        };
    }

    // ═══════════════════════════════════════════════════════════════
    //  ON GUI
    // ═══════════════════════════════════════════════════════════════

    private void OnGUI()
    {
        if (!showOverlay || databaseManager == null) return;
        InitStyles();

        var cached = databaseManager.CachedData;
        BuoyData current = null;
        if (cached != null && cached.Count > 0)
            current = cached.OrderBy(k => k.Key).Last().Value;

        // ─── Calculate panel height ──────────────────────────────
        float lh = 22f;
        float ph = 76f; // header + status + collection
        if (current != null)
        {
            ph += lh * 2; // location + position rows
            foreach (var p in paramDefs)
            {
                ph += lh; // row
                if (p.expanded) ph += graphHeight + 24f;
            }
        }
        else
        {
            ph += lh;
        }

        float x = Screen.width - panelWidth - 15f;
        float y = 15f;

        // ─── Panel background ────────────────────────────────────
        GUI.color = new Color(0.04f, 0.04f, 0.08f, 0.88f);
        GUI.DrawTexture(new Rect(x, y, panelWidth, ph), Texture2D.whiteTexture);
        GUI.color = Color.white;

        // ─── Border ──────────────────────────────────────────────
        DrawBorder(new Rect(x, y, panelWidth, ph), new Color(0.2f, 0.5f, 0.8f, 0.5f));

        float cx = x + 14f;
        float cy = y + 10f;

        // ─── Header ──────────────────────────────────────────────
        string locName = current?.loc_name ?? databaseManager.CurrentCollection;
        GUI.Label(new Rect(cx, cy, panelWidth - 28f, lh), $"{locName.ToUpper()}", headerStyle);
        cy += lh + 2f;

        // Status line
        string col = databaseManager.CurrentCollection;
        bool loading = databaseManager.IsLoading;
        statusStyle.normal.textColor = loading ? Color.yellow : new Color(0.4f, 1f, 0.4f);
        // GUI.Label(new Rect(cx, cy, panelWidth - 28f, 16f), $"Collection: {col}  |  {(loading ? "Loading..." : $"{cached?.Count ?? 0} entries")}", statusStyle);
        // cy += 18f;

        // Position info
        if (current != null && current.posisi != null)
        {
            statusStyle.normal.textColor = new Color(0.6f, 0.6f, 0.7f);
            GUI.Label(new Rect(cx, cy, panelWidth - 28f, 16f), $"Position: {current.posisi}", statusStyle);
            cy += 18f;
        }

        // ─── Separator line ──────────────────────────────────────
        GUI.color = new Color(0.3f, 0.5f, 0.8f, 0.4f);
        GUI.DrawTexture(new Rect(cx, cy, panelWidth - 32f, 1f), Texture2D.whiteTexture);
        GUI.color = Color.white;
        cy += 6f;

        if (current == null)
        {
            GUI.Label(new Rect(cx, cy, panelWidth - 28f, lh), loading ? "Fetching data..." : "No data available.", labelStyle);
            return;
        }

        // ─── Parameter rows ──────────────────────────────────────
        for (int i = 0; i < paramDefs.Count; i++)
        {
            var p = paramDefs[i];
            DrawParameterRow(ref cy, x, cx, lh, panelWidth, p, current, i, cached);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  PARAMETER ROW (expandable)
    // ═══════════════════════════════════════════════════════════════

    private void DrawParameterRow(ref float cy, float panelX, float cx, float lh,
        float pw, ParamDef param, BuoyData current, int index,
        Dictionary<string, BuoyData> cached)
    {
        Rect rowRect = new Rect(panelX, cy, pw, lh);

        // Hover highlight
        bool hover = rowRect.Contains(Event.current.mousePosition);
        if (hover)
        {
            GUI.color = new Color(1f, 1f, 1f, 0.06f);
            GUI.DrawTexture(rowRect, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        // Alternating row tint
        if (index % 2 == 0)
        {
            GUI.color = new Color(1f, 1f, 1f, 0.03f);
            GUI.DrawTexture(rowRect, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        // Arrow
        arrowStyle.normal.textColor = param.color;
        GUI.Label(new Rect(cx, cy, 18f, lh), param.expanded ? "▼" : "▶", arrowStyle);

        // Label
        labelStyle.normal.textColor = param.color;
        GUI.Label(new Rect(cx + 20f, cy, 140f, lh), param.label, labelStyle);

        // Value
        string val = param.formatter(current);
        valueStyle.normal.textColor = Color.white;
        GUI.Label(new Rect(cx + 165f, cy, pw - 200f, lh), val, valueStyle);

        // Click to toggle
        if (GUI.Button(rowRect, GUIContent.none, GUIStyle.none))
        {
            param.expanded = !param.expanded;
            if (graphTextures.ContainsKey(param.key))
            {
                Destroy(graphTextures[param.key]);
                graphTextures.Remove(param.key);
            }
        }

        cy += lh;

        // ─── Expanded graph ──────────────────────────────────────
        if (param.expanded && cached != null && cached.Count >= 2)
        {
            DrawGraph(ref cy, cx, pw - 32f, param, cached);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  GRAPH RENDERING
    // ═══════════════════════════════════════════════════════════════

    private void DrawGraph(ref float cy, float gx, float gw, ParamDef param,
        Dictionary<string, BuoyData> cached)
    {
        float gh = graphHeight;

        // Graph background
        GUI.color = new Color(0.03f, 0.03f, 0.06f, 0.92f);
        GUI.DrawTexture(new Rect(gx - 2f, cy, gw + 4f, gh), Texture2D.whiteTexture);
        GUI.color = Color.white;

        // Get sorted values
        var sorted = cached.OrderBy(k => k.Key).ToList();
        float[] values = sorted.Select(k => param.getter(k.Value)).ToArray();
        string[] timeLabels = sorted.Select(k =>
        {
            var d = k.Value;
            return d.time != null && d.time.Length >= 2 ? d.time.Substring(0, 5) : k.Key;
        }).ToArray();

        // Get or create texture
        int hash = ComputeHash(values);
        if (!graphTextures.ContainsKey(param.key) || lastDataHash != hash)
        {
            if (graphTextures.ContainsKey(param.key) && graphTextures[param.key] != null)
                Destroy(graphTextures[param.key]);
            graphTextures[param.key] = GenerateGraphTexture((int)gw, (int)gh, values, param.color);
            lastDataHash = hash;
        }

        if (graphTextures.ContainsKey(param.key) && graphTextures[param.key] != null)
            GUI.DrawTexture(new Rect(gx, cy, gw, gh), graphTextures[param.key]);

        // Stats labels
        float minV = values.Min(), maxV = values.Max(), avgV = values.Average();
        graphInfoStyle.alignment = TextAnchor.UpperLeft;
        GUI.Label(new Rect(gx + 4f, cy + 2f, 110f, 14f), $"max: {maxV:F1}", graphInfoStyle);
        graphInfoStyle.alignment = TextAnchor.LowerLeft;
        GUI.Label(new Rect(gx + 4f, cy + gh - 14f, 110f, 14f), $"min: {minV:F1}", graphInfoStyle);
        graphInfoStyle.alignment = TextAnchor.UpperRight;
        GUI.Label(new Rect(gx + gw - 114f, cy + 2f, 110f, 14f), $"avg: {avgV:F1}", graphInfoStyle);

        cy += gh + 2f;

        // Time axis
        int labelCount = Mathf.Min(sorted.Count, 6);
        float step = (float)(sorted.Count - 1) / Mathf.Max(labelCount - 1, 1);
        graphInfoStyle.alignment = TextAnchor.MiddleCenter;
        for (int i = 0; i < labelCount; i++)
        {
            int idx = Mathf.Clamp(Mathf.RoundToInt(i * step), 0, sorted.Count - 1);
            float lx = gx + (gw * idx / Mathf.Max(sorted.Count - 1, 1)) - 22f;
            GUI.Label(new Rect(lx, cy, 48f, 14f), timeLabels[idx], graphInfoStyle);
        }
        cy += 18f;
    }

    // ═══════════════════════════════════════════════════════════════
    //  GRAPH TEXTURE GENERATION
    // ═══════════════════════════════════════════════════════════════

    private Texture2D GenerateGraphTexture(int w, int h, float[] values, Color lineColor)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        Color[] px = new Color[w * h];
        Color bg = new Color(0.04f, 0.04f, 0.07f, 0f);
        for (int i = 0; i < px.Length; i++) px[i] = bg;

        // Grid lines
        Color grid = new Color(0.18f, 0.18f, 0.25f, 0.4f);
        for (int g = 0; g <= 4; g++)
        {
            int gy = Mathf.Clamp(h * g / 4, 0, h - 1);
            for (int gx = 0; gx < w; gx++) px[gy * w + gx] = grid;
        }

        float minV = values.Min(), maxV = values.Max();
        float range = maxV - minV;
        if (range < 0.001f) range = 1f;
        int pad = 6;
        int gh = h - pad * 2;

        // Filled area
        Color fill = new Color(lineColor.r, lineColor.g, lineColor.b, 0.12f);
        for (int i = 0; i < values.Length - 1; i++)
        {
            float x0 = (float)i / (values.Length - 1) * (w - 1);
            float x1 = (float)(i + 1) / (values.Length - 1) * (w - 1);
            float y0 = pad + ((values[i] - minV) / range) * gh;
            float y1 = pad + ((values[i + 1] - minV) / range) * gh;
            for (int pxi = (int)x0; pxi <= (int)x1 && pxi < w; pxi++)
            {
                float t = (x1 - x0) > 0 ? (pxi - x0) / (x1 - x0) : 0;
                int py = Mathf.Clamp((int)Mathf.Lerp(y0, y1, t), 0, h - 1);
                for (int fy = 0; fy <= py; fy++) px[fy * w + pxi] = fill;
            }
        }

        // Line (2px thick)
        for (int i = 0; i < values.Length - 1; i++)
        {
            float x0 = (float)i / (values.Length - 1) * (w - 1);
            float x1 = (float)(i + 1) / (values.Length - 1) * (w - 1);
            float y0 = pad + ((values[i] - minV) / range) * gh;
            float y1 = pad + ((values[i + 1] - minV) / range) * gh;
            DrawLine(px, w, h, x0, y0, x1, y1, lineColor, 2);
        }

        // Dots
        for (int i = 0; i < values.Length; i++)
        {
            float dx = (float)i / (values.Length - 1) * (w - 1);
            float dy = pad + ((values[i] - minV) / range) * gh;
            DrawDot(px, w, h, (int)dx, (int)dy, 2, Color.white);
        }

        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    private void DrawLine(Color[] px, int w, int h, float x0, float y0, float x1, float y1, Color c, int thick)
    {
        int steps = Mathf.Max((int)(Mathf.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)) * 2), 1);
        for (int s = 0; s <= steps; s++)
        {
            float t = (float)s / steps;
            int pxX = (int)Mathf.Lerp(x0, x1, t);
            int pxY = (int)Mathf.Lerp(y0, y1, t);
            for (int dx = -thick / 2; dx <= thick / 2; dx++)
                for (int dy = -thick / 2; dy <= thick / 2; dy++)
                {
                    int fx = pxX + dx, fy = pxY + dy;
                    if (fx >= 0 && fx < w && fy >= 0 && fy < h) px[fy * w + fx] = c;
                }
        }
    }

    private void DrawDot(Color[] px, int w, int h, int cx, int cy, int r, Color c)
    {
        for (int dx = -r; dx <= r; dx++)
            for (int dy = -r; dy <= r; dy++)
                if (dx * dx + dy * dy <= r * r)
                {
                    int fx = cx + dx, fy = cy + dy;
                    if (fx >= 0 && fx < w && fy >= 0 && fy < h) px[fy * w + fx] = c;
                }
    }

    private void DrawBorder(Rect rect, Color color)
    {
        GUI.color = color;
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 1), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.x, rect.yMax - 1, rect.width, 1), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.x, rect.y, 1, rect.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.xMax - 1, rect.y, 1, rect.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    // ═══════════════════════════════════════════════════════════════
    //  STYLES & UTILITIES
    // ═══════════════════════════════════════════════════════════════

    private void InitStyles()
    {
        if (stylesInit) return;
        stylesInit = true;

        headerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 15, fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };
        statusStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            normal = { textColor = new Color(0.7f, 0.7f, 0.7f) }
        };
        labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            normal = { textColor = new Color(0.8f, 0.8f, 0.8f) }
        };
        valueStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12, fontStyle = FontStyle.Normal,
            normal = { textColor = Color.white },
            alignment = TextAnchor.MiddleRight
        };
        arrowStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12, fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        graphInfoStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 10,
            normal = { textColor = new Color(0.55f, 0.55f, 0.6f) }
        };
    }

    private int ComputeHash(float[] values)
    {
        unchecked
        {
            int h = 17;
            foreach (var v in values) h = h * 31 + v.GetHashCode();
            return h;
        }
    }
}
