# Binocular calibration in GavinsKitchen

Unity branch: `binocular-unity`, based on
`shrinking-square-boundary-to-10-degrees-per-Jason's-recommendation` at
`e66f3f4e892478a26f7277f5e2639187e7cf2541`.
Use this with `binocular-calibration` in `project-110-calibration`.
Keep Unity **6000.4.6f1**, as recorded in ProjectVersion.txt.

## 1 October: calibration UI and nine-point update

Pull both companion branches before testing. The full sequence is still **17
targets: 9 calibration + 8 independent validation**. Python now defaults to
`--stereo-fit nine-point`, using all four corners as well as the centre and
cardinal targets. Use `--stereo-fit five-point` for the previous baseline.
The new fit uses 3D direction tangents with a fitted offset, not the old pupil
polynomial. A fresh C calibration is required after this update.

The progress label now follows 0.10m above the current target. It shows a
recording timer during collection. The scene includes the label size and
visualizer references, so no extra manual wiring is needed. The three gaze
feedback dots hide during calibration/validation and return afterwards.

Network sends now run on a background thread, with a numeric IP endpoint
prepared at startup. Repeated gaze/heartbeat logs are removed and the text is
prepared before pressing C. These remove possible stalls from the start path;
the reported full-frame pause has not been reproduced on the development Mac.
If the lab still freezes, retain `[calib timing]` Console messages and record
the Editor Profiler. The two-second collection itself must leave rendering and
head motion responsive.

The model and report include `calibration_diagnostics`: training-point errors
and jitter, kept separate from the eight independent validation scores. A
nine-point fit should report `model_n_train: 9` and model
`binocular_direction_9point`. Do not treat small training errors as proof of
sub-degree validation accuracy.

## Scene changes already saved

`Assets/Scenes/GavinsKitchen.unity` is wired for one camera per eye:

- The existing XR Origin has one UnityGazeBridge, a shared
  GazeCalibrationGeometry and a StereoGazeVisualizer. Both CalibrationDriver
  and CalibratedGazeProvider reference that geometry and the existing XR camera.
- Gavin's target directions are preserved. The old 110/96-degree FOV,
  0.1235/0.1588 boundaries and 0.8 target extent are represented by **effective
  FOV X = 24.866242, Y = 24.864738**. The outer axes remain approximately
  +/-10 degrees; the target distance remains 2 metres. These are calibration
  coordinate settings, not the XR camera's optical FOV.
- The nine calibration and eight independent validation targets and their
  order, trigger confirmation, progress text and interaction references remain.
  CalibrationDriver.NormToLocalDir delegates to the shared geometry, retaining
  compatibility with the existing overlay script.
- The previous overlay component and its GazeDot are disabled. The progress
  canvas and Interactor stay enabled. New green/cyan/magenta dots show combined,
  left and right gaze using the same target geometry after the sequence. Their URP material is
  assigned, their colliders are disabled, and they hide on tracking loss.
- Python and Unity must acknowledge matching geometry before calibration.
  Stale or invalid binocular data cannot drive gaze interaction.

No extra Unity render camera is needed. The two USB cameras run in Python.
The existing EyeTracker.cs file is retained but is not attached to this scene;
do not add a second calibration component alongside this pipeline.

The original default `vr_bridge.py` is monocular. Use the new `--stereo`
mode, which reads timestamped per-eye JSON, calibrates each eye independently,
and combines the two corrected ray intersections with the same 2m target
sphere in Python. Unity receives the combined and per-eye coordinates in one
`GAZE2` packet. This preserves one ray for existing gaze interaction; it is not
an estimate of object depth from triangulation. Do not run two old bridges or
send Jason's twelve-value CSV to the monocular reader.

## First lab run

1. Update both repositories in their own lab folders. Keep any local changes
   if Git asks you to resolve them; do not force a reset.

   ```text
   # In project-110-calibration
   git fetch origin
   git switch binocular-calibration
   git pull --ff-only

   # In vr-room-food-prep
   git fetch origin
   git switch binocular-unity
   git pull --ff-only
   ```

2. Open GavinsKitchen with Unity **6000.4.6f1**. On XR Origin /
   GazeCalibrationGeometry, use FOV X **24.866242**, FOV Y **24.864738**,
   target distance **2**, and change **IPD mm** from example 64 to the
   participant's actual IPD. All scene references are already assigned.
   With Quest Link on the same PC, UnityGazeBridge uses IP **127.0.0.1**,
   send port **9100**, receive port **9101**. Keep only one bridge running.
