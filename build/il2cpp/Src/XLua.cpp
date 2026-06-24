#include <stdint.h>
#include "lua.hpp"

#include <string>
#include "ldebug.h"
#include "lstate.h"

#include <CppObjectMapper.h>
#include "LuaClassRegister.h"
#include "XLua.h"
#include "ltracker.h"

#include <assert.h>

#if SGAME_PLATFORM_ANDROID || SGAME_PLATFORM_OPENHARMONY || SGAME_PLATFORM_LINUX
    #include <sys/syscall.h>
    #include <unistd.h>
#elif SGAME_PLATFORM_APPLE
    #include <pthread.h>
    #include <mach/mach.h>
#elif SGAME_PLATFORM_WINDOWS
    #include <Windows.h>
#endif

#define GetObjectData(Value, Type) ((Type*)(((uint8_t*)Value) + GUnityExports.SizeOfRuntimeObject))

extern "C" int luaopen_xlua(lua_State* L);

struct PersistentObjectInfo
{
    PersistentObjectInfo* EnvInfo;
    void* LuaObject;
    std::weak_ptr<int> LuaEnvLifeCycleTracker;
};

namespace xlua
{
    namespace
    {
        uint64 GetNativeThreadID()
        {
#if SGAME_PLATFORM_ANDROID || SGAME_PLATFORM_OPENHARMONY || SGAME_PLATFORM_LINUX
            return static_cast<uint64>(syscall(SYS_gettid));
#elif SGAME_PLATFORM_APPLE
            const pthread_t currentThread = pthread_self();
            const mach_port_t machThread = pthread_mach_thread_np(currentThread);
            return static_cast<uint64>(machThread);
#elif SGAME_PLATFORM_WINDOWS
            return static_cast<uint64>(::GetCurrentThreadId());
#else
            return 0;
#endif
        }
    }

    std::string g_snapshot;

    struct DummyIl2CppObject
    {
        void* klass;
        void* monitor;
    };

    namespace
    {
        int panic(lua_State* L)
        {
            const char* msg = lua_tostring(L, -1);
            if (!msg)
                msg = "unknown error";
            osgame_log->error(osgame_log->cat.Lua, msg);
            return 0;
        }
    } // namespace

    LuaEnv* LuaEnv::ms_Instance = nullptr;
    int LuaEnv::ms_AuthCode = 0;

    LuaEnv::LuaEnv()
    {
        m_ThreadID = GetNativeThreadID();
        ms_Instance = this;
        ms_AuthCode++;
        L = luaL_newstate();
        luaopen_xlua(L);

        lua_atpanic(L, panic);
        CppObjectMapper.Initialize(L);
    }

    LuaEnv::~LuaEnv()
    {
        CppObjectMapper.UnInitialize(L);
        xlua_clear_mirrored_coroutines();
        lua_close(L);
        L = nullptr;
        ms_Instance = nullptr;
    }

} // namespace xlua

extern pesapi_func_ptr reg_apis[];


#ifdef __cplusplus
extern "C" {
#endif
PESAPI_MODULE_EXPORT xlua::LuaEnv* CreateNativeLuaEnv()
{
    return new xlua::LuaEnv();
}

PESAPI_MODULE_EXPORT lua_State* GetLuaState(xlua::LuaEnv* luaEnv)
{
    return luaEnv->L;
}

PESAPI_MODULE_EXPORT void DestroyNativeLuaEnv(xlua::LuaEnv* luaEnv)
{
    delete luaEnv;
}

PESAPI_MODULE_EXPORT pesapi_env_ref GetPapiEnvRef(xlua::LuaEnv* luaEnv)
{
    lua_State* L = luaEnv->L;

    auto env = reinterpret_cast<pesapi_env>(L);
    return g_pesapi_ffi.create_env_ref(env);
}

PESAPI_MODULE_EXPORT pesapi_func_ptr* GetRegisterApi()
{
    return reg_apis;
}

PESAPI_MODULE_EXPORT pesapi_ffi* GetFFIApi()
{
    return &g_pesapi_ffi;
}

PESAPI_MODULE_EXPORT const char* GetLuaDebugSnapshot(uint64 crashThreadId)
{
    auto&& env = xlua::LuaEnv::ms_Instance;
    if (env == nullptr)
    {
        return "";
    }
    auto&& L = env->L;
    if (L == nullptr)
        return "";
    if (!env->IsLuaThread(crashThreadId))
        return "";
    return xlua_capture_mirrored_traceback(L);
}

PESAPI_MODULE_EXPORT void LuaCrash()
{
    int* ptr = nullptr;
    (*(ptr + 1)) = 0;
}

PESAPI_MODULE_EXPORT void OnUnityObjectDestroyByLua(lua_State* L, void* ptr, const char* name)
{
    xlua::CppObjectMapper* mapper = xlua::CppObjectMapper::Get();
    mapper->OnUnityObjectDestroy(L, (ptr ? (xlua::DummyIl2CppObject*)ptr - 1 : nullptr), name);
}
#ifdef __cplusplus
}
#endif
