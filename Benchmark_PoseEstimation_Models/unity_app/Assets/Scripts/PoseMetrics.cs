/// <summary>
/// Data container for one model's current metrics.
/// Populated by PythonDataReceiver and passed to ModelPanelUI.
/// </summary>
[System.Serializable]
public class PoseMetrics
{
    public float jointAngleError; // degrees - lower is better
    public float pckh; // 0-100% - higher is better
    public float fps; // frames/s - higher is better
    public float interFrameJitter; // pixels - lower is better
    public float occlusionDetectionRate; // 0-100% - higher is better
    public bool hasGT; // whether ground truth data is available

    public PoseMetrics() { }

    public PoseMetrics(float jointAngleVal, float pckhVal, float fpsVal, float jitterVal, float occlusionVal, bool hasGTVal = false)
    {
        jointAngleError = jointAngleVal;
        pckh = pckhVal;
        fps = fpsVal;
        interFrameJitter = jitterVal;
        occlusionDetectionRate = occlusionVal;
        hasGT = hasGTVal;
    }

    // Matches JSON keys sent by pose_benchmark.py:
    public static PoseMetrics FromJson(string json)
    {
        var raw = UnityEngine.JsonUtility.FromJson<RawJson>(json);
        return new PoseMetrics(raw.joint_angle, raw.pckh, raw.fps, raw.jitter, raw.occlusion);
    }

    [System.Serializable]
    private class RawJson
    {
        public float joint_angle;
        public float pckh;
        public float fps;
        public float jitter;
        public float occlusion;
    }
}
