@echo off
cd /d "%~dp0"
if exist "dist\KugouStory-win-x64\KugouStory.exe" (
    start "" "dist\KugouStory-win-x64\KugouStory.exe"
) else (
    dotnet run --project "src\KugouStory\KugouStory.csproj" -c Release
)
