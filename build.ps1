param (
    [ValidateSet("OpenVR", "OpenXR")]
    [string]$VrBackend,
    [switch]$MonoOnly,
    [switch]$Il2CppOnly,
    [switch]$DebugBuild,
    [switch]$DebugHelper,
    [switch]$PhysicsLog
)

function Resolve-MsBuildPath {
    $msbuildCmd = Get-Command msbuild -ErrorAction SilentlyContinue
    if ($msbuildCmd) {
        return $msbuildCmd.Source
    }

    $vswherePath = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswherePath) {
        $vsInstallPath = & $vswherePath -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
        if (-not [string]::IsNullOrWhiteSpace($vsInstallPath)) {
            $candidate = Join-Path $vsInstallPath "MSBuild\Current\Bin\MSBuild.exe"
            if (Test-Path $candidate) {
                return $candidate
            }
        }
    }

    return $null
}

# --- Configuration ---
$YourModName = "UnityVRMod"
$SolutionFile = "src/UnityVRMod.sln" 
$UniverseLibSln = "UniverseLib/src/UniverseLib.sln"
$NativeHelperProjectSolution = "UnityGraphicsHelper/UnityGraphicsHelper.sln"
$NativeHelperDllBuildOutputBase = "UnityGraphicsHelper/x64"
$NativeHelperDllName = "UnityGraphicsHelper.dll"
$LibDir = "lib"

# --- Tooling checks ---
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "dotnet SDK not found. Please install .NET SDK 8+ and retry."
    exit 1
}

$MsBuildExe = Resolve-MsBuildPath
$NativeBuildAvailable = ![string]::IsNullOrWhiteSpace($MsBuildExe)
if (-not $NativeBuildAvailable) {
    Write-Warning "MSBuild not found. Native C++ helper DLL ($NativeHelperDllName) will NOT be built. OpenXR builds may fail if $NativeHelperDllName is missing from lib/."
    Write-Warning "To fix: Install Visual Studio 2022 Build Tools with MSBuild and C++ toolset (v143), or run from Developer PowerShell for VS."
} else {
    Write-Host "Using MSBuild: $MsBuildExe"
}

# --- Define UniverseLib output paths ---
$UniverseLibMonoDllPath = "UniverseLib/Release/UniverseLib.Mono/UniverseLib.Mono.dll"
$UniverseLibIl2CppDllPath = "UniverseLib/Release/UniverseLib.Il2Cpp.Interop/UniverseLib.BIE.IL2CPP.Interop.dll"

# --- Build UniverseLib First ---
if (Test-Path $UniverseLibSln) {
    if (-not (Test-Path $UniverseLibMonoDllPath) -or -not (Test-Path $UniverseLibIl2CppDllPath)) {
        Write-Host "Building UniverseLib..."
        dotnet build $UniverseLibSln -c Release_Mono
        dotnet build $UniverseLibSln -c Release_IL2CPP_Interop_BIE
        Write-Host "UniverseLib builds complete."
    } else {
        Write-Host "UniverseLib DLLs already exist. Skipping UniverseLib build."
    }
}

# --- Build Native C++ Helper DLL (UnityGraphicsHelper.dll) ---
if (Test-Path $NativeHelperProjectSolution) {
    if ($NativeBuildAvailable) {
        Write-Host "Building Native C++ Helper ($NativeHelperDllName)..."
        $CppBuildConfig = if ($DebugHelper.IsPresent) { "Debug" } else { "Release" }
        Write-Host " Building $NativeHelperDllName with Configuration: $CppBuildConfig"

        & $MsBuildExe $NativeHelperProjectSolution /p:Configuration=$CppBuildConfig /p:Platform=x64 /t:Build

        if ($LASTEXITCODE -ne 0) {
            Write-Error "Native C++ Helper DLL build FAILED for configuration '$CppBuildConfig'. Aborting."
            exit 1
        }

        $ActualNativeHelperDllSourcePath = Join-Path (Join-Path $NativeHelperDllBuildOutputBase $CppBuildConfig) $NativeHelperDllName

        if (Test-Path $ActualNativeHelperDllSourcePath) {
            if (-not (Test-Path $LibDir)) { New-Item -Path $LibDir -ItemType Directory -Force | Out-Null }
            Copy-Item -Path $ActualNativeHelperDllSourcePath -Destination (Join-Path $LibDir $NativeHelperDllName) -Force
            Write-Host "  $NativeHelperDllName built and copied to $LibDir."
        } else {
            Write-Error "Native C++ Helper DLL ($NativeHelperDllName) not found at expected source path '$ActualNativeHelperDllSourcePath' after build. Aborting."
            exit 1
        }
    } else {
        Write-Warning "Skipping Native C++ Helper build (MSBuild unavailable)."
    }
}
Write-Host "--------------------------------------------------"

