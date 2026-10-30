param([string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$outputDirectory = Join-Path $projectRoot "dist\KugouStory-$Runtime"
$projectFile = Join-Path $projectRoot 'src\KugouStory\KugouStory.csproj'
dotnet publish $projectFile -c Release -r $Runtime --self-contained true -o $outputDirectory -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $outputDirectory 'README.md') -Force
$archivePath = Join-Path $projectRoot "dist\KugouStory-$Runtime.zip"
Compress-Archive -Path (Join-Path $outputDirectory '*') -DestinationPath $archivePath -Force
Write-Output "Executable: $(Join-Path $outputDirectory 'KugouStory.exe')"
Write-Output "Archive: $archivePath"
