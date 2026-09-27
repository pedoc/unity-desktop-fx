#!/usr/bin/env python3
# -*- coding: utf-8 -*-

"""Create a temporally stable person alpha-mask sequence for a video clip.

The script intentionally uses MediaPipe Selfie Segmentation as a lightweight
local prototype path. It keeps the largest center-connected subject, refines
edges with a guided filter, and applies motion-aware temporal smoothing.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path

import cv2
import mediapipe as mp
import numpy as np


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--start", type=float, default=0.0)
    parser.add_argument("--duration", type=float, default=5.5)
    parser.add_argument("--model-selection", type=int, choices=(0, 1), default=1)
    return parser.parse_args()


def keep_primary_subject(mask: np.ndarray) -> np.ndarray:
    binary = (mask >= 0.18).astype(np.uint8)
    count, labels, stats, centroids = cv2.connectedComponentsWithStats(binary, connectivity=8)
    if count <= 1:
        return mask

    height, width = mask.shape
    center = np.array([width * 0.5, height * 0.52], dtype=np.float32)
    best_label = 0
    best_score = -1.0
    for label in range(1, count):
        area = float(stats[label, cv2.CC_STAT_AREA])
        if area < width * height * 0.002:
            continue
        distance = float(np.linalg.norm(centroids[label] - center))
        center_weight = max(0.15, 1.0 - distance / (width * 0.75))
        score = area * center_weight
        if score > best_score:
            best_score = score
            best_label = label

    if best_label == 0:
        return mask

    support = (labels == best_label).astype(np.uint8)
    support = cv2.dilate(support, np.ones((9, 9), np.uint8), iterations=2)
    return mask * support.astype(np.float32)


def refine_mask(frame_bgr: np.ndarray, raw_mask: np.ndarray, previous: np.ndarray | None) -> np.ndarray:
    mask = np.clip(raw_mask.astype(np.float32), 0.0, 1.0)
    mask = keep_primary_subject(mask)

    guide = cv2.cvtColor(frame_bgr, cv2.COLOR_BGR2GRAY).astype(np.float32) / 255.0
    mask = cv2.ximgproc.guidedFilter(guide=guide, src=mask, radius=7, eps=0.0015)
    mask = np.clip((mask - 0.06) / 0.88, 0.0, 1.0)

    if previous is not None:
        difference = np.abs(mask - previous)
        stable_weight = 0.42 * np.exp(-difference * 12.0)
        mask = mask * (1.0 - stable_weight) + previous * stable_weight

    mask = cv2.GaussianBlur(mask, (0, 0), 0.75)

    # The reference recording contains its own taskbar over the lower torso.
    # Fade the final strip so the user's real taskbar can cover the character.
    fade_start = int(mask.shape[0] * 0.925)
    fade_end = int(mask.shape[0] * 0.96)
    if fade_end > fade_start:
        fade = np.ones(mask.shape[0], dtype=np.float32)
        fade[fade_start:fade_end] = np.linspace(1.0, 0.0, fade_end - fade_start, dtype=np.float32)
        fade[fade_end:] = 0.0
        mask *= fade[:, None]

    return np.clip(mask, 0.0, 1.0)



def write_image(path: Path, image: np.ndarray) -> None:
    extension = path.suffix or ".png"
    ok, encoded = cv2.imencode(extension, image)
    if not ok:
        raise RuntimeError(f"Cannot encode image: {path}")
    encoded.tofile(str(path))

def checkerboard(height: int, width: int) -> np.ndarray:
    yy, xx = np.indices((height, width))
    cells = ((xx // 24 + yy // 24) % 2).astype(np.uint8)
    base = np.where(cells[..., None] == 0, 72, 112).astype(np.uint8)
    return np.repeat(base, 3, axis=2)


def main() -> int:
    args = parse_args()
    input_path = args.input.resolve()
    output_root = args.output.resolve()
    masks_dir = output_root / "masks"
    previews_dir = output_root / "previews"
    masks_dir.mkdir(parents=True, exist_ok=True)
    previews_dir.mkdir(parents=True, exist_ok=True)

    capture = cv2.VideoCapture(str(input_path))
    if not capture.isOpened():
        raise RuntimeError(f"Cannot open video: {input_path}")

    fps = float(capture.get(cv2.CAP_PROP_FPS) or 30.0)
    total_frames = int(round(args.duration * fps))
    capture.set(cv2.CAP_PROP_POS_MSEC, args.start * 1000.0)

    segmenter_class = mp.solutions.selfie_segmentation.SelfieSegmentation
    previous: np.ndarray | None = None
    written = 0
    preview_indices = {0, max(0, total_frames // 4), max(0, total_frames // 2), max(0, total_frames - 1)}

    with segmenter_class(model_selection=args.model_selection) as segmenter:
        for frame_index in range(total_frames):
            ok, frame = capture.read()
            if not ok:
                break

            rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            result = segmenter.process(rgb)
            if result.segmentation_mask is None:
                raise RuntimeError(f"No segmentation mask at frame {frame_index}")

            mask = refine_mask(frame, result.segmentation_mask, previous)
            previous = mask
            alpha_u8 = np.round(mask * 255.0).astype(np.uint8)
            mask_path = masks_dir / f"{frame_index + 1:06d}.png"
            write_image(mask_path, alpha_u8)

            if frame_index in preview_indices:
                background = checkerboard(frame.shape[0], frame.shape[1])
                alpha = mask[..., None]
                composite = np.clip(frame * alpha + background * (1.0 - alpha), 0, 255).astype(np.uint8)
                preview = np.hstack((frame, cv2.cvtColor(alpha_u8, cv2.COLOR_GRAY2BGR), composite))
                write_image(previews_dir / f"preview-{frame_index:04d}.jpg", preview)

            written += 1
            if written % 30 == 0:
                print(f"Processed {written}/{total_frames} frames", flush=True)

    capture.release()
    metadata = {
        "source": str(input_path),
        "startSeconds": args.start,
        "durationSeconds": written / fps,
        "fps": fps,
        "frames": written,
        "width": int(capture.get(cv2.CAP_PROP_FRAME_WIDTH) or 1280),
        "height": int(capture.get(cv2.CAP_PROP_FRAME_HEIGHT) or 720),
        "model": "MediaPipe Selfie Segmentation landscape",
    }
    (output_root / "matting-result.json").write_text(
        json.dumps(metadata, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    print(json.dumps(metadata, ensure_ascii=False), flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
