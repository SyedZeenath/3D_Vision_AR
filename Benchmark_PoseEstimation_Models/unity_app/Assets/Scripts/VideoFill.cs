using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Resizes VideoDisplay (RawImage) of every frame to fill VideoArea completely,
/// cropping edges if the video aspect ratio doesn't match the panel.
///
/// Attach to: VideoArea
/// </summary>
 
[RequireComponent(typeof(RectTransform))]
public class VideoFill : MonoBehaviour
{
    [Tooltip("The RawImage child that shows the video texture.")]
    public RawImage videoDisplay;

    [Tooltip("Fallback aspect ratio used before a texture is assigned (16:9 default).")]
    public float fallbackAspect = 16f / 9f;

    RectTransform _area;

    void Awake()
    {
        _area = GetComponent<RectTransform>();
    }

    void LateUpdate()
    {
        if (videoDisplay == null) return;

        float areaW = _area.rect.width;
        float areaH = _area.rect.height;
        if (areaW <= 0 || areaH <= 0) return;

        // Use actual texture aspect once a frame is loaded
        float aspect = fallbackAspect;
        var tex = videoDisplay.texture;
        if (tex != null && tex.width > 0 && tex.height > 0)
            aspect = (float)tex.width / tex.height;

        float areaAspect = areaW / areaH;
        float vidW, vidH;

        if (areaAspect >= aspect)
        {
            // Panel is wider than video - fill by width (crops top/bottom)
            vidW = areaW;
            vidH = areaW / aspect;
        }
        else
        {
            // Panel is taller than video - fill by height (crops sides)
            vidH = areaH;
            vidW = areaH * aspect;
        }

        var rt = videoDisplay.rectTransform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(vidW, vidH);
    }
}
