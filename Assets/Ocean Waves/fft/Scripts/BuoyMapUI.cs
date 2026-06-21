using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Script utama untuk mengatur Peta Interaktif UI.
/// Tempelkan script ini di Canvas Peta (MapBackground).
/// </summary>
public class BuoyMapUI : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("Panel atau Gambar Peta Utama (yang berisi pin-pin)")]
    [SerializeField] private GameObject mapPanel;
    
    [Tooltip("Tombol untuk kembali ke peta dari mode simulasi")]
    [SerializeField] private Button backToMapButton;

    [Header("Simulation References")]
    [Tooltip("Overlay UI data buoy yang sudah kita buat")]
    [SerializeField] private BuoyDataOverlayUI dataOverlayUI;
    
    [Tooltip("Kamera (opsional) jika ingin mengubah view saat di peta vs simulasi")]
    [SerializeField] private Behaviour camController;

    private void Start()
    {
        // Pastikan tombol Back To Map memanggil fungsi ShowMap()
        if (backToMapButton != null)
        {
            backToMapButton.onClick.AddListener(ShowMap);
        }

        // Tampilkan peta di awal aplikasi berjalan
        ShowMap();
    }

    /// <summary>
    /// Menampilkan peta, menyembunyikan data simulasi.
    /// </summary>
    public void ShowMap()
    {
        if (mapPanel != null) mapPanel.SetActive(true);
        if (backToMapButton != null) backToMapButton.gameObject.SetActive(false);
        
        // Sembunyikan UI data overlay
        if (dataOverlayUI != null) dataOverlayUI.gameObject.SetActive(false);

        // Jika ada controller kamera, bisa dinonaktifkan pergerakannya di sini
        if (camController != null) camController.enabled = false; 
    }

    /// <summary>
    /// Dipanggil oleh Pin saat di-klik.
    /// Menyembunyikan peta dan memulai simulasi.
    /// </summary>
    public void HideMapAndStartSimulation()
    {
        if (mapPanel != null) mapPanel.SetActive(false);
        if (backToMapButton != null) backToMapButton.gameObject.SetActive(true);
        
        // Tampilkan UI data overlay
        if (dataOverlayUI != null) dataOverlayUI.gameObject.SetActive(true);

        // Aktifkan kembali pergerakan kamera
        if (camController != null) camController.enabled = true;
    }
}
