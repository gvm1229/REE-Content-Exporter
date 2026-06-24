# md10015 Scene MOTLIST Actor Filtering and Bone-Spacing Repair

## Summary

`gamedemo\minidemo\md*` MOTLIST files can contain scene animation tracks for multiple animated actors. `md10015.motlist.1057` is one of these mixed scene MOTLISTs: it contains `md10015_ch0000_*`, `md10015_ch0100_*`, and `md10015_wp0900_*` actions.

Exporting all of those actions onto a single ch0100 skeleton creates misleading FBX files. ch0000 and weapon actions do not belong on Diana's armature, and the visible result can look like a frozen or badly stretched character even when the mesh scale, root bone scale, and Unreal-ready Blender path are correct.

After actor filtering, the ch0100 scene actions still carried bad local translation data on many Diana bones. This was a separate problem from mixed actors. The animation rotations were usable, but the local `.location` tracks were stretching Diana's skeleton apart.

The golden `ch0100_General_unreal.fbx` action `ch0100_General_0100_Stan_Loop` keeps local spacing stable: only `Hip` has meaningful animated location. The `md10015_ch0100_Reinforce_Start` action had animated local locations on 525 bones, including large offsets on `R_Wep`, `L_Wep`, limbs, neck, and hands. That changes bone spacing instead of just rotating and moving the character.

## Fix

The CLI now recognizes scene actor prefixes in MOTLIST action names:

```text
<motlist-name>_<actor-id>_<action-name>
```

Examples:

```text
md10015_ch0100_Reinforce_Start
md10015_ch0000_Reinforce_Start
md10015_wp0900_Reinforce_FocusGun_Loop
```

When selected scene actions contain more than one actor prefix, the exporter no longer silently writes all actors onto one skeleton. It filters to the actor inferred from the primary `--mesh`, such as `ch0100` from `ch0100_00.mesh.*` or `ch0000` from `ch0000_00_playergame.mesh.*`.

Users can override inference explicitly:

```powershell
--scene-actor ch0100
```

The escape hatch is:

```powershell
--allow-mixed-scene-animations
```

Use that only for diagnostics. The current exporter still produces one armature per export, so mixed scene actors are not a valid final Unreal-ready character animation export.

For the remaining ch0100 spacing distortion, the CLI also has an opt-in Blender-stage repair:

```powershell
--bone-spacing-reference-fbx "<golden>\ch0100_General_unreal.fbx" `
--bone-spacing-reference-action ch0100_General_0100_Stan_Loop `
--bone-spacing-allow-translation root,Hip,Null_Offset
```

This repair reads frame-1 pose-bone local locations from the reference action and clamps non-allowlisted pose-bone `.location` curves in the target actions to those reference values. Rotation curves are preserved.

## Bone-Spacing Issue

Skeletal animation should normally rotate bones around a stable rest hierarchy. Character motion may translate a small number of high-level bones, but ordinary limb, neck, hand, weapon socket, and helper bones should not receive large independent local translations every frame.

The bad md10015 ch0100 export looked stretched because many pose bones had animated local `.location` curves. In Blender terms, these were not object scale issues, root-bone scale issues, FBX axis issues, quaternion continuity issues, or Unreal import scale issues. The armature scale was already correct at `0.01`, and rotation jump audits showed zero adjacent quaternion jumps greater than pi radians. The distortion came from local bone offsets that changed the distance between parent and child bones.

The golden reference file is useful because it contains a known-good Diana armature exported through the June 7 Unreal-ready path:

```text
C:\Users\user\Downloads\PRAGMATA\Z_Extracted\ree-content-exporter\ch0100_00__20260607_083505__3bb902\ch0100_General_unreal.fbx
```

That file establishes the expected frame-1 local position for every named Diana pose bone after Blender/FBX finalization. The md10015 repair uses those local positions as spacing anchors, then keeps the md10015 rotations.

## Repair Algorithm

The reusable flag path runs only during the Blender Unreal-ready finalization stage:

1. Import the reference FBX in Blender.
2. Find the reference action by `--bone-spacing-reference-action`.
3. Read frame-1 pose-bone `.location` values from that action.
4. Clear the scene.
5. Import the source FBX produced by the exporter.
6. Apply the normal Unreal-ready armature transform handling.
7. For every imported target action, replace non-allowlisted pose-bone `.location` curves with constant keys equal to the reference bone location.
8. Leave all rotation curves untouched.
9. Leave allowlisted local translations untouched.
10. Export the final `*_unreal.fbx`.

The default allowlist is:

