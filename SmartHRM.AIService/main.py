import base64
from contextlib import asynccontextmanager
import os
import threading
import time
from typing import Dict, List, Optional

import cv2
from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import StreamingResponse
import insightface
from insightface.app import FaceAnalysis
import numpy as np
from pydantic import BaseModel
import requests
import urllib3
import uvicorn

# Disable SSL verification warnings for localhost ASP.NET Core MVC
urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)

# --- Configuration ---
MVC_BASE_URL = "https://localhost:58441"
API_GET_EMBEDDINGS = f"{MVC_BASE_URL}/LiveAttendance/GetKnownEmbeddings"
API_POST_DETECTION = f"{MVC_BASE_URL}/LiveAttendance/ProcessDetection"
CAMERA_ID = "CAM-MAIN-01"

# Matching threshold & operational configs
SIMILARITY_THRESHOLD = 0.35
FRAME_SKIP = 2
COOLDOWN_SECONDS = 300  # 5-minute cooldown per employee

WWWROOT_PATH = r"C:\Users\hp\Desktop\LAB\SmartHRM\SmartHRM.Presentation\wwwroot"

# --- 1. Global Model & Cache Initialization ---
print("[INFO] Loading InsightFace buffalo_s model...")
face_app = FaceAnalysis(name="buffalo_s", providers=["CPUExecutionProvider"])
# det_thresh=0.20 ensures faces in cropped or low-light photos are detected reliably
face_app.prepare(ctx_id=-1, det_size=(640, 640), det_thresh=0.20)
print("[INFO] InsightFace model initialized successfully.")

known_embeddings: Dict[int, np.ndarray] = {}
employee_profiles: Dict[int, dict] = {}
recent_posts: Dict[int, float] = {}
camera_running = True

# Thread-safe buffer for live web streaming
latest_processed_frame: Optional[np.ndarray] = None
frame_lock = threading.Lock()


# --- Helper Functions ---
def normalize_vector(vec: np.ndarray) -> np.ndarray:
    norm = np.linalg.norm(vec)
    return (vec / norm) if norm > 1e-6 else vec


def decode_base64_image(base64_str: str) -> Optional[np.ndarray]:
    try:
        if "," in base64_str:
            base64_str = base64_str.split(",")[1]
        img_bytes = base64.b64decode(base64_str)
        nparr = np.frombuffer(img_bytes, np.uint8)
        return cv2.imdecode(nparr, cv2.IMREAD_COLOR)
    except Exception as ex:
        print(f"[ERROR] Base64 decoding failed: {ex}")
        return None


def extract_embedding_from_file_or_url(photo_path: str) -> Optional[np.ndarray]:
    """Resolves Windows paths cleanly from wwwroot or falls back to HTTP."""
    img = None

    # Clean leading slashes so os.path.join does not treat it as root
    clean_rel = photo_path.strip("/\\").replace("/", os.sep).replace("\\", os.sep)
    full_local_path = os.path.join(WWWROOT_PATH, clean_rel)

    if os.path.exists(full_local_path):
        img = cv2.imread(full_local_path)
        if img is not None:
            print(f"    [+] Loaded reference image from disk: {full_local_path}")
    else:
        # Fallback HTTP
        try:
            url = f"{MVC_BASE_URL}/{photo_path.lstrip('/')}"
            res = requests.get(url, verify=False, timeout=3)
            if res.status_code == 200:
                nparr = np.frombuffer(res.content, np.uint8)
                img = cv2.imdecode(nparr, cv2.IMREAD_COLOR)
                print(f"    [+] Downloaded reference image via HTTP: {url}")
        except Exception as e:
            print(f"    [!] HTTP image fetch failed: {e}")

    if img is None:
        print(f"    [ERROR] Could not read image at: {full_local_path}")
        return None

    faces = face_app.get(img)
    if not faces:
        print(f"    [WARNING] No face detected in: {photo_path}")
        return None

    largest_face = max(faces, key=lambda f: (f.bbox[2] - f.bbox[0]) * (f.bbox[3] - f.bbox[1]))
    return normalize_vector(largest_face.embedding)


