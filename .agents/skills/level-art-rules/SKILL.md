---
name: level-art-rules
description: Enforce level design, composition, environment storytelling (3-layer framework), color/value distributions, and asset prompting guidelines. Use when writing scene builders, configuring level lighting/materials, structuring environment storytelling layers, or writing prompts for 2D/3D art generation.
---

# Level Art & Environment Design Rules

Mandatory constraints for level design, layout composition, environment storytelling, color theory, and asset prompt construction.

## Quick Reference

| Category | Key Constraints | Code/Tool Application |
|---|---|---|
| **Clustering** | Asymmetrical fractal placement around anchors | `FractalClusterPlacement` helper |
| **Spatial Ratios** | cozy $D/L=1$, observation $D/L=2$, backdrop $D/L=3$ | Min clearance calculations |
| **Readability** | Navigable area clear. Ivy/vines on non-navigable blockers | Asset placement color blocking |
| **3-Layer Story** | Silhouette contrast (L1) -> Weathering (L2) -> Micro-details (L3) | Hero Props vs minor props separation |
| **Color & Value** | Avoid color psychology. Ground darker than walls. Muted saturation | Value assessment checks |
| **B&W Testing** | Check values in grayscale before color approval | Godot Addons: **Colorblindness** or **Eyesee Color** |

---

## 1. Spatial & Composition Rules

### 1.1 Clustering Rule (Asymmetrical Fractal)
**NEVER** scatter props (trees, rocks, barrels) using uniform random distribution. Props must be clustered around anchor objects using asymmetrical scaling.

```gdscript
# GDScript Template for Fractal Clustering (Option A)
func place_prop_cluster(anchor_scene: PackedScene, child_scene: PackedScene, anchor_pos: Vector3, num_children: int) -> void:
    # 1. Place anchor prop (large, reference scale)
    var anchor: Node3D = anchor_scene.instantiate()
    anchor.position = anchor_pos
    anchor.scale = Vector3.ONE * 1.5
    add_child(anchor)

    # 2. Place children with diminishing scale and random angles
    var rng := RandomNumberGenerator.new()
    rng.randomize()
    
    for i in range(num_children):
        var angle := rng.randf_range(0.0, TAU)
        # Diminishing distance: closer children are larger, further are smaller
        var dist_pct := rng.randf_range(0.3, 1.0)
        var distance := dist_pct * 3.0  # max radius 3m
        
        var child_pos := anchor_pos + Vector3(cos(angle), 0, sin(angle)) * distance
        child_pos.y = _get_terrain_height(child_pos.x, child_pos.z)
        
        var child := child_scene.instantiate()
        child.position = child_pos
        child.rotation.y = rng.randf_range(0.0, TAU)
        # Asymmetrical fractal scaling (diminishing based on distance)
        var scale_factor := (1.0 - (dist_pct * 0.5)) * rng.randf_range(0.6, 0.9)
        child.scale = Vector3.ONE * scale_factor
        add_child(child)
```

### 1.2 Spatial Ratios ($D/L$)
Limit open distance $D$ relative to surrounding object height $L$:
*   **$D/L = 1$ (Cozy/Safe):** Narrow paths, dense forest trails.
*   **$D/L = 2$ (Observation):** Combat arenas, encounter zones.
*   **$D/L = 3$ (Backdrop):** Scenic horizons, border areas.

### 1.3 Readability & Blockers
Ensure navigable paths are clear of visual clutter. For non-navigable blockades (cluttered walls, closed doors), unify their colors using green ivy or dense vines. This transforms the complex geometry into a simplified visual "color block" that players immediately recognize as impassable.

---

## 2. Environment Storytelling (3-Layer Framework)

1.  **Layer 1 (Primary Read) — Silhouette & Contrast:** Highlight the silhouette of the scene. Designate unique **Hero Props (Landmarks)** for navigation. Do not add high visual contrast or extreme details to minor props (e.g. trash bins, barrels).
2.  **Layer 2 (Secondary Read) — Context & History:** Indicate weathering, usage patterns, and functional history (e.g., rusted hinges, water damage, scorched ground).
3.  **Layer 3 (Tertiary Read) — Micro-Details:** Place micro-props near interactables (cracks, notes, minor items).

---

## 3. Color & Lighting Rules

### 3.1 Materiality & Values
*   **NO Shape/Color Psychology:** Do not rely on abstract shapes or colors for gameplay meanings (e.g. "red means danger"). Rely on physical materials (wood, metal, stone) and their interactions (unique collision sounds, realistic textures).
*   **Value Rule:** The floor/ground MUST have a darker value (brightness) than the surrounding walls to ground the scene. Keep texture saturation low to reserve visual space for lighting.

### 3.2 Grayscale Testing
Before approving any level design color palette, test the scene in grayscale to verify value readability.
*   **Godot Addons:** Install and use **Colorblindness** (by paulloz) or **Eyesee Color** (by Ultipuk) from the Godot Asset Library to apply simulation filters directly inside the Editor.
*   **Shader fallback:** If addons are not present, apply a 2D grayscale post-process ColorRect to check value distributions.

---

## 4. Asset Generation Prompt Template

When generating image/sprite/texture assets using generative AI, use this 7-part prompt structure:

```
[Asset Type], [Genre] genre, [Subject], [Camera/Layout], [Material & Shape], [Lighting & Color], Negative Prompt: [Constraints]
```

Example prompt:
> **2D isometric sprite sheet, dark fantasy genre, stylized old wooden barrel, fixed top-down camera layout, clean pixel art, sharp edges, dramatic rim light, muted colors, Negative Prompt: no text, no logos, no franchise references, no cluttered composition, unreadable silhouette**

---

## See Also
- [REFERENCE.md](REFERENCE.md) — Detailed level building math, grayscale shader, and clustering algorithms
