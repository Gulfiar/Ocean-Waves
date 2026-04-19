using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;

/// <summary>
/// Data model matching Firebase Realtime Database structure.
/// Key format: "2026041800" (yyyyMMddHH)
/// </summary>
[Serializable]
public class BuoyData
{
    public string date;    // "2026-04-18"
    public string time;    // "00:00"
    public int WDIR;       // Wind Direction (degrees)
    public float WSPD;     // Wind Speed (m/s)
    public float GST;      // Gust Speed (m/s)
    public float WVHT;     // Significant Wave Height (m)
    public int DPD;        // Dominant Wave Period (s)
    public float APD;      // Average Wave Period (s)
    public int MWD;        // Mean Wave Direction (degrees)
    public float PRES;     // Atmospheric Pressure (hPa)
    public float ATMP;     // Air Temperature (°C)
    public float WTMP;     // Water Temperature (°C)
    public float DEWP;     // Dewpoint Temperature (°C)

    public override string ToString()
    {
        return $"[{date} {time}] WVHT={WVHT}m, WSPD={WSPD}m/s, MWD={MWD}°, WDIR={WDIR}°";
    }
}

/// <summary>
/// Manages communication with Firebase Realtime Database via REST API.
/// Provides methods to fetch, send, update, and delete buoy data.
/// </summary>
public class DatabaseManager : MonoBehaviour
{
    // ─── Configuration ───────────────────────────────────────────────
    private const string DATABASE_URL =
        "https://ocean-waves-ef108-default-rtdb.asia-southeast1.firebasedatabase.app/";

    [Header("Settings")]
    [Tooltip("Automatically fetch all data on Start")]
    [SerializeField] private bool fetchOnStart = true;

    [Tooltip("Auto-refresh interval in seconds (0 = disabled)")]
    [SerializeField] private float autoRefreshInterval = 0f;

    // ─── Realtime Test Display ────────────────────────────────────────
    [Header("Realtime Test Display")]
    [Tooltip("Enable on-screen debug overlay")]
    [SerializeField] private bool showDebugOverlay = true;

    [Tooltip("Date to query (yyyy-MM-dd)")]
    [SerializeField] private string queryDate = "2026-04-18";

    [Tooltip("Hour to query (0-23)")]
    [Range(0, 23)]
    [SerializeField] private int queryHour = 0;

    [Tooltip("Auto-refresh test display interval in seconds (0 = manual only)")]
    [SerializeField] private float testRefreshInterval = 5f;

    [Header("Current Data (Read-Only)")]
    [SerializeField] private string currentKey = "—";
    [SerializeField] private string status = "Idle";
    [Space(5)]
    [SerializeField] private float display_WVHT;
    [SerializeField] private float display_WSPD;
    [SerializeField] private int display_WDIR;
    [SerializeField] private int display_MWD;
    [SerializeField] private float display_APD;
    [SerializeField] private int display_DPD;
    [SerializeField] private float display_GST;
    [SerializeField] private float display_PRES;
    [SerializeField] private float display_ATMP;
    [SerializeField] private float display_WTMP;
    [SerializeField] private float display_DEWP;

    // Internal state for test display
    private BuoyData currentDisplayData;
    private string lastQueryKey = "";
    private Coroutine testRefreshCoroutine;

    // ─── Events ──────────────────────────────────────────────────────
    /// <summary>Fired when all data is received from Firebase.</summary>
    public event Action<Dictionary<string, BuoyData>> OnAllDataReceived;

    /// <summary>Fired when a single entry is received.</summary>
    public event Action<string, BuoyData> OnSingleDataReceived;

    /// <summary>Fired when data is successfully sent to Firebase.</summary>
    public event Action<string, BuoyData> OnDataSent;

    /// <summary>Fired when data is successfully deleted.</summary>
    public event Action<string> OnDataDeleted;

    /// <summary>Fired on any Firebase operation error.</summary>
    public event Action<string> OnError;

    // ─── Singleton ───────────────────────────────────────────────────
    public static DatabaseManager Instance { get; private set; }

    // ─── Cached Data ─────────────────────────────────────────────────
    /// <summary>Locally cached copy of all Firebase data.</summary>
    public Dictionary<string, BuoyData> CachedData { get; private set; }
        = new Dictionary<string, BuoyData>();

    /// <summary>True while any Firebase operation is in progress.</summary>
    public bool IsLoading { get; private set; }

