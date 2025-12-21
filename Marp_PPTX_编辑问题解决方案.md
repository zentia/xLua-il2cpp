# Marp PPTX 无法编辑问题解决方案

## 问题原因

Marp 生成的 PPTX 文件无法编辑，是因为 **Marp 将每一页幻灯片渲染为图片**，然后插入到 PowerPoint 中。这种方式虽然保持了视觉效果，但文本内容无法直接编辑。

## 解决方案

### 方案1：使用 Pandoc（推荐，生成可编辑的 PPTX）

#### 安装 Pandoc
```bash
# Windows (使用 Chocolatey)
choco install pandoc

# 或从官网下载安装
# https://pandoc.org/installing.html
```

#### 转换命令
```bash
# 基本转换
pandoc "xLua-il2cpp优化解析_Marp.md" -o "xLua-il2cpp优化解析_Marp.pptx" --from markdown --to pptx --slide-level 2

# 使用参考模板（推荐，格式更好）
pandoc "xLua-il2cpp优化解析_Marp.md" -o "xLua-il2cpp优化解析_Marp.pptx" --from markdown --to pptx --slide-level 2 --reference-doc=reference.pptx
```

#### 使用批处理脚本
直接运行：
```bash
convert_to_pptx_editable.bat
```

### 方案2：使用 VS Code + Marp 插件导出

1. 安装 VS Code 的 "Marp for VS Code" 插件
2. 打开 Markdown 文件
3. 使用预览功能
4. 导出为 PDF，然后在 PowerPoint 中导入 PDF（可编辑性有限）

### 方案3：手动转换

1. 使用 Marp 导出为 PDF
2. 在 PowerPoint 中插入 PDF（作为对象）
3. 手动重新创建幻灯片（完全可编辑）

### 方案4：使用在线工具

1. **Marp Web**: https://marp.app/
   - 导出为 PDF，然后在 PowerPoint 中处理

2. **HackMD**: https://hackmd.io/
   - 支持导出为 PDF

## 推荐工作流程

### 如果需要可编辑的 PPTX：

1. **使用 Pandoc 转换**（最简单）
   ```bash
   pandoc "xLua-il2cpp优化解析_Marp.md" -o "output.pptx" --from markdown --to pptx --slide-level 2
   ```

2. **在 PowerPoint 中调整格式**
   - 打开生成的 PPTX
   - 应用主题模板
   - 调整字体、颜色、布局

### 如果需要保持 Marp 的视觉效果：

1. **使用 Marp 导出 PDF**
   ```bash
   marp "xLua-il2cpp优化解析_Marp.md" --pdf
   ```

2. **在 PowerPoint 中插入 PDF**
   - 插入 → 对象 → 从文件创建
   - 注意：这种方式仍然不可编辑

## 注意事项

- **Marp 的 PPTX 导出**：基于图片，不可编辑
- **Pandoc 的 PPTX 导出**：基于文本，可编辑，但格式可能需要调整
- **Marp 的 PDF 导出**：基于文本，但导入 PowerPoint 后可能不可编辑

## 最佳实践

1. **开发阶段**：使用 Marp 预览和调整内容
2. **最终输出**：
   - 如果需要编辑：使用 Pandoc 转换为 PPTX
   - 如果只需要展示：使用 Marp 导出 PDF

