# BFS layered link graph from README.md, then random-sample pages per layer.
param(
    [string]$RootReadme = "D:\Repos\THBWiki-Markdown\README.md",
    [string]$SourcesDir = "D:\Repos\THBWiki-Markdown\sources",
    [int]$PerLayer = 4,
    [int]$MaxLayers = 6,
    [int]$Seed = 42,
    [string]$OutJson = "D:\Repos\THBWiki-Markdown-Builder\tools\sampled-pages.json",
    [string]$OutTxt = "D:\Repos\THBWiki-Markdown-Builder\tools\sampled-pages.txt"
)

$ErrorActionPreference = "Stop"
$rng = [System.Random]::new($Seed)
$linkRe = [regex]'\[([^\]]*)\]\(([^)]+)\)'

function Resolve-LocalMdPath([string]$fromFile, [string]$href) {
    if ([string]::IsNullOrWhiteSpace($href)) { return $null }
    if ($href -match '^(https?:|mailto:|//|#)') { return $null }
    $pathPart = ($href -split '#', 2)[0]
    if ([string]::IsNullOrWhiteSpace($pathPart)) { return $null }
    # Skip query/protocol-ish or illegal Windows path chars.
    if ($pathPart -match '[?%*|"<>:]') { return $null }
    $pathPart = $pathPart -replace '/', '\'
    try {
        if (-not [System.IO.Path]::IsPathRooted($pathPart)) {
            $baseDir = Split-Path -Parent $fromFile
            $candidate = [System.IO.Path]::GetFullPath((Join-Path $baseDir $pathPart))
        } else {
            $candidate = [System.IO.Path]::GetFullPath($pathPart)
        }
    } catch {
        return $null
    }
    if (-not $candidate.EndsWith('.md', [StringComparison]::OrdinalIgnoreCase)) { return $null }
    if (-not (Test-Path -LiteralPath $candidate)) { return $null }
    return $candidate
}

$rootFull = [System.IO.Path]::GetFullPath($RootReadme)
$queue = [System.Collections.Generic.Queue[object]]::new()
$depth = @{}
$parents = @{}
$queue.Enqueue($rootFull)
$depth[$rootFull] = 0

while ($queue.Count -gt 0) {
    $current = $queue.Dequeue()
    $d = $depth[$current]
    if ($d -ge $MaxLayers) { continue }
    if (-not (Test-Path -LiteralPath $current)) { continue }

    $text = Get-Content -LiteralPath $current -Raw -Encoding UTF8
    foreach ($m in $linkRe.Matches($text)) {
        $href = $m.Groups[2].Value.Trim()
        $target = Resolve-LocalMdPath $current $href
        if ($null -eq $target) { continue }
        if ($depth.ContainsKey($target)) { continue }
        $depth[$target] = $d + 1
        $parents[$target] = $current
        $queue.Enqueue($target)
    }
}

# Group by layer (exclude root layer 0). Prefer content pages over file/media stubs.
$byLayer = @{}
foreach ($kv in $depth.GetEnumerator()) {
    $layer = [int]$kv.Value
    if ($layer -eq 0) { continue }
    $name = [System.IO.Path]::GetFileNameWithoutExtension($kv.Key)
    # Skip media/file stub pages (builder emits "文件-*.ext.md").
    if ($name -match '^(File:|文件-)' -or $name -match '\.(png|jpe?g|gif|webp|svg|ogg|mp3|mp4|webm)$') { continue }
    if (-not $byLayer.ContainsKey($layer)) { $byLayer[$layer] = [System.Collections.Generic.List[string]]::new() }
    $byLayer[$layer].Add($kv.Key)
}

$samples = [System.Collections.Generic.List[object]]::new()
$layerStats = [ordered]@{}
foreach ($layer in ($byLayer.Keys | Sort-Object)) {
    $list = $byLayer[$layer]
    $layerStats["L$layer"] = $list.Count
    $take = [Math]::Min($PerLayer, $list.Count)
    # Fisher-Yates partial shuffle
    for ($i = 0; $i -lt $take; $i++) {
        $j = $rng.Next($i, $list.Count)
        $tmp = $list[$i]; $list[$i] = $list[$j]; $list[$j] = $tmp
        $path = $list[$i]
        $rel = $path
        if ($path.StartsWith($SourcesDir, [StringComparison]::OrdinalIgnoreCase)) {
            $rel = "sources/" + [System.IO.Path]::GetFileName($path)
        }
        $samples.Add([pscustomobject]@{
            layer = $layer
            path = $path
            relative = $rel
            title = [System.IO.Path]::GetFileNameWithoutExtension($path)
        })
    }
}

$result = [ordered]@{
    root = $rootFull
    totalReachable = $depth.Count - 1
    maxLayers = $MaxLayers
    perLayer = $PerLayer
    seed = $Seed
    layerCounts = $layerStats
    samples = $samples
}

$dir = Split-Path -Parent $OutJson
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
$result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutJson -Encoding UTF8

$lines = @()
$lines += "Reachable (excl root): $($result.totalReachable)"
$lines += "Layer counts: $(($layerStats.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ', ')"
$lines += "Samples:"
foreach ($s in $samples) {
    $lines += "L$($s.layer)`t$($s.title)`t$($s.path)"
}
$lines | Set-Content -LiteralPath $OutTxt -Encoding UTF8
$lines | ForEach-Object { $_ }
Write-Host "Wrote $OutJson and $OutTxt"
