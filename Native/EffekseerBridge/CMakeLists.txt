cmake_minimum_required(VERSION 3.20)
project(HypnixEffekseerBridge LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

set(EFFEKSEER_ROOT "${CMAKE_CURRENT_LIST_DIR}/../../.research/Effekseer")
set(EFFEKSEER_BUILD "${EFFEKSEER_ROOT}/build-hypnix")

add_library(Hypnix.EffekseerBridge SHARED HypnixEffekseerBridge.cpp)
set_property(TARGET Hypnix.EffekseerBridge PROPERTY
    MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
target_compile_definitions(Hypnix.EffekseerBridge PRIVATE HYPNIX_EFFEKSEER_EXPORTS WIN32_LEAN_AND_MEAN NOMINMAX)
target_include_directories(Hypnix.EffekseerBridge PRIVATE
    "${EFFEKSEER_ROOT}/Dev/Cpp/Effekseer"
    "${EFFEKSEER_ROOT}/Dev/Cpp/EffekseerRendererCommon"
    "${EFFEKSEER_ROOT}/Dev/Cpp/EffekseerRendererDX11")

target_link_libraries(Hypnix.EffekseerBridge PRIVATE
    "${EFFEKSEER_BUILD}/Dev/Cpp/Effekseer/Release/Effekseer.lib"
    "${EFFEKSEER_BUILD}/Dev/Cpp/EffekseerRendererCommon/Release/EffekseerRendererCommon.lib"
    "${EFFEKSEER_BUILD}/Dev/Cpp/EffekseerRendererDX11/Release/EffekseerRendererDX11.lib"
    d3d11 dxgi d3dcompiler)

set_target_properties(Hypnix.EffekseerBridge PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY_RELEASE "${CMAKE_CURRENT_LIST_DIR}/../../NativeBin")
