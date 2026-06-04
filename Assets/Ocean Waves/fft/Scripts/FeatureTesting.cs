using UnityEngine;

public class FeatureTesting : MonoBehaviour
{
    [Header("References")]
    [SerializeField] WavesGenerator wavesGenerator;
    [SerializeField] OceanGeometry oceanGeometry;

    [Header("Optimisations Toggles")]
    [Tooltip("Jika aktif, spektrum JONSWAP hanya dihitung saat parameter bergeser. Jika nonaktif, dihitung ulang setiap frame.")]
    public bool enableHashChangeDetection = true;

    [Tooltip("Jika aktif, menggunakan AsyncGPUReadback. Jika nonaktif, menggunakan ReadPixels sinkronus (menyebabkan GPU stall).")]
    public bool enableAsyncReadback = true;

    [Tooltip("Jika aktif, menggunakan Clipmap Material LOD. Jika nonaktif, memaksa material detail tertinggi (CLOSE) di semua jarak.")]
    public bool enableMaterialLOD = true;

    [Header("UI Settings")]
    [SerializeField] bool showDebugUI = true;
    [SerializeField] bool minimized = false;

    // Variabel kalkulasi FPS
    float deltaTime = 0.0f;
    float msec;
    float fps;

    private GUIStyle tooltipStyle;
    private GUIStyle minimizeBtnStyle;

    private void Start()
    {
        // Cari otomatis jika referensi kosong
        if (wavesGenerator == null)
            wavesGenerator = FindFirstObjectByType<WavesGenerator>();
        if (oceanGeometry == null)
            oceanGeometry = FindFirstObjectByType<OceanGeometry>();

        // Sinkronisasi status awal
        ApplySettings();
    }

    private void Update()
    {
        // Hitung FPS secara real-time
        deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;
        msec = deltaTime * 1000.0f;
        fps = 1.0f / deltaTime;

        // Terapkan toggle optimasi
        ApplySettings();
    }

    void ApplySettings()
    {
        if (wavesGenerator != null)
        {
            // optimize = NOT recalculate always
            wavesGenerator.alwaysRecalculateInitials = !enableHashChangeDetection;
            wavesGenerator.useAsyncReadback = enableAsyncReadback;
        }

        if (oceanGeometry != null)
        {
            oceanGeometry.useMaterialLOD = enableMaterialLOD;
        }
    }

    private void InitStyles()
    {
        if (tooltipStyle != null) return;

        tooltipStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };

        minimizeBtnStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.7f, 0.7f, 0.7f) },
            hover = { textColor = Color.white },
            active = { textColor = Color.white },
        };
        minimizeBtnStyle.normal.background = null;
        minimizeBtnStyle.hover.background = null;
        minimizeBtnStyle.active.background = null;
    }

    private void OnGUI()
    {
        if (!showDebugUI) return;
        InitStyles();

        float boxSize = 36f;
        float panelWidth = 340f;
        float panelHeight = 230f;

        if (minimized)
        {
            float mx = 15f;
            float my = Screen.height - boxSize - 15f;

            Rect minRect = new Rect(mx, my, boxSize, boxSize);
            
            // Draw background
            GUI.color = new Color(0.04f, 0.04f, 0.08f, 0.88f);
            GUI.DrawTexture(minRect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            DrawBorder(minRect, new Color(0.2f, 0.5f, 0.8f, 0.5f));

            // Emoji icon
            GUIStyle iconStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white },
                hover = { textColor = new Color(0.25f, 0.55f, 0.9f) },
                active = { textColor = new Color(0.25f, 0.55f, 0.9f) }
            };
            iconStyle.normal.background = null;
            iconStyle.hover.background = null;
            iconStyle.active.background = null;

            if (GUI.Button(minRect, "⚙️", iconStyle))
                minimized = false;

            // Tooltip
            if (minRect.Contains(Event.current.mousePosition))
            {
                string tooltipText = "Tampilkan Benchmarker Performa (⚙️)";
                Vector2 tooltipSize = tooltipStyle.CalcSize(new GUIContent(tooltipText));
                tooltipSize.x += 12f;
                tooltipSize.y += 6f;

                float ttX = mx + boxSize + 8f;
                float ttY = my + (boxSize - tooltipSize.y) / 2f;

                Rect ttRect = new Rect(ttX, ttY, tooltipSize.x, tooltipSize.y);
                GUI.color = new Color(0.04f, 0.04f, 0.08f, 0.92f);
                GUI.DrawTexture(ttRect, Texture2D.whiteTexture);
                
                // Border
                DrawBorder(ttRect, new Color(0.2f, 0.5f, 0.8f, 0.5f));
                GUI.color = Color.white;

                GUI.Label(new Rect(ttX + 6f, ttY + 3f, tooltipSize.x, tooltipSize.y), tooltipText, tooltipStyle);
            }
            return;
        }

        // Draw at Bottom-Left dynamically based on Screen.height
        float x = 15f;
        float y = Screen.height - panelHeight - 15f;

        Rect rect = new Rect(x, y, panelWidth, panelHeight);
        GUI.color = new Color(0.04f, 0.04f, 0.08f, 0.88f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
        DrawBorder(rect, new Color(0.2f, 0.5f, 0.8f, 0.5f));

        GUI.Label(new Rect(x + 10f, y + 4f, panelWidth - 44f, 20f), "⚙️  PERFORMANCE BENCHMARK", new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, normal = { textColor = Color.white } });

        // Minimize button in header
        if (GUI.Button(new Rect(x + panelWidth - 36f, y + 2f, 28f, 20f), "─", minimizeBtnStyle))
            minimized = true;
        
        GUILayout.BeginArea(new Rect(x + 10f, y + 25f, panelWidth - 20f, panelHeight - 35f));
        GUILayout.Space(10);

        // Menampilkan teks FPS dengan indikasi warna
        string fpsText = string.Format("FPS: {0:0.0} ({1:0.0} ms)", fps, msec);
        GUIStyle fpsStyle = new GUIStyle(GUI.skin.label);
        fpsStyle.fontStyle = FontStyle.Bold;
        fpsStyle.fontSize = 14;

        if (fps >= 60.0f)
            fpsStyle.normal.textColor = Color.green;
        else if (fps >= 30.0f)
            fpsStyle.normal.textColor = Color.yellow;
        else
            fpsStyle.normal.textColor = Color.red;

        GUILayout.Label(fpsText, fpsStyle);
        GUILayout.Space(10);

        // Checkbox Toggles untuk mengontrol optimalisasi
        enableHashChangeDetection = GUILayout.Toggle(enableHashChangeDetection, " Enable Hash Change Detection (CPU)");
        GUILayout.Space(5);
        enableAsyncReadback = GUILayout.Toggle(enableAsyncReadback, " Enable Async GPU Readback (GPU-to-CPU)");
        GUILayout.Space(5);
        enableMaterialLOD = GUILayout.Toggle(enableMaterialLOD, " Enable Material LOD (GPU Shading)");

        GUILayout.Space(15);
        
        // Tombol Reset ke mode optimal (Default)
        if (GUILayout.Button("Reset ke Mode Optimal (Semua Aktif)"))
        {
            enableHashChangeDetection = true;
            enableAsyncReadback = true;
            enableMaterialLOD = true;
        }

        GUILayout.EndArea();
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
}
