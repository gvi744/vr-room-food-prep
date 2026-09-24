using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

public class CalibrationDriver : MonoBehaviour
{
    [SerializeField] private UnityGazeBridge bridge;
    [SerializeField] private MonoBehaviour confirmProviderObject;  // IConfirmProvider
    [SerializeField] private Camera gazeCamera;
    [SerializeField] private GameObject dotMarker;    // small sphere shown at each target
    [SerializeField] private float dotDistance = 4f;  // metres in front of the camera
    [SerializeField] private float recordTimeout = 6f; // > bridge's 5 s sampling deadline

    [Header("Calibration square (head-locked)")]
    // Must match calibration.py QUEST3_FOV_X_DEG / QUEST3_FOV_Y_DEG.
    [SerializeField] private float fovXDeg = 110f;
    [SerializeField] private float fovYDeg = 96f;
    // Fraction of FOV the outermost dots sit at, per axis. Lower X to pull the
    // sides into a comfortable viewing box (e.g. with an extended frame) without
    // touching the true optical FOV the fit relies on. 0.6 = inner 60%.
    //
    // WHATEVER THIS IS SET TO, the bridge must be started with the MATCHING
    // effective FOV, because every degree it reports is computed from that
    // number:  --fov-x  2*atan(boundaryFractionX * tan(fovXDeg/2))
    //          --fov-y  2*atan(boundaryFractionY * tan(fovYDeg/2))
    // At 0.30 with 110x96 that is 46.4 x 36.9, not the raw 110x96.
    [SerializeField, Range(0.1f, 1f)] private float boundaryFractionX = 0.6f;
    [SerializeField, Range(0.1f, 1f)] private float boundaryFractionY = 0.6f;

    // The raw target arrays reach ±0.8 at their extremes; dividing by this makes
    // the outermost dot map to the boundary fraction of the FOV exactly.
    private const float TargetExtent = 0.8f;

    [Header("Scene toggles during calibration")]
    [SerializeField] private GameObject startButtonCanvas; // world-space button, hidden while running
    [SerializeField] private GazeInteractor gazeInteractor; // disabled while running, so the
                                                            // trigger only advances dots

    [Header("Editor testing")]
    [SerializeField] private bool allowKeyboardStart = true; // press C to Begin()

    private IConfirmProvider confirm;
    private bool running;

    // The dot's parent before calibration. While calibration runs we reparent
    // dotMarker under gazeCamera so the whole target square follows the head;
    // Restore() puts it back exactly where the scene had it.
    private Transform dotOriginalParent;

    [Header("Validation")]
    [SerializeField] private bool runValidation = true; // re-check accuracy after FIT

    [Header("Progress overlay")]
    // Optional. Assign a screen-corner TMP label to show sequence position,
    // e.g. "1/17". Total is calibration dots + (validation dots if enabled).
    [SerializeField] private TMP_Text progressLabel;
    // What the label reads once the sequence is over. A terminal count like
    // "17/17" says the dots were shown, which is not the question the operator
    // is asking at that moment -- they want to know whether the run produced a
    // calibration. The two are not the same: every dot can be recorded and FIT
    // can still be refused, and in that case the counter would have read 9/9
    // and stopped, looking like success.
    [SerializeField] private string calibratedLabel = "Calibrated";
    [SerializeField] private string notCalibratedLabel = "Not calibrated";

