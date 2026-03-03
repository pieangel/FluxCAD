import json
import numpy as np
import cv2
import cairosvg

SVG_PATH = "../data/drawing.svg"
PNG_PATH = "../data/drawing_raster.png"
OUT_JSON = "../data/blocks.json"

# 1) SVG → PNG (고해상도 렌더링)
cairosvg.svg2png(
    url=SVG_PATH,
    write_to=PNG_PATH,
    output_width=4096,
    output_height=3072
)

# 2) PNG 로드(그레이스케일)
img = cv2.imread(PNG_PATH, cv2.IMREAD_GRAYSCALE)
if img is None:
    raise RuntimeError("PNG load failed. Check paths.")

# 3) 배경이 검정, 선이 흰색인 경우가 많아서 '흰색'만 추출
#    (threshold 값 10은 경험적 값. 필요하면 5~30 사이로 조절)
_, bw = cv2.threshold(img, 10, 255, cv2.THRESH_BINARY)

# 4) 선이 끊겨서 블록이 조각나는 걸 막기 위해 팽창(dilate)
#    여기 커널 크기가 '블록을 묶는 기준'입니다.
kernel = cv2.getStructuringElement(cv2.MORPH_RECT, (9, 9))
dil = cv2.dilate(bw, kernel, iterations=1)

# 5) 연결요소(블록 후보) 찾기
num_labels, labels, stats, centroids = cv2.connectedComponentsWithStats(dil, connectivity=8)

# stats: [label] = (x, y, w, h, area)
# label 0은 배경이므로 제외
blocks = []
for label in range(1, num_labels):
    x, y, w, h, area = stats[label]

    # 아주 작은 노이즈 제거(필요시 조정)
    if area < 200:
        continue

    blocks.append({
        "id": label,
        "bbox": {"x": int(x), "y": int(y), "w": int(w), "h": int(h)},
        "area": int(area),
        "centroid": {"x": float(centroids[label][0]), "y": float(centroids[label][1])}
    })

print("Detected blocks:", len(blocks))

with open(OUT_JSON, "w", encoding="utf-8") as f:
    json.dump(blocks, f, ensure_ascii=False, indent=2)

print("Saved:", OUT_JSON)