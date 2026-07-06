# AGENTS.md Template: Blender to Unreal Model Automation

Operational rules for AI agents working on Blender-based model preparation for Unreal Engine.

Copy this file into a project folder as `AGENTS.md` when you want future AI agents to handle Blender automation, FBX cleanup, skeletal mesh handling, and Unreal-ready output consistently.

## Language rule

- Always answer the user in Korean unless the user explicitly asks for another language.
- Keep generated code, commands, filenames, logs, and technical identifiers in their original language.
- If the user asks for an English artifact, write that artifact in English while still explaining your work in Korean.

## First steps for every session

- Look for a local `AGENTS.md` or equivalent project instruction file before doing work.
- Read relevant project docs, scripts, and existing Blender automation before making changes.
- Preserve user files and existing work. Do not overwrite source assets or final exports unless the user explicitly asks.
- Prefer reproducible scripts over manual Blender UI steps.
- Verify the produced asset before claiming completion.
- Record durable workflow lessons in project documentation when the discovery should guide future sessions.

## Source asset input rules

- Treat normal mesh files and streaming mesh files as complementary inputs, not interchangeable alternatives.
- Use normal mesh files as the authoritative `--mesh` or `--additional-mesh` source because they carry skeleton, bones, material slots, metadata, draw calls, LOD structure, and buffer layout.
- Use streaming mesh files only through the project's streaming input mechanism, such as `--streaming` or `--additional-streaming`, because they carry external geometry buffers referenced by the normal mesh.
- For every primary or additional mesh, check whether a matching streaming file exists and pass it explicitly when it does.
- Do not invent streaming paths that do not exist on disk. Verify source paths before exporting.
- When adding additional meshes, also verify their material and texture sources. Extra mesh geometry without its matching material data often produces a visually incomplete Unreal import.

## Blender execution rules

- Use Blender in background mode for automation:

```powershell
& $Blender --background --factory-startup --python $Script
```

- Use `--factory-startup` to avoid user add-ons, startup files, and local UI preferences affecting export results.
- Check the Blender version before relying on export behavior. If the project has a known-good Blender version, use it consistently.
- Emit explicit progress lines such as `BLENDER_PROGRESS 2/6 importing source FBX`; do not leave Blender's raw FBX tuple output as the only progress signal.
- Clear the scene and stale datablocks before each import:

```python
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete()
for datablocks in (bpy.data.actions, bpy.data.armatures, bpy.data.meshes, bpy.data.materials):
    for datablock in list(datablocks):
        datablocks.remove(datablock, do_unlink=True)
```

## Unreal unit and axis normalization

Use this baseline for Unreal-ready FBX exports unless the project has a verified reason to differ:

```python
bpy.context.scene.unit_settings.system = 'METRIC'
bpy.context.scene.unit_settings.scale_length = 0.01
```

For Blender FBX export:

```python
bpy.ops.export_scene.fbx(
    filepath=str(out),
    check_existing=False,
    use_selection=False,
    object_types={'ARMATURE', 'MESH'},
    use_mesh_modifiers=True,
    add_leaf_bones=False,
    primary_bone_axis='Y',
    secondary_bone_axis='X',
    use_armature_deform_only=False,
    axis_forward='-Z',
    axis_up='Y',
    global_scale=1.0,
    apply_unit_scale=True,
    apply_scale_options='FBX_SCALE_ALL',
    use_space_transform=True,
    bake_space_transform=False,
    path_mode='AUTO',
    embed_textures=False,
)
```

Important constraints:

- If the pipeline has a pre-Blender source export stage, record where scaling is applied. A common verified pattern is source FBX export at centimeter scale, such as `--fbx-scale 100`, followed by Blender export with `global_scale=1.0`.
- Do not compensate twice for scale. If the source stage already scaled into centimeter-sized data, do not apply another `global_scale=100` in Blender.
- Apply armature object rotation and scale before final export when normalizing imported FBX data:

```python
bpy.context.view_layer.objects.active = armature
armature.select_set(True)
bpy.ops.object.transform_apply(location=False, rotation=True, scale=True, properties=True)
```

- Do not try to "fix" a displayed root bone roll value just because Unreal shows a 90-degree roll. Validate the actual result: upright mesh, correct animation basis, stable playback, correct root motion, and scale `1.0`.
- Only add root, roll, or basis normalization after manually re-verifying the full skeleton and all relevant animations in Unreal.

## FBX import rules

Use stable import settings for source FBX files:

```python
bpy.ops.import_scene.fbx(
    filepath=str(src),
    use_anim=True,
    automatic_bone_orientation=False,
    ignore_leaf_bones=False,
    force_connect_children=False,
)
```

