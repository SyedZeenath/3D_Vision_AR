using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;

public class GraphPanel : MonoBehaviour
{
    // ****** Inspector refs ******
    [Header("Toggle")]
    public Button btnToggle; // "Graphs" button in TopBar
    public GameObject panelContainer; // the existing 3-panel container to hide

    [Header("Bar chart — one RectTransform bar per model per metric")]
    [Tooltip("5 groups x 3 bars each. Assign in order: metric0_mp, metric0_yolo, metric0_op, metric1_mp...")]
    public RectTransform[] barRects = new RectTransform[15]; // 5 metrics × 3 models
    public TextMeshProUGUI[] barValueLabels = new TextMeshProUGUI[15];
    public TextMeshProUGUI[] metricTitleLabels = new TextMeshProUGUI[5];

    [Header("Line charts")]
    public RawImage fpsLineImage;
    public RawImage jitterLineImage;

    [Header("Radar chart")]
    public RawImage radarImage;

    [Header("Appearance")]
    public Color colorMediaPipe = new Color(0.30f, 0.69f, 0.31f);
    public Color colorYOLO = new Color(1.00f, 0.60f, 0.00f);
    public Color colorOpenPose = new Color(0.96f, 0.26f, 0.21f);

    // ****** constants ******
    static readonly string[] MetricKeys = { "Joint Angle", "PCKh", "FPS", "Jitter", "Occlusion" };

    // Max expected values for bar height normalisation
    static readonly float[] MaxVal = { 30f, 100f, 40f, 20f, 100f };

    const int HISTORY = 80;
    const float BAR_MAX_H = 120f; // max bar height

    // ****** state ******
    bool _visible = false;

    // Per-model metric history [modelIdx][metricIdx]
    readonly Queue<float>[][] _history = new Queue<float>[3][];

    // Latest snapshot per model
    readonly PoseMetrics[] _latest = new PoseMetrics[3];

    // Chart texture cache
    Texture2D _fpsTex;
    Texture2D _jitterTex;
    Texture2D _radarTex;

    static readonly Color[] ModelColors = new Color[3];

    void Awake()
    {
        ModelColors[0] = colorMediaPipe;
        ModelColors[1] = colorYOLO;
        ModelColors[2] = colorOpenPose;

        for (int m = 0; m < 3; m++)
        {
            _history[m] = new Queue<float>[5];
            for (int k = 0; k < 5; k++)
                _history[m][k] = new Queue<float>();
        }

        gameObject.SetActive(false); // hidden by default

        if (btnToggle != null)
            btnToggle.onClick.AddListener(Toggle);
    }

    // ****** public API - called by ComparisonDashboard ******
    public void FeedMetrics(int modelIdx, PoseMetrics m)
    {
        if (modelIdx < 0 || modelIdx > 2) return;
        _latest[modelIdx] = m;

        float[] vals = {
            m.jointAngleAccuracy, m.pckh, m.fps,
            m.interFrameJitter, m.occlusionDetectionRate
        };

        for (int k = 0; k < 5; k++)
        {
            _history[modelIdx][k].Enqueue(vals[k]);
            if (_history[modelIdx][k].Count > HISTORY)
                _history[modelIdx][k].Dequeue();
        }
    }

    public void ResetHistory()
    {
        for (int m = 0; m < 3; m++)
        {
            for (int k = 0; k < 5; k++)
            {
                _history[m][k].Clear(); 
            }          
        }
            
    }

    // ****** toggle *******
    public void Toggle()
    {
        _visible = !_visible;
        gameObject.SetActive(_visible);
        if (panelContainer != null)
            panelContainer.SetActive(!_visible);
    }

    // ****** update ******
    void Update()
    {
        if (!_visible) return;
        RedrawBars();
        RedrawLineChart(ref _fpsTex, fpsLineImage, 2, "FPS", 40f, 0f);
        RedrawLineChart(ref _jitterTex, jitterLineImage, 3, "Jitter", 0f, 20f);
        RedrawRadar(ref _radarTex);
    }

