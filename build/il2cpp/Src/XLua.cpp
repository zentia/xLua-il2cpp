#ifdef __cplusplus
extern "C"
{
#endif
#include "lua.h"
#include "lualib.h"
#include "lauxlib.h"
#ifdef __cplusplus
}
#endif
#include <string.h>
#include <stdint.h>
#include "i64lib.h"

#if USING_LUAJIT
#include "lj_obj.h"
#else
#include "lstate.h"
#endif

#include <string>
#include <vector>
#include <thread>
#include <mutex>

#include "XLua.h"
#include "LuaClassRegister.h"
#include <CppObjectMapper.h>

#include <chrono>
using namespace std::chrono;

#define GetObjectData(Value, Type) ((Type*) (((uint8_t*) Value) + GUnityExports.SizeOfRuntimeObject))

struct PersistentObjectInfo
{
    PersistentObjectInfo* EnvInfo;
    void* LuaObject;
    std::weak_ptr<int> LuaEnvLifeCycleTracker;
};
namespace xlua
{
typedef void (*LogCallback)(const char* value);

static LogCallback GLogCallback = nullptr;

void Log(const std::string Fmt, ...)
{
    static char SLogBuffer[1024];
    va_list list;
    va_start(list, Fmt);
    vsnprintf(SLogBuffer, sizeof(SLogBuffer), Fmt.c_str(), list);
    va_end(list);

    if (GLogCallback)
    {
        GLogCallback(SLogBuffer);
    }
}

struct LuaEnv
{
    LuaEnv()
    {
        L = luaL_newstate();
        luaopen_xlua(L);
        CppObjectMapper.Initialize(L);
    }

    ~LuaEnv()
    {
        CppObjectMapper.UnInitialize(L);
        lua_close(L);
        L = nullptr;
    }

    lua_State* L;

    xlua::CppObjectMapper CppObjectMapper;
};
}    // namespace xlua

extern pesapi_func_ptr reg_apis[];

#ifdef __cplusplus
extern "C"
{
#endif

    LUA_API xlua::LuaEnv* CreateNativeLuaEnv()
    {
        return new xlua::LuaEnv();
    }

    LUA_API lua_State* GetLuaState(xlua::LuaEnv* luaEnv)
    {
        return luaEnv->L;
    }

    LUA_API void DestroyNativeLuaEnv(xlua::LuaEnv* luaEnv)
    {
        delete luaEnv;
    }

    LUA_API void SetLogCallback(xlua::LogCallback Log)
    {
        xlua::GLogCallback = Log;
    }

    LUA_API pesapi_env_ref GetPapiEnvRef(xlua::LuaEnv* luaEnv)
    {
        lua_State* L = luaEnv->L;

        auto env = reinterpret_cast<pesapi_env>(L);
        return g_pesapi_ffi.create_env_ref(env);
    }

    LUA_API pesapi_ffi* GetFFIApi()
    {
        return &g_pesapi_ffi;
    }

    LUA_API pesapi_func_ptr* GetRegisterApi()
    {
        return reg_apis;
    }
#ifdef __cplusplus
}
#endif
