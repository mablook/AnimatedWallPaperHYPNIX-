cmake_minimum_required(VERSION 3.20)
project(HypnixEffekseerBridge LANGUAGES CXX)
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

set(EFFEKSEER_SOURCE_DIR "" CACHE PATH "Path to the pinned Effekseer source checkout")
if(NOT EXISTS "${EFFEKSEER_SOURCE_DIR}/CMakeLists.txt")
    message(FATAL_ERROR "Set EFFEKSEER_SOURCE_DIR or use scripts/build-native.ps1 -FetchEffekseer")
endif()
foreach(option BUILD_EDITOR BUILD_VIEWER BUILD_TEST BUILD_EXAMPLES BUILD_TOOLS
        BUILD_GL BUILD_DX9 BUILD_DX12 BUILD_VULKAN BUILD_METAL BUILD_WEBGPU NETWORK_ENABLED)
    set(${option} OFF CACHE BOOL "Disabled for the HYPNIX runtime" FORCE)
endforeach()
set(BUILD_DX11 ON CACHE BOOL "HYPNIX Direct3D runtime" FORCE)
set(USE_MSVC_RUNTIME_LIBRARY_DLL OFF CACHE BOOL "Static CRT" FORCE)
add_subdirectory("${EFFEKSEER_SOURCE_DIR}" "${CMAKE_BINARY_DIR}/effekseer" EXCLUDE_FROM_ALL)
if(MSVC)
    foreach(runtimeTarget Effekseer EffekseerRendererCommon EffekseerRendererDX11)
        target_compile_options(${runtimeTarget} PRIVATE /EHsc)
    endforeach()
endif()

add_library(Hypnix.EffekseerBridge SHARED HypnixEffekseerBridge.cpp)
set_property(TARGET Hypnix.EffekseerBridge PROPERTY
    MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
target_compile_definitions(Hypnix.EffekseerBridge PRIVATE HYPNIX_EFFEKSEER_EXPORTS WIN32_LEAN_AND_MEAN NOMINMAX)
if(MSVC)
    target_compile_options(Hypnix.EffekseerBridge PRIVATE /EHsc)
endif()
target_link_libraries(Hypnix.EffekseerBridge PRIVATE
    Effekseer EffekseerRendererCommon EffekseerRendererDX11 d3d11 dxgi d3dcompiler)
set_target_properties(Hypnix.EffekseerBridge PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY_RELEASE "${CMAKE_BINARY_DIR}/bin")
