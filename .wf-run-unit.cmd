@echo off
cd /d %~dp0
echo === UNIT === > .wf-test.log
dotnet test Content.Tests/Content.Tests.csproj -c Debug --filter "FullyQualifiedName~MissileGuidanceTest" --nologo -m:1 >> .wf-test.log 2>&1
echo UNIT_EXIT=%ERRORLEVEL% >> .wf-test.log
echo === INTEGRATION === >> .wf-test.log
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c Debug --filter "FullyQualifiedName~WFMissileFlightTest" --nologo -m:1 >> .wf-test.log 2>&1
echo INT_EXIT=%ERRORLEVEL% >> .wf-test.log
