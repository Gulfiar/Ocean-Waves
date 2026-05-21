using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// ═══════════════════════════════════════════════════════════════════
//  DATA MODELS
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// GeoPoint data matching Firestore geoPointValue.
/// </summary>
[Serializable]
public class GeoPoint
{
    public double latitude;
    public double longitude;

    public override string ToString() => $"[{latitude}°, {longitude}°]";
}

/// <summary>
/// Data model matching Firestore document structure.
/// 
/// Fields:
///   loc_name  — Location name (e.g., "Laut Banda")
///   posisi    — GeoPoint (latitude, longitude)
///   date      — "2026-05-06"
///   time      — "07:00"
///   TEMP      — Temperature (°C)
///   HUMID     — Humidity (%)
///   WDIR      — Wind Direction (compass string)
///   WSPD      — Wind Speed (knots)
///   GST       — Gust Speed (knots)
///   WVHT      — Significant Wave Height (m)
///   CDIR      — Current Direction (compass string)
///   CSPD      — Current Speed (m/s)
///   timestamp — Firebase Timestamp
/// </summary>
[Serializable]
public class BuoyData
{
    public string loc_name;
    public GeoPoint posisi;
    public string date;
    public string time;
    public float TEMP;
    public float HUMID;
    public string WDIR;
    public int WSPD;
    public int GST;
    public float WVHT;
    public string CDIR;
    public float CSPD;
    public string timestamp;

    public float WindSpeedMs => WSPD * 0.514444f;
    public float GustSpeedMs => GST * 0.514444f;
    public float WindDirectionDeg => CompassToDegrees(WDIR);
    public float CurrentDirectionDeg => CompassToDegrees(CDIR);

    public static float CompassToDegrees(string compass)
    {
        if (string.IsNullOrEmpty(compass)) return 0f;
        switch (compass.Trim().ToUpper())
        {
            case "N": return 0f; case "NNE": return 22.5f;
            case "NE": return 45f; case "ENE": return 67.5f;
            case "E": return 90f; case "ESE": return 112.5f;
            case "SE": return 135f; case "SSE": return 157.5f;
            case "S": return 180f; case "SSW": return 202.5f;
            case "SW": return 225f; case "WSW": return 247.5f;
            case "W": return 270f; case "WNW": return 292.5f;
            case "NW": return 315f; case "NNW": return 337.5f;
            default:
                Debug.LogWarning($"[BuoyData] Unknown compass direction: '{compass}'");
                return 0f;
        }
    }

    public override string ToString()
    {
        return $"[{loc_name} | {date} {time}] WVHT={WVHT}m, WSPD={WSPD}kn({WindSpeedMs:F1}m/s), WDIR={WDIR}, TEMP={TEMP}°C";
    }
}

// ═══════════════════════════════════════════════════════════════════
//  FIRESTORE JWT AUTH HELPER
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// Minimal JWT generator for Google Service Account OAuth2.
/// Creates a signed JWT and exchanges it for an access token.
/// </summary>
public static class FirestoreAuth
{
    [Serializable] private class ServiceAccountKey
    {
        public string project_id;
        public string private_key;
        public string client_email;
    }

    [Serializable] private class TokenResponse
    {
        public string access_token;
        public int expires_in;
    }

    private static string cachedToken;
    private static float tokenExpiry;
    private static ServiceAccountKey cachedKey;

    public static string ProjectId => cachedKey?.project_id ?? "";

    public static void LoadServiceAccount(string jsonPath)
    {
        string json = System.IO.File.ReadAllText(jsonPath);
        cachedKey = JsonConvert.DeserializeObject<ServiceAccountKey>(json);
        Debug.Log($"[FirestoreAuth] Loaded service account: {cachedKey.client_email}");
    }

    public static bool HasValidToken => !string.IsNullOrEmpty(cachedToken) && Time.realtimeSinceStartup < tokenExpiry;

