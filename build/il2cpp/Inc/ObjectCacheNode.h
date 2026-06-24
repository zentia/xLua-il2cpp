#pragma once

#include "XLuaNamespaceDef.h"

namespace XLUA_NAMESPACE
{
    class ObjectCacheNode
    {
    public:
        explicit ObjectCacheNode(const void* typeId, void* userdata, void* ptr)
            : TypeId(typeId), UserData(userdata)
            , Ptr(ptr)
            , Next(nullptr)
            , Head(this)
            , Value(-1)
            , MustCallFinalize(false)
        {
        }

        ObjectCacheNode(const void* typeId, ObjectCacheNode* next, ObjectCacheNode* head, void* ptr)
            : TypeId(typeId)
            , UserData(nullptr)
            , Ptr(ptr)
            , Next(next)
            , Head(head)
            , Value(-1)
            , MustCallFinalize(false)
        {
        }

        ObjectCacheNode(ObjectCacheNode&& other) noexcept
            : TypeId(other.TypeId)
            , UserData(other.UserData)
            , Ptr(other.Ptr)
            , Next(other.Next)
            , Head(nullptr)
            , Value(std::move(other.Value))
            , MustCallFinalize(other.MustCallFinalize)
        {
            other.TypeId           = nullptr;
            other.UserData         = nullptr;
            other.Ptr = nullptr;
            other.Next             = nullptr;
            other.MustCallFinalize = false;
        }

        ObjectCacheNode& operator=(ObjectCacheNode&& rhs) noexcept
        {
            TypeId               = rhs.TypeId;
            Next                 = rhs.Next;
            Head = rhs.Head;
            Value                = std::move(rhs.Value);
            UserData             = rhs.UserData;
            Ptr = rhs.Ptr;
            MustCallFinalize     = rhs.MustCallFinalize;
            rhs.UserData         = nullptr;
            rhs.Ptr = nullptr;
            rhs.TypeId           = nullptr;
            rhs.Next             = nullptr;
            rhs.Head = nullptr;
            rhs.MustCallFinalize = false;
            return *this;
        }

        // 重复释放
        /*~ObjectCacheNode()
        {
            delete Next;
        }*/

        ObjectCacheNode* Find(const void* typeId)
        {
            if (typeId == TypeId)
            {
                return this;
            }
            if (Next)
            {
                return Next->Find(typeId);
            }
            return nullptr;
        }

        ObjectCacheNode* Remove(const void* typeId, const bool isHead)
        {
            if (typeId == TypeId)
            {
                if (isHead)
                {
                    if (Next)
                    {
                        const auto preNext = Next;
                        *this              = std::move(*Next);
                        delete preNext;
                    }
                    else
                    {
                        TypeId = nullptr;
                        Next   = nullptr;
                        Value  = -1;
                    }
                }
                return this;
            }
            if (Next)
            {
                const auto removed = Next->Remove(typeId, false);
                if (removed && removed == Next) // detach & delete by prev node
                {
                    Next          = removed->Next;
                    removed->Next = nullptr;
                    delete removed;
                }
                return removed;
            }
            return nullptr;
        }

        [[nodiscard]] bool IsValid() const
        {
            return TypeId != nullptr && Value != -1;
        }

        ObjectCacheNode* Add(const void* typeId)
        {
            Next = new ObjectCacheNode(typeId, Next, Head, Ptr);
            return Next;
        }

        const void* TypeId;

        void* UserData;
        void* Ptr;
        ObjectCacheNode* Next;
        ObjectCacheNode* Head;

        int Value;

        bool MustCallFinalize;

        ObjectCacheNode(const ObjectCacheNode&) = delete;
        void operator=(const ObjectCacheNode&)  = delete;
    };

} // namespace XLUA_NAMESPACE