    // ****** bar chart *******
    void RedrawBars()
    {
        for (int mi = 0; mi < 5; mi++)
        {
            float[] vals = new float[3];
            for (int m = 0; m < 3; m++)
                vals[m] = _latest[m] != null ? GetMetricVal(_latest[m], mi) : 0f;

            int best = System.Array.IndexOf(vals, vals.Max());

            for (int m = 0; m < 3; m++)
            {
                int idx = mi * 3 + m;
                if (idx >= barRects.Length || barRects[idx] == null) continue;

                float norm = Mathf.Clamp01(vals[m] / Mathf.Max(MaxVal[mi], 0.01f));
                // if (!HigherBetter[mi]) norm = 1f - norm;   // invert so full = best

                // Animate height
                var rt = barRects[idx];
                var delta = rt.sizeDelta;
                delta.y = Mathf.Lerp(delta.y, norm * BAR_MAX_H, Time.deltaTime * 8f);
                rt.sizeDelta = delta;

                // Colour
                var img = rt.GetComponent<Image>();
                if (img != null)
                {
                    Color c = ModelColors[m];
                    c.a = (m == best) ? 1f : 0.55f;
                    img.color = c;
                }

                // Value label
                if (idx < barValueLabels.Length && barValueLabels[idx] != null)
                    barValueLabels[idx].text = FormatVal(vals[m], mi);
            }

            // Metric title
            if (mi < metricTitleLabels.Length && metricTitleLabels[mi] != null)
                metricTitleLabels[mi].text = MetricKeys[mi];
        }
    }

    float GetMetricVal(PoseMetrics m, int idx) => idx switch {
        0 => m.jointAngleAccuracy,
        1 => m.pckh,
        2 => m.fps,
        3 => m.interFrameJitter,
        4 => m.occlusionDetectionRate,
        _ => 0f
    };

    string FormatVal(float v, int idx) => idx switch {
        0 => $"{v:F1}º",
        1 => $"{v:F0}%",
        2 => $"{v:F0}",
        3 => $"{v:F1}px",
        4 => $"{v:F0}%",
        _ => $"{v:F1}"
    };

    // ****** line chart (drawn onto a Texture2D → RawImage) *******
    void RedrawLineChart(ref Texture2D tex, RawImage target, int metricIdx, string title, float goodThreshold, float badThreshold)
    {
        if (target == null) return;

        int W = 320, H = 100;
        if (tex == null || tex.width != W || tex.height != H)
        {
            tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear
            };
            target.texture = tex;
        }

        // Clear
        Color bg = new Color(0.086f, 0.105f, 0.141f, 1f);
        var pixels = tex.GetPixels();
        for (int i = 0; i < pixels.Length; i++) pixels[i] = bg;

        // Draw threshold line
        if (goodThreshold > 0)
            DrawHLine(pixels, W, H, goodThreshold, MaxVal[metricIdx], new Color(0.30f, 0.69f, 0.31f, 0.4f));

        // Draw each model's line
        for (int m = 0; m < 3; m++)
        {
            var hist = _history[m][metricIdx].ToArray();
            if (hist.Length < 2) continue;
            DrawLine(pixels, W, H, hist, MaxVal[metricIdx], ModelColors[m]);
        }

