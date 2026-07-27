"""
================================================================================
 VR Bridge — middleware between Jason's tracker and Unity on Quest 3
 P4P #110, Outcome 1
================================================================================

WHAT THIS IS
  The single process that connects the eye tracker (PC) to Unity (Quest 3):

    Jason's tracker ──gaze_vector.txt──►  vr_bridge.py  ◄──UDP──►  Unity/Quest

  - Reads raw gaze from Jason's tracker (file, unchanged tracker).
  - Listens for calibration commands from Unity (UDP).
  - Runs the SAME calibration math as the desktop path (imports calibration.py).
  - Streams gaze back to Unity continuously:
      RAW,x,y   before a calibration exists  (lets Unity verify connectivity)
      GAZE,x,y  after fitting               (the corrected signal for the cursor)

PROTOCOL (text over UDP, one message per datagram)
  Unity -> PC  (to --listen-port)
    PING                     connectivity check
    TARGET,<i>,<x>,<y>       dot i now shown at normalized (x, y), -1..1, y up
    RECORD                   user is fixating the current dot -- sample now
    FIT                      all dots done -- fit the calibration
    RESET                    discard collected pairs, start over
    VALTARGET,<i>,<x>,<y>    validation dot i now shown (needs a fitted model)
    VALRECORD                sample calibrated gaze, score against the val dot
    VALREPORT                summarise validation accuracy, save report
  PC -> Unity  (to --quest-ip:--send-port)
    ACK,<cmd>,<detail>       command succeeded
    ERR,<cmd>,<reason>       command failed
    RAW,<x>,<y>              uncalibrated gaze stream (~50 Hz, before fit)
    GAZE,<x>,<y>             calibrated gaze stream   (~50 Hz, after fit)

  Validation is deliberately separate from TARGET/RECORD: its samples are
  scored against the CURRENT model and never feed a fit, so the reported
  accuracy is honest (measured on targets the fit has never seen).

DESIGN NOTES
  - Unity stays "dumb": it draws dots and relays user confirmation. All
    calibration logic (sampling, IQR outlier rejection, polynomial fit,
    persistence) lives here, sharing one implementation with the desktop path.
  - RECORD samples SAMPLES_PER_TARGET frames and reduces them with the same
    IQR-median used everywhere else, so VR and desktop calibrations are
    directly comparable.
  - A fitted calibration is saved to disk and can be reloaded with --load,
    so the headset can skip recalibration across sessions.

USAGE
  python vr_bridge.py --quest-ip 192.168.1.42            # fresh session
  python vr_bridge.py --quest-ip 192.168.1.42 --load     # reuse saved calibration

  Requires Jason's tracker running in this directory (writing gaze_vector.txt).
  numpy==1.26.0 (see README — newer numpy silently breaks the tracker).
--------------------------------------------------------------------------------
"""

from __future__ import annotations

import os
import sys
import json
import time
import socket
import argparse
import threading

import numpy as np

# calibration.py lives with the desktop path; make it importable no matter
# where this script is run from.
_HERE = os.path.dirname(os.path.abspath(__file__))
_CAL_DIR = os.path.join(_HERE, "jasoncamera", "Eye-tracker-part-4-project", "3DTracker")
for _p in (_HERE, _CAL_DIR):
    if os.path.isdir(_p) and _p not in sys.path:
        sys.path.insert(0, _p)

import calibration as cal


# ── Configuration ─────────────────────────────────────────────────────────────

GAZE_FILE = "gaze_vector.txt"
SAMPLES_PER_TARGET = 30      # frames sampled on each RECORD
SETTLE_FRAMES = 10           # frames discarded first (camera latency)
STREAM_HZ = 50               # gaze stream rate to Unity
MIN_TARGETS_TO_FIT = 6       # degree-2 poly has 6 coefficients per axis
STALE_S = 1.0                # gaze file older than this = tracker dead, not data


