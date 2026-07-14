# Level Art & Environment Design Rules — Reference

Detailed implementation blueprints, mathematical helpers, and asset validation tools for [SKILL.md](SKILL.md).

---

## 1. Asymmetrical Fractal Clustering Algorithm

This GDScript class can be attached to or referenced by `forest_builder.gd` or other level generators to procedurally scatter props in aesthetic clusters.

```gdscript
# level_scatter_helper.gd
class_name LevelScatterHelper
extends Node

# Place a cluster of props procedurally following the Asymmetrical Fractal Clustering rule
static func spawn_fractal_cluster(
    parent_node: Node3D,
    anchor_scene: PackedScene,
    child_scenes: Array[PackedScene],
    center_pos: Vector3,
    num_children: int = 4,
    max_radius: float = 3.5,
    base_scale: float = 1.0
) -> void:
    var rng := RandomNumberGenerator.new()
    rng.randomize()
    
    # 1. Place the Anchor (Largest object)
    var anchor := anchor_scene.instantiate() as Node3D
    anchor.position = center_pos
    anchor.rotation.y = rng.randf_range(0.0, TAU)
    anchor.scale = Vector3.ONE * base_scale * rng.randf_range(1.1, 1.4)
    parent_node.add_child(anchor)
    
    # Optional: Build collision for anchor if needed
    _add_capsule_collision(parent_node, anchor.position, 0.4 * anchor.scale.x, 3.0 * anchor.scale.y)

    # 2. Place Children (diminishing scale farther from center, asymmetrical)
    for i in range(num_children):
        if child_scenes.is_empty():
            break
            
        var child_scene := child_scenes[rng.randi() % child_scenes.size()]
        if not child_scene:
            continue
            
        var angle := rng.randf_range(0.0, TAU)
        # Asymmetrical distribution: cluster children closer on one side, sparse on the other
        var distance_factor := rng.randf_range(0.2, 1.0)
        # Apply exponential decay or curve to keep most props close to the anchor
        var distance := pow(distance_factor, 1.5) * max_radius
        
        var offset := Vector3(cos(angle), 0, sin(angle)) * distance
        var child_pos := center_pos + offset
        
        # Snap child height to terrain
        if parent_node.has_method("_get_hill_height"):
            child_pos.y = parent_node._get_hill_height(child_pos.x, child_pos.z)
            
        var child := child_scene.instantiate() as Node3D
        child.position = child_pos
        child.rotation.y = rng.randf_range(0.0, TAU)
        
        # Diminishing fractal scale: farther children are significantly smaller
        var scale_modifier := (1.0 - (distance_factor * 0.45)) * rng.randf_range(0.7, 0.95)
        child.scale = Vector3.ONE * base_scale * scale_modifier
        
        parent_node.add_child(child)

static func _add_capsule_collision(parent: Node, pos: Vector3, radius: float, height: float) -> void:
    var body := StaticBody3D.new()
    body.position = pos
    var col := CollisionShape3D.new()
    var shape := CapsuleShape3D.new()
    shape.radius = radius
    shape.height = height
    col.shape = shape
    col.position.y = height * 0.5
    body.add_child(col)
    parent.add_child(body)
```

---

## 2. Spatial Ratio ($D/L$) Validation

To ensure AI layouts maintain correct spatial containment, use this script to validate level proportions during generation or editor building.

```gdscript
# spatial_ratio_validator.gd
class_name SpatialRatioValidator
extends RefCounted

# Validates if target clearance fits spatial ratios based on enclosing obstacle heights
static func validate_clearance(
    clearance_distance: float, 
    obstacle_height: float, 
    expected_ratio: float, 
    tolerance_pct: float = 0.15
) -> bool:
    var actual_ratio := clearance_distance / obstacle_height
    var min_ratio := expected_ratio * (1.0 - tolerance_pct)
    var max_ratio := expected_ratio * (1.0 + tolerance_pct)
    
    return actual_ratio >= min_ratio and actual_ratio <= max_ratio

# Example verification helper for Level art builds
static func get_ratio_feedback(d: float, l: float) -> String:
    var ratio := d / l
    if ratio < 1.3:
        return "Cozy/Confined (D/L = %.2f) - Best for forest paths/passages" % ratio
    elif ratio < 2.5:
        return "Observation Area (D/L = %.2f) - Best for combat arenas" % ratio
    else:
        return "Scenic/Background (D/L = %.2f) - Best for horizons" % ratio
```

---

## 3. Grayscale Value Verification Shader

If the **Colorblindness** or **Eyesee Color** addons are not installed, this custom Post-Processing shader can be applied to a full-screen `ColorRect` to verify Level Values (Sắc độ) instantly.

### 3.1 Shader Code (`grayscale_debug.gdshader`)

```glsl
shader_type canvas_item;

uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap;

void fragment() {
    vec4 screen_color = texture(screen_texture, SCREEN_UV);
    // Standard perceptual luminance weights (ITU-R BT.601)
    float gray = dot(screen_color.rgb, vec3(0.299, 0.587, 0.114));
    COLOR = vec4(vec3(gray), screen_color.a);
}
```

### 3.2 Viewport Switch Node (`GrayscaleDebug.gd`)

```gdscript
# grayscale_debug.gd
extends CanvasLayer

var debug_rect: ColorRect = null

func _ready() -> void:
    process_mode = Node.PROCESS_MODE_ALWAYS
    layer = 128  # Draw on top of all UI nodes
    
    debug_rect = ColorRect.new()
    debug_rect.set_anchors_preset(Control.PRESET_FULL_RECT)
    debug_rect.mouse_filter = Control.MOUSE_FILTER_IGNORE
    debug_rect.visible = false
    add_child(debug_rect)
    
    var mat := ShaderMaterial.new()
    mat.shader = preload("res://src/world/grayscale_debug.gdshader") # or path to shader
    debug_rect.material = mat

func _input(event: InputEvent) -> void:
    # F11 toggles grayscale value verification
    if event is InputEventKey and event.pressed and event.keycode == KEY_F11:
        debug_rect.visible = not debug_rect.visible
        print("Grayscale value validation view: ", debug_rect.visible)
```

---

## 4. Environment Storytelling Anti-Pattern Checklist

Ensure environment scenes and layouts are checked against this list before finalizing level assets:

*   [ ] **Zero Uniform Scattering:** Every scattered prop is part of a cluster with asymmetrical scaling (Fractal Placement).
*   [ ] **Value Alignment:** Ground/floor surface value is darker than surrounding walls in grayscale view.
*   [ ] **Navigable Contrast:** Obstacle boundaries and blockers are simplified into green ivy/vine color blocks. Paths are clean.
*   [ ] **Hero Prop Isolation:** Landmark objects (old ruins, glowing trees) have high contrast silhouettes, while minor props (crates, barrels) use low-contrast muted tones.
*   [ ] **Texture Saturation Cap:** Environment texture saturation remains low (under 60% max saturation) to leave headroom for game lighting and VFX visibility.
*   [ ] **Material Sound Interaction:** Wood, metal, and stone surfaces trigger corresponding step footstep sound types. No visual material mismatch.
