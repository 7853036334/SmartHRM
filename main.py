import base64
from contextlib import asynccontextmanager
import json
import os
import threading
import time
from typing import Dict, List, Optional

import cv2
from fastapi import FastAPI, HTTPException, Response
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import StreamingResponse
import insightface
from insightface.app import FaceAnalysis
import numpy as np
from pydantic import BaseModel
import requests
import urllib3
import uvicorn

urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)

# --- Configuration ---
MVC_BASE_URL = "https://localhost:58441"
API_GET_EMBEDDINGS = f"{MVC_BASE_URL}/LiveAttendance/GetKnownEmbeddings"
API_POST_DETECTION = f"{MVC_BASE_URL}/LiveAttendance/ProcessDetection"
CAMERA_ID = "CAM-MAIN-01"

# Threshold lowered for typical built-in laptop webcams
SIMILARITY_THRESHOLD = 0.25
FRAME_SKIP = 2
COOLDOWN_SECONDS = 5  # 5 seconds during testing so live updates register quickly
WWWROOT_PATH = r"C:\Users\hp\Desktop\LAB\SmartHRM\SmartHRM.Presentation\wwwroot"

print("[INFO] Initializing InsightFace buffalo_s model...")
face_app = FaceAnalysis(name="buffalo_s", providers=["CPUExecutionProvider"])
face_app.prepare(ctx_id=-1, det_size=(640, 640), det_thresh=0.20)
print("[INFO] InsightFace Model Loaded Successfully.")

# Global state
known_embeddings: Dict[int, np.ndarray] = {}
employee_profiles: Dict[int, dict] = {}
recent_posts: Dict[int, float] = {}

camera_running = True
clean_frame: Optional[np.ndarray] = None
annotated_frame: Optional[np.ndarray] = None
frame_lock = threading.Lock()


def l2_normalize(vec: np.ndarray) -> np.ndarray:
    norm = np.linalg.norm(vec)
    if norm < 1e-6:
        return vec
    return vec / norm


def calculate_cosine_similarity(vec1: np.ndarray, vec2: np.ndarray) -> float:
    v1 = l2_normalize(vec1)
    v2 = l2_normalize(vec2)
    return float(np.dot(v1, v2))


def decode_base64_image(base64_str: str) -> Optional[np.ndarray]:
    try:
        if "," in base64_str:
            base64_str = base64_str.split(",")[1]
        img_bytes = base64.b64decode(base64_str)
        nparr = np.frombuffer(img_bytes, np.uint8)
        return cv2.imdecode(nparr, cv2.IMREAD_COLOR)
    except Exception as ex:
        print(f"[ERROR] Base64 decode failed: {ex}")
        return None


def extract_embedding_from_file_or_url(photo_path: str) -> Optional[np.ndarray]:
    img = None
    clean_rel = photo_path.strip("~/\\").replace("/", os.sep).replace("\\", os.sep)
    full_local_path = os.path.join(WWWROOT_PATH, clean_rel)

    if os.path.exists(full_local_path):
        img = cv2.imread(full_local_path)
    else:
        try:
            url = f"{MVC_BASE_URL}/{clean_rel.replace(os.sep, '/')}"
            res = requests.get(url, verify=False, timeout=3)
            if res.status_code == 200:
                nparr = np.frombuffer(res.content, np.uint8)
                img = cv2.imdecode(nparr, cv2.IMREAD_COLOR)
        except Exception as e:
            print(f"[!] HTTP image pull failed for {photo_path}: {e}")

    if img is None:
        print(f"[ERROR] Could not load image file: {full_local_path}")
        return None

    faces = face_app.get(img)
    if not faces:
        print(f"[WARNING] No face detected in reference image: {photo_path}")
        return None

    largest = max(faces, key=lambda f: (f.bbox[2] - f.bbox[0]) * (f.bbox[3] - f.bbox[1]))
    return l2_normalize(largest.embedding)


