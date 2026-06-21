VR Food-Prep POC: Gaze-plus-Click Interaction Primitive

A Unity proof-of-concept validating a gaze-to-target plus click-to-confirm interaction primitive, themed as a Papa's Pizzeria style burger assembly task. Built to eventually target a Meta Quest headset with an external eye tracker.

What it validates

The primitive under test is gaze selects, click confirms, kept deliberately distinct:


Gaze points at a target (currently the mouse stands in for gaze).
Click confirms the action on whatever is currently targeted.


This is not dwell-based, not gaze-only, and not a carry/physics mechanic. Placement is deterministic snap-to-plate with no physics, so the interaction data isolates the gaze-plus-click primitive cleanly.

Mechanic


Three ingredients sit on a table: bottom bun, beef patty, sesame top bun.
Gaze at an ingredient to show a "Click to Plate" prompt.
Click to place an inert copy of that ingredient onto the plate, stacked.
Gaze at the trash can and click to clear the plate.
Stack order is not validated (any combination is allowed by design).


Architecture


Render pipeline: URP (Universal 3D), not the VR template.
Input: new Input System package (Mouse.current), not legacy UnityEngine.Input.
Develop-flat-first: built on PC with the mouse standing in for gaze. OpenXR + XR Origin to be added later.


Key scripts

ScriptRoleRayCastMouseTestRaycasts from the camera through the mouse position, tracks currentTarget, fires gaze enter/leave, reads the click and calls OnSelect on the target.SelectablePer-object component. Shows/hides the hover prompt and handles OnSelect, branching on SelectableKind (Ingredient or Trash).PlateOwns the stack. Place(prefab) instantiates an inert copy at the next drop height; Clear() destroys all placed items and resets.

Object setup


Table ingredients: mesh + collider + Selectable + world-space prompt canvas. Interactive. Each holds a reference to its matching placed prefab and the Plate.
Placed prefabs (BottomBun_Placed, Patty_Placed, TopBun_Placed): mesh only, no collider, no Selectable, no canvas. Inert stack pieces.
Trash can: collider + Selectable with Kind set to Trash.


Current status

Working: full loop end to end. Gaze (mouse) selects, click confirms, plate stacks ingredients, trash clears the plate.

Next steps (not yet built):


Route the gaze ray through an IGazeProvider abstraction so the mouse-to-eye-tracker swap touches no interaction logic. A MouseGazeProvider returns the mouse-screen-point ray; a later EyeTrackerGazeProvider returns one built from the tracker.
Add OpenXR + XR Origin to run on the headset.
Wire the JEOresearch eye-tracker output (pupil ellipse in camera space, needs a calibration step) into the eye-tracker provider.


Setup on a new machine

This project uses Git. Most of a Unity project regenerates locally, so only source-of-truth files are committed.

Requirements:


The same Unity version on both machines (check ProjectSettings/ProjectVersion.txt). Install the matching version via Unity Hub.
Git.


Clone and open:

bashgit clone <repo-url>

Then open the cloned folder through Unity Hub with the matching Unity version. The first open is slow while Library/ rebuilds; after that it behaves like a normal project.

Editor settings to confirm (Edit > Project Settings > Editor):


Asset Serialization Mode: Force Text (so scenes and prefabs are diffable).
Version Control Mode: Visible Meta Files (so .meta files are generated and committed; they preserve Inspector wiring such as the placedPrefab and plate references).


Working across two machines

Pull before you start, push when you finish.

bash# before editing on a machine
git pull

# after saving the scene in Unity
git add .
git commit -m "describe what changed"
git push

Save the scene in Unity before committing (unsaved changes are not in the files yet). Pulling while Unity is closed is safest. Sticking to push-then-pull avoids merge conflicts, which are painful for scene and prefab files even as text.

What Git ignores

Excluded via .gitignore (Unity's official template): Library/, Temp/, Obj/, Build/, Logs/, UserSettings/. These regenerate locally. Everything in Assets/, Packages/, and ProjectSettings/ is committed, including .meta files.
