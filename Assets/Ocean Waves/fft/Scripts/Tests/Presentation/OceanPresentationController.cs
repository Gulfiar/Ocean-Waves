using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

/// <summary>
/// Helper component to toggle wireframe rendering for a camera.
/// Must be attached to the camera GameObject.
/// </summary>
public class WireframeCameraHelper : MonoBehaviour
{
    public bool drawWireframe = false;

    private void OnPreRender()
    {
        if (drawWireframe)
        {
            GL.wireframe = true;
        }
    }

    private void OnPostRender()
    {
        if (drawWireframe)
        {
            GL.wireframe = false;
        }
    }
}

/// <summary>
/// Controller for the academic presentation of the FFT Ocean Waves thesis.
/// Manages 7 distinct stages of the wave generation and rendering pipeline.
/// </summary>
public class OceanPresentationController : MonoBehaviour
{
    public enum PresentationStep
    {
        GridGeometry = 0,     // Step 1: Base Mesh Grid (Geometry Clipmap)
        InitialSpectrum = 1,  // Step 2: Initial Spectrum h0(k) (Static waves)
        TimeEvolution = 2,    // Step 3: Temporal Propagation (Moving waves, no choppiness)
        ChoppyDeformation = 3,// Step 4: Horizontal Displacement (Sharp crests / lambda active)
        MultiCascade = 4,     // Step 5: Multi-scale Cascades Blending (Swell vs Ripples)
        JacobianFoam = 5,     // Step 6: Wave Compression Foam (Whitecaps)
        DigitalTwinBuoy = 6   // Step 7: Buoy sensor bobbing & database integration
    }

    [Header("Presentation Settings")]
    [SerializeField] private PresentationStep currentStep = PresentationStep.GridGeometry;
    [SerializeField] private bool showPresentationUI = true;

    [Header("References")]
    [SerializeField] private WavesGenerator wavesGenerator;
    [SerializeField] private OceanGeometry oceanGeometry;
    [SerializeField] private BuoyDataApplier buoyDataApplier;
    [SerializeField] private DatabaseManager databaseManager;
    [SerializeField] private Camera mainCamera;

    [Header("Visual Options")]
    [SerializeField] private bool enableWireframe = false;
    [SerializeField] private bool showCascade0 = true;
    [SerializeField] private bool showCascade1 = true;
    [SerializeField] private bool showCascade2 = true;

    // Procedural Buoy reference
    private GameObject proceduralBuoy;
    private WireframeCameraHelper wireframeHelper;

    // Cache textures to restore or override
    private Texture flatDisplacement;
    private Texture flatDerivatives;
    private Texture flatTurbulence;

    // Default material settings backup
    private float defaultFoamScale = 1.0f;
    private float defaultContactFoam = 1.0f;
    private Material oceanMaterialInstance;

    // UI Styles
    private GUIStyle windowStyle;
    private GUIStyle headerStyle;
    private GUIStyle subHeaderStyle;
    private GUIStyle bodyStyle;
    private GUIStyle formulaStyle;
    private GUIStyle buttonStyle;
    private GUIStyle stepActiveStyle;
    private GUIStyle stepInactiveStyle;