def load_mvc_embeddings():
    """Fetches vectors and metadata from ASP.NET Core MVC."""
    global known_embeddings, employee_profiles
    try:
        print(f"\n[INFO] Fetching embeddings from {API_GET_EMBEDDINGS}...")
        response = requests.get(API_GET_EMBEDDINGS, verify=False, timeout=5)
        if response.status_code == 200:
            data = response.json()
            known_embeddings.clear()
            employee_profiles.clear()

            for emp_id_str, info in data.items():
                emp_id = int(emp_id_str)
                name = info.get("fullName", f"Employee #{emp_id}")
                code = info.get("employeeCode", f"EMP{emp_id}")
                dept = info.get("department", "General")
                photo_path = info.get("photoPath", "")
                raw_vec = info.get("vector", [])

                vec = np.array(raw_vec, dtype=np.float32)

                # Case 1: Pre-computed vector present in SQL Server
                if len(vec) == 512:
                    known_embeddings[emp_id] = normalize_vector(vec)
                    employee_profiles[emp_id] = {"name": name, "code": code, "dept": dept}
                    print(f"  [+] Loaded DB Vector: {name} [{code}] (ID: {emp_id})")

                # Case 2: Extract vector directly from photoPath
                elif photo_path:
                    print(f"  [!] Missing vector for {name} [{code}]. Attempting extraction from photo...")
                    extracted = extract_embedding_from_file_or_url(photo_path)
                    if extracted is not None and len(extracted) == 512:
                        known_embeddings[emp_id] = extracted
                        employee_profiles[emp_id] = {"name": name, "code": code, "dept": dept}
                        print(f"  [SUCCESS] Vector extracted and cached for {name} [{code}]")

            print(f"[READY] Total active recognition targets in memory: {len(known_embeddings)}\n")
        else:
            print(f"[ERROR] Failed to fetch embeddings: HTTP {response.status_code}")
    except Exception as e:
        print(f"[ERROR] Could not connect to ASP.NET MVC endpoint: {e}")


def get_camera_stream():
    """Tries DirectShow, MSMF, and standard capture across indices 0 to 3."""
    for idx in [0, 1, 2, 3]:
        for backend in [cv2.CAP_DSHOW, cv2.CAP_MSMF, cv2.CAP_ANY]:
            cap = cv2.VideoCapture(idx, backend)
            if cap.isOpened():
                time.sleep(0.4)
                ret, frame = cap.read()
                if ret and frame is not None and frame.size > 0:
                    cap.set(cv2.CAP_PROP_FRAME_WIDTH, 640)
                    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 480)
                    print(f"[SUCCESS] Camera acquired on Device Index: {idx} | Backend: {backend}")
                    return cap
                cap.release()
    return None


