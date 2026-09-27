#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Extract normalized left/right palm tracks from a character performance clip."""
from __future__ import annotations
import argparse, json
from pathlib import Path
import cv2
import mediapipe as mp

def palm_center(landmarks):
    points=[landmarks.landmark[i] for i in (0,5,9,13,17)]
    return {"x":sum(p.x for p in points)/len(points),"y":sum(p.y for p in points)/len(points),"confidence":1.0}

def main():
    ap=argparse.ArgumentParser(); ap.add_argument('--input',required=True,type=Path); ap.add_argument('--output',required=True,type=Path); a=ap.parse_args()
    cap=cv2.VideoCapture(str(a.input))
    if not cap.isOpened(): raise RuntimeError(f'Cannot open {a.input}')
    fps=float(cap.get(cv2.CAP_PROP_FPS) or 30.0); width=int(cap.get(cv2.CAP_PROP_FRAME_WIDTH)); height=int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT)); frames=[]
    with mp.solutions.hands.Hands(static_image_mode=False,max_num_hands=2,model_complexity=1,min_detection_confidence=.35,min_tracking_confidence=.35) as hands:
        i=0
        while True:
            ok,frame=cap.read()
            if not ok: break
            result=hands.process(cv2.cvtColor(frame,cv2.COLOR_BGR2RGB)); left=None; right=None
            if result.multi_hand_landmarks and result.multi_handedness:
                for lm,handed in zip(result.multi_hand_landmarks,result.multi_handedness):
                    center=palm_center(lm); label=handed.classification[0].label
                    if label=='Left': left=center
                    elif label=='Right': right=center
            frames.append({'time':round(i/fps,6),'left':left,'right':right}); i+=1
    cap.release(); a.output.parent.mkdir(parents=True,exist_ok=True)
    a.output.write_text(json.dumps({'version':1,'source':str(a.input),'fps':fps,'width':width,'height':height,'frames':frames},ensure_ascii=False,indent=2),encoding='utf-8')
    print(f'Hand track written: {a.output}; frames={len(frames)}')

if __name__=='__main__': raise SystemExit(main())
