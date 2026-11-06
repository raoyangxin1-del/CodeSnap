[CmdletBinding()]
param(
    [switch]$TestOcr,
    [switch]$ProbeMta,
    [string]$ScanTest = "",
    [string]$LogFile = "",
    [int]$ExitAfter = 0
)
# CodeSnap 引导器：编译 CodeSnap.App.cs 并启动悬浮识别窗口
$ErrorActionPreference = 'Stop'
$appDir = $PSScriptRoot

function Test-ManagedDll([string]$p) {
    try { $null = [System.Reflection.AssemblyName]::GetAssemblyName($p); return $true } catch { return $false }
}

function Resolve-Lib([string]$name) {
    $candidates = @(
        (Join-Path $appDir "lib\$name")
        (Join-Path $env:USERPROFILE ".cache\codex-runtimes\codex-primary-runtime\dependencies\native\powershell\$name")
        (Join-Path $env:USERPROFILE ".cache\codex-runtimes\codex-primary-runtime\dependencies\native\powershell\ref\$name")
    )
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }
    $asus = "C:\Program Files\ASUS\AacAmbientHal\$name"
    if (Test-Path $asus) { return $asus }
    return $null
}

$projDll = Resolve-Lib 'Microsoft.Windows.SDK.NET.dll'
$winrtDll = Resolve-Lib 'WinRT.Runtime.dll'
if (-not $projDll -or -not $winrtDll) {
    Write-Error "缺少 Microsoft.Windows.SDK.NET.dll / WinRT.Runtime.dll，请放到 $appDir\lib\ 目录。"
    return 1
}

$codePath = Join-Path $appDir 'CodeSnap.App.cs'
if (-not (Test-Path $codePath)) { Write-Error "缺少 CodeSnap.App.cs"; return 1 }

try {
    $null = [System.Reflection.Assembly]::LoadFrom($winrtDll)
    $null = [System.Reflection.Assembly]::LoadFrom($projDll)
} catch {
    Write-Error "加载 WinRT 运行库失败: $($_.Exception.Message)"
    return 1
}

Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll')
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll')

$allManaged = Get-ChildItem "$PSHOME\*.dll" -ErrorAction SilentlyContinue | Where-Object { Test-ManagedDll $_.FullName }
if (-not $allManaged) {
    Write-Error "无法定位 .NET 桌面运行库（当前 PowerShell 缺少桌面程序集）"
    return 1
}

$srcText = Get-Content -Raw -Encoding UTF8 $codePath
$tree = [Microsoft.CodeAnalysis.CSharp.SyntaxFactory]::ParseSyntaxTree($srcText)
$refs = New-Object 'System.Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]'
foreach ($d in $allManaged) { $refs.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($d.FullName)) }
$refs.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($projDll))
$refs.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($winrtDll))

$opts = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::DynamicallyLinkedLibrary)
$comp = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('CodeSnap.App', [Microsoft.CodeAnalysis.SyntaxTree[]]@($tree), $refs, $opts)
$errs = $comp.GetDiagnostics() | Where-Object { $_.Severity -eq [Microsoft.CodeAnalysis.DiagnosticSeverity]::Error }
if (@($errs).Count -gt 0) {
    foreach ($e in $errs) { Write-Host ("编译错误 " + $e.Id + ": " + $e.GetMessage()) -ForegroundColor Red }
    return 1
}
$ms = New-Object System.IO.MemoryStream
$emit = $comp.Emit($ms)
if (-not $emit.Success) {
    foreach ($e in $emit.Diagnostics) { Write-Host ("生成错误 " + $e.Id + ": " + $e.GetMessage()) -ForegroundColor Red }
    return 1
}
$null = [System.Reflection.Assembly]::Load($ms.ToArray())

if ($ProbeMta) {
    Write-Output ('MTA OCR probe: ' + [CodeSnap.MtaProbe]::Run())
    return 0
}

$argsList = New-Object 'System.Collections.Generic.List[string]'
$argsList.Add("--dir=$appDir")
if ($TestOcr) { $argsList.Add('--test-ocr') }
if ($ScanTest -ne "") { $argsList.Add("--scan-test=$ScanTest") }
if ($LogFile -ne "") { $argsList.Add("--logfile=$LogFile") }
if ($ExitAfter -gt 0) { $argsList.Add("--exit-after=$ExitAfter") }
return [CodeSnap.AppHost]::Start($argsList.ToArray())
