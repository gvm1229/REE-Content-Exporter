# ch0100 Attack FBX → Blender 4.5.9 → Unreal 워크플로우

이 문서는 PRAGMATA `ch0100` Attack MOTLIST를 Unreal에서 안정적으로 쓰기 위해 현재 검증된 FBX 생성 절차를 정리한다. 핵심은 **REE-Content-Exporter가 원본 FBX를 만들고, Blender 4.5.9가 한 번 더 애니메이션을 베이크/재내보내기 하는 것**이다.

## 현재 결론

- Unreal-ready FBX에는 **Blender가 필요하다**.
- 직접 생성한 Assimp FBX는 Blender에서는 정상 재생되지만 Unreal에서는 애니메이션이 심하게 흔들릴 수 있다.
- Blender 4.5.9로 가져온 뒤 Armature의 회전/스케일을 적용하고, NLA strip 기반으로 애니메이션을 다시 베이크해 내보내면 Unreal에서 흔들림이 사라진다.
- 현재 Unreal에서 검증된 축/스케일 조합은 다음과 같다.
  - Blender scene unit: Metric, `scale_length = 0.01`
  - Blender FBX export axis: `-Z Forward`, `Y Up`
  - Blender FBX export `global_scale = 1.0`
  - `apply_unit_scale = True`
  - `apply_scale_options = FBX_SCALE_ALL`
- `--fbx-scale 100`은 **REE-Content-Exporter의 원본 FBX 생성 단계에서 유지**한다.
- Blender 단계에서는 Unreal이 centimeter 단위로 받아들이도록 scene unit을 `0.01`로 설정하고 `global_scale=1.0`으로 내보낸다. 이렇게 해야 Unreal import scale override 없이 배치 스케일이 정상이고, Unreal에서 스케일 값도 `1.0`으로 보인다.
- Skeleton root bone의 Roll/X rotation 값 `90°`는 현재 **정규화하지 않는다**. Unreal은 FBX axis/pre-rotation/reference pose를 합성해 해석하므로, 이 표시값만 `0`으로 바꾸려는 시도는 mesh와 animation basis를 어긋나게 만들 수 있었다.

## 의존성

### 필수

- .NET SDK
- RETool로 추출한 loose RE Engine 파일
- `texconv.exe` 또는 PNG 변환이 가능한 DirectXTex 설치
- sibling checkout 형태의 patched `REE-Content-Editor`
- Blender 4.5.x

기본 개발 레이아웃:

```text
parent-folder/
  REE-Content-Editor/
  REE-Content-Exporter/
```

patched dependency 재구성:

```powershell
.\scripts\setup-content-editor-dependency.ps1 -Force
```

빌드:

```powershell
dotnet build -c Release
```

Blender는 현재 스크립트에서 다음 경로로 호출한다.

```text
C:\Program Files\Blender Foundation\Blender 4.5\blender.exe
```

호출 방식은 headless/background mode이다.

```powershell
& "C:\Program Files\Blender Foundation\Blender 4.5\blender.exe" --background --python <generated-script.py>
```

## 현재 권장 샘플 스크립트

작은 테스트용 0110 animation-only 샘플:

```powershell
.\export_ch0100_attack_0110_unreal_textured_sample.ps1
```

기본 입력:

```text
D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000
```

포함 mesh:

```text
character\ch\ch01\ch0100\00\ch0100_00.mesh.251121828
character\ch\ch01\ch0100\10\ch0100_10.mesh.251121828
character\ch\ch01\ch0100\20\ch0100_20.mesh.251121828
character\ch\ch01\ch0100\40\ch0100_40_neo.mesh.251121828
```

사용 MOTLIST/animation:

```text
character\animation\ch\ch01\ch0100\motlist\ch0100_attack.motlist.1057
0110_Hacking_Loop
```

스크립트 출력:

- source FBX: `ch0100_attack_0110_hacking_loop_unreal_textured_source.fbx`
- Unreal-ready FBX: `ch0100_attack_0110_hacking_loop_blender45_unreal_maya_axis_cm_units_apply_rot_scale.fbx`
- textures folder: `textures\`
- skipped bone channel report: `*.skipped-animation-bones.md`

## REE-Content-Exporter 단계

스크립트가 실행하는 핵심 exporter 옵션은 다음과 같다.

```powershell
& $Exporter `
  --mesh "$Root\character\ch\ch01\ch0100\00\ch0100_00.mesh.251121828" `
  --streaming "$Root\streaming\character\ch\ch01\ch0100\00\ch0100_00.mesh.251121828" `
  --additional-mesh "$Root\character\ch\ch01\ch0100\10\ch0100_10.mesh.251121828" `
  --additional-mesh "$Root\character\ch\ch01\ch0100\20\ch0100_20.mesh.251121828" `
  --additional-mesh "$Root\character\ch\ch01\ch0100\40\ch0100_40_neo.mesh.251121828" `
  --motlist "$Root\character\animation\ch\ch01\ch0100\motlist\ch0100_attack.motlist.1057" `
  --animation-name "0110_Hacking_Loop" `
  --no-placeholder-animation-bones `
  --texture-format png `
  --fbx-scale 100 `
  --output $OutputRequest
```

