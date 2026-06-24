#pragma once
#include "ZEngineApi.export.h"

namespace ZEngine
{
    Api* g_GameCore_ZEngineApi = nullptr;

    Api* GetApi()
    {
        return g_GameCore_ZEngineApi;
    }
} // namespace ZEngine
