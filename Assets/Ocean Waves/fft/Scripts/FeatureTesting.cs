using UnityEngine;
using UnityEngine.InputSystem;

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

    [Tooltip("Jika aktif, kalkulasi FFT berjalan di GPU. Jika nonaktif, beralih ke kalkulasi Gerstner Waves di CPU.")]
    public bool enableGPUSimulation = true;

    [Tooltip("Jika aktif, menggunakan Clipmap Material LOD. Jika nonaktif, memaksa material detail tertinggi (CLOSE) di semua jarak.")]
    public bool enableMaterialLOD = true;

    [Header("UI Settings")]
    [Tooltip("Apakah UI pengujian dimunculkan secara default saat aplikasi dimulai?")]
    [SerializeField] bool showUIOnStart = true;

    private bool showDebugUI;
    private GUIStyle tooltipStyle;

    private void Start()
    {
        showDebugUI = showUIOnStart;
        if (wavesGenerator == null)
            wavesGenerator = FindFirstObjectByType<WavesGenerator>();
        if (oceanGeometry == null)
            oceanGeometry = FindFirstObjectByType<OceanGeometry>();

        // Tambahkan FPSDisplay secara otomatis jika belum ada
        if (gameObject.GetComponent<FPSDisplay>() == null)
        {
            gameObject.AddComponent<FPSDisplay>();
        }

        // Sinkronisasi status awal
        ApplySettings();
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.zKey.wasPressedThisFrame)
        {
            showDebugUI = !showDebugUI;
        }
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
            wavesGenerator.useGPUSimulation = enableGPUSimulation;
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
    }

    private void OnGUI()
    {
        if (!showDebugUI) return;
        InitStyles();

        float boxSize = 36f;
        float panelWidth = 340f;
        float panelHeight = 215f;

        // Draw at Bottom-Left dynamically based on Screen.height
        float x = 15f;
        float y = Screen.height - panelHeight - 15f;

        Rect rect = new Rect(x, y, panelWidth, panelHeight);
        GUI.color = new Color(0.04f, 0.04f, 0.08f, 0.88f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
        DrawBorder(rect, new Color(0.2f, 0.5f, 0.8f, 0.5f));

        GUI.Label(new Rect(x + 10f, y + 4f, panelWidth - 44f, 20f), "⚙️  PERFORMANCE BENCHMARK", new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, normal = { textColor = Color.white } });

        GUILayout.BeginArea(new Rect(x + 10f, y + 25f, panelWidth - 20f, panelHeight - 35f));
        GUILayout.Space(10);

        // Checkbox Toggles untuk mengontrol optimalisasi
        enableGPUSimulation = GUILayout.Toggle(enableGPUSimulation, " Enable GPU Simulation (Compute Shader)");
        GUILayout.Space(5);
        enableHashChangeDetection = GUILayout.Toggle(enableHashChangeDetection, " Enable Hash Change Detection (CPU)");
        GUILayout.Space(5);
        enableAsyncReadback = GUILayout.Toggle(enableAsyncReadback, " Enable Async GPU Readback (GPU-to-CPU)");
        GUILayout.Space(5);
        enableMaterialLOD = GUILayout.Toggle(enableMaterialLOD, " Enable Material LOD (GPU Shading)");

        GUILayout.Space(15);
        
        // Tombol Reset ke mode optimal (Default)
        if (GUILayout.Button("Reset ke Mode Optimal (Semua Aktif)"))
        {
            enableGPUSimulation = true;
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