    // Running position across the whole calib+val sequence, for progressLabel.
    private int progressStep;
    private int progressTotal;

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
        if (dotMarker != null && gazeCamera != null)
        {
            dotMarker.SetActive(true);
            // Remember where the dot lived, then attach it to the camera so its
            // world pose is re-derived every frame as the head moves. PlaceDot
            // now only sets a camera-local position; the hierarchy does the
            // head-following, so the square stays in view no matter where the
            // user looks. worldPositionStays:false keeps localPosition as the
            // literal offset we write, not a value back-computed from world.
            dotOriginalParent = dotMarker.transform.parent;
            dotMarker.transform.SetParent(gazeCamera.transform, false);
        }
    }

    private void Restore()
    {
        if (dotMarker != null)
        {
            // Detach from the camera and hand the dot back to its original
            // parent, so nothing stays stuck to the head after calibration.
            dotMarker.transform.SetParent(dotOriginalParent, false);
            dotMarker.SetActive(false);
        }
        if (gazeInteractor != null)    gazeInteractor.enabled = true;
        if (startButtonCanvas != null) startButtonCanvas.SetActive(true);
    }

    private IEnumerator RunCalibration()
    {
        if (bridge == null || confirm == null) yield break;
        running = true;
        TakeOver();

        // Whole sequence: 9 calibration dots, plus validation if it will run.
        progressTotal = Targets.Length + (runValidation ? ValTargets.Length : 0);
        SetProgress(0, progressTotal);

        // Fresh session: an earlier aborted/finished run leaves pairs in the
        // bridge, and FIT would silently mix them into this run's fit.
        bridge.SendReset();

        for (int i = 0; i < Targets.Length; i++)
        {
            Vector2 t = Targets[i];
            PlaceDot(t);
            SetProgress(i + 1, progressTotal);
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
            yield return ValidationSequence(Targets.Length);

        // THIS run's outcome, not bridge.Calibrated. A stale model from --load
        // leaves the stream calibrated even when this run's FIT was refused,
        // and reporting that as "Calibrated" would be the exact false
        // reassurance the label exists to prevent.
        SetProgressText(fitOk ? calibratedLabel : notCalibratedLabel);

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
        progressTotal = ValTargets.Length;
        SetProgress(0, progressTotal);
        yield return ValidationSequence(0);
        // A calibration already existed — that was the entry condition above —
        // so the run ends calibrated whatever the validation numbers came out at.
        SetProgressText(calibratedLabel);
        Restore();
        running = false;
    }

    // Show each validation dot, have the bridge score the calibrated stream
    // against it, then request the summary report. Assumes TakeOver() is done.
    // stepOffset is how many dots preceded this sequence (Targets.Length after a
    // full calibration, 0 for validation-only), so the counter reads continuously.
    private IEnumerator ValidationSequence(int stepOffset)
    {
        for (int i = 0; i < ValTargets.Length; i++)
        {
            Vector2 t = ValTargets[i];
            PlaceDot(t);
            SetProgress(stepOffset + i + 1, progressTotal);
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

    private void SetProgress(int step, int total)
    {
        progressStep = step;
        progressTotal = total;
        if (progressLabel != null) progressLabel.text = $"{step}/{total}";
    }

    // Terminal state, replacing the counter once there are no more dots to show.
    private void SetProgressText(string text)
    {
        if (progressLabel != null) progressLabel.text = text;
    }

    private void PlaceDot(Vector2 t)
    {
        if (dotMarker == null || gazeCamera == null) return;

        // Normalize so the ±0.8 extremes become ±1 "square edge", then scale by
        // the per-axis boundary fraction to sit at that fraction of the real FOV.
        // Same tan(FOV/2) mapping calibration.py uses, so the dot is at the exact
        // angle the fit assumes for this (x,y) — Option A, Python untouched.
        float nx = (t.x / TargetExtent) * boundaryFractionX;
        float ny = (t.y / TargetExtent) * boundaryFractionY;

        float tx = nx * Mathf.Tan(fovXDeg * 0.5f * Mathf.Deg2Rad);
        float ty = ny * Mathf.Tan(fovYDeg * 0.5f * Mathf.Deg2Rad);

        // Direction in the camera's local frame: forward, offset by the tangents.
        // The dot is parented to the camera (see TakeOver), so a local position
        // is all we set — Unity re-derives the world pose each frame as the head
        // moves, making the whole square head-following. The tan(FOV/2) mapping
        // is unchanged, so each dot still sits at the exact angle the Python fit
        // assumes for this (x,y).
        // Vector3 dirLocal = new Vector3(tx, ty, 1f).normalized;
        dotMarker.transform.localPosition = NormToLocalDir(t) * dotDistance;
    }

    public Vector3 NormToLocalDir(Vector2 u)
{
        float tx = (u.x / TargetExtent) * boundaryFractionX * Mathf.Tan(fovXDeg * 0.5f * Mathf.Deg2Rad);
        float ty = (u.y / TargetExtent) * boundaryFractionY * Mathf.Tan(fovYDeg * 0.5f * Mathf.Deg2Rad);
        return new Vector3(tx, ty, 1f).normalized;
    }
}