# Hướng Dẫn Sử Dụng Skill Cho AI (GiacMoCoTich)

Tài liệu này chứa các quy tắc thiết kế, phát triển và cách lựa chọn kỹ năng (skill) phù hợp cho AI khi làm việc trong dự án game **GiacMoCoTich** (Giấc Mơ Cổ Tích).

---

## 1. Thông Tin Tổng Quan Dự Án

*   **Tên dự án:** GiacMoCoTich (Giấc Mơ Cổ Tích)
*   **Game Engine:** Godot 4.6 (Forward Plus render)
*   **Engine Vật lý:** Jolt Physics 3D
*   **Thể loại/Chiều:** 3D Game (PC / Desktop)
*   **Ngôn ngữ lập trình:** GDScript

---

## 2. Quy Tắc Định Tuyến Skill (Skill Routing Rules)

Khi nhận yêu cầu từ người dùng, hãy đối chiếu với bảng dưới đây để kích hoạt skill thích hợp nhất:

| Loại công việc cần thực hiện | Skill cần sử dụng | Lý do & Cách áp dụng |
| :--- | :--- | :--- |
| **Lập trình logic game, viết GDScript, tạo/sửa Scene, State Machine, Signals, Singletons, Resources** | `godot-gdscript-patterns` | Đây là skill kỹ thuật cốt lõi cho Godot. Cần đọc `resources/implementation-playbook.md` để tham chiếu code mẫu chuẩn (State Machine, Event Bus, Save System, Object Pool, Components). |
| **Thiết kế game loop, quản lý asset 3D, thiết kế cơ chế gameplay (GDD), âm thanh, camera 3D** | `game-development` | Dùng để định hướng các sub-skills: `pc-games` cho desktop, `3d-games` cho đồ họa 3D, `game-design` cho thiết kế màn chơi/hệ thống, `game-art` cho asset pipeline. |
| **Định nghĩa thuật ngữ game, viết/sửa từ điển thuật ngữ (`CONTEXT.md`), viết quyết định kiến trúc (ADR)** | `domain-modeling` | Giúp giữ ngôn ngữ đồng nhất (Ubiquitous Language) và lưu lại các quyết định kiến trúc quan trọng khó thay đổi tại `docs/adr/`. |
| **Thiết kế giao diện người dùng (HUD, UI), Menu, UX tương tác, thiết kế accessibility (tiếp cận)** | `ui-ux-designer` | Hướng dẫn cách tạo UI khoa học, giảm tải nhận thức, tuân thủ WCAG và tối ưu luồng trải nghiệm người chơi. |
| **Quy chuẩn clean code, DRY, cấu trúc file, thiết kế module độc lập, early return pattern** | `software-architecture` | Đảm bảo code sạch, các file GDScript không vượt quá 200 dòng, các hàm dưới 50 dòng, tránh đặt tên chung chung như `utils.gd` hay `helpers.gd`. |
| **Tạo/sửa hệ thống chiến đấu, kỹ năng tấn công, CC, AoE, hitbox/hurtbox, animation attack, input buffer** | `combat-design-rules` | Bắt buộc đọc trước khi viết bất kỳ code combat nào. Enforce 3-pha attack, cấm Hard Stun, AoE phải có khe hở và telegraph, player response ≤100ms. Tham chiếu `REFERENCE.md` cho code templates. |
| **Thiết kế level/màn chơi, sắp đặt cây cối/vật cản, ánh sáng/màu sắc bối cảnh, vẽ vật liệu 3D** | `level-art-rules` | Bắt buộc đọc trước khi thiết kế level, dựng cảnh 3D, hoặc cấu hình ánh sáng. Enforce thuật toán sinh cụm (clustering), tỷ lệ không gian D/L, 3-layer storytelling, grayscale test và prompt mẫu. |
| **Phản biện kế hoạch phát triển, stress-test thiết kế hệ thống trước khi code** | `grill-me` hoặc `grill-with-docs` | `grill-me` dùng để hỏi đáp làm rõ nghiệp vụ. `grill-with-docs` kết hợp đối chiếu với glossary trong `CONTEXT.md`. |

---

## 3. Quy Chuẩn Coding & Thiết Kế Trong Godot 4.6

