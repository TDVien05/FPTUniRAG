# Combat Design Rules — Reference

Detailed code templates and architecture patterns referenced by [SKILL.md](SKILL.md).
Read this when implementing new combat features.

## 1. Three-Phase Attack Implementation Template

### 1.1 Base Attack Resource

```gdscript
# attack_data.gd — Resource defining a single attack's parameters
class_name AttackData
extends Resource

@export var attack_name: StringName = &"unnamed"

# Phase frame counts (must satisfy ratio constraints from SKILL.md)
@export_range(1, 20) var anticipation_frames: int = 2
@export_range(1, 5) var attack_frames: int = 1  # Keep minimal — instant strike
@export_range(1, 20) var recovery_frames: int = 3

# Damage
@export var base_damage: float = 10.0
@export var is_critical_capable: bool = true

# Tags
@export var attack_tag: int = 0  # 0=MELEE, 1=AOE_TELEGRAPHED
@export var aoe_radius: float = 0.0  # Only if attack_tag == AOE_TELEGRAPHED
@export var telegraph_duration: float = 0.8  # Minimum 0.8s for AoE

# Cancel policy
@export var cancelable_in_recovery: bool = true

# Validation at load time
func validate() -> bool:
    var total: int = anticipation_frames + attack_frames + recovery_frames
    var antic_ratio: float = float(anticipation_frames) / total
    var atk_ratio: float = float(attack_frames) / total
    var recv_ratio: float = float(recovery_frames) / total
    
    assert(antic_ratio >= 0.25, "Anticipation must be >= 25%% of total frames")
    assert(atk_ratio <= 0.15, "Attack must be <= 15%% of total frames")
    assert(recv_ratio >= 0.30, "Recovery must be >= 30%% of total frames")
    
    if attack_tag == 1:  # AOE_TELEGRAPHED
        assert(telegraph_duration >= 0.8, "AoE telegraph must be >= 0.8s")
        assert(aoe_radius > 0.0, "AoE radius must be > 0")
    
    return true
```

### 1.2 Attack State Machine Integration

```gdscript
# Example integration into an existing entity script (Player or Enemy)
# These variables and functions should be added to the entity.

enum AttackPhase { NONE, ANTICIPATION, ATTACK, RECOVERY }

var attack_phase: AttackPhase = AttackPhase.NONE
var _phase_frame: int = 0
var _current_attack: AttackData = null

func execute_attack(attack: AttackData) -> void:
    _current_attack = attack
    attack_phase = AttackPhase.ANTICIPATION
    _phase_frame = 0
    velocity = Vector3.ZERO  # Lock movement during Anticipation + Attack
    _on_enter_anticipation()

func _advance_attack_frame() -> void:
    _phase_frame += 1
    match attack_phase:
        AttackPhase.ANTICIPATION:
            if _phase_frame >= _current_attack.anticipation_frames:
                attack_phase = AttackPhase.ATTACK
                _phase_frame = 0
                _on_enter_attack()
        AttackPhase.ATTACK:
            if _phase_frame >= _current_attack.attack_frames:
                attack_phase = AttackPhase.RECOVERY
                _phase_frame = 0
                _on_enter_recovery()
        AttackPhase.RECOVERY:
            if _phase_frame >= _current_attack.recovery_frames:
                attack_phase = AttackPhase.NONE
                _current_attack = null
                _on_attack_complete()

func _on_enter_anticipation() -> void:
    # Play anticipation animation — must be UNIQUE per attack to telegraph direction
    pass

func _on_enter_attack() -> void:
    hitbox_component.monitoring = true
    _spawn_weapon_trail()  # MANDATORY: visual trail on hitbox

func _on_enter_recovery() -> void:
    hitbox_component.monitoring = false
    # This is the "Window of Opportunity" — opponent can counter-attack

func _on_attack_complete() -> void:
    # Return to Idle pose — MANDATORY
    pass
```

## 2. Soft CC Component Template

### 2.1 CC Status Effect Component

```gdscript
# cc_status_component.gd
class_name CCStatusComponent
extends Node

signal cc_applied(cc_type: int, duration: float)
signal cc_removed(cc_type: int)

enum CCType {
    SLOW,       # Reduce speed, can still attack
    DAZE,       # Block attack, can still move
    IMMOBILIZE, # Block movement, can still attack
    # BANNED: STUN, FREEZE, PARALYZE
}

var _active_effects: Dictionary = {}  # CCType -> remaining_duration

func apply_cc(cc_type: CCType, duration: float, intensity: float = 0.5) -> void:
    # Intensity: 0.0-1.0 for SLOW (speed reduction %), ignored for DAZE/IMMOBILIZE
    _active_effects[cc_type] = {
        "remaining": duration,
        "intensity": clampf(intensity, 0.1, 0.8),  # Cap at 80% slow max
    }
    cc_applied.emit(cc_type, duration)

func _process(delta: float) -> void:
    var to_remove: Array[int] = []
    for cc_type: int in _active_effects:
        _active_effects[cc_type]["remaining"] -= delta
        if _active_effects[cc_type]["remaining"] <= 0.0:
            to_remove.append(cc_type)
    for cc_type: int in to_remove:
        _active_effects.erase(cc_type)
        cc_removed.emit(cc_type)

func is_movement_blocked() -> bool:
    return _active_effects.has(CCType.IMMOBILIZE)

func is_attack_blocked() -> bool:
    return _active_effects.has(CCType.DAZE)

func get_speed_multiplier() -> float:
    if not _active_effects.has(CCType.SLOW):
        return 1.0
    return 1.0 - _active_effects[CCType.SLOW]["intensity"]

func has_any_cc() -> bool:
    return not _active_effects.is_empty()
```

