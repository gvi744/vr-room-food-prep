# Binocular calibration in GavinsKitchen

Unity branch: `binocular-unity`, based on
`shrinking-square-boundary-to-10-degrees-per-Jason's-recommendation` at
`e66f3f4e892478a26f7277f5e2639187e7cf2541`.
Use this with `binocular-calibration` in `project-110-calibration`.
Keep Unity **6000.4.6f1**, as recorded in ProjectVersion.txt.

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
  left and right gaze using the same target geometry. Their URP material is
  assigned, their colliders are disabled, and they hide on tracking loss.
- Python and Unity must acknowledge matching geometry before calibration.
  Stale or invalid binocular data cannot drive gaze interaction.

No extra Unity render camera is needed. The two USB cameras run in Python.
The existing EyeTracker.cs file is retained but is not attached to this scene;
do not add a second calibration component alongside this pipeline.

## First lab run

1. Open GavinsKitchen on this branch. On XR Origin / GazeCalibrationGeometry,
   change **IPD mm** from the example 64 to the participant's actual IPD. Use
   the same number below. All other scene references are already assigned.
2. In the calibration repo's pinned Python environment, launch Jason's latest
   stereo tracker through our adapter (replace the tracker path):

   ```text
   python run_jason_stereo.py --tracker "C:/path/to/EyeTracker/3DTracker/Orlosky3DEyeTrackerStereo.py" --output stereo_gaze.json
   ```

3. Select different physical camera indices, one eye each. Verify left/right
   by briefly covering each lens. Set cropping/flips before calibration, warm
   up both eye models, then fix the spheres with F in a tracker window.
4. In another terminal in the same calibration repo, run:

   ```text
   python vr_bridge.py --stereo --stereo-file stereo_gaze.json --fov-x 24.866242 --fov-y 24.864738 --target-distance 2 --ipd-mm 64 --sample-window 2 --min-confidence 0
   ```

   Use the same actual IPD as Unity. On the Mac use `./.venv/bin/python`.
   Keep NumPy 1.26.4 and OpenCV 4.10.0.84. Add `--unity-ip` only if Unity is on
   another device. Defaults use the same PC under Quest Link (UDP 9100/9101).
   Upstream does not export stereo confidence; zero disables that filter while
   validity, timestamp, frame-pair and fixed-sphere checks remain active.
5. Play and wait for `ACK,GEOMETRY,matched`. Press C in the Unity Game window,
   or use the existing calibration start button. Complete a fresh 9+8 run,
   keeping fixation until each recording finishes. The old monocular model
   cannot be reused. Use V only to validate the current binocular model.
6. Confirm `validation_stereo_<timestamp>.json` contains eight targets and
   left/right/combined errors. Stop either camera after fitting: all three
   gaze dots and gaze selection must stop when freshness expires. Repeat a
   full run before comparing accuracy with the single-camera baseline.

If you change FOV, distance or IPD, match both sides and recalibrate. Do not
change the reported FOV alone to reduce the error number. A tracker restart,
camera restart, changed eye model or headset movement requires recalibration.

## Verification status

Python tests exercise the full nine-plus-eight UDP flow with synthetic eye
directions, including missing/stale camera data and geometry mismatch. A scene
audit checks the saved references, GUIDs, enabled state, target independence and
all 17 target directions against Gavin's original branch and Python geometry.

A Unity Editor is not installed on the development Mac. C# compilation, Quest
rendering, physical camera timing and hardware accuracy still need the lab
Play-mode check. These software checks do not establish sub-degree accuracy.
