param(    
  $Target="",
  [switch]$DryRunSign = $false)

$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

. '.\scripts\checkLastExitCode.ps1'
. '.\scripts\checkPrerequisites.ps1'
. '.\scripts\clean.ps1'
. '.\scripts\getScriptDirectory.ps1'
. '.\scripts\version.ps1'
. '.\scripts\nuget.ps1'
. '.\scripts\updateSourceWithBuildInfo.ps1'
. '.\scripts\sign.ps1'

CheckPrerequisites

$PROJECT_DIR = Get-ScriptDirectory
$RELEASE_DIR = [io.path]::combine($PROJECT_DIR, "artifacts")

$AKKA_SRC_DIR = [io.path]::combine($PROJECT_DIR, "src", "Akka.Persistence.RavenDB")
$AKKA_OUT_DIR = [io.path]::combine($PROJECT_DIR, "src", "Akka.Persistence.RavenDB", "bin", "Release")
$AKKA_DLL_PATH = [io.path]::combine($AKKA_OUT_DIR, "netstandard2.0", "Akka.Persistence.RavenDB.dll")

$AKKA_HOSTING_SRC_DIR = [io.path]::combine($PROJECT_DIR, "src", "Akka.Persistence.RavenDB.Hosting")
$AKKA_HOSTING_OUT_DIR = [io.path]::combine($PROJECT_DIR, "src", "Akka.Persistence.RavenDB.Hosting", "bin", "Release")
$AKKA_HOSTING_DLL_PATH = [io.path]::combine($AKKA_HOSTING_OUT_DIR, "netstandard2.0", "Akka.Persistence.RavenDB.Hosting.dll")

New-Item -Path $RELEASE_DIR -Type Directory -Force
CleanFiles $RELEASE_DIR

CleanSrcDirs $AKKA_SRC_DIR
CleanSrcDirs $AKKA_HOSTING_SRC_DIR

$versionObj = SetVersionInfo
$version = $versionObj.Version
Write-Host -ForegroundColor Green "Building $version"

UpdateSourceWithBuildInfo $PROJECT_DIR $version

BuildProject $AKKA_SRC_DIR
BuildProject $AKKA_HOSTING_SRC_DIR

SignFile $PROJECT_DIR $AKKA_DLL_PATH $DryRunSign
SignFile $PROJECT_DIR $AKKA_HOSTING_DLL_PATH $DryRunSign

CreateNugetPackage $AKKA_SRC_DIR $RELEASE_DIR $version
CreateNugetPackage $AKKA_HOSTING_SRC_DIR $RELEASE_DIR $version

write-host "Done creating packages."
