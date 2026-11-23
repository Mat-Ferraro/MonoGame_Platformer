"""Offline Phase 19 LDtk/source preflight; no MonoGame GPU runtime required.

Run from the PlatformerSandbox project root:
    py tools/l19_fixture_preflight.py
"""
from pathlib import Path
import json
import struct

ROOT = Path(__file__).resolve().parents[1]

def source(path: str) -> str:
    f = ROOT / path
    assert f.is_file(), f"Missing {path}"
    return f.read_text(encoding="utf-8")

fixture = json.loads(source("Content/Levels/LightingIntegrationTest.ldtk"))
level = next(x for x in fixture["levels"] if x["identifier"] == "LIGHTING_INTEGRATION_TEST")
entities = [e for layer in level["layerInstances"] for e in layer.get("entityInstances", [])]
lights = [e for e in entities if e["__identifier"] == "LightSource"]
assert len(lights) == 11, "Phase 18 multi-light fixture changed"
fields_by_id = {}
for entity in entities:
    fields = {f["__identifier"]: f["__value"] for f in entity.get("fieldInstances", [])}
    assert len(fields) == len(entity.get("fieldInstances", [])), "Duplicate authored fields"
    fields_by_id[entity["iid"]] = fields

mats = [(e, fields_by_id[e["iid"]]) for e in entities
        if e["__identifier"] == "TerrainArtwork" and
        fields_by_id[e["iid"]].get("MaterialShading") in ("Normal", "Hybrid")]
assert len(mats) == 2, "Phase 18 still needs two GPU material samples"
assert {f["MaterialShading"] for _, f in mats} == {"Normal", "Hybrid"}
assert sum(1 for e in entities if fields_by_id[e["iid"]].get("BlockerMotionAxis")
           in ("Horizontal", "Vertical") or
           fields_by_id[e["iid"]].get("BlockerRotationMode") in
           ("Clockwise", "Counterclockwise", "Swing")) >= 3
for entity, fields in mats:
    assert fields["EmissionIntensity"] >= 3.0
    color = fields["EmissionColor"]
    assert isinstance(color, str) and len(color) == 7 and color.startswith("#")
    name = fields["EmissionMap"]
    png = ROOT / "Content" / "Levels" / name
    assert png.is_file(), f"Missing emissive mask {png}"
    data = png.read_bytes()
    assert data[:8] == b"\x89PNG\r\n\x1a\n" and len(data) > 100
    width, height = struct.unpack(">II", data[16:24])
    assert 16 <= width <= 256 and 16 <= height <= 256
    print(f"{fields['MaterialShading']} sample: emission {color} x{fields['EmissionIntensity']}, {width}x{height} mask")

field_defs = next(x for x in fixture["defs"]["entities"] if x["identifier"] == "TerrainArtwork")["fieldDefs"]
assert {"EmissionColor", "EmissionIntensity", "EmissionMap"}.issubset(
    {f["identifier"] for f in field_defs})
assert len({f["uid"] for f in field_defs}) == len(field_defs)
shader = source("Content/Assets/Effects/L_BASIC_SurfaceLighting.fx")
renderer = source("Source/Lighting/GameplayLightingRenderer.cs")
ui = source("Source/Lighting/LightingToolsPanel.cs")
host = source("Source/Game1.cs")
for name in ["L19ToneMap", "L19BloomExtract", "L19BloomBlur", "L19BloomComposite",
             "L19SdrBloomComposite", "EmissionMap", "ProductionEmission", "L19SourceTexture"]:
    assert name in shader and name in renderer, f"Missing shader/renderer handshake for {name}"
assert "SurfaceFormat.HalfVector4" in renderer and "_l19HdrUnavailable" in renderer
assert "new L19PostProcessPolicy" in renderer
assert "LastPostProcessCpuMilliseconds" in renderer
for name in ["ToggleHdr", "ToggleEmission", "ToggleBloom", "CycleExposure",
             "CycleBloomThreshold", "CycleBloomStrength"]:
    assert f"LightingToolAction.{name}" in ui and f"case LightingToolAction.{name}:" in host
assert "L19 HDR " in host and "L19 BLOOM" not in host  # HUD has unified L19 line
assert "Action<SpriteBatch>?, Action<SpriteBatch>?, Action<SpriteBatch>?>" in source("Source/Gyms/LdtkLevelRenderer.cs")
assert "drawMaterial(submission, region, albedo, normals, edges, emission);" in source("Source/Gyms/LdtkVisualSceneAssets.cs")
assert "ProductionSurfaceShading.Legacy" in source("PlatformerSandbox.Foundation/Presentation/Lighting/ProductionSurfaceSubmission.cs")
print("Phase 19 HDR/emission/bloom fixture/source preflight PASSED")
print("NOTE: Not a .NET build, MGFX shader compilation, or DesktopGL/GPU visual test.")
