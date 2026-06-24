using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using XLua.LuaDLL;

namespace XLua
{
    public abstract class LuaEnvBase : IDisposable
    {
        protected IntPtr nativeLuaEnv;

        public IntPtr rawL;

        protected LuaTable _G;
        protected IntPtr apis;
        public pesapi_ffi ffi;
        internal int authCode;

        internal static LuaEnv Instance;
        public IntPtr L
        {
            get
            {
                if (rawL == IntPtr.Zero)
                {
                    throw new InvalidOperationException("this lua env had disposed!");
                }
                return rawL;
            }
        }

        public LuaTable Global
        {
            get
            {
                return _G;
            }
        }

        public delegate byte[] CustomLoader(ref string filepath);

        internal List<CustomLoader> customLoaders = new();

        //loader : CustomLoader， filepath参数：（ref类型）输入是require的参数，如果需要支持调试，需要输出真实路径。
        //返回值：如果返回null，代表加载该源下无合适的文件，否则返回UTF8编码的byte[]
        public void AddLoader(CustomLoader loader)
        {
            customLoaders.Add(loader);
        }

        protected void Init(CustomLoader loader)
        {
            if (loader != null)
            {
                AddLoader(loader);
            }
            nativeLuaEnv = NativeAPI.CreateNativeLuaEnv();
            rawL = NativeAPI.GetLuaState(nativeLuaEnv);
            Lua.lua_pushstdcallcfunction(rawL, StaticLuaCallbacks.Print);
            if (0 != Lua.xlua_setglobal(rawL, "print"))
            {
                throw new Exception("call xlua_setglobal fail!");
            }
            AddSearcher(StaticLuaCallbacks.LoadFromCustomLoaders, -1);
            apis = NativeAPI.GetFFIApi();
            ffi = Marshal.PtrToStructure<pesapi_ffi>(apis);
            authCode = ffi.get_auth_code();
            Instance = this as LuaEnv;
#if LUA_MEM_PROFILER
            ZRuntimeShared.Init();
#endif
        }

        public virtual void Dispose()
        {
            if (rawL == IntPtr.Zero)
                return;
            NativeAPI.DestroyNativeLuaEnv(nativeLuaEnv);
            rawL = IntPtr.Zero;
            nativeLuaEnv = IntPtr.Zero;
#if  LUA_MEM_PROFILER
            ZRuntimeShared.UnInit();
#endif
        }

        protected void AddSearcher(lua_CSFunction searcher, int index)
        {
            var _L = L;
            //insert the loader
            Lua.xlua_getloaders(_L);
            if (!Lua.lua_istable(_L, -1))
            {
                throw new Exception("Can not set searcher!");
            }
            uint len = Lua.xlua_objlen(_L, -1);
            index = index < 0 ? (int)(len + index + 2) : index;
            for (int e = (int)len + 1; e > index; e--)
            {
                Lua.xlua_rawgeti(_L, -1, e - 1);
                Lua.xlua_rawseti(_L, -2, e);
            }
            Lua.lua_pushstdcallcfunction(_L, searcher);
            Lua.xlua_rawseti(_L, -2, index);
            Lua.lua_pop(_L, 1);
        }
    }
}