3. In a terminal in `project-110-calibration`, use the existing pinned Python
   environment and launch Jason's stereo tracker through the adapter:

   ```text
   python run_jason_stereo.py --tracker "C:/path/to/EyeTracker/3DTracker/Orlosky3DEyeTrackerStereo.py" --output stereo_gaze.json --right-rotation 180
   ```

   Replace the tracker path. The rotation option addresses the reported
   upside-down right feed; use 0 instead if the feed is already correctly
   oriented. The GUI vertical flip is a separate setting, described below.
   On Windows, replace `python` with `.\.venv\Scripts\python.exe` if the
   environment is not activated. On Mac use `./.venv/bin/python`. Keep
   NumPy **1.26.4** and OpenCV **4.10.0.84**.
4. Select different physical camera indices, one eye each. Verify left/right
   by briefly covering each lens. Set cropping/flips before calibration,
   start both cameras, look around the target area to warm up both eye models,
   then fix the spheres with F in a tracker window or **fixed eye sphere**.
5. In a second terminal in the **same calibration folder**, check the live
   input while keeping both cameras running and looking steadily near centre:

   ```text
   python check_stereo_input.py --stereo-file stereo_gaze.json --seconds 10 --sample-window 2 --min-confidence 0
   ```

   Look for **PASS** and at least six fresh pairs in each two-second window.
   The saved `stereo_input_check.json` includes per-eye update rates, accepted
   pair rate, timing skew and rejection reasons. This tests input availability,
   not accuracy. Fix a failed camera, unfixed sphere or stale path before
   continuing. If input is valid but too slow, try `--sample-window 3` in both
   this check and the bridge. Do not loosen timestamp limits just to pass.
6. In that second terminal, start the bridge after the check finishes:

   ```text
   python vr_bridge.py --stereo --stereo-file stereo_gaze.json --stereo-fit nine-point --fov-x 24.866242 --fov-y 24.864738 --target-distance 2 --ipd-mm 64 --sample-window 2 --min-confidence 0 --session-note "binocular run 1"
   ```

   Use the same actual IPD as Unity. Add `--unity-ip` only if Unity is on
   another device. Upstream does not export stereo confidence; zero disables
   that filter while validity, timestamp, pairing and fixed-sphere checks
   remain active. Do not use the old `--min-confidence 0.5` here, or load the
   monocular calibration file. Stereo selects its own direction model.
7. Play and wait for `ACK,GEOMETRY,matched`. Press C in the Unity Game window,
   or use the existing calibration start button. Complete a fresh **9+8** run.
   At each target, look with both eyes, press the usual right-controller
   confirmation trigger, and keep fixation until the target moves. Look at
   the target. Gaze feedback dots stay hidden during the sequence and return
   afterwards when the calibrated stream is valid.
   Use V only to validate the current binocular model without refitting.
8. Read `validation_stereo_<timestamp>.json` in the calibration folder; the
   latest copy is `validation_stereo_report.json`.
   - `n_targets` must be **8**.
   - `accuracy_deg` is the mean combined error; `max_error_deg` is the worst
     target error. The all-targets goal requires **max < 1** and
     **all_targets_under_1_deg = true**, not just mean < 1.
   - `eyes.left` and `eyes.right` summarise each eye separately. Each entry in
     `per_target` contains combined `error_deg`, `left.error_deg` and
     `right.error_deg`, plus precision and accepted sample information.
   - Green is combined, cyan is left, magenta is right. If one eye is much
     worse, correct that camera/model before interpreting the combined result.
9. After saving the report, stop either camera: all three gaze dots and gaze
   selection must stop when freshness expires (about 0.5s). Restart and perform
   a fresh C calibration for further testing. Repeat full runs before comparing
   with the single-camera baseline; a software test is not a hardware result.

If you change FOV, distance or IPD, match both sides and recalibrate. Do not
change the reported FOV alone to reduce the error number. A tracker restart,
camera restart, changed eye model or headset movement requires recalibration.

## Right camera upside down

The GUI's `flip image` option is a **vertical flip**, not a 180-degree rotation.
If the right feed is only vertically inverted, toggle its own checkbox.

For a full half-turn of the current right image, restart the adapter with:

```text
python run_jason_stereo.py --tracker "C:/path/to/EyeTracker/3DTracker/Orlosky3DEyeTrackerStereo.py" --output stereo_gaze.json --right-rotation 180
```

Restore the GUI flip checkboxes to the same settings used before restarting.
This extra rotation affects only the right frame, before cropping/detection.
Use `--right-rotation 0` or omit it to disable this adapter adjustment. It does
not undo separate rotation code in a custom tracker. The left camera and the
Unity/bridge geometry settings stay unchanged. Warm up both eye models again,
fix the spheres, and press C for a fresh calibration and validation.

## Verification status

Python tests exercise the full nine-plus-eight UDP flow with synthetic eye
directions, including missing/stale camera data and geometry mismatch. A scene
audit checks the saved references, GUIDs, enabled state, target independence and
all 17 target directions against Gavin's original branch and Python geometry.

A Unity Editor is not installed on the development Mac. C# compilation, Quest
rendering, physical camera timing and hardware accuracy still need the lab
Play-mode check. These software checks do not establish sub-degree accuracy.
