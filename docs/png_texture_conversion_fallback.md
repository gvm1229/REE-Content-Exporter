# PNG Texture Conversion Fallback

This note documents the PNG conversion failure found during wizard batch export of
`sm21_007_00.mesh`, why it happened, and how the exporter now handles mixed texture
formats safely.

## Problem

REE-Content-Exporter asks REE-Lib to write RE Engine `.tex` assets as temporary DDS
files. When the user requests `--texture-format png`, the exporter then asks
DirectXTex `texconv.exe` to convert those DDS files into PNG files.

The failing mesh was:

```text
sm21_007_00.mesh
```

The failing material texture was:

```text
sm28_010_A_HGAL_HeightMap.dds
```

`texconv` read that DDS as BC5 data and decoded it to a two-channel PNG candidate:

```text
BC5_UNORM -> R8G8_UNORM
```

That conversion failed because PNG output through `texconv` does not reliably accept
the two-channel `R8G8_UNORM` result. The failure looked like this:

```text
reading sm28_010_A_HGAL_HeightMap.dds (... BC5_UNORM 2D) as (... R8G8_UNORM 2D)
writing sm28_010_A_HGAL_HeightMap.png FAILED (80070032)
```

This did not mean every PNG texture should always be forced to RGBA. Other textures,
such as BC7 albedo maps, already convert correctly through the default `texconv`
path:

```text
BC7_UNORM_SRGB -> R8G8B8A8_UNORM_SRGB
```

Forcing every texture to `R8G8B8A8_UNORM` would be broader than necessary and would
discard the useful distinction between texconv's natural decoded output and a
compatibility fallback.

## Fix

`ConvertDdsToPng(...)` now uses a two-pass conversion strategy:

1. Run default `texconv` DDS-to-PNG conversion first.
2. If default PNG conversion fails, retry with:

```text
-f R8G8B8A8_UNORM -ft png
```

The fallback widens problematic formats such as BC5/two-channel maps into a PNG
compatible RGBA output. DDS export is not changed; this only affects PNG conversion.

When the fallback is used, the exporter prints:

```text
WARNING: texconv DDS->PNG default conversion failed; retrying with R8G8B8A8_UNORM PNG-compatible output.
```

## Verification

The focused wizard CSV used for verification contained two rows:

```text
sm20_007_00.mesh
sm21_007_00.mesh
```

The batch wizard was run with:

```text
2
2
<focused-csv-path>
<accept default export root>
y
```

Result:

```text
Resolved rows: 2
Exported rows: 2
Skipped rows: 0
Failed rows: 0
```

A direct `texconv` probe confirmed both conversion paths:

```text
REGULAR_DEFAULT_EXIT=0
BC5_DEFAULT_EXIT=1
BC5_RGBA_EXIT=0
```

The direct exporter PNG run for `sm21_007_00.mesh` also completed successfully:

```text
EXIT=0
PNG_COUNT=60
```

This proves the intended behavior:

- regular textures continue to use the default `texconv` PNG path;
- BC5/two-channel textures first try the default path;
- only failing PNG conversions retry as `R8G8B8A8_UNORM`;
- mixed-format mesh exports can finish without crashing the batch process.
