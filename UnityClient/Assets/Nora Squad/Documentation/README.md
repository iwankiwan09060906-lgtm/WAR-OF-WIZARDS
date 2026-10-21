# Nora Squad — Stylized Fantasy Characters

Thank you for choosing **Nora Squad**! Nora Squad is a collection of **34 stylized fantasy characters**
(105 animations) in five themed packs, a free Starter pack and the Complete Collection. Every pack contains
hand-painted characters that work unlit or lit, a ready-to-use character component and two demo scenes. The characters
you own are in `Nora Squad/Characters`; the table under *Characters* lists the whole collection and the pack of each one.

Packs share the same folders, so you can import several Nora Squad packs into one project: the demo scenes pick up
every Nora Squad character in the project automatically when you open them.

> **About the effects:** the particle effects in the `Demo Particles` folder are **simple demo effects**. They are
> included only to show where each animation's impact events fire. They are not the focus of this pack — replace them
> with your own effects, switch them off, or remove them in one click (see *Demo effects* below).

## Quick start

1. Open **Tools ▸ Nora Squad ▸ Open Demo Scene** and press Play.
   ← / → switch characters · 1–4 play Idle / Move / Attack / Skill · Space attacks · L switches Unlit/Lit · E toggles demo effects ·
   drag to orbit · scroll to zoom.
2. Open **Tools ▸ Nora Squad ▸ Open Lineup Scene** to see all your characters at once (click a name to make it attack).
3. Drag any prefab from `Nora Squad/Characters/<Name>/Prefabs` into your scene.
4. Call the character from your own scripts:

```csharp
using NoraSquad;

public class MyUnit : MonoBehaviour
{
    [SerializeField] NoraCharacter character;

    void Start()
    {
        character.onHit.AddListener(DealDamage);   // fired on the impact frame of the attack
        character.PlayAttack();
    }

    void DealDamage() => Debug.Log("Hit!");
}
```

## Requirements

* Unity 2022.3 LTS or newer (tested with 2022.3 and Unity 6).
* **Universal Render Pipeline** or **Built-in Render Pipeline** — the materials work in both without conversion.
* No other packages are required. The demo works with both the old Input Manager and the new Input System.

## What is included

| Folder | Contents |
|---|---|
| `Characters/<Name>/Models` | `NS_<Name>.fbx` (mesh + skeleton) and one FBX per animation, `NS_<Name>@<Animation>.fbx` |
| `Characters/<Name>/Textures` | Hand-painted albedo textures (2048²) |
| `Characters/<Name>/Materials` | `M_NS_<Name>_<Part>_HandPainted` (Nora Squad / Stylized Character shader) |
| `Characters/<Name>/Animator` | Animator Override Controller with the character's clips |
| `Characters/<Name>/Prefabs` | `NS_<Name>`: ready-to-use prefab |
| `Shared` | Character shader, base animator controller, runtime scripts, editor window |
| `Demo Particles` | Generated textures, materials and effect prefabs used by the demo |
| `Demo` | Showcase scene (one character at a time), Lineup scene (all characters) and their scripts. The scenes list every Nora Squad character in the project automatically. |

Every FBX animation take has a unique name (`NS_<Name>_<Animation>`), and every asset uses the `NS_` prefix so it
never clashes with other packages.

## The NoraCharacter component

Every prefab has a **NoraCharacter** component on its root (next to the Animator).

| Member | Description |
|---|---|
| `PlayIdle()`, `PlayMove()`, `PlayAttack()`, `PlaySkill()` | Cross-fade to an animation. Attack and Skill return to Idle when finished (unless *Loop Actions* is on). |
| `Play(NoraAction action)` | Same as above with an enum. |
| `SetAnimationSpeed(float speed)` | Playback speed multiplier. |
| `SetLighting(NoraLighting mode)` | Switch between `NoraLighting.Unlit` and `NoraLighting.Lit` at runtime (shared material copies, SRP Batcher friendly). |
| `Lighting`, `HasMove`, `HasSkill` | Query the current mode and what this character supports. |
| `Target` | Optional transform that projectiles fly to. When empty they fly straight ahead. |
| `onHit` / `onSkill` | UnityEvents raised on the impact frame of the Attack / Skill animation. |
| *Hit Effect*, *Skill Effect*, *Projectile* (Inspector) | Optional prefabs spawned automatically on those events. |

Characters without a dedicated move or skill animation play Idle (or their attack) instead, so every
character can be driven with the same calls.

### Using your own Animator Controller

The clips are regular Generic animation clips inside the `@` FBX files; use them in any controller you like.
Keep a **NoraCharacter** (or any script with `OnHit()` / `OnSkill()` methods) on the Animator's GameObject to
receive the animation events, or remove the events in the FBX import settings.

## Lighting: Unlit or Lit