```text
root,Hip,Null_Offset
```

`root` and `Null_Offset` are kept because scene animation may use high-level offsets for placement. `Hip` is kept because the golden Diana reference itself has meaningful Hip local motion. The allowlist is intentionally explicit and user-configurable because a different character or scene may need a different motion carrier bone.

The repair is intentionally not a MOT parser change. It does not mutate `.mot` or `.motlist` data, does not change standalone `.mot` compatibility, and does not alter REE-Lib loading behavior. It is an export-time Blender cleanup for scene MOTLIST actions that are otherwise known to target the correct skeleton but contain bad local translation spacing.

## When to Use the Repair

Use `--bone-spacing-reference-fbx` when all of these are true:

- The export is a scene/minidemo MOTLIST action for a single filtered actor, such as `md10015_ch0100_*`.
- Mesh scale and root scale are already correct.
- The action visibly resembles the desired motion but the body stretches or bone distances change.
- Rotation jump audits are clean or are not the visible problem.
- A trusted same-character Unreal-ready reference FBX exists.

Do not use it as a general fix for:

- mixed actor exports;
- missing required mesh parts;
- wrong character skeletons;
- props or weapons exported onto a character armature;
- true animation retargeting between different skeletons;
- quaternion interpolation flukes.

If an animation is frozen, mapped to the wrong actor, or missing most channels, fix actor filtering, mesh selection, or missing-bone handling first.

## CLI Flags

```powershell
--bone-spacing-reference-fbx "<reference_unreal.fbx>" `
--bone-spacing-reference-action "<action-name-contains>" `
--bone-spacing-allow-translation root,Hip,Null_Offset
```

`--bone-spacing-reference-fbx` is required to enable the repair and must point to an existing FBX. It also requires `--unreal-ready-fbx` because the repair runs inside the Blender finalization script.

`--bone-spacing-reference-action` is optional. It defaults to:

```text
ch0100_General_0100_Stan_Loop
```

The action match is a case-insensitive contains match. For ch0100 md10015 repair, the intended reference action is `ch0100_General_0100_Stan_Loop` from the golden `ch0100_General_unreal.fbx`.

`--bone-spacing-allow-translation` is optional. It is a comma-separated bone-name list. Non-allowlisted bones are clamped to reference local positions; allowlisted bones keep their source local translation curves.

## md10015 ch0100 Probe

The focused ch0100 export selected 14 Diana actions from `md10015` instead of all 47 mixed actions:

```text
Loaded motlist md10015: total=47 nameFiltered=14 selected=14
```

The original mixed export reported 1323 skipped animation bone channels:

```text
ch0000: 875
ch0100: 56
wp0900: 392
```

After filtering to ch0100, only four unknown ch0100 bone hashes remained:

```text
2184993242
3724475523
573179932
386343545
```

Adding the otherwise omitted ch0100 mesh parts `15` and `45` did not resolve those four hashes. A REE-Lib probe found no clip names, original names, MOT bone names, or MOT parent names for them. Current evidence points to unnamed scene/helper/control channels rather than missing Diana body mesh parts.

## Verification

Build requirement:

```powershell
dotnet build -c Release
```

The branch implementation should be build-verified after the sibling `REE-Content-Editor` dependency exists at:

```text
C:\Users\user\Downloads\PRAGMATA\Tools\REE-Content-Editor\ContentEditor.App\ContentEditor.App.csproj
```

On the workstation where this fix was authored, that dependency folder was missing, so `dotnet build -c Release` could not compile the exporter and failed before type-checking the local changes. The embedded Blender repair logic was still validated by applying the same algorithm directly to the already-generated md10015 ch0100 Unreal-ready FBX.

One-off spacing-repaired output confirmed in Blender and playback:

```text
C:\Users\user\Downloads\PRAGMATA\Z_Extracted\ree-content-exporter\md10015_spacing_repair_final_20260624\md10015_ch0100_spacing_repaired_unreal.fbx
```

Repair audit:

| Export | Armatures | Meshes | Actions | Armature bones | Armature scale | Rotation jumps > pi | Non-allowlisted spacing diffs |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `md10015_ch0100_spacing_repaired_unreal.fbx` | 1 | 47 | 14 | 543 | `0.01` | 0 | 0 |

The repair clamped all non-allowlisted pose-bone local translations to the golden reference spacing. After repair, the md10015 ch0100 actions retained only the intended animated local translation channel, while rotation curves were left unchanged.

Earlier focused Unreal-ready actor-split exports:

