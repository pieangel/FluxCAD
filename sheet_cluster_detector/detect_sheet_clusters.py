import cv2
import json
import argparse
from pathlib import Path


def detect_children_in_cluster(sheet_mask, cluster_bounds):
    x, y, w, h = cluster_bounds
    crop = sheet_mask[y:y + h, x:x + w]

    num_labels, labels, stats, centroids = cv2.connectedComponentsWithStats(crop, 8)

    children = []

    for label in range(1, num_labels):
        cx, cy, cw, ch, area = stats[label]

        if area < 80:
            continue

        if cw < 10 or ch < 10:
            continue

        aspect = cw / max(ch, 1)

        children.append({
            "pixel_bounds": {
                "x": int(x + cx),
                "y": int(y + cy),
                "width": int(cw),
                "height": int(ch)
            },
            "local_bounds": {
                "x": int(cx),
                "y": int(cy),
                "width": int(cw),
                "height": int(ch)
            },
            "area": int(area),
            "aspect_ratio": round(float(aspect), 4)
        })

    return children


def detect_sheet_clusters(image_path: Path, output_json: Path, debug_image: Path):
    img = cv2.imread(str(image_path))
    if img is None:
        raise FileNotFoundError(f"Image not found: {image_path}")

    h, w = img.shape[:2]
    gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)

    blur = cv2.GaussianBlur(gray, (3, 3), 0)

    binary = cv2.adaptiveThreshold(
        blur,
        255,
        cv2.ADAPTIVE_THRESH_MEAN_C,
        cv2.THRESH_BINARY_INV,
        51,
        15
    )

    # 1차: 큰 군집 연결용
    cluster_kernel = cv2.getStructuringElement(cv2.MORPH_RECT, (35, 15))
    cluster_mask = cv2.morphologyEx(binary, cv2.MORPH_CLOSE, cluster_kernel, iterations=2)

    # 2차: cluster 내부 child 후보용
    sheet_kernel = cv2.getStructuringElement(cv2.MORPH_RECT, (7, 7))
    sheet_mask = cv2.morphologyEx(binary, cv2.MORPH_CLOSE, sheet_kernel, iterations=1)

    num_labels, labels, stats, centroids = cv2.connectedComponentsWithStats(cluster_mask, 8)

    candidates = []
    debug = img.copy()

    min_area = w * h * 0.002
    max_area = w * h * 0.95

    idx = 0

    for label in range(1, num_labels):
        x, y, bw, bh, area = stats[label]

        if area < min_area or area > max_area:
            continue

        if bw < w * 0.03 or bh < h * 0.03:
            continue

        aspect = bw / max(bh, 1)

        children = detect_children_in_cluster(sheet_mask, (x, y, bw, bh))

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
            "child_count": len(children),
            "children": children,
            "confidence": 0.5,
            "reason": [
                "connected_component_after_cluster_morphology"
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
        elif len(children) >= 5:
            candidate["type_hint"] = "InternalContentClusters"
            candidate["confidence"] = 0.6
            candidate["reason"].append("many_child_components")
        else:
            candidate["type_hint"] = "InternalContentCluster"
            candidate["confidence"] = 0.55

        candidates.append(candidate)

        # 빨간색: 큰 cluster
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

        # 노란색: 내부 child 후보
        for child in children:
            cb = child["pixel_bounds"]
            cv2.rectangle(
                debug,
                (cb["x"], cb["y"]),
                (cb["x"] + cb["width"], cb["y"] + cb["height"]),
                (0, 255, 255),
                1
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