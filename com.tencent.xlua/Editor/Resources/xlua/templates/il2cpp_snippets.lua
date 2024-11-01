﻿require("tte")

function isStructOrNullableStruct(signature)
    return (string.startsWith(signature, sigs.StructPrefix) or string.startsWith(signature, sigs.NullableStructPrefix)) and string.endsWith(signature, '_') 
end

function isNullableStruct(signature)
    return string.startsWith(signature, sigs.NullableStructPrefix)
end

function isStruct(signature)
    return string.startsWith(signature, sigs.StructPrefix) and string.startsWith(signature, '_')
end

function SToCPPType(signature)
    if signature[1] == 'D' then
        signature = string.sub(signature, 2)
    end
    if signature == 's' then
        return 'Il2CppString*'
    end
    if signature == 'o' or signature == 'O' or signature == 'a' then
        return 'Il2CppObject*'
    end
    if signature[1] == 'V' then
        return 'Il2CppArray*'
    end
    local t = PrimitiveSignatureCppTypeMap[signature]
    if not t then
        t = "void*"
    end
    if (string.startsWith(signature, sigs.StructPrefix) or string.startsWith(signature, sigs.NullableStructPrefix)) and string.endsWith(signature, '_') then
        t = string.format('struct %s', signature)
    end
    if signature[1] == 'P' then
        t = string.format('%s*', SToCPPType(string.sub(signature, 2)))
    end
    return t
end

function getThis(signature) 
    local getLuaThis = 'pesapi_value luaThis = pesapi_get_holder(info);'
    if signature == 't' then
        return string.format([[%s
        auto self = pesapi_get_native_object_ptr(env, luaThis);]], getLuaThis)
    elseif signature == 'T' then
        return string.format([[%s
        auto self = pesapi_get_native_object_ptr(env, luaThis);
        auto ptrType = (Il2CppClass*) pesapi_get_native_object_typeid(env, luaThis);
        if (il2cpp::vm::Class::IsValueType(ptrType))
        {
            self = il2cpp::vm::Object::Box(ptrType, self);
        }
]], getLuaThis)
    else
        return ''
    end
end

function getArgValue(signature, LuaName, isRef)
    if PrimitiveSignatureCppTypeMap[signature] then
        if isRef then
            return string.format('converter::Converter<std::reference_wrapper<%s>>::toCpp(env, %s)', PrimitiveSignatureCppTypeMap[signature], LuaName)
        else
            return string.format('converter::Converter<%s>::toCpp(env, %s)', PrimitiveSignatureCppTypeMap[signature], LuaName)
        end
    elseif (signature == 'Pv' or signature == 'p') and not isRef then
        return string.format('DataTransfer::GetPointer<void>(env, %s)', LuaName)
    else
        if PrimitiveSignatureCppTypeMap[signature] then
            if signature == 'v' then
                error('void has no default')
            end
            return signature == 'b' and 'false' or '0'
        end
        if (string.startsWith(signature, sigs.StructPrefix) or string.startsWith(signature, sigs.NullableStructPrefix)) and string.endsWith('_') then
            return '{}'
        end
        return 'nullptr'
    end
end