    private void Start()
    {
        // Auto-resolve references
        if (wavesGenerator == null) wavesGenerator = FindFirstObjectByType<WavesGenerator>();
        if (oceanGeometry == null) oceanGeometry = FindFirstObjectByType<OceanGeometry>();
        if (buoyDataApplier == null) buoyDataApplier = FindFirstObjectByType<BuoyDataApplier>();
        if (databaseManager == null) databaseManager = FindFirstObjectByType<DatabaseManager>();
        if (mainCamera == null) mainCamera = Camera.main;

        // Setup Wireframe Helper on main camera
        if (mainCamera != null)
        {
            wireframeHelper = mainCamera.GetComponent<WireframeCameraHelper>();
            if (wireframeHelper == null)
            {
                wireframeHelper = mainCamera.gameObject.AddComponent<WireframeCameraHelper>();
            }
        }

        // Setup Flat Textures
        flatDisplacement = Texture2D.blackTexture;
        flatDerivatives = Texture2D.blackTexture;
        flatTurbulence = Texture2D.whiteTexture;

        // Backup default material configurations
        if (oceanGeometry != null)
        {
            // Access internal material
            var field = typeof(OceanGeometry).GetField("oceanMaterial", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            if (field != null)
            {
                Material originalMat = field.GetValue(oceanGeometry) as Material;
                if (originalMat != null)
                {
                    defaultFoamScale = originalMat.HasProperty("_FoamScale") ? originalMat.GetFloat("_FoamScale") : 1.5f;
                    defaultContactFoam = originalMat.HasProperty("_ContactFoam") ? originalMat.GetFloat("_ContactFoam") : 1.0f;
                }
            }
        }

        // Apply initial step settings
        ApplyStepSettings(currentStep);
    }

    private void Update()
    {
        // Keyboard Shortcuts
        if (Keyboard.current != null)
        {
            if (Keyboard.current.pKey.wasPressedThisFrame)
            {
                showPresentationUI = !showPresentationUI;
            }

            if (Keyboard.current.rightArrowKey.wasPressedThisFrame)
            {
                NextStep();
            }

            if (Keyboard.current.leftArrowKey.wasPressedThisFrame)
            {
                PrevStep();
            }
        }

        // Update wireframe helper state
        if (wireframeHelper != null)
        {
            wireframeHelper.drawWireframe = enableWireframe;
        }

        // Handle procedural buoy bobbing in Step 7
        HandleBuoyPhysics();

        // Enforce texture and parameter overrides per frame to ensure LOD materials stay synced
        EnforceActiveOverrides();
    }

    /// <summary>
    /// Swaps textures and sets shader variables to match the active presentation stage.
    /// </summary>
    private void ApplyStepSettings(PresentationStep step)
    {
        if (wavesGenerator == null || oceanGeometry == null) return;

        // Remove buoy if not in step 7
        if (step != PresentationStep.DigitalTwinBuoy)
        {
            DestroyBuoy();
        }

        switch (step)
        {
            case PresentationStep.GridGeometry:
                // Base mesh: completely flat
                wavesGenerator.timeScale = 0f;
                wavesGenerator.lambdaOverride = 0f;
                SetMaterialFoam(0f);
                enableWireframe = true; // Auto-enable wireframe to see the clipmap grid structure
                showCascade0 = false;
                showCascade1 = false;
                showCascade2 = false;
                break;

            case PresentationStep.InitialSpectrum:
                // Initial spectrum: static waves, sinusoidal profile (no lambda choppiness)
                wavesGenerator.timeScale = 0f;
                wavesGenerator.lambdaOverride = 0f;
                SetMaterialFoam(0f);
                enableWireframe = false;
                showCascade0 = true;
                showCascade1 = false;
                showCascade2 = false;
                break;

            case PresentationStep.TimeEvolution:
                // Moving waves, sinusoidal profile (no lambda choppiness)
                wavesGenerator.timeScale = 1.0f;
                wavesGenerator.lambdaOverride = 0f;
                SetMaterialFoam(0f);
                enableWireframe = false;
                showCascade0 = true;
                showCascade1 = false;
                showCascade2 = false;
                break;

            case PresentationStep.ChoppyDeformation:
                // Add horizontal displacement (crests sharpen)
                wavesGenerator.timeScale = 1.0f;
                wavesGenerator.lambdaOverride = null; // Use original lambda settings
                SetMaterialFoam(0f);
                enableWireframe = false;
                showCascade0 = true;
                showCascade1 = false;
                showCascade2 = false;
                break;

            case PresentationStep.MultiCascade:
                // Blending multi-scale grids (swell + wind waves + ripples)
                wavesGenerator.timeScale = 1.0f;
                wavesGenerator.lambdaOverride = null;
                SetMaterialFoam(0f);
                enableWireframe = false;
                showCascade0 = true;
                showCascade1 = true;
                showCascade2 = true;
                break;

            case PresentationStep.JacobianFoam:
                // Enable wave compression foam
                wavesGenerator.timeScale = 1.0f;
                wavesGenerator.lambdaOverride = null;
                SetMaterialFoam(defaultFoamScale);
                enableWireframe = false;
                showCascade0 = true;
                showCascade1 = true;
                showCascade2 = true;
                break;

            case PresentationStep.DigitalTwinBuoy:
                // Full simulation + Bobbing Buoy active
                wavesGenerator.timeScale = 1.0f;
                wavesGenerator.lambdaOverride = null;
                SetMaterialFoam(defaultFoamScale);
                enableWireframe = false;
                showCascade0 = true;
                showCascade1 = true;
                showCascade2 = true;
                SpawnBuoy();
                break;
        }
    }

    /// <summary>
    /// Forces texture masks and parameter values to match active step configs every frame.
    /// </summary>
    private void EnforceActiveOverrides()
    {
        if (oceanGeometry == null || wavesGenerator == null) return;

        // Cascade 0 overrides
        if (showCascade0)
            oceanGeometry.SetCascadeTextures(0, wavesGenerator.cascade0.Displacement, wavesGenerator.cascade0.Derivatives, wavesGenerator.cascade0.Turbulence);
        else
            oceanGeometry.SetCascadeTextures(0, flatDisplacement, flatDerivatives, flatTurbulence);

        // Cascade 1 overrides
        if (showCascade1)
            oceanGeometry.SetCascadeTextures(1, wavesGenerator.cascade1.Displacement, wavesGenerator.cascade1.Derivatives, wavesGenerator.cascade1.Turbulence);
        else
            oceanGeometry.SetCascadeTextures(1, flatDisplacement, flatDerivatives, flatTurbulence);

        // Cascade 2 overrides
        if (showCascade2)
            oceanGeometry.SetCascadeTextures(2, wavesGenerator.cascade2.Displacement, wavesGenerator.cascade2.Derivatives, wavesGenerator.cascade2.Turbulence);
        else
            oceanGeometry.SetCascadeTextures(2, flatDisplacement, flatDerivatives, flatTurbulence);
    }

    private void SetMaterialFoam(float foamScale)
    {
        if (oceanGeometry == null) return;

        var field = typeof(OceanGeometry).GetField("oceanMaterial", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        if (field != null)
        {
            Material mat = field.GetValue(oceanGeometry) as Material;
            if (mat != null)
            {
                mat.SetFloat("_FoamScale", foamScale);
                mat.SetFloat("_ContactFoam", foamScale > 0.01f ? defaultContactFoam : 0f);
            }
        }

        var listField = typeof(OceanGeometry).GetField("materials", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        if (listField != null)
        {
            Material[] mats = listField.GetValue(oceanGeometry) as Material[];
            if (mats != null)
            {
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] != null)
                    {
                        mats[i].SetFloat("_FoamScale", foamScale);
                        mats[i].SetFloat("_ContactFoam", foamScale > 0.01f ? defaultContactFoam : 0f);
                    }
                }
            }
        }
    }

