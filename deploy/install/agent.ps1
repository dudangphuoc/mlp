# Install the MLPlatform agent on Windows (Docker Desktop + WSL 2) — plug-and-play (D-37). Served by the control plane at /install/agent.ps1.
#   iwr <cp>/install/agent.ps1 -UseBasicParsing | iex; Install-MlpAgent -Token mlpe_… -Data 'D:\datasets' -Models 'D:\models' [-Instance dev]
#   iwr <cp>/install/agent.ps1 -UseBasicParsing | iex; Test-MlpAgentHost            # doctor only
# Run in PowerShell with "Run as Administrator". The agent runs as a Scheduled Task at startup (restarts automatically on failure).
$script:MlpCp = "__MLP_PUBLIC_URL__"
$script:MlpAgentAddr = "__MLP_AGENT_ADDR__"
$script:MlpPin = "__MLP_AGENT_PIN__"

function Test-MlpAgentHost {
    param([string]$Data = "", [string]$Models = "", [string]$Instance = "")
    $ErrorActionPreference = "Continue"   # docker/nvidia-smi write to stderr normally — PowerShell 5.1 must not treat that as a terminating error
    $script:ok = 0; $script:warns = 0; $script:fails = 0
    function Ok($k, $v)   { Write-Host ("  [OK]   {0,-26} {1}" -f $k, $v) -ForegroundColor Green;  $script:ok++ }
    function Warn($k, $v) { Write-Host ("  [WARN] {0,-26} {1}" -f $k, $v) -ForegroundColor Yellow; $script:warns++ }
    function Bad($k, $v)  { Write-Host ("  [FAIL] {0,-26} {1}" -f $k, $v) -ForegroundColor Red;    $script:fails++ }
    Write-Host "`n==> Doctor — checking this machine"
    # Docker Desktop stores registry credentials in Credential Manager — unusable when running over SSH or as a Scheduled Task (S4U).
    # The agent and doctor use a dedicated DOCKER_CONFIG without credsStore (public images / the platform registry need no login).
    # On Windows the Docker CLI falls back to wincred when the config has no credsStore → declare an empty platform helper (always "not found").
    $dc = "C:\ProgramData\MLPlatform$(if ($Instance) { "-$Instance" })\docker"
    New-Item -ItemType Directory -Force $dc | Out-Null
    [IO.File]::WriteAllText("$dc\docker-credential-mlpnone.cmd", "@echo off`r`nif `"%1`"==`"get`" (echo credentials not found in native keychain& exit /b 1)`r`nif `"%1`"==`"list`" (echo {}& exit /b 0)`r`nexit /b 0`r`n")
    [IO.File]::WriteAllText("$dc\config.json", '{"auths":{},"credsStore":"mlpnone"}')
    $env:DOCKER_CONFIG = $dc
    if (-not ($env:PATH -split ";" -contains $dc)) { $env:PATH = "$dc;$env:PATH" }
    $arch = $env:PROCESSOR_ARCHITECTURE
    $os = Get-CimInstance Win32_OperatingSystem
    if ($arch -eq "AMD64") { Ok "Platform" "Windows $($os.Version) $arch" } else { Bad "Platform" "Windows $arch is not supported (x64 required)" }
    $cpus = (Get-CimInstance Win32_Processor | Measure-Object NumberOfLogicalProcessors -Sum).Sum
    $mem = [int64]$os.TotalVisibleMemorySize * 1024
    Ok "CPU / RAM" ("{0} cores, {1} GiB" -f $cpus, [math]::Round($mem / 1GB))
    $wsl = (wsl.exe --status 2>$null) -join " "
    if ($LASTEXITCODE -eq 0) { Ok "WSL 2" "enabled" } else { Bad "WSL 2" "not enabled — run: wsl --install, then restart" }
    $dockerVer = ""; $runtimes = ""
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { Bad "Docker" "Docker Desktop is not installed: https://docs.docker.com/desktop/setup/install/windows-install/" }
    else {
        $dockerVer = (docker version --format "{{.Server.Version}}" 2>$null)
        if ($LASTEXITCODE -ne 0 -or -not $dockerVer) { Bad "Docker" "Docker Desktop is not running (open Docker Desktop and enable 'Start when you sign in')" }
        else { Ok "Docker" "$dockerVer"; $runtimes = (docker info --format "{{range `$k, `$v := .Runtimes}}{{`$k}} {{end}}" 2>$null) }
    }
    $gpuCount = 0; $gpus = ""; $driver = ""; $cuda = ""
    if (Get-Command nvidia-smi -ErrorAction SilentlyContinue) {
        $lines = nvidia-smi --query-gpu=name,memory.total,driver_version --format=csv,noheader 2>$null
        if ($LASTEXITCODE -eq 0 -and $lines) {
            $gpuCount = @($lines).Count; $gpus = (@($lines) | ForEach-Object { ($_ -split ",")[0..1] -join "," }) -join ";"
            $driver = (($lines | Select-Object -First 1) -split ",")[2].Trim()
            $m = [regex]::Match(((nvidia-smi) -join " "), 'CUDA (?:UMD )?Version:\s*([0-9.]+)'); $cuda = if ($m.Success) { $m.Groups[1].Value } else { "" }
            Ok "GPU NVIDIA" "$gpuCount x $gpus (driver $driver, CUDA $cuda)"
            if ($dockerVer) {
                docker run --rm --gpus all --entrypoint nvidia-smi nvidia/cuda:12.6.3-base-ubuntu24.04 -L *> $null
                if ($LASTEXITCODE -eq 0) { Ok "GPU in Docker" "containers can see the GPU (WSL 2)" } else { Bad "GPU in Docker" "containers cannot see the GPU — update the NVIDIA driver + Docker Desktop (WSL 2 backend)" }
            }
        }
    } else { Ok "GPU" "no NVIDIA GPU — this machine runs CPU jobs" }
    foreach ($p in @($Data, $Models) | Where-Object { $_ }) {
        $drive = (Split-Path -Qualifier $p) -replace ":", ""
        $free = (Get-PSDrive $drive -ErrorAction SilentlyContinue).Free
        if ($free -lt 20GB) { Warn "Disk $p" ("{0} GiB free (< 20)" -f [math]::Round($free / 1GB)) } else { Ok "Disk $p" ("{0} GiB free" -f [math]::Round($free / 1GB)) }
    }
    try { Invoke-WebRequest "$script:MlpCp/health/live" -UseBasicParsing -TimeoutSec 5 | Out-Null; Ok "Control plane" $script:MlpCp } catch { Bad "Control plane" "cannot reach $script:MlpCp" }
    $h, $port = $script:MlpAgentAddr -split ":"
    if ((Test-NetConnection $h -Port $port -WarningAction SilentlyContinue).TcpTestSucceeded) { Ok "Agent channel" $script:MlpAgentAddr } else { Bad "Agent channel" "cannot open TCP $script:MlpAgentAddr" }
    Write-Host ("`n  Doctor: {0} passed, {1} warnings, {2} errors" -f $script:ok, $script:warns, $script:fails)
    return [ordered]@{ os = "Windows"; arch = $arch; libc = ""; kernel = $os.Version; cpus = $cpus; memory_bytes = $mem; docker = "$dockerVer";
        docker_runtimes = "$runtimes"; gpu_count = $gpuCount; gpus = $gpus; gpu_driver = $driver; cuda = $cuda; warnings = $script:warns; errors = $script:fails }
}