function checkLuaArg(signature, index)
    local ret = ''
    local typeInfoVar = string.format('TIp%d', index)
    if signature[1] == 'D' then
        ret = ret .. string.format('if (lua_args_len > %d && ', index)
    elseif signature[1] == 'V' then
        local elmSignature = string.sub(signature, 2)
        local elmClassDecl = ''
        if elmSignature == 'o' or elmSignature == 'O' or elmSignature == 'a' or ((string.startsWith(elmSignature, sigs.StructPrefix) or string.startsWith(elmSignature, sigs.NullableStructPrefix)) and string.endsWith(elmSignature, '_')) then
            elmClassDecl = string.format('auto %s_V = il2cpp::vm::Class::GetElementClass(%s);', typeInfoVar, typeInfoVar)
        end
        signature = elmSignature
        typeInfoVar = typeInfoVar .. '_V'
    else
        ret = ret .. 'if ('
    end

    if PrimitiveSignatureCppTypeMap[signature] then
        ret = ret .. string.format('!converter::Converter<%s>::accept(env, _sv%d)) return false;', PrimitiveSignatureCppTypeMap[signature], index)
    elseif signature == 'P' or signature == 'Pv' or signature == 'a' then -- IntPtr, void*, ArrayBuffer
        ret = ret .. string.format('!pesapi_is_binary(env, _sv%d) && !pesapi_is_null(env, _sv%d) && !pesapi_is_undefined(env, _sv%d)) return false;', index, index, index)
    elseif signature[1] == 'P' then
        ret = ret .. string.format('!pesapi_is_object(env, _sv%d)) return false;', index)
    elseif signature == 's' then
        ret = ret .. string.format('!converter::Converter<Il2CppString*>::accept(env, _sv%d)) return false;', index)
    elseif signature == 'o' or signature == 'a' then
        ret = ret .. string.format('!DataTransfer::IsAssignable(env, _sv%d, %s, false)) return false;', index, typeInfoVar)
    elseif signature == 'O' then
        return ''
    elseif (string.startsWith(signature, sigs.StructPrefix) or string.startsWith(signature, sigs.NullableStructPrefix)) and string.endsWith(signature, '_') then
        ret = ret .. string.format('!DataTransfer::IsAssignable(env, _sv%d, %s, true)) return false;', index, typeInfoVar)
    else
        ret = ret .. '!!true) return false;'
    end
    return ret
end 

function refSetback(signature, index)
    if signature[1] == 'P' and signature ~= 'Pv' then
        local elementSignature = string.sub(signature, 2)
        local val = CSValToLuaVal(elementSignature, string.format('*p%d', index))

        if val then
            if string.startsWith(elementSignature, sigs.StructPrefix) and string.endsWith(elementSignature, '_') then
                return string.format([[if (p%d == &up%d)
    {
        pesapi_update_boxed_value(env, _sv%d, %s);
    }
            ]], index, index, index, val)
            elseif string.startsWith(elementSignature, sigs.NullableStructPrefix) and string.endsWith(elementSignature, '+') then
                return string.format([[if (p%d == &up%d)
    {
        if (!p%d->hasValue) pesapi_update_boxed_value(env, _sv%d, pesapi_create_null(env));
        if (p%d == &up%d) pesapi_update_boxed_value(env, _sv%d, %s);
    }
            ]], index, index, index, index, index, index, index, val)
            else
                return string.format('pesapi_update_boxed_value(env, _sv%d, %s);', index, val)
            end
        end
    end
    
    return ''
end 

function returnToLua(signature)
    return string.format('pesapi_add_return(info, %s);', CSValToLuaVal(signature, 'ret'))
end

function returnToCS(signature) 
    return string.format([[
 %s
    return ret;
    ]], LuaValToCSVal(signature, 'luaret', 'ret'))
end

