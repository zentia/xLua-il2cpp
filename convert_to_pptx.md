# 转换为 PowerPoint 的方法

## 方法1：使用 Pandoc（推荐）

### 安装依赖
1. 安装 pandoc: https://pandoc.org/installing.html
2. 安装 MiKTeX 或 TeX Live（包含 XeLaTeX）

### 转换命令（支持中文）

```bash
# 使用 XeLaTeX（推荐，支持中文更好）
pandoc -s "xLua-il2cpp优化解析_PPT.md" \
    --pdf-engine=xelatex \
    -V mainfont="SimSun" \
    -V CJKmainfont="SimSun" \
    -o "xLua-il2cpp优化解析_PPT.pdf"

# 或者转换为 PowerPoint
pandoc "xLua-il2cpp优化解析_PPT.md" \
    -o "xLua-il2cpp优化解析_PPT.pptx" \
    --reference-doc=reference.pptx
```

## 方法2：使用 Marp（推荐用于 PPT）

### 安装 Marp
```bash
npm install -g @marp-team/marp-cli
```

### 转换命令
```bash
marp "xLua-il2cpp优化解析_PPT.md" --pdf
# 或
marp "xLua-il2cpp优化解析_PPT.md" --pptx
```

## 方法3：使用在线工具

1. **Marp Web**: https://marp.app/
   - 打开网页，粘贴 Markdown 内容
   - 导出为 PDF 或 PPTX

2. **HackMD**: https://hackmd.io/
   - 支持导出为 PDF

## 方法4：手动转换

1. 使用 VS Code + Marp 插件
   - 安装 "Marp for VS Code" 插件
   - 打开 Markdown 文件
   - 使用预览功能，然后导出

## 如果遇到编码问题

### Windows PowerShell
```powershell
# 设置编码
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$env:PYTHONIOENCODING = "utf-8"

# 然后运行 pandoc
pandoc -s "xLua-il2cpp优化解析_PPT.md" --pdf-engine=xelatex -V CJKmainfont="SimSun" -o output.pdf
```

### 使用 UTF-8 编码保存文件
确保 Markdown 文件以 UTF-8 编码保存（VS Code 右下角可以查看和修改编码）

