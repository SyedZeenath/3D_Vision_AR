// Controls one metric row inside MetricsPanel.
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class MetricRowUI : MonoBehaviour
{
    [Header("Wire these in the Inspector")]
    public TextMeshProUGUI metricNameLabel;
    public RectTransform barFill;
    public TextMeshProUGUI valueLabel;

    [Header("Settings")]
    public string displayName = "Metric";

    [HideInInspector] public Color accentColor = Color.green;

    Image _fillImage;
    float _trackWidth = -1f;

    void Awake()
    {
        _fillImage = barFill.GetComponent<Image>();
        if (metricNameLabel != null)
            metricNameLabel.text = displayName;
    }

    /// <summary>
    /// goodness 0 -> 1: 0 = worst, 1 = best.
    /// Bar fills proportionally; value label gets accent colour.
    /// </summary>
    public void SetValue(string formatted, float goodness)
    {
        // Lazy-read track width (not available in Awake before layout pass)
        if (_trackWidth < 0f)
            _trackWidth = barFill.parent.GetComponent<RectTransform>().rect.width;

        if (valueLabel != null)
        {
            valueLabel.text = formatted;
            valueLabel.color = accentColor;
        }

        // Clamp bar width to track width
        float w = Mathf.Clamp01(goodness) * Mathf.Max(_trackWidth, 0f);
        barFill.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, w);

        if (_fillImage != null)
            _fillImage.color = accentColor;
    }

    // Called by ModelPanelUI before any SetValue calls
    public void Init(string name, Color accent)
    {
        displayName = name;
        accentColor = accent;
        if (metricNameLabel != null)
            metricNameLabel.text = name;
    }
}
