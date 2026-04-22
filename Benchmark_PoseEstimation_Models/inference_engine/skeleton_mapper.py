import numpy as np

# MediaPipe landmark index → COCO-17 index
MP_TO_COCO = {
    0: 0, # nose
    2: 1, # left eye
    5: 2, # right eye
    7: 3, # left ear
    8: 4, # right ear
    11: 5, # left shoulder
    12: 6, # right shoulder
    13: 7, # left elbow
    14: 8, # right elbow
    15: 9, # left wrist
    16: 10, # right wrist
    23: 11, # left hip
    24: 12, # right hip
    25: 13, # left knee
    26: 14, # right knee
    27: 15, # left ankle
    28: 16, # right ankle
}

# MPII joint index → COCO-17 index
# MPII joints: 0=r-ankle,1=r-knee,2=r-hip,3=l-hip,4=l-knee,5=l-ankle,
#              6=pelvis,7=thorax,8=upper-neck,9=head-top,
#              10=r-wrist,11=r-elbow,12=r-shoulder,
#              13=l-shoulder,14=l-elbow,15=l-wrist
MPII_TO_COCO = {
    9: 0, # head top -> nose (approximation)
    13: 5, # left shoulder
    12: 6, # right shoulder
    14: 7, # left elbow
    11: 8, # right elbow
    15: 9, # left wrist
    10: 10, # right wrist
    3: 11, # left hip
    2: 12, # right hip
    4: 13, # left knee
    1: 14, # right knee
    5: 15, # left ankle
    0: 16, # right ankle
}

# MPII pairs used to estimate head size for PCKh
# distance between upper-neck (8) and head-top (9)
MPII_HEAD_PAIR = (8, 9)

class SkeletonMapper:
    def mediapipe_to_coco(self, kps: np.ndarray, conf=None):
        """Map MediaPipe 33-point landmarks to COCO-17."""
        coco_kps = np.zeros((17, 2), dtype=np.float32)
        coco_conf = np.zeros(17, dtype=np.float32)

        for mp_i, coco_i in MP_TO_COCO.items():
            if mp_i < len(kps):
                coco_kps[coco_i] = kps[mp_i]
                coco_conf[coco_i] = float(conf[mp_i]) if conf is not None else 1.0

        return coco_kps, coco_conf

    def mpii_to_coco(self, mpii_kps: np.ndarray):
        """
        Map MPII 16-point skeleton to COCO-17.
        Returns (coco_kps, head_size) where head_size is the upper-neck -> head-top distance used for PCKh normalisation.
        """
        coco = np.zeros((17, 2), dtype=np.float32)

        for mpii_idx, coco_idx in MPII_TO_COCO.items():
            if mpii_idx < len(mpii_kps):
                coco[coco_idx] = mpii_kps[mpii_idx]

        # Head size for PCKh (raw MPII pixel coords before normalisation)
        a, b = MPII_HEAD_PAIR
        if a < len(mpii_kps) and b < len(mpii_kps):
            head_size = float(np.linalg.norm(mpii_kps[a] - mpii_kps[b])) + 1e-6
        else:
            head_size = None

        return coco, head_size