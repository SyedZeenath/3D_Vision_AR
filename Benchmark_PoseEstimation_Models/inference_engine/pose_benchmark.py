"""
Python sends UDP to Unity (frames, keypoints, metrics).
Unity sends HTTP GET to Python to switch mode (Live/Kaggle).

Run:
    python pose_benchmark_simple.py # live/Kaggle benchmark
    python pose_benchmark_simple.py --eval-mpii # offline MPII eval; Run once, saves results/mpii_results.json
    python pose_benchmark_simple.py --eval-mpii --max-samples 200 # if needed limit the samples for faster testing
"""
import socket, struct, json, time, os, threading, queue, argparse
import cv2, numpy as np
import scipy.io
from flask import Flask, jsonify
from metrics import MetricsTracker, COCO_ANGLE_TRIPLETS, MP_ANGLE_TRIPLETS
from skeleton_mapper import SkeletonMapper

# -----------------------------
# Config
# -----------------------------
UNITY_IP = "127.0.0.1"
UNITY_PORT = 5000
HTTP_PORT = 5006

MODE_MPII = 0
MODE_KAGGLE = 1
MODE_LIVE = 2

MPII_MAT = "data/datasets/mpii/mpii_human_pose_v1_u12_1.mat"
MPII_IMGS = "data/datasets/mpii/images"
MPII_IMAGE = "data/datasets/mpii/images/000033016.jpg"  # sample image for live mode when MPII is selected
KAGGLE_VIDEO = "data/datasets/kaggle/shoulder press_1.mp4"

IDX_MEDIAPIPE = 0
IDX_YOLO = 1
IDX_OPENPOSE = 2

COCO17_BONES = [
    0,1, 0,2, 1,3, 2,4,
    5,6, 5,7, 7,9, 6,8, 8,10,
    5,11, 6,12, 11,12,
    11,13, 13,15, 12,14, 14,16,
]

mapper = SkeletonMapper()
_cmd_queue = queue.Queue()

# -----------------------------
# Flask HTTP server
# -----------------------------
_flask_app = Flask(__name__)

@_flask_app.route("/mode/<int:mode>")
def set_mode(mode):
    print(f"[HTTP] Mode request: {mode}")
    _cmd_queue.put(mode)
    return jsonify({"status": "ok", "mode": mode})

@_flask_app.route("/ping")
def ping():
    return jsonify({"status": "pong"})

def start_http_server():
    _flask_app.run(host="0.0.0.0", port=HTTP_PORT, use_reloader=False, threaded=True)

# -----------------------------
# UDP packet builders
# -----------------------------
def udp_send(sock, addr, data):
    if data:
        sock.sendto(data, addr)

def make_frame_packet(model_idx, frame):
    ret, jpg = cv2.imencode('.jpg', frame, [cv2.IMWRITE_JPEG_QUALITY, 70])
    if not ret:
        return b''
    b = jpg.tobytes()
    return struct.pack('<4sBi', b'FRME', model_idx, len(b)) + b

def make_keypoint_packet(model_idx, kps, conf):
    n = len(kps)
    data = struct.pack('<4sBH', b'KEYS', model_idx, n)
    for i in range(n):
        data += struct.pack('<fff', float(kps[i,0]), float(kps[i,1]), float(conf[i]))
    pairs = len(COCO17_BONES) // 2
    data += struct.pack('<H', pairs)
    for i in range(0, len(COCO17_BONES), 2):
        data += struct.pack('<hh', COCO17_BONES[i], COCO17_BONES[i+1])
    return data

def make_metrics_packet(model_idx, m):
    payload = json.dumps({
        "joint_angle": round(float(m.get("joint_angle", 0)), 2),
        "mean_angle": round(float(m.get("mean_angle", 0)), 2),
        "pckh": round(float(m.get("pckh", 0)), 1),
        "fps": round(float(m.get("fps", 0)), 1),
        "jitter": round(float(m.get("jitter", 0)), 2),
        "occlusion": round(float(m.get("occlusion", 0)), 1),
        "has_gt": bool(m.get("has_gt", False)),
    }).encode()

    return struct.pack('<4sB', b'METR', model_idx) + payload

def make_mode_packet(mode):
    return struct.pack('<4sB', b'MODE', mode)

