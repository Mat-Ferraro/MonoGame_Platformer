# Dot-source this script in the PowerShell session that launches the game.
param(
    [ValidateSet('Reference','Candidate')][string]$Profile = 'Candidate',
    [ValidateSet('On','Off')][string]$VSync = 'On',
    [ValidateRange(0,1000)][int]$RenderCap = 0,
    [ValidateRange(0,127)][int]$LightingMask = 0,
    [ValidateSet('On','Off')][string]$Overlay = 'On'
)
$value = if ($Profile -eq 'Reference') { '0' } else { '1' }
foreach ($name in @('SNAPSHOTS','HUD_ATLAS','ARTWORK','DIAGNOSTICS','LIGHT_VALIDATION','LIGHT_BUFFERS','LIGHT_PARAMETERS','LIGHT_SAMPLES')) {
    [Environment]::SetEnvironmentVariable("PS_PO_$name", $value, 'Process')
}
$env:PS_PO_VSYNC = if ($VSync -eq 'On') { '1' } else { '0' }
$env:PS_PO_HUD = if ($Overlay -eq 'On') { '1' } else { '0' }
$env:PS_PO_RENDER_CAP = "$RenderCap"
$env:PS_PO_LIGHT_MASK = "$LightingMask"
$env:PS_PO_VARIABLE_LOOP = '1'
Write-Host "PO $Profile | VSync $VSync | cap $RenderCap | production mask $LightingMask | overlay $Overlay"
