using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Reflection;
using System.Collections.Generic;

namespace XLua
{
    public class NativeAPI
    {
#if (UNITY_IPHONE || UNITY_TVOS || UNITY_WEBGL || UNITY_SWITCH) && !UNITY_EDITOR
        const string DLLNAME = "__Internal";
#else
        const string DLLNAME = "GameCore";
#endif

        [DllImport(DLLNAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr CreateNativeLuaEnv();

        [DllImport(DLLNAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr GetLuaState(IntPtr luaEnv);

        [DllImport(DLLNAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern void DestroyNativeLuaEnv(IntPtr luaEnv);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static void SetGlobalType_LuaTable(Type type)
        { }

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static void SetGlobalType_ArrayBuffer(Type type)
        { }

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static void SetGlobalType_Array(Type type)
        { }

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static void SetGlobalType_IntPtr(Type type)
        { }

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static void SetGlobalType_IEnumerable(Type type)
        { }

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static void SetGlobalType_IDictionary(Type type)
        { }
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static void SetGlobalType_LuaException(Type type)
        { }
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static void SetGlobalType_Object(Type type)
        { }
        [DllImport(DLLNAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr GetFFIApi();
        [DllImport(DLLNAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern void LuaCrash();
#if XLUA_IL2CPP && ENABLE_IL2CPP

        [DllImport("__Internal", CallingConvention = CallingConvention.Cdecl)]
        public static extern void InitialXLua(IntPtr PesapiImpl);

        [DllImport(DLLNAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr GetRegisterApi();

        [DllImport(DLLNAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr GetPapiEnvRef(IntPtr luaEnv);

        [DllImport(DLLNAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern void OnUnityObjectDestroyByLua(IntPtr L, UnityEngine.Object obj, string name);

        [DllImport("__Internal", CallingConvention = CallingConvention.Cdecl)]
        public static extern void AddPendingKillScriptObjects(IntPtr ffiApi, IntPtr valueRef);

        [DllImport("__Internal", CallingConvention = CallingConvention.Cdecl)]
        public static extern void CleanupPendingKillScriptObjects(IntPtr luaEnvPrivate, IntPtr apis);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static IntPtr InitialPapiEnvRef(IntPtr api, IntPtr envRef, Object obj, MethodBase addMethodBase, MethodBase removeMethodBase)
        {
            throw new NotImplementedException();
        }

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static void InitPerf(MethodBase beginSample, MethodBase endSample)
        {
            throw new NotImplementedException();
        }

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static void CleanupPapiEnvRef(IntPtr api, IntPtr envRef)
        {
            throw new NotImplementedException();
        }

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static void DestroyLuaEnvPrivate(IntPtr apis)
        {
            throw new NotImplementedException();
        }

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static void SetExtensionMethodGet(MethodBase methodInfo)
        {
            throw new NotImplementedException();
        }

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static void SetObjectToGlobal(IntPtr apis, IntPtr luaEnv, string key, Object obj)
        {
            throw new NotImplementedException();
        }

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static object GetModuleExecutor(IntPtr NativeLuaEnvPtr, Type type)
        {
            throw new NotImplementedException();
        }

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static void PesapiCallTest(Type type)
        {
            throw new NotImplementedException();
        }

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static object DoString(IntPtr apis, IntPtr envHolder, byte[] code, string path, LuaTable env, Type type)
        {
            throw new NotImplementedException();
        }

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static object LoadString(IntPtr apis, IntPtr envHolder, byte[] code, string path, LuaTable env, Type type)
        {
            throw new NotImplementedException();
        }

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static object NewTable(IntPtr apis, IntPtr envHolder)
        {
            throw new NotImplementedException();
        }

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static object GetGlobalTable(IntPtr apis, IntPtr envHolder)
        {
            throw new NotImplementedException();
        }
#endif
    }

    public delegate void pesapi_callback(IntPtr apis, IntPtr info);
    public delegate void pesapi_function_finalize(IntPtr apis, IntPtr data, IntPtr env_private);

    public delegate int pesapi_create_null_func(IntPtr env);
    public delegate IntPtr pesapi_create_undefined_func(IntPtr env);
    public delegate int pesapi_create_boolean_func(IntPtr env, bool value);
    public delegate int pesapi_create_int32_func(IntPtr env, int value);
    public delegate int pesapi_create_uint32_func(IntPtr env, uint value);
    public delegate int pesapi_create_int64_func(IntPtr env, long value);
    public delegate int pesapi_create_uint64_func(IntPtr env, ulong value);
    public delegate int pesapi_create_double_func(IntPtr env, double value);
    public delegate int pesapi_create_string_utf8_func(IntPtr env, byte[] str, int length);
    public delegate IntPtr pesapi_create_binary_func(IntPtr env, IntPtr str, UIntPtr length);
    public delegate IntPtr pesapi_create_array_func(IntPtr env);
    public delegate IntPtr pesapi_create_object_func(IntPtr env);
    public delegate IntPtr pesapi_create_function_func(IntPtr env, pesapi_callback native_impl, IntPtr data, pesapi_function_finalize finalize);
    public delegate IntPtr pesapi_create_class_func(IntPtr env, IntPtr type_id);

    public delegate bool pesapi_get_value_bool_func(IntPtr env, int value);
    public delegate int pesapi_get_value_int32_func(IntPtr env, int value);
    public delegate uint pesapi_get_value_uint32_func(IntPtr env, int value);
    public delegate long pesapi_get_value_int64_func(IntPtr env, int value);
    public delegate ulong pesapi_get_value_uint64_func(IntPtr env, int value);
    public delegate double pesapi_get_value_double_func(IntPtr env, int value);
    public delegate IntPtr pesapi_get_value_string_utf8_func(IntPtr env, int value, byte[] buf, ref int bufsize);
    public delegate IntPtr pesapi_get_value_binary_func(IntPtr env, IntPtr pvalue, ref UIntPtr bufsize);
    public delegate uint pesapi_get_array_length_func(IntPtr env, IntPtr value);
    public delegate void pesapi_get_array_element_func(IntPtr env, int index, int array_index);

    public delegate bool pesapi_is_null_func(IntPtr env, int value);
    public delegate bool pesapi_is_undefined_func(IntPtr env, int value);
    public delegate bool pesapi_is_boolean_func(IntPtr env, int value);
    public delegate bool pesapi_is_int32_func(IntPtr env, int value);
    public delegate bool pesapi_is_uint32_func(IntPtr env, int value);
    public delegate bool pesapi_is_int64_func(IntPtr env, int value);
    public delegate bool pesapi_is_uint64_func(IntPtr env, int value);
    public delegate bool pesapi_is_double_func(IntPtr env, int value);
    public delegate bool pesapi_is_string_func(IntPtr env, int value);
    public delegate bool pesapi_is_object_func(IntPtr env, IntPtr value);

    public delegate bool pesapi_is_userdata_func(IntPtr env, IntPtr value);
    public delegate bool pesapi_is_function_func(IntPtr env, int value);
    public delegate bool pesapi_is_binary_func(IntPtr env, IntPtr value);
    public delegate bool pesapi_is_array_func(IntPtr env, IntPtr value);

    public delegate IntPtr pesapi_native_object_to_value_func(IntPtr env, IntPtr type_id, IntPtr object_ptr, bool call_finalize);
    public delegate IntPtr pesapi_get_native_object_ptr_func(IntPtr env, int value);
    public delegate IntPtr pesapi_get_native_object_typeid_func(IntPtr env, IntPtr value);
    public delegate bool pesapi_is_instance_of_func(IntPtr env, IntPtr type_id, IntPtr value);

    public delegate IntPtr pesapi_boxing_func(IntPtr env, IntPtr value);
    public delegate IntPtr pesapi_unboxing_func(IntPtr env, IntPtr value);
    public delegate void pesapi_update_boxed_value_func(IntPtr env, IntPtr boxed_value, IntPtr value);
    public delegate bool pesapi_is_boxed_value_func(IntPtr env, IntPtr value);

    public delegate int pesapi_get_args_len_func(IntPtr info);
    public delegate int pesapi_get_arg_func(IntPtr info, int index);
    public delegate IntPtr pesapi_get_env_func(IntPtr info);
    public delegate int pesapi_get_this_func(IntPtr info);
    public delegate int pesapi_get_holder_func(IntPtr info);
    public delegate IntPtr pesapi_get_userdata_func(IntPtr info);
    public delegate void pesapi_add_return_func(IntPtr info, IntPtr value);
    public delegate int pesapi_get_return_num_func(IntPtr info);
    public delegate void pesapi_throw_by_string_func(IntPtr pinfo, string msg);

    public delegate IntPtr pesapi_create_env_ref_func(IntPtr env);
    public delegate bool pesapi_env_ref_is_valid_func(IntPtr env);
    public delegate IntPtr pesapi_get_env_from_ref_func(IntPtr env_ref);
    public delegate IntPtr pesapi_duplicate_env_ref_func(IntPtr env_ref);
    public delegate void pesapi_release_env_ref_func(IntPtr env_ref);
    public delegate int pesapi_open_scope_func(IntPtr env_ref);
    public delegate IntPtr pesapi_open_scope_placement_func(IntPtr env_ref);
    public delegate bool pesapi_has_caught_func(IntPtr scope);
    public delegate IntPtr pesapi_get_exception_as_string_func(IntPtr scope, bool with_stack);
    public delegate void pesapi_close_scope_func(IntPtr env_ref, int scope, int reserve);
    public delegate void pesapi_close_scope_placement_func(IntPtr env_ref, int scope);

    public delegate IntPtr pesapi_create_value_ref_func(IntPtr env, int value, uint internal_field_count);
    public delegate IntPtr pesapi_duplicate_value_ref_func(IntPtr value_ref);
    public delegate void pesapi_release_value_ref_func(IntPtr value_ref);
    public delegate IntPtr pesapi_get_value_from_ref_func(IntPtr env, IntPtr value_ref);
    public delegate void pesapi_set_ref_week_func(IntPtr env, IntPtr value_ref);
    public delegate bool pesapi_set_owner_func(IntPtr env, IntPtr value, IntPtr owner);
    public delegate IntPtr pesapi_get_ref_associated_env_func(IntPtr value_ref);
    public delegate IntPtr pesapi_get_ref_internal_fields_func(IntPtr value_ref);
    public delegate void pesapi_set_ref_internal_fields_func(IntPtr value_ref, uint handle);

    public delegate IntPtr pesapi_get_property_func(IntPtr env, IntPtr objectPtr, string key);
    public delegate void pesapi_set_property_func(IntPtr env, IntPtr objectPtr, string key, IntPtr value);
    public delegate bool pesapi_get_private_func(IntPtr env, IntPtr objectPtr, out IntPtr outPtr);
    public delegate bool pesapi_set_private_func(IntPtr env, IntPtr objectPtr, IntPtr ptr);
    public delegate IntPtr pesapi_get_property_uint64_func(IntPtr env, IntPtr objectPtr, ulong key);
    public delegate void pesapi_set_property_uint64_func(IntPtr env, IntPtr objectPtr, ulong key, IntPtr value);

    public delegate int pesapi_prepare_function_func(IntPtr env);

    public delegate int pesapi_call_function_func(IntPtr env, int errFunc, int argc);
    public delegate IntPtr pesapi_dostring_func(IntPtr env, IntPtr code, UIntPtr code_size, string path);
    public delegate IntPtr pesapi_loadstring_func(IntPtr env, IntPtr code, UIntPtr code_size, string path);
    public delegate IntPtr pesapi_global_func(IntPtr env);
    public delegate IntPtr pesapi_get_env_private_func(IntPtr env);
    public delegate void pesapi_set_env_private_func(IntPtr env, IntPtr ptr);
    public delegate int pesapi_next_func(IntPtr env, int idx);
    public delegate int pesapi_get_auth_code();
    public delegate void pesapi_debug_func(string msg);
    public delegate void pesapi_info_func(string msg);
    public delegate void pesapi_warning_func(string msg);
    public delegate void pesapi_error_func(string msg);
    public delegate void pesapi_fatal_func(string msg);
    public delegate void pesapi_snapshot_func(IntPtr env, string msg);

    [StructLayout(LayoutKind.Sequential)]
    public struct pesapi_ffi
    {
        public pesapi_create_null_func create_null;
        public pesapi_create_undefined_func create_undefined;
        public pesapi_create_boolean_func create_boolean;
        public pesapi_create_int32_func create_int32;
        public pesapi_create_uint32_func create_uint32;
        public pesapi_create_int64_func create_int64;
        public pesapi_create_uint64_func create_uint64;
        public pesapi_create_double_func create_double;
        public pesapi_create_string_utf8_func create_string_utf8;
        public pesapi_create_binary_func create_binary;
        public pesapi_create_array_func create_array;
        public pesapi_create_object_func create_object;
        public pesapi_create_function_func create_function;
        public pesapi_create_class_func create_class;
        public pesapi_get_value_bool_func get_value_bool;
        public pesapi_get_value_int32_func get_value_int32;
        public pesapi_get_value_uint32_func get_value_uint32;
        public pesapi_get_value_int64_func get_value_int64;
        public pesapi_get_value_uint64_func get_value_uint64;
        public pesapi_get_value_double_func get_value_double;
        public pesapi_get_value_string_utf8_func get_value_string_utf8;
        public pesapi_get_value_binary_func get_value_binary;
        public pesapi_get_array_length_func get_array_length;
        public pesapi_get_array_element_func get_array_element;
        public pesapi_is_null_func is_null;
        public pesapi_is_undefined_func is_undefined;
        public pesapi_is_boolean_func is_boolean;
        public pesapi_is_int32_func is_int32;
        public pesapi_is_uint32_func is_uint32;
        public pesapi_is_int64_func is_int64;
        public pesapi_is_uint64_func is_uint64;
        public pesapi_is_double_func is_double;
        public pesapi_is_string_func is_string;
        public pesapi_is_object_func is_object;
        public pesapi_is_userdata_func is_userdata;
        public pesapi_is_function_func is_function;
        public pesapi_is_binary_func is_binary;
        public pesapi_is_array_func is_array;
        public pesapi_native_object_to_value_func native_object_to_value;
        public pesapi_get_native_object_ptr_func get_native_object_ptr;
        public pesapi_get_native_object_typeid_func get_native_object_typeid;
        public pesapi_is_instance_of_func is_instance_of;
        public pesapi_boxing_func boxing;
        public pesapi_unboxing_func unboxing;
        public pesapi_update_boxed_value_func update_boxed_value;
        public pesapi_is_boxed_value_func is_boxed_value;
        public pesapi_get_args_len_func get_args_len;
        public pesapi_get_arg_func get_arg;
        public pesapi_get_env_func get_env;
        public pesapi_get_this_func get_this;
        public pesapi_get_holder_func get_holder;
        public pesapi_get_userdata_func get_userdata;
        public pesapi_add_return_func add_return;
        public pesapi_get_return_num_func get_return_num;
        public pesapi_throw_by_string_func throw_by_string;
        public pesapi_create_env_ref_func create_env_ref;
        public pesapi_env_ref_is_valid_func env_ref_is_valid;
        public pesapi_get_env_from_ref_func get_env_from_ref;
        public pesapi_duplicate_env_ref_func duplicate_env_ref;
        public pesapi_release_env_ref_func release_env_ref;
        public pesapi_open_scope_func open_scope;
        public pesapi_open_scope_placement_func open_scope_placement;
        public pesapi_has_caught_func has_caught;
        public pesapi_get_exception_as_string_func get_exception_as_string;
        public pesapi_close_scope_func close_scope;
        public pesapi_close_scope_placement_func close_scope_placement;
        public pesapi_create_value_ref_func create_value_ref;
        public pesapi_duplicate_value_ref_func duplicate_value_ref;
        public pesapi_release_value_ref_func release_value_ref;
        public pesapi_get_value_from_ref_func get_value_from_ref;
        public pesapi_set_ref_week_func set_ref_week;
        public pesapi_set_owner_func set_owner;
        public pesapi_get_ref_associated_env_func get_ref_associated_env;
        public pesapi_get_ref_internal_fields_func get_ref_internal_fields;
        public pesapi_set_ref_internal_fields_func set_ref_internal_fields;
        public pesapi_get_property_func get_property;
        public pesapi_set_property_func set_property;
        public pesapi_get_private_func get_private;
        public pesapi_set_private_func set_private;
        public pesapi_get_property_uint64_func get_property_uint64;
        public pesapi_set_property_uint64_func set_property_uint64;
        public pesapi_prepare_function_func prepare_function;
        public pesapi_call_function_func call_function;
        public pesapi_dostring_func dostring;
        public pesapi_loadstring_func loadstring;
        public pesapi_global_func global;
        public pesapi_get_env_private_func get_env_private;
        public pesapi_set_env_private_func set_env_private;
        public pesapi_next_func next;
        public pesapi_get_auth_code get_auth_code;
        public pesapi_debug_func debug;
        public pesapi_info_func info;
        public pesapi_warning_func warning;
        public pesapi_error_func error;
        public pesapi_fatal_func fatal;
        public pesapi_snapshot_func snapshot;
    }
}