    public static IEnumerator GetAccessToken(Action<string> callback)
    {
        if (HasValidToken) { callback?.Invoke(cachedToken); yield break; }
        if (cachedKey == null) { Debug.LogError("[FirestoreAuth] Service account not loaded!"); yield break; }

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string header = Base64UrlEncode(JsonConvert.SerializeObject(new { alg = "RS256", typ = "JWT" }));
        string payload = Base64UrlEncode(JsonConvert.SerializeObject(new
        {
            iss = cachedKey.client_email,
            scope = "https://www.googleapis.com/auth/datastore",
            aud = "https://oauth2.googleapis.com/token",
            iat = now,
            exp = now + 3600
        }));

        string unsigned = header + "." + payload;
        string signature = Base64UrlEncode(SignRS256(unsigned, cachedKey.private_key));
        string jwt = unsigned + "." + signature;

        string body = $"grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Ajwt-bearer&assertion={jwt}";
        using (var req = new UnityWebRequest("https://oauth2.googleapis.com/token", "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/x-www-form-urlencoded");
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                var resp = JsonConvert.DeserializeObject<TokenResponse>(req.downloadHandler.text);
                cachedToken = resp.access_token;
                tokenExpiry = Time.realtimeSinceStartup + resp.expires_in - 60;
                callback?.Invoke(cachedToken);
            }
            else
            {
                Debug.LogError($"[FirestoreAuth] Token error: {req.error}\n{req.downloadHandler.text}");
                callback?.Invoke(null);
            }
        }
    }

    private static string Base64UrlEncode(string input) => Base64UrlEncode(Encoding.UTF8.GetBytes(input));
    private static string Base64UrlEncode(byte[] input) =>
        Convert.ToBase64String(input).Replace("+", "-").Replace("/", "_").TrimEnd('=');

    private static byte[] SignRS256(string data, string privateKeyPem)
    {
        string pemClean = privateKeyPem
            .Replace("-----BEGIN PRIVATE KEY-----", "")
            .Replace("-----END PRIVATE KEY-----", "")
            .Replace("\\n", "").Replace("\n", "").Replace("\r", "").Trim();
        byte[] pkcs8 = Convert.FromBase64String(pemClean);

        // Manually parse PKCS#8 DER to extract RSA parameters (Mono-compatible)
        var rsaParams = DecodePkcs8RsaPrivateKey(pkcs8);

        using (var rsa = new System.Security.Cryptography.RSACryptoServiceProvider())
        {
            rsa.ImportParameters(rsaParams);
            return rsa.SignData(Encoding.UTF8.GetBytes(data), "SHA256");
        }
    }

    /// <summary>
    /// Parse PKCS#8 DER-encoded private key and extract RSA parameters.
    /// Compatible with Unity Mono runtime (no ImportPkcs8PrivateKey needed).
    /// 
    /// PKCS#8 structure:
    ///   SEQUENCE {
    ///     INTEGER (version)
    ///     SEQUENCE { OID, NULL }    -- algorithm identifier
    ///     OCTET STRING {            -- wraps the RSA private key
    ///       SEQUENCE {
    ///         INTEGER version
    ///         INTEGER modulus
    ///         INTEGER publicExponent
    ///         INTEGER privateExponent
    ///         INTEGER prime1
    ///         INTEGER prime2
    ///         INTEGER exponent1
    ///         INTEGER exponent2
    ///         INTEGER coefficient
    ///       }
    ///     }
    ///   }
    /// </summary>
    private static System.Security.Cryptography.RSAParameters DecodePkcs8RsaPrivateKey(byte[] pkcs8)
    {
        // Wrap in a MemoryStream for sequential reading
        using (var mem = new System.IO.MemoryStream(pkcs8))
        using (var reader = new System.IO.BinaryReader(mem))
        {
            // Read outer SEQUENCE
            ReadTag(reader, 0x30);
            ReadLength(reader);

            // Read version INTEGER
            ReadTag(reader, 0x02);
            int vLen = ReadLength(reader);
            reader.ReadBytes(vLen);

            // Read algorithm identifier SEQUENCE
            ReadTag(reader, 0x30);
            int algLen = ReadLength(reader);
            reader.ReadBytes(algLen);

            // Read OCTET STRING containing RSA private key
            ReadTag(reader, 0x04);
            ReadLength(reader);

            // Now parse RSA private key SEQUENCE
            ReadTag(reader, 0x30);
            ReadLength(reader);

            // Version
            ReadTag(reader, 0x02);
            int rsaVerLen = ReadLength(reader);
            reader.ReadBytes(rsaVerLen);

            // Read RSA parameters
            byte[] modulus = ReadIntegerBytes(reader);
            byte[] publicExponent = ReadIntegerBytes(reader);
            byte[] privateExponent = ReadIntegerBytes(reader);
            byte[] prime1 = ReadIntegerBytes(reader);
            byte[] prime2 = ReadIntegerBytes(reader);
            byte[] exponent1 = ReadIntegerBytes(reader);
            byte[] exponent2 = ReadIntegerBytes(reader);
            byte[] coefficient = ReadIntegerBytes(reader);

            return new System.Security.Cryptography.RSAParameters
            {
                Modulus = modulus,
                Exponent = publicExponent,
                D = privateExponent,
                P = prime1,
                Q = prime2,
                DP = exponent1,
                DQ = exponent2,
                InverseQ = coefficient
            };
        }
    }

    private static void ReadTag(System.IO.BinaryReader reader, byte expected)
    {
        byte tag = reader.ReadByte();
        if (tag != expected)
            throw new Exception($"[FirestoreAuth] Expected ASN.1 tag 0x{expected:X2}, got 0x{tag:X2}");
    }

    private static int ReadLength(System.IO.BinaryReader reader)
    {
        byte b = reader.ReadByte();
        if (b < 0x80) return b;
        int numBytes = b & 0x7F;
        int length = 0;
        for (int i = 0; i < numBytes; i++)
            length = (length << 8) | reader.ReadByte();
        return length;
    }

    /// <summary>Read ASN.1 INTEGER, strip leading zero padding.</summary>
    private static byte[] ReadIntegerBytes(System.IO.BinaryReader reader)
    {
        ReadTag(reader, 0x02);
        int len = ReadLength(reader);
        byte[] data = reader.ReadBytes(len);

        // ASN.1 integers may have a leading 0x00 byte for sign; strip it for RSA
        if (data.Length > 1 && data[0] == 0x00)
        {
            byte[] trimmed = new byte[data.Length - 1];
            Array.Copy(data, 1, trimmed, 0, trimmed.Length);
            return trimmed;
        }
        return data;
    }
}