# --- Determine Build Suffixes ---
$ConfigBuildSuffix = if ($DebugBuild.IsPresent) { "Debug" } else { "Release" }
$OutputFolderSuffix = if ($DebugBuild.IsPresent) { ".Debug" } else { "" }
Write-Host "Selected overall build type: $ConfigBuildSuffix"
if ($PhysicsLog.IsPresent) {
    Write-Host "Physics diagnostics compile flag enabled: PHYSICS_LOG"
}

# --- Base Definitions for Runtimes ---
$MonoBaseDefinition = @{
    TargetRuntimeName = "Mono";
    ConfigNamePattern = "BIE_Unity_Mono_{0}_{1}"; 
    AssemblyNamePattern = "$($YourModName).BepInEx.Mono_{0}"; 
    OutputPathPattern = "Release/{0}/$($YourModName).BepInEx.Mono"; 
    UniverseLibDllPath = $UniverseLibMonoDllPath
}
$Il2CppBaseDefinition = @{
    TargetRuntimeName = "IL2CPP";
    ConfigNamePattern = "BIE_Unity_Cpp_{0}_{1}";
    AssemblyNamePattern = "$($YourModName).BepInEx.IL2CPP_{0}";
    OutputPathPattern = "Release/{0}/$($YourModName).BepInEx.IL2CPP";
    UniverseLibDllPath = $UniverseLibIl2CppDllPath
}
$BIE5MonoBaseDefinition = @{
    TargetRuntimeName = "BIE5-Mono";
    ConfigNamePattern = "BIE5_Unity_Mono_{0}_{1}";
    AssemblyNamePattern = "$($YourModName).BepInEx5.Mono_{0}";
    OutputPathPattern = "Release/{0}/$($YourModName).BepInEx5.Mono";
    UniverseLibDllPath = $UniverseLibMonoDllPath
}

# --- Determine which VR Backends to Process ---
$VrBackendsToProcess = [System.Collections.Generic.List[string]]::new()
if (-not [string]::IsNullOrEmpty($VrBackend)) {
    $VrBackendsToProcess.Add($VrBackend)
    Write-Host "Specific VR Backend selected: $VrBackend"
} else {
    Write-Host "No specific VR Backend selected. Processing all configured backends (OpenVR, OpenXR)."
    $VrBackendsToProcess.Add("OpenVR")
    $VrBackendsToProcess.Add("OpenXR")
}

# --- Select and augment targets to build based on flags and VrBackends ---
$targetsToBuildActual = [System.Collections.Generic.List[System.Collections.Hashtable]]::new()
$RuntimeDefinitionsToConsider = [System.Collections.Generic.List[System.Collections.Hashtable]]::new()

if ($MonoOnly.IsPresent) {
    $RuntimeDefinitionsToConsider.Add($MonoBaseDefinition);
    $RuntimeDefinitionsToConsider.Add($BIE5MonoBaseDefinition)
}
elseif ($Il2CppOnly.IsPresent) { $RuntimeDefinitionsToConsider.Add($Il2CppBaseDefinition) }
else {
    $RuntimeDefinitionsToConsider.Add($MonoBaseDefinition);
    $RuntimeDefinitionsToConsider.Add($Il2CppBaseDefinition);
    $RuntimeDefinitionsToConsider.Add($BIE5MonoBaseDefinition)
}

