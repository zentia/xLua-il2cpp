require('tte')

function getAssemblyInfo(genTypes)
    if not genTypes then
        return {}
    end
    local assemblyInfo = {}
    for i = 1, genTypes.Count do
        local type = genTypes[i - 1]
        local assemblyName = type.Assembly:GetName(false).Name
        if not assemblyInfo[assemblyName] then
            assemblyInfo[assemblyName] = {}
        end
        local types = assemblyInfo[assemblyName]
        if type.IsGenericType then
            table.insert(types, string.split(type.FullName, '[')[1])
        elseif type.IsNested then
            table.insert(types, string.rep('+', '/'))
        else
            table.insert(types, type.FullName)
        end
    end
    local ret = {}
    for i, v in pairs(assemblyInfo) do
        table.insert(ret, {i, v})
    end
    return ret
end

function LinkXMLTemplate(genTypes) 
    return string.format([[
<linker>%s</linker>]], FOR(getAssemblyInfo(genTypes), function(assemblyInfo) 
        return string.format([[
    <assembly fullname="%s">%s</assembly>]], assemblyInfo.name, FOR(assemblyInfo.types, function(type) 
            return string.format([[
            <type fullname="%s" preserve="all"/>]], type)
        end))
    end))
end