# -----------------------------
# Model wrappers
# -----------------------------
class MediaPipeModel:
    def __init__(self):
        import mediapipe as mp
        from mediapipe.tasks.python import vision
        from mediapipe.tasks import python
        opts = vision.PoseLandmarkerOptions(base_options=python.BaseOptions(model_asset_path='data/models/pose_landmarker.task'),
                                            running_mode=vision.RunningMode.IMAGE)
        self.det = vision.PoseLandmarker.create_from_options(opts)

    def infer(self, frame_bgr):
        import mediapipe as mp
        rgb = cv2.cvtColor(frame_bgr, cv2.COLOR_BGR2RGB)
        res = self.det.detect(mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb))
        if not res.pose_landmarks:
            return None, None
        lms = res.pose_landmarks[0]
        kps = np.array([[l.x, l.y] for l in lms], dtype=np.float32)
        conf = np.array([l.visibility for l in lms], dtype=np.float32)
        return mapper.mediapipe_to_coco(kps, conf)

    def close(self): self.det.close()

class YOLOModel:
    def __init__(self):
        from ultralytics import YOLO
        self.model = YOLO('data/models/yolo26n-pose.pt')

    def infer(self, frame_bgr):
        res = self.model(frame_bgr, verbose=False, conf=0.3)
        if not res or res[0].keypoints is None:
            return None, None
        kd = res[0].keypoints
        if kd.xy is None or len(kd.xy) == 0:
            return None, None
        h, w = frame_bgr.shape[:2]
        xy = kd.xy[0].cpu().numpy()
        conf = kd.conf[0].cpu().numpy()
        return (xy / [w, h]).astype(np.float32), conf.astype(np.float32)

class OpenPoseModel:
    def __init__(self):
        import sys
        sys.path.insert(0, r'data/models/pytorch-openpose')
        from src.body import Body # From the cloned PyTorch OpenPose repo
        self.body = Body('data/models/pytorch-openpose/model/body_pose_model.pth')

    def infer(self, frame_bgr):
        candidate, subset = self.body(frame_bgr)
        if subset.shape[0] == 0:
            return None, None
        person = subset[0]
        h, w = frame_bgr.shape[:2]
        kps = np.zeros((17, 2), dtype=np.float32)
        conf = np.zeros(17, dtype=np.float32)
        OP18_TO_COCO17 = {
            0:0, 2:6, 3:8, 4:10, 5:5, 6:7, 7:9,
            8:12, 9:14, 10:16, 11:11, 12:13, 13:15,
            14:2, 15:1, 16:4, 17:3,
        }
        for op_idx, coco_idx in OP18_TO_COCO17.items():
            kp_idx = int(person[op_idx])
            if kp_idx == -1 or kp_idx >= len(candidate):
                continue
            kps[coco_idx] = [candidate[kp_idx][0] / w, candidate[kp_idx][1] / h]
            conf[coco_idx] = float(candidate[kp_idx][2])
        return kps, conf

    def close(self): pass

# -----------------------------
# Data sources
# -----------------------------
def load_mpii_samples(max_samples=500):
    print("[MPII] Loading annotations...")
    data = scipy.io.loadmat(MPII_MAT, squeeze_me=True, struct_as_record=False)
    annolist = data["RELEASE"].annolist
    samples = []
    for ann in annolist:
        try:
            img_path = os.path.join(MPII_IMGS, ann.image.name)
            if not os.path.exists(img_path):
                continue
            if not hasattr(ann, "annorect"):
                continue
            rect = ann.annorect
            if isinstance(rect, (list, np.ndarray)):
                rect = rect[0]
            if not hasattr(rect, "annopoints"):
                continue
            points = rect.annopoints.point
            kps_raw = np.zeros((16, 2), dtype=np.float32)
            for p in (points if hasattr(points, '__iter__') else [points]):
                idx = int(p.id)
                if idx < 16:
                    kps_raw[idx] = [float(p.x), float(p.y)]
            samples.append((img_path, kps_raw))
            if len(samples) >= max_samples:
                break
        except Exception:
            continue
    print(f"[MPII] {len(samples)} samples loaded.")
    return samples

