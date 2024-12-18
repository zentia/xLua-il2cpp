function FuncBasePara(x)
end

function FuncClassPara(x)
end

function FuncStructPara(x)
end

function FuncTwoBasePara(x, y)
end

CS.ParaStruct()
local Int32 = CS.System.Int32
local list_int32 = CS.System.Collections.Generic.List(Int32)

luaTable = list_int32()
luaTable:Add(1)

g = 0

local ClassLuaCallCS = CS.ClassLuaCallCS

function LuaAccessCSBaseMember_get(num)
    local csObj = ClassLuaCallCS()
    for i = 1, num do
        local x = csObj.id
    end
end

function LuaAccessCSBaseMember_set(num)
    local csObj = ClassLuaCallCS()
    for i = 1, num do
        csObj.id = 0
    end
end

function LuaAccessCSClassMember_get(num)
    local csObj = ClassLuaCallCS()
    for i = 1, num do
        local x = csObj.paraClass
    end
end

function LuaAccessCSClassMember_set(num)
    local csObj = ClassLuaCallCS()
    local x = CS.ParaClass()
    for i = 1, num do
        csObj.paraClass = x
    end
end

function LuaAccessStructMember_get(num)
    local csObj = ClassLuaCallCS()
    for i = 1, num do
        local x = csObj.paraStruct
    end
end

function LuaAccessStructMember_set(num)
    local csObj = ClassLuaCallCS()
    local x = CS.ParaStruct()
    for i = 1, num do
        csObj.paraStruct = x
    end
end

function LuaAccessVec3Member_get(num)
    local csObj = ClassLuaCallCS()
    for i = 1, num do
        local x = csObj.vec3Member
    end
end

function LuaAccessVec3Member_set(num)
    local csObj = ClassLuaCallCS()
    local x = CS.UnityEngine.Vector3(0, 0, 0)
    for i = 1, num do
        local x = csObj.vec3Member
    end
end

function LuaAccessCSBaseMemberFunc(num)
    local csObj = ClassLuaCallCS()
    for i = 1, num do
        csObj:funcBaseParam(0)
    end
end

function LuaAccessCSClassMemberFunc(num)
    local csObj = ClassLuaCallCS()
    local clsObj = CS.ParaClass()
    for i = 1, num do
        csObj:funcClassParam(clsObj)
    end
end

function LuaAccessCSStructMemberFunc(num)
    local csObj = ClassLuaCallCS()
    local clsObj = CS.ParaStruct()
    for i = 1, num do
        csObj:funcStructParam(clsObj)
    end
end

function LuaAccessCSVec3MemberFunc(num)
    local csObj = ClassLuaCallCS()
    local clsObj = CS.UnityEngine.Vector3(0, 0, 0)
    for i = 1, num do
        csObj:funcVec3Param(clsObj)
    end
end

function LuaAccessCSInMemberFunc(num)
    local csObj = ClassLuaCallCS()
    local x = 0
    for i = 1, num do
        csObj:funcInParam(x)
    end
end

function LuaAccessCSOutMemberFunc(num)
    local csObj = ClassLuaCallCS()
    local x
    for i = 1, num do
        x = csObj:funcOutParam()
    end
end

function LuaAccessCSInOutMemberFunc(num)
    local csObj = ClassLuaCallCS()
    local x = 0
    local y
    for i = 1, num do
        y = csObj:funcInOutParam(x)
    end
end

function LuaAccessCSTwoMemberFunc(num)
    local csObj = ClassLuaCallCS()
    for i = 1, num do
        y = csObj:funcTwoParam(0, 0)
    end
end

function LuaAccessCSStaticBaseMember_get(num)
    for i = 1, num do
        local x = ClassLuaCallCS.sId
    end
end

function LuaAccessCSStaticBaseMember_set(num)
    for i = 1, num do
        ClassLuaCallCS.sId = 0
    end
end

function LuaAccessCSStaticClassMember_get(num)
    for i = 1, num do
        local x = ClassLuaCallCS.sParamClass
    end
end

function LuaAccessCSStaticClassMember_set(num)
    local x = CS.ParaClass()
    for i = 1, num do
        ClassLuaCallCS.sParamClass = x
    end
end

function LuaAccessCSStaticStructMember_get(num)
    for i = 1, num do
        local x = ClassLuaCallCS.sParamStruct
    end
end

