@echo off
rem Starfront Control builds do not install plugins or publish releases.
dotnet build "%~dp0ninaAPI.csproj" --configuration Release %*
exit /b %errorlevel%
