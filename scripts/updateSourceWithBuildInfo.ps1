function BuildProject ( $srcDir ) {
    write-host "Building Project"
    & dotnet build /p:SourceLinkCreate=true /p:GenerateDocumentationFile=true --no-incremental `
        --configuration "Release" $srcDir;
    CheckLastExitCode
}

function UpdateSourceWithBuildInfo ( $projectDir, $version ) {
    $commit = Get-Git-Commit-Short
    UpdateCommonAssemblyInfo $projectDir $version $commit
    UpdateCsprojAndNuspecWithVersionInfo $projectDir $version
}

function UpdateCsprojAndNuspecWithVersionInfo ( $projectDir, $version ) {
    # This is a workaround for the following issue:
    # dotnet pack - version suffix missing from ProjectReference: https://github.com/NuGet/Home/issues/4337

    Write-Host "Set version in Directory.build.props..."

    $src = $(Join-Path $projectDir -ChildPath "src");
    $akkaCsproj = [io.path]::Combine($src, "Akka.Persistence.RavenDB", "Akka.Persistence.RavenDb.csproj")
    $akkaHostingCsproj = [io.path]::Combine($src, "Akka.Persistence.RavenDB.Hosting", "Akka.Persistence.RavenDb.Hosting.csproj")

    # https://github.com/Microsoft/msbuild/issues/1721
    UpdateVersionInFile $akkaCsproj $version
    UpdateVersionInFile $akkaHostingCsproj $version

    UpdateDirectoryBuildProps $projectDir "src" $version
}

function UpdateDirectoryBuildProps( $projectDir, $subDir, $version ) {
    $subDirPath = $(Join-Path $projectDir -ChildPath $subDir);
    $buildProps = Join-Path -Path $subDirPath -ChildPath "Directory.Build.props"
    UpdateVersionInFile $buildProps $version
}

function UpdateVersionInFile ( $file, $version ) {
    $versionPattern = [regex]'(?sm)<Version>[A-Za-z0-9-\.\r\n\s]*</Version>'
    $inputText = [System.IO.File]::ReadAllText($file)
    $result = $versionPattern.Replace($inputText, "<Version>$version</Version>")
    [System.IO.File]::WriteAllText($file, $result, [System.Text.Encoding]::UTF8)
}

function UpdateVersionInNuspec ( $file, $version ) {
    $versionPattern = [regex]'(?sm)<version>[A-Za-z0-9-\.\r\n\s]*</version>'
    $result = [System.IO.File]::ReadAllText($file)
    $result = $versionPattern.Replace($result, "<version>$version</version>")
    [System.IO.File]::WriteAllText($file, $result, [System.Text.Encoding]::UTF8)
}

function UpdateCommonAssemblyInfo ( $projectDir, $version, $commit ) {
    $assemblyInfoFile = [io.path]::combine($projectDir, "src", "CommonAssemblyInfo.cs")
    Write-Host "Set version in $assemblyInfoFile..."

    $fileVersion = "$($version.Split("-")[0])"

    $result = [System.IO.File]::ReadAllText($assemblyInfoFile)

    $assemblyFileVersionPattern = [regex]'\[assembly: AssemblyFileVersion\(".*"\)\]';
    $result = $assemblyFileVersionPattern.Replace($result, "[assembly: AssemblyFileVersion(""$fileVersion"")]"); 

    $assemblyInfoVersionPattern = [regex]'\[assembly: AssemblyInformationalVersion\(".*"\)\]';
    $result = $assemblyInfoVersionPattern.Replace($result, "[assembly: AssemblyInformationalVersion(""$version"")]")

    [System.IO.File]::WriteAllText($assemblyInfoFile, $result, [System.Text.Encoding]::UTF8)
}


function Get-Git-Commit-Short
{
    $(Get-Git-Commit).Substring(0, 7)
}

function Get-Git-Commit
{
    if (Get-Command "git" -ErrorAction SilentlyContinue) {
        $sha1 = & git rev-parse HEAD
        CheckLastExitCode
        $sha1
    }
    else {
        return "0000000000000000000000000000000000000000"
    }
}
