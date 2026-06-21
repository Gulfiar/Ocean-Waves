using UnityEngine;

[RequireComponent(typeof(Camera))]
public class PresentationWireframe : MonoBehaviour
{
    public bool showWireframe = false;

    private void OnPreRender()
    {
        if (showWireframe)
        {
            GL.wireframe = true;
        }
    }

    private void OnPostRender()
    {
        GL.wireframe = false;
    }
}
