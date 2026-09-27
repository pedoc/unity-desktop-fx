add_library(InteractiveWallpaperNative SHARED
    src/desktop_actions.cpp
    src/desktop_snapshot.cpp
    src/desktop_window.cpp
    src/native_api.cpp
    src/snapshot_json.cpp
)

target_include_directories(InteractiveWallpaperNative
    PUBLIC
        "${CMAKE_CURRENT_SOURCE_DIR}/include"
        "${PROJECT_SOURCE_DIR}/native/include"
)

target_compile_definitions(InteractiveWallpaperNative PRIVATE INTERACTIVE_WALLPAPER_NATIVE_EXPORTS)

target_link_libraries(InteractiveWallpaperNative PRIVATE
    ole32
    oleaut32
    shell32
    shlwapi
    user32
    uuid
    windowscodecs
)

set_target_properties(InteractiveWallpaperNative PROPERTIES OUTPUT_NAME "InteractiveWallpaper.Native")

find_program(INTERACTIVE_WALLPAPER_NATIVE_PWSH_EXECUTABLE pwsh)
if(INTERACTIVE_WALLPAPER_NATIVE_PWSH_EXECUTABLE)
    add_test(
        NAME native_desktop_snapshot_smoke
        COMMAND "${INTERACTIVE_WALLPAPER_NATIVE_PWSH_EXECUTABLE}"
            -NoLogo
            -NoProfile
            -File "${PROJECT_SOURCE_DIR}/tests/smoke-native-desktop.ps1"
            -NativeLibrary "$<TARGET_FILE:InteractiveWallpaperNative>"
            -OutputDirectory "${CMAKE_BINARY_DIR}/test-output/native-desktop"
    )
    set_tests_properties(native_desktop_snapshot_smoke PROPERTIES TIMEOUT 60)
endif()