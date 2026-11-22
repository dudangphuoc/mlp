# MLPlatform agent OTA on Windows (D-38) — the control plane generates this from the template; the agent runs it as a one-off SYSTEM Scheduled Task (detached from the agent).
# Steps are written to status.txt; the control plane writes final-ok once the new version connects stably; after 240 s → restore the old version.
# Machine rebooted midway → the "MLPlatform\AgentUpgradeRecover" task (at startup) reruns with -Recover.
param([switch]$Recover)
$ErrorActionPreference = "Continue"
$Job = $PSScriptRoot; $Status = Join-Path $Job "status.txt"; $PhaseFile = Join-Path $Job "phase"
$Exe = '__EXE__'; $Url = '__URL__'; $Sha = '__SHA__'; $Inst = '__INST__'; $Task = if ($Inst) { "MLPlatform Agent ($Inst)" } else { 'MLPlatform Agent' }
$mine = { Get-Process mlp-agent -ErrorAction SilentlyContinue | Where-Object { $_.Path -ieq $Exe } }
function Say($m) { Add-Content -Path $Status -Value ("{0} {1}" -f (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"), $m) -Encoding UTF8 }
function Phase($p) { Set-Content -Path "$PhaseFile.tmp" -Value $p -Encoding UTF8; Move-Item -Force "$PhaseFile.tmp" $PhaseFile; Say $p }
function SvcStop {
    Stop-ScheduledTask -TaskName $Task -ErrorAction SilentlyContinue
    # the parent cmd.exe (older installs wrapped the agent for logging) holds a lock on agent.log → stop it too
    Get-CimInstance Win32_Process -Filter "Name='cmd.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -like "*$Exe*" } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    & $mine | Stop-Process -Force -ErrorAction SilentlyContinue
    for ($i = 0; $i -lt 30 -and (& $mine); $i++) { Start-Sleep -Milliseconds 500 }
}
# Older installs ran the agent via "cmd /c … >> agent.log": switch to running the exe directly (the agent writes its own log).
function MigrateTask {
    $t = Get-ScheduledTask -TaskName $Task -ErrorAction SilentlyContinue
    if ($t -and $t.Actions[0].Execute -ieq "cmd.exe" -and $t.Actions[0].Arguments -match '^/c ""(.+?)" (.+?) >> ') {
        $a = New-ScheduledTaskAction -Execute $Matches[1] -Argument $Matches[2] -WorkingDirectory (Split-Path $Status | Split-Path | Split-Path)
        Set-ScheduledTask -TaskName $Task -Action $a | Out-Null
        Say "switched the task to run the agent directly"
    }
}
# A Scheduled Task runs only one instance: wait for the task to return to Ready before starting it, and verify the agent process actually comes up (up to 3 retries).
function SvcStart {
    for ($try = 0; $try -lt 3; $try++) {
        for ($i = 0; $i -lt 30 -and (Get-ScheduledTask -TaskName $Task).State -eq "Running" -and -not (& $mine); $i++) {
            Stop-ScheduledTask -TaskName $Task -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 500
        }
        Start-ScheduledTask -TaskName $Task -ErrorAction SilentlyContinue
        for ($i = 0; $i -lt 20; $i++) { if (& $mine) { return }; Start-Sleep -Milliseconds 500 }
        Say "agent not up yet — retrying start ($($try + 1))"
    }
}
function Put($src) { Copy-Item -Force $src "$Exe.new"; Move-Item -Force "$Exe.new" $Exe }
function RecoveryOn {
    $a = New-ScheduledTaskAction -Execute "powershell.exe" -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$Job\upgrade.ps1`" -Recover"
    Register-ScheduledTask -TaskName "MLPlatform\AgentUpgradeRecover" -Action $a -Trigger (New-ScheduledTaskTrigger -AtStartup) -User "SYSTEM" -RunLevel Highest -Force | Out-Null
}
function RecoveryOff { Unregister-ScheduledTask -TaskName "AgentUpgradeRecover" -TaskPath "\MLPlatform\" -Confirm:$false -ErrorAction SilentlyContinue }
function WaitOk { for ($i = 0; $i -lt 240; $i += 2) { if (Test-Path (Join-Path $Job "final-ok")) { return $true }; Start-Sleep 2 }; return $false }
function Rollback($why) { Phase "rolling back"; SvcStop; Put (Join-Path $Job "agent.kept"); SvcStart; RecoveryOff; Say "rolled back: $why"; exit 1 }

$mutex = New-Object System.Threading.Mutex($false, "Global\MLPlatformAgentUpgrade")
if (-not $mutex.WaitOne(0)) { Say "failed: another upgrade is already running"; exit 1 }
try {
    if ($Recover) {
        $p = if (Test-Path $PhaseFile) { (Get-Content $PhaseFile -Raw).Trim() } else { "" }
        if ($p -in @("stopping", "installing", "starting", "connecting")) {
            if (Test-Path (Join-Path $Job "final-ok")) { Phase "done"; RecoveryOff; exit 0 }
            Say "recovering after restart (was at $p)"; SvcStart
            if (WaitOk) { Phase "done"; RecoveryOff; exit 0 }
            Rollback "machine restarted during the upgrade and the new version did not connect"
        }
        RecoveryOff; exit 0
    }
    $drive = (Get-Item $Job).PSDrive
    if ($drive.Free -lt 300MB) { Say "failed: at least 300 MB of free space is required"; exit 1 }
    Phase "downloading"
    try { Invoke-WebRequest -Uri $Url -OutFile (Join-Path $Job "agent.new") -UseBasicParsing } catch { Say "failed: could not download the new version ($($_.Exception.Message))"; exit 1 }
    Phase "checking"
    if ((Get-FileHash (Join-Path $Job "agent.new") -Algorithm SHA256).Hash.ToLowerInvariant() -ne $Sha) { Say "failed: SHA-256 mismatch"; exit 1 }
    Copy-Item -Force $Exe (Join-Path $Job "agent.kept")
    RecoveryOn
    Phase "stopping"; SvcStop; MigrateTask
    Phase "installing"
    try { Put (Join-Path $Job "agent.new") } catch { Rollback "could not replace the file ($($_.Exception.Message))" }
    Phase "starting"; SvcStart
    Phase "connecting"
    if (-not (WaitOk)) { Rollback "the new version did not connect to the control plane within 240 s" }
    RecoveryOff
    Remove-Item -Force (Join-Path $Job "agent.new"), (Join-Path $Job "agent.kept") -ErrorAction SilentlyContinue
    Phase "done"
} finally { $mutex.ReleaseMutex() }
