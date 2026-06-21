using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Controller presentasi skripsi bertahap untuk memandu penguji/penonton dalam alur simulasi
/// gelombang laut 3D berbasis FFT (Fast Fourier Transform), dari geometri awal hingga integrasi data buoy.
/// Disertai UI overlay OnGUI premium bertema dark-glassmorphism.
/// </summary>
public class OceanPresentationController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private WavesGenerator wavesGenerator;
    [SerializeField] private OceanGeometry oceanGeometry;
    [SerializeField] private BuoyDataApplier buoyDataApplier;

    [Header("Current State")]
    [Range(1, 8)]
    [SerializeField] private int currentStage = 1;

    [Header("UI Settings")]
    [SerializeField] private float panelWidth = 480f;
    [SerializeField] private float panelHeight = 700f;

    // ─── Temporary / Sub-stage States ───────────────────────────────
    // Sub-stage untuk Tahap 5 (Cascade Integration)
    // 0 = All Combined, 1 = Swell Only, 2 = Wind Wave Only, 3 = Ripples Only
    private int activeCascadeMode = 0; 

    // Preset data untuk Tahap 8 (Buoy Integration)
    private struct BuoyPreset
    {
        public string name;
        public float windSpeed;      // m/s
        public float windDirection;  // degrees
        public float waveHeight;     // meters
        public string desc;
    }
    private List<BuoyPreset> buoyPresets;
    private int selectedPresetIndex = 0;

    [Header("Presentation Settings")]
    [Tooltip("Apakah plane laut bergerak mengikuti kamera secara real-time")]
    [SerializeField] private bool planeFollowsCamera = false;

    [Tooltip("Apakah mengaktifkan kamera terbang (Fly-Cam) internal untuk navigasi bebas")]
    [SerializeField] private bool useBuiltInCameraController = true;

    [Tooltip("Jumlah Clip Levels (tingkat grid) yang digunakan pada Tahap 1 - 5 (untuk mengecilkan plane agar terlihat semua)")]
    [SerializeField, Range(0, 8)] private int presentationClipLevels = 2;

    [Header("Fly-Cam Speed Settings")]
    [SerializeField] private float mainSpeed = 20.0f;
    [SerializeField] private float shiftAdd = 50.0f;
    [SerializeField] private float maxShift = 100.0f;
    [SerializeField] private float camSens = 0.25f;

    private Transform dummyStaticViewer;
    private Transform targetCamTransform;
    private Vector3 lastMouse = new Vector3(255, 255, 255);
    private float totalRun = 1.0f;

    // Cache setting original untuk merestorasi status normal
    private float originalTimeScale = 1.0f;
    private bool originalShowLods = false;
    private bool originalUseMaterialLOD = true;
    private int originalClipLevels = 8;
    private float? originalLambdaOverride = null;

    private float originalFoamBiasLOD0 = 1.0f;
    private float originalFoamBiasLOD1 = 1.0f;
    private float originalFoamBiasLOD2 = 1.0f;
    private float originalFoamScale = 1.0f;

    // GUI Styles
    private GUIStyle panelBgStyle;
    private GUIStyle headerStyle;
    private GUIStyle subHeaderStyle;
    private GUIStyle textStyle;
    private GUIStyle boldTextStyle;
    private GUIStyle codeStyle;
    private GUIStyle buttonStyle;
    private GUIStyle activeButtonStyle;
    private GUIStyle labelStyle;
    private GUIStyle sliderLabelStyle;
    private bool stylesInitialized = false;

    // Cache referensi texture untuk restorasi cascade
    private Texture cacheDisp0, cacheDeriv0, cacheTurb0;
    private Texture cacheDisp1, cacheDeriv1, cacheTurb1;
    private Texture cacheDisp2, cacheDeriv2, cacheTurb2;
    private bool texturesCached = false;

    private void Start()
    {
        if (wavesGenerator == null) wavesGenerator = FindObjectOfType<WavesGenerator>();
        if (oceanGeometry == null) oceanGeometry = FindObjectOfType<OceanGeometry>();
        if (buoyDataApplier == null) buoyDataApplier = FindObjectOfType<BuoyDataApplier>();

        // Cache original values
        if (wavesGenerator != null)
        {
            originalTimeScale = wavesGenerator.timeScale;
            originalLambdaOverride = wavesGenerator.lambdaOverride;
        }

        if (oceanGeometry != null)
        {
            originalShowLods = oceanGeometry.ShowMaterialLods;
            originalUseMaterialLOD = oceanGeometry.useMaterialLOD;
            originalClipLevels = oceanGeometry.ClipLevels;
        }

        // Cache material original parameters
        Material oceanMat = GetSharedOceanMaterial();
        if (oceanMat != null)
        {
            originalFoamBiasLOD0 = oceanMat.GetFloat("_FoamBiasLOD0");
            originalFoamBiasLOD1 = oceanMat.GetFloat("_FoamBiasLOD1");
            originalFoamBiasLOD2 = oceanMat.GetFloat("_FoamBiasLOD2");
            originalFoamScale = oceanMat.GetFloat("_FoamScale");
        }

        // Inisialisasi preset data buoy untuk Tahap 8
        buoyPresets = new List<BuoyPreset>
        {
            new BuoyPreset { name = "Selat Sunda (Tenang)", windSpeed = 3.1f, windDirection = 90f, waveHeight = 0.45f, desc = "Keadaan normal, hembusan angin sepoi-sepoi, riak air tenang." },
            new BuoyPreset { name = "Laut Jawa (Sedang)", windSpeed = 8.5f, windDirection = 145f, waveHeight = 1.6f, desc = "Keadaan sedang, ombak mulai terbentuk secara konstan." },
            new BuoyPreset { name = "Samudra Hindia (Badai/Ekstrem)", windSpeed = 19.5f, windDirection = 210f, waveHeight = 5.4f, desc = "Keadaan ekstrim/badai, gelombang sangat tinggi disertai puncak yang curam." }
        };

        // Inisialisasi dummy static viewer transform untuk mematikan fitur plane ikut kamera
        GameObject dummyGO = new GameObject("_StaticPresentationViewer");
        dummyGO.transform.position = Vector3.zero;
        dummyGO.hideFlags = HideFlags.HideAndDontSave;
        dummyStaticViewer = dummyGO.transform;

        if (Camera.main != null)
        {
            targetCamTransform = Camera.main.transform;
        }

        // Cache default textures of cascades to prevent lost references when swapping
        CacheOriginalCascadeTextures();

        // Apply initial stage overrides
        ApplyStageSettings(currentStage);
    }

    private void CacheOriginalCascadeTextures()
    {
        if (wavesGenerator == null || texturesCached) return;

        if (wavesGenerator.cascade0 != null)
        {
            cacheDisp0 = wavesGenerator.cascade0.Displacement;
            cacheDeriv0 = wavesGenerator.cascade0.Derivatives;
            cacheTurb0 = wavesGenerator.cascade0.Turbulence;
        }
        if (wavesGenerator.cascade1 != null)
        {
            cacheDisp1 = wavesGenerator.cascade1.Displacement;
            cacheDeriv1 = wavesGenerator.cascade1.Derivatives;
            cacheTurb1 = wavesGenerator.cascade1.Turbulence;
        }
        if (wavesGenerator.cascade2 != null)
        {
            cacheDisp2 = wavesGenerator.cascade2.Displacement;
            cacheDeriv2 = wavesGenerator.cascade2.Derivatives;
            cacheTurb2 = wavesGenerator.cascade2.Turbulence;
        }

        if (cacheDisp0 != null && cacheDisp1 != null && cacheDisp2 != null)
        {
            texturesCached = true;
        }
    }

    private void Update()
    {
        // Update plane viewer snapping based on toggle
        UpdatePlaneViewer();

        // Update fly-cam controller if enabled
        if (useBuiltInCameraController)
        {
            UpdateCameraMovement();
        }

        // Keyboard navigation
        if (IsKeyDown(KeyCode.RightArrow) || IsKeyDown(KeyCode.PageDown))
        {
            NextStage();
        }
        else if (IsKeyDown(KeyCode.LeftArrow) || IsKeyDown(KeyCode.PageUp))
        {
            PrevStage();
        }

        // Direct number selection
        for (int i = 1; i <= 8; i++)
        {
            if (IsStageKeyDown(i))
            {
                JumpToStage(i);
            }
        }
    }

    private void JumpToStage(int stage)
    {
        currentStage = Mathf.Clamp(stage, 1, 8);
        ApplyStageSettings(currentStage);
    }

    private void NextStage()
    {
        if (currentStage < 8)
        {
            currentStage++;
            ApplyStageSettings(currentStage);
        }
    }

    private void PrevStage()
    {
        if (currentStage > 1)
        {
            currentStage--;
            ApplyStageSettings(currentStage);
        }
    }

    private Material GetSharedOceanMaterial()
    {
        if (oceanGeometry == null) return null;
        // Gunakan reflection untuk mengambil private field oceanMaterial jika tidak public
        System.Reflection.FieldInfo field = typeof(OceanGeometry).GetField("oceanMaterial", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (field != null)
        {
            return (Material)field.GetValue(oceanGeometry);
        }
        return null;
    }

    private void ApplyStageSettings(int stage)
    {
        // Pastikan tekstur cascade original ter-cache terlebih dahulu
        CacheOriginalCascadeTextures();

        // 1. Reset ke kondisi dasar/bersih sebelum menerapkan override stage
        RestoreBaseStates();

        switch (stage)
        {
            case 1: // Pembangkitan Grid Mesh (LOD Clipmap)
                // Rampingkan/ratakan air sepenuhnya
                FlattenOcean(true);
                // Tampilkan visualisasi LOD
                if (oceanGeometry != null)
                {
                    oceanGeometry.ShowMaterialLods = true;
                    oceanGeometry.useMaterialLOD = true;
                }
                break;

            case 2: // Pembangkitan Noise Gaussian (Box-Muller)
                // Rampingkan/ratakan air sepenuhnya (kita hanya mengamati seed acak)
                FlattenOcean(true);
                if (oceanGeometry != null)
                {
                    oceanGeometry.ShowMaterialLods = false;
                }
                break;

            case 3: // Spektrum Awal h0(k) (JONSWAP + TMA)
                // Naikkan gelombang statis (beku)
                FlattenOcean(false);
                if (wavesGenerator != null)
                {
                    wavesGenerator.timeScale = 0.0f; // Freeze time propagation
                }
                if (oceanGeometry != null)
                {
                    oceanGeometry.ShowMaterialLods = false;
                }
                break;

            case 4: // Evolusi Temporal & IFFT (Animasi)
                // Jalankan gelombang normal bergerak
                FlattenOcean(false);
                if (wavesGenerator != null)
                {
                    wavesGenerator.timeScale = 1.0f; // Jalankan waktu
                }
                if (oceanGeometry != null)
                {
                    oceanGeometry.ShowMaterialLods = false;
                }
                break;

            case 5: // Integrasi Multi-Cascade Grid
                FlattenOcean(false);
                ApplyCascadeMode(activeCascadeMode);
                if (oceanGeometry != null)
                {
                    oceanGeometry.ShowMaterialLods = false;
                }
                break;

            case 6: // Struktur Grid LOD (Optimasi Jarak)
                FlattenOcean(false);
                if (oceanGeometry != null)
                {
                    oceanGeometry.ShowMaterialLods = true; // Warnai LOD (Merah, Hijau, Biru)
                    oceanGeometry.useMaterialLOD = true;
                }
                break;

            case 7: // PBR Shading & Buih Gelombang
                FlattenOcean(false);
                if (oceanGeometry != null)
                {
                    oceanGeometry.ShowMaterialLods = false;
                }
                break;

            case 8: // Integrasi Data Lingkungan Buoy
                FlattenOcean(false);
                if (oceanGeometry != null)
                {
                    oceanGeometry.ShowMaterialLods = false;
                }
                // Terapkan preset pertama jika belum ada buoy yang aktif
                if (buoyDataApplier != null && buoyDataApplier.LastAppliedData == null)
                {
                    ApplyPreset(selectedPresetIndex);
                }
                break;
        }
    }

    private void RestoreBaseStates()
    {
        // Kembalikan referensi cascade texture original
        RestoreCascadeTextures();

        // Kembalikan variabel WavesGenerator
        if (wavesGenerator != null)
        {
            wavesGenerator.timeScale = originalTimeScale;
            wavesGenerator.lambdaOverride = originalLambdaOverride;
        }

        // Kembalikan variabel OceanGeometry
        if (oceanGeometry != null)
        {
            oceanGeometry.ShowMaterialLods = originalShowLods;
            oceanGeometry.useMaterialLOD = originalUseMaterialLOD;
            oceanGeometry.ClipLevels = originalClipLevels;
            oceanGeometry.Viewer = (Camera.main != null) ? Camera.main.transform : null; // Restore camera as viewer
            ForceRestoreMaterials();
        }

        // Kembalikan material parameters
        Material oceanMat = GetSharedOceanMaterial();
        if (oceanMat != null)
        {
            oceanMat.SetFloat("_FoamBiasLOD0", originalFoamBiasLOD0);
            oceanMat.SetFloat("_FoamBiasLOD1", originalFoamBiasLOD1);
            oceanMat.SetFloat("_FoamBiasLOD2", originalFoamBiasLOD2);
            oceanMat.SetFloat("_FoamScale", originalFoamScale);
        }
    }

    private void ForceRestoreMaterials()
    {
        if (oceanGeometry == null) return;
        Material oceanMat = GetSharedOceanMaterial();
        if (oceanMat == null) return;

        // Ambil private array materials dari OceanGeometry via Reflection
        System.Reflection.FieldInfo materialsField = typeof(OceanGeometry).GetField("materials", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (materialsField != null)
        {
            Material[] mats = (Material[])materialsField.GetValue(oceanGeometry);
            if (mats != null && mats.Length >= 3)
            {
                // Salin properti dari oceanMaterial ke masing-masing material LOD
                mats[0].CopyPropertiesFromMaterial(oceanMat);
                mats[0].EnableKeyword("CLOSE");

                mats[1].CopyPropertiesFromMaterial(oceanMat);
                mats[1].EnableKeyword("MID");
                mats[1].DisableKeyword("CLOSE");

                mats[2].CopyPropertiesFromMaterial(oceanMat);
                mats[2].DisableKeyword("MID");
                mats[2].DisableKeyword("CLOSE");
            }
        }
    }

    private void UpdatePlaneViewer()
    {
        if (oceanGeometry == null) return;

        if (planeFollowsCamera)
        {
            Transform camTransform = Camera.main != null ? Camera.main.transform : null;
            if (camTransform != null && oceanGeometry.Viewer != camTransform)
            {
                oceanGeometry.Viewer = camTransform;
            }
        }
        else
        {
            if (dummyStaticViewer != null && oceanGeometry.Viewer != dummyStaticViewer)
            {
                // Set viewer to static dummy transform to lock plane position
                oceanGeometry.Viewer = dummyStaticViewer;
            }
        }
    }

    private void UpdateCameraMovement()
    {
        if (targetCamTransform == null)
        {
            if (Camera.main != null)
                targetCamTransform = Camera.main.transform;
            else
                return;
        }

        // Mouse Look: hanya aktif saat klik kanan mouse ditahan (Right Mouse Button)
        if (IsMouseButtonDown(1))
        {
            lastMouse = GetMousePosition();
        }

        if (IsMouseButtonHeld(1))
        {
            Vector3 mousePos = GetMousePosition();
            Vector3 delta = mousePos - lastMouse;
            lastMouse = mousePos;

            float yaw = targetCamTransform.localEulerAngles.y + delta.x * camSens;
            float pitch = targetCamTransform.localEulerAngles.x - delta.y * camSens;

            // Batasi pitch (pitch normalization) agar kamera tidak berputar terbalik
            if (pitch > 180f) pitch -= 360f;
            pitch = Mathf.Clamp(pitch, -85f, 85f);

            targetCamTransform.localEulerAngles = new Vector3(pitch, yaw, 0f);
        }

        // Keyboard Movement (WASD / QE)
        Vector3 p = GetBaseInputDirection();
        if (IsKeyHeld(KeyCode.LeftShift))
        {
            totalRun += Time.deltaTime;
            p = p * totalRun * shiftAdd;
            p.x = Mathf.Clamp(p.x, -maxShift, maxShift);
            p.y = Mathf.Clamp(p.y, -maxShift, maxShift);
            p.z = Mathf.Clamp(p.z, -maxShift, maxShift);
        }
        else
        {
            totalRun = Mathf.Clamp(totalRun * 0.5f, 1f, 1000f);
            p = p * mainSpeed;
        }

        p = p * Time.deltaTime;
        targetCamTransform.Translate(p);
    }

    private Vector3 GetBaseInputDirection()
    {
        Vector3 p_Velocity = new Vector3();
        if (IsKeyHeld(KeyCode.W)) p_Velocity += Vector3.forward;
        if (IsKeyHeld(KeyCode.S)) p_Velocity += Vector3.back;
        if (IsKeyHeld(KeyCode.A)) p_Velocity += Vector3.left;
        if (IsKeyHeld(KeyCode.D)) p_Velocity += Vector3.right;
        if (IsKeyHeld(KeyCode.Q)) p_Velocity += Vector3.down; // Turun
        if (IsKeyHeld(KeyCode.E)) p_Velocity += Vector3.up;   // Naik
        return p_Velocity;
    }

    // ─── Input System Compatibility Wrappers ─────────────────────────

    private bool IsStageKeyDown(int stageIndex)
    {
        KeyCode alphaKey = (KeyCode)((int)KeyCode.Alpha0 + stageIndex);
        KeyCode keypadKey = (KeyCode)((int)KeyCode.Keypad0 + stageIndex);
        return IsKeyDown(alphaKey) || IsKeyDown(keypadKey);
    }

    private bool IsKeyDown(KeyCode key)
    {
#if ENABLE_INPUT_SYSTEM && !UNITY_LEGACY_INPUT_SOURCE
        return GetNewInputSystemKeyDown(key);
#else
        try { return Input.GetKeyDown(key); }
        catch (System.InvalidOperationException) { return GetNewInputSystemKeyDown(key); }
#endif
    }

    private bool IsKeyHeld(KeyCode key)
    {
#if ENABLE_INPUT_SYSTEM && !UNITY_LEGACY_INPUT_SOURCE
        return GetNewInputSystemKeyHeld(key);
#else
        try { return Input.GetKey(key); }
        catch (System.InvalidOperationException) { return GetNewInputSystemKeyHeld(key); }
#endif
    }

    private bool IsMouseButtonHeld(int button)
    {
#if ENABLE_INPUT_SYSTEM && !UNITY_LEGACY_INPUT_SOURCE
        return GetNewInputSystemMouseButtonHeld(button);
#else
        try { return Input.GetMouseButton(button); }
        catch (System.InvalidOperationException) { return GetNewInputSystemMouseButtonHeld(button); }
#endif
    }

    private bool IsMouseButtonDown(int button)
    {
#if ENABLE_INPUT_SYSTEM && !UNITY_LEGACY_INPUT_SOURCE
        return GetNewInputSystemMouseButtonDown(button);
#else
        try { return Input.GetMouseButtonDown(button); }
        catch (System.InvalidOperationException) { return GetNewInputSystemMouseButtonDown(button); }
#endif
    }

    private Vector3 GetMousePosition()
    {
#if ENABLE_INPUT_SYSTEM && !UNITY_LEGACY_INPUT_SOURCE
        return GetNewInputSystemMousePosition();
#else
        try { return Input.mousePosition; }
        catch (System.InvalidOperationException) { return GetNewInputSystemMousePosition(); }
#endif
    }

#if ENABLE_INPUT_SYSTEM
    private bool GetNewInputSystemKeyDown(KeyCode key)
    {
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard == null) return false;

        switch (key)
        {
            case KeyCode.Alpha1: return keyboard.digit1Key.wasPressedThisFrame;
            case KeyCode.Alpha2: return keyboard.digit2Key.wasPressedThisFrame;
            case KeyCode.Alpha3: return keyboard.digit3Key.wasPressedThisFrame;
            case KeyCode.Alpha4: return keyboard.digit4Key.wasPressedThisFrame;
            case KeyCode.Alpha5: return keyboard.digit5Key.wasPressedThisFrame;
            case KeyCode.Alpha6: return keyboard.digit6Key.wasPressedThisFrame;
            case KeyCode.Alpha7: return keyboard.digit7Key.wasPressedThisFrame;
            case KeyCode.Alpha8: return keyboard.digit8Key.wasPressedThisFrame;
            case KeyCode.Keypad1: return keyboard.numpad1Key.wasPressedThisFrame;
            case KeyCode.Keypad2: return keyboard.numpad2Key.wasPressedThisFrame;
            case KeyCode.Keypad3: return keyboard.numpad3Key.wasPressedThisFrame;
            case KeyCode.Keypad4: return keyboard.numpad4Key.wasPressedThisFrame;
            case KeyCode.Keypad5: return keyboard.numpad5Key.wasPressedThisFrame;
            case KeyCode.Keypad6: return keyboard.numpad6Key.wasPressedThisFrame;
            case KeyCode.Keypad7: return keyboard.numpad7Key.wasPressedThisFrame;
            case KeyCode.Keypad8: return keyboard.numpad8Key.wasPressedThisFrame;
            case KeyCode.RightArrow: return keyboard.rightArrowKey.wasPressedThisFrame;
            case KeyCode.PageDown: return keyboard.pageDownKey.wasPressedThisFrame;
            case KeyCode.LeftArrow: return keyboard.leftArrowKey.wasPressedThisFrame;
            case KeyCode.PageUp: return keyboard.pageUpKey.wasPressedThisFrame;
            default: return false;
        }
    }

    private bool GetNewInputSystemKeyHeld(KeyCode key)
    {
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard == null) return false;

        switch (key)
        {
            case KeyCode.W: return keyboard.wKey.isPressed;
            case KeyCode.S: return keyboard.sKey.isPressed;
            case KeyCode.A: return keyboard.aKey.isPressed;
            case KeyCode.D: return keyboard.dKey.isPressed;
            case KeyCode.Q: return keyboard.qKey.isPressed;
            case KeyCode.E: return keyboard.eKey.isPressed;
            case KeyCode.LeftShift: return keyboard.leftShiftKey.isPressed;
            default: return false;
        }
    }

    private bool GetNewInputSystemMouseButtonHeld(int button)
    {
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse == null) return false;

        if (button == 0) return mouse.leftButton.isPressed;
        if (button == 1) return mouse.rightButton.isPressed;
        if (button == 2) return mouse.middleButton.isPressed;
        return false;
    }

    private bool GetNewInputSystemMouseButtonDown(int button)
    {
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse == null) return false;

        if (button == 0) return mouse.leftButton.wasPressedThisFrame;
        if (button == 1) return mouse.rightButton.wasPressedThisFrame;
        if (button == 2) return mouse.middleButton.wasPressedThisFrame;
        return false;
    }

    private Vector3 GetNewInputSystemMousePosition()
    {
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse == null) return Vector3.zero;
        Vector2 pos = mouse.position.ReadValue();
        return new Vector3(pos.x, pos.y, 0f);
    }
