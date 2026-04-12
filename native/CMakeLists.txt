cmake_minimum_required(VERSION 3.18)
project(adofai_native LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

add_library(adofai_native SHARED adofai_native.cpp)

if (MSVC)
  target_compile_options(adofai_native PRIVATE /O2 /GL)
else()
  target_compile_options(adofai_native PRIVATE -O3 -march=native)
endif()
