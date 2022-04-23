for /d %%f in ("%~f1\*") do (
  "%~dp0/../../GeoArcSysAIOCLITool.exe" PAC "%%f" pack -c -bak
)
pause