```text
C:\Users\user\Downloads\PRAGMATA\Z_Extracted\ree-content-exporter\md10015_scene_actor_final_20260624\ch0100_00__20260624_104204__eb750f\md10015_ch0100_unreal.fbx
C:\Users\user\Downloads\PRAGMATA\Z_Extracted\ree-content-exporter\md10015_scene_actor_final_20260624\ch0000_00_playergame__20260624_104404__640827\md10015_ch0000_unreal.fbx
```

The source FBXs were removed after successful Blender finalization. The final verification folder contains only the two final `*_unreal.fbx` files and their skipped-bone reports.

Blender audit:

| Export | Armatures | Meshes | Actions | Armature bones | Armature scale | Rotation jumps > pi |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| `md10015_ch0100_unreal.fbx` | 1 | 46 | 14 | 543 | `0.01` | 0 |
| `md10015_ch0000_unreal.fbx` | 1 | 21 | 21 | 229 | `0.01` | 0 |

The ch0100 final report lists 56 skipped channels, which are the same four unknown hashes repeated across 14 selected ch0100 actions. The ch0000 final report is separate and lists 124 skipped channels for Hugh's selected actions.

Mesh-inferred filtering was also smoke-tested without passing `--scene-actor`. With `ch0100_00.mesh.*` as the primary mesh, the CLI inferred `ch0100` and selected 14 of 47 `md10015` actions:

```text
Scene actor: ch0100 (inferred)
Scene MOTLIST actor filter: source=md10015 actor=ch0100 selected=14/47 actors=ch0000:21, ch0100:14, wp0900:12
```

Reference-spacing audit before the repair:

| Action | Meaningful animated local-location bones | Largest local-location delta |
| --- | ---: | ---: |
| `ch0100_General_0100_Stan_Loop` | 1 | `Hip`, about `1.70` |
| `md10015_ch0100_Reinforce_Start` | 525 | `R_Wep`, about `163.75` |

The important signal is the count difference. A stable Diana reference has only one meaningful animated local-location bone. The bad scene action had hundreds, so it was changing internal skeleton spacing throughout the body.

First-frame local-location differences from the golden reference were also large before repair:

| Bone | Difference from golden reference |
| --- | ---: |
| `root` | about `599.09` |
| `R_Wep` | about `154.96` |
| `L_Wep` | about `132.83` |
| `Null_Offset` | about `109.72` |
| `Hip` | about `70.79` |

After applying the repair, the representative output had zero non-allowlisted spacing differences above the audit tolerance. Playback confirmed that the Slenderman-like stretching was removed while the scene motion remained recognizable.

## Correct Workflow

For Diana scene actions:

```powershell
REE-Content-Exporter-CLI.exe `
  --mesh "<re_chunk_000>\character\ch\ch01\ch0100\00\ch0100_00.mesh.251121828" `
  --streaming "<re_chunk_000>\streaming\character\ch\ch01\ch0100\00\ch0100_00.mesh.251121828" `
  --additional-mesh "<re_chunk_000>\character\ch\ch01\ch0100\10\ch0100_10.mesh.251121828" `
  --additional-mesh "<re_chunk_000>\character\ch\ch01\ch0100\20\ch0100_20.mesh.251121828" `
  --additional-mesh "<re_chunk_000>\character\ch\ch01\ch0100\40\ch0100_40_neo.mesh.251121828" `
  --additional-streaming "<mesh40>=<stream40>" `
  --motlist "<re_chunk_000>\gamedemo\minidemo\md10015\motlist\md10015.motlist.1057" `
  --scene-actor ch0100 `
  --bone-spacing-reference-fbx "<golden>\ch0100_General_unreal.fbx" `
  --fbx-scale 100 `
  --no-placeholder-animation-bones `
  --unreal-ready-fbx `
  --blender "C:\Program Files\Blender Foundation\Blender 4.5\blender.exe" `
  --output "<export-root>\md10015_ch0100.fbx"
```

For Hugh scene actions, use the ch0000 mesh set and `--scene-actor ch0000`.

## Notes

This fix does not change MOT or MOTLIST parsing. It only changes which already-loaded MOTLIST actions are selected for a single-armature export.

This fix also does not replace the Unreal-ready Blender finalization path. `*_unreal.fbx` remains the final expected artifact for Unreal import.

The bone-spacing repair should remain opt-in. Normal character MOTLISTs, known-good ch0100 Attack/General exports, and standalone `.mot` exports should not need reference spacing. If future scene MOTLISTs show the same failure pattern, first prove that the actor is filtered correctly, then audit local-location curves, then choose a same-character golden reference.