- Use `use_anim=False` for mesh-only donor imports when animations are not needed.
- Avoid `automatic_bone_orientation=True` unless the project has verified that it preserves the target skeleton and animation basis.
- After import, count armatures, meshes, actions, and expected target bones before modifying the scene.

## Split animation set rules

- For large animation folders, prefer split exports that produce one final Unreal-ready FBX per exportable animation set or MOTLIST.
- Empty or no-selected source animation sets should be skipped by exporter logic and documented in a report such as `skipped-motlists.md`.
- Some source FBXs can be generated successfully but import into Blender with zero actions. Treat these as skipped Blender imports, not full script failures, and document them in `skipped-blender-motlists.md`.
- Do not classify a zero-action import as fatal unless the user specifically asks to debug that animation set.
- The final Unreal deliverable is the Blender re-exported file, commonly named like `<asset>_<animation-set>_unreal.fbx`. Do not treat the temporary source FBX as the golden Unreal import artifact unless the user explicitly asks for intermediates.

## Animation export rules

For animated skeletal FBX exports:

- Create explicit NLA strips for actions instead of relying on Blender's "all actions" scan.
- Bake every frame with no simplification when targeting Unreal.
- Use all bones and forced start/end keys so Unreal receives stable animation stacks.

Recommended animation export settings:

```python
bpy.ops.export_scene.fbx(
    filepath=str(out),
    check_existing=False,
    use_selection=False,
    object_types={'ARMATURE', 'MESH'},
    use_mesh_modifiers=True,
    add_leaf_bones=False,
    primary_bone_axis='Y',
    secondary_bone_axis='X',
    use_armature_deform_only=False,
    bake_anim=True,
    bake_anim_use_all_bones=True,
    bake_anim_use_nla_strips=True,
    bake_anim_use_all_actions=False,
    bake_anim_force_startend_keying=True,
    bake_anim_step=1.0,
    bake_anim_simplify_factor=0.0,
    axis_forward='-Z',
    axis_up='Y',
    global_scale=1.0,
    apply_unit_scale=True,
    apply_scale_options='FBX_SCALE_ALL',
    use_space_transform=True,
    bake_space_transform=False,
    path_mode='AUTO',
    embed_textures=False,
)
```

For mesh-only exports:

- Set `bake_anim=False`.
- Confirm no unexpected actions or animation data remain if the output should be static.

### Quaternion and root-rotation safety

- Normalize quaternion rotation keys and keep each bone channel on a continuous quaternion hemisphere before export.
- Do not leave sparse quaternion gaps for downstream importers to solve differently. For Unreal-targeted FBX, bake animated rotations at integer frames with shortest-path interpolation behavior.
- Adjacent quaternions can represent the same pose with opposite signs. Use the previous exported key as the continuity reference; when the dot product is negative, negate the current key before writing it.
- Guard against zero-length quaternions by reusing the previous valid key, or identity for the first key.
- Blender finalization should also rebake imported sparse pose-bone quaternion curves at integer frames before writing the final FBX.
- Root spinning or wobbling during an otherwise simple turn can come from transient off-axis quaternion components in a mostly single-axis root rotation. Stabilize short off-axis root spikes by interpolating them between surrounding clean root-axis frames.
- Do not confuse real root translation motion with root rotation wobble. If the root has local location keys in the source FBX, Blender may be preserving valid root motion rather than introducing a wobble bug.
- Do not mutate source animation data just to make FBX interpolation safer. Treat this as an export representation repair unless the user explicitly asks for source data editing.

## Mesh merge and attachment rules

- Decide which armature is authoritative before merging anything. The final FBX should normally contain exactly one armature.
- When using one mesh as a donor for another skeleton, remove the donor armature before final export.
- Bind kept donor meshes to the final armature with an Armature modifier and parent relationship.
- Remove vertex groups that do not exist in the final armature bone set.
- Attachments should be weighted to real target bones, not helper, root, socket, or control bones unless the user explicitly requests those.
- Preserve world transforms when transferring manually placed objects between files.
- For scale changes on an attachment, scale around a deliberate pivot such as the combined mesh bounds center or a target bone transform, not accidentally around the source object's origin.

Useful attachment pattern:

```python
group = obj.vertex_groups.new(name=target_bone_name)
group.add(list(range(len(obj.data.vertices))), 1.0, 'ADD')
modifier = obj.modifiers.new(name='Armature', type='ARMATURE')
modifier.object = target_armature
obj.parent = target_armature
```

## Geometry cleanup rules

