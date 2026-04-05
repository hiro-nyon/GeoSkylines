param(
    [string]$Configuration = "Debug",
    [string]$CitiesSkylinesManagedDir = "C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines\Cities_Data\Managed",
    [string]$CitiesSkylinesModsDir = "$env:LOCALAPPDATA\Colossal Order\Cities_Skylines\Addons\Mods"
)

$root = Split-Path -Parent $PSScriptRoot

msbuild "$root\GeoSkylines.sln" /t:GeoSkylines /p:Configuration=$Configuration /p:CitiesSkylinesManagedDir="$CitiesSkylinesManagedDir" /p:CitiesSkylinesModsDir="$CitiesSkylinesModsDir"
dotnet build "$root\GeoSkylines.ExportCli\GeoSkylines.ExportCli.csproj" -c $Configuration
