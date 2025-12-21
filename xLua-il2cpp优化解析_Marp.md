---
marp: true
theme: default
paginate: true
style: |
  section {
    font-family: 'Microsoft YaHei', 'SimSun', Arial, sans-serif;
  }
  code {
    font-family: 'Consolas', 'Courier New', monospace;
  }
---

<!-- _class: lead -->
# xLua-il2cpp 优化解析

## 性能优化思路与原理

---

## 优化概述

**传统模式**：`C# → PInvoke → Native Plugin → Lua`  
**il2cpp 模式**：`il2cpp → 直接调用 → Native Plugin → Lua`

### 核心目标
- ✅ 消除 PInvoke 开销（节省 50-80ns）
- ✅ 减少 GC 分配（泛型、枚举、值类型 0 GC）
- ✅ 提升调用性能 **2-5倍**

### 性能提升
- ⚡ 函数调用：~100ns → ~20ns（**5倍**）
- ⚡ 直接内存访问：**提升 2-3倍**
- 🚫 泛型、枚举、值类型：**0 GC**

---

## 核心优化思路

```
┌─────────┐    直接调用      ┌─────┐
│ il2cpp  │ ──────────────> │ Lua │
└─────────┘                 └─────┘
```

### 设计原则
- **零开销抽象**：编译时生成最优代码
- **类型安全**：利用 il2cpp 类型信息
- **平台统一**：pesapi 统一桥接接口

---

## 架构优化 - pesapi 桥接层

**pesapi (Portable Embedded Scripting API)**

统一桥接接口，跨平台支持

### 优势
- 🌐 跨平台统一接口
- ⚡ 直接调用，零 PInvoke 开销

### 调用路径
**传统**：`C# → PInvoke → Native → Lua`  
**il2cpp**：`il2cpp → 直接调用 → Native → Lua`

---

## 代码生成优化

**C++ Wrapper** 替代 C# Wrapper

| 特性 | 传统模式 | il2cpp 模式 |
|------|---------|------------|
| Wrapper 类型 | C# Static Wrapper | C++ FunctionBridge.h |
| 调用方式 | 运行时反射 | 编译时直接链接 |
| 运行时开销 | 有 | **零** |

### 生成的 Bridge 函数
- 类型信息编译时确定
- 避免运行时类型查询
- 直接内存操作，内联优化

---

## 数据结构优化 - dense_hash_map

**dense_hash_map** 替代 `std::unordered_map`

```cpp
// 优化前
std::unordered_map<void*, ObjectCacheNode*> m_DataCache;

// 优化后
dense_hash_map<void*, ObjectCacheNode*, PointerHashFunctor> m_DataCache;
```

### 性能提升
- 📈 **查找速度**：快 **2-3倍**
- ⚡ **缓存友好**：连续存储，指针直接 hash

---

## 类型系统优化（0 GC）

### 泛型
```lua
-- 传统：装箱产生 GC
sendaction_generic(CS.System.Int32)
-- il2cpp：直接传入类型，无 GC
xlua.get_generic_method(CS.Class, 'SendAction', CS.System.Int32)
```

### 枚举
```lua
-- 传统：枚举对象，有 GC
TssSDKIoctlCMD.CommQuery.value__
-- il2cpp：直接作为 number，零开销
TssSDKIoctlCMD.CommQuery
```

### 值类型
- ✅ 栈上传递，无需装箱，减少内存分配

---

<!-- _class: lead -->
# 性能提升总结

### 核心指标
- ⚡ 函数调用：~100ns → ~20ns（**5倍**）
- 🚫 GC 分配：频繁 → 极少

### 核心价值
- ⚡ 性能提升 **2-5倍**
- 🚫 GC 分配显著降低

## Q&A