class VideoSource:
    def __init__(self, path_or_index):
        self.cap = cv2.VideoCapture(path_or_index)
        if not self.cap.isOpened():
            raise RuntimeError(f"Cannot open: {path_or_index}")

    def get_next(self):
        ret, frame = self.cap.read()
        if not ret:
            self.cap.set(cv2.CAP_PROP_POS_FRAMES, 0)
            ret, frame = self.cap.read()
        return (frame, None, None) if ret else (None, None, None)

    def release(self): self.cap.release()

class MPIIImageSource:
    """
    Sends the same fixed MPII image repeatedly.
    Models still run inference on it so metrics update normally.
    Workers use pre-computed GT so PCKh is real.
    """
    def __init__(self):
        self.frame = cv2.imread(MPII_IMAGE)
        if self.frame is None:
            raise RuntimeError(
                f"Cannot load MPII sample image: {MPII_IMAGE}\n"
                f"Pick any image from data/datasets/mpii/images/ and copy it there."
            )
        self.frame = cv2.resize(self.frame, (640, 480))
        print(f"[MPII] Loaded sample image: {MPII_IMAGE}")

    def get_next(self):
        # Always return the same frame, no GT for a single image
        return self.frame.copy(), None, None

    def release(self): pass
    
def build_source(mode):
    if mode == MODE_KAGGLE:
        return VideoSource(KAGGLE_VIDEO)
    elif mode == MODE_MPII:
        return MPIIImageSource()
    else:
        return VideoSource(0)

# -----------------------------
# Model worker thread
# -----------------------------
class ModelWorker:
    def __init__(self, idx, name, model, angle_triplets, send_sock, unity_addr):
        self.idx = idx
        self.name = name
        self.model = model
        self.sock = send_sock
        self.addr = unity_addr
        self.tracker = MetricsTracker(name, angle_triplets=angle_triplets)
        self.frame_q = queue.Queue(maxsize=1)
        self._stop = threading.Event()
        self._thread = threading.Thread(target=self._run, daemon=True)
        self._thread.start()

    def submit(self, frame, gt_kps, head_size):
        try:
            self.frame_q.get_nowait()
        except queue.Empty:
            pass
        self.frame_q.put((frame, gt_kps, head_size))

    def reset(self):
        self.tracker.reset()

    def stop(self):
        self._stop.set()

    def _run(self):
        last_metrics = 0.0
        while not self._stop.is_set():
            try:
                frame, gt_kps, head_size = self.frame_q.get(timeout=0.1)
            except queue.Empty:
                continue
            try:
                kps, conf = self.model.infer(frame)
            except Exception as e:
                print(f"[{self.name}] infer error: {e}")
                kps, conf = None, None

            self.tracker.update(kps, conf, gt_kps=gt_kps, head_size=head_size)

            if kps is not None:
                udp_send(self.sock, self.addr, make_keypoint_packet(self.idx, kps, conf))

            now = time.time()
            if now - last_metrics > 0.1:
                udp_send(self.sock, self.addr, make_metrics_packet(self.idx, self.tracker.get_metrics()))
                last_metrics = now

# -----------------------------
# Live benchmark
# -----------------------------
def run():
    threading.Thread(target=start_http_server, daemon=True).start()
    print(f"[HTTP] http://localhost:{HTTP_PORT}/ping to test")

    send_sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    unity_addr = (UNITY_IP, UNITY_PORT)

    print("Loading models...")
    workers = [
        ModelWorker(IDX_MEDIAPIPE, "MediaPipe", MediaPipeModel(), MP_ANGLE_TRIPLETS, send_sock, unity_addr),
        ModelWorker(IDX_YOLO, "YOLOv8", YOLOModel(), COCO_ANGLE_TRIPLETS, send_sock, unity_addr),
        ModelWorker(IDX_OPENPOSE, "OpenPose", OpenPoseModel(), COCO_ANGLE_TRIPLETS, send_sock, unity_addr),
    ]
    print("Models ready. Press Q to quit.")

    current_mode = MODE_LIVE
    source = build_source(current_mode)
    udp_send(send_sock, unity_addr, make_mode_packet(current_mode))

    while True:
        try:
            new_mode = _cmd_queue.get_nowait()
            if new_mode in (MODE_KAGGLE, MODE_LIVE, MODE_MPII) and new_mode != current_mode:
                print(f"[Mode] Switching to {['','Kaggle','Live','MPII'][new_mode]}")
                source.release()
                for w in workers:
                    w.reset()
                current_mode = new_mode
                source = build_source(current_mode)
                udp_send(send_sock, unity_addr, make_mode_packet(current_mode))
        except queue.Empty:
            pass

        frame, gt_kps, head_size = source.get_next()
        if frame is None:
            continue

        frame = cv2.resize(frame, (640, 480))

        for idx in (IDX_MEDIAPIPE, IDX_YOLO, IDX_OPENPOSE):
            udp_send(send_sock, unity_addr, make_frame_packet(idx, frame))

        for w in workers:
            w.submit(frame.copy(), gt_kps, head_size)

        cv2.imshow("Benchmark", frame)
        if cv2.waitKey(1) & 0xFF == ord('q'):
            break

    for w in workers:
        w.stop()
    source.release()
    cv2.destroyAllWindows()
    print("Done.")