    #region Navigational API
    public void GoToStep(PresentationStep step)
    {
        currentStep = step;
        ApplyStepSettings(currentStep);
    }

    public void NextStep()
    {
        int next = (int)currentStep + 1;
        if (next < System.Enum.GetValues(typeof(PresentationStep)).Length)
        {
            GoToStep((PresentationStep)next);
        }
    }

    public void PrevStep()
    {
        int prev = (int)currentStep - 1;
        if (prev >= 0)
        {
            GoToStep((PresentationStep)prev);
        }
    }
    #endregion

    #region Procedural Buoy Physics
    private void SpawnBuoy()
    {
        if (proceduralBuoy != null) return;

        // Base float: bright red sphere
        proceduralBuoy = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        proceduralBuoy.name = "Presenter Buoy (Twin)";
        proceduralBuoy.transform.position = new Vector3(0, 0, 0);
        proceduralBuoy.transform.localScale = new Vector3(2.0f, 1.8f, 2.0f);

        Renderer buoyRenderer = proceduralBuoy.GetComponent<Renderer>();
        buoyRenderer.material = new Material(Shader.Find("Standard"));
        buoyRenderer.material.color = new Color(0.85f, 0.15f, 0.15f); // Premium Red
        buoyRenderer.material.SetFloat("_Glossiness", 0.6f);

        // Mast: cylinder
        GameObject mast = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(mast.GetComponent<Collider>()); // Avoid collisions
        mast.name = "Buoy Mast";
        mast.transform.SetParent(proceduralBuoy.transform);
        mast.transform.localPosition = new Vector3(0f, 1.3f, 0f);
        mast.transform.localScale = new Vector3(0.12f, 1.2f, 0.12f);
        mast.GetComponent<Renderer>().material.color = Color.grey;

        // Led: bright blinking sphere on top
        GameObject led = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(led.GetComponent<Collider>());
        led.name = "Buoy LED";
        led.transform.SetParent(proceduralBuoy.transform);
        led.transform.localPosition = new Vector3(0f, 2.5f, 0f);
        led.transform.localScale = new Vector3(0.25f, 0.25f, 0.25f);
        
        Material ledMat = new Material(Shader.Find("Standard"));
        ledMat.color = Color.yellow;
        ledMat.EnableKeyword("_EMISSION");
        ledMat.SetColor("_EmissionColor", Color.yellow * 2.0f);
        led.GetComponent<Renderer>().material = ledMat;

        // Ensure the camera focuses on the buoy initially
        if (mainCamera != null)
        {
            mainCamera.transform.LookAt(proceduralBuoy.transform.position + Vector3.up);
        }
    }

