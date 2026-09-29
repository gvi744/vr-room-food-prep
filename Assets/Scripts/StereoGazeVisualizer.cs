// Uses the same target geometry as calibration; all dots hide on tracking loss.
using UnityEngine;

public class StereoGazeVisualizer : MonoBehaviour
{
    public UnityGazeBridge bridge;
    public GazeCalibrationGeometry geometry;
    public Material dotMaterial;
    public bool showPerEye = true;
    [Min(0.001f)] public float dotRadius = 0.006f;
    private GameObject fused, left, right;

    private GameObject MakeDot(string label, Color color)
    {
        GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        dot.name = label;
        dot.layer = 2; // Ignore Raycast: debug dots must not become interaction targets.
        dot.transform.SetParent(transform, false);
        dot.transform.localScale = Vector3.one * (2f * dotRadius);
        Collider collider = dot.GetComponent<Collider>();
        collider.enabled = false;
        Destroy(collider);
        // Clone a scene-referenced URP material so its shader is included in builds.
        Material material = new Material(dotMaterial);
        material.color = color;
        dot.GetComponent<Renderer>().sharedMaterial = material;
        dot.SetActive(false);
        return dot;
    }

    private void Start()
    {
        if (dotMaterial == null)
        {
            Debug.LogError("StereoGazeVisualizer: assign a material compatible with the scene renderer.");
            enabled = false;
            return;
        }
        fused = MakeDot("Combined gaze", Color.green);
        left = MakeDot("Left gaze", Color.cyan);
        right = MakeDot("Right gaze", Color.magenta);
    }

    private void Update()
    {
        bool visible = bridge != null && geometry != null && geometry.gazeCamera != null
                       && geometry.bridge == bridge && bridge.GeometryVerified && bridge.StereoValid;
        fused.SetActive(visible);
        left.SetActive(visible && showPerEye);
        right.SetActive(visible && showPerEye);
        if (!visible) return;
        fused.transform.position = geometry.WorldPoint(bridge.Gaze);
        left.transform.position = geometry.WorldPoint(bridge.LeftGaze);
        right.transform.position = geometry.WorldPoint(bridge.RightGaze);
    }

    private void OnDisable()
    {
        if (fused != null) fused.SetActive(false);
        if (left != null) left.SetActive(false);
        if (right != null) right.SetActive(false);
    }

    private void OnDestroy()
    {
        foreach (GameObject dot in new[] { fused, left, right })
        {
            if (dot == null) continue;
            Destroy(dot.GetComponent<Renderer>().sharedMaterial);
            Destroy(dot);
        }
    }
}