# ── Raw gaze input (same defensive read as the desktop path) ──────────────────
#
# Two calibratable signals (dual-input experiment, design note in calibration.py):
#   "gaze"  -> indices 3,4: final 3D gaze direction, DOWNSTREAM of the tracker's
#              hardcoded eye geometry
#   "pupil" -> indices 6,7: raw pupil centre (normalized), UPSTREAM of it —
#              requires the tracker's extended 8-value file write

def read_gaze(signal: str = "gaze"):
    """(x, y) of the chosen signal from gaze_vector.txt, or None if unavailable.
    A file the tracker stopped updating counts as missing — otherwise a dead
    tracker would freeze the stream on its last value and RECORD would happily
    calibrate on it."""
    try:
        if time.time() - os.path.getmtime(GAZE_FILE) > STALE_S:
            return None
        with open(GAZE_FILE) as f:
            vals = [float(v) for v in f.read().strip().split(",")]
    except (OSError, ValueError):
        return None
    if signal == "pupil":
        if len(vals) < 8:
            return None                  # tracker without the extended write
        return vals[6], vals[7]          # pupil centre ndc x, y
    if len(vals) < 6:
        return None
    return vals[3], vals[4]              # gaze direction x, y


# ── The bridge ────────────────────────────────────────────────────────────────

