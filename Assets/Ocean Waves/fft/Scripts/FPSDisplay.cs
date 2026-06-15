using UnityEngine;

public class FPSDisplay : MonoBehaviour
{
    private Rect windowRect;
    private float deltaTime = 0.0f;
    private float msec;
    private float fps;
    private GUIStyle fpsStyle;
    private GUIStyle titleStyle;
    private bool initializedPos = false;

    private void Awake()
    {
        // Memaksa unlock FPS limits untuk keperluan benchmarking/testing
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
    }

    private void Update()
    {
        // Calculate FPS dynamically
        deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;
        msec = deltaTime * 1000.0f;
        fps = 1.0f / deltaTime;
    }

    private void OnGUI()
    {
        float width = 125f;
        float height = 30f;

        // Initialize position at bottom right of the screen
        if (!initializedPos && Screen.width > 0 && Screen.height > 0)
        {
            float x = Screen.width - width - 15f;
            float y = Screen.height - height - 15f;
            windowRect = new Rect(x, y, width, height);
            initializedPos = true;
        }

        // Initialize styles
        if (fpsStyle == null)
        {
            fpsStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
        }

        // Keep widget within screen boundaries if screen size changes
        if (windowRect.x > Screen.width) windowRect.x = Screen.width - width - 15f;
        if (windowRect.y > Screen.height) windowRect.y = Screen.height - height - 15f;

        // Draw a completely transparent window box that is draggable (using GUIStyle.none)
        windowRect = GUI.Window(999, windowRect, DrawFPSWindow, "", GUIStyle.none);
    }

    private void DrawFPSWindow(int windowID)
    {
        // Color coding for FPS
        Color mainColor = Color.green;
        if (fps >= 60.0f)
            mainColor = Color.green;
        else if (fps >= 30.0f)
            mainColor = Color.yellow;
        else
            mainColor = Color.red;

        // FPS and frame time details in one single line
        string fpsText = string.Format("{0:0.0} FPS \n({1:0.0} ms)", fps, msec);

        // Draw black drop-shadow for high-contrast readability against the waves
        fpsStyle.normal.textColor = new Color(0f, 0f, 0f, 0.9f);
        GUI.Label(new Rect(1.5f, 1.5f, windowRect.width, windowRect.height), fpsText, fpsStyle);

        // Draw main colored text
        fpsStyle.normal.textColor = mainColor;
        GUI.Label(new Rect(0f, 0f, windowRect.width, windowRect.height), fpsText, fpsStyle);

        // Make the entire text area draggable with the mouse
        GUI.DragWindow(new Rect(0, 0, windowRect.width, windowRect.height));
    }
}
