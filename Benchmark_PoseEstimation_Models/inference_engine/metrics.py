import time
import numpy as np
from collections import deque

# COCO-17 joint angle triplets (joint_a, vertex, joint_b)
# Angle is measured at the vertex joint.
COCO_ANGLE_TRIPLETS = [
    (5, 7, 9), # left elbow
    (6, 8, 10), # right elbow
    (11, 13, 15), # left knee
    (12, 14, 16), # right knee
    (7, 5, 11), # left shoulder
    (8, 6, 12), # right shoulder
]

# MediaPipe BlazePose 33-point angle triplets
MP_ANGLE_TRIPLETS = [
    (11, 13, 15), # left elbow
    (12, 14, 16), # right elbow
    (23, 25, 27), # left knee
    (24, 26, 28), # right knee
    (13, 11, 23), # left shoulder
    (14, 12, 24), # right shoulder
]

class MetricsTracker:
    def __init__(self, model_name: str, window: int = 60, angle_triplets=None, 
                 conf_threshold: float = 0.5, image_width: int = 640, image_height: int = 480):
        self.model_name = model_name
        self.window = window
        self.conf_threshold = conf_threshold
        self.img_w = image_width
        self.img_h = image_height
        self.angle_triplets = angle_triplets or COCO_ANGLE_TRIPLETS

        # Rolling buffers
        self._frame_times = deque(maxlen=window)
        self._angle_vals = deque(maxlen=window)
        self._jitter_vals = deque(maxlen=window)
        self._pckh_vals = deque(maxlen=window)

        # State for temporal metrics
        self._prev_kps = None
        self._prev_vel = None
        self._prev_angles = None

        # Occlusion tracking
        self._in_occlusion = False
        self._occ_events = 0
        self._occ_recoveries = 0
        self._recovery_frames = []
        self._current_recovery_len = 0

    # ---------------
    # Public API
    # ---------------
    def update(self, pred_kps: np.ndarray, confidences: np.ndarray, 
               gt_kps: np.ndarray | None = None, head_size: float | None = None):
        """
        Parameters
        ----------
        pred_kps : (N, 2) normalised [0,1] keypoints
        confidences : (N,) per-keypoint confidence scores
        gt_kps : (N, 2) ground-truth keypoints(MPII mode); None for live/Kaggle
        head_size : scalar head size in pixels for PCKh normalisation(MPII only). When None the proxy detection-rate is used instead.
        """
        if pred_kps is None or confidences is None:
            # No detection this frame — still tick the clock for FPS
            self._frame_times.append(time.perf_counter())
            return

        if confidences is None:
            confidences = np.ones(len(pred_kps), dtype=np.float32)

        self._frame_times.append(time.perf_counter())

        visible = confidences >= self.conf_threshold
        n_visible = int(visible.sum())
        n_total = len(pred_kps)

        # ------ PCKh/detection rate ------
        if gt_kps is not None:
            self._pckh_vals.append(self._compute_pckh(pred_kps, gt_kps, head_size=head_size))
        else:
            # Proxy: fraction of joints detected above threshold
            self._pckh_vals.append(n_visible / max(n_total, 1) * 100.0)

        # ------ Joint angle error: Lower is better ------
        angles = self._compute_angles(pred_kps, visible)

        if gt_kps is not None:
            # For MPII we can compute actual angle error against Ground truth(GT)
            gt_vis = np.ones(len(gt_kps), dtype=bool)
            gt_angles = self._compute_angles(gt_kps, gt_vis)
            angle_metric = float(np.mean(np.abs(angles - gt_angles)))
        else:
            # for live/Kaggle modes we don't have GT angles, so track inter-frame angle changes as a proxy for stability
            if self._prev_angles is not None:
                angle_metric = float(np.mean(np.abs(angles - self._prev_angles)))
            else:
                angle_metric = 0.0

        self._angle_vals.append(angle_metric)
        self._prev_angles = angles

        # ------ Inter-frame jitter ------
        jitter = 0.0
        if self._prev_kps is not None:
            vel = pred_kps - self._prev_kps
            if self._prev_vel is not None:
                acc = vel - self._prev_vel
                acc_pixels = acc * np.array([self.img_w, self.img_h], dtype=np.float32)
                jitter = float(np.linalg.norm(acc_pixels, axis=1).mean())
            self._prev_vel = vel

        self._jitter_vals.append(jitter)
        self._prev_kps = pred_kps.copy()

        # ------ Occlusion robustness ------
        partial = n_visible < int(0.6 * n_total)
        full = n_visible >= int(0.8 * n_total)

        if partial and not self._in_occlusion:
            self._in_occlusion = True
            self._occ_events += 1
            self._current_recovery_len = 0
        elif self._in_occlusion:
            self._current_recovery_len += 1
            if full:
                self._occ_recoveries += 1
                self._recovery_frames.append(self._current_recovery_len)
                self._in_occlusion = False

    def get_metrics(self) -> dict:
        return {
            "joint_angle": self._mean(self._angle_vals),
            "pckh": self._mean(self._pckh_vals),
            "fps": self._calc_fps(),
            "jitter": self._mean(self._jitter_vals),
            "occlusion": self._occlusion_score(),
        }

    def reset(self):
        """Clear all rolling buffers and state, called on mode switch."""
        self._frame_times.clear()
        self._angle_vals.clear()
        self._jitter_vals.clear()
        self._pckh_vals.clear()
        self._prev_kps = None
        self._prev_vel = None
        self._prev_angles = None
        self._in_occlusion = False
        self._occ_events = 0
        self._occ_recoveries = 0
        self._recovery_frames = []
        self._current_recovery_len = 0

    # --------------------
    # Internal helpers
    # --------------------
    def _compute_pckh(self, pred: np.ndarray, gt: np.ndarray, alpha=0.5, head_size=None) -> float:
        """
        Compute PCKh (Percentage of Correct Keypoints within a threshold of head size).
        head_size: pre-computed from MPII upper-neck -> head-top distance (pixels). 
        If None, fall back to distance between first two GT joints.
        """
        if head_size is None or head_size <= 0:
            head_size = float(np.linalg.norm(gt[0] - gt[1])) + 1e-6

        dists   = np.linalg.norm(pred - gt, axis=1)
        correct = dists < alpha * head_size # alpha=0.5 means within 50% of head size is considered correct
        return float(correct.mean() * 100.0)

    def _compute_angles(self, kps: np.ndarray, visible: np.ndarray) -> np.ndarray:
        angles = []
        for (a, v, b) in self.angle_triplets:
            max_idx = max(a, v, b)
            if max_idx >= len(kps) or not (visible[a] and visible[v] and visible[b]):
                angles.append(0.0)
                continue

            v1 = kps[a] - kps[v]
            v2 = kps[b] - kps[v]
            n1, n2 = np.linalg.norm(v1), np.linalg.norm(v2)

            # If either vector is too small, we can't define a stable angle, so treat as 0 error (no penalty)
            if n1 < 1e-6 or n2 < 1e-6:
                angles.append(0.0)
                continue

            cos = np.clip(np.dot(v1, v2) / (n1 * n2), -1.0, 1.0)
            angles.append(float(np.degrees(np.arccos(cos))))

        return np.array(angles, dtype=np.float32)

    def _calc_fps(self) -> float:
        if len(self._frame_times) < 2:
            return 0.0
        elapsed = self._frame_times[-1] - self._frame_times[0]
        return (len(self._frame_times) - 1) / elapsed if elapsed > 0 else 0.0

    def _mean(self, buf: deque) -> float:
        return float(np.mean(buf)) if buf else 0.0

    def _occlusion_score(self) -> float:
        if self._occ_events == 0:
            return 100.0
        recovery_rate = self._occ_recoveries / self._occ_events
        avg_recovery = float(np.mean(self._recovery_frames)) if self._recovery_frames else 0.0
        return float(recovery_rate * 100.0 - avg_recovery)