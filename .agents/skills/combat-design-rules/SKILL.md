---
name: combat-design-rules
description: Enforce combat design rules for 2.5D action game without dodge/dash. Prevents hard stun, full-screen AoE, dead frames, and unstructured attacks. Provides 3-phase attack template, soft CC system, input buffer/IASA, and telegraph requirements. Use when creating or modifying combat skills, enemy attacks, player attacks, CC effects, AoE abilities, hitbox/hurtbox logic, or animation state machines.
---

# Combat Design Rules

Mandatory reading before creating or modifying any combat system code.
This game has **NO Dodge/Dash and NO I-frames**. All design rules derive from this constraint.

## Quick Reference

| Rule | Player | Enemy | Shared |
|------|--------|-------|--------|
| 3-phase attack structure | ✅ | ✅ | ✅ |
| No dead frames before Anticipation | ✅ | — | — |
| ≤100ms input response | ✅ | — | — |
| Press Buffer + IASA | ✅ | — | — |
| Telegraph/decal before AoE | — | ✅ | — |
| AoE must have gap (no full coverage) | — | ✅ | — |
| Soft CC only (no Hard Stun) | — | ✅ | ✅ |
| Weapon trail VFX on Attack phase | ✅ | ✅ | ✅ |

## Shared Rules (Player + Enemy)

### S1 — Three-Phase Attack Structure
Every attack MUST be a state machine with exactly 3 phases. **Never** use a flat `play_animation()` → deal damage approach.

```gdscript
# MANDATORY parameters for every attack definition
@export var anticipation_ratio: float = 0.30  # ≥ 25% of total frames
@export var attack_ratio: float = 0.10        # ≤ 15% — must be instant
@export var recovery_ratio: float = 0.35      # ≥ 30% — punishment window

enum AttackPhase { ANTICIPATION, ATTACK, RECOVERY }
```

**Anticipation**: Unique animation telegraphing direction. Hitbox OFF.
**Attack**: Instant strike. Hitbox ON. Spawn weapon trail VFX here.
**Recovery**: "Window of Opportunity" for opponent. Hitbox OFF. Must return to Idle pose.

### S2 — Soft CC Only (Hard Stun Banned)
**NEVER** create a skill that removes ALL player control simultaneously.

Allowed CC types (lock **one** action axis only):

```gdscript
enum CCType {
    SLOW,       # Reduce speed by percentage, player can still attack
    DAZE,       # Block attack input, player can still move
    IMMOBILIZE, # Block movement, player can still attack
}
# BANNED: STUN, FREEZE, PARALYZE, or any effect that locks both move AND attack
```

Exception: Hard Stun allowed ONLY if a break-free mechanic is attached (spend resource to escape). Must be explicitly approved by user.

### S3 — Weapon Trail VFX on Attack Phase
During the Attack phase, spawn a visual trail effect that follows the hitbox to clearly communicate the damage zone.

```gdscript
# In the Attack phase transition:
func _enter_attack_phase() -> void:
    hitbox.monitoring = true
    _spawn_weapon_trail(hitbox_shape.global_position, facing_direction)
```

## Player-Only Rules

### P1 — Zero Dead Frames (≤100ms Response)
Player combat actions MUST respond within 100ms of input. **NEVER** insert extra frames before the Anticipation phase.

```gdscript
# CORRECT: Input → immediately enter Anticipation
func _start_attack() -> void:
    attack_phase = AttackPhase.ANTICIPATION  # Frame 0 is already Anticipation
    anim_frame = 0

# BANNED: Any delay, timer, or "windup warmup" before Anticipation
```

### P2 — Press Buffer + IASA Windows
Define input windows explicitly. Never auto-chain attacks from buffer.

```gdscript
var _buffered_action: StringName = &""
const BUFFER_WINDOW_SEC: float = 0.15  # Only remember last 150ms of input

# During Recovery phase, allow movement cancel (IASA):
func _physics_process(delta: float) -> void:
    if attack_phase == AttackPhase.RECOVERY:
        if _has_movement_input():
            _cancel_to_movement()  # IASA: Interruptable As Soon As
        elif _buffered_action != &"":
            _execute_buffered_action()
            _buffered_action = &""

# Buffer logic: remember input pressed during non-cancelable phases
func _input(event: InputEvent) -> void:
    if _is_in_locked_phase():
        if event.is_action_pressed(&"attack"):
            _buffered_action = &"attack"
```

**BANNED**: Applying buffer to ALL skills. Maintain a cancelable/locked classification:

```gdscript
# Every skill must declare its cancel policy
const SKILL_CANCEL_POLICY: Dictionary = {
    "light_attack": true,   # cancelable in Recovery
    "heavy_attack": false,  # locked until complete
    "special_1": true,
}
```

### P3 — Player Invulnerability Constraint
The existing 0.5s invulnerable timer after being hit is the ONLY damage immunity window. Do not add I-frames to any other action.

## Enemy-Only Rules

### E1 — AoE Must Have Escape Gap
Since there is NO dodge/dash, every AoE attack MUST provide a way for the player to escape by running.

```gdscript
# CORRECT: Ring AoE with inner safe zone
var ring_inner_radius: float = 1.0  # Safe zone
var ring_outer_radius: float = 4.0  # Damage zone

# CORRECT: Line AoE with side gap
var line_width: float = 2.0  # Never wider than 60% of arena width

# BANNED: Full-screen AoE that covers 100% of playable area
# BANNED: AoE wall that blocks all escape routes
```

**Maximum AoE coverage rule**: No single AoE may cover more than 60% of the visible play area.

### E2 — Mandatory Telegraph Before Un-dodgeable Attacks
Every enemy attack that deals AoE damage MUST show a ground decal/telegraph BEFORE dealing damage.

```gdscript
# MANDATORY sequence for any AoE attack:
# 1. Show red warning decal on ground (minimum 0.8s visible)
# 2. Optional: Play anticipation animation
# 3. Deal damage only AFTER telegraph duration ends
# 4. Remove decal

const MIN_TELEGRAPH_DURATION: float = 0.8  # seconds

func _start_aoe_attack() -> void:
    var decal := _spawn_warning_decal(target_position, aoe_radius)
    await get_tree().create_timer(MIN_TELEGRAPH_DURATION).timeout
    _deal_aoe_damage(target_position, aoe_radius)
    decal.queue_free()
```

Tag system for enemy attacks:

```gdscript
enum AttackTag {
    MELEE,            # Standard melee, escapable by distance
    AOE_TELEGRAPHED,  # Area attack with mandatory ground warning
}
# Since there is no dodge, [Dodgeable] tag is NOT used in this game.
# ALL attacks are escapable by positioning only.
```

### E3 — Boss Attack Pattern Spacing
Boss attacks must have clear rhythm. Recovery phase is the player's counterattack window.

```gdscript
# Boss attack cooldown must be ≥ 1.5x the attack total duration
# Example: if attack takes 1.0s, cooldown must be ≥ 1.5s
@export var boss_cooldown_multiplier: float = 1.5
```

## See Also

- [REFERENCE.md](REFERENCE.md) — Full code templates, CC component design, status effect architecture