function LuaAccessCSStaticStructMember_set(num)
    local x = CS.ParaStruct()
    for i = 1, num do
        ClassLuaCallCS.sParamStruct = x
    end
end

function LuaAccessCSStaticVec3Member_get(num)
    for i = 1, num do
        local x = ClassLuaCallCS.sParamVec3
    end
end

function LuaAccessCSStaticVec3Member_set(num)
    local x = CS.UnityEngine.Vector3(0, 0, 0)
    for i = 1, num do
        ClassLuaCallCS.sParamVec3 = x
    end
end

function LuaAccessCSStaticBaseMemberFunc(num)
    for i = 1, num do
        ClassLuaCallCS.sFuncBaseParam(0)
    end
end

function LuaAccessCSStaticClassMemberFunc(num)
    local clsObj = CS.ParaClass()
    for i = 1, num do
        ClassLuaCallCS.sFuncClassParam(clsObj)
    end
end

function LuaAccessCSStaticStructMemberFunc(num)
    local clsObj = CS.ParaStruct()
    for i = 1, num do
        ClassLuaCallCS.sFuncStructParam(clsObj)
    end
end

function LuaAccessCSStaticVec3MemberFunc(num)
    local clsObj = CS.UnityEngine.Vector3(0, 0, 0)
    for i = 1, num do
        ClassLuaCallCS.sFuncVec3Param(clsObj)
    end
end

function LuaAccessCSStaticInMemberFunc(num)
    local x = 0
    for i = 1, num do
        ClassLuaCallCS.sFuncInParam(x)
    end
end

function LuaAccessCSStaticOutMemberFunc(num)
    local x
    for i = 1, num do
        x = ClassLuaCallCS.sFuncOutParam()
    end
end

function LuaAccessCSStaticInOutMemberFunc(num)
    local x = 0
    local y
    for i = 1, num do
        y = ClassLuaCallCS.sFuncInOutParam(x)
    end
end

function LuaAccessCSStaticTwoMemberFunc(num)
    for i = 1, num do
        y = ClassLuaCallCS.sFuncTwoParam(0, 0)
    end
end

function LuaAccessCSEnumFunc_get(num)
    local csObj = ClassLuaCallCS()
    for i = 1, num do
        local x = csObj.enumParam
    end
end

function LuaAccessCSEnumFunc_set(num)
    local csObj = ClassLuaCallCS()
    local one = ClassLuaCallCS.LuaEnum.ONE
    for i = 1, num do
        csObj.enumParam = one
    end
end

function LuaAccessCSArrayFunc_get(num)
    local csObj = ClassLuaCallCS()
    local csArray = csObj.array
    for i = 1, num do
        --local x = csArray:get_Item(0)
    end
end

function LuaAccessCSArrayFunc_set(num)
    local csObj = ClassLuaCallCS()
    local csArray = csObj.array
    for i = 1, num do
        --csArray:set_Item(0, 1)
    end
end

function LuaAddRemoveCB(num)
    local csObj = ClassLuaCallCS();
    local function cb()
    end
    --local fn = CS.NullEventHandler(cb)
    --for i = 1, num do
    --    csObj:add_NullEvent(fn)
    --    csObj:remove_NullEvent(fn)
    --end
end

function LuaBaseParaCB()
    --local csObj = ClassLuaCallCS();
    --csObj:add_BaseParaEvent(CS.BaseParaEventHandler(function(x)
    --end))
    --csObj:InvokeBaseParaCB()
end

function LuaClassParaCB()
    --local csObj = ClassLuaCallCS();
    --csObj:add_ClassParaEvent(CS.ClassParaEventHandler(function(x)
    --end))
    --csObj:InvokeClassParaCB()
end

function LuaStructParaCB()
    --local csObj = ClassLuaCallCS();
    --csObj:add_StructParaEvent(CS.StructParaEventHandler(function(x)
    --end))
    --csObj:InvokeStructParaCB()
end

function LuaVec3ParaCB()
    --local csObj = ClassLuaCallCS();
    --csObj:add_Vec3ParaEvent(CS.Vec3ParamEventHandler(function(x)
    --end))
    --csObj:InvokeVec3ParaCB()
end

function LuaConstructClass(num)
    for i = 1, num do
        local clsObj = CS.ParaClass()
    end
end

function LuaConstructStruct(num)
    local obj
    for i = 1, num do
        obj = CS.UnityEngine.Color(1, 1, 1, 1)
    end
end