def load_mvc_embeddings():
    global known_embeddings, employee_profiles
    try:
        print(f"[INFO] Fetching enrolled embeddings from: {API_GET_EMBEDDINGS}")
        response = requests.get(API_GET_EMBEDDINGS, verify=False, timeout=5)
        if response.status_code == 200:
            data = response.json()
            known_embeddings.clear()
            employee_profiles.clear()

            for emp_id_str, info in data.items():
                emp_id = int(emp_id_str)
                name = info.get("fullName", f"Employee #{emp_id}")
                code = info.get("employeeCode", f"EMP{emp_id}")
                photo_path = info.get("photoPath", "")
                raw_vec = info.get("vector")

                if isinstance(raw_vec, str):
                    try:
                        raw_vec = json.loads(raw_vec)
                    except Exception:
                        raw_vec = []

                vec = np.array(raw_vec, dtype=np.float32) if raw_vec else np.array([])

                if len(vec) == 512:
                    known_embeddings[emp_id] = l2_normalize(vec)
                    employee_profiles[emp_id] = {"name": name, "code": code}
                    print(f"  [+] Loaded DB Vector: {name} [{code}]")
                elif photo_path:
                    extracted = extract_embedding_from_file_or_url(photo_path)
                    if extracted is not None and len(extracted) == 512:
                        known_embeddings[emp_id] = extracted
                        employee_profiles[emp_id] = {"name": name, "code": code}
                        print(f"  [+] Extracted Disk Vector: {name} [{code}]")

            print(f"[READY] Recognition targets active in memory: {len(known_embeddings)}")
        else:
            print(f"[ERROR] MVC endpoint returned status code: {response.status_code}")
    except Exception as e:
        print(f"[ERROR] Could not connect to ASP.NET MVC backend: {e}")


def get_camera():
    for idx in [0, 1]:
        for backend in [cv2.CAP_DSHOW, cv2.CAP_MSMF, cv2.CAP_ANY]:
            cap = cv2.VideoCapture(idx, backend)
            if cap.isOpened():
                time.sleep(0.2)
                ret, frame = cap.read()
                if ret and frame is not None and frame.size > 0:
                    cap.set(cv2.CAP_PROP_FRAME_WIDTH, 640)
                    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 480)
                    print(f"[SUCCESS] Physical camera initialized on Index: {idx}")
                    return cap
                cap.release()
    return None


def camera_stream_worker():
    global camera_running, clean_frame, annotated_frame, recent_posts
    load_mvc_embeddings()

    cap = get_camera()
    if cap is None:
        print("[ERROR] Failed to open any camera. Verify no other app has locked the webcam.")
        return

    frame_count = 0

    while camera_running:
        ret, frame = cap.read()
        if not ret or frame is None:
            time.sleep(0.01)
            continue

        raw_copy = frame.copy()
        display_frame = frame.copy()
        frame_count += 1

        if frame_count % FRAME_SKIP == 0:
            faces = face_app.get(frame)

            for face in faces:
                bbox = [int(x) for x in face.bbox]
                current_vec = face.embedding

                best_match_id = None
                best_score = -1.0

                for emp_id, stored_vec in known_embeddings.items():
                    score = calculate_cosine_similarity(current_vec, stored_vec)
                    if score > best_score:
                        best_score = score
                        best_match_id = emp_id

                if best_match_id is not None:
                    emp_name = employee_profiles.get(best_match_id, {}).get("name", f"Emp #{best_match_id}")
                    print(f"[MATCH SCORE] Candidate: {emp_name} | Score: {best_score * 100:.1f}% | Required: {SIMILARITY_THRESHOLD * 100:.1f}%")

                if best_match_id is not None and best_score >= SIMILARITY_THRESHOLD:
                    emp_name = employee_profiles.get(best_match_id, {}).get("name", f"Emp #{best_match_id}")

                    # Green detection box
                    cv2.rectangle(display_frame, (bbox[0], bbox[1]), (bbox[2], bbox[3]), (0, 255, 0), 2)
                    cv2.putText(
                        display_frame,
                        f"{emp_name} ({best_score * 100:.1f}%)",
                        (bbox[0], max(25, bbox[1] - 10)),
                        cv2.FONT_HERSHEY_SIMPLEX,
                        0.55,
                        (0, 255, 0),
                        2
                    )

                    now = time.time()
                    if now - recent_posts.get(best_match_id, 0) > COOLDOWN_SECONDS:
                        recent_posts[best_match_id] = now
                        payload = {
                            "EmployeeId": int(best_match_id),
                            "CameraId": CAMERA_ID,
                            "ConfidenceScore": round(best_score * 100, 2)
                        }
                        
                        def post_to_mvc(data_payload):
                            try:
                                res = requests.post(API_POST_DETECTION, json=data_payload, verify=False, timeout=2)
                                if res.status_code == 200:
                                    print(f"[POST SUCCESS] Attendance logged for ID {data_payload['EmployeeId']}")
                            except Exception as ex:
                                print(f"[POST ERROR] MVC update failed: {ex}")

                        threading.Thread(target=post_to_mvc, args=(payload,), daemon=True).start()
                else:
                    score_label = f" ({best_score * 100:.1f}%)" if best_score > 0 else ""
                    # Red detection box
                    cv2.rectangle(display_frame, (bbox[0], bbox[1]), (bbox[2], bbox[3]), (0, 0, 255), 2)
                    cv2.putText(
                        display_frame,
                        f"Unrecognized{score_label}",
                        (bbox[0], max(25, bbox[1] - 10)),
                        cv2.FONT_HERSHEY_SIMPLEX,
                        0.55,
                        (0, 0, 255),
                        2
                    )

        with frame_lock:
            clean_frame = raw_copy
            annotated_frame = display_frame

    cap.release()