Every character uses the same hand-painted textures in two ways:

* **Unlit** (default): shown exactly as painted, independent of scene lights. The cheapest option and the classic
  look for mobile and stylized games.
* **Lit**: soft stylized lighting from the main light, ambient light and received shadows, for lit 3D scenes.

Set **Lighting** on the NoraCharacter component (applied in play mode), call `SetLighting()` from code, or move the
material's *Lighting* slider to change the look in the editor.

### Shader settings (Nora Squad / Stylized Character)

| Property | Description |
|---|---|
| Albedo / Tint | Base texture and colour multiplier |
| Lighting | 0 = unlit (as painted), 1 = stylized lit |
| Shadow Color, Light Wrap | Colour and softness of the shaded side when lit |
| Receive Shadows | Strength of received shadows when lit |
| Rim Color / Rim Power | Optional rim highlight |
| Emission / Emission Color | Optional glow map |

## Demo effects

Every character can spawn an optional effect when its attack or skill lands (*Hit Effect*, *Skill Effect* or
*Projectile* on the NoraCharacter component). The pack ships with simple demo effects made with Unity's built-in
Particle System (`Demo Particles` folder). The `onHit` / `onSkill` events are raised whether effects are on, off or removed.

| How | What it does |
|---|---|
| **Tools ▸ Nora Squad ▸ Demo Effects ▸ Disable On All Characters** | Keeps the effects in the project but stops all characters from spawning them. |
| **Tools ▸ Nora Squad ▸ Demo Effects ▸ Enable On All Characters** | Turns them back on. |
| **Tools ▸ Nora Squad ▸ Demo Effects ▸ Remove Demo Effects From Project…** | Clears the effect references on every character prefab and deletes the `Demo Particles` folder — no missing references are left behind. Re-import the package to get them back. |
| `NoraCharacter.GlobalEffectsEnabled = false;` | Runtime switch for all characters. |
| `character.SpawnEffects = false;` | Runtime switch for one character. |
| *E* key / *Effects* button in the demo scenes | Toggle the demo effects while playing. |

To use your own effects, assign your prefabs to *Hit Effect*, *Skill Effect* or *Projectile* (a prefab with a
`NoraProjectile` component) and set *Hit Point* / *Skill Point* to where they should appear.

## Technical specs

| | |
|---|---|
| Characters in the collection | 34 in 5 themed packs |
| Animations | 105 (2–4 per character: Idle, Walk/Run, Attack, Skill) |
| Triangles per character | 4,778 – 15,794 (average 9,970) |
| Joints per character | 24 – 123 |
| Skinned meshes (draw calls) per character | 2 – 9 |
| Materials / textures per character | 1 – 3 (one hand-painted texture per material) |
| Texture size | 2048² on desktop, 1024² override on Android/iOS |
| Texture memory per texture | ~5.3 MB desktop (BC7), ~0.6 MB mobile (ASTC 6x6), with mip maps |
| Rig | Generic, in-place animations (no root motion) |
| Rendering | URP and Built-in · SRP Batcher compatible materials |

## Characters

