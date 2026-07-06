# ch6500 ArmBlade Animation Anomaly Lessons

This document captures what was learned while repairing PRAGMATA `ch6500_attack.motlist.1057` ArmBlade animation exports. Use it as the first diagnostic checklist before applying similar fixes to other MOTLISTs such as General.

## Verified export context

The verified Attack export uses:

```powershell
--game pragmata
--mesh "<extract>\character\ch\ch65\ch6500\00\ch6500_00.mesh.251121828"
--additional-mesh "<extract>\character\ch\ch65\ch6500\60\ch6500_60.mesh.251121828"
--streaming "<extract>\streaming\character\ch\ch65\ch6500\00\ch6500_00.mesh.251121828"
--additional-streaming "<ch6500_60 normal mesh>=<ch6500_60 streaming mesh>"
--motlist "<extract>\character\animation\ch\ch65\ch6500\motlist\ch6500_attack.motlist.1057"
--fbx-scale 100
--no-placeholder-animation-bones
--no-textures
--fix-ch6500-armblade-translation
--unreal-ready-fbx
--blender "<Blender 4.5.9>\blender.exe"
```

Keep source FBXs during diagnosis. Compare both the source FBX and Unreal-ready Blender FBX before deciding where a symptom is introduced.

## Core references

For the verified `0575` action:

- Idle should use the left ArmBlade idle placement as the reference.
- Extension should use the right ArmBlade extended placement as the reference and mirror it to the left by X sign.
- `R_ArmBlade_Gimic_01` and `R_ArmBlade_Gimic_03` can contain one-frame rotation outliers during the deployment window; smooth these separately from location fixes.

Do not assume every action should be extended. Several actions are explicitly idle-right-blade actions.

## Anomaly classes found

### 1. Good after generic 0575-style repair

Actions:

- `1010`
- `0700`
- `0575`
- `0570`
- `0254`
- `0252`
- `0250`

Observed state: blade deployment/retraction is already correct after the generic 0575-style visible-curve repair. Do not add targeted overrides unless a new Unreal check proves a specific regression.

### 2. Left blade underextends

Actions:

- `0510`
- `0270`
- `0231`

Observed state: the left blade is in the correct idle location but barely extends, or remains near idle, while the animation intent needs left-side extension.

Repair rule:

- Use the `L_ArmBlade_00` source extension alpha as the timing driver.
- Apply that same alpha to both `L_ArmBlade_00` and `L_ArmBlade_Gimic_05`.
- Do not let `L_ArmBlade_Gimic_05` force to full extension while `L_ArmBlade_00` is still idle.
- For `0231`, `0270`, and `0510`, also restore the right blade to idle-side X placement after fixing the left extension.

Diagnostic clue for future MOTLISTs: if a blade "floats from idle" or "barely extends", check whether the main blade bone has a tiny source X span and whether a Gimic bone is carrying a constant offset. This is a left/right extension-amplification problem, not a root-motion problem.

### 3. Left blade extension window is wrong

Action:

- `0230`

Observed state: the left blade only extends a little during frames 12-71, then fully extends during frames 72-148. The intended state is full extension across frames 12-148.

Repair rule:

- Force `L_ArmBlade_00` and `L_ArmBlade_Gimic_05` to the full left extension reference for frames 12-148.
- Preserve surrounding frames so the action can return to its original state outside the window.

Diagnostic clue: if the blade eventually reaches a correct extension but too late, it is a timing-window problem, not an amplitude problem.

### 4. Right side should mirror fully extended left

Actions:

- `0001`
- `1012`
- `1014`
- `1016`

Observed state: the left blade is fully extended and correct, while the right blade is idle or barely extended.

Repair rule:

- Mirror the fully extended left blade X onto `R_ArmBlade_00`.
- Mirror the left Gimic X onto `R_ArmBlade_Gimic_05`.
- Preserve the non-X axes unless a separate audit proves they are wrong.

Diagnostic clue: if L is stable and fully extended for the entire action, but R remains at an idle or small value, classify it as a right-mirror-missing problem.

### 5. Right blade is idle, not extended