function LuaValToCSVal(signature, LuaName, CSName)
    if signature == 's' then -- string
        return string.format([[     // LuaValToCSVal s
    Il2CppString* %s = converter::Converter<Il2CppString*>::toCpp(env, %s);]], CSName, LuaName)
    elseif signature == 'Ps' then
        return string.format([[     // LuaValToCSVal Ps
    Il2CppString* u%s = converter::Converter<std::reference_wrapper<Il2CppString*>>::toCpp(env, %s); // string ref
    Il2CppString** %s = &u%s;
        ]], CSName, LuaName, CSName, CSName)
    elseif signature == 'o' or signature == 'O' or signature == 'a' then    -- object
        return string.format([[     // LuaValToCSVal o/O
    Il2CppObject* %s = LuaValToCSRef(TI%s, env %s);
        ]], CSName, CSName, LuaName)
    elseif signature == 'Po' or signature == 'PO' or signature == 'Pa' then
        return string.format([[     // LuaValToCSVal Po/PO
    Il2CppObject* u%s = DataTransfer::GetPointer<Il2CppObject>(env, pesapi_unboxing(env, %s)); // object ret
    Il2CppObject** %s = &u%s;
        ]], CSName, LuaName, CSName, CSName)
    elseif (string.startsWith(signature, sigs.StructPrefix) or string.startsWith(signature, sigs.NullableStructPrefix)) and string.endsWith(signature, '_') then -- valuetype
        return string.format([[     // LuaValToCSVal struct
    %s* p%s = DataTransfer::GetPointer<%s>(env, %s);
    %s %s = p%s ? *p%s : %s {};
        ]], signature, CSName, signature, LuaName, signature, CSName, CSName, CSName, signature)
    elseif (string.startsWith(signature, 'P' .. sigs.StructPrefix) or string.startsWith(signature, 'P' .. sigs.NullableStructPrefix)) and string.endsWith(signature, '_') then -- valuetype ref
        local s = string.sub(signature, 2)
        return string.format([[     // LuaValToCSVal Pstruct
    %s* %s = DataTransfer::GetPointer<%s>(env, pesapi_unboxing(env, %s)); // valuetype ref
    %s u%s;
    if (!%s) {
        memset(&u%s, 0, sizeof(%s));
    }
        ]], s, CSName, s, LuaName, s, CSName, CSName, CSName, CSName, CSName, CSName)
    elseif signature[1] == 'P' and signature ~= 'Pv' then
        local s = string.sub(signature, 2)
        if PrimitiveSignatureCppTypeMap[s] then
            return string.format([[     // LuaValToCSVal P primitive
    %s u%s = %s;
    %s* %s = &u%s;
        ]], SToCPPType(s), CSName, getArgValue(s, LuaName, true), SToCPPType(s), CSName, CSName)
        else
            return string.format([[ // LuaValToCSVal P not primitive
    %s %s = %s;
            ]], SToCPPType(signature), CSName, getArgValue(s, LuaName, true))
        end
    elseif signature[1] == 'V' then
        local si = string.sub(signature, 2)
        local start = tonumber(string.match(LuaName, '_sv(%d+)'))
        if PrimitiveSignatureCppTypeMap[si] then
            return string.format([[     // LuaValToCSVal primitive params
    Il2CppArray* %s = Params<%s>::PackPrimitive(env, info, TI%s, lua_args_len, %d);
            ]], CSName, PrimitiveSignatureCppTypeMap[si], CSName, start)
        elseif si == 's' then
            return string.format([[     // LuaValToCSVal string params
    Il2CppArray* %s = Params<void*>::PackString<env, info, TI%s, lua_args_len, %d);
            ]], CSName, CSName, start)
        elseif si == 'o' or si == 'O' or si == 'a' then
            return string.format([[     // LuaValToCSVal ref params
    Il2CppArray* %s = Params<void*>::PackRef(env, info TI%s, lua_args_len, %d);
            ]], CSName, CSName, start)
        elseif (string.startsWith(si, sigs.StructPrefix) or string.startsWith(si, sigs.NullableStructPrefix)) and string.endsWith(si, '_') then
            return string.format([[ // LuaValToCSVal valuetype params
    Il2CppArray* %s = Params<%s>::PackValueType(env, info, TI%s, lua_args_len, %d);            
            ]], CSName, si, CSName, start)
        else
            return string.format([[     // LuaValToCSVal unknown params type
    Il2CppArray* %s = nullptr;            
            ]], CSName)
        end
    elseif signature[1] == 'D' then
        local si = string.sub(signature, 2)
        local start = tonumber(string.match(LuaName, '_sv(%d+)'))
        if PrimitiveSignatureCppTypeMap[si] then
            return string.format([[ // LuaValToCSVal primitive with default
    %s %s = OptionalParameter<%s>::GetPrimitive(env, info, method, wrapData, lua_args_len, %d);            
            ]], PrimitiveSignatureCppTypeMap[si], CSName, PrimitiveSignatureCppTypeMap[si], start)
        elseif si == 's' then
            return string.format([[ // LuaValToCSVal string with default
    Il2CppString* %s = OptionalParameter<Il2CppString*>::GetString(env, info, method, wrapData, lua_args_len, %d);            
            ]], CSName, start)
        elseif si == 'o' or si == 'O' or si == 'a' then
            return string.format([[ // LuaValToCSVal ref with default
    Il2CppObject* %s = OptionalParameter<Il2CppObject*>::GetRefType(env, info, method, wrapData, lua_args_len, %d, TI%s);            
            ]], CSName, start, CSName)
        elseif (string.startsWith(si, sigs.StructPrefix) or string.startsWith(si, sigs.NullableStructPrefix)) and string.endsWith(si, '_') then
            return string.format([[ // LuaValToCSVal valuetype with default
    %s %s = OptionalParameter<%s>::GetValueType(env, info, method, wrapData, lua_args_len, %d);            
            ]], CSName, si, start)
        else
            return string.format([[ // LuaValToCSVal unknown type with default
    void* %s = nullptr;            
            ]], CSName)
        end
    else
        return string.format([[ // LuaValToCSVal P any
    %s %s = %s;        
        ]], SToCPPType(signature), CSName, getArgValue(signature, LuaName))
    end
