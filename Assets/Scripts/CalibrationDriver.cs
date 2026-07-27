using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class CalibrationDriver : MonoBehaviour
{
    [SerializeField] private UnityGazeBridge bridge;
    [SerializeField] private MonoBehaviour confirmProviderObject;  // IConfirmProvider
    [SerializeField] private Camera gazeCamera;
    [SerializeField] private GameObject dotMarker;    // small sphere shown at each target
    [SerializeField] private float dotDistance = 4f;  // metres in front of the camera
    [SerializeField] private float recordTimeout = 6f; // > bridge's 5 s sampling deadline

    [Header("Scene toggles during calibration")]
    [SerializeField] private GameObject startButtonCanvas; // world-space button, hidden while running
    [SerializeField] private GazeInteractor gazeInteractor; // disabled while running, so the
                                                            // trigger only advances dots

    [Header("Editor testing")]
    [SerializeField] private bool allowKeyboardStart = true; // press C to Begin()

    private IConfirmProvider confirm;
    private bool running;

    [Header("Validation")]
    [SerializeField] private bool runValidation = true; // re-check accuracy after FIT

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

    // Same as collect_9point.py VALID_TARGETS: deliberately DIFFERENT positions
    // from the calibration set, so accuracy is measured on points the fit never
    // saw (testing on the calibration dots would measure overfitting).
    private static readonly Vector2[] ValTargets =
    {
        new(0.4f,  0.4f),
        new(-0.4f, 0.4f),
        new(0.4f, -0.4f),
        new(-0.4f,-0.4f),
        new(0f,    0.5f),
        new(0.5f,  0f),
        new(-0.5f, 0f),
        new(0f,   -0.5f),
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
        if (!allowKeyboardStart || Keyboard.current == null) return;
        if (Keyboard.current.cKey.wasPressedThisFrame) Begin();
        if (Keyboard.current.vKey.wasPressedThisFrame) BeginValidation();
    }

    // Call this from the world-space button's OnClick, or press C in the Editor.
    public void Begin()
    {
        if (!running) StartCoroutine(RunCalibration());
    }

    // Re-check accuracy of the CURRENT calibration (e.g. after starting the
    // bridge with --load) without recalibrating. Press V, or call from a button.
    public void BeginValidation()
    {
        if (!running) StartCoroutine(RunValidationOnly());
    }

    // Hide the start button and stop gameplay selection so a trigger press only
    // advances the current dot; Restore() hands control back.
    private void TakeOver()
    {
        if (startButtonCanvas != null) startButtonCanvas.SetActive(false);
        if (gazeInteractor != null)    gazeInteractor.enabled = false;
        if (dotMarker != null)         dotMarker.SetActive(true);
    }

    private void Restore()
    {
        if (dotMarker != null)         dotMarker.SetActive(false);
        if (gazeInteractor != null)    gazeInteractor.enabled = true;
        if (startButtonCanvas != null) startButtonCanvas.SetActive(true);
    }

    private IEnumerator RunCalibration()
    {
        if (bridge == null || confirm == null) yield break;
        running = true;
        TakeOver();

        // Fresh session: an earlier aborted/finished run leaves pairs in the
        // bridge, and FIT would silently mix them into this run's fit.
        bridge.SendReset();

        for (int i = 0; i < Targets.Length; i++)
        {
            Vector2 t = Targets[i];
            PlaceDot(t);
            bridge.SendTarget(i + 1, t.x, t.y);   // 1-indexed, matches the spec

            Debug.Log($"[calib] target {i + 1}/9 at ({t.x:+0.00},{t.y:+0.00}) — fixate + trigger");

            // Wait for a fresh trigger press. WaitUntil polls each frame, so it
            // catches the one-frame WasPressedThisFrame edge.
            yield return new WaitUntil(() => confirm.IsConfirmed());

            int acks = bridge.RecordAcks, errs = bridge.RecordErrs;
            bridge.SendRecord();                  // bridge samples ~1 s, IQR-reduces

            // Advance on the bridge's actual response, not a blind timer — a
            // slow tracker means RECORD can take up to 5 s, and moving the dot
            // while the bridge is still sampling contaminates the pair.
            float deadline = Time.time + recordTimeout;
            yield return new WaitUntil(() =>
                bridge.RecordAcks != acks || bridge.RecordErrs != errs ||
                Time.time >= deadline);

            if (bridge.RecordAcks == acks)
            {
                // ERR or timeout (tracker down / no gaze data): stay on this
                // dot and let the user try again instead of feeding FIT a hole.
                Debug.LogWarning($"[calib] target {i + 1} not recorded — " +
                                 "check tracker/bridge, then fixate + trigger again");
                i--;
                continue;
            }
        }

        int fAcks = bridge.FitAcks, fErrs = bridge.FitErrs;
        bridge.SendFit();
        float fitDeadline = Time.time + 3f;
        yield return new WaitUntil(() =>
            bridge.FitAcks != fAcks || bridge.FitErrs != fErrs ||
            Time.time >= fitDeadline);

        bool fitOk = bridge.FitAcks != fAcks;
        if (fitOk)
            Debug.Log("[calib] done — FIT acknowledged; provider now receives GAZE");
        else
            Debug.LogError("[calib] FIT failed or timed out — stream stays RAW; see bridge log");

        // Immediately re-check accuracy on targets the fit never saw.
        if (fitOk && runValidation)
            yield return ValidationSequence();

        Restore();
        running = false;
    }

    private IEnumerator RunValidationOnly()
    {
        if (bridge == null || confirm == null) yield break;
        if (!bridge.Calibrated)
        {
            Debug.LogWarning("[valid] no calibrated stream — run calibration first, " +
                             "or start the bridge with --load");
            yield break;
        }
        running = true;
        TakeOver();
        yield return ValidationSequence();
        Restore();
        running = false;
    }

    // Show each validation dot, have the bridge score the calibrated stream
    // against it, then request the summary report. Assumes TakeOver() is done.
    private IEnumerator ValidationSequence()
    {
        for (int i = 0; i < ValTargets.Length; i++)
        {
            Vector2 t = ValTargets[i];
            PlaceDot(t);
            bridge.SendValTarget(i + 1, t.x, t.y);

            Debug.Log($"[valid] target {i + 1}/{ValTargets.Length} at " +
                      $"({t.x:+0.00},{t.y:+0.00}) — fixate + trigger");

            yield return new WaitUntil(() => confirm.IsConfirmed());

            int acks = bridge.ValRecordAcks, errs = bridge.ValRecordErrs;
            bridge.SendValRecord();
            float deadline = Time.time + recordTimeout;
            yield return new WaitUntil(() =>
                bridge.ValRecordAcks != acks || bridge.ValRecordErrs != errs ||
                Time.time >= deadline);

            if (bridge.ValRecordAcks == acks)
            {
                Debug.LogWarning($"[valid] target {i + 1} not recorded — " +
                                 "check tracker/bridge, then fixate + trigger again");
                i--;
                continue;
            }
        }

        int rAcks = bridge.ValReportAcks, rErrs = bridge.ValReportErrs;
        bridge.SendValReport();
        float reportDeadline = Time.time + 3f;
        yield return new WaitUntil(() =>
            bridge.ValReportAcks != rAcks || bridge.ValReportErrs != rErrs ||
            Time.time >= reportDeadline);

        if (bridge.ValReportAcks != rAcks)
            Debug.Log("[valid] RESULT — " + bridge.LastValReport +
                      " (saved to validation_report.json next to the bridge)");
        else
            Debug.LogError("[valid] no validation report received — see bridge log");
    }

    private void PlaceDot(Vector2 t)
    {
        if (dotMarker == null || gazeCamera == null) return;
        // -1..1 -> 0..1 viewport, z = distance in front of the camera.
        Vector3 vp = new((t.x + 1f) * 0.5f, (t.y + 1f) * 0.5f, dotDistance);
        dotMarker.transform.position = gazeCamera.ViewportToWorldPoint(vp);
    }
}