### 2.2 Integration with Player Movement

```gdscript
# In player.gd _physics_process:
func _physics_process(delta: float) -> void:
    # Check CC before processing input
    var cc: CCStatusComponent = get_node_or_null("CCStatusComponent")
    if cc:
        if cc.is_movement_blocked():
            velocity.x = 0.0
            velocity.z = 0.0
            # Still allow attack input — IMMOBILIZE does NOT block attacks
        if cc.is_attack_blocked():
            # Block attack input — DAZE does NOT block movement
            pass
        # Apply slow
        var speed_mult: float = cc.get_speed_multiplier() if cc else 1.0
        var current_speed: float = speed * speed_mult
```

## 3. Input Buffer + IASA Template

### 3.1 Press Buffer System

```gdscript
# input_buffer.gd — Attach to Player
class_name InputBuffer
extends Node

const BUFFER_DURATION: float = 0.15  # 150ms window

var _buffered_action: StringName = &""
var _buffer_timer: float = 0.0

func buffer_action(action: StringName) -> void:
    _buffered_action = action
    _buffer_timer = BUFFER_DURATION

func consume() -> StringName:
    var action: StringName = _buffered_action
    _buffered_action = &""
    _buffer_timer = 0.0
    return action

func has_buffered() -> bool:
    return _buffered_action != &""

func _process(delta: float) -> void:
    if _buffer_timer > 0.0:
        _buffer_timer -= delta
        if _buffer_timer <= 0.0:
            _buffered_action = &""  # Expire buffer — prevents ghost inputs
```

### 3.2 IASA (Interruptable As Soon As)

```gdscript
# In player combat logic:
func _check_iasa() -> void:
    # Only allow IASA during Recovery phase
    if attack_phase != AttackPhase.RECOVERY:
        return
    
    # Check if current attack allows cancel
    if _current_attack and not _current_attack.cancelable_in_recovery:
        return  # This attack is LOCKED — no cancel allowed
    
    # Movement cancel: player pressed movement during Recovery
    if _has_movement_input():
        _cancel_attack_to_movement()
        return
    
    # Buffered attack: execute next attack from buffer
    var input_buf: InputBuffer = get_node_or_null("InputBuffer")
    if input_buf and input_buf.has_buffered():
        var next_action: StringName = input_buf.consume()
        if next_action == &"attack":
            _start_next_attack()
```

## 4. AoE Telegraph System Template

### 4.1 Ground Warning Decal

```gdscript
# aoe_telegraph.gd — Spawn before any enemy AoE attack
class_name AoETelegraph
extends Node3D

@export var radius: float = 3.0
@export var warning_duration: float = 1.0  # Must be >= 0.8s
@export var color: Color = Color(1.0, 0.1, 0.1, 0.4)

var _decal: MeshInstance3D = null
var _timer: float = 0.0

signal telegraph_expired

func _ready() -> void:
    assert(warning_duration >= 0.8, "Telegraph must be visible for >= 0.8s")
    _create_ground_decal()

func _process(delta: float) -> void:
    _timer += delta
    # Pulse effect to draw attention
    if _decal:
        var pulse: float = 0.7 + 0.3 * sin(_timer * 6.0)
        var mat: StandardMaterial3D = _decal.get_active_material(0)
        if mat:
            mat.albedo_color.a = color.a * pulse
    
    if _timer >= warning_duration:
        telegraph_expired.emit()
        queue_free()

func _create_ground_decal() -> void:
    _decal = MeshInstance3D.new()
    var disc := PlaneMesh.new()
    disc.size = Vector2(radius * 2.0, radius * 2.0)
    _decal.mesh = disc
    
    var mat := StandardMaterial3D.new()
    mat.albedo_color = color
    mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
    mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
    mat.no_depth_test = true
    mat.render_priority = 1
    _decal.set_surface_override_material(0, mat)
    
    add_child(_decal)
    _decal.position.y = 0.02  # Slightly above ground to prevent z-fighting
```

### 4.2 AoE Coverage Validation

```gdscript
# Utility function: validate AoE doesn't cover too much area
static func validate_aoe_coverage(
    aoe_radius: float,
    arena_visible_width: float = 20.0,
    arena_visible_depth: float = 15.0
) -> bool:
    var aoe_area: float = PI * aoe_radius * aoe_radius
    var visible_area: float = arena_visible_width * arena_visible_depth
    var coverage: float = aoe_area / visible_area
    # RULE: No single AoE > 60% visible play area
    assert(coverage <= 0.6, "AoE covers %.0f%% of visible area (max 60%%)" % [coverage * 100])
    return coverage <= 0.6
```

## 5. Anti-Pattern Checklist

Before submitting any combat code, verify:

- [ ] No `AnimState` directly deals damage without 3-phase structure
- [ ] No CC effect locks both movement AND attack simultaneously
- [ ] No enemy AoE covers > 60% of visible play area
- [ ] No enemy AoE activates without telegraph decal (≥ 0.8s)
- [ ] No player action has dead frames before Anticipation
- [ ] No player action takes > 100ms from input to visual response
- [ ] Player attack Recovery phase returns to Idle pose
- [ ] Every attack has explicit `cancelable_in_recovery` declaration
- [ ] Input buffer expires after 150ms (no ghost inputs)
- [ ] Buffer never auto-chains more than 1 queued action
