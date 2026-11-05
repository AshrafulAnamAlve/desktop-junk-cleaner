# Builds the portable SafeClean release: a folder, a ZIP and a SHA-256 checksum file.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\publish-release.ps1
#
# Safety:
# - It deletes nothing. If the output already exists it stops, so an old release can't be overwritten by accident.
# - IncludeNativeLibrariesForSelfExtract stays false. If it were true, .NET would unpack DLLs into %TEMP%\.net on
#   every run, and a Temp cleaner must not put its own files in the folder it cleans.
# - Each build is checked before it is zipped (see "Checks" below). Any failed check stops the script.
param([string]$OutputRoot)
$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
if (-not $OutputRoot) { $OutputRoot = Join-Path $repo 'publish' }
$project = Join-Path $repo 'src\SafeClean.App\SafeClean.App.csproj'

$version = ([xml](Get-Content -Raw -LiteralPath (Join-Path $repo 'Directory.Build.props'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw 'Could not read the version from Directory.Build.props.' }
$name = "SafeClean-$version-win-x64"
$folder = Join-Path $OutputRoot $name
$zip = "$folder.zip"
$checksum = "$zip.sha256"
foreach ($path in $folder, $zip, $checksum) {
  if (Test-Path -LiteralPath $path) { throw "$path already exists. Nothing was changed. Move it elsewhere, or raise the version." }
}

Push-Location $repo
try {
  $changes = git status --porcelain
  if ($changes) { "WARNING: there are uncommitted changes, so this build can't be traced to a single commit." }
  $commit = git rev-parse --short HEAD
  $sdk = dotnet --version
  "Publishing SafeClean $version from commit $commit with SDK $sdk"

  # Release builds restore different packages from everyday builds, so they keep their own lock file (exact package
  # hashes). Once those files are committed, locked mode makes the build fail if any package differs.
  $lockFile = 'packages.win-x64.lock.json'
  $projects = 'SafeClean.App', 'SafeClean.Core', 'SafeClean.Infrastructure'
  $locked = @($projects | Where-Object { Test-Path -LiteralPath (Join-Path $repo "src\$_\$lockFile") }).Count -eq $projects.Count
  if (-not $locked) { "NOTE: creating $lockFile files. Commit them so later release builds are held to the same packages." }

  & dotnet publish $project -c Release -r win-x64 --self-contained true -o $folder `
    -p:NuGetLockFilePath=$lockFile `
    -p:RestoreLockedMode=$($locked.ToString().ToLowerInvariant()) `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=false `
    -p:PublishTrimmed=false `
    -p:DebugType=none -p:DebugSymbols=false
  if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

  # Checks ------------------------------------------------------------------------------------------------------------
  $exe = Join-Path $folder 'SafeClean.exe'
  if (-not (Test-Path -LiteralPath $exe)) { throw 'SafeClean.exe was not produced.' }

  $latin1 = [Text.Encoding]::GetEncoding(28591)
  $bytes = $latin1.GetString([IO.File]::ReadAllBytes($exe))
  function Test-Contains([string]$text) {
    $bytes.Contains($text) -or $bytes.Contains($latin1.GetString([Text.Encoding]::Unicode.GetBytes($text)))
  }

  # The same search must find ordinary SafeClean code, otherwise "no sandbox code found" below would mean nothing.
  if (-not (Test-Contains 'CleanupService')) { throw 'Check failed: the search cannot see inside the program.' }
  "Check passed: the search can see inside the program."
  foreach ($marker in 'sandbox-root', 'DebugSandboxRootResolver', '.safeclean-sandbox') {
    if (Test-Contains $marker) { throw "Check failed: Debug-only sandbox code ('$marker') is in the release." }
  }
  "Check passed: no sandbox code."
  if (-not $bytes.Contains('level="asInvoker"')) { throw 'Check failed: the program does not say it runs with normal rights.' }
  if ($bytes.Contains('level="requireAdministrator"') -or $bytes.Contains('level="highestAvailable"')) { throw 'Check failed: the program asks for administrator rights.' }
  "Check passed: runs with normal rights (asInvoker)."

  $found = [regex]::Match($bytes, 'Microsoft\.WindowsDesktop\.App\.Runtime\.win-x64/(\d+\.\d+\.\d+)')
  if (-not $found.Success) { throw 'Check failed: could not find which .NET runtime was bundled.' }
  $runtime = $found.Groups[1].Value
  $newest = dotnet --list-runtimes | Where-Object { $_ -match '^Microsoft\.WindowsDesktop\.App 10\.' } |
    ForEach-Object { [version](($_ -split ' ')[1]) } | Sort-Object | Select-Object -Last 1
  if ([version]$runtime -lt $newest) { throw "Check failed: bundled runtime $runtime is older than the installed $newest." }
  "Check passed: bundles .NET runtime $runtime."

  $loose = Get-ChildItem -LiteralPath $folder -File | Where-Object { $_.Extension -eq '.pdb' -or $_.Name -like 'SafeClean.*.dll' -or $_.Name -like '*.deps.json' }
  if ($loose) { throw "Check failed: unexpected files: $($loose.Name -join ', ')" }
  "Check passed: only the program and the native libraries it needs."

  # Licence notices: .NET's MIT licence asks for its notice to travel with copies. ------------------------------------
  $packages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
  $notices = @(
    @{ Pack = 'microsoft.netcore.app.runtime.win-x64'; File = 'LICENSE.TXT'; CopyAs = 'LICENSE-dotnet-runtime.txt' },
    @{ Pack = 'microsoft.netcore.app.runtime.win-x64'; File = 'THIRD-PARTY-NOTICES.TXT'; CopyAs = 'THIRD-PARTY-NOTICES-dotnet-runtime.txt' },
    @{ Pack = 'microsoft.windowsdesktop.app.runtime.win-x64'; File = 'LICENSE'; CopyAs = 'LICENSE-windows-desktop.txt' })
  foreach ($notice in $notices) {
    $source = Join-Path $packages "$($notice.Pack)\$runtime\$($notice.File)"
    if (-not (Test-Path -LiteralPath $source)) { throw "Licence file not found: $source" }
    Copy-Item -LiteralPath $source -Destination (Join-Path $folder $notice.CopyAs)
  }
  Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README-portable.txt') -Destination (Join-Path $folder 'README.txt')

  # ZIP and checksum --------------------------------------------------------------------------------------------------
  Add-Type -AssemblyName System.IO.Compression.FileSystem
  [IO.Compression.ZipFile]::CreateFromDirectory($folder, $zip, [IO.Compression.CompressionLevel]::Optimal, $true)
  $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
  [IO.File]::WriteAllText($checksum, "$hash *$name.zip`n", [Text.Encoding]::ASCII)

  ''
  'Files in the release:'
  Get-ChildItem -LiteralPath $folder -File | ForEach-Object { "  {0,-45} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB) }
  ''
  "Folder size: {0:N0} MB" -f ((Get-ChildItem -LiteralPath $folder -File | Measure-Object Length -Sum).Sum / 1MB)
  "ZIP: $zip ({0:N0} MB)" -f ((Get-Item -LiteralPath $zip).Length / 1MB)
  "SHA-256: $hash"
}
finally { Pop-Location }
