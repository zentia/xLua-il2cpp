#pragma once

#include "lua.hpp"

#include <map>
#include <memory>
#include <vector>

#include "dense_hash_map.h"

#include "LuaClassRegister.h"
#include "ObjectCacheNode.h"

typedef struct
{
    void* Ptr;
    const void* TypeId;
    const char* ScriptName;
    bool NeedDelete;
} CppObject;

struct IntHashFunctor
{
    inline size_t operator()(int x) const
    {
        return (size_t)x;
    }
};

struct pesapi_callback_info__
{
    lua_State* L;
    int ArgStart; // 0 or 1
    int RetNum;
};

typedef pesapi_callback_info__ PersistentObjectEnvInfo;
extern pesapi_ffi g_pesapi_ffi;

namespace xlua
{
    struct PesapiCallbackData
    {
        pesapi_callback Callback;
        void* Data;
        pesapi_function_finalize Finalize = nullptr;
    };

    struct MetaInfo
    {
        int ref;
        bool dictionary;
        bool enumerable;

        MetaInfo(int _ref, bool _dictionary, bool _enumerable)
            : ref(_ref)
            , dictionary(_dictionary)
            , enumerable(_enumerable)
        {
        }
    };

    class CppObjectMapper
    {
    public:
        CppObjectMapper();
        void Initialize(lua_State* L);

        void Traceback(lua_State* L);

        bool IsInstanceOfCppObject(lua_State* L, const void* TypeId, int ObjectIndex);

        std::weak_ptr<int> GetLuaEnvLifeCycleTracker();

        int FindOrAddCppObject(lua_State* L, const void* typeId, void* ptr, bool passByPointer);

        int CreateFunction(lua_State* L, pesapi_callback Callback, void* Data, pesapi_function_finalize Finalize);

        void UnBindCppObject(const LuaClassDefinition* classDefinition, void* ptr);

        void RemoveCacheNode(ObjectCacheNode* node, const LuaClassDefinition* classDefinition);

        void OnUnityObjectDestroy(lua_State* L, void* Ptr, const char* name);

        void BindCppObject(lua_State* L, const LuaClassDefinition* classDefinition, void* ptr, bool PassByPointer);

        void* GetPrivateData(lua_State* L, int index) const;

        bool SetPrivateData(lua_State* L, int index, void* ptr, const char* func_name);

        int LoadTypeById(lua_State* L, const void* typeId);

        void UnInitialize(lua_State* L);

        bool HasDisposed() const
        {
            return m_Disposed;
        }

        static CppObjectMapper* Get();
        static void CallbackDataGarbageCollected(PesapiCallbackData* Data);
#if OSG_PROFILE
        static int PrefPropertyGetterIndex;
        static int PrefPropertySetterIndex;
        static int PrefFieldGetterIndex;
        static int PrefFieldSetterIndex;
        static int PrefMethodIndex;
        static int PrefFunctionIndex;
        static int PrefNewIndex;
        static int PrefGCIndex;
#endif

    private:
        void ReleaseNode(lua_State* L, ObjectCacheNode* PNode, void* ptr, const char* name);

        dense_hash_map<void*, ObjectCacheNode*, PointerHashFunctor> m_DataCache;

        dense_hash_map<const void*, MetaInfo*, ConstPointerHashFunctor> m_TypeIdToMetaMap;

        std::map<std::string, const char*> supportOp;

        std::shared_ptr<int> Ref = std::make_shared<int>(0);

        int m_Traceback = 0;
        int m_CacheRef = 0;
        int m_CachePrivateDataRef = 0;
        int m_IDictionary = 0;
        int m_Enumerable = 0;

        MetaInfo* GetMetaRefOfClass(lua_State* L, const LuaClassDefinition* classDefinition);

        std::vector<PesapiCallbackData*> m_FunctionDatas;
        bool m_Disposed;
        bool m_FixPtrError;
    };
} // namespace xlua