- Use `obj.matrix_world` when comparing geometry between objects in world space.
- Use evaluated meshes when modifiers or armature deformation affect the geometry being measured:

```python
depsgraph = bpy.context.evaluated_depsgraph_get()
evaluated_obj = obj.evaluated_get(depsgraph)
evaluated_mesh = evaluated_obj.to_mesh()
try:
    # inspect evaluated_mesh vertices/polygons here
    pass
finally:
    evaluated_obj.to_mesh_clear()
```

- Use `bmesh` for face-level edits. Refresh lookup tables and indices before deleting faces:

```python
bm.faces.ensure_lookup_table()
bm.faces.index_update()
```

- If cleanup in the staging scene disagrees with the exported result, use a two-pass process:
  1. Export the assembled FBX.
  2. Reimport that FBX into a clean Blender scene.
  3. Perform pruning or measurement in final FBX space.
  4. Export the deliverable FBX again.

## Texture and material rules

- Do not silently accept missing textures unless the user intentionally requested a no-texture export.
- Verify that the final export folder contains a non-empty texture folder and any expected material or texture manifest files when textures are enabled.
- When merging multiple source assets, copy or rename material manifests so one asset does not overwrite another asset's manifest.
- Keep `embed_textures=False` by default unless the project's Unreal import workflow requires embedded textures.
- Treat unsupported optional texture types, such as depth or 3D resources not used by the final material workflow, as warnings only. Missing normal 2D texture sources, read failures, or conversion failures should remain fatal unless the user chooses otherwise.
- If texture conversion depends on helper executables or native DLLs, release artifacts must bundle them with the exporter or script entry point. Do not require users to install converter runtimes separately after download.

## Output and reporting rules

- Write outputs to a generated job folder, not beside source assets.
- Use outcome-visible logs, for example `export-SUCCESS.log` or `export-FAIL.log`.
- Remove temporary source FBXs after successful final export unless the user asks to keep debug intermediates.
- Also remove temporary source FBXs for animation sets skipped after zero-action Blender import unless the user asks to keep debug intermediates.
- Write a short report when the script performs nontrivial decisions such as skipped animations, removed donor meshes, pruned faces, remapped materials, or removed vertex groups.
- Common report names include `skipped-motlists.md`, `skipped-blender-motlists.md`, and `*.skipped-animation-bones.md`.

## Required verification

Before calling an export complete, reimport the final FBX in Blender and verify:

- Final FBX exists and has nonzero size.
- Expected number of armatures.
- Expected bone count and no accidental donor bones.
- Expected mesh count and object names.
- Expected action count and frame ranges for animated exports.
- Dense integer-frame rotation keys where sparse source rotations could cause long-arc importer glitches.
- No unexpected adjacent quaternion jumps greater than pi radians.
- No short off-axis root rotation spikes in actions that are supposed to be clean single-axis root turns.
- Mesh transforms, attachment placement, and scale are plausible.
- Vertex groups match the final armature bones.
- Texture/material files exist when textures are expected.

Then verify in Unreal when the task is about Unreal readiness:

- Skeletal mesh stands upright.
- Mesh imports at the intended size with actor/component scale `1.0`.
- Animations play without wobble or long-arc rotation glitches.
- Root motion behaves in the expected direction and distance when relevant.
- Materials can be reconnected and use the expected texture set.

## Troubleshooting heuristics

- Correct in Blender but wobbling in Unreal: audit quaternion continuity and sparse rotation curves, then rebake through Blender with explicit NLA strips and no animation simplification.
- Root spins for only a few frames during a turn: inspect the root quaternion curve for transient off-axis components in an otherwise single-axis rotation.
- Root moves in source and final FBX: distinguish intended root motion from rotation wobble before applying a stabilizer.
- Model lies down in Unreal: axis settings or skeleton/animation basis are mismatched.
- Model is tiny or huge in Unreal: unit scale or double scaling is wrong.
- Only the displayed root roll looks odd: do not treat that alone as failure; validate actual Unreal playback and placement.
- Missing or empty texture folder: treat as a failed export unless the task intentionally disables textures.
- Attachment floats away after scaling: the object was probably scaled around the wrong pivot.
- Donor mesh moves incorrectly after binding: check vertex groups, final armature bone names, parent inverse, and world transform preservation.
- Character stretches or explodes only in some scene animations: inspect pose-bone local `.location` curves before changing quaternion logic. Local-translation spacing repair should stay opt-in and reference-based unless the project proves it is broadly safe.
- Mixed actor scene animations on one skeleton can cause missing bone channels or bad deformation. Filter to the intended actor instead of exporting all scene actors onto one armature.
