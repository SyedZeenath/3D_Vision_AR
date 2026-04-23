// Drives one model column: header, video, skeleton, metrics, score footer.
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ModelPanelUI : MonoBehaviour
{
    [Header("Identity")]
    public string modelName = "MediaPipe";
    public Color accentColor = new Color(0.29f, 0.86f, 0.50f);

    [Header("Header")]
    public TextMeshProUGUI modelNameLabel;
    public GameObject bestBadge;
    public TextMeshProUGUI rankLabel; // text inside BestBadge, reused for rank

    [Header("Video")]
    public RawImage videoDisplay;
    public TextMeshProUGUI fpsChip;
    public TextMeshProUGUI sourceChip;

    [Header("Skeleton")]
    public SkeletonOverlayRenderer skeletonOverlay;

    [Header("Metrics - assign in order: MAE, PCKh, FPS, Jitter, Occlusion")]
    public MetricRowUI[] metricRows = new MetricRowUI[5];

    [Header("Footer")]
    public TextMeshProUGUI overallScoreLabel;

    [Header("Border")]
    public Image panelBorderImage;
    private Outline panelOutline;

    static readonly string[] MetricNames = { "Joint angle Error", "PCKh", "FPS", "Skeleton Stability", "Occlusion" };

    // Weights must sum to 1.0
    static readonly float[] Weights = { 0.30f, 0.25f, 0.15f, 0.20f, 0.10f };

    // true = lower raw value is better (inverts bar direction)
    static readonly bool[] LowerIsBetter = { true, false, false, true, false };

    // Reference ranges for normalisation [worst, best]
    static readonly float[] WorstRef = { 180f, 0f, 1f, 30f, 0f };
    static readonly float[] BestRef = { 0f, 100f, 30f, 0f, 100f };

    PoseMetrics _metrics;
    bool _isBest;

    static readonly Color BorderDefault = new Color(0.12f, 0.14f, 0.20f, 1f);
    static readonly Color BorderBest = new Color(0.13f, 0.53f, 0.20f, 1f);

    void Start()
    {
        if (modelNameLabel != null) modelNameLabel.text = modelName;
        if (bestBadge != null) bestBadge.SetActive(false);

        // Initialise metric rows with names and accent colour
        string[] names = MetricNames;
        for (int i = 0; i < metricRows.Length && i < names.Length; i++)
        {
            metricRows[i]?.Init(names[i], accentColor);
        }
    }

    void Awake()
    {
        if (panelBorderImage != null) panelOutline = panelBorderImage.GetComponent<Outline>();
    }

    public void SetFrame(Texture2D tex)
    {
        if (videoDisplay != null) videoDisplay.texture = tex;
    }

    public void SetKeypoints(Vector2[] kps, float[] conf, int[] connections)
    {
        if (skeletonOverlay != null) skeletonOverlay.UpdateSkeleton(kps, conf, connections);
    }

    public void SetMetrics(PoseMetrics m)
    {
        _metrics = m;
        UpdateMetricRows(m);
        UpdateScore(m);
        if (fpsChip != null) fpsChip.text = $"{m.fps:F0} fps";
    }

    public void SetSource(string src)
    {
        if (sourceChip != null) sourceChip.text = src;
    }

    public void SetBest(bool isBest)
    {
        _isBest = isBest;
        if (bestBadge != null) bestBadge.SetActive(isBest);

        if (panelOutline != null) panelOutline.effectColor = isBest ? BorderBest : BorderDefault;

        // When best, show "Best overall" text; rank label hidden
        if (rankLabel != null && isBest)
        {
            rankLabel.text = "Best overall";
            rankLabel.color = accentColor;
        }
    }

    public void SetRank(int rank)
    {
        if (_isBest) return;
        if (bestBadge != null) bestBadge.SetActive(true); // show as rank badge
        if (rankLabel != null)
        {
            rankLabel.text = rank == 2 ? "2nd" : "3rd";
            rankLabel.color = new Color(0.61f, 0.64f, 0.69f); // muted gray
        }
    }

    public float GetCompositeScore()
    {
        return _metrics == null ? 0f : ComputeScore(_metrics);
    }

    void UpdateMetricRows(PoseMetrics m)
    {
        // Joint angle range depends on mode
        float jointWorst = m.hasGT ? 30f : 180f; // Mean Angle Error(MAE) for MPII, mean angle for live
        float jointBest = m.hasGT ? 0f : 90f; // 0=perfect MAE, 90=mid-range angle

        float[] worst = { jointWorst, 0f, 1f, 30f, 0f };
        float[] best = { jointBest, 100f, 30f, 0f, 100f };

        float[] raw = {
            m.jointAngleAccuracy, m.pckh, m.fps,
            m.interFrameJitter, m.occlusionDetectionRate
        };
        string[] formatted = {
            m.hasGT ? $"{m.jointAngleAccuracy:F1}°" : $"{m.jointAngleAccuracy:F1}°",
            $"{m.pckh:F0}%",
            $"{m.fps:F0}",
            $"{m.interFrameJitter:F1}px",
            $"{m.occlusionDetectionRate:F0}%"
        };

        for (int i = 0; i < metricRows.Length && i < raw.Length; i++)
        {
            if (metricRows[i] == null) continue;
            float goodness = Mathf.Clamp01(Mathf.InverseLerp(worst[i], best[i], raw[i]));
            metricRows[i].SetValue(formatted[i], goodness);
        }
    }

    void UpdateScore(PoseMetrics m)
    {
        float score = ComputeScore(m);
        if (overallScoreLabel != null)
        {
            overallScoreLabel.text = $"{score:F0} / 100";
            overallScoreLabel.color = accentColor;
        }
    }

    float ComputeScore(PoseMetrics m)
    {
        float[] raw = {
            m.jointAngleAccuracy, m.pckh, m.fps,
            m.interFrameJitter, m.occlusionDetectionRate
        };
        float total = 0f;
        for (int i = 0; i < 5; i++)
            total += Goodness(raw[i], i) * Weights[i] * 100f;
        return Mathf.Clamp(total, 0f, 100f);
    }

    // Returns 0 (worst) -> 1 (best) for metric index i
    float Goodness(float value, int i)
    {
        return Mathf.Clamp01(Mathf.InverseLerp(WorstRef[i], BestRef[i], value));
    }

    public void ClearSkeleton()
    {
        if (skeletonOverlay != null)
            skeletonOverlay.ClearSkeleton();
    }
}