| Character | Pack | Height | Tris | Joints | Meshes / Materials | Animations | Demo effect |
|---|---|---|---|---|---|---|---|
| Alchemist | Kingdom Heroes | 1.60 m | 11,902 | 59 | 7 / 2 | Idle (loop), Walk (loop), Attack | Projectile_Water |
| Anchor | Kingdom Heroes | 2.00 m | 9,146 | 58 | 6 / 2 | Idle (loop), Walk (loop), Attack, Skill | Hit_Physical / Hit_Water |
| Archer | Kingdom Heroes | 1.80 m | 10,786 | 59 | 6 / 2 | Idle (loop), Walk (loop), Attack | Projectile_Arrow |
| Armor Master | Kingdom Heroes | 1.50 m | 10,544 | 55 | 9 / 2 | Idle (loop), Walk (loop), Attack | Hit_Physical |
| Barbarian | Monsters & Brutes | 2.30 m | 7,538 | 54 | 6 / 2 | Idle (loop), Walk (loop), Attack | Hit_Physical |
| Bearman | Monsters & Brutes | 2.40 m | 14,726 | 101 | 5 / 1 | Idle (loop), Walk (loop), Attack | Hit_Physical |
| Cyclops | Monsters & Brutes | 2.60 m | 8,620 | 60 | 5 / 2 | Idle (loop), Walk (loop), Attack | Hit_Physical |
| Damage Totem | Forest Guardians | 2.30 m | 6,732 | 42 | 5 / 1 | Idle (loop), Walk (loop), Attack | Hit_Fire |
| Dark Lady | Dark Legion | 2.60 m | 10,412 | 62 | 5 / 1 | Idle (loop), Walk (loop), Attack, Skill | Projectile_Dark / Ground_Summon |
| Dark Lord | Dark Legion | 2.40 m | 11,516 | 63 | 6 / 2 | Idle (loop), Walk (loop), Attack | Hit_Dark |
| Dryad | Forest Guardians | 1.90 m | 9,678 | 67 | 5 / 1 | Idle (loop), Walk (loop), Attack | Projectile_Nature |
| Elf Archer | Forest Guardians | 1.80 m | 10,950 | 61 | 6 / 2 | Idle (loop), Walk (loop), Attack | Projectile_Arrow |
| Elf Mage | Arcane Mages | 1.90 m | 10,718 | 68 | 6 / 1 | Idle (loop), Walk (loop), Attack | Projectile_Nature |
| Frost Wizard | Arcane Mages | 1.80 m | 7,988 | 62 | 5 / 1 | Idle (loop), Attack | Projectile_Frost |
| Gaia | Forest Guardians | 2.60 m | 13,702 | 105 | 5 / 1 | Idle (loop), Walk (loop), Attack | Projectile_Nature |
| Goblin Berserker | Monsters & Brutes | 1.30 m | 8,584 | 64 | 7 / 2 | Idle (loop), Run (loop), Attack | Hit_Physical |
| Goblin Bomber | Monsters & Brutes | 1.30 m | 9,782 | 72 | 6 / 1 | Idle (loop), Walk (loop), Attack | Projectile_Bomb |
| Golem | Monsters & Brutes | 2.80 m | 5,362 | 51 | 5 / 1 | Idle (loop), Walk (loop), Attack | Hit_Fire |
| Grukal | Monsters & Brutes | 2.80 m | 13,108 | 69 | 6 / 2 | Idle (loop), Walk (loop), Whirlwind, Leap | Hit_Physical / Ground_Slam |
| Healer | Forest Guardians | 2.20 m | 10,686 | 123 | 7 / 2 | Idle (loop), Walk (loop), Attack | Projectile_Nature |
| Knight | Kingdom Heroes | 2.70 m | 15,794 | 122 | 8 / 3 | Idle (loop), Run (loop), Attack | Hit_Physical |
| Minion Skeleton | Dark Legion | 1.40 m | 8,548 | 54 | 3 / 1 | Idle (loop), Walk (loop), Attack | Hit_Physical |
| Nature Templar | Forest Guardians | 2.40 m | 11,818 | 85 | 7 / 3 | Idle (loop), Walk (loop), Attack | Hit_Nature |
| Ocean Queen | Arcane Mages | 1.90 m | 11,710 | 95 | 5 / 1 | Idle (loop), Walk (loop), Attack | Projectile_Water |
| Old Soldier | Kingdom Heroes | 1.85 m | 8,334 | 61 | 7 / 2 | Idle (loop), Walk (loop), Attack | Hit_Physical |
| Rifleman | Kingdom Heroes | 1.45 m | 8,154 | 56 | 6 / 2 | Idle (loop), Walk (loop), Attack | Projectile_Bullet |
| Rogue | Kingdom Heroes | 1.75 m | 7,822 | 72 | 5 / 1 | Idle (loop), Run (loop), Attack | Hit_Physical |
| Skeleton | Dark Legion | 1.80 m | 7,292 | 24 | 3 / 1 | Idle (loop), Walk (loop), Attack | Hit_Physical |
| Slime | Monsters & Brutes | 1.50 m | 4,778 | 50 | 2 / 1 | Idle (loop), Walk (loop), Attack | Hit_Nature |
| Succubus | Dark Legion | 2.00 m | 9,974 | 84 | 5 / 1 | Idle (loop), Walk (loop), Attack | Hit_Fire |
| Summoner | Arcane Mages | 1.90 m | 9,812 | 88 | 5 / 1 | Idle (loop), Walk (loop), Rise, ControlLoop (loop) | Hit_Dark / Ground_Summon |
| Wind Master | Arcane Mages | 1.90 m | 9,586 | 64 | 6 / 2 | Idle (loop), Walk (loop), Attack | Projectile_Arcane |
| Wizard | Arcane Mages | 2.00 m | 9,426 | 62 | 6 / 2 | Idle (loop), Walk (loop), Attack | Projectile_Arcane |
| Wolfman | Monsters & Brutes | 2.10 m | 13,440 | 48 | 5 / 1 | Idle (loop), Walk (loop), Attack | Hit_Physical |

Heights are measured in the idle pose including headgear and weapons. Animations use in-place motion (no root motion).

## Performance notes

* Textures import at 2048 on desktop and are overridden to 1024 ASTC on Android and iOS. Lower *Max Size* in the
  texture importer for smaller builds.
* All character materials are SRP Batcher compatible.

## Support

Questions, bug reports or feature requests: **contact.norasquad@gmail.com**

Please include your Unity version and render pipeline. Thank you for your purchase!