Actions include the Attack actions not listed in the other classes, for example:

- `0000`
- `0005`
- `0010`
- `0240`
- `0260`
- `0300`
- `0500`
- `0520`
- `0521`
- `0522`

Observed state: these are idle R-blade actions. Earlier attempts that forced the right blade to the full 0575 extension made them wrong.

Repair rule:

- Restore `R_ArmBlade_Gimic_05` X to the idle-side mirrored placement.
- Restore `R_ArmBlade_00` X to the idle-side placement.
- `0005` is a special idle offset case where `R_ArmBlade_00` needs the doubled idle offset compared with `0000`.

Manual Unreal corrections that established this class:

- `0000`: `R_ArmBlade_00` X from about `-107.64669` to `-21.328344`; `R_ArmBlade_Gimic_05` X from about `-52.003036` to `-32.209831`.
- `0005`: `R_ArmBlade_00` X from about `-107.646683` to `-42.656688`; `R_ArmBlade_Gimic_05` X from about `-52.003052` to `-32.209831`.

Diagnostic clue: if the action intent is idle, do not use the 0575 extension reference. Check the intended pose first.

### 6. Extended-to-idle timed retraction

Action:

- `1018`

Observed state: both blades should start fully extended, transition to idle during frames 99-107, and remain idle from frame 107 onward. Before the targeted fix, R was barely extended and L stayed extended.

Repair rule:

- Hold both blades fully extended through frame 99.
- Interpolate both `ArmBlade_00` and `ArmBlade_Gimic_05` X values to idle over frames 99-107.
- Keep both sides idle after frame 107.

Diagnostic clue: if a blade starts correctly but fails to return, classify it as a state-transition problem. Do not solve it with a constant full-extension or constant idle override.

## General diagnostic workflow

Use this workflow before changing more animations:

1. Identify intended blade state from Unreal playback or trusted reference: idle, left extended, right extended, both extended, or transition.
2. Audit `L_ArmBlade_00`, `R_ArmBlade_00`, `L_ArmBlade_Gimic_05`, and `R_ArmBlade_Gimic_05` local location curves in the source FBX and Unreal-ready FBX.
3. Separate amplitude errors from timing errors:
   - small span or barely moving means amplitude/mirror repair;
   - correct peak at wrong frames means timing-window repair;
   - correct start/end but wrong interpolation means transition repair.
4. Check Gimic bones separately from `ArmBlade_00`; they may need the same timing alpha but not the same raw X value.
5. Do not apply one global rule across a MOTLIST. The Attack set contains at least six distinct ArmBlade anomaly classes.

For General animations, first classify the action using these same buckets. For example, if General `0101` is intended to extend the left blade but the left blade floats from idle, start by testing the left-underextension class: inspect the left main blade X span, drive `L_ArmBlade_Gimic_05` from `L_ArmBlade_00` timing, and preserve the right side according to the intended action state.

## General MOTLIST auto-repair trial

The General pass is intentionally curve-based, not token-table based.

Current rules:

- If a General action has a tiny but real `L_ArmBlade_00` X span, with peak X still near idle, classify it as left-underextension and scale that timing to the known full left extension X values.
- Use the `L_ArmBlade_00` X alpha to drive `L_ArmBlade_Gimic_05` X, so the Gimic bone does not float independently from the main blade.
- If `R_ArmBlade_00` or `R_ArmBlade_Gimic_05` starts at a large constant offset and has near-zero span, classify it as a right-idle-offset problem and restore both right-side X curves to idle.
- Scope the Attack token table to action names containing `Attack_`; do not let numeric Attack tokens such as `0000` or `0500` match General action names.

Initial General diagnostic findings:

- `ch6500_General_0101_idle_reaction_L_verC` showed the same underextension/floating pattern: `L_ArmBlade_00` moved only from about `-1.694` to `0.000`, while the intended state is left extension.
- Most General actions showed constant right idle offsets around `R_ArmBlade_00 = 42.657` and `R_ArmBlade_Gimic_05 = 64.387`, which should be treated as idle-right placement errors, not right extension.
