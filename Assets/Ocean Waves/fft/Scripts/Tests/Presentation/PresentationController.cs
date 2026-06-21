using UnityEngine;
using UnityEngine.InputSystem;
using UnityTemplateProjects;

public class PresentationController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] WavesGenerator wavesGenerator;
    [SerializeField] OceanGeometry oceanGeometry;
    [SerializeField] Camera presentationCamera;

    [Header("Camera Lerping Settings")]
    [SerializeField] float cameraLerpSpeed = 4f;
    [SerializeField] float orbitSpeed = 15f;

    // Presentation slides
    public enum PresentationStage
    {
        GridBaseSmallFlat,     // 1. Grid Mesh Kecil (Datar)
        GaussianNoiseInput,    // 2. Gaussian Noise
        InitialSpectrumH0,     // 3. JONSWAP Spectrum h0
        PrecomputeData,        // 4. Precomputed dispersion & wave vectors
        TimeDependentSpectrum, // 5. Time dependent rotation
        IFFT_Cascade0,         // 6. Cascade 0 Waves (Large)
        Add_Cascade1,          // 7. Cascade 0 + 1 (Medium detail)
        Add_Cascade2,          // 8. Cascade 0 + 1 + 2 (High detail)
        LargeSingleMesh,       // 9. Large Single Mesh (Scalability problem)
        ClipmapLODGrid,        // 10. Clipmap Grid Mesh LOD
        MaterialLODColor,      // 11. Material Shader LOD (Colored)
        FullOceanSimulation    // 12. Final full simulation
    }

    private PresentationStage currentStage = PresentationStage.GridBaseSmallFlat;
    private int stageIndex => (int)currentStage;
    private int totalStages = System.Enum.GetValues(typeof(PresentationStage)).Length;

    // Camera preset structure
    private struct CameraPreset
    {
        public Vector3 position;
        public Vector3 rotation;
        public bool allowOrbit;
        public float orbitDistance;
        public float orbitHeight;
        public float orbitPitch;

        public CameraPreset(Vector3 pos, Vector3 rot, bool orbit, float dist = 20f, float height = 15f, float pitch = 35f)
        {
            position = pos;
            rotation = rot;
            allowOrbit = orbit;
            orbitDistance = dist;
            orbitHeight = height;
            orbitPitch = pitch;
        }
    }

    private CameraPreset[] cameraPresets;

    // Presentation state variables
    private bool wireframeEnabled = false;
    private bool orbitEnabled = false;
    private bool isManualControl = false;
    private float orbitAngle = 0f;

    // Cache original settings to restore on end/deactivate
    private int originalClipLevels;
    private float originalLengthScale;
    private bool originalUseMaterialLOD;
    private bool originalShowMaterialLods;

    // UI assets and styles
    private Texture2D panelBackground;
    private GUIStyle panelStyle;
    private GUIStyle titleStyle;
    private GUIStyle descStyle;
    private GUIStyle mathStyle;
    private GUIStyle btnStyle;
    private GUIStyle toggleStyle;

    private PresentationWireframe wireframeController;
    private SimpleCameraController cameraController;

    private void Start()
    {
        // Auto-assign references if empty
        if (wavesGenerator == null)
            wavesGenerator = FindFirstObjectByType<WavesGenerator>();
        if (oceanGeometry == null)
            oceanGeometry = FindFirstObjectByType<OceanGeometry>();
        if (presentationCamera == null)
            presentationCamera = Camera.main;

        if (presentationCamera != null)
        {
            // Add or find wireframe helper
            wireframeController = presentationCamera.gameObject.GetComponent<PresentationWireframe>();
            if (wireframeController == null)
                wireframeController = presentationCamera.gameObject.AddComponent<PresentationWireframe>();

            // Find camera controller
            cameraController = presentationCamera.gameObject.GetComponent<SimpleCameraController>();
        }

        // Cache original ocean configurations
        if (oceanGeometry != null)
        {
            originalClipLevels = oceanGeometry.ClipLevels;
            originalLengthScale = oceanGeometry.LengthScale;
            originalUseMaterialLOD = oceanGeometry.useMaterialLOD;
            originalShowMaterialLods = oceanGeometry.ShowMaterialLods;
        }

        InitializeCameraPresets();
        ApplyStageSettings();
    }

    private void InitializeCameraPresets()
    {
        cameraPresets = new CameraPreset[totalStages];
        
        // 1. Grid Mesh Kecil (Datar)
        cameraPresets[0] = new CameraPreset(new Vector3(0, 18, -25), new Vector3(35, 0, 0), true, 30f, 18f, 35f);
        // 2. Gaussian Noise
        cameraPresets[1] = new CameraPreset(new Vector3(0, 28, -0.01f), new Vector3(90, 0, 0), false);
        // 3. JONSWAP Spectrum h0
        cameraPresets[2] = new CameraPreset(new Vector3(0, 28, -0.01f), new Vector3(90, 0, 0), false);
        // 4. Precompute Data
        cameraPresets[3] = new CameraPreset(new Vector3(0, 28, -0.01f), new Vector3(90, 0, 0), false);
        // 5. Time Dependent Spectrum
        cameraPresets[4] = new CameraPreset(new Vector3(0, 28, -0.01f), new Vector3(90, 0, 0), false);
        // 6. IFFT Cascade 0
        cameraPresets[5] = new CameraPreset(new Vector3(0, 10, -18), new Vector3(25, 0, 0), true, 21f, 10f, 25f);
        // 7. Cascade 0 + 1
        cameraPresets[6] = new CameraPreset(new Vector3(0, 8, -15), new Vector3(20, 0, 0), true, 17f, 8f, 20f);
        // 8. Cascade 0 + 1 + 2 (Small Plane)
        cameraPresets[7] = new CameraPreset(new Vector3(0, 6, -11), new Vector3(15, 0, 0), true, 13f, 6f, 15f);
        // 9. Large Single Plane
        cameraPresets[8] = new CameraPreset(new Vector3(0, 45, -95), new Vector3(25, 0, 0), true, 105f, 45f, 25f);
        // 10. Clipmap Grid LOD
        cameraPresets[9] = new CameraPreset(new Vector3(0, 150, -270), new Vector3(30, 0, 0), true, 310f, 150f, 30f);
        // 11. Material LOD Colored
        cameraPresets[10] = new CameraPreset(new Vector3(0, 100, -190), new Vector3(28, 0, 0), true, 210f, 100f, 28f);
        // 12. Final Full Ocean
        cameraPresets[11] = new CameraPreset(new Vector3(0, 14, -28), new Vector3(15, 0, 0), true, 31f, 14f, 15f);
    }

    private void Update()
    {
        // Keyboard inputs for presentation control
        if (Keyboard.current != null)
        {
            if (Keyboard.current.spaceKey.wasPressedThisFrame || Keyboard.current.rightArrowKey.wasPressedThisFrame)
            {
                NextStage();
            }
            else if (Keyboard.current.leftArrowKey.wasPressedThisFrame)
            {
                PreviousStage();
            }
            else if (Keyboard.current.fKey.wasPressedThisFrame)
            {
                ToggleWireframe();
            }
            else if (Keyboard.current.rKey.wasPressedThisFrame)
            {
                ToggleOrbit();
            }
            else if (Keyboard.current.cKey.wasPressedThisFrame)
            {
                ToggleManualControl();
            }
        }

        // Camera movement updates
        if (!isManualControl && presentationCamera != null)
        {
            CameraPreset preset = cameraPresets[stageIndex];
            Vector3 targetPos = preset.position;
            Vector3 targetRot = preset.rotation;

            if (preset.allowOrbit && orbitEnabled)
            {
                orbitAngle += Time.deltaTime * orbitSpeed;
                float angleRad = orbitAngle * Mathf.Deg2Rad;
                Vector3 offset = new Vector3(Mathf.Sin(angleRad) * preset.orbitDistance, preset.orbitHeight, -Mathf.Cos(angleRad) * preset.orbitDistance);
                targetPos = Vector3.zero + offset;
                targetRot = new Vector3(preset.orbitPitch, orbitAngle, 0);
            }

            presentationCamera.transform.position = Vector3.Lerp(presentationCamera.transform.position, targetPos, Time.deltaTime * cameraLerpSpeed);
            presentationCamera.transform.rotation = Quaternion.Slerp(presentationCamera.transform.rotation, Quaternion.Euler(targetRot), Time.deltaTime * cameraLerpSpeed);
        }
    }

    public void NextStage()
    {
        int next = (stageIndex + 1) % totalStages;
        SetStage((PresentationStage)next);
    }

    public void PreviousStage()
    {
        int prev = (stageIndex - 1 + totalStages) % totalStages;
        SetStage((PresentationStage)prev);
    }

    public void SetStage(PresentationStage stage)
    {
        currentStage = stage;
        isManualControl = false; // Reset to automatic camera on slide transition
        
        // Match orbit angle starting point to default rotation of preset
        orbitAngle = cameraPresets[stageIndex].rotation.y;

        ApplyStageSettings();
    }

    private void ApplyStageSettings()
    {
        if (oceanGeometry == null || wavesGenerator == null) return;

        // Toggle manual camera script based on manual override
        if (cameraController != null)
        {
            cameraController.enabled = isManualControl;
            if (isManualControl)
            {
                // Lock mouse cursor when manual camera is active
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        // Apply wireframe to camera controller
        if (wireframeController != null)
        {
            wireframeController.showWireframe = wireframeEnabled;
        }

        // Hide/show skirt based on stage
        Transform skirtTr = oceanGeometry.transform.Find("Skirt");
        bool showSkirt = (currentStage == PresentationStage.FullOceanSimulation);
        if (skirtTr != null)
        {
            skirtTr.gameObject.SetActive(showSkirt);
        }

        // Apply specific configurations per stage
        switch (currentStage)
        {
            case PresentationStage.GridBaseSmallFlat:
                oceanGeometry.ClipLevels = 0;
                oceanGeometry.LengthScale = 10f;
                oceanGeometry.useMaterialLOD = false;
                oceanGeometry.ShowMaterialLods = false;
                wireframeEnabled = true;
                orbitEnabled = true;
                SetAllCascadesActive(false);
                break;

            case PresentationStage.GaussianNoiseInput:
                oceanGeometry.ClipLevels = 0;
                oceanGeometry.LengthScale = 10f;
                oceanGeometry.useMaterialLOD = false;
                oceanGeometry.ShowMaterialLods = false;
                wireframeEnabled = false;
                orbitEnabled = false;
                SetAllCascadesActive(false);
                break;

            case PresentationStage.InitialSpectrumH0:
                oceanGeometry.ClipLevels = 0;
                oceanGeometry.LengthScale = 10f;
                oceanGeometry.useMaterialLOD = false;
                oceanGeometry.ShowMaterialLods = false;
                wireframeEnabled = false;
                orbitEnabled = false;
                SetAllCascadesActive(false);
                break;

            case PresentationStage.PrecomputeData:
                oceanGeometry.ClipLevels = 0;
                oceanGeometry.LengthScale = 10f;
                oceanGeometry.useMaterialLOD = false;
                oceanGeometry.ShowMaterialLods = false;
                wireframeEnabled = false;
                orbitEnabled = false;
                SetAllCascadesActive(false);
                break;

            case PresentationStage.TimeDependentSpectrum:
                oceanGeometry.ClipLevels = 0;
                oceanGeometry.LengthScale = 10f;
                oceanGeometry.useMaterialLOD = false;
                oceanGeometry.ShowMaterialLods = false;
                wireframeEnabled = false;
                orbitEnabled = false;
                SetAllCascadesActive(false);
                break;

            case PresentationStage.IFFT_Cascade0:
                oceanGeometry.ClipLevels = 0;
                oceanGeometry.LengthScale = 10f;
                oceanGeometry.useMaterialLOD = false;
                oceanGeometry.ShowMaterialLods = false;
                wireframeEnabled = false;
                orbitEnabled = true;
                SetCascadeActive(0, true);
                SetCascadeActive(1, false);
                SetCascadeActive(2, false);
                break;

            case PresentationStage.Add_Cascade1:
                oceanGeometry.ClipLevels = 0;
                oceanGeometry.LengthScale = 10f;
                oceanGeometry.useMaterialLOD = false;
                oceanGeometry.ShowMaterialLods = false;
                wireframeEnabled = false;
                orbitEnabled = true;
                SetCascadeActive(0, true);
                SetCascadeActive(1, true);
                SetCascadeActive(2, false);
                break;

            case PresentationStage.Add_Cascade2:
                oceanGeometry.ClipLevels = 0;
                oceanGeometry.LengthScale = 10f;
                oceanGeometry.useMaterialLOD = false;
                oceanGeometry.ShowMaterialLods = false;
                wireframeEnabled = false;
                orbitEnabled = true;
                SetCascadeActive(0, true);
                SetCascadeActive(1, true);
                SetCascadeActive(2, true);
                break;

            case PresentationStage.LargeSingleMesh:
                oceanGeometry.ClipLevels = 0;
                oceanGeometry.LengthScale = 100f; // Scale increased by 10x!
                oceanGeometry.useMaterialLOD = false;
                oceanGeometry.ShowMaterialLods = false;
                wireframeEnabled = true;
                orbitEnabled = true;
                SetCascadeActive(0, true);
                SetCascadeActive(1, true);
                SetCascadeActive(2, true);
                break;

            case PresentationStage.ClipmapLODGrid:
                oceanGeometry.ClipLevels = originalClipLevels;
                oceanGeometry.LengthScale = originalLengthScale;
                oceanGeometry.useMaterialLOD = false;
                oceanGeometry.ShowMaterialLods = false;
                wireframeEnabled = true;
                orbitEnabled = true;
                SetCascadeActive(0, true);
                SetCascadeActive(1, true);
                SetCascadeActive(2, true);
                break;

            case PresentationStage.MaterialLODColor:
                oceanGeometry.ClipLevels = originalClipLevels;
                oceanGeometry.LengthScale = originalLengthScale;
                oceanGeometry.useMaterialLOD = true;
                oceanGeometry.ShowMaterialLods = true;
                wireframeEnabled = false;
                orbitEnabled = true;
                SetCascadeActive(0, true);
                SetCascadeActive(1, true);
                SetCascadeActive(2, true);
                break;

            case PresentationStage.FullOceanSimulation:
                oceanGeometry.ClipLevels = originalClipLevels;
                oceanGeometry.LengthScale = originalLengthScale;
                oceanGeometry.useMaterialLOD = originalUseMaterialLOD;
                oceanGeometry.ShowMaterialLods = false;
                wireframeEnabled = false;
                orbitEnabled = false; // Turn off orbit to allow camera control
                isManualControl = true; // Turn on free camera flight
                if (cameraController != null) cameraController.enabled = true;
                SetCascadeActive(0, true);
                SetCascadeActive(1, true);
                SetCascadeActive(2, true);
                break;
        }

        // Apply wireframe to camera controller again to make sure it matches
        if (wireframeController != null)
        {
            wireframeController.showWireframe = wireframeEnabled;
        }
    }

    private void SetAllCascadesActive(bool active)
    {
        SetCascadeActive(0, active);
        SetCascadeActive(1, active);
        SetCascadeActive(2, active);
    }

    private void SetCascadeActive(int index, bool active)
    {
        if (oceanGeometry == null || wavesGenerator == null) return;

        if (active)
        {
            if (index == 0)
                oceanGeometry.SetCascadeTextures(0, wavesGenerator.cascade0.Displacement, wavesGenerator.cascade0.Derivatives, wavesGenerator.cascade0.Turbulence);
            else if (index == 1)
                oceanGeometry.SetCascadeTextures(1, wavesGenerator.cascade1.Displacement, wavesGenerator.cascade1.Derivatives, wavesGenerator.cascade1.Turbulence);
            else if (index == 2)
                oceanGeometry.SetCascadeTextures(2, wavesGenerator.cascade2.Displacement, wavesGenerator.cascade2.Derivatives, wavesGenerator.cascade2.Turbulence);
        }
        else
        {
            oceanGeometry.SetCascadeTextures(index, Texture2D.blackTexture, Texture2D.blackTexture, Texture2D.whiteTexture);
        }
    }

    private void ToggleWireframe()
    {
        wireframeEnabled = !wireframeEnabled;
        if (wireframeController != null)
            wireframeController.showWireframe = wireframeEnabled;
    }

    private void ToggleOrbit()
    {
        orbitEnabled = !orbitEnabled;
    }

    private void ToggleManualControl()
    {
        isManualControl = !isManualControl;
        if (cameraController != null)
        {
            cameraController.enabled = isManualControl;
        }
    }

    private void OnDestroy()
    {
        // Restore original ocean settings on exit
        if (oceanGeometry != null)
        {
            oceanGeometry.ClipLevels = originalClipLevels;
            oceanGeometry.LengthScale = originalLengthScale;
            oceanGeometry.useMaterialLOD = originalUseMaterialLOD;
            oceanGeometry.ShowMaterialLods = originalShowMaterialLods;
            
            // Re-bind original textures
            SetCascadeActive(0, true);
            SetCascadeActive(1, true);
            SetCascadeActive(2, true);
        }

        if (panelBackground != null)
        {
            Destroy(panelBackground);
        }
    }

    private void InitUIStyles()
    {
        if (panelBackground == null)
        {
            panelBackground = new Texture2D(1, 1);
            panelBackground.SetPixel(0, 0, new Color(0.04f, 0.04f, 0.08f, 0.88f));
            panelBackground.Apply();
        }

        if (panelStyle == null)
        {
            panelStyle = new GUIStyle(GUI.skin.box);
            panelStyle.normal.background = panelBackground;
            panelStyle.border = new RectOffset(0, 0, 0, 0);
            
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };
            titleStyle.normal.textColor = Color.white;

            descStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                wordWrap = true,
                alignment = TextAnchor.UpperLeft
            };
            descStyle.normal.textColor = new Color(0.9f, 0.9f, 0.95f, 1f);

            mathStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 12,
                fontStyle = FontStyle.Italic,
                alignment = TextAnchor.MiddleCenter
            };
            mathStyle.normal.textColor = new Color(0.3f, 0.7f, 1f, 1f);

            btnStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold
            };
            btnStyle.normal.textColor = Color.white;

            toggleStyle = new GUIStyle(GUI.skin.toggle)
            {
                fontSize = 12
            };
            toggleStyle.normal.textColor = Color.white;
        }
    }

    private void OnGUI()
    {
        InitUIStyles();

        // 1. TOP PANEL: Title & Slide progress bar
        float screenW = Screen.width;
        float screenH = Screen.height;

        Rect topRect = new Rect(15, 15, screenW - 30, 45);
        DrawGlassPanel(topRect);
        
        GUI.Label(new Rect(30, 20, 500, 35), "🌊 PRESENTASI TUGAS AKHIR: ANALISIS OPTIMASI SIMULASI FFT OCEAN WAVES", titleStyle);
        
        GUIStyle progressStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Bold, fontSize = 14 };
        progressStyle.normal.textColor = new Color(0.2f, 0.6f, 1f, 1f);
        GUI.Label(new Rect(screenW - 250, 20, 220, 35), $"Tahap {stageIndex + 1} / {totalStages}", progressStyle);

        // 2. LEFT PANEL: Explanation text
        float leftWidth = 360f;
        float leftHeight = screenH - 180f;
        Rect leftRect = new Rect(15, 75, leftWidth, leftHeight);
        DrawGlassPanel(leftRect);

        GUILayout.BeginArea(new Rect(25, 85, leftWidth - 20, leftHeight - 20));
        GUILayout.Label(GetStageNameIndonesian(), titleStyle);
        GUILayout.Space(15);
        GUILayout.Label(GetStageDescriptionIndonesian(), descStyle);
        
        string formula = GetStageFormula();
        if (!string.IsNullOrEmpty(formula))
        {
            GUILayout.Space(15);
            GUILayout.Box(formula, mathStyle, GUILayout.Height(35));
        }

        GUILayout.EndArea();

        // 3. RIGHT PANEL: Math/GPU Texture Visualizer
        Texture texToDraw = GetStageVisualizedTexture();
        if (texToDraw != null)
        {
            float rightWidth = 280f;
            float rightHeight = 310f;
            Rect rightRect = new Rect(screenW - rightWidth - 15, 75, rightWidth, rightHeight);
            DrawGlassPanel(rightRect);

            GUILayout.BeginArea(new Rect(screenW - rightWidth - 5, 85, rightWidth - 20, rightHeight - 20));
            GUIStyle rightTitleStyle = new GUIStyle(titleStyle) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
            GUILayout.Label(GetTextureVisualizerTitle(), rightTitleStyle);
            GUILayout.Space(10);
            
            // Draw Texture
            Rect texRect = GUILayoutUtility.GetRect(240, 240);
            GUI.DrawTexture(texRect, texToDraw, ScaleMode.ScaleToFit);
            
            GUILayout.EndArea();
        }

        // 4. BOTTOM PANEL: Navigation & Metrics
        float bottomHeight = 70f;
        Rect bottomRect = new Rect(15, screenH - bottomHeight - 15, screenW - 30, bottomHeight);
        DrawGlassPanel(bottomRect);

        GUILayout.BeginArea(new Rect(30, screenH - bottomHeight - 5, screenW - 60, bottomHeight - 10));
        GUILayout.BeginHorizontal();

        // Navigation buttons
        if (GUILayout.Button("◀ BACK", btnStyle, GUILayout.Width(100), GUILayout.Height(40)))
        {
            PreviousStage();
        }
        GUILayout.Space(10);
        if (GUILayout.Button("NEXT ▶", btnStyle, GUILayout.Width(100), GUILayout.Height(40)))
        {
            NextStage();
        }

        GUILayout.Space(40);

        // Control Toggles
        GUILayout.BeginVertical();
        GUILayout.Space(5);
        bool nextWire = GUILayout.Toggle(wireframeEnabled, " [F] Wireframe Mode", toggleStyle);
        if (nextWire != wireframeEnabled) ToggleWireframe();

        bool nextOrbit = GUILayout.Toggle(orbitEnabled, " [R] Camera Auto-Orbit", toggleStyle);
        if (nextOrbit != orbitEnabled) ToggleOrbit();
        GUILayout.EndVertical();

        GUILayout.Space(20);

        GUILayout.BeginVertical();
        GUILayout.Space(5);
        bool nextManual = GUILayout.Toggle(isManualControl, " [C] Free-Fly Camera", toggleStyle);
        if (nextManual != isManualControl) ToggleManualControl();
        GUILayout.EndVertical();

        GUILayout.FlexibleSpace();

        // Metrics Panel
        GUILayout.BeginVertical();
        GUILayout.Space(2);
        
        float fps = 1.0f / Time.deltaTime;
        GUIStyle metricStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleRight };
        metricStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f);

        GUILayout.Label($"<b>FPS:</b> {fps:0.0} ({Time.deltaTime * 1000:0.0} ms)", metricStyle);
        GUILayout.Label($"<b>Clip Levels:</b> {oceanGeometry.ClipLevels} | <b>Length Scale:</b> {oceanGeometry.LengthScale:0}m", metricStyle);
        GUILayout.Label($"<b>LOD Mode:</b> {(oceanGeometry.useMaterialLOD ? (oceanGeometry.ShowMaterialLods ? "Material LOD (Colored)" : "Material LOD") : "Disabled")}", metricStyle);
        GUILayout.EndVertical();

        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    private void DrawGlassPanel(Rect rect)
    {
        GUI.color = new Color(0.04f, 0.04f, 0.08f, 0.88f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
        DrawBorder(rect, new Color(0.2f, 0.5f, 0.8f, 0.5f));
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

    // Indonesian titles for each stage
    private string GetStageNameIndonesian()
    {
        switch (currentStage)
        {
            case PresentationStage.GridBaseSmallFlat: return "1. Grid Mesh Dasar (Datar)";
            case PresentationStage.GaussianNoiseInput: return "2. Gaussian Noise Input";
            case PresentationStage.InitialSpectrumH0: return "3. Spektrum Awal JONSWAP";
            case PresentationStage.PrecomputeData: return "4. Precomputed Data (Dispersi)";
            case PresentationStage.TimeDependentSpectrum: return "5. Spektrum Dependen-Waktu";
            case PresentationStage.IFFT_Cascade0: return "6. IFFT - Cascade 0 (Gelombang Besar)";
            case PresentationStage.Add_Cascade1: return "7. Cascade 1 (Gelombang Sedang)";
            case PresentationStage.Add_Cascade2: return "8. Cascade 2 (Riak Air)";
            case PresentationStage.LargeSingleMesh: return "9. Masalah Skalabilitas Mesh Tunggal";
            case PresentationStage.ClipmapLODGrid: return "10. Solusi Geometri: Clipmap LOD";
            case PresentationStage.MaterialLODColor: return "11. Solusi Shading: Material LOD";
            case PresentationStage.FullOceanSimulation: return "12. Simulasi Penuh & Navigasi Bebas";
            default: return "";
        }
    }

    // Indonesian detailed descriptions
    private string GetStageDescriptionIndonesian()
    {
        switch (currentStage)
        {
            case PresentationStage.GridBaseSmallFlat:
                return "Ini adalah plane dasar berukuran kecil (40m x 40m) dengan resolusi grid rapat yang terpusat di sekitar kamera. Tahap awal ini menunjukkan struktur grid mesh mentah sebelum diterapkan deformasi (displacement) fisik dari perhitungan gelombang. Grid ini menjadi landasan dasar di mana perpindahan posisi vertex 3D akan dipetakan.";
            
            case PresentationStage.GaussianNoiseInput:
                return "Proses pemodelan gelombang dimulai dengan membangkitkan noise acak terdistribusi Gaussian di CPU menggunakan transformasi Box-Muller. Angka acak ini merepresentasikan ketidakaturan awal permukaan laut. Tekstur noise yang dihasilkan dikirim ke GPU sebagai data masukan (seed) awal berukuran 256x256.";
            
            case PresentationStage.InitialSpectrumH0:
                return "Tekstur Gaussian Noise disaring (di-filter) di GPU melalui Compute Shader menggunakan model spektrum empiris JONSWAP. Model JONSWAP merumuskan profil energi gelombang berdasarkan parameter fisik nyata: kecepatan angin, arah hembusan angin, kedalaman air, dan fetch (panjang permukaan air bebas yang tertiup angin). Menghasilkan tekstur spektrum amplitudo awal h0(k).";
            
            case PresentationStage.PrecomputeData:
                return "Untuk menghindari perhitungan fungsi matematika trigonometri dan hiperbolik yang berat di setiap frame, nilai hubungan dispersi gelombang w(k) dan vektor gelombang k dihitung satu kali saja di awal. Nilai dispersi ini didasarkan pada kedalaman air dan gravitasi bumi. Hasilnya disimpan dalam precomputed texture untuk langsung dibaca di compute shader.";
            
            case PresentationStage.TimeDependentSpectrum:
                return "Agar gelombang dapat bergerak seiring waktu, spektrum awal h0(k) dikalikan dengan faktor fase harmonik temporal e^(i*w*t) pada setiap frame. Proses rotasi spektrum ini mensimulasikan dinamika pergerakan gelombang di domain frekuensi. Pada visualisasi kanan, Anda dapat melihat intensitas nilai spektrum berfluktuasi secara dinamis.";
            
            case PresentationStage.IFFT_Cascade0:
                return "Inverse Fast Fourier Transform (IFFT) digunakan untuk mengubah spektrum dari domain frekuensi kembali ke domain spasial (posisi 3D nyata). Proses IFFT menghasilkan displacement map 3D (X, Y, Z offsets) pada koordinat vertex. Cascade 0 mensimulasikan gelombang terbesar dengan skala panjang gelombang terpanjang (250m) untuk membentuk pola dasar gelombang.";
            
            case PresentationStage.Add_Cascade1:
                return "Menambahkan Cascade 1 (panjang skala 17m) di atas Cascade 0. Cascade 1 menangani gelombang frekuensi menengah. Penambahan cascade kedua ini memecah pola visual gelombang utama yang monoton, memberikan variasi bentuk yang lebih organik dan dinamis pada permukaan air.";
            
            case PresentationStage.Add_Cascade2:
                return "Menambahkan Cascade 2 (panjang skala 5m) yang merepresentasikan riak air kecil (ripples) berfrekuensi tinggi. Kombinasi 3 Cascade FFT dihitung secara paralel di GPU untuk menghasilkan detail gelombang yang sangat kaya dan realistis pada permukaan plane kecil.";
            
            case PresentationStage.LargeSingleMesh:
                return "Mendemonstrasikan masalah skalabilitas. Ketika plane tunggal diperbesar (dari 40m ke 400m) tanpa optimasi LOD, jarak antar vertex merenggang (resolusi berkurang). Akibatnya, detail gelombang halus (Cascade 2) menghilang dan permukaan air tampak patah-patah. Jika kerapatan vertex dinaikkan di seluruh area, beban rendering vertex akan melonjak drastis dan tidak efisien.";
            
            case PresentationStage.ClipmapLODGrid:
                return "Solusi LOD Geometri: membagi mesh menjadi ring konsentris (Clipmap) di sekitar kamera. Grid sangat padat di dekat kamera untuk menangkap detail gelombang kecil, dan secara bertahap merenggang di jarak jauh. Saat kamera bergerak, posisi ring ikut tergeser (snap) menyesuaikan posisi kamera, menghemat jutaan poligon secara dinamis.";
            
            case PresentationStage.MaterialLODColor:
                return "Solusi Shading LOD: membagi beban pixel shader berdasarkan jarak kamera menggunakan multi-material. Zona MERAH (Dekat) merender semua 3 Cascade FFT, zona HIJAU (Sedang) hanya merender 2 Cascade, dan zona BIRU (Jauh) merender 1 Cascade. Hal ini secara drastis mengurangi beban kalkulasi spektrum dan IFFT di pixel shader untuk area yang jauh.";
            
            case PresentationStage.FullOceanSimulation:
                return "Hasil akhir penggabungan seluruh sistem optimasi. Geometri Clipmap, 3 Cascade FFT, Material LOD, busa (foam) berbasis Jacobian, dan kamera bebas diaktifkan bersamaan. Menghasilkan visualisasi lautan luas tanpa batas yang detail di dekat mata, efisien di kejauhan, dan berjalan dengan frame rate tinggi (>60 FPS).";
            
            default:
                return "";
        }
    }

    // Mathematical formulas/representations for each stage
    private string GetStageFormula()
    {
        switch (currentStage)
        {
            case PresentationStage.GridBaseSmallFlat: return "Vertex Positions: V(x, y, z) = (x, 0, z)";
            case PresentationStage.GaussianNoiseInput: return "RNG: x_g = cos(2*pi*u_1) * sqrt(-2 * ln(u_2))";
            case PresentationStage.InitialSpectrumH0: return "JONSWAP: S(w) = alpha * g^2 * w^-5 * exp(-1.25 * (wp/w)^4) * gamma^r";
            case PresentationStage.PrecomputeData: return "Dispersion: w^2 = g * k * tanh(depth * k)";
            case PresentationStage.TimeDependentSpectrum: return "Spectrum Time Evolution: h(k, t) = h0(k)*e^(i*w*t) + h0*(-k)*e^(-i*w*t)";
            case PresentationStage.IFFT_Cascade0: return "IFFT Spatial Displacement: D(x, t) = sum[ h(k, t) * e^(i * k * x) ]";
            case PresentationStage.Add_Cascade1: return "Combined Waves: D_total = D_c0 + D_c1";
            case PresentationStage.Add_Cascade2: return "Multi-scale Cascades: D_total = D_c0 + D_c1 + D_c2";
            case PresentationStage.LargeSingleMesh: return "Density loss: Distance between vertices dx = Length / Resolution";
            case PresentationStage.ClipmapLODGrid: return "Clipmap scale at level L: scale_L = base_scale * 2^L";
            case PresentationStage.MaterialLODColor: return "Active Cascades: Close (C0+C1+C2) -> Mid (C0+C1) -> Far (C0)";
            case PresentationStage.FullOceanSimulation: return "Total Optimized Ocean System (Real-Time)";
            default: return "";
        }
    }

    private Texture GetStageVisualizedTexture()
    {
        if (wavesGenerator == null) return null;

        switch (currentStage)
        {
            case PresentationStage.GaussianNoiseInput:
                return wavesGenerator.cascade0.GaussianNoise;
            
            case PresentationStage.InitialSpectrumH0:
            case PresentationStage.TimeDependentSpectrum:
                return wavesGenerator.cascade0.InitialSpectrum;
            
            case PresentationStage.PrecomputeData:
                return wavesGenerator.cascade0.PrecomputedData;
            
            case PresentationStage.IFFT_Cascade0:
                return wavesGenerator.cascade0.Displacement;
            
            case PresentationStage.Add_Cascade1:
                return wavesGenerator.cascade1.Displacement;
            
            case PresentationStage.Add_Cascade2:
            case PresentationStage.LargeSingleMesh:
                return wavesGenerator.cascade2.Displacement;
            
            default:
                return null;
        }
    }

    private string GetTextureVisualizerTitle()
    {
        switch (currentStage)
        {
            case PresentationStage.GaussianNoiseInput: return "GAUSSIAN NOISE TEXTURE (2D)";
            case PresentationStage.InitialSpectrumH0: return "INITIAL SPECTRUM H0(K)";
            case PresentationStage.PrecomputeData: return "PRECOMPUTED DATA (W & K)";
            case PresentationStage.TimeDependentSpectrum: return "SPECTRUM OVER TIME H(K, T)";
            case PresentationStage.IFFT_Cascade0: return "IFFT DISPLACEMENT TEXTURE C0";
            case PresentationStage.Add_Cascade1: return "IFFT DISPLACEMENT TEXTURE C1";
            case PresentationStage.Add_Cascade2:
            case PresentationStage.LargeSingleMesh: return "IFFT DISPLACEMENT TEXTURE C2";
            default: return "";
        }
    }
}