    // ─── Lifecycle ───────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    private void Start()
    {
        if (fetchOnStart)
        {
            FetchAllData();
        }

        if (autoRefreshInterval > 0)
        {
            StartCoroutine(AutoRefreshCoroutine());
        }

        // Start test display auto-refresh
        if (testRefreshInterval > 0)
        {
            testRefreshCoroutine = StartCoroutine(TestRefreshCoroutine());
        }
    }

    private void OnValidate()
    {
        // Clamp hour
        queryHour = Mathf.Clamp(queryHour, 0, 23);
    }

    private IEnumerator AutoRefreshCoroutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(autoRefreshInterval);
            FetchAllData();
        }
    }

    private IEnumerator TestRefreshCoroutine()
    {
        while (true)
        {
            FetchTestData();
            yield return new WaitForSeconds(testRefreshInterval);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PUBLIC API — FETCH (GET)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Fetch all buoy data from Firebase.
    /// </summary>
    public void FetchAllData(Action<Dictionary<string, BuoyData>> callback = null)
    {
        StartCoroutine(GetRequestCoroutine(".json", json =>
        {
            if (string.IsNullOrEmpty(json) || json == "null")
            {
                Debug.LogWarning("[DatabaseManager] No data found in Firebase.");
                var empty = new Dictionary<string, BuoyData>();
                callback?.Invoke(empty);
                OnAllDataReceived?.Invoke(empty);
                return;
            }

            var data = JsonConvert.DeserializeObject<Dictionary<string, BuoyData>>(json);
            CachedData = data ?? new Dictionary<string, BuoyData>();

            Debug.Log($"[DatabaseManager] Fetched {CachedData.Count} entries from Firebase.");
            callback?.Invoke(CachedData);
            OnAllDataReceived?.Invoke(CachedData);
        }));
    }

    /// <summary>
    /// Fetch a single entry by key (e.g., "2026041800").
    /// </summary>
    public void FetchDataByKey(string key, Action<BuoyData> callback = null)
    {
        StartCoroutine(GetRequestCoroutine($"{key}.json", json =>
        {
            if (string.IsNullOrEmpty(json) || json == "null")
            {
                Debug.LogWarning($"[DatabaseManager] No data found for key: {key}");
                callback?.Invoke(null);
                return;
            }

            var data = JsonConvert.DeserializeObject<BuoyData>(json);
            if (data != null)
            {
                CachedData[key] = data;
            }

            Debug.Log($"[DatabaseManager] Fetched entry: {key} → {data}");
            callback?.Invoke(data);
            OnSingleDataReceived?.Invoke(key, data);
        }));
    }

    /// <summary>
    /// Fetch the latest entry (highest key = most recent datetime).
    /// </summary>
    public void FetchLatestData(Action<string, BuoyData> callback = null)
    {
        string query = ".json?orderBy=\"$key\"&limitToLast=1";
        StartCoroutine(GetRequestCoroutine(query, json =>
        {
            if (string.IsNullOrEmpty(json) || json == "null")
            {
                Debug.LogWarning("[DatabaseManager] No data found.");
                callback?.Invoke(null, null);
                return;
            }

            var data = JsonConvert.DeserializeObject<Dictionary<string, BuoyData>>(json);
            if (data != null)
            {
                foreach (var kvp in data)
                {
                    Debug.Log($"[DatabaseManager] Latest entry: {kvp.Key} → {kvp.Value}");
                    callback?.Invoke(kvp.Key, kvp.Value);
                    OnSingleDataReceived?.Invoke(kvp.Key, kvp.Value);
                    break;
                }
            }
        }));
    }

    /// <summary>
    /// Fetch entries within a date range using key-based ordering.
    /// Keys are formatted as "yyyyMMddHH", so lexicographic ordering works.
    /// </summary>
    /// <param name="startKey">Start key inclusive, e.g., "2026041800"</param>
    /// <param name="endKey">End key inclusive, e.g., "2026041823"</param>
    public void FetchDataByRange(string startKey, string endKey,
        Action<Dictionary<string, BuoyData>> callback = null)
    {
        string query = $".json?orderBy=\"$key\"&startAt=\"{startKey}\"&endAt=\"{endKey}\"";
        StartCoroutine(GetRequestCoroutine(query, json =>
        {
            if (string.IsNullOrEmpty(json) || json == "null")
            {
                Debug.LogWarning($"[DatabaseManager] No data in range {startKey}–{endKey}.");
                callback?.Invoke(new Dictionary<string, BuoyData>());
                return;
            }

            var data = JsonConvert.DeserializeObject<Dictionary<string, BuoyData>>(json);
            Debug.Log($"[DatabaseManager] Fetched {data?.Count ?? 0} entries in range.");
            callback?.Invoke(data ?? new Dictionary<string, BuoyData>());
            OnAllDataReceived?.Invoke(data);
        }));
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PUBLIC API — SEND (PUT / PATCH)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Send (overwrite) a single entry to Firebase.
    /// </summary>
    /// <param name="key">Entry key, e.g., "2026041800"</param>
    /// <param name="data">The buoy data to store</param>
    public void SendData(string key, BuoyData data, Action<bool> callback = null)
    {
        string json = JsonConvert.SerializeObject(data, Formatting.None);
        StartCoroutine(PutRequestCoroutine($"{key}.json", json, success =>
        {
            if (success)
            {
                CachedData[key] = data;
                Debug.Log($"[DatabaseManager] Sent data: {key} → {data}");
                OnDataSent?.Invoke(key, data);
            }
            callback?.Invoke(success);
        }));
    }

    /// <summary>
    /// Send multiple entries at once (merges with existing data).
    /// </summary>
    public void SendMultipleData(Dictionary<string, BuoyData> entries,
        Action<bool> callback = null)
    {
        string json = JsonConvert.SerializeObject(entries, Formatting.None);
        StartCoroutine(PatchRequestCoroutine(".json", json, success =>
        {
            if (success)
            {
                foreach (var kvp in entries)
                {
                    CachedData[kvp.Key] = kvp.Value;
                }
                Debug.Log($"[DatabaseManager] Sent {entries.Count} entries to Firebase.");
            }
            callback?.Invoke(success);
        }));
    }

    /// <summary>
    /// Update specific fields of an existing entry.
    /// </summary>
    public void UpdateData(string key, Dictionary<string, object> fieldsToUpdate,
        Action<bool> callback = null)
    {
        string json = JsonConvert.SerializeObject(fieldsToUpdate, Formatting.None);
        StartCoroutine(PatchRequestCoroutine($"{key}.json", json, success =>
        {
            if (success)
            {
                Debug.Log($"[DatabaseManager] Updated fields for key: {key}");
            }
            callback?.Invoke(success);
        }));
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PUBLIC API — DELETE
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Delete a single entry from Firebase.
    /// </summary>
    public void DeleteData(string key, Action<bool> callback = null)
    {
        StartCoroutine(DeleteRequestCoroutine($"{key}.json", success =>
        {
            if (success)
            {
                CachedData.Remove(key);
                Debug.Log($"[DatabaseManager] Deleted entry: {key}");
                OnDataDeleted?.Invoke(key);
            }
            callback?.Invoke(success);
        }));
    }

    /// <summary>
    /// Delete ALL data from Firebase. Use with caution!
    /// </summary>
    public void DeleteAllData(Action<bool> callback = null)
    {
        StartCoroutine(DeleteRequestCoroutine(".json", success =>
        {
            if (success)
            {
                CachedData.Clear();
                Debug.Log("[DatabaseManager] Deleted all data from Firebase.");
            }
            callback?.Invoke(success);
        }));
    }

    // ═══════════════════════════════════════════════════════════════════
    //  HELPER — Key Generation
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generate a Firebase key from a DateTime. Format: "yyyyMMddHH"
    /// </summary>
    public static string GenerateKey(DateTime dateTime)
    {
        return dateTime.ToString("yyyyMMddHH");
    }

    /// <summary>
    /// Generate a Firebase key from date and time strings.
    /// e.g., ("2026-04-18", "01:00") → "2026041801"
    /// </summary>
    public static string GenerateKey(string date, string time)
    {
        return date.Replace("-", "") + time.Substring(0, 2);
    }

    /// <summary>
    /// Parse a Firebase key back to date and time strings.
    /// e.g., "2026041801" → ("2026-04-18", "01:00")
    /// </summary>
    public static (string date, string time) ParseKey(string key)
    {
        // key: "2026041801"
        string year = key.Substring(0, 4);
        string month = key.Substring(4, 2);
        string day = key.Substring(6, 2);
        string hour = key.Substring(8, 2);
        return ($"{year}-{month}-{day}", $"{hour}:00");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  REALTIME TEST DISPLAY
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Fetch data for the date/hour set in the Inspector.
    /// Can be triggered via Inspector context menu: right-click → Fetch Test Data.
    /// </summary>
    [ContextMenu("Fetch Test Data")]
    public void FetchTestData()
    {
        string key = queryDate.Replace("-", "") + queryHour.ToString("D2");
        currentKey = key;
        status = "Fetching...";

        FetchDataByKey(key, data =>
        {
            if (data != null)
            {
                currentDisplayData = data;
                UpdateDisplayFields(data);
                status = $"OK — {data.date} {data.time}";
                Debug.Log($"[DatabaseManager][Test] {key} → {data}");
            }
            else
            {
                currentDisplayData = null;
                ClearDisplayFields();
                status = $"Not Found — key: {key}";
                Debug.LogWarning($"[DatabaseManager][Test] No data for key: {key}");
            }
        });
    }

    /// <summary>
    /// Fetch data for the next hour (increments queryHour or rolls to next day).
    /// </summary>
    [ContextMenu("Fetch Next Hour")]
    public void FetchNextHour()
    {
        queryHour++;
        if (queryHour > 23)
        {
            queryHour = 0;
            // Increment date
            if (DateTime.TryParse(queryDate, out DateTime dt))
            {
                queryDate = dt.AddDays(1).ToString("yyyy-MM-dd");
            }
        }
        FetchTestData();
    }

    /// <summary>
    /// Fetch data for the previous hour (decrements queryHour or rolls to previous day).
    /// </summary>
    [ContextMenu("Fetch Previous Hour")]
    public void FetchPreviousHour()
    {
        queryHour--;
        if (queryHour < 0)
        {
            queryHour = 23;
            if (DateTime.TryParse(queryDate, out DateTime dt))
            {
                queryDate = dt.AddDays(-1).ToString("yyyy-MM-dd");
            }
        }
        FetchTestData();
    }

    private void UpdateDisplayFields(BuoyData data)
    {
        display_WVHT = data.WVHT;
        display_WSPD = data.WSPD;
        display_WDIR = data.WDIR;
        display_MWD = data.MWD;
        display_APD = data.APD;
        display_DPD = data.DPD;
        display_GST = data.GST;
        display_PRES = data.PRES;
        display_ATMP = data.ATMP;
        display_WTMP = data.WTMP;
        display_DEWP = data.DEWP;
    }

    private void ClearDisplayFields()
    {
        display_WVHT = 0;
        display_WSPD = 0;
        display_WDIR = 0;
        display_MWD = 0;
        display_APD = 0;
        display_DPD = 0;
        display_GST = 0;
        display_PRES = 0;
        display_ATMP = 0;
        display_WTMP = 0;
        display_DEWP = 0;
    }

    // ─── On-Screen Debug Overlay ──────────────────────────────────────
    private void OnGUI()
    {
        if (!showDebugOverlay) return;

        float panelWidth = 340f;
        float panelHeight = 320f;
        float x = Screen.width - panelWidth - 15f;
        float y = 15f;

        // Semi-transparent background
        GUI.color = new Color(0, 0, 0, 0.75f);
        GUI.DrawTexture(new Rect(x, y, panelWidth, panelHeight), Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUIStyle headerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.3f, 0.85f, 1f) }
        };

        GUIStyle labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            normal = { textColor = Color.white }
        };

        GUIStyle valueStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.95f, 0.4f) }
        };

        GUIStyle statusStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            normal = { textColor = IsLoading ? Color.yellow : new Color(0.4f, 1f, 0.4f) }
        };

        float lineH = 20f;
        float cx = x + 12f;
        float cy = y + 8f;

        GUI.Label(new Rect(cx, cy, panelWidth, lineH), "Drifting Buoy Data", headerStyle);
        cy += lineH + 2f;

        GUI.Label(new Rect(cx, cy, panelWidth, lineH), $"Key: {currentKey}   |   {status}", statusStyle);
        cy += lineH + 6f;

        if (currentDisplayData != null)
        {
            DrawDataRow(ref cy, cx, lineH, labelStyle, valueStyle, "Wave Height (WVHT)", $"{display_WVHT:F2} m");
            DrawDataRow(ref cy, cx, lineH, labelStyle, valueStyle, "Wind Speed  (WSPD)", $"{display_WSPD:F1} m/s");
            DrawDataRow(ref cy, cx, lineH, labelStyle, valueStyle, "Wind Dir    (WDIR)", $"{display_WDIR}°");
            DrawDataRow(ref cy, cx, lineH, labelStyle, valueStyle, "Mean Wave Dir (MWD)", $"{display_MWD}°");
            DrawDataRow(ref cy, cx, lineH, labelStyle, valueStyle, "Avg Period  (APD)", $"{display_APD:F1} s");
            DrawDataRow(ref cy, cx, lineH, labelStyle, valueStyle, "Dom Period  (DPD)", $"{display_DPD} s");
            DrawDataRow(ref cy, cx, lineH, labelStyle, valueStyle, "Gust Speed  (GST)", $"{display_GST:F1} m/s");
            DrawDataRow(ref cy, cx, lineH, labelStyle, valueStyle, "Pressure   (PRES)", $"{display_PRES:F1} hPa");
            DrawDataRow(ref cy, cx, lineH, labelStyle, valueStyle, "Air Temp   (ATMP)", $"{display_ATMP:F1} °C");
            DrawDataRow(ref cy, cx, lineH, labelStyle, valueStyle, "Water Temp (WTMP)", $"{display_WTMP:F1} °C");
            DrawDataRow(ref cy, cx, lineH, labelStyle, valueStyle, "Dewpoint   (DEWP)", $"{display_DEWP:F1} °C");
        }
        else
        {
            GUI.Label(new Rect(cx, cy, panelWidth, lineH), IsLoading ? "Loading..." : "No data. Set date/hour in Inspector.", labelStyle);
        }
    }

    private void DrawDataRow(ref float cy, float cx, float lineH,
        GUIStyle labelStyle, GUIStyle valueStyle, string label, string value)
    {
        GUI.Label(new Rect(cx, cy, 180f, lineH), label, labelStyle);
        GUI.Label(new Rect(cx + 185f, cy, 140f, lineH), value, valueStyle);
        cy += lineH;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  INTERNAL — HTTP Request Coroutines
    // ═══════════════════════════════════════════════════════════════════

    private IEnumerator GetRequestCoroutine(string endpoint, Action<string> onSuccess)
    {
        IsLoading = true;
        string url = DATABASE_URL + endpoint;

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                onSuccess?.Invoke(request.downloadHandler.text);
            }
            else
            {
                string error = $"GET {endpoint} failed: {request.error}";
                Debug.LogError($"[DatabaseManager] {error}");
                OnError?.Invoke(error);
            }
        }

        IsLoading = false;
    }

    private IEnumerator PutRequestCoroutine(string endpoint, string jsonData,
        Action<bool> onComplete)
    {
        IsLoading = true;
        string url = DATABASE_URL + endpoint;
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);

        using (UnityWebRequest request = new UnityWebRequest(url, "PUT"))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            bool success = request.result == UnityWebRequest.Result.Success;
            if (!success)
            {
                string error = $"PUT {endpoint} failed: {request.error}";
                Debug.LogError($"[DatabaseManager] {error}");
                OnError?.Invoke(error);
            }
            onComplete?.Invoke(success);
        }

        IsLoading = false;
    }

    private IEnumerator PatchRequestCoroutine(string endpoint, string jsonData,
        Action<bool> onComplete)
    {
        IsLoading = true;
        string url = DATABASE_URL + endpoint;
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);

        using (UnityWebRequest request = new UnityWebRequest(url, "PATCH"))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            bool success = request.result == UnityWebRequest.Result.Success;
            if (!success)
            {
                string error = $"PATCH {endpoint} failed: {request.error}";
                Debug.LogError($"[DatabaseManager] {error}");
                OnError?.Invoke(error);
            }
            onComplete?.Invoke(success);
        }

        IsLoading = false;
    }

    private IEnumerator DeleteRequestCoroutine(string endpoint, Action<bool> onComplete)
    {
        IsLoading = true;
        string url = DATABASE_URL + endpoint;

        using (UnityWebRequest request = UnityWebRequest.Delete(url))
        {
            yield return request.SendWebRequest();

            bool success = request.result == UnityWebRequest.Result.Success;
            if (!success)
            {
                string error = $"DELETE {endpoint} failed: {request.error}";
                Debug.LogError($"[DatabaseManager] {error}");
                OnError?.Invoke(error);
            }
            onComplete?.Invoke(success);
        }

        IsLoading = false;
    }
}
