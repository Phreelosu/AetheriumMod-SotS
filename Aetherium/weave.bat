REM original version https://risk-of-thunder.github.io/R2Wiki/Mod-Creation/C%23-Programming/Networking/UNet/
REM open this in vs it'll be so much nicer

set TargetFileName=Aetherium.dll
set TargetDir=bin\Debug\netstandard2.1

REM robocopy to our weaver folder. idk what the fuck robocopy does but we leave one there for storage
robocopy %TargetDir% NetworkWeaver %TargetFileName% > %TargetDir%\Robocopy

REM rename our original build to prepatch
IF EXIST %TargetDir%\%TargetFileName%.prepatch (
    DEL /F %TargetDir%\%TargetFileName%.prepatch
)
ren %TargetDir%\%TargetFileName% %TargetFileName%.prepatch

REM le epic networking patch
REM    Unity.UNetWeaver.exe    {path to Coremodule}                      {Path  Networking}                                     {output path} {Path to patching dll}  {Path to all needed references for the to-be-patched dll}
NetworkWeaver\Unity.UNetWeaver.exe .\libs\weaver\UnityEngine.CoreModule.dll .\libs\weaver\com.unity.multiplayer-hlapi.Runtime.dll %TargetDir%\ NetworkWeaver\%TargetFileName% .\libs\weaver
del NetworkWeaver\%TargetFileName%
del %TargetDir%\Robocopy

REM that's it. This is meant to pretend we just built a dll like any other time except this one is networked
REM add your postbuilds in vs like it's any other project