[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$GameDirectory,[Parameter(Mandatory=$true)][string]$CompilerPath)
$ErrorActionPreference='Stop'
$managed=Join-Path $GameDirectory 'Probably Stolen_Data/Managed'
if(!(Test-Path -LiteralPath (Join-Path $managed 'Assembly-CSharp.dll'))){throw '没有找到游戏的 Managed 目录。'}
if(!(Test-Path -LiteralPath $CompilerPath)){throw '请指定 Roslyn csc.exe 的路径。'}
$package=Join-Path $PSScriptRoot '../build/pawnshop_companion'
New-Item -ItemType Directory -Path $package -Force | Out-Null
$references=@('mscorlib','System','System.Core','netstandard','Assembly-CSharp','UnityEngine.CoreModule','UnityEngine.UIModule','UnityEngine.TextRenderingModule','UnityEngine.InputLegacyModule','UnityEngine.UnityWebRequestModule','UnityEngine.UnityWebRequestWWWModule','UnityEngine.UI','Unity.TextMeshPro','Newtonsoft.Json','0Harmony') |
    ForEach-Object {"/reference:$managed/$_.dll"}
$sources=@(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | ForEach-Object {$_.FullName})
& $CompilerPath /nologo /noconfig /nostdlib+ /warnaserror+ /nowarn:1701 /optimize+ /deterministic+ /target:library "/out:$package/PawnshopCompanion.dll" @references @sources
if($LASTEXITCODE -ne 0){throw '编译未完成。'}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'manifest.xml') -Destination $package -Force
$textures=Join-Path $package 'Textures'
New-Item -ItemType Directory -Path $textures -Force | Out-Null
foreach($name in @('gpt.png','deepseek.png')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot "../assets/portraits/$name") -Destination $textures -Force}
Write-Output '公开版已生成，不含任何本机连接码。'
