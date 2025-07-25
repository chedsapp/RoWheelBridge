param(
    [string]$Runtime = "win-x64"
)

Write-Host "Building RoWheelBridge for $Runtime..." -ForegroundColor Green

if (Test-Path "publish") {
    Remove-Item -Recurse -Force "publish"
}

dotnet publish -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true -o "./publish/$Runtime"

if ($LASTEXITCODE -eq 0) {
    Write-Host "Build successful! Output in ./publish/$Runtime/" -ForegroundColor Green
    
    # Show the built files
    Get-ChildItem "./publish/$Runtime/" | Format-Table Name, Length, LastWriteTime
    
    Write-Host "`nTo build for Windows ARM64, run:" -ForegroundColor Yellow
    Write-Host "  .\build-release.ps1 -Runtime win-arm64" -ForegroundColor Cyan
} else {
    Write-Host "Build failed!" -ForegroundColor Red
}