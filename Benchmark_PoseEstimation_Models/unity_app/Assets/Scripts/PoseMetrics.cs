/// <summary>
/// Data container for one model's current metrics.
/// Populated by PythonDataReceiver and passed to ModelPanelUI.
/// </summary>
[System.Serializable]
public class PoseMetrics
{
    public float jointAngleAccuracy; // degrees - lower is better
    public float pckh; // 0-100% - higher is better
    public float fps; // frames/s - higher is better
    public float interFrameJitter; // pixels - lower is better
    public float occlusionDetectionRate; // 0-100% - higher is better
    public bool hasGT; // whether ground truth data is available

    public PoseMetrics() { }

    public PoseMetrics(float jointAngleVal, float pckhVal, float fpsVal, float jitterVal, float occlusionVal, bool hasGTVal = false)
    {
        jointAngleAccuracy = jointAngleVal;
        pckh = pckhVal;
        fps = fpsVal;
        interFrameJitter = jitterVal;
        occlusionDetectionRate = occlusionVal;
        hasGT = hasGTVal;
    }

    // Matches JSON keys sent by pose_benchmark.py:
    // {"mae":f, "pckh":f, "fps":f, "jitter":f, "occlusion":f}
    public static PoseMetrics FromJson(string json)
    {
        var raw = UnityEngine.JsonUtility.FromJson<RawJson>(json);
        return new PoseMetrics(raw.mae, raw.pckh, raw.fps,
                               raw.jitter, raw.occlusion, raw.hasGT);
    }

    [System.Serializable]
    private class RawJson
    {
        public float mae;
        public float pckh;
        public float fps;
        public float jitter;
        public float occlusion;
        public bool hasGT;
    }
}
