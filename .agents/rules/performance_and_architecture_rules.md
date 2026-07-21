# Quy Tắc Tối Ưu Hiệu Năng & Kiến Trúc GDScript (GiacMoCoTich)

Quy chuẩn bắt buộc đối với mọi Agent khi viết code cho dự án game Giấc Mơ Cổ Tích nhằm tránh lặp lại các lỗi nghiêm trọng về hiệu năng (Performance) và kiến trúc (Architecture).

---

## 1. Quy Tắc Tránh God Object & Giới Hạn Dòng Code

*   **Quy tắc 200 dòng:** Bất kỳ file `.gd` nào dài hơn **200 dòng** bắt buộc phải được chia nhỏ thành các file/component riêng biệt với nhiệm vụ đơn nhất (Single Responsibility).
*   **Quy tắc hàm 50 dòng:** Mọi hàm phải thực hiện một nhiệm vụ duy nhất và không được vượt quá **50 dòng**. Nếu dài hơn, hãy tách nhỏ thành các helper functions.
*   **Tách biệt Hệ Thống:** Không được gộp các logic khác hệ thống (ví dụ: UI/HUD, hiệu ứng thời tiết, spawning quái, camera) vào chung một Node quản lý lớn (God Object). Mỗi hệ thống phải kế thừa Node riêng.

---

## 2. Quy Tắc Tối Ưu Hiệu Năng Hoạt Ảnh & Load Asset

*   **Không gọi `load()` trong hot loop:** Tuyệt đối không gọi `load("res://...")` hoặc `ResourceLoader.exists()` trong `_process()`, `_physics_process()`, hoặc bất kỳ vòng lặp/hàm nào chạy mỗi frame.
*   **Cơ chế Texture Caching:** Sử dụng một biến cục bộ `_texture_cache: Dictionary` để lưu trữ các texture đã load. Chỉ gọi `load()` lần đầu tiên và tái sử dụng cho các lần sau.
*   **Preload tĩnh:** Ưu tiên sử dụng `@export var scene: PackedScene` hoặc `preload("res://...")` ở đầu file thay vì gọi `load()` động tại runtime trừ trường hợp bất khả kháng.

---

## 3. Quy Tắc Vật Lý & Xử Lý Va Chạm (Collision)

*   **Không tạo hàng ngàn CollisionShape3D:** Tuyệt đối không tạo các CollisionShape3D riêng lẻ cho từng ô gạch, đất đồi núi hoặc cây cối nếu số lượng lên tới hàng trăm/hàng ngàn.
*   **Mesh Collision:** 
    *   Đối với địa hình đồi núi phức tạp sinh ngẫu nhiên, hãy gộp dữ liệu các đỉnh lại và tạo một `ConcavePolygonShape3D` duy nhất hoặc sử dụng `HeightMapShape3D`.
    *   Đối với cây cối/bụi cỏ tĩnh, hãy gộp collision của chúng thành các shape con của một `StaticBody3D` duy nhất thay vì tạo hàng trăm `StaticBody3D` riêng biệt.

---

## 4. Quy Tắc Khởi Tạo Node & Spawning

*   **Tránh dùng `set_script()` tại Runtime:** Không tạo node thô bằng `Node.new()` rồi gán script bằng `set_script(script)`. Điều này phá vỡ khả năng kiểm tra trực quan trong Godot Editor và dễ gây lỗi race condition.
*   **Sử dụng PackedScene (.tscn):** Luôn đóng gói đối tượng (Player, Enemy, Prop) thành một file `.tscn` hoàn chỉnh chứa cấu trúc cây Node và script liên kết sẵn, sau đó sinh ra bằng `.instantiate()`.
*   **Async/Deferred Spawning:** Khi sinh số lượng lớn các đối tượng (như lúc khởi tạo rừng), không sinh đồng bộ tất cả trong `_ready()`. Hãy chia nhỏ quy trình sinh bằng cách sử dụng `call_deferred()` hoặc rải qua nhiều frame để tránh làm đứng hình game (freeze).

---

## 5. Tránh Hardcoded Node Paths

*   **Không gọi trực tiếp đường dẫn tuyệt đối:** Tránh viết `get_node("/root/World/...")`.
*   **Sử dụng `@export`:** Hãy dùng `@export var target_node: NodePath` hoặc kết nối qua hệ thống Signal của `EventBus` để giao tiếp decoupled giữa các Manager độc lập.
