cmake_minimum_required(VERSION 3.22.1)
project(silkbridge LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_library(node SHARED IMPORTED)
set_target_properties(node PROPERTIES IMPORTED_LOCATION ${NODE_DIR}/bin/${ANDROID_ABI}/libnode.so)

add_library(silkbridge SHARED native-lib.cpp)
target_include_directories(silkbridge PRIVATE ${NODE_DIR}/include/node)

find_library(log-lib log)
target_link_libraries(silkbridge node ${log-lib})