#else
    private bool GetNewInputSystemKeyDown(KeyCode key) => false;
    private bool GetNewInputSystemKeyHeld(KeyCode key) => false;
    private bool GetNewInputSystemMouseButtonHeld(int button) => false;
    private bool GetNewInputSystemMouseButtonDown(int button) => false;
    private Vector3 GetNewInputSystemMousePosition() => Vector3.zero;
#endif

    private void FlattenOcean(bool flatten)
    {
        if (oceanGeometry == null) return;

        if (flatten)
        {
            // Set all cascades to black/white textures to eliminate displacement, slope normal and foam
            oceanGeometry.SetCascadeTextures(0, Texture2D.blackTexture, Texture2D.blackTexture, Texture2D.whiteTexture);
            oceanGeometry.SetCascadeTextures(1, Texture2D.blackTexture, Texture2D.blackTexture, Texture2D.whiteTexture);
            oceanGeometry.SetCascadeTextures(2, Texture2D.blackTexture, Texture2D.blackTexture, Texture2D.whiteTexture);
        }
        else
        {
            RestoreCascadeTextures();
        }
    }

    private void RestoreCascadeTextures()
    {
        if (oceanGeometry == null || !texturesCached) return;

        // Gunakan texture yang sudah di-cache saat Start
        oceanGeometry.SetCascadeTextures(0, cacheDisp0, cacheDeriv0, cacheTurb0);
        oceanGeometry.SetCascadeTextures(1, cacheDisp1, cacheDeriv1, cacheTurb1);
        oceanGeometry.SetCascadeTextures(2, cacheDisp2, cacheDeriv2, cacheTurb2);
    }

    private void ApplyCascadeMode(int mode)
    {
        if (oceanGeometry == null || !texturesCached) return;

        switch (mode)
        {
            case 0: // All Combined
                RestoreCascadeTextures();
                break;
            case 1: // Swell Only (C0)
                oceanGeometry.SetCascadeTextures(0, cacheDisp0, cacheDeriv0, cacheTurb0);
                oceanGeometry.SetCascadeTextures(1, Texture2D.blackTexture, Texture2D.blackTexture, Texture2D.whiteTexture);
                oceanGeometry.SetCascadeTextures(2, Texture2D.blackTexture, Texture2D.blackTexture, Texture2D.whiteTexture);
                break;
            case 2: // Wind Waves Only (C1)
                oceanGeometry.SetCascadeTextures(0, Texture2D.blackTexture, Texture2D.blackTexture, Texture2D.whiteTexture);
                oceanGeometry.SetCascadeTextures(1, cacheDisp1, cacheDeriv1, cacheTurb1);
                oceanGeometry.SetCascadeTextures(2, Texture2D.blackTexture, Texture2D.blackTexture, Texture2D.whiteTexture);
                break;
            case 3: // Ripples Only (C2)
                oceanGeometry.SetCascadeTextures(0, Texture2D.blackTexture, Texture2D.blackTexture, Texture2D.whiteTexture);
                oceanGeometry.SetCascadeTextures(1, Texture2D.blackTexture, Texture2D.blackTexture, Texture2D.whiteTexture);
                oceanGeometry.SetCascadeTextures(2, cacheDisp2, cacheDeriv2, cacheTurb2);
                break;
        }
    }

    private void ApplyPreset(int index)
    {
        if (buoyDataApplier == null || index < 0 || index >= buoyPresets.Count) return;
        selectedPresetIndex = index;
        BuoyPreset preset = buoyPresets[index];
        // Apply via manual method
        buoyDataApplier.ApplyManual(preset.windSpeed, preset.windDirection, preset.waveHeight);
    }

    private void OnDisable()
    {
        RestoreBaseStates();
    }

    private void OnDestroy()
    {
        RestoreBaseStates();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  ON GUI (Premium Glassmorphism Overlay)
    // ═══════════════════════════════════════════════════════════════

    private void OnGUI()
    {
        InitGUIStyles();

        // ─── Panel Layout ─────────────────────────────────────────────
        float x = 20f;
        float y = 20f;

        // Background Box (Dark glassmorphism)
        GUI.color = new Color(0.04f, 0.05f, 0.09f, 0.94f);
        GUI.DrawTexture(new Rect(x, y, panelWidth, panelHeight), Texture2D.whiteTexture);
        GUI.color = Color.white;

        // Panel Border
        DrawOutlineBorder(new Rect(x, y, panelWidth, panelHeight), new Color(0.2f, 0.55f, 0.9f, 0.7f), 2);

        float cx = x + 24f;
        float cy = y + 20f;
        float cw = panelWidth - 48f;

        // ─── Header ──────────────────────────────────────────────────
        GUI.Label(new Rect(cx, cy, cw, 24f), "SIMULASI 3D GELOMBANG LAUT FFT", headerStyle);
        cy += 22f;
        GUI.Label(new Rect(cx, cy, cw, 18f), "Sidang Tugas Akhir: Noer Fauzan Detya Gulfiar (2210511151)", subHeaderStyle);
        cy += 20f;

        // Separator
        GUI.color = new Color(0.2f, 0.55f, 0.9f, 0.4f);
        GUI.DrawTexture(new Rect(cx, cy, cw, 2f), Texture2D.whiteTexture);
        GUI.color = Color.white;
        cy += 12f;

        // Global Presentation Toggles (Plane Follow & Free-Fly Camera)
        float toggleW = cw / 2f - 10f;
        planeFollowsCamera = GUI.Toggle(new Rect(cx, cy, toggleW, 20f), planeFollowsCamera, " Plane Ikut Kamera", labelStyle);
        useBuiltInCameraController = GUI.Toggle(new Rect(cx + toggleW + 20f, cy, toggleW, 20f), useBuiltInCameraController, " Kamera Terbang (Fly-Cam)", labelStyle);
        cy += 28f;

        // Separator
        GUI.color = new Color(0.2f, 0.55f, 0.9f, 0.15f);
        GUI.DrawTexture(new Rect(cx, cy, cw, 1f), Texture2D.whiteTexture);
        GUI.color = Color.white;
        cy += 12f;

        // ─── Stage Navigation Header ─────────────────────────────────
        string stageTitle = GetStageTitle(currentStage);
        GUI.Label(new Rect(cx, cy, cw, 24f), $"TAHAP {currentStage}/8: {stageTitle.ToUpper()}", boldTextStyle);
        cy += 28f;

        // ─── Stage Content / Explanations ────────────────────────────
        float descHeight = 150f;
        string explanation = GetStageExplanation(currentStage);
        GUI.Label(new Rect(cx, cy, cw, descHeight), explanation, textStyle);
        cy += descHeight + 10f;

        // Separator
        GUI.color = new Color(0.2f, 0.55f, 0.9f, 0.15f);
        GUI.DrawTexture(new Rect(cx, cy, cw, 1f), Texture2D.whiteTexture);
        GUI.color = Color.white;
        cy += 14f;

        // ─── Interactive Controls (Stage Dependent) ──────────────────
        GUI.Label(new Rect(cx, cy, cw, 20f), "KONTROL INTERAKTIF TAHAP INI:", boldTextStyle);
        cy += 24f;

        float controlsHeight = 240f;
        Rect controlsRect = new Rect(cx, cy, cw, controlsHeight);
        DrawStageControls(currentStage, controlsRect);
        cy += controlsHeight + 15f;

        // ─── Footer / Navigation Buttons ─────────────────────────────
        GUI.color = new Color(0.2f, 0.55f, 0.9f, 0.15f);
        GUI.DrawTexture(new Rect(cx, cy, cw, 1f), Texture2D.whiteTexture);
        GUI.color = Color.white;
        cy += 12f;

        // Next and Prev Buttons
        float btnW = 120f;
        float btnH = 32f;
        
        // Prev Button
        GUI.enabled = (currentStage > 1);
        if (GUI.Button(new Rect(cx, cy, btnW, btnH), "◀ SEBELUMNYA", buttonStyle))
        {
            PrevStage();
        }
        GUI.enabled = true;

        // Stage Indicator dots
        float dotsW = 120f;
        float dotsX = cx + (cw - dotsW) / 2f;
        string dotsText = "";
        for (int i = 1; i <= 8; i++)
        {
            dotsText += (i == currentStage) ? "● " : "○ ";
        }
        GUI.Label(new Rect(dotsX, cy + 4f, dotsW, 20f), dotsText, subHeaderStyle);

        // Next Button
        GUI.enabled = (currentStage < 8);
        if (GUI.Button(new Rect(cx + cw - btnW, cy, btnW, btnH), "BERIKUTNYA ▶", buttonStyle))
        {
            NextStage();
        }
        GUI.enabled = true;
    }

    private string GetStageTitle(int stage)
    {
        switch (stage)
        {
            case 1: return "Pembangkitan Grid Mesh (LOD Clipmap)";
            case 2: return "Pembangkitan Noise Gaussian";
            case 3: return "Spektrum Awal h0(k) (JONSWAP + TMA)";
            case 4: return "Evolusi Temporal & IFFT";
            case 5: return "Integrasi Multi-Cascade Grid";
            case 6: return "Struktur Grid LOD (Optimasi Jarak)";
            case 7: return "PBR Shading & Buih Gelombang";
            case 8: return "Integrasi Data Lingkungan Buoy";
            default: return "Unknown Stage";
        }
    }

    private string GetStageExplanation(int stage)
    {
        switch (stage)
        {
            case 1:
                return "Permukaan air direpresentasikan secara geometris menggunakan skema LOD Clipmap. Skema ini membagi mesh menjadi beberapa tingkat detail (Center, Rings, Trims, dan Skirt). Grid di dekat kamera memiliki kerapatan vertex tinggi untuk detail tajam, sedangkan grid jauh lebih renggang untuk menghemat memori. Pada tahap ini, deformasi gelombang dimatikan untuk memperlihatkan struktur wireframe datar dasar.";
            case 2:
                return "Fase awal gelombang stokastik dibangkitkan menggunakan bilangan acak terdistribusi Gaussian (Normal) di CPU. Konversi dari nilai acak seragam menggunakan metode Transformasi Box-Muller untuk menghasilkan representasi amplitudo riak awal. Di sebelah kanan bawah panel, Anda dapat melihat visualisasi tekstur Gaussian Noise (RGFloat) berukuran N x N yang berfungsi sebagai seed spektral.";
            case 3:
                return "Di tahap ini, Spektrum Energi JONSWAP diterapkan pada domain frekuensi. Spektrum ini mendistribusikan energi gelombang berdasarkan kecepatan angin (U) dan kedalaman laut dangkal menggunakan faktor TMA. Gelombang dibekukan (waktu = 0) untuk memperlihatkan profil superposisi spasial (bentuk ombak awal) sebelum perambatan temporal diaktifkan.";
            case 4:
                return "Propagasi gelombang dinamis dijalankan dengan memajukan fase di domain frekuensi menggunakan Relasi Dispersi Airy. Data spektral kompleks hasil evolusi waktu kemudian dikonversi ke domain ruang (spasial) secara paralel pada GPU menggunakan algoritma Inverse Fast Fourier Transform (2D IFFT).";
            case 5:
                return "Untuk menghasilkan visualisasi laut dengan detail kaya (dari riak halus hingga ombak besar) tanpa pola tiling berulang, simulasi menggunakan 3 kaskade (cascade) grid fisik:\n" +
                       "• Cascade 0 (Swell): Grid skala besar (L = 250m)\n" +
                       "• Cascade 1 (Wind Wave): Grid skala lokal (L = 17m)\n" +
                       "• Cascade 2 (Ripples): Grid skala kecil (L = 5m)\n" +
                       "Gunakan tombol interaktif di bawah untuk mengisolasi visual masing-masing cascade.";
            case 6:
                return "Untuk mempertahankan performa FPS tinggi, detail komputasi disesuaikan dengan jarak viewer (Material LOD). Cascade detail tinggi (Ripples) hanya dihitung dan dirender pada jarak dekat (Red), sedangkan area menengah (Green) dan jauh (Blue) hanya menggunakan kaskade swell dengan shading yang disederhanakan. Anda dapat menguji perbandingan performa FPS saat optimasi ini dimatikan.";
            case 7:
                return "Deformasi vertikal dan horizontal mesh diselesaikan secara fisik. Peta kemiringan (derivatives) digunakan untuk menghitung normal vector untuk shading PBR. Buih laut (foam) dihasilkan secara dinamis di puncak gelombang yang curam menggunakan cek nilai Jacobian (penyusutan spasial). Gunakan slider di bawah untuk bereksperimen dengan curamnya ombak.";
            case 8:
                return "Sistem simulasi dihubungkan dengan REST API Cloud Database (Firebase Firestore) untuk mengambil data telemetri buoy BMKG/NOAA (Hs dan Wind Speed). Nilai Fetch (lintasan angin) yang tidak diukur langsung diestimasi menggunakan Newton-Raphson Solver berbasis model pertumbuhan gelombang Sverdrup-Munk-Bretschneider (SMB) secara asinkron.";
            default:
                return "";
        }
    }

    private void DrawStageControls(int stage, Rect rect)
    {
        float cx = rect.x;
        float cy = rect.y;
        float cw = rect.width;
        float lh = 22f;

        switch (stage)
        {
            case 1:
                GUI.Label(new Rect(cx, cy, cw, lh), "Status Geometri: Flat Plane Mode", boldTextStyle);
                cy += lh + 10f;
                if (oceanGeometry != null)
                {
                    GUI.Label(new Rect(cx, cy, cw, lh), $"• Grid Clip Levels: {oceanGeometry.ClipLevels}", textStyle);
                    cy += lh;
                    GUI.Label(new Rect(cx, cy, cw, lh), $"• Base Length Scale: {oceanGeometry.LengthScale} meter", textStyle);
                    cy += lh;
                    GUI.Label(new Rect(cx, cy, cw, lh), $"• Vertex Density per Ring: {4 * 30 + 1} x {4 * 30 + 1} vertices", textStyle);
                    cy += lh + 15f;

                    // Toggle button untuk visualisasi warna LOD
                    bool curShowLods = oceanGeometry.ShowMaterialLods;
                    string btnTxt = curShowLods ? "MATIKAN WARNA LOD" : "AKTIFKAN WARNA LOD";
                    if (GUI.Button(new Rect(cx, cy, 200f, 30f), btnTxt, buttonStyle))
                    {
                        oceanGeometry.ShowMaterialLods = !curShowLods;
                        if (!oceanGeometry.ShowMaterialLods)
                        {
                            ForceRestoreMaterials();
                        }
                    }
                }
                break;

            case 2:
                GUI.Label(new Rect(cx, cy, cw, lh), "Gaussian Noise Seed (CPU Box-Muller)", boldTextStyle);
                cy += lh + 10f;
                if (wavesGenerator != null && wavesGenerator.cascade0 != null)
                {
                    Texture2D noiseTex = wavesGenerator.cascade0.GaussianNoise;
                    if (noiseTex != null)
                    {
                        GUI.Label(new Rect(cx, cy, cw, lh), $"• Format Tekstur: {noiseTex.format}", textStyle);
                        cy += lh;
                        GUI.Label(new Rect(cx, cy, cw, lh), $"• Dimensi Grid: {noiseTex.width} x {noiseTex.height} piksel", textStyle);
                        cy += lh;
                        GUI.Label(new Rect(cx, cy, cw, lh), "• Komponen R: Bilangan Acak Riil (Cos)", textStyle);
                        cy += lh;
                        GUI.Label(new Rect(cx, cy, cw, lh), "• Komponen G: Bilangan Acak Imajiner (Sin)", textStyle);
                        cy += lh + 15f;

                        // Draw the noise texture as visual feedback
                        GUI.Label(new Rect(cx, cy, 100f, 100f), "Noise Preview (RG Float):", textStyle);
                        GUI.DrawTexture(new Rect(cx + 180f, cy - 10f, 120f, 120f), noiseTex);
                    }
                }
                break;

            case 3:
                GUI.Label(new Rect(cx, cy, cw, lh), "Spektrum Energi JONSWAP (Beku)", boldTextStyle);
                cy += lh + 10f;
                GUI.Label(new Rect(cx, cy, cw, lh), "• Kecepatan Angin (U): 8.5 m/s", textStyle);
                cy += lh;
                GUI.Label(new Rect(cx, cy, cw, lh), "• Tinggi Gelombang Signifikan (Hs): 1.6m", textStyle);
                cy += lh;
                GUI.Label(new Rect(cx, cy, cw, lh), "• Kedalaman Air (h): 50.0m (TMA Correction)", textStyle);
                cy += lh;
                GUI.Label(new Rect(cx, cy, cw, lh), "• Waktu Simulasi: t = 0 (Frozen)", textStyle);
                cy += lh + 15f;

                GUI.Label(new Rect(cx, cy, cw, 40f), "Persamaan JONSWAP Spektral:\n" +
                           "S(w) = alpha * g^2 * w^-5 * exp(-1.25 * (wp/w)^4) * gamma^r", codeStyle);
                break;

            case 4:
                GUI.Label(new Rect(cx, cy, cw, lh), "FFT & Propagasi Temporal (GPU)", boldTextStyle);
                cy += lh + 10f;
                
                // Slider untuk timeScale
                if (wavesGenerator != null)
                {
                    GUI.Label(new Rect(cx, cy, 150f, lh), $"Kecepatan Waktu: {wavesGenerator.timeScale:F1}x", sliderLabelStyle);
                    wavesGenerator.timeScale = GUI.HorizontalSlider(new Rect(cx + 160f, cy + 4f, cw - 160f, lh), wavesGenerator.timeScale, 0f, 5f);
                    cy += lh + 10f;
                    
                    GUI.Label(new Rect(cx, cy, cw, lh), $"• Kompleksitas 2D IFFT: O(N log N)", textStyle);
                    cy += lh;
                    GUI.Label(new Rect(cx, cy, cw, lh), $"• Ukuran FFT: {noiseTextureWidth()} x {noiseTextureHeight()}", textStyle);
                    cy += lh;
                    
                    bool useGPU = wavesGenerator.useGPUSimulation;
                    string modeStr = useGPU ? "GPU (Compute Shader)" : "CPU (Harmonic Solver)";
                    GUI.Label(new Rect(cx, cy, cw, lh), $"• Mode Eksekusi Aktif: {modeStr}", textStyle);
                    cy += lh + 15f;

                    if (GUI.Button(new Rect(cx, cy, 220f, 30f), "TOGGLE CPU/GPU MODE", buttonStyle))
                    {
                        wavesGenerator.useGPUSimulation = !useGPU;
                    }
                }
                break;

            case 5:
                GUI.Label(new Rect(cx, cy, cw, lh), "Filter & Isolasi Kaskade Gelombang", boldTextStyle);
                cy += lh + 10f;
                
                // Buttons for Cascade Modes
                float bW = (cw - 15f) / 2f;
                float bH = 30f;

                if (GUI.Button(new Rect(cx, cy, bW, bH), "ALL COMBINED", activeCascadeMode == 0 ? activeButtonStyle : buttonStyle))
                {
                    activeCascadeMode = 0;
                    ApplyCascadeMode(activeCascadeMode);
                }
                if (GUI.Button(new Rect(cx + bW + 15f, cy, bW, bH), "SWELL ONLY (C0)", activeCascadeMode == 1 ? activeButtonStyle : buttonStyle))
                {
                    activeCascadeMode = 1;
                    ApplyCascadeMode(activeCascadeMode);
                }
                cy += bH + 10f;

                if (GUI.Button(new Rect(cx, cy, bW, bH), "WIND WAVES ONLY (C1)", activeCascadeMode == 2 ? activeButtonStyle : buttonStyle))
                {
                    activeCascadeMode = 2;
                    ApplyCascadeMode(activeCascadeMode);
                }
                if (GUI.Button(new Rect(cx + bW + 15f, cy, bW, bH), "RIPPLES ONLY (C2)", activeCascadeMode == 3 ? activeButtonStyle : buttonStyle))
                {
                    activeCascadeMode = 3;
                    ApplyCascadeMode(activeCascadeMode);
                }
                cy += bH + 15f;

                // Deskripsi cascade aktif
                string modeDesc = "";
                if (activeCascadeMode == 0) modeDesc = "Gabungan ketiga kaskade menghasilkan detail visual yang kaya dari ombak besar hingga riak permukaan kecil secara real-time.";
                else if (activeCascadeMode == 1) modeDesc = "Kaskade 0 (Swell): Menampilkan komponen gelombang berperiode panjang, panjang gelombang besar (L = 250m).";
                else if (activeCascadeMode == 2) modeDesc = "Kaskade 1 (Wind Wave): Menampilkan komponen gelombang angin lokal (L = 17m) dengan kecepatan angin sedang.";
                else if (activeCascadeMode == 3) modeDesc = "Kaskade 2 (Ripples): Menampilkan riak permukaan mikro (L = 5m) berfrekuensi tinggi.";
                GUI.Label(new Rect(cx, cy, cw, 60f), modeDesc, textStyle);
                break;

            case 6:
                GUI.Label(new Rect(cx, cy, cw, lh), "Optimasi Kinerja (LOD Mesh & Material)", boldTextStyle);
                cy += lh + 10f;
                if (oceanGeometry != null)
                {
                    bool mLog = oceanGeometry.useMaterialLOD;
                    GUI.Label(new Rect(cx, cy, 180f, lh), $"Optimasi Material LOD: {(mLog ? "AKTIF" : "NONAKTIF")}", sliderLabelStyle);
                    
                    if (GUI.Button(new Rect(cx + 200f, cy - 4f, cw - 200f, 26f), mLog ? "NONAKTIFKAN LOD" : "AKTIFKAN LOD", buttonStyle))
                    {
                        oceanGeometry.useMaterialLOD = !mLog;
                    }
                    cy += lh + 10f;

                    bool curShowLods = oceanGeometry.ShowMaterialLods;
                    string btnTxt = curShowLods ? "MATIKAN WARNA LOD" : "AKTIFKAN WARNA LOD";
                    if (GUI.Button(new Rect(cx, cy, 200f, 26f), btnTxt, buttonStyle))
                    {
                        oceanGeometry.ShowMaterialLods = !curShowLods;
                        if (!oceanGeometry.ShowMaterialLods)
                        {
                            ForceRestoreMaterials();
                        }
                    }
                    cy += 36f;

                    // Display info
                    GUI.Label(new Rect(cx, cy, cw, lh), "• Dekat (Merah): Shading lengkap + Kaskade C0, C1, C2", textStyle);
                    cy += lh;
                    GUI.Label(new Rect(cx, cy, cw, lh), "• Sedang (Hijau): Tanpa kaskade C2 (Ripples disembunyikan)", textStyle);
                    cy += lh;
                    GUI.Label(new Rect(cx, cy, cw, lh), "• Jauh (Biru): Tanpa C1 & C2, hanya C0 (Swell lambat)", textStyle);
                    cy += lh + 15f;

                    if (!mLog)
                    {
                        GUI.Label(new Rect(cx, cy, cw, 40f), "PERINGATAN: Menonaktifkan Material LOD akan memaksa pixel shader menjalankan loop dummy (beban berat) di seluruh permukaan, menurunkan FPS.", codeStyle);
                    }
                    else
                    {
                        GUI.Label(new Rect(cx, cy, cw, 40f), "SISTEM STABIL: Material LOD mengurangi kalkulasi tekstur piksel di kejauhan, menjaga frame rate tetap stabil.", textStyle);
                    }
                }
                break;

            case 7:
                GUI.Label(new Rect(cx, cy, cw, lh), "PBR Rendering & Cek Kompresi Jacobian", boldTextStyle);
                cy += lh + 10f;

                // Slider untuk Lambda (Wave Steepness)
                if (wavesGenerator != null && wavesGenerator.cascade0 != null)
                {
                    float currentLambda = wavesGenerator.cascade0.Lambda;
                    GUI.Label(new Rect(cx, cy, 150f, lh), $"Steepness (Lambda): {currentLambda:F2}", sliderLabelStyle);
                    float newLambda = GUI.HorizontalSlider(new Rect(cx + 160f, cy + 4f, cw - 160f, lh), currentLambda, 0f, 1.5f);
                    if (newLambda != currentLambda)
                    {
                        wavesGenerator.lambdaOverride = newLambda;
                    }
                    cy += lh + 10f;
                }

                // Sliders untuk Foam Parameters di Material
                Material oceanMat = GetSharedOceanMaterial();
                if (oceanMat != null)
                {
                    float bias = oceanMat.GetFloat("_FoamBiasLOD0");
                    GUI.Label(new Rect(cx, cy, 150f, lh), $"Foam Threshold: {bias:F2}", sliderLabelStyle);
                    float newBias = GUI.HorizontalSlider(new Rect(cx + 160f, cy + 4f, cw - 160f, lh), bias, 0.1f, 3.0f);
                    if (newBias != bias)
                    {
                        oceanMat.SetFloat("_FoamBiasLOD0", newBias);
                        oceanMat.SetFloat("_FoamBiasLOD1", newBias);
                        oceanMat.SetFloat("_FoamBiasLOD2", newBias);
                    }
                    cy += lh + 10f;

                    float scale = oceanMat.GetFloat("_FoamScale");
                    GUI.Label(new Rect(cx, cy, 150f, lh), $"Foam Intensity: {scale:F2}", sliderLabelStyle);
                    float newScale = GUI.HorizontalSlider(new Rect(cx + 160f, cy + 4f, cw - 160f, lh), scale, 0f, 15.0f);
                    if (newScale != scale)
                    {
                        oceanMat.SetFloat("_FoamScale", newScale);
                    }
                    cy += lh + 15f;
                }

                GUI.Label(new Rect(cx, cy, cw, 40f), "Formulasi Jacobian (Foam):\n" +
                           "J = (1 + dx_dx * lambda) * (1 + dz_dz * lambda) < 0", codeStyle);
                break;

            case 8:
                GUI.Label(new Rect(cx, cy, cw, lh), "Simulasi Telemetri Data Buoy BMKG", boldTextStyle);
                cy += lh + 10f;

                // Preset selection
                float prW = (cw - 20f) / 3f;
                for (int i = 0; i < buoyPresets.Count; i++)
                {
                    if (GUI.Button(new Rect(cx + i * (prW + 10f), cy, prW, 30f), $"PRESET {i+1}", selectedPresetIndex == i ? activeButtonStyle : buttonStyle))
                    {
                        ApplyPreset(i);
                    }
                }
                cy += 38f;

                // Tampilkan info preset aktif
                GUI.Label(new Rect(cx, cy, cw, lh), $"Lokasi Preset: {buoyPresets[selectedPresetIndex].name}", boldTextStyle);
                cy += lh;
                GUI.Label(new Rect(cx, cy, cw, lh), $"* {buoyPresets[selectedPresetIndex].desc}", textStyle);
                cy += lh + 10f;

                // Display live solver variables
                if (buoyDataApplier != null)
                {
                    GUI.Label(new Rect(cx, cy, cw, lh), "STATISTIK NEWTON-RAPHSON SOLVER:", boldTextStyle);
                    cy += lh + 6f;
                    GUI.Label(new Rect(cx, cy, cw, lh), $"• Input Hs Observasi: {buoyDataApplier.LastAppliedData?.WVHT ?? 0f:F2} m", textStyle);
                    cy += lh;
                    GUI.Label(new Rect(cx, cy, cw, lh), $"• Input Kecepatan Angin (U): {buoyDataApplier.AppliedWindSpeed:F1} m/s", textStyle);
                    cy += lh;
                    GUI.Label(new Rect(cx, cy, cw, lh), $"• Arah Angin (WDIR): {buoyDataApplier.AppliedWindDirection:F0}° (+180° offset)", textStyle);
                    cy += lh;
                    GUI.Label(new Rect(cx, cy, cw, lh), $"• Hasil Estimasi Fetch (F): {buoyDataApplier.AppliedFetch / 1000f:F1} km (Iterasi NR)", textStyle);
                    cy += lh;
                    GUI.Label(new Rect(cx, cy, cw, lh), $"• Kalibrasi Skala Amplitudo (Alpha Scale): {buoyDataApplier.AppliedScale:F3}", textStyle);
                    cy += lh;
                    GUI.Label(new Rect(cx, cy, cw, lh), $"• Teoretis Hs (Formula SMB): {buoyDataApplier.AppliedTheoreticalHs:F2} m", textStyle);
                }
                break;
        }
    }

    private int noiseTextureWidth()
    {
        if (wavesGenerator != null && wavesGenerator.cascade0 != null && wavesGenerator.cascade0.GaussianNoise != null)
            return wavesGenerator.cascade0.GaussianNoise.width;
        return 256;
    }

    private int noiseTextureHeight()
    {
        if (wavesGenerator != null && wavesGenerator.cascade0 != null && wavesGenerator.cascade0.GaussianNoise != null)
            return wavesGenerator.cascade0.GaussianNoise.height;
        return 256;
    }

    // Helper untuk menggambar outline border
    private void DrawOutlineBorder(Rect rect, Color color, int thickness)
    {
        GUI.color = color;
        // Top
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), Texture2D.whiteTexture);
        // Bottom
        GUI.DrawTexture(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), Texture2D.whiteTexture);
        // Left
        GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), Texture2D.whiteTexture);
        // Right
        GUI.DrawTexture(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    // Inisialisasi visual style GUI
    private void InitGUIStyles()
    {
        if (stylesInitialized) return;
        stylesInitialized = true;

        panelBgStyle = new GUIStyle();
        
        headerStyle = new GUIStyle();
        headerStyle.fontSize = 18;
        headerStyle.fontStyle = FontStyle.Bold;
        headerStyle.normal.textColor = new Color(0.95f, 0.96f, 0.98f);
        headerStyle.alignment = TextAnchor.UpperLeft;

        subHeaderStyle = new GUIStyle();
        subHeaderStyle.fontSize = 11;
        subHeaderStyle.fontStyle = FontStyle.Italic;
        subHeaderStyle.normal.textColor = new Color(0.6f, 0.7f, 0.85f);
        subHeaderStyle.alignment = TextAnchor.UpperLeft;

        textStyle = new GUIStyle();
        textStyle.fontSize = 12;
        textStyle.wordWrap = true;
        textStyle.normal.textColor = new Color(0.8f, 0.85f, 0.92f);
        textStyle.richText = true;

        boldTextStyle = new GUIStyle(textStyle);
        boldTextStyle.fontStyle = FontStyle.Bold;
        boldTextStyle.normal.textColor = new Color(0.25f, 0.65f, 1.0f);

        codeStyle = new GUIStyle();
        codeStyle.fontSize = 11;
        codeStyle.fontStyle = FontStyle.Normal;
        codeStyle.wordWrap = true;
        codeStyle.normal.textColor = new Color(0.4f, 0.85f, 0.55f);
        codeStyle.padding = new RectOffset(6, 6, 6, 6);

        buttonStyle = new GUIStyle(GUI.skin.button);
        buttonStyle.fontSize = 11;
        buttonStyle.fontStyle = FontStyle.Bold;
        buttonStyle.normal.textColor = Color.white;
        buttonStyle.hover.textColor = new Color(0.3f, 0.7f, 1f);

        activeButtonStyle = new GUIStyle(buttonStyle);
        activeButtonStyle.normal.textColor = new Color(0.2f, 0.95f, 0.45f);

        labelStyle = new GUIStyle();
        labelStyle.fontSize = 12;
        labelStyle.normal.textColor = Color.white;

        sliderLabelStyle = new GUIStyle();
        sliderLabelStyle.fontSize = 12;
        sliderLabelStyle.normal.textColor = new Color(0.7f, 0.8f, 0.9f);
    }
}
