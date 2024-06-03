param(    
    [string]$BuildOptions = "",
    [string]$ArtifactNameSuffix = "",
    [switch]$Help)

if ([string]::IsNullOrEmpty($BuildOptions) -eq $False) {
  $env:RAVEN_BuildOptions = $BuildOptions
}

if ([string]::IsNullOrEmpty($ArtifactNameSuffix) -eq $False) {
  $env:RAVEN_ArtifactNameSuffix = $ArtifactNameSuffix
}

$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

. '.\scripts\checkLastExitCode.ps1'
. '.\scripts\checkPrerequisites.ps1'
. '.\scripts\clean.ps1'
. '.\scripts\getScriptDirectory.ps1'
. '.\scripts\version.ps1'
. '.\scripts\nuget.ps1'
. '.\scripts\updateSourceWithBuildInfo.ps1'

CheckPrerequisites

$PROJECT_DIR = Get-ScriptDirectory
$RELEASE_DIR = [io.path]::combine($PROJECT_DIR, "artifacts")

$CLIENT_SRC_DIR = [io.path]::combine($PROJECT_DIR, "src", "Akka.Persistence.RavenDB")

New-Item -Path $RELEASE_DIR -Type Directory -Force
CleanFiles $RELEASE_DIR

CleanSrcDirs $CLIENT_SRC_DIR

$versionObj = SetVersionInfo
$version = $versionObj.Version
Write-Host -ForegroundColor Green "Building $version"

UpdateSourceWithBuildInfo $PROJECT_DIR $version

BuildClient $CLIENT_SRC_DIR

CreateNugetPackage $CLIENT_SRC_DIR $RELEASE_DIR $version

write-host "Done creating packages."
