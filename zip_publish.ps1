Param(
	[string]$Configuration = "Release"
)

$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'gamebuddybrain.csproj'
if (-not (Test-Path $project)) { throw "Project file not found: $project" }

$desktop = [Environment]::GetFolderPath('Desktop')
$outRoot = Join-Path $desktop 'New_v2'
$publishDir = Join-Path $outRoot 'publish'
$zipPublish = Join-Path $outRoot 'GameBuddyBrain_publish.zip'
$zipProject = Join-Path $outRoot 'GameBuddyBrain_project.zip'

New-Item -ItemType Directory -Force -Path $publishDir | Out-Null
# Clean publish directory to avoid stale files
Get-ChildItem -Path $publishDir -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

# Try to close any running instance to avoid file lock during publish
try {
	$running = Get-Process -Name 'GameBuddyBrain' -ErrorAction SilentlyContinue
	if ($running) {
		Write-Host "Closing running GameBuddyBrain.exe instances..."
		foreach ($p in $running) {
			try { $null = $p.CloseMainWindow() } catch {}
		}
		Start-Sleep -Milliseconds 900
		# Force kill if still running
		Get-Process -Name 'GameBuddyBrain' -ErrorAction SilentlyContinue |
			Stop-Process -Force -ErrorAction SilentlyContinue
		Start-Sleep -Milliseconds 300
	}
} catch {
	Write-Warning "Could not check/stop running instances: $_"
}

Write-Host "Cleaning previous build..."
dotnet clean $project -c $Configuration | Write-Host

Write-Host "Publishing self-contained single-file to $publishDir ..."
# Ensure no PDBs or debug info in Release publish
$pubArgs = @(
	$project,
	'-c', $Configuration,
	'-r', 'win-x64',
	'--self-contained', 'true',
	'/p:PublishSingleFile=true',
	'/p:PublishTrimmed=false',
	'/p:DebugSymbols=false',
	'/p:DebugType=None',
	'/p:GenerateDocumentationFile=false',
	"/p:PublishDir=$publishDir"
)
dotnet publish @pubArgs | Write-Host

if (-not (Test-Path $publishDir)) { throw "Publish dir missing: $publishDir" }

# Remove any .pdb left by tooling just in case
Get-ChildItem -Path $publishDir -Filter *.pdb -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue

# Create a desktop shortcut to the published EXE
$exePath = Join-Path $publishDir 'GameBuddyBrain.exe'
if (Test-Path $exePath) {
	try {
		$shell = New-Object -ComObject WScript.Shell
		$shortcut = $shell.CreateShortcut((Join-Path $desktop 'GameBuddyBrain.lnk'))
		$shortcut.TargetPath = $exePath
		$shortcut.WorkingDirectory = $publishDir
		$shortcut.IconLocation = $exePath
		$shortcut.Save()
		Write-Host "Shortcut created on Desktop."
	} catch {
		Write-Warning "Failed to create desktop shortcut: $_"
	}
}

Write-Host "Zipping publish output to $zipPublish ..."
if (Test-Path $zipPublish) { Remove-Item $zipPublish -Force }
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPublish -CompressionLevel Optimal

Write-Host "Zipping entire project to $zipProject (excluding bin/obj/.git) ..."
if (Test-Path $zipProject) { Remove-Item $zipProject -Force }
${stage} = Join-Path $env:TEMP "GBB_stage_$(Get-Date -Format yyyyMMddHHmmss)"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null
# Copy project excluding bin/obj/.git directories
robocopy "$PSScriptRoot" "$stage" /E /XD ".git" "bin" "obj" ".vs" | Out-Null
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zipProject -CompressionLevel Optimal
Remove-Item $stage -Recurse -Force

Write-Host "Done. Open: $outRoot"
