#if LUA_MEM_PROFILER
using System;
using System.Runtime.InteropServices;

public class ZRuntimeShared
{
#if UNITY_IOS && !UNITY_EDITOR
    private const string ZRuntimeSharedDllName = "__Internal";
    private const string GameCoreDllName = "__Internal";
#else
    private const string ZRuntimeSharedDllName = "ZRuntimeShared";
    private const string GameCoreDllName = "GameCore";
#endif

    [DllImport(ZRuntimeSharedDllName, CharSet = CharSet.Ansi)]
    public static extern void InitMemoryProfilerData();

    [DllImport(ZRuntimeSharedDllName)]
    private static extern void UnInitMemoryProfilerData();

    [DllImport(ZRuntimeSharedDllName, CharSet = CharSet.Ansi)]
    public static extern void AddCSharpData(int key, string name, string stack);

    [DllImport(ZRuntimeSharedDllName)]
    public static extern IntPtr RemoveCSharpData(int key);

    [DllImport(ZRuntimeSharedDllName, CharSet = CharSet.Ansi)]
    public static extern void AddUserData(IntPtr ptr, string stack);

    [DllImport(ZRuntimeSharedDllName)]
    public static extern void RemoveUserData(IntPtr ptr);

    [DllImport(ZRuntimeSharedDllName)]
    public static extern void SaveData(string path);

    [DllImport(ZRuntimeSharedDllName)]
    private static extern IntPtr ZEngineApi();

    [DllImport(GameCoreDllName)]
    private static extern void GameCore_ZEngineAPI_Init(IntPtr api);

    public static void Init()
    {
        InitMemoryProfilerData();
        GameCore_ZEngineAPI_Init(ZEngineApi());
    }

    public static void UnInit()
    {
        UnInitMemoryProfilerData();
    }
}
#endif