// ═══════════════════════════════════════════════════════════════════
//  DATABASE MANAGER — FIRESTORE REST API
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// Manages communication with Cloud Firestore via REST API.
/// Supports multiple buoy location collections.
/// </summary>
public class DatabaseManager : MonoBehaviour
{
    [Header("Firestore Settings")]
    [Tooltip("Path to serviceAccountKey.json (relative to Assets)")]
    [SerializeField] private string serviceAccountKeyPath = "Assets/Ocean Waves/fft/Database/serviceAccountKey.json";

    [Tooltip("Daftar collection (lokasi buoy) dari Firestore")]
    [SerializeField] private List<string> collectionList = new List<string>();

    [Tooltip("Index collection yang dipilih")]
    [SerializeField] private int selectedCollectionIndex = 0;

    [Header("Settings")]
    [SerializeField] private bool fetchOnStart = true;
    [SerializeField] private float autoRefreshInterval = 0f;

    [Header("Realtime Test Display")]
    [SerializeField] private string queryDate = "2026-05-06";
    [Range(0, 23)]
    [SerializeField] private int queryHour = 7;
    [SerializeField] private float testRefreshInterval = 5f;

    [Header("Status (Read-Only)")]
    [SerializeField] private string currentKey = "—";
    [SerializeField] private string status = "Idle";

    private Coroutine testRefreshCoroutine;

    // ─── Events ──────────────────────────────────────────────────
    public event Action<Dictionary<string, BuoyData>> OnAllDataReceived;
    public event Action<string, BuoyData> OnSingleDataReceived;
    public event Action<string> OnError;

    // ─── Singleton ───────────────────────────────────────────────
    public static DatabaseManager Instance { get; private set; }
    public Dictionary<string, BuoyData> CachedData { get; private set; } = new Dictionary<string, BuoyData>();
    public bool IsLoading { get; private set; }

    /// <summary>Nama collection yang sedang aktif.</summary>
    public string CurrentCollection => (collectionList != null && collectionList.Count > 0 && selectedCollectionIndex >= 0 && selectedCollectionIndex < collectionList.Count)
        ? collectionList[selectedCollectionIndex] : "";

    /// <summary>List of available collection names (buoy locations).</summary>
    public List<string> CollectionList => collectionList;
    public int SelectedCollectionIndex { get => selectedCollectionIndex; set => selectedCollectionIndex = value; }

