# Register Channeled API as a Windows service (run elevated).
param(
    [string]$InstallDir = "C:\channeled",
    [string]$ServiceName = "ChanneledApi"
)

$exe = Join-Path $InstallDir "Channeled.Api.exe"
if (-not (Test-Path $exe)) {
    Write-Error "Publish the API to $InstallDir first (dotnet publish -c Release -o $InstallDir)."
}

New-Service -Name $ServiceName -BinaryPathName $exe -DisplayName "Channeled API" -StartupType Automatic
Write-Host "Service $ServiceName created. Configure env via $InstallDir\channeled.env or system environment."
