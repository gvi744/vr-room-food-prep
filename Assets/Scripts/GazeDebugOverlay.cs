using UnityEngine;

public class GazeDebugOverlay : MonoBehaviour
{
    [SerializeField] private UnityGazeBridge bridge;
    [SerializeField] private Camera gazeCamera;
    [SerializeField] private RectTransform canvasRect;
    [SerializeField] private RectTransform dot;
    [SerializeField] private float planeDistance = 1f;
    [SerializeField] private bool showWhenRaw = true;   // draw uncalibrated too

    private void Start()
    {
        if (gazeCamera == null) gazeCamera = Camera.main;
        SizeCanvasToFrustum();
    }

    private void SizeCanvasToFrustum()
    {
        float scale = canvasRect.localScale.x;
        float h = 2f * planeDistance * Mathf.Tan(gazeCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float w = h * gazeCamera.aspect;

        canvasRect.localPosition = new Vector3(0f, 0f, planeDistance);
        canvasRect.localRotation = Quaternion.identity;
        canvasRect.sizeDelta = new Vector2(w, h) / scale;
    }

    private void Update()
    {
        if (bridge == null) return;

        bool visible = showWhenRaw || bridge.Calibrated;
        if (dot.gameObject.activeSelf != visible) dot.gameObject.SetActive(visible);
        if (!visible) return;

        Vector2 g = bridge.Gaze;   // -1..1, y up
        dot.anchoredPosition = new Vector2(
            g.x * 0.5f * canvasRect.sizeDelta.x,
            g.y * 0.5f * canvasRect.sizeDelta.y);
    }
}