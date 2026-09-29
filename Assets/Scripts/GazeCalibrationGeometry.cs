// Integration helper for Gavin's Unity project. See STEREO_SETUP.md.
using UnityEngine;

public class GazeCalibrationGeometry : MonoBehaviour
{
    public UnityGazeBridge bridge;
    public Camera gazeCamera;
    // Equivalent to the existing 110/96 FOV, .1235/.1588 boundaries and .8 extent.
    [Range(1f, 100f)] public float fovXDeg = 24.866242f;
    [Range(1f, 100f)] public float fovYDeg = 24.864738f;
    [Min(0.2f)] public float targetDistance = 2f;
    [Range(40f, 85f)] public float ipdMm = 64f;

    private float nextCheck;
    private Vector4 lastSettings;
    private bool sent;

    public Vector3 LocalDirection(Vector2 coordinates) => new Vector3(
        coordinates.x * Mathf.Tan(fovXDeg * Mathf.Deg2Rad * 0.5f),
        coordinates.y * Mathf.Tan(fovYDeg * Mathf.Deg2Rad * 0.5f), 1f).normalized;

    public Vector3 LocalPoint(Vector2 coordinates) => LocalDirection(coordinates) * targetDistance;
    public Vector3 WorldPoint(Vector2 coordinates) =>
        gazeCamera.transform.TransformPoint(LocalPoint(coordinates));

    private void Update()
    {
        if (bridge == null || gazeCamera == null) return;
        Vector4 settings = new(fovXDeg, fovYDeg, targetDistance, ipdMm);
        if (!sent || settings != lastSettings)
        {
            bridge.GeometryVerified = false;
            lastSettings = settings;
            sent = true;
            nextCheck = 0f;
        }
        // Periodic verification also recovers if the Python bridge restarts.
        if (Time.unscaledTime < nextCheck) return;
        bridge.SendGeometry(fovXDeg, fovYDeg, targetDistance, ipdMm);
        nextCheck = Time.unscaledTime + 1f;
    }
}