class VRBridge:
    def __init__(self, quest_ip: str, listen_port: int, send_port: int,
                 fov_deg: float, load_existing: bool, signal: str = "gaze"):
        self.unity_addr = (quest_ip, send_port)
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.sock.bind(("0.0.0.0", listen_port))
        self.sock.settimeout(0.2)            # lets the loop check the stop flag

        self.fov_deg = fov_deg
        self.signal = signal                 # which tracker signal to calibrate on
        self.stop = threading.Event()

        # Calibration session state
        self.current_target: tuple[float, float] | None = None
        self.pairs_raw: list[tuple[float, float]] = []
        self.pairs_truth: list[tuple[float, float]] = []
        self.model: cal.Calibration | None = None

        # Validation session state (scored against the model, never fed to a fit)
        self.val_target: tuple[float, float] | None = None
        self.val_errors_deg: list[float] = []

        if load_existing:
            try:
                self.model = cal.load()
                print(f"[bridge] loaded calibration "
                      f"({self.model.n_train} targets, {self.model.input_signal})")
                # The stream must feed the model the signal it was trained on.
                if self.model.input_signal != self.signal:
                    print(f"[bridge] note: loaded model was trained on "
                          f"'{self.model.input_signal}' — overriding --signal "
                          f"'{self.signal}' to match")
                    self.signal = self.model.input_signal
            except FileNotFoundError:
                print("[bridge] --load given but calibration.json not found; "
                      "starting uncalibrated")

    # ── Outbound ──────────────────────────────────────────────────────────

    def send(self, msg: str) -> None:
        self.sock.sendto(msg.encode("utf-8"), self.unity_addr)

    def _no_data_reason(self) -> str:
        """Signal-aware ERR detail: in pupil mode the usual cause is a tracker
        without the extended 8-value write, not a dead tracker."""
        if self.signal == "pupil":
            return ("no pupil data (tracker running? it must write the "
                    "extended 8-value line for --signal pupil)")
        return "no gaze data (is the tracker running?)"

    # ── Command handling (Unity → PC) ─────────────────────────────────────

    def handle(self, msg: str) -> None:
        parts = msg.strip().split(",")
        cmd = parts[0].upper()

        if cmd == "PING":
            self.send("ACK,PING,bridge alive")

        elif cmd == "TARGET":
            try:
                idx, tx, ty = int(parts[1]), float(parts[2]), float(parts[3])
            except (IndexError, ValueError):
                self.send("ERR,TARGET,expected TARGET,<i>,<x>,<y>")
                return
            self.current_target = (tx, ty)
            self.send(f"ACK,TARGET,{idx} at ({tx:+.2f},{ty:+.2f})")
            print(f"[bridge] target {idx}: ({tx:+.2f}, {ty:+.2f})")

        elif cmd == "RECORD":
            if self.current_target is None:
                self.send("ERR,RECORD,no TARGET announced")
                return
            median = self._sample_window()
            if median is None:
                self.send(f"ERR,RECORD,{self._no_data_reason()}")
                return
            self.pairs_raw.append(median)
            self.pairs_truth.append(self.current_target)
            n = len(self.pairs_raw)
            self.send(f"ACK,RECORD,{n} targets collected")
            print(f"[bridge] recorded pair {n}: "
                  f"raw ({median[0]:+.3f},{median[1]:+.3f}) "
                  f"-> target {self.current_target}")

        elif cmd == "FIT":
            n = len(self.pairs_raw)
            if n < MIN_TARGETS_TO_FIT:
                self.send(f"ERR,FIT,need >= {MIN_TARGETS_TO_FIT} targets, have {n}")
                return
            raw = np.array(self.pairs_raw)
            truth = np.array(self.pairs_truth)
            self.model = cal.fit_calibration(raw, truth,
                                             input_signal=self.signal,
                                             fov_deg=self.fov_deg)
            cal.save(self.model)
            report = cal._evaluate(self.model, raw, truth)   # in-sample sanity
            self.send(f"ACK,FIT,{n} targets, in-sample "
                      f"{report['accuracy_deg']}deg")
            print(f"[bridge] fitted on {n} targets "
                  f"(in-sample {report['accuracy_deg']} deg) — saved; "
                  f"stream switches to GAZE")

        elif cmd == "RESET":
            self.pairs_raw.clear()
            self.pairs_truth.clear()
            self.current_target = None
            self.val_target = None
            self.val_errors_deg.clear()
            self.send("ACK,RESET,cleared")
            print("[bridge] session reset")

        # ── Validation: score the fitted model on independent targets ─────
        elif cmd == "VALTARGET":
            if self.model is None:
                self.send("ERR,VALTARGET,no calibration fitted yet")
                return
            try:
                idx, tx, ty = int(parts[1]), float(parts[2]), float(parts[3])
            except (IndexError, ValueError):
                self.send("ERR,VALTARGET,expected VALTARGET,<i>,<x>,<y>")
                return
            self.val_target = (tx, ty)
            self.send(f"ACK,VALTARGET,{idx} at ({tx:+.2f},{ty:+.2f})")
            print(f"[bridge] validation target {idx}: ({tx:+.2f}, {ty:+.2f})")

        elif cmd == "VALRECORD":
            if self.model is None:
                self.send("ERR,VALRECORD,no calibration fitted yet")
                return
            if self.val_target is None:
                self.send("ERR,VALRECORD,no VALTARGET announced")
                return
            median = self._sample_window()
            if median is None:
                self.send(f"ERR,VALRECORD,{self._no_data_reason()}")
                return
            ex, ey = self.model.apply(median[0], median[1])
            tx, ty = self.val_target
            err_deg = (float(np.hypot(ex - tx, ey - ty)) / 2.0) * self.model.fov_deg
            self.val_errors_deg.append(err_deg)
            n = len(self.val_errors_deg)
            self.send(f"ACK,VALRECORD,dot {n}: {err_deg:.2f}deg")
            print(f"[bridge] validation {n}: corrected ({ex:+.3f},{ey:+.3f}) "
                  f"vs target ({tx:+.2f},{ty:+.2f}) -> {err_deg:.2f} deg")

        elif cmd == "VALREPORT":
            if not self.val_errors_deg:
                self.send("ERR,VALREPORT,no validation targets recorded")
                return
            errs = np.array(self.val_errors_deg)
            report = {
                "n_targets": int(len(errs)),
                "accuracy_deg": round(float(errs.mean()), 3),
                "max_error_deg": round(float(errs.max()), 3),
                "precision_deg": round(float(errs.std()), 3),
                "model_n_train": self.model.n_train if self.model else 0,
                "timestamp": time.strftime("%Y-%m-%d %H:%M:%S"),
            }
            with open("validation_report.json", "w") as f:
                json.dump(report, f, indent=2)
            self.send(f"ACK,VALREPORT,{report['n_targets']} targets, "
                      f"accuracy {report['accuracy_deg']}deg, "
                      f"max {report['max_error_deg']}deg, "
                      f"precision {report['precision_deg']}deg")
            print(f"[bridge] validation report: {report} — saved")
            self.val_errors_deg.clear()
            self.val_target = None

        else:
            self.send(f"ERR,{cmd},unknown command")

    def _sample_window(self):
        """Sample the gaze file for ~1 s; IQR-median reduce.
        Mirrors the desktop collection so VR and desktop data are comparable.
        Shared by RECORD (fit data) and VALRECORD (accuracy scoring)."""
        for _ in range(SETTLE_FRAMES):
            read_gaze(self.signal)
            time.sleep(1.0 / STREAM_HZ)
        samples = []
        deadline = time.time() + 5.0          # safety: never block forever
        while len(samples) < SAMPLES_PER_TARGET and time.time() < deadline:
            g = read_gaze(self.signal)
            if g is not None:
                samples.append(g)
            time.sleep(1.0 / STREAM_HZ)
        if not samples:
            return None
        if len(samples) < SAMPLES_PER_TARGET:
            print(f"[bridge] WARNING: only {len(samples)}/{SAMPLES_PER_TARGET} "
                  f"samples before deadline — tracker running slow?")
        return cal._iqr_median(samples)

    # ── Threads ───────────────────────────────────────────────────────────

    def listener(self) -> None:
        """Receive and dispatch commands from Unity."""
        print("[bridge] listening for Unity commands")
        while not self.stop.is_set():
            try:
                data, _ = self.sock.recvfrom(1024)
            except socket.timeout:
                continue
            except OSError:
                break
            try:
                self.handle(data.decode("utf-8"))
            except Exception as e:                     # never let one bad
                print(f"[bridge] handler error: {e}")  # packet kill the bridge

    def streamer(self) -> None:
        """Continuously send gaze to Unity: RAW before fit, GAZE after."""
        period = 1.0 / STREAM_HZ
        while not self.stop.is_set():
            g = read_gaze(self.signal)
            if g is not None:
                if self.model is not None:
                    x, y = self.model.apply(g[0], g[1])
                    self.send(f"GAZE,{x:.4f},{y:.4f}")
                else:
                    self.send(f"RAW,{g[0]:.4f},{g[1]:.4f}")
            time.sleep(period)

    def run(self) -> None:
        t_listen = threading.Thread(target=self.listener, daemon=True)
        t_stream = threading.Thread(target=self.streamer, daemon=True)
        t_listen.start()
        t_stream.start()
        mode = "GAZE (calibrated)" if self.model else "RAW (uncalibrated)"
        print(f"[bridge] running — streaming {mode} to "
              f"{self.unity_addr[0]}:{self.unity_addr[1]}, "
              f"signal={self.signal}  (Ctrl-C to stop)")
        try:
            while True:
                time.sleep(0.5)
        except KeyboardInterrupt:
            print("\n[bridge] stopping")
            self.stop.set()
            self.sock.close()


# ── Main ──────────────────────────────────────────────────────────────────────

def main():
    ap = argparse.ArgumentParser(
        description="UDP middleware between Jason's tracker and Unity on Quest 3.")
    ap.add_argument("--quest-ip", required=True,
                    help="Quest 3's IP address on the shared network")
    ap.add_argument("--listen-port", type=int, default=9100,
                    help="port to receive Unity commands on (default 9100)")
    ap.add_argument("--send-port", type=int, default=9101,
                    help="port on the Quest to send gaze to (default 9101)")
    ap.add_argument("--fov", type=float, default=90.0,
                    help="display FOV in degrees — use the headset's REAL value")
    ap.add_argument("--load", action="store_true",
                    help="load calibration.json and stream GAZE immediately")
    ap.add_argument("--signal", choices=["gaze", "pupil"], default="gaze",
                    help="tracker signal to calibrate on: 'gaze' = 3D direction "
                         "(downstream of the hardcoded geometry), 'pupil' = raw "
                         "pupil centre (upstream; needs the extended file write)")
    args = ap.parse_args()

    bridge = VRBridge(args.quest_ip, args.listen_port, args.send_port,
                      args.fov, args.load, args.signal)
    bridge.run()


if __name__ == "__main__":
    main()