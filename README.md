# Benchmark Pose Estimation Models

**Part of**: 3D Vision & AR Projects Repository
**Project Status**: Primary Implementation
**Repository Component**: Main Benchmarking Framework

A comprehensive framework for benchmarking and comparing multiple pose estimation models in real-world scenarios using augmented reality visualization. This project implements three state-of-the-art pose estimation models and provides an interactive Unity dashboard to visualize and analyze their performance metrics.

## Project Context

This benchmarking framework is the primary project within the 3D Vision & AR Projects repository. It demonstrates advanced techniques in:

- Computer Vision and Pose Estimation
- Augmented Reality Visualization
- Performance Benchmarking and Comparative Analysis
- Real-time Multi-model Inference

Developed as part of the Advanced 3D Vision course (Spring 2026) at Maynooth University.

## Project Overview

This is a comprehensive class project focused on benchmarking three pose estimation models used in real-world scenarios. The system combines a Python-based inference engine with a Unity AR application to provide:

- **Multi-model comparison**: Simultaneously run and compare MediaPipe, YOLOv26, and other pose estimation models
- **Real-time visualization**: Live skeletal overlay and pose visualization in Unity
- **Comprehensive metrics**: Track accuracy, latency, and robustness metrics
- **Multiple evaluation modes**: Live camera feed, Kaggle dataset, and MPII dataset benchmarking
- **Interactive dashboard**: Real-time comparison of model performance metrics

## Features

✅ **Three Pose Estimation Models**

- MediaPipe
- YOLOv26
- OpenPose

✅ **Multiple Benchmarking Modes**

- Live camera feed analysis
- Kaggle dataset evaluation
- MPII dataset offline evaluation
- Customizable sample limits

✅ **Real-Time Performance Metrics**

- Accuracy metrics (PCKh, etc.)
- Latency measurements
- Skeletal correctness validation
- Angle-based metrics (for pose quality)

✅ **Interactive Visualization**

- Real-time skeleton overlay on video frames
- Comparison dashboard with metrics
- Model selection and switching
- Graph visualization of metrics

✅ **Dual Architecture**

- Python backend for inference
- Unity frontend for visualization and interaction

## Project Structure

```
 3D_Vision_AR/
├── Benchmark_PoseEstimation_Models/    # Main project directory
│   ├── inference_engine/                # Python inference backend
│   │   ├── pose_benchmark.py            # Main benchmarking script
│   │   ├── metrics.py                   # Metrics calculation module
│   │   ├── skeleton_mapper.py           # Skeleton mapping utilities
│   │   ├── requirements.txt             # Python dependencies
│   │   ├── data/                        # Datasets directory
│   │   │   ├── datasets/                # MPII and Kaggle datasets
│   │   │   └── models/                  # Pre-trained model weights
│   │   └── results/                     # MPII Benchmark results (JSON)
│   │
│   └── unity_app/                       # Unity AR visualization frontend
│       ├── Assets/
│       │   ├── Scripts/                 # C# scripts for visualization
│       │   ├── Scenes/                  # Unity scenes
│       │   ├── Prefabs/                 # Reusable game objects
│       │   └── Settings/                # Project settings
│       ├── Packages/                    # Unity package dependencies
│       └── ProjectSettings/             # Unity project configuration
│
├── README.md                   
└── .git/                                # Version control
```

## Key Components

### Python Inference Engine

- **pose_benchmark.py**: Main script that runs pose estimation models
- **metrics.py**: Calculates performance metrics (accuracy, latency, etc.)
- **skeleton_mapper.py**: Maps skeleton joints between different model formats
- Supports UDP communication to send frames and keypoints to Unity
- Flask server for HTTP communication to switch between benchmarking modes

### Unity Application

- **PythonDataReceiver.cs**: Receives UDP data from Python backend
- **SkeletonOverlayRenderer.cs**: Renders skeleton overlays on video
- **ComparisonDashboard.cs**: Interactive UI for model comparison
- **PoseMetrics.cs**: Displays and tracks metrics in real-time
- **GraphPanel.cs**: Visualizes metrics in graph format

## Requirements

### Python

- Python 3.8+
- mediapipe >= 0.10.0
- ultralytics = 26.0.0 (YOLOv26)
- opencv-python >= 4.8.0
- numpy >= 1.24.0
- flask >= 2.3.0
- scipy >= 1.10.0
- tqdm >= 4.65.0

### Unity

- Unity 2022.3 LTS or later
- URP (Universal Render Pipeline)
- TextMesh Pro

## Installation

### Step 1: Clone the Repository

```bash
git clone https://github.com/yourusername/3D_Vision_AR.git
cd 3D_Vision_AR
```

### Step 2: Set Up Python Environment

```bash
# Navigate to the inference engine directory
cd Benchmark_PoseEstimation_Models/inference_engine

# Create a virtual environment (optional but recommended)
python -m venv venv
source venv/bin/activate  # On Windows: venv\Scripts\activate

# Install dependencies
pip install -r requirements.txt
```

### Step 3: Prepare Datasets

Place your datasets in the appropriate directories:

Already available in this repo:

- MPII dataset: `data/datasets/mpii/`
- Kaggle dataset: `data/datasets/kaggle/`
- Pre-trained models: `data/models/`

### Step 4: Open Unity Project

