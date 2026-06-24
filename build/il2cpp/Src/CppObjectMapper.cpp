#include "CppObjectMapper.h"
#include <memory>
#include "DataTransfer.h"
#include "XLua.h"
#include "logger/osgame_log.h"
#include "lua.hpp"
#include "pesapi.h"

#if OSG_PROFILE
    #include "cmlua/cmlua.h"
#endif

#if LUA_MEM_PROFILER
    #include "il2cpp/ZEngineApi/ZEngineApi.h"
    #include "ltracker.h"
#endif

int pesapi_dostring(pesapi_env env, const uint8_t* code, size_t code_size, const char* path, pesapi_value_ref value_ref);
EXTERN_C_START
int pesapi_create_function(pesapi_env env, pesapi_callback native_impl, void* data, pesapi_function_finalize finalize);
int error_func(lua_State* L);
EXTERN_C_END

namespace xlua
{
    std::string Printf(const char* format, ...)
    {
        va_list argsToCheckSize;
        int n;
        std::string ret;

        va_start(argsToCheckSize, format);
#if SGAME_PLATFORM_WINDOWS
        // MS vsnprintf always returns -1 if string doesn't fit, rather than
        // the needed size. Used their 'special' function instead to get required size
        n = _vscprintf_p(format, argsToCheckSize);
#else
        // use a temporary buffer as some docs indicate we cannot pass NULL to vsnprintf
        char buf[1];
        n = vsnprintf(buf, 0, format, argsToCheckSize);
#endif
        if (n == -1)
            return NULL;

        ret.resize(n + 1, 0);
        va_end(argsToCheckSize);

        va_list argsToFormat;

        va_start(argsToFormat, format);
        n = vsnprintf(&ret[0], ret.size(), format, argsToFormat);
        va_end(argsToFormat);

        assert(n < (int)ret.size());

        if (n == -1)
            return NULL;

        // The v*printf methods might put a trailing NUL character, which should not not be in a
        // std::string, so strip it out.
        if (!ret.empty() && ret[ret.size() - 1] == '\0')
            ret = ret.substr(0, ret.size() - 1);

        return ret;
    }

    static int dummy_idx_tag = 0;

#if OSG_PROFILE
    int CppObjectMapper::PrefPropertyGetterIndex = 0x100;
    int CppObjectMapper::PrefPropertySetterIndex = 0x101;
    int CppObjectMapper::PrefFieldGetterIndex = 0x102;
    int CppObjectMapper::PrefFieldSetterIndex = 0x103;
    int CppObjectMapper::PrefMethodIndex = 0x104;
    int CppObjectMapper::PrefFunctionIndex = 0x105;
    int CppObjectMapper::PrefNewIndex = 0x106;
    int CppObjectMapper::PrefGCIndex = 0x107;
#endif

    CppObjectMapper::CppObjectMapper()
        : m_Disposed(true)
    {
        supportOp = {
            {"op_Addition", "__add"},
            {"op_Subtraction", "__sub"},
            {"op_Multiply", "__mul"},
            {"op_Division", "__div"},
            {"op_Equality", "__eq"},
            {"op_UnaryNegation", "__unm"},
            {"op_LessThan", "__lt"},
            {"op_LessThanOrEqual", "__le"},
            {"op_Modulus", "__mod"},
            {"op_BitwiseAnd", "__band"},
            {"op_BitwiseOr", "__bor"},
            {"op_ExclusiveOr", "__bxor"},
            {"op_OnesComplement", "__bnot"},
            {"op_LeftShift", "__shl"},
            {"op_RightShift", "__shr"},
        };
    }

    LUAENV_API CppObjectMapper* CppObjectMapper::Get()
    {
        if (xlua::LuaEnv::ms_Instance == nullptr)
            return nullptr;
        return &xlua::LuaEnv::ms_Instance->CppObjectMapper;
    }

    int CppObjectMapper::LoadTypeById(lua_State* L, const void* typeId)
    {
        if (const auto classDef = xlua::LoadClassByID(typeId))
        {
            lua_createtable(L, 0, 0);
            const MetaInfo* metaInfo = GetMetaRefOfClass(L, classDef);
            lua_rawgeti(L, LUA_REGISTRYINDEX, metaInfo->ref);
            lua_pushlightuserdata(L, &dummy_idx_tag);
            lua_rawget(L, -2);
            lua_remove(L, -2);
            if (!lua_isnil(L, -1))
            {
                lua_setmetatable(L, -2);
                return lua_gettop(L);
            }
            return luaL_error(L, "type meta not find");
        }
        return luaL_error(L, "no such type");
    }

    void CppObjectMapper::Initialize(lua_State* L)
    {
        lua_getglobal(L, "debug");
        // debug
        lua_getfield(L, -1, "traceback");
        // debug,traceback
        lua_remove(L, -2);
        // traceback
        m_Traceback = luaL_ref(L, LUA_REGISTRYINDEX);

        lua_newtable(L);
        lua_newtable(L);
        lua_pushstring(L, "__mode");
        lua_pushstring(L, "v");
        lua_rawset(L, -3);
        lua_setmetatable(L, -2);
        m_CacheRef = luaL_ref(L, LUA_REGISTRYINDEX);

        lua_newtable(L);
        m_CachePrivateDataRef = luaL_ref(L, LUA_REGISTRYINDEX);
        m_TypeIdToMetaMap.set_empty_key((const void*)0);
        m_TypeIdToMetaMap.set_deleted_key((const void*)-1);
        m_DataCache.set_empty_key((void*)0);
        m_DataCache.set_deleted_key((void*)-1);

        const char* dictionary = R"(return function(obj)
        local function lua_iter(cs_iter, k)
            if cs_iter:MoveNext() then
                local current = cs_iter.Current
                return current.Key, current.Value
            end
        end
        return lua_iter, obj:GetEnumerator(), -1
    end)";
        size_t size = strlen(dictionary);
        int oldTop = lua_gettop(L);
        int index = pesapi_dostring(reinterpret_cast<pesapi_env>(L), reinterpret_cast<const uint8_t*>(dictionary), size, "dictionary", nullptr);
        lua_pushvalue(L, index);
        m_IDictionary = luaL_ref(L, LUA_REGISTRYINDEX);
        lua_settop(L, oldTop);