function Install-MlpAgent {
    param([Parameter(Mandatory)][string]$Token, [Parameter(Mandatory)][string]$Data, [Parameter(Mandatory)][string]$Models, [string]$Instance = "", [string[]]$FsRoot = @())
    $ErrorActionPreference = "Stop"
    if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Run PowerShell with 'Run as Administrator'" }
    $doctor = Test-MlpAgentHost -Data $Data -Models $Models -Instance $Instance
    if ($doctor.errors -gt 0) { throw "Fix the [FAIL] items, then run the install command again." }

    Write-Host "`n==> Asking the control plane to build the agent for this machine"
    $hdr = @{ Authorization = "Bearer $Token" }
    $st = Invoke-RestMethod -Method Post -Uri "$script:MlpCp/install/agent/build" -Headers $hdr -ContentType "application/json" -Body ($doctor | ConvertTo-Json -Compress)
    $waited = 0
    while ($st.status -eq "building") {
        if ($waited -ge 1200) { throw "Build took longer than 20 minutes — try again later" }
        if ($waited % 30 -eq 0) { Write-Host "  building agent $($st.rid) (the first build for this platform takes a few minutes)…" }
        Start-Sleep 5; $waited += 5
        $st = Invoke-RestMethod "$script:MlpCp/install/agent/build/$($st.rid)"
    }
    if ($st.status -ne "ready") { throw "Agent build failed: $($st | ConvertTo-Json -Compress)" }

    $sfx = if ($Instance) { "-$Instance" } else { "" }
    $taskName = if ($Instance) { "MLPlatform Agent ($Instance)" } else { "MLPlatform Agent" }
    $prefix = "C:\Program Files\MLPlatform$sfx"; $state = "C:\ProgramData\MLPlatform$sfx"
    New-Item -ItemType Directory -Force $prefix, $state, $Data, $Models | Out-Null
    Write-Host "`n==> Downloading agent ($($st.rid))"
    $tmp = Join-Path $env:TEMP "mlp-agent.exe"
    Invoke-WebRequest "$script:MlpCp$($st.url)" -OutFile $tmp -UseBasicParsing
    if ((Get-FileHash $tmp -Algorithm SHA256).Hash.ToLowerInvariant() -ne $st.sha256) { Remove-Item $tmp; throw "SHA-256 mismatch — aborting" }
    # Reinstall: stop the running agent (the .exe is locked while the process is alive).
    Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue | Stop-ScheduledTask -ErrorAction SilentlyContinue
    $mine = { Get-Process mlp-agent -ErrorAction SilentlyContinue | Where-Object { $_.Path -ieq "$prefix\mlp-agent.exe" } }
    & $mine | Stop-Process -Force -ErrorAction SilentlyContinue
    for ($i = 0; $i -lt 20 -and (& $mine); $i++) { Start-Sleep -Milliseconds 500 }
    Move-Item -Force $tmp "$prefix\mlp-agent.exe"

    Write-Host "`n==> Registering Scheduled Task '$taskName'"
    $agentArgs = "--state `"$state\agent.json`" --cp $script:MlpAgentAddr --token $Token --data `"$Data`" --models `"$Models`""
    if ($script:MlpPin) { $agentArgs += " --pin $script:MlpPin" }
    if ($Instance) { $agentArgs += " --instance $Instance" }
    foreach ($r in $FsRoot) { $agentArgs += " --fs-root `"$r`"" }
    $agentArgs += " --service schtasks"   # OTA (D-38): the upgrade script stops/starts the agent via the Scheduled Task
    # The agent writes agent.log itself (no cmd.exe wrapper — cmd used to hold a file lock that made the next start fail).
    $action = New-ScheduledTaskAction -Execute "$prefix\mlp-agent.exe" -Argument $agentArgs -WorkingDirectory $state
    # Runs as the installing user's account (Docker Desktop belongs to the user session), no stored password needed (S4U).
    $me = [Security.Principal.WindowsIdentity]::GetCurrent().Name   # MACHINE\user or DOMAIN\user (over SSH USERDOMAIN may be WORKGROUP)
    $principal = New-ScheduledTaskPrincipal -UserId $me -LogonType S4U -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit ([TimeSpan]::Zero) -StartWhenAvailable
    $triggers = @((New-ScheduledTaskTrigger -AtStartup), (New-ScheduledTaskTrigger -AtLogOn -User $me))
    Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal -Settings $settings -Trigger $triggers -Force -ErrorAction Stop | Out-Null
    Start-ScheduledTask -TaskName $taskName -ErrorAction Stop
    Write-Host "`n==> Done — the machine will appear on the Machines (Fleet) page once it connects" -ForegroundColor Green
}
