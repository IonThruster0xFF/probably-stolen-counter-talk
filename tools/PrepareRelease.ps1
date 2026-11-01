param([string]$RepositoryUrl)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$version = ([xml](Get-Content -LiteralPath "$root\src\CounterTalk\manifest.xml" -Raw)).Manifest.ModVersion
$folder = Join-Path $root "dist\publish-$version\CounterTalk"
New-Item -ItemType Directory -Force -Path $folder | Out-Null
Copy-Item -LiteralPath "$root\dist\CounterTalk\CounterTalk.dll", "$root\dist\CounterTalk\manifest.xml", "$root\assets\preview.png" -Destination $folder
Copy-Item -LiteralPath "$root\README.md" -Destination "$folder\README.txt"
if (Test-Path -LiteralPath "$root\LICENSE") { Copy-Item -LiteralPath "$root\LICENSE" -Destination "$folder\LICENSE.txt" }
if (Test-Path -LiteralPath "$root\assets\preview-sources.txt") {
    $credits = "`n`nCover artwork attribution (not covered by the MIT code license):`n" + [IO.File]::ReadAllText("$root\assets\preview-sources.txt")
    [IO.File]::AppendAllText("$folder\LICENSE.txt", $credits, (New-Object Text.UTF8Encoding $false))
}
$allowed = @('CounterTalk.dll','manifest.xml','preview.png','README.txt','LICENSE.txt')
foreach ($file in Get-ChildItem -LiteralPath $folder -File -Recurse) {
    if ($file.DirectoryName -ne $folder -or $file.Name -notin $allowed) { throw "Unexpected publication file: $($file.Name)" }
    if ($file.Extension -ne '.png') {
        $bytes = [IO.File]::ReadAllBytes($file.FullName)
        foreach ($encoding in @([Text.Encoding]::UTF8, [Text.Encoding]::Unicode)) {
            if ($encoding.GetString($bytes) -match '(sk-[A-Za-z0-9_-]{16,}|gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,})') { throw 'Credential-like content found; publication preparation stopped.' }
        }
    }
}
$zip = Join-Path $root "dist\CounterTalk-$version-public.zip"
# Package contains the CounterTalk folder; extract it into the game's Mods directory.
Compress-Archive -LiteralPath $folder -DestinationPath $zip -Force
Get-FileHash -LiteralPath "$folder\CounterTalk.dll", $zip | Select-Object Path,Hash
if (!$RepositoryUrl) { Write-Output 'Package prepared. Workshop VDF awaits the real GitHub repository URL.'; exit 0 }
if ($RepositoryUrl -notmatch '^https://github\.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/?$') { throw 'Use the real GitHub repository URL.' }
$description = [IO.File]::ReadAllText("$root\release\workshop-description.zh-CN.txt").Replace('__GITHUB_URL__', $RepositoryUrl.TrimEnd('/'))
# SteamCMD preserves literal \\n text; use real newlines inside quoted descriptions.
function Escape-Vdf([string]$value) { return $value.Replace('\','\\').Replace('"','\"').Replace("`r",'') }
$vdf = [IO.File]::ReadAllText("$root\release\workshop-item.vdf.example").Replace('__CONTENT_FOLDER__',(Escape-Vdf $folder)).Replace('__PREVIEW_FILE__',(Escape-Vdf "$folder\preview.png")).Replace('__DESCRIPTION__',(Escape-Vdf $description))
$local = Join-Path $root 'publish-local'
New-Item -ItemType Directory -Force -Path $local | Out-Null
$vdfPath = Join-Path $local 'counter-talk.vdf'
if (Test-Path -LiteralPath $vdfPath) { throw 'Existing VDF retained: preserve its publishedfileid before updating.' }
[IO.File]::WriteAllText($vdfPath,$vdf,(New-Object Text.UTF8Encoding $false))
[IO.File]::WriteAllText((Join-Path $local 'workshop-description-final.txt'),$description,(New-Object Text.UTF8Encoding $true))
Write-Output "Prepared hidden Workshop draft: $vdfPath. No upload performed. Review the final description before uploading."
