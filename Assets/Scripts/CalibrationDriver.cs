// CalibrationDriver.cs — drives the in-VR 9-point calibration through the bridge.
//
// Sequence per INTERFACE_SPEC.md:
//   for i in 1..9:  show dot i -> TARGET,i,x,y -> (user fixates + trigger) -> RECORD
//   then FIT  (bridge fits, saves calibration.json, switches stream RAW -> GAZE)
//
// The 9 targets are identical to collect_9point.py's CALIB_TARGETS and are sent
// as the -1..1 truth. The same -1..1 value both positions the dot (via viewport)
// and is sent as TARGET, so the fit and the runtime ray share one convention.
//
// Confirm reuses your existing IConfirmProvider (right-trigger). Because that is
// the SAME trigger GazeInteractor uses to plate, this driver disables the
// interactor for the duration of calibration so one press only advances the dot.

using System.Collections;
using UnityEngine;

public class CalibrationDriver : MonoBehaviour
{
    [SerializeField] private UnityGazeBridge bridge;
    [SerializeField] private MonoBehaviour confirmProviderObject;  // IConfirmProvider
    [SerializeField] private Camera gazeCamera;
    [SerializeField] private GameObject dotMarker;    // small sphere shown at each target
    [SerializeField] private float dotDistance = 4f;  // metres in front of the camera
    [SerializeField] private float recordWindow = 1.2f; // > bridge's ~1 s sample time

    [Header("Scene toggles during calibration")]
    [SerializeField] private GameObject startButtonCanvas; // world-space button, hidden while running
    [SerializeField] private GazeInteractor gazeInteractor; // disabled while running, so the
                                                            // trigger only advances dots

    [Header("Editor testing")]
    [SerializeField] private bool enableEditorKey = true;   // press C in the Editor to Begin

    private IConfirmProvider confirm;
    private bool running;

    // Same order and positions as collect_9point.py CALIB_TARGETS. Normalized
    // -1..1, y up, (0,0) = centre.
    private static readonly Vector2[] Targets =
    {
        new(0f,    0f),    // centre
        new(0f,    0.8f),  // up
        new(0.8f,  0f),    // right
        new(0f,   -0.8f),  // down
        new(-0.8f, 0f),    // left
        new(0.8f,  0.8f),  // upper-right
        new(-0.8f, 0.8f),  // upper-left
        new(0.8f, -0.8f),  // lower-right
        new(-0.8f,-0.8f),  // lower-left
    };

    private void Start()
    {
        confirm = confirmProviderObject as IConfirmProvider;
        if (gazeCamera == null) gazeCamera = Camera.main;
        if (confirm == null)
            Debug.LogError("CalibrationDriver: confirmProviderObject does not implement IConfirmProvider");
    }

    private void Update()
    {
        if (enableEditorKey && Application.isEditor && !running && Input.GetKeyDown(KeyCode.C))
            Begin();
    }

    // Call this from the world-space button's OnClick, or press C in the Editor.
    public void Begin()
    {
        if (!running) StartCoroutine(RunCalibration());
    }

    private IEnumerator RunCalibration()
    {
        if (bridge == null || confirm == null) yield break;
        running = true;

        // Take over the trigger: hide the button, stop gameplay selection so one
        // press only advances the calibration dot.
        if (startButtonCanvas != null) startButtonCanvas.SetActive(false);
        if (gazeInteractor != null)    gazeInteractor.enabled = false;
        if (dotMarker != null)         dotMarker.SetActive(true);

        for (int i = 0; i < Targets.Length; i++)
        {
            Vector2 t = Targets[i];
            PlaceDot(t);
            bridge.SendTarget(i + 1, t.x, t.y);   // 1-indexed, matches the spec

            Debug.Log($"[calib] target {i + 1}/9 at ({t.x:+0.00},{t.y:+0.00}) — fixate + trigger");

            // Wait for a fresh trigger press. WaitUntil polls each frame, so it
            // catches the one-frame WasPressedThisFrame edge.
            yield return new WaitUntil(() => confirm.IsConfirmed());

            bridge.SendRecord();                  // bridge samples ~1 s, IQR-reduces
            yield return new WaitForSeconds(recordWindow);
        }

        bridge.SendFit();   // expect ACK,FIT,... in the log; stream switches to GAZE
        Debug.Log("[calib] done — FIT sent; provider should now receive GAZE");

        // Hand the trigger back to gameplay, restore the button.
        if (dotMarker != null)         dotMarker.SetActive(false);
        if (gazeInteractor != null)    gazeInteractor.enabled = true;
        if (startButtonCanvas != null) startButtonCanvas.SetActive(true);
        running = false;
    }

    private void PlaceDot(Vector2 t)
    {
        if (dotMarker == null || gazeCamera == null) return;
        // -1..1 -> 0..1 viewport, z = distance in front of the camera.
        Vector3 vp = new((t.x + 1f) * 0.5f, (t.y + 1f) * 0.5f, dotDistance);
        dotMarker.transform.position = gazeCamera.ViewportToWorldPoint(vp);
    }
}