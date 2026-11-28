#!/usr/bin/env python3
"""Generates every ZeroPad icon from one description: SVG, PNG, Windows .ico, Android adaptive-icon vectors.
Run from the repo root:  python3 assets/make_icon.py   (needs Pillow and rsvg-convert)"""
import subprocess
from PIL import Image

BG, BODY, INK = "#0B0D10", "#4C8DFF", "#0B0D10"

def rrect(x, y, w, h, r):
    return (f"M{x+r},{y} H{x+w-r} A{r},{r} 0 0 1 {x+w},{y+r} V{y+h-r} A{r},{r} 0 0 1 {x+w-r},{y+h} "
            f"H{x+r} A{r},{r} 0 0 1 {x},{y+h-r} V{y+r} A{r},{r} 0 0 1 {x+r},{y} Z")

def circle(cx, cy, r):
    return f"M{cx-r},{cy} A{r},{r} 0 1 0 {cx+r},{cy} A{r},{r} 0 1 0 {cx-r},{cy} Z"

body = [rrect(22, 38, 64, 30, 15), circle(35, 67, 11.5), circle(73, 67, 11.5)]
details = [rrect(32, 50.5, 12, 5, 2.2), rrect(35.5, 47, 5, 12, 2.2),                      # d-pad
           circle(72, 47.5, 2.8), circle(72, 58.5, 2.8), circle(66.5, 53, 2.8), circle(77.5, 53, 2.8),  # ABXY
           circle(54, 53, 1.9)]
SHAPES = [(d, BODY) for d in body] + [(d, INK) for d in details]

svg = f'''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 108 108" width="512" height="512">
  <rect width="108" height="108" rx="24" fill="{BG}"/>
  <g transform="translate(54 54) scale(1.4) translate(-54 -54)">
''' + "\n".join(f'    <path d="{d}" fill="{c}"/>' for d, c in SHAPES) + "\n  </g>\n</svg>\n"
open("assets/icon.svg", "w").write(svg)
subprocess.run(["rsvg-convert", "-w", "512", "-h", "512", "assets/icon.svg", "-o", "assets/icon-512.png"], check=True)
img = Image.open("assets/icon-512.png").convert("RGBA")
img.save("windows/ZeroPad.Server/zeropad.ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])

vec = '''<vector xmlns:android="http://schemas.android.com/apk/res/android"
    android:width="108dp" android:height="108dp" android:viewportWidth="108" android:viewportHeight="108">
''' + "\n".join(f'    <path android:fillColor="{c}" android:pathData="{d}"/>' for d, c in SHAPES) + "\n</vector>\n"
res = "android/app/src/main/res"
open(f"{res}/drawable/ic_launcher_foreground.xml", "w").write(vec)
open(f"{res}/values/ic_launcher_background.xml", "w").write(
    f'<?xml version="1.0" encoding="utf-8"?>\n<resources>\n    <color name="ic_launcher_background">{BG}</color>\n</resources>\n')
adaptive = '''<?xml version="1.0" encoding="utf-8"?>
<adaptive-icon xmlns:android="http://schemas.android.com/apk/res/android">
    <background android:drawable="@color/ic_launcher_background"/>
    <foreground android:drawable="@drawable/ic_launcher_foreground"/>
    <monochrome android:drawable="@drawable/ic_launcher_foreground"/>
</adaptive-icon>
'''
for name in ("ic_launcher", "ic_launcher_round"):
    open(f"{res}/mipmap-anydpi-v26/{name}.xml", "w").write(adaptive)
print("icons written")
