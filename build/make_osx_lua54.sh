mkdir -p build_osx_54 && cd build_osx_54
cmake -DLUA_VERSION=5.4.7 -GXcode ../
cd ..
cmake --build build_osx_54 --config Release
cp build_osx_54/Release/xlua_il2cpp.bundle ../com.tencent.xlua/Plugins/xlua_il2cpp.bundle