    private void DestroyBuoy()
    {
        if (proceduralBuoy != null)
        {
            Destroy(proceduralBuoy);
            proceduralBuoy = null;
        }
    }

    private void HandleBuoyPhysics()
    {
        if (proceduralBuoy == null || wavesGenerator == null) return;

        Vector3 pos = proceduralBuoy.transform.position;

        // Bobbing: vertical displacement via bilinear interpolation
        float currentHeight = wavesGenerator.GetWaterHeight(pos);
        pos.y = currentHeight;
        proceduralBuoy.transform.position = pos;

        // Tilting: construct normal vector by evaluating height at 3 triangular offsets
        float offset = 1.2f;
        Vector3 p0 = pos + new Vector3(offset, 0f, 0f);
        Vector3 p1 = pos + new Vector3(-offset * 0.5f, 0f, offset * 0.866f);
        Vector3 p2 = pos + new Vector3(-offset * 0.5f, 0f, -offset * 0.866f);

        float h0 = wavesGenerator.GetWaterHeight(p0);
        float h1 = wavesGenerator.GetWaterHeight(p1);
        float h2 = wavesGenerator.GetWaterHeight(p2);

        Vector3 v01 = new Vector3(p1.x - p0.x, h1 - h0, p1.z - p0.z);
        Vector3 v02 = new Vector3(p2.x - p0.x, h2 - h0, p2.z - p0.z);
        Vector3 normal = Vector3.Cross(v01, v02).normalized;

        if (normal.y < 0) normal = -normal;

        // Blend buoy tilt smoothly with wave normals
        proceduralBuoy.transform.up = Vector3.Slerp(proceduralBuoy.transform.up, normal, Time.deltaTime * 6.0f);

        // Blinking light logic
        Transform led = proceduralBuoy.transform.Find("Buoy LED");
        if (led != null)
        {
            Renderer ledRen = led.GetComponent<Renderer>();
            if (ledRen != null)
            {
                float intensity = Mathf.PingPong(Time.time * 3f, 1f);
                ledRen.material.SetColor("_EmissionColor", Color.yellow * (intensity > 0.4f ? 3.0f : 0.1f));
            }
        }
    }
    #endregion