def generate_mjpeg_frames():
    global annotated_frame, camera_running
    while camera_running:
        with frame_lock:
            if annotated_frame is None:
                time.sleep(0.03)
                continue
            ret, buffer = cv2.imencode(".jpg", annotated_frame)
            if not ret:
                continue
            frame_bytes = buffer.tobytes()

        yield (b"--frame\r\n"
               b"Content-Type: image/jpeg\r\n\r\n" + frame_bytes + b"\r\n")
        time.sleep(0.03)


@asynccontextmanager
async def lifespan(app: FastAPI):
    global camera_running
    camera_running = True
    cam_thread = threading.Thread(target=camera_stream_worker, daemon=True)
    cam_thread.start()
    yield
    camera_running = False


app = FastAPI(title="SmartHRM Face Recognition Service", lifespan=lifespan)

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)


@app.get("/video_feed")
def video_feed():
    headers = {
        "Cache-Control": "no-cache, no-store, must-revalidate",
        "Pragma": "no-cache",
        "Expires": "0",
        "Access-Control-Allow-Origin": "*"
    }
    return StreamingResponse(
        generate_mjpeg_frames(),
        media_type="multipart/x-mixed-replace; boundary=frame",
        headers=headers
    )


@app.get("/capture-single-frame")
def capture_single_frame():
    global clean_frame, annotated_frame
    with frame_lock:
        target = clean_frame if clean_frame is not None else annotated_frame
        if target is None:
            raise HTTPException(status_code=503, detail="Camera frame not ready")
        ret, buffer = cv2.imencode(".jpg", target)
        if not ret:
            raise HTTPException(status_code=500, detail="Encoding failed")
        b64 = base64.b64encode(buffer.tobytes()).decode("utf-8")
        return {"image": f"data:image/jpeg;base64,{b64}"}


@app.post("/reload-embeddings")
async def reload_embeddings():
    load_mvc_embeddings()
    return {"status": "synced", "total_registered": len(known_embeddings)}


class FrameBatchRequest(BaseModel):
    images: List[str]


@app.post("/extract-embedding")
async def extract_master_embedding(request: FrameBatchRequest):
    embeddings = []
    for frame_b64 in request.images:
        img = decode_base64_image(frame_b64)
        if img is None:
            continue
        faces = face_app.get(img)
        if faces:
            largest = max(faces, key=lambda f: (f.bbox[2] - f.bbox[0]) * (f.bbox[3] - f.bbox[1]))
            embeddings.append(l2_normalize(largest.embedding))

    if not embeddings:
        raise HTTPException(status_code=400, detail="No faces detected in the provided images.")

    master_vec = l2_normalize(np.mean(embeddings, axis=0))
    return {"embedding": master_vec.tolist()}
import cv2
from fastapi.responses import Response

@app.get("/snapshot")
def get_snapshot():
    success, frame = camera.read()
    if not success:
        return Response(status_code=503)
    _, buffer = cv2.imencode('.jpg', frame)
    return Response(content=buffer.tobytes(), media_type="image/jpeg")


if __name__ == "__main__":
    uvicorn.run(app, host="127.0.0.1", port=8000)