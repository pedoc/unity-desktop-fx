cmake_minimum_required(VERSION 3.25)

# Ignore a machine-wide broken vcpkg MSBuild integration until the project opts in.
set(CMAKE_VS_GLOBALS
    "VcpkgEnabled=false"
    "VcpkgApplocalDeps=false"
)

project(InteractiveWallpaper
    VERSION 0.2.0
    DESCRIPTION "Single-process interactive 3D desktop wallpaper for Windows"
    LANGUAGES CXX
)

if(NOT WIN32)
    message(FATAL_ERROR "Interactive Wallpaper currently targets Windows only.")
endif()

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)
set(CMAKE_RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin")
set(CMAKE_LIBRARY_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin")
set(CMAKE_ARCHIVE_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/lib")

add_compile_definitions(
    UNICODE
    _UNICODE
    WIN32_LEAN_AND_MEAN
    NOMINMAX
)

if(MSVC)
    add_compile_options(/W4 /permissive- /utf-8 /EHsc)
    # Ship the native bridge without a dependency on Debug or VC++ runtime DLLs.
    set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()

enable_testing()
add_subdirectory(native)

find_program(INTERACTIVE_WALLPAPER_DOTNET_EXECUTABLE dotnet)
if(INTERACTIVE_WALLPAPER_DOTNET_EXECUTABLE)
    add_test(
        NAME runtime_contract_managed_verifier
        COMMAND "${INTERACTIVE_WALLPAPER_DOTNET_EXECUTABLE}" run
            --project "${PROJECT_SOURCE_DIR}/tools/RuntimeContractVerifier/RuntimeContractVerifier.csproj"
            --configuration Release
    )
    set_tests_properties(runtime_contract_managed_verifier PROPERTIES TIMEOUT 60)
endif()

find_program(INTERACTIVE_WALLPAPER_UNITY_EDITOR_EXECUTABLE
    NAMES Unity.exe
    HINTS
        "$ENV{INTERACTIVE_WALLPAPER_UNITY_EDITOR_DIR}"
        "C:/Program Files/Unity 6000.6.1f1/Editor"
        "C:/Program Files/Unity/Hub/Editor/6000.6.1f1/Editor"
)
find_program(INTERACTIVE_WALLPAPER_PWSH_EXECUTABLE pwsh)

if(INTERACTIVE_WALLPAPER_UNITY_EDITOR_EXECUTABLE AND INTERACTIVE_WALLPAPER_PWSH_EXECUTABLE)
    add_test(
        NAME unity_runtime_static_compile
        COMMAND "${INTERACTIVE_WALLPAPER_PWSH_EXECUTABLE}"
            -NoLogo
            -NoProfile
            -File "${PROJECT_SOURCE_DIR}/tests/compile-unity-runtime.ps1"
            -EditorExecutable "${INTERACTIVE_WALLPAPER_UNITY_EDITOR_EXECUTABLE}"
            -ProjectPath "${PROJECT_SOURCE_DIR}/unity/InteractiveWallpaper3D"
            -OutputDirectory "${CMAKE_BINARY_DIR}/test-output/unity-static-compile"
    )
    set_tests_properties(unity_runtime_static_compile PROPERTIES TIMEOUT 90)

    add_test(
        NAME unity_project_structure_validation
        COMMAND "${INTERACTIVE_WALLPAPER_PWSH_EXECUTABLE}"
            -NoLogo
            -NoProfile
            -File "${PROJECT_SOURCE_DIR}/tests/validate-unity-project.ps1"
            -EditorExecutable "${INTERACTIVE_WALLPAPER_UNITY_EDITOR_EXECUTABLE}"
            -ProjectPath "${PROJECT_SOURCE_DIR}/unity/InteractiveWallpaper3D"
    )
    set_tests_properties(unity_project_structure_validation PROPERTIES TIMEOUT 60)

    add_test(
        NAME unity_editmode_tests
        COMMAND "${INTERACTIVE_WALLPAPER_PWSH_EXECUTABLE}"
            -NoLogo
            -NoProfile
            -File "${PROJECT_SOURCE_DIR}/tests/run-unity-editmode.ps1"
            -EditorExecutable "${INTERACTIVE_WALLPAPER_UNITY_EDITOR_EXECUTABLE}"
            -ProjectPath "${PROJECT_SOURCE_DIR}/unity/InteractiveWallpaper3D"
            -OutputDirectory "${CMAKE_BINARY_DIR}/test-output/unity-editmode"
    )
    set_tests_properties(unity_editmode_tests PROPERTIES
        TIMEOUT 660
        SKIP_REGULAR_EXPRESSION "SKIP:"
    )
endif()

set(INTERACTIVE_WALLPAPER_UNITY_PLAYER_EXECUTABLE "" CACHE FILEPATH "Built Interactive Wallpaper executable for direct runtime smoke testing")
if(INTERACTIVE_WALLPAPER_UNITY_PLAYER_EXECUTABLE AND
   EXISTS "${INTERACTIVE_WALLPAPER_UNITY_PLAYER_EXECUTABLE}" AND
   INTERACTIVE_WALLPAPER_PWSH_EXECUTABLE)
    add_test(
        NAME unity_player_direct_integration_smoke
        COMMAND "${INTERACTIVE_WALLPAPER_PWSH_EXECUTABLE}"
            -NoLogo
            -NoProfile
            -File "${PROJECT_SOURCE_DIR}/tests/smoke-unity-player-direct.ps1"
            -PlayerExecutable "${INTERACTIVE_WALLPAPER_UNITY_PLAYER_EXECUTABLE}"
            -NativeLibrary "$<TARGET_FILE:InteractiveWallpaperNative>"
            -OutputDirectory "${CMAKE_BINARY_DIR}/test-output/unity-player-direct"
    )
    set_tests_properties(unity_player_direct_integration_smoke PROPERTIES TIMEOUT 120)
endif()