    #region GUI Academic Dashboard

    private void InitStyles()
    {
        if (windowStyle != null) return;

        // Window style: dark premium glassmorphism overlay
        windowStyle = new GUIStyle(GUI.skin.box);
        Texture2D bgTex = new Texture2D(1, 1);
        bgTex.SetPixel(0, 0, new Color(0.06f, 0.08f, 0.14f, 0.94f));
        bgTex.Apply();
        windowStyle.normal.background = bgTex;
        windowStyle.border = new RectOffset(2, 2, 2, 2);

        // Header style
        headerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 15,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        headerStyle.normal.textColor = new Color(0.35f, 0.7f, 1.0f); // Vivid Blue Accent

        // Subheader style
        subHeaderStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold
        };
        subHeaderStyle.normal.textColor = Color.white;

        // Body style
        bodyStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            wordWrap = true,
            alignment = TextAnchor.UpperLeft
        };
        bodyStyle.normal.textColor = new Color(0.85f, 0.88f, 0.93f);

        // Formula/Math style
        formulaStyle = new GUIStyle(GUI.skin.box)
        {
            fontSize = 10,
            fontStyle = FontStyle.Italic,
            alignment = TextAnchor.MiddleCenter
        };
        Texture2D formulaBg = new Texture2D(1, 1);
        formulaBg.SetPixel(0, 0, new Color(0.0f, 0.0f, 0.0f, 0.35f));
        formulaBg.Apply();
        formulaStyle.normal.background = formulaBg;
        formulaStyle.normal.textColor = new Color(0.4f, 1.0f, 0.6f); // Light Mint Green

        // Button style
        buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 11,
            fontStyle = FontStyle.Bold
        };
        buttonStyle.normal.textColor = Color.white;

        // Step indicators active/inactive
        stepActiveStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            fontStyle = FontStyle.Bold
        };
        stepActiveStyle.normal.textColor = new Color(0.2f, 0.7f, 1.0f);

        stepInactiveStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 10,
            fontStyle = FontStyle.Normal
        };
        stepInactiveStyle.normal.textColor = new Color(0.5f, 0.55f, 0.65f);
    }

    private void OnGUI()
    {
        if (!showPresentationUI) return;
        InitStyles();

        // Screen parameters
        float panelWidth = 380f;
        float panelHeight = 620f;
        float x = Screen.width - panelWidth - 20f;
        float y = 20f;

        // Main Panel Background Draw
        Rect rect = new Rect(x, y, panelWidth, panelHeight);
        GUI.color = Color.white;
        GUI.Box(rect, "", windowStyle);
        DrawPanelBorder(rect, new Color(0.2f, 0.45f, 0.8f, 0.45f));

        // Content Area Layout
        GUILayout.BeginArea(new Rect(x + 15f, y + 15f, panelWidth - 30f, panelHeight - 30f));

        // Academic Header
        GUILayout.Label("🎓  TAHAPAN PEMBANGKITAN GELOMBANG FFT", headerStyle);
        GUILayout.Space(5);
        DrawHorizontalLine();
        GUILayout.Space(10);

        // Steps Progress Sidebar-style Layout
        GUILayout.Label("Alur Komputasi Spektral:", subHeaderStyle);
        GUILayout.Space(3);
        
        // Render Steps List
        DisplayStepIndicator(PresentationStep.GridGeometry, "1. Mesh Grid Geometri (Clipmap)");
        DisplayStepIndicator(PresentationStep.InitialSpectrum, "2. Pembangkitan Spektrum Awal h0(k)");
        DisplayStepIndicator(PresentationStep.TimeEvolution, "3. Relasi Dispersi & Evolusi Waktu");
        DisplayStepIndicator(PresentationStep.ChoppyDeformation, "4. Deformasi Puncak Choppy (Lambda)");
        DisplayStepIndicator(PresentationStep.MultiCascade, "5. Penggabungan Multi-Cascade Grid");
        DisplayStepIndicator(PresentationStep.JacobianFoam, "6. Deteksi Buih Puncak (Jacobian)");
        DisplayStepIndicator(PresentationStep.DigitalTwinBuoy, "7. Bobbing Buoy & Digital Twin");

        GUILayout.Space(10);
        DrawHorizontalLine();
        GUILayout.Space(10);

        // Stage Description
        GUILayout.Label($"LANGKAH {(int)currentStep + 1}: {currentStep.ToString().ToUpper()}", subHeaderStyle);
        GUILayout.Space(5);
        
        string explanation = GetStepExplanation(currentStep);
        string formula = GetStepFormula(currentStep);
        
        GUILayout.Label(explanation, bodyStyle);
        GUILayout.Space(8);
        if (!string.IsNullOrEmpty(formula))
        {
            GUILayout.Label(formula, formulaStyle);
            GUILayout.Space(8);
        }

        // Toggles & Control Overrides Area
        GUILayout.Label("Kontrol Tahapan & Opsi Visual:", subHeaderStyle);
        GUILayout.Space(3);
        
        enableWireframe = GUILayout.Toggle(enableWireframe, " Aktifkan Tampilan Wireframe (LOD)");
        
        if (currentStep >= PresentationStep.MultiCascade)
        {
            GUILayout.Space(3);
            GUILayout.BeginHorizontal();
            showCascade0 = GUILayout.Toggle(showCascade0, " Cascade 0 (Swell)");
            showCascade1 = GUILayout.Toggle(showCascade1, " Cascade 1 (Wind)");
            showCascade2 = GUILayout.Toggle(showCascade2, " Cascade 2 (Ripples)");
            GUILayout.EndHorizontal();
        }

        GUILayout.FlexibleSpace();

        // Navigation Footer Buttons
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("◀  SEBELUMNYA", buttonStyle, GUILayout.Height(35f)))
        {
            PrevStep();
        }
        GUILayout.Space(15);
        if (GUILayout.Button("SELANJUTNYA  ▶", buttonStyle, GUILayout.Height(35f)))
        {
            NextStep();
        }
        GUILayout.EndHorizontal();
        
        GUILayout.Space(5);
        GUILayout.Label("[Hotkey: ← / → untuk navigasi, P untuk sembunyikan UI]", 
            new GUIStyle(GUI.skin.label) { fontSize = 9, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.5f, 0.5f, 0.6f) } });

        GUILayout.EndArea();
    }

    private void DisplayStepIndicator(PresentationStep step, string title)
    {
        if (currentStep == step)
        {
            GUILayout.Label(" ▶ " + title + " (Aktif)", stepActiveStyle);
        }
        else
        {
            GUILayout.Label("    " + title, stepInactiveStyle);
        }
    }

    private string GetStepExplanation(PresentationStep step)
    {
        switch (step)
        {
            case PresentationStep.GridGeometry:
                return "Permukaan laut datar tanpa ada simpangan vertikal maupun deformasi horizontal. Ini menunjukkan basis mesh Geometry Clipmap bertingkat (LOD) yang secara dinamis snapping di bawah koordinat kamera untuk merender medan tak terbatas secara efisien.";
            case PresentationStep.InitialSpectrum:
                return "Amplitudo spektrum awal h0(k) dan konjugasinya h0*(-k) dibangkitkan pada ranah frekuensi menggunakan kombinasi noise Gaussian kompleks (Box-Muller) dan parameter spektral JONSWAP serta TMA dangkal. Gelombang masih beku (t=0) dan bersifat sinusoidal linear.";
            case PresentationStep.TimeEvolution:
                return "Mengaktifkan propagasi waktu spektrum dengan mengalikan amplitudo spektrum awal h0 dengan fase temporal e^(i*w*t), di mana w(k) dihitung berdasarkan relasi dispersi perairan dangkal linear. Gelombang sinusoidal linear sekarang bergerak/merambat.";
            case PresentationStep.ChoppyDeformation:
                return "Mengaktifkan pergeseran horizontal (horizontal displacement) Dx dan Dz pada ranah frekuensi via transformasi Hilbert dari tinggi gelombang. Vertices digeser secara horizontal seiring tinggi air untuk membuat puncak gelombang menjadi tajam (choppy) dan lembah menjadi landai.";
            case PresentationStep.MultiCascade:
                return "Menggabungkan tiga kaskade grid fisik yang independen (Cascade 0: 250m swell, Cascade 1: 17m wind-waves, Cascade 2: 5m ripples) untuk disintesis melalui FFT individual. Penggabungan multi-skala ini menciptakan detail gelombang yang sangat kaya dan alamiah.";
            case PresentationStep.JacobianFoam:
                return "Deteksi area turbulensi puncak ombak menggunakan determinan matriks Jacobian (J) dari medan pergeseran horizontal. Ketika J < 0, koordinat vertices saling bertubrukan (kompresi ekstrem), memicu kemunculan tekstur buih laut (foam/whitecaps) secara dinamis.";
            case PresentationStep.DigitalTwinBuoy:
                return "Simulasi sinkronisasi data buoy cuaca secara real-time. Menggunakan AsyncGPUReadback untuk mentransfer displacement map dari VRAM ke RAM, diikuti dengan interpolasi bilinear pada CPU untuk menghitung ketinggian air presisi di posisi buoy sehingga buoy mengapung dan miring secara real-time.";
            default:
                return "";
        }
    }

    private string GetStepFormula(PresentationStep step)
    {
        switch (step)
        {
            case PresentationStep.GridGeometry:
                return "LOD Level Snapping: Snapped = Floor(CamPos / Scale) * Scale";
            case PresentationStep.InitialSpectrum:
                return "JONSWAP Spectrum: S_jonswap(w) = alpha * (g^2 / w^5) * exp(-1.25 * (wp/w)^4) * gamma^r";
            case PresentationStep.TimeEvolution:
                return "Dispersion Relation: w^2 = g * |k| * tanh(k * h)\nSpectrum Evolution: h(k, t) = h0(k)e^(iwt) + h0*(-k)e^(-iwt)";
            case PresentationStep.ChoppyDeformation:
                return "Horizontal Displacement: D_x(x, t) = Sum( -i * (kx / |k|) * h(k, t) * e^(i k x) )";
            case PresentationStep.MultiCascade:
                return "Wave Blending: Displacement = Sum_c( Displacement_c(x, t) )";
            case PresentationStep.JacobianFoam:
                return "Jacobian Determinant: J = (1 + L * dDx/dx) * (1 + L * dDz/dz) - L^2 * (dDx/dz * dDz/dx)";
            case PresentationStep.DigitalTwinBuoy:
                return "Bobbing Physics: Y_buoy = BilinearInterpolate(DisplacementMap, Pos_buoy)";
            default:
                return "";
        }
    }

    private void DrawPanelBorder(Rect rect, Color color)
    {
        GUI.color = color;
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 1), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.x, rect.yMax - 1, rect.width, 1), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.x, rect.y, 1, rect.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.xMax - 1, rect.y, 1, rect.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    private void DrawHorizontalLine()
    {
        Rect lineRect = GUILayoutUtility.GetRect(10, 1);
        GUI.color = new Color(0.2f, 0.45f, 0.8f, 0.3f);
        GUI.DrawTexture(lineRect, Texture2D.whiteTexture);
        GUI.color = Color.white;
    }
    #endregion
}
