using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Script untuk setiap pin penanda lokasi di peta.
/// Tempelkan script ini di UI Button (Pin) di dalam Canvas Peta.
/// </summary>
[RequireComponent(typeof(Button))]
public class BuoyMapPin : MonoBehaviour
{
    [Header("Data Koleksi Firestore")]
    [Tooltip("Nama koleksi Firestore untuk lokasi ini (misal: 'Laut Banda')")]
    [SerializeField] private string collectionName;

    [Header("References")]
    [Tooltip("Pilih objek BuoyMapUI yang mengatur peta")]
    [SerializeField] private BuoyMapUI mapUI;

    private Button pinButton;

    private void Awake()
    {
        pinButton = GetComponent<Button>();
        pinButton.onClick.AddListener(OnPinClicked);

        if (mapUI == null)
            mapUI = FindObjectOfType<BuoyMapUI>();
    }

    private void OnPinClicked()
    {
        if (string.IsNullOrEmpty(collectionName))
        {
            Debug.LogWarning("[BuoyMapPin] Nama collection kosong! Isi di Inspector.");
            return;
        }

        Debug.Log($"[BuoyMapPin] Mengklik pin untuk lokasi: {collectionName}");

        // 1. Ganti collection di DatabaseManager
        DatabaseManager.Instance.SetCollection(collectionName);

        // 2. Fetch data (semua / yang terbaru) dari lokasi tersebut
        DatabaseManager.Instance.FetchAllData();

        // 3. Sembunyikan peta dan mulai simulasi
        if (mapUI != null)
        {
            mapUI.HideMapAndStartSimulation();
        }
    }
}
