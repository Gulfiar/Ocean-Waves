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

    private void Update()
    {
        // Calculate FPS dynamically
        deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;
        msec = deltaTime * 1000.0f;
        fps = 1.0f / deltaTime;
    }

    private void OnGUI()
    {
        float width = 145f;
        float height = 65f;

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
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 9,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.7f, 0.7f, 0.7f) }
            };
        }

        // Keep widget within screen boundaries if screen size changes
        if (windowRect.x > Screen.width) windowRect.x = Screen.width - width - 15f;
        if (windowRect.y > Screen.height) windowRect.y = Screen.height - height - 15f;

        // Draw a dark glassmorphic window box that is draggable
        GUI.color = new Color(0.04f, 0.04f, 0.08f, 0.88f);
        windowRect = GUI.Window(999, windowRect, DrawFPSWindow, "", GUI.skin.box);
        GUI.color = Color.white;
    }

    private void DrawFPSWindow(int windowID)
    {
        // Color coding for FPS
        if (fps >= 60.0f)
            fpsStyle.normal.textColor = Color.green;
        else if (fps >= 30.0f)
            fpsStyle.normal.textColor = Color.yellow;
        else
            fpsStyle.normal.textColor = Color.red;

        // Header label
        GUILayout.Label(":: FPS MONITOR (DRAG) ::", titleStyle);
        GUILayout.Space(2);

        // FPS and frame time details
        string fpsText = string.Format("{0:0.0} FPS\n({1:0.0} ms)", fps, msec);
        GUILayout.Label(fpsText, fpsStyle);

        // Make the entire box draggable
        GUI.DragWindow(new Rect(0, 0, windowRect.width, windowRect.height));
    }
}
