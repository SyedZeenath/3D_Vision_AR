using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Add this to the SkeletonOverlay GameObject.
///
/// The problem: when you Instantiate UI prefabs (JointDot, Bone) they sometimes
/// get a Canvas component that renders them on top of everything else in the scene,
/// ignoring the panel hierarchy and causing dots to appear on other panels.
///
/// This script removes any rogue Canvas/GraphicRaycaster components on the
/// overlay and its children, and sets a RectMask2D on VideoArea to clip
/// anything that strays outside the panel boundary.
/// </summary>

[RequireComponent(typeof(RectTransform))]
public class OverlayCanvasFix : MonoBehaviour
{
    void Awake()
    {
        // Remove any Canvas component on this overlay itself (shouldn't be there
        // but Unity sometimes adds one when you drag a prefab in as a child)
        var c = GetComponent<Canvas>();
        if (c != null) Destroy(c);

        var gr = GetComponent<GraphicRaycaster>();
        if (gr != null) Destroy(gr);

        // Add RectMask2D to VideoArea (our parent) so nothing can render outside the video bounds
        var parent = transform.parent;
        if (parent != null && parent.GetComponent<RectMask2D>() == null)
            parent.gameObject.AddComponent<RectMask2D>();
    }
}