        tex.SetPixels(pixels);
        tex.Apply();
    }

    void DrawHLine(Color[] pixels, int W, int H, float val, float maxVal, Color c)
    {
        int y = Mathf.Clamp((int)(val / maxVal * H), 0, H - 1);
        for (int x = 0; x < W; x++)
            pixels[y * W + x] = c;
    }

    void DrawLine(Color[] pixels, int W, int H, float[] data, float maxVal, Color c)
    {
        int n = data.Length;
        for (int i = 1; i < n; i++)
        {
            int x0 = (int)((i - 1) / (float)(n - 1) * (W - 1));
            int x1 = (int)(i / (float)(n - 1) * (W - 1));
            int y0 = Mathf.Clamp((int)(data[i - 1] / maxVal * H), 0, H - 1);
            int y1 = Mathf.Clamp((int)(data[i] / maxVal * H), 0, H - 1);
            DrawSegment(pixels, W, H, x0, y0, x1, y1, c);
        }
    }

    void DrawSegment(Color[] pixels, int W, int H, int x0, int y0, int x1, int y1, Color c)
    {
        int dx = Mathf.Abs(x1 - x0), dy = Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;

        while (true)
        {
            if (x0 >= 0 && x0 < W && y0 >= 0 && y0 < H)
                pixels[y0 * W + x0] = Color.Lerp(pixels[y0 * W + x0], c, 0.9f);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x0 += sx; }
            if (e2 < dx) { err += dx; y0 += sy; }
        }
    }

    // ****** radar chart ******
    void RedrawRadar(ref Texture2D tex)
    {
        if (radarImage == null) return;

        int W = 200, H = 200;
        if (tex == null)
        {
            tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear
            };
            radarImage.texture = tex;
        }

        Color bg = new Color(0.086f, 0.105f, 0.141f, 1f);
        var pixels = tex.GetPixels32();
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = new Color32(22, 27, 34, 255);

        int cx = W / 2, cy = H / 2, r = 80;
        int N = 5;

        // Normalise all metrics 0-1 where 1 = best
        float[][] norm = new float[3][];
        for (int m = 0; m < 3; m++)
        {
            norm[m] = new float[N];
            if (_latest[m] == null) continue;
            float[] raw = {
                _latest[m].jointAngleAccuracy,
                _latest[m].pckh,
                _latest[m].fps,
                _latest[m].interFrameJitter,
                _latest[m].occlusionDetectionRate
            };
            for (int k = 0; k < N; k++)
            {
                float n01 = Mathf.Clamp01(raw[k] / MaxVal[k]);
                norm[m][k] = n01;
            }
        }

        // Draw grid rings
        for (int ring = 1; ring <= 4; ring++)
        {
            float fr = ring / 4f * r;
            DrawRadarPolygon(pixels, W, H, cx, cy, (int)fr, N, new Color(0.18f, 0.22f, 0.27f, 1f));
        }

        // Draw each model
        for (int m = 2; m >= 0; m--) // draw back-to-front
        {
            Color c = ModelColors[m];
            c.a = 0.35f;
            FillRadarPolygon(pixels, W, H, cx, cy, r, N, norm[m], c);
            c.a = 0.9f;
            DrawRadarPolygon(pixels, W, H, cx, cy, r, N, c, norm[m]);
        }

        tex.SetPixels32(pixels);
        tex.Apply();
    }

    void DrawRadarPolygon(Color32[] pixels, int W, int H, int cx, int cy,
                          int r, int N, Color c, float[] scale = null)
    {
        float step = 2f * Mathf.PI / N;
        for (int i = 0; i < N; i++)
        {
            float a0 = i * step - Mathf.PI / 2f;
            float a1 = (i + 1) * step - Mathf.PI / 2f;
            float s0 = scale != null ? scale[i] : 1f;
            float s1 = scale != null ? scale[(i + 1) % N] : 1f;
            int x0 = cx + (int)(Mathf.Cos(a0) * r * s0);
            int y0 = cy + (int)(Mathf.Sin(a0) * r * s0);
            int x1 = cx + (int)(Mathf.Cos(a1) * r * s1);
            int y1 = cy + (int)(Mathf.Sin(a1) * r * s1);
            Color32 color = new Color32((byte)(c.r*255),(byte)(c.g*255),(byte)(c.b*255),(byte)(c.a*255));
            DrawSegment32(pixels, W, H, x0, y0, x1, y1, color);
        }
    }

    void FillRadarPolygon(Color32[] pixels, int W, int H, int cx, int cy, int r, int N, float[] scale, Color c)
    {
        float step = 2f * Mathf.PI / N;
        Vector2[] pts = new Vector2[N];
        for (int i = 0; i < N; i++)
        {
            float a = i * step - Mathf.PI / 2f;
            pts[i] = new Vector2(cx + Mathf.Cos(a) * r * scale[i], cy + Mathf.Sin(a) * r * scale[i]);
        }

        // Scanline fill
        int minY = (int)pts.Min(p => p.y);
        int maxY = (int)pts.Max(p => p.y);
        Color32 c32 = new Color32((byte)(c.r*255),(byte)(c.g*255),(byte)(c.b*255),(byte)(c.a*255));

        for (int y = Mathf.Max(0, minY); y <= Mathf.Min(H-1, maxY); y++)
        {
            var xs = new List<int>();
            for (int i = 0; i < N; i++)
            {
                Vector2 p1 = pts[i], p2 = pts[(i+1)%N];
                if ((p1.y <= y && p2.y > y) || (p2.y <= y && p1.y > y))
                {
                    float t = (y - p1.y) / (p2.y - p1.y);
                    xs.Add((int)(p1.x + t * (p2.x - p1.x)));
                }
            }
            xs.Sort();
            for (int j = 0; j + 1 < xs.Count; j += 2)
                for (int x = Mathf.Max(0, xs[j]); x <= Mathf.Min(W-1, xs[j+1]); x++)
                    pixels[y * W + x] = c32;
        }
    }

    void DrawSegment32(Color32[] pixels, int W, int H, int x0, int y0, int x1, int y1, Color32 c)
    {
        int dx = Mathf.Abs(x1-x0), dy = Mathf.Abs(y1-y0);
        int sx = x0<x1?1:-1, sy = y0<y1?1:-1, err = dx-dy;
        while (true)
        {
            if (x0>=0&&x0<W&&y0>=0&&y0<H) pixels[y0*W+x0] = c;
            if (x0==x1&&y0==y1) break;
            int e2=2*err;
            if (e2>-dy){err-=dy;x0+=sx;}
            if (e2< dx){err+=dx;y0+=sy;}
        }
    }
}