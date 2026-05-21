using UnityEngine;

/// <summary>
/// Bridges BuoyData from DatabaseManager (Firestore) to WavesSettings for real-time simulation.
/// Maps buoy observations to JONSWAP spectrum parameters.
/// 
/// Firestore schema:
///   WSPD (Wind Speed, knots)       → converted to m/s → local.windSpeed
///   WDIR (Wind Direction, compass) → converted to degrees → local.windDirection
///   WVHT (Wave Height, m)          → local.fetch (via inverse SMB) + local.scale (calibration)
///   loc_name, posisi (GeoPoint)    → location metadata
/// 
/// Fetch is derived using the inverse SMB method with Newton-Raphson iteration:
///   Given Hs_observed and U10, solve for F in:
///     Hs = 0.283 × (U²/g) × tanh(0.0125 × (gF/U²)^0.42)
/// </summary>
public class BuoyDataApplier : MonoBehaviour
{
    // ─── References ──────────────────────────────────────────────────
    [Header("References")]
    [Tooltip("WavesSettings ScriptableObject yang digunakan simulasi")]
    [SerializeField] private WavesSettings wavesSettings;

    [Tooltip("DatabaseManager untuk mengambil data buoy")]
    [SerializeField] private DatabaseManager databaseManager;

    // ─── Apply Settings ──────────────────────────────────────────────
    [Header("Apply Settings")]
    [Tooltip("Otomatis apply data buoy saat diterima dari Firebase")]
    [SerializeField] private bool autoApply = true;

    [Tooltip("Offset arah angin dalam derajat.\n" +
             "Konvensi meteorologi: WDIR = arah DARI mana angin bertiup.\n" +
             "Simulasi: arah KE mana gelombang berjalan.\n" +
             "Default 180 — membalik arah 'dari' menjadi 'ke'.")]
    [SerializeField] private float windDirectionOffset = 180f;

    // ─── Calibration ─────────────────────────────────────────────────
    [Header("Calibration")]
    [Tooltip("Multiplier tambahan untuk fine-tuning tinggi gelombang")]
    [SerializeField] private float waveHeightMultiplier = 1.0f;

    [Tooltip("Batas minimum fetch (meter)")]
    [SerializeField] private float minFetch = 1000f;

    [Tooltip("Batas maksimum fetch (meter)")]
    [SerializeField] private float maxFetch = 1000000f;

    [Tooltip("JONSWAP gamma (peak enhancement) default.\n" +
             "3.3 = standar JONSWAP, 1 = Pierson-Moskowitz")]
    [SerializeField] private float defaultGamma = 3.3f;

    [Tooltip("Jumlah iterasi Newton-Raphson untuk inverse SMB")]
    [SerializeField] private int newtonRaphsonIterations = 20;

    // ─── Current Applied Values (Read-Only) ──────────────────────────
    [Header("Current Applied Values (Read-Only)")]
    [SerializeField] private string appliedFromKey = "—";
    [SerializeField] private float applied_WindSpeed;
    [SerializeField] private float applied_WindDirection;
    [SerializeField] private float applied_Fetch;
    [SerializeField] private float applied_Scale;
    [SerializeField] private float applied_TheoreticalHs;

    // ─── Properties ──────────────────────────────────────────────────
    private float Gravity => wavesSettings != null ? wavesSettings.g : 9.81f;

    /// <summary>Data buoy yang terakhir diterapkan.</summary>
    public BuoyData LastAppliedData { get; private set; }

    // ═══════════════════════════════════════════════════════════════════
    //  LIFECYCLE
    // ═══════════════════════════════════════════════════════════════════

    private void OnEnable()
    {
        if (databaseManager != null)
        {
            databaseManager.OnSingleDataReceived += OnBuoyDataReceived;
        }
    }

    private void OnDisable()
    {
        if (databaseManager != null)
        {
            databaseManager.OnSingleDataReceived -= OnBuoyDataReceived;
        }
    }

