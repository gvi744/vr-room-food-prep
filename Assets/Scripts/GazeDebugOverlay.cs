using UnityEngine;
using UnityEngine.UI;

public class GazeDebugOverlay : MonoBehaviour
{
    [SerializeField] private UnityGazeBridge bridge;
    [SerializeField] private Camera gazeCamera;
    [SerializeField] private RectTransform canvasRect;
    [SerializeField] private RectTransform dot;
    [SerializeField] private float planeDistance = 1f;
    [SerializeField] private bool showWhenRaw = true;   // draw uncalibrated too

    [Header("Cursor colour")]
    // The visible state signal. The bridge streams RAW before a fit exists and
    // GAZE after, and UnityGazeBridge.Calibrated is set from whichever arrived
    // last -- so this tracks the actual stream, not a flag Unity sets hopefully
    // when it thinks FIT worked. If FIT is refused the bridge keeps sending RAW
    // and the dot stays uncalibrated, which is the honest reading.
    [SerializeField] private bool tintByCalibration = true;
    [SerializeField] private Color uncalibratedColor = new(1f, 0.25f, 0.2f, 0.9f);
    [SerializeField] private Color calibratedColor = new(0.2f, 1f, 0.35f, 0.9f);

    // The dot's renderable. Cached because Update runs every frame and
    // GetComponent does not; resolved in Start so an unassigned or
    // non-Graphic dot degrades to "no tint" rather than throwing per frame.
    private Graphic dotGraphic;
    // Last colour written, so we only touch the Graphic when the state
    // actually changes. Setting Graphic.color dirties the canvas and forces a
    // rebuild, and doing that 90 times a second for an unchanged value is a
    // needless cost on the headset. Nullable so the first frame always writes.
    private bool? lastCalibrated;

    private void Start()
    {
        if (gazeCamera == null) gazeCamera = Camera.main;
        if (dot != null) dotGraphic = dot.GetComponent<Graphic>();
        if (tintByCalibration && dotGraphic == null)
            Debug.LogWarning("GazeDebugOverlay: dot has no Image/Graphic component, " +
                             "so it cannot be tinted. Colour feedback is off.");
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

        bool calibrated = bridge.Calibrated;

        bool visible = showWhenRaw || calibrated;
        if (dot.gameObject.activeSelf != visible) dot.gameObject.SetActive(visible);
        if (!visible) return;

        if (tintByCalibration && dotGraphic != null && lastCalibrated != calibrated)
        {
            dotGraphic.color = calibrated ? calibratedColor : uncalibratedColor;
            lastCalibrated = calibrated;
        }

        Vector2 g = bridge.Gaze;   // -1..1, y up
        dot.anchoredPosition = new Vector2(
            g.x * 0.5f * canvasRect.sizeDelta.x,
            g.y * 0.5f * canvasRect.sizeDelta.y);
    }
}