#pragma once

namespace ZEngine
{
    using InitMemoryProfilerDataFunction = void (*)();

    using UnInitMemoryProfilerDataFunction = void (*)();

    using AddCSharpDataFunction = void (*)(int index, const char* name, const char* stack);

    using RemoveCSharpDataFunction = void (*)(int index);

    using AddUserDataFunction = void (*)(void* ptr, const char* stack);

    using RemoveUserDataFunction = void (*)(void* ptr);

    using AddLuaDataFunction = void (*)(void* ptr, const char* stack, uint32_t size);

    using ResizeLuaDataFunction = void (*)(void* ptr, uint32_t size);

    using RemoveLuaDataFunction = void (*)(void* ptr);

    using SaveDataFunction = void (*)(const char* path);

    struct Api
    {
        InitMemoryProfilerDataFunction   InitMemoryProfilerData;
        UnInitMemoryProfilerDataFunction UnInitMemoryProfilerData;
        AddCSharpDataFunction            AddCSharpData;
        RemoveCSharpDataFunction         RemoveCSharpData;
        AddUserDataFunction              AddUserData;
        RemoveUserDataFunction           RemoveUserData;
        AddLuaDataFunction               AddLuaData;
        ResizeLuaDataFunction            ResizeLuaData;
        RemoveLuaDataFunction            RemoveLuaData;
        SaveDataFunction                 SaveDataFunction;
    };
}  // namespace ZEngine