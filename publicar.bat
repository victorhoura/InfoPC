@echo off
rem Gera o InfoPC pronto para levar a outro PC (nao precisa instalar o .NET la).
rem Saida: ..\InfoPC-dist\InfoPC.exe + LEIA-ME.txt + LICENSE.txt + THIRD-PARTY-NOTICES.txt (as licencas exigem os avisos junto do programa)
rem Sempre recompila do zero a partir do codigo atual desta pasta.
cd /d "%~dp0"
echo Fechando o InfoPC se estiver aberto ...
taskkill /f /im InfoPC.exe >nul 2>&1
echo Gerando InfoPC.exe a partir do codigo atual ...
rmdir /s /q obj\Release >nul 2>&1
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o "..\InfoPC-dist" -nologo -v q
if errorlevel 1 (
    echo.
    echo FALHOU. Veja os erros acima. O .exe antigo NAO foi atualizado.
    pause
    exit /b 1
)
copy /y "LEIA-ME.txt" "..\InfoPC-dist\LEIA-ME.txt" >nul
copy /y "LICENSE" "..\InfoPC-dist\LICENSE.txt" >nul
copy /y "THIRD-PARTY-NOTICES.txt" "..\InfoPC-dist\THIRD-PARTY-NOTICES.txt" >nul
echo.
echo Pronto: %~dp0..\InfoPC-dist
for %%F in ("..\InfoPC-dist\InfoPC.exe") do echo Arquivo gerado em %%~tF (confira se e de agora^)
pause
