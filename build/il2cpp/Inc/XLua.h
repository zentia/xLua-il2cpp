#pragma once

#include <cassert>
#include <string>

#include "CppObjectMapper.h"
#include "lua.hpp"

struct FunctionHashFunctor
{
    inline size_t operator()(lua_CFunction x) const
    {
        return (size_t)x;
    }
};

extern "C"
{
    OSG_API void xlua_clear_mirrored_coroutines();
    OSG_API const char* xlua_capture_mirrored_traceback(lua_State* L);
}

namespace xlua
{
    extern std::string g_snapshot;
    struct LuaEnv

    {
        LuaEnv();

        ~LuaEnv();

        void GetTraceback(lua_State* L)
        {
            CppObjectMapper.Traceback(L);
        }

        bool IsLuaThread(uint64 threadID)
        {
            osgame_log->error(osgame_log->cat.GameCore, "IsLuaThread:{} {}", threadID, m_ThreadID);
            return threadID == m_ThreadID;
        }

        lua_State* L;

        xlua::CppObjectMapper CppObjectMapper;
        uint64 m_ThreadID;

        static LuaEnv* ms_Instance;
        static int ms_AuthCode;
    };
} // namespace xlua
