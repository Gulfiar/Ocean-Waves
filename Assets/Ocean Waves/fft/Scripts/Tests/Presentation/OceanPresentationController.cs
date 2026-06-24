using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Controller presentasi bertahap untuk simulasi gelombang laut FFT.
/// Mendemonstrasikan alur pipeline simulasi sesuai skripsi:
///
///   Tahap 1: Plane Datar                  — Mesh dasar tanpa deformasi
///   Tahap 2: Geometri Clipmap (LOD)       — Struktur mesh multi-level
///   Tahap 3: Gaussian Noise               — Bilangan acak sebagai fase awal
///   Tahap 4: Spektrum JONSWAP             — Pembangkitan spektrum energi gelombang
///   Tahap 5: IFFT → Displacement          — Transformasi domain frekuensi ke spasial
///   Tahap 6: Choppy Waves (Lambda)        — Pergeseran horizontal (choppy waves)
///   Tahap 7: Multi-Cascade                — Tiga skala grid (swell, wind, ripple)
///   Tahap 8: Shading & Foam              — Material PBR lengkap + Jacobian foam
///   Tahap 9: Integrasi Data Buoy          — Data real-time dari Firebase Firestore
///
/// Cara pakai:
///   1. Attach ke GameObject di scene yang memiliki WavesGenerator & OceanGeometry.
///   2. Assign referensi di Inspector.
///   3. Tekan tombol 1-9 untuk langsung ke tahap tertentu,
///      atau panah kanan/kiri untuk maju/mundur.
///   4. Atau aktifkan autoAdvance untuk mode otomatis.
/// </summary>
public class OceanPresentationController : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════
    //  REFERENSI
    // ════════════════════════════════════════════════════════════════════

    [Header("Referensi Scene")]
    [Tooltip("WavesGenerator utama di scene")]
    [SerializeField] private WavesGenerator wavesGenerator;

    [Tooltip("OceanGeometry yang mengelola mesh clipmap")]
    [SerializeField] private OceanGeometry oceanGeometry;

    [Tooltip("Material laut (ocean shader)")]
    [SerializeField] private Material oceanMaterial;

    [Tooltip("BuoyDataApplier untuk tahap integrasi data (opsional)")]
    [SerializeField] private BuoyDataApplier buoyDataApplier;

    [Tooltip("Kamera utama untuk presentasi")]
    [SerializeField] private Camera presentationCamera;

    // ════════════════════════════════════════════════════════════════════
    //  PENGATURAN PRESENTASI
    // ════════════════════════════════════════════════════════════════════

    [Header("Pengaturan Presentasi")]
    [Tooltip("Tahap saat ini (1-9)")]
    [SerializeField, Range(1, 9)] private int currentStage = 1;

    [Tooltip("Durasi transisi antar-tahap (detik)")]
    [SerializeField] private float transitionDuration = 1.5f;

    [Tooltip("Aktifkan mode auto-advance")]
    [SerializeField] private bool autoAdvance = false;

    [Tooltip("Durasi per tahap saat auto-advance (detik)")]
    [SerializeField] private float autoAdvanceDuration = 8f;

    [Tooltip("Tampilkan overlay UI informasi tahap")]
    [SerializeField] private bool showStageUI = true;

    // ════════════════════════════════════════════════════════════════════
    //  STATE INTERNAL
    // ════════════════════════════════════════════════════════════════════

    private int previousStage = -1;
    private bool isTransitioning = false;
    private float transitionProgress = 0f;

    // Simple plane untuk Tahap 1
    private GameObject simplePlane;

    // Cached original values
    private float originalLambda;
    private float originalTimeScale;
    private int originalClipLevels;
    private float originalLengthScale;
    private bool originalUseMaterialLOD;
    private bool originalShowMaterialLods;

    // Stage descriptions
    private static readonly string[] stageNames = new string[]
    {
        "",
        "Tahap 1: Plane Datar (Mesh Dasar)",
        "Tahap 2: Geometri Clipmap (Level of Detail)",
        "Tahap 3: Gaussian Noise (Bilangan Acak)",
        "Tahap 4: Spektrum Energi JONSWAP",
        "Tahap 5: IFFT 2D → Displacement Map",
        "Tahap 6: Choppy Waves (Pergeseran Lambda)",
        "Tahap 7: Multi-Cascade (3 Skala Grid)",
        "Tahap 8: Shading PBR & Jacobian Foam",
        "Tahap 9: Integrasi Data Buoy (Firebase)"
    };

    private static readonly string[] stageDescriptions = new string[]
    {
        "",
        "Langkah pertama: Membuat bidang datar (plane) sebagai\nrepresentasi permukaan laut sebelum deformasi.\nMesh sederhana N×N vertex tanpa displacement.",

        "Struktur mesh menggunakan Geometric Clipmap\ndengan beberapa level LOD (Level of Detail).\nResolusi tinggi di dekat kamera, rendah di kejauhan\nuntuk efisiensi rendering.",

        "Membangkitkan tekstur Gaussian Noise (ξr + iξi)\nsebagai fase acak awal gelombang.\nDistribusi normal μ=0, σ=1 menggunakan\nmetode Box-Muller Transform.",

        "Menghitung spektrum energi JONSWAP S(ω)\nberdasarkan parameter angin (U, θ).\nDikombinasikan dengan koreksi kedalaman TMA\ndan directional spreading (Cosine-2s).",

        "Transformasi Inverse FFT 2D pada GPU:\nh(x,t) = IFFT2D(h(k,t))\nMengonversi domain frekuensi → domain spasial\nmenghasilkan displacement map ketinggian air.",

        "Menambahkan pergeseran horizontal (λ)\nuntuk efek gelombang tajam (choppy waves):\nDx = IFFT2D(-i·kx/|k| · h(k,t))\nDz = IFFT2D(-i·kz/|k| · h(k,t))",

        "Tiga kaskade skala grid independen:\n• Cascade 0 (L=250m): Gelombang swell\n• Cascade 1 (L=17m): Gelombang angin lokal\n• Cascade 2 (L=5m): Riak air halus (ripple)",

        "Material PBR dengan vertex/fragment shader:\n• Subsurface Scattering + Fresnel\n• Normal mapping dari derivatives\n• Jacobian foam detection (J < 0)\n• Turbulence map untuk buih dinamis",

        "Integrasi data observasi buoy via REST API:\n• Firebase Firestore → WSPD, WDIR, WVHT\n• Newton-Raphson inverse SMB → Fetch\n• Parameter JONSWAP diperbarui real-time\n• AsyncGPUReadback untuk fisika buoy 3D"
    };

    // ════════════════════════════════════════════════════════════════════
    //  LIFECYCLE
    // ════════════════════════════════════════════════════════════════════

    private void Start()
    {
        CacheOriginalValues();

        // Set stage awal
        if (currentStage < 1) currentStage = 1;
        ApplyStage(currentStage, immediate: true);
        previousStage = currentStage;

        if (autoAdvance)
        {
            StartCoroutine(AutoAdvanceCoroutine());
        }
    }

    private void Update()
    {
        HandleInput();

        // Jika stage berubah (dari Inspector atau input)
        if (currentStage != previousStage)
        {
            ApplyStage(currentStage, immediate: false);
            previousStage = currentStage;
        }

        // Handle smooth transitions
        if (isTransitioning)
        {
            transitionProgress += Time.deltaTime / transitionDuration;
            if (transitionProgress >= 1f)
            {
                transitionProgress = 1f;
                isTransitioning = false;
            }
            UpdateTransition(transitionProgress);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  INPUT HANDLING
    // ════════════════════════════════════════════════════════════════════

    private void HandleInput()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        // Tombol 1-9 untuk langsung ke tahap tertentu
        Key[] numberKeys = {
            Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5,
            Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9
        };
        Key[] numpadKeys = {
            Key.Numpad1, Key.Numpad2, Key.Numpad3, Key.Numpad4, Key.Numpad5,
            Key.Numpad6, Key.Numpad7, Key.Numpad8, Key.Numpad9
        };
        for (int i = 0; i < 9; i++)
        {
            if (keyboard[numberKeys[i]].wasPressedThisFrame || keyboard[numpadKeys[i]].wasPressedThisFrame)
            {
                GoToStage(i + 1);
                return;
            }
        }

        // Panah kanan: maju
        if (keyboard[Key.RightArrow].wasPressedThisFrame || keyboard[Key.D].wasPressedThisFrame)
        {
            NextStage();
        }

        // Panah kiri: mundur
        if (keyboard[Key.LeftArrow].wasPressedThisFrame || keyboard[Key.A].wasPressedThisFrame)
        {
            PreviousStage();
        }

        // Spasi: toggle auto-advance
        if (keyboard[Key.Space].wasPressedThisFrame)
        {
            autoAdvance = !autoAdvance;
            if (autoAdvance)
                StartCoroutine(AutoAdvanceCoroutine());
        }

        // R: reset ke tahap 1
        if (keyboard[Key.R].wasPressedThisFrame)
        {
            GoToStage(1);
        }

        // F: toggle fullscreen mode (LOD visualization)
        if (keyboard[Key.F].wasPressedThisFrame)
        {
            if (oceanGeometry != null)
                oceanGeometry.ShowMaterialLods = !oceanGeometry.ShowMaterialLods;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  PUBLIC API
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Pindah ke tahap tertentu.
    /// </summary>
    public void GoToStage(int stage)
    {
        currentStage = Mathf.Clamp(stage, 1, 9);
    }

    /// <summary>
    /// Maju ke tahap berikutnya.
    /// </summary>
    public void NextStage()
    {
        if (currentStage < 9)
            currentStage++;
    }

    /// <summary>
    /// Mundur ke tahap sebelumnya.
    /// </summary>
    public void PreviousStage()
    {
        if (currentStage > 1)
            currentStage--;
    }

    /// <summary>
    /// Mendapatkan nama tahap saat ini.
    /// </summary>
    public string GetCurrentStageName()
    {
        return stageNames[Mathf.Clamp(currentStage, 1, 9)];
    }

    /// <summary>
    /// Mendapatkan deskripsi tahap saat ini.
    /// </summary>
    public string GetCurrentStageDescription()
    {
        return stageDescriptions[Mathf.Clamp(currentStage, 1, 9)];
    }

    // ════════════════════════════════════════════════════════════════════
    //  STAGE APPLICATION — INTI LOGIKA TAHAPAN
    // ════════════════════════════════════════════════════════════════════

    private void CacheOriginalValues()
    {
        if (wavesGenerator != null)
        {
            originalTimeScale = wavesGenerator.timeScale;
            // Lambda akan di-cache dari cascade jika tersedia
            if (wavesGenerator.cascade0 != null)
                originalLambda = wavesGenerator.cascade0.Lambda;
        }

        if (oceanGeometry != null)
        {
            originalClipLevels = oceanGeometry.ClipLevels;
            originalLengthScale = oceanGeometry.LengthScale;
            originalUseMaterialLOD = oceanGeometry.useMaterialLOD;
            originalShowMaterialLods = oceanGeometry.ShowMaterialLods;
        }
    }

    /// <summary>
    /// Menerapkan konfigurasi visual untuk tahap tertentu.
    /// </summary>
    private void ApplyStage(int stage, bool immediate)
    {
        if (!immediate)
        {
            isTransitioning = true;
            transitionProgress = 0f;
        }

        Debug.Log($"[Presentasi] {stageNames[stage]}");

        switch (stage)
        {
            case 1: ApplyStage1_FlatPlane(); break;
            case 2: ApplyStage2_ClipmapLOD(); break;
            case 3: ApplyStage3_GaussianNoise(); break;
            case 4: ApplyStage4_JONSWAPSpectrum(); break;
            case 5: ApplyStage5_IFFTDisplacement(); break;
            case 6: ApplyStage6_ChoppyWaves(); break;
            case 7: ApplyStage7_MultiCascade(); break;
            case 8: ApplyStage8_ShadingFoam(); break;
            case 9: ApplyStage9_BuoyIntegration(); break;
        }
    }

    // ──────────────────────────────────────────────────────────────────
    //  TAHAP 1: PLANE DATAR
    //  Menampilkan mesh datar tanpa deformasi gelombang.
    //  Merepresentasikan kondisi awal sebelum simulasi dimulai.
    // ──────────────────────────────────────────────────────────────────
    private void ApplyStage1_FlatPlane()
    {
        // Hentikan simulasi gelombang
        SetSimulationActive(false);

        // Matikan displacement pada shader
        SetShaderDisplacement(false);

        // Sembunyikan OceanGeometry clipmap — tampilkan plane sederhana saja
        ShowOceanGeometry(false);
        ShowSimplePlane(true);

        // Material: warna solid biru muda (flat shading)
        SetMaterialMode(MaterialMode.FlatColor);

        // Lambda = 0 (tidak ada choppy waves)
        SetLambda(0f);
    }

    // ──────────────────────────────────────────────────────────────────
    //  TAHAP 2: GEOMETRI CLIPMAP (LOD)
    //  Menunjukkan struktur mesh Geometric Clipmap dengan
    //  beberapa level resolusi. Visualisasi warna per LOD level.
    // ──────────────────────────────────────────────────────────────────
    private void ApplyStage2_ClipmapLOD()
    {
        SetSimulationActive(false);
        SetShaderDisplacement(false);

        // Sembunyikan plane sederhana — tampilkan OceanGeometry clipmap
        ShowSimplePlane(false);
        ShowOceanGeometry(true);

        // Aktifkan multi-level clipmap
        if (oceanGeometry != null)
        {
            oceanGeometry.ClipLevels = originalClipLevels;
            oceanGeometry.useMaterialLOD = true;
            // Tampilkan warna berbeda per LOD level untuk visualisasi
            oceanGeometry.ShowMaterialLods = true;
        }

        SetMaterialMode(MaterialMode.LODVisualization);
        SetLambda(0f);
    }

    // ──────────────────────────────────────────────────────────────────
    //  TAHAP 3: GAUSSIAN NOISE
    //  Menampilkan tekstur noise Gaussian yang digunakan
    //  sebagai fase acak awal (ξr + iξi) dalam pembangkitan
    //  spektrum gelombang. Visualisasi noise pada mesh.
    // ──────────────────────────────────────────────────────────────────
    private void ApplyStage3_GaussianNoise()
    {
        SetSimulationActive(false);
        SetShaderDisplacement(false);

        ShowSimplePlane(false);
        ShowOceanGeometry(true);

        if (oceanGeometry != null)
        {
            oceanGeometry.ClipLevels = originalClipLevels;
            oceanGeometry.useMaterialLOD = false;
            oceanGeometry.ShowMaterialLods = false;
        }

        // Tampilkan tekstur Gaussian noise pada material
        SetMaterialMode(MaterialMode.GaussianNoise);
        SetLambda(0f);
    }

    // ──────────────────────────────────────────────────────────────────
    //  TAHAP 4: SPEKTRUM JONSWAP
    //  Mengaktifkan perhitungan spektrum energi JONSWAP di GPU.
    //  Menampilkan Initial Spectrum texture (h0(k)) yang
    //  merepresentasikan distribusi energi gelombang.
    // ──────────────────────────────────────────────────────────────────
    private void ApplyStage4_JONSWAPSpectrum()
    {
        // Aktifkan kalkulasi initial spectrum saja (belum animasi)
        SetSimulationActive(true);

        // Hentikan waktu agar tidak bergerak
        if (wavesGenerator != null)
            wavesGenerator.timeScale = 0f;

        SetShaderDisplacement(false);

        ShowSimplePlane(false);
        ShowOceanGeometry(true);

        if (oceanGeometry != null)
        {
            oceanGeometry.ClipLevels = originalClipLevels;
            oceanGeometry.useMaterialLOD = false;
            oceanGeometry.ShowMaterialLods = false;
        }

        // Tampilkan initial spectrum texture pada mesh
        SetMaterialMode(MaterialMode.InitialSpectrum);
        SetLambda(0f);
    }

    // ──────────────────────────────────────────────────────────────────
    //  TAHAP 5: IFFT → DISPLACEMENT MAP
    //  Mengaktifkan IFFT 2D dan menampilkan hasil displacement.
    //  Gelombang mulai bergerak tetapi hanya vertikal (Y),
    //  tanpa pergeseran horizontal (lambda = 0).
    // ──────────────────────────────────────────────────────────────────
    private void ApplyStage5_IFFTDisplacement()
    {
        SetSimulationActive(true);

        // Aktifkan kembali waktu
        if (wavesGenerator != null)
            wavesGenerator.timeScale = 1.0f;

        // Aktifkan displacement pada shader — hanya vertikal
        SetShaderDisplacement(true);

        ShowSimplePlane(false);
        ShowOceanGeometry(true);

        if (oceanGeometry != null)
        {
            oceanGeometry.ClipLevels = originalClipLevels;
            oceanGeometry.useMaterialLOD = false;
            oceanGeometry.ShowMaterialLods = false;
        }

        // Material: wireframe atau height-based coloring
        SetMaterialMode(MaterialMode.HeightVisualization);

        // Lambda = 0 → hanya perpindahan vertikal
        SetLambda(0f);
    }

    // ──────────────────────────────────────────────────────────────────
    //  TAHAP 6: CHOPPY WAVES (LAMBDA)
    //  Menambahkan pergeseran horizontal menggunakan parameter λ.
    //  Transisi smooth dari λ=0 ke λ optimal.
    //  Gelombang menjadi tajam dan realistis.
    // ──────────────────────────────────────────────────────────────────
    private void ApplyStage6_ChoppyWaves()
    {
        SetSimulationActive(true);

        if (wavesGenerator != null)
            wavesGenerator.timeScale = 1.0f;

        SetShaderDisplacement(true);

        ShowSimplePlane(false);
        ShowOceanGeometry(true);

        if (oceanGeometry != null)
        {
            oceanGeometry.ClipLevels = originalClipLevels;
            oceanGeometry.useMaterialLOD = false;
            oceanGeometry.ShowMaterialLods = false;
        }

        // Material: height visualization untuk melihat efek choppy
        SetMaterialMode(MaterialMode.HeightVisualization);

        // Aktifkan lambda untuk choppy waves
        SetLambda(originalLambda);
    }

    // ──────────────────────────────────────────────────────────────────
    //  TAHAP 7: MULTI-CASCADE
    //  Menampilkan kontribusi dari 3 cascade secara bertahap:
    //  Cascade 0 (swell) → + Cascade 1 (wind) → + Cascade 2 (ripple).
    //  Menunjukkan bagaimana multi-cascade menghilangkan tiling.
    // ──────────────────────────────────────────────────────────────────
    private void ApplyStage7_MultiCascade()
    {
        SetSimulationActive(true);

        if (wavesGenerator != null)
            wavesGenerator.timeScale = 1.0f;

        SetShaderDisplacement(true);

        ShowSimplePlane(false);
        ShowOceanGeometry(true);

        if (oceanGeometry != null)
        {
            oceanGeometry.ClipLevels = originalClipLevels;
            oceanGeometry.useMaterialLOD = true;
            oceanGeometry.ShowMaterialLods = false;
        }

        // Material: tampilkan semua cascade, warna berdasarkan tinggi
        SetMaterialMode(MaterialMode.HeightVisualization);
        SetLambda(originalLambda);
    }

    // ──────────────────────────────────────────────────────────────────
    //  TAHAP 8: SHADING PBR & JACOBIAN FOAM
    //  Mengaktifkan material PBR lengkap dengan:
    //  - Subsurface Scattering dan Fresnel reflection
    //  - Normal mapping dari derivatives texture
    //  - Jacobian-based foam detection dan turbulence map
    //  - Skybox reflection
    // ──────────────────────────────────────────────────────────────────
    private void ApplyStage8_ShadingFoam()
    {
        SetSimulationActive(true);

        if (wavesGenerator != null)
            wavesGenerator.timeScale = 1.0f;

        SetShaderDisplacement(true);

        ShowSimplePlane(false);
        ShowOceanGeometry(true);

        if (oceanGeometry != null)
        {
            oceanGeometry.ClipLevels = originalClipLevels;
            oceanGeometry.useMaterialLOD = true;
            oceanGeometry.ShowMaterialLods = false;
        }

        // Material: Full PBR ocean shading
        SetMaterialMode(MaterialMode.FullPBR);
        SetLambda(originalLambda);
    }

    // ──────────────────────────────────────────────────────────────────
    //  TAHAP 9: INTEGRASI DATA BUOY (FIREBASE)
    //  Mendemonstrasikan alur data real-time:
    //  Firebase Firestore → REST API → BuoyDataApplier →
    //  Newton-Raphson (Fetch) → WavesSettings → GPU Simulation
    //  + AsyncGPUReadback untuk fisika buoy 3D.
    // ──────────────────────────────────────────────────────────────────
    private void ApplyStage9_BuoyIntegration()
    {
        SetSimulationActive(true);

        if (wavesGenerator != null)
            wavesGenerator.timeScale = 1.0f;

        SetShaderDisplacement(true);

        ShowSimplePlane(false);
        ShowOceanGeometry(true);

        if (oceanGeometry != null)
        {
            oceanGeometry.ClipLevels = originalClipLevels;
            oceanGeometry.useMaterialLOD = true;
            oceanGeometry.ShowMaterialLods = false;
        }

        // Material: Full PBR
        SetMaterialMode(MaterialMode.FullPBR);
        SetLambda(originalLambda);

        // Trigger fetch data buoy jika BuoyDataApplier tersedia
        if (buoyDataApplier != null)
        {
            buoyDataApplier.FetchAndApplyLatest();
            Debug.Log("[Presentasi] Fetching latest buoy data from Firebase...");
        }
        else
        {
            Debug.LogWarning("[Presentasi] BuoyDataApplier tidak di-assign. " +
                "Tahap 9 tetap berjalan dengan parameter manual.");
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  HELPER METHODS
    // ════════════════════════════════════════════════════════════════════

    private enum MaterialMode
    {
        FlatColor,
        LODVisualization,
        GaussianNoise,
        InitialSpectrum,
        HeightVisualization,
        FullPBR
    }

    /// <summary>
    /// Mengatur mode simulasi gelombang (aktif/nonaktif).
    /// </summary>
    private void SetSimulationActive(bool active)
    {
        if (wavesGenerator != null)
        {
            wavesGenerator.useGPUSimulation = active;
            if (!active)
                wavesGenerator.timeScale = 0f;
        }
    }

    // ──── Simple Plane Management ────────────────────────────────────

    /// <summary>
    /// Membuat plane sederhana kecil untuk Tahap 1.
    /// Plane ini independen dari OceanGeometry clipmap.
    /// </summary>
    private void CreateSimplePlane()
    {
        if (simplePlane != null) return;

        simplePlane = new GameObject("PresentationPlane");
        simplePlane.transform.SetParent(transform);
        simplePlane.transform.localPosition = Vector3.zero;

        // Buat mesh plane sederhana (grid 32x32 vertex)
        int resolution = 32;
        float planeSize = 40f; // 40x40 meter — cukup besar untuk terlihat jelas
        Mesh mesh = new Mesh();
        mesh.name = "PresentationPlane";

        Vector3[] vertices = new Vector3[(resolution + 1) * (resolution + 1)];
        Vector3[] normals = new Vector3[(resolution + 1) * (resolution + 1)];
        Vector2[] uvs = new Vector2[(resolution + 1) * (resolution + 1)];
        int[] triangles = new int[resolution * resolution * 6];

        float halfSize = planeSize * 0.5f;
        float step = planeSize / resolution;

        for (int z = 0; z <= resolution; z++)
        {
            for (int x = 0; x <= resolution; x++)
            {
                int i = z * (resolution + 1) + x;
                vertices[i] = new Vector3(-halfSize + x * step, 0f, -halfSize + z * step);
                normals[i] = Vector3.up;
                uvs[i] = new Vector2((float)x / resolution, (float)z / resolution);
            }
        }

        int t = 0;
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int i = z * (resolution + 1) + x;
                triangles[t++] = i;
                triangles[t++] = i + resolution + 1;
                triangles[t++] = i + 1;
                triangles[t++] = i + 1;
                triangles[t++] = i + resolution + 1;
                triangles[t++] = i + resolution + 2;
            }
        }

        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = triangles;

        MeshFilter mf = simplePlane.AddComponent<MeshFilter>();
        mf.mesh = mesh;

        MeshRenderer mr = simplePlane.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = true;

        // Gunakan ocean material yang sama agar _PresentationMode berlaku
        if (oceanMaterial != null)
            mr.material = oceanMaterial;

        simplePlane.SetActive(false);
    }

    /// <summary>
    /// Tampilkan atau sembunyikan plane sederhana.
    /// </summary>
    private void ShowSimplePlane(bool show)
    {
        if (simplePlane == null && show)
            CreateSimplePlane();

        if (simplePlane != null)
            simplePlane.SetActive(show);
    }

    /// <summary>
    /// Tampilkan atau sembunyikan OceanGeometry (clipmap mesh).
    /// </summary>
    private void ShowOceanGeometry(bool show)
    {
        if (oceanGeometry != null)
            oceanGeometry.gameObject.SetActive(show);
    }

    /// <summary>
    /// Mengaktifkan/menonaktifkan displacement pada shader global.
    /// </summary>
    private void SetShaderDisplacement(bool enabled)
    {
        // Gunakan global shader property untuk mengontrol displacement
        Shader.SetGlobalFloat("_DisplacementEnabled", enabled ? 1f : 0f);

        // Jika material tersedia, set langsung juga
        if (oceanMaterial != null)
        {
            if (enabled)
                oceanMaterial.EnableKeyword("DISPLACEMENT_ON");
            else
                oceanMaterial.DisableKeyword("DISPLACEMENT_ON");
        }
    }

    /// <summary>
    /// Mengatur parameter lambda (choppy waves) pada semua cascade.
    /// </summary>
    private void SetLambda(float lambda)
    {
        if (wavesGenerator != null)
        {
            wavesGenerator.lambdaOverride = lambda;
        }
    }

    /// <summary>
    /// Mengatur mode tampilan material berdasarkan tahap presentasi.
    /// </summary>
    private void SetMaterialMode(MaterialMode mode)
    {
        if (oceanMaterial == null) return;

        switch (mode)
        {
            case MaterialMode.FlatColor:
                // Tampilkan warna solid biru muda — flat shading
                oceanMaterial.SetFloat("_PresentationMode", 1f);
                break;

            case MaterialMode.LODVisualization:
                // LOD color visualization sudah dihandle oleh OceanGeometry.ShowMaterialLods
                oceanMaterial.SetFloat("_PresentationMode", 0f);
                break;

            case MaterialMode.GaussianNoise:
                // Tampilkan gaussian noise texture
                if (wavesGenerator != null && wavesGenerator.cascade0 != null)
                {
                    Texture noiseTexture = wavesGenerator.cascade0.GaussianNoise;
                    if (noiseTexture != null)
                        oceanMaterial.SetTexture("_PresentationTex", noiseTexture);
                }
                oceanMaterial.SetFloat("_PresentationMode", 2f);
                break;

            case MaterialMode.InitialSpectrum:
                // Tampilkan initial spectrum texture h0(k)
                if (wavesGenerator != null && wavesGenerator.cascade0 != null)
                {
                    RenderTexture spectrumTex = wavesGenerator.cascade0.InitialSpectrum;
                    if (spectrumTex != null)
                        oceanMaterial.SetTexture("_PresentationTex", spectrumTex);
                }
                oceanMaterial.SetFloat("_PresentationMode", 3f);
                break;

            case MaterialMode.HeightVisualization:
                // Pewarnaan berdasarkan ketinggian (height-based coloring)
                oceanMaterial.SetFloat("_PresentationMode", 4f);
                break;

            case MaterialMode.FullPBR:
                // Mode normal — PBR ocean shading penuh
                oceanMaterial.SetFloat("_PresentationMode", 0f);
                break;
        }
    }

    /// <summary>
    /// Update transisi smooth (interpolasi parameter).
    /// </summary>
    private void UpdateTransition(float t)
    {
        // Smooth easing
        float eased = t * t * (3f - 2f * t); // smoothstep

        // Untuk tahap 6 (choppy waves), transisi lambda secara smooth
        if (currentStage == 6 && wavesGenerator != null)
        {
            float targetLambda = originalLambda;
            wavesGenerator.lambdaOverride = Mathf.Lerp(0f, targetLambda, eased);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  AUTO-ADVANCE COROUTINE
    // ════════════════════════════════════════════════════════════════════

    private IEnumerator AutoAdvanceCoroutine()
    {
        while (autoAdvance && currentStage <= 9)
        {
            yield return new WaitForSeconds(autoAdvanceDuration);

            if (!autoAdvance) yield break;

            if (currentStage < 9)
            {
                currentStage++;
                Debug.Log($"[Presentasi] Auto-advance → {stageNames[currentStage]}");
            }
            else
            {
                autoAdvance = false;
                Debug.Log("[Presentasi] Presentasi selesai.");
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  ON-SCREEN UI (Overlay Informasi Tahap)
    // ════════════════════════════════════════════════════════════════════

    private GUIStyle titleStyle;
    private GUIStyle descStyle;
    private GUIStyle controlStyle;
    private GUIStyle progressStyle;
    private bool stylesInitialized = false;

    private void InitStyles()
    {
        if (stylesInitialized) return;

        titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 28,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.UpperLeft,
            wordWrap = true,
            richText = true
        };
        titleStyle.normal.textColor = Color.white;

        descStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16,
            alignment = TextAnchor.UpperLeft,
            wordWrap = true,
            richText = true
        };
        descStyle.normal.textColor = new Color(0.9f, 0.95f, 1f, 0.95f);

        controlStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            alignment = TextAnchor.LowerLeft,
            wordWrap = true,
            richText = true
        };
        controlStyle.normal.textColor = new Color(0.7f, 0.8f, 0.9f, 0.7f);

        progressStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            alignment = TextAnchor.UpperRight,
            fontStyle = FontStyle.Bold
        };
        progressStyle.normal.textColor = new Color(0.5f, 0.8f, 1f, 0.9f);

        stylesInitialized = true;
    }

    private void OnGUI()
    {
        if (!showStageUI) return;
        InitStyles();

        int stage = Mathf.Clamp(currentStage, 1, 9);
        float padding = 20f;
        float panelWidth = 520f;
        float panelHeight = 280f;

        // ── Background Panel ──
        Rect panelRect = new Rect(padding, padding, panelWidth, panelHeight);
        GUI.color = new Color(0.05f, 0.1f, 0.2f, 0.85f);
        GUI.DrawTexture(panelRect, Texture2D.whiteTexture);
        GUI.color = Color.white;

        // ── Accent line ──
        Rect accentRect = new Rect(padding, padding, 4f, panelHeight);
        GUI.color = new Color(0.2f, 0.6f, 1f, 1f);
        GUI.DrawTexture(accentRect, Texture2D.whiteTexture);
        GUI.color = Color.white;

        // ── Progress indicator ──
        string progressText = $"{stage} / 9";
        Rect progressRect = new Rect(padding + 10f, padding + 8f, panelWidth - 20f, 30f);
        GUI.Label(progressRect, progressText, progressStyle);

        // ── Progress bar ──
        float barY = padding + panelHeight - 6f;
        float barWidth = panelWidth * (stage / 9f);
        Rect barBgRect = new Rect(padding, barY, panelWidth, 4f);
        GUI.color = new Color(0.15f, 0.2f, 0.3f, 0.8f);
        GUI.DrawTexture(barBgRect, Texture2D.whiteTexture);
        Rect barFillRect = new Rect(padding, barY, barWidth, 4f);
        GUI.color = new Color(0.2f, 0.6f, 1f, 1f);
        GUI.DrawTexture(barFillRect, Texture2D.whiteTexture);
        GUI.color = Color.white;

        // ── Title ──
        float contentX = padding + 16f;
        Rect titleRect = new Rect(contentX, padding + 12f, panelWidth - 32f, 40f);
        GUI.Label(titleRect, stageNames[stage], titleStyle);

        // ── Description ──
        Rect descRect = new Rect(contentX, padding + 58f, panelWidth - 32f, 160f);
        GUI.Label(descRect, stageDescriptions[stage], descStyle);

        // ── Controls hint ──
        float controlY = Screen.height - 50f;
        Rect controlRect = new Rect(padding, controlY, Screen.width - 2 * padding, 40f);
        string controls = "◄ ► Navigasi  |  1-9 Langsung  |  Space Auto  |  R Reset  |  F LOD View";
        GUI.Label(controlRect, controls, controlStyle);

        // ── Auto-advance indicator ──
        if (autoAdvance)
        {
            Rect autoRect = new Rect(Screen.width - 180f, padding, 160f, 30f);
            GUI.color = new Color(0.2f, 0.8f, 0.4f, 0.9f);
            GUI.Label(autoRect, "▶ AUTO ADVANCE", progressStyle);
            GUI.color = Color.white;
        }

        // ── Transition indicator ──
        if (isTransitioning)
        {
            Rect transRect = new Rect(Screen.width - 180f, padding + 30f, 160f, 30f);
            GUI.color = new Color(1f, 0.8f, 0.2f, 0.8f);
            GUI.Label(transRect, $"Transisi: {transitionProgress * 100f:F0}%", progressStyle);
            GUI.color = Color.white;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  CLEANUP
    // ════════════════════════════════════════════════════════════════════

    private void OnDisable()
    {
        // Restore original values saat script dinonaktifkan
        RestoreOriginalValues();
    }

    private void OnDestroy()
    {
        RestoreOriginalValues();
    }

    private void RestoreOriginalValues()
    {
        // Hapus simple plane
        ShowSimplePlane(false);
        if (simplePlane != null)
        {
            Destroy(simplePlane);
            simplePlane = null;
        }

        // Pastikan OceanGeometry aktif kembali
        ShowOceanGeometry(true);

        if (wavesGenerator != null)
        {
            wavesGenerator.timeScale = originalTimeScale;
            wavesGenerator.lambdaOverride = null;
            wavesGenerator.useGPUSimulation = true;
        }

        if (oceanGeometry != null)
        {
            oceanGeometry.ClipLevels = originalClipLevels;
            oceanGeometry.useMaterialLOD = originalUseMaterialLOD;
            oceanGeometry.ShowMaterialLods = originalShowMaterialLods;
        }

        // Reset material mode
        if (oceanMaterial != null)
        {
            oceanMaterial.SetFloat("_PresentationMode", 0f);
            oceanMaterial.EnableKeyword("DISPLACEMENT_ON");
        }

        SetShaderDisplacement(true);
    }
}
