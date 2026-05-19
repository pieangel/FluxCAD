import cv2
import json
import argparse
import numpy as np
from pathlib import Path


def detect_sheet_clusters(image_path: Path, output_json: Path, debug_image: Path):
    img = cv2.imread(str(image_path))
    if img is None:
        raise FileNotFoundError(f"Image not found: {image_path}")

    h, w = img.shape[:2]

    gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)

    # 흰 배경 + 검은 선 도면 기준
    blur = cv2.GaussianBlur(gray, (3, 3), 0)
    binary = cv2.adaptiveThreshold(
        blur,
        255,
        cv2.ADAPTIVE_THRESH_MEAN_C,
        cv2.THRESH_BINARY_INV,
        51,
        15
    )

    # 작은 글자/치수/잡음보다 큰 구조를 보기 위한 morphology
    kernel = cv2.getStructuringElement(cv2.MORPH_RECT, (25, 25))
    closed = cv2.morphologyEx(binary, cv2.MORPH_CLOSE, kernel, iterations=2)

    # connected component 기반 후보 추출
    num_labels, labels, stats, centroids = cv2.connectedComponentsWithStats(closed, 8)

    candidates = []

    min_area = w * h * 0.002
    max_area = w * h * 0.95

    debug = img.copy()

    idx = 0
    for label in range(1, num_labels):
        x, y, bw, bh, area = stats[label]

        if area < min_area or area > max_area:
            continue

        aspect = bw / max(bh, 1)

        # 너무 가늘거나 너무 작은 것 제외
        if bw < w * 0.03 or bh < h * 0.03:
            continue

        candidate = {
            "id": f"cluster_{idx:03d}",
            "type_hint": "UnknownCluster",
            "pixel_bounds": {
                "x": int(x),
                "y": int(y),
                "width": int(bw),
                "height": int(bh)
            },
            "area": int(area),
            "aspect_ratio": round(float(aspect), 4),
            "confidence": 0.5,
            "reason": [
                "connected_component_after_morphology"
            ]
        }

        if aspect > 3.0:
            candidate["type_hint"] = "LongRowSheetCollection"
            candidate["confidence"] = 0.65
            candidate["reason"].append("wide_aspect_ratio")
        elif bw > w * 0.4 and bh > h * 0.4:
            candidate["type_hint"] = "SingleOuterFrameOrFramedClusters"
            candidate["confidence"] = 0.6
            candidate["reason"].append("large_outer_region")
        else:
            candidate["type_hint"] = "InternalContentCluster"
            candidate["confidence"] = 0.55

        candidates.append(candidate)

        cv2.rectangle(debug, (x, y), (x + bw, y + bh), (0, 0, 255), 3)
        cv2.putText(
            debug,
            candidate["id"],
            (x, max(30, y - 10)),
            cv2.FONT_HERSHEY_SIMPLEX,
            1.0,
            (0, 0, 255),
            2
        )

        idx += 1

    result = {
        "source_image": str(image_path),
        "image_size": {
            "width": int(w),
            "height": int(h)
        },
        "cluster_count": len(candidates),
        "clusters": candidates
    }

    output_json.parent.mkdir(parents=True, exist_ok=True)
    debug_image.parent.mkdir(parents=True, exist_ok=True)

    with open(output_json, "w", encoding="utf-8") as f:
        json.dump(result, f, ensure_ascii=False, indent=2)

    cv2.imwrite(str(debug_image), debug)

    print(f"[OK] clusters: {len(candidates)}")
    print(f"[OK] json: {output_json}")
    print(f"[OK] debug: {debug_image}")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--image", required=True, help="Full_Drawing_HD.png path")
    parser.add_argument("--json", default="outputs/sheet_cluster_regions.json")
    parser.add_argument("--debug", default="debug/opencv_debug.png")
    args = parser.parse_args()

    detect_sheet_clusters(
        Path(args.image),
        Path(args.json),
        Path(args.debug)
    )


if __name__ == "__main__":
    main()