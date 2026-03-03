import cv2

# 도면 이미지 경로를 넣으세요
image_path = "../data/Full_Drawing_HD.png"

img = cv2.imread(image_path)

if img is not None:
    # 이미지가 너무 크면 화면에 안 보일 수 있으니 20% 크기로 리사이즈
    resized_img = cv2.resize(img, None, fx=0.2, fy=0.2)

    cv2.imshow("CAD Drawing Preview", resized_img)
    cv2.waitKey(0)  # 아무 키나 누르면 닫힙니다.
    cv2.destroyAllWindows()
else:
    print("이미지 파일을 찾을 수 없습니다. 경로를 확인해 주세요.")