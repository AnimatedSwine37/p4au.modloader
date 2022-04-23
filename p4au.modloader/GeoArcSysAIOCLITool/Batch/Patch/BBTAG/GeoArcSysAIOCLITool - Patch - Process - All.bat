@echo off
set /p prc="Enter Process Name or ID: "
@echo on
"%~dp0/../../../GeoArcSysAIOCLITool.exe" Patch %prc% -prc -p ~a -g BBTAG