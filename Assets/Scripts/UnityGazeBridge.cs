// UnityGazeBridge.cs — receiver/sender for vr_bridge.py.
// See INTERFACE_SPEC.md for the protocol.
//
// Under Meta Horizon Link, Unity runs on the same PC as vr_bridge.py, so
// pcIp is 127.0.0.1 (loopback), not the Quest's WiFi address.
//
// Receives GAZE2 (combined, left and right calibrated coordinates), INVALID,
// and geometry ACK/ERR. RAW/GAZE remain supported for legacy protocol clients.
// SendTarget/SendRecord/SendFit/SendReset drive the 9-point calibration.

using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class UnityGazeBridge : MonoBehaviour
{
    [Header("Network")]
    public string pcIp = "127.0.0.1";   // vr_bridge.py, same machine under Horizon Link
    public int sendPort = 9100;         // the bridge listens here
    public int listenPort = 9101;       // we listen here

    // Latest gaze state, written on the receive thread, read on the main thread.
    private Vector2 gaze;               // -1..1, y up
    private bool calibrated;            // false while the stream is still RAW
    private readonly object gazeLock = new();
    private Vector2 leftGaze, rightGaze;
    private long receivedAt;
    private bool stereoValid;
    public volatile bool GeometryVerified;
    private static double NowSeconds =>
        System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
    public bool HasFreshGaze
    {
        get { lock (gazeLock) { return calibrated && receivedAt != 0 &&
            NowSeconds - receivedAt / (double)System.Diagnostics.Stopwatch.Frequency < 0.5; } }
    }
    public Vector2 LeftGaze { get { lock (gazeLock) { return leftGaze; } } }
    public Vector2 RightGaze { get { lock (gazeLock) { return rightGaze; } } }
    public bool StereoValid { get { lock (gazeLock) { return stereoValid && HasFreshGaze; } } }

    public Vector2 Gaze      { get { lock (gazeLock) { return gaze; } } }
    public bool    Calibrated { get { lock (gazeLock) { return calibrated; } } }

    // ACK/ERR tallies per command, so CalibrationDriver can wait for the
    // bridge's actual response instead of a blind timer. Written on the
    // receive thread, read on the main thread.
    private volatile int recordAcks, recordErrs, fitAcks, fitErrs;
    private volatile int valRecordAcks, valRecordErrs, valReportAcks, valReportErrs;
    public int RecordAcks    => recordAcks;
    public int RecordErrs    => recordErrs;
    public int FitAcks       => fitAcks;
    public int FitErrs       => fitErrs;
    public int ValRecordAcks => valRecordAcks;
    public int ValRecordErrs => valRecordErrs;
    public int ValReportAcks => valReportAcks;
    public int ValReportErrs => valReportErrs;

    // Last ACK,VALREPORT detail ("8 targets, accuracy 1.2deg, ..."), for UI/logs.
    public volatile string LastValReport = "";

    private UdpClient rx;
    private UdpClient tx;
    private Thread rxThread;
    private Thread txThread;
    private readonly ConcurrentQueue<byte[]> outgoing = new();
    private readonly ConcurrentQueue<string> notices = new();
    private readonly AutoResetEvent sendReady = new(false);
    private volatile bool running;

    void OnEnable()
    {
        // Resolve a numeric address once, never a hostname on the render thread.
        if (!IPAddress.TryParse(pcIp, out IPAddress address))
        {
            Debug.LogError("UnityGazeBridge: pcIp must be a numeric IP address, e.g. 127.0.0.1");
            enabled = false;
            return;
        }
        var destination = new IPEndPoint(address, sendPort);
        try
        {
            tx = new UdpClient(address.AddressFamily);
            tx.Client.SendTimeout = 250;
            rx = new UdpClient(listenPort);
        }
        catch (SocketException e)
        {
            Debug.LogError("UnityGazeBridge: cannot open UDP ports: " + e.Message);
            enabled = false;
            return;
        }
        GeometryVerified = false;
        running = true;
        UdpClient receiver = rx, sender = tx;
        rxThread = new Thread(() => ReceiveLoop(receiver)) { IsBackground = true };
        txThread = new Thread(() => SendLoop(sender, destination)) { IsBackground = true };
        rxThread.Start();
        txThread.Start();
        Send("PING");   // connectivity smoke test; expect ACK,PING in the log
    }

    void SendLoop(UdpClient sender, IPEndPoint destination)
    {
        while (running && sender == tx)
        {
            sendReady.WaitOne(100);
            while (running && sender == tx && outgoing.TryDequeue(out byte[] data))
            {
                try { sender.Send(data, data.Length, destination); }
                catch (ObjectDisposedException) { return; }
                catch (SocketException e) { Notice("[bridge] send failed: " + e.Message); }
            }
        }
    }

    void Notice(string message)
    {
        if (notices.Count < 64) notices.Enqueue(message);
    }

    void ReceiveLoop(UdpClient receiver)
    {
        var ep = new IPEndPoint(IPAddress.Any, listenPort);
        while (running && receiver == rx)
        {
            try
            {
                var data = receiver.Receive(ref ep);
                var msg = Encoding.UTF8.GetString(data);
                var p = msg.Split(',');
                switch (p[0])
                {
                    case "GAZE2":
                        if (p.Length != 7) break;
                        Vector2 fused = new(Parse(p[1]), Parse(p[2]));
                        Vector2 left = new(Parse(p[3]), Parse(p[4]));
                        Vector2 right = new(Parse(p[5]), Parse(p[6]));
                        lock (gazeLock)
                        {
                            gaze = fused; leftGaze = left; rightGaze = right;
                            calibrated = stereoValid = true;
                            receivedAt = System.Diagnostics.Stopwatch.GetTimestamp();
                        }
                        break;
                    case "INVALID":
                        lock (gazeLock) { calibrated = stereoValid = false; receivedAt = 0; }
                        break;
                    case "GAZE":
                        SetGaze(Parse(p[1]), Parse(p[2]), true);
                        break;
                    case "RAW":
                        SetGaze(Parse(p[1]), Parse(p[2]), false);
                        break;
                    case "ACK":
                    case "ERR":
                        bool logResponse = true;
                        if (p.Length > 1)
                        {
                            bool ok = p[0] == "ACK";
                            if (p[1] == "GEOMETRY")
                            {
                                logResponse = GeometryVerified != ok || !ok;
                                GeometryVerified = ok;
                            }
                            if (p[1] == "RECORD")    { if (ok) recordAcks++;    else recordErrs++;    }
                            if (p[1] == "FIT")       { if (ok) fitAcks++;       else fitErrs++;       }
                            if (p[1] == "VALRECORD") { if (ok) valRecordAcks++; else valRecordErrs++; }
                            if (p[1] == "VALREPORT")
                            {
                                if (ok && p.Length >= 3) { LastValReport = msg.Substring("ACK,VALREPORT,".Length); valReportAcks++; }
                                else valReportErrs++;
                            }
                        }
                        if (logResponse) Notice("[bridge] " + msg);
                        break;
                }
            }
            catch (SocketException) { /* timeout or close */ }
            catch (ObjectDisposedException) { break; /* socket closed on shutdown */ }
            catch (FormatException)  { /* half-written packet; skip */ }
            catch (IndexOutOfRangeException) { /* malformed packet; skip */ }
            catch (OverflowException) { /* malformed number; skip */ }
        }
    }

    private static float Parse(string s)
    {
        float value = float.Parse(s, CultureInfo.InvariantCulture);
        if (float.IsNaN(value) || float.IsInfinity(value)) throw new FormatException("Nonfinite gaze");
        return value;
    }

    private void SetGaze(float x, float y, bool cal)
    {
        lock (gazeLock)
        {
            gaze = new Vector2(x, y); calibrated = cal; stereoValid = false;
            receivedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        }
    }

    // ── Calibration commands ────────────────────────────────────────────────
    public void SendTarget(int index, float x, float y) =>
        Send($"TARGET,{index},{x.ToString("F3", CultureInfo.InvariantCulture)}," +
             $"{y.ToString("F3", CultureInfo.InvariantCulture)}");
    public void SendRecord() => Send("RECORD");
    public void SendFit()    => Send("FIT");
    public void SendReset()  => Send("RESET");
    public void SendGeometry(float fovX, float fovY, float distanceM, float ipdMm) =>
        Send(string.Format(CultureInfo.InvariantCulture, "GEOMETRY,{0:F6},{1:F6},{2:F6},{3:F6}",
                           fovX, fovY, distanceM, ipdMm));

    // ── Validation commands (score the fit on independent targets) ──────────
    public void SendValTarget(int index, float x, float y) =>
        Send($"VALTARGET,{index},{x.ToString("F3", CultureInfo.InvariantCulture)}," +
             $"{y.ToString("F3", CultureInfo.InvariantCulture)}");
    public void SendValRecord() => Send("VALRECORD");
    public void SendValReport() => Send("VALREPORT");

    void Send(string msg)
    {
        if (!running) return;
        // Enqueue only: socket sends and any OS wait happen on the worker.
        outgoing.Enqueue(Encoding.UTF8.GetBytes(msg));
        sendReady.Set();
    }

    void OnDisable()
    {
        running = false;
        GeometryVerified = false;
        lock (gazeLock) { calibrated = stereoValid = false; receivedAt = 0; }
        sendReady.Set();
        try { rx?.Close(); } catch { }
        try { tx?.Close(); } catch { }
        rxThread?.Join(100);
        txThread?.Join(100);
        while (outgoing.TryDequeue(out _)) { }
    }

    void Update()
    {
        // Do not write per-frame gaze logs or repeated successful heartbeats.
        for (int i = 0; i < 4 && notices.TryDequeue(out string message); i++)
            Debug.Log(message);
    }
}
