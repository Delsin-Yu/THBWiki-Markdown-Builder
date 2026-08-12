# Heuristic static checks for common malformed Markdown patterns in generated wiki pages.
param(
    [Parameter(Mandatory = $true)][string[]]$Paths,
    [string]$OutJson = "D:\Repos\THBWiki-Markdown-Builder\tools\malformed-report.json"
)

$ErrorActionPreference = "Stop"
$issues = [System.Collections.Generic.List[object]]::new()

function Add-Issue($path, $line, $kind, $snippet) {
    $script:issues.Add([pscustomobject]@{
        path = $path
        line = $line
        kind = $kind
        snippet = ($snippet -replace '\s+', ' ').Trim()
    })
}

foreach ($path in $Paths) {
    if (-not (Test-Path -LiteralPath $path)) {
        Add-Issue $path 0 "missing-file" ""
        continue
    }
    $lines = Get-Content -LiteralPath $path -Encoding UTF8
    $inFence = $false
    $fenceStart = 0
    $boldOpen = $false
    $italicOpen = $false
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        $ln = $i + 1

        if ($line -match '^\s*```') {
            $inFence = -not $inFence
            if ($inFence) { $fenceStart = $ln }
            continue
        }
        if ($inFence) { continue }

        if ($line -match '<unsupported html=([^>]+)>') {
            Add-Issue $path $ln "unsupported-html" $line
        }

        # Unescaped raw broken bold split: lone ** at EOL then next content
        if ($line -match '\*\*\s*$' -and $i + 1 -lt $lines.Count -and $lines[$i + 1] -match '^\S' -and $lines[$i + 1] -notmatch '^\*\*') {
            # may be intentional hard-break bold; flag for review
            Add-Issue $path $ln "bold-maybe-split-across-lines" $line
        }

        # Empty emphasis
        if ($line -match '\*\*\*\*' -or $line -match '(?<!\*)\*\*(?!\*)\s*\*\*(?!\*)') {
            Add-Issue $path $ln "empty-bold" $line
        }

        # Broken markdown link: ]( without matching [
        if ($line -match '\]\([^)]*$' -and $line -notmatch '\[[^\]]*\]\([^)]*$') {
            Add-Issue $path $ln "unclosed-link-dest" $line
        }

        # Link with spaces in destination not encoded (common renderer break)
        if ($line -match '\[[^\]]*\]\(([^)]*\s[^)]*)\)') {
            $dest = $Matches[1]
            if ($dest -notmatch '^https?://' -and $dest -match '\s') {
                Add-Issue $path $ln "link-dest-has-space" $line
            }
        }

        # Heading with only whitespace / empty after hashes
        if ($line -match '^\s{0,3}#{1,6}\s*$') {
            Add-Issue $path $ln "empty-heading" $line
        }

        # Heading that still contains raw <br> or nested ##
        if ($line -match '^\s{0,3}#{1,6}\s+.*<br') {
            Add-Issue $path $ln "heading-contains-br" $line
        }

        # Table row with odd pipe balance / empty header separator issues
        if ($line -match '^\|' -and $line -match '\|\s*$') {
            # consecutive empty cells with ||| often ok; look for unmatched **
            $stars = ([regex]::Matches($line, '\*\*')).Count
            if ($stars % 2 -ne 0) {
                Add-Issue $path $ln "table-unbalanced-bold" $line
            }
        }

        # Unmatched ** on a single non-table line (rough)
        if ($line -notmatch '^\|' -and $line -notmatch '^\s*<!--') {
            $plain = $line -replace '`[^`]*`', ''
            $stars = ([regex]::Matches($plain, '\*\*')).Count
            if ($stars % 2 -ne 0) {
                Add-Issue $path $ln "unbalanced-bold" $line
            }
            # single * italic rough: ignore list markers
            $tmp = $plain -replace '^\s*[-*+]\s+', '' -replace '\*\*', ''
            $single = ([regex]::Matches($tmp, '(?<!\*)\*(?!\*)')).Count
            if ($single % 2 -ne 0) {
                Add-Issue $path $ln "unbalanced-italic" $line
            }
        }

        # Nested list glued to previous text: "text- item" without newline (already line-based; look for ":- " weird)
        if ($line -match '[^\s|-]-[ ]{1}\S' -and $line -notmatch 'https?://' -and $line -notmatch '\]\(') {
            # too noisy; skip
        }

        # Raw parser debris
        if ($line -match 'mw-parser-output|navigation-not-searchable') {
            Add-Issue $path $ln "raw-wiki-class-leak" $line
        }
    }
    if ($inFence) {
        Add-Issue $path $fenceStart "unclosed-code-fence" ""
    }
}

$grouped = $issues | Group-Object kind | Sort-Object Count -Descending | ForEach-Object {
    [pscustomobject]@{ kind = $_.Name; count = $_.Count }
}

$result = [ordered]@{
    checkedFiles = $Paths.Count
    issueCount = $issues.Count
    byKind = $grouped
    issues = $issues
}
$result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutJson -Encoding UTF8
Write-Host "Checked $($Paths.Count) files; $($issues.Count) issues -> $OutJson"
$grouped | Format-Table -AutoSize
$issues | Select-Object -First 40 | Format-Table -AutoSize