        const char* enumerable = R"(return function(obj)
        local function lua_iter(cs_iter, k)
            if cs_iter:MoveNext() then
                return k + 1, cs_iter.Current
            end
        end
        return lua_iter, obj:GetEnumerator(), -1
    end)";
        size = strlen(enumerable);
        oldTop = lua_gettop(L);
        index = pesapi_dostring(reinterpret_cast<pesapi_env>(L), reinterpret_cast<const uint8_t*>(enumerable), size, "enumerable", nullptr);
        lua_pushvalue(L, index);
        m_Enumerable = luaL_ref(L, LUA_REGISTRYINDEX);
        lua_settop(L, oldTop);
        m_Disposed = false;
        m_FixPtrError = bq::ccs::get_settings_value_bool("XLUA_FixPtrError", true);
    }

    void CppObjectMapper::Traceback(lua_State* L)
    {
        lua_rawgeti(L, LUA_REGISTRYINDEX, m_Traceback);
    }

    struct GameCoreCSharpMethodInfo
    {
        const char* Name;
        bool IsStatic;
        bool IsGetter;
        bool IsSetter;
        bool NeedBoxing;
        std::vector<void*> OverloadDatas;
    };

    static int PesapiFunctionCallback(lua_State* L)
    {
        PesapiCallbackData* FunctionInfo = reinterpret_cast<PesapiCallbackData*>(lua_touserdata(L, lua_upvalueindex(1)));
        pesapi_callback_info__ info{L, 0, 0};
        GameCoreCSharpMethodInfo* method = static_cast<GameCoreCSharpMethodInfo*>(FunctionInfo->Data);
        if (method && !method->IsStatic)
        {
            info.ArgStart = 1;
        }
        FunctionInfo->Callback(&g_pesapi_ffi, &info);
        return 1;
    }

    static int PesapiFunctionGC(lua_State* L)
    {
        PesapiCallbackData* FunctionInfo = reinterpret_cast<PesapiCallbackData*>(lua_touserdata(L, lua_upvalueindex(1)));
        CppObjectMapper::CallbackDataGarbageCollected(FunctionInfo);
        return 0;
    }

    void CppObjectMapper::CallbackDataGarbageCollected(PesapiCallbackData* CallbackData)
    {
        if (CallbackData->Finalize)
        {
            CallbackData->Finalize(&g_pesapi_ffi, CallbackData->Data, GameCoreDataTransfer::GetLuaEnvPrivate());
        }
        CppObjectMapper* instance = Get();
        if (instance != nullptr)
        {
            for (auto it = instance->m_FunctionDatas.begin(); it != instance->m_FunctionDatas.end();)
            {
                if (*it == CallbackData)
                {
                    it = instance->m_FunctionDatas.erase(it);
                }
                else
                {
                    ++it;
                }
            }
        }
    }

    int CppObjectMapper::CreateFunction(lua_State* L, pesapi_callback Callback, void* Data, pesapi_function_finalize Finalize)
    {
        const auto CallbackData = (PesapiCallbackData*)lua_newuserdata(L, sizeof(PesapiCallbackData));
        lua_newtable(L);
        lua_pushlightuserdata(L, CallbackData);
        lua_pushcclosure(L, PesapiFunctionGC, 1);
        lua_setfield(L, -2, "__gc");
        lua_setmetatable(L, -2);
        CallbackData->Callback = Callback;
        CallbackData->Data = Data;
        CallbackData->Finalize = Finalize;
        m_FunctionDatas.push_back(CallbackData);

        lua_pushlightuserdata(L, Data);
        lua_pushcclosure(L, PesapiFunctionCallback, 2);
        return lua_gettop(L);
    }

    bool CppObjectMapper::IsInstanceOfCppObject(lua_State* L, const void* TypeId, int ObjectIndex)
    {
        CppObject* cppObject = (CppObject*)lua_touserdata(L, ObjectIndex);
        return cppObject && cppObject->TypeId == TypeId;
    }

    std::weak_ptr<int> CppObjectMapper::GetLuaEnvLifeCycleTracker()
    {
        return std::weak_ptr<int>(Ref);
    }

    int CppObjectMapper::FindOrAddCppObject(lua_State* L, const void* typeId, void* ptr, bool passByPointer)
    {
        if (typeId == nullptr)
        {
            osgame_log->error(osgame_log->cat.Lua, "typeId is null!");
            lua_pushnil(L);
            return lua_gettop(L);
        }
        if (ptr == nullptr)
        {
            lua_pushnil(L);
            return lua_gettop(L);
        }

        if (passByPointer)
        {
            const auto iterator = m_DataCache.find(ptr);
            if (iterator != m_DataCache.end())
            {
                xlua::ObjectCacheNode* head = iterator->second;
                if (xlua::ObjectCacheNode* node = head->Find(typeId))
                {
                    // printf("find in cache:%p\n", Ptr);
                    // 这里由于是弱引用，所以值不一定会存在，但是由于GC函数不是立即执行，也就是说这里有可能拿错对象
                    lua_rawgeti(L, LUA_REGISTRYINDEX, m_CacheRef);
                    if (lua_rawgeti(L, -1, node->Value) != LUA_TNIL)
                    {
                        // 校验一下这个对象对不对
                        CppObject* o = static_cast<CppObject*>(lua_touserdata(L, -1));
                        if (o->Ptr == ptr && o->TypeId == typeId)
                        {
                            lua_remove(L, -2);
                            return lua_gettop(L);
                        }
                    }
                    lua_pop(L, 2);
                }
            }
        }
        const LuaClassDefinition* classDefinition = LoadClassByID(typeId);
        if (classDefinition == nullptr)
        {
            osgame_log->error(osgame_log->cat.Lua, "LoadClassByID error");
            lua_pushnil(L);
            return lua_gettop(L);
        }
        BindCppObject(L, classDefinition, ptr, passByPointer);
        return lua_gettop(L);
    }

    void CppObjectMapper::BindCppObject(lua_State* L, const LuaClassDefinition* classDefinition, void* ptr, bool passByPointer)
    {
        CppObject* obj = static_cast<CppObject*>(lua_newuserdata(L, sizeof(CppObject)));
#if LUA_MEM_PROFILER
        ZEngine::GetApi()->AddUserData(obj, luaT_print_stack(L));
#endif
        obj->Ptr = ptr;
        obj->TypeId = classDefinition->TypeId;
        obj->ScriptName = classDefinition->ScriptName;
        obj->NeedDelete = !passByPointer;
        const MetaInfo* metaInfo = GetMetaRefOfClass(L, classDefinition);
        lua_rawgeti(L, LUA_REGISTRYINDEX, metaInfo->ref);
        if (lua_isnil(L, -1))
        {
            osgame_log->error(osgame_log->cat.Lua, "BindCppObject error!");
        }
        lua_setmetatable(L, -2);

        lua_rawgeti(L, LUA_REGISTRYINDEX, m_CacheRef);
        lua_pushvalue(L, -2);
        int ref = luaL_ref(L, -2);

        lua_pop(L, 1);
        auto iterator = m_DataCache.find(ptr);
        ObjectCacheNode* cacheNodePtr;
        if (iterator != m_DataCache.end())
        {
            cacheNodePtr = iterator->second->Add(classDefinition->TypeId);
        }
        else
        {
            void* userdata = nullptr;
            if (!m_FixPtrError)
            {
                if (classDefinition->OnEnter)
                {
                    userdata = classDefinition->OnEnter(ptr, classDefinition->Data, GameCoreDataTransfer::GetLuaEnvPrivate());
#if LUA_MEM_PROFILER
                    ZEngine::GetApi()->AddCSharpData((uint64_t)userdata, classDefinition->ScriptName, luaT_print_stack(L));
#endif // LUA_MEM_PROFILER
                }
            }
            auto ret = m_DataCache.insert({ptr, new ObjectCacheNode(classDefinition->TypeId, userdata, ptr)});
            cacheNodePtr = ret.first->second;
        }
        cacheNodePtr->Value = ref;
        if (m_FixPtrError)
        {
            if (classDefinition->OnEnter)
            {
                cacheNodePtr->UserData = classDefinition->OnEnter(ptr, classDefinition->Data, GameCoreDataTransfer::GetLuaEnvPrivate());
#if LUA_MEM_PROFILER
                ZEngine::GetApi()->AddCSharpData((uint64_t)cacheNodePtr->UserData, classDefinition->ScriptName, luaT_print_stack(L));
#endif // LUA_MEM_PROFILER
            }
        }
    }

    void CppObjectMapper::UnBindCppObject(const LuaClassDefinition* classDefinition, void* ptr)
    {
        auto iterator = m_DataCache.find(ptr);
        if (iterator != m_DataCache.end())
        {
            ObjectCacheNode* node = iterator->second;
            if (ptr == node->Ptr)
                RemoveCacheNode(node, classDefinition);
            else
                osgame_log->fatal(osgame_log->cat.Lua, "UnBindCppObject failed!");
        }
    }

    void CppObjectMapper::RemoveCacheNode(ObjectCacheNode* node, const LuaClassDefinition* classDefinition)
    {
        // Guard against null node pointer
        if (node == nullptr)
        {
            return;
        }

        // Save node info before any operations that might invalidate the pointer
        void* nodePtr = node->Ptr;

        // Check if this node has already been removed by checking if it still exists in the cache
        // This prevents double-removal crashes and validates that the node is still valid
        auto cacheIt = m_DataCache.find(nodePtr);
        if (cacheIt == m_DataCache.end())
        {
            // Node has already been removed from cache, skip removal
            // This can happen if RemoveCacheNode was called twice (e.g., from FindOrAddCppObject and BindCppObject)
            return;
        }

        // Now that we know the node exists in cache, it's safe to access its members
        ObjectCacheNode* head = node->Head;

        // Verify that the cached head matches our head pointer
        // This ensures we're operating on a valid, consistent node
        if (cacheIt->second != head)
        {
            // The cache entry has been modified - this shouldn't happen in normal flow
            // but can occur if there's a race condition or logic error
            return;
        }

        int oldValue = node->Value;
        void* userdata = node->UserData;
        bool nodeIsHead = (node == head);

        // Check if the node has already been removed (head node with TypeId == nullptr means it was removed)
        if (nodeIsHead && head->TypeId == nullptr)
        {
            // Node has already been removed, just clean up the cache entry and delete head
            m_DataCache.erase(nodePtr);
            delete head;
            return;
        }

        // Remove the node from the linked list
        // Note: After Remove(), the behavior depends on whether node is head and whether head has Next:
        //   1. If node is head and has Next: Next is moved to head, Next is deleted, head pointer remains valid
        //   2. If node is head and no Next: head->TypeId is set to nullptr, head pointer remains but is invalid
        //   3. If node is not head: node is deleted, head pointer remains valid
        head->Remove(classDefinition->TypeId, true);

        // Call OnExit callback before potentially deleting the node
        if (classDefinition->OnExit)
        {
            classDefinition->OnExit(nodePtr, classDefinition->Data, GameCoreDataTransfer::GetLuaEnvPrivate(), userdata);
        }

        // After Remove(), determine if we need to delete head and remove from cache:
        //   - If node was head and head->TypeId is nullptr: head was removed and had no Next, delete head and remove from cache
        //   - If node was head and head->TypeId is not nullptr: head was replaced by move (Next moved to head), keep head in cache
        //   - If node was not head: node was deleted, head still exists, keep head in cache
        if (nodeIsHead && !head->TypeId)
        {
            // Head node was removed and had no Next - delete the head node and remove from cache
            m_DataCache.erase(nodePtr);
            delete head;
        }
        // Otherwise, head still exists (either wasn't removed or was replaced by move), so keep it in cache
        // If node was not the head, it was already deleted by Remove(), so we don't need to delete it
    }

    void CppObjectMapper::OnUnityObjectDestroy(lua_State* L, void* ptr, const char* name)
    {
        if (L == nullptr || ptr == nullptr)
            return;
        auto iterator = m_DataCache.find(ptr);
        if (iterator == m_DataCache.end())
        {
            // osgame_log->warning(osgame_log->cat.Lua, Printf("OnUnityObjectDestroy failed %p", ptr).c_str());
            return;
        }
        lua_rawgeti(L, LUA_REGISTRYINDEX, m_CacheRef);
        ReleaseNode(L, iterator->second, ptr, name);
        m_DataCache.erase(ptr);
        lua_pop(L, 1);
        // osgame_log->warning(osgame_log->cat.Lua, Printf("OnUnityObjectDestroy success %p", ptr).c_str());
    }

    void* CppObjectMapper::GetPrivateData(lua_State* L, int index) const
    {
        index = lua_absindex(L, index);
        lua_rawgeti(L, LUA_REGISTRYINDEX, m_CachePrivateDataRef);
        lua_pushvalue(L, index);
        lua_rawget(L, -2);
        if (lua_isnil(L, -1))
        {
            lua_pop(L, 2);
            return nullptr;
        }
        void* data = lua_touserdata(L, -1);
        lua_pop(L, 2);
        return data;
    }

    bool CppObjectMapper::SetPrivateData(lua_State* L, int index, void* ptr, const char* func_name)
    {
        lua_rawgeti(L, LUA_REGISTRYINDEX, m_CachePrivateDataRef);
        // table index is nil, crash
        lua_rawgeti(L, LUA_REGISTRYINDEX, index);
        if (lua_isnil(L, -1))
        {
            osgame_log->error(osgame_log->cat.Lua, "SetPrivateData error, index is {}! invoke by {}", index, func_name);
            lua_pop(L, 2);
            return false;
        }
        if (ptr == nullptr)
        {
            lua_pushnil(L);
        }
        else
        {
            lua_pushlightuserdata(L, ptr);
        }
        lua_rawset(L, -3);
        lua_pop(L, 1);
        return true;
    }

    void CppObjectMapper::UnInitialize(lua_State* L)
    {
        lua_rawgeti(L, LUA_REGISTRYINDEX, m_CacheRef);
        for (auto& KV : m_DataCache)
        {
            ReleaseNode(L, KV.second, KV.first, "");
        }
        m_FunctionDatas.clear();
        m_DataCache.clear();
        for (const auto& pairs : m_TypeIdToMetaMap)
        {
            delete pairs.second;
        }
        m_TypeIdToMetaMap.clear();
        luaL_unref(L, LUA_REGISTRYINDEX, m_Traceback);
        luaL_unref(L, LUA_REGISTRYINDEX, m_CacheRef);
        luaL_unref(L, LUA_REGISTRYINDEX, m_CachePrivateDataRef);
        luaL_unref(L, LUA_REGISTRYINDEX, m_IDictionary);
        luaL_unref(L, LUA_REGISTRYINDEX, m_Enumerable);
        m_Disposed = true;
    }

    void CppObjectMapper::ReleaseNode(lua_State* L, ObjectCacheNode* PNode, void* ptr, const char* name)
    {
        const auto PData = GameCoreDataTransfer::GetLuaEnvPrivate();
        bool need_delete = true;
        bool exit = false;
        while (PNode)
        {
            if (need_delete)
            {
                lua_rawgeti(L, -1, PNode->Value);
                // lua_assert是私有宏，只有在lua中生效，GameCore中不生效
                lua_assert(lua_isuserdata(L, -1));
                const auto obj = (CppObject*)lua_touserdata(L, -1);
                // RAX为0，地址真的是0，也就是说unbind的时候，userdata删掉了，但是注册表索引还在，有可能逻辑有问题，有可能GC的时候，没有及时同步过来，这里先做一下保护
                if (obj != nullptr)
                {
                    auto&& typeId = PNode->TypeId;
                    auto objNeedDelete = obj->NeedDelete;
                    if (obj->TypeId != PNode->TypeId)
                    {
                        osgame_log->error(osgame_log->cat.Lua, "Error ptr! name is {}", name);
                        objNeedDelete = false;
                    }
                    const LuaClassDefinition* ClassDefinition = FindClassByID(typeId);
                    // 值类型需要删除，所以这里必须要执行，但是执行之后，指针会被删除，所以下面的就不要执行了，虚拟机会销毁，所以lua不会内存泄露，LuaEnv也会销毁，所以C#也不会内存泄露
                    // 因为值类型的内存是手动申请的，所以必须要手动释放
                    if (ClassDefinition && ClassDefinition->Finalize && need_delete && objNeedDelete)
                    {
                        ClassDefinition->Finalize(&g_pesapi_ffi, ptr, ClassDefinition->Data, PData);
                        need_delete = false;
                    }
                    if (ClassDefinition && ClassDefinition->OnExit && !exit && PData)
                    {
                        ClassDefinition->OnExit(ptr, ClassDefinition->Data, PData, PNode->UserData);
                        exit = true;
                    }
                    if (obj->Ptr != ptr)
                        osgame_log->error(osgame_log->cat.Lua, "Error ptr! class name is {}.", ClassDefinition->ScriptName);
                    else
                    {
                        obj->Ptr = nullptr;
                    }
                }
                lua_pop(L, 1);
            }
            ObjectCacheNode* temp = PNode;
            PNode = PNode->Next;
            // 存在重复释放的可能
            delete temp;
        }
    }

    // param   --- [1]: obj, [2]: key
    static int obj_internal_indexer(lua_State* L, bool is_indexer)
    {
        // method
        if (!lua_isnil(L, lua_upvalueindex(1)))
        {
            if (is_indexer)
            {
                lua_gettable(L, lua_upvalueindex(1));
                if (!lua_isnil(L, -1))
                {
                    // has method
                    lua_pushvalue(L, 1);
                    lua_pushvalue(L, 2);
                    lua_call(L, 2, 1);
                    return 1;
                }
                lua_pop(L, 1);
            }
            else
            {
                lua_pushvalue(L, 2);
                lua_gettable(L, lua_upvalueindex(1));
                if (!lua_isnil(L, -1))
                { // has method
                    return 1;
                }
                lua_pop(L, 1);
            }
        }
        // getter
        if (!is_indexer)
        {
            if (!lua_isnil(L, lua_upvalueindex(2)))
            {
                lua_pushvalue(L, 2);
                lua_gettable(L, lua_upvalueindex(2));
                if (!lua_isnil(L, -1))
                { // has getter
                    lua_pushvalue(L, 1);
                    lua_call(L, 1, 1);
                    return 1;
                }
                lua_pop(L, 1);
            }
        }
        // parent
        if (!lua_isnil(L, lua_upvalueindex(3)))
        {
            lua_settop(L, 2);
            lua_pushvalue(L, lua_upvalueindex(3));
            lua_insert(L, 1);
            lua_call(L, 2, 1);
            return 1;
        }
        return 0;
    }

    namespace
    {
        // param   --- [1]: obj, [2]: key
        int object_indexer(lua_State* L)
        {
            int t = lua_type(L, 2);
            if (t == LUA_TSTRING)
            {
                return obj_internal_indexer(L, false);
            }
            else if (t == LUA_TNUMBER)
            {
                lua_pushstring(L, "get_Item");
                return obj_internal_indexer(L, true);
            }
            else
            {
                return 0;
            }
        }

        // upvalue --- [1]:methods, [2]:setters, [3]:base_newindex
        // param   --- [1]: obj, [2]: key, [3]: value [4]: string
        int obj_internal_new_indexer(lua_State* L, bool is_indexer)
        {
            if (is_indexer)
            {
                lua_gettable(L, lua_upvalueindex(1));
                //[1]: obj, [2]: key, [3]: value [4]: string [5]: set_Item
                // set_Item(1,2,3)
                if (!lua_isnil(L, -1))
                {
                    // has method
                    lua_pushvalue(L, 1);
                    lua_pushvalue(L, 2);
                    lua_pushvalue(L, 3);
                    lua_call(L, 3, 0);
                    return 0;
                }
                lua_pop(L, 1);
            }
            else
            {
                // setter
                if (!lua_isnil(L, lua_upvalueindex(2)))
                {
                    if (!is_indexer)
                        lua_pushvalue(L, 2);
                    lua_gettable(L, lua_upvalueindex(2));
                    if (!lua_isnil(L, -1))
                    { // has setter
                        lua_pushvalue(L, 1);
                        lua_pushvalue(L, 3);
                        lua_call(L, 2, 0);
                        return 0;
                    }
                    lua_pop(L, 1);
                }
            }

            if (!lua_isnil(L, lua_upvalueindex(3)))
            {
                lua_settop(L, 3);
                lua_pushvalue(L, lua_upvalueindex(3));
                lua_insert(L, 1);
                lua_call(L, 3, 0);
                return 0;
            }
            else
            {
                return luaL_error(L, "class cannot set %s, no such field", lua_tostring(L, 2));
            }
        }

        // upvalue --- [1]:setters, [2]:base_newindex
        // param   --- [1]: obj, [2]: key, [3]: value
        int obj_new_indexer(lua_State* L)
        {
            int t = lua_type(L, 2);
            if (t == LUA_TSTRING)
            {
                return obj_internal_new_indexer(L, false);
            }
            else if (t == LUA_TNUMBER)
            {
                lua_pushstring(L, "set_Item");
                return obj_internal_new_indexer(L, true);
            }
            else
            {
                return 0;
            }
        }

        int object_new(lua_State* L)
        {
            LuaClassDefinition* class_definition = static_cast<LuaClassDefinition*>(lua_touserdata(L, lua_upvalueindex(1)));
#if OSG_PROFILE
            int index = 0;
            if (pf_stats_begin_sample != nullptr)
                index = pf_stats_begin_sample(CppObjectMapper::PrefNewIndex, pf_stats_regist_custom_name(class_definition->ScriptName));
#endif
            pesapi_callback_info__ callback_info{L, 1, 0};
            xlua::CppObjectMapper::Get()->BindCppObject(L, class_definition, class_definition->Initialize(&g_pesapi_ffi, &callback_info), false);
#if OSG_PROFILE
            if (pf_stats_end_sample_by_index != nullptr)
            {
                pf_stats_end_sample_by_index(index);
            }
#endif
            return 1;
        }

        // https://crashsight.qq.com/crash-reporting/crashes/6dd8864548/898163596C46E5F4374117888D9196A6?pid=1&crashDataType=unSystemExit
        // 虚拟机销毁的时候，会进入这里。
        int object_gc(lua_State* L)
        {
            // 类型信息全局唯一，永远不会销毁
            LuaClassDefinition* class_definition = (LuaClassDefinition*)lua_touserdata(L, lua_upvalueindex(1));
#if OSG_PROFILE
            int index = 0;
            if (pf_stats_begin_sample != nullptr)
                index = pf_stats_begin_sample(CppObjectMapper::PrefGCIndex, pf_stats_regist_custom_name(class_definition->ScriptName));
#endif
            // newuserdata构造出来的，此时不会销毁
            CppObject* cppObject = (CppObject*)lua_touserdata(L, -1);
#if LUA_MEM_PROFILER
            ZEngine::GetApi()->RemoveUserData(cppObject);
#endif
            const auto instance = xlua::CppObjectMapper::Get();
            // 虚拟机销毁的时候，Ptr指针已经无效了
            if (instance == nullptr || instance->HasDisposed())
                return 0;
            instance->UnBindCppObject(class_definition, cppObject->Ptr);
            if (cppObject->NeedDelete)
            {
                // printf("Finalize %p\n", cppObject->Ptr);
                // 通知C#释放此对象
                if (class_definition->Finalize)
                {
                    // Crash到这里，Finalize是一个静态函数，目的是删除cppObject->Ptr，说明Ptr已经被释放了
                    // CppObjectMapper在卸载的时候会释放Ptr
                    class_definition->Finalize(&g_pesapi_ffi, cppObject->Ptr, class_definition->Data, L);
                }
            }
#if OSG_PROFILE
            if (pf_stats_end_sample_by_index != nullptr)
            {
                pf_stats_end_sample_by_index(index);
            }
#endif
            return 0;
        }

        // upvalue --- [1]:getters, [2]:fields, [3]:base_index
        // param   --- [1]: obj, [2]: key
        int static_indexer(lua_State* L)
        {
            if (!lua_isnil(L, lua_upvalueindex(1)))
            {
                lua_pushvalue(L, 2);
                lua_gettable(L, lua_upvalueindex(1));
                if (!lua_isnil(L, -1))
                { // has getter
                    lua_call(L, 0, 1);
                    return 1;
                }
                lua_pop(L, 1);
            }

            if (!lua_isnil(L, lua_upvalueindex(2)))
            {
                lua_pushvalue(L, 2);
                lua_rawget(L, lua_upvalueindex(2));
                if (!lua_isnil(L, -1))
                { // has field
                    return 1;
                }
                lua_pop(L, 1);
            }

            if (!lua_isnil(L, lua_upvalueindex(3)))
            {
                lua_settop(L, 2);
                lua_pushvalue(L, lua_upvalueindex(3));
                lua_insert(L, 1);
                lua_call(L, 2, 1);
                return 1;
            }
            else
            {
                lua_pushnil(L);
                return 1;
            }
        }

        // upvalue --- [1]:setters, [2]:base_newindex
        // param   --- [1]: obj, [2]: key, [3]: value
        static int static_newindexer(lua_State* L)
        {
            if (!lua_isnil(L, lua_upvalueindex(1)))
            {
                lua_pushvalue(L, 2);
                lua_gettable(L, lua_upvalueindex(1));
                if (!lua_isnil(L, -1))
                { // has setter
                    lua_pushvalue(L, 3);
                    lua_call(L, 1, 0);
                    return 0;
                }
            }

            if (!lua_isnil(L, lua_upvalueindex(2)))
            {
                lua_settop(L, 3);
                lua_pushvalue(L, lua_upvalueindex(2));
                lua_insert(L, 1);
                lua_call(L, 3, 0);
                return 0;
            }
            else
            {
                return luaL_error(L, "no static field %s", lua_tostring(L, 2));
            }
        }

        int property_getter_wrap(lua_State* L)
        {
            LuaPropertyInfo* prop_info = (LuaPropertyInfo*)lua_touserdata(L, lua_upvalueindex(1));
#if OSG_PROFILE
            int index = 0;
            if (pf_stats_begin_sample != nullptr)
                index = pf_stats_begin_sample(CppObjectMapper::PrefPropertyGetterIndex, pf_stats_regist_custom_name(prop_info->Name));
#endif
            // printf("property_getter_wrap %p\n", prop_info);
            pesapi_callback_info__ callback_info{L, 1, 0};
            prop_info->Getter(&g_pesapi_ffi, &callback_info);
#if OSG_PROFILE
            if (pf_stats_end_sample_by_index != nullptr)
            {
                pf_stats_end_sample_by_index(index);
            }
#endif
            return callback_info.RetNum;
        }

        static int property_setter_wrap(lua_State* L)
        {
            LuaPropertyInfo* prop_info = (LuaPropertyInfo*)lua_touserdata(L, lua_upvalueindex(1));
#if OSG_PROFILE
            int index = 0;
            if (pf_stats_begin_sample != nullptr)
                index = pf_stats_begin_sample(CppObjectMapper::PrefPropertySetterIndex, pf_stats_regist_custom_name(prop_info->Name));
#endif
            // printf("property_setter_wrap %p\n", prop_info);
            pesapi_callback_info__ callback_info{L, 1, 0};
            prop_info->Setter(&g_pesapi_ffi, &callback_info);
#if OSG_PROFILE
            if (pf_stats_end_sample_by_index != nullptr)
            {
                pf_stats_end_sample_by_index(index);
            }
#endif
            return callback_info.RetNum;
        }

        static int variable_getter_wrap(lua_State* L)
        {

            LuaPropertyInfo* prop_info = (LuaPropertyInfo*)lua_touserdata(L, lua_upvalueindex(1));
#if OSG_PROFILE
            int index = 0;
            if (pf_stats_begin_sample != nullptr)
                index = pf_stats_begin_sample(CppObjectMapper::PrefFieldGetterIndex, pf_stats_regist_custom_name(prop_info->Name));
#endif
            // printf("variable_getter_wrap %p top=%d\n", prop_info, lua_gettop(L));
            pesapi_callback_info__ callback_info{L, 0, 0};
            prop_info->Getter(&g_pesapi_ffi, &callback_info);
#if OSG_PROFILE
            if (pf_stats_end_sample_by_index != nullptr)
            {
                pf_stats_end_sample_by_index(index);
            }
#endif
            return callback_info.RetNum;
        }

        static int variable_setter_wrap(lua_State* L)
        {
            LuaPropertyInfo* prop_info = (LuaPropertyInfo*)lua_touserdata(L, lua_upvalueindex(1));
#if OSG_PROFILE
            int index = 0;
            if (pf_stats_begin_sample != nullptr)
                index = pf_stats_begin_sample(CppObjectMapper::PrefFieldSetterIndex, pf_stats_regist_custom_name(prop_info->Name));
#endif

            // printf("variable_setter_wrap %p top=%d\n", prop_info, lua_gettop(L));
            pesapi_callback_info__ callback_info{L, 0, 0};
            prop_info->Setter(&g_pesapi_ffi, &callback_info);
#if OSG_PROFILE
            if (pf_stats_end_sample_by_index != nullptr)
            {
                pf_stats_end_sample_by_index(index);
            }
#endif
            return callback_info.RetNum;
        }

        static int method_wrap(lua_State* L)
        {
            LuaFunctionInfo* func_info = (LuaFunctionInfo*)lua_touserdata(L, lua_upvalueindex(1));
#if OSG_PROFILE
            int index = 0;
            if (pf_stats_begin_sample != nullptr)
                index = pf_stats_begin_sample(CppObjectMapper::PrefMethodIndex, pf_stats_regist_custom_name(func_info->Name));
#endif
            // printf("method_wrap %p\n", func_info);
            pesapi_callback_info__ callback_info{L, 1, 0};
            func_info->Callback(&g_pesapi_ffi, &callback_info);
#if OSG_PROFILE
            if (pf_stats_end_sample_by_index != nullptr)
            {
                pf_stats_end_sample_by_index(index);
            }
#endif
            return callback_info.RetNum;
        }

        static int function_wrap(lua_State* L)
        {

            LuaFunctionInfo* func_info = (LuaFunctionInfo*)lua_touserdata(L, lua_upvalueindex(1));
#if OSG_PROFILE
            int index = 0;
            if (pf_stats_begin_sample != nullptr)
                index = pf_stats_begin_sample(CppObjectMapper::PrefFunctionIndex, pf_stats_regist_custom_name(func_info->Name));
#endif
            // printf("function_wrap %p\n", func_info);
            pesapi_callback_info__ callback_info{L, 0, 0};
            func_info->Callback(&g_pesapi_ffi, &callback_info);
#if OSG_PROFILE
            if (pf_stats_end_sample_by_index != nullptr)
            {
                pf_stats_end_sample_by_index(index);
            }
#endif
            return callback_info.RetNum;
        }
    } // namespace

    MetaInfo* CppObjectMapper::GetMetaRefOfClass(lua_State* L, const LuaClassDefinition* classDefinition)
    {
        // requires at least 13 slots in stack: 8 fixed slots (obj_methods, obj_getters, obj_setters, static_functions, static_getters, static_setters, meta, cls_meta), 5 extension
        // slots (__index, obj_methods, obj_getters, super_meta_ref, __index)
        luaL_checkstack(L, 13, "not enough stack space for GetMetaRefOfClass");
        // printf("GetMetaRefOfClass %p \n", ClassDefinition);
        const auto iterator = m_TypeIdToMetaMap.find(classDefinition->TypeId);
        if (iterator == m_TypeIdToMetaMap.end())
        {
            MetaInfo* metaInfo = new MetaInfo(0, classDefinition->dictionary, classDefinition->enumerable);
            int org_top = lua_gettop(L);
            MetaInfo* super_meta_ref = nullptr;
            bool has_super = false;
            if (classDefinition->SuperTypeId)
            {
                if (const xlua::LuaClassDefinition* superDefinition = LoadClassByID(classDefinition->SuperTypeId))
                {
                    super_meta_ref = GetMetaRefOfClass(L, superDefinition);
                    has_super = true;
                    metaInfo->dictionary = super_meta_ref->dictionary || metaInfo->dictionary;
                    metaInfo->enumerable = super_meta_ref->enumerable || metaInfo->enumerable;
                }
            }

            lua_createtable(L, 0, 0);
            int obj_methods = lua_gettop(L);
            lua_createtable(L, 0, 0);
            int obj_getters = lua_gettop(L);
            lua_createtable(L, 0, 0);
            int obj_setters = lua_gettop(L);

            lua_createtable(L, 0, 0);
            int static_functions = lua_gettop(L);
            lua_createtable(L, 0, 0);
            int static_getters = lua_gettop(L);
            lua_createtable(L, 0, 0);
            int static_setters = lua_gettop(L);

            lua_newtable(L);
            int meta = lua_gettop(L);

            LuaPropertyInfo* PropertyInfo = classDefinition->Properties;
            while (PropertyInfo && PropertyInfo->Name)
            {
                if (PropertyInfo->Getter)
                {
                    lua_pushstring(L, PropertyInfo->Name);
                    lua_pushlightuserdata(L, PropertyInfo);
                    lua_pushlightuserdata(L, PropertyInfo->GetterData);
                    // printf("p get %s, %p \n", PropertyInfo->Name, PropertyInfo);
                    lua_pushcclosure(L, property_getter_wrap, 2);
                    lua_rawset(L, obj_getters);
                }
                if (PropertyInfo->Setter)
                {
                    lua_pushstring(L, PropertyInfo->Name);
                    lua_pushlightuserdata(L, PropertyInfo);
                    lua_pushlightuserdata(L, PropertyInfo->SetterData);
                    // printf("p set %s, %p \n", PropertyInfo->Name, PropertyInfo);
                    lua_pushcclosure(L, property_setter_wrap, 2);
                    lua_rawset(L, obj_setters);
                }
                ++PropertyInfo;
            }

            PropertyInfo = classDefinition->Variables;
            while (PropertyInfo && PropertyInfo->Name)
            {
                if (PropertyInfo->Getter)
                {
                    lua_pushstring(L, PropertyInfo->Name);
                    lua_pushlightuserdata(L, PropertyInfo);
                    lua_pushlightuserdata(L, PropertyInfo->GetterData);
                    // printf("v get %s, %p \n", PropertyInfo->Name, PropertyInfo);
                    lua_pushcclosure(L, variable_getter_wrap, 2);
                    lua_rawset(L, static_getters);
                }
                if (PropertyInfo->Setter)
                {
                    lua_pushstring(L, PropertyInfo->Name);
                    lua_pushlightuserdata(L, PropertyInfo);
                    lua_pushlightuserdata(L, PropertyInfo->SetterData);
                    // printf("v set %s, %p \n", PropertyInfo->Name, PropertyInfo);
                    lua_pushcclosure(L, variable_setter_wrap, 2);
                    lua_rawset(L, static_setters);
                }
                ++PropertyInfo;
            }

            LuaFunctionInfo* FunctionInfo = classDefinition->Methods;
            while (FunctionInfo && FunctionInfo->Name && FunctionInfo->Callback)
            {
                if (strcmp(FunctionInfo->Name, "Invoke") == 0)
                {
                    lua_pushstring(L, "__call");
                    lua_pushlightuserdata(L, FunctionInfo);
                    lua_pushlightuserdata(L, FunctionInfo->Data);
                    lua_pushcclosure(L, method_wrap, 2);
                    lua_rawset(L, meta);
                }
                else if (strcmp(FunctionInfo->Name, "ToString") == 0)
                {
                    lua_pushstring(L, "__tostring");
                    lua_pushlightuserdata(L, FunctionInfo);
                    lua_pushlightuserdata(L, FunctionInfo->Data);
                    lua_pushcclosure(L, method_wrap, 2);
                    lua_rawset(L, meta);
                }
                lua_pushstring(L, FunctionInfo->Name);
                lua_pushlightuserdata(L, FunctionInfo);
                lua_pushlightuserdata(L, FunctionInfo->Data);
                // printf("m %s, %p \n", FunctionInfo->Name, FunctionInfo);
                lua_pushcclosure(L, method_wrap, 2);
                lua_rawset(L, obj_methods);
                ++FunctionInfo;
            }
            if (metaInfo->dictionary)
            {
                lua_pushstring(L, "__pairs");
                lua_rawgeti(L, LUA_REGISTRYINDEX, m_IDictionary);
                lua_rawset(L, meta);
            }
            else if (metaInfo->enumerable)
            {
                lua_pushstring(L, "__pairs");
                lua_rawgeti(L, LUA_REGISTRYINDEX, m_Enumerable);
                lua_rawset(L, meta);
            }
            FunctionInfo = classDefinition->Functions;
            while (FunctionInfo && FunctionInfo->Name && FunctionInfo->Callback)
            {
                auto op = supportOp.find(FunctionInfo->Name);
                if (op != supportOp.end())
                {
                    lua_pushstring(L, op->second);
                    lua_pushlightuserdata(L, FunctionInfo);
                    lua_pushlightuserdata(L, FunctionInfo->Data);
                    // printf("f %s, %p \n", FunctionInfo->Name, FunctionInfo);
                    lua_pushcclosure(L, function_wrap, 2);
                    lua_rawset(L, meta);
                }
                else
                {
                    lua_pushstring(L, FunctionInfo->Name);
                    lua_pushlightuserdata(L, FunctionInfo);
                    lua_pushlightuserdata(L, FunctionInfo->Data);
                    // printf("f %s, %p \n", FunctionInfo->Name, FunctionInfo);
                    lua_pushcclosure(L, function_wrap, 2);
                    lua_rawset(L, static_functions);
                }
                ++FunctionInfo;
            }

            lua_pushstring(L, "__index");
            lua_pushvalue(L, obj_methods);
            lua_pushvalue(L, obj_getters);
            if (has_super)
            {
                lua_rawgeti(L, LUA_REGISTRYINDEX, super_meta_ref->ref);
                lua_pushstring(L, "__index");
                lua_rawget(L, -2);
                lua_remove(L, -2);
            }
            else
            {
                lua_pushnil(L);
            }
            lua_pushcclosure(L, object_indexer, 3);
            lua_rawset(L, meta);

            lua_pushstring(L, "__newindex");
            lua_pushvalue(L, obj_methods);
            lua_pushvalue(L, obj_setters);
            if (has_super)
            {
                lua_rawgeti(L, LUA_REGISTRYINDEX, super_meta_ref->ref);
                lua_pushstring(L, "__newindex");
                lua_rawget(L, -2);
                lua_remove(L, -2);
            }
            else
            {
                lua_pushnil(L);
            }
            lua_pushcclosure(L, obj_new_indexer, 3);
            lua_rawset(L, meta);

            lua_pushstring(L, "__gc");
            lua_pushlightuserdata(L, const_cast<LuaClassDefinition*>(classDefinition));
            lua_pushcclosure(L, object_gc, 1);
            lua_rawset(L, meta);

            lua_createtable(L, 0, 0);
            int cls_meta = lua_gettop(L);

            lua_pushstring(L, "__call");
            lua_pushlightuserdata(L, const_cast<LuaClassDefinition*>(classDefinition));
            lua_pushlightuserdata(L, classDefinition->Data);
            lua_pushcclosure(L, object_new, 2);
            lua_rawset(L, cls_meta);

            lua_pushstring(L, "__index");
            lua_pushvalue(L, static_getters);
            lua_pushvalue(L, static_functions);
            if (has_super)
            {
                lua_rawgeti(L, LUA_REGISTRYINDEX, super_meta_ref->ref);
                lua_pushlightuserdata(L, &dummy_idx_tag);

                lua_rawget(L, -2);
                lua_remove(L, -2);
                if (!lua_isnil(L, -1))
                {
                    lua_pushstring(L, "__index");
                    lua_rawget(L, -2);
                    lua_remove(L, -2);
                }
            }
            else
            {
                lua_pushnil(L);
            }
            lua_pushcclosure(L, static_indexer, 3);
            lua_rawset(L, cls_meta);

            lua_pushstring(L, "__newindex");
            lua_pushvalue(L, static_setters);
            if (has_super)
            {
                lua_rawgeti(L, LUA_REGISTRYINDEX, super_meta_ref->ref);
                lua_pushlightuserdata(L, &dummy_idx_tag);
                lua_rawget(L, -2);
                lua_remove(L, -2);
                if (!lua_isnil(L, -1))
                {
                    lua_pushstring(L, "__newindex");
                    lua_rawget(L, -2);
                    lua_remove(L, -2);
                }
            }
            else
            {
                lua_pushnil(L);
            }
            lua_pushcclosure(L, static_newindexer, 2);
            lua_rawset(L, cls_meta);

            lua_pushlightuserdata(L, &dummy_idx_tag);
            lua_insert(L, cls_meta);
            lua_rawset(L, meta);
            metaInfo->ref = luaL_ref(L, LUA_REGISTRYINDEX);
            lua_pop(L, 6);

            if (org_top != lua_gettop(L))
            {
                luaL_error(L, "stack top changed ? %d, %d\n", org_top, lua_gettop(L));
            }
            m_TypeIdToMetaMap[classDefinition->TypeId] = metaInfo;
            return metaInfo;
        }
        return iterator->second;
    }
} // namespace xlua
