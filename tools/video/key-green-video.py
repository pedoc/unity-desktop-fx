#!/usr/bin/env python3
# -*- coding: utf-8 -*-

"""Generate synchronized corrected RGB frames and alpha masks from green screen."""
from __future__ import annotations

import argparse
from pathlib import Path
import cv2
import mediapipe as mp
import numpy as np


def write_image(path: Path, image: np.ndarray, extension: str = '.png', params: list[int] | None = None) -> None:
    ok, encoded = cv2.imencode(extension, image, params or [])
    if not ok:
        raise RuntimeError(f'Cannot encode {path}')
    encoded.tofile(str(path))


def build_outputs(frame_bgr: np.ndarray, previous: np.ndarray | None, subject_mask: np.ndarray | None) -> tuple[np.ndarray, np.ndarray]:
    frame = frame_bgr.astype(np.float32)
    b, g, r = cv2.split(frame)
    red_blue_max = np.maximum(r, b)
    chroma = g - red_blue_max

    low = 8.0
    high = 28.0
    alpha = 1.0 - np.clip((chroma - low) / (high - low), 0.0, 1.0)
    alpha = cv2.GaussianBlur(alpha, (0, 0), 0.7)
    if previous is not None:
        delta = np.abs(alpha - previous)
        stable = 0.18 * np.exp(-delta * 10.0)
        alpha = alpha * (1.0 - stable) + previous * stable
    alpha = np.clip(alpha, 0.0, 1.0)
    if subject_mask is not None:
        subject_mask = cv2.GaussianBlur(np.clip(subject_mask, 0.0, 1.0), (0, 0), 0.7)
        alpha = np.minimum(alpha, subject_mask)

    # Green-spill suppression: only reduce green when it is actually stronger
    # than both red and blue, preserving skin and purple clothing.
    green_excess = np.clip(g - red_blue_max, 0.0, 255.0)
    spill_strength = 1.0 * np.clip((chroma - 3.0) / 24.0, 0.0, 1.0)
    corrected = frame.copy()
    edge = alpha > 0.02
    corrected[..., 1] = np.where(
        edge,
        np.minimum(g - green_excess * spill_strength, red_blue_max + 2.0),
        g,
    )
    corrected = np.clip(corrected, 0.0, 255.0).astype(np.uint8)
    return alpha, corrected


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument('--input', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--masks', required=True, type=Path)
    parser.add_argument('--frames', required=True, type=Path)
    args = parser.parse_args()

    args.masks.mkdir(parents=True, exist_ok=True)
    args.frames.mkdir(parents=True, exist_ok=True)
    cap = cv2.VideoCapture(str(args.input))
    if not cap.isOpened():
        raise RuntimeError(f'Cannot open {args.input}')

    frame_count = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
    fps = float(cap.get(cv2.CAP_PROP_FPS) or 30.0)
    width = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
    height = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
    previous = None
    written = 0
    with mp.solutions.selfie_segmentation.SelfieSegmentation(model_selection=1) as segmenter:
        while True:
            ok, frame = cap.read()
            if not ok:
                break
            result = segmenter.process(cv2.cvtColor(frame, cv2.COLOR_BGR2RGB))
            subject_mask = result.segmentation_mask if result.segmentation_mask is not None else None
            alpha, corrected = build_outputs(frame, previous, subject_mask)
            previous = alpha
            number = f'{written + 1:06d}'
            write_image(args.masks / f'{number}.png', np.round(alpha * 255).astype(np.uint8))
            write_image(args.frames / f'{number}.jpg', corrected, '.jpg', [cv2.IMWRITE_JPEG_QUALITY, 98])
            written += 1
            if written % 60 == 0:
                print(f'Processed {written}/{frame_count} frames', flush=True)
    cap.release()
    print(f'Completed {args.input.name}: {width}x{height} @ {fps:.2f} fps, frames={written}', flush=True)
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