foreach ($currentProcessingVrBackend in $VrBackendsToProcess) {
    Write-Host ""
    Write-Host "Processing for VR Backend: $currentProcessingVrBackend"
    foreach ($baseDef in $RuntimeDefinitionsToConsider) {
        $currentTargetDef = $baseDef.Clone()
        $currentTargetDef.VrBackend = $currentProcessingVrBackend
        $currentTargetDef.FullConfigName = $baseDef.ConfigNamePattern -f $currentProcessingVrBackend, $ConfigBuildSuffix
        $currentTargetDef.EffectiveAssemblyName = $baseDef.AssemblyNamePattern -f $currentProcessingVrBackend
        $currentTargetDef.EffectiveOutputPathBase = $baseDef.OutputPathPattern -f $currentProcessingVrBackend
        $targetsToBuildActual.Add($currentTargetDef)
    }
}

if ($targetsToBuildActual.Count -eq 0) { Write-Error "No build targets were finalized. Exiting."; exit 1 }

# --- Loop through selected targets and build ---
foreach ($currentTargetDef in $targetsToBuildActual) {
    $ConfigToUse = $currentTargetDef.FullConfigName
    $CurrentAssemblyName = $currentTargetDef.EffectiveAssemblyName
    $CurrentOutputPath = "$($currentTargetDef.EffectiveOutputPathBase)${OutputFolderSuffix}" 
    $FinalDllName = "$($CurrentAssemblyName).dll"

    Write-Host ""
    Write-Host "Building $($currentTargetDef.TargetRuntimeName) (Configuration: $ConfigToUse)..."
    Write-Host "  Outputting to: $CurrentOutputPath"

    if (-not (Test-Path $CurrentOutputPath)) { New-Item -Path $CurrentOutputPath -ItemType Directory -Force | Out-Null }

    $DotNetBuildArgs = @("build", $SolutionFile, "-c", $ConfigToUse)
    if ($PhysicsLog.IsPresent) {
        $DotNetBuildArgs += "/p:PhysicsLog=true"
    }

    & dotnet @DotNetBuildArgs
    if ($LASTEXITCODE -ne 0) { Write-Error "Build FAILED for $ConfigToUse."; continue }

    $OutputDllPath = Join-Path $CurrentOutputPath $FinalDllName
    if (-not (Test-Path $OutputDllPath)) { Write-Error "Build for $ConfigToUse produced no output DLL at '$OutputDllPath'."; continue }
    Write-Host "  Build successful: $OutputDllPath"

    Write-Host "  Preparing final plugin directory structure..."
    $FinalPluginSubDir = Join-Path $CurrentOutputPath "plugins\$YourModName" 
    New-Item -Path $FinalPluginSubDir -ItemType Directory -Force | Out-Null

     Move-Item -Path $OutputDllPath -Destination (Join-Path $FinalPluginSubDir "$YourModName.dll") -Force
     Copy-Item $currentTargetDef.UniverseLibDllPath -Destination $FinalPluginSubDir -Force
     
     $ModelSourceDir = "src/Model"
     if (Test-Path $ModelSourceDir) {
         $ModelDestDir = Join-Path $FinalPluginSubDir "Model"
         New-Item -Path $ModelDestDir -ItemType Directory -Force | Out-Null
         Copy-Item (Join-Path $ModelSourceDir "*") -Destination $ModelDestDir -Recurse -Force
         Write-Host "  Copied model assets to: $ModelDestDir"
     }
     
     if ($currentTargetDef.VrBackend -eq "OpenVR") {
         Copy-Item (Join-Path $LibDir "openvr_api.dll") -Destination $FinalPluginSubDir -Force
     } elseif ($currentTargetDef.VrBackend -eq "OpenXR") {
        Copy-Item (Join-Path $LibDir "openxr_loader.dll") -Destination $FinalPluginSubDir -Force
        Copy-Item (Join-Path $LibDir $NativeHelperDllName) -Destination $FinalPluginSubDir -Force
    }
    
    Write-Host "  $($currentTargetDef.TargetRuntimeName) build for $VrBackend complete. Output: $FinalPluginSubDir"
    Write-Host "--------------------------------------------------"
}

Write-Host "Build script finished."
