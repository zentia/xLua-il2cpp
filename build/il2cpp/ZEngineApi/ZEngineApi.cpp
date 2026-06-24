
#define LUA_LIB
#include "ZEngineApi.h"
#include "ltracker.h"

extern "C" {
OSG_API void GameCore_ZEngineAPI_Init(ZEngine::Api* api)
{
    ZEngine::g_GameCore_ZEngineApi = api;
#if LUA_MEM_PROFILER
    luaT_register_profile(api->AddLuaData, api->RemoveLuaData, api->ResizeLuaData);
#endif
}
}
