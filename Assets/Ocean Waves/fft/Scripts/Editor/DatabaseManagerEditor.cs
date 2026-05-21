using UnityEngine;
using UnityEditor;

/// <summary>
/// Custom Editor untuk DatabaseManager.
/// Menampilkan dropdown untuk memilih collection (lokasi buoy) 
/// dari daftar yang di-fetch dari Firestore.
/// </summary>
[CustomEditor(typeof(DatabaseManager))]
public class DatabaseManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DatabaseManager dm = (DatabaseManager)target;
        serializedObject.Update();

        // ─── Firestore Settings ──────────────────────────────────────
        EditorGUILayout.LabelField("Firestore Settings", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("serviceAccountKeyPath"));

        // ─── Collection Dropdown ─────────────────────────────────────
        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("Buoy Location (Collection)", EditorStyles.boldLabel);

        var collectionListProp = serializedObject.FindProperty("collectionList");
        var selectedIndexProp = serializedObject.FindProperty("selectedCollectionIndex");

        if (collectionListProp.arraySize > 0)
        {
            // Build display options
            string[] options = new string[collectionListProp.arraySize];
            for (int i = 0; i < collectionListProp.arraySize; i++)
                options[i] = collectionListProp.GetArrayElementAtIndex(i).stringValue;

            int currentIndex = selectedIndexProp.intValue;
            int newIndex = EditorGUILayout.Popup("Lokasi Buoy", currentIndex, options);

            if (newIndex != currentIndex)
            {
                selectedIndexProp.intValue = newIndex;
                serializedObject.ApplyModifiedProperties();

                // Jika sedang Play, langsung switch dan fetch data
                if (Application.isPlaying)
                {
                    dm.SetCollectionByIndex(newIndex);
                    dm.FetchAllData();
                }
            }

            // Info label
            EditorGUILayout.HelpBox($"Collection: \"{options[Mathf.Clamp(newIndex, 0, options.Length - 1)]}\"  ({options.Length} lokasi tersedia)", MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox("Belum ada collection. Klik 'Fetch Collections' atau Play untuk auto-fetch.", MessageType.Warning);
        }

        // Fetch Collections button (works in Editor via coroutine runner)
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Fetch Collections", GUILayout.Height(25)))
        {
            if (Application.isPlaying)
            {
                dm.FetchCollections();
            }
            else
            {
                EditorGUILayout.HelpBox("Tekan Play terlebih dahulu untuk fetch collections.", MessageType.Info);
                Debug.LogWarning("[DatabaseManagerEditor] Fetch Collections hanya bisa di Play Mode.");
            }
        }

        if (GUILayout.Button("Clear List", GUILayout.Width(80), GUILayout.Height(25)))
        {
            collectionListProp.ClearArray();
            selectedIndexProp.intValue = 0;
            serializedObject.ApplyModifiedProperties();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(10);

        // ─── Draw remaining properties ───────────────────────────────
        // Skip the properties we already drew
        DrawPropertiesExcluding(serializedObject,
            "m_Script",
            "serviceAccountKeyPath",
            "collectionList",
            "selectedCollectionIndex");

        serializedObject.ApplyModifiedProperties();
    }
}
