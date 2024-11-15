﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace XLua.Editor
{
    namespace Generator
    {
        public class FileExporter
        {
            public static void GenRegisterInfo(string outDir)
            {
                var configure = Configure.GetConfigureByTags(new List<string>()
                {
                    "XLua.LuaCallCSharpAttribute"
                });
                var genTypes = configure["XLua.LuaCallCSharpAttribute"].Select(kv => kv.Key)
                    .Where(o => o is Type)
                    .Cast<Type>()
                    .Where(t => !t.IsGenericTypeDefinition && !t.Name.StartsWith("<"))
                    .Distinct()
                    .ToList();
                
                if (!Utils.HasFilter)
                {
                    Utils.SetFilters(Configure.GetFilters());
                }
                
                var RegisterInfos = RegisterInfoGenerator.GetRegisterInfos(genTypes);

                using (var luaEnv = new LuaEnv())
                {
                    var assetPath = Path.GetFullPath("Packages/com.tencent.xlua/");
                    assetPath = assetPath.Replace("\\", "/");
                    luaEnv.DoString($"package.path = package.path..';{assetPath + "Editor/Resources/xlua/templates"}/?.lua'");
                    var path = Path.Combine(assetPath, "Editor/Resources/xlua/templates/registerinfo.tpl.lua");
                    var bytes = File.ReadAllBytes(path);
                    luaEnv.DoString<LuaFunction>(bytes, path);
                    var func = luaEnv.Global.Get<LuaFunction>("RegisterInfoTemplate");
                    var registerInfoContent = func.Func<List<RegisterInfoForGenerate>, string>(RegisterInfos);
                    var registerInfoPath = outDir + "RegisterInfo_Gen.cs";
                    using (var textWriter = new StreamWriter(registerInfoPath, false, Encoding.UTF8))
                    {
                        textWriter.Write(registerInfoContent);
                        textWriter.Flush();
                    }
                }
            }
        }    
    }
    
}