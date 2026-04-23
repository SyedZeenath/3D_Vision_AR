using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Collections;
using System.Collections.Concurrent;
using System.Text;

/// <summary>
/// Listens on UDP port 5000 for packets from pose_benchmark_simple.py.
/// Sends mode-switch via HTTP GET to http://localhost:5006/mode/{0|1|2}
/// </summary>
public class PythonDataReceiver : MonoBehaviour
{
    [Header("Network")]
    public int listenPort = 5000;
    public int httpPort = 5006;
    public string pythonIP = "127.0.0.1";

    [Header("References")]
    public ComparisonDashboard dashboard;

    // thread-safe queues
    readonly ConcurrentQueue<FrameMsg> _frames = new();
    readonly ConcurrentQueue<KpMsg> _keypoints = new();
    readonly ConcurrentQueue<MetricsMsg> _metrics = new();
    readonly ConcurrentQueue<int> _modes = new();

    UdpClient _udp;
    Thread _thread;
    bool _running;
    Texture2D[] _textures;

    struct FrameMsg { public int idx; public byte[] jpeg; }
    struct KpMsg { public int idx; public Vector2[] kps; public float[] conf; public int[] connections; }
    struct MetricsMsg { public int idx; public string json; }

    public const int MODE_MPII = 0;
    public const int MODE_KAGGLE = 1;
    public const int MODE_LIVE = 2;

    void Start()
    {
        _textures = new Texture2D[3];
        for (int i = 0; i < 3; i++)
            _textures[i] = new Texture2D(2, 2, TextureFormat.RGB24, false);

        _udp = new UdpClient(listenPort);
        _running = true;
        _thread = new Thread(ReceiveLoop) { IsBackground = true, Name = "UDPReceive" };
        _thread.Start();

        Debug.Log($"[Receiver] UDP listening on :{listenPort}");
    }

    void Update()
    {
        while (_frames.TryDequeue(out var f))
        {
            if (f.idx < 0 || f.idx > 2) continue;
            _textures[f.idx].LoadImage(f.jpeg);
            dashboard?.OnFrameReceived(f.idx, _textures[f.idx]);
        }

        while (_keypoints.TryDequeue(out var k))
            dashboard?.OnKeypointsReceived(k.idx, k.kps, k.conf, k.connections);

        while (_metrics.TryDequeue(out var m))
        {
            try { dashboard?.OnMetricsReceived(m.idx, PoseMetrics.FromJson(m.json)); }
            catch (Exception e) { Debug.LogWarning($"[Receiver] Metrics parse: {e.Message}"); }
        }

        while (_modes.TryDequeue(out var mode))
        {
            Debug.Log($"[Receiver] Python confirmed mode: {mode}");
            dashboard?.OnModeChanged(mode);
        }
    }

    void OnDestroy()
    {
        _running = false;
        _udp?.Close();
        _thread?.Join(300);
    }

    // ****** HTTP mode switch ******
    // Unity -> Python via HTTP GET
    public void SelectMPII() => StartCoroutine(SendModeHttp(MODE_MPII));
    public void SelectKaggle() => StartCoroutine(SendModeHttp(MODE_KAGGLE));
    public void SelectLive() => StartCoroutine(SendModeHttp(MODE_LIVE));

    public void SendMode(int mode) => StartCoroutine(SendModeHttp(mode));

    IEnumerator SendModeHttp(int mode)
    {
        string url = $"http://{pythonIP}:{httpPort}/mode/{mode}";
        Debug.Log($"[Receiver] HTTP GET {url}");

        using var req = UnityWebRequest.Get(url);
        req.timeout = 3;
        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success)
            Debug.Log($"[Receiver] Mode {mode} confirmed: {req.downloadHandler.text}");
        else
            Debug.LogWarning($"[Receiver] HTTP failed: {req.error}");
    }

    // ****** UDP receive thread ******
    void ReceiveLoop()
    {
        var remote = new IPEndPoint(IPAddress.Any, 0);
        while (_running)
        {
            try
            {
                byte[] data = _udp.Receive(ref remote);
                if (data.Length < 5) continue;

                string magic = Encoding.ASCII.GetString(data, 0, 4);
                int idx = data[4];

                switch (magic)
                {
                    case "FRME": ParseFrame(data, idx); break;
                    case "KEYS": ParseKeypoints(data, idx); break;
                    case "METR": ParseMetrics(data, idx); break;
                    case "MODE": _modes.Enqueue(data[4]); break;
                }
            }
            catch (SocketException) { break; }
            catch (Exception e) { if (_running) Debug.LogWarning($"[Receiver] {e.Message}"); }
        }
    }

    void ParseFrame(byte[] data, int idx)
    {
        if (data.Length < 9) return;
        int len = BitConverter.ToInt32(data, 5);
        if (data.Length < 9 + len) return;
        var jpeg = new byte[len];
        Buffer.BlockCopy(data, 9, jpeg, 0, len);
        _frames.Enqueue(new FrameMsg { idx = idx, jpeg = jpeg });
    }

    void ParseKeypoints(byte[] data, int idx)
    {
        int off = 5;
        if (data.Length < off + 2) return;
        int nKps = BitConverter.ToUInt16(data, off); off += 2;
        if (data.Length < off + nKps * 12) return;

        var kps = new Vector2[nKps];
        var conf = new float[nKps];
        for (int i = 0; i < nKps; i++)
        {
            float x = BitConverter.ToSingle(data, off); off += 4;
            float y = BitConverter.ToSingle(data, off); off += 4;
            conf[i] = BitConverter.ToSingle(data, off); off += 4;
            kps[i] = new Vector2(x, y);
        }

        if (data.Length < off + 2) return;
        int nPairs = BitConverter.ToUInt16(data, off); off += 2;
        if (data.Length < off + nPairs * 4) return;

        var conn = new int[nPairs * 2];
        for (int i = 0; i < nPairs * 2; i++)
        {
            conn[i] = BitConverter.ToInt16(data, off); off += 2;
        }
        _keypoints.Enqueue(new KpMsg { idx = idx, kps = kps, conf = conf, connections = conn });
    }

    void ParseMetrics(byte[] data, int idx)
    {
        if (data.Length < 6) return;
        string json = Encoding.UTF8.GetString(data, 5, data.Length - 5);
        _metrics.Enqueue(new MetricsMsg { idx = idx, json = json });
    }
}