cmake_minimum_required(VERSION 3.22)
project(galnet_miniaudio LANGUAGES C)

# Shared libraries for desktop/Android; a static archive for an iOS NativeReference.
option(GALNET_MINIAUDIO_STATIC "Build for static linking (including iOS)" OFF)
if(CMAKE_SYSTEM_NAME STREQUAL "iOS" AND NOT GALNET_MINIAUDIO_STATIC)
    message(FATAL_ERROR "iOS requires -DGALNET_MINIAUDIO_STATIC=ON and a host NativeReference")
endif()
if(GALNET_MINIAUDIO_STATIC)
    add_library(galnet_miniaudio STATIC galnet_miniaudio.c)
else()
    add_library(galnet_miniaudio SHARED galnet_miniaudio.c)
endif()
set_target_properties(galnet_miniaudio PROPERTIES
    C_STANDARD 99
    C_STANDARD_REQUIRED YES
    C_VISIBILITY_PRESET hidden
    POSITION_INDEPENDENT_CODE YES)

# A missing real output must fail, rather than silently selecting the null device.
target_compile_definitions(galnet_miniaudio PRIVATE MA_NO_NULL)
if(MSVC)
    set_property(TARGET galnet_miniaudio PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
elseif(APPLE)
    target_link_libraries(galnet_miniaudio PRIVATE "-framework CoreFoundation" "-framework CoreAudio" "-framework AudioToolbox")
elseif(ANDROID)
    target_link_options(galnet_miniaudio PRIVATE "-Wl,-z,max-page-size=16384")
    target_link_libraries(galnet_miniaudio PRIVATE log android OpenSLES m ${CMAKE_DL_LIBS})
elseif(UNIX)
    find_package(Threads REQUIRED)
    target_link_libraries(galnet_miniaudio PRIVATE Threads::Threads m ${CMAKE_DL_LIBS})
endif()

install(TARGETS galnet_miniaudio
    RUNTIME DESTINATION .
    LIBRARY DESTINATION .
    ARCHIVE DESTINATION .)