```bash
 # Navigate to the Unity app directory
cd ../../Benchmark_PoseEstimation_Models/unity_app

# Open with Unity Hub or directly:
# File -> Open Projects -> Add(drop down) -> Add project from disk -> Select this directory
```

## Usage

### Running the Benchmark

#### Mode 1: Live Camera Feed

```bash
cd Benchmark_PoseEstimation_Models/inference_engine
python pose_benchmark.py
```

This runs live pose estimation on your webcam and streams results to Unity.

#### Mode 2: Kaggle Dataset Evaluation

```bash
python pose_benchmark.py
# Then switch to Kaggle mode via the Unity dashboard
```

#### Mode 3: Offline MPII Dataset Evaluation

```bash
python pose_benchmark.py --eval-mpii
# Results are saved to results/mpii_results.json
```

With sample limit (faster testing):

```bash
python pose_benchmark.py --eval-mpii --max-samples 200
```

### Running the Unity Application

1. Open the project in Unity
2. Open the main scene from `Assets/Scenes/`
3. Press Play to start the visualization
4. The app will connect to the Python backend via UDP
5. Use the dashboard to compare models and view metrics

## Communication Protocol

### Python → Unity (UDP)

- Frames with skeleton overlays
- Joint keypoints (x, y, confidence)
- Performance metrics

### Unity → Python (HTTP GET)

- Mode switching (Live/Kaggle/MPII)
- Model selection
- Configuration changes

**Default Ports:**

- UDP: 5000 (frames)
- HTTP: 5006 (control)
- Server IP: 127.0.0.1 (localhost)

## Output and Results

### Real-Time Metrics

- **Accuracy**: Percentage of Correct Keypoints (PCKh)
- **Latency**: Inference time per frame (ms)
- **Confidence**: Average confidence scores per model
- **FPS**: Frames per second processed

### Offline Evaluation Results

Saved to `Benchmark_PoseEstimation_Models/inference_engine/results/mpii_results.json`

Format:

```json
{
  "model_name": {
    "joint_angle": 4.5,
    "pckh": 3.4,
    "fps": 0.1,
    "jitter": 108.2,
    "occlusion": 40.0
  }
}
```

## Performance Metrics

The project tracks multiple metrics to evaluate pose estimation quality:

- **PCK (Percentage of Correct Keypoints)**: Measures accuracy within a threshold
- **PCKh (Head-normalized PCK)**: Normalized accuracy for better comparison
- **Inference Latency**: Processing time per frame
- **Skeleton Validity**: Verification of joint relationships
- **Angle Metrics**: COCO and MediaPipe angle triplet validation

## Project Details

**Component**: Primary Project in 3D Vision & AR Repository
**Classification**: Benchmarking Framework for Pose Estimation

### Academic Details

- **Course**: Advanced 3D Vision
- **Institution**: Maynooth University
- **Semester**: Spring 2026 (Semester 02)
- **Project Type**: Class Project - Comparative Analysis & Implementation
- **Primary Developer**: Zeenath Ara Syed

### Technical Details

- **Technologies**: Python, C#, Unity, Computer Vision
- **Key Libraries**: MediaPipe, YOLOv26, OpenCV, Flask
- **Architecture**: Python Backend + Unity Frontend
- **Communication Protocol**: UDP (Streaming) + HTTP (Control)

### Repository Context

This project is the primary implementation within the broader **3D Vision & AR Projects Repository**, designed to showcase advanced techniques in computer vision and augmented reality visualization. It serves as a reference implementation for pose estimation benchmarking in academic and practical contexts.

## Troubleshooting

### UDP Connection Issues

- Ensure Python backend is running before starting Unity app
- Check that ports 5000 and 5006 are not blocked by firewall
- Verify both applications are on the same network

### Missing Dataset Files

- Download MPII dataset from official source
- Place in `data/datasets/mpii/`
- Ensure image directory structure matches expectations

### Model Loading Errors

- Verify pre-trained weights are in `data/models/`
- Check model paths in `pose_benchmark.py`
- Ensure all dependencies are correctly installed

## Future Enhancements

- [ ]  Add more pose estimation models (HRNet,etc.,)
- [ ]  Support for 3D pose estimation
- [ ]  Multi-person pose estimation improvements
- [ ]  Export results to various formats (CSV, Excel)
- [ ]  Cloud-based benchmarking
- [ ]  Mobile AR support

## Author

- Zeenath Ara Syed

## Project Context

This project is part of the **3D Vision & AR Projects Repository**, a comprehensive collection of computer vision and augmented reality implementations developed at Maynooth University.

### Related Documentation

- **Main Repository README**: See the parent directory [README.md](../README.md) for complete project overview and additional 3D Vision projects

## License

This project is part of a class assignment at Maynooth University. Please refer to your institution's guidelines for usage and distribution.

## References

- [MediaPipe Pose Documentation](https://google.github.io/mediapipe/solutions/pose)
- [YOLOv26 (YOLOv8) by Ultralytics](https://github.com/ultralytics/ultralytics)
- [MPII Human Pose Dataset](http://human-pose.mpi-inf.mpg.de/)
- [Kaggle Pose Estimation Datasets](https://www.kaggle.com/)
- [Unity Documentation](https://docs.unity3d.com/)
- [OpenCV: Computer Vision Library](https://docs.opencv.org/)

---