중요 사항:

- `--no-placeholder-animation-bones`를 사용한다. 누락 bone channel은 placeholder bone을 만들지 않고 channel 단위로 skip한다.
- `--texture-format png`를 사용해 Unreal/Blender 확인이 쉬운 texture folder를 생성한다.
- `--fbx-scale 100`은 제거하지 않는다. 이것이 원본 FBX 단계에서 모델 크기를 centimeter 기준에 맞추는 데 필요하다.
- 이후 Blender 단계에서 다시 `global_scale=100`을 주면 과보정될 수 있으므로 사용하지 않는다.

## texture path fallback

texture 누락 문제는 RE Engine extract layout이 두 가지 형태로 섞이면서 반복됐다.

지원해야 하는 형태:

```text
<extract>\re_chunk_000\natives\STM\...
<extract>\re_chunk_000\...
```

현재 exporter는 streaming buffer와 loose texture lookup에서 old STM layout과 flat `re_chunk_000` layout을 모두 후보로 시도한다.

예:

```text
D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000\character\...
D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000\streaming\character\...
D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000\natives\STM\character\...
D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000\natives\STM\streaming\character\...
```

샘플 스크립트는 export 직후 `textures\` 폴더가 존재하고 파일 개수가 1개 이상인지 확인한다. 비어 있으면 실패로 처리한다.

## Blender 단계에서 하는 일

스크립트는 임시 Python 파일을 만들고 Blender를 background mode로 실행한다.

### 1. scene 초기화

- 기본 scene 삭제
- 이전 action/armature/mesh datablock 제거

### 2. centimeter unit 설정

```python
bpy.context.scene.unit_settings.system = 'METRIC'
bpy.context.scene.unit_settings.scale_length = 0.01
```

이 설정이 중요하다. Unreal은 centimeter 기반이고 Blender는 meter 기반이다. 여기서 단위 변환을 잡아야 Unreal import dialog에서 scale 100을 다시 넣지 않아도 된다.

### 3. source FBX import

```python
bpy.ops.import_scene.fbx(
    filepath=str(src),
    use_anim=True,
    automatic_bone_orientation=False,
    ignore_leaf_bones=False,
    force_connect_children=False,
)
```

### 4. Armature transform apply

```python
bpy.ops.object.transform_apply(
    location=False,
    rotation=True,
    scale=True,
    properties=True,
)
```

이 단계는 Blender object-level 회전/스케일을 적용해 export 기준을 안정화한다. 단, 이것은 Unreal skeleton root bone의 표시 Roll `90°`를 없애기 위한 처리가 아니다.

### 5. action을 NLA strip으로 명시화

각 action마다 하나의 NLA track/strip을 만든다.

이유:

- Blender의 `All Actions` compatibility scan에 의존하지 않는다.
- FBX animation stack/take가 명확하게 생성된다.
- 다수 animation export에서 일부 animation만 import되거나 static으로 들어가는 문제를 줄인다.

### 6. Blender FBX export 설정

현재 검증된 설정:

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

주의:

- 이전 문서/실험의 `-Y Forward / Z Up`, `global_scale=100` 조합은 현재 권장값이 아니다.
- 현재 Unreal에서 정상으로 확인된 조합은 `-Z Forward / Y Up`, centimeter scene unit, `global_scale=1.0`이다.
- `bake_space_transform=True`는 root Roll 표시 문제를 해결하지 못했고, 현재 사용하지 않는다.

## Unreal import 확인 항목

Unreal에서 새 test folder에 import한다. 기존 skeleton/animation asset 재사용 때문에 결과가 오염될 수 있으므로, variant 테스트 시에는 clean folder를 권장한다.

확인 항목:

1. Skeletal Mesh가 upright 상태인지
2. Animation이 upright 상태에서 재생되는지
3. Animation wobble이 없는지
4. Skeletal Mesh를 level/map에 배치했을 때 크기가 정상인지
5. Unreal import dialog scale override 없이 scale이 정상인지
6. Unreal asset/component scale이 `1.0`인지
7. texture folder가 같이 생성됐고 material 재연결에 필요한 PNG/manifest가 존재하는지
8. root motion을 사용할 경우 animation sequence의 root motion 설정을 켜고 capsule 이동 방향/거리도 별도로 확인할 것

## root Roll/X rotation 90°에 대한 현재 방침

현재 Unreal에서 Skeleton root bone의 Roll/X rotation이 `90°`로 보일 수 있다. 이것은 아직 남아 있는 표시값이지만, 현재는 의도적으로 정규화하지 않는다.

이유:

- 모델 upright, animation upright, no wobble, scale 1.0 상태가 이미 달성됐다.
- root roll 표시값만 0으로 만들기 위해 FBX transform layer를 건드리면 skeleton basis와 animation basis가 분리되어 animation이 누워서 재생되는 문제가 발생했다.
- FBX의 `PreRotation`으로 값을 옮기는 실험도 Unreal import 결과에서는 동일하게 `90°`로 합성되어 보였다.
- Unreal은 FBX의 axis conversion, pre-rotation, reference pose를 자체 방식으로 합성한다. 따라서 raw FBX property를 바꾼다고 Unreal Skeleton Editor의 표시값이 반드시 0이 되지 않는다.

따라서 현재 원칙:

- root Roll 90 정규화 코드는 제거한다.
- exporter/script에는 roll-0 normalization flag를 두지 않는다.
- root motion은 Unreal에서 실제 movement/capsule behavior로 검증한다.
- root Roll 값 자체보다 skeleton reference pose와 animation root track이 같은 basis를 유지하는지를 우선한다.

## 실패했던 접근과 교훈

### 1. exporter-level normalization flag

초기에는 root/object roll 값을 0으로 만들기 위해 normalization flag를 계획했다. 그러나 실제 문제는 Unreal의 animation wobble이었고, wobble은 normalization 때문이 아니라 direct Assimp FBX를 Unreal이 해석하는 방식에서 발생했다.

결론: normalization flag는 제거했다.

### 2. Blender `bake_space_transform=True`

root Roll 표시값을 바꾸지 못했다.

결론: 사용하지 않는다.

### 3. Armature scale-only/roll-0 후보

일부 FBX 내부값은 0에 가까워졌지만 Unreal에서 animation이 누워서 재생됐다.

결론: skeleton과 animation의 coordinate basis가 어긋날 수 있으므로 폐기했다.

### 4. FBX `PreRotation` patch

FBX 내부의 `Lcl Rotation` 값을 `PreRotation`으로 옮기는 방식은 파일 레벨에서는 의미가 있어 보였지만, Unreal은 이를 합성해서 다시 skeleton transform으로 표시했다.

결론: Unreal 표시 Roll 값을 0으로 만드는 해결책이 아니었다.

## 자체 검증 절차

코드 변경 후 최소 검증:

```powershell
dotnet build -c Release
.\export_ch0100_attack_0110_unreal_textured_sample.ps1
```

성공 조건:

- build: warning 0, error 0
- exporter exit code 0
- Blender exit code 0
- source FBX 생성
- Blender Unreal-ready FBX 생성
- `textures\` 폴더 생성
- texture file count > 0
- animation stack count가 테스트 목적과 일치
  - 0110 샘플: 1개
  - attack 전체: `ch0100_attack.motlist.1057` 내 선택된 전체 animation 개수

2026-06-06에 재검증한 0110 샘플 출력 예:

```text
C:\Users\hojin\Downloads\PRAG_PROJ\ree_exporter\ch0100_00__20260606_181559__b75ac6\ch0100_attack_0110_hacking_loop_blender45_unreal_maya_axis_cm_units_apply_rot_scale.fbx
```

해당 run에서 확인된 내용:

- source FBX 생성 성공
- Blender re-export 성공
- texture folder 생성 성공
- texture count: 59
- animation action/stack: 1개
- Blender export size: 약 51.6 MB

## all attack animations로 확장할 때

0110 샘플이 Unreal에서 통과하면 같은 방식으로 `--animation-name` 필터를 제거해 `ch0100_attack.motlist.1057` 전체 animation을 내보낸다.

주의:

- 많은 animation을 내보낼 때는 반드시 action별 NLA strip 생성 방식을 유지한다.
- `bake_anim_use_nla_strips=True`
- `bake_anim_use_all_actions=False`
- `bake_anim_simplify_factor=0.0`
- Unreal import 시 FBX takes/stacks 전체가 animation sequence로 생성되는지 확인한다.

## 문제 발생 시 빠른 판단 기준

- Blender에서도 animation이 틀림: exporter/source FBX 문제일 가능성이 높다.
- Blender에서는 맞고 Unreal에서만 wobble: Blender re-bake/re-export 단계 필요 또는 설정 오류.
- Unreal에서 모델이 누움: axis 설정이 잘못됐거나 skeleton/animation basis가 어긋난 것이다.
- Unreal에서 모델이 너무 작음: Blender scene unit/export scale 조합을 확인한다. 현재 권장값은 `scale_length=0.01`, `global_scale=1.0`이다.
- texture folder가 없음: source path layout fallback 또는 texture export 단계 문제다. 현재 샘플 스크립트는 texture folder가 없거나 비면 실패한다.
- root Roll 90만 남음: 현재는 known limitation으로 둔다. 실제 root motion behavior를 Unreal에서 검증한다.