end

function CSValToLuaVal(signature, CSName)
    local TIName = CSName
    if CSName[1] == '*' then
        TIName = string.sub(CSName, 2)
    end
    TIName = 'TI' .. TIName
    if PrimitiveSignatureCppTypeMap[signature] then
        return string.format('converter::Converter<%s>::toScript(env, %s)', PrimitiveSignatureCppTypeMap[signature], CSName)
    elseif signature == 'O' then -- System.Object
        return string.format('CSRefToLuaValue(env, %s, %s)', TIName, CSName)
    elseif signature == 'o' then -- classes except System.Object
        return string.format('CSRefToLuaValue(env, %s, %s)', TIName, CSName)
    elseif signature =='a' then -- ArrayBuffer
        return string.format('CSRefToLuaValue(env, %s, %s)', TIName, CSName)
    elseif string.startsWith(signature, sigs.NullableStructPrefix) and string.endsWith(signature, '_') then
        return string.format('DataTransfer::CopyNullableValueType(env, %s, %s)', CSName, TIName)
    elseif signature == 's' then
        return string.format('converter::Converter<Il2CppString*>::toScript(env, %s)', CSName)
    elseif signature == 'p' or signature == 'Pv' then -- IntPtr, void*
         return string.format('pesapi_create_binary(env, %s, 0)', CSName)
    elseif string.startsWith(signature, sigs.StructPrefix) and string.endsWith(signature, '_') then
        return string.format('DataTransfer::CopyValueType(env, %s, %s)', CSName, TIName)
    elseif signature[1] == 'P' and signature ~= 'Pv' then
        local elemSignature = string.sub(signature, 2)
        if PrimitiveSignatureCppTypeMap[elemSignature] then
            return string.format('converter::Converter<std::reference_wrapper<%s>>::toScript(env, *%s)', PrimitiveSignatureCppTypeMap[elemSignature], CSName)
        elseif isStruct(elemSignature) then
            return string.format('pesapi_native_object_to_value(env, %s, %s, false)', TIName, CSName)
        end
    end
    return string.format('// unknown ret signature: %s', signature)
end 

function genArgsLenCheck(parameterSignatures)
    local requireNum = 1
    while requireNum < #parameterSignatures and parameterSignatures[requireNum][1] ~= 'V' and parameterSignatures[requireNum][1] ~= 'D' do
        requireNum = requireNum + 1
    end
    return requireNum ~= #parameterSignatures and string.format('lua_args_len < %d', requireNum) or string.format('lua_args_len != %d', #parameterSignatures)
end

function declareTypeInfo(wrapperInfo)
    local returnHasTypeInfo = wrapperInfo.ReturnSignature and not PrimitiveSignatureCppTypeMap[getSignatureWithoutRefAndPrefix(wrapperInfo.ReturnSignature)]
    local ret = {}
    local i = 0
    if returnHasTypeInfo then
        i = i + 1
        table.insert(ret, string.format('auto TIret = wrapData->TypeInfos[%d];', i))
    end
    local luaArray = listToLuaArray(wrapperInfo.ParameterSignatures)
    for index, ps in ipairs(luaArray) do
        if not PrimitiveSignatureCppTypeMap[getSignatureWithoutRefAndPrefix(ps)] then
            i = i + 1
            table.insert(ret, string.format('auto TIp%d = wrapData->TypeInfos[%d];', index, i))
        end
    end
    return table.concat(ret, '\n    ')
end