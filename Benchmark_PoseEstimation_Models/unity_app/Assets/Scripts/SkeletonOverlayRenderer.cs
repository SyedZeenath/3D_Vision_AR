using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class SkeletonOverlayRenderer : MonoBehaviour
{
    [Header("References")]
    public RawImage videoDisplay;
    public GameObject jointDotPrefab;
    public GameObject bonePrefab;

    [Header("Appearance")]
    public Color jointColor = new Color(0.29f, 0.86f, 0.50f, 1f);
    public Color boneColor = new Color(0.29f, 0.86f, 0.50f, 0.75f);
    public float jointRadius = 5f;
    public float boneWidth = 2f;

    [Header("Confidence threshold (joints below this are hidden)")]
    public float confidenceThreshold = 0.1f;

    readonly List<RectTransform> _joints = new();
    readonly List<RectTransform> _bones = new();

    RectTransform _rt;
    Vector2[] _pendingKps;
    float[] _pendingConf;
    int[] _pendingConns;
    bool _dirty;

    void Awake() => _rt = GetComponent<RectTransform>();

    void LateUpdate()
    {
        SyncToVideo();
        if (_dirty && _pendingKps != null)
        {
            DrawSkeleton(_pendingKps, _pendingConf, _pendingConns);
            _dirty = false;
        }
    }

    void SyncToVideo()
    {
        if (videoDisplay == null) return;
        var vrt = videoDisplay.rectTransform;
        _rt.SetParent(vrt.parent, false);
        _rt.anchorMin = vrt.anchorMin;
        _rt.anchorMax = vrt.anchorMax;
        _rt.pivot = vrt.pivot;
        _rt.anchoredPosition = vrt.anchoredPosition;
        _rt.sizeDelta = vrt.sizeDelta;
        _rt.localScale = vrt.localScale;
        _rt.SetAsLastSibling();
    }

    // Called from ModelPanelUI.SetKeypoints
    public void UpdateSkeleton(Vector2[] normalizedKeypoints, float[] confidences, int[] connections)
    {
        _pendingKps = normalizedKeypoints;
        _pendingConf = confidences;
        _pendingConns = connections;
        _dirty = true;
    }

    public void SetColors(Color joint, Color bone)
    {
        jointColor = joint;
        boneColor = bone;
    }

    public void ClearSkeleton()
    {
        foreach (var j in _joints) if (j) j.gameObject.SetActive(false);
        foreach (var b in _bones) if (b) b.gameObject.SetActive(false);
    }

    // A joint is valid if confidence is sufficient - clamp position to frame bounds
    bool IsValid(int i, Vector2[] kps, float[] conf)
    {
        if (conf != null && i < conf.Length && conf[i] < confidenceThreshold)
            return false;
        var k = kps[i];
        return k.x != 0f || k.y != 0f; // only reject true zero (undetected)
    }

    // Clamp normalised coords to visible area with a small margin
    Vector2 Clamp(Vector2 norm) => new Vector2(Mathf.Clamp(norm.x, 0f, 1f), Mathf.Clamp(norm.y, 0f, 1f));

    void DrawSkeleton(Vector2[] kps, float[] conf, int[] connections)
    {
        if (videoDisplay == null) return;
        var vrt = videoDisplay.rectTransform;
        float vidW = vrt.sizeDelta.x;
        float vidH = vrt.sizeDelta.y;
        if (vidW <= 0 || vidH <= 0) return;

        int nKps = kps.Length;
        int nBones = connections != null ? connections.Length / 2 : 0;

        EnsurePool(_joints, nKps, jointDotPrefab);
        EnsurePool(_bones, nBones, bonePrefab);

        Vector2 ToLocal(Vector2 norm)
        {
            var c = Clamp(norm);
            return new Vector2((c.x - 0.5f) * vidW, (0.5f - c.y) * vidH);
        }

        // ****** Joints ******
        for (int i = 0; i < nKps; i++)
        {
            var jrt = _joints[i];
            if (!IsValid(i, kps, conf))
            {
                jrt.gameObject.SetActive(false);
                continue;
            }
            jrt.gameObject.SetActive(true);
            jrt.pivot = new Vector2(0.5f, 0.5f);
            jrt.anchoredPosition = ToLocal(kps[i]);
            jrt.sizeDelta = new Vector2(jointRadius * 2f, jointRadius * 2f);
            var img = jrt.GetComponent<Image>();
            if (img) 
            {
                Color jointConf = Color.Lerp(Color.red, Color.green, conf[i]);
                img.color = jointConf;
            } 
        }
        for (int i = nKps; i < _joints.Count; i++)
            if (_joints[i]) _joints[i].gameObject.SetActive(false);

        // ****** Bones ******
        if (connections != null)
        {
            for (int b = 0; b < nBones; b++)
            {
                int ai = connections[b * 2];
                int bi = connections[b * 2 + 1];
                var brt = _bones[b];

                if (ai < 0 || bi < 0 || ai >= nKps || bi >= nKps ||
                    !IsValid(ai, kps, conf) || !IsValid(bi, kps, conf))
                {
                    brt.gameObject.SetActive(false);
                    continue;
                }

                Vector2 a = ToLocal(kps[ai]);
                Vector2 bv = ToLocal(kps[bi]);
                Vector2 diff = bv - a;
                float len = diff.magnitude;

                if (len < 2f) { brt.gameObject.SetActive(false); continue; }

                brt.gameObject.SetActive(true);
                brt.pivot = new Vector2(0.5f, 0.5f);
                brt.anchoredPosition = a + diff * 0.5f;
                brt.sizeDelta = new Vector2(len, boneWidth);
                brt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(diff.y, diff.x) * Mathf.Rad2Deg);
                var img = brt.GetComponent<Image>();
                if (img) img.color = boneColor;
            }
        }
        for (int b = nBones; b < _bones.Count; b++)
            if (_bones[b]) _bones[b].gameObject.SetActive(false);
    }

    void EnsurePool(List<RectTransform> pool, int needed, GameObject prefab)
    {
        if (prefab == null) return;
        while (pool.Count < needed)
        {
            var go = Instantiate(prefab, _rt);
            go.SetActive(false);
            var rt = go.GetComponent<RectTransform>();
            var le = go.GetComponent<LayoutElement>();
            if (le != null) Destroy(le);
            rt.pivot = new Vector2(0.5f, 0.5f);
            pool.Add(rt);
        }
    }
}