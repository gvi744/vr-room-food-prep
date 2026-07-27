// UnityGazeBridge.cs — receiver/sender for vr_bridge.py.
// See INTERFACE_SPEC.md for the protocol.
//
// Under Meta Horizon Link, Unity runs on the same PC as vr_bridge.py, so
// pcIp is 127.0.0.1 (loopback), not the Quest's WiFi address.
//
// Receives RAW (uncalibrated) / GAZE (calibrated) into Gaze, both normalized
// -1..1, y up. GAZE is the corrected screen position from your polynomial.
// SendTarget/SendRecord/SendFit/SendReset drive the 9-point calibration.

using System;
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
    private volatile bool running;

    void Start()
    {
        tx = new UdpClient();
        rx = new UdpClient(listenPort);
        running = true;
        rxThread = new Thread(ReceiveLoop) { IsBackground = true };
        rxThread.Start();
        Send("PING");   // connectivity smoke test; expect ACK,PING in the log
    }

    void ReceiveLoop()
    {
        var ep = new IPEndPoint(IPAddress.Any, listenPort);
        while (running)
        {
            try
            {
                var data = rx.Receive(ref ep);
                var msg = Encoding.UTF8.GetString(data);
                var p = msg.Split(',');
                switch (p[0])
                {
                    case "GAZE":
                        SetGaze(Parse(p[1]), Parse(p[2]), true);
                        break;
                    case "RAW":
                        SetGaze(Parse(p[1]), Parse(p[2]), false);
                        break;
                    case "ACK":
                    case "ERR":
                        if (p.Length > 1)
                        {
                            bool ok = p[0] == "ACK";
                            if (p[1] == "RECORD")    { if (ok) recordAcks++;    else recordErrs++;    }
                            if (p[1] == "FIT")       { if (ok) fitAcks++;       else fitErrs++;       }
                            if (p[1] == "VALRECORD") { if (ok) valRecordAcks++; else valRecordErrs++; }
                            if (p[1] == "VALREPORT")
                            {
                                if (ok) { LastValReport = msg.Substring("ACK,VALREPORT,".Length); valReportAcks++; }
                                else valReportErrs++;
                            }
                        }
                        Debug.Log("[bridge] " + msg);
                        break;
                }
            }
            catch (SocketException) { /* timeout or close */ }
            catch (ObjectDisposedException) { break; /* socket closed on shutdown */ }
            catch (FormatException)  { /* half-written packet; skip */ }
        }
    }

    private static float Parse(string s) =>
        float.Parse(s, CultureInfo.InvariantCulture);   // bridge always sends "." decimals

    private void SetGaze(float x, float y, bool cal)
    {
        lock (gazeLock) { gaze = new Vector2(x, y); calibrated = cal; }
    }

    // ── Calibration commands ────────────────────────────────────────────────
    public void SendTarget(int index, float x, float y) =>
        Send($"TARGET,{index},{x.ToString("F3", CultureInfo.InvariantCulture)}," +
             $"{y.ToString("F3", CultureInfo.InvariantCulture)}");
    public void SendRecord() => Send("RECORD");
    public void SendFit()    => Send("FIT");
    public void SendReset()  => Send("RESET");

    // ── Validation commands (score the fit on independent targets) ──────────
    public void SendValTarget(int index, float x, float y) =>
        Send($"VALTARGET,{index},{x.ToString("F3", CultureInfo.InvariantCulture)}," +
             $"{y.ToString("F3", CultureInfo.InvariantCulture)}");
    public void SendValRecord() => Send("VALRECORD");
    public void SendValReport() => Send("VALREPORT");

    void Send(string msg)
    {
        var bytes = Encoding.UTF8.GetBytes(msg);
        tx.Send(bytes, bytes.Length, pcIp, sendPort);
    }

    void OnDisable()
    {
        running = false;
        try { rx?.Close(); } catch { }
        try { tx?.Close(); } catch { }
    }

    void Update()
{
    if (Time.frameCount % 60 == 0)
        Debug.Log($"[bridge] gaze={Gaze} calibrated={Calibrated}");
    }
}