// Uses the shared binocular calibration geometry for the scene ray.
using UnityEngine;

public class CalibratedGazeProvider : MonoBehaviour, IGazeProvider
{
    [SerializeField] private UnityGazeBridge bridge;
    [SerializeField] private Camera gazeCamera;        // the XR eye camera (real projection)
    [SerializeField] private float maxDistance = 10f;
    [SerializeField] private LayerMask layerMask = ~0;
    [SerializeField] private bool requireCalibrated = false;  // if true, ignore RAW until a fit exists otherwise if false, then just send a ray regardless
    [SerializeField] private GazeCalibrationGeometry geometry;

    [Header("Debug ray colour")]
    // Debug.DrawRay is for the Editor; StereoGazeVisualizer draws headset dots.
    [SerializeField] private Color uncalibratedRayColor = new(1f, 0.25f, 0.2f, 1f);
    [SerializeField] private Color calibratedRayColor = new(0.2f, 1f, 0.35f, 1f);

    public Vector2 ViewportPoint { get; private set; }
    public bool hasGaze { get; private set; }

    private void Start()
    {
        if (gazeCamera == null) gazeCamera = Camera.main;
        if (bridge == null)
            Debug.LogError("CalibratedGazeProvider: bridge is not assigned");
    }

    public bool Raycast(out RaycastHit hit)
    {
        hit = default;
        hasGaze = false;
        if (bridge == null || gazeCamera == null || geometry == null) return false;
        if (geometry.bridge != bridge || geometry.gazeCamera != gazeCamera ||
            !bridge.GeometryVerified || !bridge.StereoValid) return false;

        bool calibrated = bridge.Calibrated;
        if (requireCalibrated && !calibrated) return false;
        Vector2 g = bridge.Gaze;
        Vector3 d = geometry.LocalDirection(g);
        Ray ray = new Ray(gazeCamera.transform.position,
                          gazeCamera.transform.TransformDirection(d));
        ViewportPoint = gazeCamera.WorldToViewportPoint(geometry.WorldPoint(g));
        hasGaze = true;

        Debug.DrawRay(ray.origin, ray.direction * maxDistance,
                      calibrated ? calibratedRayColor : uncalibratedRayColor);
        return Physics.Raycast(ray, out hit, maxDistance, layerMask);
    }
}
