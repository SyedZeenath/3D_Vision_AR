// Main controller for the pose estimation comparison dashboard.
// Handles UI interactions, receives data from Python, and manages MPII benchmark display.
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class ComparisonDashboard : MonoBehaviour
{
    // Assign these in the Unity Editor by dragging the corresponding UI elements
    [Header("Panels")]
    public ModelPanelUI[] panels = new ModelPanelUI[3];
    public GraphPanel graphPanel;

    [Header("Source Buttons")]
    public Button btnLive;
    public Button btnKaggle;

    [Header("MPII Benchmark Button")]
    public Button btnMPII;
    public TextMeshProUGUI mpiiStatusLabel = null;

    [Header("Python HTTP")]
    public string pythonIP = "127.0.0.1";
    public int pythonPort = 5006; // Must match the port used by the Python server

    [Header("MPII Results JSON path (relative to StreamingAssets)")]
    public string mpiiJsonPath = "mpii_results.json";

    public string mpiiImagePath = "mpii_sample.jpg"; // image shown when MPII clicked

    static readonly Color ActiveBtn = new Color(0.20f, 0.60f, 0.20f, 1f);
    static readonly Color InactiveBtn = new Color(0.16f, 0.19f, 0.25f, 1f);

    // Cached MPII results loaded from JSON
    PoseMetrics[] _mpiiMetrics; // index 0=MediaPipe, 1=YOLO, 2=OpenPose
    Texture2D _mpiiTexture;
    bool _showingMPII = false;

    void Start()
    {
        if (btnLive != null) btnLive.onClick.AddListener(OnLiveClicked);
        if (btnKaggle != null) btnKaggle.onClick.AddListener(OnKaggleClicked);
        if (btnMPII != null) btnMPII.onClick.AddListener(OnMPIIClicked);
        if (mpiiStatusLabel != null) mpiiStatusLabel.text = "";

        SetActiveButton(btnLive);
        LoadMPIIResults();
        StartCoroutine(LoadMPIIImage());
    }

    // ****** button handlers ******
    void OnLiveClicked()
    {
        _showingMPII = false;
        SetActiveButton(btnLive);
        SetStatusLabel("");
        StartCoroutine(SendMode(2));
        graphPanel?.ResetHistory();
    }

    void OnKaggleClicked()
    {
        _showingMPII = false;
        SetActiveButton(btnKaggle);
        SetStatusLabel("");
        StartCoroutine(SendMode(1));
        graphPanel?.ResetHistory();
    }

    void OnMPIIClicked()
    {
        if (_mpiiMetrics == null)
        {
            Debug.LogWarning("MPII results not loaded. Run mpii evaluation first.");
            SetStatusLabel("Run mpii evaluation first!");
            return;
        }
        Debug.Log("Displaying MPII benchmark results.");
        _showingMPII = true;
        SetActiveButton(btnMPII);
        SetStatusLabel("Showing MPII Benchmark Results");
        StartCoroutine(SendMode(0)); // 0 = MODE_MPII

        // Push pre-computed metrics to each panel; Because the MPII has no video feed, the panels will display 
        // these static metrics computed on the images instead of waiting for Python updates
        for (int i = 0; i < panels.Length && i < _mpiiMetrics.Length; i++)
            if (panels[i] != null && _mpiiMetrics[i] != null)
                panels[i].SetMetrics(_mpiiMetrics[i]);

        RankPanels();
    }

    IEnumerator LoadMPIIImage()
    {
        string fullPath = System.IO.Path.Combine(Application.streamingAssetsPath, mpiiImagePath);

        // Use file:/// on standalone, direct path on editor
        // Because the MPII has no video feed, the panels will display 
        // a default static image picked from the MPII dataset instead of waiting for Python updates
        string url = fullPath;
        if (!fullPath.StartsWith("http"))
            url = "file:///" + fullPath.Replace("\\", "/");

        Debug.Log($"[Dashboard] Loading MPII image from: {url}");

        using var req = UnityWebRequestTexture.GetTexture(url);
        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success)
        {
            _mpiiTexture = DownloadHandlerTexture.GetContent(req);
            Debug.Log("[Dashboard] MPII image loaded successfully.");

            // If MPII panel is already showing, update it now
            if (_showingMPII)
                foreach (var p in panels)
                    p?.SetFrame(_mpiiTexture);
        }
        else
        {
            // Fallback — try loading directly with Texture2D
            Debug.LogWarning($"[Dashboard] WebRequest failed ({req.error}), trying direct load...");
            byte[] fileData = System.IO.File.ReadAllBytes(fullPath);
            _mpiiTexture = new Texture2D(2, 2);
            if (_mpiiTexture.LoadImage(fileData))
            {
                Debug.Log("[Dashboard] MPII image loaded via direct file read.");
                if (_showingMPII)
                    foreach (var p in panels)
                        p?.SetFrame(_mpiiTexture);
            }
            else
            {
                Debug.LogError($"[Dashboard] Could not load MPII image from {fullPath}. " + "Make sure mpii_sample.jpg is in Assets/StreamingAssets/");
            }
        }
    }

    // ****** receive from Python (only applied when NOT showing MPII) ******
    public void OnFrameReceived(int modelIndex, Texture2D frame)
    {
        if (!Valid(modelIndex)) return;
        panels[modelIndex].SetFrame(frame);
    }

    public void OnKeypointsReceived(int modelIndex, Vector2[] kps, float[] conf, int[] connections)
    {
        if (!Valid(modelIndex)) return;
        panels[modelIndex].SetKeypoints(kps, conf, connections);
    }

    public void OnMetricsReceived(int modelIndex, PoseMetrics metrics)
    {
        if (_showingMPII) return; // freeze display while showing MPII results
        if (!Valid(modelIndex)) return;
        panels[modelIndex].SetMetrics(metrics);
        graphPanel?.FeedMetrics(modelIndex, metrics);
        RankPanels();
    }

    public void OnModeChanged(int mode)
    {
        string[] labels = { "MPII", "Kaggle", "Live" };
        Debug.Log($"[Dashboard] Python confirmed mode: {labels[mode]}");
    }

    // ****** MPII JSON loader *******
    void LoadMPIIResults()
    {
        string path = System.IO.Path.Combine(Application.streamingAssetsPath, mpiiJsonPath);

        if (!System.IO.File.Exists(path))
        {
            Debug.LogWarning($"[Dashboard] MPII results not found at {path}. " + "Run mpii_eval.py and copy results/mpii_results.json " 
                            + "to Assets/StreamingAssets/");
            return;
        }
        try
        {
            string json = System.IO.File.ReadAllText(path);
            // Parse manually — each key is model name and value is metrics dict
            // Expected: {"MediaPipe":{...}, "YOLOv26":{...}, "OpenPose":{...}}
            _mpiiMetrics = new PoseMetrics[3];

            string[] modelKeys = { "MediaPipe", "YOLOv26", "OpenPose" };
            for (int i = 0; i < modelKeys.Length; i++)
            {
                var m = ParseModelMetrics(json, modelKeys[i]);
                if (m != null)
                {
                    m.hasGT = true; // MPII has ground truth
                    _mpiiMetrics[i] = m;
                }
            }
            Debug.Log("[Dashboard] MPII results loaded successfully.");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Dashboard] Failed to load MPII results: {e.Message}");
        }
    }

    // ****** Simple JSON field extractor ******
    PoseMetrics ParseModelMetrics(string json, string modelKey)
    {
        int start = json.IndexOf($"\"{modelKey}\"");
        if (start < 0) return null;

        int braceOpen = json.IndexOf('{', start);
        int braceClose = json.IndexOf('}', braceOpen);
        if (braceOpen < 0 || braceClose < 0) return null;

        string block = json.Substring(braceOpen, braceClose - braceOpen + 1);

        return new PoseMetrics
        {
            jointAngleAccuracy = ExtractFloat(block, "joint_angle"),
            pckh = ExtractFloat(block, "pckh"),
            fps = ExtractFloat(block, "fps"),
            interFrameJitter = ExtractFloat(block, "jitter"),
            occlusionDetectionRate = ExtractFloat(block, "occlusion"),
            hasGT = true,
        };
    }

    float ExtractFloat(string json, string key)
    {
        int idx = json.IndexOf($"\"{key}\"");
        if (idx < 0) return 0f;
        int colon = json.IndexOf(':', idx);
        if (colon < 0) return 0f;
        int end = json.IndexOfAny(new char[]{',','}'}, colon);
        string val = json.Substring(colon + 1, end - colon - 1).Trim();
        return float.TryParse(val, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float f) ? f : 0f;
    }

    // ****** helpers *******
    IEnumerator SendMode(int mode)
    {
        string url = $"http://{pythonIP}:{pythonPort}/mode/{mode}";
        using var req = UnityWebRequest.Get(url);
        req.timeout = 3;
        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success)
            Debug.Log($"[Dashboard] Mode {mode} confirmed");
        else
            Debug.LogError($"[Dashboard] HTTP failed: {req.error}");
    }

    void SetActiveButton(Button active)
    {
        foreach (var btn in new[] { btnLive, btnKaggle, btnMPII })
        {
            if (btn == null) continue;
            var img = btn.GetComponent<Image>();
            if (img != null)
                img.color = (btn == active) ? ActiveBtn : InactiveBtn;
        }
    }

    void SetStatusLabel(string text)
    {
        if (mpiiStatusLabel != null)
            mpiiStatusLabel.text = text;
    }

    void RankPanels()
    {
        var ranked = panels.Select((p, i) => (panel: p, score: p.GetCompositeScore())).OrderByDescending(x => x.score).ToList();

        for (int r = 0; r < ranked.Count; r++)
        {
            ranked[r].panel.SetBest(r == 0);
            if (r > 0) ranked[r].panel.SetRank(r + 1);
        }
    }

    bool Valid(int i) => i >= 0 && i < panels.Length && panels[i] != null;
}