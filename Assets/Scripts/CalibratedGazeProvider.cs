// CalibratedGazeProvider.cs — Fork B, direction-to-screen.
//
// The bridge streams GAZE,(x,y) already corrected by your polynomial, in
// normalized -1..1 (y up), which is a screen/viewport position. We map that to
// a viewport point and let the camera turn it into a world-space ray. Because
// the XR camera's projection already encodes the headset's real FOV, the ray
// angle is correct without setting fov_deg manually here. (fov_deg in
// calibration.json remains only the reporting unit for accuracy-in-degrees.)
//
// Drop-in replacement for HeadGazeProvider: same IGazeProvider shape, so
// GazeInteractor needs no changes.

using UnityEngine;

public class CalibratedGazeProvider : MonoBehaviour, IGazeProvider
{
    [SerializeField] private UnityGazeBridge bridge;
    [SerializeField] private Camera gazeCamera;        // the XR eye camera (real projection)
    [SerializeField] private float maxDistance = 10f;
    [SerializeField] private LayerMask layerMask = ~0;
    [SerializeField] private bool requireCalibrated = true;  // ignore RAW until a fit exists

    private void Start()
    {
        if (gazeCamera == null) gazeCamera = Camera.main;
        if (bridge == null)
            Debug.LogError("CalibratedGazeProvider: bridge is not assigned");
    }

    public bool Raycast(out RaycastHit hit)
    {
        hit = default;
        if (bridge == null || gazeCamera == null) return false;

        // Before FIT the stream is RAW (uncalibrated direction, not a screen
        // position). Optionally skip it so gameplay only uses calibrated gaze.
        if (requireCalibrated && !bridge.Calibrated) return false;

        Vector2 g = bridge.Gaze;                          // -1..1, y up
        Vector3 vp = new Vector3((g.x + 1f) * 0.5f,       // -1..1 -> 0..1 viewport
                                 (g.y + 1f) * 0.5f, 0f);
        Ray ray = gazeCamera.ViewportPointToRay(vp);
        Debug.DrawRay(ray.origin, ray.direction * maxDistance, Color.cyan);
        return Physics.Raycast(ray, out hit, maxDistance, layerMask);
    }
}