### A. Static Typing trong GDScript
Tất cả các biến, tham số truyền vào và kiểu trả về của hàm phải được khai báo kiểu tĩnh (static typing) rõ ràng để tối ưu hiệu năng và phát hiện lỗi sớm:
```gdscript
# Đúng:
@export var speed: float = 200.0
func take_damage(amount: int) -> void:
    pass

# Sai:
@export var speed = 200
func take_damage(amount):
    pass
```

### B. Component (Composition) thay vì Thừa Kế (Inheritance)
Hạn chế tối đa việc tạo cây thừa kế quá sâu cho các Class nhân vật/đối tượng. Hãy sử dụng hệ thống Component dạng Node con:
*   `HealthComponent` (Node): Quản lý lượng máu, nhận sát thương và phát tín hiệu báo chết.
*   `HitboxComponent` (Area3D): Nơi gây sát thương lên đối tượng khác.
*   `HurtboxComponent` (Area3D): Nơi nhận sát thương từ Hitbox và truyền vào HealthComponent.

### C. Giao Tiếp decoupled bằng Tín Hiệu (Signals) & Event Bus
*   **Quy tắc:** *Up với Signals, Down với Function Calls*. Node con báo lên Node cha bằng Signal, Node cha gọi hàm/thay đổi thuộc tính của Node con trực tiếp.
*   Tránh để các Scene liên kết chặt chẽ với nhau. Sử dụng một Autoload Event Bus (`event_bus.gd`) làm trạm trung chuyển signal toàn cục cho các sự kiện liên hệ thống (ví dụ: `player_died`, `score_changed`).

### D. Tối Ưu Hiệu Năng
*   **Cache references:** Luôn lưu trữ cache các node con thông qua `@onready var name: Type = $Path` thay vì gọi `get_node()` nhiều lần trong `_process()` hay `_physics_process()`.
*   **Object Pooling:** Sử dụng cơ chế Object Pool cho các đối tượng xuất hiện/biến mất tần suất cao (ví dụ: đạn, hiệu ứng hạt/particles, popup sát thương) để tránh tình trạng trễ hình do Garbage Collector.

---

## 4. Quy Tắc Tạo Tài Liệu Cho AI

*   **Không tạo file rác:** Giữ cấu trúc thư mục sạch sẽ.
*   **Tạo CONTEXT.md:** Tạo file `CONTEXT.md` ngay ở root dự án để định nghĩa các từ khóa gameplay khi chúng được thống nhất (ví dụ: DreamState, SoulEnergy, JoltPhysicsConfig).
*   **ADR (Architecture Decision Records):** Viết ADR lưu vào `docs/adr/xxxx-title.md` khi đưa ra một quyết định kiến trúc quan trọng (ví dụ: sử dụng State Machine cho AI của quái, cấu hình save/load mã hóa).

---

## 5. Quy Tắc Tối Ưu Hiệu Năng & Tránh God Object

Đọc tài liệu bắt buộc tại [.agents/rules/performance_and_architecture_rules.md](file:///d:/openclaw/giac-mo-co-tich/.agents/rules/performance_and_architecture_rules.md) trước khi sửa đổi hoặc thêm mới code:
- Giới hạn file tối đa **200 dòng**, hàm tối đa **50 dòng**.
- Tránh gọi `load()` động trong vòng lặp cập nhật mỗi frame; bắt buộc dùng texture cache.
- Tuyệt đối không tạo hàng ngàn `CollisionShape3D` riêng lẻ; gộp thành một Mesh Shape hoặc dùng HeightMap.
- Tránh dùng `set_script()` ở runtime; instantiate đối tượng từ `.tscn` được đóng gói sẵn.
- Tránh dùng hardcoded absolute paths (ví dụ: `/root/World/AudioManager`), sử dụng NodePath hoặc EventBus.

---

## 6. Hợp Đồng Thiết Kế Bắt Buộc: Tree Occlusion

Trước khi sửa cây, camera orthographic, shader/material cây, ánh sáng actor hoặc khả năng nhìn thấy player, bắt buộc đọc [.agents/rules/tree_occlusion_readability_rules.md](rules/tree_occlusion_readability_rules.md).

Không được thay thế thuật toán screen-space AABB, thay đổi alpha/timing/giới hạn 3 cây, tắt collider, hoặc quay lại heuristic `diff_x`/`diff_z` nếu chưa có xác nhận rõ ràng của người dùng.