    private string FirestoreBaseUrl => $"https://firestore.googleapis.com/v1/projects/{FirestoreAuth.ProjectId}/databases/(default)/documents";

    // ─── Lifecycle ───────────────────────────────────────────────
    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        string fullPath = System.IO.Path.Combine(Application.dataPath, "..",  serviceAccountKeyPath);
        FirestoreAuth.LoadServiceAccount(fullPath);
    }

    private void Start()
    {
        // Fetch daftar collection dulu, lalu fetch data
        StartCoroutine(InitializeCoroutine());
    }

    private IEnumerator InitializeCoroutine()
    {
        // Fetch collection list dari Firestore
        yield return FetchCollectionsCoroutine();

        if (fetchOnStart && !string.IsNullOrEmpty(CurrentCollection))
            FetchAllData();
        if (autoRefreshInterval > 0) StartCoroutine(AutoRefreshCoroutine());
        if (testRefreshInterval > 0) testRefreshCoroutine = StartCoroutine(TestRefreshCoroutine());
    }

    private void OnValidate() { queryHour = Mathf.Clamp(queryHour, 0, 23); }

    private IEnumerator AutoRefreshCoroutine()
    {
        while (true) { yield return new WaitForSeconds(autoRefreshInterval); FetchAllData(); }
    }

    private IEnumerator TestRefreshCoroutine()
    {
        while (true) { FetchTestData(); yield return new WaitForSeconds(testRefreshInterval); }
    }

    // ═══════════════════════════════════════════════════════════════
    //  PUBLIC API — COLLECTION MANAGEMENT
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Fetch daftar collection (lokasi buoy) dari Firestore.</summary>
    [ContextMenu("Fetch Collections")]
    public void FetchCollections()
    {
        StartCoroutine(FetchCollectionsCoroutine());
    }

    private IEnumerator FetchCollectionsCoroutine()
    {
        string token = null;
        yield return FirestoreAuth.GetAccessToken(t => token = t);
        if (token == null) yield break;

        // Firestore REST: GET documents root returns list of documents.
        // To list collections, use the listCollectionIds endpoint.
        string url = $"https://firestore.googleapis.com/v1/projects/{FirestoreAuth.ProjectId}/databases/(default)/documents:listCollectionIds";
        string body = JsonConvert.SerializeObject(new { pageSize = 100 });

        using (var req = new UnityWebRequest(url, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Authorization", $"Bearer {token}");
            req.SetRequestHeader("Content-Type", "application/json");
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                var root = JObject.Parse(req.downloadHandler.text);
                var ids = root["collectionIds"] as JArray;
                if (ids != null)
                {
                    collectionList.Clear();
                    foreach (var id in ids)
                        collectionList.Add(id.ToString());

                    // Keep selectedCollectionIndex valid
                    if (selectedCollectionIndex >= collectionList.Count)
                        selectedCollectionIndex = 0;

                    Debug.Log($"[DatabaseManager] Found {collectionList.Count} collections: {string.Join(", ", collectionList)}");
                }
            }
            else
            {
                Debug.LogError($"[DatabaseManager] FetchCollections failed: {req.error}\n{req.downloadHandler?.text}");
            }
        }
    }

    /// <summary>Ganti collection berdasarkan index.</summary>
    public void SetCollectionByIndex(int index)
    {
        if (index >= 0 && index < collectionList.Count)
        {
            selectedCollectionIndex = index;
            CachedData.Clear();
            Debug.Log($"[DatabaseManager] Switched to collection: {CurrentCollection}");
        }
    }

    /// <summary>Ganti collection berdasarkan nama.</summary>
    public void SetCollection(string collectionName)
    {
        int idx = collectionList.IndexOf(collectionName);
        if (idx >= 0)
        {
            SetCollectionByIndex(idx);
        }
        else
        {
            // Tambahkan jika belum ada
            collectionList.Add(collectionName);
            selectedCollectionIndex = collectionList.Count - 1;
            CachedData.Clear();
            Debug.Log($"[DatabaseManager] Added & switched to collection: {collectionName}");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  PUBLIC API — FETCH
    // ═══════════════════════════════════════════════════════════════

    public void FetchAllData(Action<Dictionary<string, BuoyData>> callback = null)
    {
        StartCoroutine(FetchAllDataCoroutine(callback));
    }

    private IEnumerator FetchAllDataCoroutine(Action<Dictionary<string, BuoyData>> callback)
    {
        IsLoading = true;
        string token = null;
        yield return FirestoreAuth.GetAccessToken(t => token = t);
        if (token == null) { IsLoading = false; yield break; }

        string url = $"{FirestoreBaseUrl}/{Uri.EscapeDataString(CurrentCollection)}";
        using (var req = UnityWebRequest.Get(url))
        {
            req.SetRequestHeader("Authorization", $"Bearer {token}");
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                var data = ParseFirestoreList(req.downloadHandler.text);
                CachedData = data;
                Debug.Log($"[DatabaseManager] Fetched {data.Count} docs from '{CurrentCollection}'");
                callback?.Invoke(data);
                OnAllDataReceived?.Invoke(data);
            }
            else
            {
                HandleError($"FetchAll failed: {req.error}", req.downloadHandler?.text);
            }
        }
        IsLoading = false;
    }

    public void FetchDataByKey(string key, Action<BuoyData> callback = null)
    {
        StartCoroutine(FetchByKeyCoroutine(key, callback));
    }

    private IEnumerator FetchByKeyCoroutine(string key, Action<BuoyData> callback)
    {
        IsLoading = true;
        string token = null;
        yield return FirestoreAuth.GetAccessToken(t => token = t);
        if (token == null) { IsLoading = false; yield break; }

        string url = $"{FirestoreBaseUrl}/{Uri.EscapeDataString(CurrentCollection)}/{key}";
        using (var req = UnityWebRequest.Get(url))
        {
            req.SetRequestHeader("Authorization", $"Bearer {token}");
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                var data = ParseFirestoreDocument(req.downloadHandler.text);
                if (data != null) CachedData[key] = data;
                Debug.Log($"[DatabaseManager] Fetched: {key} → {data}");
                callback?.Invoke(data);
                OnSingleDataReceived?.Invoke(key, data);
            }
            else
            {
                Debug.LogWarning($"[DatabaseManager] No data for key: {key}");
                callback?.Invoke(null);
            }
        }
        IsLoading = false;
    }

    public void FetchLatestData(Action<string, BuoyData> callback = null)
    {
        StartCoroutine(FetchLatestCoroutine(callback));
    }

    private IEnumerator FetchLatestCoroutine(Action<string, BuoyData> callback)
    {
        IsLoading = true;
        string token = null;
        yield return FirestoreAuth.GetAccessToken(t => token = t);
        if (token == null) { IsLoading = false; yield break; }

        // Firestore structured query: order by timestamp desc, limit 1
        string url = $"{FirestoreBaseUrl}:runQuery";
        var query = new
        {
            structuredQuery = new
            {
                from = new[] { new { collectionId = CurrentCollection } },
                orderBy = new[] { new { field = new { fieldPath = "timestamp" }, direction = "DESCENDING" } },
                limit = 1
            }
        };
        string body = JsonConvert.SerializeObject(query);

        using (var req = new UnityWebRequest(url, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Authorization", $"Bearer {token}");
            req.SetRequestHeader("Content-Type", "application/json");
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                var results = JArray.Parse(req.downloadHandler.text);
                if (results.Count > 0 && results[0]["document"] != null)
                {
                    var doc = results[0]["document"];
                    string docName = doc["name"]?.ToString() ?? "";
                    string docId = docName.Substring(docName.LastIndexOf('/') + 1);
                    var data = ParseDocumentFields(doc);
                    if (data != null) CachedData[docId] = data;
                    Debug.Log($"[DatabaseManager] Latest: {docId} → {data}");
                    callback?.Invoke(docId, data);
                    OnSingleDataReceived?.Invoke(docId, data);
                }
                else
                {
                    callback?.Invoke(null, null);
                }
            }
            else
            {
                HandleError($"FetchLatest failed: {req.error}", req.downloadHandler?.text);
                callback?.Invoke(null, null);
            }
        }
        IsLoading = false;
    }

    // ═══════════════════════════════════════════════════════════════
    //  HELPER — Key Generation
    // ═══════════════════════════════════════════════════════════════

    public static string GenerateKey(DateTime dateTime) => dateTime.ToString("yyyyMMddHH");
    public static string GenerateKey(string date, string time) => date.Replace("-", "") + time.Substring(0, 2);
    public static (string date, string time) ParseKey(string key)
    {
        string y = key.Substring(0, 4), m = key.Substring(4, 2), d = key.Substring(6, 2), h = key.Substring(8, 2);
        return ($"{y}-{m}-{d}", $"{h}:00");
    }

    // ═══════════════════════════════════════════════════════════════
    //  TEST DATA FETCH
    // ═══════════════════════════════════════════════════════════════

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
                status = $"OK — {data.loc_name} | {data.date} {data.time}";
            }
            else
            {
                status = $"Not Found — key: {key}";
            }
        });
    }

    [ContextMenu("Fetch Next Hour")]
    public void FetchNextHour()
    {
        queryHour++;
        if (queryHour > 23) { queryHour = 0; if (DateTime.TryParse(queryDate, out DateTime dt)) queryDate = dt.AddDays(1).ToString("yyyy-MM-dd"); }
        FetchTestData();
    }

    [ContextMenu("Fetch Previous Hour")]
    public void FetchPreviousHour()
    {
        queryHour--;
        if (queryHour < 0) { queryHour = 23; if (DateTime.TryParse(queryDate, out DateTime dt)) queryDate = dt.AddDays(-1).ToString("yyyy-MM-dd"); }
        FetchTestData();
    }

    // ═══════════════════════════════════════════════════════════════
    //  FIRESTORE DOCUMENT PARSING
    // ═══════════════════════════════════════════════════════════════

    private Dictionary<string, BuoyData> ParseFirestoreList(string json)
    {
        var result = new Dictionary<string, BuoyData>();
        var root = JObject.Parse(json);
        var documents = root["documents"] as JArray;
        if (documents == null) return result;

        foreach (var doc in documents)
        {
            string name = doc["name"]?.ToString() ?? "";
            string docId = name.Substring(name.LastIndexOf('/') + 1);
            var data = ParseDocumentFields(doc);
            if (data != null) result[docId] = data;
        }
        return result;
    }

    private BuoyData ParseFirestoreDocument(string json)
    {
        var doc = JObject.Parse(json);
        return ParseDocumentFields(doc);
    }

    private BuoyData ParseDocumentFields(JToken doc)
    {
        var fields = doc["fields"];
        if (fields == null) return null;

        var data = new BuoyData
        {
            loc_name = GetStringValue(fields, "loc_name"),
            date = GetStringValue(fields, "date"),
            time = GetStringValue(fields, "time"),
            WDIR = GetStringValue(fields, "WDIR"),
            CDIR = GetStringValue(fields, "CDIR"),
            WVHT = (float)GetDoubleValue(fields, "WVHT"),
            CSPD = (float)GetDoubleValue(fields, "CSPD"),
            TEMP = (float)GetDoubleValue(fields, "TEMP"),
            HUMID = (float)GetDoubleValue(fields, "HUMID"),
            WSPD = (int)GetDoubleValue(fields, "WSPD"),
            GST = (int)GetDoubleValue(fields, "GST"),
        };

        // Parse GeoPoint
        var posisiField = fields["posisi"];
        if (posisiField != null && posisiField["geoPointValue"] != null)
        {
            var geo = posisiField["geoPointValue"];
            data.posisi = new GeoPoint
            {
                latitude = geo["latitude"]?.Value<double>() ?? 0,
                longitude = geo["longitude"]?.Value<double>() ?? 0
            };
        }

        // Parse timestamp
        var tsField = fields["timestamp"];
        if (tsField != null && tsField["timestampValue"] != null)
            data.timestamp = tsField["timestampValue"].ToString();

        return data;
    }

    private string GetStringValue(JToken fields, string key)
    {
        var f = fields[key];
        if (f == null) return "";
        return f["stringValue"]?.ToString() ?? "";
    }

    private double GetDoubleValue(JToken fields, string key)
    {
        var f = fields[key];
        if (f == null) return 0;
        if (f["doubleValue"] != null) return f["doubleValue"].Value<double>();
        if (f["integerValue"] != null) return f["integerValue"].Value<double>();
        return 0;
    }

    private void HandleError(string msg, string responseBody)
    {
        Debug.LogError($"[DatabaseManager] {msg}\n{responseBody}");
        OnError?.Invoke(msg);
    }
}
