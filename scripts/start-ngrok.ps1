<#
.SYNOPSIS
    Inicia ngrok para exponer la app localmente y actualiza appsettings.Development.json
    con la URL pública para que MercadoPago pueda enviar webhooks.

.DESCRIPTION
    Uso:
        .\scripts\start-ngrok.ps1              # apunta al perfil Kestrel HTTP (puerto 5268)
        .\scripts\start-ngrok.ps1 -Port 26320  # apunta a IIS Express HTTP

    Requisitos:
        - ngrok instalado y en el PATH  (https://ngrok.com/download)
        - Cuenta ngrok con authtoken configurado:  ngrok config add-authtoken <TOKEN>

    Qué hace:
        1. Inicia ngrok apuntando al puerto local.
        2. Espera hasta obtener la URL pública HTTPS del túnel.
        3. Escribe esa URL en MercadoPago:UrlBase de appsettings.Development.json.
        4. La app la usa automáticamente (sin tocar la BD) en el próximo reinicio.
        5. Mantiene ngrok corriendo en primer plano hasta que se presione Ctrl+C.
#>

param(
    [int]$Port = 5268
)

$AppSettingsPath = "$PSScriptRoot\..\src\LitoralMarket.Web\appsettings.Development.json"

# ── 1. Verificar que ngrok está disponible ──────────────────────────
if (-not (Get-Command ngrok -ErrorAction SilentlyContinue)) {
    Write-Error "ngrok no está en el PATH. Instalalo desde https://ngrok.com/download"
    exit 1
}

# ── 2. Iniciar ngrok en background ──────────────────────────────────
Write-Host "Iniciando ngrok en http://localhost:$Port ..." -ForegroundColor Cyan
$ngrokProcess = Start-Process ngrok `
    -ArgumentList "http http://localhost:$Port --log=stdout" `
    -PassThru `
    -WindowStyle Normal

# ── 3. Esperar a que el túnel esté listo (API local de ngrok) ───────
$ngrokApiUrl = "http://localhost:4040/api/tunnels"
$tunnelUrl   = $null
$intentos    = 0

Write-Host "Esperando tunnel de ngrok..." -ForegroundColor Yellow
while ($null -eq $tunnelUrl -and $intentos -lt 20) {
    Start-Sleep -Seconds 1
    $intentos++
    try {
        $resp      = Invoke-RestMethod $ngrokApiUrl -ErrorAction Stop
        $tunnelUrl = ($resp.tunnels | Where-Object { $_.proto -eq "https" } | Select-Object -First 1).public_url
    } catch { }
}

if ($null -eq $tunnelUrl) {
    Write-Error "No se pudo obtener la URL de ngrok después de $intentos intentos."
    $ngrokProcess | Stop-Process -Force
    exit 1
}

# ── 4. Actualizar appsettings.Development.json ───────────────────────
Write-Host ""
Write-Host "Tunnel activo: $tunnelUrl" -ForegroundColor Green
Write-Host "Webhook MP:    $tunnelUrl/api/mp-webhook" -ForegroundColor Green
Write-Host ""

$json = Get-Content $AppSettingsPath -Raw | ConvertFrom-Json

if ($null -eq $json.MercadoPago) {
    $json | Add-Member -MemberType NoteProperty -Name "MercadoPago" -Value ([PSCustomObject]@{ UrlBase = $tunnelUrl })
} else {
    $json.MercadoPago.UrlBase = $tunnelUrl
}

$json | ConvertTo-Json -Depth 10 | Set-Content $AppSettingsPath -Encoding UTF8
Write-Host "appsettings.Development.json actualizado con UrlBase = $tunnelUrl" -ForegroundColor Cyan
Write-Host ""
Write-Host "IMPORTANTE: reiniciá la app (dotnet run / F5) para que tome el nuevo URL." -ForegroundColor Yellow
Write-Host "Presioná Ctrl+C para detener ngrok." -ForegroundColor Gray
Write-Host ""

# ── 5. Mantener el proceso en primer plano ───────────────────────────
try {
    $ngrokProcess.WaitForExit()
} finally {
    # Limpiar la URL al salir
    $json.MercadoPago.UrlBase = ""
    $json | ConvertTo-Json -Depth 10 | Set-Content $AppSettingsPath -Encoding UTF8
    Write-Host "ngrok detenido. UrlBase limpiado en appsettings.Development.json." -ForegroundColor Gray
}
