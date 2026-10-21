FREE Shield Effect - Procedural Hexagonal Shield (URP)
======================================================
Procedural hexagonal energy shield for Universal Render Pipeline (URP).
Additive fresnel glow, scrolling hex grid, and depth-based intersection highlight.

Contents
--------
ShieldEffect/
  Prefabs/           Ready-to-use shield prefabs (Sphere, Cube, Quad)
  Materials/         Blue / Green / Purple shield material variants
  Shaders/           HexShield URP shader
  Scenes/            ShieldEffect_Demo showcase scene
  Documentation/     This file

Prefabs
-------
1. PF_Shield_Sphere   — Blue hexagonal shield (sphere mesh)
2. PF_Shield_Cube     — Green hexagonal shield (cube mesh)
3. PF_Shield_Quad     — Purple hexagonal shield (quad mesh)

Materials
---------
1. M_HexShield_Blue
2. M_HexShield_Green
3. M_HexShield_Purple
4. M_DemoFloor_Dark   (demo scene floor only — optional for your game)

How to use (in your game)
-------------------------
1. Import this package into a URP project.
2. Enable Depth Texture (required for intersection glow — see URP Setup below).
3. Drag a prefab from Prefabs/ into your scene, OR instantiate at runtime:
   Instantiate(shieldPrefab, position, Quaternion.identity);
4. Scale the transform to fit your character / vehicle / area.
5. Duplicate a material if you want a custom color without editing the shared asset.

URP Setup (required)
--------------------
This shader samples the camera depth texture for contact / intersection glow.

Enable Depth Texture using ONE of these methods:

A) URP Asset (recommended for the whole project)
   1. Select your Universal Render Pipeline Asset
      (Project Settings → Graphics → Scriptable Render Pipeline Settings,
       or find the asset referenced by your Quality level).
   2. Enable Depth Texture.

B) Per-Camera
   1. Select your Camera.
   2. In Universal Additional Camera Data / Rendering, enable Depth Texture.

If Depth Texture is off, the hex grid and fresnel still render, but the
intersection glow against floors and geometry will be weak or missing.

Demo Scene
----------
Open Scenes/ShieldEffect_Demo and press Play (or view in Scene/Game view).
- Dark environment for clearer additive glow
- Three labeled shield variants (Sphere / Cube / Quad)
- On-screen usage hints for Prefabs and Depth Texture

Material properties
-------------------
Open a material using shader ShieldEffect/HexShield:

- Base Color          HDR tint of the shield
- Hex Scale           Density of the hexagonal grid
- Line Width          Thickness of hex cell edges
- Glow Intensity      Overall brightness multiplier
- Fresnel Power       Edge glow falloff (higher = tighter rim)
- Intersection Range  Depth contact glow distance
- Scroll Speed        Vertical scroll speed of the hex pattern

Requirements
------------
- Unity 6000.0 or newer (Unity 6)
- Universal Render Pipeline (URP)
- Depth Texture enabled for full intersection effect

Asset Store upload checklist
----------------------------
1. Export / upload ONLY the folder: Assets/ShieldEffect
2. Do not include other project folders
3. Tag as URP + VFX / Shader
4. Attach ShieldEffect_Demo screenshots / GIF for the store page
5. Validate with Asset Store Tools → Validator before submit

Support note
------------
Keep all pack assets inside ShieldEffect/ so imports stay clean for buyers.
If the intersection glow looks wrong after import, verify Depth Texture is enabled.
