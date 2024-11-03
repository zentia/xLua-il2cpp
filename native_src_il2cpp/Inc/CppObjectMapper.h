#pragma once

#include "lua.hpp"

#include <map>
#include <memory>

#include "LuaClassRegister.h"
#include "ObjectCacheNode.h"

typedef struct
{
    void* Ptr;
    const void* TypeId;
    bool NeedDelete;
} CppObject;

struct pesapi_callback_info__
{
    lua_State* L;
    int ArgStart;    // 0 or 1
    int RetNum;
};

typedef pesapi_callback_info__ PersistentObjectEnvInfo;

namespace xlua
{
class CppObjectMapper
{
public:
    void Initialize(lua_State* L);

    int LoadCppType(lua_State* L);

    bool IsInstanceOfCppObject(lua_State* L, const void* TypeId, int ObjectIndex);

    std::weak_ptr<int> GetLuaEnvLifeCycleTracker();

    int FindOrAddCppObject(lua_State* L, const void* TypeId, void* Ptr, bool PassByPointer);

    void UnBindCppObject(lua_State* L, LuaClassDefinition* ClassDefinition, void* Ptr);

    void BindCppObject(lua_State* L, LuaClassDefinition* ClassDefinition, void* Ptr, bool PassByPointer);

    void* GetPrivateData(lua_State* L, int index);

    void SetPrivateData(lua_State* L, int index, void* Ptr);

    void UnInitialize(lua_State* L);

    static CppObjectMapper* Get(lua_State* L);

private:
    std::map<void*, ObjectCacheNode> CDataCache;

    std::map<const void*, int> TypeIdToMetaMap;

    int PointerConstructor;

    std::map<void*, FinalizeFunc> CDataFinalizeMap;

    std::shared_ptr<int> Ref = std::make_shared<int>(0);

    int CacheRef = 0;
    ;

    int GetMetaRefOfClass(lua_State* L, const LuaClassDefinition* ClassDefinition);

    void BindCppObject(
        lua_State* L, LuaClassDefinition* ClassDefinition, void* Ptr, bool PassByPointer, ObjectCacheNode* CacheNodePtr);
};

}    // namespace xlua