cmake_minimum_required(VERSION 3.21)

project(SourceLauncherNative C CXX)

if(WIN32)
	set(SN_CEF_PLATFORM windows64)
else()
	set(SN_CEF_PLATFORM linux64)
endif()
set(CEF_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/../Dependencies/CEF/154.0.34/${SN_CEF_PLATFORM}" CACHE PATH "CEF binary distribution root")
list(APPEND CMAKE_MODULE_PATH "${CEF_ROOT}/cmake")

find_package(CEF REQUIRED)

add_subdirectory(${CEF_LIBCEF_DLL_WRAPPER_PATH} libcef_dll_wrapper)

set(SN_SOURCES App.cpp App.h)

if(OS_WINDOWS)
	set(NETHOST_DIR "" CACHE PATH "Directory containing nethost.h, hostfxr.h and nethost.lib")
	if(NOT EXISTS "${NETHOST_DIR}/nethost.h")
		message(FATAL_ERROR "NETHOST_DIR must point at the .NET apphost pack native directory (got '${NETHOST_DIR}')")
	endif()

	add_library(Source.Launcher SHARED ${SN_SOURCES} MainWin.cpp)
	SET_LIBRARY_TARGET_PROPERTIES(Source.Launcher)
	add_dependencies(Source.Launcher libcef_dll_wrapper)

	ADD_LOGICAL_TARGET("libcef_lib" "${CEF_LIB_RELEASE}" "${CEF_LIB_RELEASE}")
	target_include_directories(Source.Launcher PRIVATE "${NETHOST_DIR}")
	target_link_libraries(Source.Launcher libcef_lib libcef_dll_wrapper ${CEF_STANDARD_LIBS} "${NETHOST_DIR}/nethost.lib" delayimp.lib)
	target_link_options(Source.Launcher PRIVATE "/DELAYLOAD:libcef.dll")
elseif(OS_LINUX)
	set(CEF_LIBCEF_SO "" CACHE FILEPATH "Path to libcef.so to link against")
	if(NOT EXISTS "${CEF_LIBCEF_SO}")
		message(FATAL_ERROR "CEF_LIBCEF_SO must point at libcef.so (got '${CEF_LIBCEF_SO}')")
	endif()

	add_executable(Source.Launcher.CEF ${SN_SOURCES} MainLinux.cpp)
	SET_EXECUTABLE_TARGET_PROPERTIES(Source.Launcher.CEF)
	add_dependencies(Source.Launcher.CEF libcef_dll_wrapper)

	ADD_LOGICAL_TARGET("libcef_lib" "${CEF_LIBCEF_SO}" "${CEF_LIBCEF_SO}")
	target_link_libraries(Source.Launcher.CEF libcef_lib libcef_dll_wrapper ${CEF_STANDARD_LIBS})
	set_target_properties(Source.Launcher.CEF PROPERTIES BUILD_RPATH "\$ORIGIN" INSTALL_RPATH "\$ORIGIN")
else()
	message(FATAL_ERROR "Source.Launcher.Native supports Windows and Linux only")
endif()