# --- 2. Live Camera Recognition Loop ---
def camera_stream_worker():
    global camera_running, recent_posts, latest_processed_frame
    load_mvc_embeddings()

    cap = get_camera_stream()
    if cap is None or not cap.isOpened():
        print("[ERROR] Could not initialize webcam. Ensure no other application or tab is locking the camera.")
        return

    print("[INFO] Camera processing loop active.")
    frame_count = 0

    while camera_running:
        ret, frame = cap.read()
        if not ret or frame is None:
            time.sleep(0.01)
            continue

        frame_count += 1

        if frame_count % FRAME_SKIP == 0:
            faces = face_app.get(frame)

            for face in faces:
                bbox = [int(x) for x in face.bbox]
                current_vec = normalize_vector(face.embedding)

                best_match_id = None
                best_score = 0.0

                # Cosine similarity against loaded employee vectors
                for emp_id, stored_vec in known_embeddings.items():
                    score = float(np.dot(current_vec, stored_vec))
                    if score > best_score:
                        best_score = score
                        best_match_id = emp_id

                # Real-time console diagnostics
                if best_match_id is not None:
                    emp_info = employee_profiles.get(best_match_id, {})
                    emp_name = emp_info.get("name", f"Emp #{best_match_id}")
                    print(f"[MATCH SCORE] Candidate: {emp_name} | Score: {best_score * 100:.1f}% | Required: {SIMILARITY_THRESHOLD * 100:.1f}%")

                if best_match_id is not None and best_score >= SIMILARITY_THRESHOLD:
                    emp_info = employee_profiles.get(best_match_id, {})
                    emp_name = emp_info.get("name", f"Emp #{best_match_id}")
                    emp_code = emp_info.get("code", "")

                    # Draw Green Bounding Box
                    label = f"{emp_name} ({best_score * 100:.1f}%)"
                    cv2.rectangle(frame, (bbox[0], bbox[1]), (bbox[2], bbox[3]), (0, 255, 0), 2)
                    cv2.putText(
                        frame,
                        label,
                        (bbox[0], max(25, bbox[1] - 10)),
                        cv2.FONT_HERSHEY_SIMPLEX,
                        0.55,
                        (0, 255, 0),
                        2,
                    )

                    # Post Detection to ASP.NET Core MVC (Respecting cooldown)
                    now = time.time()
                    if now - recent_posts.get(best_match_id, 0) > COOLDOWN_SECONDS:
                        payload = {
                            "EmployeeId": int(best_match_id),
                            "CameraId": CAMERA_ID,
                            "ConfidenceScore": round(best_score * 100, 2),
                        }
                        try:
                            res = requests.post(API_POST_DETECTION, json=payload, verify=False, timeout=2)
                            if res.status_code == 200:
                                recent_posts[best_match_id] = now
                                print(f"[ATTENDANCE LOGGED] {emp_name} [{emp_code}] - Score: {best_score * 100:.1f}%")
                        except Exception as ex:
                            print(f"[ERROR] Failed to post detection to MVC: {ex}")
                else:
                    # Draw Red Bounding Box for Unknown / Below Threshold
                    score_str = f" ({best_score * 100:.1f}%)" if best_score > 0 else ""
                    cv2.rectangle(frame, (bbox[0], bbox[1]), (bbox[2], bbox[3]), (0, 0, 255), 2)
                    cv2.putText(
                        frame,
                        f"Unrecognized{score_str}",
                        (bbox[0], max(25, bbox[1] - 10)),
                        cv2.FONT_HERSHEY_SIMPLEX,
                        0.55,
                        (0, 0, 255),
                        2,
                    )

        # Store latest frame for the web browser MJPEG stream
        with frame_lock:
            latest_processed_frame = frame.copy()

    cap.release()


# --- 3. MJPEG Video Stream Generator ---
def generate_mjpeg_frames():
    global latest_processed_frame, camera_running
    while camera_running:
        with frame_lock:
            if latest_processed_frame is None:
                time.sleep(0.04)
                continue
            ret, buffer = cv2.imencode(".jpg", latest_processed_frame)
            if not ret:
                continue
            frame_bytes = buffer.tobytes()

        yield (b"--frame\r\n"
               b"Content-Type: image/jpeg\r\n\r\n" + frame_bytes + b"\r\n")
        time.sleep(0.035)


# --- 4. FastAPI Service ---
@asynccontextmanager
async def lifespan(app: FastAPI):
    global camera_running
    camera_running = True
    cam_thread = threading.Thread(target=camera_stream_worker, daemon=True)
    cam_thread.start()
    yield
    camera_running = False


app = FastAPI(title="SmartHRM Face Recognition AI Service", lifespan=lifespan)

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)


@app.get("/video_feed")
def video_feed():
    """Streams the live AI camera directly to the browser Razor view."""
    return StreamingResponse(
        generate_mjpeg_frames(),
        media_type="multipart/x-mixed-replace; boundary=frame"
    )


@app.post("/reload-embeddings")
async def reload_embeddings():
    """Refreshes memory embeddings after creating a new employee."""
    load_mvc_embeddings()
    return {"status": "synced", "total_registered": len(known_embeddings)}


class FrameBatchRequest(BaseModel):
    images: List[str]


@app.post("/extract-embedding")
async def extract_master_embedding(request: FrameBatchRequest):
    """Averages 20 capture frames from employee creation into a single 512-d vector."""
    embeddings = []

    for frame_b64 in request.images:
        img = decode_base64_image(frame_b64)
        if img is None:
            continue
        faces = face_app.get(img)
        if faces:
            largest = max(faces, key=lambda f: (f.bbox[2] - f.bbox[0]) * (f.bbox[3] - f.bbox[1]))
            embeddings.append(normalize_vector(largest.embedding))

    if not embeddings:
        raise HTTPException(status_code=400, detail="No faces detected in the provided images.")

    master_vec = normalize_vector(np.mean(embeddings, axis=0))
    return {"embedding": master_vec.tolist()}


if __name__ == "__main__":
    uvicorn.run(app, host="127.0.0.1", port=8000)