# -----------------------------------------------------------------------
# Offline MPII evaluation — run once, saves results/mpii_results.json
# -----------------------------------------------------------------------
def run_mpii_eval(max_samples=500, output="results/mpii_results.json"):
    try:
        from tqdm import tqdm
        use_tqdm = True
    except ImportError:
        use_tqdm = False
        print("[MPII] tip: pip install tqdm for a progress bar")

    print("Loading models for MPII eval...")
    model_defs = [
        (IDX_MEDIAPIPE, "MediaPipe", MediaPipeModel(), MP_ANGLE_TRIPLETS),
        (IDX_YOLO, "YOLOv8", YOLOModel(), COCO_ANGLE_TRIPLETS),
        (IDX_OPENPOSE, "OpenPose", OpenPoseModel(), COCO_ANGLE_TRIPLETS),
    ]
    trackers = {
        name: MetricsTracker(name, angle_triplets=triplets)
        for _, name, _, triplets in model_defs
    }

    samples = load_mpii_samples(max_samples)
    iterable = tqdm(samples, desc="Evaluating") if use_tqdm else samples

    for img_path, kps_raw in iterable:
        frame = cv2.imread(img_path)
        if frame is None:
            continue

        h, w = frame.shape[:2]
        gt_coco, head_px = mapper.mpii_to_coco(kps_raw)
        gt_norm = gt_coco / np.array([w, h], dtype=np.float32)
        head_norm = head_px / np.sqrt(w**2 + h**2) if head_px else None

        for _, name, model, _ in model_defs:
            try:
                kps, conf = model.infer(frame)
            except Exception as e:
                print(f"[{name}] error: {e}")
                kps, conf = None, None
            trackers[name].update(kps, conf, gt_kps=gt_norm, head_size=head_norm)

    # Collect and save results
    results = {}
    print("\n" + "="*60)
    print(f"{'Model':<14} {'PCKh':>8} {'MAE°':>8} {'FPS':>7} {'Jitter':>8} {'Occ':>7}")
    print("-"*60)

    for _, name, _, _ in model_defs:
        m = trackers[name].get_metrics()
        results[name] = {
            "joint_angle": round(m["joint_angle"], 2),
            "pckh": round(m["pckh"], 1),
            "fps": round(m["fps"], 1),
            "jitter": round(m["jitter"], 2),
            "occlusion": round(m["occlusion"], 1),
        }
        print(f"{name:<14} {m['pckh']:>7.1f}% {m['joint_angle']:>6.2f}º"
              f"{m['fps']:>5.1f} {m['jitter']:>6.2f} {m['occlusion']:>5.1f}")

    print("="*60)

    os.makedirs(os.path.dirname(output) if os.path.dirname(output) else ".", exist_ok=True)
    with open(output, "w") as f:
        json.dump(results, f, indent=2)

    print(f"\n[MPII] Results saved to: {output}")
    print(f"[MPII] Copy this file to: Assets/StreamingAssets/mpii_results.json in Unity")

    for _, _, model, _ in model_defs:
        if hasattr(model, "close"):
            model.close()

# -----------------------------
# Entry point
# -----------------------------
if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--eval-mpii', action='store_true', help='Run offline MPII evaluation instead of live benchmark')
    parser.add_argument('--max-samples', type=int, default=500, help='Number of MPII images to evaluate (default 500)')
    parser.add_argument('--output', default='results/mpii_results.json', help='Output path for MPII results JSON')
    args = parser.parse_args()

    if args.eval_mpii:
        run_mpii_eval(args.max_samples, args.output)
    else:
        run()