    /// <summary>
    /// Callback saat DatabaseManager menerima data buoy.
    /// </summary>
    private void OnBuoyDataReceived(string key, BuoyData data)
    {
        if (autoApply && data != null)
        {
            appliedFromKey = key;
            ApplyBuoyData(data);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PUBLIC API
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Fetch data terbaru dari Firebase lalu terapkan ke simulasi.
    /// </summary>
    [ContextMenu("Fetch & Apply Latest Data")]
    public void FetchAndApplyLatest()
    {
        if (databaseManager == null)
        {
            Debug.LogError("[BuoyDataApplier] DatabaseManager not assigned!");
            return;
        }

        databaseManager.FetchLatestData((key, data) =>
        {
            if (data != null)
            {
                appliedFromKey = key;
                ApplyBuoyData(data);
            }
            else
            {
                Debug.LogWarning("[BuoyDataApplier] No data received from Firebase.");
            }
        });
    }

    /// <summary>
    /// Apply data buoy yang sedang ditampilkan di DatabaseManager test display.
    /// Cocok untuk testing: atur date/hour di DatabaseManager, lalu panggil ini.
    /// </summary>
    [ContextMenu("Apply Current Test Data")]
    public void ApplyCurrentTestData()
    {
        if (databaseManager == null || wavesSettings == null)
        {
            Debug.LogError("[BuoyDataApplier] DatabaseManager or WavesSettings not assigned!");
            return;
        }

        // Trigger fetch dari test display DatabaseManager, 
        // data akan diterima via OnBuoyDataReceived jika autoApply aktif
        databaseManager.FetchTestData();
    }

    /// <summary>
    /// Metode utama: memetakan data observasi buoy ke parameter spektrum JONSWAP.
    /// 
    /// Pemetaan (schema baru — tanpa APD):
    ///   WSPD (knots) → m/s → windSpeed
    ///   WDIR (compass) → degrees → windDirection (+ offset)
    ///   WVHT + WSPD → fetch (via inverse SMB Newton-Raphson)
    ///   WVHT → scale (rasio Hs_observed / Hs_theoretical)
    /// </summary>
    public void ApplyBuoyData(BuoyData data)
    {
        if (wavesSettings == null)
        {
            Debug.LogError("[BuoyDataApplier] WavesSettings not assigned!");
            return;
        }

        // Convert wind speed from knots to m/s
        float wspd = data.WindSpeedMs;
        // Convert compass direction to degrees
        float wdir = data.WindDirectionDeg;
        float wvht = data.WVHT;
        float g    = Gravity;

        // ─── 1. WSPD → windSpeed (knots → m/s) ─────────────────────
        wavesSettings.local.windSpeed = wspd;

        // ─── 2. WDIR → windDirection (compass → degrees + offset) ───
        // Konvensi meteorologi: WDIR = arah DARI mana angin datang
        // Default offset 180° membalik menjadi arah propagasi gelombang
        float direction = wdir + windDirectionOffset;
        // Normalisasi ke 0-360
        direction = ((direction % 360f) + 360f) % 360f;
        wavesSettings.local.windDirection = direction;

        // ─── 3. WVHT + WSPD → fetch (inverse SMB Newton-Raphson) ────
        // Karena APD tidak tersedia di database baru,
        // kita turunkan fetch dari Hs_observed dan U10
        // menggunakan inverse SMB dengan Newton-Raphson
        float fetch = DeriveFetchInverseSMB(wspd, wvht, g);
        fetch = Mathf.Clamp(fetch, minFetch, maxFetch);
        wavesSettings.local.fetch = fetch;

        // ─── 4. WVHT → scale (kalibrasi tinggi gelombang) ───────────
        float theoreticalHs;
        float scale = DeriveScaleFromWaveHeight(wspd, fetch, wvht, g, out theoreticalHs);
        scale *= waveHeightMultiplier;
        scale = Mathf.Clamp01(scale);
        wavesSettings.local.scale = scale;

        // ─── 5. Set JONSWAP gamma ───────────────────────────────────
        wavesSettings.local.peakEnhancement = defaultGamma;

        // ─── Update display fields ──────────────────────────────────
        applied_WindSpeed = wspd;
        applied_WindDirection = direction;
        applied_Fetch = fetch;
        applied_Scale = scale;
        applied_TheoreticalHs = theoreticalHs;
        LastAppliedData = data;

        Debug.Log($"[BuoyDataApplier] Applied buoy data ({appliedFromKey}) [{data.loc_name}]:\n" +
                  $"  WSPD={data.WSPD}kn → windSpeed={wspd:F1} m/s\n" +
                  $"  WDIR={data.WDIR} → windDirection={direction:F0}°\n" +
                  $"  WVHT={wvht:F2}m → fetch={fetch:F0}m (inverse SMB)\n" +
                  $"  scale={scale:F3} (Hs_theory={theoreticalHs:F2}m)");
    }

    /// <summary>
    /// Terapkan data buoy secara manual dari parameter individual.
    /// Berguna untuk testing tanpa Firebase.
    /// windSpeed dalam m/s, windDirection dalam degrees.
    /// </summary>
    public void ApplyManual(float windSpeedMs, float windDirectionDeg, float waveHeight)
    {
        // Convert m/s back to knots for BuoyData storage
        int wspdKnots = Mathf.RoundToInt(windSpeedMs / 0.514444f);
        
        // Find closest compass direction
        string compass = DegreesToCompass(windDirectionDeg);

        BuoyData data = new BuoyData
        {
            WSPD = wspdKnots,
            WDIR = compass,
            WVHT = waveHeight,
            TEMP = 28f,
            HUMID = 80f,
            GST = wspdKnots,
            CDIR = "N",
            CSPD = 0f
        };
        appliedFromKey = "manual";
        ApplyBuoyData(data);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  DERIVATION METHODS
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Menurunkan fetch menggunakan inverse SMB dengan Newton-Raphson iteration.
    /// 
    /// SMB formula:
    ///   Hs = 0.283 × (U²/g) × tanh(0.0125 × (gF/U²)^0.42)
    /// 
    /// Kita ingin mencari F sehingga Hs(F) = Hs_observed.
    /// 
    /// Definisikan:
    ///   x = gF/U²  (dimensionless fetch)
    ///   f(x) = 0.283 × (U²/g) × tanh(0.0125 × x^0.42) - Hs_obs = 0
    ///   f'(x) = 0.283 × (U²/g) × sech²(0.0125 × x^0.42) × 0.0125 × 0.42 × x^(-0.58)
    /// 
    /// Newton-Raphson: x_{n+1} = x_n - f(x_n) / f'(x_n)
    /// Lalu: F = x × U² / g
    /// </summary>
    private float DeriveFetchInverseSMB(float windSpeed, float observedHs, float g)
    {
        // Guard: hindari division by zero
        if (windSpeed <= 0.1f || observedHs <= 0f)
            return minFetch;

        float uSqOverG = windSpeed * windSpeed / g;
        float coefficient = 0.283f * uSqOverG;

        // Target: tanh(0.0125 × x^0.42) = Hs_obs / coefficient
        float targetTanh = observedHs / coefficient;

        // Jika target > 1, fully developed sea — gunakan fetch besar
        if (targetTanh >= 1.0f)
        {
            Debug.Log($"[BuoyDataApplier] Fully developed sea detected (target tanh={targetTanh:F2} ≥ 1). Using max fetch.");
            return maxFetch;
        }

        // Initial guess via inverse tanh: atanh(targetTanh) = 0.0125 × x^0.42
        // x = (atanh(targetTanh) / 0.0125) ^ (1/0.42)
        float atanhVal = Atanh(targetTanh);
        float xGuess = Mathf.Pow(atanhVal / 0.0125f, 1f / 0.42f);

        // Clamp initial guess to reasonable range
        xGuess = Mathf.Clamp(xGuess, 1f, 1e8f);

        // Newton-Raphson iteration
        float x = xGuess;
        for (int i = 0; i < newtonRaphsonIterations; i++)
        {
            float xPow = Mathf.Pow(x, 0.42f);
            float arg = 0.0125f * xPow;
            float tanhVal = (float)System.Math.Tanh(arg);
            float sechSq = 1f - tanhVal * tanhVal;  // sech²(x) = 1 - tanh²(x)

            // f(x) = coefficient × tanh(0.0125 × x^0.42) - Hs_obs
            float fx = coefficient * tanhVal - observedHs;

            // f'(x) = coefficient × sech²(arg) × 0.0125 × 0.42 × x^(-0.58)
            float dfx = coefficient * sechSq * 0.0125f * 0.42f * Mathf.Pow(x, -0.58f);

            if (Mathf.Abs(dfx) < 1e-12f) break;

            float xNew = x - fx / dfx;
            if (xNew <= 0f) xNew = x * 0.5f; // Safety: don't go negative

            // Convergence check
            if (Mathf.Abs(xNew - x) / Mathf.Max(Mathf.Abs(x), 1f) < 1e-6f)
            {
                x = xNew;
                break;
            }
            x = xNew;
        }

        // Convert dimensionless fetch back to real fetch
        // x = gF/U² → F = x × U²/g
        float fetch = x * windSpeed * windSpeed / g;

        return fetch;
    }

    /// <summary>
    /// Menurunkan scale factor dengan membandingkan WVHT observasi dengan Hs teoritis.
    /// Menggunakan formula empiris Sverdrup-Munk-Bretschneider (SMB).
    /// 
    /// Rumus SMB:
    ///   Hs = 0.283 × (U²/g) × tanh(0.0125 × (gF/U²)^0.42)
    /// 
    /// Lalu:
    ///   scale = WVHT_observed / Hs_theoretical
    /// </summary>
    private float DeriveScaleFromWaveHeight(float windSpeed, float fetch, 
        float observedHs, float g, out float theoreticalHs)
    {
        theoreticalHs = 0f;

        // Guard
        if (windSpeed <= 0.1f || observedHs <= 0f)
            return 0f;

        // SMB empirical formula untuk significant wave height
        float uSqOverG = windSpeed * windSpeed / g;
        float gFOverUSq = g * fetch / (windSpeed * windSpeed);
        float tanhTerm = (float)System.Math.Tanh(0.0125 * Mathf.Pow(gFOverUSq, 0.42f));
        theoreticalHs = 0.283f * uSqOverG * tanhTerm;

        if (theoreticalHs <= 0.001f)
            return 1f;

        // Scale = rasio observasi / teori
        return observedHs / theoreticalHs;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  UTILITY
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Inverse hyperbolic tangent: atanh(x) = 0.5 × ln((1+x)/(1-x))
    /// </summary>
    private static float Atanh(float x)
    {
        // Clamp to avoid log(0) or log(negative)
        x = Mathf.Clamp(x, -0.9999f, 0.9999f);
        return 0.5f * Mathf.Log((1f + x) / (1f - x));
    }

    /// <summary>
    /// Convert degrees (0-360) to nearest 16-point compass direction.
    /// </summary>
    private static string DegreesToCompass(float degrees)
    {
        degrees = ((degrees % 360f) + 360f) % 360f;
        string[] directions = { "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE",
                                "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW" };
        int index = Mathf.RoundToInt(degrees / 22.5f) % 16;
        return directions[index];
    }
}
