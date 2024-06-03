function CreateNugetPackage ( $srcDir, $targetFilename, $versionSuffix ) {
    dotnet pack /p:GenerateDocumentationFile=true --output $targetFilename `
        --configuration "Release" `
        --version-suffix $versionSuffix `
        $srcDir

    CheckLastExitCode
}
