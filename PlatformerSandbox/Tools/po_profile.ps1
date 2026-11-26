# Dot-source this script in the PowerShell session that launches the game.
param(
    [ValidateSet('Reference','Candidate')][string]$Profile = 'Candidate',
    [ValidateSet('On','Off')][string]$VSync = 'On',
    [ValidateRange(0,1000)][int]$RenderCap = 0,
    [ValidateRange(-1,127)][int]$LightingMask = -1,
    [ValidateSet('Reference','OptimizedCandidate','Custom')][string]$LightingProfile = 'Reference',
    [ValidateSet(0,1,3,5,7,9)][int]$AreaSamples = 0,
    [ValidateSet('On','Off')][string]$Overlay = 'On',
    [ValidateSet(25,50,75,100)][int]$LightScale = 100,
    [ValidateSet(25,50,75,100)][int]$MaskScale = 100,
    [ValidateSet('Reference','Candidate')][string]$Renderer = 'Reference',
    [ValidateSet(25,50,75,100)][int]$BloomScale = 100,
    [switch]$AdaptiveSamples,
    [switch]$StaticFields
)
$value = if ($Profile -eq 'Reference') { '0' } else { '1' }
foreach ($name in @('SNAPSHOTS','HUD_ATLAS','ARTWORK','DIAGNOSTICS','LIGHT_VALIDATION','LIGHT_BUFFERS','LIGHT_PARAMETERS','LIGHT_SAMPLES')) {
    [Environment]::SetEnvironmentVariable("PS_PO_$name", $value, 'Process')
}
$env:PS_PO_VSYNC = if ($VSync -eq 'On') { '1' } else { '0' }
$env:PS_PO_HUD = if ($Overlay -eq 'On') { '1' } else { '0' }
$env:PS_PO_RENDER_CAP = "$RenderCap"
foreach ($name in @('SHARED_SAMPLES','HASHED_VISIBILITY','VISIBLE_LAYERS','PREPARED_MATERIALS')) {
    [Environment]::SetEnvironmentVariable("PS_PO_B_$name", $value, 'Process')
}
$env:PS_PO_B_LIGHT_PROFILE = $LightingProfile
$env:PS_PO_B_AREA_SAMPLES = "$AreaSamples"
if ($LightingMask -ge 0) { $env:PS_PO_LIGHT_MASK = "$LightingMask" }
else { Remove-Item Env:PS_PO_LIGHT_MASK -ErrorAction SilentlyContinue }
$env:PS_PO_VARIABLE_LOOP = '1'
Write-Host "PO $Profile | VSync $VSync | cap $RenderCap | production profile $LightingProfile / mask $LightingMask | area samples $AreaSamples | overlay $Overlay"

$env:PS_PO_10_LIGHT_SCALE = "$LightScale"
$env:PS_PO_10_MASK_SCALE = "$MaskScale"
$po10value = if ($Renderer -eq 'Candidate') { '1' } else { '0' }
foreach ($name in @('MATERIAL_REGIONS','LIGHT_REGIONS','SHARED_FIELDS','SKIP_CLEARS','CACHE_CAPACITY')) {
    [Environment]::SetEnvironmentVariable("PS_PO_10_$name", $po10value, 'Process')
}
Write-Host "PO10A $Renderer | light scale $LightScale% | mask scale $MaskScale%"

$env:PS_PO_10B_BLOOM_SCALE = "$BloomScale"
$env:PS_PO_10B_ADAPTIVE = if ($AdaptiveSamples) { '1' } else { '0' }
$env:PS_PO_10B_STATIC_FIELDS = if ($StaticFields) { '1' } else { '0' }
Write-Host "PO10B bloom base $BloomScale% | adaptive samples $AdaptiveSamples | static fields $StaticFields"
